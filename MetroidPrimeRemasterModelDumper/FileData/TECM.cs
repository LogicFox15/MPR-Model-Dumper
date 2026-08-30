using AvaloniaToolbox.Core.IO;
using IONET.Collada.FX.Texturing;
using MetroidPrimeRemasterModelDumper;
using RetroStudioPlugin.Files.FileData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static DKCTF.TERR;
using static System.Runtime.InteropServices.JavaScript.JSType;
#nullable disable

namespace DKCTF
{
    /// <summary>
    /// Represents a model file format for loading mesh and material data.
    /// </summary>
    public class TECM : FileForm
    {
        public TerrainClipMapHeader Header { get; private set; }
        public List<TerrainClipMapTile> tiles { get; private set; } = new List<TerrainClipMapTile>();

        public TECM() { }

        public TECM(System.IO.Stream stream) : base(stream)
        {
        }

        public override void ReadMetaData(FileReader reader, CFormDescriptor pakVersion)
        {

        }

        public override void WriteMetaData(FileWriter writer, CFormDescriptor pakVersion)
        {

        }

        public override void ReadChunk(FileReader reader, CChunkDescriptor chunk)
        {
            Console.WriteLine("Chunk Type: " + chunk.ChunkType);
            Console.WriteLine("Chunk Size: " + chunk.DataSize.ToString("X8"));

            switch (chunk.ChunkType)
            {
                case "CHAN":
                    Header = new TerrainClipMapHeader(reader);
                    break;
                case "DATA":
                    // Everything here is completely experimental. It does not accurately reflect the exact steps taken by the game. 
                    var totalTileCount = Header.Dimensions[0].X * Header.Dimensions[0].Y;
                    for (int i = 0; i < totalTileCount; i++)
                    {
                        foreach (var chan in Header.TerrainClipMapChannels)
                        {
                            if (chan.Active)
                            {
                                TerrainClipMapTile tile = new TerrainClipMapTile();
                                tile.data = reader.ReadBytes((int)chan.DataSize);
                                tile.ChannelDesc = chan;
                                tiles.Add(tile);
                            }
                        }
                    }
                    break;
            }

            //BuildTiles(PrimaryRecord);
        }

        public class TerrainClipMapHeader
        {
            public byte ConfigurationFlag;      // Stored value in file is 0.
            public uint Unk1;
            public uint Unk2;
            public uint TileDimensions;
            public uint IndexCount;

            public List<Vector2> Dimensions = new List<Vector2>();
            public List<uint> LODDataSize = new List<uint>();
            public byte[] EmbeddedDataBlob;

            public List<TerrainChannelDescription> TerrainClipMapChannels = new();

            public TerrainClipMapHeader(FileReader reader)
            {
                // Header fields
                ConfigurationFlag = reader.ReadByte();
                Unk1 = reader.ReadUInt32();
                Unk2 = reader.ReadUInt32();
                TileDimensions = reader.ReadUInt32();
                IndexCount = reader.ReadUInt32();

                uint count1 = reader.ReadUInt32();
                for (int i = 0; i < count1; i++)
                    Dimensions.Add(new Vector2(reader.ReadUInt32(), reader.ReadUInt32()));

                uint count2 = reader.ReadUInt32();
                for (int i = 0; i < count2; i++)
                    LODDataSize.Add(reader.ReadUInt32());

                uint bufferSize = reader.ReadUInt32();
                EmbeddedDataBlob = reader.ReadBytes((int)bufferSize);

                uint count3 = reader.ReadUInt32();
                for (int i = 0; i < count3; i++)
                    TerrainClipMapChannels.Add(new TerrainChannelDescription(reader));
            }
        }

        public class TerrainClipMapTile
        {
            public byte[] data;
            public TerrainChannelDescription ChannelDesc;
        }
    }

    public static class DdsExporter
    {
        /// <summary>
        /// Wraps raw DXT1/BC1 block data in a DDS header and saves it to a file.
        /// </summary>
        /// <param name="filePath">The output file path (e.g., "output.dds")</param>
        /// <param name="rawDxt1Data">The raw compressed byte array</param>
        /// <param name="width">The width of the texture in pixels</param>
        /// <param name="height">The height of the texture in pixels</param>
        public static void ExportDxt1ToDds(string filePath, byte[] rawDxt1Data, int width, int height)
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                // 1. Magic Number: "DDS " (0x20534444)
                writer.Write(0x20534444);

                // 2. DDS_HEADER (124 bytes)
                writer.Write(124); // dwSize
                writer.Write(0x00081007); // dwFlags (CAPS | HEIGHT | WIDTH | PIXELFORMAT | LINEARSIZE)
                writer.Write(height); // dwHeight
                writer.Write(width); // dwWidth

                // dwPitchOrLinearSize (Calculate total bytes for the top-level image)
                int linearSize = Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * 8;
                writer.Write(linearSize);

                writer.Write(0); // dwDepth
                writer.Write(1); // dwMipMapCount (Assuming 1 mipmap level for a raw dump)

                // dwReserved1 (11 empty DWORDs)
                for (int i = 0; i < 11; i++) writer.Write(0);

                // DDS_PIXELFORMAT structure (32 bytes)
                writer.Write(32); // dwSize
                writer.Write(0x4); // dwFlags (DDPF_FOURCC)
                writer.Write(0x31545844); // dwFourCC ("DXT1")
                writer.Write(0); // dwRGBBitCount
                writer.Write(0); // dwRBitMask
                writer.Write(0); // dwGBitMask
                writer.Write(0); // dwBBitMask
                writer.Write(0); // dwABitMask

                // DDSCAPS structure
                writer.Write(0x1000); // dwCaps (DDSCAPS_TEXTURE)
                writer.Write(0); // dwCaps2
                writer.Write(0); // dwCaps3
                writer.Write(0); // dwCaps4

                writer.Write(0); // dwReserved2

                // 3. Write the raw texture payload
                writer.Write(rawDxt1Data);
            }

            Console.WriteLine($"Successfully wrote {width}x{height} DDS image to {filePath}");
        }
    }
}