using System;
using System.Collections.Generic;
using System.IO;

#nullable disable

namespace DKCTF
{
    /// <summary>
    /// Stateful CHPR CompStream keyframe decoder and sampling-bytecode interpreter.
    /// This is separate from the structural parser so frame state is per playback
    /// instance rather than shared across animation-table entries.
    /// </summary>
    public static partial class CHPRCompStreamAnimation
    {
        public sealed class Pose
        {
            // op 2: {qw, qx, qy, qz, sx, sy, sz, tx, ty, tz}
            // op 3: {qw, qx, qy, qz, tx, ty, tz}
            public Dictionary<int, float[]> Transforms { get; } = new Dictionary<int, float[]>();
            public Dictionary<int, float> Floats { get; } = new Dictionary<int, float>();
            public Dictionary<int, int> Ints { get; } = new Dictionary<int, int>();

            // Flags from group type 3, keyed by (stream, group, channel).
            public Dictionary<long, int> Flags { get; } = new Dictionary<long, int>();
        }

        public static Player CreatePlayer(ParsedClip clip)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            return new Player(clip);
        }

        /// <summary>
        /// One playback instance. The decoder advances key streams forward and
        /// restarts a stream when the requested frame moves backwards.
        /// </summary>
        public sealed class Player
        {
            private readonly ParsedClip clip;
            private readonly int[] currentFrame;
            private readonly long[] bitPositions;
            private readonly int[][] previousKeyFrames;
            private readonly int[][] nextKeyFrames;
            private readonly float[][] inverseDeltas;
            private readonly byte[][] values;
            private readonly Dictionary<long, int> flags = new Dictionary<long, int>();
            private readonly bool[] streamInitialized;

            internal Player(ParsedClip clip)
            {
                this.clip = clip;
                int count = clip.Streams.Count;
                currentFrame = new int[count];
                bitPositions = new long[count];
                previousKeyFrames = new int[count][];
                nextKeyFrames = new int[count][];
                inverseDeltas = new float[count][];
                values = new byte[count][];
                streamInitialized = new bool[count];

                for (int s = 0; s < count; s++)
                {
                    StreamInfo stream = clip.Streams[s];
                    previousKeyFrames[s] = new int[stream.ChannelCount];
                    nextKeyFrames[s] = new int[stream.ChannelCount];
                    inverseDeltas[s] = new float[stream.ChannelCount];
                    values[s] = (byte[])stream.ValueBuffer.Clone();
                }
            }

            public int StreamFrame(int streamIndex)
            {
                ValidateStreamIndex(streamIndex);
                return currentFrame[streamIndex];
            }

            public long StreamBitPosition(int streamIndex)
            {
                ValidateStreamIndex(streamIndex);
                return bitPositions[streamIndex];
            }

            /// <summary>
            /// Samples outputs at clip-frame time (not seconds). Constant pools
            /// should be supplied in the order used by the owning character info.
            /// overridePool, when non-null, replaces the selected pool for every stream.
            /// </summary>
            public Pose Sample(float clipFrameTime, IReadOnlyList<float[]> constantPools = null, float[] overridePool = null)
            {
                if (float.IsNaN(clipFrameTime) || float.IsInfinity(clipFrameTime))
                    throw new ArgumentOutOfRangeException(nameof(clipFrameTime), "Sample time must be finite.");

                var result = new Pose();

                for (int s = 0; s < clip.Streams.Count; s++)
                {
                    if (!streamInitialized[s])
                        InitializeStream(s);
                }

                for (int s = 0; s < clip.Streams.Count; s++)
                {
                    StreamInfo stream = clip.Streams[s];
                    float streamTime = Math.Max(0.0f, Math.Min(clipFrameTime * stream.RateMultiplier, stream.LastFrame));
                    SeekStream(s, (int)Math.Floor(streamTime));

                    float[] pool = overridePool ?? ResolvePool(stream.ConstantPoolIndex, constantPools);
                    RunSamplingBytecode(s, streamTime, pool, result);
                }

                foreach (KeyValuePair<long, int> flag in flags)
                    result.Flags[flag.Key] = flag.Value;

                return result;
            }

            /// <summary>Advances or rewinds one stream to a discrete stream frame.</summary>
            public void SeekStream(int streamIndex, int targetFrame)
            {
                ValidateStreamIndex(streamIndex);
                if (!streamInitialized[streamIndex] || targetFrame < currentFrame[streamIndex])
                    InitializeStream(streamIndex);

                StreamInfo stream = clip.Streams[streamIndex];
                targetFrame = Math.Max(0, Math.Min(targetFrame, stream.LastFrame));
                while (currentFrame[streamIndex] < targetFrame)
                    StepStream(streamIndex);
            }

            private void ValidateStreamIndex(int streamIndex)
            {
                if ((uint)streamIndex >= (uint)clip.Streams.Count)
                    throw new ArgumentOutOfRangeException(nameof(streamIndex));
            }

            private void InitializeStream(int streamIndex)
            {
                StreamInfo stream = clip.Streams[streamIndex];

                // Restore descriptor-initialized channel values before replaying a stream.
                // Otherwise sampling backward can retain the final values from its previous pass.
                if (stream.ValueBuffer.Length != values[streamIndex].Length)
                    throw new InvalidDataException($"Stream {streamIndex} value-buffer size changed after parsing.");
                Buffer.BlockCopy(stream.ValueBuffer, 0, values[streamIndex], 0, stream.ValueBuffer.Length);

                // Rewinding a stream must also discard flag values written by its later frames.
                var oldFlagKeys = new List<long>();
                foreach (long key in flags.Keys)
                    if ((key >> 40) == streamIndex)
                        oldFlagKeys.Add(key);
                foreach (long key in oldFlagKeys)
                    flags.Remove(key);
                currentFrame[streamIndex] = 0;
                Array.Clear(previousKeyFrames[streamIndex], 0, previousKeyFrames[streamIndex].Length);
                Array.Clear(nextKeyFrames[streamIndex], 0, nextKeyFrames[streamIndex].Length);
                Array.Clear(inverseDeltas[streamIndex], 0, inverseDeltas[streamIndex].Length);

                var bits = new LsbBitReader(clip.RawCompStream, stream.BitstreamOffset, 0);
                List<int[]> firstUpdates = BuildDeltaLists(streamIndex, bits, 0);
                ApplyUpdates(streamIndex, bits, firstUpdates);

                var initialNonHoldUpdates = new List<int[]>();
                for (int g = 0; g < stream.Groups.Count; g++)
                {
                    GroupInfo group = stream.Groups[g];
                    if (!group.Hold && group.ChannelCount > 0)
                        initialNonHoldUpdates.Add(AllChannels(g, group.ChannelCount));
                }
                ApplyUpdates(streamIndex, bits, initialNonHoldUpdates);
                bitPositions[streamIndex] = bits.PositionBits;
                streamInitialized[streamIndex] = true;
            }

            private void StepStream(int streamIndex)
            {
                StreamInfo stream = clip.Streams[streamIndex];
                currentFrame[streamIndex]++;
                var bits = new LsbBitReader(clip.RawCompStream, stream.BitstreamOffset, bitPositions[streamIndex]);
                int updateCount = checked((int)bits.Read(stream.ChangedChannelCountBits));

                if (updateCount != 0)
                {
                    List<int[]> updates;
                    if (currentFrame[streamIndex] == stream.LastFrame)
                    {
                        updates = new List<int[]>();
                        for (int g = 0; g < stream.Groups.Count; g++)
                        {
                            GroupInfo group = stream.Groups[g];
                            if (group.Hold && group.ChannelCount > 0)
                                updates.Add(AllChannels(g, group.ChannelCount));
                        }
                    }
                    else
                    {
                        updates = BuildDeltaLists(streamIndex, bits, currentFrame[streamIndex]);
                    }

                    ApplyUpdates(streamIndex, bits, updates);
                }

                bitPositions[streamIndex] = bits.PositionBits;
            }

            /// <summary>
            /// Builds an update list containing the group index followed by every
            /// channel index in that group: [groupIndex, 0, 1, ..., channelCount - 1].
            /// </summary>
            private static int[] AllChannels(int groupIndex, int channelCount)
            {
                if (channelCount < 0)
                    throw new ArgumentOutOfRangeException(nameof(channelCount));

                int[] result = new int[channelCount + 1];
                result[0] = groupIndex;
                for (int i = 0; i < channelCount; i++)
                    result[i + 1] = i;

                return result;
            }

            private List<int[]> BuildDeltaLists(int streamIndex, LsbBitReader bits, int frame)
            {
                StreamInfo stream = clip.Streams[streamIndex];
                int deltaWidth = checked((int)bits.Read(4));
                var lists = new List<int[]>();

                for (int g = 0; g < stream.Groups.Count; g++)
                {
                    GroupInfo group = stream.Groups[g];
                    var changedChannels = new int[group.ChannelCount + 1];
                    int changedCount = 0;

                    for (int c = 0; c < group.ChannelCount; c++)
                    {
                        int channel = group.ChannelBase + c;
                        if (nextKeyFrames[streamIndex][channel] != frame)
                            continue;

                        previousKeyFrames[streamIndex][channel] = nextKeyFrames[streamIndex][channel];
                        int delta = checked((int)bits.Read(deltaWidth)) & 0xFFFF;
                        nextKeyFrames[streamIndex][channel] = (nextKeyFrames[streamIndex][channel] + delta) & 0xFFFF;
                        inverseDeltas[streamIndex][channel] = 1.0f / delta;
                        changedChannels[++changedCount] = c;
                    }

                    if (changedCount > 0)
                    {
                        changedChannels[0] = g;
                        var exactList = new int[changedCount + 1];
                        Array.Copy(changedChannels, exactList, exactList.Length);
                        lists.Add(exactList);
                    }
                }

                return lists;
            }

            private void ApplyUpdates(int streamIndex, LsbBitReader bits, List<int[]> updateLists)
            {
                StreamInfo stream = clip.Streams[streamIndex];
                byte[] valueData = values[streamIndex];

                foreach (int[] update in updateLists)
                {
                    GroupInfo group = stream.Groups[update[0]];

                    if (group.Type == 3)
                    {
                        for (int k = 1; k < update.Length; k++)
                        {
                            long key = MakeFlagKey(streamIndex, update[0], update[k]);
                            flags[key] = checked((int)bits.Read(1));
                        }
                        continue;
                    }

                    for (int k = 1; k < update.Length; k++)
                    {
                        int channel = update[k];
                        switch (group.Type)
                        {
                            case 0:
                            case 1:
                            case 2:
                                {
                                    int offset = checked(group.ValueOffset + channel * 0x1C);
                                    Buffer.BlockCopy(valueData, offset + 0x10, valueData, offset + 4, 12);
                                    Array.Clear(valueData, offset + 0x10, 12);

                                    int field = CHPRCompStreamAnimation.ReadUInt24(valueData, offset + 1);
                                    if (group.Type == 0)
                                    {
                                        field = (field & ~(1 << 22)) | (((field >> 23) & 1) << 22);
                                        int interpolationFlags = checked((int)bits.Read(2));
                                        field = (field & ~0x800080) |
                                            ((interpolationFlags & 1) << 7) |
                                            (((interpolationFlags >> 1) & 1) << 23);
                                        CHPRCompStreamAnimation.WriteUInt24(valueData, offset + 1, field);
                                    }

                                    int componentList = (field >> 16) & 0x3F;
                                    int shift = (field >> 11) & 31;
                                    int bitWidth = valueData[offset];
                                    while (true)
                                    {
                                        int raw = checked((int)bits.Read(bitWidth));
                                        WriteInt32(valueData, offset + 0x10 + 4 * (componentList & 3), SignExtend(raw, shift));
                                        if (componentList <= 3)
                                            break;
                                        componentList >>= 2;
                                    }
                                    break;
                                }
                            case 4:
                                {
                                    int offset = checked(group.ValueOffset + channel * 0x0C);
                                    int raw = checked((int)bits.Read(valueData[offset]));
                                    int decoded = SignExtend(raw, unchecked((sbyte)valueData[offset + 1]));
                                    Buffer.BlockCopy(valueData, offset + 8, valueData, offset + 4, 4);
                                    WriteInt32(valueData, offset + 8, decoded);
                                    break;
                                }
                            case 5:
                                {
                                    int offset = checked(group.ValueOffset + channel * 8);
                                    int raw = checked((int)bits.Read(valueData[offset]));
                                    int decoded = SignExtend(raw, unchecked((sbyte)valueData[offset + 1]));
                                    WriteInt32(valueData, offset + 4, decoded);
                                    break;
                                }
                            default:
                                throw new InvalidDataException($"Unsupported updated CompStream group type {group.Type} in stream {streamIndex}.");
                        }
                    }
                }
            }

            private void RunSamplingBytecode(int streamIndex, float time, float[] pool, Pose output)
            {
                StreamInfo stream = clip.Streams[streamIndex];
                byte[] data = clip.RawCompStream;
                int start = stream.SamplingBytecodeOffset;

                if (data == null || start < 0 || start >= data.Length)
                    return;

                RunSamplingBytecodeRange(streamIndex, time, pool, output, start, data.Length, 0);
            }

            private void RunSamplingBytecodeRange(int streamIndex, float time, float[] pool, Pose output, int start, int end, int depth)
            {
                const int MaxInstructions = 100000;
                const int MaxNestedBlocks = 16;
                if (depth > MaxNestedBlocks)
                    throw new InvalidDataException($"Nested sampling-bytecode blocks exceeded {MaxNestedBlocks} levels in stream {streamIndex}.");

                StreamInfo stream = clip.Streams[streamIndex];
                byte[] data = clip.RawCompStream;
                int q = start;

                for (int instructionIndex = 0; instructionIndex < MaxInstructions && q < end; instructionIndex++)
                {
                    int op = data[q];
                    if (op == 0)
                        return; // Native FUN_7100e56cb0: stop this dispatch pass.

                    if (op == 1)
                    {
                        // Native FUN_71000615d0: after the opcode is a uint16 byte length,
                        // followed by a nested stream of sampling instructions. When the native
                        // cache misses it evaluates that stream and returns null to stop this pass.
                        // Pose is rebuilt for each Sample call, so we reevaluate the nested body.
                        if (q > end - 3)
                            throw new InvalidDataException($"Truncated opcode-1 header in stream {streamIndex} at 0x{q:X}.");

                        int payloadLength = CHPRCompStreamAnimation.ReadUInt16(data, q + 1);
                        int bodyStart = q + 3;
                        int bodyEnd = checked(bodyStart + payloadLength);
                        if (bodyEnd > end)
                            throw new InvalidDataException($"Opcode-1 block in stream {streamIndex} at 0x{q:X} extends past its containing bytecode range.");

                        RunSamplingBytecodeRange(streamIndex, time, pool, output, bodyStart, bodyEnd, depth + 1);
                        return;
                    }

                    if (op == 4)
                    {
                        // Native FUN_7100e58e10 consumes {count, descriptor-group byte, uint16 targets...}
                        // and updates auxiliary output bitfields. The current Pose model does not yet
                        // represent that output storage; preserve the native instruction advance safely.
                        if (q > end - 3)
                            throw new InvalidDataException($"Truncated opcode-4 header in stream {streamIndex} at 0x{q:X}.");
                        int bitfieldCount = data[q + 1];
                        int bitfieldBytes = checked(bitfieldCount * 2);
                        RequireSamplingBytecodeRange(data, q + 3, bitfieldBytes, end, "bitfield sampling entries", streamIndex, q);
                        q += 3 + bitfieldBytes;
                        continue;
                    }

                    if (op == 5)
                    {
                        // Native FUN_710006edd0 has no inline payload, but updates external action/event
                        // structures. That side effect is intentionally not fabricated as a transform.
                        q += 1;
                        continue;
                    }

                    if (q > end - 4)
                        throw new InvalidDataException($"Truncated sampling-bytecode instruction in stream {streamIndex} at 0x{q:X}.");

                    int count = data[q + 1];
                    int destination = CHPRCompStreamAnimation.ReadUInt16(data, q + 2);
                    switch (op)
                    {
                        case 2:
                            {
                                int bytes = checked(count * 6);
                                RequireSamplingBytecodeRange(data, q + 4, bytes, end, "transform sampling entries", streamIndex, q);
                                for (int i = 0; i < count; i++)
                                {
                                    int entry = q + 4 + 6 * i;
                                    float[] transform = new float[10];
                                    DecodeRotation(streamIndex, CHPRCompStreamAnimation.ReadUInt16(data, entry), time, pool, transform, 0);

                                    int scaleRef = CHPRCompStreamAnimation.ReadUInt16(data, entry + 2);
                                    if ((scaleRef & 0x8000) == 0)
                                    {
                                        int scaleIndex = scaleRef & 0x3FFF;
                                        int scaleCount = (scaleRef & 0x4000) != 0 ? 1 : 3;
                                        CopyConstants(pool, scaleIndex, scaleCount, transform, 4);
                                        if ((scaleRef & 0x4000) != 0)
                                        {
                                            transform[5] = transform[4];
                                            transform[6] = transform[4];
                                        }
                                    }
                                    else if ((scaleRef & 0x4000) != 0)
                                    {
                                        transform[4] = DecodeScalar(streamIndex, scaleRef, time);
                                        transform[5] = transform[4];
                                        transform[6] = transform[4];
                                    }
                                    else
                                    {
                                        DecodeVector(streamIndex, scaleRef, time, transform, 4);
                                    }

                                    // Native FUN_710021dfd0 adds one to each decoded scale component.
                                    transform[4] += 1.0f;
                                    transform[5] += 1.0f;
                                    transform[6] += 1.0f;

                                    int translationRef = CHPRCompStreamAnimation.ReadUInt16(data, entry + 4);
                                    if ((translationRef & 0x8000) == 0)
                                        CopyConstants(pool, translationRef, 3, transform, 7);
                                    else
                                        DecodeVector(streamIndex, translationRef, time, transform, 7);

                                    output.Transforms[AdvanceDestination(destination, 5 * i)] = transform;
                                }
                                q += 4 + bytes;
                                break;
                            }
                        case 3:
                            {
                                int bytes = checked(count * 4);
                                RequireSamplingBytecodeRange(data, q + 4, bytes, end, "rotation/translation sampling entries", streamIndex, q);
                                for (int i = 0; i < count; i++)
                                {
                                    int entry = q + 4 + 4 * i;
                                    float[] transform = new float[7];
                                    DecodeRotation(streamIndex, CHPRCompStreamAnimation.ReadUInt16(data, entry), time, pool, transform, 0);

                                    int translationRef = CHPRCompStreamAnimation.ReadUInt16(data, entry + 2);
                                    if ((translationRef & 0x8000) == 0)
                                        CopyConstants(pool, translationRef, 3, transform, 4);
                                    else
                                        DecodeVector(streamIndex, translationRef, time, transform, 4);

                                    output.Transforms[AdvanceDestination(destination, 7 * i)] = transform;
                                }
                                q += 4 + bytes;
                                break;
                            }
                        case 6:
                            {
                                int bytes = checked(count * 2);
                                RequireSamplingBytecodeRange(data, q + 4, bytes, end, "scalar sampling entries", streamIndex, q);
                                for (int i = 0; i < count; i++)
                                {
                                    int reference = CHPRCompStreamAnimation.ReadUInt16(data, q + 4 + 2 * i);
                                    float value;
                                    if ((reference & 0x8000) == 0)
                                        value = ConstantAt(pool, reference & 0x3FFF);
                                    else
                                        value = DecodeScalar(streamIndex, reference, time);
                                    output.Floats[AdvanceDestination(destination, i)] = value;
                                }
                                q += 4 + bytes;
                                break;
                            }
                        case 7:
                            {
                                int bytes = checked(count * 2);
                                RequireSamplingBytecodeRange(data, q + 4, bytes, end, "integer sampling entries", streamIndex, q);
                                for (int i = 0; i < count; i++)
                                {
                                    int reference = CHPRCompStreamAnimation.ReadUInt16(data, q + 4 + 2 * i);
                                    if ((reference & 0x8000) != 0)
                                    {
                                        int groupIndex = (reference >> 10) & 15;
                                        int channel = reference & 0x3FF;
                                        GroupInfo group = GetSamplingGroup(stream, groupIndex);
                                        ValidateGroupChannel(group, channel, streamIndex);
                                        if (group.Type != 5)
                                            throw new InvalidDataException($"Integer sampling reference points to group type {group.Type}, not type 5.");
                                        int valueOffset = checked(group.ValueOffset + channel * 8 + 4);
                                        RequireRange(values[streamIndex], valueOffset, 4, "integer channel value");
                                        output.Ints[AdvanceDestination(destination, i)] = ReadInt32(values[streamIndex], valueOffset);
                                    }
                                    else if (pool != null && (reference & 0x3FFF) < pool.Length)
                                    {
                                        output.Ints[AdvanceDestination(destination, i)] = BitConverter.SingleToInt32Bits(pool[reference & 0x3FFF]);
                                    }
                                }
                                q += 4 + bytes;
                                break;
                            }
                        default:
                            // The native dispatch table only has handlers for opcodes 0 through 7.
                            // Don't guess an instruction size for any other value.
                            return;
                    }
                }

                if (q >= end)
                    return;
                throw new InvalidDataException($"Sampling bytecode in stream {streamIndex} exceeded {MaxInstructions} instructions without termination.");
            }

            private static void RequireSamplingBytecodeRange(byte[] data, int offset, int count, int end, string context, int streamIndex, int instructionOffset)
            {
                if (data == null || offset < 0 || count < 0 || end < 0 || end > data.Length || offset > end - count)
                    throw new InvalidDataException($"Truncated CompStream {context} in stream {streamIndex} at 0x{instructionOffset:X} (range end 0x{end:X}).");
            }

            private void DecodeRotation(int streamIndex, int reference, float time, float[] pool, float[] output, int outputOffset)
            {
                if ((reference & 0x8000) == 0)
                {
                    CopyConstants(pool, reference & 0x3FFF, 4, output, outputOffset);
                    return;
                }

                StreamInfo stream = clip.Streams[streamIndex];
                int groupIndex = (reference >> 10) & 15;
                int channel = reference & 0x3FF;
                GroupInfo group = GetSamplingGroup(stream, groupIndex);
                ValidateGroupChannel(group, channel, streamIndex);
                int offset = checked(group.ValueOffset + channel * 0x1C);
                RequireRange(values[streamIndex], offset, 0x1C, "rotation channel");
                int field = CHPRCompStreamAnimation.ReadUInt24(values[streamIndex], offset + 1);
                float scale = 1.0f / (1 << (field & 31));
                float[] current = ReadQuaternion(values[streamIndex], offset + 4, scale, ((field >> 22) & 1) != 0);
                int absoluteChannel = checked(group.ChannelBase + channel);
                float factor = InterpolationFactor(streamIndex, absoluteChannel, time);

                if (factor < 0)
                {
                    Array.Copy(current, 0, output, outputOffset, 4);
                    return;
                }

                float[] next = ReadQuaternion(values[streamIndex], offset + 0x10, scale, ((field >> 23) & 1) != 0);
                float[] interpolated = (field & 0x80) != 0
                    ? TwoStepQuaternionBlend(current, next, factor)
                    : NormalizedLinearBlend(current, next, factor);
                Array.Copy(interpolated, 0, output, outputOffset, 4);
            }

            private void DecodeVector(int streamIndex, int reference, float time, float[] output, int outputOffset)
            {
                StreamInfo stream = clip.Streams[streamIndex];
                int groupIndex = (reference >> 10) & 15;
                int channel = reference & 0x3FF;
                GroupInfo group = GetSamplingGroup(stream, groupIndex);
                int offset = checked(group.ValueOffset + channel * 0x1C);
                RequireRange(values[streamIndex], offset, 0x1C, "vector channel");
                float scale = 1.0f / (1 << (values[streamIndex][offset + 1] & 31));
                float factor = InterpolationFactor(streamIndex, checked(group.ChannelBase + channel), time);

                for (int component = 0; component < 3; component++)
                {
                    float a = ReadInt32(values[streamIndex], offset + 4 + 4 * component) * scale;
                    if (factor < 0)
                    {
                        output[outputOffset + component] = a;
                    }
                    else
                    {
                        float b = ReadInt32(values[streamIndex], offset + 0x10 + 4 * component) * scale;
                        output[outputOffset + component] = a + factor * (b - a);
                    }
                }
            }

            private float DecodeScalar(int streamIndex, int reference, float time)
            {
                StreamInfo stream = clip.Streams[streamIndex];
                int groupIndex = (reference >> 10) & 15;
                int channel = reference & 0x3FF;
                GroupInfo group = GetSamplingGroup(stream, groupIndex);
                ValidateGroupChannel(group, channel, streamIndex);
                int offset = checked(group.ValueOffset + channel * 0x0C);
                RequireRange(values[streamIndex], offset, 0x0C, "scalar channel");
                float scale = 1.0f / (1 << (values[streamIndex][offset + 2] & 31));
                float a = ReadInt32(values[streamIndex], offset + 4) * scale;
                float factor = InterpolationFactor(streamIndex, checked(group.ChannelBase + channel), time);
                if (factor < 0)
                    return a;

                float b = ReadInt32(values[streamIndex], offset + 8) * scale;
                return a + factor * (b - a);
            }

            private float InterpolationFactor(int streamIndex, int channel, float time)
            {
                float previous = previousKeyFrames[streamIndex][channel];
                return time == previous
                    ? -1.0f
                    : (time - previous) * inverseDeltas[streamIndex][channel];
            }

            private static float[] ReadQuaternion(byte[] data, int offset, float scale, bool negativeW)
            {
                float x = ReadInt32(data, offset) * scale;
                float y = ReadInt32(data, offset + 4) * scale;
                float z = ReadInt32(data, offset + 8) * scale;
                float wSquared = 1.0f - (y * y + x * x + z * z);
                // Native FUN_710021dfd0 / FUN_7100ba8af0 sqrt this value without clamping.
                float w = MathF.Sqrt(wSquared);
                if (negativeW)
                    w = -w;
                return new[] { w, x, y, z };
            }

            private static float[] NormalizedLinearBlend(float[] a, float[] b, float amount)
            {
                float[] result = new float[4];
                for (int i = 0; i < 4; i++)
                    result[i] = a[i] + amount * (b[i] - a[i]);

                // Native source accumulates quaternion length as component 1, then 0, 2, 3.
                float lengthSquared = result[1] * result[1] + result[0] * result[0] +
                                      result[2] * result[2] + result[3] * result[3];

                // Native code performs reciprocal square-root directly, without an epsilon clamp.
                float inverseLength = 1.0f / MathF.Sqrt(lengthSquared);
                for (int i = 0; i < 4; i++)
                    result[i] *= inverseLength;
                return result;
            }

            private static float[] TwoStepQuaternionBlend(float[] a, float[] b, float amount)
            {
                // Preserve FUN_710021e5c0's accumulation order: component 1, then 0, 2, 3.
                float dot = a[1] * b[1] + b[0] * a[0] + b[2] * a[2] + b[3] * a[3];
                if (dot < -0.001953125f)
                    b = new[] { -b[0], -b[1], -b[2], -b[3] };

                float[] midpoint = NormalizedLinearBlend(a, b, 0.5f);
                if (amount < 0.5f)
                    return NormalizedLinearBlend(a, midpoint, amount + amount);
                if (amount <= 0.5f)
                    return midpoint;

                // Keep the native left-to-right arithmetic order from FUN_710021e5c0.
                float secondHalfAmount = amount - 0.5f + amount - 0.5f;
                return NormalizedLinearBlend(midpoint, b, secondHalfAmount);
            }

            private GroupInfo GetSamplingGroup(StreamInfo stream, int groupIndex)
            {
                if ((uint)groupIndex >= (uint)stream.Groups.Count)
                    throw new InvalidDataException($"Sampling reference uses group {groupIndex}, but stream {stream.Index} has {stream.Groups.Count} groups.");
                return stream.Groups[groupIndex];
            }

            private static void ValidateGroupChannel(GroupInfo group, int channel, int streamIndex)
            {
                if ((uint)channel >= (uint)group.ChannelCount)
                    throw new InvalidDataException($"Sampling reference uses channel {channel} in stream {streamIndex}, group {group.Index}, which has {group.ChannelCount} channels.");
            }

            private static int AdvanceDestination(int destination, int units)
            {
                return (destination & 0x3F) | (((destination >> 6) + units) << 6);
            }

            private static long MakeFlagKey(int stream, int group, int channel)
            {
                return ((long)stream << 40) | ((long)group << 20) | (uint)channel;
            }

            private static float[] ResolvePool(int poolIndex, IReadOnlyList<float[]> constantPools)
            {
                if (constantPools == null || (uint)poolIndex >= (uint)constantPools.Count)
                    return null;
                return constantPools[poolIndex];
            }

            private static void CopyConstants(float[] pool, int index, int count, float[] destination, int destinationOffset)
            {
                for (int i = 0; i < count; i++)
                    destination[destinationOffset + i] = ConstantAt(pool, index + i);
            }

            private static float ConstantAt(float[] pool, int index)
            {
                return pool != null && index >= 0 && index < pool.Length
                    ? pool[index]
                    : float.NaN;
            }

            private static int SignExtend(int value, int shift)
            {
                shift &= 31;
                return (value << shift) >> shift;
            }

            private static int ReadInt32(byte[] data, int offset)
            {
                return (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
            }

            private static void WriteInt32(byte[] data, int offset, int value)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), value);
            }

            private static void RequireRange(byte[] data, int offset, int count, string context)
            {
                if (data == null || offset < 0 || count < 0 || offset > data.Length - count)
                    throw new InvalidDataException($"Truncated CompStream {context} at offset 0x{offset:X} (length 0x{data?.Length ?? 0:X}).");
            }

        }
    }
}
