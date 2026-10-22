using System;
using System.Collections.Generic;
using System.IO;

namespace DKCTF;

/// <summary>
/// Ghidra-first parser for the type-0 CHPR CompStream object.
///
/// This version implements the data-layout and stream-table reads observable in
/// FUN_71003cec08 and the LSB-first bit-reader behavior in FUN_71008a6d44.
/// It deliberately does not infer descriptor bytecode or sampled pose semantics
/// until the corresponding native handler functions have been decompiled.
/// </summary>
public sealed class CHPRAnimationDecoder
{
    public sealed class ConstantRecord
    {
        public ushort Key { get; internal set; }
        public uint RawValue { get; internal set; }
        public float[]? Rotation { get; internal set; }
        public float[]? Translation { get; internal set; }
    }

    public sealed class ChannelGroup
    {
        public int Index { get; internal set; }
        public int ChannelCount { get; internal set; }
        public int HandlerIndex { get; internal set; }
        public bool Flag13 { get; internal set; }
        public int ExtraBits { get; internal set; }
    }

    public sealed class NativeStream
    {
        // Native stream record fields populated by FUN_71003cec08.
        public int Offset21A { get; internal set; }
        public int Offset21B { get; internal set; }
        public bool HasConstantRecords { get; internal set; }
        public bool Flag0 { get; internal set; }
        public int KeyStep { get; internal set; }
        public int CountBitWidth { get; internal set; }
        public int ConstantPoolField { get; internal set; }
        public int TableHeaderField10 { get; internal set; }
        public int TableHeaderField4 { get; internal set; }
        public List<ChannelGroup> Groups { get; } = new();
        public List<ConstantRecord> Constants { get; } = new();
    }

    private readonly byte[] _payload;
    private readonly List<NativeStream> _streams = new();

    public uint TotalSize { get; }
    public ushort StreamTableOffset { get; }
    public ushort DescriptorProgramOffset { get; }
    public uint HeaderValueAt08 { get; }
    public float Rate { get; }
    public ushort FrameCount { get; }
    public byte[] HeaderBytes18To23 { get; }
    public byte[] HeaderExtension { get; }
    public ushort NativeWordAt1A { get; }
    public IReadOnlyList<NativeStream> Streams => _streams;
    public long StreamTableBitsConsumed { get; private set; }

    private CHPRAnimationDecoder(byte[] payload)
    {
        _payload = payload;
        if (payload.Length < 0x1C)
            throw new InvalidDataException("CHPR CompStream is smaller than the 0x1C-byte native header read by FUN_71003cec08.");

        TotalSize = ReadU32(payload, 0x00);
        StreamTableOffset = ReadU16(payload, 0x04);
        DescriptorProgramOffset = ReadU16(payload, 0x06);
        HeaderValueAt08 = ReadU32(payload, 0x08);
        Rate = ReadF32(payload, 0x0C);
        FrameCount = ReadU16(payload, 0x10);
        HeaderBytes18To23 = Slice(payload, 0x12, 6);
        NativeWordAt1A = ReadU16(payload, 0x1A);

        if (TotalSize < 0x1C || TotalSize > payload.Length)
            throw new InvalidDataException("CompStream total size is outside its containing animation entry.");
        if (StreamTableOffset < 0x1C || StreamTableOffset >= TotalSize)
            throw new InvalidDataException("CompStream stream-table offset is outside the declared payload.");
        if (DescriptorProgramOffset >= TotalSize)
            throw new InvalidDataException("CompStream descriptor-program offset is outside the declared payload.");

        HeaderExtension = Slice(payload, 0x18, StreamTableOffset - 0x18);
        ParseNativeStreamTable();
    }

    /// <summary>
    /// Parse a CAnim type-0 CompStream at compStreamOffset inside an animation entry.
    /// The entry is copied to the declared native TotalSize; no byte-pattern scanning is used.
    /// </summary>
    public static CHPRAnimationDecoder Read(byte[] animationEntry, int compStreamOffset)
    {
        ArgumentNullException.ThrowIfNull(animationEntry);
        if (compStreamOffset < 0 || compStreamOffset > animationEntry.Length - 4)
            throw new ArgumentOutOfRangeException(nameof(compStreamOffset));

        uint totalSize = ReadU32(animationEntry, compStreamOffset);
        if (totalSize < 0x1C || totalSize > int.MaxValue ||
            (long)compStreamOffset + totalSize > animationEntry.Length)
        {
            throw new InvalidDataException(
                "CompStream TotalSize is invalid for animation-entry offset 0x" +
                compStreamOffset.ToString("X") + ".");
        }

        byte[] payload = Slice(animationEntry, compStreamOffset, checked((int)totalSize));
        return new CHPRAnimationDecoder(payload);
    }

    private void ParseNativeStreamTable()
    {
        // FUN_71003cec08 starts its bit-reader at base + the u16 field at +0x04.
        // The first 4, 10, and 4-bit fields are read before its per-stream loop.
        var bits = new GhidraBitReader(_payload, StreamTableOffset, checked((int)TotalSize));
        int streamCount = checked((int)bits.ReadBits(4));
        int tableHeader10 = checked((int)bits.ReadBits(10));
        int tableHeader4 = checked((int)bits.ReadBits(4));

        if (streamCount > 15)
            throw new InvalidDataException("Native CompStream stream count exceeds its four-bit field.");

        for (int streamIndex = 0; streamIndex < streamCount; streamIndex++)
        {
            var stream = new NativeStream
            {
                Offset21A = checked((int)bits.ReadBits(21)),
                Offset21B = checked((int)bits.ReadBits(21))
            };

            int groupCount = checked((int)bits.ReadBits(4));
            stream.HasConstantRecords = bits.ReadBits(1) != 0;
            stream.Flag0 = bits.ReadBits(1) != 0;
            stream.KeyStep = checked((int)bits.ReadBits(4));
            stream.CountBitWidth = checked((int)bits.ReadBits(4));
            stream.ConstantPoolField = checked((int)bits.ReadBits(4));
            stream.TableHeaderField10 = tableHeader10;
            stream.TableHeaderField4 = tableHeader4;

            if (groupCount > 15)
                throw new InvalidDataException("Native CompStream group count exceeds its four-bit field.");

            for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
            {
                int channelCount = checked((int)bits.ReadBits(10));
                int handlerIndex = checked((int)bits.ReadBits(3));
                bool flag13 = bits.ReadBits(1) != 0;
                int extra = handlerIndex == 3 ? checked((int)bits.ReadBits(6)) : 0;

                stream.Groups.Add(new ChannelGroup
                {
                    Index = groupIndex,
                    ChannelCount = channelCount,
                    HandlerIndex = handlerIndex,
                    Flag13 = flag13,
                    ExtraBits = extra
                });
            }

            // FUN_71003cec08 reads the following constant-record block only when the
            // stream's native HasConstantRecords flag is set.
            if (stream.HasConstantRecords)
            {
                int constantCount = checked((int)bits.ReadBits(4));
                for (int i = 0; i < constantCount; i++)
                {
                    var constant = new ConstantRecord
                    {
                        Key = checked((ushort)bits.ReadBits(16)),
                        RawValue = checked((uint)bits.ReadBits(32))
                    };

                    if (bits.ReadBits(1) != 0)
                    {
                        constant.Rotation = new float[4];
                        for (int c = 0; c < 4; c++)
                            constant.Rotation[c] = BitConverter.Int32BitsToSingle(
                                unchecked((int)bits.ReadBits(32)));
                    }

                    if (bits.ReadBits(1) != 0)
                    {
                        constant.Translation = new float[3];
                        for (int c = 0; c < 3; c++)
                            constant.Translation[c] = BitConverter.Int32BitsToSingle(
                                unchecked((int)bits.ReadBits(32)));
                    }

                    stream.Constants.Add(constant);
                }
            }

            _streams.Add(stream);
        }

        StreamTableBitsConsumed = bits.BitsConsumed;
    }

    /// <summary>
    /// Strict LSB-first reader modeled on FUN_71008a6d44: bits are taken from the
    /// low end of a 64-bit little-endian reservoir; exhausting the payload is an error.
    /// </summary>
    private sealed class GhidraBitReader
    {
        private readonly byte[] _data;
        private readonly int _start;
        private readonly int _end;
        private long _position;

        public long BitsConsumed => _position;

        public GhidraBitReader(byte[] data, int start, int end)
        {
            if (start < 0 || end < start || end > data.Length)
                throw new ArgumentOutOfRangeException(nameof(start));
            _data = data;
            _start = start;
            _end = end;
        }

        public ulong ReadBits(int count)
        {
            if (count < 0 || count > 64)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return 0;

            ulong value = 0;
            int written = 0;
            while (written < count)
            {
                long absoluteByte = (long)_start + (_position >> 3);
                if (absoluteByte >= _end)
                    throw new EndOfStreamException(
                        "Truncated native CHPR bitstream at bit " + _position + ".");

                int bitInByte = (int)(_position & 7);
                int take = Math.Min(8 - bitInByte, count - written);
                int mask = take == 8 ? 0xFF : (1 << take) - 1;
                ulong part = (ulong)((_data[(int)absoluteByte] >> bitInByte) & mask);
                value |= part << written;

                written += take;
                _position += take;
            }

            return value;
        }
    }

    private static ushort ReadU16(byte[] data, int offset) =>
        (ushort)(data[offset] | data[offset + 1] << 8);

    private static uint ReadU32(byte[] data, int offset) =>
        (uint)(data[offset] | data[offset + 1] << 8 |
               data[offset + 2] << 16 | data[offset + 3] << 24);

    private static float ReadF32(byte[] data, int offset) =>
        BitConverter.Int32BitsToSingle(unchecked((int)ReadU32(data, offset)));

    private static byte[] Slice(byte[] data, int offset, int count)
    {
        if (offset < 0 || count < 0 || (long)offset + count > data.Length)
            throw new InvalidDataException("CompStream byte range is outside the available data.");
        var result = new byte[count];
        Buffer.BlockCopy(data, offset, result, 0, count);
        return result;
    }
}
