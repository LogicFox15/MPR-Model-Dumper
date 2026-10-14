using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace RetroStudioPlugin.Files.FileData;

/// <summary>
/// Reads the CHPR animation node table, hierarchy, pose addresses, and local bind-transform table.
/// Slot-major transform rows are calibrated against known skinned-bone world transforms when
/// available, then exposed for animation sampling and export.
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
        public int ResolvedTransformIndex { get; internal set; } = -1;

        public bool HasBindTransform { get; internal set; }
        public Vector3 BindTranslation { get; internal set; }
        public Quaternion BindRotation { get; internal set; } = Quaternion.Identity;
        public Vector3 BindScale { get; internal set; } = Vector3.One;
    }

    public sealed record PoseMapping(
        IReadOnlyDictionary<int, int> BoneIndexToJointIndex,
        int TransformCount,
        int MappedTransforms,
        int UnmappedTransforms,
        int MatchedJoints);

    public sealed record BindPoseReport(
        int TransformRows,
        int ResolvedJointTransforms,
        int CalibratedSlots,
        int UpdatedCharacterBones);

    private readonly List<Joint> _joints = new();
    private readonly Dictionary<int, int> _jointByPoseAddress = new();
    private readonly List<string> _diagnostics = new();
    private float[] _transforms = Array.Empty<float>();

    public IReadOnlyList<Joint> Joints => _joints;
    public IReadOnlyDictionary<int, int> JointByPoseAddress => _jointByPoseAddress;
    public IReadOnlyList<string> Diagnostics => _diagnostics;

    private CHPRAnimationSkeleton() { }

    public static CHPRAnimationSkeleton Read(byte[] decompressedChpr)
    {
        ArgumentNullException.ThrowIfNull(decompressedChpr);

        var result = new CHPRAnimationSkeleton();
        List<string> strings = CHPRAnimationInfo.ReadStrings(decompressedChpr);
        if (strings.Count == 0)
        {
            result._diagnostics.Add("The CHPR animation string table was not found.");
            return result;
        }

        int stringTableEnd = FindStringTableEnd(decompressedChpr);
        if (stringTableEnd < 0 || stringTableEnd > decompressedChpr.Length)
        {
            result._diagnostics.Add(
                $"The animation string table was read ({strings.Count} strings), but its end offset could not be validated.");
            return result;
        }

        result._diagnostics.Add(
            $"String table: {strings.Count} strings, end offset 0x{stringTableEnd:X}.");

        List<string> nodes = ReadNodeNames(decompressedChpr, stringTableEnd, strings);
        if (nodes.Count == 0)
        {
            result._diagnostics.Add(
                "No node-name hash/index array beginning with 'root.anchorBlend' was found after the string table.");
            return result;
        }

        result._diagnostics.Add($"Node-name array: {nodes.Count} names, first='{nodes[0]}'.");

        result.CreateJointList(decompressedChpr, stringTableEnd, nodes);
        if (result._joints.Count == 0)
            result._diagnostics.Add("The node-name array was found, but no joint entries were found in the node table.");

        result.ReadHierarchy(decompressedChpr, stringTableEnd);
        result._transforms = ReadTransforms(decompressedChpr, stringTableEnd);
        result._diagnostics.Add($"Local-transform table: {result._transforms.Length / 7} validated rows.");
        int addressed = result._joints.Count(joint => joint.PoseAddress >= 0);
        result._diagnostics.Add($"Hierarchy result: {result._joints.Count} joints, {addressed} pose addresses.");

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

        // Return a direct bone-index -> animation-joint-index map. The map includes matched
        // skeleton joints even when a particular sample omits their transform (held values may
        // be implicit); MappedTransforms counts only addresses present in this sampled pose.
        return new PoseMapping(
            jointByBone,
            pose.Transforms.Count,
            transformToBone.Count,
            pose.Transforms.Count - transformToBone.Count,
            matchedJoints);
    }

    /// <summary>
    /// Resolves the raw seven-float local transform table to joints. Type-04 records store their
    /// transform as a slot/row pair; slot offsets are calibrated against known skinned-bone world
    /// matrices, matching Mocktroid's assignLocalIndices strategy. Joints without a resolved
    /// table row retain the current CHPR bind transform as a fallback.
    /// </summary>
    public BindPoseReport ReconstructBindTransforms(CHPR.CharacterInfo character)
    {
        ArgumentNullException.ThrowIfNull(character);

        var boneByName = new Dictionary<string, CHPR.BoneData>(StringComparer.Ordinal);
        foreach (CHPR.BoneData bone in character.Bones)
        {
            if (!string.IsNullOrWhiteSpace(bone.Name))
                boneByName.TryAdd(bone.Name, bone);
        }

        // Only use matrices actually read from a render context. A bone name may appear in a
        // skin palette even if its matrix was missing/invalid; in that case the world transform
        // may be an estimate and must not be used to calibrate the animation table.
        var knownWorld = new Dictionary<int, double[]>();
        foreach (Joint joint in _joints)
        {
            if (boneByName.TryGetValue(joint.Name, out CHPR.BoneData? bone) &&
                bone.HasExactWorldTransform)
            {
                knownWorld[joint.Index] = ToColumnMatrix(bone.WorldTransform);
            }
        }

        var votes = new Dictionary<int, int>[64];
        for (int i = 0; i < votes.Length; i++)
            votes[i] = new Dictionary<int, int>();

        int transformCount = _transforms.Length / 7;
        int calibrationPairs = 0;
        double bestCalibrationError = double.PositiveInfinity;
        string bestCalibrationPair = "none";

        foreach (Joint joint in _joints)
        {
            if (joint.TransformSlot < 0 || joint.TransformSlot >= votes.Length || joint.TransformRow < 0 ||
                !knownWorld.TryGetValue(joint.Index, out double[]? jointWorld) ||
                !knownWorld.TryGetValue(joint.ParentIndex, out double[]? parentWorld))
                continue;

            calibrationPairs++;
            double[] local = Multiply(InvertAffine(parentWorld), jointWorld);
            int bestRow = -1;
            double pairBestError = double.PositiveInfinity;
            double secondBestError = double.PositiveInfinity;

            for (int row = 0; row < transformCount; row++)
            {
                int offset = row * 7;
                double[] candidate = QuaternionMatrix(
                    _transforms[offset], _transforms[offset + 1], _transforms[offset + 2], _transforms[offset + 3],
                    _transforms[offset + 4], _transforms[offset + 5], _transforms[offset + 6]);
                double error = MaxDifference(candidate, local);

                if (error < pairBestError)
                {
                    secondBestError = pairBestError;
                    pairBestError = error;
                    bestRow = row;
                }
                else if (error < secondBestError)
                {
                    secondBestError = error;
                }
            }

            if (pairBestError < bestCalibrationError)
            {
                bestCalibrationError = pairBestError;
                bestCalibrationPair = joint.Name;
            }

            // Do not let duplicate/identity rows establish a slot base. The best row must be
            // both close to the observed local matrix and meaningfully better than the runner-up.
            bool uniqueMatch =
                pairBestError < 1e-3 &&
                (double.IsPositiveInfinity(secondBestError) || secondBestError - pairBestError > 1e-5);

            if (uniqueMatch && bestRow >= 0)
            {
                int baseIndex = bestRow - joint.TransformRow;
                votes[joint.TransformSlot].TryGetValue(baseIndex, out int count);
                votes[joint.TransformSlot][baseIndex] = count + 1;
            }
        }

        int[] slotBase = Enumerable.Repeat(-1, 64).ToArray();
        bool[] calibrated = new bool[64];
        for (int slot = 0; slot < 64; slot++)
        {
            int bestValue = 0;
            int bestVotes = 0;
            bool tied = false;
            foreach (var vote in votes[slot])
            {
                if (vote.Value > bestVotes)
                {
                    bestValue = vote.Key;
                    bestVotes = vote.Value;
                    tied = false;
                }
                else if (vote.Value == bestVotes && vote.Value > 0)
                {
                    tied = true;
                }
            }

            bool hasWinner = bestVotes > 0 && !tied && bestValue >= 0 && bestValue < transformCount;
            // Critical: never fall back to sequential row offsets. A slot with no calibration
            // evidence is unknown, not the next contiguous run of transform rows.
            slotBase[slot] = hasWinner ? bestValue : -1;
            calibrated[slot] = hasWinner;
        }

        int calibratedSlots = calibrated.Count(value => value);
        foreach (Joint joint in _joints)
        {
            if (joint.TransformSlot >= 0 && joint.TransformSlot < 64 &&
                joint.TransformRow >= 0 && calibrated[joint.TransformSlot])
                joint.ResolvedTransformIndex = slotBase[joint.TransformSlot] + joint.TransformRow;
            else if (joint.LocalTransformIndex >= 0)
                joint.ResolvedTransformIndex = joint.LocalTransformIndex;
            else
                joint.ResolvedTransformIndex = -1;
        }

        _diagnostics.Add(
            $"Transform-slot calibration evidence: {calibrationPairs} skinned parent-child pairs tested; " +
            $"best matrix error {(double.IsFinite(bestCalibrationError) ? bestCalibrationError.ToString("G5") : "n/a")} " +
            $"at joint '{bestCalibrationPair}'.");

        int resolved = 0;
        int updatedBones = 0;
        foreach (Joint joint in _joints)
        {
            int index = joint.ResolvedTransformIndex;
            if (index < 0 || index >= transformCount)
                continue;

            int offset = index * 7;
            float qw = _transforms[offset];
            float qx = _transforms[offset + 1];
            float qy = _transforms[offset + 2];
            float qz = _transforms[offset + 3];
            float length = MathF.Sqrt(qw * qw + qx * qx + qy * qy + qz * qz);
            if (!float.IsFinite(length) || length < 1e-6f)
                continue;

            joint.BindRotation = Quaternion.Normalize(new Quaternion(qx / length, qy / length, qz / length, qw / length));
            joint.BindTranslation = new Vector3(
                _transforms[offset + 4],
                _transforms[offset + 5],
                _transforms[offset + 6]);
            joint.BindScale = Vector3.One;
            joint.HasBindTransform = true;
            resolved++;

            if (boneByName.TryGetValue(joint.Name, out CHPR.BoneData? bone))
            {
                bone.LocalTransform =
                    Matrix4x4.CreateScale(joint.BindScale) *
                    Matrix4x4.CreateFromQuaternion(joint.BindRotation) *
                    Matrix4x4.CreateTranslation(joint.BindTranslation);
                updatedBones++;
            }
        }

        _diagnostics.Add(
            $"Bind reconstruction: {transformCount} transform rows, {resolved} joint locals resolved, " +
            $"{calibratedSlots} transform slots calibrated, {updatedBones} existing bones updated.");

        return new BindPoseReport(transformCount, resolved, calibratedSlots, updatedBones);
    }

    private static float[] ReadTransforms(byte[] data, int stringTableEnd)
    {
        int bestCount = 0;
        int bestOffset = 0;
        int offset = stringTableEnd;

        while (offset < data.Length - 28 * 8)
        {
            int count = 0;
            while (offset + 28L * (count + 1) <= data.Length && IsValidTransform(data, offset + 28 * count))
                count++;

            if (count > bestCount)
            {
                bestCount = count;
                bestOffset = offset;
            }

            offset += count >= 8 ? 28 * count : 1;
        }

        if (bestCount == 0)
            return Array.Empty<float>();

        var rows = new List<float[]>(bestCount);
        for (int i = 0; i < bestCount; i++)
            rows.Add(ReadTransformRow(data, bestOffset + 28 * i));

        int end = bestOffset + 28 * bestCount;
        // Larger rigs sometimes continue after a short run of non-transform bytes.
        while (true)
        {
            int next = -1;
            int scanEnd = (int)Math.Min(data.Length - 28L * 8, end + 28L);
            for (int candidate = end; candidate < scanEnd; candidate++)
            {
                int run = 0;
                while (run < 8 && candidate + 28L * run <= data.Length - 28 &&
                       IsValidTransform(data, candidate + 28 * run))
                    run++;

                if (run == 8)
                {
                    next = candidate;
                    break;
                }
            }

            if (next < 0)
                break;

            int appended = 0;
            while (next + 28L * (appended + 1) <= data.Length &&
                   IsValidTransform(data, next + 28 * appended))
            {
                rows.Add(ReadTransformRow(data, next + 28 * appended));
                appended++;
            }

            if (appended == 0)
                break;
            end = next + 28 * appended;
        }

        return rows.SelectMany(row => row).ToArray();
    }

    private static bool IsValidTransform(byte[] data, int offset)
    {
        if (offset < 0 || offset > data.Length - 28)
            return false;

        double quaternionLength = 0;
        double translationMax = 0;
        for (int i = 0; i < 7; i++)
        {
            float value = F32(data, offset + i * 4);
            if (!float.IsFinite(value))
                return false;

            if (i < 4)
                quaternionLength += value * value;
            else
                translationMax = Math.Max(translationMax, Math.Abs(value));
        }

        return Math.Abs(quaternionLength - 1) <= 2e-3 && translationMax <= 1e3;
    }

    private static float[] ReadTransformRow(byte[] data, int offset)
    {
        var row = new float[7];
        for (int i = 0; i < row.Length; i++)
            row[i] = F32(data, offset + i * 4);
        return row;
    }

    private static double[] ToColumnMatrix(Matrix4x4 rowVectorMatrix) => new[]
    {
        (double)rowVectorMatrix.M11, rowVectorMatrix.M21, rowVectorMatrix.M31, rowVectorMatrix.M41,
        (double)rowVectorMatrix.M12, rowVectorMatrix.M22, rowVectorMatrix.M32, rowVectorMatrix.M42,
        (double)rowVectorMatrix.M13, rowVectorMatrix.M23, rowVectorMatrix.M33, rowVectorMatrix.M43,
        (double)rowVectorMatrix.M14, rowVectorMatrix.M24, rowVectorMatrix.M34, rowVectorMatrix.M44
    };

    private static double[] QuaternionMatrix(double w, double x, double y, double z, double tx, double ty, double tz) =>
        new[]
        {
            1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), tx,
            2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w), ty,
            2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y), tz,
            0, 0, 0, 1
        };

    private static double[] Multiply(double[] a, double[] b)
    {
        var result = new double[16];
        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                for (int k = 0; k < 4; k++)
                    result[row * 4 + column] += a[row * 4 + k] * b[k * 4 + column];
        return result;
    }

    private static double[] InvertAffine(double[] m)
    {
        double a = m[0], b = m[1], c = m[2];
        double d = m[4], e = m[5], f = m[6];
        double g = m[8], h = m[9], i = m[10];
        double determinant =
            a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);

        if (Math.Abs(determinant) < 1e-12)
            return IdentityMatrix();

        var result = new double[16];
        result[0] = (e * i - f * h) / determinant;
        result[1] = (c * h - b * i) / determinant;
        result[2] = (b * f - c * e) / determinant;
        result[4] = (f * g - d * i) / determinant;
        result[5] = (a * i - c * g) / determinant;
        result[6] = (c * d - a * f) / determinant;
        result[8] = (d * h - e * g) / determinant;
        result[9] = (b * g - a * h) / determinant;
        result[10] = (a * e - b * d) / determinant;

        for (int row = 0; row < 3; row++)
            result[row * 4 + 3] = -(
                result[row * 4] * m[3] +
                result[row * 4 + 1] * m[7] +
                result[row * 4 + 2] * m[11]);

        result[15] = 1;
        return result;
    }

    private static double[] IdentityMatrix() =>
        new[] { 1d, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

    private static double MaxDifference(double[] a, double[] b)
    {
        double max = 0;
        for (int i = 0; i < 16; i++)
            max = Math.Max(max, Math.Abs(a[i] - b[i]));
        return max;
    }

    private static float F32(byte[] data, int offset) =>
        BitConverter.Int32BitsToSingle(I32(data, offset));

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
        // Match CHPRAnimationInfo.ReadStrings: accept the common header only if the candidate
        // table actually begins with "Primary". A plausible but false length can otherwise
        // send the node-table scanner to the wrong offset.
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
            {
                List<string> strings = ReadStringTable(data, header + 4, (int)stringSize);
                if (strings.Count > 0 && strings[0] == "Primary")
                    return header + 4 + (int)stringSize;
            }
        }

        // Large/multi-segment CHPRs can require a content-based fallback. Reject occurrences of
        // "Primary" that don't have a valid length-prefixed string table behind them.
        byte[] needle = Encoding.ASCII.GetBytes("Primary\0");
        for (int at = IndexOf(data, needle, 4); at >= 4; at = IndexOf(data, needle, at + 1))
        {
            uint stringSize = U32(data, at - 4);
            if (stringSize > 0 && stringSize <= int.MaxValue &&
                (ulong)at + stringSize <= (ulong)data.Length)
            {
                List<string> strings = ReadStringTable(data, at, (int)stringSize);
                if (strings.Count > 0 && strings[0] == "Primary")
                    return at + (int)stringSize;
            }
        }

        return -1;
    }

    private static List<string> ReadStringTable(byte[] data, int offset, int size)
    {
        if (offset < 0 || size <= 0 || offset > data.Length - size ||
            data[offset + size - 1] != 0)
            return new List<string>();

        return Encoding.Latin1.GetString(data, offset, size - 1).Split('\0').ToList();
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
