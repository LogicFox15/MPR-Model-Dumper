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
                    var gridX = Header.Dimensions[0].X;
                    var gridY = Header.Dimensions[0].Y;
                    var totalTileCount = gridX * gridY;

                    for (int i = 0; i < totalTileCount; i++)
                    {
                        TerrainClipMapTile tile = new TerrainClipMapTile();

                        foreach (var chan in Header.TerrainClipMapChannels)
                        {
                            if (chan.Active)
                            {
                                TerrainTextureChannelData chanData = new TerrainTextureChannelData();
                                chanData.Data = reader.ReadBytes((int)chan.DataSize);
                                chanData.Format = chan.FormatType;
                                chanData.ChannelDesc = chan;

                                tile.Channels.Add(chanData);
                            }
                        }
                        tiles.Add(tile);
                    }
                    break;
            }
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
            // A single 96x96 cell contains multiple channels (e.g., Albedo, Normal, Height)
            public List<TerrainTextureChannelData> Channels { get; private set; } = new List<TerrainTextureChannelData>();
        }

        public class TerrainTextureChannelData
        {
            public byte[] Data;
            public TerrainChannelDescription ChannelDesc;
            public byte Format;
            // Formats:
            // Format 1/3: 16-byte blocks (BC3 / BC5)
            // Format 2: 8-byte blocks (BC1 / DXT1)
        }
    }
}