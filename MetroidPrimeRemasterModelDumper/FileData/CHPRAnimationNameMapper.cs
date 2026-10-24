using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DKCTF
{
    /// <summary>
    /// Resolves the game's animation names from the CHPR string/name tables.
    /// Clip-name list positions are matched to payload identifiers using clip frame counts.
    /// This remains best-effort because some CHPRs contain payloads without the nearby
    /// clip-record signatures that provide direct index/name/frame-count evidence.
    /// </summary>
    internal static class CHPRAnimationNameMapper
    {
        internal sealed class MappingResult
        {
            public int StringCount;
            public int NameListCount;
            public int ClipRecordCount;
            public int NamesAssigned;
            public string IdentifierField = "unresolved";
            public int IdentifierDelta;
            public int AlignmentMatches;
        }

        private sealed class ClipRecord
        {
            public int Index;
            public string Name;
            public int FrameCount;
        }

        private sealed class Alignment
        {
            public bool UsesOutputSlot;
            public int Delta;
            public int Votes;
            public int Corroborated;
            public int Conflicts;
            public int InRange;
        }

        public static MappingResult Assign(byte[] bodyData, IList<CHPR.AnimationArrayEntry> entries)
        {
            var result = new MappingResult();
            if (entries == null)
                return result;

            foreach (CHPR.AnimationArrayEntry entry in entries)
            {
                entry.GameAnimationName = null;
                entry.GameAnimationNameIndex = -1;
            }

            if (!CHPRPoseAddressMapper.TryFindStringTable(
                    bodyData, out int stringTableOffset, out int stringTableEnd, out List<string> strings))
            {
                return result;
            }

            result.StringCount = strings.Count;

            // First collect signed clip-record candidates without depending on a
            // previously identified name list. Their index/name pairs help select
            // the correct one of the post-string-table hash/index arrays.
            Dictionary<int, ClipRecord> discoveredClipRecords = FindClipRecords(bodyData, strings, null);
            List<string> clipNameList = FindClipNameList(
                bodyData, stringTableEnd, strings, discoveredClipRecords);
            result.NameListCount = clipNameList?.Count ?? 0;

            Dictionary<int, ClipRecord> clipRecords = clipNameList != null
                ? FindClipRecords(bodyData, strings, clipNameList)
                : discoveredClipRecords;
            result.ClipRecordCount = clipRecords.Count;

            Alignment outputSlotAlignment = FindBestAlignment(entries, clipRecords, clipNameList, useOutputSlot: true);
            Alignment baseIndexAlignment = FindBestAlignment(entries, clipRecords, clipNameList, useOutputSlot: false);
            Alignment alignment = ChooseAlignment(outputSlotAlignment, baseIndexAlignment);

            if (alignment != null)
            {
                result.IdentifierField = alignment.UsesOutputSlot ? "OutputSlot" : "BaseInfo.Index";
                result.IdentifierDelta = alignment.Delta;
                result.AlignmentMatches = alignment.Corroborated;
            }

            foreach (CHPR.AnimationArrayEntry entry in entries)
            {
                if (entry.Animation == null)
                    continue;

                int? frameCount = GetKnownFrameCount(entry);
                int baseIndex = entry.Animation.BaseInfo == null ? -1 : entry.Animation.BaseInfo.Index;
                int outputSlot = entry.OutputSlot <= int.MaxValue ? (int)entry.OutputSlot : -1;

                // Prefer explicit clip-record matches, checked against the compressed
                // payload's frame count where available.
                if (TryGetDirectRecordName(baseIndex, frameCount, clipRecords, out string directName, out int directIndex) ||
                    TryGetDirectRecordName(outputSlot, frameCount, clipRecords, out directName, out directIndex))
                {
                    entry.GameAnimationName = directName;
                    entry.GameAnimationNameIndex = directIndex;
                    result.NamesAssigned++;
                    continue;
                }

                // For payloads missing a nearby name record, list D stores the full
                // ordered animation-name list. Infer the constant payload-id offset
                // from the entries for which both a name record and frame count exist.
                if (alignment != null && clipNameList != null)
                {
                    int identifier = alignment.UsesOutputSlot ? outputSlot : baseIndex;
                    long candidateLong = (long)identifier - alignment.Delta;
                    if (identifier >= 0 && candidateLong >= 0 && candidateLong < clipNameList.Count)
                    {
                        int candidate = (int)candidateLong;
                        if (frameCount.HasValue &&
                            clipRecords.TryGetValue(candidate, out ClipRecord expected) &&
                            expected.FrameCount != frameCount.Value)
                        {
                            // Don't attach a name if the candidate contradicts the known
                            // frame count for that clip index.
                            continue;
                        }

                        string candidateName = clipNameList[candidate];
                        if (!string.IsNullOrWhiteSpace(candidateName) &&
                            !string.Equals(candidateName, "Primary", StringComparison.Ordinal))
                        {
                            entry.GameAnimationName = candidateName;
                            entry.GameAnimationNameIndex = candidate;
                            result.NamesAssigned++;
                            continue;
                        }
                    }
                }

                // Last-resort direct list-D lookup is used only when the data did not
                // provide enough evidence to infer a payload offset.
                if (alignment == null && clipNameList != null &&
                    baseIndex >= 0 && baseIndex < clipNameList.Count &&
                    !string.IsNullOrWhiteSpace(clipNameList[baseIndex]))
                {
                    entry.GameAnimationName = clipNameList[baseIndex];
                    entry.GameAnimationNameIndex = baseIndex;
                    result.NamesAssigned++;
                }
            }

            return result;
        }

        private static bool TryGetDirectRecordName(
            int index,
            int? frameCount,
            Dictionary<int, ClipRecord> records,
            out string name,
            out int resolvedIndex)
        {
            name = null;
            resolvedIndex = -1;
            if (index < 0 || !records.TryGetValue(index, out ClipRecord record))
                return false;
            if (frameCount.HasValue && record.FrameCount != frameCount.Value)
                return false;

            name = record.Name;
            resolvedIndex = record.Index;
            return !string.IsNullOrWhiteSpace(name);
        }

        private static Alignment FindBestAlignment(
            IList<CHPR.AnimationArrayEntry> entries,
            Dictionary<int, ClipRecord> records,
            List<string> clipNames,
            bool useOutputSlot)
        {
            if (records.Count == 0)
                return null;

            var votes = new Dictionary<int, int>();
            foreach (CHPR.AnimationArrayEntry entry in entries)
            {
                if (!TryGetIdentifier(entry, useOutputSlot, out int identifier))
                    continue;
                if (!TryGetKnownFrameCount(entry, out int frameCount))
                    continue;

                foreach (ClipRecord record in records.Values)
                {
                    if (record.FrameCount != frameCount)
                        continue;
                    int delta = identifier - record.Index;
                    votes.TryGetValue(delta, out int count);
                    votes[delta] = count + 1;
                }
            }

            if (votes.Count == 0)
                return null;

            Alignment best = null;
            foreach (KeyValuePair<int, int> vote in votes)
            {
                var candidate = new Alignment
                {
                    UsesOutputSlot = useOutputSlot,
                    Delta = vote.Key,
                    Votes = vote.Value
                };

                foreach (CHPR.AnimationArrayEntry entry in entries)
                {
                    if (!TryGetIdentifier(entry, useOutputSlot, out int identifier))
                        continue;
                    long candidateLong = (long)identifier - candidate.Delta;
                    if (candidateLong < 0 || candidateLong > int.MaxValue)
                        continue;
                    int clipIndex = (int)candidateLong;
                    if (clipNames != null && clipIndex >= clipNames.Count)
                        continue;

                    candidate.InRange++;
                    if (!TryGetKnownFrameCount(entry, out int frameCount))
                        continue;

                    if (records.TryGetValue(clipIndex, out ClipRecord record))
                    {
                        bool frameMatches = record.FrameCount == frameCount;
                        bool nameMatches = clipNames == null ||
                            (clipIndex < clipNames.Count &&
                             string.Equals(clipNames[clipIndex], record.Name, StringComparison.Ordinal));
                        if (frameMatches && nameMatches)
                            candidate.Corroborated++;
                        else
                            candidate.Conflicts++;
                    }
                }

                if (IsBetterAlignment(candidate, best))
                    best = candidate;
            }

            // An offset that cannot be corroborated against an actual clip record is
            // too speculative to use for naming unrecorded payloads.
            return best != null && best.Corroborated > 0 ? best : null;
        }

        private static bool IsBetterAlignment(Alignment candidate, Alignment current)
        {
            if (current == null) return true;
            if (candidate.Corroborated != current.Corroborated)
                return candidate.Corroborated > current.Corroborated;
            if (candidate.Conflicts != current.Conflicts)
                return candidate.Conflicts < current.Conflicts;
            if (candidate.Votes != current.Votes)
                return candidate.Votes > current.Votes;
            if (candidate.InRange != current.InRange)
                return candidate.InRange > current.InRange;
            if (Math.Abs((long)candidate.Delta) != Math.Abs((long)current.Delta))
                return Math.Abs((long)candidate.Delta) < Math.Abs((long)current.Delta);
            // Prefer the base animation index when all available evidence ties.
            return !candidate.UsesOutputSlot && current.UsesOutputSlot;
        }

        private static Alignment ChooseAlignment(Alignment outputSlot, Alignment baseIndex)
        {
            if (outputSlot == null) return baseIndex;
            if (baseIndex == null) return outputSlot;
            if (outputSlot.Corroborated != baseIndex.Corroborated)
                return outputSlot.Corroborated > baseIndex.Corroborated ? outputSlot : baseIndex;
            if (outputSlot.Conflicts != baseIndex.Conflicts)
                return outputSlot.Conflicts < baseIndex.Conflicts ? outputSlot : baseIndex;
            if (outputSlot.Votes != baseIndex.Votes)
                return outputSlot.Votes > baseIndex.Votes ? outputSlot : baseIndex;
            if (outputSlot.InRange != baseIndex.InRange)
                return outputSlot.InRange > baseIndex.InRange ? outputSlot : baseIndex;
            if (outputSlot.Delta == 0 && baseIndex.Delta != 0)
                return outputSlot;
            if (baseIndex.Delta == 0 && outputSlot.Delta != 0)
                return baseIndex;
            return baseIndex;
        }

        private static bool TryGetIdentifier(
            CHPR.AnimationArrayEntry entry,
            bool useOutputSlot,
            out int identifier)
        {
            identifier = -1;
            if (entry == null)
                return false;
            if (useOutputSlot)
            {
                if (entry.OutputSlot > int.MaxValue)
                    return false;
                identifier = (int)entry.OutputSlot;
                return true;
            }

            if (entry.Animation?.BaseInfo == null)
                return false;
            identifier = entry.Animation.BaseInfo.Index;
            return true;
        }

        private static int? GetKnownFrameCount(CHPR.AnimationArrayEntry entry)
        {
            return TryGetKnownFrameCount(entry, out int frameCount) ? frameCount : (int?)null;
        }

        private static bool TryGetKnownFrameCount(CHPR.AnimationArrayEntry entry, out int frameCount)
        {
            if (entry?.CompStreamClip != null)
            {
                frameCount = entry.CompStreamClip.FrameCount;
                return true;
            }

            frameCount = 0;
            return false;
        }

        private static List<string> FindClipNameList(
            byte[] data,
            int stringTableEnd,
            List<string> strings,
            Dictionary<int, ClipRecord> clipRecords)
        {
            var hashToIndex = new Dictionary<uint, int>();
            for (int i = 0; i < strings.Count; i++)
            {
                uint hash = Fnv1a(strings[i] ?? string.Empty);
                if (!hashToIndex.ContainsKey(hash))
                    hashToIndex.Add(hash, i);
            }

            List<string> bestNames = null;
            int bestRecordMatches = -1;
            int end = data.Length - 8;
            for (int offset = Math.Max(0, stringTableEnd); offset < end;)
            {
                if (!hashToIndex.ContainsKey(ReadUInt32(data, offset)))
                {
                    offset++;
                    continue;
                }

                int count = 0;
                while (offset + 4L * count < end &&
                    hashToIndex.ContainsKey(ReadUInt32(data, offset + 4 * count)))
                {
                    count++;
                }

                if (count >= 3 && (long)offset + 8L * count <= data.Length)
                {
                    var names = new List<string>(count);
                    bool valid = true;
                    for (int i = 0; i < count; i++)
                    {
                        uint stringIndex = ReadUInt32(data, offset + 4 * count + 4 * i);
                        if (stringIndex >= strings.Count)
                        {
                            valid = false;
                            break;
                        }
                        names.Add(strings[(int)stringIndex]);
                    }

                    // Lists A and B begin with root.anchorBlend; list D is the
                    // remaining clip-name list. Among non-skeleton lists, D is the
                    // largest in the known CHPR layout.
                    if (valid &&
                        !string.Equals(names[0], "root.anchorBlend", StringComparison.Ordinal))
                    {
                        int recordMatches = 0;
                        foreach (ClipRecord record in clipRecords.Values)
                        {
                            if (record.Index < names.Count &&
                                string.Equals(names[record.Index], record.Name, StringComparison.Ordinal))
                            {
                                recordMatches++;
                            }
                        }

                        if (recordMatches > bestRecordMatches ||
                            (recordMatches == bestRecordMatches &&
                             names.Count > (bestNames?.Count ?? 0)))
                        {
                            bestNames = names;
                            bestRecordMatches = recordMatches;
                        }
                    }

                    offset += 8 * count;
                }
                else
                {
                    offset += 4 * Math.Max(count, 1);
                }
            }

            return bestNames;
        }

        private static Dictionary<int, ClipRecord> FindClipRecords(
            byte[] data,
            List<string> strings,
            List<string> clipNames)
        {
            var records = new Dictionary<int, ClipRecord>();
            byte[] signature = { 0, 0, 0, 1, 1 };

            for (int i = IndexOf(data, signature, 4); i >= 0; i = IndexOf(data, signature, i + 1))
            {
                int offset = i - 4;
                if (offset < 0 || offset + 0x23 > data.Length ||
                    data[offset] != 0 || data[offset + 1] != 0)
                    continue;

                if ((data[offset + 13] & data[offset + 14] &
                     data[offset + 15] & data[offset + 16]) != 0xFF)
                    continue;

                uint nameIndex = ReadUInt32(data, offset + 9);
                if (nameIndex >= strings.Count)
                    continue;

                int clipIndex = ReadUInt16(data, offset + 2);
                if (clipNames != null)
                {
                    if (clipIndex >= clipNames.Count ||
                        !string.Equals(clipNames[clipIndex], strings[(int)nameIndex], StringComparison.Ordinal))
                        continue;
                }

                if (!records.ContainsKey(clipIndex))
                {
                    records.Add(clipIndex, new ClipRecord
                    {
                        Index = clipIndex,
                        Name = strings[(int)nameIndex],
                        FrameCount = ReadUInt16(data, offset + 0x21)
                    });
                }
            }

            return records;
        }

        private static int IndexOf(byte[] data, byte[] needle, int start)
        {
            for (int i = Math.Max(0, start); i <= data.Length - needle.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (data[i + j] == needle[j])
                        continue;
                    found = false;
                    break;
                }
                if (found)
                    return i;
            }

            return -1;
        }

        private static uint Fnv1a(string value)
        {
            uint hash = 0x811C9DC5;
            foreach (byte valueByte in Encoding.Latin1.GetBytes(value))
                hash = unchecked((hash ^ valueByte) * 0x01000193);
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
