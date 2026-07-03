using AvaloniaToolbox.Core.IO;
using System.Numerics;
#nullable disable

namespace DKCTF
{
    /// <summary>
    /// Represents a skeletal file format.
    /// </summary>
    public class SKEL : FileForm
    {
        public List<string> JointNames = new List<string>();
        public List<string> JointNamesVis = new List<string>();

        public List<JointSet> JointSets = new List<JointSet>();

        public List<BoneCoord> JointCoords = new List<BoneCoord>();

        public byte[] SkinnedBonesRemap = new byte[0];
        public byte[] BoneParentingIndices = new byte[0];

        CAnimationAttrData AnimationAttributes = null;

        CSkelMap SkelMap = null;

        public SKEL() { }

        public SKEL(System.IO.Stream stream) : base(stream)
        {
        }

        public override void Read(FileReader reader)
        {
            reader.ReadStruct<CAssetHeader>(); //version 0x9e22

            //CSkelLayout

            //CJointNameArray
            JointNames = ReadCJointNameArray(reader);

            ushort numTotalJoints = reader.ReadUInt16(); //Includes vis and attribute nodes
            ushort numJoints = reader.ReadUInt16(); //Normal joint list with coordinates associated
            ushort numSkinnedJoints = reader.ReadUInt16();
            ushort numJointSets = reader.ReadUInt16();
            ushort numUnkE = reader.ReadUInt16();
            bool hasSkeletonMap = reader.ReadBoolean();
            //CSkelMap
            if (hasSkeletonMap)
            {
                SkelMap = new CSkelMap();

                ushort numRemap = reader.ReadUInt16();
                byte numUnk1 = reader.ReadByte();
                byte numUnk2 = reader.ReadByte();
                SkelMap.JointIndices = reader.ReadBytes(numRemap);
                SkelMap.Unk2 = reader.ReadUInt16s(numRemap * 2);
                SkelMap.Unk3 = reader.ReadUInt32s(numUnk1);
                SkelMap.Unk4 = reader.ReadInt16s(numUnk2);
                SkelMap.Flag = reader.ReadUInt32();
            }
            bool hasAnimationAttributes = reader.ReadBoolean();
            //CAnimationAttrData
            if (hasAnimationAttributes)
            {
                bool hasVisibilityNameGroup = reader.ReadBoolean();
                if (hasVisibilityNameGroup)
                {
                    JointNamesVis = ReadCJointNameArray(reader);
                }
                uint stateParam1 = reader.ReadUInt32();
                uint stateParam2 = reader.ReadUInt32();

                bool hasAnimationAttributeData = reader.ReadBoolean();
                if (hasAnimationAttributeData)
                {
                    AnimationAttributes = new CAnimationAttrData();

                    AnimationAttributes.Joints = ReadCJointNameArray(reader);
                    uint numAttributes = reader.ReadUInt32();
                    for (int i = 0; i < numAttributes; i++)
                    {
                        CAnimAttrInfo att = new CAnimAttrInfo();

                        att.Flag = reader.ReadUInt32();
                        if (att.Flag == 1)
                        {
                            att.Value1 = reader.ReadSingle();
                            att.Value2 = reader.ReadSingle();
                        }
                        AnimationAttributes.Attributes.Add(att);
                    }
                }
            }
            BoneParentingIndices = reader.ReadBytes((int)numJoints);
            SkinnedBonesRemap = reader.ReadBytes((int)numSkinnedJoints);
            byte[] unkA = reader.ReadBytes((int)numTotalJoints);
            byte[] unkE = reader.ReadBytes((int)numUnkE);
            uint[] unkD = reader.ReadUInt32s((int)numJointSets);

            for (int i = 0; i < numJoints; i++)
            {
                var rot = reader.ReadQuaternion();
                JointCoords.Add(new BoneCoord()
                {
                    Rotation = new Quaternion(rot.Y, rot.Z, rot.W, rot.X),
                    Scale = reader.ReadVector3(),
                    Position = reader.ReadVector3(),
                });
            }
            for (int i = 0; i < numJointSets; i++)
            {
                JointSet jointSet = new JointSet();

                jointSet.unk1 = reader.ReadUInt32();
                uint jointSetCount = reader.ReadUInt32();
                jointSet.unk2 = reader.ReadUInt32s(8);
                jointSet.JointIndices = reader.ReadBytes((int)jointSetCount);

                JointSets.Add(jointSet);
            }
            Console.WriteLine();
        }

        private List<string> ReadCJointNameArray(FileReader reader)
        {
            List<string> joints = new List<string>();

            uint field_0 = reader.ReadUInt32();
            uint count = reader.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                joints.Add(IOFileExtension.ReadFixedString(reader, true));
            }
            uint unk2 = reader.ReadUInt32();

            return joints;
        }

        public class BoneCoord
        {
            public Vector3 Position;
            public Vector3 Scale;
            public Quaternion Rotation;
        }

        public class CSkelMap
        {
            public byte[] JointIndices;
            public ushort[] Unk2;
            public uint[] Unk3;
            public short[] Unk4;
            public uint Flag;
        }

        public class CAnimationAttrData
        {
            public List<string> Joints = new List<string>();
            public List<CAnimAttrInfo> Attributes = new List<CAnimAttrInfo>();
        }

        public class CAnimAttrInfo
        {
            public uint Flag;
            public float Value1;
            public float Value2;
        }

        public class JointSet
        {
            public uint unk1;
            public uint[] unk2;
            public byte[] JointIndices;
        }
    }
}
