using IONET.Core;
using IONET.Core.Animation;
using RetroStudioPlugin.Files.FileData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MetroidPrimeRemasterModelDumper.Tools;

/// <summary>
/// Builds IONET animation groups/tracks from sampled Prime 4 CHPR poses.
/// The decoder emits bind-relative transforms keyed by the game's packed pose addresses;
/// this class applies those deltas to local bind TRS and writes tracks against bone names.
/// </summary>
public static class CHPRAnimationExporter
{
    private const float DefaultFramesPerSecond = 60f;
    private const float ScalarChangeTolerance = 1e-5f;
    private const float ScalarReductionTolerance = 1e-4f;
    private const float RotationReductionTolerance = 2e-5f;

    private sealed record BindPose(Vector3 Translation, Quaternion Rotation, Vector3 Scale);

    private sealed class BoneSamples
    {
        public required CHPRAnimationSkeleton.Joint Joint { get; init; }
        public required string BoneName { get; init; }
        public required BindPose Bind { get; init; }
        public List<Vector3> Translations { get; } = new();
        public List<Quaternion> Rotations { get; } = new();
        public List<Vector3> Scales { get; } = new();
    }

    public sealed record ExportReport(
        int ClipsConsidered,
        int ClipsDecoded,
        int ClipsExported,
        int TracksExported,
        int SamplesDecoded,
        int FailedClips,
        IReadOnlyList<string> Diagnostics);

    /// <summary>
    /// Appends all decodable clips to the scene. Channels that remain constant are omitted, and
    /// redundant scalar/quaternion keys are reduced before IONET tracks are created.
    /// </summary>
    public static ExportReport AddAnimations(
        IOScene scene,
        CHPR characterProject,
        CHPR.CharacterInfo character,
        CHPRAnimationInfo.Catalog catalog,
        CHPRAnimationSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(characterProject);
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(skeleton);

        var diagnostics = new List<string>();
        int clipsConsidered = catalog.Clips.Count(clip => clip.HasPayload && clip.FrameCount > 1);
        int clipsDecoded = 0;
        int clipsExported = 0;
        int tracksExported = 0;
        int samplesDecoded = 0;
        int failedClips = 0;

        var boneByName = new Dictionary<string, CHPR.BoneData>(StringComparer.Ordinal);
        foreach (CHPR.BoneData bone in character.Bones)
        {
            if (!string.IsNullOrWhiteSpace(bone.Name))
                boneByName.TryAdd(bone.Name, bone);
        }

        var animationTargets = new List<(CHPRAnimationSkeleton.Joint Joint, CHPR.BoneData Bone, BindPose Bind)>();
        foreach (CHPRAnimationSkeleton.Joint joint in skeleton.Joints)
        {
            if (joint.PoseAddress < 0 || !boneByName.TryGetValue(joint.Name, out CHPR.BoneData? bone))
                continue;

            BindPose bind = GetBindPose(joint, bone);
            animationTargets.Add((joint, bone, bind));
        }

        if (animationTargets.Count == 0)
        {
            diagnostics.Add("No animation joints could be paired with the character's existing bones.");
            return new ExportReport(clipsConsidered, 0, 0, 0, 0, 0, diagnostics);
        }

        foreach (CHPRAnimationInfo.ClipInfo clip in catalog.Clips)
        {
            if (clip.Payload is null || clip.FrameCount <= 1)
                continue;

            try
            {
                CHPRAnimationDecoder? decoder = catalog.Decode(clip);
                if (decoder is null)
                    continue;

                int frameCount = Math.Min(clip.FrameCount, decoder.Frames);
                if (frameCount <= 1)
                    continue;

                float fps = DefaultFramesPerSecond * (decoder.Rate > 0 ? decoder.Rate : 1f);
                CHPRAnimationDecoder.Player player = decoder.CreatePlayer();

                var samples = animationTargets.Select(target => new BoneSamples
                {
                    Joint = target.Joint,
                    BoneName = target.Bone.Name,
                    Bind = target.Bind
                }).ToList();

                for (int frame = 0; frame < frameCount; frame++)
                {
                    CHPRAnimationDecoder.Pose pose = player.Sample(frame);
                    samplesDecoded++;

                    for (int i = 0; i < samples.Count; i++)
                    {
                        BoneSamples target = samples[i];
                        PoseAtFrame value = SampleBone(target.Joint, target.Bind, pose);
                        target.Translations.Add(value.Translation);

                        // q and -q encode the same rotation, but a sign flip between consecutive
                        // samples produces avoidable component-track discontinuities.
                        Quaternion rotation = value.Rotation;
                        if (target.Rotations.Count > 0 &&
                            Quaternion.Dot(target.Rotations[^1], rotation) < 0)
                        {
                            rotation = Negate(rotation);
                        }
                        target.Rotations.Add(rotation);
                        target.Scales.Add(value.Scale);
                    }
                }

                clipsDecoded++;
                IOAnimation animation = BuildAnimation(clip.Name, frameCount, fps, samples, out int trackCount);
                if (animation.Groups.Count == 0 || trackCount == 0)
                    continue;

                scene.Animations.Add(animation);
                clipsExported++;
                tracksExported += trackCount;
            }
            catch (Exception ex)
            {
                failedClips++;
                if (diagnostics.Count < 12)
                    diagnostics.Add($"Clip '{clip.Name}' (index {clip.Index}) failed: {ex.Message}");
            }
        }

        return new ExportReport(
            clipsConsidered,
            clipsDecoded,
            clipsExported,
            tracksExported,
            samplesDecoded,
            failedClips,
            diagnostics);
    }

    private readonly record struct PoseAtFrame(Vector3 Translation, Quaternion Rotation, Vector3 Scale);

    private static BindPose GetBindPose(CHPRAnimationSkeleton.Joint joint, CHPR.BoneData bone)
    {
        if (joint.HasBindTransform)
            return new BindPose(joint.BindTranslation, Normalize(joint.BindRotation), joint.BindScale);

        if (Matrix4x4.Decompose(bone.LocalTransform, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
            return new BindPose(translation, Normalize(rotation), scale);

        return new BindPose(Vector3.Zero, Quaternion.Identity, Vector3.One);
    }

    /// <summary>
    /// Game's op-2/op-3 transform outputs are bind-relative: clip rotation * bind rotation,
    /// bind translation + clip offset, and (for op-2) sampled scale.
    /// Decoder rotation storage is W,X,Y,Z; System.Numerics uses X,Y,Z,W.
    /// </summary>
    private static PoseAtFrame SampleBone(
        CHPRAnimationSkeleton.Joint joint,
        BindPose bind,
        CHPRAnimationDecoder.Pose pose)
    {
        if (!pose.Transforms.TryGetValue(joint.PoseAddress, out float[]? transform) ||
            (transform.Length != 7 && transform.Length != 10))
        {
            return new PoseAtFrame(bind.Translation, bind.Rotation, bind.Scale);
        }

        Quaternion rotation = bind.Rotation;
        if (transform.Length >= 4 &&
            float.IsFinite(transform[0]) &&
            float.IsFinite(transform[1]) &&
            float.IsFinite(transform[2]) &&
            float.IsFinite(transform[3]))
        {
            Quaternion delta = new Quaternion(transform[1], transform[2], transform[3], transform[0]);
            float lengthSquared = delta.LengthSquared();
            if (lengthSquared > 1e-12f && float.IsFinite(lengthSquared))
                rotation = Normalize(delta) * bind.Rotation;
        }
        rotation = Normalize(rotation);

        int translationOffset = transform.Length == 10 ? 7 : 4;
        Vector3 translationOffsetValue = new Vector3(
            FiniteOr(transform[translationOffset], 0),
            FiniteOr(transform[translationOffset + 1], 0),
            FiniteOr(transform[translationOffset + 2], 0));

        Vector3 translation = bind.Translation + translationOffsetValue;
        Vector3 scale = bind.Scale;
        if (transform.Length == 10)
        {
            scale = new Vector3(
                FiniteOr(transform[4], bind.Scale.X),
                FiniteOr(transform[5], bind.Scale.Y),
                FiniteOr(transform[6], bind.Scale.Z));
        }

        return new PoseAtFrame(translation, rotation, scale);
    }

    private static IOAnimation BuildAnimation(
        string name,
        int frameCount,
        float fps,
        IReadOnlyList<BoneSamples> samples,
        out int trackCount)
    {
        var animation = new IOAnimation
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Animation" : name,
            StartFrame = 0,
            EndFrame = frameCount - 1
        };

        trackCount = 0;
        foreach (BoneSamples sample in samples)
        {
            bool rotationChanges = RotationChanges(sample.Rotations);
            bool translationXChanges = ScalarChanges(sample.Translations.Select(v => v.X).ToList());
            bool translationYChanges = ScalarChanges(sample.Translations.Select(v => v.Y).ToList());
            bool translationZChanges = ScalarChanges(sample.Translations.Select(v => v.Z).ToList());
            bool scaleXChanges = ScalarChanges(sample.Scales.Select(v => v.X).ToList());
            bool scaleYChanges = ScalarChanges(sample.Scales.Select(v => v.Y).ToList());
            bool scaleZChanges = ScalarChanges(sample.Scales.Select(v => v.Z).ToList());

            if (!rotationChanges &&
                !translationXChanges && !translationYChanges && !translationZChanges &&
                !scaleXChanges && !scaleYChanges && !scaleZChanges)
                continue;

            var group = new IOAnimation
            {
                Name = sample.BoneName,
                StartFrame = 0,
                EndFrame = frameCount - 1
            };

            if (rotationChanges)
            {
                List<int> keys = SimplifyRotations(sample.Rotations, RotationReductionTolerance);
                AddQuaternionTrack(group, IOAnimationTrackType.QuatX, sample.Rotations, keys, 0, fps);
                AddQuaternionTrack(group, IOAnimationTrackType.QuatY, sample.Rotations, keys, 1, fps);
                AddQuaternionTrack(group, IOAnimationTrackType.QuatZ, sample.Rotations, keys, 2, fps);
                AddQuaternionTrack(group, IOAnimationTrackType.QuatW, sample.Rotations, keys, 3, fps);
                trackCount += 4;
            }

            if (translationXChanges)
            {
                AddScalarTrack(group, IOAnimationTrackType.PositionX,
                    sample.Translations.Select(v => v.X).ToList(), fps);
                trackCount++;
            }
            if (translationYChanges)
            {
                AddScalarTrack(group, IOAnimationTrackType.PositionY,
                    sample.Translations.Select(v => v.Y).ToList(), fps);
                trackCount++;
            }
            if (translationZChanges)
            {
                AddScalarTrack(group, IOAnimationTrackType.PositionZ,
                    sample.Translations.Select(v => v.Z).ToList(), fps);
                trackCount++;
            }

            if (scaleXChanges)
            {
                AddScalarTrack(group, IOAnimationTrackType.ScaleX, sample.Scales.Select(v => v.X).ToList(), fps);
                trackCount++;
            }
            if (scaleYChanges)
            {
                AddScalarTrack(group, IOAnimationTrackType.ScaleY, sample.Scales.Select(v => v.Y).ToList(), fps);
                trackCount++;
            }
            if (scaleZChanges)
            {
                AddScalarTrack(group, IOAnimationTrackType.ScaleZ, sample.Scales.Select(v => v.Z).ToList(), fps);
                trackCount++;
            }

            if (group.Tracks.Count > 0)
                animation.Groups.Add(group);
        }

        return animation;
    }

    private static void AddQuaternionTrack(
        IOAnimation group,
        IOAnimationTrackType type,
        IReadOnlyList<Quaternion> values,
        IReadOnlyList<int> keyFrames,
        int component,
        float fps)
    {
        var track = new IOAnimationTrack { ChannelType = type };
        foreach (int frame in keyFrames)
        {
            Quaternion q = values[frame];
            float value = component switch
            {
                0 => q.X,
                1 => q.Y,
                2 => q.Z,
                _ => q.W
            };

            track.KeyFrames.Add(new IOKeyFrame
            {
                Frame = frame,
                Time = frame / fps,
                Value = value
            });
        }

        group.Tracks.Add(track);
    }

    private static void AddScalarTrack(
        IOAnimation group,
        IOAnimationTrackType type,
        IReadOnlyList<float> values,
        float fps)
    {
        List<int> frames = SimplifyScalar(values, ScalarReductionTolerance);
        var track = new IOAnimationTrack { ChannelType = type };
        foreach (int frame in frames)
        {
            track.KeyFrames.Add(new IOKeyFrame
            {
                Frame = frame,
                Time = frame / fps,
                Value = values[frame]
            });
        }

        group.Tracks.Add(track);
    }

    private static bool ScalarChanges(IReadOnlyList<float> values)
    {
        if (values.Count < 2)
            return false;

        float first = values[0];
        return values.Any(value => !float.IsFinite(value) || Math.Abs(value - first) > ScalarChangeTolerance);
    }

    private static bool RotationChanges(IReadOnlyList<Quaternion> values)
    {
        if (values.Count < 2)
            return false;

        Quaternion first = Normalize(values[0]);
        for (int i = 1; i < values.Count; i++)
        {
            Quaternion current = Normalize(values[i]);
            float dot = Math.Abs(Quaternion.Dot(first, current));
            if (!float.IsFinite(dot) || 1f - Math.Min(1f, dot) > ScalarChangeTolerance)
                return true;
        }

        return false;
    }

    private static List<int> SimplifyScalar(IReadOnlyList<float> values, float tolerance)
    {
        if (values.Count <= 2)
            return Enumerable.Range(0, values.Count).ToList();

        var keep = new bool[values.Count];
        keep[0] = keep[^1] = true;
        SimplifyScalarSegment(values, 0, values.Count - 1, tolerance, keep);
        return Enumerable.Range(0, keep.Length).Where(i => keep[i]).ToList();
    }

    private static void SimplifyScalarSegment(
        IReadOnlyList<float> values,
        int start,
        int end,
        float tolerance,
        bool[] keep)
    {
        // Use an explicit stack instead of recursion: animation frame counts are 16-bit and
        // a noisy track can require splitting at almost every frame.
        var pending = new Stack<(int Start, int End)>();
        pending.Push((start, end));

        while (pending.Count > 0)
        {
            (int segmentStart, int segmentEnd) = pending.Pop();
            if (segmentEnd <= segmentStart + 1)
                continue;

            float a = values[segmentStart];
            float b = values[segmentEnd];
            float maxError = -1;
            int maxIndex = -1;

            for (int i = segmentStart + 1; i < segmentEnd; i++)
            {
                float t = (float)(i - segmentStart) / (segmentEnd - segmentStart);
                float expected = a + (b - a) * t;
                float error = Math.Abs(values[i] - expected);
                if (!float.IsFinite(error))
                    error = float.PositiveInfinity;

                if (error > maxError)
                {
                    maxError = error;
                    maxIndex = i;
                }
            }

            if (maxError > tolerance && maxIndex > segmentStart)
            {
                keep[maxIndex] = true;
                pending.Push((segmentStart, maxIndex));
                pending.Push((maxIndex, segmentEnd));
            }
        }
    }

    private static List<int> SimplifyRotations(IReadOnlyList<Quaternion> values, float tolerance)
    {
        if (values.Count <= 2)
            return Enumerable.Range(0, values.Count).ToList();

        var keep = new bool[values.Count];
        keep[0] = keep[^1] = true;
        SimplifyRotationSegment(values, 0, values.Count - 1, tolerance, keep);
        return Enumerable.Range(0, keep.Length).Where(i => keep[i]).ToList();
    }

    private static void SimplifyRotationSegment(
        IReadOnlyList<Quaternion> values,
        int start,
        int end,
        float tolerance,
        bool[] keep)
    {
        var pending = new Stack<(int Start, int End)>();
        pending.Push((start, end));

        while (pending.Count > 0)
        {
            (int segmentStart, int segmentEnd) = pending.Pop();
            if (segmentEnd <= segmentStart + 1)
                continue;

            Quaternion a = Normalize(values[segmentStart]);
            Quaternion b = Normalize(values[segmentEnd]);
            if (Quaternion.Dot(a, b) < 0)
                b = Negate(b);

            float maxError = -1;
            int maxIndex = -1;
            for (int i = segmentStart + 1; i < segmentEnd; i++)
            {
                float t = (float)(i - segmentStart) / (segmentEnd - segmentStart);
                Quaternion expected = Normalize(Quaternion.Slerp(a, b, t));
                Quaternion actual = Normalize(values[i]);
                float dot = Math.Abs(Quaternion.Dot(expected, actual));
                float error = !float.IsFinite(dot) ? float.PositiveInfinity : 1 - Math.Min(1, dot);

                if (error > maxError)
                {
                    maxError = error;
                    maxIndex = i;
                }
            }

            if (maxError > tolerance && maxIndex > segmentStart)
            {
                keep[maxIndex] = true;
                pending.Push((segmentStart, maxIndex));
                pending.Push((maxIndex, segmentEnd));
            }
        }
    }

    private static Quaternion Normalize(Quaternion value)
    {
        float lengthSquared = value.LengthSquared();
        return float.IsFinite(lengthSquared) && lengthSquared > 1e-12f
            ? Quaternion.Normalize(value)
            : Quaternion.Identity;
    }

    private static Quaternion Negate(Quaternion value) =>
        new(-value.X, -value.Y, -value.Z, -value.W);

    private static float FiniteOr(float value, float fallback) =>
        float.IsFinite(value) ? value : fallback;
}
