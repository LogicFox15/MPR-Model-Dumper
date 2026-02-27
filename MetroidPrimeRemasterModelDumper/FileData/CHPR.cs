using AvaloniaToolbox.Core.IO;
using DKCTF;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using static RetroStudioPlugin.Files.FileData.CHPR.SBaseInfo;

namespace RetroStudioPlugin.Files.FileData
{
    public class CHPR : FileForm
    {
        public List<CharacterInfo> CharacterInfos = new List<CharacterInfo>();

        private CAssetHeader CAssetHeader;


        public CHPR(System.IO.Stream stream) : base(stream)
        {
        }

        public override void Read(FileReader reader)
        {
            ReadCharacterProject(reader);
        }


        private void ReadCharacterProject(FileReader reader)
        {
            // Asset header
            CAssetHeader = reader.ReadStruct<CAssetHeader>();
            byte num = reader.ReadByte();
            for (int i = 0; i < num; i++)
            {
                CharacterInfos.Add(new CharacterInfo(reader));
            }
        }

        public class CharacterInfo
        {
            public NamePool NamePool;

            public List<BoneData> Bones = new List<BoneData>();
            public List<string> SkinnedBones = new List<string>();

            public List<SChannel> Channels = new List<SChannel>();
            public List<SFragDecode> FragDecodes = new List<SFragDecode>();
            public List<SConstPool> ConstPools = new List<SConstPool>();
            public List<CAnim> Anims = new List<CAnim>();
            public List<SAnchorInfo> AnchorInfos = new List<SAnchorInfo>();
            public List<SModelNode> ModelNodes = new List<SModelNode>();
            public SSubCharData SubCharData;

            public CharacterInfo(FileReader reader)
            {
                // Name pool
                NamePool = new NamePool(reader);
                byte unk1 = reader.ReadByte();
                byte unk2 = reader.ReadByte();
                byte[] data = reader.ReadBytes(8);
                SAnimContext animCtxLoadDummy = new SAnimContext(reader);
                SAbsContext absContext = new SAbsContext(reader);
                SRenderContext renderContext = new SRenderContext(reader);

                /* SChannelLoad::Load */
                byte numChannels = reader.ReadByte();
                for (int i = 0; i < numChannels; i++)
                    Channels.Add(new SChannel(reader));

                byte numModels = reader.ReadByte();
                byte unk4 = reader.ReadByte();
                byte numAnchorInfos = reader.ReadByte();
                ushort numAnims = reader.ReadUInt16();
                ushort numAnimParams = reader.ReadUInt16();

                bool hasStreamFragDecodeData = reader.ReadBoolean();
                if (hasStreamFragDecodeData)
                {
                    uint fragDecodeCount = reader.ReadUInt32();
                    for (int i = 0; i < fragDecodeCount; i++)
                        FragDecodes.Add(new SFragDecode(reader));
                }

                bool hasConstPool = reader.ReadBoolean();
                if (hasConstPool)
                {
                    uint constPoolCount = reader.ReadUInt32();
                    for (int i = 0; i < constPoolCount; i++)
                        ConstPools.Add(new SConstPool(reader));
                }

                ushort unk10 = reader.ReadUInt16();
                if (numAnims > 0)
                {
                    for (int i = 0; i < numAnims; i++)
                        Anims.Add(new CAnim(reader));
                }

                if (numAnchorInfos > 0)
                {
                    for (int i = 0; i < numAnchorInfos; i++)
                        AnchorInfos.Add(new SAnchorInfo(reader));
                }

                if (numModels > 0)
                {
                    for (int i = 0; i < numModels; i++)
                        ModelNodes.Add(new SModelNode(reader));
                }

                SubCharData = new SSubCharData(reader);


                for (int i = 0; i < Channels.Count; i++)
                {
                    if (Channels[i].PooledName.HasName)
                        Console.WriteLine($"Channel[{i}] {NamePool.Strings[(int)Channels[i].PooledName.NameID]}");
                }

                for (int i = 0; i < ModelNodes.Count; i++)
                {
                    if (ModelNodes[i].Name.HasName)
                        Console.WriteLine(
                            $"ModelNode[{i}] {NamePool.Strings[(int)ModelNodes[i].Name.NameID]} {ModelNodes[i].ModelFileGuid}");
                }

                for (int i = 0; i < Anims.Count; i++)
                {
                    if (Anims[i].BaseInfo.PooledName.HasName)
                        Console.WriteLine(
                            $"Anims[{i}]_{Anims[i].BaseInfo.Type} {NamePool.Strings[(int)Anims[i].BaseInfo.PooledName.NameID]}");
                }

                for (int i = 0; i < SubCharData.SubChars.Count; i++)
                {
                    if (SubCharData.SubChars[i].Name.HasName)
                        Console.WriteLine(
                            $"SubChars[{i}] {NamePool.Strings[(int)SubCharData.SubChars[i].Name.NameID]}");
                }

                for (int i = 0; i < renderContext.SkinnedMatrixBoneIDs.Length; i++)
                {
                    var boneID = renderContext.SkinnedMatrixBoneIDs[i];
                    // Find the index where the node ID is located at
                    var nodeIndex = absContext.NodeSet.FindIndex(boneID);

                    string boneName = NamePool.GetString(absContext.NodeSet.NameSet, nodeIndex);
                    this.SkinnedBones.Add(boneName);
                }

                for (int i = 0; i < renderContext.InverseMatrices.Length; i++)
                {
                    // Bone ID
                    var boneID = renderContext.InverseMatrixBoneIds[i];
                    // Parent ID
                    var parentD = absContext.Section5[i].Item1;
                    // Find the index where the parent ID is located at
                    var parentIndex = absContext.NodeSet.FindIndex(parentD);
                    // Find the index where the node ID is located at
                    var nodeIndex = absContext.NodeSet.FindIndex(boneID);

                    string parentName = "";
                    if (parentIndex != -1)
                        parentName = NamePool.GetString(absContext.NodeSet.NameSet, parentIndex);

                    Matrix4x4.Invert(Matrix4x4.Transpose(renderContext.InverseMatrices[i]), out Matrix4x4 worldSpace);

                    Bones.Add(new BoneData()
                    {
                        Name = NamePool.GetString(absContext.NodeSet.NameSet, nodeIndex),
                        Parent = parentName,
                        InverseTransform = renderContext.InverseMatrices[i],
                        WorldTransform = worldSpace,
                        LocalTransform = worldSpace, // Adjusted afterward
                    });
                }

                for (int i = 0; i < Bones.Count; i++)
                {
                    // Use parents to bring to local space
                    if (!string.IsNullOrEmpty(Bones[i].Parent))
                    {
                        var parent = Bones.FirstOrDefault(x => x.Name == Bones[i].Parent);
                        Matrix4x4.Invert(parent.WorldTransform, out Matrix4x4 inverted);

                        Bones[i].LocalTransform = Bones[i].WorldTransform * inverted;
                    }
                }


                Console.WriteLine($"SAnimContext");

                for (int i = 0; i < animCtxLoadDummy.NodeSet.NodeIDs.Length; i++)
                {
                    string name = NamePool.GetString(animCtxLoadDummy.NodeSet.NameSet, i);
                    var idx = animCtxLoadDummy.NodeSet.GetBoneID(i);
                    var parentIdx = animCtxLoadDummy.NodeSet.GetBoneParentID(i);
                    Console.WriteLine($"{idx} {parentIdx} {name}");
                }

                Console.WriteLine($"SAbsContext");

                for (int i = 0; i < absContext.NodeSet.NodeIDs.Length; i++)
                {
                    string name = NamePool.GetString(absContext.NodeSet.NameSet, i);
                    var idx = absContext.NodeSet.GetBoneID(i);
                    var parentIdx = absContext.NodeSet.GetBoneParentID(i);
                    Console.WriteLine($"{idx} {parentIdx} {name}");
                }
            }
        }

        public class BoneData
        {
            public string Name;
            public string Parent;
            public Matrix4x4 InverseTransform;
            public Matrix4x4 LocalTransform = Matrix4x4.Identity;
            public Matrix4x4 WorldTransform = Matrix4x4.Identity;
        }

        public class SNodeSet
        {
            public uint[] NodeIDs;
            public ushort NameSet;

            public SNodeSet(FileReader reader)
            {
                ushort numNodes = reader.ReadUInt16();
                NameSet = reader.ReadUInt16();
                NodeIDs = reader.ReadUInt32s(numNodes);
            }

            public int FindIndex(uint id)
            {
                for (int i = 0; i < NodeIDs.Length; i++)
                {
                    if ((NodeIDs[i] & 0xFFFF) == id)
                        return i;
                }

                return -1;
            }

            public int GetBoneID(int index)
            {
                return (int)(NodeIDs[index] >> 16) & 0xFFFF;
                ;
            }

            public int GetBoneParentID(int index)
            {
                return (int)(NodeIDs[index] & 0xFFFF);
            }
        }

        public class SAnimContext
        {
            public SNodeSet NodeSet;

            public SAnimContext(FileReader reader)
            {
                /* SAnimContext::Load  */

                byte num1 = reader.ReadByte();
                byte num2 = reader.ReadByte();
                ushort num3 = reader.ReadUInt16();
                ushort num4 = reader.ReadUInt16();
                ushort num5 = reader.ReadUInt16();

                /* SAnimContext::ReadIntoMemory */

                byte[] section1 = reader.ReadBytes(num1 * 6);
                byte[] section2 = reader.ReadBytes(num3);
                ushort[] section3 = reader.ReadUInt16s(num2);
                byte[] section4 = reader.ReadBytes(num4 - (num2 * 8));
                ushort[] section5 = reader.ReadUInt16s(num1);
                byte[] section6 = reader.ReadBytes(num5 - (num1 * 8));
                NodeSet = new SNodeSet(reader);
            }
        }

        public class SAbsContext
        {
            public SNodeSet NodeSet;

            public (ushort, ushort)[] Section5;

            public SAbsContext(FileReader reader)
            {
                /* SAbsContext::Load  */

                byte num1 = reader.ReadByte();
                ushort num2 = reader.ReadUInt16();
                ushort num3 = reader.ReadUInt16();
                ushort num4 = reader.ReadUInt16();
                ushort num5 = reader.ReadUInt16();

                /* SAbsContext::ReadIntoMemory */

                byte[] section1 = reader.ReadBytes(num1 * 6);
                byte[] section2 = reader.ReadBytes(num3);
                byte[] section3 = reader.ReadBytes(num4);
                byte[] section4 = reader.ReadBytes(num5);

                NodeSet = new SNodeSet(reader);

                Section5 = new (ushort, ushort)[num2];
                for (int i = 0; i < Section5.Length; i++)
                    Section5[i] = (reader.ReadUInt16(), reader.ReadUInt16());

                byte unk6 = reader.ReadByte();
            }
        }

        public class SRenderContext
        {
            public Matrix4x4[] InverseMatrices;
            public Matrix4x4[] SkinnedInverseMatrices;

            public ushort[] SkinnedMatrixBoneIDs;
            public ushort[] InverseMatrixBoneIds;

            public SRenderContext(FileReader reader)
            {
                ushort num1 = reader.ReadUInt16();
                ushort num2 = reader.ReadUInt16();
                ushort num3 = reader.ReadUInt16();
                ushort num4 = reader.ReadUInt16();
                uint num5 = reader.ReadUInt32();

                SkinnedMatrixBoneIDs = reader.ReadUInt16s((int)num1);
                ushort[] section2 = reader.ReadUInt16s((int)num2);
                ushort[] section3 = reader.ReadUInt16s((int)num3);
                InverseMatrixBoneIds = reader.ReadUInt16s((int)num4);

                SkinnedInverseMatrices = reader.ReadMatrix3x4s(num1 + 1);
                InverseMatrices = reader.ReadMatrix3x4s(num4);
                byte[] section4 = reader.ReadBytes((int)num5);
            }
        }

        public class SChannel
        {
            public CPooledName PooledName;
            public ushort Unk2;
            public byte Unk3;
            public byte Unk4;

            public CObjectId Guid;

            public SChannel(FileReader reader)
            {
                PooledName = new CPooledName(reader);
                Unk2 = reader.ReadUInt16();
                Unk3 = reader.ReadByte();
                Unk4 = reader.ReadByte();
                Guid = IOFileExtension.ReadID(reader);
            }
        }

        public class SFragDecode
        {
            public ushort Unknown1;
            public ushort[] Unknowns2;

            public SFragDecode(FileReader reader)
            {
                Unknown1 = reader.ReadUInt16();
                ushort count = reader.ReadUInt16();
                Unknowns2 = reader.ReadUInt16s(count);
            }
        }

        public class SConstPool
        {
            public ushort Unknown1;
            public float[] Unknowns2;

            public SConstPool(FileReader reader)
            {
                Unknown1 = reader.ReadUInt16();
                ushort count = reader.ReadUInt16();
                Unknowns2 = reader.ReadSingles(count);
            }
        }

        public class SBaseInfo
        {
            public AnimType Type;
            public byte Unk1;

            public ushort Index;

            public byte Unk3;

            public byte Unk4;
            public byte Unk5;

            public byte Unk6;
            public byte Unk7;

            public CPooledName PooledName;

            public SBaseInfo(FileReader reader)
            {
                Type = (AnimType)reader.ReadByte();
                Unk1 = reader.ReadByte();
                Index = reader.ReadUInt16();
                Unk3 = reader.ReadByte();
                Unk4 = reader.ReadByte();
                if (Unk4 != 0)
                    Unk5 = reader.ReadByte();
                Unk6 = reader.ReadByte();
                if (Unk6 != 0)
                    Unk7 = reader.ReadByte();

                PooledName = new CPooledName(reader);
            }

            public enum AnimType : byte
            {
                CompStream,
                Sequence,
                Grid,
            }
        }

        public class CAnimCompStream
        {
            public uint TotalSize;
            public ushort DataOffset;
            public ushort Unk3;
            public uint Unk4;
            public float Scale;
            public ushort Unk6;
            public uint Unk7;
            public ushort Unk8;

            public byte[] Data;

            public CPooledName PooledName;

            public CAnimCompStream(FileReader reader)
            {
                TotalSize = reader.ReadUInt32();
                DataOffset = reader.ReadUInt16();
                Unk3 = reader.ReadUInt16();
                Unk4 = reader.ReadUInt32();
                Scale = reader.ReadSingle();
                Unk6 = reader.ReadUInt16();
                Unk7 = reader.ReadUInt32();
                Unk8 = reader.ReadUInt16();

                Data = reader.ReadBytes((int)(TotalSize - DataOffset));
            }
        }

        public class CAnimSequence
        {
            public byte Unk1;
            public byte Unk2;

            public byte[] Data;

            public float Unk3;

            public CPooledName PooledName;

            public List<CAnimSequenceEntry> Entries = new List<CAnimSequenceEntry>();

            public CAnimSequence(FileReader reader)
            {
                var numEntries = reader.ReadUInt32();
                var dataSize = reader.ReadUInt32();
                Unk1 = reader.ReadByte();
                Unk2 = reader.ReadByte();

                for (int i = 0; i < numEntries; i++)
                {
                    Entries.Add(new CAnimSequenceEntry()
                    {
                        Unk1 = reader.ReadByte(),
                        Unk2 = reader.ReadByte(),
                        Unk3 = reader.ReadByte(),
                        Unk4 = reader.ReadUInt16(),
                        Unk5 = reader.ReadUInt32(),
                    });
                }

                Data = reader.ReadBytes((int)dataSize);
                Unk3 = reader.ReadSingle();
            }

            public class CAnimSequenceEntry
            {
                public byte Unk1;
                public byte Unk2;
                public byte Unk3;
                public ushort Unk4;
                public uint Unk5;
            }
        }

        public class CGridPointData
        {
            public byte PointCount;
            public byte Unk1;
            public byte Unk2;
            public byte Unk3;
            public byte Unk4;
            public byte[] Unk5;
            public byte[] Unk6;

            public CGridPointData(FileReader reader)
            {
                PointCount = reader.ReadByte();
                Unk1 = reader.ReadByte();
                Unk2 = reader.ReadByte();
                Unk3 = reader.ReadByte();
                Unk4 = reader.ReadByte();
                Unk5 = reader.ReadBytes((int)PointCount);
                Unk6 = reader.ReadBytes((int)(Unk1 == 0 ? PointCount * 4 : 4));
            }
        }

        public class CGridGeo2d
        {
            public CGridPointData GridPointData;

            public byte[] Unk3;
            public byte[] Unk4;

            public byte[] Unk5;
            public byte[] Unk6;
            public byte[] Unk7;

            public CGridGeo2d(FileReader reader)
            {
                GridPointData = new CGridPointData(reader);
                byte num1 = reader.ReadByte();
                byte num2 = reader.ReadByte();
                byte num3 = reader.ReadByte();

                Unk3 = reader.ReadBytes(8);
                Unk4 = reader.ReadBytes(8);

                Unk5 = reader.ReadBytes(num1 * 4);
                Unk6 = reader.ReadBytes(num2 * 8);
                Unk7 = reader.ReadBytes(num3 * 4);
            }
        }

        public class CGridGeo1d
        {
            public CGridPointData GridPointData;

            public float Unk3;
            public float Unk4;

            public byte[] Buffer;

            public CGridGeo1d(FileReader reader)
            {
                GridPointData = new CGridPointData(reader);
                Unk3 = reader.ReadSingle();
                Unk4 = reader.ReadSingle();
                Buffer = reader.ReadBytes(GridPointData.PointCount * 4);
            }
        }

        public class CAnimGrid
        {
            public CGridGeo1d Grid1;
            public CGridGeo2d Grid2;
            public byte Type;

            public uint[] Unknowns2;
            public uint[] Unknowns3;

            public CAnimGrid(FileReader reader)
            {
                var numUnk3 = reader.ReadInt32();
                Type = reader.ReadByte();
                switch (Type)
                {
                    case 0: // None
                        break;
                    case 1:
                        Grid1 = new CGridGeo1d(reader);
                        break;
                    case 2:
                        Grid2 = new CGridGeo2d(reader);
                        break;
                }

                var numUnk2 = reader.ReadInt32();
                if (numUnk2 > 0)
                {
                    Unknowns2 = reader.ReadUInt32s(numUnk2);
                }

                if (numUnk3 > 0)
                {
                    Unknowns3 = reader.ReadUInt32s(numUnk3);
                }
            }
        }

        public class CAnim
        {
            public SBaseInfo BaseInfo;
            public CAnimCompStream AnimCompStream;
            public CAnimSequence AnimSequence;
            public CAnimGrid AnimGrid;

            public CAnim(FileReader reader)
            {
                BaseInfo = new SBaseInfo(reader);
                switch (BaseInfo.Type)
                {
                    case AnimType.CompStream:
                        AnimCompStream = new CAnimCompStream(reader);
                        break;
                    case AnimType.Sequence:
                        AnimSequence = new CAnimSequence(reader);
                        break;
                    case AnimType.Grid:
                        AnimGrid = new CAnimGrid(reader);
                        break;
                }
            }
        }

        public class SAnchorInfo
        {
            public ushort Unk1;
            public ushort Unk2;
            public uint Unk3;
            public uint Unk4;

            public SAnchorInfo(FileReader reader)
            {
                Unk1 = reader.ReadUInt16();
                Unk2 = reader.ReadUInt16();
                Unk3 = reader.ReadUInt32();
                Unk4 = reader.ReadUInt32();
            }
        }

        public class SModelNode
        {
            public CPooledName Name;
            public uint Unk1;
            public uint Unk2;
            public CObjectId Guid;
            public CObjectId ModelFileGuid;

            public SModelNode(FileReader reader)
            {
                Name = new CPooledName(reader);
                Unk1 = reader.ReadUInt32();
                Unk2 = reader.ReadUInt32();
                Guid = IOFileExtension.ReadID(reader);
                ModelFileGuid = IOFileExtension.ReadID(reader);
            }
        }

        public class SSubChar
        {
            public CPooledName Name;
            public CObjectId Guid;

            public byte[] Unk1;
            public byte[] Unk2;

            public SSubChar(FileReader reader, uint size)
            {
                Name = new CPooledName(reader);
                Guid = IOFileExtension.ReadID(reader);
                byte num = reader.ReadByte();
                // total bytes = size
                Unk1 = reader.ReadBytes((int)(num * -12 + size));
                Unk2 = reader.ReadBytes((int)(num * 12));
            }
        }

        public class SSubCharData
        {
            public uint[] DataSizes;
            public List<SSubChar> SubChars = new List<SSubChar>();

            public SSubCharData(FileReader reader)
            {
                ushort numSub = reader.ReadUInt16();
                DataSizes = reader.ReadUInt32s((int)numSub);
                reader.ReadUInt16();
                for (int i = 0; i < numSub; i++)
                    SubChars.Add(new SSubChar(reader, DataSizes[i]));
            }
        }

        public class CPooledName
        {
            public bool HasName;
            public uint NameID;

            public CPooledName(FileReader reader)
            {
                HasName = reader.ReadByte() != 0;
                if (HasName)
                    NameID = reader.ReadUInt32();
            }
        }

        public class CTransform4f
        {
            public Matrix4x4 Transform;

            public CTransform4f(FileReader reader)
            {
                Transform = reader.ReadMatrix3x4();
            }
        }

        public class NamePool
        {
            public List<string> Strings = new List<string>();
            public List<HashList> HashLists = new List<HashList>();

            public uint[] StringGroupCounts;

            public NamePool(FileReader reader)
            {
                // String list
                uint stringTableSize = reader.ReadUInt32();
                var pos = reader.Position;
                while (reader.BaseStream.Position < pos + stringTableSize)
                {
                    Strings.Add(reader.ReadStringZeroTerminated());
                }

                reader.SeekBegin(pos + stringTableSize);

                ushort groupCount = reader.ReadUInt16();
                ushort hashCount = reader.ReadUInt16();
                StringGroupCounts = reader.ReadUInt32s(groupCount);
                uint[] nameHashCounts = reader.ReadUInt32s(hashCount);
                uint numTotalStrings = reader.ReadUInt32();

                for (int i = 0; i < hashCount; i++)
                    HashLists.Add(new HashList(reader));
            }

            public string GetString(int set, int index)
            {
                var stringIdx = HashLists[set].StringIDs[index];
                return this.Strings[(int)stringIdx];
            }

            public string GetString(CPooledName name)
            {
                if (name.HasName)
                    return this.Strings[(int)name.NameID];
                return "";
            }
        }

        public class HashList
        {
            public uint[] Hashes;
            public uint[] StringIDs;

            public HashList(FileReader reader)
            {
                uint count = reader.ReadUInt32();
                Hashes = reader.ReadUInt32s((int)count);
                StringIDs = reader.ReadUInt32s((int)count);
            }

            public void Write(FileWriter writer)
            {
                writer.Write(this.Hashes.Length);
                writer.Write(this.Hashes);
                writer.Write(this.StringIDs);
            }
        }
    }
}