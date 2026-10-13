using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RetroStudioPlugin.Files.FileData;

/// <summary>
/// Reads the CHPR animation node table and hierarchy records needed to connect decoded pose
/// addresses to the character's named joints. This first-stage skeleton reader deliberately
/// leaves bind-transform reconstruction to a separate pass.
/// 
/// Reverse-engineered layout follows Infinite00857/Mocktroid (MIT):
/// https://github.com/Infinite00857/Mocktroid
/// </summary>
public sealed class CHPRAnimationSkeleton
{
    private const int JointBankSize = 170;

    public sealed class Joint
    {
        public int Index { get; internal set; }
        public string Name { get; internal set; } = string.Empty;
        public int ParentIndex { get; internal set; } = -1;

        /// <summary>Packed game address: slot | (index &lt;&lt; 6), or -1 if no record was found.</summary>
        public int PoseAddress { get; internal set; } = -1;

        /// <summary>Raw transform-table index for non-slot-major hierarchy records, or -1.</summary>
        public int LocalTransformIndex { get; internal set; } = -1;

        /// <summary>Slot and row encoded by a type-04 record; row must be resolved against slot sizes.</summary>
        public int TransformSlot { get; internal set; } = -1;
        public int TransformRow { get; internal set; } = -1;
    }

    public sealed record PoseMapping(
        IReadOnlyDictionary<int, int> BoneIndexToJointIndex,
        int TransformCount,
        int MappedTransforms,
        int UnmappedTransforms,
        int MatchedJoints);

    private readonly List<Joint> _joints = new();
    private readonly Dictionary<int, int> _jointByPoseAddress = new();

    public IReadOnlyList<Joint> Joints => _joints;
    public IReadOnlyDictionary<int, int> JointByPoseAddress => _jointByPoseAddress;

    private CHPRAnimationSkeleton() { }

    public static CHPRAnimationSkeleton Read(byte[] decompressedChpr)
    {
        ArgumentNullException.ThrowIfNull(decompressedChpr);

        var result = new CHPRAnimationSkeleton();
        List<string> strings = CHPRAnimationInfo.ReadStrings(decompressedChpr);
        if (strings.Count == 0)
            return result;

        int stringTableEnd = FindStringTableEnd(decompressedChpr);
        if (stringTableEnd < 0 || stringTableEnd > decompressedChpr.Length)
            return result;

        List<string> nodes = ReadNodeNames(decompressedChpr, stringTableEnd, strings);
        if (nodes.Count == 0)
            return result;

        result.CreateJointList(decompressedChpr, stringTableEnd, nodes);
        result.ReadHierarchy(decompressedChpr, stringTableEnd);
        return result;
    }

    /// <summary>
    /// Associates decoded pose transforms with the existing CHPR CharacterInfo.Bones by name.
    /// Transform addresses that do not belong to a parsed joint remain unmatched; they may be
    /// scalar channels, root motion, or joints whose hierarchy entry is absent.
    /// </summary>
    public PoseMapping MapPoseToBones(
        CHPRAnimationDecoder.Pose pose,
        IReadOnlyList<CHPR.BoneData> bones)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(bones);

        var boneIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < bones.Count; i++)
        {
            string? name = bones[i]?.Name;
            if (!string.IsNullOrEmpty(name) && !boneIndexByName.ContainsKey(name))
                boneIndexByName.Add(name, i);
        }

        var jointByBone = new Dictionary<int, int>();
        var transformToBone = new Dictionary<int, int>();
        int matchedJoints = 0;

        foreach (Joint joint in _joints)
        {
            if (joint.PoseAddress < 0 || !boneIndexByName.TryGetValue(joint.Name, out int boneIndex))
                continue;

            matchedJoints++;
            jointByBone.TryAdd(boneIndex, joint.Index);
            if (pose.Transforms.ContainsKey(joint.PoseAddress))
                transformToBone.TryAdd(joint.PoseAddress, boneIndex);
        }

        var mapped = new Dictionary<int, int>();
        foreach (var pair in transformToBone)
        {
            int jointIndex = _joints.First(j => j.PoseAddress == pair.Key && j.Name == bones[pair.Value].Name).Index;
            mapped[pair.Value] = jointIndex;
        }

        return new PoseMapping(
            mapped,
            pose.Transforms.Count,
            transformToBone.Count,
            pose.Transforms.Count - transformToBone.Count,
            matchedJoints);
    }

    private void CreateJointList(byte[] data, int stringTableEnd, IReadOnlyList<string> nodes)
    {
        int table = -1;
        byte[] signature = { 0, 0, 0x62 };

        for (int i = IndexOf(data, signature, stringTableEnd); i >= 0; i = IndexOf(data, signature, i + 1))
        {
            if (i + 7 <= data.Length &&
                data[i + 4] == 0x80 &&
                data[i + 5] == 1 &&
                data[i + 6] == 0x62)
            {
                table = i;
                break;
            }
        }

        if (table < 0)
        {
            // Some files don't expose a detectable node table. Preserve the ordered names as a
            // fallback, but don't invent pose addresses for them.
            for (int i = 0; i < nodes.Count; i++)
                _joints.Add(new Joint { Index = i, Name = nodes[i] });
            return;
        }

        var namesByIndex = new Dictionary<int, string>();
        int maxJoint = -1;

        for (int node = 0; node < nodes.Count && table + 4L * node + 3 < data.Length; node++)
        {
            int value = U16(data, table + 4 * node);
            int kind = data[table + 4 * node + 2];

            if (kind == 0x62)
            {
                int jointIndex = JointBankSize * (value & 0x7F) + (value >> 7) / 3;
                namesByIndex.TryAdd(jointIndex, nodes[node]);
                maxJoint = Math.Max(maxJoint, jointIndex);
            }
            else if (kind != 0x60 && kind != 0x61)
            {
                break;
            }
        }

        for (int index = 0; index <= maxJoint; index++)
        {
            _joints.Add(new Joint
            {
                Index = index,
                Name = namesByIndex.TryGetValue(index, out string? name) ? name : $"joint{index}"
            });
        }
    }

    private void ReadHierarchy(byte[] data, int stringTableEnd)
    {
        int start = -1;
        for (int offset = stringTableEnd; offset < data.Length - 32; offset++)
        {
            if (!IsHierarchyRecord(data, offset))
                continue;

            int a = U16(data, offset + 1);
            if (a % 6 == 0 && a >= 6 && a <= 6 * 32 &&
                CountSequentialRecords(data, offset, a / 6) >= 20)
            {
                start = offset;
                break;
            }
        }

        if (start < 0)
            return;

        int cursor = start;
        int selfBank = 0;
        int parentBank = 0;
        int nextRecord = U16(data, cursor + 1) / 6;

        while (cursor < data.Length - 9)
        {
            if (data[cursor] == 3 &&
                data[cursor + 1] < 16 &&
                data[cursor + 2] < 16)
            {
                int newBank = data[cursor + 1];
                if (newBank != selfBank)
                    nextRecord = 0;

                selfBank = newBank;
                parentBank = data[cursor + 2];
                cursor += 3;
                continue;
            }

            int hit = -1;
            for (int skip = 0; skip < 4 && cursor + skip + 6 < data.Length; skip++)
            {
                int candidate = cursor + skip;
                if (IsHierarchyRecord(data, candidate) &&
                    U16(data, candidate + 1) == 6 * nextRecord)
                {
                    hit = candidate;
                    break;
                }
            }

            if (hit < 0)
            {
                hit = Resync(data, cursor, nextRecord);
                if (hit < 0)
                    break;

                nextRecord = U16(data, hit + 1) / 6;
            }

            cursor = hit;
            int type = data[cursor];
            int aValue = U16(data, cursor + 1);
            int bValue = U16(data, cursor + 3);
            int cValue = U16(data, cursor + 5);
            int jointIndex = JointBankSize * selfBank + aValue / 6;

            if (jointIndex >= 0 && jointIndex < _joints.Count)
            {
                Joint joint = _joints[jointIndex];
                if (bValue % 6 == 0)
                {
                    int parent = JointBankSize * parentBank + bValue / 6;
                    joint.ParentIndex = parent >= 0 && parent < _joints.Count && parent != jointIndex
                        ? parent
                        : -1;
                }

                joint.PoseAddress = cValue;
                if (type == 4)
                {
                    joint.TransformSlot = cValue & 63;
                    joint.TransformRow = (cValue >> 6) / 5;
                }
                else
                {
                    joint.LocalTransformIndex = cValue / 7;
                }

                _jointByPoseAddress.TryAdd(cValue, jointIndex);
            }

            cursor += type == 7 ? 9 : 7;
            nextRecord++;
        }
    }

    private static int CountSequentialRecords(byte[] data, int start, int firstIndex)
    {
        int count = 0;
        int cursor = start;
        while (count < 40)
        {
            int hit = -1;
            for (int skip = 0; skip < 4; skip++)
            {
                int candidate = cursor + skip;
                if (candidate + 3 < data.Length &&
                    IsHierarchyRecord(data, candidate) &&
                    U16(data, candidate + 1) == 6 * (firstIndex + count))
                {
                    hit = candidate;
                    break;
                }
            }

            if (hit < 0)
                break;

            cursor = hit + (data[hit] == 7 ? 9 : 7);
            count++;
        }

        return count;
    }

    private static int Resync(byte[] data, int start, int expectedIndex)
    {
        int end = (int)Math.Min(data.Length - 18L, start + 48L);
        for (int candidate = start; candidate < end; candidate++)
        {
            if (!IsHierarchyRecord(data, candidate))
                continue;

            int a = U16(data, candidate + 1);
            int b = U16(data, candidate + 3);
            if (a % 6 != 0 ||
                a / 6 <= expectedIndex ||
                a / 6 > expectedIndex + 16 ||
                b % 6 != 0)
                continue;

            int next = candidate + (data[candidate] == 7 ? 9 : 7);
            if (next + 3 < data.Length &&
                IsHierarchyRecord(data, next) &&
                U16(data, next + 1) == a + 6)
                return candidate;
        }

        return -1;
    }

    private static bool IsHierarchyRecord(byte[] data, int offset) =>
        offset >= 0 && offset < data.Length &&
        (data[offset] == 4 || data[offset] == 5 || data[offset] == 7) &&
        offset + 7 <= data.Length;

    private static List<string> ReadNodeNames(byte[] data, int stringTableEnd, IReadOnlyList<string> strings)
    {
        var stringIndexByHash = new Dictionary<int, int>();
        for (int i = 0; i < strings.Count; i++)
            stringIndexByHash.TryAdd(Fnv1A(strings[i]), i);

        List<string> best = new();
        int cursor = stringTableEnd;
        int end = data.Length - 8;

        while (cursor < end)
        {
            if (!stringIndexByHash.ContainsKey(I32(data, cursor)))
            {
                cursor++;
                continue;
            }

            int count = 0;
            while (cursor + 4L * count < end &&
                   stringIndexByHash.ContainsKey(I32(data, cursor + 4 * count)))
                count++;

            if (count >= 3 && cursor + 8L * count <= data.Length)
            {
                var candidate = new List<string>(count);
                bool valid = true;

                for (int i = 0; i < count; i++)
                {
                    uint stringIndex = U32(data, cursor + 4 * count + 4 * i);
                    if (stringIndex >= strings.Count)
                    {
                        valid = false;
                        break;
                    }

                    candidate.Add(strings[(int)stringIndex]);
                }

                if (valid)
                {
                    if (candidate[0] == "root.anchorBlend" && candidate.Count > best.Count)
                        best = candidate;

                    cursor += 8 * count;
                    continue;
                }
            }

            cursor += 4 * Math.Max(count, 1);
        }

        return best;
    }

    private static int FindStringTableEnd(byte[] data)
    {
        int header = 0x2C;
        while (header >= 0 && header <= data.Length - 12)
        {
            uint index = U32(data, header);
            uint offset = U32(data, header + 4);
            uint size = U32(data, header + 8);

            bool plausible = offset > 0x30 &&
                offset < data.Length &&
                (ulong)offset + size <= (ulong)data.Length &&
                index < 0x100000;

            if (!plausible)
                break;

            header += 12;
        }

        if (header <= data.Length - 4)
        {
            uint stringSize = U32(data, header);
            if (stringSize > 0 && stringSize <= int.MaxValue &&
                (ulong)header + 4 + stringSize <= (ulong)data.Length)
                return header + 4 + (int)stringSize;
        }

        byte[] needle = Encoding.ASCII.GetBytes("Primary\0");
        for (int at = IndexOf(data, needle, 4); at >= 4; at = IndexOf(data, needle, at + 1))
        {
            uint stringSize = U32(data, at - 4);
            if (stringSize > 0 && stringSize <= int.MaxValue &&
                (ulong)at + stringSize <= (ulong)data.Length)
                return at + (int)stringSize;
        }

        return -1;
    }

    private static int Fnv1A(string value)
    {
        unchecked
        {
            int hash = (int)0x811C9DC5;
            foreach (byte b in Encoding.Latin1.GetBytes(value))
                hash = (hash ^ b) * 0x01000193;
            return hash;
        }
    }

    private static int IndexOf(byte[] data, byte[] needle, int start)
    {
        if (needle.Length == 0)
            return Math.Clamp(start, 0, data.Length);

        for (int i = Math.Max(0, start); i <= data.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && data[i + j] == needle[j])
                j++;
            if (j == needle.Length)
                return i;
        }

        return -1;
    }

    private static ushort U16(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, sizeof(ushort)));

    private static uint U32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, sizeof(uint)));

    private static int I32(byte[] data, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, sizeof(int)));
}
