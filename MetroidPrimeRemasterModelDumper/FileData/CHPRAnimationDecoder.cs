using AvaloniaToolbox.Core.IO;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using static DKCTF.CHPR;

#nullable disable

namespace DKCTF
{
    /// <summary>
    /// Contains the CHPR animation payload parser, the parsed top-level data types,
    /// shared packed-bit reading, packed component command parsing, and low-level
    /// value-record operations. These helpers do not yet reconstruct complete
    /// animation tracks or keyframes from serialized CHPR component data.
    /// CHPR.cs remains responsible for locating and bounds-checking each payload.
    /// </summary>
    public static class CHPRAnimationDecoder
    {
        /// <summary>
        /// Result of parsing the known top-level CAnim wrapper from one bounded payload.
        /// Any unparsed suffix is retained instead of being discarded.
        /// </summary>
        public sealed class PayloadParseResult
        {
            public CAnim Animation { get; }
            public int BytesParsed { get; }
            public byte[] TrailingData { get; }

            internal PayloadParseResult(CAnim animation, int bytesParsed, byte[] trailingData)
            {
                Animation = animation;
                BytesParsed = bytesParsed;
                TrailingData = trailingData ?? Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Parse the known top-level animation wrapper from one payload.
        /// The byte order matches the existing CHPR parser (SetByteOrder(false)).
        /// Exceptions are intentionally allowed to propagate so CHPR.cs can log
        /// the failure alongside the animation table entry and its offset.
        /// </summary>
        public static PayloadParseResult ParsePayload(byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            using (FileReader reader = new FileReader(payload))
            {
                reader.SetByteOrder(false);

                CAnim animation = new CAnim(reader);
                int bytesParsed = checked((int)reader.Position);
                int remaining = payload.Length - bytesParsed;

                if (remaining < 0)
                {
                    throw new InvalidDataException("Animation parser consumed beyond its bounded payload.");
                }

                byte[] trailingData = remaining > 0 ? ReadExactBytes(reader, remaining, "trailing animation data") : Array.Empty<byte>();

                return new PayloadParseResult(animation, bytesParsed, trailingData);
            }
        }

        #region Existing top-level CAnim structures

        public class SBaseInfo
        {
            public AnimType Type;
            public byte Unk1;
            public ushort Index;

            public byte Unk3;
            public byte Unk4;
            public byte Unk5;
            public byte Unk6;
            public byte Unk7;
            public byte Unk8;

            public bool HasSharedReference;
            public uint SharedReference;

            public ushort Unk10;
            public ushort Unk12;

            public SBaseInfo(FileReader reader)
            {
                Type = (AnimType)reader.ReadByte();
                Unk1 = reader.ReadByte();
                Index = reader.ReadUInt16();
                Unk3 = reader.ReadByte();
                Unk4 = reader.ReadByte();
                if (Unk4 != 0)
                {
                    Unk5 = reader.ReadByte();
                }
                Unk6 = reader.ReadByte();
                if (Unk6 != 0)
                {
                    Unk7 = reader.ReadByte();
                }
                Unk8 = reader.ReadByte();
                HasSharedReference = reader.ReadByte() != 0;
                if (HasSharedReference)
                {
                    SharedReference = reader.ReadUInt32();
                }
                Unk10 = reader.ReadUInt16();
                Unk12 = reader.ReadUInt16();
            }
        }

        public enum AnimType : byte
        {
            CompStream = 0,
            Sequence = 1,
            Grid = 2,
        }

        public class CAnimCompStream
        {
            public uint TotalSize;
            public ushort DataOffset;
            public ushort Unk3;
            public uint Unk4;
            public float Scale;
            public ushort Unk6;

            // FUN_71003cec08 reads an unaligned 64-bit field at +0x12,
            // followed by a ushort at +0x1A. Their meanings remain unknown.
            public ulong HeaderValueAt0x12;
            public ushort HeaderValueAt0x1A;

            public byte[] HeaderExtension = Array.Empty<byte>();
            public byte[] Data;

            public CPooledName PooledName;

            public CAnimCompStream(FileReader reader)
            {
                long start = reader.Position;

                TotalSize = reader.ReadUInt32();
                DataOffset = reader.ReadUInt16();
                Unk3 = reader.ReadUInt16();
                Unk4 = reader.ReadUInt32();
                Scale = reader.ReadSingle();
                Unk6 = reader.ReadUInt16();
                HeaderValueAt0x12 = BinaryPrimitives.ReadUInt64LittleEndian(ReadExactBytes(reader, 8, "CompStream header field at 0x12"));
                HeaderValueAt0x1A = reader.ReadUInt16();

                // The Ghidra constructor reads through offset 0x1B (28 bytes).
                const int ParsedHeaderSize = 0x1C;

                if (TotalSize < ParsedHeaderSize || TotalSize > int.MaxValue)
                {
                    throw new InvalidDataException($"Invalid CHPR CompStream size: {TotalSize}");
                }

                if (DataOffset < ParsedHeaderSize || DataOffset > TotalSize)
                {
                    throw new InvalidDataException($"Invalid CHPR CompStream data offset: {DataOffset}");
                }

                int extensionSize = DataOffset - ParsedHeaderSize;
                int dataSize = checked((int)(TotalSize - DataOffset));

                HeaderExtension = reader.ReadBytes(extensionSize);
                if (HeaderExtension.Length != extensionSize)
                {
                    throw new EndOfStreamException("Unexpected end of CHPR CompStream header extension.");
                }

                Data = reader.ReadBytes(dataSize);
                if (Data.Length != dataSize)
                {
                    throw new EndOfStreamException("Unexpected end of CHPR CompStream data.");
                }

                if (reader.Position - start != TotalSize)
                {
                    throw new InvalidDataException("CHPR CompStream did not consume its declared size.");
                }
            }
        }

        public class CAnimSequence
        {
            public byte Unk1;
            public byte Unk2;
            public byte[] Data = Array.Empty<byte>();
            public uint Unk3;
            public List<CAnimSequenceEntry> Entries = new List<CAnimSequenceEntry>();

            public CAnimSequence(FileReader reader)
            {
                uint numEntries = reader.ReadUInt32();
                uint dataSize = reader.ReadUInt32();

                Unk1 = reader.ReadByte();
                Unk2 = reader.ReadByte();

                if (numEntries > int.MaxValue || dataSize > int.MaxValue)
                {
                    throw new InvalidDataException("CHPR sequence contains an invalid size.");
                }

                for (int i = 0; i < (int)numEntries; i++)
                {
                    Entries.Add(new CAnimSequenceEntry
                    {
                        Unk1 = reader.ReadByte(),
                        Unk2 = reader.ReadByte(),
                        Unk3 = reader.ReadByte(),
                        Unk4 = reader.ReadUInt16(),
                        Unk5 = reader.ReadUInt32()
                    });
                }

                Data = ReadExactBytes(reader, (int)dataSize, "sequence data");
                Unk3 = reader.ReadUInt32();
            }

            public class CAnimSequenceEntry
            {
                public byte Unk1;
                public byte Unk2;
                public byte Unk3;
                public ushort Unk4;
                public uint Unk5;

                // The native reader preserves the low seven bits of Unk2 and
                // derives bit 7 from whether Unk3 is nonzero.
                public byte PackedFlags => (byte)((Unk2 & 0x7F) | (Unk3 != 0 ? 0x80 : 0));
            }
        }

        public class CGridPointData
        {
            public byte PointCount;
            public byte Unk1;
            public byte Unk2;
            public byte Unk3;
            public byte Unk4;
            public byte[] Unk5;
            public byte[] Unk6;

            public CGridPointData(FileReader reader)
            {
                PointCount = reader.ReadByte();
                Unk1 = reader.ReadByte();
                Unk2 = reader.ReadByte();
                Unk3 = reader.ReadByte();
                Unk4 = reader.ReadByte();
                Unk5 = ReadExactBytes(reader, PointCount, "grid point values");
                Unk6 = ReadExactBytes(reader, Unk1 == 0 ? PointCount * 4 : 4, "grid point metadata");
            }
        }

        public class CGridGeo2d
        {
            public CGridPointData GridPointData;
            public byte[] Unk3;
            public byte[] Unk4;
            public byte[] Unk5;
            public byte[] Unk6;
            public byte[] Unk7;

            public CGridGeo2d(FileReader reader)
            {
                GridPointData = new CGridPointData(reader);
                byte num1 = reader.ReadByte();
                byte num2 = reader.ReadByte();
                byte num3 = reader.ReadByte();

                Unk3 = ReadExactBytes(reader, 8, "2D grid block 1");
                Unk4 = ReadExactBytes(reader, 8, "2D grid block 2");
                Unk5 = ReadExactBytes(reader, num1 * 4, "2D grid block 3");
                Unk6 = ReadExactBytes(reader, num2 * 8, "2D grid block 4");
                Unk7 = ReadExactBytes(reader, num3 * 4, "2D grid block 5");
            }
        }

        public class CGridGeo1d
        {
            public CGridPointData GridPointData;
            public float Unk3;
            public float Unk4;
            public byte[] Buffer;

            public CGridGeo1d(FileReader reader)
            {
                GridPointData = new CGridPointData(reader);
                Unk3 = reader.ReadSingle();
                Unk4 = reader.ReadSingle();
                Buffer = ReadExactBytes(reader, GridPointData.PointCount * 4, "1D grid values");
            }
        }

        public class CAnimGrid
        {
            public CGridGeo1d Grid1;
            public CGridGeo2d Grid2;
            public byte Type;
            public uint[] Unknowns2;
            public uint[] Unknowns3;

            public CAnimGrid(FileReader reader)
            {
                int numUnk3 = reader.ReadInt32();
                Type = reader.ReadByte();

                if (numUnk3 < 0)
                {
                    throw new InvalidDataException("Invalid CHPR grid entry count.");
                }

                switch (Type)
                {
                    case 0:
                        break;
                    case 1:
                        Grid1 = new CGridGeo1d(reader);
                        break;
                    case 2:
                        Grid2 = new CGridGeo2d(reader);
                        break;
                    default:
                        throw new InvalidDataException($"Unknown CHPR grid type: {Type}");
                }

                int numUnk2 = reader.ReadInt32();
                if (numUnk2 < 0)
                {
                    throw new InvalidDataException("Invalid CHPR grid auxiliary count.");
                }

                if (numUnk2 > 0)
                    Unknowns2 = reader.ReadUInt32s(numUnk2);

                if (numUnk3 > 0)
                    Unknowns3 = reader.ReadUInt32s(numUnk3);
            }
        }

        public class CAnim
        {
            public SBaseInfo BaseInfo;
            public CAnimCompStream AnimCompStream;
            public CAnimSequence AnimSequence;
            public CAnimGrid AnimGrid;

            public CAnim(FileReader reader)
            {
                BaseInfo = new SBaseInfo(reader);

                switch (BaseInfo.Type)
                {
                    case AnimType.CompStream:
                        AnimCompStream = new CAnimCompStream(reader);
                        break;
                    case AnimType.Sequence:
                        AnimSequence = new CAnimSequence(reader);
                        break;
                    case AnimType.Grid:
                        AnimGrid = new CAnimGrid(reader);
                        break;
                    default:
                        throw new InvalidDataException($"Unsupported CHPR animation type: {(byte)BaseInfo.Type}");
                }
            }
        }

        #endregion

        #region Shared packed-data helper

        /// <summary>
        /// Bounded low-bit-first reader matching FUN_71008a6d44's bit-ordering.
        /// Shared by the component opcode parser and low-level value decoders.
        /// </summary>
        public sealed class PackedBitReader
        {
            private readonly byte[] data;
            private long bitPosition;

            public PackedBitReader(byte[] data)
            {
                this.data = data ?? throw new ArgumentNullException(nameof(data));
            }

            public long BitPosition => bitPosition;
            public long BitsConsumed => bitPosition;
            public long RemainingBits => (long)data.Length * 8 - bitPosition;

            public uint ReadBits(int count)
            {
                if (count < 0 || count > 32)
                {
                    throw new ArgumentOutOfRangeException(nameof(count), "Bit count must be between 0 and 32.");
                }

                if (count == 0)
                    return 0;

                if (count > RemainingBits)
                {
                    throw new EndOfStreamException($"Cannot read {count} bits at bit offset {bitPosition}; " + "the packed buffer has ended.");
                }

                uint result = 0;
                for (int i = 0; i < count; i++)
                {
                    int byteIndex = checked((int)(bitPosition >> 3));
                    int bitIndex = (int)(bitPosition & 7);
                    uint bit = (uint)((data[byteIndex] >> bitIndex) & 1);

                    result |= bit << i;
                    bitPosition++;
                }

                return result;
            }
        }

        #endregion

        private static byte[] ReadExactBytes(FileReader reader, int count, string context)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            byte[] bytes = reader.ReadBytes(count);
            if (bytes == null || bytes.Length != count)
            {
                throw new EndOfStreamException($"Unexpected end of CHPR {context}: expected {count} bytes, " + $"received {bytes?.Length ?? 0}.");
            }

            return bytes;
        }
    }

    /// <summary>
    /// Low-level value-record decoders from the CHPR animation runtime.
    /// These operations remain separate from command parsing because they need
    /// already-resolved record/index buffers; they do not independently map bones.
    /// </summary>
    public static class CHPRAnimationValueDecoder
    {
        /// <summary>
        /// Decode the sparse scalar updates performed by FUN_71008af1f0.
        ///
        /// records is a packed runtime record array with an 8-byte stride:
        /// byte +0 = number of input bits, byte +1 = signed-value shift,
        /// int32 at +4 = decoded value.
        /// indexList stores [count, index0, index1, ...].
        ///
        /// The caller must supply the record indexes already resolved by the
        /// surrounding CHPR component machinery.
        /// </summary>
        public static void DecodeSparseSignedValues(CHPRAnimationDecoder.PackedBitReader bits, byte[] records, ushort[] indexList)
        {
            if (bits == null) throw new ArgumentNullException(nameof(bits));
            if (records == null) throw new ArgumentNullException(nameof(records));
            ValidateIndexList(indexList);
            ValidateRecordStride(records, 8, "8-byte sparse value records");

            int count = indexList[0];
            for (int i = 0; i < count; i++)
            {
                int recordIndex = indexList[i + 1];
                int offset = GetRecordOffset(recordIndex, 8, records.Length);
                int bitCount = records[offset];
                int shift = records[offset + 1] & 0x1F;
                int raw = ReadSignedScaled(bits, bitCount, shift);
                BinaryPrimitives.WriteInt32LittleEndian(records.AsSpan(offset + 4, 4), raw);
            }
        }

        /// <summary>
        /// Decode the differential scalar updates performed by FUN_7100e58a50.
        /// Records have a 12-byte stride: byte +0 = bit count, byte +1 = shift,
        /// int32 +4 = previous value, int32 +8 = current value. Each update
        /// moves current to previous before writing the new signed value.
        /// indexList stores [count, index0, index1, ...].
        /// </summary>
        public static void DecodeDifferentialSignedValues(CHPRAnimationDecoder.PackedBitReader bits, byte[] records, ushort[] indexList)
        {
            if (bits == null) throw new ArgumentNullException(nameof(bits));
            if (records == null) throw new ArgumentNullException(nameof(records));
            ValidateIndexList(indexList);
            ValidateRecordStride(records, 12, "12-byte differential value records");

            int count = indexList[0];
            for (int i = 0; i < count; i++)
            {
                int recordIndex = indexList[i + 1];
                int offset = GetRecordOffset(recordIndex, 12, records.Length);
                int bitCount = records[offset];
                int shift = records[offset + 1] & 0x1F;

                int current = BinaryPrimitives.ReadInt32LittleEndian(records.AsSpan(offset + 8, 4));
                BinaryPrimitives.WriteInt32LittleEndian(records.AsSpan(offset + 4, 4), current);

                int decoded = ReadSignedScaled(bits, bitCount, shift);
                BinaryPrimitives.WriteInt32LittleEndian(records.AsSpan(offset + 8, 4), decoded);
            }
        }

        /// <summary>
        /// Decode the bit-mask update performed by FUN_7100e58b60.
        ///
        /// The bit stream contributes indexList[0] individual bits. Each
        /// subsequent uint16 index is a delta; deltas are accumulated. The
        /// accumulated value selects a uint16 in lookupTable at
        /// lookupTableOffset + accumulatedIndex*2. In that lookup value,
        /// bits 0..9 select a uint32 in values and bits 10..15 select the bit
        /// within that uint32. This follows the native table arithmetic.
        /// </summary>
        public static void DecodeBitMaskUpdates(CHPRAnimationDecoder.PackedBitReader bits, byte[] lookupTable, int lookupTableOffset, byte[] values, ushort[] indexList)
        {
            if (bits == null) throw new ArgumentNullException(nameof(bits));
            if (lookupTable == null) throw new ArgumentNullException(nameof(lookupTable));
            if (values == null) throw new ArgumentNullException(nameof(values));
            ValidateIndexList(indexList);
            if (lookupTableOffset < 0 || lookupTableOffset > lookupTable.Length)
                throw new ArgumentOutOfRangeException(nameof(lookupTableOffset));
            ValidateRecordStride(values, 4, "bit-mask value words");

            int count = indexList[0];
            if (count == 0) return;
            uint accumulatedIndex = 0;

            for (int i = 0; i < count; i++)
            {
                // Read one bit per indexed entry. This also handles counts
                // greater than the 32-bit limit of PackedBitReader.ReadBits.
                uint bit = bits.ReadBits(1);
                accumulatedIndex = checked(accumulatedIndex + indexList[i + 1]);
                ulong lookupOffset64 = (ulong)lookupTableOffset + (ulong)accumulatedIndex * 2UL;
                if (lookupOffset64 + 2UL > (ulong)lookupTable.Length)
                    throw new InvalidDataException("CHPR bit-mask lookup index exceeds its table.");

                int lookupOffset = checked((int)lookupOffset64);
                ushort lookup = BinaryPrimitives.ReadUInt16LittleEndian(lookupTable.AsSpan(lookupOffset, 2));
                int wordIndex = lookup & 0x03FF;
                int bitIndex = lookup >> 10;
                int wordOffset = GetRecordOffset(wordIndex, 4, values.Length);
                uint current = BinaryPrimitives.ReadUInt32LittleEndian(values.AsSpan(wordOffset, 4));
                uint mask = 1u << bitIndex;
                uint updated = (current & ~mask) | (bit << bitIndex);
                BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(wordOffset, 4), updated);
            }
        }

        /// <summary>
        /// Port of FUN_7100b0c86c: shortest-path, sign-corrected normalized
        /// linear interpolation between two quaternions. Quaternion components
        /// are treated as X/Y/Z/W, matching System.Numerics.Quaternion.
        /// </summary>
        public static Quaternion NlerpQuaternion(float amount, Quaternion a, Quaternion b)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot < 0.0f)
                b = new Quaternion(-b.X, -b.Y, -b.Z, -b.W);

            float x = a.X + (b.X - a.X) * amount;
            float y = a.Y + (b.Y - a.Y) * amount;
            float z = a.Z + (b.Z - a.Z) * amount;
            float w = a.W + (b.W - a.W) * amount;
            float lengthSquared = x * x + y * y + z * z + w * w;

            // The native implementation protects reciprocal-square-root from
            // a zero/tiny length. This exact sqrt is numerically equivalent for
            // normal inputs and avoids NaNs for the zero quaternion.
            float inverseLength = 1.0f / MathF.Sqrt(MathF.Max(lengthSquared, 1.0e-30f));
            return new Quaternion(x * inverseLength, y * inverseLength, z * inverseLength, w * inverseLength);
        }

        private static int ReadSignedScaled(CHPRAnimationDecoder.PackedBitReader bits, int bitCount, int shift)
        {
            if (bitCount < 0 || bitCount > 32)
                throw new InvalidDataException($"Invalid CHPR value bit count: {bitCount}.");

            // This mirrors the native signed operation: (raw << shift) >> shift.
            // C# masks shift counts to five bits, just as the ARM instruction does.
            int raw = unchecked((int)bits.ReadBits(bitCount));
            return unchecked(raw << shift) >> shift;
        }

        private static void ValidateIndexList(ushort[] indexList)
        {
            if (indexList == null) throw new ArgumentNullException(nameof(indexList));
            if (indexList.Length == 0)
                throw new InvalidDataException("CHPR index list is missing its count word.");
            if (indexList[0] > indexList.Length - 1)
                throw new InvalidDataException("CHPR index list count exceeds its available indexes.");
        }

        private static void ValidateRecordStride(byte[] records, int stride, string label)
        {
            if (records.Length % stride != 0)
                throw new InvalidDataException($"CHPR {label} length is not divisible by {stride}.");
        }

        private static int GetRecordOffset(int index, int stride, int bufferLength)
        {
            long offset = (long)index * stride;
            if (offset < 0 || offset + stride > bufferLength)
                throw new InvalidDataException($"CHPR record index {index} exceeds its data buffer.");
            return (int)offset;
        }
    }


    /// <summary>
    /// Parser for the packed command stream dispatched by PTR_FUN_71017b2f20.
    /// It consumes caller-supplied native-layout descriptor blocks and does not
    /// assume that those blocks are serialized directly in CAnimCompStream.Data.
    /// </summary>
    public static class CHPRComponentOpcodeDecoder
    {
        public const int DescriptorTableOffset = 0x18;
        public const int DescriptorStride = 6;
        public const int DescriptorItemCountMask = 0x03FF;

        // In FUN_7100e51a58, the command loop continues while the handler returns
        // a non-null context. The body for table slot 1 was not in the supplied
        // extract; all other included handlers return that context unchanged.
        // Therefore opcode 1 is treated as the terminator, with this inference
        // explicitly exposed rather than hidden in the parsing logic.
        public const int InferredTerminatorOpcode = 1;

        public sealed class DescriptorBlock
        {
            public byte[] Data { get; }
            public int DescriptorCount { get; }
            public string Label { get; }

            /// <param name="data">
            /// Byte view rooted at the same base address used as param_1[2] by
            /// the native handlers. Descriptor entries are expected at +0x18.
            /// </param>
            /// <param name="descriptorCount">Number of six-byte records available.</param>
            public DescriptorBlock(byte[] data, int descriptorCount, string label = null)
            {
                Data = data ?? throw new ArgumentNullException(nameof(data));
                if (descriptorCount < 0)
                    throw new ArgumentOutOfRangeException(nameof(descriptorCount));

                int required = checked(DescriptorTableOffset + descriptorCount * DescriptorStride);
                if (data.Length < required)
                    throw new InvalidDataException($"Descriptor block is too short: needs 0x{required:X} bytes for {descriptorCount} records, " + $"has 0x{data.Length:X}.");

                DescriptorCount = descriptorCount;
                Label = label;
            }

            internal int ReadItemCount(int descriptorIndex)
            {
                if ((uint)descriptorIndex >= (uint)DescriptorCount)
                    throw new InvalidDataException($"Descriptor index {descriptorIndex} is outside block '{Label ?? "(unnamed)"}' " + $"with {DescriptorCount} records.");

                int offset = checked(DescriptorTableOffset + descriptorIndex * DescriptorStride);
                ushort word = BinaryPrimitives.ReadUInt16LittleEndian(Data.AsSpan(offset, 2));
                return word & DescriptorItemCountMask;
            }
        }

        public sealed class PackedItem
        {
            // Fields are preserved as raw decoded values; the handlers discard
            // these fields natively, so no semantic names are assigned here.
            public uint Field0 { get; internal set; }
            public uint Field1 { get; internal set; }
            public bool Flag { get; internal set; }
            public uint Field2 { get; internal set; }
            public ushort Value16 { get; internal set; }
        }

        public sealed class Command
        {
            public int Opcode { get; internal set; }
            public long StartBit { get; internal set; }
            public long EndBit { get; internal set; }
            public int? SelectedBlockIndex { get; internal set; }
            public int? DescriptorIndex { get; internal set; }
            public int? ItemCount { get; internal set; }
            public int? IgnoredNibble { get; internal set; }
            public List<PackedItem> Items { get; } = new List<PackedItem>();
        }

        public sealed class DecodeResult
        {
            public List<Command> Commands { get; } = new List<Command>();

            // Opcode 5 appends the decoded 16-bit values to the native ushort
            // vector. These are lookup IDs, not yet bone indices or keyframes.
            public List<ushort> SelectedValues { get; } = new List<ushort>();

            public long BitsConsumed { get; internal set; }
            public bool EncounteredTerminator { get; internal set; }
            public int TerminatorOpcode { get; internal set; }
            public int? FinalSelectedBlockIndex { get; internal set; }
        }

        /// <summary>
        /// Parses commands from a byte slice that starts exactly at the packed
        /// command bitstream. Bits are read least-significant-bit first, matching
        /// FUN_71008a6d44.
        /// </summary>
        public static DecodeResult Decode(
            byte[] commandBytes,
            IReadOnlyList<DescriptorBlock> descriptorBlocks,
            int terminatorOpcode = InferredTerminatorOpcode,
            int maxCommands = 1_000_000)
        {
            if (commandBytes == null)
                throw new ArgumentNullException(nameof(commandBytes));
            if (descriptorBlocks == null)
                throw new ArgumentNullException(nameof(descriptorBlocks));
            if (terminatorOpcode < 0 || terminatorOpcode > 15)
                throw new ArgumentOutOfRangeException(nameof(terminatorOpcode));
            if (maxCommands < 1)
                throw new ArgumentOutOfRangeException(nameof(maxCommands));

            var bits = new CHPRAnimationDecoder.PackedBitReader(commandBytes);
            var result = new DecodeResult { TerminatorOpcode = terminatorOpcode };
            int? currentBlockIndex = null;

            for (int commandNumber = 0; commandNumber < maxCommands; commandNumber++)
            {
                long startBit = bits.BitsConsumed;
                int opcode;
                try
                {
                    opcode = checked((int)bits.ReadBits(4));
                }
                catch (EndOfStreamException ex)
                {
                    throw new InvalidDataException($"Packed CHPR command stream ended without terminator opcode {terminatorOpcode} " + $"after {result.Commands.Count} commands ({bits.BitsConsumed} bits consumed).", ex);
                }

                var command = new Command
                {
                    Opcode = opcode,
                    StartBit = startBit
                };

                if (opcode == terminatorOpcode)
                {
                    command.EndBit = bits.BitsConsumed;
                    result.Commands.Add(command);
                    result.EncounteredTerminator = true;
                    result.BitsConsumed = bits.BitsConsumed;
                    result.FinalSelectedBlockIndex = currentBlockIndex;
                    return result;
                }

                switch (opcode)
                {
                    case 0:
                        {
                            int blockIndex = checked((int)bits.ReadBits(4));
                            if ((uint)blockIndex >= (uint)descriptorBlocks.Count)
                                throw FormatError(commandNumber, startBit, $"selects descriptor block {blockIndex}, but only {descriptorBlocks.Count} blocks were provided");
                            currentBlockIndex = blockIndex;
                            command.SelectedBlockIndex = blockIndex;
                            break;
                        }
                    case 2:
                    case 3:
                        {
                            int descriptorIndex = checked((int)bits.ReadBits(4));
                            int count = GetDescriptorItemCount(descriptorBlocks, currentBlockIndex, descriptorIndex, commandNumber, startBit);
                            command.DescriptorIndex = descriptorIndex;
                            command.ItemCount = count;

                            for (int i = 0; i < count; i++)
                            {
                                command.Items.Add(new PackedItem
                                {
                                    Field0 = bits.ReadBits(5),
                                    Field1 = bits.ReadBits(5),
                                    Flag = bits.ReadBits(1) != 0,
                                    Field2 = bits.ReadBits(3)
                                });
                            }
                            break;
                        }
                    case 4:
                        {
                            int descriptorIndex = checked((int)bits.ReadBits(4));
                            int count = GetDescriptorItemCount(descriptorBlocks, currentBlockIndex, descriptorIndex, commandNumber, startBit);
                            command.DescriptorIndex = descriptorIndex;
                            command.ItemCount = count;

                            for (int i = 0; i < count; i++)
                            {
                                command.Items.Add(new PackedItem
                                {
                                    Field0 = bits.ReadBits(5),
                                    Field1 = bits.ReadBits(5),
                                    Flag = bits.ReadBits(1) != 0
                                });
                            }
                            break;
                        }
                    case 5:
                        {
                            // The native handler reads this nibble but never uses it.
                            command.IgnoredNibble = checked((int)bits.ReadBits(4));
                            int count = checked((int)bits.ReadBits(12));
                            command.ItemCount = count;
                            for (int i = 0; i < count; i++)
                            {
                                bool flag = bits.ReadBits(1) != 0;
                                ushort value = checked((ushort)bits.ReadBits(16));
                                command.Items.Add(new PackedItem { Flag = flag, Value16 = value });
                                result.SelectedValues.Add(value);
                            }
                            break;
                        }
                    case 6:
                        {
                            int descriptorIndex = checked((int)bits.ReadBits(4));
                            int count = GetDescriptorItemCount(descriptorBlocks, currentBlockIndex, descriptorIndex, commandNumber, startBit);
                            command.DescriptorIndex = descriptorIndex;
                            command.ItemCount = count;

                            for (int i = 0; i < count; i++)
                            {
                                command.Items.Add(new PackedItem
                                {
                                    Field0 = bits.ReadBits(5),
                                    Flag = bits.ReadBits(1) != 0
                                });
                            }
                            break;
                        }
                    default:
                        throw FormatError(commandNumber, startBit, $"uses unsupported opcode {opcode}; supplied table has handlers only for slots 0 through 6");
                }

                command.EndBit = bits.BitsConsumed;
                result.Commands.Add(command);
            }

            throw new InvalidDataException($"CHPR command stream exceeded the safety limit of {maxCommands} commands without reaching terminator opcode {terminatorOpcode}.");
        }

        private static int GetDescriptorItemCount(IReadOnlyList<DescriptorBlock> descriptorBlocks, int? currentBlockIndex, int descriptorIndex, int commandNumber, long startBit)
        {
            if (!currentBlockIndex.HasValue)
                throw FormatError(commandNumber, startBit, "uses a descriptor record before opcode 0 has selected a descriptor block");

            DescriptorBlock block = descriptorBlocks[currentBlockIndex.Value];
            try
            {
                return block.ReadItemCount(descriptorIndex);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentOutOfRangeException)
            {
                throw FormatError(commandNumber, startBit, ex.Message, ex);
            }
        }

        private static InvalidDataException FormatError(int commandNumber, long bitOffset, string message, Exception inner = null)
        {
            return new InvalidDataException($"Invalid CHPR packed command #{commandNumber} at bit 0x{bitOffset:X}: {message}.", inner);
        }
    }

}
