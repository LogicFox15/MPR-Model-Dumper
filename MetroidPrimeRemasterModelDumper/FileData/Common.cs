using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using AvaloniaToolbox.Core.IO;
using System.Numerics;
using IONET.Collada.Core.Data_Flow;

namespace DKCTF
{
    #region File descriptors
    /// <summary>
    /// Represents the base of a file format.
    /// </summary>
    public class FileForm
    {
        /// <summary>
        /// The form header containing the type and version of the file.
        /// </summary>
        public CFormDescriptor FileHeader;

        public FileForm() { }

        public bool IsLittleEndian = false;
        public bool IsMPR = false;

        public FileForm(Stream stream, bool leaveOpen = false)
        {
            using (var reader = new FileReader(stream, leaveOpen))
            {
                //Small "hack" to detect endianness.
                using (reader.TemporarySeek(4, SeekOrigin.Begin)) {
                    //Size is a uint64. If the first 4 bytes are present, file is little endian
                    IsLittleEndian = reader.ReadUInt32() != 0;
                    //MPR is only game currently that is little endian
                    IsMPR = IsLittleEndian;
                }

                reader.SetByteOrder(!IsLittleEndian);
                FileHeader = reader.ReadStruct<CFormDescriptor>();

                if (FileHeader.VersionA == 0 && FileHeader.VersionB == 0)
                    reader.SetByteOrder(true);

                Read(reader);
                AfterLoad();
            }
        }


        /// <summary>
        /// Reads the file by looping through chunks..
        /// </summary>
        public virtual void Read(FileReader reader)
        {
            var endPos = ReadMetaFooter(reader);

            while (reader.BaseStream.Position < endPos)
            {
                var chunk = reader.ReadStruct<CChunkDescriptor>();
                var pos = reader.Position;

                reader.SeekBegin(pos + chunk.DataOffset);
                ReadChunk(reader, chunk);

                reader.SeekBegin(pos + chunk.DataSize);
            }
        }

        /// <summary>
        /// Reads a specified chunk.
        /// </summary>
        public virtual void ReadChunk(FileReader reader, CChunkDescriptor chunk)
        {
        }

        /// <summary>
        /// Reads meta data information within the pak archive.
        /// </summary>
        public virtual void ReadMetaData(FileReader reader, CFormDescriptor pakVersion)
        {

        }

        /// <summary>
        /// Writes meta data information within the pak archive.
        /// </summary>
        public virtual void WriteMetaData(FileWriter writer, CFormDescriptor pakVersion)
        {

        }

        /// <summary>
        /// Executes after the file has been fully read.
        /// </summary>
        public virtual void AfterLoad()
        {

        }

        /// <summary>
        /// Reads the tool created footer containing meta data used for decompressing buffer data.
        /// </summary>
        public long ReadMetaFooter(FileReader reader)
        {
            using (reader.TemporarySeek(reader.BaseStream.Length - 20, SeekOrigin.Begin))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "META")
                    return reader.BaseStream.Length;

            }

            using (reader.TemporarySeek(reader.BaseStream.Length - 20, SeekOrigin.Begin))
            {
                reader.ReadSignature("META");
                reader.ReadFixedString(4); //type of file
                uint versionA = reader.ReadUInt32(); //pak version A
                uint versionB = reader.ReadUInt32(); //pak version B
                uint size = reader.ReadUInt32(); //size of meta data
                //Seek back to meta data
                reader.SeekBegin(reader.Position - size);
                //Read meta data
                CFormDescriptor pakHeader = new CFormDescriptor();
                pakHeader.VersionA = versionA;
                pakHeader.VersionB = versionB;

                ReadMetaData(reader, pakHeader);

                return reader.BaseStream.Length - size;
            }
        }

        /// <summary>
        /// Writes a footer to a file for accessing meta data information outside a .pak archive.
        /// </summary>
        public static byte[] WriteMetaFooter(FileReader reader, uint metaOffset, string type, PACK pack)
        {
            //Magic + meta offset first
            var mem = new MemoryStream();
            using (var writer = new FileWriter(mem))
            {
                writer.SetByteOrder(!pack.IsLittleEndian);

                reader.SeekBegin(metaOffset);
                var file = GetFileForm(type);
                file.ReadMetaData(reader, pack.FileHeader);
                file.WriteMetaData(writer, pack.FileHeader);

                //Write footer header last
                writer.WriteSignature("META");
                writer.WriteSignature(type);
                writer.Write(pack.FileHeader.VersionA);
                writer.Write(pack.FileHeader.VersionB);
                writer.Write((uint)(writer.BaseStream.Length + 4)); //size
            }
            return mem.ToArray();
        }

        //Creates file instances for read/writing meta entries from pak archives
        static FileForm GetFileForm(string type)
        {
            switch (type)
            {
                case "CMDL": return new CMDL();
                case "SMDL": return new CMDL();
                case "WMDL": return new CMDL();
                case "TXTR": return new TXTR();
                case "LTPB": return new LTPB();
                //case "ROOM": return new ROOM();
            }
            return new FileForm();
        }
    }

    //Documentation from https://github.com/Kinnay/Nintendo-File-Formats/wiki/DKCTF-Types#cformdescriptor

    /// <summary>
    /// Represents the header of a file format.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class CFormDescriptor
    {
        public Magic Magic = "RFRM";
        public ulong DataSize;
        public ulong Unknown;
        public Magic FormType; //File type identifier
        public uint VersionA;
        public uint VersionB;
    }

    /// <summary>
    /// Represents the header of a chunk of a file form.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class CChunkDescriptor
    {
        public Magic ChunkType;
        public long DataSize;
        public uint Unknown;
        public long DataOffset;
    }
    #endregion

    #region Maya Spines
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class CMayaSpline
    {
        public Magic ChunkType;
        public ulong DataSize;
        public uint Unknown;
        public ulong DataOffset;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class CMayaSplineKnot
    {
        public float Time;
        public float Value;
        public ETangentType TangentType1;
        public ETangentType TangentType2;
        public float FieldC;
        public float Field10;
        public float Field14;
        public float Field18;
    }
    #endregion

    #region Generics
    /// <summary>
    /// Tag data for an object providing the type and id.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class CObjectTag
    {
        public Magic Type;
        public CObjectId Objectid;
    }

    /// <summary>
    /// A header for asset data providing a type and version number.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class CAssetHeader
    {
        public ushort TypeID;
        public ushort Version;
    }


    /// <summary>
    /// Stores a unique ID for a given object to identify it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CObjectId
    {
        public CGuid Guid;

        public override string ToString()
        {
            return Guid.ToString();
        }

        public bool IsZero()
        {
            return Guid.Part1 == 0 && 
                   Guid.Part2 == 0 && 
                   Guid.Part3 == 0 &&
                   Guid.Part4[0] == 0;
        }
    }

    /// <summary>
    /// An axis aligned bounding box with a min and max position value.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class CAABox
    {
        public Vector3f Min;
        public Vector3f Max;
    }

    /// <summary>
    /// A vector with X/Y/Z axis values.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class Vector3f
    {
        public float X;
        public float Y;
        public float Z;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class Vector4f
    {
        public float X;
        public float Y;
        public float Z;
        public float W;
    }

    public struct CVector4f
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public CVector4f(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }


    /// <summary>
    /// A matrix of vector 4 values.
    /// </summary>
    public struct CTransform4f
    {
        public CVector4f M0;
        public CVector4f M1;
        public CVector4f M2;

        public CTransform4f(CVector4f m0, CVector4f m1, CVector4f m2)
        {
            M0 = m0;
            M1 = m1;
            M2 = m2;
        }

        public static CTransform4f Read(BinaryReader br)
        {
            return new CTransform4f
            {
                M0 = new CVector4f(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                M1 = new CVector4f(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
                M2 = new CVector4f(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()),
            };
        }

    }

    /// <summary>
    /// A color struct of RGBA values.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class Color4f
    {
        public float R;
        public float G;
        public float B;
        public float A;
    }
    #endregion

    public class CRenderOctree
    {
        public SHeader header;
        public uint bitmaskWordCount;
        public uint[] bitmaskWords;
        public uint edgeOffsetCount;
        public uint[] edgeOffsets;
        public uint dataSize;
        public byte[] data;
        public uint aaboxCount;
        public List<CAABox> aaboxes = new List<CAABox>();

        public static CRenderOctree Read(FileReader br)
        {
            CRenderOctree renderOctree = new CRenderOctree();
            renderOctree.header = SHeader.Read(br);
            renderOctree.bitmaskWordCount = br.ReadUInt32();
            renderOctree.bitmaskWords = new uint[renderOctree.bitmaskWordCount];
            for (int i = 0; i < renderOctree.bitmaskWordCount; i++)
            {
                renderOctree.bitmaskWords[i] = br.ReadUInt32();
            }
            renderOctree.edgeOffsetCount = br.ReadUInt32();
            renderOctree.edgeOffsets = new uint[renderOctree.edgeOffsetCount];
            for (int i = 0; i < renderOctree.edgeOffsetCount; i++)
            {
                renderOctree.edgeOffsets[i] = br.ReadUInt32();
            }
            renderOctree.dataSize = br.ReadUInt32();
            renderOctree.data = br.ReadBytes((int)renderOctree.dataSize);
            renderOctree.aaboxCount = br.ReadUInt32();
            for (int i = 0; i < renderOctree.aaboxCount; i++)
            {
                renderOctree.aaboxes.Add(br.ReadStruct<CAABox>());
            }
            return renderOctree;
        }
    }

    public struct SHeader
    {
        public Magic fcc;
        public uint version;
        public uint bitmaskCount;
        public uint bitmaskNumBits;
        public uint entryNodeCount;
        public CAABox bounds;

        public static SHeader Read(FileReader br)
        {
            return new SHeader
            {
                fcc = br.ReadStruct<Magic>(),
                version = br.ReadUInt32(),
                bitmaskCount = br.ReadUInt32(),
                bitmaskNumBits = br.ReadUInt32(),
                entryNodeCount = br.ReadUInt32(),
                bounds = br.ReadStruct<CAABox>()
            };
        }
    }

    /// <summary>
    /// A 128 bit guid for identifying objects.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CGuid
    {
        public uint Part1;
        public ushort Part2;
        public ushort Part3;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] Part4;

        public Guid ToGUID()
        {
            return new Guid(Part1, Part2, Part3, Part4[0], Part4[1], Part4[2], Part4[3], Part4[4], Part4[5], Part4[6], Part4[7]);
        }

        public override string ToString() //Represented based on output guids in demo files
        {
            return ToGUID().ToString();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CDataEnumValue
    {
        public uint unk1;
        public CObjectId unk2;
        public uint unk3;
        public CObjectId unk4;

        public static CDataEnumValue Read(FileReader br)
        {
            return new CDataEnumValue
            {
                unk1 = br.ReadUInt32(),
                unk2 = br.ReadStruct<CObjectId>(),
                unk3 = br.ReadUInt32(),
                unk4 = br.ReadStruct<CObjectId>()
            };
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CDataEnumBitField
    {
        public uint intCount;
        public uint[] ints;
        public uint boolCount;
        public byte[] bools;
        public CObjectId enumId;
        public CObjectId unkId;

        public static CDataEnumBitField Read(FileReader br)
        {
            CDataEnumBitField bitField = new CDataEnumBitField();
            bitField.intCount = br.ReadUInt32();
            if(bitField.intCount > 0)
            {
                bitField.ints = br.ReadUInt32s((int)bitField.intCount);
            }
            bitField.boolCount = br.ReadUInt32();
            if (bitField.boolCount > 0)
            {
                bitField.bools = br.ReadBytes((int)bitField.intCount);
            }
            bitField.enumId = br.ReadStruct<CObjectId>();
            bitField.unkId = br.ReadStruct<CObjectId>();
            return bitField;
        }
    }



    public struct SAtlasLookup
    {
        public float offsetU;
        public float offsetV;
        public float scale;
        public float unkD;

        public static SAtlasLookup Read(BinaryReader br)
        {
            return new SAtlasLookup
            {
                offsetU = br.ReadSingle(),
                offsetV = br.ReadSingle(),
                scale = br.ReadSingle(),
                unkD = br.ReadSingle()
            };
        }
    }

    public struct ObjectXF
    {
        public CObjectId id;
        public CTransform4f xf;

        public static ObjectXF Read(FileReader br)
        {
            return new ObjectXF
            {
                id = br.ReadStruct<CObjectId>(),
                xf = CTransform4f.Read(br)
            };
        }
    }

    public enum ETangentType
    {
        Linear,
        Flat,
        Smooth,
        Step,
        Clamped,
        Fixed,
    }

    public enum EInfinityType
    {
        Constant,
        Linear,
        Cycle,
        CycleRelative,
        Oscillate,
    }
}
