using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DKCTF
{
    /// <summary>
    /// Experimental mapper from CHPR hierarchy records to animation pose addresses.
    ///
    /// The CompStream output address is known to be slot | (index << 6), but the
    /// current CharacterInfo reader doesn't directly expose the hierarchy record
    /// which owns that address. This parser follows the observed 0x03 bank selectors
    /// and 0x04/0x05/0x07 hierarchy records. The containing tables are located by
    /// signatures, so callers must treat an unsuccessful map as unknown, not guess.
    /// </summary>
    internal static class CHPRPoseAddressMapper
    {
        private const int JointBankSize = 170;

        internal sealed class MappingResult
        {
            public bool Success { get; internal set; }
            public int StringTableOffset { get; internal set; } = -1;
            public int StringTableEnd { get; internal set; } = -1;
            public int StringCount { get; internal set; }
            public int NameListOffset { get; internal set; } = -1;
            public int NameListCount { get; internal set; }
            public int NodeTableOffset { get; internal set; } = -1;
            public int HierarchyOffset { get; internal set; } = -1;
            public int HierarchyRecords { get; internal set; }
            // Counts hierarchy records that assigned a pose address; repeated assignments to one bone count repeatedly.
            public int AddressesAssigned { get; internal set; }
            // Type-5 records matched to a named bone. These store a transform-table index rather than a pose address.
            public int Type5Records { get; internal set; }
            // Number of BoneData entries that retain a pose address after all hierarchy records were applied.
            public int BonesWithFinalAddress { get; internal set; }
            public string Error { get; internal set; }
        }

        private sealed class HierarchyEntry
        {
            public int JointIndex;
            public int RecordType;
            public int PoseAddress;
        }

        public static MappingResult Assign(byte[] bodyData, CHPR.CharacterInfo characterInfo)
        {
            var result = new MappingResult();
            if (bodyData == null || bodyData.Length < 16)
            {
                result.Error = "CHPR body is too short for hierarchy-table scanning.";
                return result;
            }
            if (characterInfo?.NamePool?.Strings == null || characterInfo.Bones == null)
            {
                result.Error = "CharacterInfo is missing its name pool or bone list.";
                return result;
            }

            if (!TryFindStringTable(bodyData, out int stringTableOffset,
                    out int stringTableEnd, out List<string> strings))
            {
                result.Error = "Could not locate the CHPR top-level string table.";
                return result;
            }
            result.StringTableOffset = stringTableOffset;
            result.StringTableEnd = stringTableEnd;
            result.StringCount = strings.Count;

            // The node-name hashes index the top-level CHPR string table in the raw body,
            // not necessarily CharacterInfo.NamePool.Strings. Those two pools may differ
            // in large/multi-segment projects.
            var hashToIndex = new Dictionary<uint, int>();
            for (int i = 0; i < strings.Count; i++)
            {
                uint hash = Fnv1a(strings[i] ?? string.Empty);
                if (!hashToIndex.ContainsKey(hash))
                    hashToIndex.Add(hash, i);
            }

            if (!TryFindNodeNameList(bodyData, stringTableEnd, strings, hashToIndex,
                    out List<string> nodeNames, out int nameListOffset))
            {
                result.Error = "Could not locate a hash/string-index node-name list.";
                return result;
            }
            result.NameListOffset = nameListOffset;
            result.NameListCount = nodeNames.Count;

            if (!TryFindNodeTable(bodyData, stringTableEnd, nodeNames,
                    out Dictionary<int, string> jointNames, out int nodeTableOffset))
            {
                result.Error = "Could not locate the joint/channel node table.";
                return result;
            }
            result.NodeTableOffset = nodeTableOffset;

            if (!TryReadHierarchy(bodyData, stringTableEnd, out List<HierarchyEntry> entries,
                    out int hierarchyOffset))
            {
                result.Error = "Could not locate a sufficiently long sequential hierarchy-record run.";
                return result;
            }
            result.HierarchyOffset = hierarchyOffset;
            result.HierarchyRecords = entries.Count;

            var bonesByName = new Dictionary<string, List<CHPR.BoneData>>(StringComparer.Ordinal);
            foreach (CHPR.BoneData bone in characterInfo.Bones)
            {
                if (string.IsNullOrEmpty(bone.Name))
                    continue;
                if (!bonesByName.TryGetValue(bone.Name, out List<CHPR.BoneData> matches))
                {
                    matches = new List<CHPR.BoneData>();
                    bonesByName.Add(bone.Name, matches);
                }
                matches.Add(bone);
                bone.PoseAddress = -1;
                bone.PoseAddressRecordType = 0;
            }

            foreach (HierarchyEntry entry in entries)
            {
                if (!jointNames.TryGetValue(entry.JointIndex, out string name))
                    continue;
                if (!bonesByName.TryGetValue(name, out List<CHPR.BoneData> matches))
                    continue;

                foreach (CHPR.BoneData bone in matches)
                {
                    // Type 5 stores a transform-table index (c/7), not an animation
                    // address. It must clear any earlier address for the same joint
                    // because the later hierarchy record wins in the observed parser.
                    if (entry.RecordType == 5)
                    {
                        result.Type5Records++;
                        bone.PoseAddress = -1;
                        bone.PoseAddressRecordType = 5;
                        continue;
                    }

                    bone.PoseAddress = entry.PoseAddress;
                    bone.PoseAddressRecordType = (byte)entry.RecordType;
                    result.AddressesAssigned++;
                }
            }

            result.BonesWithFinalAddress = characterInfo.Bones.Count(bone => bone.PoseAddress >= 0);
            result.Success = result.BonesWithFinalAddress > 0;
            if (!result.Success)
                result.Error = "Hierarchy records were found, but no CharacterInfo bones retained a pose address.";
            return result;
        }

        internal static bool TryFindStringTable(
            byte[] data,
            out int stringTableOffset,
            out int stringTableEnd,
            out List<string> strings)
        {
            stringTableOffset = -1;
            stringTableEnd = -1;
            strings = null;

            // Common layout: RFRM header, zero or more side-blob triples, then
            // u32 string-byte-count followed by the NUL-separated string table.
            int o = 0x2C;
            while (o + 12 <= data.Length)
            {
                uint index = ReadUInt32(data, o);
                uint offset = ReadUInt32(data, o + 4);
                uint size = ReadUInt32(data, o + 8);

                if (offset > 0x30 && offset < data.Length &&
                    (ulong)offset + size <= (ulong)data.Length && index < 0x100000)
                {
                    o += 12;
                }
                else
                {
                    break;
                }
            }

            List<string> standardStrings = null;
            int standardOffset = -1;
            int standardEnd = -1;
            if (TryReadStringTableAt(data, o, out standardOffset, out standardEnd, out standardStrings) &&
                standardStrings.Count > 0 &&
                string.Equals(standardStrings[0], "Primary", StringComparison.Ordinal))
            {
                stringTableOffset = standardOffset;
                stringTableEnd = standardEnd;
                strings = standardStrings;
                return true;
            }

            // Large/multi-segment CHPRs can have a different leading header. In that
            // layout, find the table by its documented first string ("Primary\0"),
            // and validate the preceding size field and terminating NUL before accepting it.
            byte[] primaryNeedle = Encoding.Latin1.GetBytes("Primary\0");
            int contentStart = IndexOf(data, primaryNeedle, 4);
            while (contentStart >= 4)
            {
                int sizeFieldOffset = contentStart - 4;
                if (TryReadStringTableAt(data, sizeFieldOffset,
                        out int candidateOffset, out int candidateEnd, out List<string> candidateStrings) &&
                    candidateOffset == contentStart &&
                    candidateStrings.Count > 0 &&
                    string.Equals(candidateStrings[0], "Primary", StringComparison.Ordinal))
                {
                    stringTableOffset = candidateOffset;
                    stringTableEnd = candidateEnd;
                    strings = candidateStrings;
                    return true;
                }

                contentStart = IndexOf(data, primaryNeedle, contentStart + 1);
            }

            // Keep support for a different but structurally valid table whose first
            // entry isn't "Primary". Prefer content search above whenever available.
            if (standardStrings != null && standardStrings.Count > 0)
            {
                stringTableOffset = standardOffset;
                stringTableEnd = standardEnd;
                strings = standardStrings;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reads a string table whose u32 byte count is at sizeFieldOffset and whose
        /// bytes begin immediately after it. Offsets returned are relative to data[0].
        /// </summary>
        private static bool TryReadStringTableAt(
            byte[] data,
            int sizeFieldOffset,
            out int stringTableOffset,
            out int stringTableEnd,
            out List<string> strings)
        {
            stringTableOffset = -1;
            stringTableEnd = -1;
            strings = null;

            if (sizeFieldOffset < 0 || sizeFieldOffset > data.Length - 4)
                return false;

            uint byteCount = ReadUInt32(data, sizeFieldOffset);
            if (byteCount == 0 || byteCount > int.MaxValue ||
                (ulong)sizeFieldOffset + 4UL + byteCount > (ulong)data.Length)
                return false;

            int contentOffset = sizeFieldOffset + 4;
            int count = (int)byteCount;
            if (data[contentOffset + count - 1] != 0)
                return false;

            string text = Encoding.Latin1.GetString(data, contentOffset, count);
            string[] parts = text.Split('\0');
            var parsed = new List<string>(parts);
            if (parsed.Count > 0 && parsed[parsed.Count - 1].Length == 0)
                parsed.RemoveAt(parsed.Count - 1);

            if (parsed.Count == 0)
                return false;

            stringTableOffset = contentOffset;
            stringTableEnd = checked(contentOffset + count);
            strings = parsed;
            return true;
        }

        private static bool TryFindNodeNameList(
            byte[] data,
            int stringTableEnd,
            List<string> strings,
            Dictionary<uint, int> hashToIndex,
            out List<string> bestNames,
            out int bestOffset)
        {
            bestNames = null;
            bestOffset = -1;
            bool bestStartsWithAnchorBlend = false;
            int end = data.Length - 8;

            for (int o = Math.Max(0, stringTableEnd); o < end;)
            {
                uint firstHash = ReadUInt32(data, o);
                if (!hashToIndex.ContainsKey(firstHash))
                {
                    o++;
                    continue;
                }

                int n = 0;
                while (o + 4 * n < end && hashToIndex.ContainsKey(ReadUInt32(data, o + 4 * n)))
                    n++;

                if (n >= 3 && (long)o + 8L * n <= data.Length)
                {
                    var names = new List<string>(n);
                    bool valid = true;
                    for (int i = 0; i < n; i++)
                    {
                        uint stringIndex = ReadUInt32(data, o + 4 * n + 4 * i);
                        if (stringIndex >= strings.Count)
                        {
                            valid = false;
                            break;
                        }
                        names.Add(strings[(int)stringIndex]);
                    }

                    if (valid)
                    {
                        bool startsWithAnchorBlend = string.Equals(names[0], "root.anchorBlend", StringComparison.Ordinal);
                        if ((startsWithAnchorBlend && !bestStartsWithAnchorBlend) ||
                            (startsWithAnchorBlend == bestStartsWithAnchorBlend && names.Count > (bestNames?.Count ?? 0)))
                        {
                            bestNames = names;
                            bestOffset = o;
                            bestStartsWithAnchorBlend = startsWithAnchorBlend;
                        }
                    }

                    o += 8 * n;
                }
                else
                {
                    o += 4 * Math.Max(n, 1);
                }
            }

            return bestNames != null;
        }

        private static bool TryFindNodeTable(
            byte[] data,
            int stringTableEnd,
            List<string> nodeNames,
            out Dictionary<int, string> jointNames,
            out int nodeTableOffset)
        {
            jointNames = new Dictionary<int, string>();
            nodeTableOffset = -1;
            int marker = IndexOf(data, new byte[] { 0, 0, 0x62 }, stringTableEnd);
            int table = -1;

            while (marker >= 0)
            {
                if (marker + 7 <= data.Length &&
                    data[marker + 4] == 0x80 &&
                    data[marker + 5] == 1 &&
                    data[marker + 6] == 0x62)
                {
                    table = marker;
                    break;
                }
                marker = IndexOf(data, new byte[] { 0, 0, 0x62 }, marker + 1);
            }

            if (table < 0)
                return false;

            int nameCount = nodeNames.Count;
            int maxJoint = -1;
            for (int i = 0; i < nameCount && table + 4L * i + 3 < data.Length; i++)
            {
                int value = ReadUInt16(data, table + 4 * i);
                int kind = data[table + 4 * i + 2];

                if (kind == 0x62)
                {
                    int jointIndex = JointBankSize * (value & 0x7F) + (value >> 7) / 3;
                    jointNames[jointIndex] = nodeNames[i];
                    maxJoint = Math.Max(maxJoint, jointIndex);
                }
                else if (kind != 0x60 && kind != 0x61)
                {
                    break;
                }
            }

            nodeTableOffset = table;
            return jointNames.Count > 0 && maxJoint >= 0;
        }

        private static bool TryReadHierarchy(
            byte[] data,
            int stringTableEnd,
            out List<HierarchyEntry> entries,
            out int hierarchyOffset)
        {
            entries = new List<HierarchyEntry>();
            hierarchyOffset = -1;
            int start = -1;

            for (int o = Math.Max(0, stringTableEnd); o < data.Length - 32; o++)
            {
                if (!IsRecord(data, o))
                    continue;
                int a = ReadUInt16(data, o + 1);
                if (a % 6 == 0 && a >= 6 && a <= 6 * 32 && CountSequentialRun(data, o, a / 6) >= 20)
                {
                    start = o;
                    break;
                }
            }

            if (start < 0)
                return false;

            hierarchyOffset = start;
            int q = start;
            int sourceBank = 0;
            int next = ReadUInt16(data, q + 1) / 6;

            while (q < data.Length - 9)
            {
                if (data[q] == 3 && data[q + 1] < 16 && data[q + 2] < 16)
                {
                    if (data[q + 1] != sourceBank)
                        next = 0;
                    sourceBank = data[q + 1];
                    q += 3;
                    continue;
                }

                int hit = -1;
                for (int k = 0; k < 4 && q + k + 7 <= data.Length; k++)
                {
                    int p = q + k;
                    if (IsRecord(data, p) && ReadUInt16(data, p + 1) == 6 * next)
                    {
                        hit = p;
                        break;
                    }
                }

                if (hit < 0)
                {
                    hit = Resync(data, q, next);
                    if (hit < 0)
                        break;
                    next = ReadUInt16(data, hit + 1) / 6;
                }

                q = hit;
                int type = data[q];
                int a = ReadUInt16(data, q + 1);
                int c = ReadUInt16(data, q + 5);
                int jointIndex = JointBankSize * sourceBank + a / 6;

                entries.Add(new HierarchyEntry
                {
                    JointIndex = jointIndex,
                    RecordType = type,
                    PoseAddress = type == 5 ? -1 : c
                });

                q += type == 7 ? 9 : 7;
                next++;
            }

            return entries.Count > 0;
        }

        private static int CountSequentialRun(byte[] data, int offset, int initialIndex)
        {
            int count = 0;
            while (count < 40)
            {
                int hit = -1;
                for (int k = 0; k < 4; k++)
                {
                    int p = offset + k;
                    if (p + 3 <= data.Length && IsRecord(data, p) &&
                        ReadUInt16(data, p + 1) == 6 * (initialIndex + count))
                    {
                        hit = p;
                        break;
                    }
                }

                if (hit < 0)
                    break;

                count++;
                offset = hit + (data[hit] == 7 ? 9 : 7);
            }

            return count;
        }

        private static int Resync(byte[] data, int offset, int next)
        {
            for (int p = offset; p < offset + 48 && p + 18 < data.Length; p++)
            {
                if (!IsRecord(data, p))
                    continue;
                int a = ReadUInt16(data, p + 1);
                if (a % 6 != 0 || a / 6 <= next || a / 6 > next + 16 ||
                    ReadUInt16(data, p + 3) % 6 != 0)
                    continue;

                int following = p + (data[p] == 7 ? 9 : 7);
                if (IsRecord(data, following) && ReadUInt16(data, following + 1) == a + 6)
                    return p;
            }
            return -1;
        }

        private static bool IsRecord(byte[] data, int offset)
        {
            return offset >= 0 && offset < data.Length &&
                (data[offset] == 4 || data[offset] == 5 || data[offset] == 7);
        }

        private static int IndexOf(byte[] data, byte[] needle, int from)
        {
            for (int i = Math.Max(0, from); i <= data.Length - needle.Length; i++)
            {
                bool match = true;
                for (int k = 0; k < needle.Length; k++)
                {
                    if (data[i + k] == needle[k])
                        continue;
                    match = false;
                    break;
                }
                if (match)
                    return i;
            }
            return -1;
        }

        private static uint Fnv1a(string value)
        {
            uint hash = 0x811C9DC5;
            foreach (byte b in Encoding.Latin1.GetBytes(value))
                hash = unchecked((hash ^ b) * 0x01000193);
            return hash;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        }
    }
}
