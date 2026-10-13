using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RetroStudioPlugin.Files.FileData;

/// <summary>
/// Discovers named animation clips and their compressed payloads in a decompressed MP4 CHPR.
///
/// The CHPR body is a packed serialization, so this class uses validated signatures rather than
/// assuming that clip metadata and payloads are laid out in a single aligned table. The discovery
/// approach was cross-checked against Infinite00857/Mocktroid (MIT):
/// https://github.com/Infinite00857/Mocktroid
///
/// This class deliberately does not decode keyframes or modify the existing CHPR parser. It is
/// the first stage of the animation pipeline and can be tested independently.
/// </summary>
public static class CHPRAnimationInfo
{
    public sealed record ClipPayload(
        int Id,
        int Offset,
        int Size,
        int FrameCount);

    public sealed record ClipInfo(
        int Index,
        string Name,
        int FrameCount,
        int MetadataOffset,
        ClipPayload? Payload)
    {
        public bool HasPayload => Payload is not null;
    }

    public sealed record Catalog(
        byte[] Data,
        IReadOnlyList<string> Strings,
        IReadOnlyList<ClipInfo> Clips,
        IReadOnlyList<ClipPayload> Payloads,
        IReadOnlyList<float[]> ConstantPools,
        int? PayloadIdDelta,
        bool PayloadMappingIsAmbiguous,
        IReadOnlyList<string> Diagnostics)
    {
        /// <summary>
        /// Creates the compressed decoder for a clip whose metadata-to-payload binding was
        /// validated by this catalog. Returns null when the clip has no safe payload binding.
        /// </summary>
        public CHPRAnimationDecoder? Decode(ClipInfo clip)
        {
            ArgumentNullException.ThrowIfNull(clip);
            if (clip.Payload is null)
                return null;

            var decoder = CHPRAnimationDecoder.Read(Data, clip.Payload.Offset);
            decoder.SetConstantPools(ConstantPools);
            return decoder;
        }
    }

    /// <summary>
    /// Parses clip names / metadata, locates compressed payloads, and maps payload IDs to clip
    /// indices by voting on matching frame counts.
    /// </summary>
    public static Catalog Read(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var diagnostics = new List<string>();
        var strings = ReadStrings(data);
        if (strings.Count == 0)
            diagnostics.Add("The CHPR string table could not be located.");

        var clipMetadata = ReadClipMetadata(data, strings);
        var payloads = LocatePayloads(data);

        if (clipMetadata.Count == 0)
            diagnostics.Add("No named animation clip records were found.");
        if (payloads.Count == 0)
            diagnostics.Add("No validated compressed animation payloads were found.");

        int? delta = FindPayloadIdDelta(clipMetadata, payloads, out bool ambiguous);
        if (delta is null)
            diagnostics.Add("Could not establish a reliable payload-ID offset from matching frame counts.");
        else if (ambiguous)
            diagnostics.Add("Multiple payload-ID offsets received the same number of votes; payload bindings were left unresolved.");

        var payloadById = new Dictionary<int, ClipPayload>();
        foreach (var payload in payloads)
            payloadById[payload.Id] = payload;

        var clips = new List<ClipInfo>(clipMetadata.Count);
        foreach (var clip in clipMetadata)
        {
            ClipPayload? payload = null;
            if (delta.HasValue && !ambiguous && payloadById.TryGetValue(clip.Index + delta.Value, out var candidate))
            {
                // Frame count is an independent consistency check. Do not silently bind an
                // animation to a payload that only happens to have the expected numeric ID.
                if (candidate.FrameCount == clip.FrameCount)
                    payload = candidate;
                else
                    diagnostics.Add($"Clip {clip.Index} ('{clip.Name}') maps to payload {candidate.Id}, but its frame count differs ({clip.FrameCount} vs {candidate.FrameCount}).");
            }

            if (payload is null)
                diagnostics.Add($"Clip {clip.Index} ('{clip.Name}') has metadata but no validated payload binding.");

            clips.Add(new ClipInfo(clip.Index, clip.Name, clip.FrameCount, clip.MetadataOffset, payload));
        }

        var constantPools = CHPRAnimationDecoder.FindConstantPools(data);
        return new Catalog(data, strings, clips, payloads, constantPools, delta, ambiguous, diagnostics);
    }

    /// <summary>
    /// Reads the NUL-separated string table. Supports the common side-blob header and the
    /// content-scanning fallback used by Mocktroid for larger CHPR resources.
    /// </summary>
    public static List<string> ReadStrings(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        int tableHeader = 0x2C;

        while (tableHeader >= 0 && tableHeader <= data.Length - 12)
        {
            uint index = U32(data, tableHeader);
            uint offset = U32(data, tableHeader + 4);
            uint size = U32(data, tableHeader + 8);

            bool plausible =
                offset > 0x30 &&
                offset < data.Length &&
                (ulong)offset + size <= (ulong)data.Length &&
                index < 0x100000;

            if (!plausible)
                break;

            tableHeader += 12;
        }

        if (tableHeader <= data.Length - 4)
        {
            uint size = U32(data, tableHeader);
            if (size > 0 && size <= int.MaxValue)
            {
                List<string> strings = ReadStringTable(data, tableHeader + 4, (int)size);
                if (strings.Count > 0 && strings[0] == "Primary")
                    return strings;
            }
        }

        // The large CHPRs do not always expose the table through the small side-blob header.
        // The string-table length immediately precedes the occurrence of "Primary\0".
        byte[] needle = Encoding.ASCII.GetBytes("Primary\0");
        for (int at = IndexOf(data, needle, 4); at >= 4; at = IndexOf(data, needle, at + 1))
        {
            uint size = U32(data, at - 4);
            if (size > 0 && size <= int.MaxValue)
            {
                List<string> strings = ReadStringTable(data, at, (int)size);
                if (strings.Count > 0)
                    return strings;
            }
        }

        return new List<string>();
    }

    private sealed record ClipMetadata(
        int Index,
        string Name,
        int FrameCount,
        int Offset);

    /// <summary>
    /// Locates records with the pattern used by Mocktroid's ChprInfo parser.
    /// The name is a string-table index; the frame count is stored at record + 0x21.
    /// </summary>
    private static List<ClipMetadata> ReadClipMetadata(byte[] data, IReadOnlyList<string> strings)
    {
        var found = new SortedDictionary<int, ClipMetadata>();
        byte[] signature = { 0, 0, 0, 1, 1 };

        for (int match = IndexOf(data, signature, 4);
             match >= 0;
             match = IndexOf(data, signature, match + 1))
        {
            int offset = match - 4;
            if (offset < 0 || offset > data.Length - 0x23)
                continue;

            if (data[offset] != 0 || data[offset + 1] != 0)
                continue;

            if (data[offset + 13] != 0xFF ||
                data[offset + 14] != 0xFF ||
                data[offset + 15] != 0xFF ||
                data[offset + 16] != 0xFF)
                continue;

            uint nameIndex = U32(data, offset + 9);
            if (nameIndex >= strings.Count)
                continue;

            int index = U16(data, offset + 2);
            int frameCount = U16(data, offset + 0x21);
            found.TryAdd(index, new ClipMetadata(index, strings[(int)nameIndex], frameCount, offset));
        }

        return found.Values.ToList();
    }

    /// <summary>
    /// Locates compressed clip payloads by their preceding 01 01 + u32 ID + FF FF FF FF marker.
    /// All offsets and the declared size are range-checked before a payload is accepted.
    /// </summary>
    public static List<ClipPayload> LocatePayloads(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var found = new List<ClipPayload>();

        for (int marker = 6; marker <= data.Length - 4 - 0x20; marker++)
        {
            if (data[marker] != 0xFF ||
                data[marker + 1] != 0xFF ||
                data[marker + 2] != 0xFF ||
                data[marker + 3] != 0xFF)
                continue;

            if (data[marker - 6] != 0x01 || data[marker - 5] != 0x01)
                continue;

            int id = I32(data, marker - 4);
            int payloadOffset = marker + 4;
            if (payloadOffset < 0 || payloadOffset > data.Length - 0x20)
                continue;

            int size = I32(data, payloadOffset);
            int headerSize = U16(data, payloadOffset + 4);
            int descriptorOffset = U16(data, payloadOffset + 6);
            int bitstreamOffset = I32(data, payloadOffset + 8);

            if (size <= 0 ||
                headerSize != 0x20 ||
                descriptorOffset <= 0x20 ||
                bitstreamOffset <= descriptorOffset ||
                bitstreamOffset >= size ||
                (long)payloadOffset + size > data.Length)
                continue;

            int frameCount = U16(data, payloadOffset + 0x10);
            found.Add(new ClipPayload(id, payloadOffset, size, frameCount));

            // The marker and payload have both been validated; skip their contents so that
            // embedded marker-like bytes are not mistakenly treated as separate payloads.
            marker = payloadOffset + size - 1;
        }

        return found;
    }

    private static int? FindPayloadIdDelta(
        IReadOnlyList<ClipMetadata> clips,
        IReadOnlyList<ClipPayload> payloads,
        out bool ambiguous)
    {
        ambiguous = false;
        if (clips.Count == 0 || payloads.Count == 0)
            return null;

        var votes = new Dictionary<int, int>();
        foreach (var clip in clips)
        {
            foreach (var payload in payloads)
            {
                if (payload.FrameCount != clip.FrameCount)
                    continue;

                int delta = payload.Id - clip.Index;
                votes.TryGetValue(delta, out int current);
                votes[delta] = current + 1;
            }
        }

        if (votes.Count == 0)
            return null;

        int highest = votes.Values.Max();
        var winners = votes.Where(pair => pair.Value == highest)
                           .Select(pair => pair.Key)
                           .OrderBy(value => Math.Abs((long)value))
                           .ThenBy(value => value)
                           .ToList();

        if (winners.Count != 1)
        {
            ambiguous = true;
            return null;
        }

        return winners[0];
    }

    private static List<string> ReadStringTable(byte[] data, int offset, int size)
    {
        if (offset < 0 || size <= 0 || offset > data.Length - size)
            return new List<string>();

        // These tables are length-prefixed including the final NUL byte. Strip that terminator
        // before splitting, as the Java reference implementation does.
        if (data[offset + size - 1] != 0)
            return new List<string>();

        string contents = Encoding.Latin1.GetString(data, offset, size - 1);
        return contents.Split('\0').ToList();
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
