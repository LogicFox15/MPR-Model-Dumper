using AvaloniaToolbox.Core.IO;
using DKCTF;
using ImageLibrary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using static DKCTF.TXTR;

namespace DKCTF
{
    /// <summary>
    /// Represents a texture file format.
    /// </summary>
    internal class LTPB : FileForm
    {
        public LightProbeBundleHeader bundleHeader;
        public CChunkDescriptor PTEX;
        public List<LightProbeBundle> lightProbeBundles = new List<LightProbeBundle>();

        public SLightProbeMetaData Meta;

        public LTPB() { }

        public LTPB(System.IO.Stream stream) : base(stream)
        {
        }

        public override void Read(FileReader reader)
        {
            // Because LTPB overrides FileForm.Read(), the base implementation
            // never gets a chance to read the META footer. Read it explicitly.
            long endPos = ReadMetaFooter(reader);

            // Restore the byte order selected by FileForm.
            reader.SetByteOrder(!IsLittleEndian);

            lightProbeBundles.Clear();

            // Read the bundle header chunk. Its payload is consumed by
            // LightProbeBundleHeader.Read(), so the next descriptor is PTEX.
            bundleHeader = LightProbeBundleHeader.Read(reader);

            if (reader.Position + Marshal.SizeOf<CChunkDescriptor>() > endPos)
                throw new InvalidDataException("LTPB PTEX descriptor lies beyond the file.");
            PTEX = reader.ReadStruct<CChunkDescriptor>();

            // The LTPB metadata supplies separate offsets for the per-texture
            // metadata and the embedded TXTR forms.
            if (Meta == null)
                return;

            int metaCount = Meta.metaOffsets?.Length ?? 0;
            int textureCount = Meta.textureOffsets?.Length ?? 0;

            if (metaCount != textureCount)
            {
                Console.WriteLine(
                    $"Warning: LTPB metadata/texture offset counts differ ({metaCount} vs {textureCount}).");
            }

            int bundleCount = Math.Min(metaCount, textureCount);

            for (int i = 0; i < bundleCount; i++)
            {
                // TXTR.ReadEmbedded() operates on the same FileReader and may
                // change its byte order while parsing the embedded RFRM.
                // Always restore the LTPB file byte order before starting the
                // next bundle.
                reader.SetByteOrder(!IsLittleEndian);

                lightProbeBundles.Add(LightProbeBundle.Read(reader, Meta.metaOffsets[i], Meta.textureOffsets[i]));

                Console.WriteLine("Added bundle");
            }
        }


        public override void ReadMetaData(FileReader reader, CFormDescriptor pakVersion)
        {
            Meta = new SLightProbeMetaData();
            Meta.unk1 = reader.ReadUInt32();
            Meta.unk2 = reader.ReadUInt32();
            Meta.metaOffsetCount = reader.ReadUInt32();
            if(Meta.metaOffsetCount > 0)
            {
                Meta.metaOffsets = reader.ReadUInt64s((int)Meta.metaOffsetCount);
            }
            Meta.textureOffsetCount = reader.ReadUInt32();
            if (Meta.textureOffsetCount > 0)
            {
                Meta.textureOffsets = reader.ReadUInt64s((int)Meta.textureOffsetCount);
            }
        }

        public override void WriteMetaData(FileWriter writer, CFormDescriptor pakVersion)
        {
            if (Meta == null)
                throw new InvalidOperationException(
                    "Cannot write LTPB metadata because Meta is null.");

            writer.Write(Meta.unk1);
            writer.Write(Meta.unk2);

            uint metaCount = (uint)(Meta.metaOffsets?.Length ?? 0);
            writer.Write(metaCount);

            if (Meta.metaOffsets != null)
            {
                foreach (ulong offset in Meta.metaOffsets)
                    writer.Write(offset);
            }

            uint textureCount = (uint)(Meta.textureOffsets?.Length ?? 0);
            writer.Write(textureCount);

            if (Meta.textureOffsets != null)
            {
                foreach (ulong offset in Meta.textureOffsets)
                    writer.Write(offset);
            }
        }

        public class LightProbeBundleHeader
        {
            public CChunkDescriptor chunkDescriptor;
            public uint unk1;
            public uint unk2;
            public Vector3 gridSpacing;
            public CBakedLightingUniformProbeGridIndex minIndex;
            public CBakedLightingUniformProbeGridIndex maxIndex; // inclusive

            public static LightProbeBundleHeader Read(FileReader br)
            {
                return new LightProbeBundleHeader
                {
                    chunkDescriptor = br.ReadStruct<CChunkDescriptor>(),
                    unk1 = br.ReadUInt32(),
                    unk2 = br.ReadUInt32(),
                    gridSpacing = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                    minIndex = CBakedLightingUniformProbeGridIndex.Read(br),
                    maxIndex = CBakedLightingUniformProbeGridIndex.Read(br)
                };
            }
        }

        public class CBakedLightingUniformProbeGridIndex 
        {
            public short x;
            public short y;
            public short z;

            public static CBakedLightingUniformProbeGridIndex Read(FileReader br)
            {
                return new CBakedLightingUniformProbeGridIndex
                {
                    x = br.ReadInt16(),
                    y = br.ReadInt16(),
                    z = br.ReadInt16()
                };
            }
        }

        public class LightProbeBundle
        {
            public TXTR texture;
            public Vector3 tilePosition;
            public uint layer;

            public static LightProbeBundle Read( FileReader reader, ulong metaOffset, ulong textureOffset)
            {
                EnsureOffset(reader, metaOffset, "LTPB metadata");
                EnsureOffset(reader, textureOffset, "LTPB texture");

                LightProbeBundle bundle = new LightProbeBundle();

                // The LTPB metadata offset points to the embedded TXTR metadata,
                // followed by the probe's integer grid/tile position.
                reader.SeekBegin((long)metaOffset);
                Console.WriteLine("Bundle metadata position: " + reader.BaseStream.Position.ToString("X8"));

                bundle.texture = new TXTR();

                ReadEmbeddedTextureMetaData(reader, bundle.texture);

                bundle.tilePosition = new Vector3(
                    reader.ReadInt32(),
                    reader.ReadInt32(),
                    reader.ReadInt32());

                bundle.layer = reader.ReadUInt32();

                reader.SeekBegin((long)textureOffset);
                bundle.texture.ReadEmbedded(reader);

                return bundle;
            }

            private static void EnsureOffset(FileReader reader, ulong offset, string description)
            {
                if (offset > long.MaxValue)
                    throw new InvalidDataException(
                        $"{description} offset 0x{offset:X} is outside the supported range.");

                long position = (long)offset;

                if (position < 0 || position >= reader.BaseStream.Length)
                {
                    throw new InvalidDataException(
                        $"{description} offset 0x{offset:X} is outside the LTPB stream.");
                }
            }

            private static void ReadEmbeddedTextureMetaData(FileReader reader, TXTR texture)
            {
                texture.Meta = new TXTR.STextureMetaData();

                // LTPB embeds the real STextureMetaData structure directly.
                // Unlike the PAK TXTR metadata path, there is no extra MPR uint here.

                texture.Meta.Unknown = reader.ReadUInt32();
                texture.Meta.Unknown2 = reader.ReadUInt32();
                texture.Meta.AllocCategory = reader.ReadUInt32();
                texture.Meta.GPUOffset = reader.ReadUInt32();
                texture.Meta.BaseAlignment = reader.ReadUInt32();
                texture.Meta.DecompressedSize = reader.ReadUInt32();

                uint infoCount = reader.ReadUInt32();

                texture.Meta.TextureInfo.Clear();

                for (uint i = 0; i < infoCount; i++)
                {
                    texture.Meta.TextureInfo.Add(new TXTR.STextureInfo
                    {
                        Index = reader.ReadByte(),
                        StartOffset = reader.ReadUInt32(),
                        EndOffset = reader.ReadUInt32()
                    });
                }

                uint bufferCount = reader.ReadUInt32();

                texture.Meta.BufferInfo.Clear();

                for (uint i = 0; i < bufferCount; i++)
                {
                    texture.Meta.BufferInfo.Add(new TXTR.SCompressedBufferInfo
                    {
                        Index = reader.ReadUInt32(),
                        StartOffset = reader.ReadUInt32(),
                        CompressedSize = reader.ReadUInt32(),
                        DestOffset = reader.ReadUInt32(),
                        DestSize = reader.ReadUInt32()
                    });
                }
            }
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SLightProbeMetaData
        {
            public uint unk1;
            public uint unk2;
            public uint metaOffsetCount;
            public ulong[] metaOffsets;
            public uint textureOffsetCount;
            public ulong[] textureOffsets;
        }


    }
}
