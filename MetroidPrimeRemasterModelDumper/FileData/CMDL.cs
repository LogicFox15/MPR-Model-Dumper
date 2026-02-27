using AvaloniaToolbox.Core.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
#nullable disable

namespace DKCTF
{
    /// <summary>
    /// Represents a model file format for loading mesh and material data.
    /// </summary>
    public class CMDL : FileForm
    {
        public SModelHeader Header;

        /// <summary>
        /// The meshes of the model used to display the model.
        /// </summary>
        public List<CMesh> Meshes = new List<CMesh>();

        /// <summary>
        /// The materials of the model for rendering the mesh.
        /// </summary>
        public List<CMaterial> Materials = new List<CMaterial>();

        /// <summary>
        /// The vertex buffer list to read the buffer attributes.
        /// </summary>
        public List<VertexBuffer> VertexBuffers = new List<VertexBuffer>();

        /// <summary>
        /// The index buffer list to read the index buffer data.
        /// </summary>
        List<CGraphicsIndexBufferToken> IndexBuffer = new List<CGraphicsIndexBufferToken>();

        public List<byte[]> VertexBytes = new List<byte[]>();
        public List<byte[]> IndexBytes = new List<byte[]>();

        /// <summary>
        /// Determines which variant of the file to parse. Switch reads strings and materials differently.
        /// </summary>
        bool IsSwitch => this.FileHeader.FormType == "SMDL" && this.FileHeader.VersionA >= 0x3A ||
                         this.FileHeader.FormType == "CMDL" && this.FileHeader.VersionA >= 0x35 ||
                         this.FileHeader.FormType == "WMDL" && this.FileHeader.VersionA >= 0x36;

        public bool IsR11 = true;

        /// <summary>
        /// The meta data header for parsing gpu buffers and decompressing.
        /// </summary>
        public SMetaData Meta;

        public CMDL() { }

        public CMDL(System.IO.Stream stream) : base(stream)
        {
        }

        public override void ReadMetaData(FileReader reader, CFormDescriptor pakVersion)
        {
            Meta = new SMetaData();
            Meta.Unknown = reader.ReadUInt32();
            Meta.GPUOffset = reader.ReadUInt32();
            Meta.ReadBufferInfo = IOFileExtension.ReadList<SReadBufferInfo>(reader);
            Meta.VertexBuffers = IOFileExtension.ReadList<SBufferInfo>(reader);
            Meta.IndexBuffers = IOFileExtension.ReadList<SBufferInfo>(reader);
        }

        public override void WriteMetaData(FileWriter writer, CFormDescriptor pakVersion)
        {
            writer.Write(Meta.Unknown);
            writer.Write(Meta.GPUOffset);
            IOFileExtension.WriteList(writer, Meta.ReadBufferInfo);
            IOFileExtension.WriteList(writer, Meta.VertexBuffers);
            IOFileExtension.WriteList(writer, Meta.IndexBuffers);
        }

        public override void ReadChunk(FileReader reader, CChunkDescriptor chunk)
        {
            switch (chunk.ChunkType)
            {
                case "CMDL":
                    break;
                case "WMDL":
                    break;
                case "SMDL":
                    reader.ReadUInt32(); //unk
                    break;
                case "HEAD":
                    Header = reader.ReadStruct<SModelHeader>();
                    break;
                case "MTRL":
                    if (IsSwitch)
                        ReadMaterials(reader);
                    else
                        ReadMaterialsU(reader);
                    break;
                case "MESH":
                    ReadMesh(reader);
                    break;
                case "VBUF":
                    ReadVertexBuffer(reader);
                    break;
                case "IBUF":
                    ReadIndexBuffer(reader);
                    break;
                case "GPU ":

                    long startPos = reader.Position;
                    for (int i = 0; i < Meta.IndexBuffers.Count; i++)
                    {
                        var buffer = Meta.IndexBuffers[i];
                        //First buffer or specific buffer
                        var info = Meta.ReadBufferInfo[(int)buffer.ReadBufferIndex];
                        //Seek into the buffer region
                        reader.SeekBegin(info.Offset + buffer.Offset);

                        //Decompress
                        var data = IOFileExtension.DecompressedBuffer(reader, buffer.CompressedSize, buffer.DecompressedSize, IsSwitch);
                        //  if (buffer.DecompressedSize != data.Length)
                        //      throw new Exception();

                        IndexBytes.Add(data);

                        //All indices
                        var indices = BufferHelper.LoadIndexBuffer(data, this.IndexBuffer[i].IndexType, IsSwitch);

                        //Read
                        foreach (var mesh in Meshes)
                        {
                            if (mesh.Header.IndexBufferIndex == i)
                            {
                                //Select indices to use
                                mesh.Indices = indices.Skip((int)mesh.Header.IndexStart).Take((int)mesh.Header.IndexCount).ToArray();
                            }
                        }
                    }

                    //Prepare buffer list
                    List<byte[]> vertexData = new List<byte[]>();
                    for (int i = 0; i < Meta.VertexBuffers.Count; i++)
                    {
                        var buffer = Meta.VertexBuffers[i];
                        //First buffer or specific buffer
                        var info = Meta.ReadBufferInfo[(int)buffer.ReadBufferIndex];
                        //Seek into the buffer region
                        reader.SeekBegin(info.Offset + buffer.Offset);

                        //Decompress
                        var data = IOFileExtension.DecompressedBuffer(reader, buffer.CompressedSize, buffer.DecompressedSize, IsSwitch);
                        if (buffer.DecompressedSize != data.Length)
                            throw new Exception();

                        VertexBytes.Add(data);

                        vertexData.Add(data);

                        startPos += buffer.CompressedSize;
                    }

                    for (int j = 0; j < VertexBuffers.Count; j++)
                    {
                        var vertexInfo = VertexBuffers[j];
                        var bufferID = j * 2;
                        if (!this.IsMPR && !IsR11)
                            bufferID = j;

                        var vertices = BufferHelper.LoadVertexBuffer(vertexData, bufferID, vertexInfo, IsSwitch, this.IsMPR);

                        //Read
                        foreach (var mesh in Meshes)
                            if (mesh.Header.VertexBufferIndex == j)
                                mesh.SetupVertices(vertices.ToList());
                    }
                    break;
            }
        }

        private void ReadMaterialsU(FileReader reader)
        {
            uint numMaterials = reader.ReadUInt32();
            for (int i = 0; i < numMaterials; i++)
            {
                CMaterial material = new CMaterial();
                Materials.Add(material);
                material.Name = reader.ReadStringZeroTerminated();
                material.ID = reader.ReadStruct<CObjectId>();
                material.Type = reader.ReadStruct<Magic>();
                material.Flags = reader.ReadUInt32();
                uint numData = reader.ReadUInt32();

                //Actual data type data
                for (int j = 0; j < numData; j++)
                {
                    var dtype = reader.ReadStruct<Magic>();
                    uint dformat = reader.ReadUInt32();

                    Console.WriteLine($"dtype {dtype} {dformat}");

                    switch (dformat)
                    {
                        case 0: //Texture
                            material.Textures.Add(reader.ReadStruct<CMaterialTextureTokenData>());
                            break;
                        case 1: //Color
                            //material.Colors.Add(reader.ReadStruct<Color4f>());
                            material.Colors.Add(dtype, reader.ReadStruct<Color4f>());
                            break;
                        case 2: //Scaler
                            //material.Scalars.Add(reader.ReadSingle());
                            material.Scalars.Add(dtype, reader.ReadSingle());
                            break;
                        case 3: //int
                            //material.Int.Add(reader.ReadInt32());
                            material.Int.Add(dtype, reader.ReadInt32());
                            break;
                        case 4: //CLayeredTextureData
                            {
                                
                                reader.ReadUInt32();
                                reader.ReadSingles(4); //color
                                reader.ReadSingles(4); //color
                                reader.ReadSingles(4); //color
                                reader.ReadByte(); //Flags
                                var texture1 = reader.ReadStruct<CObjectId>();
                                if (!texture1.IsZero())
                                    reader.ReadStruct<STextureUsageInfo>();
                                var texture2 = reader.ReadStruct<CObjectId>();
                                if (!texture2.IsZero())
                                    reader.ReadStruct<STextureUsageInfo>();
                                var texture3 = reader.ReadStruct<CObjectId>();
                                if (!texture3.IsZero())
                                    reader.ReadStruct<STextureUsageInfo>();
                                
                            }
                            break;
                        case 5: //int4
                            //material.Int4.Add(reader.ReadInt32s(4));
                            material.Int4.Add(dtype, reader.ReadInt32s(4));
                            break;
                        default:
                            throw new Exception($"Unsupported material type {dformat}!");
                    }
                }
            }
        }

        private void ReadMaterials(FileReader reader)
        {
            if (this.IsMPR)
                reader.ReadUInt32();
            uint numMaterials = reader.ReadUInt32();
            for (int i = 0; i < numMaterials; i++)
            {
                CMaterial material = new CMaterial();
                Materials.Add(material);

                uint size = reader.ReadUInt32();
                material.Name = reader.ReadFixedString((int)size);
                material.ID = reader.ReadStruct<CObjectId>();
                if (this.IsMPR)
                {
                    reader.ReadBytes(24); //Shader guid and extras?
                    uint numTypes = reader.ReadByte();
                    reader.ReadBytes(3);
                    reader.ReadUInt32s((int)numTypes); //type list, fourcc

                    uint numDataInts = reader.ReadUInt32();

                    //A list of data types with extra flags
                    for (int j = 0; j < numDataInts; j++)
                    {
                        var dtype = reader.ReadStruct<Magic>();
                        var dformat = reader.ReadStruct<Magic>();
                        reader.ReadUInt16();
                    }
                }
                else
                    reader.ReadBytes(8); //Type, Flags

                uint numData = reader.ReadUInt32();

                //A list of data types
                for (int j = 0; j < numData; j++)
                {
                    var dtype = reader.ReadStruct<Magic>();
                    var dformat = reader.ReadStruct<Magic>();
                }

                //Actual data type data
                for (int j = 0; j < numData; j++)
                {
                    var dtype = reader.ReadStruct<Magic>();
                    var dformat = reader.ReadStruct<Magic>();

                    Console.WriteLine($"dtype {dtype} {dformat}");

                    switch (dformat)
                    {
                        case "TXTR": //Texture
                            material.Textures.Add(reader.ReadStruct<CMaterialTextureTokenData>());
                            break;
                        case "COLR": //Color
                            //material.Colors.Add(reader.ReadStruct<Color4f>());
                            material.Colors.Add(dtype, reader.ReadStruct<Color4f>());
                            break;
                        case "SCLR": //Scaler
                            //material.Scalars.Add(reader.ReadSingle());
                            material.Scalars.Add(dtype, reader.ReadSingle());
                            break;
                        case "INT ": //int
                            //material.Int.Add(reader.ReadInt32());
                            material.Int.Add(dtype, reader.ReadInt32());
                            break;
                        case "INT4": //int4
                            //material.Int4.Add(reader.ReadInt32s(4));
                            material.Int4.Add(dtype, reader.ReadInt32s(4));
                            break;
                        case "CPLX": //CLayeredTextureData
                            {
                                reader.ReadUInt32();
                                reader.ReadSingles(4); //color
                                reader.ReadSingles(4); //color
                                reader.ReadSingles(4); //color
                                reader.ReadByte(); //Flags

                                var texture1 = reader.ReadStruct<CObjectId>();
                                if (!texture1.IsZero())
                                {
                                    var info1 = reader.ReadStruct<STextureUsageInfo>();
                                    CMaterialTextureTokenData TextureData1 = new CMaterialTextureTokenData();
                                    TextureData1.FileID = texture1;
                                    TextureData1.UsageInfo = info1;
                                    material.Textures.Add(TextureData1);
                                }

                                var texture2 = reader.ReadStruct<CObjectId>();
                                if (!texture2.IsZero())
                                {
                                    var info2 = reader.ReadStruct<STextureUsageInfo>();
                                    CMaterialTextureTokenData TextureData2 = new CMaterialTextureTokenData();
                                    TextureData2.FileID = texture2;
                                    TextureData2.UsageInfo = info2;
                                    material.Textures.Add(TextureData2);
                                }

                                var texture3 = reader.ReadStruct<CObjectId>();
                                if (!texture3.IsZero())
                                {
                                    var info3 = reader.ReadStruct<STextureUsageInfo>();
                                    CMaterialTextureTokenData TextureData3 = new CMaterialTextureTokenData();
                                    TextureData3.FileID = texture3;
                                    TextureData3.UsageInfo = info3;
                                    material.Textures.Add(TextureData3);
                                }
                            }
                            break;
                        case "MA4": //Matrix4x4
                            //material.Matrices.Add(reader.ReadSingles(16));
                            material.Matrices.Add(dtype, reader.ReadSingles(16));
                            break;
                        default:
                            throw new Exception($"Unsupported material type {dformat}!");
                    }
                }
            }
        }

        private void ReadMesh(FileReader reader)
        {
            uint numMeshes = reader.ReadUInt32();
            for (int i = 0; i < numMeshes; i++)
            {
                var mesh = new CRenderMesh();

                if (this.IsMPR)
                    mesh = reader.ReadStruct<CRenderMesh>();
                else if (IsR11)
                {
                    mesh.MaterialIndex = reader.ReadUInt16();
                    mesh.VertexBufferIndex = reader.ReadByte();
                    mesh.IndexBufferIndex = reader.ReadByte();
                    mesh.IndexStart = reader.ReadUInt32();
                    mesh.IndexCount = reader.ReadUInt32();
                    reader.ReadByte();
                }
                else
                {
                    uint type = reader.ReadUInt32(); //prim type
                    mesh.MaterialIndex = reader.ReadUInt16();
                    mesh.VertexBufferIndex = reader.ReadByte();
                    mesh.IndexBufferIndex = reader.ReadByte();
                    mesh.IndexStart = reader.ReadUInt32();
                    mesh.IndexCount = reader.ReadUInt32();
                    reader.ReadUInt16(); //0x10
                    reader.ReadByte(); //0x12
                    reader.ReadByte(); //0x13
                    reader.ReadByte(); //flags
                }

                Meshes.Add(new CMesh()
                {
                    Header = mesh,
                });
            }
            Console.WriteLine();
        }

        private void ReadVertexBuffer(FileReader reader)
        {
            uint numBuffers = reader.ReadUInt32();
            for (int i = 0; i < numBuffers; i++)
            {
                VertexBuffer vertexBuffer = new VertexBuffer();
                vertexBuffer.VertexCount = reader.ReadUInt32();

                uint numAttributes = reader.ReadUInt32();

                for (int j = 0; j < numAttributes; j++)
                    vertexBuffer.Components.Add(reader.ReadStruct<SVertexDataComponent>());

                VertexBuffers.Add(vertexBuffer);
                if (this.IsMPR || IsR11)
                    reader.ReadByte();
            }
        }

        private void ReadIndexBuffer(FileReader reader)
        {
            uint numBuffers = reader.ReadUInt32();
            for (int i = 0; i < numBuffers; i++)
                IndexBuffer.Add(reader.ReadStruct<CGraphicsIndexBufferToken>());
        }

        public class VertexBuffer
        {
            public List<SVertexDataComponent> Components = new List<SVertexDataComponent>();

            public uint VertexCount;
        }

        public class CVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 TexCoord0;
            public Vector2 TexCoord1;
            public Vector2 TexCoord2;
            public Vector2 TexCoord3;

            public Vector4 BoneWeights = new Vector4(1, 0, 0, 0);
            public Vector4 BoneIndices = new Vector4(0);

            public Vector4 Color1 = Vector4.One;

            public Vector4 Tangent;
        }

        public class CMaterial
        {
            public string Name { get; set; }
            public string Type { get; set; }

            public CObjectId ID { get; set; }

            public uint Flags { get; set; }

            public List<CMaterialTextureTokenData> Textures = new List<CMaterialTextureTokenData>();

            //public List<float> Scalars = new List<float>();
            //public List<int> Int = new List<int>();
            //public List<int[]> Int4 = new List<int[]>();
            //public List<float[]> Matrices = new List<float[]>();

            //public List<Color4f> Colors = new List<Color4f>();

            //public Dictionary<string, CMaterialTextureTokenData> Textures = new Dictionary<string, CMaterialTextureTokenData>();

            public Dictionary<string, float> Scalars = new Dictionary<string, float>();
            public Dictionary<string, int> Int = new Dictionary<string, int>();
            public Dictionary<string, int[]> Int4 = new Dictionary<string, int[]>();
            public Dictionary<string, float[]> Matrices = new Dictionary<string, float[]>();

            public Dictionary<string, Color4f> Colors = new Dictionary<string, Color4f>();
        }

        public class CMesh
        {
            public CRenderMesh Header;

            public List<CVertex> Vertices = new List<CVertex>();

            public uint[] Indices = new uint[0];

            public void SetupVertices(List<CVertex> vertices)
            {
                //Here we optmize the vertices to only use the vertices used by the mesh rather than use one giant list
                List<CVertex> vertexList = new List<CVertex>();
                List<uint> remappedIndices = new List<uint>();
                for (int i = 0; i < Indices.Length; i++)
                {
                    remappedIndices.Add((uint)vertexList.Count);
                    vertexList.Add(vertices[(int)Indices[i]]);
                }
                this.Vertices = vertexList;
                this.Indices = remappedIndices.ToArray();
            }
        }

        public class SSkinnedModelHeader : CChunkDescriptor
        {
            public uint Unknown;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class CRenderMesh
        {
            public ushort MaterialIndex;
            public byte VertexBufferIndex;
            public byte IndexBufferIndex;
            public uint IndexStart;
            public uint IndexCount;
            public ushort field_C; 
            public ushort field_E; //0x4000
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class CMaterialTextureTokenData
        {
            public CObjectId FileID;
            public STextureUsageInfo UsageInfo;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class STextureUsageInfo
        {
            public uint Flags;
            public uint TextureFilter;
            public uint TextureWrapX;
            public uint TextureWrapY;
            public uint TextureWrapZ;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SModelHeader 
        {
            public uint NumOpaqueMeshes;
            public uint Num1PassTranslucentMeshes;
            public uint Num2PassTranslucentMeshes;
            public uint Num1BitMeshes;
            public uint NumAdditiveMeshes;
            public CAABox BoundingBox;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class CGraphicsIndexBufferToken
        {
            public IndexFormat IndexType;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SVertexDataComponent
        {
            public uint BufferID;
            public uint Offset;
            public uint Stride;
            public VertexFormat Format;
            public EVertexComponent Type;
        }

        public enum IndexFormat
        {
            Uint16 = 1,
            Uint32 = 2,
        }

        public enum PrimtiiveType
        {
            Triangles = 3,
        }

        public enum VertexFormat
        {
            Byte = 0, // Unsigned Byte 5121 SCALAR
            Format_16_16_HalfSingle = 20, // Float 5126 VEC2
            Format_8_8_8_8_UNorm = 21, // Float 5126 VEC2
            Format_8_8_8_8_Uint = 22, // Unsigned Byte 5121 VEC4
            Format_16_16_16_HalfSingle = 34, // Float 5126 VEC3
            Format_32_32_32_Single = 37, // Float 5126 VEC3
            Format_32_32_32_32_Single = 40, // Float 5126 VEC4
        }

        public enum EVertexComponent
        {
            in_position = 0,
            in_normal = 1,
            in_tangent0 = 2,
            in_tangent1 = 3,
            in_tangent2 = 4,
            in_texCoord0 = 5,
            in_texCoord1 = 6,
            in_texCoord2 = 7,
            in_texCoord3 = 8,
            in_color = 9,
            in_boneIndices = 10,
            in_boneWeights = 11,
            in_bakedLightingCoord = 12,
            in_bakedLightingTangent = 13,
            in_vertInstanceParams = 14,
            in_vertInstanceColor = 15,
            in_vertTransform0 = 16,
            in_vertTransform1 = 17,
            in_vertTransform2 = 18,
            in_currentPosition = 19,
            in_VertInstanceOpacityParams = 20,
            in_VertInstanceColorIndexingParams = 21,
            in_VertInstanceOpacityIndexingParams = 22,
            in_VertInstancePaintParams = 23,
            in_BakedLightingLookup = 24,
            in_MaterialChoice0 = 25,
            in_MaterialChoice1 = 26,
            in_MaterialChoice2 = 27,
            in_MaterialChoice3 = 28,


        }

        //Meta data from PAK archive

        public class SMetaData
        {
            public uint Unknown;
            public uint GPUOffset;
            public List<SReadBufferInfo> ReadBufferInfo = new List<SReadBufferInfo>();
            public List<SBufferInfo> VertexBuffers = new List<SBufferInfo>();
            public List<SBufferInfo> IndexBuffers = new List<SBufferInfo>();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SReadBufferInfo
        {
            public uint Size;
            public uint Offset;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class SBufferInfo
        {
            public uint ReadBufferIndex;
            public uint Offset;
            public uint CompressedSize;
            public uint DecompressedSize;
        }
    }
}
