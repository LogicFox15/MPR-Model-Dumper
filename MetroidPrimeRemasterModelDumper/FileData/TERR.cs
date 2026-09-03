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
using static System.Runtime.InteropServices.JavaScript.JSType;
#nullable disable

namespace DKCTF
{
    /// <summary>
    /// Represents a model file format for loading mesh and material data.
    /// </summary>
    public class TERR : FileForm
    {
        public TerrainHeader Header { get; private set; }
        public List<TerrainDataRecord> DataRecords { get; private set; } = new List<TerrainDataRecord>();
        public TerrainDataRecord PrimaryRecord { get; private set; }
        public List<TerrainTile> tiles { get; private set; } = new List<TerrainTile>();

        public byte[] buffer;

        public TERR() { }

        public TERR(System.IO.Stream stream) : base(stream)
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
                    long start = reader.Position;
                    Header = new TerrainHeader(reader);
                    long consumed = reader.Position - start;
                    if (consumed > chunk.DataSize)
                        throw new InvalidDataException($"CHAN parser consumed {consumed:X} bytes, larger than chunk {chunk.DataSize:X}.");
                    break;

                case "DATA":

                    // Everything here is completely experimental. It does not accurately reflect the exact steps taken by the game. 

                    var fieldDimensions = (uint)Header.VertexStreamDescription.Dimensions[0].X * (uint)Header.VertexStreamDescription.Dimensions[0].Y;
                    // Get the side length (e.g., 96) for building the 2D grid
                    var tileSideLength = Header.VertexStreamDescription.TileDimensions;
                    // Get the total area (e.g., 9216) for iterating through the flat array
                    var totalTileVerts = tileSideLength * tileSideLength;

                    for (int i = 0; i < fieldDimensions; i++)
                    {
                        // Pass the side length, not the total area!
                        TerrainTile tile = new TerrainTile(tileSideLength);
                        tiles.Add(tile);
                    }

                    for (int j = 0; j < fieldDimensions; j++)
                    {
                        for (int i = 0; i < totalTileVerts; i++)
                        {
                            tiles[j].tileVerts[i].Position.Y = reader.ReadSingle();
                        }

                        // Still unsure. 1920 bytes of data.
                        var dat1 = reader.ReadBytes((int)Header.VertexStreamDescription.TerrainChannels[1].DataSize);

                        for (int i = 0; i < (Header.VertexStreamDescription.TerrainChannels[2].DataSize / 2); i++)
                        {
                            byte usage = reader.ReadByte();
                            tiles[j].textureSelect.Add(usage);
                            tiles[j].tileVerts[i].textureUsage = usage; // <-- Add this line to bind it to the vertex
                            byte unk = reader.ReadByte();
                            tiles[j].unk1.Add(unk);
                            tiles[j].tileVerts[i].unknownByte = unk; // <-- Add this line to bind it to the vertex
                        }

                        // Still unsure. When divided by 2 bytes, the size is 9216.
                        for (int i = 0; i < (Header.VertexStreamDescription.TerrainChannels[3].DataSize / 2); i++)
                        {
                            tiles[j].halfs2.Add(reader.ReadUInt16());
                        }
                    }

                    break;
            }

            //BuildTiles(PrimaryRecord);
        }

        public class TerrainHeader
        {
            public byte ConfigurationFlag;      // Stored value in file is 0.
            public uint Unk1;                   // Stored value in file is 2 in decimal.
            public uint Unk2;                   // Is not submesh count, I think. Stored value in file is -73 in decimal.
            public uint Unk3;                   // Stored value in file is 436 in decimal.

            public TerrainSubMeshConfiguration SubMeshConfiguration;
            public TerrainVertexStreamDescription VertexStreamDescription;

            public TerrainHeader(FileReader reader)
            {
                // Header fields
                ConfigurationFlag = reader.ReadByte();
                Unk1 = reader.ReadUInt32();
                Unk2 = reader.ReadUInt32();
                Unk3 = reader.ReadUInt32();

                SubMeshConfiguration = new TerrainSubMeshConfiguration(reader);
                VertexStreamDescription = new TerrainVertexStreamDescription(reader);

                Console.WriteLine("Finished with CHAN chunk");
                Console.WriteLine("Reader Position: " + reader.BaseStream.Position.ToString("X8"));
            }
        }

        #region SubStruct A
        public class TerrainSubMeshConfiguration    // Header Substruct A
        {
            public byte Flag;
            public uint TileElementDimension;
            public uint QuadTreeDepth;

            // Extracted from FUN_710044cd88 -> FUN_710044ce00
            public List<List<Vector2>> NestedTileArrays = new List<List<Vector2>>();

            public uint ClutterModelsPerSample;
            public uint UnknownField;
            public uint ClutterTileMaxInstances;
            public uint ClutterCullResolution;

            public CObjectId RuntimeId1;              // Unsure, likely runtime ID. Does not match any known asset.
            public CObjectId TextureId;

            public TerrainSpatialNodeParameters SpatialNodeParameters;

            public TerrainLODParameters LODParameters;

            public TerrainSubMeshConfiguration(FileReader reader)
            {
                Flag = reader.ReadByte();
                TileElementDimension = reader.ReadUInt32();
                QuadTreeDepth = reader.ReadUInt32();

                uint outerCount = reader.ReadUInt32();
                for (int i = 0; i < outerCount; i++)
                {
                    var innerList = new List<Vector2>();
                    uint innerCount = reader.ReadUInt32();
                    for (int j = 0; j < innerCount; j++)
                    {
                        innerList.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));
                    }
                    NestedTileArrays.Add(innerList);
                }

                ClutterModelsPerSample = reader.ReadUInt32();
                UnknownField = reader.ReadUInt32();
                ClutterTileMaxInstances = reader.ReadUInt32();
                ClutterCullResolution = reader.ReadUInt32();

                RuntimeId1 = IOFileExtension.ReadID(reader);
                TextureId = IOFileExtension.ReadID(reader);

                Console.WriteLine("Texture ID Check: " + TextureId.ToString());
                SpatialNodeParameters = new TerrainSpatialNodeParameters(reader);                                    // FUN_710044cd20 -> FUN_710044d384
                Console.WriteLine("Position after SubObject1: " + reader.BaseStream.Position.ToString("X8"));

                // FUN_710044cff8
                if (Flag == 0)
                {
                    LODParameters = new TerrainLODParameters(reader);         // FUN_710044d0b0 -> FUN_710044d118
                }

                Console.WriteLine("Position after SubObject2: " + reader.BaseStream.Position.ToString("X8"));
            }
        }

        public class TerrainSpatialNodeParameters        // SubStructA_Sub1
        {
            public uint NodeFlags;
            public TerrainElevationScaleConfig ElevationScaleConfig;
            public uint GridResolutionX;
            public uint GridResolutionY;
            public Vector2 UVScale;
            public Vector2 UVOffset;
            public Vector2 TextureTiling1;
            public Vector2 TextureTiling2;
            public CAABox BoundingBox;
            public List<Vector2> LayerScaleOffsets = new List<Vector2>();
            public List<List<Vector2>> NestedLayerScaleOffsets = new List<List<Vector2>>();

            public TerrainSpatialNodeParameters(FileReader reader)
            {

                // FUN_710044d384
                NodeFlags = reader.ReadUInt32();                                                   // FUN_710058b2c8
                ElevationScaleConfig = new TerrainElevationScaleConfig(reader);                                        // FUN_710044d490
                GridResolutionX = reader.ReadUInt32();                                                  // FUN_71009548e8
                GridResolutionY = reader.ReadUInt32();                                                  // FUN_71009548e8

                UVScale = new Vector2(reader.ReadSingle(), reader.ReadSingle());                  // FUN_710044d248
                UVOffset = new Vector2(reader.ReadSingle(), reader.ReadSingle());                  // FUN_710044d448
                TextureTiling1 = new Vector2(reader.ReadSingle(), reader.ReadSingle());                  // FUN_7100865800
                TextureTiling2 = new Vector2(reader.ReadSingle(), reader.ReadSingle());                  // FUN_7100865800

                BoundingBox = reader.ReadStruct<CAABox>();                                      // CAABox::CAABox

                // FUN_710044d1d0
                uint pairCount = reader.ReadUInt32();                                           // FUN_710058aec0
                for (int i = 0; i < pairCount; i++)
                {
                    LayerScaleOffsets.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));
                }

                // FUN_710044d4e8 -> FUN_710044d5e8
                uint nestedCount = reader.ReadUInt32();                                         // FUN_710058aec0
                for (int i = 0; i < nestedCount; i++)
                {
                    uint subCount = reader.ReadUInt32();                                        // FUN_710058aec0
                    var subList = new List<Vector2>();
                    for (int j = 0; j < subCount; j++)
                    {
                        subList.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));     // FUN_710044d6a4
                    }
                    NestedLayerScaleOffsets.Add(subList);
                }
            }
        }

        public class TerrainElevationScaleConfig        // SubStructA_Sub1_Sub
        {
            public uint ElevationMidpoint { get; set; }
            public uint ElevationRange { get; set; }
            public uint TerrainDimension { get; set; }

            public TerrainElevationScaleConfig(FileReader reader)
            {
                // FUN_710044d490

                ElevationMidpoint = reader.ReadUInt32();  // FUN_710058b2c8
                ElevationRange = reader.ReadUInt32();  // FUN_710058b400
                TerrainDimension = reader.ReadUInt32();  // FUN_710058b400
            }
        }

        public class TerrainLODParameters       // SubStructA_Sub2
        {
            public uint LODLevel;
            public uint DistanceThreshold;
            public uint ScreenSizeScale;
            public uint TessellationFactor;
            public uint Flags;
            public List<Vector2> LODLayerScaleOffsets = new List<Vector2>();

            public TerrainLODParameters(FileReader reader)
            {
                // FUN_710044d118
                LODLevel = reader.ReadUInt32();                                                 // FUN_710058b400
                DistanceThreshold = reader.ReadUInt32();                                        // FUN_710058b2c8
                ScreenSizeScale = reader.ReadUInt32();                                          // FUN_710058b2c8
                TessellationFactor = reader.ReadUInt32();                                       // FUN_710058b2c8
                Flags = reader.ReadUInt32();                                                    // FUN_71009548e8

                // FUN_710044d1d0
                uint count = reader.ReadUInt32();                                               // FUN_710058aec0
                for (int i = 0; i < count; i++)
                {
                    LODLayerScaleOffsets.Add(new Vector2(reader.ReadSingle(), reader.ReadSingle()));       // FUN_710044d248
                }
            }
        }
        #endregion

        #region SubStruct B
        public class TerrainVertexStreamDescription        // Header SubStruct B
        {
            public byte StreamFlags;
            public uint Unknown1;
            public uint Unknown2;
            public uint TileDimensions;
            public uint IndexCount;

            public List<Vector2> Dimensions = new List<Vector2>();                  // FUN_710058ae00
            public List<uint> LODDataSize = new List<uint>();                           // FUN_710036db3c
            public byte[] EmbeddedDataBlob;                                                 // FUN_710058b028

            // FUN_710058b120 -> FUN_710058b1a8 (Reads a 0x30 / 48-byte struct)
            public List<TerrainChannelDescription> TerrainChannels = new();

            public TerrainVertexStreamDescription(FileReader reader)
            {
                StreamFlags = reader.ReadByte();
                Unknown1 = reader.ReadUInt32();
                Unknown2 = reader.ReadUInt32();
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
                    TerrainChannels.Add(new TerrainChannelDescription(reader));
            }
        }

        public class TerrainChannelDescription
        {
            public byte ChannelNumber;
            public byte FormatType;
            public byte StreamSelector;
            // Formats
            // 0 = The stuff actually used in the terrain


            public uint ElementCount;       // Maybe Row Count
            public uint Stride;             // Maybe Bytes Per Row
            public uint DataSize;           // Byte count / size
            public uint DerivedA4;
            public uint DerivedA5;
            public uint DerivedA6;
            public uint UnknownA7;
            public uint LodCount;

            public uint Unknown1;
            public uint Unknown2;

            public bool bool0;
            public bool Active;

            public TerrainChannelDescription(FileReader reader)
            {
                // FUN_710058b1a8 (Reads total 0x30 / 48 bytes)
                ChannelNumber = reader.ReadByte();                                              // FUN_710058b33c
                FormatType = reader.ReadByte();                                             // FUN_710058b33c
                StreamSelector = reader.ReadByte();                                         // FUN_710058b33c

                // 8 uints from FUN_710058b2c8
                // High-confidence meaning from FUN_71006296f4:
                // DataSize is the number of bytes this channel contributes to one level.
                ElementCount = reader.ReadUInt32();
                Stride = reader.ReadUInt32();
                DataSize = reader.ReadUInt32();                                         // Confirmed

                // These are derived/layout quantities in the current sample. Their exact
                // semantic names are not yet proven, so retain neutral names.
                DerivedA4 = reader.ReadUInt32();
                DerivedA5 = reader.ReadUInt32();
                DerivedA6 = reader.ReadUInt32();
                UnknownA7 = reader.ReadUInt32();
                LodCount = reader.ReadUInt32();

                // 2 uints from FUN_710058b400

                Unknown1 = reader.ReadUInt32();
                Unknown2 = reader.ReadUInt32();

                bool0 = reader.ReadByte() != 0;                                         // FUN_710058b39c
                // Confirmed by the Terrain runtime: this flag gates whether the channel
                // participates in DATA layout construction.
                Active = reader.ReadByte() != 0;                                       // FUN_710058b39c
            }
        }
        #endregion

        /// <summary>
        /// One contiguous DATA payload assembled from the active TERR channels.
        /// The native runtime accumulates channel DataSize values in channel order.
        /// </summary>
        public class TerrainDataRecord
        {
            public uint LevelIndex;
            public TerrainDataChannel0 Channel0;
            public TerrainDataChannel1 Channel1;
            public TerrainDataChannel2 Channel2;
            public TerrainDataChannel3 Channel3;
            public long DataRelativeStart;
            public uint DimensionX;
            public uint DimensionY;
            //public long ByteSize { get; private set; }
            //public long ExpectedByteSize { get; }
        }

        public class TerrainDataChannel0
        {
            public byte ChannelNumber;
            public byte FormatType;
            public byte StreamSelector;
            public uint ElementCount;
            public uint Stride;
            public uint DataSize;   
        }

        public class TerrainDataChannel1
        {
            public byte ChannelNumber;
            public byte FormatType;
            public byte StreamSelector;
            public uint ElementCount;
            public uint Stride;
            public uint DataSize;
        }

        public class TerrainDataChannel2
        {
            public byte ChannelNumber;
            public byte FormatType;
            public byte StreamSelector;
            public uint ElementCount;
            public uint Stride;
            public uint DataSize;
        }

        public class TerrainDataChannel3
        {
            public byte ChannelNumber;
            public byte FormatType;
            public byte StreamSelector;
            public uint ElementCount;
            public uint Stride;
            public uint DataSize;
        }

        public class TerrainTile
        {
            public List<TVertex> tileVerts = new List<TVertex>();
            public List<float> heights = new List<float>();
            public List<byte> textureSelect = new List<byte>();
            public List<byte> unk1 = new List<byte>();
            public List<ushort> halfs2 = new List<ushort>();
            public TerrainTile(uint dimensions) 
            {
                float ZCoord = 0;
                for (int i = 0; i < dimensions; i++)
                {
                    float XCoord = 0;

                    for (int j = 0; j < dimensions; j++)
                    {
                        TVertex vert = new TVertex();

                        vert.Position = new Vector3(XCoord, 0, -ZCoord);
                        tileVerts.Add(vert);

                        XCoord += 1;
                    }
                    ZCoord += 1;
                }
            }
        }


        public class TVertex
        {
            public Vector3 Position;
            public byte textureUsage;
            public byte unknownByte;

            public Vector4 Color = Vector4.One;

            public Vector4 Tangent;
        }
    }
}