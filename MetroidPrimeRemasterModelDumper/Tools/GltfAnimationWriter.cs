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

    public static WriteReport WriteAnimations(
        string gltfPath,
        IReadOnlyList<IOAnimation> sourceAnimations,
        string? sharedAnimationBufferPath = null)
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

            // When exporting multiple models from one CHPR, the first model creates a canonical
            // animation bundle. Subsequent glTFs reuse its exact accessors and binary values,
            // remapping only node indices by joint name. This avoids per-model binary fallbacks.
            if (!string.IsNullOrWhiteSpace(sharedAnimationBufferPath))
            {
                WriteReport? cachedReport = TryWriteCachedBundle(
                    gltfPath, root, nodes, allNodesByName, jointNodesByName,
                    views, accessors, buffers, sharedAnimationBufferPath);
                if (cachedReport is not null)
                    return cachedReport;
            }

            int baseViewIndex = views.Count;
            int baseAccessorIndex = accessors.Count;
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
            byte[] animationBytes = binary.ToArray();
            string localSidecarName = Path.GetFileNameWithoutExtension(gltfPath) + ".animations.bin";
            string localSidecarPath = Path.Combine(directory, localSidecarName);
            string sidecarPath;
            string? sharingWarning = null;

            if (string.IsNullOrWhiteSpace(sharedAnimationBufferPath))
            {
                sidecarPath = localSidecarPath;
                File.WriteAllBytes(sidecarPath, animationBytes);
            }
            else
            {
                sidecarPath = Path.GetFullPath(sharedAnimationBufferPath);
                string? sharedDirectory = Path.GetDirectoryName(sidecarPath);
                if (!string.IsNullOrEmpty(sharedDirectory))
                    Directory.CreateDirectory(sharedDirectory);

                // The first glTF reaching this point becomes the canonical animation bundle.
                // The cache manifest preserves its animation/accessor layout so later models can
                // reuse the same binary regardless of their own mesh-buffer/accessor counts.
                File.WriteAllBytes(sidecarPath, animationBytes);
                JsonObject manifest = BuildSharedManifest(
                    animationBytes.Length, views, accessors, gltfAnimations, nodes,
                    baseViewIndex, baseAccessorIndex, channelsWritten, groupsWritten);
                string manifestPath = GetSharedManifestPath(sidecarPath);
                File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }

            // Remove stale per-model sidecars left by earlier versions of the exporter once
            // this glTF successfully points at the shared CHPR buffer.
            if (!string.IsNullOrWhiteSpace(sharedAnimationBufferPath) &&
                !string.Equals(
                    Path.GetFullPath(sidecarPath),
                    Path.GetFullPath(localSidecarPath),
                    StringComparison.Ordinal) &&
                File.Exists(localSidecarPath))
            {
                try
                {
                    File.Delete(localSidecarPath);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    sharingWarning = string.IsNullOrEmpty(sharingWarning)
                        ? $"Could not remove stale per-model sidecar '{localSidecarPath}': {ex.Message}"
                        : sharingWarning + $" Could not remove stale sidecar '{localSidecarPath}': {ex.Message}";
                }
            }

            string relativeBufferUri = Path.GetRelativePath(directory, sidecarPath).Replace('\\', '/');
            string bufferUri = EncodeRelativeUriPath(relativeBufferUri);

            buffers.Add(new JsonObject
            {
                ["uri"] = bufferUri,
                ["byteLength"] = animationBytes.Length
            });

            // Replace any animations emitted by the bundled exporter. These channels use valid
            // glTF VEC3/VEC4 accessors rather than relying on a vendor-specific scalar-track merge.
            root["animations"] = gltfAnimations;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(gltfPath, root.ToJsonString(options));

            return new WriteReport(gltfAnimations.Count, channelsWritten, groupsWritten, unmatchedGroups, sharingWarning);
        }
        catch (Exception ex)
        {
            return new WriteReport(0, 0, 0, 0, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string GetSharedManifestPath(string sharedBufferPath) =>
        Path.ChangeExtension(Path.GetFullPath(sharedBufferPath), ".json");

    private static JsonObject BuildSharedManifest(
        int byteLength,
        JsonArray views,
        JsonArray accessors,
        JsonArray animations,
        JsonArray nodes,
        int baseViewIndex,
        int baseAccessorIndex,
        int channelsWritten,
        int groupsWritten)
    {
        var cachedViews = new JsonArray();
        for (int i = baseViewIndex; i < views.Count; i++)
        {
            if (views[i] is not JsonObject sourceView)
                continue;

            JsonObject view = (JsonObject)sourceView.DeepClone();
            view.Remove("buffer");
            cachedViews.Add(view);
        }

        var cachedAccessors = new JsonArray();
        for (int i = baseAccessorIndex; i < accessors.Count; i++)
        {
            if (accessors[i] is not JsonObject sourceAccessor)
                continue;

            JsonObject accessor = (JsonObject)sourceAccessor.DeepClone();
            if (accessor["bufferView"] is JsonNode bufferViewNode)
            {
                int absoluteView = GetInt(bufferViewNode, -1);
                if (absoluteView >= 0)
                    accessor["bufferView"] = absoluteView - baseViewIndex;
            }
            cachedAccessors.Add(accessor);
        }

        var cachedAnimations = new JsonArray();
        foreach (JsonNode? sourceAnimation in animations)
        {
            if (sourceAnimation is not JsonObject sourceClip)
                continue;

            JsonObject clip = (JsonObject)sourceClip.DeepClone();
            if (clip["samplers"] is JsonArray samplers)
            {
                foreach (JsonNode? samplerNode in samplers)
                {
                    if (samplerNode is not JsonObject sampler)
                        continue;

                    if (sampler["input"] is JsonNode input)
                        sampler["input"] = GetInt(input, baseAccessorIndex) - baseAccessorIndex;
                    if (sampler["output"] is JsonNode output)
                        sampler["output"] = GetInt(output, baseAccessorIndex) - baseAccessorIndex;
                }
            }

            if (clip["channels"] is JsonArray channels)
            {
                foreach (JsonNode? channelNode in channels)
                {
                    if (channelNode is not JsonObject channel ||
                        channel["target"] is not JsonObject target)
                        continue;

                    int nodeIndex = GetInt(target["node"], -1);
                    if (nodeIndex < 0 || nodeIndex >= nodes.Count || nodes[nodeIndex] is not JsonObject node)
                        continue;

                    string? nodeName = GetString(node["name"]);
                    if (string.IsNullOrWhiteSpace(nodeName))
                        continue;

                    target.Remove("node");
                    target["nodeName"] = nodeName;
                }
            }

            cachedAnimations.Add(clip);
        }

        return new JsonObject
        {
            ["version"] = 1,
            ["byteLength"] = byteLength,
            ["channelsWritten"] = channelsWritten,
            ["groupsWritten"] = groupsWritten,
            ["bufferViews"] = cachedViews,
            ["accessors"] = cachedAccessors,
            ["animations"] = cachedAnimations
        };
    }

    /// <summary>
    /// Applies the canonical per-CHPR animation bundle to another model glTF. Accessor/view
    /// indices are made local to that glTF, while animation target nodes are resolved by name.
    /// Returns null if no usable cache exists, allowing the first model to create it.
    /// </summary>
    private static WriteReport? TryWriteCachedBundle(
        string gltfPath,
        JsonObject root,
        JsonArray nodes,
        IReadOnlyDictionary<string, int> allNodesByName,
        IReadOnlyDictionary<string, int> jointNodesByName,
        JsonArray views,
        JsonArray accessors,
        JsonArray buffers,
        string sharedAnimationBufferPath)
    {
        string sharedPath = Path.GetFullPath(sharedAnimationBufferPath);
        string manifestPath = GetSharedManifestPath(sharedPath);
        if (!File.Exists(sharedPath) || !File.Exists(manifestPath))
            return null;

        try
        {
            JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject
                ?? throw new InvalidDataException("Shared animation manifest is not a JSON object.");
            if (GetInt(manifest["version"], 0) != 1)
                return null;

            int byteLength = GetInt(manifest["byteLength"], -1);
            var fileInfo = new FileInfo(sharedPath);
            if (byteLength <= 0 || !fileInfo.Exists || fileInfo.Length != byteLength)
                return null;

            JsonArray? cachedViews = manifest["bufferViews"] as JsonArray;
            JsonArray? cachedAccessors = manifest["accessors"] as JsonArray;
            JsonArray? cachedAnimations = manifest["animations"] as JsonArray;
            if (cachedViews is null || cachedAccessors is null || cachedAnimations is null)
                return null;

            // Resolve every cached channel first, so a model with a different skeleton falls
            // back to fresh serialization rather than producing an empty/malformed animation.
            var resolvedClips = new List<(JsonObject Clip, int ChannelCount)>();
            int unmatched = 0;
            foreach (JsonNode? sourceClipNode in cachedAnimations)
            {
                if (sourceClipNode is not JsonObject sourceClip ||
                    sourceClip["samplers"] is not JsonArray sourceSamplers ||
                    sourceClip["channels"] is not JsonArray sourceChannels)
                    continue;

                var clip = new JsonObject
                {
                    ["name"] = GetString(sourceClip["name"]) ?? "Animation",
                    ["samplers"] = new JsonArray(),
                    ["channels"] = new JsonArray()
                };

                JsonArray targetSamplers = (JsonArray)clip["samplers"]!;
                foreach (JsonNode? samplerNode in sourceSamplers)
                {
                    if (samplerNode is not JsonObject sourceSampler)
                        continue;

                    JsonObject sampler = (JsonObject)sourceSampler.DeepClone();
                    if (sampler["input"] is JsonNode input)
                        sampler["input"] = GetInt(input, 0) + accessors.Count + cachedAccessors.Count;
                    if (sampler["output"] is JsonNode output)
                        sampler["output"] = GetInt(output, 0) + accessors.Count + cachedAccessors.Count;
                    targetSamplers.Add(sampler);
                }

                JsonArray targetChannels = (JsonArray)clip["channels"]!;
                foreach (JsonNode? channelNode in sourceChannels)
                {
                    if (channelNode is not JsonObject sourceChannel ||
                        sourceChannel["target"] is not JsonObject sourceTarget)
                        continue;

                    string? nodeName = GetString(sourceTarget["nodeName"]);
                    if (string.IsNullOrWhiteSpace(nodeName))
                    {
                        unmatched++;
                        continue;
                    }

                    int nodeIndex;
                    if (jointNodesByName.TryGetValue(nodeName, out int jointNode))
                        nodeIndex = jointNode;
                    else if (!allNodesByName.TryGetValue(nodeName, out nodeIndex))
                    {
                        unmatched++;
                        continue;
                    }

                    JsonObject channel = (JsonObject)sourceChannel.DeepClone();
                    JsonObject target = (JsonObject)channel["target"]!;
                    target.Remove("nodeName");
                    target["node"] = nodeIndex;
                    target["path"] = GetString(sourceTarget["path"]) ?? "translation";
                    targetChannels.Add(channel);
                }

                if (targetChannels.Count > 0)
                    resolvedClips.Add((clip, targetChannels.Count));
            }

            int channelsWritten = resolvedClips.Sum(clip => clip.ChannelCount);
            if (channelsWritten == 0)
                return new WriteReport(0, 0, 0, unmatched,
                    $"Shared animation bundle '{manifestPath}' had no channels matching this model's nodes.");

            int baseViewIndex = views.Count;
            int baseAccessorIndex = accessors.Count;
            int outputBufferIndex = buffers.Count;

            foreach (JsonNode? sourceViewNode in cachedViews)
            {
                if (sourceViewNode is not JsonObject sourceView)
                    continue;

                JsonObject view = (JsonObject)sourceView.DeepClone();
                view["buffer"] = outputBufferIndex;
                views.Add(view);
            }

            foreach (JsonNode? sourceAccessorNode in cachedAccessors)
            {
                if (sourceAccessorNode is not JsonObject sourceAccessor)
                    continue;

                JsonObject accessor = (JsonObject)sourceAccessor.DeepClone();
                int relativeView = GetInt(accessor["bufferView"], -1);
                if (relativeView >= 0)
                    accessor["bufferView"] = baseViewIndex + relativeView;
                accessors.Add(accessor);
            }

            var animations = new JsonArray();
            foreach (var resolved in resolvedClips)
            {
                JsonObject clip = resolved.Clip;
                // Cached samplers reference accessors relative to the canonical bundle.
                JsonArray samplers = (JsonArray)clip["samplers"]!;
                foreach (JsonNode? samplerNode in samplers)
                {
                    if (samplerNode is not JsonObject sampler)
                        continue;

                    if (sampler["input"] is JsonNode input)
                        sampler["input"] = baseAccessorIndex + GetInt(input, baseAccessorIndex + cachedAccessors.Count);
                    if (sampler["output"] is JsonNode output)
                        sampler["output"] = baseAccessorIndex + GetInt(output, baseAccessorIndex + cachedAccessors.Count);
                }
                animations.Add(clip);
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(gltfPath)) ?? Directory.GetCurrentDirectory();
            string localSidecarPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(gltfPath) + ".animations.bin");
            string relativeBufferUri = Path.GetRelativePath(directory, sharedPath).Replace('\\', '/');
            string bufferUri = EncodeRelativeUriPath(relativeBufferUri);
            buffers.Add(new JsonObject
            {
                ["uri"] = bufferUri,
                ["byteLength"] = byteLength
            });
            root["animations"] = animations;

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(gltfPath, root.ToJsonString(options));

            if (!string.Equals(sharedPath, Path.GetFullPath(localSidecarPath), StringComparison.Ordinal) &&
                File.Exists(localSidecarPath))
            {
                try { File.Delete(localSidecarPath); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            }

            return new WriteReport(
                animations.Count,
                channelsWritten,
                GetInt(manifest["groupsWritten"], 0),
                unmatched,
                null);
        }
        catch
        {
            // A bad/stale cache must not block exports; the current model can recreate it.
            return null;
        }
    }

    private static string EncodeRelativeUriPath(string relativePath)
    {
        // Encode each path segment separately so directory separators and ../ are preserved.
        return string.Join(
            "/",
            relativePath.Replace('\\', '/').Split('/')
                .Select(segment => segment == "." || segment == ".."
                    ? segment
                    : Uri.EscapeDataString(segment)));
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
