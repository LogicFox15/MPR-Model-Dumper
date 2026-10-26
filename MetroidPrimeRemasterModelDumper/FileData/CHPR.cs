using AvaloniaToolbox.Core.IO;
using DKCTF;
using System;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Xml.Linq;
#nullable disable

namespace DKCTF
{
    public class CHPR : FileForm
    {
        public List<CharacterInfo> CharacterInfos = new List<CharacterInfo>();

        private List<UnknownArrayEntry> array1 = new List<UnknownArrayEntry>();
        private List<AnimationArrayEntry> animationClipArray = new List<AnimationArrayEntry>();
        public List<AnimationArrayEntry> animations => animationClipArray;

        #region Meta Data Variables
        public CCharInfoMetaData Meta;
        private uint arrayCount1;
        private uint storedAnimationCount;
        private uint packedField;
        #endregion

        public CHPR() { }

        public CHPR(System.IO.Stream stream) : base(stream)
        {
        }

        public override void Read(FileReader reader)
        {
            reader.SetByteOrder(false);
            long payloadStart = reader.Position;
            long bodyEnd = ReadMetaFooter(reader);

            if (Meta?.loaderDescriptor == null)
            {
                throw new InvalidDataException("CHPR metadata was not loaded. Check whether the input stream contains a metadata footer.");
            }

            ReadCharacterProject(reader, bodyEnd, payloadStart);
        }

        public override void ReadMetaData(FileReader reader, CFormDescriptor pakVersion)
        {
            Meta = new CCharInfoMetaData();
            Meta.assetType = reader.ReadBytes(2);
            Meta.versionA = reader.ReadByte();
            Meta.versionB = reader.ReadByte();
            Meta.loaderDescriptor = new CCharInfoMetaDataLoaderDescriptor();
            Meta.loaderDescriptor.packedCountsAndFlags = reader.ReadUInt32();
            // array counts
            arrayCount1 = (Meta.loaderDescriptor.packedCountsAndFlags >> 1) & 0xFF;
            storedAnimationCount = Meta.loaderDescriptor.packedCountsAndFlags >> 12;
            packedField = (Meta.loaderDescriptor.packedCountsAndFlags >> 9) & 7;

            Meta.loaderDescriptor.allocationSizeInput = reader.ReadUInt32();
            Meta.loaderDescriptor.charInfoBodyOffset = reader.ReadUInt32();
            Meta.loaderDescriptor.charInfoBodySize = reader.ReadUInt32();
            Meta.loaderDescriptor.additionalOffsetOrBoundary = reader.ReadUInt32();

            Meta.loaderDescriptor.arrayAInitialEntry = new CCharInfoMetaDataLoaderTableEntry();
            Meta.loaderDescriptor.arrayBInitialEntry = new CCharInfoMetaDataLoaderTableEntry();

            Meta.loaderDescriptor.arrayAInitialEntry.field0 = reader.ReadUInt32();
            Meta.loaderDescriptor.arrayAInitialEntry.field4 = reader.ReadUInt32();
            Meta.loaderDescriptor.arrayAInitialEntry.field8 = reader.ReadUInt32();

            Meta.loaderDescriptor.arrayBInitialEntry.field0 = reader.ReadUInt32();
            Meta.loaderDescriptor.arrayBInitialEntry.field4 = reader.ReadUInt32();
            Meta.loaderDescriptor.arrayBInitialEntry.field8 = reader.ReadUInt32();
        }

        public override void WriteMetaData(FileWriter writer, CFormDescriptor pakVersion)
        {
            long startPosition = writer.BaseStream.Position;

            if (Meta?.loaderDescriptor == null)
                throw new InvalidDataException("Cannot write CHPR metadata before it has been read.");

            if (Meta.assetType == null || Meta.assetType.Length != 2)
                throw new InvalidDataException("CHPR metadata assetType must contain exactly 2 bytes.");

            CCharInfoMetaDataLoaderDescriptor d = Meta.loaderDescriptor;

            if (d.arrayAInitialEntry == null || d.arrayBInitialEntry == null)
                throw new InvalidDataException("CHPR metadata is missing an initial table entry.");

            writer.Write(Meta.assetType);
            writer.Write(Meta.versionA);
            writer.Write(Meta.versionB);

            writer.Write(d.packedCountsAndFlags);
            writer.Write(d.allocationSizeInput);
            writer.Write(d.charInfoBodyOffset);
            writer.Write(d.charInfoBodySize);
            writer.Write(d.additionalOffsetOrBoundary);

            writer.Write(d.arrayAInitialEntry.field0);
            writer.Write(d.arrayAInitialEntry.field4);
            writer.Write(d.arrayAInitialEntry.field8);

            writer.Write(d.arrayBInitialEntry.field0);
            writer.Write(d.arrayBInitialEntry.field4);
            writer.Write(d.arrayBInitialEntry.field8);

            if (writer.BaseStream.Position - startPosition != 0x30)
                throw new InvalidDataException("CHPR metadata serialization did not write exactly 0x30 bytes.");
        }

        private void ReadCharacterProject(FileReader reader, long bodyEnd, long payloadStart)
        {
            for (int i = 0; i < arrayCount1; i++)
                array1.Add(UnknownArrayEntry.Read(reader));

            for (int i = 0; i < storedAnimationCount; i++)
                animationClipArray.Add(AnimationArrayEntry.Read(reader));

            if (reader.Position > bodyEnd)
            {
                throw new InvalidDataException("CHPR partition tables extend beyond the body boundary.");
            }

            var characterInfo = new CharacterInfo(reader);
            CharacterInfos.Add(characterInfo);

            // Build Animations
            ReadAnimationPayloads(reader, payloadStart, bodyEnd);
        }

        #region Header Arrays
        public class UnknownArrayEntry
        {
            public uint Offset;
            public uint Length;
            public uint Length2;

            public static UnknownArrayEntry Read(FileReader reader)
            {
                UnknownArrayEntry part = new UnknownArrayEntry();
                part.Offset = reader.ReadUInt32();
                part.Length = reader.ReadUInt32();
                part.Length2 = reader.ReadUInt32();
                return part;
            }
        }

        public class AnimationArrayEntry
        {
            public uint OutputSlot;
            public uint Offset;
            public uint Size;
            // The parsed animation, when decoding succeeds.
            public CHPRAnimationDecoder.CAnim Animation;

            // Parsed CompStream structure, populated independently of the top-level CAnim parse.
            public CHPRCompStreamAnimation.ParsedClip CompStreamClip;
            public string CompStreamParseError;

            // Resolved game clip name from the CHPR name tables, when available.
            public string GameAnimationName;
            public int GameAnimationNameIndex = -1;

            // Number of bytes consumed by CAnim.
            public int BytesParsed;
            // Any remaining bytes after the parsed animation.
            public byte[] TrailingData = Array.Empty<byte>();
            // Diagnostic information if decoding fails.
            public string ParseError;

            public static AnimationArrayEntry Read(FileReader reader)
            {
                AnimationArrayEntry part = new AnimationArrayEntry();
                part.OutputSlot = reader.ReadUInt32();
                part.Offset = reader.ReadUInt32();
                part.Size = reader.ReadUInt32();
                return part;
            }
        }
        #endregion

        public class CharacterInfo
        {
            public NamePool NamePool;

            public SAnimContext AnimContext;
            public SAbsContext AbsContext;

            public bool HasRenderContext;
            public SRenderContext RenderContext;
            public List<SRenderContext> RenderContexts = new List<SRenderContext>();

            public bool HasUnkContext5;
            public SUnkContext5 UnkContext5;

            public List<SChannel> Channels = new List<SChannel>();

            public byte NumModels;
            public byte Unk4;
            public byte NumAnchorInfos;
            public ushort NumAnims;
            public ushort NumAnimParams;

            public bool HasStreamFragDecodeData;
            public List<SFragDecode> FragDecodes = new List<SFragDecode>();

            public bool HasConstPool;
            public List<SConstPool> ConstPools = new List<SConstPool>();

            public ushort Unk10;

            public List<SAnchorInfo> AnchorInfos = new List<SAnchorInfo>();
            public List<SModelNode> ModelNodes = new List<SModelNode>();

            public bool HasUnkContext6;
            public SUnkContext6 UnkContext6;
            public SSubCharData SubCharData;
            public SUnkContext7 UnkContext7;
            public SUnkContext8 UnkContext8;

            public List<SAnimParamNode> AnimParamNodes = new List<SAnimParamNode>();
            public List<byte[]> AnimDescriptorValues = new List<byte[]>();
            public List<uint> AnimDescriptorWords = new List<uint>();

            public bool HasCollisionPrimData;
            public SAnimCollisionPrimData CollisionPrimData;
            public bool HasEffectState;
            public SEffectState EffectState;
            public bool HasActionData;
            public SActionData ActionData;
            public bool HasEmbeddedAnimData;
            public SEmbeddedAnimData EmbeddedAnimData;
            public bool HasEventStreamDataSet;
            public SEventStreamDataSet EventStreamDataSet;

            public CObjectId FinalGuid1;
            public CObjectId FinalGuid2;

            public List<byte[]> FinalValues16 = new List<byte[]>();

            public List<BoneData> Bones = new List<BoneData>();
            public List<SkinnedBoneData> SkinnedBones = new List<SkinnedBoneData>();


            public CharacterInfo(FileReader reader)
            {
                Console.WriteLine("Position of character info: " + reader.BaseStream.Position.ToString("X8")); // For testing 
                // First Block: Name pool
                NamePool = new NamePool(reader); // Name pool
                byte unk1 = reader.ReadByte();
                byte unk2 = reader.ReadByte();
                byte[] data = ReadExactBytes(reader, (unk1 & 0x7F) + 7, "CCharInfo pre-context block");

                AnimContext = new SAnimContext(reader);  // animation context
                AbsContext = new SAbsContext(reader);  // absolute context (bind info)

                HasRenderContext = reader.ReadBoolean(); // Render context is now optional, for some reason
                if (HasRenderContext) // FUN_71008feef0?
                {
                    RenderContext = new SRenderContext(reader);
                    RenderContexts = RenderContext.CreateRenderContextList(RenderContext); // For the sake of not having to dive in and get the child contexts later on.
                }

                HasUnkContext5 = reader.ReadBoolean();
                if (HasUnkContext5)
                {
                    UnkContext5 = new SUnkContext5(reader); // FUN_7100692dd8
                }

                byte numChannels = reader.ReadByte();
                // Console.WriteLine("numChannels: " + numChannels.ToString());
                for (int i = 0; i < numChannels; i++)
                {
                    Channels.Add(new SChannel(reader));
                }

                NumModels = reader.ReadByte();
                Unk4 = reader.ReadByte();
                NumAnchorInfos = reader.ReadByte();
                NumAnims = reader.ReadUInt16();
                NumAnimParams = reader.ReadUInt16();

                HasStreamFragDecodeData = reader.ReadBoolean();
                if (HasStreamFragDecodeData)
                {
                    uint fragDecodeCount = reader.ReadUInt32();
                    for (int i = 0; i < fragDecodeCount; i++)
                    {
                        FragDecodes.Add(new SFragDecode(reader));
                    }
                }

                HasConstPool = reader.ReadBoolean();
                if (HasConstPool)
                {
                    uint constPoolCount = reader.ReadUInt32();
                    for (int i = 0; i < constPoolCount; i++)
                    {
                        ConstPools.Add(new SConstPool(reader));
                    }
                }
                ushort unk10 = reader.ReadUInt16();
                if (NumAnchorInfos > 0)
                {
                    for (int i = 0; i < NumAnchorInfos; i++)
                    {
                        AnchorInfos.Add(new SAnchorInfo(reader));
                    }
                }
                if (NumModels > 0)
                {
                    for (int i = 0; i < NumModels; i++)
                    {
                        ModelNodes.Add(new SModelNode(reader));
                    }
                }

                HasUnkContext6 = reader.ReadBoolean();
                if (HasUnkContext6)
                {
                    UnkContext6 = new SUnkContext6(reader);
                }

                SubCharData = new SSubCharData(reader);
                UnkContext7 = new SUnkContext7(reader);
                UnkContext8 = new SUnkContext8(reader);

                for (int i = 0; i < NumAnimParams; i++)
                {
                    AnimParamNodes.Add(new SAnimParamNode(reader));
                }
                for (int i = 0; i < NumAnims; i++)
                {
                    AnimDescriptorValues.Add(ReadExactBytes(reader, 16, "animation descriptor 16-byte value"));
                }
                for (int i = 0; i < NumAnims; i++)
                {
                    AnimDescriptorWords.Add(reader.ReadUInt32());
                }

                HasCollisionPrimData = reader.ReadBoolean();
                if (HasCollisionPrimData)
                {
                    CollisionPrimData = new SAnimCollisionPrimData(reader);
                }

                HasEffectState = reader.ReadBoolean();
                if (HasEffectState)
                {
                    EffectState = new SEffectState(reader);
                }

                HasActionData = reader.ReadBoolean();
                if (HasActionData)
                {
                    ActionData = new SActionData(reader);
                }

                HasEmbeddedAnimData = reader.ReadBoolean();
                if (HasEmbeddedAnimData)
                {
                    EmbeddedAnimData = new SEmbeddedAnimData(reader);
                }

                HasEventStreamDataSet = reader.ReadBoolean();
                if (HasEventStreamDataSet)
                {
                    EventStreamDataSet = new SEventStreamDataSet(reader);
                }

                FinalGuid1 = IOFileExtension.ReadID(reader);
                FinalGuid2 = IOFileExtension.ReadID(reader);


                // The Absolute Context defines one skeleton. Render contexts only provide
                // skinned-bone subsets and matrices for that skeleton; they do not each
                // define another complete rig.
                for (int i = 0; i < AbsContext.NodeSet.NodeIDs.Length; i++)
                {
                    string boneName = NamePool.GetString(AbsContext.NodeSet.NameSet, i);
                    ushort parentID = AbsContext.Section5[i].Item1;
                    int parentIndex = AbsContext.NodeSet.FindIndex(parentID);

                    string parentName = "";
                    if (parentIndex >= 0)
                        parentName = NamePool.GetString(AbsContext.NodeSet.NameSet, parentIndex);

                    Bones.Add(new BoneData()
                    {
                        Name = boneName,
                        Parent = parentName,
                        InverseTransform = Matrix4x4.Identity,
                        WorldTransform = Matrix4x4.Identity,
                        LocalTransform = Matrix4x4.Identity
                    });
                }

                // Preserve the original per-context / per-entry order for CMDL bone-index
                // lookup, while applying all contexts to the same absolute skeleton.
                HashSet<int> skinnedBoneIndices = new HashSet<int>();
                for (int r = 0; r < RenderContexts.Count; r++)
                {
                    SRenderContext renderContext = RenderContexts[r];

                    for (int i = 0; i < renderContext.SkinnedMatrixBoneIDs.Length; i++)
                    {
                        ushort boneID = renderContext.SkinnedMatrixBoneIDs[i];
                        int nodeIndex = AbsContext.NodeSet.FindIndex(boneID);
                        string boneName = NamePool.GetString(AbsContext.NodeSet.NameSet, nodeIndex);

                        SkinnedBones.Add(new SkinnedBoneData()
                        {
                            Name = boneName,
                            BoneID = boneID,
                            NodeIDX = nodeIndex
                        });
                    }

                    for (int i = 0; i < renderContext.SkinnedMatrixBoneIDs.Length; i++)
                    {
                        ushort boneID = renderContext.SkinnedMatrixBoneIDs[i];
                        int nodeIndex = AbsContext.NodeSet.FindIndex(boneID);

                        if (nodeIndex < 0 || nodeIndex >= Bones.Count ||
                            i >= renderContext.SkinnedInverseMatrices.Length)
                            continue;

                        skinnedBoneIndices.Add(nodeIndex);
                        Matrix4x4.Invert(
                            Matrix4x4.Transpose(renderContext.SkinnedInverseMatrices[i]),
                            out Matrix4x4 worldSpace);

                        Bones[nodeIndex].InverseTransform = renderContext.SkinnedInverseMatrices[i];
                        // Preserve the previous render-context order: a later context may
                        // provide the final matrix for a joint also listed by an earlier one.
                        Bones[nodeIndex].WorldTransform = worldSpace;
                    }
                }

                // The game data currently gives explicit transforms only for skinned bones.
                // Keep the existing interpolation fallback, but perform it once over the
                // single shared skeleton rather than repeatedly creating / revisiting rigs.
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

                        var parentBone = Bones.FirstOrDefault(b => b.Name == Bones[i].Parent);
                        if (parentBone != null && parentBone.WorldTransform != Matrix4x4.Identity)
                        {
                            if (Matrix4x4.Decompose(parentBone.WorldTransform, out _, out Quaternion pRot, out Vector3 pTrans))
                            {
                                translations.Add(pTrans);
                                rotations.Add(pRot);
                            }
                        }

                        var children = Bones.Where(b => b.Parent == Bones[i].Name).ToList();
                        foreach (var child in children)
                        {
                            if (child.WorldTransform == Matrix4x4.Identity)
                                continue;

                            if (Matrix4x4.Decompose(child.WorldTransform, out _, out Quaternion cRot, out Vector3 cTrans))
                            {
                                translations.Add(cTrans);

                                if (rotations.Count > 0 && Quaternion.Dot(rotations[0], cRot) < 0)
                                    cRot = Quaternion.Inverse(cRot); // Preserve the existing hemisphere behavior.

                                rotations.Add(cRot);
                            }
                        }

                        if (translations.Count == 0)
                            continue;

                        Vector3 avgPos = Vector3.Zero;
                        foreach (Vector3 translation in translations)
                            avgPos += translation;
                        avgPos /= translations.Count;

                        Quaternion avgRot = rotations[0];
                        if (rotations.Count > 1)
                        {
                            Vector4 accumulated = new Vector4(
                                rotations[0].X, rotations[0].Y, rotations[0].Z, rotations[0].W);
                            for (int x = 1; x < rotations.Count; x++)
                            {
                                accumulated += new Vector4(
                                    rotations[x].X, rotations[x].Y, rotations[x].Z, rotations[x].W);
                            }
                            accumulated /= rotations.Count;
                            avgRot = Quaternion.Normalize(new Quaternion(
                                accumulated.X, accumulated.Y, accumulated.Z, accumulated.W));
                        }

                        Matrix4x4 newWorld = Matrix4x4.CreateFromQuaternion(avgRot) *
                            Matrix4x4.CreateTranslation(avgPos);
                        if (Vector3.Distance(Bones[i].WorldTransform.Translation, avgPos) > 0.001f)
                        {
                            Bones[i].WorldTransform = newWorld;
                            changed = true;
                        }
                    }
                }

                // Convert the completed world-space rig into local transforms once.
                for (int i = 0; i < Bones.Count; i++)
                {
                    if (!string.IsNullOrEmpty(Bones[i].Parent))
                    {
                        var parent = Bones.FirstOrDefault(x => x.Name == Bones[i].Parent);
                        if (parent != null)
                        {
                            Matrix4x4.Invert(parent.WorldTransform, out Matrix4x4 parentInverse);
                            Bones[i].LocalTransform = Bones[i].WorldTransform * parentInverse;
                        }
                    }
                    else
                    {
                        Bones[i].LocalTransform = Bones[i].WorldTransform;
                    }
                }

                // The native animation builder connects type-5 animation-context nodes to
                // CActionData event addresses through the packed code in SAbsContext's node
                // table. Do this only after all of those native structures have been parsed.
                BuildNativePoseAddressMap();
            }

            private void BuildNativePoseAddressMap()
            {
                // The name-hash lookup in FUN_7100e548e0 returns the packed code from
                // SAbsContext. That packed code selects a type-5 absolute-context record
                // and an entry within that record. It is NOT an animation-context node ID.
                // The earlier implementation incorrectly searched AnimContext.NodeSet using
                // packedCode & 0xFFFF, which rejected almost every bone in the test log.
                if (AbsContext?.NodeSet?.NodeIDs == null ||
                    NamePool == null ||
                    ActionData?.Events == null || ActionData.Events.Count == 0)
                {
                    Console.WriteLine("[CHPR] Native pose-address map unavailable: absolute-context node data, names, or action-event data is absent.");
                    return;
                }

                if (AbsContext.FirstType5RecordIndex < 0 || AbsContext.FirstType5RecordCount <= 0)
                {
                    Console.WriteLine("[CHPR] Native pose-address map unavailable: SAbsContext has no type-5 record range.");
                    return;
                }

                int candidateCount = Math.Min(Bones.Count, AbsContext.NodeSet.NodeIDs.Length);
                int type5RecordMatches = 0;
                int mappedCount = 0;
                int missingPackedCodes = 0;
                int invalidRecordIndices = 0;
                int nonType5RecordCodes = 0;
                int invalidEntryIndices = 0;
                int invalidEventIndices = 0;
                int invalidAddress = 0;
                var addressCounts = new Dictionary<int, int>();

                for (int boneIndex = 0; boneIndex < candidateCount; boneIndex++)
                {
                    // Bones are created in SAbsContext.NodeSet order, so this hash-list
                    // index corresponds to the current bone. FUN_7100e548e0 performs the
                    // reverse name-hash lookup in the native implementation.
                    if (!NamePool.TryGetStringHashFromHashList(
                        AbsContext.NodeSet.NameSet, boneIndex, out uint nameHash) ||
                        !AbsContext.TryFindPackedNodeCode(nameHash, NamePool, out uint packedCode))
                    {
                        missingPackedCodes++;
                        continue;
                    }

                    int recordIndex = (int)(packedCode & 0x3Fu);
                    int entryWithinRecord = (int)((packedCode >> 6) & 0x3FFu);

                    if (recordIndex < 0 || recordIndex >= AbsContext.Section1.Count)
                    {
                        invalidRecordIndices++;
                        continue;
                    }

                    // FUN_7100e5497c computes a prefix sum over the contiguous type-5
                    // records. Only codes pointing into that run can safely index the
                    // native event-address table this way.
                    int type5Start = AbsContext.FirstType5RecordIndex;
                    int type5EndExclusive = type5Start + AbsContext.FirstType5RecordCount;
                    if (recordIndex < type5Start || recordIndex >= type5EndExclusive ||
                        AbsContext.Section1[recordIndex].Type != 5)
                    {
                        nonType5RecordCodes++;
                        continue;
                    }

                    type5RecordMatches++;

                    if (entryWithinRecord >= AbsContext.Section1[recordIndex].Count)
                    {
                        invalidEntryIndices++;
                        continue;
                    }

                    int eventIndex = AbsContext.GetCumulativeRecordBlockOffset(recordIndex) + entryWithinRecord;
                    if (eventIndex < 0 || eventIndex >= ActionData.Events.Count)
                    {
                        invalidEventIndices++;
                        continue;
                    }

                    // FUN_7100e51a58 takes the first uint32 of the selected CActionData
                    // event and masks it to the native 26-bit pose address.
                    int poseAddress = (int)(ActionData.Events[eventIndex].Address & 0x03FFFFFFu);
                    if (poseAddress == 0x03FFFFFF)
                    {
                        invalidAddress++;
                        continue;
                    }

                    Bones[boneIndex].PoseAddress = poseAddress;
                    Bones[boneIndex].PoseAddressRecordType = 5;
                    mappedCount++;

                    addressCounts.TryGetValue(poseAddress, out int previousCount);
                    addressCounts[poseAddress] = previousCount + 1;
                }

                int duplicateAddressGroups = addressCounts.Count(pair => pair.Value > 1);
                Console.WriteLine(
                    $"[CHPR] Native pose-address map: bones={Bones.Count}, candidates={candidateCount}, " +
                    $"type5RecordMatches={type5RecordMatches}, mapped={mappedCount}, missingPackedCodes={missingPackedCodes}, " +
                    $"invalidRecordIndices={invalidRecordIndices}, nonType5RecordCodes={nonType5RecordCodes}, " +
                    $"invalidEntryIndices={invalidEntryIndices}, invalidEventIndices={invalidEventIndices}, " +
                    $"invalidAddresses={invalidAddress}, duplicateAddressGroups={duplicateAddressGroups}.");
            }
        }

        #region Bones
        public class BoneData
        {
            public string Name;
            public string Parent;
            public uint ParentIndex;

            // Raw CHPR animation output address: slot | (index << 6).
            // -1 means no trustworthy hierarchy mapping was recovered.
            public int PoseAddress = -1;
            public byte PoseAddressRecordType;

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
        #endregion

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

        #region Animation Context
        public class SAnimContext
        {
            public SNodeSet NodeSet;
            public byte RecordCountA;
            public byte RecordCountB;
            public ushort Section2ByteSize;
            public ushort Section3TotalSize;
            public ushort Section4TotalSize;
            public List<SAnimContextRecord> Section1Records = new List<SAnimContextRecord>();
            public byte[] Section2Data = Array.Empty<byte>();
            public ushort[] Section3Table = Array.Empty<ushort>();
            public byte[] Section4Data = Array.Empty<byte>();
            public ushort[] Section5Table = Array.Empty<ushort>();
            public byte[] Section6Data = Array.Empty<byte>();

            public SAnimContext(FileReader reader)
            {
                /* SAnimContext::Load  */
                RecordCountA = reader.ReadByte();
                RecordCountB = reader.ReadByte();
                ushort Section2ByteSize = reader.ReadUInt16();
                ushort Section3TotalSize = reader.ReadUInt16();
                ushort Section4TotalSize = reader.ReadUInt16();

                /* SAnimContext::ReadIntoMemory */
                for (int i = 0; i < RecordCountA; i++)
                {
                    Section1Records.Add(new SAnimContextRecord(reader));
                }
                Section2Data = ReadExactBytes(reader, Section2ByteSize, "SAnimContext section 2");
                Section3Table = reader.ReadUInt16s(RecordCountB);

                int section4Size = Section3TotalSize - (RecordCountB * 8);
                int section6Size = Section4TotalSize - (RecordCountA * 8);

                if (section4Size < 0 || section6Size < 0)
                {
                    throw new InvalidDataException("SAnimContext section sizes are smaller than their associated tables.");
                }

                Section4Data = ReadExactBytes(reader, section4Size, "SAnimContext section 4");
                Section5Table = reader.ReadUInt16s(RecordCountA);
                Section6Data = ReadExactBytes(reader, section6Size, "SAnimContext section 6");

                NodeSet = new SNodeSet(reader);
            }
        }

        public class SAnimContextRecord
        {
            public ushort FieldA;
            public ushort FieldB;
            public ushort FieldC;

            public SAnimContextRecord(FileReader reader)
            {
                FieldA = reader.ReadUInt16();
                FieldB = reader.ReadUInt16();
                FieldC = reader.ReadUInt16();
            }
        }
        #endregion

        #region Absolute Context
        public class SAbsContext
        {
            public SNodeSet NodeSet;
            public (ushort, ushort)[] Section5;

            public byte Section1Count;
            public ushort Section5EntryCount;
            public ushort Section2ByteSize;
            public ushort Section3ByteSize;
            public ushort Section4ByteSize;
            // Fifth header word: byte length of the block between Section4 and SNodeSet.
            public ushort HeaderField5;
            public byte TrailingFlag;

            public List<SAbsContextRecord> Section1 = new List<SAbsContextRecord>();
            public byte[] Section2 = Array.Empty<byte>();
            public byte[] Section3 = Array.Empty<byte>();
            public byte[] Section4 = Array.Empty<byte>();
            // Native SAbsContext reads this size-delimited block after sections 2-4 and
            // before the node set. Omitting it shifts the node set and every following
            // field, eventually making SRenderContext.num5 look like an impossible size.
            public byte[] Section5Data = Array.Empty<byte>();
            public int FirstType5RecordIndex = -1;
            public int FirstType5RecordCount;

            public SAbsContext(FileReader reader)
            {
                // Native layout: one byte followed by five ushorts. Unlike SAnimContext,
                // SAbsContext has no second leading byte.
                Section1Count = reader.ReadByte();
                Section5EntryCount = reader.ReadUInt16();
                Section2ByteSize = reader.ReadUInt16();
                Section3ByteSize = reader.ReadUInt16();
                Section4ByteSize = reader.ReadUInt16();
                HeaderField5 = reader.ReadUInt16();

                for (int i = 0; i < Section1Count; i++)
                {
                    Section1.Add(new SAbsContextRecord(reader));
                }

                // The native reader records the first contiguous run of type-5 records.
                for (int i = 0; i < Section1.Count; i++)
                {
                    if (Section1[i].Type != 5)
                    {
                        continue;
                    }

                    FirstType5RecordIndex = i;
                    for (int j = i; j < Section1.Count && Section1[j].Type == 5; j++)
                    {
                        FirstType5RecordCount++;
                    }
                    break;
                }

                Section2 = ReadExactBytes(reader, Section2ByteSize, "SAbsContext section 2");
                Section3 = ReadExactBytes(reader, Section3ByteSize, "SAbsContext section 3");
                Section4 = ReadExactBytes(reader, Section4ByteSize, "SAbsContext section 4");
                Section5Data = ReadExactBytes(reader, HeaderField5, "SAbsContext section 5 data");

                // FUN_7100692edc reads the node-set header and its uint32 entries here.
                NodeSet = new SNodeSet(reader);

                Section5 = new (ushort, ushort)[Section5EntryCount];
                for (int i = 0; i < Section5.Length; i++)
                {
                    Section5[i] = (reader.ReadUInt16(), reader.ReadUInt16());
                }

                TrailingFlag = reader.ReadByte();
            }

            public int GetCumulativeRecordBlockOffset(int recordIndex)
            {
                if (FirstType5RecordIndex < 0 || recordIndex <= FirstType5RecordIndex)
                {
                    return 0;
                }

                int total = 0;
                int end = Math.Min(recordIndex, Section1.Count);
                for (int i = FirstType5RecordIndex; i < end; i++)
                {
                    total += Section1[i].TypeAndCount >> 5;
                }
                return total;
            }

            public bool TryFindPackedNodeCode(uint nameHash, NamePool namePool, out uint packedCode)
            {
                packedCode = 0xFFFFFFFF;
                if (NodeSet == null || namePool == null || NodeSet.NameSet >= namePool.HashLists.Count)
                {
                    return false;
                }

                HashList hashList = namePool.HashLists[NodeSet.NameSet];
                int count = Math.Min(hashList.Hashes.Length, NodeSet.NodeIDs.Length);
                for (int i = 0; i < count; i++)
                {
                    if (hashList.Hashes[i] == nameHash)
                    {
                        packedCode = NodeSet.NodeIDs[i];
                        return true;
                    }
                }
                return false;
            }
        }

        public class SAbsContextRecord
        {
            public ushort TypeAndCount;
            public ushort Field1;
            public ushort Field2;
            public int Type => TypeAndCount & 0x1F;
            public int Count => TypeAndCount >> 5;

            public SAbsContextRecord(FileReader reader)
            {
                TypeAndCount = reader.ReadUInt16();
                Field1 = reader.ReadUInt16();
                Field2 = reader.ReadUInt16();
            }
        }
        #endregion

        public class SRenderContext         // Works as intended
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
            public byte[] sectionNum4Data;

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
                InverseMatrixBoneIds = reader.ReadUInt32s((int)num4); // Is always 0 in Prime 4
                section4 = ReadExactBytes(reader, checked((int)num5), "SRenderContext section 4");

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

        #region Unknown Context 5
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
                // FUN_710060910c & FUN_7100692dd8
                List1Count1 = reader.ReadByte();
                List1Count2 = reader.ReadUInt16();

                // FUN_71000d5760
                List1Data = reader.ReadBytes(List1Count1 * 6);

                // FUN_7100692edc
                List2Count = reader.ReadUInt16();
                List2Unk = reader.ReadUInt16();
                List2Data = reader.ReadUInt32s(List2Count);

                HasPolyList = reader.ReadBoolean();
                if (HasPolyList)
                {
                    // FUN_71006930c8
                    uint polyCount = reader.ReadUInt32();
                    for (int i = 0; i < polyCount; i++)
                    {
                        uint typeHash = reader.ReadUInt32();
                        PolyBase polyObj = null;

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
        #endregion

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

        #region Animation components
        private void ReadAnimationPayloads(FileReader reader, long payloadStart, long bodyEnd)
        {
            int successCount = 0;
            int failedCount = 0;
            int emptyCount = 0;

            for (int i = 0; i < animationClipArray.Count; i++)
            {
                AnimationArrayEntry entry = animationClipArray[i];

                entry.Animation = null;
                entry.CompStreamClip = null;
                entry.CompStreamParseError = null;
                entry.TrailingData = Array.Empty<byte>();
                entry.BytesParsed = 0;
                entry.ParseError = null;

                string label = $"[CHPR] Animation {i + 1}/{animationClipArray.Count} " + $"(slot={entry.OutputSlot}, offset=0x{entry.Offset:X}, " + $"size=0x{entry.Size:X})";

                if (entry.Size == 0)
                {
                    emptyCount++;
                    Console.WriteLine($"{label}: SKIPPED - empty payload.");
                    continue;
                }

                if (entry.Size > int.MaxValue)
                {
                    entry.ParseError = $"Animation payload is too large: {entry.Size} bytes.";

                    failedCount++;
                    Console.WriteLine($"{label}: FAILED - {entry.ParseError}");

                    continue;
                }

                long dataStart = entry.Offset;
                long dataSize = entry.Size;

                if (dataStart < payloadStart || dataStart > bodyEnd || dataSize > bodyEnd - dataStart)
                {
                    entry.ParseError = "Animation range extends beyond the available payload.";

                    failedCount++;
                    Console.WriteLine($"{label}: FAILED - {entry.ParseError}");

                    continue;
                }

                int length = checked((int)entry.Size);
                byte[] animationBytes;

                using (reader.TemporarySeek(dataStart, System.IO.SeekOrigin.Begin))
                {
                    animationBytes = reader.ReadBytes(length);
                }

                if (animationBytes.Length != length)
                {
                    entry.ParseError = $"Expected {length} bytes but read " + $"{animationBytes.Length} bytes.";
                    failedCount++;

                    Console.WriteLine($"{label}: FAILED - {entry.ParseError}");

                    continue;
                }

                try
                {
                    // Parse the common header and type-specific animation data.
                    CHPRAnimationDecoder.PayloadParseResult result = CHPRAnimationDecoder.ParsePayload(animationBytes);

                    entry.Animation = result.Animation;
                    entry.BytesParsed = result.BytesParsed;
                    entry.TrailingData = result.TrailingData;

                    successCount++;

                    Console.WriteLine($"{label}: SUCCESS - type={entry.Animation.BaseInfo.Type}, index={entry.Animation.BaseInfo.Index}, " +
                        $"parsed={entry.BytesParsed}/{length} bytes, trailing={entry.TrailingData.Length} bytes.");

                    // CompStream parsing is intentionally separate: a new or not-yet-
                    // understood stream format must not invalidate the outer CAnim parse.
                    if (entry.Animation.BaseInfo.Type == CHPRAnimationDecoder.AnimType.CompStream)
                    {
                        try
                        {
                            entry.CompStreamClip = CHPRCompStreamAnimation.ParseAnimationPayload(animationBytes, entry.Animation);
                            Console.WriteLine($"[CHPR] Animation {i + 1}: CompStream structure parsed - streams={entry.CompStreamClip.Streams.Count}, " +
                                $"descriptorCommands={entry.CompStreamClip.DescriptorCommands.Count}, descriptorTerminated={entry.CompStreamClip.DescriptorTerminated}, " +
                                $"frameCount={entry.CompStreamClip.FrameCount}, rate={entry.CompStreamClip.Rate}.");
                        }
                        catch (Exception streamEx)
                        {
                            entry.CompStreamParseError = $"{streamEx.GetType().Name}: {streamEx.Message}";
                            Console.WriteLine($"[CHPR] Animation {i + 1}: CompStream structure parse failed - {entry.CompStreamParseError}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    entry.Animation = null;
                    entry.BytesParsed = 0;
                    entry.TrailingData = Array.Empty<byte>();
                    entry.ParseError = $"{ex.GetType().Name}: {ex.Message}";

                    failedCount++;

                    Console.WriteLine($"{label}: FAILED - {entry.ParseError}");
                }
            }

            // GameAnimationName remains unset unless a name is resolved through a parsed
            // game structure. CHPRAnimationExporter will use the output slot as its fallback.

            // Summarize the inventory in one line as well as the per-entry diagnostics above.
            // A large table count is not the same as a large number of supported CompStream clips.
            var parsedTypeCounts = animationClipArray.Where(entry => entry.Animation != null).GroupBy(entry => (byte)entry.Animation.BaseInfo.Type).OrderBy(group => group.Key).Select(group => $"0x{group.Key:X2}={group.Count()}").ToList();

            int compStreamPayloads = animationClipArray.Count(entry => entry.Animation != null && entry.Animation.BaseInfo.Type == CHPRAnimationDecoder.AnimType.CompStream);
            int compStreamParsed = animationClipArray.Count(entry => entry.CompStreamClip != null);
            int compStreamStructureFailed = animationClipArray.Count(entry => entry.Animation != null && entry.Animation.BaseInfo.Type == CHPRAnimationDecoder.AnimType.CompStream && entry.CompStreamClip == null);
            int sequenceCount = animationClipArray.Count(entry => entry.Animation != null && entry.Animation.BaseInfo.Type == CHPRAnimationDecoder.AnimType.Sequence);
            int gridCount = animationClipArray.Count(entry => entry.Animation != null && entry.Animation.BaseInfo.Type == CHPRAnimationDecoder.AnimType.Grid);

            Console.WriteLine($"[CHPR] Animation reading finished: {successCount} succeeded, {failedCount} failed, {emptyCount} empty.");
            Console.WriteLine($"[CHPR] Animation inventory: entries={animationClipArray.Count}, parsedTypes=[{string.Join(", ", parsedTypeCounts)}], " +
                $"CompStream={compStreamPayloads} (parsed={compStreamParsed}, structureFailed={compStreamStructureFailed}), " +
                $"Sequence={sequenceCount}, Grid={gridCount}.");
        }
        #endregion

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

        public class SModelNode
        {
            public CPooledName Name;
            public uint Unk1;
            public uint Unk2;
            public uint Unk3;
            public ushort Unk4;
            public bool Unk5;
            public CObjectId Guid;
            public uint Unk6;
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

        #region Unknown Context 6
        public class SUnkContext6
        {
            public ushort countA;
            public ushort countB;
            public List<SUnkContext6Entry1> Entries1 = new List<SUnkContext6Entry1>();
            public List<uint> Entries2 = new List<uint>();

            public SUnkContext6(FileReader reader)
            {
                countA = reader.ReadUInt16();
                countB = reader.ReadUInt16();

                for (int i = 0; i < countA; i++)
                {
                    Entries1.Add(new SUnkContext6Entry1(reader));
                }

                if (countB > 0)
                {
                    for (int i = 0; i < countB; i++)
                    {
                        Entries2.Add(reader.ReadUInt32());
                    }
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
        #endregion

        #region Sub Character
        public class SSubChar
        {
            public bool HasReference;
            public uint PackedReference;

            public CObjectId Guid;

            public byte SubRecordCount;
            public byte[] Unk1 = Array.Empty<byte>();
            public byte[] Unk2 = Array.Empty<byte>();

            public SSubChar(FileReader reader, uint rawSize)
            {
                HasReference = reader.ReadByte() != 0;

                if (HasReference)
                {
                    PackedReference = reader.ReadUInt32();
                }

                Guid = IOFileExtension.ReadID(reader);
                SubRecordCount = reader.ReadByte();

                // The native code casts the entry size to a signed short.
                if (rawSize > short.MaxValue)
                {
                    throw new InvalidDataException($"Sub-character size exceeds the native signed-short range: {rawSize}");
                }

                int dataSize = (int)(short)rawSize;
                int secondSize = checked(SubRecordCount * 12);
                int firstSize = dataSize - secondSize;

                if (firstSize < 0)
                {
                    throw new InvalidDataException($"Invalid sub-character payload size: {dataSize} bytes for {SubRecordCount} sub-records.");
                }

                Unk1 = reader.ReadBytes(firstSize);
                Unk2 = reader.ReadBytes(secondSize);

                if (Unk1.Length != firstSize || Unk2.Length != secondSize)
                {
                    throw new EndOfStreamException("Unexpected end of stream while reading sub-character data.");
                }
            }
        }

        public class SSubCharData
        {
            public ushort EntryCount;
            public uint[] DataSizes = Array.Empty<uint>();
            public ushort HeaderField;

            public List<SSubChar> SubChars = new List<SSubChar>();

            public SSubCharData(FileReader reader)
            {
                EntryCount = reader.ReadUInt16();
                DataSizes = reader.ReadUInt32s(EntryCount);
                HeaderField = reader.ReadUInt16();

                for (int i = 0; i < EntryCount; i++)
                {
                    SubChars.Add(new SSubChar(reader, DataSizes[i]));
                }
            }
        }
        #endregion

        #region Unknown Context 7
        public class SUnkContext7
        {
            public ushort Count;
            public ushort PayloadSize;

            // Native code copies Count * 4 bytes.
            public byte[] EntryData = Array.Empty<byte>();

            // Native code then copies PayloadSize bytes.
            public byte[] Payload = Array.Empty<byte>();

            public SUnkContext7(FileReader reader)
            {
                Count = reader.ReadUInt16();
                Console.WriteLine("Unknown Context 7 Count: " + Count.ToString());
                PayloadSize = reader.ReadUInt16();

                EntryData = ReadExactBytes(reader, checked((int)Count * 4), "SUnkContext7 entries");
                Payload = ReadExactBytes(reader, PayloadSize, "SUnkContext7 payload");
            }
        }
        #endregion

        #region Unknown Context 8
        public class SUnkContext8
        {
            public uint EntryCount;
            public uint SizeWord;
            public uint ConditionalDataSize;
            public byte[] EntryData = Array.Empty<byte>();
            public byte[] ConditionalData = Array.Empty<byte>();
            public byte[] Payload = Array.Empty<byte>();

            public SUnkContext8(FileReader reader)
            {
                EntryCount = reader.ReadUInt32();
                SizeWord = reader.ReadUInt32();
                if ((SizeWord & 0x80000000u) != 0)
                {
                    ConditionalDataSize = reader.ReadUInt32();
                }

                uint payloadSize = SizeWord & 0x7FFFFFFFu;
                int entryBytes = checked((int)((long)EntryCount * 8));
                EntryData = ReadExactBytes(reader, entryBytes, "SUnkContext8 entries");

                if (ConditionalDataSize > 0)
                {
                    ConditionalData = ReadExactBytes(reader, CheckedByteLength(ConditionalDataSize, "SUnkContext8 conditional data"), "SUnkContext8 conditional data");
                }
                Payload = ReadExactBytes(reader, CheckedByteLength(payloadSize, "SUnkContext8 payload"), "SUnkContext8 payload");
            }
        }
        #endregion

        #region Animation Parameters
        public class SAnimParamNode
        {
            public byte[] Value16 = Array.Empty<byte>();
            public uint ParameterWord;

            public SAnimParamMap Parameters;

            public SAnimParamNode(FileReader reader)
            {
                Value16 = reader.ReadBytes(16);
                ParameterWord = reader.ReadUInt32();
                Parameters = new SAnimParamMap(reader);
            }
        }

        // FUN_71004491e8
        public class SAnimParamMap
        {
            public const uint PAS4 = 0x50415334;
            public const uint PAS6 = 0x50415336;

            public uint FormatTag;
            public uint EntryCount;

            // PAS4 has a four-byte value which the native reader skips.
            public byte[] ExtraHeaderBytes = Array.Empty<byte>();

            public List<SAnimParamMapEntry> Entries = new List<SAnimParamMapEntry>();

            public SAnimParamMap(FileReader reader)
            {
                FormatTag = reader.ReadUInt32();
                EntryCount = reader.ReadUInt32();

                int count = ReadCount(EntryCount, "animation parameter map");

                if (FormatTag == PAS4)
                {
                    ExtraHeaderBytes = ReadExactBytes(reader, 4, "PAS4 animation-parameter header bytes");
                }

                for (int i = 0; i < count; i++)
                {
                    Entries.Add(new SAnimParamMapEntry(reader, FormatTag));
                }
            }

            internal static int ReadCount(uint count, string label)
            {
                // Sanity limit to prevent corrupt counts from allocating enormous lists.
                if (count > 1_000_000)
                {
                    throw new InvalidDataException($"Unreasonable {label} count: {count}");
                }

                return checked((int)count);
            }
        }

        // FUN_710044967c
        public class SAnimParamMapEntry
        {
            public uint EntryKey;
            public uint DescriptorCount;
            public uint ValueRecordCount;

            public List<SAnimParamFieldDescriptor> Descriptors = new List<SAnimParamFieldDescriptor>();

            public List<SAnimParamValueRecord> ValueRecords = new List<SAnimParamValueRecord>();

            public SAnimParamMapEntry(FileReader reader, uint formatTag)
            {
                EntryKey = reader.ReadUInt32();
                DescriptorCount = reader.ReadUInt32();
                ValueRecordCount = reader.ReadUInt32();

                int descriptorCount = SAnimParamMap.ReadCount(DescriptorCount, "field descriptor");

                int valueRecordCount = SAnimParamMap.ReadCount(ValueRecordCount, "parameter value record");

                for (int i = 0; i < descriptorCount; i++)
                {
                    Descriptors.Add(new SAnimParamFieldDescriptor(reader, formatTag));
                }

                for (int i = 0; i < valueRecordCount; i++)
                {
                    ValueRecords.Add(new SAnimParamValueRecord(reader, Descriptors));
                }
            }
        }

        // FUN_7100449c64
        public class SAnimParamFieldDescriptor
        {
            // Only read for the PAS6 format.
            public uint? FormatSpecificValue;

            public uint TypeCode;
            public uint Field08;
            public uint Field0C;

            // Used by some descriptor types.
            public uint ExtraValue0;
            public uint ExtraValue1;

            // Used by type 3.
            public byte? ExtraByte0;
            public byte? ExtraByte1;

            public SAnimParamFieldDescriptor(FileReader reader, uint formatTag)
            {
                if (formatTag == SAnimParamMap.PAS6)
                {
                    FormatSpecificValue = reader.ReadUInt32();
                }

                TypeCode = reader.ReadUInt32();
                Field08 = reader.ReadUInt32();
                Field0C = reader.ReadUInt32();

                switch (TypeCode)
                {
                    case 0:
                        // FUN_710058aec0 reads a 32-bit value each time.
                        ExtraValue0 = reader.ReadUInt32();
                        ExtraValue1 = reader.ReadUInt32();
                        break;
                    case 1:
                    case 2:
                    case 5:
                        ExtraValue0 = reader.ReadUInt32();
                        ExtraValue1 = reader.ReadUInt32();
                        break;
                    case 3:
                        ExtraByte0 = reader.ReadByte();
                        ExtraByte1 = reader.ReadByte();
                        break;
                    case 4:
                        ExtraValue0 = reader.ReadUInt32();
                        ExtraValue1 = reader.ReadUInt32();
                        break;
                    default:
                        // The native function consumes no additional bytes
                        // for other type codes in this descriptor parser.
                        break;
                }
            }
        }

        public class SAnimParamValueRecord
        {
            public uint Key;

            public List<SAnimParamValue> Values = new List<SAnimParamValue>();

            public SAnimParamValueRecord(FileReader reader, IReadOnlyList<SAnimParamFieldDescriptor> descriptors)
            {
                Key = reader.ReadUInt32();

                foreach (SAnimParamFieldDescriptor descriptor in descriptors)
                {
                    Values.Add(new SAnimParamValue(reader, descriptor.TypeCode));
                }
            }
        }

        public class SAnimParamValue
        {
            public uint TypeCode;

            public bool HasSerializedValue;
            public uint UIntValue;
            public byte ByteValue;

            public bool IsBoolean => TypeCode == 3;
            public bool BooleanValue => ByteValue != 0;

            public SAnimParamValue(FileReader reader, uint typeCode)
            {
                TypeCode = typeCode;

                switch (typeCode)
                {
                    case 0:
                    case 1:
                    case 2:
                    case 4:
                    case 5:
                        UIntValue = reader.ReadUInt32();
                        HasSerializedValue = true;
                        break;
                    case 3:
                        ByteValue = reader.ReadByte();
                        HasSerializedValue = true;
                        break;
                    default:
                        // Matches the provided native switch:
                        // no value bytes are consumed for other type codes.
                        break;
                }
            }
        }
        #endregion

        #region Animation Collision Primitive Data
        public class SAnimCollisionPrimData
        {
            public uint EntryCount;
            public List<SAnimCollisionPrimEntry> Entries = new List<SAnimCollisionPrimEntry>();

            public uint GroupCount;
            public List<SAnimCollisionPrimGroup> Groups = new List<SAnimCollisionPrimGroup>();

            // The native CAnimCollisionPrimData constructor reads a third array
            // after the named groups. Each entry is a serialized CObjectId.
            public uint ObjectIdCount;
            public List<CObjectId> ObjectIds = new List<CObjectId>();

            public SAnimCollisionPrimData(FileReader reader)
            {
                EntryCount = reader.ReadUInt32();
                int count = CheckedCount(EntryCount, "collision-primitive entry");
                for (int i = 0; i < count; i++)
                {
                    Entries.Add(new SAnimCollisionPrimEntry(reader));
                }

                GroupCount = reader.ReadUInt32();
                int groupCount = CheckedCount(GroupCount, "collision-primitive group");
                for (int i = 0; i < groupCount; i++)
                {
                    Groups.Add(new SAnimCollisionPrimGroup(reader));
                }

                // FUN_7100498f84 reads this count and then calls
                // CObjectId::CObjectId once per entry. Omitting this array
                // shifts the following HasEffectState flag and all later fields.
                ObjectIdCount = reader.ReadUInt32();
                int objectIdCount = CheckedCount(ObjectIdCount, "collision-primitive object ID");
                for (int i = 0; i < objectIdCount; i++)
                {
                    ObjectIds.Add(IOFileExtension.ReadID(reader));
                }
            }
        }

        public class SAnimCollisionPrimEntry
        {
            public uint PayloadSize;
            public string Name = string.Empty;
            public uint FieldA;
            public uint FieldB;
            public CObjectId ObjectId;
            public byte[] Payload = Array.Empty<byte>();

            public SAnimCollisionPrimEntry(FileReader reader)
            {
                PayloadSize = reader.ReadUInt32();
                Name = ReadLengthPrefixedString(reader, "collision-primitive name");
                FieldA = reader.ReadUInt32();
                FieldB = reader.ReadUInt32();
                ObjectId = IOFileExtension.ReadID(reader);
                // The native routine transforms selected fields after reading these bytes.
                // Preserve the serialized payload unchanged for now.
                Payload = ReadExactBytes(reader, CHPR.CheckedByteLength(PayloadSize, "collision-primitive payload"), "collision-primitive payload");
            }
        }

        public class SAnimCollisionPrimGroup
        {
            public string Name = string.Empty;
            public uint ChildCount;
            public List<string> ChildNames = new List<string>();

            public SAnimCollisionPrimGroup(FileReader reader)
            {
                Name = ReadLengthPrefixedString(reader, "collision-primitive group name");
                ChildCount = reader.ReadUInt32();
                int count = CheckedCount(ChildCount, "collision-primitive child name");
                for (int i = 0; i < count; i++)
                {
                    ChildNames.Add(ReadLengthPrefixedString(reader, "collision-primitive child name"));
                }
            }
        }
        #endregion

        #region Effect State
        public class SEffectState
        {
            public ushort EntryCountA;
            public ushort EntryCountB;
            public bool HasBitfield;
            public List<SEffectStateEntryA> EntriesA = new List<SEffectStateEntryA>();
            public List<SEffectStateEntryB> EntriesB = new List<SEffectStateEntryB>();
            public List<uint> BitfieldWords = new List<uint>();

            public SEffectState(FileReader reader)
            {
                EntryCountA = reader.ReadUInt16();
                EntryCountB = reader.ReadUInt16();
                HasBitfield = reader.ReadByte() != 0;

                for (int i = 0; i < EntryCountA; i++)
                {
                    EntriesA.Add(new SEffectStateEntryA(reader));
                }
                for (int i = 0; i < EntryCountB; i++)
                {
                    EntriesB.Add(new SEffectStateEntryB(reader));
                }

                if (HasBitfield && EntryCountA > 0)
                {
                    int wordCount = Math.Max(1, (EntryCountA + 31) / 32);
                    for (int i = 0; i < wordCount; i++)
                    {
                        BitfieldWords.Add(reader.ReadUInt32());
                    }
                }
            }
        }

        public class SEffectStateEntryA
        {
            public uint Word;
            public byte[] Value16 = Array.Empty<byte>();

            public SEffectStateEntryA(FileReader reader)
            {
                Word = reader.ReadUInt32();
                Value16 = ReadExactBytes(reader, 16, "effect-state 16-byte value");
            }
        }

        public class SEffectStateEntryB
        {
            public uint PackedLengthAndCount;
            public uint ByteLength;
            public uint Metadata;
            public byte[] FourByteItems = Array.Empty<byte>();
            public byte[] Bytes = Array.Empty<byte>();

            public SEffectStateEntryB(FileReader reader)
            {
                uint countAndFlags = reader.ReadUInt32();
                uint byteLength = reader.ReadUInt32();
                Metadata = reader.ReadUInt32();

                uint fourByteCount = countAndFlags & 0xFu;
                PackedLengthAndCount = countAndFlags;
                ByteLength = byteLength;
                FourByteItems = ReadExactBytes(reader, checked((int)(fourByteCount * 4)), "effect-state four-byte items");
                Bytes = ReadExactBytes(reader, CheckedByteLength(byteLength, "effect-state record"), "effect-state record");
            }
        }
        #endregion

        #region Action Data
        public class SActionData
        {
            public uint StreamCount;
            public ushort EventCount;
            public uint ModeCount;
            public uint TrackCount;
            public uint StringBlobSize;
            public bool[] Flags = new bool[4];
            public List<SActionStream> Streams = new List<SActionStream>();
            public List<(ushort First, ushort Second)> StreamPairs = new List<(ushort, ushort)>();
            public List<uint> Modes = new List<uint>();
            public List<SActionEvent> Events = new List<SActionEvent>();
            public byte[] TrackData = Array.Empty<byte>();
            public byte[] StringBlob = Array.Empty<byte>();

            public SActionData(FileReader reader)
            {
                StreamCount = reader.ReadUInt32();
                EventCount = reader.ReadUInt16();
                ModeCount = reader.ReadUInt32();
                TrackCount = reader.ReadUInt32();
                StringBlobSize = reader.ReadUInt32();

                for (int i = 0; i < Flags.Length; i++)
                {
                    Flags[i] = reader.ReadByte() != 0;
                }

                int streamCount = CheckedCount(StreamCount, "action stream");
                int modeCount = CheckedCount(ModeCount, "action mode");
                int trackCount = CheckedCount(TrackCount, "action track");
                int stringBlobSize = CheckedByteLength(StringBlobSize, "action string blob");

                for (int i = 0; i < streamCount; i++)
                {
                    Streams.Add(new SActionStream(reader));
                }
                for (int i = 0; i < streamCount; i++)
                {
                    StreamPairs.Add((reader.ReadUInt16(), reader.ReadUInt16()));
                }
                for (int i = 0; i < modeCount; i++)
                {
                    Modes.Add(reader.ReadUInt32());
                }
                for (int i = 0; i < EventCount; i++)
                {
                    Events.Add(new SActionEvent(reader));
                }

                if (TrackCount != 0)
                {
                    TrackData = ReadExactBytes(reader, checked(trackCount * 4), "action track data");
                    StringBlob = ReadExactBytes(reader, stringBlobSize, "action string blob");
                }
            }
        }

        public class SActionStream
        {
            public ushort DataSize;
            public byte FlagA;
            public byte FlagB;
            public byte[] Data = Array.Empty<byte>();

            public SActionStream(FileReader reader)
            {
                DataSize = reader.ReadUInt16();
                FlagA = reader.ReadByte();
                FlagB = reader.ReadByte();
                Data = ReadExactBytes(reader, DataSize, "action stream payload");
            }
        }

        public class SActionEvent
        {
            public uint Address;
            public uint Value;

            public SActionEvent(FileReader reader)
            {
                Address = reader.ReadUInt32();
                Value = reader.ReadUInt32();
            }
        }
        #endregion

        #region Embedded Animation Data
        public class SEmbeddedAnimData
        {
            public ushort TypeRecordCount;
            public ushort Record12ByteCount;
            public ushort LinkRecordCount;
            public ushort Record6ByteCount;
            public ushort HeaderValueA;
            public ushort HeaderValueB;
            public ushort UIntRecordCount;
            public ushort Value16RecordCount;
            public List<SEmbeddedTypedRecord> TypedRecords = new List<SEmbeddedTypedRecord>();
            public List<SEmbeddedRecord12> Records12 = new List<SEmbeddedRecord12>();
            public List<SEmbeddedLinkRecord> LinkRecords = new List<SEmbeddedLinkRecord>();
            public List<SEmbeddedRecord6> Records6 = new List<SEmbeddedRecord6>();
            public List<uint> UIntRecords = new List<uint>();
            public List<byte[]> Value16Records = new List<byte[]>();

            public SEmbeddedAnimData(FileReader reader)
            {
                // Native order: uVar18, uVar33, uVar28, uVar41, uVar44, uVar46, uVar50, uVar47.
                TypeRecordCount = reader.ReadUInt16();
                Record12ByteCount = reader.ReadUInt16();
                LinkRecordCount = reader.ReadUInt16();
                Record6ByteCount = reader.ReadUInt16();
                HeaderValueA = reader.ReadUInt16();
                HeaderValueB = reader.ReadUInt16();
                UIntRecordCount = reader.ReadUInt16();
                Value16RecordCount = reader.ReadUInt16();

                for (int i = 0; i < TypeRecordCount; i++)
                {
                    TypedRecords.Add(new SEmbeddedTypedRecord(reader));
                }
                for (int i = 0; i < Record12ByteCount; i++)
                {
                    Records12.Add(new SEmbeddedRecord12(reader));
                }
                for (int i = 0; i < LinkRecordCount; i++)
                {
                    LinkRecords.Add(new SEmbeddedLinkRecord(reader));
                }
                for (int i = 0; i < Record6ByteCount; i++)
                {
                    Records6.Add(new SEmbeddedRecord6(reader));
                }
                for (int i = 0; i < UIntRecordCount; i++)
                {
                    UIntRecords.Add(reader.ReadUInt32());
                }
                for (int i = 0; i < Value16RecordCount; i++)
                {
                    Value16Records.Add(ReadExactBytes(reader, 16, "embedded animation 16-byte record"));
                }
            }
        }

        public class SEmbeddedTypedRecord
        {
            public byte Type;
            public uint WordA;
            public uint WordB;
            public uint Count;
            public uint[] ArrayA = Array.Empty<uint>();
            public uint[] ArrayB = Array.Empty<uint>();

            public SEmbeddedTypedRecord(FileReader reader)
            {
                Type = reader.ReadByte();
                switch (Type)
                {
                    case 1:
                        WordA = reader.ReadUInt32();
                        WordB = reader.ReadUInt32();
                        break;
                    case 2:
                        WordA = reader.ReadUInt32();
                        break;
                    case 4:
                        Count = reader.ReadUInt32();
                        int count = CheckedCount(Count, "embedded typed-record array");
                        ArrayA = new uint[count];
                        ArrayB = new uint[count];
                        for (int i = 0; i < count; i++) ArrayA[i] = reader.ReadUInt32();
                        for (int i = 0; i < count; i++) ArrayB[i] = reader.ReadUInt32();
                        break;
                        // Native type 0 has no payload; type 3 has no switch case and consumes none.
                }
            }
        }

        public class SEmbeddedRecord12
        {
            public uint A;
            public uint B;
            public uint C;

            public SEmbeddedRecord12(FileReader reader)
            {
                A = reader.ReadUInt32();
                B = reader.ReadUInt32();
                C = reader.ReadUInt32();
            }
        }

        public class SEmbeddedLinkRecord
        {
            public uint FieldA;
            public ushort FieldB;
            public bool FlagA;
            public bool FlagB;
            public ushort LinkCount;
            public List<SEmbeddedLink> Links = new List<SEmbeddedLink>();

            public SEmbeddedLinkRecord(FileReader reader)
            {
                long recordStart = reader.BaseStream.Position;

                // FUN_7100ba9a5c reads this exact serialized order:
                // uint32 field, ushort link count, two one-byte flags, then links.
                // FieldB is the serialized count; it must not be read a second time
                // after the flags (that would consume the first link's A field).
                FieldA = reader.ReadUInt32();
                FieldB = reader.ReadUInt16();
                LinkCount = FieldB;
                FlagA = reader.ReadByte() != 0;
                FlagB = reader.ReadByte() != 0;

                // Each serialized child link consumes 9 bytes: four ushorts and a byte.
                // Native storage packs that final byte into a flag bit, but the byte is
                // still present in the file. Fail here with context if counts/position
                // are inconsistent instead of eventually throwing at an arbitrary field.
                long remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
                long requiredLinkBytes = (long)LinkCount * 9;
                if (requiredLinkBytes > remainingBytes)
                {
                    throw new InvalidDataException(
                        $"Embedded animation link record at 0x{recordStart:X8} declares " +
                        $"{LinkCount} links ({requiredLinkBytes} bytes), but only " +
                        $"{remainingBytes} bytes remain in the stream.");
                }

                for (int i = 0; i < LinkCount; i++)
                {
                    Links.Add(new SEmbeddedLink(reader));
                }
            }
        }

        public class SEmbeddedLink
        {
            public ushort A;
            public ushort B;
            public ushort C;
            public ushort D;
            public byte Flag;

            public SEmbeddedLink(FileReader reader)
            {
                A = reader.ReadUInt16();
                B = reader.ReadUInt16();
                C = reader.ReadUInt16();
                D = reader.ReadUInt16();
                Flag = reader.ReadByte();
            }
        }

        public class SEmbeddedRecord6
        {
            public ushort A;
            public ushort B;
            public ushort C;

            public SEmbeddedRecord6(FileReader reader)
            {
                A = reader.ReadUInt16();
                B = reader.ReadUInt16();
                C = reader.ReadUInt16();
            }
        }
        #endregion

        #region Event Stream Data
        public class SEventStreamDataSet
        {
            public uint EntryCount;
            public List<SEventStreamEntry> Entries = new List<SEventStreamEntry>();

            public SEventStreamDataSet(FileReader reader)
            {
                EntryCount = reader.ReadUInt32();
                int count = CheckedCount(EntryCount, "event-stream entry");
                for (int i = 0; i < count; i++)
                {
                    Entries.Add(new SEventStreamEntry(reader));
                }
            }
        }

        public class SEventStreamEntry
        {
            public ushort Type;
            public ushort Size;
            public byte[] Data = Array.Empty<byte>();

            public SEventStreamEntry(FileReader reader)
            {
                Type = reader.ReadUInt16();
                Size = reader.ReadUInt16();
                if (Size > short.MaxValue)
                {
                    throw new InvalidDataException($"Event-stream payload size exceeds the native signed-short range: {Size}.");
                }
                Data = ReadExactBytes(reader, Size, "event-stream entry payload");
            }
        }
        #endregion

        #region Name Pool
        public class CPooledName
        {
            public bool HasName;
            public uint NameID;

            public CPooledName(FileReader reader)
            {
                HasName = reader.ReadByte() != 0; // currently errors here
                if (HasName)
                {
                    NameID = reader.ReadUInt32();
                }
            }
        }

        public class NamePool
        {
            public List<string> Strings = new List<string>();
            public List<byte[]> StringBytes = new List<byte[]>();
            public List<NameGroup> StringGroups = new List<NameGroup>();
            public List<HashList> HashLists = new List<HashList>();
            // These first two tables are capacities used by the native allocator. Actual counts
            // are serialized afterward, separately for each string group and hash list.
            public uint[] StringGroupCounts;
            public uint[] HashListCapacityCounts;
            public uint[] StringGroupEntryCounts;

            public NamePool(FileReader reader)
            {
                uint stringTableSize = reader.ReadUInt32();
                byte[] stringTable = ReadExactBytes(reader, CheckedByteLength(stringTableSize, "NamePool string table"), "NamePool string table");
                int stringOffset = 0;
                while (stringOffset < stringTable.Length)
                {
                    int terminator = Array.IndexOf(stringTable, (byte)0, stringOffset);
                    if (terminator < 0)
                    {
                        throw new InvalidDataException("A CHPR NamePool string is not NUL-terminated within the declared string table.");
                    }

                    int stringLength = terminator - stringOffset;
                    byte[] rawString = new byte[stringLength];
                    Array.Copy(stringTable, stringOffset, rawString, 0, stringLength);
                    StringBytes.Add(rawString);
                    Strings.Add(Encoding.UTF8.GetString(rawString));
                    stringOffset = terminator + 1;
                }

                ushort groupCount = reader.ReadUInt16();
                ushort hashCount = reader.ReadUInt16();
                StringGroupCounts = reader.ReadUInt32s(groupCount);
                HashListCapacityCounts = reader.ReadUInt32s(hashCount);
                StringGroupEntryCounts = new uint[groupCount];

                int stringIndex = 0;
                for (int groupIndex = 0; groupIndex < groupCount; groupIndex++)
                {
                    uint entryCount = reader.ReadUInt32();
                    StringGroupEntryCounts[groupIndex] = entryCount;
                    int count = CheckedCount(entryCount, "NamePool string-group entry");
                    var group = new NameGroup();

                    for (int entryIndex = 0; entryIndex < count; entryIndex++)
                    {
                        if (stringIndex >= Strings.Count)
                        {
                            throw new InvalidDataException("NamePool string-group counts exceed the number of strings in the string table.");
                        }

                        string value = Strings[stringIndex++];
                        group.Entries.Add(new NameGroupEntry
                        {
                            StringIndex = (uint)(stringIndex - 1),
                            Value = value,
                            Hash = ComputeFNV1a(StringBytes[stringIndex - 1]),
                            PackedReference = ((uint)groupIndex << 28) | (uint)entryIndex
                        });
                    }

                    StringGroups.Add(group);
                }

                for (int i = 0; i < hashCount; i++)
                {
                    HashLists.Add(new HashList(reader));
                }
            }

            public string GetString(int set, int index)
            {
                if (set < 0 || set >= HashLists.Count || index < 0 || index >= HashLists[set].StringIDs.Length)
                {
                    return string.Empty;
                }

                // NamePool hash-list values are packed (group << 28) | entryIndex
                // references, not flat indices into the original string blob.
                uint packedReference = HashLists[set].StringIDs[index];
                NameGroupEntry entry = Resolve(packedReference);
                return entry?.Value ?? string.Empty;
            }

            public string GetString(CPooledName name)
            {
                if (name == null || !name.HasName)
                {
                    return string.Empty;
                }
                NameGroupEntry entry = Resolve(name.NameID);
                return entry?.Value ?? string.Empty;
            }

            public uint GetHash(CPooledName name)
            {
                if (name == null || !name.HasName)
                {
                    return 0;
                }
                NameGroupEntry entry = Resolve(name.NameID);
                return entry?.Hash ?? 0;
            }

            public bool TryGetStringHashFromHashList(ushort set, int index, out uint hash)
            {
                hash = 0;
                if (set >= HashLists.Count || index < 0 || index >= HashLists[set].Hashes.Length)
                {
                    return false;
                }
                hash = HashLists[set].Hashes[index];
                return true;
            }

            private NameGroupEntry Resolve(uint packedReference)
            {
                uint groupIndex = packedReference >> 28;
                uint entryIndex = packedReference & 0x0FFFFFFFu;
                if (groupIndex >= StringGroups.Count || entryIndex >= StringGroups[(int)groupIndex].Entries.Count)
                {
                    return null;
                }
                return StringGroups[(int)groupIndex].Entries[(int)entryIndex];
            }

            private static int CheckedCount(uint count, string label)
            {
                if (count > 1_000_000)
                {
                    throw new InvalidDataException($"Unreasonable {label} count: {count}.");
                }
                return checked((int)count);
            }

            public static uint ComputeFNV1a(string value)
            {
                return ComputeFNV1a(Encoding.UTF8.GetBytes(value ?? string.Empty));
            }

            public static uint ComputeFNV1a(byte[] valueBytes)
            {
                uint hash = 0x811C9DC5;
                if (valueBytes != null)
                {
                    foreach (byte b in valueBytes)
                    {
                        hash = unchecked((hash ^ b) * 0x01000193);
                    }
                }
                return hash;
            }
        }

        public class NameGroup
        {
            public List<NameGroupEntry> Entries = new List<NameGroupEntry>();
        }

        public class NameGroupEntry
        {
            public uint StringIndex;
            public string Value;
            public uint Hash;
            public uint PackedReference;
        }

        public class HashList
        {
            public uint[] Hashes;
            public uint[] StringIDs;

            public HashList(FileReader reader)
            {
                uint count = reader.ReadUInt32();
                if (count > 1_000_000)
                {
                    throw new InvalidDataException($"Unreasonable CHPR NamePool hash-list count: {count}.");
                }
                Hashes = reader.ReadUInt32s((int)count);
                StringIDs = reader.ReadUInt32s((int)count);
            }

            public void Write(FileWriter writer)
            {
                writer.Write((uint)Hashes.Length);
                writer.Write(Hashes);
                writer.Write(StringIDs);
            }
        }
        #endregion

        #region Helpers
        private static string ReadLengthPrefixedString(FileReader reader, string label)
        {
            uint byteCount = reader.ReadUInt32();
            int count = CheckedByteLength(byteCount, label);
            return Encoding.UTF8.GetString(ReadExactBytes(reader, count, label));
        }
        #endregion

        #region Helpers for debugging
        private static byte[] ReadExactBytes(FileReader reader, int length, string label)
        {
            if (length < 0)
                throw new InvalidDataException($"Negative byte count while reading {label}: {length}.");

            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException($"Unexpected end of stream while reading {label}.");
            return bytes;
        }

        private static int CheckedCount(uint count, string label, uint maximum = 1_000_000)
        {
            if (count > maximum)
                throw new InvalidDataException($"Unreasonable {label} count: {count}.");
            return checked((int)count);
        }

        private static int CheckedByteLength(uint length, string label, uint maximum = 64 * 1024 * 1024)
        {
            if (length > maximum)
                throw new InvalidDataException($"Unreasonable {label} byte length: {length}.");
            return checked((int)length);
        }
        #endregion

        #region CHPR Meta Data
        public class CCharInfoMetaData
        {
            public byte[] assetType = new byte[2];
            public byte versionA;
            public byte versionB;

            public CCharInfoMetaDataLoaderDescriptor loaderDescriptor;
        }

        public class CCharInfoMetaDataLoaderDescriptor
        {
            public uint packedCountsAndFlags;
            public uint allocationSizeInput;
            public uint charInfoBodyOffset;
            public uint charInfoBodySize;
            public uint additionalOffsetOrBoundary;

            public CCharInfoMetaDataLoaderTableEntry arrayAInitialEntry;
            public CCharInfoMetaDataLoaderTableEntry arrayBInitialEntry;
        }

        public class CCharInfoMetaDataLoaderTableEntry
        {
            public uint field0;
            public uint field4;
            public uint field8;
        }
        #endregion

    }
}