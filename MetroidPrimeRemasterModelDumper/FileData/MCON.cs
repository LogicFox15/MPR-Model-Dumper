using AvaloniaToolbox.Core;
using AvaloniaToolbox.Core.IO;
using DKCTF;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
#nullable disable

namespace DKCTF
{
    public class MCON : FileForm
    {
        public CModConData data = new CModConData();
        public CObjectId fileName;                      // For debugging down the line

        public MCON(System.IO.Stream stream) : base(stream)
        {
        }

        public override void Read(FileReader reader)
        {
            var endPos = ReadMetaFooter(reader);

            while (reader.BaseStream.Position < endPos)
            {
                var chunk = reader.ReadStruct<CChunkDescriptor>();
                var pos = reader.Position;

                //reader.SeekBegin(pos + chunk.DataOffset);
                ReadChunk(reader, chunk);

                reader.SeekBegin(pos + chunk.DataSize);
            }
        }

        public override void ReadChunk(FileReader reader, CChunkDescriptor chunk)
        {
            switch (chunk.ChunkType)
            {
                case "MCHD":
                    data.header = ModConHeader.Read(reader);
                    break;
                case "MCVD":
                    data.visualData = ModConVisualData.Read(reader);
                    break;
                case "MCCD":
                    if (chunk.DataSize > 0)
                    {
                        reader.ReadBytes((int)chunk.DataSize);
                    }
                    //data.header = ModConHeader.Read(reader);
                    break;
                case "MCDD":
                    data.decalData = ModConDecalData.Read(reader);
                    break;
                case "MCVB":
                    data.vertexBlendData = ModConVertexBlendData.Read(reader);
                    break;
                default:
                    if(chunk.DataSize > 0)
                    {
                        reader.ReadBytes((int)chunk.DataSize);
                    }
                    break;
            }
        }


        public class CModConData
        {
            public ModConHeader header;
            public ModConVisualData visualData;
            public ModConDecalData decalData;
            public ModConVertexBlendData vertexBlendData;
        }

        #region ModCon Header
        public class ModConHeader
        {
            public CChunkDescriptor chunkDescriptor;
            public uint unk1;

            public static ModConHeader Read(FileReader br)
            {
                return new ModConHeader
                {
                    chunkDescriptor = br.ReadStruct<CChunkDescriptor>(),
                    unk1 = br.ReadUInt32()
                };
            }
        }
        #endregion

        #region ModCon Visual Data
        public class ModConVisualData // MCVD
        {
            public uint modelIdCount;
            public List<CObjectId> modelID;
            public uint worldModelCount;
            public List<CObjectId> worldModelID;
            public uint colorCount;
            public List<Color4f> color;
            public uint transformCount;
            public List<CTransform4f> xf;
            public uint worldInstanceCount;
            public List<ObjectXF> worldInstance;
            public uint byteCount;
            public byte[] bytes;
            public uint modelIndexCount;
            public ushort[] modelIndex;
            public uint colorIndexCount;
            public ushort[] colorIndex;
            public uint gbufferFlagCount;
            public byte[] gbufferFlags;
            public uint byteCount3;
            public byte[] bytes3;
            public uint visualAtlasCount;
            public List<SAtlasLookup> visualAtlas;
            public uint waterAtlasCount;
            public List<SAtlasLookup> waterAtlas;
            public uint renderOctreeFlags;
            public List<CRenderOctree> renderOctrees;

            public static ModConVisualData Read(FileReader br)
            {
                ModConVisualData data = new ModConVisualData();

                data.modelIdCount = br.ReadUInt32();
                Console.WriteLine("Model Count: " + data.modelIdCount.ToString());
                if (data.modelIdCount > 0)
                {
                    data.modelID = new List<CObjectId>();
                    for (int i = 0; i < data.modelIdCount; i++)
                    {
                        data.modelID.Add(br.ReadStruct<CObjectId>());
                    }
                }

                data.worldModelCount = br.ReadUInt32();
                Console.WriteLine("World Model Count: " + data.worldModelCount.ToString());
                if (data.worldModelCount > 0)
                {
                    data.worldModelID = new List<CObjectId>();
                    for (int i = 0; i < data.worldModelCount; i++)
                    {
                        

                        data.worldModelID.Add(br.ReadStruct<CObjectId>());
                    }
                }

                data.colorCount = br.ReadUInt32();
                data.color = new List<Color4f>();
                for (int i = 0; i < data.colorCount; i++)
                {
                    data.color.Add(br.ReadStruct<Color4f>());
                }

                data.transformCount = br.ReadUInt32();
                Console.WriteLine("Transform Count: " + data.transformCount.ToString());
                data.xf = new List<CTransform4f>();
                for (int i = 0; i < data.transformCount; i++)
                {
                    data.xf.Add(CTransform4f.Read(br));
                }

                data.worldInstanceCount = br.ReadUInt32();
                Console.WriteLine("Object Transform Count: " + data.worldInstanceCount.ToString());
                data.worldInstance = new List<ObjectXF>();
                for (int i = 0; i < data.worldInstanceCount; i++)
                {
                    data.worldInstance.Add(ObjectXF.Read(br));
                }

                data.byteCount = br.ReadUInt32();
                Console.WriteLine("Byte Count: " + data.byteCount.ToString());
                if (data.byteCount > 0)
                {
                    data.bytes = br.ReadBytes((int)data.byteCount);
                }

                data.modelIndexCount = br.ReadUInt32();
                Console.WriteLine("Model Index Count: " + data.modelIndexCount.ToString());
                data.modelIndex = new ushort[data.modelIndexCount];
                for (int i = 0; i < data.modelIndexCount; i++)
                {
                    data.modelIndex[i] = br.ReadUInt16();
                }

                data.colorIndexCount = br.ReadUInt32();
                Console.WriteLine("Color Index Count: " + data.colorIndexCount.ToString());
                data.colorIndex = new ushort[data.colorIndexCount];
                for (int i = 0; i < data.colorIndexCount; i++)
                {
                    data.colorIndex[i] = br.ReadUInt16();
                }

                data.gbufferFlagCount = br.ReadUInt32();
                Console.WriteLine("gbuffer Flag Count: " + data.gbufferFlagCount.ToString());
                if (data.gbufferFlagCount > 0)
                {
                    data.gbufferFlags = br.ReadBytes((int)data.gbufferFlagCount);
                }

                data.byteCount3 = br.ReadUInt32();
                Console.WriteLine("Byte Count 3: " + data.byteCount3.ToString());
                if (data.byteCount3 > 0)
                {
                    data.bytes3 = br.ReadBytes((int)data.byteCount3);
                }

                data.visualAtlasCount = br.ReadUInt32();
                data.visualAtlas = new List<SAtlasLookup>();
                for (int i = 0; i < data.visualAtlasCount; i++)
                {
                    data.visualAtlas.Add(br.ReadStruct<SAtlasLookup>());
                }

                data.waterAtlasCount = br.ReadUInt32();
                data.waterAtlas = new List<SAtlasLookup>();
                for (int i = 0; i < data.waterAtlasCount; i++)
                {
                    data.waterAtlas.Add(br.ReadStruct<SAtlasLookup>());
                }

                data.renderOctreeFlags = br.ReadUInt32();
                data.renderOctrees = new List<CRenderOctree>();
                for (int i = 0; i < data.renderOctreeFlags; i++)
                {
                    data.renderOctrees.Add(CRenderOctree.Read(br));
                }
                return data;
            }
        }
        #endregion

        #region ModCon Decal Data
        public class ModConDecalData
        {
            public uint decalCount;
            public ushort materialCount;
            public ushort unknownShort;
            public uint[] unknownWords = new uint[2];

            public List<CObjectId> MaterialIDs = new List<CObjectId>();
            public List<ModConDecal> decals = new List<ModConDecal>();

            public static ModConDecalData Read(FileReader br)
            {
                ModConDecalData data = new ModConDecalData();

                data.decalCount = br.ReadUInt32();
                data.materialCount = br.ReadUInt16();
                data.unknownShort = br.ReadUInt16();
                data.unknownWords[0] = br.ReadUInt32();
                data.unknownWords[1] = br.ReadUInt32();

                if (data.materialCount > 0)
                {
                    data.MaterialIDs.Add(br.ReadStruct<CObjectId>());
                }
                if (data.decalCount > 0)
                {
                    ModConDecal decal = ModConDecal.Read(br);
                    data.decals.Add(decal);
                }
                return data;
            }
        }

        public class ModConDecal
        {
            public CObjectId id;
            public CTransform4f xf;
            public ushort materialIndex;
            public float angleRadians;
            public ushort sortOrder;
            public byte unknown;

            public static ModConDecal Read(FileReader br)
            {
                ModConDecal data = new ModConDecal();

                data.id = br.ReadStruct<CObjectId>();
                data.xf = CTransform4f.Read(br);
                data.materialIndex = br.ReadUInt16();
                data.angleRadians = br.ReadSingle();
                data.sortOrder = br.ReadUInt16();
                data.unknown = br.ReadByte();

                return data;
            }

        };
        #endregion

        #region ModCon Vertex Blend Data
        public class ModConVertexBlendData
        {
            public uint vertexBlendDataCount;
            public List<SNodeVertexBlendData> vertexBlendData = new List<SNodeVertexBlendData>();

            public static ModConVertexBlendData Read(FileReader br)
            {
                ModConVertexBlendData data = new ModConVertexBlendData();

                data.vertexBlendDataCount = br.ReadUInt32();
                for (int i = 0; i < data.vertexBlendDataCount; i++)
                {
                    SNodeVertexBlendData blendData = new SNodeVertexBlendData();
                    blendData = SNodeVertexBlendData.Read(br);
                    data.vertexBlendData.Add(blendData);
                }

                return data;
            }
        }

        public class SNodeVertexBlendData
        {
            public uint unkDataCount;
            public uint unkIntCount;
            public uint modelVertexBlendDataCount;

            public List<byte> unkData = new List<byte>();
            public List<uint> unkInt = new List<uint>();
            public List<SModelVertexBlendData> modelVertexBlendData = new List<SModelVertexBlendData>();

            public static SNodeVertexBlendData Read(FileReader br)
            {
                SNodeVertexBlendData data = new SNodeVertexBlendData();

                data.unkDataCount = br.ReadUInt32();
                for (int i = 0; i < data.unkDataCount; i++)
                {
                    data.unkData.Add(br.ReadByte());
                }

                data.unkIntCount = br.ReadUInt32();
                for (int i = 0; i < data.unkIntCount; i++)
                {
                    data.unkInt.Add(br.ReadByte());
                }

                data.modelVertexBlendDataCount = br.ReadUInt32();
                for (int i = 0; i < data.modelVertexBlendDataCount; i++)
                {
                    SModelVertexBlendData blendData = new SModelVertexBlendData();
                    blendData = SModelVertexBlendData.Read(br);
                    data.modelVertexBlendData.Add(blendData);
                }

                return data;
            }
        }

        public class SModelVertexBlendData
        {
            public uint unk1;
            public uint intCount;
            public uint shortCount;

            public List<uint> ints = new List<uint>();
            public List<ushort> shorts = new List<ushort>();

            public static SModelVertexBlendData Read(FileReader br)
            {
                SModelVertexBlendData data = new SModelVertexBlendData();
                data.unk1 = br.ReadUInt32();
                data.intCount = br.ReadUInt32();

                for(int i = 0; i < data.intCount; i++)
                {
                    data.ints.Add(br.ReadUInt32());
                }

                data.shortCount = br.ReadUInt32();
                for (int i = 0; i < data.shortCount; i++)
                {
                    data.shorts.Add(br.ReadUInt16());
                }

                return data;
            }
        }

        #endregion

    }

}

