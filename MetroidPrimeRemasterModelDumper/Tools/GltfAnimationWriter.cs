using IONET.Core.Animation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MetroidPrimeRemasterModelDumper.Tools;

/// <summary>
/// Writes animation data into a glTF 2.0 file using spec-compliant vector accessors.
/// IONET represents TRS values as separate scalar tracks; glTF requires one VEC3/VEC4
/// output accessor for each animated node property.
/// </summary>
public static class GltfAnimationWriter
{
    public sealed record WriteReport(
        int ClipsWritten,
        int ChannelsWritten,
        int GroupsWritten,
        int UnmatchedGroups,
        string? Warning);

    private sealed record TrackKey(float Time, float Value);
    private sealed record Trs(Vector3 Translation, Quaternion Rotation, Vector3 Scale);

    public static WriteReport WriteAnimations(string gltfPath, IReadOnlyList<IOAnimation> sourceAnimations)
    {
        if (string.IsNullOrWhiteSpace(gltfPath))
            return new WriteReport(0, 0, 0, 0, "The glTF output path is empty.");
        if (sourceAnimations is null || sourceAnimations.Count == 0)
            return new WriteReport(0, 0, 0, 0, "No IONET animations were supplied.");
        if (!File.Exists(gltfPath))
            return new WriteReport(0, 0, 0, 0, $"glTF file was not found after the mesh export: {gltfPath}");

        try
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(gltfPath)) as JsonObject
                ?? throw new InvalidDataException("The exported glTF JSON root is not an object.");
            if (root["asset"]?["version"]?.GetValue<string>() is string version &&
                !version.StartsWith("2.", StringComparison.Ordinal))
                return new WriteReport(0, 0, 0, 0, $"Expected glTF 2.x, found version '{version}'.");

            JsonArray nodes = root["nodes"] as JsonArray
                ?? throw new InvalidDataException("The exported glTF has no nodes array.");
            var allNodesByName = new Dictionary<string, int>(StringComparer.Ordinal);
            var jointNodesByName = new Dictionary<string, int>(StringComparer.Ordinal);
            HashSet<int> skinJointIndices = ReadSkinJointIndices(root);

            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] is not JsonObject node)
                    continue;

                string? name = GetString(node["name"]);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                allNodesByName.TryAdd(name, i);
                if (skinJointIndices.Contains(i))
                    jointNodesByName.TryAdd(name, i);
            }

            JsonArray views = GetOrCreateArray(root, "bufferViews");
            JsonArray accessors = GetOrCreateArray(root, "accessors");
            JsonArray buffers = GetOrCreateArray(root, "buffers");
            int outputBufferIndex = buffers.Count;
            var binary = new List<byte>(1024 * 1024);
            var gltfAnimations = new JsonArray();
            int channelsWritten = 0;
            int groupsWritten = 0;
            int unmatchedGroups = 0;

            foreach (IOAnimation clip in sourceAnimations)
            {
                if (clip is null)
                    continue;

                var samplers = new JsonArray();
                var channels = new JsonArray();

                foreach (IOAnimation group in clip.Groups)
                {
                    if (group is null || string.IsNullOrWhiteSpace(group.Name))
                        continue;

                    int nodeIndex;
                    if (jointNodesByName.TryGetValue(group.Name, out int jointNode))
                        nodeIndex = jointNode;
                    else if (!allNodesByName.TryGetValue(group.Name, out nodeIndex))
                    {
                        unmatchedGroups++;
                        continue;
                    }

                    if (nodeIndex < 0 || nodeIndex >= nodes.Count || nodes[nodeIndex] is not JsonObject targetNode)
                        continue;

                    Trs defaults = EnsureTrs(targetNode);
                    // glTF forbids targeting TRS properties while the node still uses a
                    // matrix, so skip a node if its matrix could not be decomposed.
                    if (targetNode.ContainsKey("matrix"))
                    {
                        unmatchedGroups++;
                        continue;
                    }
                    int before = channels.Count;

                    AddVectorChannel(group, targetNode, nodeIndex, "rotation",
                        new[]
                        {
                            IOAnimationTrackType.QuatX, IOAnimationTrackType.QuatY,
                            IOAnimationTrackType.QuatZ, IOAnimationTrackType.QuatW
                        },
                        new[] { defaults.Rotation.X, defaults.Rotation.Y, defaults.Rotation.Z, defaults.Rotation.W },
                        4, clip.Name, samplers, channels, binary, views, accessors, outputBufferIndex);

                    AddVectorChannel(group, targetNode, nodeIndex, "translation",
                        new[]
                        {
                            IOAnimationTrackType.PositionX, IOAnimationTrackType.PositionY,
                            IOAnimationTrackType.PositionZ
                        },
                        new[] { defaults.Translation.X, defaults.Translation.Y, defaults.Translation.Z },
                        3, clip.Name, samplers, channels, binary, views, accessors, outputBufferIndex);

                    AddVectorChannel(group, targetNode, nodeIndex, "scale",
                        new[]
                        {
                            IOAnimationTrackType.ScaleX, IOAnimationTrackType.ScaleY,
                            IOAnimationTrackType.ScaleZ
                        },
                        new[] { defaults.Scale.X, defaults.Scale.Y, defaults.Scale.Z },
                        3, clip.Name, samplers, channels, binary, views, accessors, outputBufferIndex);

                    if (channels.Count > before)
                        groupsWritten++;
                }

                if (channels.Count > 0)
                {
                    gltfAnimations.Add(new JsonObject
                    {
                        ["name"] = string.IsNullOrWhiteSpace(clip.Name) ? "Animation" : clip.Name,
                        ["samplers"] = samplers,
                        ["channels"] = channels
                    });
                    channelsWritten += channels.Count;
                }
            }

            if (gltfAnimations.Count == 0 || binary.Count == 0)
            {
                return new WriteReport(0, 0, groupsWritten, unmatchedGroups,
                    $"No animatable bone groups matched glTF nodes. Scene nodes: {nodes.Count}; " +
                    $"source clips: {sourceAnimations.Count}; unmatched groups: {unmatchedGroups}.");
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(gltfPath)) ?? Directory.GetCurrentDirectory();
            string sidecarName = Path.GetFileNameWithoutExtension(gltfPath) + ".animations.bin";
            string sidecarPath = Path.Combine(directory, sidecarName);
            File.WriteAllBytes(sidecarPath, binary.ToArray());

            buffers.Add(new JsonObject
            {
                ["uri"] = Uri.EscapeDataString(sidecarName),
                ["byteLength"] = binary.Count
            });

            // Replace any animations emitted by the bundled exporter. These channels use valid
            // glTF VEC3/VEC4 accessors rather than relying on a vendor-specific scalar-track merge.
            root["animations"] = gltfAnimations;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(gltfPath, root.ToJsonString(options));

            return new WriteReport(gltfAnimations.Count, channelsWritten, groupsWritten, unmatchedGroups, null);
        }
        catch (Exception ex)
        {
            return new WriteReport(0, 0, 0, 0, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AddVectorChannel(
        IOAnimation group,
        JsonObject targetNode,
        int nodeIndex,
        string path,
        IOAnimationTrackType[] componentTypes,
        float[] defaultValues,
        int componentCount,
        string clipName,
        JsonArray samplers,
        JsonArray channels,
        List<byte> binary,
        JsonArray views,
        JsonArray accessors,
        int bufferIndex)
    {
        var componentTracks = new IOAnimationTrack?[componentCount];
        for (int i = 0; i < componentTypes.Length; i++)
            componentTracks[i] = group.Tracks.FirstOrDefault(t => t.ChannelType == componentTypes[i] && t.KeyFrames.Count > 0);

        if (componentTracks.All(track => track is null))
            return;

        // glTF rotation output is a single VEC4 accessor; translation and scale are VEC3.
        // For a partly animated vector, use the node's bind value for components with no track.
        if (path == "rotation" && componentTracks.Any(track => track is null))
            return;

        var keyLists = componentTracks
            .Where(track => track is not null)
            .Select(track => NormalizeKeys(track!))
            .Where(keys => keys.Count > 0)
            .ToList();
        if (keyLists.Count == 0)
            return;

        var times = keyLists.SelectMany(keys => keys.Select(key => key.Time))
            .Where(float.IsFinite)
            .Distinct()
            .OrderBy(time => time)
            .ToList();
        if (times.Count == 0)
            return;
        if (times.Count == 1)
        {
            // glTF input accessors for animation must have a valid time range; a single key is
            // legal in practice, but retaining the current channel is useful for still clips.
        }

        var outputValues = new List<float>(times.Count * componentCount);
        foreach (float time in times)
        {
            for (int component = 0; component < componentCount; component++)
            {
                List<TrackKey>? keys = componentTracks[component] is { } track
                    ? NormalizeKeys(track)
                    : null;
                float value = keys is { Count: > 0 }
                    ? Evaluate(keys, time)
                    : defaultValues[component];

                if (!float.IsFinite(value))
                    value = defaultValues[component];
                outputValues.Add(value);
            }

            if (path == "rotation")
            {
                int offset = outputValues.Count - 4;
                var q = new Quaternion(
                    outputValues[offset],
                    outputValues[offset + 1],
                    outputValues[offset + 2],
                    outputValues[offset + 3]);
                float length = q.LengthSquared();
                if (float.IsFinite(length) && length > 1e-12f)
                {
                    q = Quaternion.Normalize(q);
                    outputValues[offset] = q.X;
                    outputValues[offset + 1] = q.Y;
                    outputValues[offset + 2] = q.Z;
                    outputValues[offset + 3] = q.W;
                }
                else
                {
                    outputValues[offset] = defaultValues[0];
                    outputValues[offset + 1] = defaultValues[1];
                    outputValues[offset + 2] = defaultValues[2];
                    outputValues[offset + 3] = defaultValues[3];
                }
            }
        }

        int inputAccessor = AddAccessor(
            times, "SCALAR", times.Count, binary, views, accessors, bufferIndex, includeTimeBounds: true);
        int outputAccessor = AddAccessor(
            outputValues, componentCount == 4 ? "VEC4" : "VEC3",
            times.Count, binary, views, accessors, bufferIndex, includeTimeBounds: false);

        int samplerIndex = samplers.Count;
        samplers.Add(new JsonObject
        {
            ["input"] = inputAccessor,
            ["output"] = outputAccessor,
            ["interpolation"] = "LINEAR"
        });
        channels.Add(new JsonObject
        {
            ["sampler"] = samplerIndex,
            ["target"] = new JsonObject
            {
                ["node"] = nodeIndex,
                ["path"] = path
            }
        });
    }

    private static List<TrackKey> NormalizeKeys(IOAnimationTrack track)
    {
        var values = new List<TrackKey>();
        foreach (IOKeyFrame key in track.KeyFrames)
        {
            float time = key.Time;
            float value = Convert.ToSingle(key.Value, CultureInfo.InvariantCulture);
            if (!float.IsFinite(time) || !float.IsFinite(value))
                continue;

            values.Add(new TrackKey(time, value));
        }

        values.Sort((a, b) => a.Time.CompareTo(b.Time));
        if (values.Count <= 1)
            return values;

        var unique = new List<TrackKey>(values.Count);
        foreach (TrackKey key in values)
        {
            if (unique.Count > 0 && Math.Abs(unique[^1].Time - key.Time) < 1e-7f)
                unique[^1] = key;
            else
                unique.Add(key);
        }
        return unique;
    }

    private static float Evaluate(IReadOnlyList<TrackKey> keys, float time)
    {
        if (time <= keys[0].Time)
            return keys[0].Value;
        if (time >= keys[^1].Time)
            return keys[^1].Value;

        int low = 0;
        int high = keys.Count - 1;
        while (high - low > 1)
        {
            int mid = low + ((high - low) / 2);
            if (keys[mid].Time <= time)
                low = mid;
            else
                high = mid;
        }

        TrackKey a = keys[low];
        TrackKey b = keys[high];
        float duration = b.Time - a.Time;
        if (duration <= 1e-8f)
            return b.Value;

        float t = (time - a.Time) / duration;
        return a.Value + (b.Value - a.Value) * t;
    }

    private static int AddAccessor(
        IReadOnlyList<float> values,
        string type,
        int count,
        List<byte> binary,
        JsonArray views,
        JsonArray accessors,
        int bufferIndex,
        bool includeTimeBounds)
    {
        while ((binary.Count & 3) != 0)
            binary.Add(0);

        int byteOffset = binary.Count;
        foreach (float value in values)
        {
            uint bits = BitConverter.SingleToUInt32Bits(value);
            binary.Add((byte)(bits & 0xFF));
            binary.Add((byte)((bits >> 8) & 0xFF));
            binary.Add((byte)((bits >> 16) & 0xFF));
            binary.Add((byte)((bits >> 24) & 0xFF));
        }
        int byteLength = binary.Count - byteOffset;

        int viewIndex = views.Count;
        views.Add(new JsonObject
        {
            ["buffer"] = bufferIndex,
            ["byteOffset"] = byteOffset,
            ["byteLength"] = byteLength
        });

        var accessor = new JsonObject
        {
            ["bufferView"] = viewIndex,
            ["byteOffset"] = 0,
            ["componentType"] = 5126,
            ["count"] = count,
            ["type"] = type
        };
        if (includeTimeBounds && values.Count > 0)
        {
            accessor["min"] = new JsonArray(JsonValue.Create(values.Min()));
            accessor["max"] = new JsonArray(JsonValue.Create(values.Max()));
        }

        int accessorIndex = accessors.Count;
        accessors.Add(accessor);
        return accessorIndex;
    }

    private static HashSet<int> ReadSkinJointIndices(JsonObject root)
    {
        var indices = new HashSet<int>();
        if (root["skins"] is not JsonArray skins)
            return indices;

        foreach (JsonNode? skinNode in skins)
        {
            if (skinNode?["joints"] is not JsonArray joints)
                continue;
            foreach (JsonNode? joint in joints)
            {
                int value = GetInt(joint, -1);
                if (value >= 0)
                    indices.Add(value);
            }
        }
        return indices;
    }

    private static Trs EnsureTrs(JsonObject node)
    {
        Vector3 translation = new(0, 0, 0);
        Quaternion rotation = Quaternion.Identity;
        Vector3 scale = Vector3.One;

        if (node["matrix"] is JsonArray matrix && matrix.Count == 16)
        {
            float[] m = matrix.Select(v => GetFloat(v, 0)).ToArray();
            var rowMatrix = new Matrix4x4(
                m[0], m[1], m[2], m[3],
                m[4], m[5], m[6], m[7],
                m[8], m[9], m[10], m[11],
                m[12], m[13], m[14], m[15]);

            if (Matrix4x4.Decompose(rowMatrix, out Vector3 matrixScale,
                    out Quaternion matrixRotation, out Vector3 matrixTranslation))
            {
                translation = matrixTranslation;
                rotation = Normalize(matrixRotation);
                scale = matrixScale;
                node.Remove("matrix");
                node["translation"] = FloatArray(translation.X, translation.Y, translation.Z);
                node["rotation"] = FloatArray(rotation.X, rotation.Y, rotation.Z, rotation.W);
                node["scale"] = FloatArray(scale.X, scale.Y, scale.Z);
                return new Trs(translation, rotation, scale);
            }
        }

        if (node["translation"] is JsonArray t && t.Count >= 3)
            translation = new Vector3(GetFloat(t[0], 0), GetFloat(t[1], 0), GetFloat(t[2], 0));
        if (node["rotation"] is JsonArray r && r.Count >= 4)
            rotation = Normalize(new Quaternion(
                GetFloat(r[0], 0), GetFloat(r[1], 0), GetFloat(r[2], 0), GetFloat(r[3], 1)));
        if (node["scale"] is JsonArray s && s.Count >= 3)
            scale = new Vector3(GetFloat(s[0], 1), GetFloat(s[1], 1), GetFloat(s[2], 1));

        return new Trs(translation, rotation, scale);
    }

    private static Quaternion Normalize(Quaternion value)
    {
        float lengthSquared = value.LengthSquared();
        return float.IsFinite(lengthSquared) && lengthSquared > 1e-12f
            ? Quaternion.Normalize(value)
            : Quaternion.Identity;
    }

    private static JsonArray FloatArray(params float[] values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static JsonArray GetOrCreateArray(JsonObject root, string property)
    {
        if (root[property] is JsonArray existing)
            return existing;

        var created = new JsonArray();
        root[property] = created;
        return created;
    }

    private static string? GetString(JsonNode? node)
    {
        try { return node?.GetValue<string>(); }
        catch { return null; }
    }

    private static int GetInt(JsonNode? node, int fallback)
    {
        try { return node?.GetValue<int>() ?? fallback; }
        catch
        {
            try { return (int)(node?.GetValue<long>() ?? fallback); }
            catch { return fallback; }
        }
    }

    private static float GetFloat(JsonNode? node, float fallback)
    {
        if (node is null)
            return fallback;

        try { return node.GetValue<float>(); }
        catch
        {
            // JsonNode preserves an integer's original numeric type, so GetValue<float>()
            // may fail for perfectly valid glTF values such as 2 or 0. Parse the raw
            // JSON number as a fallback instead of silently replacing it with a default.
            return float.TryParse(
                node.ToJsonString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float parsed)
                ? parsed
                : fallback;
        }
    }
}
