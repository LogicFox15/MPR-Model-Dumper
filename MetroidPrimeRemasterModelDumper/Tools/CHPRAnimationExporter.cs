using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Security.Cryptography;
using DKCTF;
using SharpGLTF.Schema2;

namespace EvilWithin2Tool
{
    /// <summary>
    /// Bakes CHPR CompStream poses onto the named skeleton nodes in an already-exported glTF.
    /// The per-frame bake preserves the game's frame samples instead of attempting to reproduce
    /// CHPR's per-channel interpolation with glTF's different interpolation rules.
    /// </summary>
    public static class CHPRAnimationExporter
    {
        // CompStream frame indices are sampled at the game's nominal 30 Hz animation rate.
        // glTF stores animation key times in seconds; Blender's scene FPS controls timeline-frame placement.
        private const float GameFramesPerSecond = 30.0f;
        // Keep glTF binary buffers below the 2 GiB merge limit while preserving a single rig and animation file.
        private const int MaxGltfBufferSizeBytes = 512 * 1024 * 1024;
        // Raw diagnostics are written beside the glTF, never mixed into the normal export path.
        private const int MaxDiagnosticHexBytes = 96;
        private const int MaxDiagnosticPoseEntries = 128;
        private const int MaxDiagnosticOpcodeLinesPerStream = 5000;

        private sealed class BoneTrack
        {
            public CHPR.BoneData Bone;
            public Node Node;
            public Vector3 BindTranslation;
            public Quaternion BindRotation;
            public Vector3 BindScale;
            public SortedDictionary<float, Vector3> Translation = new SortedDictionary<float, Vector3>();
            public SortedDictionary<float, Quaternion> Rotation = new SortedDictionary<float, Quaternion>();
            public SortedDictionary<float, Vector3> Scale = new SortedDictionary<float, Vector3>();
            public bool HasAnimationData;
        }

        /// <summary>
        /// Adds every successfully parsed CompStream animation in <paramref name="chpr"/> to
        /// an existing glTF file. The model must already contain nodes named like CHPR bones.
        /// Returns false without changing the file if no usable animation channels were found.
        /// </summary>
        public static bool AppendToGltf(string gltfPath, CHPR chpr, out string report)
        {
            report = string.Empty;
            if (string.IsNullOrWhiteSpace(gltfPath))
            {
                report = "No glTF path was provided.";
                return false;
            }
            if (!File.Exists(gltfPath))
            {
                report = $"glTF file was not produced, so animation channels were not appended: {gltfPath}";
                return false;
            }
            if (chpr == null || chpr.CharacterInfos.Count == 0)
            {
                report = "CHPR has no parsed CharacterInfo skeleton.";
                return false;
            }

            string outputDirectory = Path.GetDirectoryName(gltfPath) ?? string.Empty;
            string diagnosticsPath = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(gltfPath) + "_Diagnostics.txt");
            string rawDiagnosticsDirectory = Path.Combine(
                outputDirectory,
                Path.GetFileNameWithoutExtension(gltfPath) + "_RawDiagnostics");
            var diagnostics = new StringBuilder();
            diagnostics.AppendLine("CHPR CompStream Animation Diagnostics");
            diagnostics.AppendLine($"GeneratedUtc: {DateTime.UtcNow:O}");
            diagnostics.AppendLine($"SharedAnimationGltf: {gltfPath}");
            diagnostics.AppendLine("Purpose: capture CompStream bytes, sampling-bytecode interpretation, sampled output values, and mapped CHPR pose addresses.");
            diagnostics.AppendLine("RawCompStream files contain the parsed CompStream byte array supplied to this decoder, not a separately re-read or re-decompressed file.");
            diagnostics.AppendLine("Pose snapshots show decoded Pose.Transforms results; they are not direct snapshots of internal allocation bytes before/after each native handler write.");
            diagnostics.AppendLine("Important: lower-16-bit candidate comparisons in this file are diagnostic-only; they do not change animation mapping.");
            diagnostics.AppendLine();

            CHPR.CharacterInfo characterInfo = chpr.CharacterInfos[0];

            // Never retry or infer a pose map by scanning raw CHPR bytes. Animation channels
            // can only be attached when a parsed game structure has supplied bone addresses.
            var bonesWithAddresses = characterInfo.Bones
                .Where(b => b.PoseAddress >= 0)
                .ToList();

            if (bonesWithAddresses.Count == 0)
            {
                diagnostics.AppendLine($"SkeletonBones={characterInfo.Bones.Count}; BonesWithPoseAddress=0.");
                diagnostics.AppendLine("No pose-address mappings were available from the parsed game structures.");
                SaveDiagnosticsFile(diagnosticsPath, diagnostics);
                report = "No pose-address mapping is available from the parsed game structures. " +
                    "The exporter will not infer addresses from raw bytes or retry mapping; leaving glTF unchanged. " +
                    $"Diagnostics: {diagnosticsPath}";
                return false;
            }

            Console.WriteLine($"[CHPR] Animation export pose map: bones={characterInfo.Bones.Count}, " +
                $"bonesWithAddress={bonesWithAddresses.Count}.");

            List<float[]> constantPools = BuildConstantPools(characterInfo);
            Console.WriteLine($"[CHPR] Opening shared animation target: {gltfPath}");
            Stopwatch totalTimer = Stopwatch.StartNew();

            ModelRoot model;
            try
            {
                model = ModelRoot.Load(gltfPath);
                Console.WriteLine($"[CHPR] Shared animation target loaded: nodes={model.LogicalNodes.Count()}, " +
                    $"constantPools={constantPools.Count}, parsedClips={chpr.animations.Count(a => a.CompStreamClip != null)}.");
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"ERROR reopening shared glTF: {ex.GetType().Name}: {ex.Message}");
                SaveDiagnosticsFile(diagnosticsPath, diagnostics);
                report = $"Could not reopen exported glTF to add animation: {ex.GetType().Name}: {ex.Message}. " +
                    $"Diagnostics: {diagnosticsPath}";
                return false;
            }

            var nodesByName = new Dictionary<string, Node>(StringComparer.Ordinal);
            foreach (Node node in model.LogicalNodes)
            {
                if (!string.IsNullOrWhiteSpace(node.Name) && !nodesByName.ContainsKey(node.Name))
                    nodesByName.Add(node.Name, node);
            }

            var tracksByAddress = new Dictionary<int, List<BoneTrack>>();
            int nodeMatches = 0;
            foreach (CHPR.BoneData bone in bonesWithAddresses)
            {
                if (string.IsNullOrWhiteSpace(bone.Name) || !nodesByName.TryGetValue(bone.Name, out Node node))
                    continue;

                if (!tracksByAddress.TryGetValue(bone.PoseAddress, out List<BoneTrack> addressTracks))
                {
                    addressTracks = new List<BoneTrack>();
                    tracksByAddress.Add(bone.PoseAddress, addressTracks);
                }

                if (!Matrix4x4.Decompose(bone.LocalTransform, out Vector3 bindScale, out Quaternion bindRotation, out Vector3 bindTranslation))
                {
                    bindScale = Vector3.One;
                    bindRotation = Quaternion.Identity;
                    bindTranslation = Vector3.Zero;
                }

                addressTracks.Add(new BoneTrack
                {
                    Bone = bone,
                    Node = node,
                    BindTranslation = bindTranslation,
                    BindRotation = SafeNormalize(bindRotation, Quaternion.Identity),
                    BindScale = bindScale
                });
                nodeMatches++;
            }

            try
            {
                AppendBoneAddressDiagnostics(diagnostics, characterInfo.Bones, bonesWithAddresses,
                    nodesByName, tracksByAddress, nodeMatches);
            }
            catch (Exception diagnosticsEx)
            {
                diagnostics.AppendLine($"Bone-map diagnostics failed without affecting export: {diagnosticsEx.GetType().Name}: {diagnosticsEx.Message}");
                Console.WriteLine($"[CHPR] Bone-map diagnostics failed; continuing export: {diagnosticsEx.GetType().Name}: {diagnosticsEx.Message}");
            }

            if (nodeMatches == 0)
            {
                diagnostics.AppendLine("ERROR: No mapped CHPR bone names matched nodes in the glTF.");
                SaveDiagnosticsFile(diagnosticsPath, diagnostics);
                report = $"Found {bonesWithAddresses.Count} mapped CHPR bones, but none matched a node name in the glTF. " +
                    $"Diagnostics: {diagnosticsPath}";
                return false;
            }

            Console.WriteLine($"[CHPR] Shared animation target mapping complete: matchedBoneNodes={nodeMatches}, " +
                $"mappedAddressGroups={tracksByAddress.Count}. Beginning clip sampling.");

            int animationsAdded = 0;
            int channelsAdded = 0;
            int clipsSkipped = 0;
            var warnings = new List<string>();

            for (int entryIndex = 0; entryIndex < chpr.animations.Count; entryIndex++)
            {
                CHPR.AnimationArrayEntry entry = chpr.animations[entryIndex];
                CHPRCompStreamAnimation.ParsedClip clip = entry.CompStreamClip;
                if (clip == null || clip.FrameCount == 0)
                    continue;

                if (clip.FrameCount > 100000)
                {
                    clipsSkipped++;
                    warnings.Add($"entry {entryIndex}: frame count {clip.FrameCount} exceeds the safety limit");
                    continue;
                }

                Stopwatch clipTimer = Stopwatch.StartNew();
                string gameNameForLog = entry.GameAnimationName ?? "(unresolved)";
                Console.WriteLine($"[CHPR] Clip {entryIndex + 1}/{chpr.animations.Count} " +
                    $"(slot={entry.OutputSlot}, name=\"{gameNameForLog}\", entry={entryIndex}): beginning bake, " +
                    $"frames={clip.FrameCount}, streams={clip.Streams.Count}.");

                var player = CHPRCompStreamAnimation.CreatePlayer(clip);
                var clipTracksByAddress = new Dictionary<int, List<BoneTrack>>();

                diagnostics.AppendLine();
                diagnostics.AppendLine($"CLIP entry={entryIndex}; slot={entry.OutputSlot}; name=\"{gameNameForLog}\";");
                diagnostics.AppendLine($"  frameCount={clip.FrameCount}; rate={clip.Rate.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)}; " +
                    $"streams={clip.Streams.Count}; outputSlotMask=0x{clip.OutputSlotMask:X12}; " +
                    $"headerSize=0x{clip.HeaderSize:X}; descriptorOffset=0x{clip.DescriptorOffset:X}; " +
                    $"bitstreamOffset=0x{clip.BitstreamOffset:X}; totalSize=0x{clip.TotalSize:X}.");
                try
                {
                    AppendRawCompStreamDiagnostics(
                        diagnostics, rawDiagnosticsDirectory, entryIndex, entry.OutputSlot,
                        gameNameForLog, clip);
                }
                catch (Exception diagnosticsEx)
                {
                    diagnostics.AppendLine($"  Raw CompStream dump failed without affecting export: {diagnosticsEx.GetType().Name}: {diagnosticsEx.Message}");
                    Console.WriteLine($"[CHPR] Raw CompStream dump failed; continuing export: {diagnosticsEx.GetType().Name}: {diagnosticsEx.Message}");
                }
                try
                {
                    AppendSamplingBytecodeListings(diagnostics, clip);
                }
                catch (Exception diagnosticsEx)
                {
                    diagnostics.AppendLine($"  Sampling-bytecode listing failed without affecting export: {diagnosticsEx.GetType().Name}: {diagnosticsEx.Message}");
                }

                Dictionary<int, List<int>> bytecodeDestinationsByStream = new Dictionary<int, List<int>>();
                try
                {
                    bytecodeDestinationsByStream = AppendStreamDestinationDiagnostics(diagnostics, clip);
                }
                catch (Exception diagnosticsEx)
                {
                    diagnostics.AppendLine($"  Stream destination diagnostics failed without affecting export: {diagnosticsEx.GetType().Name}: {diagnosticsEx.Message}");
                    Console.WriteLine($"[CHPR] Stream destination diagnostics failed; continuing export: {diagnosticsEx.GetType().Name}: {diagnosticsEx.Message}");
                }

                try
                {
                    // Sampling bytecode has a fixed destination layout for a clip. Use
                    // frame zero to find only the addresses this clip actually emits,
                    // rather than checking every mapped bone on every frame.
                    CHPRCompStreamAnimation.Pose firstPose = player.Sample(0.0f, constantPools);
                    TryAppendSampledOutputValueDiagnostics(diagnostics, 0, firstPose);
                    try
                    {
                        AppendSampledPoseDiagnostics(diagnostics, firstPose,
                            bytecodeDestinationsByStream, bonesWithAddresses);
                    }
                    catch (Exception diagnosticsEx)
                    {
                        diagnostics.AppendLine($"  Frame-zero destination diagnostics failed without affecting bake: {diagnosticsEx.GetType().Name}: {diagnosticsEx.Message}");
                        Console.WriteLine($"[CHPR] Frame-zero destination diagnostics failed; continuing bake: {diagnosticsEx.GetType().Name}: {diagnosticsEx.Message}");
                    }

                    foreach (int poseAddress in firstPose.Transforms.Keys)
                    {
                        if (!tracksByAddress.TryGetValue(poseAddress, out List<BoneTrack> addressTracks))
                            continue;

                        clipTracksByAddress.Add(poseAddress, addressTracks);
                        foreach (BoneTrack track in addressTracks)
                        {
                            track.Translation.Clear();
                            track.Rotation.Clear();
                            track.Scale.Clear();
                            track.HasAnimationData = false;
                        }
                    }

                    if (clipTracksByAddress.Count == 0)
                    {
                        // Even when bone-address matching fails, capture a few decoded outputs.
                        // Sample only diagnostic checkpoints so the log stays bounded.
                        int diagnosticMidFrame = (clip.FrameCount - 1) / 2;
                        int diagnosticLastFrame = clip.FrameCount - 1;
                        if (diagnosticMidFrame > 0)
                        {
                            CHPRCompStreamAnimation.Pose midPose = player.Sample(diagnosticMidFrame, constantPools);
                            TryAppendSampledOutputValueDiagnostics(diagnostics, diagnosticMidFrame, midPose);
                        }
                        if (diagnosticLastFrame > 0 && diagnosticLastFrame != diagnosticMidFrame)
                        {
                            CHPRCompStreamAnimation.Pose lastPose = player.Sample(diagnosticLastFrame, constantPools);
                            TryAppendSampledOutputValueDiagnostics(diagnostics, diagnosticLastFrame, lastPose);
                        }

                        clipsSkipped++;
                        diagnostics.AppendLine("  BakeDecision: skipped because no frame-zero transform destination exactly matched an exported rig-node pose address.");
                        Console.WriteLine($"[CHPR] Clip {entryIndex + 1}/{chpr.animations.Count} " +
                            $"(slot={entry.OutputSlot}): no frame-zero pose addresses matched exported rig nodes; " +
                            $"skipping after {clipTimer.Elapsed.TotalSeconds:0.00}s.");
                        continue;
                    }

                    int progressInterval = Math.Max(1, clip.FrameCount / 10);
                    for (int frame = 0; frame < clip.FrameCount; frame++)
                    {
                        CHPRCompStreamAnimation.Pose pose =
                            frame == 0 ? firstPose : player.Sample(frame, constantPools);
                        int diagnosticMidFrameForBake = (clip.FrameCount - 1) / 2;
                        if (frame > 0 && (frame == diagnosticMidFrameForBake || frame == clip.FrameCount - 1))
                            TryAppendSampledOutputValueDiagnostics(diagnostics, frame, pose);
                        float timeSeconds = frame / GameFramesPerSecond;

                        foreach (KeyValuePair<int, List<BoneTrack>> addressTracks in clipTracksByAddress)
                        {
                            pose.Transforms.TryGetValue(addressTracks.Key, out float[] sample);
                            foreach (BoneTrack track in addressTracks.Value)
                            {
                                if (sample != null && HasAnyFiniteValue(sample))
                                    track.HasAnimationData = true;

                                ApplySample(track, sample, out Vector3 translation, out Quaternion rotation, out Vector3 scale);
                                track.Translation[timeSeconds] = translation;
                                track.Rotation[timeSeconds] = rotation;
                                track.Scale[timeSeconds] = scale;
                            }
                        }

                        if (frame == 0 || frame + 1 == clip.FrameCount || (frame + 1) % progressInterval == 0)
                        {
                            Console.WriteLine($"[CHPR] Clip {entryIndex + 1}/{chpr.animations.Count} " +
                                $"(slot={entry.OutputSlot}): sampled frame {frame + 1}/{clip.FrameCount}; " +
                                $"activeAddresses={clipTracksByAddress.Count}, elapsed={clipTimer.Elapsed.TotalSeconds:0.00}s.");
                        }
                    }

                    List<BoneTrack> tracksWithData = clipTracksByAddress.Values
                        .SelectMany(addressTracks => addressTracks)
                        .Where(track => track.HasAnimationData)
                        .ToList();
                    int clipChannelCount = tracksWithData.Count * 3;
                    if (clipChannelCount == 0)
                    {
                        clipsSkipped++;
                        diagnostics.AppendLine("  BakeDecision: skipped because sampled frames produced no finite mapped transforms.");
                        Console.WriteLine($"[CHPR] Clip {entryIndex + 1}/{chpr.animations.Count} " +
                            $"(slot={entry.OutputSlot}): sampled successfully but produced no finite mapped transforms; " +
                            $"elapsed={clipTimer.Elapsed.TotalSeconds:0.00}s.");
                        continue;
                    }

                    string animationName = BuildAnimationName(entry, entryIndex);
                    Animation animation = model.CreateAnimation(animationName);

                    Console.WriteLine($"[CHPR] Clip {entryIndex + 1}/{chpr.animations.Count} " +
                        $"(slot={entry.OutputSlot}): creating {clipChannelCount} channels across {tracksWithData.Count} nodes.");

                    foreach (BoneTrack track in tracksWithData)
                    {
                        animation.CreateTranslationChannel(track.Node, track.Translation, linear: true);
                        animation.CreateRotationChannel(track.Node, track.Rotation, linear: true);
                        animation.CreateScaleChannel(track.Node, track.Scale, linear: true);
                    }

                    animationsAdded++;
                    channelsAdded += clipChannelCount;
                    diagnostics.AppendLine($"  BakeDecision: baked; exactMatchedDestinations={clipTracksByAddress.Count}; " +
                        $"animatedBoneNodes={tracksWithData.Count}; channels={clipChannelCount}.");
                    Console.WriteLine($"[CHPR] Clip {entryIndex + 1}/{chpr.animations.Count} " +
                        $"(slot={entry.OutputSlot}): baked successfully in {clipTimer.Elapsed.TotalSeconds:0.00}s.");
                }
                catch (Exception ex)
                {
                    clipsSkipped++;
                    string warning = $"entry {entryIndex}, slot {entry.OutputSlot}: {ex.GetType().Name}: {ex.Message}";
                    warnings.Add(warning);
                    diagnostics.AppendLine($"  BakeDecision: ERROR {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"[CHPR] Clip {entryIndex + 1}/{chpr.animations.Count} " +
                        $"(slot={entry.OutputSlot}) failed after {clipTimer.Elapsed.TotalSeconds:0.00}s: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            diagnostics.AppendLine();
            diagnostics.AppendLine($"BAKE SUMMARY: clipsWithCompStream={chpr.animations.Count(a => a.CompStreamClip != null)}; " +
                $"animationsAdded={animationsAdded}; channelsAdded={channelsAdded}; skippedClips={clipsSkipped}; " +
                $"matchedBoneNodes={nodeMatches}; mappedFullAddressGroups={tracksByAddress.Count}.");

            if (animationsAdded == 0)
            {
                diagnostics.AppendLine("FinalOutcome: no animation clips produced mapped glTF channels; the glTF was not changed by animation baking.");
                SaveDiagnosticsFile(diagnosticsPath, diagnostics);
                Console.WriteLine($"[CHPR] Animation diagnostic summary: mappedBones={bonesWithAddresses.Count}, " +
                    $"matchedRigNodes={nodeMatches}, mappedFullAddressGroups={tracksByAddress.Count}, " +
                    $"animationsAdded=0; details={diagnosticsPath}.");
                report = $"No CompStream clips produced mapped glTF channels. Matched skeleton nodes={nodeMatches}; " +
                    $"CompStream clips={chpr.animations.Count(a => a.CompStreamClip != null)}; skipped={clipsSkipped}; " +
                    $"diagnostics={diagnosticsPath}.";
                if (warnings.Count > 0)
                    report += " " + string.Join(" | ", warnings.Take(4));
                return false;
            }

            Console.WriteLine($"[CHPR] Baking complete; saving shared animation glTF with " +
                $"{animationsAdded} animation(s) and {channelsAdded} channel(s).");

            try
            {
                Console.WriteLine($"[CHPR] Saving shared animation glTF using binary buffer chunks capped at {MaxGltfBufferSizeBytes / (1024 * 1024)} MiB.");
                model.SaveGLTF(gltfPath, new WriteSettings
                {
                    MergeBuffers = true,
                    BuffersMaxSize = MaxGltfBufferSizeBytes
                });
                totalTimer.Stop();
                Console.WriteLine($"[CHPR] Shared animation glTF saved: {gltfPath} " +
                    $"(elapsed={totalTimer.Elapsed.TotalSeconds:0.00}s).");
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"FinalOutcome: channels were built, but glTF save failed: {ex.GetType().Name}: {ex.Message}");
                SaveDiagnosticsFile(diagnosticsPath, diagnostics);
                report = $"Animation channels were built, but glTF save failed: {ex.GetType().Name}: {ex.Message}. " +
                    $"Diagnostics: {diagnosticsPath}";
                return false;
            }

            diagnostics.AppendLine($"FinalOutcome: saved shared animation glTF successfully; animationsAdded={animationsAdded}; channelsAdded={channelsAdded}.");
            SaveDiagnosticsFile(diagnosticsPath, diagnostics);
            Console.WriteLine($"[CHPR] Animation diagnostic summary: mappedBones={bonesWithAddresses.Count}, " +
                $"matchedRigNodes={nodeMatches}, mappedFullAddressGroups={tracksByAddress.Count}, " +
                $"animationsAdded={animationsAdded}, channelsAdded={channelsAdded}; details={diagnosticsPath}.");

            report = $"Added {animationsAdded} animation(s), {channelsAdded} channels, and matched {nodeMatches} bone nodes " +
                $"at {GameFramesPerSecond:0} FPS; skipped {clipsSkipped} clips; " +
                $"diagnostics={diagnosticsPath}; elapsed={totalTimer.Elapsed.TotalSeconds:0.00}s.";
            if (warnings.Count > 0)
                report += " Warnings: " + string.Join(" | ", warnings.Take(4));
            return true;
        }

        private static void SaveDiagnosticsFile(string path, StringBuilder diagnostics)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(path, diagnostics.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                Console.WriteLine($"[CHPR] Animation diagnostics saved: {Path.GetFullPath(path)}");
            }
            catch (Exception ex)
            {
                // Diagnostics must never turn an otherwise valid static or animated export into a failure.
                Console.WriteLine($"[CHPR] Could not write animation diagnostics file '{path}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void AppendRawCompStreamDiagnostics(
            StringBuilder diagnostics,
            string rawDiagnosticsDirectory,
            int entryIndex,
            uint outputSlot,
            string gameName,
            CHPRCompStreamAnimation.ParsedClip clip)
        {
            byte[] data = clip.RawCompStream;
            diagnostics.AppendLine("  RAW COMPSTREAM DATA");
            if (data == null)
            {
                diagnostics.AppendLine("  RawCompStream is null; no binary dump was written.");
                return;
            }

            Directory.CreateDirectory(rawDiagnosticsDirectory);
            string safeName = SanitizeAnimationName(gameName);
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "unresolved";
            if (safeName.Length > 40) safeName = safeName.Substring(0, 40);
            string fileName = $"entry_{entryIndex:D4}_slot_{outputSlot:D4}_{safeName}.compstream.bin";
            string path = Path.Combine(rawDiagnosticsDirectory, fileName);
            File.WriteAllBytes(path, data);

            string sha256;
            using (SHA256 sha = SHA256.Create())
                sha256 = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty);

            diagnostics.AppendLine($"  RawCompStreamLength=0x{data.Length:X} ({data.Length} bytes); SHA256={sha256};");
            diagnostics.AppendLine($"  RawCompStreamBinary={path}");
            AppendHexWindow(diagnostics, data, "CompStream beginning", 0, MaxDiagnosticHexBytes);
            if (clip.HeaderSize >= 0 && clip.HeaderSize < data.Length)
                AppendHexWindow(diagnostics, data, "Header boundary", Math.Max(0, clip.HeaderSize - 32), 64);
            if (clip.DescriptorOffset >= 0 && clip.DescriptorOffset < data.Length)
                AppendHexWindow(diagnostics, data, "Descriptor area", Math.Max(0, clip.DescriptorOffset - 16), MaxDiagnosticHexBytes);
            if (clip.BitstreamOffset >= 0 && clip.BitstreamOffset < data.Length)
                AppendHexWindow(diagnostics, data, "Bitstream area", Math.Max(0, checked((int)clip.BitstreamOffset) - 16), MaxDiagnosticHexBytes);
            diagnostics.AppendLine();
        }

        private static void AppendHexWindow(StringBuilder diagnostics, byte[] data, string label, int offset, int requestedLength)
        {
            if (data == null || offset < 0 || offset >= data.Length || requestedLength <= 0)
                return;
            int length = Math.Min(requestedLength, data.Length - offset);
            diagnostics.AppendLine($"  HEX {label}: offset=0x{offset:X}; length=0x{length:X} ({length} bytes).");
            int end = offset + length;
            for (int line = offset; line < end; line += 16)
            {
                int count = Math.Min(16, end - line);
                var hex = new StringBuilder(count * 3);
                var ascii = new StringBuilder(count);
                for (int i = 0; i < count; i++)
                {
                    byte value = data[line + i];
                    if (i > 0) hex.Append(' ');
                    hex.Append(value.ToString("X2"));
                    ascii.Append(value >= 0x20 && value <= 0x7E ? (char)value : '.');
                }
                diagnostics.AppendLine($"    {line:X8}: {hex.ToString().PadRight(47)} |{ascii}|");
            }
        }

        private static void AppendSamplingBytecodeListings(StringBuilder diagnostics, CHPRCompStreamAnimation.ParsedClip clip)
        {
            byte[] data = clip.RawCompStream;
            if (data == null) return;
            diagnostics.AppendLine("  SAMPLING BYTECODE INSTRUCTION LISTINGS (diagnostic-only)");
            foreach (CHPRCompStreamAnimation.StreamInfo stream in clip.Streams.OrderBy(s => s.Index))
            {
                int start = stream.SamplingBytecodeOffset;
                diagnostics.AppendLine($"  Stream {stream.Index}: bytecode listing begins at 0x{start:X}.");
                try
                {
                    AppendSamplingBytecodeRangeListing(diagnostics, data, start, data.Length, stream.Index, 0);
                }
                catch (Exception ex)
                {
                    diagnostics.AppendLine($"    Listing stopped safely: {ex.GetType().Name}: {ex.Message}");
                }
            }
            diagnostics.AppendLine();
        }

        private static void AppendSamplingBytecodeRangeListing(
            StringBuilder diagnostics, byte[] data, int start, int end, int streamIndex, int depth)
        {
            const int MaxNestedBlocks = 16;
            if (depth > MaxNestedBlocks)
                throw new InvalidDataException("Nested sampling bytecode exceeded diagnostic depth limit.");
            if (start < 0 || end < start || end > data.Length || start >= end)
                throw new InvalidDataException($"Invalid sampling bytecode range 0x{start:X}-0x{end:X}.");

            int q = start;
            int logged = 0;
            while (q < end && logged < MaxDiagnosticOpcodeLinesPerStream)
            {
                int op = data[q];
                if (op == 0)
                {
                    diagnostics.AppendLine($"    stream={streamIndex} depth={depth} offset=0x{q:X} opcode=0 END raw=[{FormatHexBytes(data, q, 1)}]");
                    return;
                }
                if (op == 1)
                {
                    if (q > end - 3) throw new InvalidDataException($"Truncated opcode-1 header at 0x{q:X}.");
                    int payloadLength = ReadDiagnosticUInt16(data, q + 1);
                    int bodyStart = q + 3;
                    int bodyEnd = checked(bodyStart + payloadLength);
                    if (bodyEnd > end) throw new InvalidDataException($"Opcode-1 block at 0x{q:X} exceeds containing range.");
                    diagnostics.AppendLine($"    stream={streamIndex} depth={depth} offset=0x{q:X} opcode=1 NESTED length={payloadLength} raw=[{FormatHexBytes(data, q, Math.Min(3 + payloadLength, MaxDiagnosticHexBytes))}]{(3 + payloadLength > MaxDiagnosticHexBytes ? " ..." : string.Empty)}");
                    AppendSamplingBytecodeRangeListing(diagnostics, data, bodyStart, bodyEnd, streamIndex, depth + 1);
                    return;
                }
                if (op == 4)
                {
                    if (q > end - 3) throw new InvalidDataException($"Truncated opcode-4 header at 0x{q:X}.");
                    int count = data[q + 1];
                    int bitfieldLength = checked(3 + count * 2);
                    RequireDiagnosticBytecodeRange(data, q, bitfieldLength, end, "opcode-4 block", q);
                    diagnostics.AppendLine($"    stream={streamIndex} depth={depth} offset=0x{q:X} opcode=4 BITFIELD count={count} length={bitfieldLength} raw=[{FormatHexBytes(data, q, Math.Min(bitfieldLength, MaxDiagnosticHexBytes))}]{(bitfieldLength > MaxDiagnosticHexBytes ? " ..." : string.Empty)}");
                    q += bitfieldLength; logged++; continue;
                }
                if (op == 5)
                {
                    diagnostics.AppendLine($"    stream={streamIndex} depth={depth} offset=0x{q:X} opcode=5 EVENT raw=[{FormatHexBytes(data, q, 1)}]");
                    q++; logged++; continue;
                }
                if (q > end - 4) throw new InvalidDataException($"Truncated sampling-bytecode instruction at 0x{q:X}.");
                int entryCount = data[q + 1];
                int destination = ReadDiagnosticUInt16(data, q + 2);
                int entrySize;
                string kind;
                switch (op)
                {
                    case 2: entrySize = 6; kind = "TRANSFORM"; break;
                    case 3: entrySize = 4; kind = "ROTATION_TRANSLATION"; break;
                    case 6: entrySize = 2; kind = "SCALAR"; break;
                    case 7: entrySize = 2; kind = "INTEGER"; break;
                    default:
                        diagnostics.AppendLine($"    stream={streamIndex} depth={depth} offset=0x{q:X} opcode={op} UNKNOWN_TERMINATES raw=[{FormatHexBytes(data, q, Math.Min(end - q, 16))}]");
                        return;
                }
                int length = checked(4 + entryCount * entrySize);
                RequireDiagnosticBytecodeRange(data, q, length, end, kind + " instruction", q);
                diagnostics.AppendLine($"    stream={streamIndex} depth={depth} offset=0x{q:X} opcode={op} kind={kind} count={entryCount} length={length} " +
                    $"destination=0x{destination:X4} slot={destination & 0x3F} element={(destination >> 6) & 0x3FF} " +
                    $"raw=[{FormatHexBytes(data, q, Math.Min(length, MaxDiagnosticHexBytes))}]{(length > MaxDiagnosticHexBytes ? " ..." : string.Empty)}");
                q += length; logged++;
            }
            if (q < end)
                diagnostics.AppendLine($"    stream={streamIndex} depth={depth}: listing capped after {MaxDiagnosticOpcodeLinesPerStream} instructions in this range.");
        }

        private static string FormatHexBytes(byte[] data, int offset, int count)
        {
            if (data == null || offset < 0 || count <= 0 || offset >= data.Length) return string.Empty;
            int length = Math.Min(count, data.Length - offset);
            return BitConverter.ToString(data, offset, length).Replace("-", " ");
        }

        private static void TryAppendSampledOutputValueDiagnostics(
            StringBuilder diagnostics, int frameIndex, CHPRCompStreamAnimation.Pose pose)
        {
            try
            {
                AppendSampledOutputValueDiagnostics(diagnostics, frameIndex, pose);
            }
            catch (Exception ex)
            {
                diagnostics.AppendLine($"  Sampled-output snapshot failed at frame {frameIndex} without affecting export: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void AppendSampledOutputValueDiagnostics(
            StringBuilder diagnostics, int frameIndex, CHPRCompStreamAnimation.Pose pose)
        {
            if (pose == null || pose.Transforms == null)
            {
                diagnostics.AppendLine($"  SAMPLED OUTPUT SNAPSHOT frame={frameIndex}: Pose or Transform dictionary is null.");
                return;
            }
            List<KeyValuePair<int, float[]>> entries = pose.Transforms.OrderBy(pair => pair.Key).ToList();
            diagnostics.AppendLine($"  SAMPLED OUTPUT SNAPSHOT frame={frameIndex}; transformEntryCount={entries.Count}; " +
                $"note=decoded Pose.Transforms values, not raw internal buffer bytes.");
            int count = Math.Min(entries.Count, MaxDiagnosticPoseEntries);
            for (int i = 0; i < count; i++)
            {
                KeyValuePair<int, float[]> pair = entries[i];
                string values = pair.Value == null ? "<null>" : string.Join(", ", pair.Value.Select(value =>
                    value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture)));
                diagnostics.AppendLine($"    destination=0x{pair.Key:X4} slot={pair.Key & 0x3F} element={(pair.Key >> 6) & 0x3FF} valueCount={(pair.Value == null ? 0 : pair.Value.Length)} values=[{values}]");
            }
            if (entries.Count > count)
                diagnostics.AppendLine($"    ... {entries.Count - count} additional destinations omitted by diagnostic output cap.");
        }

        private static void AppendBoneAddressDiagnostics(
            StringBuilder diagnostics,
            IReadOnlyList<CHPR.BoneData> allBones,
            IReadOnlyList<CHPR.BoneData> bonesWithAddresses,
            IReadOnlyDictionary<string, Node> nodesByName,
            IReadOnlyDictionary<int, List<BoneTrack>> tracksByAddress,
            int matchedNodeCount)
        {
            diagnostics.AppendLine("BONE ADDRESS MAP");
            diagnostics.AppendLine($"  skeletonBones={allBones.Count}; bonesWithPoseAddress={bonesWithAddresses.Count}; " +
                $"matchedRigNodes={matchedNodeCount}; distinctFullAddressGroupsInRig={tracksByAddress.Count}; " +
                $"distinctFullPoseAddresses={bonesWithAddresses.Select(b => b.PoseAddress).Distinct().Count()}.");
            diagnostics.AppendLine("  Address format: full=PoseAddress & 0x03FFFFFF; lower16=full & 0xFFFF; contextBits=(full >> 21) & 7.");

            foreach (CHPR.BoneData bone in bonesWithAddresses
                .OrderBy(b => b.PoseAddress)
                .ThenBy(b => b.Name, StringComparer.Ordinal))
            {
                int fullAddress = bone.PoseAddress & 0x03FFFFFF;
                int lower16 = fullAddress & 0xFFFF;
                int contextBits = (fullAddress >> 21) & 7;
                bool rigNodeMatched = !string.IsNullOrWhiteSpace(bone.Name) && nodesByName.ContainsKey(bone.Name);
                diagnostics.AppendLine($"  Bone name=\"{bone.Name ?? "(unnamed)"}\"; full=0x{fullAddress:X7}; " +
                    $"lower16=0x{lower16:X4}; contextBits={contextBits}; recordType={bone.PoseAddressRecordType}; " +
                    $"rigNodeMatched={rigNodeMatched}; fullAddressGroupContainsRigNode={tracksByAddress.ContainsKey(bone.PoseAddress)}.");
            }

            diagnostics.AppendLine("  DUPLICATE LOWER-16-BIT ADDRESS GROUPS (diagnostic only)");
            var duplicateLower16Groups = bonesWithAddresses
                .GroupBy(b => (b.PoseAddress & 0x03FFFFFF) & 0xFFFF)
                .Where(g => g.Count() > 1)
                .OrderBy(g => g.Key)
                .ToList();
            diagnostics.AppendLine($"  duplicateGroups={duplicateLower16Groups.Count}.");
            foreach (var group in duplicateLower16Groups)
            {
                diagnostics.AppendLine($"  lower16=0x{group.Key:X4}; count={group.Count()}; members=" +
                    string.Join(" | ", group.OrderBy(b => b.PoseAddress).ThenBy(b => b.Name, StringComparer.Ordinal)
                        .Select(b => $"{b.Name ?? "(unnamed)"}@0x{(b.PoseAddress & 0x03FFFFFF):X7}/ctx{((b.PoseAddress & 0x03FFFFFF) >> 21) & 7}")) + ".");
            }
            diagnostics.AppendLine();
        }

        private static Dictionary<int, List<int>> AppendStreamDestinationDiagnostics(
            StringBuilder diagnostics,
            CHPRCompStreamAnimation.ParsedClip clip)
        {
            diagnostics.AppendLine("  STREAM SAMPLING-BYTECODE DESTINATIONS");
            var byStream = new Dictionary<int, List<int>>();
            foreach (CHPRCompStreamAnimation.StreamInfo stream in clip.Streams.OrderBy(s => s.Index))
            {
                try
                {
                    List<int> destinations = ReadTransformDestinationsFromBytecode(clip, stream);
                    byStream[stream.Index] = destinations;
                    diagnostics.AppendLine($"  Stream {stream.Index}: samplingBytecodeOffset=0x{stream.SamplingBytecodeOffset:X}; " +
                        $"bitstreamOffset=0x{stream.BitstreamOffset:X}; groups={stream.GroupCount}; channels={stream.ChannelCount}; " +
                        $"rateMultiplier={stream.RateMultiplier}; constantPoolIndex={stream.ConstantPoolIndex}; " +
                        $"transformEntries={destinations.Count}; distinctDestinations={destinations.Distinct().Count()}; " +
                        $"destinations=[{FormatDestinationList(destinations)}].");
                }
                catch (Exception ex)
                {
                    diagnostics.AppendLine($"  Stream {stream.Index}: destination scan failed without affecting export: {ex.GetType().Name}: {ex.Message}");
                }
            }

            List<int> union = byStream.Values.SelectMany(values => values).Distinct().OrderBy(value => value).ToList();
            diagnostics.AppendLine($"  BytecodeTransformDestinationUnionCount={union.Count}; destinations=[{FormatDestinationList(union)}].");
            var crossStreamCollisions = byStream
                .SelectMany(pair => pair.Value.Distinct().Select(destination => new { StreamIndex = pair.Key, Destination = destination }))
                .GroupBy(item => item.Destination)
                .Where(group => group.Select(item => item.StreamIndex).Distinct().Count() > 1)
                .OrderBy(group => group.Key)
                .ToList();
            diagnostics.AppendLine($"  DestinationsWrittenByMultipleStreams={crossStreamCollisions.Count}.");
            foreach (var group in crossStreamCollisions)
            {
                diagnostics.AppendLine($"  CrossStreamDestinationCollision=0x{group.Key:X4}; " +
                    $"streams=[{string.Join(", ", group.Select(item => item.StreamIndex).Distinct().OrderBy(index => index))}].");
            }
            return byStream;
        }

        /// <summary>
        /// Mirrors the sampling-bytecode instruction stepping used by CHPRCompStreamPlayer, but only
        /// extracts transform destinations for diagnostics. It never changes playback or bone mapping.
        /// </summary>
        private static List<int> ReadTransformDestinationsFromBytecode(
            CHPRCompStreamAnimation.ParsedClip clip,
            CHPRCompStreamAnimation.StreamInfo stream)
        {
            byte[] data = clip.RawCompStream;
            int start = stream.SamplingBytecodeOffset;
            if (data == null || start < 0 || start >= data.Length)
                throw new InvalidDataException($"Sampling-bytecode offset 0x{start:X} is outside the CompStream.");

            var result = new List<int>();
            ScanSamplingBytecodeRange(data, start, data.Length, result, 0, stream.SamplingBytecodeOffset);
            return result;
        }

        /// <summary>
        /// Diagnostic-only bytecode scanner based on the native dispatcher table at
        /// PTR_LAB_71017b3a58. Opcode 1 contains a ushort byte length followed by a
        /// nested instruction stream. The native handler evaluates that nested stream
        /// and stops this dispatch pass; it is not a fixed three-byte preamble.
        /// </summary>
        private static void ScanSamplingBytecodeRange(
            byte[] data,
            int start,
            int end,
            List<int> result,
            int depth,
            int streamOffset)
        {
            const int MaxInstructions = 100000;
            const int MaxNestedBlocks = 16;
            if (depth > MaxNestedBlocks)
                throw new InvalidDataException($"Sampling bytecode at 0x{streamOffset:X} exceeded {MaxNestedBlocks} nested opcode-1 blocks.");

            int q = start;
            for (int instructionIndex = 0; instructionIndex < MaxInstructions && q < end; instructionIndex++)
            {
                int op = data[q];
                if (op == 0)
                    return;

                // Native FUN_71000615d0 reads a uint16 length at q+1, executes the
                // nested opcode stream at q+3, then returns null on a cache miss.
                // This diagnostic pass always evaluates the nested block, as the
                // frame-zero sampler does for a freshly created player.
                if (op == 1)
                {
                    if (q > end - 3)
                        throw new InvalidDataException($"Truncated opcode-1 header at 0x{q:X}.");
                    int payloadLength = ReadDiagnosticUInt16(data, q + 1);
                    int bodyStart = q + 3;
                    int bodyEnd = checked(bodyStart + payloadLength);
                    if (bodyEnd > end)
                        throw new InvalidDataException($"Opcode-1 block at 0x{q:X} extends beyond its containing bytecode range.");

                    ScanSamplingBytecodeRange(data, bodyStart, bodyEnd, result, depth + 1, streamOffset);
                    return;
                }

                if (op == 4)
                {
                    if (q > end - 3)
                        throw new InvalidDataException($"Truncated opcode-4 header at 0x{q:X}.");
                    int bitfieldCount = data[q + 1];
                    int bitfieldBytes = checked(bitfieldCount * 2);
                    RequireDiagnosticBytecodeRange(data, q + 3, bitfieldBytes, end, "bitfield sampling entries", q);
                    // FUN_7100e58e10 updates auxiliary output bitfields; it emits no skeletal transform.
                    q += 3 + bitfieldBytes;
                    continue;
                }
                if (op == 5)
                {
                    // FUN_710006edd0 updates auxiliary action/event structures and consumes no inline payload.
                    q += 1;
                    continue;
                }
                if (q > end - 4)
                    throw new InvalidDataException($"Truncated sampling-bytecode instruction at 0x{q:X}.");

                int count = data[q + 1];
                int destination = ReadDiagnosticUInt16(data, q + 2);
                switch (op)
                {
                    case 2:
                        {
                            int bytes = checked(count * 6);
                            RequireDiagnosticBytecodeRange(data, q + 4, bytes, end, "transform sampling entries", q);
                            for (int i = 0; i < count; i++)
                                result.Add(AdvanceDiagnosticDestination(destination, 5 * i));
                            q += 4 + bytes;
                            break;
                        }
                    case 3:
                        {
                            int bytes = checked(count * 4);
                            RequireDiagnosticBytecodeRange(data, q + 4, bytes, end, "rotation/translation sampling entries", q);
                            for (int i = 0; i < count; i++)
                                result.Add(AdvanceDiagnosticDestination(destination, 7 * i));
                            q += 4 + bytes;
                            break;
                        }
                    case 6:
                    case 7:
                        {
                            int bytes = checked(count * 2);
                            RequireDiagnosticBytecodeRange(data, q + 4, bytes, end, "scalar/integer sampling entries", q);
                            q += 4 + bytes;
                            break;
                        }
                    default:
                        // Unknown opcodes terminate native dispatch. Do not guess an advance.
                        return;
                }
            }

            if (q >= end)
                return;
            throw new InvalidDataException($"Sampling bytecode at 0x{streamOffset:X} exceeded {MaxInstructions} instructions without termination.");
        }

        // CHPRCompStreamAnimation.ReadUInt16 is intentionally private to its class.
        // The exporter diagnostics only need a little-endian read of this two-byte field.
        private static int ReadDiagnosticUInt16(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset > data.Length - 2)
                throw new InvalidDataException($"Cannot read a diagnostic uint16 at offset 0x{offset:X}.");

            return data[offset] | (data[offset + 1] << 8);
        }

        private static void RequireDiagnosticBytecodeRange(byte[] data, int offset, int count, int end, string context, int instructionOffset)
        {
            if (data == null || offset < 0 || count < 0 || end < 0 || end > data.Length || offset > end - count)
                throw new InvalidDataException($"Truncated {context} at sampling-bytecode offset 0x{instructionOffset:X} (range end 0x{end:X}).");
        }

        private static int AdvanceDiagnosticDestination(int destination, int units)
        {
            return (destination & 0x3F) | (((destination >> 6) + units) << 6);
        }

        private static string FormatDestinationList(IEnumerable<int> destinations)
        {
            return string.Join(", ", destinations.Select(destination =>
                $"0x{destination:X4}(slot={destination & 0x3F},element={destination >> 6})"));
        }

        private static void AppendSampledPoseDiagnostics(
            StringBuilder diagnostics,
            CHPRCompStreamAnimation.Pose firstPose,
            IReadOnlyDictionary<int, List<int>> bytecodeDestinationsByStream,
            IReadOnlyList<CHPR.BoneData> bonesWithAddresses)
        {
            List<int> sampledDestinations = firstPose.Transforms.Keys.OrderBy(value => value).ToList();
            List<int> bytecodeDestinations = bytecodeDestinationsByStream.Values
                .SelectMany(values => values).Distinct().OrderBy(value => value).ToList();
            List<int> exactMatches = sampledDestinations
                .Where(destination => bonesWithAddresses.Any(bone => bone.PoseAddress == destination))
                .ToList();
            List<int> lower16CandidateDestinations = sampledDestinations
                .Where(destination => bonesWithAddresses.Any(bone =>
                    ((bone.PoseAddress & 0x03FFFFFF) & 0xFFFF) == (destination & 0xFFFF)))
                .ToList();
            List<int> bytecodeOnly = bytecodeDestinations.Except(sampledDestinations).ToList();
            List<int> sampledOnly = sampledDestinations.Except(bytecodeDestinations).ToList();

            diagnostics.AppendLine("  SAMPLED FRAME-0 DESTINATIONS");
            diagnostics.AppendLine($"  frame0TransformKeys={sampledDestinations.Count}; bytecodeDestinationUnion={bytecodeDestinations.Count}; " +
                $"exactFullAddressMatches={exactMatches.Count}; destinationsWithLower16Candidates={lower16CandidateDestinations.Count}; " +
                $"bytecodeOnly={bytecodeOnly.Count}; sampledOnly={sampledOnly.Count}.");
            diagnostics.AppendLine($"  Frame0Destinations=[{FormatDestinationList(sampledDestinations)}].");
            if (bytecodeOnly.Count > 0)
                diagnostics.AppendLine($"  BytecodeOnlyDestinations=[{FormatDestinationList(bytecodeOnly)}].");
            if (sampledOnly.Count > 0)
                diagnostics.AppendLine($"  SampledOnlyDestinations=[{FormatDestinationList(sampledOnly)}].");

            diagnostics.AppendLine("  DESTINATIONS WITH EXACT OR LOWER-16-BIT BONE CANDIDATES (lower-16 comparison is diagnostic only)");
            foreach (int destination in sampledDestinations)
            {
                List<CHPR.BoneData> exactBones = bonesWithAddresses
                    .Where(bone => bone.PoseAddress == destination)
                    .OrderBy(bone => bone.Name, StringComparer.Ordinal)
                    .ToList();
                List<CHPR.BoneData> lower16Candidates = bonesWithAddresses
                    .Where(bone => ((bone.PoseAddress & 0x03FFFFFF) & 0xFFFF) == (destination & 0xFFFF))
                    .OrderBy(bone => bone.PoseAddress)
                    .ThenBy(bone => bone.Name, StringComparer.Ordinal)
                    .ToList();
                if (exactBones.Count == 0 && lower16Candidates.Count == 0)
                    continue;

                diagnostics.AppendLine($"  destination=0x{destination:X4}; slot={destination & 0x3F}; element={destination >> 6}; " +
                    $"exactFullAddressBones=[{string.Join(", ", exactBones.Select(bone => bone.Name ?? "(unnamed)"))}]; " +
                    $"lower16Candidates=[{string.Join(" | ", lower16Candidates.Select(bone =>
                        $"{bone.Name ?? "(unnamed)"}@0x{(bone.PoseAddress & 0x03FFFFFF):X7}/ctx{((bone.PoseAddress & 0x03FFFFFF) >> 21) & 7}"))}].");
            }
            diagnostics.AppendLine("  Existing exact-full-address matching logic was not changed by this diagnostic pass.");
        }

        private const int MaxBlenderIdNameLength = 63;

        private static string BuildAnimationName(CHPR.AnimationArrayEntry entry, int entryIndex)
        {
            // Keep the slot first, followed by the actual game clip name. Blender ID
            // names are limited, so keep the complete name <= 63 ASCII characters.
            string prefix = entry.OutputSlot.ToString("D3") + "_";
            string gameName = SanitizeAnimationName(entry.GameAnimationName);
            if (string.IsNullOrWhiteSpace(gameName))
                gameName = $"entry{entryIndex:D3}";

            int suffixLimit = Math.Max(1, MaxBlenderIdNameLength - prefix.Length);
            if (gameName.Length > suffixLimit)
                gameName = gameName.Substring(0, suffixLimit).TrimEnd('_', '-', '.');

            if (gameName.Length == 0)
                gameName = $"e{entryIndex:D3}";

            return prefix + gameName;
        }

        private static string SanitizeAnimationName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var chars = new char[name.Length];
            int length = 0;
            bool previousUnderscore = false;

            foreach (char c in name)
            {
                bool safe = (c >= 'A' && c <= 'Z') ||
                    (c >= 'a' && c <= 'z') ||
                    (c >= '0' && c <= '9') ||
                    c == '_' || c == '-' || c == '.';

                char output = safe ? c : '_';
                if (output == '_' && previousUnderscore)
                    continue;

                chars[length++] = output;
                previousUnderscore = output == '_';
            }

            return new string(chars, 0, length).Trim('_', '-', '.');
        }

        private static void ApplySample(BoneTrack track, float[] sample,
            out Vector3 translation, out Quaternion rotation, out Vector3 scale)
        {
            translation = track.BindTranslation;
            rotation = track.BindRotation;
            scale = track.BindScale;

            if (sample == null)
                return;

            // CompStream output transform order is {qw,qx,qy,qz,scale xyz,translation xyz}
            // for opcode 2, or {qw,qx,qy,qz,translation xyz} for opcode 3.
            if (sample.Length >= 4 &&
                IsFinite(sample[0]) && IsFinite(sample[1]) && IsFinite(sample[2]) && IsFinite(sample[3]))
            {
                Quaternion clipRotation = new Quaternion(sample[1], sample[2], sample[3], sample[0]);
                clipRotation = SafeNormalize(clipRotation, Quaternion.Identity);
                rotation = SafeNormalize(Multiply(clipRotation, track.BindRotation), track.BindRotation);
            }

            int translationOffset = sample.Length == 10 ? 7 : 4;
            if (sample.Length >= translationOffset + 3 && !float.IsNaN(sample[translationOffset]))
            {
                // This matches Mocktroid's current pose application: animated translation
                // is an offset added to the local bind translation.
                translation += new Vector3(sample[translationOffset], sample[translationOffset + 1], sample[translationOffset + 2]);
            }

            if (sample.Length == 10 && !float.IsNaN(sample[4]))
                scale = new Vector3(sample[4], sample[5], sample[6]);
        }

        private static Quaternion Multiply(Quaternion a, Quaternion b)
        {
            // Explicit Hamilton product to make the clip * bind ordering clear.
            return new Quaternion(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
        }

        private static Quaternion SafeNormalize(Quaternion value, Quaternion fallback)
        {
            float lengthSquared = value.LengthSquared();
            if (!IsFinite(lengthSquared) || lengthSquared < 1.0e-20f)
                return fallback;
            return Quaternion.Normalize(value);
        }

        private static bool HasAnyFiniteValue(float[] values)
        {
            foreach (float value in values)
                if (IsFinite(value))
                    return true;
            return false;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static List<float[]> BuildConstantPools(CHPR.CharacterInfo characterInfo)
        {
            var pools = new List<float[]>();
            foreach (CHPR.SConstPool pool in characterInfo.ConstPools)
            {
                // Unknown1 is the index read from the SConstPool record and is the
                // index consumed by the stream table's constant-pool field.
                if (pool.Unknown1 > 0xFFF)
                    continue;

                while (pools.Count <= pool.Unknown1)
                    pools.Add(null);
                pools[pool.Unknown1] = pool.Unknowns2;
            }
            return pools;
        }
    }
}
