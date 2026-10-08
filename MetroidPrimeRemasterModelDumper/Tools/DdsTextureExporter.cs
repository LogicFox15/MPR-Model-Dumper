using ImageLibrary;
using ImageLibrary.PlatformSwizzle.Algorithms.Switch;
using System;
using System.Collections.Generic;
using System.IO;

namespace DKCTF
{
    /// <summary>
    /// Lossless DDS exporter for Metroid Prime 4 TXTR files.
    ///
    /// This exporter:
    ///   - deswizzles Switch/Tegra texture data;
    ///   - preserves the original compressed/uncompressed format;
    ///   - writes DX10 DDS headers for formats that need DXGI metadata;
    ///   - preserves array/cubemap layers in the full DDS;
    ///   - can additionally write each physical layer as its own DDS.
    ///
    /// No decode/re-encode step is performed, so BC6H SF16 data stays bit-for-bit
    /// identical to the TXTR texture payload after deswizzling.
    /// </summary>
    internal static class DdsTextureExporter
    {
        private const uint DDS_MAGIC = 0x20534444;       // "DDS "
        private const uint FOURCC_DX10 = 0x30315844;     // "DX10"

        private const uint DDSD_CAPS = 0x00000001;
        private const uint DDSD_HEIGHT = 0x00000002;
        private const uint DDSD_WIDTH = 0x00000004;
        private const uint DDSD_PITCH = 0x00000008;
        private const uint DDSD_PIXELFORMAT = 0x00001000;
        private const uint DDSD_MIPMAPCOUNT = 0x00020000;
        private const uint DDSD_LINEARSIZE = 0x00080000;
        private const uint DDSD_DEPTH = 0x00800000;

        private const uint DDPF_FOURCC = 0x00000004;
        private const uint DDPF_RGB = 0x00000040;
        private const uint DDPF_ALPHAPIXELS = 0x00000001;
        private const uint DDPF_LUMINANCE = 0x00020000;

        private const uint FOURCC_DXT1 = 0x31545844;     // "DXT1"
        private const uint FOURCC_DXT3 = 0x33545844;     // "DXT3"
        private const uint FOURCC_DXT5 = 0x35545844;     // "DXT5"
        private const uint FOURCC_BC4U = 0x55344342;     // "BC4U"
        private const uint FOURCC_BC4S = 0x53344342;     // "BC4S"
        private const uint FOURCC_BC5U = 0x55354342;     // "BC5U"
        private const uint FOURCC_BC5S = 0x53354342;     // "BC5S"

        private const uint DDSCAPS_COMPLEX = 0x00000008;
        private const uint DDSCAPS_TEXTURE = 0x00001000;
        private const uint DDSCAPS_MIPMAP = 0x00400000;

        private const uint DDSCAPS2_CUBEMAP = 0x00000200;
        private const uint DDSCAPS2_CUBEMAP_POSITIVEX = 0x00000400;
        private const uint DDSCAPS2_CUBEMAP_NEGATIVEX = 0x00000800;
        private const uint DDSCAPS2_CUBEMAP_POSITIVEY = 0x00001000;
        private const uint DDSCAPS2_CUBEMAP_NEGATIVEY = 0x00002000;
        private const uint DDSCAPS2_CUBEMAP_POSITIVEZ = 0x00004000;
        private const uint DDSCAPS2_CUBEMAP_NEGATIVEZ = 0x00008000;
        private const uint DDSCAPS2_CUBEMAP_ALLFACES =
            DDSCAPS2_CUBEMAP |
            DDSCAPS2_CUBEMAP_POSITIVEX |
            DDSCAPS2_CUBEMAP_NEGATIVEX |
            DDSCAPS2_CUBEMAP_POSITIVEY |
            DDSCAPS2_CUBEMAP_NEGATIVEY |
            DDSCAPS2_CUBEMAP_POSITIVEZ |
            DDSCAPS2_CUBEMAP_NEGATIVEZ;

        private const uint DDS_RESOURCE_MISC_TEXTURECUBE = 0x4;

        private const uint DDS_DIMENSION_TEXTURE1D = 2;
        private const uint DDS_DIMENSION_TEXTURE2D = 3;
        private const uint DDS_DIMENSION_TEXTURE3D = 4;

        private enum MprTextureType : uint
        {
            D1 = 0,
            D2 = 1,
            D3 = 2,
            Cube = 3,
            D1Array = 4,
            D2Array = 5,
            D2Multisample = 6,
            D2MultisampleArray = 7,
            CubeArray = 8,
        }

        private readonly struct FormatInfo
        {
            public uint DxgiFormat { get; }
            public uint BlockWidth { get; }
            public uint BlockHeight { get; }
            public uint BlockDepth { get; }
            public uint BytesPerBlockOrPixel { get; }
            public bool Compressed { get; }
            public bool UseLegacyHeader { get; }
            public uint LegacyFourCC { get; }
            public bool LegacyRgb24 { get; }

            public FormatInfo(
                uint dxgiFormat,
                uint blockWidth,
                uint blockHeight,
                uint blockDepth,
                uint bytesPerBlockOrPixel,
                bool compressed,
                bool useLegacyHeader = false,
                uint legacyFourCC = 0,
                bool legacyRgb24 = false)
            {
                DxgiFormat = dxgiFormat;
                BlockWidth = blockWidth;
                BlockHeight = blockHeight;
                BlockDepth = blockDepth;
                BytesPerBlockOrPixel = bytesPerBlockOrPixel;
                Compressed = compressed;
                UseLegacyHeader = useLegacyHeader;
                LegacyFourCC = legacyFourCC;
                LegacyRgb24 = legacyRgb24;
            }
        }

        private readonly struct TextureShape
        {
            public bool Is3D { get; }
            public bool Is1D { get; }
            public bool IsCube { get; }
            public uint PhysicalLayerCount { get; }
            public uint DdsArrayCount { get; }
            public uint Depth { get; }

            public TextureShape(
                bool is3D,
                bool is1D,
                bool isCube,
                uint physicalLayerCount,
                uint ddsArrayCount,
                uint depth)
            {
                Is3D = is3D;
                Is1D = is1D;
                IsCube = isCube;
                PhysicalLayerCount = physicalLayerCount;
                DdsArrayCount = ddsArrayCount;
                Depth = depth;
            }
        }

        /// <summary>
        /// Writes the complete texture to <paramref name="path"/>.
        /// When splitLayers is true, additional files are created for each
        /// physical array/cubemap face.
        /// </summary>
        internal static void Export(TXTR texture, string path, bool splitLayers = true)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));

            if (texture.TextureHeader == null)
                throw new InvalidDataException("TXTR does not contain a HEAD chunk.");

            if (texture.BufferData == null || texture.BufferData.Length == 0)
                throw new InvalidDataException("TXTR does not contain decompressed GPU data.");

            if (texture.MipSizes == null || texture.MipSizes.Length == 0)
                throw new InvalidDataException("TXTR does not contain mip sizes.");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            FormatInfo format = GetFormatInfo(texture.TextureHeader.Format);
            TextureShape shape = GetTextureShape(texture.TextureHeader);

            uint mipCount = checked((uint)texture.MipSizes.Length);

            // TXTR GPU data is still Switch/Tegra swizzled. Convert it to the
            // normal array/mip surface order used by DDS.
            byte[] linearData = TextureConverter.Deswizzle(
                texture.TextureHeader.Width,
                texture.TextureHeader.Height,
                shape.Depth,
                shape.PhysicalLayerCount,
                mipCount,
                (format.BlockWidth, format.BlockHeight, format.BlockDepth),
                format.BytesPerBlockOrPixel,
                // Prime 4 no longer stores TileMode in STextureHeader.
                // PlatformSwizzleSwitch/TextureConverter use TileMode = 0
                // for these textures; the current TextureConverter implementation
                // also derives the block height from the texture dimensions.
                0,
                texture.BufferData,
                target: 1,
                is_orin: false);

            ulong logicalSize = GetLogicalLinearSize(texture.MipSizes);

            if ((ulong)linearData.Length < logicalSize)
            {
                throw new InvalidDataException(
                    $"Deswizzled TXTR is smaller than the logical mip payload. " +
                    $"Expected at least {logicalSize} bytes, got {linearData.Length}.");
            }

            if (logicalSize > int.MaxValue)
                throw new InvalidDataException("Texture is too large for the current DDS exporter.");

            // A deswizzler may return trailing alignment bytes. Those do not
            // belong in the DDS payload because HEAD.MipSizes describes the
            // actual texture surfaces.
            if ((ulong)linearData.Length != logicalSize)
                Array.Resize(ref linearData, checked((int)logicalSize));

            // The Prime 4/Tegra deswizzler returns non-3D arrays in mip-major order:
            //
            //   Mip0: Layer0 Layer1 Layer2 ...
            //   Mip1: Layer0 Layer1 Layer2 ...
            //
            // DDS stores array textures in layer-major order:
            //
            //   Layer0: Mip0 Mip1 Mip2 ...
            //   Layer1: Mip0 Mip1 Mip2 ...
            //
            // Reorder only non-3D arrays before writing DDS.
            if (!shape.Is3D && shape.PhysicalLayerCount > 1)
            {
                linearData = ReorderMipMajorToLayerMajor(
                    linearData,
                    texture.MipSizes,
                    shape.PhysicalLayerCount);
            }

            uint firstMipPitchOrLinearSize = CalculateDdsPitchOrLinearSize(
                texture.TextureHeader.Width,
                texture.TextureHeader.Height,
                shape.Depth,
                format,
                shape.Is3D);

            WriteDds(
                path,
                texture.TextureHeader.Width,
                texture.TextureHeader.Height,
                shape.Depth,
                mipCount,
                shape.DdsArrayCount,
                shape.IsCube,
                shape.Is1D,
                shape.Is3D,
                shape.PhysicalLayerCount,
                format,
                firstMipPitchOrLinearSize,
                linearData);

            if (!splitLayers || shape.Is3D || shape.PhysicalLayerCount <= 1)
                return;

            string basePath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(path))!,
                Path.GetFileNameWithoutExtension(path));

            for (uint layer = 0; layer < shape.PhysicalLayerCount; layer++)
            {
                byte[] layerData = ExtractLayer(
                    linearData,
                    texture.MipSizes,
                    shape.PhysicalLayerCount,
                    layer);

                string suffix;

                if (shape.IsCube)
                {
                    uint cubeIndex = layer / 6;
                    uint faceIndex = layer % 6;
                    suffix = $"_cube{cubeIndex:D2}_face{faceIndex:D2}";
                }
                else
                {
                    suffix = $"_layer{layer:D2}";
                }

                string layerPath = basePath + suffix + ".dds";

                WriteDds(
                    layerPath,
                    texture.TextureHeader.Width,
                    texture.TextureHeader.Height,
                    1,
                    mipCount,
                    1,
                    false,
                    shape.Is1D,
                    false,
                    1,
                    format,
                    CalculateDdsPitchOrLinearSize(
                        texture.TextureHeader.Width,
                        texture.TextureHeader.Height,
                        1,
                        format,
                        false),
                    layerData);
            }
        }

        /// <summary>
        /// Returns the Prime 4 texture type and converts the TXTR Depth field into
        /// the correct DDS concepts:
        ///   - for non-3D array textures, TXTR Depth is a layer count;
        ///   - for 3D textures, TXTR Depth is the true volume depth;
///   - plain cubemaps are always six physical faces in Prime 4.
        /// </summary>
        private static TextureShape GetTextureShape(TXTR.STextureHeader header)
        {
            MprTextureType type = header.Type switch
            {
                0 => MprTextureType.D1,
                1 => MprTextureType.D2,
                2 => MprTextureType.D3,
                3 => MprTextureType.Cube,
                4 => MprTextureType.D1Array,
                5 => MprTextureType.D2Array,
                6 => MprTextureType.D2Multisample,
                7 => MprTextureType.D2MultisampleArray,
                8 => MprTextureType.CubeArray,
                _ => throw new InvalidDataException(
                    $"Unknown MPR TXTR texture type {header.Type}.")
            };

            if (type == MprTextureType.D3)
            {
                uint depth = Math.Max(header.Depth, 1);

                return new TextureShape(
                    is3D: true,
                    is1D: false,
                    isCube: false,
                    physicalLayerCount: 1,
                    ddsArrayCount: 1,
                    depth: depth);
            }

            bool isCube =
                type == MprTextureType.Cube ||
                type == MprTextureType.CubeArray;

            bool is1D =
                type == MprTextureType.D1 ||
                type == MprTextureType.D1Array;

            // Prime 4's TXTR header does not use Depth as the six-face count
            // for a plain cubemap. Type == Cube implies exactly six physical
            // faces, matching the existing Prime 4 PNG path.
            uint physicalLayers;

            if (type == MprTextureType.Cube)
            {
                physicalLayers = 6;
            }
            else
            {
                physicalLayers = Math.Max(header.Depth, 1);

                if (type == MprTextureType.CubeArray && physicalLayers % 6 != 0)
                {
                    throw new InvalidDataException(
                        $"Cubemap-array TXTR reports {physicalLayers} physical layers; " +
                        "the layer count must be divisible by 6.");
                }
            }

            // DDS/DX10 arraySize is the number of cubes for cubemaps and the
            // number of physical layers for ordinary array textures.
            uint ddsArrayCount = isCube
                ? physicalLayers / 6
                : physicalLayers;

            return new TextureShape(
                is3D: false,
                is1D: is1D,
                isCube: isCube,
                physicalLayerCount: physicalLayers,
                ddsArrayCount: ddsArrayCount,
                depth: 1);
        }

        /// <summary>
        /// MPR format IDs are NOT the same numeric values as DXGI_FORMAT.
        /// TXTR.FormatList already maps the MPR IDs to ImageLibrary's
        /// TextureFormat enum, whose values match DXGI_FORMAT.
        ///
        /// Two IDs are intentionally handled separately:
        ///   11 is RGB8_UNORM. It has no DXGI enum, so it uses a legacy
        ///      24-bit R8G8B8 DDS header.
        ///   52 is the "None"/unknown format and is rejected.
        /// </summary>
        private static FormatInfo GetFormatInfo(uint mprFormat)
        {
            // Prime 4 TXTR format IDs are game-specific. The values below are the
            // corresponding DXGI values written into DDS_HEADER_DXT10.
            uint dxgi = mprFormat switch
            {
                0 => 61,
                1 => 63,
                2 => 62,
                3 => 64,
                4 => 56,
                5 => 58,
                6 => 57,
                7 => 59,
                8 => 54,
                9 => 42,
                10 => 43,
                11 => 0,    // RGB8_UNORM: legacy DDS R8G8B8 (no DXGI equivalent)
                12 => 28,
                13 => 29,
                14 => 10,
                15 => 2,
                16 => 55,
                17 => 55,
                18 => 45,
                19 => 40,
                20 => 71,
                21 => 72,
                22 => 74,
                23 => 75,
                24 => 77,
                25 => 78,
                26 => 80,
                27 => 81,
                28 => 83,
                29 => 84,
                30 => 26,
                31 => 41,
                32 => 49,
                33 => 51,
                34 => 50,
                35 => 52,
                36 => 34,
                37 => 35,
                38 => 37,
                39 => 36,
                40 => 38,
                41 => 24,
                42 => 25,
                43 => 17,
                44 => 18,
                45 => 16,
                46 => 11,
                47 => 13,
                48 => 12,
                49 => 14,
                50 => 3,
                51 => 4,
                52 => throw new NotSupportedException("Prime 4 TXTR texture format 52 is the None/unknown format."),
                53 => 134,
                54 => 138,
                55 => 142,
                56 => 146,
                57 => 150,
                58 => 154,
                59 => 158,
                60 => 162,
                61 => 166,
                62 => 170,
                63 => 174,
                64 => 178,
                65 => 182,
                66 => 186,
                67 => 135,
                68 => 139,
                69 => 143,
                70 => 147,
                71 => 151,
                72 => 155,
                73 => 159,
                74 => 163,
                75 => 167,
                76 => 171,
                77 => 175,
                78 => 179,
                79 => 183,
                80 => 187,
                81 => 95,
                82 => 96,
                83 => 98,
                84 => 99,
                _ => throw new NotSupportedException($"Prime 4 TXTR texture format {mprFormat} is not supported by the DDS exporter.")
            };

            uint bytesPerUnit = GetBytesPerBlockOrPixel(mprFormat);
            bool compressed = IsBlockCompressed(mprFormat);
            (uint blockWidth, uint blockHeight) = GetBlockSize(mprFormat);

            bool useLegacyHeader = mprFormat switch
            {
                0 or 11 or 12 or 20 or 22 or 24 or 26 or 27 or 28 or 29 => true,
                _ => false
            };

            uint legacyFourCC = mprFormat switch
            {
                20 => FOURCC_DXT1,
                22 => FOURCC_DXT3,
                24 => FOURCC_DXT5,
                26 => FOURCC_BC4U,
                27 => FOURCC_BC4S,
                28 => FOURCC_BC5U,
                29 => FOURCC_BC5S,
                _ => 0
            };

            // Legacy DDS has no sRGB representation for BC1/2/3.
            if (mprFormat is 21 or 23 or 25)
                useLegacyHeader = false;

            return new FormatInfo(
                dxgi,
                blockWidth,
                blockHeight,
                1,
                bytesPerUnit,
                compressed,
                useLegacyHeader,
                legacyFourCC,
                legacyRgb24: mprFormat == 11);
        }

        private static bool IsBlockCompressed(uint format)
        {
            return
                (format >= 20 && format <= 29) ||
                (format >= 53 && format <= 80) ||
                (format >= 81 && format <= 84);
        }

        private static (uint width, uint height) GetBlockSize(uint format)
        {
            if (format >= 53 && format <= 80)
            {
                return format switch
                {
                    53 or 67 => (4, 4),
                    54 or 68 => (5, 4),
                    55 or 69 => (5, 5),
                    56 or 70 => (6, 5),
                    57 or 71 => (6, 6),
                    58 or 72 => (8, 5),
                    59 or 73 => (8, 6),
                    60 or 74 => (8, 8),
                    61 or 75 => (10, 5),
                    62 or 76 => (10, 6),
                    63 or 77 => (10, 8),
                    64 or 78 => (10, 10),
                    65 or 79 => (12, 10),
                    66 or 80 => (12, 12),
                    _ => throw new InvalidDataException()
                };
            }

            if ((format >= 20 && format <= 29) ||
                (format >= 81 && format <= 84))
            {
                return (4, 4);
            }

            return (1, 1);
        }

        private static uint GetBytesPerBlockOrPixel(uint format)
        {
            return format switch
            {
                0 or 1 or 2 or 3 => 1,

                4 or 5 or 6 or 7 or 8 => 2,

                9 or 10 or 12 or 13 => 4,
                11 => 3,
                14 => 8,
                15 => 16,

                16 or 17 => 2,
                18 or 19 => 4,

                20 or 21 => 8,
                22 or 23 or 24 or 25 => 16,
                26 or 27 => 8,
                28 or 29 => 16,

                30 or 31 => 4,

                32 or 33 or 34 or 35 => 2,
                36 or 37 or 38 or 39 or 40 => 4,

                41 or 42 => 4,
                43 or 44 or 45 => 8,

                46 or 47 or 48 or 49 => 8,
                50 or 51 => 16,

                // 52 is handled as unsupported above.

                // ASTC: every block is 128 bits = 16 bytes.
                >= 53 and <= 80 => 16,

                // BC6H / BC7: every block is 128 bits = 16 bytes.
                81 or 82 or 83 or 84 => 16,

                _ => throw new NotSupportedException(
                    $"No storage-size rule exists for MPR texture format {format}.")
            };
        }

        private static ulong GetLogicalLinearSize(uint[] mipSizes)
        {
            ulong total = 0;

            foreach (uint mipSize in mipSizes)
                total += mipSize;

            return total;
        }

        /// <summary>
        /// Converts the MPR/Tegra non-3D texture layout from mip-major to
        /// the layer-major layout required by DDS.
        ///
        /// Source:
        ///   Mip0: L0 L1 L2 ...
        ///   Mip1: L0 L1 L2 ...
        ///
        /// Destination:
        ///   L0: Mip0 Mip1 ...
        ///   L1: Mip0 Mip1 ...
        /// </summary>
        private static byte[] ReorderMipMajorToLayerMajor(
            byte[] mipMajorData,
            uint[] mipSizes,
            uint physicalLayerCount)
        {
            if (physicalLayerCount <= 1)
                return mipMajorData;

            ulong totalSize = 0;

            foreach (uint mipSize in mipSizes)
            {
                if (mipSize % physicalLayerCount != 0)
                {
                    throw new InvalidDataException(
                        $"Mip size {mipSize} is not divisible by the layer count " +
                        $"{physicalLayerCount}.");
                }

                totalSize += mipSize;
            }

            byte[] output = new byte[checked((int)totalSize)];

            // Destination is layer-major. Every layer has the complete mip
            // chain, so calculate its stride once.
            ulong layerStride = 0;
            foreach (uint mipSize in mipSizes)
                layerStride += mipSize / physicalLayerCount;

            ulong sourceOffset = 0;

            for (uint mip = 0; mip < mipSizes.Length; mip++)
            {
                uint mipSize = mipSizes[mip];
                uint perLayerSize = mipSize / physicalLayerCount;

                for (uint layer = 0; layer < physicalLayerCount; layer++)
                {
                    ulong source = sourceOffset + (ulong)layer * perLayerSize;
                    ulong destination =
                        (ulong)layer * layerStride +
                        GetMipOffsetWithinLayer(
                            mipSizes,
                            physicalLayerCount,
                            mip);

                    Buffer.BlockCopy(
                        mipMajorData,
                        checked((int)source),
                        output,
                        checked((int)destination),
                        checked((int)perLayerSize));
                }

                sourceOffset += mipSize;
            }

            return output;
        }

        /// <summary>
        /// Returns one physical layer's complete mip chain from layer-major
        /// DDS data.
        /// </summary>
        private static byte[] ExtractLayer(
            byte[] layerMajorData,
            uint[] mipSizes,
            uint physicalLayerCount,
            uint layerIndex)
        {
            if (physicalLayerCount == 0)
                throw new InvalidDataException("Invalid zero layer count.");

            if (layerIndex >= physicalLayerCount)
                throw new ArgumentOutOfRangeException(nameof(layerIndex));

            ulong layerStride = 0;
            foreach (uint mipSize in mipSizes)
            {
                if (mipSize % physicalLayerCount != 0)
                {
                    throw new InvalidDataException(
                        $"Mip size {mipSize} is not divisible by the layer count " +
                        $"{physicalLayerCount}.");
                }

                layerStride += mipSize / physicalLayerCount;
            }

            ulong layerOffset = layerStride * layerIndex;

            if (layerOffset + layerStride > (ulong)layerMajorData.Length)
            {
                throw new InvalidDataException(
                    $"Layer {layerIndex} falls outside the DDS data.");
            }

            byte[] result = new byte[checked((int)layerStride)];

            Buffer.BlockCopy(
                layerMajorData,
                checked((int)layerOffset),
                result,
                0,
                result.Length);

            return result;
        }

        private static uint GetMipOffsetWithinLayer(
            uint[] mipSizes,
            uint layerCount,
            uint mipIndex)
        {
            uint offset = 0;

            for (uint i = 0; i < mipIndex; i++)
            {
                if (mipSizes[i] % layerCount != 0)
                {
                    throw new InvalidDataException(
                        $"Mip size {mipSizes[i]} is not divisible by the layer count {layerCount}.");
                }

                offset = checked(offset + mipSizes[i] / layerCount);
            }

            return offset;
        }

        private static uint CalculateDdsPitchOrLinearSize(
            uint width,
            uint height,
            uint depth,
            FormatInfo format,
            bool is3D)
        {
            uint effectiveDepth = is3D ? Math.Max(depth, 1) : 1;

            if (!format.Compressed)
            {
                // For uncompressed textures this field is the row pitch.
                return checked(width * format.BytesPerBlockOrPixel);
            }

            uint blocksWide =
                Math.Max((width + format.BlockWidth - 1) / format.BlockWidth, 1);

            uint blocksHigh =
                Math.Max((height + format.BlockHeight - 1) / format.BlockHeight, 1);

            return checked(
                blocksWide *
                blocksHigh *
                format.BytesPerBlockOrPixel *
                effectiveDepth);
        }

        private static void WriteDds(
            string path,
            uint width,
            uint height,
            uint depth,
            uint mipCount,
            uint arrayCount,
            bool isCube,
            bool is1D,
            bool is3D,
            uint physicalLayerCount,
            FormatInfo format,
            uint pitchOrLinearSize,
            byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            // Legacy DDS cannot describe a texture array or a 1D array.
            // It can describe a normal 2D texture and a six-face cubemap.
            bool legacy =
                format.UseLegacyHeader &&
                !is3D &&
                (arrayCount == 1 || (isCube && physicalLayerCount == 6)) &&
                !is1D;

            if (format.LegacyRgb24 && !legacy)
            {
                throw new NotSupportedException(
                    "Prime 4 RGB8_UNORM can only be exported as a single 2D surface " +
                    "or a legacy six-face cubemap.");
            }

            using FileStream stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            using BinaryWriter writer = new BinaryWriter(
                stream,
                System.Text.Encoding.ASCII,
                leaveOpen: false);

            writer.Write(DDS_MAGIC);
            writer.Write(124u);

            uint headerFlags =
                DDSD_CAPS |
                DDSD_HEIGHT |
                DDSD_WIDTH |
                DDSD_PIXELFORMAT |
                (format.Compressed ? DDSD_LINEARSIZE : DDSD_PITCH);

            if (mipCount > 1)
                headerFlags |= DDSD_MIPMAPCOUNT;

            if (is3D)
                headerFlags |= DDSD_DEPTH;

            writer.Write(headerFlags);
            writer.Write(height);
            writer.Write(width);
            writer.Write(pitchOrLinearSize);
            writer.Write(is3D ? depth : 0u);
            writer.Write(mipCount);

            for (int i = 0; i < 11; i++)
                writer.Write(0u);

            // DDS_PIXELFORMAT
            writer.Write(32u);

            if (legacy)
            {
                if (format.Compressed)
                {
                    writer.Write(DDPF_FOURCC);
                    writer.Write(format.LegacyFourCC);
                    writer.Write(0u);
                    writer.Write(0u);
                    writer.Write(0u);
                    writer.Write(0u);
                    writer.Write(0u);
                }
                else if (format.LegacyRgb24)
                {
                    // MPR RGB8_UNORM (format 11) has no DXGI enum.
                    // Use a legacy 24-bit DDS pixel format and describe the
                    // byte order explicitly as R8G8B8.
                    writer.Write(DDPF_RGB);
                    writer.Write(0u);
                    writer.Write(24u);
                    writer.Write(0x000000FFu);
                    writer.Write(0x0000FF00u);
                    writer.Write(0x00FF0000u);
                    writer.Write(0u);
                }
                else if (format.DxgiFormat == 28) // R8G8B8A8_UNORM
                {
                    writer.Write(DDPF_RGB | DDPF_ALPHAPIXELS);
                    writer.Write(0u);
                    writer.Write(32u);
                    writer.Write(0x000000FFu);
                    writer.Write(0x0000FF00u);
                    writer.Write(0x00FF0000u);
                    writer.Write(0xFF000000u);
                }
                else if (format.DxgiFormat == 61) // R8_UNORM
                {
                    writer.Write(DDPF_LUMINANCE);
                    writer.Write(0u);
                    writer.Write(8u);
                    writer.Write(0x000000FFu);
                    writer.Write(0u);
                    writer.Write(0u);
                    writer.Write(0u);
                }
                else
                {
                    // Should never happen because only the exact legacy cases
                    // above set UseLegacyHeader.
                    throw new InvalidDataException(
                        $"No legacy DDS header mapping exists for DXGI format {format.DxgiFormat}.");
                }

                uint caps1 = DDSCAPS_TEXTURE;

                if (mipCount > 1)
                    caps1 |= DDSCAPS_COMPLEX | DDSCAPS_MIPMAP;

                if (isCube)
                    caps1 |= DDSCAPS_COMPLEX;

                uint legacyCaps2 =
                    isCube
                        ? DDSCAPS2_CUBEMAP_ALLFACES
                        : 0u;

                writer.Write(caps1);
                writer.Write(legacyCaps2);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(0u);

                writer.Write(data);
                return;
            }

            // DX10 DDS path. This is required for:
            //   - BC6H
            //   - BC7
            //   - signed/float formats
            //   - sRGB BC1/2/3
            //   - texture arrays
            //   - cube arrays
            //   - 1D/3D textures
            writer.Write(DDPF_FOURCC);
            writer.Write(FOURCC_DX10);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);

            uint caps = DDSCAPS_TEXTURE;

            if (mipCount > 1)
                caps |= DDSCAPS_COMPLEX | DDSCAPS_MIPMAP;

            if (isCube)
                caps |= DDSCAPS_COMPLEX;

            uint dx10Caps2 = isCube ? DDSCAPS2_CUBEMAP_ALLFACES : 0u;

            writer.Write(caps);
            writer.Write(dx10Caps2);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);

            writer.Write(format.DxgiFormat);

            uint resourceDimension =
                is3D
                    ? DDS_DIMENSION_TEXTURE3D
                    : is1D
                        ? DDS_DIMENSION_TEXTURE1D
                        : DDS_DIMENSION_TEXTURE2D;

            writer.Write(resourceDimension);
            writer.Write(isCube ? DDS_RESOURCE_MISC_TEXTURECUBE : 0u);

            // For DX10 cubemaps this is the number of cubes.
            // For ordinary arrays it is the array element count.
            // For 3D textures it must be 1.
            writer.Write(
                is3D
                    ? 1u
                    : Math.Max(arrayCount, 1u));

            writer.Write(0u); // alpha mode / misc flags 2

            writer.Write(data);
        }
    }

    // Convenience method for calling the exporter directly from TXTR code.
    internal static class TxtrDdsExportExtensions
    {
        public static void ExportDDS(this TXTR texture, string path, bool splitLayers = true)
        {
            DdsTextureExporter.Export(texture, path, splitLayers);
        }
    }
}
