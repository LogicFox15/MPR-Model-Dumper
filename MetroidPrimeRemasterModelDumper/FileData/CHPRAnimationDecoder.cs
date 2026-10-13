using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RetroStudioPlugin.Files.FileData;

/// <summary>
/// Decoder for Prime 4 CHPR compressed animation payloads.
///
/// The stream-table, descriptor, keyframe, and sampling-program logic is ported from
/// Infinite00857/Mocktroid (MIT):
/// https://github.com/Infinite00857/Mocktroid
///
/// The result is a pose keyed by packed game pose addresses. This class intentionally has no
/// dependency on IONET or the model exporter so that decoding can be validated independently.
/// </summary>
public sealed class CHPRAnimationDecoder
{
    private static readonly int[] GroupBase = { 0, 0, 0, 4, 0, 0, 0, 0 };
    private static readonly int[] GroupPerChannel = { 0x1C, 0x1C, 0x1C, 2, 0xC, 8, 0, 1 };

    public sealed record Group(int Count, int Type, bool Hold, int ChannelBase, int ValueOffset, int Extra);

    public sealed record ConstantRecord(int Key, int Raw, float[]? Rotation, float[]? Translation);

    public sealed class Stream
    {
        public int BitOffset { get; internal set; }
        public int CodeOffset { get; internal set; }
        public int KeyStep { get; internal set; }
        public int LastFrame { get; internal set; }
        public int CountBits { get; internal set; }
        public int PoolIndex { get; internal set; }
        public int ChannelCount { get; internal set; }
        public int ValueSize { get; internal set; }
        public bool HasConstants { get; internal set; }
        public bool Flag0 { get; internal set; }
        public List<Group> Groups { get; } = new();
        public List<ConstantRecord> Constants { get; } = new();
    }

    public sealed class Pose
    {
        /// <summary>
        /// op 2: {qw, qx, qy, qz, sx, sy, sz, tx, ty, tz}; op 3: {qw, qx, qy, qz, tx, ty, tz}.
        /// Addresses use the game's packed pose-address convention (slot | index &lt;&lt; 6).
        /// </summary>
        public Dictionary<int, float[]> Transforms { get; } = new();
        public Dictionary<int, float> Floats { get; } = new();
        public Dictionary<int, int> Integers { get; } = new();
    }

    private readonly byte[] _payload;
    private readonly int _descriptorOffset;
    private readonly float _rate;
    private readonly int _frames;
    private readonly ulong _slotMask;
    private readonly List<Stream> _streams = new();
    private IReadOnlyList<float[]> _pools = Array.Empty<float[]>();

    public int Frames => _frames;
    public float Rate => _rate;
    public ulong SlotMask => _slotMask;
    public IReadOnlyList<Stream> Streams => _streams;

    private CHPRAnimationDecoder(byte[] payload)
    {
        _payload = payload;
        if (payload.Length < 0x20)
            throw new InvalidDataException("Animation payload is smaller than its fixed header.");

        int headerSize = U16(payload, 4);
        _descriptorOffset = U16(payload, 6);
        int bitstreamOffset = I32(payload, 8);
        _rate = F32(payload, 0x0C);
        _frames = U16(payload, 0x10);

        if (headerSize < 0x20 || headerSize >= payload.Length)
            throw new InvalidDataException($"Invalid animation stream-table offset 0x{headerSize:X}.");
        if (_descriptorOffset <= 0x20 || _descriptorOffset >= payload.Length)
            throw new InvalidDataException($"Invalid animation descriptor offset 0x{_descriptorOffset:X}.");
        if (bitstreamOffset <= _descriptorOffset || bitstreamOffset >= payload.Length)
            throw new InvalidDataException($"Invalid animation bitstream offset 0x{bitstreamOffset:X}.");
        if (!float.IsFinite(_rate) || _rate <= 0)
            throw new InvalidDataException($"Invalid animation rate {_rate}.");

        ulong mask = 0;
        for (int i = 0; i < 6; i++)
            mask |= (ulong)payload[0x12 + i] << (8 * i);
        _slotMask = mask;

        var reader = new Bits(payload, headerSize, 0);
        int streamCount = (int)reader.Read(4);
        reader.Read(10);
        reader.Read(4);
        if (streamCount > 15)
            throw new InvalidDataException($"Unreasonable animation stream count {streamCount}.");

        for (int streamIndex = 0; streamIndex < streamCount; streamIndex++)
        {
            var stream = new Stream
            {
                BitOffset = CheckedOffset(reader.Read(21), payload.Length, "bitstream"),
                CodeOffset = CheckedOffset(reader.Read(21), payload.Length, "sampling bytecode")
            };

            int groupCount = (int)reader.Read(4);
            stream.HasConstants = reader.Read(1) != 0;
            stream.Flag0 = reader.Read(1) != 0;
            stream.KeyStep = (int)reader.Read(4);
            stream.CountBits = (int)reader.Read(4);
            stream.PoolIndex = (int)reader.Read(4);
            stream.LastFrame = Math.Max(0, (_frames - 1) * stream.KeyStep);

            int channelBase = 0;
            int valueOffset = 0;
            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                int count = (int)reader.Read(10);
                int type = (int)reader.Read(3);
                bool hold = reader.Read(1) != 0;
                int extra = type == 3 ? (int)reader.Read(6) : 0;
                if (type < 0 || type >= GroupBase.Length)
                    throw new InvalidDataException($"Unsupported animation group type {type}.");

                stream.Groups.Add(new Group(count, type, hold, channelBase, valueOffset, extra));
                channelBase = checked(channelBase + count);
                valueOffset = checked(valueOffset + GroupBase[type] + GroupPerChannel[type] * count
                    + (type == 3 ? extra * 8 : 0));
                if (channelBase > 1_000_000 || valueOffset > payload.Length * 32)
                    throw new InvalidDataException("Animation stream declares an unreasonable number of channels.");
            }

            stream.ChannelCount = channelBase;
            stream.ValueSize = valueOffset;

            if (stream.HasConstants)
            {
                int constantCount = (int)reader.Read(4);
                for (int i = 0; i < constantCount; i++)
                {
                    int key = (int)reader.Read(16);
                    int raw = unchecked((int)reader.Read(32));
                    float[]? rotation = null;
                    float[]? translation = null;

                    if (reader.Read(1) != 0)
                    {
                        rotation = new float[4];
                        for (int c = 0; c < 4; c++)
                            rotation[c] = BitConverter.Int32BitsToSingle(unchecked((int)reader.Read(32)));
                    }

                    if (reader.Read(1) != 0)
                    {
                        translation = new float[3];
                        for (int c = 0; c < 3; c++)
                            translation[c] = BitConverter.Int32BitsToSingle(unchecked((int)reader.Read(32)));
                    }

                    stream.Constants.Add(new ConstantRecord(key, raw, rotation, translation));
                }
            }

            _streams.Add(stream);
        }
    }

    /// <summary>Read a payload at its byte offset within the decompressed CHPR.</summary>
    public static CHPRAnimationDecoder Read(byte[] decompressedChpr, int payloadOffset)
    {
        ArgumentNullException.ThrowIfNull(decompressedChpr);
        if (payloadOffset < 0 || payloadOffset > decompressedChpr.Length - 4)
            throw new ArgumentOutOfRangeException(nameof(payloadOffset));

        int size = I32(decompressedChpr, payloadOffset);
        if (size < 0x20 || (long)payloadOffset + size > decompressedChpr.Length)
            throw new InvalidDataException($"Animation payload at 0x{payloadOffset:X} has invalid size 0x{size:X}.");

        byte[] payload = new byte[size];
        Buffer.BlockCopy(decompressedChpr, payloadOffset, payload, 0, size);
        return new CHPRAnimationDecoder(payload);
    }

    /// <summary>Supply the CHPR's constant pools, indexed by each stream's pool number.</summary>
    public void SetConstantPools(IReadOnlyList<float[]>? pools) =>
        _pools = pools ?? Array.Empty<float[]>();

    /// <summary>
    /// Locate the constant float pools in a decompressed CHPR. The structure is unaligned in
    /// the file, so this deliberately scans bytes rather than assuming word alignment.
    /// </summary>
    public static List<float[]> FindConstantPools(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        for (int i = 0; i + 26 <= data.Length; i++)
        {
            if (data[i] != 1 || data[i + 1] != 0 || data[i + 2] != 0 || data[i + 3] != 0 ||
                data[i + 4] != 0 || data[i + 5] != 1 || data[i + 7] != 0 || data[i + 8] != 0 ||
                data[i + 9] != 0 || data[i + 10] != 0 || data[i + 11] != 0)
                continue;

            int count = data[i + 6];
            int offset = i + 10;
            var pools = new List<float[]>();
            bool valid = count > 0 && count <= 16;

            for (int poolIndex = 0; poolIndex < count && valid; poolIndex++)
            {
                if (offset > data.Length - 4 || U16(data, offset) != poolIndex)
                {
                    valid = false;
                    break;
                }

                int floatCount = U16(data, offset + 2);
                offset += 4;
                if (floatCount < 4 || (long)offset + (long)floatCount * 4 > data.Length ||
                    F32(data, offset) != 1f || F32(data, offset + 4) != 0f)
                {
                    valid = false;
                    break;
                }

                var pool = new float[floatCount];
                for (int j = 0; j < floatCount; j++)
                    pool[j] = F32(data, offset + 4 * j);

                pools.Add(pool);
                offset += floatCount * 4;
            }

            if (valid)
                return pools;
        }

        return new List<float[]>();
    }

    public Player CreatePlayer() => new(this);

    /// <summary>Stateful playback/sampling instance. Create one player per independently sampled clip.</summary>
    public sealed class Player
    {
        private readonly CHPRAnimationDecoder _owner;
        private readonly int[] _frame;
        private readonly long[] _bitPosition;
        private readonly int[][] _last;
        private readonly int[][] _next;
        private readonly float[][] _inverseDelta;
        private readonly byte[][] _values;
        private readonly Dictionary<long, int> _flags = new();
        private bool _started;

        internal Player(CHPRAnimationDecoder owner)
        {
            _owner = owner;
            int count = owner._streams.Count;
            _frame = new int[count];
            _bitPosition = new long[count];
            _last = new int[count][];
            _next = new int[count][];
            _inverseDelta = new float[count][];
            _values = new byte[count][];

            for (int s = 0; s < count; s++)
            {
                Stream stream = owner._streams[s];
                _last[s] = new int[stream.ChannelCount];
                _next[s] = new int[stream.ChannelCount];
                _inverseDelta[s] = new float[stream.ChannelCount];
                _values[s] = new byte[stream.ValueSize];
            }

            RunDescriptors();
        }

        public int Frame(int stream) => _frame[stream];
        public long BitPosition(int stream) => _bitPosition[stream];

        /// <summary>Decode forward to the requested stream frame, reinitializing when seeking backward.</summary>
        public void Seek(int streamIndex, int targetFrame)
        {
            if (streamIndex < 0 || streamIndex >= _owner._streams.Count)
                throw new ArgumentOutOfRangeException(nameof(streamIndex));

            if (!_started || targetFrame < _frame[streamIndex])
                Init(streamIndex);

            Stream stream = _owner._streams[streamIndex];
            int target = Math.Clamp(targetFrame, 0, stream.LastFrame);
            while (_frame[streamIndex] < target)
                Step(streamIndex);
        }

        /// <summary>Sample all streams at a clip-frame time in the range 0..Frames-1.</summary>
        public Pose Sample(float time, float[]? pool = null)
        {
            var result = new Pose();
            if (_owner._streams.Count == 0)
                return result;

            if (!float.IsFinite(time))
                throw new ArgumentOutOfRangeException(nameof(time));

            if (!_started)
            {
                for (int i = 0; i < _owner._streams.Count; i++)
                    Init(i);
                _started = true;
            }

            for (int s = 0; s < _owner._streams.Count; s++)
            {
                Stream stream = _owner._streams[s];
                float t = Math.Clamp(time * stream.KeyStep, 0, stream.LastFrame);
                Seek(s, (int)Math.Floor(t));

                float[]? selectedPool = pool ??
                    (stream.PoolIndex >= 0 && stream.PoolIndex < _owner._pools.Count
                        ? _owner._pools[stream.PoolIndex]
                        : null);

                RunSample(s, t, selectedPool, result);
            }

            return result;
        }

        private void RunDescriptors()
        {
            var reader = new Bits(_owner._payload, _owner._descriptorOffset, 0);
            int currentStream = 0;
            int operationBudget = _owner._payload.Length * 8;

            while (operationBudget-- > 0)
            {
                int op = (int)reader.Read(4);
                switch (op)
                {
                    case 0:
                        currentStream = (int)reader.Read(4);
                        if (currentStream >= _owner._streams.Count)
                            throw new InvalidDataException("Descriptor references an unknown animation stream.");
                        break;

                    case 1:
                        return;

                    case 2:
                    case 3:
                    {
                        Group group = DescriptorGroup(currentStream, (int)reader.Read(4));
                        byte[] values = _values[currentStream];
                        for (int channel = 0; channel < group.Count; channel++)
                        {
                            int offset = group.ValueOffset + channel * 0x1C;
                            int bits = (int)reader.Read(5);
                            int precision = (int)reader.Read(5);
                            int sign = (int)reader.Read(1);
                            int componentMask = (int)reader.Read(3);
                            int componentList = 0;
                            int present = 0;
                            for (int component = 0; component < 3; component++)
                            {
                                if (((componentMask >> component) & 1) != 0)
                                {
                                    componentList |= component << (2 * present);
                                    present++;
                                }
                            }

                            int field = ReadField(values, offset);
                            field = (field & (op == 2 ? 0xFFC00080 : 0xFFC00000)) | precision;
                            field |= present << 8 | (((sign - bits - 1) & 31) << 11) | (componentList << 16);
                            values[offset] = (byte)bits;
                            WriteField(values, offset, field);
                        }
                        break;
                    }

                    case 4:
                    {
                        Group group = DescriptorGroup(currentStream, (int)reader.Read(4));
                        byte[] values = _values[currentStream];
                        for (int channel = 0; channel < group.Count; channel++)
                        {
                            int offset = group.ValueOffset + channel * 0xC;
                            int bits = (int)reader.Read(5);
                            int precision = (int)reader.Read(5);
                            int sign = (int)reader.Read(1);
                            values[offset] = (byte)bits;
                            values[offset + 1] = (byte)(sign - bits - 1);
                            values[offset + 2] = (byte)precision;
                        }
                        break;
                    }

                    case 5:
                    {
                        reader.Read(4);
                        int count = (int)reader.Read(12);
                        for (int i = 0; i < count; i++)
                        {
                            reader.Read(1);
                            reader.Read(16);
                        }
                        break;
                    }

                    case 6:
                    {
                        Group group = DescriptorGroup(currentStream, (int)reader.Read(4));
                        byte[] values = _values[currentStream];
                        for (int channel = 0; channel < group.Count; channel++)
                        {
                            int offset = group.ValueOffset + channel * 8;
                            int bits = (int)reader.Read(5);
                            int sign = (int)reader.Read(1);
                            values[offset] = (byte)bits;
                            values[offset + 1] = (byte)(sign - bits - 1);
                        }
                        break;
                    }

                    default:
                        throw new InvalidDataException($"Unknown animation descriptor opcode {op}.");
                }
            }

            throw new InvalidDataException("Animation descriptor program did not terminate.");
        }

        private Group DescriptorGroup(int streamIndex, int groupIndex)
        {
            if (streamIndex < 0 || streamIndex >= _owner._streams.Count)
                throw new InvalidDataException("Animation descriptor references an invalid stream.");
            var groups = _owner._streams[streamIndex].Groups;
            if (groupIndex < 0 || groupIndex >= groups.Count)
                throw new InvalidDataException("Animation descriptor references an invalid channel group.");
            return groups[groupIndex];
        }

        private void Init(int s)
        {
            Stream stream = _owner._streams[s];
            _frame[s] = 0;
            Array.Fill(_last[s], 0);
            Array.Fill(_next[s], 0);
            var reader = new Bits(_owner._payload, stream.BitOffset, 0);
            List<int[]> lists = ReadDeltas(s, reader, 0);
            HandleKeys(s, reader, lists);

            var held = new List<int[]>();
            for (int g = 0; g < stream.Groups.Count; g++)
            {
                Group group = stream.Groups[g];
                if (!group.Hold && group.Count > 0)
                    held.Add(AllChannels(g, group.Count));
            }
            HandleKeys(s, reader, held);
            _bitPosition[s] = reader.Position;
            _started = true;
        }

        private void Step(int s)
        {
            Stream stream = _owner._streams[s];
            _frame[s]++;
            var reader = new Bits(_owner._payload, stream.BitOffset, _bitPosition[s]);
            int changedCount = (int)reader.Read(stream.CountBits);
            if (changedCount != 0)
            {
                List<int[]> lists;
                if (_frame[s] == stream.LastFrame)
                {
                    lists = new List<int[]>();
                    for (int g = 0; g < stream.Groups.Count; g++)
                    {
                        Group group = stream.Groups[g];
                        if (group.Hold && group.Count > 0)
                            lists.Add(AllChannels(g, group.Count));
                    }
                }
                else
                {
                    lists = ReadDeltas(s, reader, _frame[s]);
                }

                HandleKeys(s, reader, lists);
            }

            _bitPosition[s] = reader.Position;
        }

        private static int[] AllChannels(int groupIndex, int count)
        {
            var result = new int[count + 1];
            result[0] = groupIndex;
            for (int i = 0; i < count; i++)
                result[i + 1] = i;
            return result;
        }

        private List<int[]> ReadDeltas(int s, Bits reader, int frame)
        {
            Stream stream = _owner._streams[s];
            int width = (int)reader.Read(4);
            var lists = new List<int[]>();

            for (int g = 0; g < stream.Groups.Count; g++)
            {
                Group group = stream.Groups[g];
                var tmp = new int[group.Count + 1];
                int count = 0;

                for (int c = 0; c < group.Count; c++)
                {
                    int channel = group.ChannelBase + c;
                    if (_next[s][channel] != frame)
                        continue;

                    _last[s][channel] = _next[s][channel];
                    int delta = (int)reader.Read(width) & 0xFFFF;
                    _next[s][channel] = (_next[s][channel] + delta) & 0xFFFF;
                    _inverseDelta[s][channel] = delta == 0 ? float.PositiveInfinity : 1f / delta;
                    tmp[++count] = c;
                }

                if (count > 0)
                {
                    tmp[0] = g;
                    lists.Add(tmp.Take(count + 1).ToArray());
                }
            }

            return lists;
        }

        private void HandleKeys(int s, Bits reader, List<int[]> lists)
        {
            Stream stream = _owner._streams[s];
            byte[] values = _values[s];

            foreach (int[] list in lists)
            {
                Group group = stream.Groups[list[0]];
                if (group.Type == 3)
                {
                    ulong flags = reader.Read(list.Length - 1);
                    for (int k = 1; k < list.Length; k++)
                    {
                        long key = ((long)s << 40) | ((long)list[0] << 20) | (uint)list[k];
                        _flags[key] = (int)((flags >> (k - 1)) & 1);
                    }
                    continue;
                }

                for (int k = 1; k < list.Length; k++)
                {
                    int channel = list[k];
                    switch (group.Type)
                    {
                        case 0:
                        case 1:
                        case 2:
                        {
                            int offset = group.ValueOffset + channel * 0x1C;
                            Buffer.BlockCopy(values, offset + 0x10, values, offset + 4, 12);
                            Array.Clear(values, offset + 0x10, 12);
                            int field = ReadField(values, offset);

                            if (group.Type == 0)
                            {
                                field = (field & ~(1 << 22)) | (((field >> 23) & 1) << 22);
                                int flags = (int)reader.Read(2);
                                field = (field & ~0x800080) | ((flags & 1) << 7) | (((flags >> 1) & 1) << 23);
                                WriteField(values, offset, field);
                            }

                            int componentList = (field >> 16) & 0x3F;
                            int shift = (field >> 11) & 31;
                            int bitWidth = values[offset];
                            while (true)
                            {
                                int component = componentList & 3;
                                PutI32(values, offset + 0x10 + 4 * component,
                                    SignExtend(reader.Read(bitWidth), shift));
                                if (componentList <= 3)
                                    break;
                                componentList >>= 2;
                            }
                            break;
                        }

                        case 4:
                        {
                            int offset = group.ValueOffset + channel * 0xC;
                            int decoded = SignExtend(reader.Read(values[offset]), values[offset + 1]);
                            Buffer.BlockCopy(values, offset + 8, values, offset + 4, 4);
                            PutI32(values, offset + 8, decoded);
                            break;
                        }

                        case 5:
                        {
                            int offset = group.ValueOffset + channel * 8;
                            PutI32(values, offset + 4,
                                SignExtend(reader.Read(values[offset]), values[offset + 1]));
                            break;
                        }

                        default:
                            throw new InvalidDataException($"Unsupported animation key group type {group.Type}.");
                    }
                }
            }
        }

        private void RunSample(int s, float time, float[]? pool, Pose output)
        {
            Stream stream = _owner._streams[s];
            int q = stream.CodeOffset;
            if (q < 0 || q >= _owner._payload.Length || _owner._payload[q] != 1)
                return;

            q += 3;
            int operationBudget = _owner._payload.Length;
            while (q < _owner._payload.Length && operationBudget-- > 0)
            {
                int op = _owner._payload[q];
                if (op == 0)
                    return;
                if (q + 2 >= _owner._payload.Length)
                    return;

                int count = _owner._payload[q + 1];
                if (q + 4 > _owner._payload.Length)
                    return;

                int destination = U16(_owner._payload, q + 2);
                switch (op)
                {
                    case 2:
                    {
                        if (q + 4L + 6L * count > _owner._payload.Length)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            int entry = q + 4 + 6 * i;
                            var value = new float[10];
                            Rotation(s, U16(_owner._payload, entry), time, pool, value, 0);

                            int scaleRef = U16(_owner._payload, entry + 2);
                            if ((scaleRef & 0x8000) == 0)
                            {
                                bool scalarConstant = (scaleRef & 0x4000) != 0;
                                Constants(pool, scaleRef & 0x3FFF, scalarConstant ? 1 : 3, value, 4);
                                if (scalarConstant)
                                    value[5] = value[6] = value[4];
                            }
                            else if ((scaleRef & 0x4000) != 0)
                            {
                                value[4] = Scalar(s, scaleRef, time);
                                value[5] = value[6] = value[4];
                            }
                            else
                            {
                                Vector(s, scaleRef, time, value, 4);
                            }

                            value[4] += 1;
                            value[5] += 1;
                            value[6] += 1;

                            int translationRef = U16(_owner._payload, entry + 4);
                            if ((translationRef & 0x8000) == 0)
                                Constants(pool, translationRef, 3, value, 7);
                            else
                                Vector(s, translationRef, time, value, 7);

                            output.Transforms[Advance(destination, 5 * i)] = value;
                        }

                        q += 4 + 6 * count;
                        break;
                    }

                    case 3:
                    {
                        if (q + 4L + 4L * count > _owner._payload.Length)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            int entry = q + 4 + 4 * i;
                            var value = new float[7];
                            Rotation(s, U16(_owner._payload, entry), time, pool, value, 0);
                            int translationRef = U16(_owner._payload, entry + 2);
                            if ((translationRef & 0x8000) == 0)
                                Constants(pool, translationRef, 3, value, 4);
                            else
                                Vector(s, translationRef, time, value, 4);

                            output.Transforms[Advance(destination, 7 * i)] = value;
                        }

                        q += 4 + 4 * count;
                        break;
                    }

                    case 4:
                        q += 3 + 2 * count;
                        break;

                    case 5:
                        q += 1;
                        break;

                    case 6:
                    {
                        if (q + 4L + 2L * count > _owner._payload.Length)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            int valueRef = U16(_owner._payload, q + 4 + 2 * i);
                            float value;
                            if ((valueRef & 0x8000) == 0)
                            {
                                int index = valueRef & 0x3FFF;
                                value = pool is not null && index < pool.Length ? pool[index] : float.NaN;
                            }
                            else
                            {
                                value = Scalar(s, valueRef, time);
                            }

                            output.Floats[Advance(destination, i)] = value;
                        }

                        q += 4 + 2 * count;
                        break;
                    }

                    case 7:
                    {
                        if (q + 4L + 2L * count > _owner._payload.Length)
                            return;

                        for (int i = 0; i < count; i++)
                        {
                            int valueRef = U16(_owner._payload, q + 4 + 2 * i);
                            if ((valueRef & 0x8000) != 0)
                            {
                                int groupIndex = (valueRef >> 10) & 15;
                                if (groupIndex >= stream.Groups.Count)
                                    continue;
                                Group group = stream.Groups[groupIndex];
                                int channel = valueRef & 0x3FF;
                                int offset = group.ValueOffset + channel * 8 + 4;
                                if (offset >= 0 && offset <= _values[s].Length - 4)
                                    output.Integers[Advance(destination, i)] = I32(_values[s], offset);
                            }
                            else
                            {
                                int poolIndex = valueRef & 0x3FFF;
                                if (pool is not null && poolIndex < pool.Length)
                                    output.Integers[Advance(destination, i)] = BitConverter.SingleToInt32Bits(pool[poolIndex]);
                            }
                        }

                        q += 4 + 2 * count;
                        break;
                    }

                    default:
                        return;
                }
            }
        }

        private static int Advance(int destination, int units) =>
            (destination & 0x3F) | (((destination >> 6) + units) << 6);

        private static void Constants(float[]? pool, int index, int count, float[] output, int destination)
        {
            for (int i = 0; i < count; i++)
                output[destination + i] = pool is not null && index + i >= 0 && index + i < pool.Length
                    ? pool[index + i]
                    : float.NaN;
        }

        private float Factor(int stream, int channel, float time)
        {
            float last = _last[stream][channel];
            return time == last ? -1 : (time - last) * _inverseDelta[stream][channel];
        }

        private void Rotation(int streamIndex, int reference, float time, float[]? pool, float[] output, int destination)
        {
            if ((reference & 0x8000) == 0)
            {
                Constants(pool, reference & 0x3FFF, 4, output, destination);
                return;
            }

            Stream stream = _owner._streams[streamIndex];
            int groupIndex = (reference >> 10) & 15;
            int channel = reference & 0x3FF;
            if (groupIndex >= stream.Groups.Count)
            {
                Array.Fill(output, float.NaN, destination, 4);
                return;
            }

            Group group = stream.Groups[groupIndex];
            int offset = group.ValueOffset + channel * 0x1C;
            if (offset < 0 || offset > _values[streamIndex].Length - 0x1C)
            {
                Array.Fill(output, float.NaN, destination, 4);
                return;
            }

            byte[] values = _values[streamIndex];
            int field = ReadField(values, offset);
            float scale = 1f / (1 << (field & 31));
            float[] a = Quaternion(values, offset + 4, scale, ((field >> 22) & 1) != 0);
            float factor = Factor(streamIndex, group.ChannelBase + channel, time);
            if (factor < 0)
            {
                Array.Copy(a, 0, output, destination, 4);
                return;
            }

            float[] b = Quaternion(values, offset + 0x10, scale, ((field >> 23) & 1) != 0);
            float[] rotation = (field & 0x80) != 0 ? TwoStep(a, b, factor) : Nlerp(a, b, factor);
            Array.Copy(rotation, 0, output, destination, 4);
        }

        private static float[] Quaternion(byte[] values, int offset, float scale, bool negativeW)
        {
            float x = I32(values, offset) * scale;
            float y = I32(values, offset + 4) * scale;
            float z = I32(values, offset + 8) * scale;
            float w = (float)Math.Sqrt(Math.Max(0, 1 - (x * x + y * y + z * z)));
            if (negativeW)
                w = -w;
            return new[] { w, x, y, z };
        }

        private void Vector(int streamIndex, int reference, float time, float[] output, int destination)
        {
            Stream stream = _owner._streams[streamIndex];
            int groupIndex = (reference >> 10) & 15;
            int channel = reference & 0x3FF;
            if (groupIndex >= stream.Groups.Count)
            {
                Array.Fill(output, float.NaN, destination, 3);
                return;
            }

            Group group = stream.Groups[groupIndex];
            int offset = group.ValueOffset + channel * 0x1C;
            if (offset < 0 || offset > _values[streamIndex].Length - 0x1C)
            {
                Array.Fill(output, float.NaN, destination, 3);
                return;
            }

            byte[] values = _values[streamIndex];
            float scale = 1f / (1 << (values[offset + 1] & 31));
            float factor = Factor(streamIndex, group.ChannelBase + channel, time);
            for (int i = 0; i < 3; i++)
            {
                float a = I32(values, offset + 4 + 4 * i) * scale;
                output[destination + i] = factor < 0
                    ? a
                    : a + factor * (I32(values, offset + 0x10 + 4 * i) * scale - a);
            }
        }

        private float Scalar(int streamIndex, int reference, float time)
        {
            Stream stream = _owner._streams[streamIndex];
            int groupIndex = (reference >> 10) & 15;
            int channel = reference & 0x3FF;
            if (groupIndex >= stream.Groups.Count)
                return float.NaN;

            Group group = stream.Groups[groupIndex];
            int offset = group.ValueOffset + channel * 0xC;
            if (offset < 0 || offset > _values[streamIndex].Length - 0xC)
                return float.NaN;

            byte[] values = _values[streamIndex];
            float scale = 1f / (1 << (values[offset + 2] & 31));
            float a = I32(values, offset + 4) * scale;
            float factor = Factor(streamIndex, group.ChannelBase + channel, time);
            return factor < 0 ? a : a + factor * (I32(values, offset + 8) * scale - a);
        }
    }

    private static float[] Nlerp(float[] a, float[] b, float t)
    {
        var result = new float[4];
        float lengthSquared = 0;
        for (int i = 0; i < 4; i++)
        {
            result[i] = a[i] + t * (b[i] - a[i]);
            lengthSquared += result[i] * result[i];
        }

        if (lengthSquared <= 1e-20f)
            return new[] { 1f, 0f, 0f, 0f };

        float inverseLength = 1f / (float)Math.Sqrt(lengthSquared);
        for (int i = 0; i < 4; i++)
            result[i] *= inverseLength;
        return result;
    }

    /// <summary>Game's two-stage normalized interpolation for channels that request it.</summary>
    private static float[] TwoStep(float[] a, float[] b, float t)
    {
        float dot = a[0] * b[0] + a[1] * b[1] + a[2] * b[2] + a[3] * b[3];
        if (dot < -1f / 512)
            b = new[] { -b[0], -b[1], -b[2], -b[3] };

        float[] middle = Nlerp(a, b, 0.5f);
        if (t < 0.5f)
            return Nlerp(a, middle, 2 * t);
        if (t > 0.5f)
            return Nlerp(middle, b, 2 * (t - 0.5f));
        return middle;
    }

    private static int CheckedOffset(long value, int length, string label)
    {
        if (value < 0 || value >= length)
            throw new InvalidDataException($"Animation {label} offset 0x{value:X} is outside its payload.");
        return (int)value;
    }

    private static int ReadField(byte[] values, int offset) =>
        values[offset + 1] | (values[offset + 2] << 8) | (values[offset + 3] << 16);

    private static void WriteField(byte[] values, int offset, int field)
    {
        values[offset + 1] = (byte)field;
        values[offset + 2] = (byte)(field >> 8);
        values[offset + 3] = (byte)(field >> 16);
    }

    private static int SignExtend(long value, int shift)
    {
        shift &= 31;
        return (unchecked((int)value) << shift) >> shift;
    }

    private sealed class Bits
    {
        private readonly byte[] _data;
        private readonly int _start;
        public long Position { get; private set; }

        public Bits(byte[] data, int start, long bitPosition)
        {
            _data = data;
            _start = start;
            Position = bitPosition;
        }

        public ulong Read(int count)
        {
            if (count < 0 || count > 64)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return 0;

            ulong value = 0;
            int received = 0;
            while (received < count)
            {
                long byteIndexLong = (long)_start + (Position >> 3);
                int bitOffset = (int)(Position & 7);
                int take = Math.Min(8 - bitOffset, count - received);
                int current = byteIndexLong >= 0 && byteIndexLong < _data.Length
                    ? _data[(int)byteIndexLong]
                    : 0;

                int mask = take == 8 ? 0xFF : (1 << take) - 1;
                value |= (ulong)((current >> bitOffset) & mask) << received;
                received += take;
                Position += take;
            }

            return value;
        }
    }

    private static ushort U16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));

    private static int I32(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int)));

    private static float F32(byte[] data, int offset) =>
        BitConverter.Int32BitsToSingle(I32(data, offset));

    private static void PutI32(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, sizeof(int)), value);
}
