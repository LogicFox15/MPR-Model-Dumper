using System;

namespace DKCTF
{
    /// <summary>
    /// Decompresses the Huffman-coded LZSS variants shown in the Rust reference.
    ///
    /// M = 0: 0xC format (8-bit groups)  => groupSize = 1
    /// M = 1: 0xD format (16-bit groups) => groupSize = 2
    /// M = 2: 0xE format (32-bit groups) => groupSize = 4
    ///
    /// Stream structure:
    /// - Flag bits (MSB-first) indicate: literal (0) or back-reference (1)
    /// - Literal emits groupSize bytes
    /// - Backref emits (length_code + (3 - M)) * groupSize bytes copied from (outCur - offset)
    /// - length_code, offset_lo, offset_hi are Huffman-decoded using fixed tables
    /// </summary>
    public static class HuffmanLzssDecompressor
    {
        /// <summary>
        /// Decompress into output. Returns false on malformed/truncated input.
        /// </summary>
        public static bool Decompress(ReadOnlySpan<byte> input, Span<byte> output, int m)
        {
            if ((uint)m > 2) throw new ArgumentOutOfRangeException(nameof(m), "M must be 0, 1, or 2.");
            int groupSize = 1 << m;

            var state = new DecompressionState(input);

            int outCur = 0;
            while (outCur < output.Length)
            {
                bool? isBackref = state.ReadBit();
                if (isBackref is null) return false;

                if (isBackref.Value)
                {
                    if (!state.DecodeBackref(m, output, ref outCur)) return false;
                }
                else
                {
                    // Literal emits groupSize bytes
                    if (state.BitsRemaining != 0)
                    {
                        // Bitwise literal read (not byte-aligned)
                        if (outCur + groupSize > output.Length) return false;

                        for (int i = 0; i < groupSize; i++)
                        {
                            byte? b = state.ReadByteBitwise();
                            if (b is null) return false;
                            output[outCur + i] = b.Value;
                        }

                        outCur += groupSize;
                    }
                    else
                    {
                        // Byte-aligned literal read
                        if (!state.ReadBytesDirect(output, outCur, groupSize)) return false;
                        outCur += groupSize;
                    }
                }
            }

            return true;
        }

        #region Core state + bit/byte reading

        private ref struct DecompressionState
        {
            private ReadOnlySpan<byte> _input;
            private int _pos;          // current index into _input

            private byte _flagByte;    // "current byte" used by ReadBit / ReadByteBitwise
            public int BitsRemaining;  // matches the Rust meaning: 0..7

            public DecompressionState(ReadOnlySpan<byte> input)
            {
                _input = input;
                _pos = 0;
                _flagByte = 0;
                BitsRemaining = 0;
            }

            private byte? ReadByte()
            {
                if ((uint)_pos < (uint)_input.Length)
                    return _input[_pos++];
                return null;
            }

            public bool ReadBytesDirect(Span<byte> output, int outPos, int count)
            {
                if (count < 0) return false;
                if (outPos < 0 || outPos + count > output.Length) return false;
                if (_pos + count > _input.Length) return false;

                _input.Slice(_pos, count).CopyTo(output.Slice(outPos, count));
                _pos += count;
                return true;
            }

            /// <summary>
            /// Read a single flag bit (MSB-first). Returns null if truncated.
            ///
            /// Rust logic:
            /// if bits_remaining == 0:
            ///   flag_byte = read_byte()
            ///   bits_remaining = 7
            /// else:
            ///   bits_remaining -= 1
            /// bit = (flag_byte >> bits_remaining) & 1
            /// </summary>
            public bool? ReadBit()
            {
                if (BitsRemaining == 0)
                {
                    byte? b = ReadByte();
                    if (b is null) return null;
                    _flagByte = b.Value;
                    BitsRemaining = 7;
                }
                else
                {
                    BitsRemaining -= 1;
                }

                return (((_flagByte >> BitsRemaining) & 1) != 0);
            }

            /// <summary>
            /// Read a literal byte with bitwise extraction from the bitstream.
            /// Mirrors Rust's read_byte_bitwise exactly.
            ///
            /// It consumes one raw input byte and combines it with the current _flagByte
            /// depending on BitsRemaining. Then it sets _flagByte = nextByte.
            /// </summary>
            public byte? ReadByteBitwise()
            {
                byte? next = ReadByte();
                if (next is null) return null;

                byte result;
                if (BitsRemaining == 0)
                {
                    // If aligned, the current flag byte is the literal byte.
                    result = _flagByte;
                }
                else
                {
                    int bitsFromCur = 8 - BitsRemaining; // 1..7
                    // mask = !(0xFF << bitsFromCur) but in byte domain
                    byte mask = (byte)~(0xFF << bitsFromCur);

                    result = (byte)((_flagByte << bitsFromCur) |
                                    ((next.Value >> BitsRemaining) & mask));
                }

                _flagByte = next.Value;
                return result;
            }

            #endregion

            #region Huffman decode (table-driven)

            public byte? DecodeHuffman(ReadOnlySpan<HuffmanEntry> table)
            {
                int idx = 0;
                int bitsRead = 0;
                uint codeValue = 0;

                while (idx >= 0 && idx < table.Length)
                {
                    HuffmanEntry entry = table[idx];
                    int targetBits = entry.BitCount;

                    if (targetBits > bitsRead)
                    {
                        int remaining = targetBits - bitsRead;

                        while (remaining >= 8)
                        {
                            byte? b = ReadByteBitwise();
                            if (b is null) return null;
                            codeValue = (codeValue << 8) | b.Value;
                            remaining -= 8;
                        }

                        while (remaining > 0)
                        {
                            bool? bit = ReadBit();
                            if (bit is null) return null;
                            codeValue = (codeValue << 1) | (bit.Value ? 1u : 0u);
                            remaining -= 1;
                        }

                        bitsRead = targetBits;
                    }

                    int nextIdx = idx + entry.NextOffset;
                    int thresholdIdx = nextIdx - 1;
                    if ((uint)thresholdIdx >= (uint)table.Length) return null;

                    uint threshold = table[thresholdIdx].Threshold;
                    if (codeValue <= threshold)
                    {
                        // symbol_idx = code_value + next_idx - 1 - threshold
                        int symbolIdx = (int)(codeValue + (uint)nextIdx - 1u - threshold);
                        if ((uint)symbolIdx >= (uint)table.Length) return null;
                        return table[symbolIdx].Symbol;
                    }

                    idx = nextIdx;
                }

                return null;
            }

            #endregion

            #region Backref decoding

            public bool DecodeBackref(int m, Span<byte> output, ref int outCur)
            {
                byte? lengthCode = DecodeHuffman(HuffmanTables.HUFFMAN_LENGTH);
                if (lengthCode is null) return false;

                byte? offsetLo = DecodeHuffman(HuffmanTables.HUFFMAN_OFFSET_LO);
                if (offsetLo is null) return false;

                byte? offsetHi = DecodeHuffman(HuffmanTables.HUFFMAN_OFFSET_HI);
                if (offsetHi is null) return false;

                int groupShift = m;
                int offset =
                    (offsetHi.Value << (8 + groupShift)) |
                    (offsetLo.Value << groupShift);

                int cur = outCur;
                if (offset > cur) return false;

                int copyLen = (lengthCode.Value + (3 - m)) * (1 << groupShift);
                if (copyLen < 0) return false;
                if (cur + copyLen > output.Length) return false;

                // Overlap-safe forward copy (LZ77 style)
                int src = cur - offset;
                for (int i = 0; i < copyLen; i++)
                    output[cur + i] = output[src + i];

                outCur = cur + copyLen;
                return true;
            }

            #endregion
        }

        #region Huffman table representation

        public readonly struct HuffmanEntry
        {
            public readonly ushort BitCount;
            public readonly byte NextOffset;
            public readonly byte Symbol;
            public readonly uint Threshold;

            public HuffmanEntry(ushort bitCount, byte nextOffset, byte symbol, uint threshold)
            {
                BitCount = bitCount;
                NextOffset = nextOffset;
                Symbol = symbol;
                Threshold = threshold;
            }
        }

        private static class HuffmanTables
        {
            // NOTE:
            // You should paste your three 256-entry tables here.
            // To keep this response readable, I included compact placeholders plus
            // an example of how to define them. Replace the contents with the
            // exact values from your Rust arrays.

            public static readonly HuffmanEntry[] HUFFMAN_LENGTH = new HuffmanEntry[256]
            {
                    new HuffmanEntry( 1,  1, 0x00, 0x00000000), new HuffmanEntry( 2,  1, 0x01, 0x00000002), new HuffmanEntry( 4,  1, 0x02, 0x0000000c), new HuffmanEntry( 5,  3, 0x03, 0x0000001a),
    new HuffmanEntry( 5,  2, 0x04, 0x0000001b), new HuffmanEntry( 5,  1, 0x05, 0x0000001c), new HuffmanEntry( 6,  3, 0x06, 0x0000003a), new HuffmanEntry( 6,  2, 0x07, 0x0000003b),
    new HuffmanEntry( 6,  1, 0x09, 0x0000003c), new HuffmanEntry( 7,  2, 0x08, 0x0000007a), new HuffmanEntry( 7,  1, 0xff, 0x0000007b), new HuffmanEntry( 8,  1, 0x0a, 0x000000f8),
    new HuffmanEntry( 9,  5, 0x0b, 0x000001f2), new HuffmanEntry( 9,  4, 0x0c, 0x000001f3), new HuffmanEntry( 9,  3, 0x0d, 0x000001f4), new HuffmanEntry( 9,  2, 0x0e, 0x000001f5),
    new HuffmanEntry( 9,  1, 0x0f, 0x000001f6), new HuffmanEntry(10,  3, 0x10, 0x000003ee), new HuffmanEntry(10,  2, 0x11, 0x000003ef), new HuffmanEntry(10,  1, 0x13, 0x000003f0),
    new HuffmanEntry(11,  9, 0x12, 0x000007e2), new HuffmanEntry(11,  8, 0x14, 0x000007e3), new HuffmanEntry(11,  7, 0x15, 0x000007e4), new HuffmanEntry(11,  6, 0x16, 0x000007e5),
    new HuffmanEntry(11,  5, 0x1e, 0x000007e6), new HuffmanEntry(11,  4, 0x1f, 0x000007e7), new HuffmanEntry(11,  3, 0x2f, 0x000007e8), new HuffmanEntry(11,  2, 0x3f, 0x000007e9),
    new HuffmanEntry(11,  1, 0x47, 0x000007ea), new HuffmanEntry(12, 11, 0x17, 0x00000fd6), new HuffmanEntry(12, 10, 0x18, 0x00000fd7), new HuffmanEntry(12,  9, 0x19, 0x00000fd8),
    new HuffmanEntry(12,  8, 0x1b, 0x00000fd9), new HuffmanEntry(12,  7, 0x20, 0x00000fda), new HuffmanEntry(12,  6, 0x21, 0x00000fdb), new HuffmanEntry(12,  5, 0x23, 0x00000fdc),
    new HuffmanEntry(12,  4, 0x26, 0x00000fdd), new HuffmanEntry(12,  3, 0x27, 0x00000fde), new HuffmanEntry(12,  2, 0x33, 0x00000fdf), new HuffmanEntry(12,  1, 0x3e, 0x00000fe0),
    new HuffmanEntry(13, 24, 0x1a, 0x00001fc2), new HuffmanEntry(13, 23, 0x1c, 0x00001fc3), new HuffmanEntry(13, 22, 0x1d, 0x00001fc4), new HuffmanEntry(13, 21, 0x22, 0x00001fc5),
    new HuffmanEntry(13, 20, 0x24, 0x00001fc6), new HuffmanEntry(13, 19, 0x25, 0x00001fc7), new HuffmanEntry(13, 18, 0x28, 0x00001fc8), new HuffmanEntry(13, 17, 0x29, 0x00001fc9),
    new HuffmanEntry(13, 16, 0x2b, 0x00001fca), new HuffmanEntry(13, 15, 0x2e, 0x00001fcb), new HuffmanEntry(13, 14, 0x30, 0x00001fcc), new HuffmanEntry(13, 13, 0x36, 0x00001fcd),
    new HuffmanEntry(13, 12, 0x37, 0x00001fce), new HuffmanEntry(13, 11, 0x40, 0x00001fcf), new HuffmanEntry(13, 10, 0x41, 0x00001fd0), new HuffmanEntry(13,  9, 0x46, 0x00001fd1),
    new HuffmanEntry(13,  8, 0x48, 0x00001fd2), new HuffmanEntry(13,  7, 0x5e, 0x00001fd3), new HuffmanEntry(13,  6, 0x5f, 0x00001fd4), new HuffmanEntry(13,  5, 0x67, 0x00001fd5),
    new HuffmanEntry(13,  4, 0x7b, 0x00001fd6), new HuffmanEntry(13,  3, 0x7e, 0x00001fd7), new HuffmanEntry(13,  2, 0x7f, 0x00001fd8), new HuffmanEntry(13,  1, 0x83, 0x00001fd9),
    new HuffmanEntry(14, 30, 0x2a, 0x00003fb4), new HuffmanEntry(14, 29, 0x2c, 0x00003fb5), new HuffmanEntry(14, 28, 0x2d, 0x00003fb6), new HuffmanEntry(14, 27, 0x31, 0x00003fb7),
    new HuffmanEntry(14, 26, 0x32, 0x00003fb8), new HuffmanEntry(14, 25, 0x34, 0x00003fb9), new HuffmanEntry(14, 24, 0x35, 0x00003fba), new HuffmanEntry(14, 23, 0x38, 0x00003fbb),
    new HuffmanEntry(14, 22, 0x39, 0x00003fbc), new HuffmanEntry(14, 21, 0x3b, 0x00003fbd), new HuffmanEntry(14, 20, 0x3d, 0x00003fbe), new HuffmanEntry(14, 19, 0x42, 0x00003fbf),
    new HuffmanEntry(14, 18, 0x43, 0x00003fc0), new HuffmanEntry(14, 17, 0x45, 0x00003fc1), new HuffmanEntry(14, 16, 0x49, 0x00003fc2), new HuffmanEntry(14, 15, 0x4a, 0x00003fc3),
    new HuffmanEntry(14, 14, 0x4e, 0x00003fc4), new HuffmanEntry(14, 13, 0x4f, 0x00003fc5), new HuffmanEntry(14, 12, 0x53, 0x00003fc6), new HuffmanEntry(14, 11, 0x56, 0x00003fc7),
    new HuffmanEntry(14, 10, 0x57, 0x00003fc8), new HuffmanEntry(14,  9, 0x60, 0x00003fc9), new HuffmanEntry(14,  8, 0x61, 0x00003fca), new HuffmanEntry(14,  7, 0x66, 0x00003fcb),
    new HuffmanEntry(14,  6, 0x6f, 0x00003fcc), new HuffmanEntry(14,  5, 0x8e, 0x00003fcd), new HuffmanEntry(14,  4, 0x8f, 0x00003fce), new HuffmanEntry(14,  3, 0x9f, 0x00003fcf),
    new HuffmanEntry(14,  2, 0xc7, 0x00003fd0), new HuffmanEntry(14,  1, 0xf7, 0x00003fd1), new HuffmanEntry(15, 46, 0x3a, 0x00007fa4), new HuffmanEntry(15, 45, 0x3c, 0x00007fa5),
    new HuffmanEntry(15, 44, 0x44, 0x00007fa6), new HuffmanEntry(15, 43, 0x4b, 0x00007fa7), new HuffmanEntry(15, 42, 0x4c, 0x00007fa8), new HuffmanEntry(15, 41, 0x4d, 0x00007fa9),
    new HuffmanEntry(15, 40, 0x50, 0x00007faa), new HuffmanEntry(15, 39, 0x51, 0x00007fab), new HuffmanEntry(15, 38, 0x52, 0x00007fac), new HuffmanEntry(15, 37, 0x58, 0x00007fad),
    new HuffmanEntry(15, 36, 0x5b, 0x00007fae), new HuffmanEntry(15, 35, 0x5d, 0x00007faf), new HuffmanEntry(15, 34, 0x62, 0x00007fb0), new HuffmanEntry(15, 33, 0x63, 0x00007fb1),
    new HuffmanEntry(15, 32, 0x68, 0x00007fb2), new HuffmanEntry(15, 31, 0x69, 0x00007fb3), new HuffmanEntry(15, 30, 0x6a, 0x00007fb4), new HuffmanEntry(15, 29, 0x6b, 0x00007fb5),
    new HuffmanEntry(15, 28, 0x6e, 0x00007fb6), new HuffmanEntry(15, 27, 0x76, 0x00007fb7), new HuffmanEntry(15, 26, 0x77, 0x00007fb8), new HuffmanEntry(15, 25, 0x7d, 0x00007fb9),
    new HuffmanEntry(15, 24, 0x80, 0x00007fba), new HuffmanEntry(15, 23, 0x81, 0x00007fbb), new HuffmanEntry(15, 22, 0x82, 0x00007fbc), new HuffmanEntry(15, 21, 0x86, 0x00007fbd),
    new HuffmanEntry(15, 20, 0x87, 0x00007fbe), new HuffmanEntry(15, 19, 0x90, 0x00007fbf), new HuffmanEntry(15, 18, 0x93, 0x00007fc0), new HuffmanEntry(15, 17, 0xa3, 0x00007fc1),
    new HuffmanEntry(15, 16, 0xa5, 0x00007fc2), new HuffmanEntry(15, 15, 0xaf, 0x00007fc3), new HuffmanEntry(15, 14, 0xb0, 0x00007fc4), new HuffmanEntry(15, 13, 0xb3, 0x00007fc5),
    new HuffmanEntry(15, 12, 0xb7, 0x00007fc6), new HuffmanEntry(15, 11, 0xb9, 0x00007fc7), new HuffmanEntry(15, 10, 0xbd, 0x00007fc8), new HuffmanEntry(15,  9, 0xbe, 0x00007fc9),
    new HuffmanEntry(15,  8, 0xbf, 0x00007fca), new HuffmanEntry(15,  7, 0xef, 0x00007fcb), new HuffmanEntry(15,  6, 0xf3, 0x00007fcc), new HuffmanEntry(15,  5, 0xf6, 0x00007fcd),
    new HuffmanEntry(15,  4, 0xf9, 0x00007fce), new HuffmanEntry(15,  3, 0xfb, 0x00007fcf), new HuffmanEntry(15,  2, 0xfc, 0x00007fd0), new HuffmanEntry(15,  1, 0xfd, 0x00007fd1),
    new HuffmanEntry(16, 70, 0x54, 0x0000ffa4), new HuffmanEntry(16, 69, 0x55, 0x0000ffa5), new HuffmanEntry(16, 68, 0x59, 0x0000ffa6), new HuffmanEntry(16, 67, 0x5a, 0x0000ffa7),
    new HuffmanEntry(16, 66, 0x5c, 0x0000ffa8), new HuffmanEntry(16, 65, 0x64, 0x0000ffa9), new HuffmanEntry(16, 64, 0x65, 0x0000ffaa), new HuffmanEntry(16, 63, 0x6c, 0x0000ffab),
    new HuffmanEntry(16, 62, 0x6d, 0x0000ffac), new HuffmanEntry(16, 61, 0x70, 0x0000ffad), new HuffmanEntry(16, 60, 0x71, 0x0000ffae), new HuffmanEntry(16, 59, 0x73, 0x0000ffaf),
    new HuffmanEntry(16, 58, 0x75, 0x0000ffb0), new HuffmanEntry(16, 57, 0x78, 0x0000ffb1), new HuffmanEntry(16, 56, 0x79, 0x0000ffb2), new HuffmanEntry(16, 55, 0x7a, 0x0000ffb3),
    new HuffmanEntry(16, 54, 0x7c, 0x0000ffb4), new HuffmanEntry(16, 53, 0x84, 0x0000ffb5), new HuffmanEntry(16, 52, 0x85, 0x0000ffb6), new HuffmanEntry(16, 51, 0x88, 0x0000ffb7),
    new HuffmanEntry(16, 50, 0x89, 0x0000ffb8), new HuffmanEntry(16, 49, 0x8b, 0x0000ffb9), new HuffmanEntry(16, 48, 0x8d, 0x0000ffba), new HuffmanEntry(16, 47, 0x91, 0x0000ffbb),
    new HuffmanEntry(16, 46, 0x92, 0x0000ffbc), new HuffmanEntry(16, 45, 0x94, 0x0000ffbd), new HuffmanEntry(16, 44, 0x95, 0x0000ffbe), new HuffmanEntry(16, 43, 0x96, 0x0000ffbf),
    new HuffmanEntry(16, 42, 0x97, 0x0000ffc0), new HuffmanEntry(16, 41, 0x9b, 0x0000ffc1), new HuffmanEntry(16, 40, 0x9e, 0x0000ffc2), new HuffmanEntry(16, 39, 0xa0, 0x0000ffc3),
    new HuffmanEntry(16, 38, 0xa4, 0x0000ffc4), new HuffmanEntry(16, 37, 0xa6, 0x0000ffc5), new HuffmanEntry(16, 36, 0xa7, 0x0000ffc6), new HuffmanEntry(16, 35, 0xab, 0x0000ffc7),
    new HuffmanEntry(16, 34, 0xac, 0x0000ffc8), new HuffmanEntry(16, 33, 0xad, 0x0000ffc9), new HuffmanEntry(16, 32, 0xae, 0x0000ffca), new HuffmanEntry(16, 31, 0xb1, 0x0000ffcb),
    new HuffmanEntry(16, 30, 0xb4, 0x0000ffcc), new HuffmanEntry(16, 29, 0xb5, 0x0000ffcd), new HuffmanEntry(16, 28, 0xb6, 0x0000ffce), new HuffmanEntry(16, 27, 0xb8, 0x0000ffcf),
    new HuffmanEntry(16, 26, 0xba, 0x0000ffd0), new HuffmanEntry(16, 25, 0xbb, 0x0000ffd1), new HuffmanEntry(16, 24, 0xc3, 0x0000ffd2), new HuffmanEntry(16, 23, 0xc6, 0x0000ffd3),
    new HuffmanEntry(16, 22, 0xc9, 0x0000ffd4), new HuffmanEntry(16, 21, 0xcb, 0x0000ffd5), new HuffmanEntry(16, 20, 0xce, 0x0000ffd6), new HuffmanEntry(16, 19, 0xcf, 0x0000ffd7),
    new HuffmanEntry(16, 18, 0xd3, 0x0000ffd8), new HuffmanEntry(16, 17, 0xd7, 0x0000ffd9), new HuffmanEntry(16, 16, 0xdb, 0x0000ffda), new HuffmanEntry(16, 15, 0xde, 0x0000ffdb),
    new HuffmanEntry(16, 14, 0xdf, 0x0000ffdc), new HuffmanEntry(16, 13, 0xe3, 0x0000ffdd), new HuffmanEntry(16, 12, 0xe5, 0x0000ffde), new HuffmanEntry(16, 11, 0xe6, 0x0000ffdf),
    new HuffmanEntry(16, 10, 0xe7, 0x0000ffe0), new HuffmanEntry(16,  9, 0xeb, 0x0000ffe1), new HuffmanEntry(16,  8, 0xee, 0x0000ffe2), new HuffmanEntry(16,  7, 0xf0, 0x0000ffe3),
    new HuffmanEntry(16,  6, 0xf1, 0x0000ffe4), new HuffmanEntry(16,  5, 0xf4, 0x0000ffe5), new HuffmanEntry(16,  4, 0xf5, 0x0000ffe6), new HuffmanEntry(16,  3, 0xf8, 0x0000ffe7),
    new HuffmanEntry(16,  2, 0xfa, 0x0000ffe8), new HuffmanEntry(16,  1, 0xfe, 0x0000ffe9), new HuffmanEntry(17, 42, 0x72, 0x0001ffd4), new HuffmanEntry(17, 41, 0x74, 0x0001ffd5),
    new HuffmanEntry(17, 40, 0x8a, 0x0001ffd6), new HuffmanEntry(17, 39, 0x8c, 0x0001ffd7), new HuffmanEntry(17, 38, 0x98, 0x0001ffd8), new HuffmanEntry(17, 37, 0x99, 0x0001ffd9),
    new HuffmanEntry(17, 36, 0x9a, 0x0001ffda), new HuffmanEntry(17, 35, 0x9c, 0x0001ffdb), new HuffmanEntry(17, 34, 0x9d, 0x0001ffdc), new HuffmanEntry(17, 33, 0xa1, 0x0001ffdd),
    new HuffmanEntry(17, 32, 0xa2, 0x0001ffde), new HuffmanEntry(17, 31, 0xa8, 0x0001ffdf), new HuffmanEntry(17, 30, 0xa9, 0x0001ffe0), new HuffmanEntry(17, 29, 0xaa, 0x0001ffe1),
    new HuffmanEntry(17, 28, 0xb2, 0x0001ffe2), new HuffmanEntry(17, 27, 0xbc, 0x0001ffe3), new HuffmanEntry(17, 26, 0xc0, 0x0001ffe4), new HuffmanEntry(17, 25, 0xc1, 0x0001ffe5),
    new HuffmanEntry(17, 24, 0xc2, 0x0001ffe6), new HuffmanEntry(17, 23, 0xc4, 0x0001ffe7), new HuffmanEntry(17, 22, 0xc5, 0x0001ffe8), new HuffmanEntry(17, 21, 0xc8, 0x0001ffe9),
    new HuffmanEntry(17, 20, 0xca, 0x0001ffea), new HuffmanEntry(17, 19, 0xcc, 0x0001ffeb), new HuffmanEntry(17, 18, 0xcd, 0x0001ffec), new HuffmanEntry(17, 17, 0xd0, 0x0001ffed),
    new HuffmanEntry(17, 16, 0xd1, 0x0001ffee), new HuffmanEntry(17, 15, 0xd2, 0x0001ffef), new HuffmanEntry(17, 14, 0xd4, 0x0001fff0), new HuffmanEntry(17, 13, 0xd5, 0x0001fff1),
    new HuffmanEntry(17, 12, 0xd6, 0x0001fff2), new HuffmanEntry(17, 11, 0xd8, 0x0001fff3), new HuffmanEntry(17, 10, 0xdc, 0x0001fff4), new HuffmanEntry(17,  9, 0xdd, 0x0001fff5),
    new HuffmanEntry(17,  8, 0xe0, 0x0001fff6), new HuffmanEntry(17,  7, 0xe1, 0x0001fff7), new HuffmanEntry(17,  6, 0xe4, 0x0001fff8), new HuffmanEntry(17,  5, 0xe8, 0x0001fff9),
    new HuffmanEntry(17,  4, 0xe9, 0x0001fffa), new HuffmanEntry(17,  3, 0xec, 0x0001fffb), new HuffmanEntry(17,  2, 0xed, 0x0001fffc), new HuffmanEntry(17,  1, 0xf2, 0x0001fffd),
    new HuffmanEntry(18,  4, 0xd9, 0x0003fffc), new HuffmanEntry(18,  3, 0xda, 0x0003fffd), new HuffmanEntry(18,  2, 0xe2, 0x0003fffe), new HuffmanEntry(18,  1, 0xea, 0x0003ffff),
            };

            public static readonly HuffmanEntry[] HUFFMAN_OFFSET_LO = new HuffmanEntry[256]
            {
                new HuffmanEntry( 5,  5, 0x00, 0x00000000), new HuffmanEntry( 5,  4, 0x08, 0x00000001), new HuffmanEntry( 5,  3, 0x10, 0x00000002), new HuffmanEntry( 5,  2, 0x18, 0x00000003),
                new HuffmanEntry( 5,  1, 0x20, 0x00000004), new HuffmanEntry( 6, 31, 0x04, 0x0000000a), new HuffmanEntry( 6, 30, 0x0c, 0x0000000b), new HuffmanEntry( 6, 29, 0x12, 0x0000000c),
                new HuffmanEntry( 6, 28, 0x24, 0x0000000d), new HuffmanEntry( 6, 27, 0x28, 0x0000000e), new HuffmanEntry( 6, 26, 0x30, 0x0000000f), new HuffmanEntry( 6, 25, 0x38, 0x00000010),
                new HuffmanEntry( 6, 24, 0x40, 0x00000011), new HuffmanEntry( 6, 23, 0x48, 0x00000012), new HuffmanEntry( 6, 22, 0x50, 0x00000013), new HuffmanEntry( 6, 21, 0x58, 0x00000014),
                new HuffmanEntry( 6, 20, 0x60, 0x00000015), new HuffmanEntry( 6, 19, 0x68, 0x00000016), new HuffmanEntry( 6, 18, 0x70, 0x00000017), new HuffmanEntry( 6, 17, 0x78, 0x00000018),
                new HuffmanEntry( 6, 16, 0x80, 0x00000019), new HuffmanEntry( 6, 15, 0x88, 0x0000001a), new HuffmanEntry( 6, 14, 0x90, 0x0000001b), new HuffmanEntry( 6, 13, 0x98, 0x0000001c),
                new HuffmanEntry( 6, 12, 0xa0, 0x0000001d), new HuffmanEntry( 6, 11, 0xa8, 0x0000001e), new HuffmanEntry( 6, 10, 0xb0, 0x0000001f), new HuffmanEntry( 6,  9, 0xb8, 0x00000020),
                new HuffmanEntry( 6,  8, 0xc0, 0x00000021), new HuffmanEntry( 6,  7, 0xc8, 0x00000022), new HuffmanEntry( 6,  6, 0xd0, 0x00000023), new HuffmanEntry( 6,  5, 0xd8, 0x00000024),
                new HuffmanEntry( 6,  4, 0xe0, 0x00000025), new HuffmanEntry( 6,  3, 0xe8, 0x00000026), new HuffmanEntry( 6,  2, 0xf0, 0x00000027), new HuffmanEntry( 6,  1, 0xf8, 0x00000028),
                new HuffmanEntry( 7, 27, 0x01, 0x00000052), new HuffmanEntry( 7, 26, 0x14, 0x00000053), new HuffmanEntry( 7, 25, 0x1c, 0x00000054), new HuffmanEntry( 7, 24, 0x2c, 0x00000055),
                new HuffmanEntry( 7, 23, 0x34, 0x00000056), new HuffmanEntry( 7, 22, 0x3c, 0x00000057), new HuffmanEntry( 7, 21, 0x44, 0x00000058), new HuffmanEntry( 7, 20, 0x4c, 0x00000059),
                new HuffmanEntry( 7, 19, 0x54, 0x0000005a), new HuffmanEntry( 7, 18, 0x5c, 0x0000005b), new HuffmanEntry( 7, 17, 0x64, 0x0000005c), new HuffmanEntry( 7, 16, 0x6c, 0x0000005d),
                new HuffmanEntry( 7, 15, 0x74, 0x0000005e), new HuffmanEntry( 7, 14, 0x7c, 0x0000005f), new HuffmanEntry( 7, 13, 0x84, 0x00000060), new HuffmanEntry( 7, 12, 0x8c, 0x00000061),
                new HuffmanEntry( 7, 11, 0x9c, 0x00000062), new HuffmanEntry( 7, 10, 0xa4, 0x00000063), new HuffmanEntry( 7,  9, 0xac, 0x00000064), new HuffmanEntry( 7,  8, 0xb4, 0x00000065),
                new HuffmanEntry( 7,  7, 0xbc, 0x00000066), new HuffmanEntry( 7,  6, 0xc4, 0x00000067), new HuffmanEntry( 7,  5, 0xcc, 0x00000068), new HuffmanEntry( 7,  4, 0xd4, 0x00000069),
                new HuffmanEntry( 7,  3, 0xe4, 0x0000006a), new HuffmanEntry( 7,  2, 0xf4, 0x0000006b), new HuffmanEntry( 7,  1, 0xfc, 0x0000006c), new HuffmanEntry( 8,  7, 0x02, 0x000000da),
                new HuffmanEntry( 8,  6, 0x06, 0x000000db), new HuffmanEntry( 8,  5, 0x0e, 0x000000dc), new HuffmanEntry( 8,  4, 0x36, 0x000000dd), new HuffmanEntry( 8,  3, 0x94, 0x000000de),
                new HuffmanEntry( 8,  2, 0xdc, 0x000000df), new HuffmanEntry( 8,  1, 0xec, 0x000000e0), new HuffmanEntry( 9, 11, 0x03, 0x000001c2), new HuffmanEntry( 9, 10, 0x09, 0x000001c3),
                new HuffmanEntry( 9,  9, 0x0a, 0x000001c4), new HuffmanEntry( 9,  8, 0x16, 0x000001c5), new HuffmanEntry( 9,  7, 0x1e, 0x000001c6), new HuffmanEntry( 9,  6, 0x2a, 0x000001c7),
                new HuffmanEntry( 9,  5, 0x42, 0x000001c8), new HuffmanEntry( 9,  4, 0x5a, 0x000001c9), new HuffmanEntry( 9,  3, 0x7e, 0x000001ca), new HuffmanEntry( 9,  2, 0xa2, 0x000001cb),
                new HuffmanEntry( 9,  1, 0xc6, 0x000001cc), new HuffmanEntry(10, 56, 0x05, 0x0000039a), new HuffmanEntry(10, 55, 0x07, 0x0000039b), new HuffmanEntry(10, 54, 0x0b, 0x0000039c),
                new HuffmanEntry(10, 53, 0x0f, 0x0000039d), new HuffmanEntry(10, 52, 0x17, 0x0000039e), new HuffmanEntry(10, 51, 0x1a, 0x0000039f), new HuffmanEntry(10, 50, 0x22, 0x000003a0),
                new HuffmanEntry(10, 49, 0x26, 0x000003a1), new HuffmanEntry(10, 48, 0x2e, 0x000003a2), new HuffmanEntry(10, 47, 0x32, 0x000003a3), new HuffmanEntry(10, 46, 0x3a, 0x000003a4),
                new HuffmanEntry(10, 45, 0x3e, 0x000003a5), new HuffmanEntry(10, 44, 0x46, 0x000003a6), new HuffmanEntry(10, 43, 0x4a, 0x000003a7), new HuffmanEntry(10, 42, 0x4e, 0x000003a8),
                new HuffmanEntry(10, 41, 0x52, 0x000003a9), new HuffmanEntry(10, 40, 0x56, 0x000003aa), new HuffmanEntry(10, 39, 0x5e, 0x000003ab), new HuffmanEntry(10, 38, 0x62, 0x000003ac),
                new HuffmanEntry(10, 37, 0x66, 0x000003ad), new HuffmanEntry(10, 36, 0x6a, 0x000003ae), new HuffmanEntry(10, 35, 0x6e, 0x000003af), new HuffmanEntry(10, 34, 0x72, 0x000003b0),
                new HuffmanEntry(10, 33, 0x76, 0x000003b1), new HuffmanEntry(10, 32, 0x7a, 0x000003b2), new HuffmanEntry(10, 31, 0x82, 0x000003b3), new HuffmanEntry(10, 30, 0x86, 0x000003b4),
                new HuffmanEntry(10, 29, 0x8a, 0x000003b5), new HuffmanEntry(10, 28, 0x8e, 0x000003b6), new HuffmanEntry(10, 27, 0x92, 0x000003b7), new HuffmanEntry(10, 26, 0x96, 0x000003b8),
                new HuffmanEntry(10, 25, 0x9a, 0x000003b9), new HuffmanEntry(10, 24, 0x9e, 0x000003ba), new HuffmanEntry(10, 23, 0xa6, 0x000003bb), new HuffmanEntry(10, 22, 0xaa, 0x000003bc),
                new HuffmanEntry(10, 21, 0xae, 0x000003bd), new HuffmanEntry(10, 20, 0xb2, 0x000003be), new HuffmanEntry(10, 19, 0xb6, 0x000003bf), new HuffmanEntry(10, 18, 0xba, 0x000003c0),
                new HuffmanEntry(10, 17, 0xbe, 0x000003c1), new HuffmanEntry(10, 16, 0xc2, 0x000003c2), new HuffmanEntry(10, 15, 0xca, 0x000003c3), new HuffmanEntry(10, 14, 0xce, 0x000003c4),
                new HuffmanEntry(10, 13, 0xd2, 0x000003c5), new HuffmanEntry(10, 12, 0xd6, 0x000003c6), new HuffmanEntry(10, 11, 0xda, 0x000003c7), new HuffmanEntry(10, 10, 0xde, 0x000003c8),
                new HuffmanEntry(10,  9, 0xe2, 0x000003c9), new HuffmanEntry(10,  8, 0xe6, 0x000003ca), new HuffmanEntry(10,  7, 0xea, 0x000003cb), new HuffmanEntry(10,  6, 0xee, 0x000003cc),
                new HuffmanEntry(10,  5, 0xf2, 0x000003cd), new HuffmanEntry(10,  4, 0xf6, 0x000003ce), new HuffmanEntry(10,  3, 0xfa, 0x000003cf), new HuffmanEntry(10,  2, 0xfe, 0x000003d0),
                new HuffmanEntry(10,  1, 0xff, 0x000003d1), new HuffmanEntry(11, 65, 0x0d, 0x000007a4), new HuffmanEntry(11, 64, 0x11, 0x000007a5), new HuffmanEntry(11, 63, 0x15, 0x000007a6),
                new HuffmanEntry(11, 62, 0x19, 0x000007a7), new HuffmanEntry(11, 61, 0x1b, 0x000007a8), new HuffmanEntry(11, 60, 0x1f, 0x000007a9), new HuffmanEntry(11, 59, 0x21, 0x000007aa),
                new HuffmanEntry(11, 58, 0x27, 0x000007ab), new HuffmanEntry(11, 57, 0x29, 0x000007ac), new HuffmanEntry(11, 56, 0x2d, 0x000007ad), new HuffmanEntry(11, 55, 0x2f, 0x000007ae),
                new HuffmanEntry(11, 54, 0x31, 0x000007af), new HuffmanEntry(11, 53, 0x37, 0x000007b0), new HuffmanEntry(11, 52, 0x39, 0x000007b1), new HuffmanEntry(11, 51, 0x3f, 0x000007b2),
                new HuffmanEntry(11, 50, 0x41, 0x000007b3), new HuffmanEntry(11, 49, 0x45, 0x000007b4), new HuffmanEntry(11, 48, 0x47, 0x000007b5), new HuffmanEntry(11, 47, 0x49, 0x000007b6),
                new HuffmanEntry(11, 46, 0x4f, 0x000007b7), new HuffmanEntry(11, 45, 0x51, 0x000007b8), new HuffmanEntry(11, 44, 0x57, 0x000007b9), new HuffmanEntry(11, 43, 0x59, 0x000007ba),
                new HuffmanEntry(11, 42, 0x5f, 0x000007bb), new HuffmanEntry(11, 41, 0x61, 0x000007bc), new HuffmanEntry(11, 40, 0x67, 0x000007bd), new HuffmanEntry(11, 39, 0x69, 0x000007be),
                new HuffmanEntry(11, 38, 0x6b, 0x000007bf), new HuffmanEntry(11, 37, 0x6d, 0x000007c0), new HuffmanEntry(11, 36, 0x6f, 0x000007c1), new HuffmanEntry(11, 35, 0x71, 0x000007c2),
                new HuffmanEntry(11, 34, 0x77, 0x000007c3), new HuffmanEntry(11, 33, 0x79, 0x000007c4), new HuffmanEntry(11, 32, 0x7f, 0x000007c5), new HuffmanEntry(11, 31, 0x81, 0x000007c6),
                new HuffmanEntry(11, 30, 0x87, 0x000007c7), new HuffmanEntry(11, 29, 0x89, 0x000007c8), new HuffmanEntry(11, 28, 0x8f, 0x000007c9), new HuffmanEntry(11, 27, 0x91, 0x000007ca),
                new HuffmanEntry(11, 26, 0x97, 0x000007cb), new HuffmanEntry(11, 25, 0x99, 0x000007cc), new HuffmanEntry(11, 24, 0x9f, 0x000007cd), new HuffmanEntry(11, 23, 0xa1, 0x000007ce),
                new HuffmanEntry(11, 22, 0xa7, 0x000007cf), new HuffmanEntry(11, 21, 0xa9, 0x000007d0), new HuffmanEntry(11, 20, 0xaf, 0x000007d1), new HuffmanEntry(11, 19, 0xb1, 0x000007d2),
                new HuffmanEntry(11, 18, 0xb7, 0x000007d3), new HuffmanEntry(11, 17, 0xb9, 0x000007d4), new HuffmanEntry(11, 16, 0xbf, 0x000007d5), new HuffmanEntry(11, 15, 0xc1, 0x000007d6),
                new HuffmanEntry(11, 14, 0xc7, 0x000007d7), new HuffmanEntry(11, 13, 0xc9, 0x000007d8), new HuffmanEntry(11, 12, 0xcf, 0x000007d9), new HuffmanEntry(11, 11, 0xd1, 0x000007da),
                new HuffmanEntry(11, 10, 0xd7, 0x000007db), new HuffmanEntry(11,  9, 0xd9, 0x000007dc), new HuffmanEntry(11,  8, 0xdf, 0x000007dd), new HuffmanEntry(11,  7, 0xe1, 0x000007de),
                new HuffmanEntry(11,  6, 0xe7, 0x000007df), new HuffmanEntry(11,  5, 0xe9, 0x000007e0), new HuffmanEntry(11,  4, 0xef, 0x000007e1), new HuffmanEntry(11,  3, 0xf1, 0x000007e2),
                new HuffmanEntry(11,  2, 0xf7, 0x000007e3), new HuffmanEntry(11,  1, 0xf9, 0x000007e4), new HuffmanEntry(12, 54, 0x13, 0x00000fca), new HuffmanEntry(12, 53, 0x1d, 0x00000fcb),
                new HuffmanEntry(12, 52, 0x23, 0x00000fcc), new HuffmanEntry(12, 51, 0x25, 0x00000fcd), new HuffmanEntry(12, 50, 0x2b, 0x00000fce), new HuffmanEntry(12, 49, 0x33, 0x00000fcf),
                new HuffmanEntry(12, 48, 0x35, 0x00000fd0), new HuffmanEntry(12, 47, 0x3b, 0x00000fd1), new HuffmanEntry(12, 46, 0x3d, 0x00000fd2), new HuffmanEntry(12, 45, 0x43, 0x00000fd3),
                new HuffmanEntry(12, 44, 0x4b, 0x00000fd4), new HuffmanEntry(12, 43, 0x4d, 0x00000fd5), new HuffmanEntry(12, 42, 0x53, 0x00000fd6), new HuffmanEntry(12, 41, 0x55, 0x00000fd7),
                new HuffmanEntry(12, 40, 0x5b, 0x00000fd8), new HuffmanEntry(12, 39, 0x5d, 0x00000fd9), new HuffmanEntry(12, 38, 0x63, 0x00000fda), new HuffmanEntry(12, 37, 0x65, 0x00000fdb),
                new HuffmanEntry(12, 36, 0x73, 0x00000fdc), new HuffmanEntry(12, 35, 0x75, 0x00000fdd), new HuffmanEntry(12, 34, 0x7b, 0x00000fde), new HuffmanEntry(12, 33, 0x7d, 0x00000fdf),
                new HuffmanEntry(12, 32, 0x83, 0x00000fe0), new HuffmanEntry(12, 31, 0x85, 0x00000fe1), new HuffmanEntry(12, 30, 0x8b, 0x00000fe2), new HuffmanEntry(12, 29, 0x8d, 0x00000fe3),
                new HuffmanEntry(12, 28, 0x93, 0x00000fe4), new HuffmanEntry(12, 27, 0x95, 0x00000fe5), new HuffmanEntry(12, 26, 0x9b, 0x00000fe6), new HuffmanEntry(12, 25, 0x9d, 0x00000fe7),
                new HuffmanEntry(12, 24, 0xa3, 0x00000fe8), new HuffmanEntry(12, 23, 0xa5, 0x00000fe9), new HuffmanEntry(12, 22, 0xab, 0x00000fea), new HuffmanEntry(12, 21, 0xad, 0x00000feb),
                new HuffmanEntry(12, 20, 0xb3, 0x00000fec), new HuffmanEntry(12, 19, 0xb5, 0x00000fed), new HuffmanEntry(12, 18, 0xbb, 0x00000fee), new HuffmanEntry(12, 17, 0xbd, 0x00000fef),
                new HuffmanEntry(12, 16, 0xc3, 0x00000ff0), new HuffmanEntry(12, 15, 0xc5, 0x00000ff1), new HuffmanEntry(12, 14, 0xcb, 0x00000ff2), new HuffmanEntry(12, 13, 0xcd, 0x00000ff3),
                new HuffmanEntry(12, 12, 0xd3, 0x00000ff4), new HuffmanEntry(12, 11, 0xd5, 0x00000ff5), new HuffmanEntry(12, 10, 0xdb, 0x00000ff6), new HuffmanEntry(12,  9, 0xdd, 0x00000ff7),
                new HuffmanEntry(12,  8, 0xe3, 0x00000ff8), new HuffmanEntry(12,  7, 0xe5, 0x00000ff9), new HuffmanEntry(12,  6, 0xeb, 0x00000ffa), new HuffmanEntry(12,  5, 0xed, 0x00000ffb),
                new HuffmanEntry(12,  4, 0xf3, 0x00000ffc), new HuffmanEntry(12,  3, 0xf5, 0x00000ffd), new HuffmanEntry(12,  2, 0xfb, 0x00000ffe), new HuffmanEntry(12,  1, 0xfd, 0x00000fff),
            };

            public static readonly HuffmanEntry[] HUFFMAN_OFFSET_HI = new HuffmanEntry[256]
            {
                new HuffmanEntry( 2,  1, 0x00, 0x00000000), new HuffmanEntry( 5,  3, 0x01, 0x00000008), new HuffmanEntry( 5,  2, 0x02, 0x00000009), new HuffmanEntry( 5,  1, 0x07, 0x0000000a),
        new HuffmanEntry( 6,  8, 0x03, 0x00000016), new HuffmanEntry( 6,  7, 0x04, 0x00000017), new HuffmanEntry( 6,  6, 0x05, 0x00000018), new HuffmanEntry( 6,  5, 0x06, 0x00000019),
        new HuffmanEntry( 6,  4, 0x08, 0x0000001a), new HuffmanEntry( 6,  3, 0x0e, 0x0000001b), new HuffmanEntry( 6,  2, 0x0f, 0x0000001c), new HuffmanEntry( 6,  1, 0x10, 0x0000001d),
        new HuffmanEntry( 7, 16, 0x09, 0x0000003c), new HuffmanEntry( 7, 15, 0x0a, 0x0000003d), new HuffmanEntry( 7, 14, 0x0b, 0x0000003e), new HuffmanEntry( 7, 13, 0x0c, 0x0000003f),
        new HuffmanEntry( 7, 12, 0x0d, 0x00000040), new HuffmanEntry( 7, 11, 0x11, 0x00000041), new HuffmanEntry( 7, 10, 0x12, 0x00000042), new HuffmanEntry( 7,  9, 0x13, 0x00000043),
        new HuffmanEntry( 7,  8, 0x14, 0x00000044), new HuffmanEntry( 7,  7, 0x17, 0x00000045), new HuffmanEntry( 7,  6, 0x18, 0x00000046), new HuffmanEntry( 7,  5, 0x1c, 0x00000047),
        new HuffmanEntry( 7,  4, 0x1e, 0x00000048), new HuffmanEntry( 7,  3, 0x1f, 0x00000049), new HuffmanEntry( 7,  2, 0x20, 0x0000004a), new HuffmanEntry( 7,  1, 0x30, 0x0000004b),
        new HuffmanEntry( 8, 38, 0x15, 0x00000098), new HuffmanEntry( 8, 37, 0x16, 0x00000099), new HuffmanEntry( 8, 36, 0x19, 0x0000009a), new HuffmanEntry( 8, 35, 0x1a, 0x0000009b),
        new HuffmanEntry( 8, 34, 0x1b, 0x0000009c), new HuffmanEntry( 8, 33, 0x1d, 0x0000009d), new HuffmanEntry( 8, 32, 0x21, 0x0000009e), new HuffmanEntry( 8, 31, 0x22, 0x0000009f),
        new HuffmanEntry( 8, 30, 0x23, 0x000000a0), new HuffmanEntry( 8, 29, 0x24, 0x000000a1), new HuffmanEntry( 8, 28, 0x25, 0x000000a2), new HuffmanEntry( 8, 27, 0x26, 0x000000a3),
        new HuffmanEntry( 8, 26, 0x27, 0x000000a4), new HuffmanEntry( 8, 25, 0x28, 0x000000a5), new HuffmanEntry( 8, 24, 0x29, 0x000000a6), new HuffmanEntry( 8, 23, 0x2a, 0x000000a7),
        new HuffmanEntry( 8, 22, 0x2b, 0x000000a8), new HuffmanEntry( 8, 21, 0x2c, 0x000000a9), new HuffmanEntry( 8, 20, 0x2d, 0x000000aa), new HuffmanEntry( 8, 19, 0x2e, 0x000000ab),
        new HuffmanEntry( 8, 18, 0x2f, 0x000000ac), new HuffmanEntry( 8, 17, 0x31, 0x000000ad), new HuffmanEntry( 8, 16, 0x32, 0x000000ae), new HuffmanEntry( 8, 15, 0x33, 0x000000af),
        new HuffmanEntry( 8, 14, 0x35, 0x000000b0), new HuffmanEntry( 8, 13, 0x36, 0x000000b1), new HuffmanEntry( 8, 12, 0x37, 0x000000b2), new HuffmanEntry( 8, 11, 0x38, 0x000000b3),
        new HuffmanEntry( 8, 10, 0x3e, 0x000000b4), new HuffmanEntry( 8,  9, 0x3f, 0x000000b5), new HuffmanEntry( 8,  8, 0x40, 0x000000b6), new HuffmanEntry( 8,  7, 0x41, 0x000000b7),
        new HuffmanEntry( 8,  6, 0x4f, 0x000000b8), new HuffmanEntry( 8,  5, 0x50, 0x000000b9), new HuffmanEntry( 8,  4, 0x58, 0x000000ba), new HuffmanEntry( 8,  3, 0x5f, 0x000000bb),
        new HuffmanEntry( 8,  2, 0x60, 0x000000bc), new HuffmanEntry( 8,  1, 0xb0, 0x000000bd), new HuffmanEntry( 9, 74, 0x34, 0x0000017c), new HuffmanEntry( 9, 73, 0x39, 0x0000017d),
        new HuffmanEntry( 9, 72, 0x3a, 0x0000017e), new HuffmanEntry( 9, 71, 0x3b, 0x0000017f), new HuffmanEntry( 9, 70, 0x3c, 0x00000180), new HuffmanEntry( 9, 69, 0x3d, 0x00000181),
        new HuffmanEntry( 9, 68, 0x42, 0x00000182), new HuffmanEntry( 9, 67, 0x43, 0x00000183), new HuffmanEntry( 9, 66, 0x44, 0x00000184), new HuffmanEntry( 9, 65, 0x45, 0x00000185),
        new HuffmanEntry( 9, 64, 0x46, 0x00000186), new HuffmanEntry( 9, 63, 0x47, 0x00000187), new HuffmanEntry( 9, 62, 0x48, 0x00000188), new HuffmanEntry( 9, 61, 0x49, 0x00000189),
        new HuffmanEntry( 9, 60, 0x4a, 0x0000018a), new HuffmanEntry( 9, 59, 0x4b, 0x0000018b), new HuffmanEntry( 9, 58, 0x4c, 0x0000018c), new HuffmanEntry( 9, 57, 0x4d, 0x0000018d),
        new HuffmanEntry( 9, 56, 0x4e, 0x0000018e), new HuffmanEntry( 9, 55, 0x51, 0x0000018f), new HuffmanEntry( 9, 54, 0x52, 0x00000190), new HuffmanEntry( 9, 53, 0x53, 0x00000191),
        new HuffmanEntry( 9, 52, 0x54, 0x00000192), new HuffmanEntry( 9, 51, 0x55, 0x00000193), new HuffmanEntry( 9, 50, 0x56, 0x00000194), new HuffmanEntry( 9, 49, 0x57, 0x00000195),
        new HuffmanEntry( 9, 48, 0x59, 0x00000196), new HuffmanEntry( 9, 47, 0x5a, 0x00000197), new HuffmanEntry( 9, 46, 0x5b, 0x00000198), new HuffmanEntry( 9, 45, 0x5c, 0x00000199),
        new HuffmanEntry( 9, 44, 0x5d, 0x0000019a), new HuffmanEntry( 9, 43, 0x5e, 0x0000019b), new HuffmanEntry( 9, 42, 0x61, 0x0000019c), new HuffmanEntry( 9, 41, 0x62, 0x0000019d),
        new HuffmanEntry( 9, 40, 0x63, 0x0000019e), new HuffmanEntry( 9, 39, 0x64, 0x0000019f), new HuffmanEntry( 9, 38, 0x65, 0x000001a0), new HuffmanEntry( 9, 37, 0x66, 0x000001a1),
        new HuffmanEntry( 9, 36, 0x67, 0x000001a2), new HuffmanEntry( 9, 35, 0x68, 0x000001a3), new HuffmanEntry( 9, 34, 0x69, 0x000001a4), new HuffmanEntry( 9, 33, 0x6a, 0x000001a5),
        new HuffmanEntry( 9, 32, 0x6b, 0x000001a6), new HuffmanEntry( 9, 31, 0x6c, 0x000001a7), new HuffmanEntry( 9, 30, 0x6d, 0x000001a8), new HuffmanEntry( 9, 29, 0x6e, 0x000001a9),
        new HuffmanEntry( 9, 28, 0x6f, 0x000001aa), new HuffmanEntry( 9, 27, 0x70, 0x000001ab), new HuffmanEntry( 9, 26, 0x71, 0x000001ac), new HuffmanEntry( 9, 25, 0x72, 0x000001ad),
        new HuffmanEntry( 9, 24, 0x73, 0x000001ae), new HuffmanEntry( 9, 23, 0x74, 0x000001af), new HuffmanEntry( 9, 22, 0x76, 0x000001b0), new HuffmanEntry( 9, 21, 0x77, 0x000001b1),
        new HuffmanEntry( 9, 20, 0x78, 0x000001b2), new HuffmanEntry( 9, 19, 0x79, 0x000001b3), new HuffmanEntry( 9, 18, 0x7d, 0x000001b4), new HuffmanEntry( 9, 17, 0x7e, 0x000001b5),
        new HuffmanEntry( 9, 16, 0x7f, 0x000001b6), new HuffmanEntry( 9, 15, 0x80, 0x000001b7), new HuffmanEntry( 9, 14, 0x81, 0x000001b8), new HuffmanEntry( 9, 13, 0x82, 0x000001b9),
        new HuffmanEntry( 9, 12, 0x86, 0x000001ba), new HuffmanEntry( 9, 11, 0x87, 0x000001bb), new HuffmanEntry( 9, 10, 0x88, 0x000001bc), new HuffmanEntry( 9,  9, 0x89, 0x000001bd),
        new HuffmanEntry( 9,  8, 0x8f, 0x000001be), new HuffmanEntry( 9,  7, 0x90, 0x000001bf), new HuffmanEntry( 9,  6, 0x9f, 0x000001c0), new HuffmanEntry( 9,  5, 0xa0, 0x000001c1),
        new HuffmanEntry( 9,  4, 0xa8, 0x000001c2), new HuffmanEntry( 9,  3, 0xaf, 0x000001c3), new HuffmanEntry( 9,  2, 0xb1, 0x000001c4), new HuffmanEntry( 9,  1, 0xc0, 0x000001c5),
        new HuffmanEntry(10,116, 0x75, 0x0000038c), new HuffmanEntry(10,115, 0x7a, 0x0000038d), new HuffmanEntry(10,114, 0x7b, 0x0000038e), new HuffmanEntry(10,113, 0x7c, 0x0000038f),
        new HuffmanEntry(10,112, 0x83, 0x00000390), new HuffmanEntry(10,111, 0x84, 0x00000391), new HuffmanEntry(10,110, 0x85, 0x00000392), new HuffmanEntry(10,109, 0x8a, 0x00000393),
        new HuffmanEntry(10,108, 0x8b, 0x00000394), new HuffmanEntry(10,107, 0x8c, 0x00000395), new HuffmanEntry(10,106, 0x8d, 0x00000396), new HuffmanEntry(10,105, 0x8e, 0x00000397),
        new HuffmanEntry(10,104, 0x91, 0x00000398), new HuffmanEntry(10,103, 0x92, 0x00000399), new HuffmanEntry(10,102, 0x93, 0x0000039a), new HuffmanEntry(10,101, 0x94, 0x0000039b),
        new HuffmanEntry(10,100, 0x95, 0x0000039c), new HuffmanEntry(10, 99, 0x96, 0x0000039d), new HuffmanEntry(10, 98, 0x97, 0x0000039e), new HuffmanEntry(10, 97, 0x98, 0x0000039f),
        new HuffmanEntry(10, 96, 0x99, 0x000003a0), new HuffmanEntry(10, 95, 0x9a, 0x000003a1), new HuffmanEntry(10, 94, 0x9b, 0x000003a2), new HuffmanEntry(10, 93, 0x9c, 0x000003a3),
        new HuffmanEntry(10, 92, 0x9d, 0x000003a4), new HuffmanEntry(10, 91, 0x9e, 0x000003a5), new HuffmanEntry(10, 90, 0xa1, 0x000003a6), new HuffmanEntry(10, 89, 0xa2, 0x000003a7),
        new HuffmanEntry(10, 88, 0xa3, 0x000003a8), new HuffmanEntry(10, 87, 0xa4, 0x000003a9), new HuffmanEntry(10, 86, 0xa5, 0x000003aa), new HuffmanEntry(10, 85, 0xa6, 0x000003ab),
        new HuffmanEntry(10, 84, 0xa7, 0x000003ac), new HuffmanEntry(10, 83, 0xa9, 0x000003ad), new HuffmanEntry(10, 82, 0xaa, 0x000003ae), new HuffmanEntry(10, 81, 0xab, 0x000003af),
        new HuffmanEntry(10, 80, 0xac, 0x000003b0), new HuffmanEntry(10, 79, 0xad, 0x000003b1), new HuffmanEntry(10, 78, 0xae, 0x000003b2), new HuffmanEntry(10, 77, 0xb2, 0x000003b3),
        new HuffmanEntry(10, 76, 0xb3, 0x000003b4), new HuffmanEntry(10, 75, 0xb4, 0x000003b5), new HuffmanEntry(10, 74, 0xb5, 0x000003b6), new HuffmanEntry(10, 73, 0xb6, 0x000003b7),
        new HuffmanEntry(10, 72, 0xb7, 0x000003b8), new HuffmanEntry(10, 71, 0xb8, 0x000003b9), new HuffmanEntry(10, 70, 0xb9, 0x000003ba), new HuffmanEntry(10, 69, 0xba, 0x000003bb),
        new HuffmanEntry(10, 68, 0xbb, 0x000003bc), new HuffmanEntry(10, 67, 0xbc, 0x000003bd), new HuffmanEntry(10, 66, 0xbd, 0x000003be), new HuffmanEntry(10, 65, 0xbe, 0x000003bf),
        new HuffmanEntry(10, 64, 0xbf, 0x000003c0), new HuffmanEntry(10, 63, 0xc1, 0x000003c1), new HuffmanEntry(10, 62, 0xc2, 0x000003c2), new HuffmanEntry(10, 61, 0xc3, 0x000003c3),
        new HuffmanEntry(10, 60, 0xc4, 0x000003c4), new HuffmanEntry(10, 59, 0xc5, 0x000003c5), new HuffmanEntry(10, 58, 0xc6, 0x000003c6), new HuffmanEntry(10, 57, 0xc7, 0x000003c7),
        new HuffmanEntry(10, 56, 0xc8, 0x000003c8), new HuffmanEntry(10, 55, 0xc9, 0x000003c9), new HuffmanEntry(10, 54, 0xca, 0x000003ca), new HuffmanEntry(10, 53, 0xcb, 0x000003cb),
        new HuffmanEntry(10, 52, 0xcc, 0x000003cc), new HuffmanEntry(10, 51, 0xcd, 0x000003cd), new HuffmanEntry(10, 50, 0xce, 0x000003ce), new HuffmanEntry(10, 49, 0xcf, 0x000003cf),
        new HuffmanEntry(10, 48, 0xd0, 0x000003d0), new HuffmanEntry(10, 47, 0xd1, 0x000003d1), new HuffmanEntry(10, 46, 0xd2, 0x000003d2), new HuffmanEntry(10, 45, 0xd3, 0x000003d3),
        new HuffmanEntry(10, 44, 0xd4, 0x000003d4), new HuffmanEntry(10, 43, 0xd5, 0x000003d5), new HuffmanEntry(10, 42, 0xd6, 0x000003d6), new HuffmanEntry(10, 41, 0xd7, 0x000003d7),
        new HuffmanEntry(10, 40, 0xd8, 0x000003d8), new HuffmanEntry(10, 39, 0xd9, 0x000003d9), new HuffmanEntry(10, 38, 0xda, 0x000003da), new HuffmanEntry(10, 37, 0xdb, 0x000003db),
        new HuffmanEntry(10, 36, 0xdc, 0x000003dc), new HuffmanEntry(10, 35, 0xdd, 0x000003dd), new HuffmanEntry(10, 34, 0xde, 0x000003de), new HuffmanEntry(10, 33, 0xdf, 0x000003df),
        new HuffmanEntry(10, 32, 0xe0, 0x000003e0), new HuffmanEntry(10, 31, 0xe1, 0x000003e1), new HuffmanEntry(10, 30, 0xe2, 0x000003e2), new HuffmanEntry(10, 29, 0xe3, 0x000003e3),
        new HuffmanEntry(10, 28, 0xe4, 0x000003e4), new HuffmanEntry(10, 27, 0xe5, 0x000003e5), new HuffmanEntry(10, 26, 0xe6, 0x000003e6), new HuffmanEntry(10, 25, 0xe7, 0x000003e7),
        new HuffmanEntry(10, 24, 0xe8, 0x000003e8), new HuffmanEntry(10, 23, 0xe9, 0x000003e9), new HuffmanEntry(10, 22, 0xea, 0x000003ea), new HuffmanEntry(10, 21, 0xeb, 0x000003eb),
        new HuffmanEntry(10, 20, 0xec, 0x000003ec), new HuffmanEntry(10, 19, 0xed, 0x000003ed), new HuffmanEntry(10, 18, 0xee, 0x000003ee), new HuffmanEntry(10, 17, 0xef, 0x000003ef),
        new HuffmanEntry(10, 16, 0xf0, 0x000003f0), new HuffmanEntry(10, 15, 0xf1, 0x000003f1), new HuffmanEntry(10, 14, 0xf2, 0x000003f2), new HuffmanEntry(10, 13, 0xf3, 0x000003f3),
        new HuffmanEntry(10, 12, 0xf4, 0x000003f4), new HuffmanEntry(10, 11, 0xf5, 0x000003f5), new HuffmanEntry(10, 10, 0xf6, 0x000003f6), new HuffmanEntry(10,  9, 0xf7, 0x000003f7),
        new HuffmanEntry(10,  8, 0xf8, 0x000003f8), new HuffmanEntry(10,  7, 0xf9, 0x000003f9), new HuffmanEntry(10,  6, 0xfa, 0x000003fa), new HuffmanEntry(10,  5, 0xfb, 0x000003fb),
        new HuffmanEntry(10,  4, 0xfc, 0x000003fc), new HuffmanEntry(10,  3, 0xfd, 0x000003fd), new HuffmanEntry(10,  2, 0xfe, 0x000003fe), new HuffmanEntry(10,  1, 0xff, 0x000003ff),
            };
        }

        #endregion
    }
}
