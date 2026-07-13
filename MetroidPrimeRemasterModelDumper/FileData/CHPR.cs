using AvaloniaToolbox.Core.IO;
using DKCTF;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using System.Numerics;
using System.Xml.Linq;
using static RetroStudioPlugin.Files.FileData.CHPR;
using static RetroStudioPlugin.Files.FileData.CHPR.SBaseInfo;
#nullable disable

namespace RetroStudioPlugin.Files.FileData
{
    public class CHPR : FileForm
    {
        public List<CharacterInfo> CharacterInfos = new List<CharacterInfo>();
        private List<Partition1> Partition1s = new List<Partition1>();
        private List<Partition2> Partition2s = new List<Partition2>();

        public CHPR(System.IO.Stream stream) : base(stream)
        {
        }

        public override void Read(FileReader reader)
        {
            reader.SetByteOrder(false);
            ReadCharacterProject(reader);
        }

        private void ReadCharacterProject(FileReader reader)
        {
            Console.WriteLine("Stream Length: " + reader.BaseStream.Length.ToString("X8"));

            /* BRUTEFORCE TO CHAR INFO */
            bool readingPartitions = true;
            while (readingPartitions)
            {
                Partition1 partition = new Partition1().Read(reader);

                if(partition.Offset + partition.Length == reader.BaseStream.Length)
                {
                    readingPartitions = false;
                }
                Partition1s.Add(partition);
            }

            // Need to find a better way to identify the second array, its size, and if it even exists.
            uint mode = 0;
            var pos = reader.Position;
            bool readingPartition2 = true;

            // Safety checks
            while (readingPartition2)
            {
                reader.BaseStream.Position += 4;

                string stringCheck = reader.ReadStringZeroTerminated();
                if (Enum.IsDefined(typeof(EVertexComponent), stringCheck))
                {
                    // Found the bone names;
                    reader.SeekBegin(pos);
                    readingPartition2 = false;
                }
                else
                {
                    reader.SeekBegin(pos);
                    // Continue reading things
                    Partition2 part = new Partition2(reader);
                    pos = reader.Position;
                }

            }
            
            /* END BRUTEFORCE TO CHAR INFO */


            Console.WriteLine("Total partitions in CHPR: " + Partition1s.Count.ToString());

            CharacterInfos.Add(new CharacterInfo(reader));
        }

        public class Partition1
        {
            public uint Offset;
            public uint Length;
            public uint Length2;

            public Partition1 Read(FileReader reader)
            {
                Partition1 part = new Partition1();
                part.Offset = reader.ReadUInt32();
                part.Length = reader.ReadUInt32();
                part.Length2 = reader.ReadUInt32();
                return part;
            }
        }

        public class Partition2
        {
            public uint tempEntry;
            public uint tempOffset;
            public uint tempSize;

            public Partition2(FileReader reader)
            {
                tempEntry = reader.ReadUInt32();
                tempOffset = reader.ReadUInt32();
                tempSize = reader.ReadUInt32();
            }
        }

        public enum EVertexComponent
        {
            Primary = 0,
            Base = 1,
        }



        public class CharacterInfo
        {
            public NamePool NamePool;

            public SAnimContext AnimContext;
            public SAbsContext AbsContext;
            public SRenderContext RenderContext;
            public List<SRenderContext> RenderContexts = new List<SRenderContext>();

            public SUnkContext5 UnkContext5;

            //public List<Matrix4x4> SkinnedInverseMatrices = new List<Matrix4x4>();
            //public List<ushort> SkinnedMatrixBoneIDs =  new List<ushort>();


            //public SUnkContext6 UnkContext6;

            //public ushort UnkContext7_1;
            //public ushort UnkContext7_2;
            //public uint UnkContext7_3;
            //public uint UnkContext7_4;
            //public uint UnkContext7_5;

            public List<BoneData> Bones = new List<BoneData>();
            public List<SkinnedBoneData> SkinnedBones = new List<SkinnedBoneData>();

            public List<SChannel> Channels = new List<SChannel>();

            public byte NumModels;
            public byte Unk4;
            public byte NumAnchorInfos;
            public ushort NumAnims;
            public ushort NumAnimParams;

            public List<SFragDecode> FragDecodes = new List<SFragDecode>();
            public List<SConstPool> ConstPools = new List<SConstPool>();

            public ushort Unk10;

            public List<CAnim> Anims = new List<CAnim>();
            public List<SAnchorInfo> AnchorInfos = new List<SAnchorInfo>();
            public List<SModelNode> ModelNodes = new List<SModelNode>();

            public SSubCharData SubCharData;

            public bool HasRenderContext;
            public bool HasUnkContext5;

            public CharacterInfo(FileReader reader)
            {
                Console.WriteLine("Position of character info: " + reader.BaseStream.Position.ToString("X8")); // For testing 
                // First Block: Name pool
                NamePool = new NamePool(reader); // Name pool
                byte unk1 = reader.ReadByte();
                byte unk2 = reader.ReadByte();
                byte[] data = reader.ReadBytes((unk1 & 0x7F) + 7);

                AnimContext = new SAnimContext(reader);  // animation context
                AbsContext = new SAbsContext(reader);  // absolute context (bind info)

                HasRenderContext = reader.ReadBoolean(); // Render context is now optional, for some reason
                if(HasRenderContext) // FUN_71008feef0?
                {
                    RenderContext = new SRenderContext(reader);
                    RenderContexts = RenderContext.CreateRenderContextList(RenderContext); // For the sake of not having to dive in and get the child contexts later on.

                    Console.WriteLine("Number of Render Contexts: " + RenderContexts.Count); // Here to double check I'm actually getting the render contexts properly
                }

                Console.WriteLine("Position of unknown context 5: " + reader.BaseStream.Position.ToString("X8")); // For testing

                HasUnkContext5 = reader.ReadBoolean();
                if (HasUnkContext5)
                {
                    UnkContext5 = new SUnkContext5(reader); // FUN_7100692dd8
                }

                Console.WriteLine("Position after new context in binary (Should be channels): " + reader.BaseStream.Position.ToString("X8")); // For testing                 

                // SChannelLoad::Load 
                byte numChannels = reader.ReadByte();
                for (int i = 0; i < numChannels; i++)
                    Channels.Add(new SChannel(reader));
                Console.WriteLine("numChannels: " + numChannels.ToString());

                NumModels = reader.ReadByte();
                Unk4 = reader.ReadByte();
                NumAnchorInfos = reader.ReadByte();
                NumAnims = reader.ReadUInt16();
                NumAnimParams = reader.ReadUInt16();

                Console.WriteLine("NumModels: " + NumModels.ToString());
                Console.WriteLine("Unk4: " + Unk4.ToString());
                Console.WriteLine("NumAnchorInfos: " + NumAnchorInfos.ToString());
                Console.WriteLine("NumAnims: " + NumAnims.ToString());
                Console.WriteLine("NumAnimParams: " + NumAnimParams.ToString());

                Console.WriteLine("Position of Frag Decode Data: " + reader.BaseStream.Position.ToString("X8")); // For testing 
                bool hasStreamFragDecodeData = reader.ReadBoolean();
                if (hasStreamFragDecodeData)
                {
                    uint fragDecodeCount = reader.ReadUInt32();
                    for (int i = 0; i < fragDecodeCount; i++)
                        FragDecodes.Add(new SFragDecode(reader));
                    Console.WriteLine("fragDecodeCount: " + fragDecodeCount.ToString());
                }

                Console.WriteLine("Position of Const Pool: " + reader.BaseStream.Position.ToString("X8")); // For testing 
                bool hasConstPool = reader.ReadBoolean();
                if (hasConstPool)
                {
                    uint constPoolCount = reader.ReadUInt32();
                    for (int i = 0; i < constPoolCount; i++)
                        ConstPools.Add(new SConstPool(reader));
                    Console.WriteLine("constPoolCount: " + constPoolCount.ToString());
                }

                ushort unk10 = reader.ReadUInt16();

                Console.WriteLine("Position of Anchor Infos: " + reader.BaseStream.Position.ToString("X8")); // For testing 
                if (NumAnchorInfos > 0)
                {
                    for (int i = 0; i < NumAnchorInfos; i++)
                        AnchorInfos.Add(new SAnchorInfo(reader));
                }


                Console.WriteLine("Position of model nodes: " + reader.BaseStream.Position.ToString("X8")); // For testing   
                if (NumModels > 0) 
                {
                    for (int i = 0; i < NumModels; i++)
                        ModelNodes.Add(new SModelNode(reader));
                }

                bool hasOptionalBlock1 = reader.ReadBoolean();
                if (hasOptionalBlock1)
                {
                    ushort countA = reader.ReadUInt16();
                    ushort countB = reader.ReadUInt16();

                    for (int i = 0; i < countA; i++)
                    {
                        var name = new CPooledName(reader);
                        uint val1 = reader.ReadUInt32();
                        uint val2 = reader.ReadUInt32();
                        uint val3 = reader.ReadUInt32();
                        uint val4 = reader.ReadUInt32();
                    }

                    if (countB > 0)
                    {
                        for (int i = 0; i < countB; i++)
                        {
                            uint val5 = reader.ReadUInt32();
                        }
                    }
                }

                SubCharData = new SSubCharData(reader);

                /*
                // Logging
                for (int i = 0; i < Channels.Count; i++)
                {
                    if (Channels[i].PooledName.HasName)
                        Console.WriteLine($"Channel[{i}] {NamePool.Strings[(int)Channels[i].PooledName.NameID]}");
                }

                for (int i = 0; i < ModelNodes.Count; i++)
                {
                    if (ModelNodes[i].Name.HasName)
                        Console.WriteLine($"ModelNode[{i}] {NamePool.Strings[(int)ModelNodes[i].Name.NameID]} {ModelNodes[i].ModelFileGuid}");
                }

                for (int i = 0; i < Anims.Count; i++)
                {
                    if (Anims[i].BaseInfo.PooledName.HasName)
                        Console.WriteLine($"Anims[{i}]_{Anims[i].BaseInfo.Type} {NamePool.Strings[(int)Anims[i].BaseInfo.PooledName.NameID]}");
                }

                for (int i = 0; i < SubCharData.SubChars.Count; i++)
                {
                    if (SubCharData.SubChars[i].Name.HasName)
                        Console.WriteLine($"SubChars[{i}] {NamePool.Strings[(int)SubCharData.SubChars[i].Name.NameID]}");
                }
                */

                for (int r = 0; r < RenderContexts.Count; r++)
                {
                    // Bone building?
                    for (int i = 0; i < RenderContexts[r].SkinnedMatrixBoneIDs.Length; i++)
                    {
                        var boneID = RenderContexts[r].SkinnedMatrixBoneIDs[i];
                        // Find the index where the node ID is located at
                        var nodeIndex = AbsContext.NodeSet.FindIndex(boneID);

                        string boneName = NamePool.GetString(AbsContext.NodeSet.NameSet, nodeIndex);
                        this.SkinnedBones.Add(new SkinnedBoneData()
                        {
                            Name = boneName,
                            BoneID = boneID,
                            NodeIDX = nodeIndex
                        });
                    }

                    // 1. Build the COMPLETE skeleton from the Absolute Context
                    for (int i = 0; i < AbsContext.NodeSet.NodeIDs.Length; i++)
                    {
                        uint nodeID = AbsContext.NodeSet.NodeIDs[i];
                        string boneName = NamePool.GetString(AbsContext.NodeSet.NameSet, i);

                        // Section5 perfectly aligns with the global NodeSet for parenting info
                        ushort parentID = AbsContext.Section5[i].Item1;
                        int parentIndex = AbsContext.NodeSet.FindIndex(parentID);

                        string parentName = "";
                        if (parentIndex >= 0)
                        {
                            parentName = NamePool.GetString(AbsContext.NodeSet.NameSet, parentIndex);
                        }

                        Bones.Add(new BoneData()
                        {
                            Name = boneName,
                            Parent = parentName,
                            // Default matrices to Identity; we will overwrite the skinned ones below
                            InverseTransform = Matrix4x4.Identity,
                            WorldTransform = Matrix4x4.Identity,
                            LocalTransform = Matrix4x4.Identity
                        });
                    }

                    // 2. Assign Render Context matrices exclusively to the skinned subset
                    HashSet<int> skinnedBoneIndices = new HashSet<int>(); // Track what we've solved
                    for (int i = 0; i < RenderContexts[r].SkinnedMatrixBoneIDs.Length; i++)
                    {
                        var boneID = RenderContexts[r].SkinnedMatrixBoneIDs[i];
                        var nodeIndex = AbsContext.NodeSet.FindIndex(boneID);

                        if (nodeIndex >= 0 && nodeIndex < Bones.Count)
                        {
                            skinnedBoneIndices.Add(nodeIndex);
                            Matrix4x4.Invert(Matrix4x4.Transpose(RenderContexts[r].SkinnedInverseMatrices[i]), out Matrix4x4 worldSpace);

                            Bones[nodeIndex].InverseTransform = RenderContexts[r].SkinnedInverseMatrices[i];
                            Bones[nodeIndex].WorldTransform = worldSpace;
                            // LocalTransform is handled in the next step
                        }
                    }


                    // This is here because Retro did not include the transforms of unskinned bones. Because of this,
                    // if one wants the rigs to not fall apart immediately, we must average the positions and rotations
                    // of the skinned bones between the neighbors. This is a temporary solution while we try to figure
                    // out how Retro is doing it.
                    bool changed = true;
                    int iterations = 0;
                    while (changed && iterations < 3)
                    {
                        changed = false;
                        iterations++;

                        for (int i = 0; i < Bones.Count; i++)
                        {
                            if (skinnedBoneIndices.Contains(i)) continue;

                            List<Vector3> translations = new List<Vector3>();
                            List<Quaternion> rotations = new List<Quaternion>();

                            // 1. Collect Neighbor Data (Parent)
                            var parentBone = Bones.FirstOrDefault(b => b.Name == Bones[i].Parent);
                            if (parentBone != null && parentBone.WorldTransform != Matrix4x4.Identity)
                            {
                                if (Matrix4x4.Decompose(parentBone.WorldTransform, out _, out Quaternion pRot, out Vector3 pTrans))
                                {
                                    translations.Add(pTrans);
                                    rotations.Add(pRot);
                                }
                            }

                            // 2. Collect Neighbor Data (Children)
                            var children = Bones.Where(b => b.Parent == Bones[i].Name).ToList();
                            foreach (var child in children)
                            {
                                if (child.WorldTransform != Matrix4x4.Identity)
                                {
                                    if (Matrix4x4.Decompose(child.WorldTransform, out _, out Quaternion cRot, out Vector3 cTrans))
                                    {
                                        translations.Add(cTrans);

                                        // Ensure the quaternion is in the same hemisphere as the first one to avoid "flipping"
                                        if (rotations.Count > 0 && Quaternion.Dot(rotations[0], cRot) < 0)
                                            cRot = Quaternion.Inverse(cRot); // Or -cRot depending on library version

                                        rotations.Add(cRot);
                                    }
                                }
                            }

                            // 3. Average and Apply
                            if (translations.Count > 0)
                            {
                                // Average Position
                                Vector3 avgPos = Vector3.Zero;
                                foreach (var t in translations) avgPos += t;
                                avgPos /= translations.Count;

                                // Average Rotation (Normalized Linear Interpolation)
                                Quaternion avgRot = rotations[0];
                                if (rotations.Count > 1)
                                {
                                    Vector4 acc = new Vector4(rotations[0].X, rotations[0].Y, rotations[0].Z, rotations[0].W);
                                    for (int x = 1; x < rotations.Count; x++)
                                    {
                                        acc += new Vector4(rotations[x].X, rotations[x].Y, rotations[x].Z, rotations[x].W);
                                    }
                                    acc /= rotations.Count;
                                    avgRot = Quaternion.Normalize(new Quaternion(acc.X, acc.Y, acc.Z, acc.W));
                                }

                                Matrix4x4 newWorld = Matrix4x4.CreateFromQuaternion(avgRot) * Matrix4x4.CreateTranslation(avgPos);

                                if (Vector3.Distance(Bones[i].WorldTransform.Translation, avgPos) > 0.001f)
                                {
                                    Bones[i].WorldTransform = newWorld;
                                    changed = true;
                                }
                            }
                        }
                    }

                    // Convert world transforms into local transforms
                    for (int i = 0; i < Bones.Count; i++)
                    {
                        if (!string.IsNullOrEmpty(Bones[i].Parent))
                        {
                            var parent =
                                Bones.FirstOrDefault(x => x.Name == Bones[i].Parent);

                            if (parent != null)
                            {
                                Matrix4x4.Invert(
                                    parent.WorldTransform,
                                    out Matrix4x4 parentInverse);

                                Bones[i].LocalTransform =
                                    Bones[i].WorldTransform * parentInverse;
                            }
                        }
                        else
                        {
                            // Root bone
                            Bones[i].LocalTransform = Bones[i].WorldTransform;
                        }
                    }
                    

                }



                /* BONE INFORMATION LOGGER */
                
                /*
                string boneIDContents = "Bone Information: ";

                boneIDContents = boneIDContents + Environment.NewLine + Environment.NewLine + "";
                for (int i = 0; i < SkinnedBones.Count; i++)
                {
                    boneIDContents = boneIDContents + Environment.NewLine + "Skinned Bone ID: " + SkinnedBones[i].BoneID.ToString() + "     Skinned Bone Node Index: " + SkinnedBones[i].NodeIDX.ToString() + "     Skinned Bone Name: " + SkinnedBones[i].Name.ToString();
                }

                System.IO.File.WriteAllText(AppContext.BaseDirectory + "/BoneIDCheck.json", boneIDContents);
                */

                // Temporary kill switch
                //throw new Exception("Hit the end of a CHPR. Kill application.");
            }
        }

        public class BoneData
        {
            public string Name;
            public string Parent;
            public uint ParentIndex;
            public Matrix4x4 InverseTransform;
            public Matrix4x4 LocalTransform = Matrix4x4.Identity;
            public Matrix4x4 WorldTransform = Matrix4x4.Identity;
        }

        public class SkinnedBoneData
        {
            public string Name;
            public uint BoneID;
            public int NodeIDX;
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

                byte[] section1 = reader.ReadBytes(num1 * 6); // Animation descriptor entries?
                byte[] section2 = reader.ReadBytes(num3); // Animation data block?
                ushort[] section3 = reader.ReadUInt16s(num2); // Pointer table?
                byte[] section4 = reader.ReadBytes(num4 - (num2 * 8)); // Auxiliary animation data?
                ushort[] section5 = reader.ReadUInt16s(num1); // Metadata or second pointer table?
                byte[] section6 = reader.ReadBytes(num5 - (num1 * 8)); // Final data segment?

                NodeSet = new SNodeSet(reader);
            }
        }

        public class SAbsContext
        {
            public SNodeSet NodeSet;

            public (ushort, ushort)[] Section5;

            public byte num1;
            public ushort num2;
            public ushort num3;
            public ushort num4;
            public ushort num5;
            public ushort num6;

            public SAbsContext(FileReader reader)
            {
                /* SAbsContext::Load  */

                num1 = reader.ReadByte(); //
                num2 = reader.ReadUInt16();
                num3 = reader.ReadUInt16();
                num4 = reader.ReadUInt16();
                num5 = reader.ReadUInt16();
                num6 = reader.ReadUInt16(); // NEW

                /* SAbsContext::ReadIntoMemory */

                byte[] section1 = reader.ReadBytes(num1 * 6);
                byte[] section2 = reader.ReadBytes(num3);
                byte[] section3 = reader.ReadBytes(num4);
                byte[] section4 = reader.ReadBytes(num5);
                byte[] section6 = reader.ReadBytes(num6); // NEW

                NodeSet = new SNodeSet(reader);

                Section5 = new (ushort, ushort)[num2];
                for (int i = 0; i < Section5.Length; i++)
                    Section5[i] = (reader.ReadUInt16(), reader.ReadUInt16());

                byte unk7 = reader.ReadByte();
            }
        }

        public class SRenderContext // Has been modified slightly
        {
            public Matrix4x4[] InverseMatrices = new Matrix4x4[0];
            public Matrix4x4[] SkinnedInverseMatrices;

            public ushort[] SkinnedMatrixBoneIDs;
            public uint[] InverseMatrixBoneIds = new uint[0];

            public bool HasSubContext;
            public SRenderContext SubContext;

            public ushort num1;
            public ushort num2;
            public ushort num3;
            public byte num4;
            public uint num5;
            public byte num6;

            public ushort[] section2;
            public ushort[] section3;

            public byte[] section4;

            public SRenderContext(FileReader reader)
            {
                num1 = reader.ReadUInt16();
                num2 = reader.ReadUInt16();
                num3 = reader.ReadUInt16();
                num4 = reader.ReadByte();
                num5 = reader.ReadUInt32();
                num6 = reader.ReadByte();

                SkinnedMatrixBoneIDs = reader.ReadUInt16s((int)num1);
                section2 = reader.ReadUInt16s((int)num2);
                section3 = reader.ReadUInt16s((int)num3);

                SkinnedInverseMatrices = reader.ReadMatrix3x4s(num1 + 1);

                //InverseMatrixBoneIds = reader.ReadUInt32s((int)num4); // Is not present

                //InverseMatrices = reader.ReadMatrix3x4s(num4); // Is not present

                section4 = reader.ReadBytes((int)num5);

                HasSubContext = reader.ReadBoolean(); // New sub context
                if (HasSubContext)
                {
                    Console.WriteLine("Has sub render context at: " + reader.BaseStream.Position.ToString("X8")); // For debugging
                    SubContext = new SRenderContext(reader);
                }
            }

            public List<SRenderContext> CreateRenderContextList(SRenderContext context)
            {
                // This exists merely for the sake of my sanity
                List<SRenderContext> renderContexts = new List<SRenderContext>();

                renderContexts = RenderContextToList(context, renderContexts);

                return renderContexts;
            }

            public static List<SRenderContext> RenderContextToList(SRenderContext context, List<SRenderContext> renderlist)
            {
                renderlist.Add(context);

                if (context.HasSubContext)
                {
                    renderlist = RenderContextToList(context.SubContext, renderlist);
                    return renderlist;
                }
                else
                {
                    return renderlist;
                }

            }

        }


        /* NEW UNKNOWN CONTEXT 5 START */

        public class SUnkContext5
        {
            public byte List1Count1;
            public ushort List1Count2;
            public byte[] List1Data;

            public ushort List2Count;
            public ushort List2Unk;
            public uint[] List2Data;

            public bool HasPolyList;
            public List<PolyBase> PolyList = new List<PolyBase>();

            public SUnkContext5(FileReader reader)
            {
                //Console.WriteLine("Has Unknown Context 5");
                // FUN_710060910c & FUN_7100692dd8
                List1Count1 = reader.ReadByte();
                List1Count2 = reader.ReadUInt16();

                // FUN_71000d5760
                List1Data = reader.ReadBytes(List1Count1 * 6);
                //Console.WriteLine("Unknown Context 5: Position after first data list: " + reader.BaseStream.Position.ToString("X8")); // for debugging

                // FUN_7100692edc
                List2Count = reader.ReadUInt16();
                List2Unk = reader.ReadUInt16();
                List2Data = reader.ReadUInt32s(List2Count);
                //Console.WriteLine("Unknown Context 5: Position after second data list: " + reader.BaseStream.Position.ToString("X8")); // for debugging

                HasPolyList = reader.ReadBoolean();
                if (HasPolyList)
                {
                    //Console.WriteLine("Unknown Context 5: Has polymorphic list");
                    //Console.WriteLine("Unknown Context 5: Position of polymorphic list: " + reader.BaseStream.Position.ToString("X8")); // for debugging

                    // FUN_71006930c8
                    uint polyCount = reader.ReadUInt32();
                    for (int i = 0; i < polyCount; i++)
                    {
                        uint typeHash = reader.ReadUInt32();
                        PolyBase polyObj = null;

                        Console.WriteLine($"Found poly hash: {typeHash:X8}");
                        Console.WriteLine("Poly hash located at: " + reader.BaseStream.Position.ToString("X8"));

                        switch (typeHash)
                        {
                            case 0x92973fbd: polyObj = new Poly_92973fbd(); break;
                            case 0x8f0cfd08: polyObj = new Poly_8f0cfd08(); break;
                            case 0xc5bb2903: polyObj = new Poly_c5bb2903(); break;
                            case 0xf1d9a313: polyObj = new Poly_f1d9a313(); break;
                            case 0xa76646e9: polyObj = new Poly_a76646e9(); break;
                            case 0x2543199d: polyObj = new Poly_2543199d(); break;
                            case 0x704064a0: polyObj = new Poly_704064a0(); break;
                            case 0x3a771b80: polyObj = new Poly_3a771b80(); break;
                            default: throw new Exception($"Unknown poly hash: 0x{typeHash:X8}");
                        }

                        polyObj?.Read(reader);
                        PolyList.Add(polyObj);
                    }


                }
            }


        }

        // ==========================================
        // POLYMORPHIC BASE & IMPLEMENTATIONS
        // ==========================================

        public abstract class PolyBase
        {
            public virtual void Read(FileReader reader) { }
        }

        // Types 0x92973fbd and 0xf1d9a313 both utilize the vtable function FUN_7100a89d3c 
        // which relies on FUN_7100594d24 for its primary structure parsing.
        public abstract class PolyCommonParse : PolyBase
        {
            public uint CountA;
            public uint CountB;

            public byte Ref1HasValue;
            public uint Ref1Value;
            public uint Unk1;
            public byte Unk2;

            public List<ElementA> ElementsA = new List<ElementA>();
            public List<ElementB> ElementsB = new List<ElementB>();

            // Parses FUN_7100594d24
            protected void ReadCommon(FileReader reader)
            {
                // FUN_71003ce8c0 conditional parsing
                Ref1HasValue = reader.ReadByte();
                if (Ref1HasValue != 0) Ref1Value = reader.ReadUInt32();

                Unk1 = reader.ReadUInt32();
                Unk2 = reader.ReadByte();

                for (int i = 0; i < CountA; i++)
                {
                    var el = new ElementA();
                    el.Ref1HasValue = reader.ReadByte();
                    if (el.Ref1HasValue != 0) el.Ref1Value = reader.ReadUInt32();

                    el.Ref2HasValue = reader.ReadByte();
                    if (el.Ref2HasValue != 0) el.Ref2Value = reader.ReadUInt32();

                    el.Val1 = reader.ReadUInt16();
                    el.Val2 = reader.ReadUInt16();
                    el.Val3 = reader.ReadUInt32();
                    el.Val4 = reader.ReadUInt32();
                    el.Val5 = reader.ReadByte() != 0;
                    el.Val6 = reader.ReadByte() != 0;
                    ElementsA.Add(el);
                }

                for (int i = 0; i < (ushort)CountB; i++)
                {
                    uint typeHash = reader.ReadUInt32();
                    ElementB el = null;

                    if (typeHash == 0x0ae72740) el = new ElementB_0ae72740();
                    else if (typeHash == 0xf651a9f7) el = new ElementB_f651a9f7();
                    else if (typeHash == 0xa5628f01) el = new ElementB_a5628f01();
                    else
                    {
                        // Throw immediately in the case of a missing hash. Document
                        // stream position to learn exactly where its data starts.
                        throw new Exception($"Unknown ElementB typeHash: 0x{typeHash:X8} at offset {reader.BaseStream.Position:X8}");
                    }

                    el.Read(reader);
                    ElementsB.Add(el);
                }
            }
        }

        // Replicates FUN_7100a89d3c which extends the base parse with an ExtraCount array
        public abstract class PolyCommonParse_WithExtra : PolyCommonParse
        {
            public uint ExtraCount;
            public List<uint> ExtraList = new List<uint>();

            protected void ReadExtra(FileReader reader)
            {
                ExtraCount = reader.ReadUInt32();
                for (int i = 0; i < ExtraCount; i++)
                {
                    ExtraList.Add(reader.ReadUInt32());
                }
            }
        }

        // VTable: PTR_FUN_71017b3c18 -> FUN_7100a89d3c
        public class Poly_92973fbd : PolyCommonParse_WithExtra
        {
            public override void Read(FileReader reader)
            {
                CountA = reader.ReadUInt32();
                CountB = reader.ReadUInt32();
                ReadCommon(reader);
                ReadExtra(reader);
            }
        }

        // Setup identical to 0x92973fbd and 0xf1d9a313 via FUN_7100693440.
        public class Poly_8f0cfd08 : PolyCommonParse_WithExtra
        {
            public override void Read(FileReader reader)
            {
                CountA = reader.ReadUInt32();
                CountB = reader.ReadUInt32();
                ReadCommon(reader);
                ReadExtra(reader);
            }
        }

        // VTable: PTR_FUN_71017b35b8 -> FUN_7100ba87bc
        public class Poly_c5bb2903 : PolyCommonParse
        {
            public ushort ValC;
            public ushort ValD;
            public byte[] Data;

            public override void Read(FileReader reader)
            {
                CountA = reader.ReadUInt32();
                CountB = reader.ReadUInt32();
                ValC = reader.ReadUInt16(); // via FUN_7100693e68

                ReadCommon(reader);         // via FUN_7100594d24

                ValD = reader.ReadUInt16(); // via FUN_7100693e68
                Data = reader.ReadBytes((int)ValC * 8);
            }
        }

        // VTable: PTR_FUN_71017b42b8 -> thunk_FUN_7100a89d3c
        /*
        public class Poly_f1d9a313 : PolyCommonParse_WithExtra
        {
            public override void Read(FileReader reader)
            {
                CountA = reader.ReadUInt32();
                CountB = reader.ReadUInt32();
                ReadCommon(reader);
                ReadExtra(reader);
            }
        }
        */

        public class Poly_f1d9a313 : PolyCommonParse
        {
            public override void Read(FileReader reader)
            {
                CountA = reader.ReadUInt32();
                CountB = reader.ReadUInt32();
                ReadCommon(reader);
                // REMOVED: ReadExtra(reader);
            }
        }

        // VTable: PTR_FUN_71017b36a0 -> FUN_7100e55ed4
        public class Poly_a76646e9 : PolyCommonParse
        {
            public uint CountC;
            public uint ValD;
            public uint ValE;
            public uint[] ArrayC;

            public override void Read(FileReader reader)
            {
                // Constructor reads (FUN_7100694194)
                CountA = reader.ReadUInt32();
                CountB = reader.ReadUInt32();
                CountC = reader.ReadUInt32();

                // Base parse (FUN_7100594d24)
                ReadCommon(reader);

                // FUN_7100669b78 remainder
                ValD = reader.ReadUInt32();
                ValE = reader.ReadUInt32();

                // FUN_7100e55ed4 remainder
                if (CountC > 0)
                {
                    ArrayC = reader.ReadUInt32s((int)CountC);
                }
            }
        }

        // VTable: PTR_LAB_71017b3f58 -> LAB_710059449c
        public class Poly_2543199d : PolyCommonParse
        {
            public ushort ValC;
            public ushort ValD;
            public uint ValE;
            public byte ValF;

            public ushort[] UnkShorts = new ushort[5];
            public uint UnkUint;
            public List<ElementD> ElementsD = new List<ElementD>();

            public byte[] Buffer1;
            public byte[] Buffer2;
            public uint[] ConditionalUints;

            public override void Read(FileReader reader)
            {
                CountA = reader.ReadUInt32();
                CountB = reader.ReadUInt32();
                ValC = reader.ReadUInt16();
                ValD = reader.ReadUInt16();
                ValE = reader.ReadUInt32();
                ValF = reader.ReadByte();

                ReadCommon(reader);

                for (int i = 0; i < 5; i++) UnkShorts[i] = reader.ReadUInt16();
                UnkUint = reader.ReadUInt32();

                for (int i = 0; i < ValC; i++)
                {
                    var el = new ElementD();
                    el.B1 = reader.ReadByte();
                    el.B2 = reader.ReadByte();
                    el.S1 = reader.ReadUInt16();
                    el.B3 = reader.ReadByte();
                    el.BoolFlag1 = reader.ReadByte() != 0;
                    el.BoolFlag2 = reader.ReadByte() != 0;
                    el.BoolFlag3 = reader.ReadByte() != 0;
                    el.LengthByte = reader.ReadByte();
                    el.B4 = reader.ReadByte();
                    el.S2 = reader.ReadUInt16();
                    el.S3 = reader.ReadUInt16();
                    el.S4 = reader.ReadUInt16();

                    // Reads lengthByte - 14. 
                    el.ExtraData = reader.ReadBytes(el.LengthByte - 14);
                    ElementsD.Add(el);
                }

                Buffer1 = reader.ReadBytes(ValD * 4);
                Buffer2 = reader.ReadBytes(ValC);

                if (ValF != 0)
                {
                    ConditionalUints = reader.ReadUInt32s(4);
                }
            }
        }

        // VTable: PTR_LAB_71017b3f18 -> FUN_7100ba90b8
        public class Poly_704064a0 : PolyCommonParse
        {
            public uint CountC;
            public uint Unk3;
            public List<ElementC> ElementsC = new List<ElementC>();

            public override void Read(FileReader reader)
            {
                CountA = reader.ReadUInt32();
                CountB = reader.ReadUInt32();
                CountC = reader.ReadUInt32();

                ReadCommon(reader);
                Unk3 = reader.ReadUInt32();

                for (int i = 0; i < CountC; i++)
                {
                    var el = new ElementC();
                    el.Val1_12 = reader.ReadUInt32s(12);
                    el.Val13 = reader.ReadByte() != 0;
                    el.Val14_18 = reader.ReadUInt32s(5);
                    ElementsC.Add(el);
                }
            }
        }

        // VTable: PTR_FUN_71017b3660 -> FUN_7100669b34
        public class Poly_3a771b80 : PolyCommonParse
        {
            public uint Unk3;
            public uint Unk4;
            public uint Unk5;

            public override void Read(FileReader reader)
            {
                CountA = reader.ReadUInt32();
                CountB = reader.ReadUInt32();
                ReadCommon(reader);
                Unk3 = reader.ReadUInt32();
                Unk4 = reader.ReadUInt32();
                Unk5 = reader.ReadUInt32();
            }
        }

        // ==========================================
        // INNER STRUCTURES FOR ElementB Arrays
        // ==========================================

        public class ElementA
        {
            public byte Ref1HasValue;
            public uint Ref1Value;
            public byte Ref2HasValue;
            public uint Ref2Value;
            public ushort Val1;
            public ushort Val2;
            public uint Val3;
            public uint Val4;
            public bool Val5;
            public bool Val6;
        }

        public abstract class ElementB
        {
            public abstract void Read(FileReader reader);
        }

        // Parses FUN_710059519c
        public class ElementB_0ae72740 : ElementB
        {
            public uint Val1;
            public uint Val2;

            public override void Read(FileReader reader)
            {
                Val1 = reader.ReadUInt32();
                Val2 = reader.ReadUInt32();
            }
        }

        // Parses FUN_7100595458
        public class ElementB_f651a9f7 : ElementB
        {
            public uint Val1, Val2, Val3;
            public byte[] Val4; // FUN_71005956c0 reads 16 bytes
            public uint Val5;
            public bool Val6;
            public uint Val7, Val8;
            public bool Val9;
            public uint Val10, Val11;
            public bool Val12;
            public uint Val13, Val14;
            public bool Val15;
            public uint Val16;
            public bool Val17;
            public uint Val18, Val19;
            public bool Val20;
            public uint Val21, Val22;
            public bool Val23;
            public uint Val24;
            public byte[] Val25; // FUN_71005956c0 reads 16 bytes

            public override void Read(FileReader reader)
            {
                Val1 = reader.ReadUInt32();  // FUN_7100595328
                Val2 = reader.ReadUInt32();
                Val3 = reader.ReadUInt32();  // ReadLong
                Val4 = reader.ReadBytes(16); // FUN_71005956c0
                Val5 = reader.ReadUInt32();  // FUN_7100595648
                Val6 = reader.ReadByte() != 0; // FUN_71005955e4
                Val7 = reader.ReadUInt32();
                Val8 = reader.ReadUInt32();
                Val9 = reader.ReadByte() != 0;
                Val10 = reader.ReadUInt32();
                Val11 = reader.ReadUInt32();
                Val12 = reader.ReadByte() != 0;
                Val13 = reader.ReadUInt32();
                Val14 = reader.ReadUInt32();
                Val15 = reader.ReadByte() != 0;
                Val16 = reader.ReadUInt32();
                Val17 = reader.ReadByte() != 0;
                Val18 = reader.ReadUInt32();
                Val19 = reader.ReadUInt32();
                Val20 = reader.ReadByte() != 0;
                Val21 = reader.ReadUInt32();
                Val22 = reader.ReadUInt32();
                Val23 = reader.ReadByte() != 0;
                Val24 = reader.ReadUInt32();
                Val25 = reader.ReadBytes(16); // FUN_71005956c0
            }
        }

        // Parses FUN_71005952dc
        public class ElementB_a5628f01 : ElementB
        {
            public uint Val1;
            public byte RefHasValue;
            public uint RefValue;
            public uint Val2;
            public uint Val3;

            public override void Read(FileReader reader)
            {
                Val1 = reader.ReadUInt32(); // FUN_7100595328
                RefHasValue = reader.ReadByte(); // FUN_71003ce8c0
                if (RefHasValue != 0) RefValue = reader.ReadUInt32();
                Val2 = reader.ReadUInt32(); // ReadLong
                Val3 = reader.ReadUInt32(); // ReadLong
            }
        }

        // Derived from FUN_7100ba90b8 layout
        public class ElementC
        {
            public uint[] Val1_12;
            public bool Val13;
            public uint[] Val14_18;
        }

        // Derived from LAB_710059449c layout
        public class ElementD
        {
            public byte B1;
            public byte B2;
            public ushort S1;
            public byte B3;
            public bool BoolFlag1;
            public bool BoolFlag2;
            public bool BoolFlag3;
            public byte LengthByte;
            public byte B4;
            public ushort S2;
            public ushort S3;
            public ushort S4;
            public byte[] ExtraData;
        }

        /* NEW UNKNOWN CONTEXT 5 END */





        public class SUnkContext6
        {
            public ushort Count1;
            public ushort Count2;
            public List<SUnkContext6Entry1> Entries1 = new List<SUnkContext6Entry1>();
            public uint[] Entries2;

            public SUnkContext6(FileReader reader)
            {
                Count1 = reader.ReadUInt16();
                Count2 = reader.ReadUInt16();

                for (int i = 0; i < Count1; i++)
                {
                    Entries1.Add(new SUnkContext6Entry1(reader));
                }

                if (Count2 > 0)
                {
                    Entries2 = reader.ReadUInt32s(Count2);
                }
            }

            public class SUnkContext6Entry1
            {
                public CPooledName Name;
                public uint Unk1;
                public uint Unk2;
                public uint Unk3;
                public uint Unk4;

                public SUnkContext6Entry1(FileReader reader)
                {
                    Name = new CPooledName(reader);
                    Unk1 = reader.ReadUInt32();
                    Unk2 = reader.ReadUInt32();
                    Unk3 = reader.ReadUInt32();
                    Unk4 = reader.ReadUInt32();
                }
            }
        }

        public class SChannel
        {
            public CPooledName PooledName;
            public ushort Unk2;
            public byte FlagsA;
            public byte FlagsB;

            public CObjectId Guid; // FIXED

            public SChannel(FileReader reader)
            {
                PooledName = new CPooledName(reader);
                Unk2 = reader.ReadUInt16();
                FlagsA = reader.ReadByte();
                FlagsB = reader.ReadByte();
                Guid = IOFileExtension.ReadID(reader); // instead of GUID
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

        public class SAnchorInfo // Removed one uint
        {
            public ushort Unk1;
            public ushort Unk2;
            public uint Unk3;
            //public uint Unk4;

            public SAnchorInfo(FileReader reader)
            {
                Unk1 = reader.ReadUInt16();
                Unk2 = reader.ReadUInt16();
                Unk3 = reader.ReadUInt32();
                //Unk4 = reader.ReadUInt32();
            }
        }

        public class SModelNode // Add more stuff
        {
            public CPooledName Name;
            public uint Unk1;
            public uint Unk2;
            public uint Unk3;           // Missing
            public ushort Unk4;         // Missing
            public bool Unk5;           // Missing
            public CObjectId Guid;      
            public uint Unk6;           // Missing
            public CObjectId ModelFileGuid; 

            public SModelNode(FileReader reader)
            {
                Name = new CPooledName(reader);
                Unk1 = reader.ReadUInt32();
                Unk2 = reader.ReadUInt32();
                Unk3 = reader.ReadUInt32();
                Unk4 = reader.ReadUInt16();
                Unk5 = reader.ReadBoolean();
                Guid = IOFileExtension.ReadID(reader);
                Unk6 = reader.ReadUInt32();
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
                Name = new CPooledName(reader); // Error here
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
                    SubChars.Add(new SSubChar(reader, DataSizes[i])); // error here
            }
        }

        public class CPooledName
        {
            public bool HasName;
            public uint NameID;

            public CPooledName(FileReader reader)
            {
                HasName = reader.ReadByte() != 0; // currently errors here
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
                var pos =  reader.Position;

                while (reader.BaseStream.Position < pos + stringTableSize)
                {
                    string temp = (reader.ReadStringZeroTerminated());
                    //Console.WriteLine("String found: " + temp);
                    Strings.Add(temp);
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