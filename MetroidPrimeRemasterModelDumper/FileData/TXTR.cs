using AvaloniaToolbox.Core.IO;
using ImageLibrary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DKCTF
{
    /// <summary>
    /// Represents a texture file format.
    /// </summary>
    internal class TXTR : FileForm
    {
        public STextureHeader TextureHeader;

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
                    foreach (var info in Meta.TextureInfo)
                    {
                        var buff = Meta.BufferInfo[info.Index];

                        //Go to the buffer
                        reader.SeekBegin(info.StartOffset + buff.StartOffset);
                        //Decompress the buffer
                        byte[] BufferData = IOFileExtension.DecompressedBuffer(reader, buff.CompressedSize, buff.DestSize, IsSwitch || isLittleEndian);
                        combinedBuffer.Add(BufferData);

                        Array.Copy(BufferData, 0, textureData, (int)buff.DestOffset, (int)buff.DestSize);
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
                    uint numMips = reader.ReadUInt32();
                    MipSizes = reader.ReadUInt32s((int)numMips);
                    TextureSize = reader.ReadUInt32();
                    Unknown = reader.ReadUInt32();
                    break;
                case "GPU ":
                    if (Meta != null)
                    {
                        var buffer = Meta.BufferInfo[0];
                        reader.SeekBegin(buffer.StartOffset);
                        BufferData = IOFileExtension.DecompressedBuffer(reader, (uint)buffer.CompressedSize, (uint)buffer.DestSize, IsSwitch);
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
            // MPR
            if (pakVersion.VersionA >= 1 && pakVersion.VersionB >= 1)
            {
                reader.ReadUInt32(); //Extra uint in MPR
                Meta.Unknown = reader.ReadUInt32();
                Meta.AllocCategory = reader.ReadUInt32();
                Meta.GPUOffset = reader.ReadUInt32();
                Meta.BaseAlignment = reader.ReadUInt32();
                Meta.DecompressedSize = reader.ReadUInt32(); //total decomp size
                Meta.TextureInfo = IOFileExtension.ReadList<STextureInfo>(reader);
                Meta.BufferInfo = IOFileExtension.ReadList<SCompressedBufferInfo>(reader);
            }
            else
            {
                Meta.Unknown = reader.ReadUInt32();
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
                writer.Write(Meta.Unknown);
                writer.Write(0);
                writer.Write(Meta.AllocCategory);
                writer.Write(Meta.GPUOffset);
                writer.Write(Meta.BaseAlignment);
                writer.Write(Meta.DecompressedSize);
                IOFileExtension.WriteList(writer, Meta.TextureInfo);
                IOFileExtension.WriteList(writer, Meta.BufferInfo);
            }
            else
            {
                writer.Write(Meta.Unknown);
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
            public uint Type; //1 = 2D 3 = Cubemap
            public uint Format;
            public uint Width;
            public uint Height;
            public uint Depth;
            public uint TileMode;
            public uint Swizzle;
        }

        //Meta data from PAK archive
        public class SMetaData
        {
            public uint Unknown; //4
            public uint Unknown2;
            public uint AllocCategory;
            public uint GPUOffset;
            public uint GPUDataStart;
            public uint GPUDataSize;
            public uint BaseAlignment;
            public uint DecompressedSize;
            public List<STextureInfo> TextureInfo = new List<STextureInfo>();
            public List<SCompressedBufferInfo> BufferInfo = new List<SCompressedBufferInfo>();
            public List<SCompressedBufferInfoV1> BufferInfoV1 = new List<SCompressedBufferInfoV1>();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class STextureInfo
        {
            public byte Index;
            public uint StartOffset;
            public uint EndOffset;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SCompressedBufferInfo
        {
            public uint Index;
            public uint StartOffset;
            public uint CompressedSize;
            public uint DestOffset;
            public uint DestSize;
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

            {  32, TextureFormat.RG8_UNORM },
            {  33, TextureFormat.RG8_SNORM },
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
