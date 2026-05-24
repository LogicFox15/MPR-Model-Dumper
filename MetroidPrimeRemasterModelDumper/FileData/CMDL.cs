using AvaloniaToolbox.Core.IO;
using MetroidPrimeRemasterModelDumper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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
        /// <summary>
        /// The meshes of the model used to display the model.
        /// </summary>
        public List<CMesh> Meshes = new List<CMesh>();

        /// <summary>
        /// The materials of the model for rendering the mesh.
        /// </summary>
        public List<CMaterial> Materials = new List<CMaterial>();
        public List<CMaterialNew> MaterialsNew = new List<CMaterialNew>();

        /// <summary>
        /// The entries of the material shape for the model.
        /// </summary>
        public List<CMaterialRef> MSHPEntries = new List<CMaterialRef>();

        /// <summary>
        /// The vertex buffer list to read the buffer attributes.
        /// </summary>
        List<VertexBuffer> VertexBuffers = new List<VertexBuffer>();

        /// <summary>
        /// The index buffer list to read the index buffer data.
        /// </summary>
        List<CGraphicsIndexBufferToken> IndexBuffer = new List<CGraphicsIndexBufferToken>();

        /// <summary>
        /// Determines which variant of the file to parse. Switch reads strings and materials differently.
        /// </summary>
        bool IsSwitch => this.FileHeader.FormType == "SMDL" && this.FileHeader.VersionA >= 0x3A ||
                         this.FileHeader.FormType == "CMDL" && this.FileHeader.VersionA >= 0x35 ||
                         this.FileHeader.FormType == "WMDL" && this.FileHeader.VersionA >= 0x36;

        public bool IsR11 = true;

        public byte[] unk1;
        public byte[] unk2;
        public uint shortCount;
        public ushort[] shorts;
        public byte lodCount;
        public LODinfo[] lods;
        public uint hasLODRule;
        public LodRule[] LodRules;


        /// <summary>
        /// The meta data header for parsing gpu buffers and decompressing.
        /// </summary>
        SMetaData Meta;

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
                    reader.ReadStruct<SModelHeader>();
                    break;
                case "MTRL":
                    // A check test that might not even work.
                    string MagicCheck = new string(reader.ReadChars(4));
                    //Console.WriteLine("Material Type: " + MagicCheck);
                    switch (MagicCheck)
                    {
                        case "LEGA":
                            ReadLegacyMaterials(reader);
                            break;
                        case "MSHP":
                            ReadMaterialShape(reader);
                            break;
                    }
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
                        Console.WriteLine("Vertex Buffer Position: " + reader.BaseStream.Position.ToString("X8"));

                        //Decompress
                        var data = IOFileExtension.DecompressedBuffer(reader, buffer.CompressedSize, buffer.DecompressedSize, IsSwitch);
                        if (buffer.DecompressedSize != data.Length)
                            throw new Exception();

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

        private void ReadLegacyMaterials(FileReader reader)
        {
            reader.ReadUInt32();

            uint numMaterials = reader.ReadUInt32();
            //Console.WriteLine("Number of Materials?: " + numMaterials);

            for (int i = 0; i < numMaterials; i++)
            {
                CMaterial material = new CMaterial();
                Materials.Add(material);

                uint size = reader.ReadUInt32();
                material.Name = reader.ReadFixedString((int)size);
                material.ID = reader.ReadStruct<CObjectId>();
                reader.ReadStruct<CObjectId>(); // Not sure.
                Console.WriteLine("Material Name Check: " + material.Name.ToString());

                uint check = reader.ReadUInt32(); // unk1
                Console.WriteLine("Data Check: " + check.ToString("X8"));

                reader.ReadUInt32(); // unk2
                uint traitCount = reader.ReadUInt32();
                Console.WriteLine("Trait Count Check: " + traitCount.ToString("X8"));

                for (int v = 0; v < traitCount; v++)
                {
                    reader.ReadChars(4); // This stuff. RLTG.
                }
                uint variableDescCount = reader.ReadUInt32();
                Console.WriteLine("Variable Desc Count Check: " + variableDescCount.ToString("X8"));

                for (int v = 0; v < variableDescCount; v++)
                {
                    CVariableDesc variableDesc = new CVariableDesc();

                    variableDesc = reader.ReadStruct<CVariableDesc>();
                }
                uint numData = reader.ReadUInt32();
                Console.WriteLine("Data Number Check: " + numData.ToString("X8"));

                //Actual data type data
                for (int j = 0; j < numData; j++)
                {
                    var dID = reader.ReadStruct<Magic>();
                    var dType = reader.ReadStruct<Magic>();

                    reader.Position -= 8;

                    string typeCheck = new string(reader.ReadChars(4));
                    string formatCheck = new string(reader.ReadChars(4));

                    Console.WriteLine($"dtype {typeCheck} {formatCheck}");

                    switch (dType)
                    {
                        case "TXTR": //Texture
                            material.TextureIDs.Add(dID);
                            material.Textures.Add(reader.ReadStruct<CMaterialTextureTokenData>());
                            //Console.WriteLine("material format: TXTR");
                            break;
                        case "COLR": //Color
                            material.Colors.Add(dID, reader.ReadStruct<Color4f>());
                            //Console.WriteLine("material format: COLR");
                            break;
                        case "SCLR": //Scaler
                            material.Scalars.Add(dID, reader.ReadSingle());
                            //Console.WriteLine("material format: SCLR");
                            break;
                        case "INT ": //int
                            material.Int.Add(dID, reader.ReadInt32());
                            //Console.WriteLine("material format: INT");
                            break;
                        case "INT4": //int4
                            material.Int4.Add(dID, reader.ReadInt32s(4));
                            //Console.WriteLine("material format: INT4");
                            break;
                        case "CPLX": //CLayeredTextureData
                            //Console.WriteLine("material format: CPLX");
                            reader.ReadUInt32();
                            uint cplxSize = reader.ReadUInt32();
                            reader.ReadBytes((int)cplxSize);
                            break;
                        case "MA4": //Matrix4x4
                            //Console.WriteLine("material format: MA4");
                            material.Matrices.Add(dID, reader.ReadSingles(16));
                            break;
                        default:
                            throw new Exception($"Unsupported material type {formatCheck}!");
                    }
                }
            }
        }

        private void ReadMaterialShape(FileReader reader)
        {
            uint numMaterials = reader.ReadUInt32();
            uint unk1 = reader.ReadUInt32();
            uint unk2 = reader.ReadUInt32();

            //CMaterialShapeEntry[] Entries = new CMaterialShapeEntry[numMaterials];

            for(int i = 0; i < unk2; i++)
            {
                reader.ReadUInt32();
            }

            for (int i = 0; i < numMaterials; i++)
            {
                CMaterialRef Entry = new CMaterialRef();

                Entry.MatiID = IOFileExtension.ReadID(reader);
                Entry.MtrlID = IOFileExtension.ReadID(reader);
                Entry.unk = reader.ReadBytes(6);

                MSHPEntries.Add(Entry);
            }

            foreach (var item in MSHPEntries)
            {
                FileEntry file = BatchPakExtractor.SearchForMaterial(item.MatiID.ToString(), 0);

                // Time to brute force this crap, because apparently no one ever fully looked into it.
                FileReader MATIReader = new FileReader(file.FileData);
                ReadMATI(MATIReader, file.FileName.ToString());
            }
        }

        // New Material Functions.
        public void ReadMATI(FileReader reader, string name)
        {
            MATI currentMat = new MATI();

            CMaterialNew material = new CMaterialNew();
            MaterialsNew.Add(material);
            material.Name = name;

            //CFormDescriptor form = new CFormDescriptor();

            currentMat.form = reader.ReadStruct<CFormDescriptor>();

            currentMat.materialInstance.header = reader.ReadStruct<CChunkDescriptor>();
            currentMat.materialInstance.GUID1 = reader.ReadStruct<CObjectId>();
            currentMat.materialInstance.GUID2 = reader.ReadStruct<CObjectId>();
            currentMat.materialInstance.unk1 = reader.ReadByte();
            currentMat.materialInstance.unk2 = reader.ReadByte();
            currentMat.materialInstance.unkInt1 = reader.ReadUInt32();
            currentMat.materialInstance.unkInt2 = reader.ReadUInt32();
            currentMat.materialInstance.variableDescCount = reader.ReadUInt32();

            CVariableDesc[] descs = new CVariableDesc[currentMat.materialInstance.variableDescCount];
            for(int i = 0; i < currentMat.materialInstance.variableDescCount; i++)
            {
                CVariableDesc tempDesc = reader.ReadStruct<CVariableDesc>();

                descs[i] = tempDesc;
            }
            currentMat.materialInstance.cVariableDescs = descs;

            // Maya splines, because why not.
            currentMat.materialInstance.MayaSplineCount = reader.ReadByte();
            if(currentMat.materialInstance.MayaSplineCount > 0)
            {
                for(int i = 0; i < currentMat.materialInstance.MayaSplineCount; i++)
                {
                    byte packedCount = reader.ReadByte();
                    int trueCount = GetCountFromBits(packedCount);
                    if(trueCount > 0)
                    {
                        for(int b = 0; b < trueCount; b++)
                        {
                            CMayaSpline mayaSpline = new CMayaSpline();

                            mayaSpline.knotCount = reader.ReadUInt32();

                            if (mayaSpline.knotCount > 0)
                            {
                                CMayaSplineKnot[] mayaSplineKnot = new CMayaSplineKnot[mayaSpline.knotCount];

                                for (int c = 0; c < mayaSpline.knotCount; c++)
                                {
                                    CMayaSplineKnot knot = new CMayaSplineKnot();
                                    knot.unk1 = reader.ReadUInt32();
                                    knot.unk2 = reader.ReadUInt32();
                                    knot.unk3 = reader.ReadByte();
                                    knot.unk4 = reader.ReadByte();
                                    if (knot.unk3 == 5)
                                    {
                                        knot.tangentA = new Vector2(reader.ReadUInt32(), reader.ReadUInt32());
                                    }
                                    if (knot.unk4 == 5)
                                    {
                                        knot.tangentB = new Vector2(reader.ReadUInt32(), reader.ReadUInt32());
                                    }

                                    mayaSplineKnot[c] = knot;
                                }
                            }

                            mayaSpline.minAmplitudeTime = reader.ReadUInt32();
                            mayaSpline.maxAmplitudeTime = reader.ReadUInt32();
                            mayaSpline.unk1 = reader.ReadByte();
                            mayaSpline.unk2 = reader.ReadByte();
                            mayaSpline.unk3 = reader.ReadByte();

                            material.MayaSplines.Add(mayaSpline);
                        }
                    }
                }
            }
            currentMat.materialInstance.unk4 = reader.ReadByte();

            // The important stuff
            uint EntryCount = reader.ReadUInt32();
            bool done = false;
            for(int i = 0; i < EntryCount; i++)
            {
                string Type = new string(reader.ReadChars(4));
                

                if (done)
                {
                    break;
                }

                byte Format = reader.ReadByte();

                Console.WriteLine("Material Data Type: " + Type + " Format: " + Format.ToString());
                switch (Format)
                {
                    case 2: // Single 4Byte value
                        //material.Scalars.Add(Type, reader.ReadSingle());
                        break;
                    case 3: // Color
                        material.Colors.Add(Type, reader.ReadStruct<Color4f>());
                        break;
                    case 4: // Color
                        material.Colors.Add(Type, reader.ReadStruct<Color4f>());
                        break;
                    case 12: // Color
                        material.Colors.Add(Type, reader.ReadStruct<Color4f>());
                        break;
                    case 5: // Texture
                        CTextureNew tempTex0 = new CTextureNew();
                        tempTex0.FileID = reader.ReadStruct<CObjectId>();
                        tempTex0.unkUint = reader.ReadUInt32();
                        tempTex0.unkGUID = reader.ReadStruct<CObjectId>();
                        tempTex0.type = Type;
                        material.Textures.Add(tempTex0);
                        break;
                    case 6: // Texture
                        CTextureNew tempTex1 = new CTextureNew();
                        tempTex1.FileID = reader.ReadStruct<CObjectId>();
                        tempTex1.unkUint = reader.ReadUInt32();
                        tempTex1.unkGUID = reader.ReadStruct<CObjectId>();
                        tempTex1.type = Type;
                        material.Textures.Add(tempTex1);
                        break;
                    case 7: // Texture
                        CTextureNew tempTex2 = new CTextureNew();
                        tempTex2.FileID = reader.ReadStruct<CObjectId>();
                        tempTex2.unkUint = reader.ReadUInt32();
                        tempTex2.unkGUID = reader.ReadStruct<CObjectId>();
                        tempTex2.type = Type;
                        material.Textures.Add(tempTex2);
                        break;
                    case 8: // Texture
                        CTextureNew tempTex3 = new CTextureNew();
                        tempTex3.FileID = reader.ReadStruct<CObjectId>();
                        tempTex3.unkUint = reader.ReadUInt32();
                        tempTex3.unkGUID = reader.ReadStruct<CObjectId>();
                        tempTex3.type = Type;
                        material.Textures.Add(tempTex3);
                        break;
                    case 10: // Another Color?
                        material.Colors.Add(Type, reader.ReadStruct<Color4f>());
                        break;
                    default:
                        done = true;
                        return;
                }
            }

            //throw new Exception("Kill the reader");
        }

        private void ReadMesh(FileReader reader)
        {
            uint numMeshes = reader.ReadUInt32();
            for (int i = 0; i < numMeshes; i++)
            {
                // Simply read the 16-byte struct to match the Rust implementation
                // and keep the binary stream perfectly aligned.
                var meshHeader = reader.ReadStruct<CRenderMesh>();

                Meshes.Add(new CMesh()
                {
                    Header = meshHeader,
                });
            }

            /*
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
            */

            this.unk1 = new byte[(numMeshes + 3) / 4];
            this.unk2 = new byte[(numMeshes + 7) / 8];
            for (int i = 0; i < unk1.Length; i++)
            {
                this.unk1[i] = reader.ReadByte();
            }
            for (int i = 0; i < unk2.Length; i++)
            {
                this.unk2[i] = reader.ReadByte();
            }

            this.shortCount = reader.ReadUInt32();
            this.shorts = new ushort[this.shortCount];

            for (int i = 0; i < shortCount; i++)
            {
                this.shorts[i] = reader.ReadUInt16();
            }

            this.lodCount = reader.ReadByte();
            this.lods = new LODinfo[lodCount];

            for (int i = 0; i < lodCount; i++)
            {
                //Console.WriteLine("LOD outer: " + i);
                LODinfo info = new LODinfo();

                info.ReadInner(reader);

                this.lods[i] = info;
            }

            this.hasLODRule = reader.ReadUInt32();

            // Conditionally read the LOD rules if the flag is set to 1
            if (this.hasLODRule == 1)
            {
                this.LodRules = new LodRule[this.lodCount];
                for (int i = 0; i < this.lodCount; i++)
                {
                    this.LodRules[i] = new LodRule
                    {
                        Value = reader.ReadSingle() // SRenderModelLODRule is just a standard f32
                    };
                }
            }
            else
            {
                // Initialize empty to avoid null reference exceptions down the line
                this.LodRules = new LodRule[0];
            }


            // Map meshes to ALL LODs they appear in
            for (int lodIndex = 0; lodIndex < this.lodCount; lodIndex++)
            {
                LODinfo currentLOD = this.lods[lodIndex];

                foreach (LODInner inner in currentLOD.inner)
                {
                    for (uint i = 0; i < inner.count; i++)
                    {
                        ushort meshIndex = this.shorts[inner.offset + i];

                        if (meshIndex < this.Meshes.Count)
                        {
                            this.Meshes[meshIndex].LODs.Add(lodIndex);
                        }
                    }
                }
            }
        }

        private void ReadVertexBuffer(FileReader reader)
        {
            uint numBuffers = reader.ReadUInt32();
            for (int i = 0; i < numBuffers; i++)
            {
                VertexBuffer vertexBuffer = new VertexBuffer();
                //vertexBuffer.VertexCount = reader.ReadUInt32(); // FIX THIS BEFORE TESTING ON PRIME 4

                uint numAttributes = reader.ReadUInt32();

                for (int j = 0; j < numAttributes; j++)
                    vertexBuffer.Components.Add(reader.ReadStruct<SVertexDataComponent>());

                VertexBuffers.Add(vertexBuffer);
                reader.ReadUInt32(); // FIX THIS BEFORE TESTING ON PRIME 4
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

            //public uint VertexCount;
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

            public Vector4 Color = Vector4.One;

            public Vector4 Tangent;
        }

        public class MATI
        {
            public CFormDescriptor form = new CFormDescriptor();
            public MaterialInstance materialInstance = new MaterialInstance();
        }

        public class MaterialInstance()
        {
            public CChunkDescriptor header = new CChunkDescriptor();
            public CObjectId GUID1;
            public CObjectId GUID2;
            public byte unk1;
            public byte unk2;
            public uint unkInt1;
            public uint unkInt2;
            public uint variableDescCount;
            public CVariableDesc[] cVariableDescs;
            public byte MayaSplineCount;
            public byte unk4;
        }

        public class CMayaSpline
        {
            public uint knotCount;
            public CMayaSplineKnot[] knots;

            public uint minAmplitudeTime;
            public uint maxAmplitudeTime;
            public byte unk1;
            public byte unk2;
            public byte unk3;
        }

        public class CMayaSplineKnot
        {
            public uint unk1;
            public uint unk2;
            public byte unk3;
            public byte unk4;

            public Vector2 tangentA = new Vector2(0, 0);
            public Vector2 tangentB = new Vector2(0, 0);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class CVariableDesc
        {
            public Magic Type;
            public Magic ID;
            public byte unk1;
            public byte unk2;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class CMaterialRef
        {
            public CObjectId MatiID;
            public CObjectId MtrlID;
            public byte[] unk = new byte[6];
        }

        public int GetCountFromBits(int b) // Bit shifting to get the true count of Maya Splines.
        {
            int count = 0;
            while( b > 0)
            {
                count += (b & 1);
                b >>= 1;
            }
            return count;
        }

        public class CMaterial
        {
            public string Name { get; set; }
            public string Type { get; set; }

            public CObjectId ID { get; set; }

            public uint Flags { get; set; }

            public List<Magic> TextureIDs = new List<Magic>();
            public List<CMaterialTextureTokenData> Textures = new List<CMaterialTextureTokenData>();

            //public Dictionary<string, CMaterialTextureTokenData> Textures = new Dictionary<string, CMaterialTextureTokenData>();

            public Dictionary<string, float> Scalars = new Dictionary<string, float>();
            public Dictionary<string, int> Int = new Dictionary<string, int>();
            public Dictionary<string, int[]> Int4 = new Dictionary<string, int[]>();
            public Dictionary<string, float[]> Matrices = new Dictionary<string, float[]>();
            public Dictionary<string, Color4f> Colors = new Dictionary<string, Color4f>();

            // New stuff
        }

        public class CMaterialNew
        {
            public string Name;

            public List<CTextureNew> Textures = new List<CTextureNew>();
            public List<CMayaSpline> MayaSplines = new List<CMayaSpline>();
            public List<CVariableDesc> VariableDescs = new List<CVariableDesc>();

            public Dictionary<string, float> Scalars = new Dictionary<string, float>();
            public Dictionary<string, int> Int = new Dictionary<string, int>();
            public Dictionary<string, int[]> Int4 = new Dictionary<string, int[]>();
            public Dictionary<string, float[]> Matrices = new Dictionary<string, float[]>();
            public Dictionary<string, Color4f> Colors = new Dictionary<string, Color4f>();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class CTextureNew
        {
            public CObjectId FileID;
            public uint unkUint;
            public CObjectId unkGUID;
            public string type;
        }

        public class CMesh
        {
            public CRenderMesh Header;
            public List<CVertex> Vertices = new List<CVertex>();
            public uint[] Indices = new uint[0];

            public HashSet<int> LODs = new HashSet<int>();

            //public List<int> LODs = new List<int>();

            //public int parentLOD;

            public void SetupVertices(List<CVertex> vertices)
            {
                //Here we optmize the vertices to only use the vertices used by the mesh rather than use one giant list
                List<CVertex> vertexList = new List<CVertex>();
                List<uint> remappedIndices = new List<uint>();

                //Console.WriteLine("Vertex Indices Count: " + Indices.Length);

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
            public uint unknown;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class CRenderMesh
        {
            public ushort MaterialIndex; // 2 bytes
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
            Byte = 0,
            Format_16_16_HalfSingle = 20,
            Format_8_8_8_8_UNorm = 21,
            Format_8_8_8_8_Uint = 22,
            Format_16_16_16_16_UNorm = 30,
            Format_16_16_16_HalfSingle = 34,
            Format_32_32_32_Single = 37,
            Format_32_32_32_32_Single = 40,
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
            public uint unk;  // FIX THIS BEFORE TESTING ON PRIME 4
        }

        // LOD stuff
        public class LODinfo
        {
            public LODInner[] inner = new LODInner[5];

            public void ReadInner(FileReader reader)
            {
                for(int i  = 0; i < inner.Length; i++)
                {
                    LODInner tempinner = new LODInner();

                    tempinner.offset = reader.ReadUInt32();
                    tempinner.count = reader.ReadUInt32();

                    inner[i] = tempinner;
                }
            }
        }

        public class LODInner
        {
            public uint offset;
            public uint count;
        }

        public class LodRule
        {
            public float Value;
        }


    }
}