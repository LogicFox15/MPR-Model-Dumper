using AvaloniaToolbox.Core.IO;
using ImageLibrary;
using System.Runtime.InteropServices;

namespace DKCTF
{
    /// <summary>
    /// Represents a texture file format.
    /// </summary>
    internal class TXTR : FileForm
    {
        public STextureHeader TextureHeader;
        public STextureSamplerData SamplerData; // Replaces TextureSize and Unknown
        public SMetaData Meta;
        public byte[] BufferData;
        public uint[] MipSizes = new uint[0];

        public uint TextureSize { get; set; }
        public uint Unknown { get; set; }
        public bool IsSwitch => this.FileHeader.VersionA >= 0x0F;

        public TXTR() { }

        public TXTR(System.IO.Stream stream) : base(stream)
        {
        }

        public byte[] CreateUncompressedFile(byte[] fileData, CFormDescriptor pakHeader, bool isLittleEndian)
        {
            var mem = new MemoryStream();
            using (var writer = new FileWriter(mem))
            using (var reader = new FileReader(fileData))
            {
                reader.SetByteOrder(!isLittleEndian);
                writer.SetByteOrder(!isLittleEndian);

                FileHeader = reader.ReadStruct<CFormDescriptor>();
                ReadMetaFooter(reader);

                //Rewrite header for saving uncompressed file
                reader.Position = 0;
                byte[] textureInfo = reader.ReadBytes((int)Meta.GPUOffset + 24);

                long pos = reader.BaseStream.Position - 24;

                List<byte[]> combinedBuffer = new List<byte[]>();

                byte[] textureData = new byte[Meta.DecompressedSize];

                if (pakHeader.VersionA >= 1 && pakHeader.VersionB >= 1)
                {
                    //Decompress all buffers
                    // --- FIRST BUFFER ---
                    if (Meta.Buffers.FirstSize > 0)
                    {
                        var readInfo = Meta.TextureInfo[(int)Meta.Buffers.FirstIndex];

                        reader.SeekBegin(readInfo.Offset);

                        // Read the block to check if it's actually compressed
                        byte[] compBuf = reader.ReadBytes((int)Meta.Buffers.FirstSize);

                        if (compBuf.Length == Meta.Buffers.FirstDestSize)
                        {
                            // Data is uncompressed, copy directly
                            Array.Copy(compBuf, 0, textureData, (int)Meta.Buffers.FirstDestOffset, compBuf.Length);
                        }
                        else
                        {
                            // Data is compressed, rewind and decompress
                            reader.SeekBegin(readInfo.Offset);
                            byte[] decBuf = IOFileExtension.DecompressedBuffer(reader, Meta.Buffers.FirstSize, Meta.Buffers.FirstDestSize, IsSwitch || isLittleEndian);
                            Array.Copy(decBuf, 0, textureData, (int)Meta.Buffers.FirstDestOffset, decBuf.Length);
                        }
                    }

                    // --- SECOND BUFFER ---
                    if (Meta.Buffers.SecondSize > 0)
                    {
                        var readInfo = Meta.TextureInfo[(int)Meta.Buffers.SecondIndex];

                        // Part A: Compressed portion of the second buffer
                        if (Meta.Buffers.SecondCompressedOffset < Meta.Buffers.SecondSize)
                        {
                            reader.SeekBegin(readInfo.Offset + Meta.Buffers.SecondCompressedOffset);

                            uint dstStart = Meta.Buffers.SecondDestOffset;

                            // FIX: Check if the buffer is actually uncompressed by comparing compressed vs decompressed lengths
                            if (Meta.Buffers.SecondCompressedLen == Meta.Buffers.SecondDecompressedLen)
                            {
                                // Data is uncompressed, copy directly
                                byte[] uncompBuf = reader.ReadBytes((int)Meta.Buffers.SecondCompressedLen);
                                Array.Copy(uncompBuf, 0, textureData, (int)dstStart, uncompBuf.Length);
                                // Note: Change `textureData` to `BufferData` when applying this inside ReadChunk()
                            }
                            else
                            {
                                // Data is compressed, decompress it
                                byte[] decBuf = IOFileExtension.DecompressedBuffer(reader, Meta.Buffers.SecondCompressedLen, Meta.Buffers.SecondDecompressedLen, IsSwitch || isLittleEndian);
                                // Note: Use `IsSwitch` instead of `IsSwitch || isLittleEndian` inside ReadChunk() to match your existing logic

                                Array.Copy(decBuf, 0, textureData, (int)dstStart, decBuf.Length);
                            }
                        }

                        // Part B: Uncompressed start of the second buffer
                        if (Meta.Buffers.SecondCompressedOffset > 0)
                        {
                            reader.SeekBegin(readInfo.Offset);
                            byte[] uncompStart = reader.ReadBytes((int)Meta.Buffers.SecondCompressedOffset);

                            uint dstStart = Meta.Buffers.SecondDestOffset + Meta.Buffers.SecondDecompressedLen;
                            Array.Copy(uncompStart, 0, textureData, (int)dstStart, uncompStart.Length);
                        }

                        // Part C: Uncompressed end of the second buffer
                        uint endCompOffset = Meta.Buffers.SecondCompressedOffset + Meta.Buffers.SecondCompressedLen;
                        if (endCompOffset < Meta.Buffers.SecondSize)
                        {
                            reader.SeekBegin(readInfo.Offset + endCompOffset);

                            uint uncompEndSize = Meta.Buffers.SecondSize - endCompOffset;
                            byte[] uncompEnd = reader.ReadBytes((int)uncompEndSize);

                            uint dstStart = Meta.Buffers.SecondDestOffset + Meta.Buffers.SecondCompressedOffset + Meta.Buffers.SecondDecompressedLen;
                            Array.Copy(uncompEnd, 0, textureData, (int)dstStart, uncompEnd.Length);
                        }
                    }
                }
                else
                {
                    var buffer = Meta.BufferInfoV1[0];
                    reader.SeekBegin(Meta.GPUDataStart + buffer.Offset);

                    textureData = IOFileExtension.DecompressedBuffer(reader, buffer.CompressedSize, buffer.DecompressedSize, IsSwitch);
                }

                writer.Write(textureInfo);
                writer.Write(textureData);

                using (writer.TemporarySeek(pos + 4, SeekOrigin.Begin)) {
                    writer.Write((long)textureData.Length);
                }

                return mem.ToArray();
            }
        }

        public override void ReadChunk(FileReader reader, CChunkDescriptor chunk)
        {
            switch (chunk.ChunkType)
            {
                case "HEAD":
                    TextureHeader = reader.ReadStruct<STextureHeader>();
                    // MipCount is directly in the header now
                    MipSizes = reader.ReadUInt32s((int)TextureHeader.MipCount);
                    // Read the appended sampler data
                    SamplerData = reader.ReadStruct<STextureSamplerData>();
                    break;

                case "GPU ":
                    if (Meta != null)
                    {
                        if (this.FileHeader.VersionA >= 1 && this.FileHeader.VersionB >= 1)
                        {
                            // NEW FORMAT: Decompress both buffers into a single BufferData array
                            BufferData = new byte[Meta.DecompressedSize];

                            // --- FIRST BUFFER ---
                            if (Meta.Buffers.FirstSize > 0)
                            {
                                var readInfo = Meta.TextureInfo[(int)Meta.Buffers.FirstIndex];

                                reader.SeekBegin(readInfo.Offset);
                                byte[] compBuf = reader.ReadBytes((int)Meta.Buffers.FirstSize);

                                if (compBuf.Length == Meta.Buffers.FirstDestSize)
                                {
                                    // Data is uncompressed, copy directly
                                    Array.Copy(compBuf, 0, BufferData, (int)Meta.Buffers.FirstDestOffset, compBuf.Length);
                                }
                                else
                                {
                                    // Data is compressed, rewind and decompress
                                    reader.SeekBegin(readInfo.Offset);
                                    byte[] decBuf = IOFileExtension.DecompressedBuffer(reader, Meta.Buffers.FirstSize, Meta.Buffers.FirstDestSize, IsSwitch);
                                    Array.Copy(decBuf, 0, BufferData, (int)Meta.Buffers.FirstDestOffset, decBuf.Length);
                                }
                            }

                            // --- SECOND BUFFER ---
                            if (Meta.Buffers.SecondSize > 0)
                            {
                                var readInfo = Meta.TextureInfo[(int)Meta.Buffers.SecondIndex];

                                // Part A: Compressed portion of the second buffer
                                if (Meta.Buffers.SecondCompressedOffset < Meta.Buffers.SecondSize)
                                {
                                    reader.SeekBegin(readInfo.Offset + Meta.Buffers.SecondCompressedOffset);
                                    uint dstStart = Meta.Buffers.SecondDestOffset;
                                    byte[] decBuf = IOFileExtension.DecompressedBuffer(reader, Meta.Buffers.SecondCompressedLen, Meta.Buffers.SecondDecompressedLen, IsSwitch);
                                    Array.Copy(decBuf, 0, BufferData, (int)dstStart, decBuf.Length);
                                }

                                // Part B: Uncompressed start of the second buffer
                                if (Meta.Buffers.SecondCompressedOffset > 0)
                                {
                                    reader.SeekBegin(readInfo.Offset);
                                    byte[] uncompStart = reader.ReadBytes((int)Meta.Buffers.SecondCompressedOffset);
                                    uint dstStart = Meta.Buffers.SecondDestOffset + Meta.Buffers.SecondDecompressedLen;
                                    Array.Copy(uncompStart, 0, BufferData, (int)dstStart, uncompStart.Length);
                                }

                                // Part C: Uncompressed end of the second buffer
                                uint endCompOffset = Meta.Buffers.SecondCompressedOffset + Meta.Buffers.SecondCompressedLen;
                                if (endCompOffset < Meta.Buffers.SecondSize)
                                {
                                    reader.SeekBegin(readInfo.Offset + endCompOffset);
                                    uint uncompEndSize = Meta.Buffers.SecondSize - endCompOffset;
                                    byte[] uncompEnd = reader.ReadBytes((int)uncompEndSize);
                                    uint dstStart = Meta.Buffers.SecondDestOffset + Meta.Buffers.SecondCompressedOffset + Meta.Buffers.SecondDecompressedLen;
                                    Array.Copy(uncompEnd, 0, BufferData, (int)dstStart, uncompEnd.Length);
                                }
                            }
                        }
                        else
                        {
                            // OLD FORMAT: Use the V1 fallback logic
                            if (Meta.BufferInfoV1.Count > 0)
                            {
                                var buffer = Meta.BufferInfoV1[0];
                                reader.SeekBegin(Meta.GPUDataStart + buffer.Offset);
                                BufferData = IOFileExtension.DecompressedBuffer(reader, buffer.CompressedSize, buffer.DecompressedSize, IsSwitch);
                            }
                        }
                    }
                    else
                    {
                        BufferData = reader.ReadBytes((int)chunk.DataSize);
                    }
                    break;
            }
        }

        public override void ReadMetaData(FileReader reader, CFormDescriptor pakVersion)
        {
            Meta = new SMetaData();
            if (pakVersion.VersionA >= 1 && pakVersion.VersionB >= 1)
            {
                Meta.Unknown1 = reader.ReadUInt32();
                Meta.Unknown2 = reader.ReadUInt32();
                Meta.AllocCategory = reader.ReadUInt32();
                Meta.GPUOffset = reader.ReadUInt32();
                Meta.BaseAlignment = reader.ReadUInt32();
                Meta.DecompressedSize = reader.ReadUInt32();

                // Read the info count and explicitly loop it
                uint infoCount = reader.ReadUInt32();
                Meta.TextureInfo = new List<STextureReadInfo>((int)infoCount);
                for (int i = 0; i < infoCount; i++)
                {
                    Meta.TextureInfo.Add(reader.ReadStruct<STextureReadInfo>());
                }

                // Read the single fixed buffer struct
                Meta.Buffers = reader.ReadStruct<SCompressedBufferInfo2>();
            }
            else
            {
                Meta.Unknown1 = reader.ReadUInt32();
                Meta.AllocCategory = reader.ReadUInt32();
                Meta.GPUOffset = reader.ReadUInt32();
                Meta.BaseAlignment = reader.ReadUInt32();
                Meta.GPUDataStart = reader.ReadUInt32();
                Meta.GPUDataSize = reader.ReadUInt32();
                Meta.BufferInfoV1 = IOFileExtension.ReadList<SCompressedBufferInfoV1>(reader);
            }
        }

        public override void WriteMetaData(FileWriter writer, CFormDescriptor pakVersion)
        {
            if (pakVersion.VersionA >= 1 && pakVersion.VersionB >= 1)
            {
                writer.Write(Meta.Unknown1);
                writer.Write(Meta.Unknown2);
                writer.Write(Meta.AllocCategory);
                writer.Write(Meta.GPUOffset);
                writer.Write(Meta.BaseAlignment);
                writer.Write(Meta.DecompressedSize);

                // Write Texture Info array
                writer.Write((uint)Meta.TextureInfo.Count);
                foreach (var info in Meta.TextureInfo)
                {
                    writer.Write(info.Index);
                    writer.Write(info.Offset);
                    writer.Write(info.Size);
                }

                // Write the single buffer struct
                writer.Write(Meta.Buffers.FirstIndex);
                writer.Write(Meta.Buffers.FirstSize);
                writer.Write(Meta.Buffers.FirstDestOffset);
                writer.Write(Meta.Buffers.FirstDestSize);
                writer.Write(Meta.Buffers.Unknown);
                writer.Write(Meta.Buffers.SecondIndex);
                writer.Write(Meta.Buffers.SecondSize);
                writer.Write(Meta.Buffers.SecondDestOffset);
                writer.Write(Meta.Buffers.SecondDestSize);
                writer.Write(Meta.Buffers.SecondDecompressedLen);
                writer.Write(Meta.Buffers.SecondCompressedLen);
                writer.Write(Meta.Buffers.SecondCompressedOffset);
            }
            else
            {
                // V1 Fallback
                writer.Write(Meta.Unknown1);
                writer.Write(Meta.AllocCategory);
                writer.Write(Meta.GPUOffset);
                writer.Write(Meta.BaseAlignment);
                writer.Write(Meta.GPUDataStart);
                writer.Write(Meta.GPUDataSize);
                IOFileExtension.WriteList(writer, Meta.BufferInfoV1);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class STextureHeader
        {
            public uint Type; // 1 = 2D, 3 = Cubemap
            public uint Format;
            public uint Width;
            public uint Height;
            public uint Depth;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] Components;

            public uint MipCount;
        }

        // NEW: Replaces TextureSize and Unknown
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class STextureSamplerData
        {
            public uint Unk;
            public byte Filter;
            public byte MipFilter;
            public byte WrapX;
            public byte WrapY;
            public byte WrapZ;
            public byte Aniso;
        }

        //Meta data from PAK archive
        public class SMetaData
        {
            public uint Unknown1;
            public uint Unknown2;
            public uint AllocCategory;
            public uint GPUOffset;
            public uint GPUDataStart;
            public uint GPUDataSize;
            public uint BaseAlignment;
            public uint DecompressedSize;

            // Updated to use the new Info struct
            public List<STextureReadInfo> TextureInfo = new List<STextureReadInfo>();

            // NEW: Replaces the List<SCompressedBufferInfo>
            public SCompressedBufferInfo2 Buffers;

            public List<SCompressedBufferInfoV1> BufferInfoV1 = new List<SCompressedBufferInfoV1>();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class STextureReadInfo // Replaces STextureInfo
        {
            public byte Index;
            public uint Offset; // Rust uses Offset and Size instead of Start/End offsets
            public uint Size;
        }



        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SCompressedBufferInfo2 // Replaces SCompressedBufferInfo
        {
            public uint FirstIndex;
            public uint FirstSize;
            public uint FirstDestOffset;
            public uint FirstDestSize;
            public uint Unknown;
            public uint SecondIndex;
            public uint SecondSize;
            public uint SecondDestOffset;
            public uint SecondDestSize;
            // The new format allows the second buffer to be partially uncompressed
            public uint SecondDecompressedLen;
            public uint SecondCompressedLen;
            public uint SecondCompressedOffset;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SCompressedBufferInfoV1
        {
            public uint DecompressedSize;
            public uint CompressedSize;
            public uint Offset;
        }


        public static Dictionary<uint, TextureFormat> FormatList = new()
        {       
            {  0, TextureFormat.R8_UNORM },
            {  1, TextureFormat.R8_SNORM },
            {  2, TextureFormat.R8_UINT },
            {  3, TextureFormat.R8_SINT },
            {  4, TextureFormat.R16_UNORM },
            {  5, TextureFormat.R16_SNORM },
            {  6, TextureFormat.R16_UINT },
            {  7, TextureFormat.R16_SINT },
            {  8, TextureFormat.R16_FLOAT },
            {  9, TextureFormat.R32_UINT },
            {  10, TextureFormat.R32_SINT },
            {  11, TextureFormat.R32_FLOAT },
            {  12, TextureFormat.RGBA8_UNORM },
            {  13, TextureFormat.RGBA8_SRGB },
            {  14, TextureFormat.RGBA16_FLOAT },
            {  15, TextureFormat.RGBA32_FLOAT },
            {  16, TextureFormat.D16_UNORM },
            {  17, TextureFormat.D16_UNORM },
            {  18, TextureFormat.D24_UNORM_S8_UINT },
            {  19, TextureFormat.D32_FLOAT },
            {  20, TextureFormat.BC1_UNORM },
            {  21, TextureFormat.BC1_SRGB },
            {  22, TextureFormat.BC2_UNORM },
            {  23, TextureFormat.BC2_SRGB },
            {  24, TextureFormat.BC3_UNORM },
            {  25, TextureFormat.BC3_SRGB },
            {  26, TextureFormat.BC4_UNORM },
            {  27, TextureFormat.BC4_SNORM },
            {  28, TextureFormat.BC5_UNORM },
            {  29, TextureFormat.BC5_SNORM },
            {  30, TextureFormat.RG11B10_FLOAT },
            {  31, TextureFormat.R32_FLOAT },
            {  32, TextureFormat.RG16_FLOAT },
            {  33, TextureFormat.RG8_UNORM },
            {  34, TextureFormat.RG8_UINT },
            {  35, TextureFormat.RG8_SINT },
            {  36, TextureFormat.RG16_FLOAT },
            {  37, TextureFormat.RG16_UNORM },
            {  38, TextureFormat.RG16_SNORM },
            {  39, TextureFormat.RG16_UINT },
            {  40, TextureFormat.RG16_SINT },
            {  41, TextureFormat.RGBB10A2_UNORM },
            {  42, TextureFormat.RGB10A2_UINT },
            {  43, TextureFormat.RG32_UINT },
            {  44, TextureFormat.RG32_SINT },
            {  45, TextureFormat.RG32_FLOAT },
            {  46, TextureFormat.RGBA16_UNORM },
            {  47, TextureFormat.RGBA16_SNORM },
            {  48, TextureFormat.RGBA16_UINT },
            {  49, TextureFormat.RGBA16_SINT },
            {  50, TextureFormat.RGBA32_UINT },
            {  51, TextureFormat.RGBA32_SINT },
            {  52, TextureFormat.RGBA8_UNORM }, // None
            {  53, TextureFormat.ASTC_4x4_UNORM },
            {  54, TextureFormat.ASTC_5x4_UNORM },
            {  55, TextureFormat.ASTC_5x5_UNORM },
            {  56, TextureFormat.ASTC_6x5_UNORM },
            {  57, TextureFormat.ASTC_6x6_UNORM },
            {  58, TextureFormat.ASTC_8x5_UNORM },
            {  59, TextureFormat.ASTC_8x6_UNORM },
            {  60, TextureFormat.ASTC_8x8_UNORM },
            {  61, TextureFormat.ASTC_10x5_UNORM },
            {  62, TextureFormat.ASTC_10x6_UNORM},
            {  63, TextureFormat.ASTC_10x8_UNORM},
            {  64, TextureFormat.ASTC_10x10_UNORM},
            {  65, TextureFormat.ASTC_12x10_UNORM},
            {  66, TextureFormat.ASTC_12x12_UNORM},

            {  67, TextureFormat.ASTC_4x4_SRGB},
            {  68, TextureFormat.ASTC_5x4_SRGB },
            {  69, TextureFormat.ASTC_5x5_SRGB },
            {  70, TextureFormat.ASTC_6x5_SRGB },
            {  71, TextureFormat.ASTC_6x6_SRGB },
            {  72, TextureFormat.ASTC_8x5_SRGB },
            {  73, TextureFormat.ASTC_8x6_SRGB },
            {  74, TextureFormat.ASTC_8x8_SRGB},
            {  75, TextureFormat.ASTC_10x5_SRGB },
            {  76, TextureFormat.ASTC_10x6_SRGB},
            {  77, TextureFormat.ASTC_10x8_SRGB},
            {  78, TextureFormat.ASTC_10x10_SRGB},
            {  79, TextureFormat.ASTC_12x10_SRGB},
            {  80, TextureFormat.ASTC_12x12_SRGB},

            {  81, TextureFormat.BC6H_UF16 },
            {  82, TextureFormat.BC6H_SF16 },
            {  83, TextureFormat.BC7_UNORM },
            {  84, TextureFormat.BC7_SRGB },
        };
    }
}
