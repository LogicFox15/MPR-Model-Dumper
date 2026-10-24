using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

#nullable disable

namespace DKCTF
{
    /// <summary>
    /// Structural decoder for a CHPR CompStream animation block.
    ///
    /// Offsets in this class are relative to the beginning of the CompStream,
    /// not to the outer animation-table payload (which starts with SBaseInfo).
    /// The parser deliberately retains unknown header fields and descriptor
    /// operands so later frame-decoding work does not need to re-read the file.
    /// </summary>
    public static partial class CHPRCompStreamAnimation
    {
        private const int MinimumHeaderSize = 0x20;
        private const int MaximumDescriptorCommands = 100000;

        // Value-record layout used by the native stream handlers.
        private static readonly int[] ValueBaseSizes = { 0, 0, 0, 4, 0, 0, 0, 0 };
        private static readonly int[] ValuePerChannelSizes = { 0x1C, 0x1C, 0x1C, 2, 0x0C, 8, 0, 1 };

        public sealed class ParsedClip
        {
            public uint TotalSize { get; internal set; }
            public ushort HeaderSize { get; internal set; }
            public ushort DescriptorOffset { get; internal set; }
            public uint BitstreamOffset { get; internal set; }
            public float Rate { get; internal set; }
            public ushort FrameCount { get; internal set; }
            public ulong OutputSlotMask { get; internal set; }
            public ushort DecodedSize { get; internal set; }
            public ushort HeaderValueAt0x1A { get; internal set; }
            public byte[] HeaderExtension { get; internal set; } = Array.Empty<byte>();
            public byte[] RawCompStream { get; internal set; } = Array.Empty<byte>();

            public List<StreamInfo> Streams { get; } = new List<StreamInfo>();
            public List<DescriptorCommand> DescriptorCommands { get; } = new List<DescriptorCommand>();
            public bool DescriptorTerminated { get; internal set; }
            public long DescriptorBitsConsumed { get; internal set; }
            public int DescriptorStreamSwitchCount { get; internal set; }
        }

        public sealed class StreamInfo
        {
            public int Index { get; internal set; }
            public int BitstreamOffset { get; internal set; }
            public int SamplingBytecodeOffset { get; internal set; }
            public int GroupCount { get; internal set; }
            public bool HasConstants { get; internal set; }
            public bool Flag0 { get; internal set; }
            public int RateMultiplier { get; internal set; }
            public int LastFrame { get; internal set; }
            public int ChangedChannelCountBits { get; internal set; }
            public int ConstantPoolIndex { get; internal set; }
            public int ChannelCount { get; internal set; }
            public int ValueBufferSize { get; internal set; }
            public byte[] ValueBuffer { get; internal set; } = Array.Empty<byte>();

            public List<GroupInfo> Groups { get; } = new List<GroupInfo>();
            public List<ConstantRecord> Constants { get; } = new List<ConstantRecord>();
        }

        public sealed class GroupInfo
        {
            public int Index { get; internal set; }
            public int ChannelCount { get; internal set; }
            public int Type { get; internal set; }
            public bool Hold { get; internal set; }
            public int ChannelBase { get; internal set; }
            public int ValueOffset { get; internal set; }
            public int ExtraCount { get; internal set; }
        }

        public sealed class ConstantRecord
        {
            public ushort Key { get; internal set; }
            public uint RawValue { get; internal set; }
            public float[] Rotation { get; internal set; }
            public float[] Translation { get; internal set; }
        }

        public sealed class DescriptorCommand
        {
            public int Opcode { get; internal set; }
            public long StartBit { get; internal set; }
            public long EndBit { get; internal set; }
            public int? SelectedStreamIndex { get; internal set; }
            public int? GroupIndex { get; internal set; }
            public int? ItemCount { get; internal set; }
            public int? IgnoredNibble { get; internal set; }
            public List<DescriptorItem> Items { get; } = new List<DescriptorItem>();
        }

        public sealed class DescriptorItem
        {
            // The names are intentionally generic: these are the raw bit fields
            // read by the native descriptor handlers, not asserted semantic names.
            public uint Field0 { get; internal set; }
            public uint Field1 { get; internal set; }
            public bool Flag { get; internal set; }
            public uint Field2 { get; internal set; }
            public ushort Value16 { get; internal set; }
        }

        /// <summary>
        /// Parses a complete animation-table payload. The leading BaseInfo record
        /// has variable length, so its length is measured from its conditional flags
        /// rather than assumed to be 17 bytes.
        /// </summary>
        public static ParsedClip ParseAnimationPayload(byte[] rawAnimationPayload, CHPRAnimationDecoder.CAnim parsedAnimation)
        {
            if (rawAnimationPayload == null)
                throw new ArgumentNullException(nameof(rawAnimationPayload));
            if (parsedAnimation == null)
                throw new ArgumentNullException(nameof(parsedAnimation));
            if (parsedAnimation.BaseInfo == null || parsedAnimation.BaseInfo.Type != CHPRAnimationDecoder.AnimType.CompStream)
                throw new InvalidDataException("The supplied animation is not a CompStream animation.");
            if (parsedAnimation.AnimCompStream == null)
                throw new InvalidDataException("The CompStream header was not parsed.");

            int baseInfoSize = GetBaseInfoSize(rawAnimationPayload);
            uint declaredSize = parsedAnimation.AnimCompStream.TotalSize;
            if (declaredSize > int.MaxValue)
                throw new InvalidDataException("CompStream is too large for an in-memory animation decoder.");
            if ((long)baseInfoSize + declaredSize > rawAnimationPayload.Length)
                throw new InvalidDataException("The CompStream range extends beyond its animation-table payload.");

            int size = checked((int)declaredSize);
            byte[] compStream = new byte[size];
            Buffer.BlockCopy(rawAnimationPayload, baseInfoSize, compStream, 0, size);
            return Parse(compStream);
        }

        /// <summary>
        /// Parses a CompStream block whose byte zero is its own u32 size field.
        /// The input is expected to contain exactly one CompStream block.
        /// </summary>
        public static ParsedClip Parse(byte[] compStream)
        {
            if (compStream == null)
                throw new ArgumentNullException(nameof(compStream));
            if (compStream.Length < MinimumHeaderSize)
                throw new InvalidDataException("CompStream is shorter than its 0x20-byte header.");

            uint totalSize = ReadUInt32(compStream, 0);
            ushort headerSize = ReadUInt16(compStream, 4);
            ushort descriptorOffset = ReadUInt16(compStream, 6);
            uint bitstreamOffset = ReadUInt32(compStream, 8);
            float rate = BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt32(compStream, 0x0C)));
            ushort frameCount = ReadUInt16(compStream, 0x10);

            ulong outputSlotMask = 0;
            for (int i = 0; i < 6; i++)
                outputSlotMask |= (ulong)compStream[0x12 + i] << (i * 8);

            ushort decodedSize = ReadUInt16(compStream, 0x18);
            ushort headerValueAt0x1A = ReadUInt16(compStream, 0x1A);

            if (totalSize < MinimumHeaderSize || totalSize > int.MaxValue)
                throw new InvalidDataException("CompStream has an invalid declared size.");
            if (totalSize != compStream.Length)
                throw new InvalidDataException($"CompStream size mismatch: header declares 0x{totalSize:X} bytes, input contains 0x{compStream.Length:X}.");
            if (headerSize < MinimumHeaderSize || headerSize > totalSize)
                throw new InvalidDataException($"CompStream stream-table offset 0x{headerSize:X} is outside the block.");
            if (descriptorOffset < MinimumHeaderSize || descriptorOffset >= totalSize)
                throw new InvalidDataException($"CompStream descriptor offset 0x{descriptorOffset:X} is outside the block.");
            if (bitstreamOffset > totalSize)
                throw new InvalidDataException($"CompStream bitstream offset 0x{bitstreamOffset:X} is outside the block.");

            var result = new ParsedClip
            {
                TotalSize = totalSize,
                HeaderSize = headerSize,
                DescriptorOffset = descriptorOffset,
                BitstreamOffset = bitstreamOffset,
                Rate = rate,
                FrameCount = frameCount,
                OutputSlotMask = outputSlotMask,
                DecodedSize = decodedSize,
                HeaderValueAt0x1A = headerValueAt0x1A,
                RawCompStream = (byte[])compStream.Clone(),
                HeaderExtension = Slice(compStream, 0x1C, headerSize - 0x1C)
            };

            ParseStreamTable(result);
            ParseDescriptors(result);
            return result;
        }

        private static void ParseStreamTable(ParsedClip clip)
        {
            var bits = new LsbBitReader(clip.RawCompStream, clip.HeaderSize, 0);
            int streamCount = checked((int)bits.Read(4));
            bits.Read(10); // Reserved/unknown stream-table bits.
            bits.Read(4);  // Reserved/unknown stream-table bits.

            for (int s = 0; s < streamCount; s++)
            {
                var stream = new StreamInfo
                {
                    Index = s,
                    BitstreamOffset = checked((int)bits.Read(21)),
                    SamplingBytecodeOffset = checked((int)bits.Read(21)),
                    GroupCount = checked((int)bits.Read(4)),
                    HasConstants = bits.Read(1) != 0,
                    Flag0 = bits.Read(1) != 0,
                    RateMultiplier = checked((int)bits.Read(4)),
                    ChangedChannelCountBits = checked((int)bits.Read(4)),
                    ConstantPoolIndex = checked((int)bits.Read(4))
                };

                stream.LastFrame = clip.FrameCount == 0
                    ? 0
                    : checked((clip.FrameCount - 1) * stream.RateMultiplier);

                int channelBase = 0;
                int valueOffset = 0;

                for (int g = 0; g < stream.GroupCount; g++)
                {
                    int channelCount = checked((int)bits.Read(10));
                    int type = checked((int)bits.Read(3));
                    bool hold = bits.Read(1) != 0;
                    int extra = type == 3 ? checked((int)bits.Read(6)) : 0;

                    var group = new GroupInfo
                    {
                        Index = g,
                        ChannelCount = channelCount,
                        Type = type,
                        Hold = hold,
                        ChannelBase = channelBase,
                        ValueOffset = valueOffset,
                        ExtraCount = extra
                    };
                    stream.Groups.Add(group);

                    channelBase = checked(channelBase + channelCount);
                    valueOffset = checked(valueOffset +
                        ValueBaseSizes[type] +
                        ValuePerChannelSizes[type] * channelCount +
                        (type == 3 ? extra * 8 : 0));
                }

                stream.ChannelCount = channelBase;
                stream.ValueBufferSize = valueOffset;
                stream.ValueBuffer = new byte[valueOffset];

                if (stream.BitstreamOffset >= clip.TotalSize)
                    throw new InvalidDataException($"Stream {s} bitstream offset 0x{stream.BitstreamOffset:X} is outside CompStream.");
                if (stream.SamplingBytecodeOffset >= clip.TotalSize)
                    throw new InvalidDataException($"Stream {s} sampling-bytecode offset 0x{stream.SamplingBytecodeOffset:X} is outside CompStream.");

                if (stream.HasConstants)
                {
                    int constantCount = checked((int)bits.Read(4));
                    for (int c = 0; c < constantCount; c++)
                    {
                        var constant = new ConstantRecord
                        {
                            Key = checked((ushort)bits.Read(16)),
                            RawValue = checked((uint)bits.Read(32))
                        };

                        if (bits.Read(1) != 0)
                        {
                            constant.Rotation = new float[4];
                            for (int component = 0; component < 4; component++)
                                constant.Rotation[component] = ReadSingleBits(bits.Read(32));
                        }

                        if (bits.Read(1) != 0)
                        {
                            constant.Translation = new float[3];
                            for (int component = 0; component < 3; component++)
                                constant.Translation[component] = ReadSingleBits(bits.Read(32));
                        }

                        stream.Constants.Add(constant);
                    }
                }

                clip.Streams.Add(stream);
            }
        }

        private static void ParseDescriptors(ParsedClip clip)
        {
            var bits = new LsbBitReader(clip.RawCompStream, clip.DescriptorOffset, 0);
            int? currentStream = null;

            for (int commandIndex = 0; commandIndex < MaximumDescriptorCommands; commandIndex++)
            {
                long startBit = bits.PositionBits;
                int opcode = checked((int)bits.Read(4));
                var command = new DescriptorCommand
                {
                    Opcode = opcode,
                    StartBit = startBit
                };

                if (opcode == 1)
                {
                    command.EndBit = bits.PositionBits;
                    clip.DescriptorCommands.Add(command);
                    clip.DescriptorTerminated = true;
                    clip.DescriptorBitsConsumed = bits.PositionBits;
                    return;
                }

                switch (opcode)
                {
                    case 0:
                        {
                            int streamIndex = checked((int)bits.Read(4));
                            if ((uint)streamIndex >= (uint)clip.Streams.Count)
                                throw DescriptorError(commandIndex, startBit, $"selects stream {streamIndex}, but clip has {clip.Streams.Count} streams");
                            currentStream = streamIndex;
                            command.SelectedStreamIndex = streamIndex;
                            clip.DescriptorStreamSwitchCount++;
                            break;
                        }
                    case 2:
                    case 3:
                        {
                            StreamInfo stream = RequireCurrentStream(clip, currentStream, commandIndex, startBit);
                            int groupIndex = checked((int)bits.Read(4));
                            GroupInfo group = GetGroup(stream, groupIndex, commandIndex, startBit);
                            command.GroupIndex = groupIndex;
                            command.ItemCount = group.ChannelCount;

                            for (int i = 0; i < group.ChannelCount; i++)
                            {
                                var item = new DescriptorItem
                                {
                                    Field0 = checked((uint)bits.Read(5)),
                                    Field1 = checked((uint)bits.Read(5)),
                                    Flag = bits.Read(1) != 0,
                                    Field2 = checked((uint)bits.Read(3))
                                };
                                command.Items.Add(item);
                                ApplyVectorDescriptor(stream, group, i, opcode, item);
                            }
                            break;
                        }
                    case 4:
                        {
                            StreamInfo stream = RequireCurrentStream(clip, currentStream, commandIndex, startBit);
                            int groupIndex = checked((int)bits.Read(4));
                            GroupInfo group = GetGroup(stream, groupIndex, commandIndex, startBit);
                            command.GroupIndex = groupIndex;
                            command.ItemCount = group.ChannelCount;

                            for (int i = 0; i < group.ChannelCount; i++)
                            {
                                var item = new DescriptorItem
                                {
                                    Field0 = checked((uint)bits.Read(5)),
                                    Field1 = checked((uint)bits.Read(5)),
                                    Flag = bits.Read(1) != 0
                                };
                                command.Items.Add(item);
                                ApplyScalarDescriptor(stream, group, i, item);
                            }
                            break;
                        }
                    case 5:
                        {
                            command.IgnoredNibble = checked((int)bits.Read(4));
                            int count = checked((int)bits.Read(12));
                            command.ItemCount = count;
                            for (int i = 0; i < count; i++)
                            {
                                command.Items.Add(new DescriptorItem
                                {
                                    Flag = bits.Read(1) != 0,
                                    Value16 = checked((ushort)bits.Read(16))
                                });
                            }
                            break;
                        }
                    case 6:
                        {
                            StreamInfo stream = RequireCurrentStream(clip, currentStream, commandIndex, startBit);
                            int groupIndex = checked((int)bits.Read(4));
                            GroupInfo group = GetGroup(stream, groupIndex, commandIndex, startBit);
                            command.GroupIndex = groupIndex;
                            command.ItemCount = group.ChannelCount;

                            for (int i = 0; i < group.ChannelCount; i++)
                            {
                                var item = new DescriptorItem
                                {
                                    Field0 = checked((uint)bits.Read(5)),
                                    Flag = bits.Read(1) != 0
                                };
                                command.Items.Add(item);
                                ApplyRawDescriptor(stream, group, i, item);
                            }
                            break;
                        }
                    default:
                        throw DescriptorError(commandIndex, startBit, $"uses unsupported opcode {opcode}; known descriptor handlers are 0 through 6");
                }

                command.EndBit = bits.PositionBits;
                clip.DescriptorCommands.Add(command);
            }

            throw new InvalidDataException($"CompStream descriptor stream exceeded {MaximumDescriptorCommands} commands without terminator opcode 1.");
        }

        private static void ApplyVectorDescriptor(StreamInfo stream, GroupInfo group, int channel, int opcode, DescriptorItem item)
        {
            // Native opcodes 2 and 3 describe the per-channel 0x1C-byte records.
            int offset = checked(group.ValueOffset + channel * 0x1C);
            int oldField = ReadUInt24(stream.ValueBuffer, offset + 1);
            int b = checked((int)item.Field0);
            int p = checked((int)item.Field1);
            int sign = item.Flag ? 1 : 0;
            int mask = checked((int)item.Field2);
            int list = 0;
            int listCount = 0;

            for (int component = 0; component < 3; component++)
            {
                if ((mask & (1 << component)) != 0)
                {
                    list |= component << (2 * listCount);
                    listCount++;
                }
            }

            int preserveMask = opcode == 2
                ? unchecked((int)0xFFC00080)
                : unchecked((int)0xFFC00000);
            int field = (oldField & preserveMask) | p;
            field |= listCount << 8 | (((sign - b - 1) & 31) << 11) | (list << 16);

            stream.ValueBuffer[offset] = (byte)b;
            WriteUInt24(stream.ValueBuffer, offset + 1, field);
        }

        private static void ApplyScalarDescriptor(StreamInfo stream, GroupInfo group, int channel, DescriptorItem item)
        {
            int offset = checked(group.ValueOffset + channel * 0x0C);
            int b = checked((int)item.Field0);
            int p = checked((int)item.Field1);
            int sign = item.Flag ? 1 : 0;
            stream.ValueBuffer[offset] = (byte)b;
            stream.ValueBuffer[offset + 1] = unchecked((byte)(sign - b - 1));
            stream.ValueBuffer[offset + 2] = (byte)p;
        }

        private static void ApplyRawDescriptor(StreamInfo stream, GroupInfo group, int channel, DescriptorItem item)
        {
            int offset = checked(group.ValueOffset + channel * 8);
            int b = checked((int)item.Field0);
            int sign = item.Flag ? 1 : 0;
            stream.ValueBuffer[offset] = (byte)b;
            stream.ValueBuffer[offset + 1] = unchecked((byte)(sign - b - 1));
        }

        private static StreamInfo RequireCurrentStream(ParsedClip clip, int? currentStream, int commandIndex, long startBit)
        {
            if (!currentStream.HasValue)
                throw DescriptorError(commandIndex, startBit, "uses a group before opcode 0 selects a stream");
            return clip.Streams[currentStream.Value];
        }

        private static GroupInfo GetGroup(StreamInfo stream, int groupIndex, int commandIndex, long startBit)
        {
            if ((uint)groupIndex >= (uint)stream.Groups.Count)
                throw DescriptorError(commandIndex, startBit, $"references group {groupIndex}, but stream {stream.Index} has {stream.Groups.Count} groups");
            return stream.Groups[groupIndex];
        }

        private static InvalidDataException DescriptorError(int commandIndex, long bitOffset, string message)
        {
            return new InvalidDataException($"Invalid CompStream descriptor command #{commandIndex} at bit 0x{bitOffset:X}: {message}.");
        }

        private static int GetBaseInfoSize(byte[] payload)
        {
            if (payload.Length < 10)
                throw new InvalidDataException("Animation payload is too short to contain SBaseInfo.");
            if (payload[0] != (byte)CHPRAnimationDecoder.AnimType.CompStream)
                throw new InvalidDataException("Animation payload does not begin with a CompStream SBaseInfo.");

            int offset = 6; // type, Unk1, Index, Unk3, Unk4
            if (payload[5] != 0) // Unk4 controls the presence of Unk5.
                offset++;

            if (offset >= payload.Length)
                throw new InvalidDataException("Truncated SBaseInfo before Unk6.");
            byte unk6 = payload[offset++];
            if (unk6 != 0)
                offset++; // Unk7

            offset++; // Unk8
            if (offset >= payload.Length)
                throw new InvalidDataException("Truncated SBaseInfo before shared-reference flag.");
            bool hasSharedReference = payload[offset++] != 0;
            if (hasSharedReference)
                offset = checked(offset + 4); // SharedReference

            offset = checked(offset + 4); // Unk10 and Unk12
            if (offset > payload.Length)
                throw new InvalidDataException("SBaseInfo extends beyond the animation-table payload.");
            return offset;
        }

        private static byte[] Slice(byte[] data, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new InvalidDataException("Requested CompStream slice is outside the block.");
            var result = new byte[count];
            if (count != 0)
                Buffer.BlockCopy(data, offset, result, 0, count);
            return result;
        }

        private static float ReadSingleBits(ulong bits)
        {
            return BitConverter.Int32BitsToSingle(unchecked((int)(uint)bits));
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        }

        private static int ReadUInt24(byte[] data, int offset)
        {
            return data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);
        }

        private static void WriteUInt24(byte[] data, int offset, int value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
        }

        private sealed class LsbBitReader
        {
            private readonly byte[] data;
            private readonly int startByte;
            private long positionBits;

            public long PositionBits => positionBits;

            public LsbBitReader(byte[] data, int startByte, long positionBits)
            {
                this.data = data ?? throw new ArgumentNullException(nameof(data));
                if (startByte < 0 || startByte > data.Length)
                    throw new ArgumentOutOfRangeException(nameof(startByte));
                if (positionBits < 0)
                    throw new ArgumentOutOfRangeException(nameof(positionBits));
                this.startByte = startByte;
                this.positionBits = positionBits;
            }

            public ulong Read(int count)
            {
                if (count < 0 || count > 32)
                    throw new ArgumentOutOfRangeException(nameof(count), "Bit count must be from 0 through 32.");
                if (count == 0)
                    return 0;
                if ((long)startByte * 8 + positionBits + count > (long)data.Length * 8)
                    throw new EndOfStreamException($"CompStream bitstream ended at byte 0x{startByte + (positionBits >> 3):X}, bit {(int)(positionBits & 7)}.");

                ulong value = 0;
                for (int i = 0; i < count; i++)
                {
                    int byteIndex = checked(startByte + (int)(positionBits >> 3));
                    int bitIndex = (int)(positionBits & 7);
                    value |= (ulong)((data[byteIndex] >> bitIndex) & 1) << i;
                    positionBits++;
                }
                return value;
            }
        }
    }
}
