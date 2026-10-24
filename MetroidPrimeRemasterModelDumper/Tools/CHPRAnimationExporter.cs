using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
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

            CHPR.CharacterInfo characterInfo = chpr.CharacterInfos[0];

            // Pose addresses are assigned once during CHPR parsing. Export consumes the
            // parsed mapping instead of attempting to reconstruct it a second time.
            var bonesWithAddresses = characterInfo.Bones
                .Where(b => b.PoseAddress >= 0)
                .ToList();

            if (bonesWithAddresses.Count == 0)
            {
                report = $"No CHPR bones received a pose address during parsing. " +
                    $"CharacterInfo bones={characterInfo.Bones.Count}; leaving glTF unchanged.";
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
                report = $"Could not reopen exported glTF to add animation: {ex.GetType().Name}: {ex.Message}";
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

            if (nodeMatches == 0)
            {
                report = $"Found {bonesWithAddresses.Count} mapped CHPR bones, but none matched a node name in the glTF.";
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

                try
                {
                    // Sampling bytecode has a fixed destination layout for a clip. Use
                    // frame zero to find only the addresses this clip actually emits,
                    // rather than checking every mapped bone on every frame.
                    CHPRCompStreamAnimation.Pose firstPose = player.Sample(0.0f, constantPools);
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
                        clipsSkipped++;
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
                    Console.WriteLine($"[CHPR] Clip {entryIndex + 1}/{chpr.animations.Count} " +
                        $"(slot={entry.OutputSlot}): baked successfully in {clipTimer.Elapsed.TotalSeconds:0.00}s.");
                }
                catch (Exception ex)
                {
                    clipsSkipped++;
                    string warning = $"entry {entryIndex}, slot {entry.OutputSlot}: {ex.GetType().Name}: {ex.Message}";
                    warnings.Add(warning);
                    Console.WriteLine($"[CHPR] Clip {entryIndex + 1}/{chpr.animations.Count} " +
                        $"(slot={entry.OutputSlot}) failed after {clipTimer.Elapsed.TotalSeconds:0.00}s: " +
                        $"{ex.GetType().Name}: {ex.Message}");
                }
            }

            if (animationsAdded == 0)
            {
                report = $"No CompStream clips produced mapped glTF channels. Matched skeleton nodes={nodeMatches}; " +
                    $"CompStream clips={chpr.animations.Count(a => a.CompStreamClip != null)}; skipped={clipsSkipped}.";
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
                report = $"Animation channels were built, but glTF save failed: {ex.GetType().Name}: {ex.Message}";
                return false;
            }

            report = $"Added {animationsAdded} animation(s), {channelsAdded} channels, and matched {nodeMatches} bone nodes " +
                $"at {GameFramesPerSecond:0} FPS; skipped {clipsSkipped} clips; " +
                $"elapsed={totalTimer.Elapsed.TotalSeconds:0.00}s.";
            if (warnings.Count > 0)
                report += " Warnings: " + string.Join(" | ", warnings.Take(4));
            return true;
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
