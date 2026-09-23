using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace TelleR.ConcaveCollider
{
    /// <summary>
    /// 스킨드 메시 본별 분해(EMBER 이식). 정점을 가중치가 있는 본 중 스키닝 위치에서 가장 가까운 본에 묶고
    /// (지배 가중치가 아님), 바인드 포즈 본 로컬 공간에서 그룹별 껍질을 만든다. 작은 그룹은 조상/가장 가까운
    /// 그룹 중 부피 팽창이 적은 쪽에 합치거나(빈 공간을 이으면 제거), 이름 역할(head/body/arm/leg)로 묶고,
    /// 인접 조각은 관절 평면에서 자른다. 본 공간 좌표가 극소(스케일 x130 본)여도 그룹마다 단위 크기로 정규화한다.
    /// </summary>
    internal static class SkinnedPipeline
    {
        private const double BoneChainMaxAngle = 45.0;
        private const double BoneAppendageFraction = 0.12;
        private const double BoneChainSizeRatio = 0.35;
        private const double ClipMinimumVolumeFraction = 0.05;
        private const double BoneMergeBloatLimit = 1.6;

        private sealed class BoneGroup
        {
            public int Bone;
            public List<D3> Points = new List<D3>();
            public readonly List<int> Members = new List<int>();
            public double Volume;
            public double BaseVolume;
        }

        private enum BoneRole
        {
            Unknown,
            Head,
            Body,
            Arm,
            LegUpper,
            LegLower
        }

        private static readonly string[] HeadKeywords = { "head", "neck", "jaw", "ear", "eye", "lid", "nose", "mouth", "tongue", "skull", "face", "beak" };
        private static readonly string[] LegUpperKeywords = { "thigh", "upperleg", "upper_leg", "upper leg", "upleg" };
        private static readonly string[] LegLowerKeywords = { "shin", "calf", "lowerleg", "lower_leg", "lower leg", "knee", "foot", "hook", "toe", "ankle", "heel", "leg" };
        private static readonly string[] ArmKeywords = { "upperarm", "upper_arm", "upper arm", "shoulder", "clavicle", "collar", "forearm", "lowerarm", "lower_arm", "lower arm", "elbow", "pastern", "hand", "finger", "thumb", "wrist", "wing", "arm" };
        private static readonly string[] BodyKeywords = { "spine", "pelvis", "hips", "hip", "chest", "torso", "body", "ribs", "belly", "abdomen" };
        private static readonly char[] BoneNameSeparators = { ' ', '.', '_', '-', ':', '|' };

        private struct BoneJoint
        {
            public BoneGroup Child;
            public BoneGroup Parent;
            public int ChildBone;
            public int ParentBone;
            public D3 Position;
        }

        private sealed class Context
        {
            public ConcaveBone[] Bones;
            public Matrix4x4[] LocalToWorld;
            public Matrix4x4[] WorldToLocal;
            public D3[] Positions;
            public double[] Determinant;
            public ResolvedSettings Settings;
        }

        public static void Execute(ConcaveSkinnedInput input, ResolvedSettings settings, Reporter reporter, ConcaveDecompositionResult result)
        {
            reporter.Report(0f, "스킨드 입력 확인 중");
            if (input == null || input.Bones == null || input.Bones.Length == 0)
                throw new ArgumentException("본 정보가 없습니다.");
            ConcaveBone[] bones = input.Bones;
            int boneCount = bones.Length;
            bool hasSkin = input.Vertices != null && input.BoneWeights != null && input.Bindposes != null &&
                           input.BoneWeights.Length == input.Vertices.Length && input.Bindposes.Length == boneCount;
            if (!hasSkin && input.RigidParts.Count == 0)
                throw new ArgumentException("정점·본 가중치·바인드포즈 개수가 맞지 않습니다.");

            var ctx = new Context
            {
                Bones = bones,
                LocalToWorld = new Matrix4x4[boneCount],
                WorldToLocal = new Matrix4x4[boneCount],
                Positions = new D3[boneCount],
                Determinant = new double[boneCount],
                Settings = settings
            };
            for (int b = 0; b < boneCount; b++)
            {
                Matrix4x4 l2w = bones[b] != null ? bones[b].LocalToWorld : Matrix4x4.identity;
                ctx.LocalToWorld[b] = l2w;
                ctx.WorldToLocal[b] = l2w.inverse;
                ctx.Positions[b] = new D3(l2w.m03, l2w.m13, l2w.m23);
                ctx.Determinant[b] = Math.Abs(Determinant3(l2w));
            }

            var excluded = new bool[boneCount];
            for (int b = 0; b < boneCount; b++) excluded[b] = bones[b] == null || IsExcluded(bones[b].Name, settings.ExcludedBoneKeywords);

            // 정점 → 본 그룹
            var groups = new Dictionary<int, BoneGroup>();
            int skipped = 0;
            if (hasSkin)
            {
                var skin = new Matrix4x4[boneCount];
                for (int b = 0; b < boneCount; b++) skin[b] = ctx.LocalToWorld[b] * input.Bindposes[b];
                Vector3[] vertices = input.Vertices;
                for (int v = 0; v < vertices.Length; v++)
                {
                    int bone = ChooseBone(input.BoneWeights[v], vertices[v], skin, ctx.Positions);
                    if (bone < 0 || bone >= boneCount)
                    {
                        skipped++;
                        continue;
                    }

                    if (excluded[bone]) continue;
                    GroupOf(groups, bone).Points.Add(Transform(input.Bindposes[bone], D3.From(vertices[v])));
                }
            }

            for (int r = 0; r < input.RigidParts.Count; r++)
            {
                ConcaveRigidPart part = input.RigidParts[r];
                if (part == null || part.Points == null || part.BoneIndex < 0 || part.BoneIndex >= boneCount || excluded[part.BoneIndex]) continue;
                BoneGroup group = GroupOf(groups, part.BoneIndex);
                for (int i = 0; i < part.Points.Length; i++) group.Points.Add(D3.From(part.Points[i]));
            }

            if (skipped > 0) result.Warnings.Add($"본 가중치가 없는 정점 {skipped}개를 건너뛰었습니다.");
            if (groups.Count == 0) throw new InvalidOperationException("콜라이더를 만들 본 그룹이 없습니다(모든 본이 제외되었을 수 있습니다).");

            reporter.Report(0.2f, "본 그룹 병합 중");
            var list = new List<BoneGroup>(groups.Values);
            list.Sort((a, b) => a.Bone.CompareTo(b.Bone));
            MergeSmallBoneGroups(ctx, list, groups, settings.MaxBonePieces);

            reporter.Report(0.5f, "본 조각 껍질 계산 중");
            var hulls = new Dictionary<BoneGroup, HullShape>();
            var maps = new Dictionary<BoneGroup, OutputMap>();
            var centroids = new Dictionary<BoneGroup, D3>();
            for (int i = 0; i < list.Count; i++)
            {
                BoneGroup group = list[i];
                if (group.Points.Count < 4) continue;
                OutputMap map = Normalize(group.Points, out D3[] work);
                HullShape hull = HullShape.FromPoints(work, work.Length);
                if (hull == null || hull.Volume <= 0) continue;
                hulls[group] = hull;
                maps[group] = map;
                D3 centroid = D3.Zero;
                for (int p = 0; p < group.Points.Count; p++) centroid += group.Points[p];
                centroids[group] = Transform(ctx.LocalToWorld[group.Bone], centroid / group.Points.Count);
            }

            if (settings.ClipAtJoints)
            {
                List<BoneJoint> joints = FindBoneJoints(ctx, list);
                for (int j = 0; j < joints.Count; j++)
                {
                    BoneJoint joint = joints[j];
                    if (!hulls.ContainsKey(joint.Child) || !hulls.ContainsKey(joint.Parent)) continue;
                    D3 normal = (centroids[joint.Child] - centroids[joint.Parent]).NormalizedOr(D3.Zero);
                    if (normal.SqrLength == 0) continue;
                    ClipGroupHull(ctx, hulls, maps, joint.Child, joint.Position, normal);
                    ClipGroupHull(ctx, hulls, maps, joint.Parent, joint.Position, -normal);
                }
            }

            reporter.Report(0.7f, "프리미티브 피팅 중");
            var ordered = new List<BoneGroup>();
            for (int i = 0; i < list.Count; i++)
            {
                if (hulls.ContainsKey(list[i])) ordered.Add(list[i]);
            }

            var finished = new WorkPiece[ordered.Count];
            var forced = new Frame3?[ordered.Count];
            var paddings = new double[ordered.Count];
            for (int i = 0; i < ordered.Count; i++)
            {
                BoneGroup group = ordered[i];
                forced[i] = HasNonUniformScale(ctx.LocalToWorld[group.Bone]) ? Frame3.Identity : (Frame3?)null;
                double boneScale = Math.Pow(Math.Max(ctx.Determinant[group.Bone], 1e-300), 1.0 / 3.0);
                paddings[i] = settings.Padding / boneScale / maps[group].Scale;
            }

            Parallel.For(0, ordered.Count, i =>
            {
                BoneGroup group = ordered[i];
                WorkPiece piece = PieceFinisher.Finish(hulls[group], settings, forced[i], paddings[i]);
                piece.Map = maps[group];
                piece.BoneIndex = group.Bone;
                finished[i] = piece;
            });

            // 측정(현재 포즈 월드 메시 기준)
            reporter.Report(0.88f, "커버리지 측정 중");
            if (hasSkin && input.Triangles != null && input.Triangles.Length >= 12)
                MeasureSkinned(ctx, input, finished, result);

            for (int i = 0; i < finished.Length; i++)
            {
                int boneIndex = finished[i].BoneIndex;
                ConcaveBone bone = bones[boneIndex];
                ConcavePiece piece = finished[i].ToPiece(bone != null ? bone.Name : null);
                // 본 스케일 상쇄: 조각 좌표를 월드 크기로 두고 콜라이더 localScale로 되돌린다.
                double boneScale = Math.Pow(Math.Max(ctx.Determinant[boneIndex], 1e-300), 1.0 / 3.0);
                result.Pieces.Add(piece.WithParentScaleCompensation((float)boneScale));
            }

            if (result.Pieces.Count == 0) throw new InvalidOperationException("유효한 본 조각을 만들지 못했습니다.");
            result.Watertight = true;
            result.ResolvedQuality = settings.Quality;
            result.ResolvedMaxPieces = settings.MaxBonePieces;
            result.ResolvedTolerance = (float)settings.Tolerance;
            result.ResolvedMaxHullVertices = settings.MaxHullVertices;
            result.SplitPartCount = groups.Count;
            reporter.Report(1f, "완료");
        }

        private static BoneGroup GroupOf(Dictionary<int, BoneGroup> groups, int bone)
        {
            if (groups.TryGetValue(bone, out BoneGroup group)) return group;
            group = new BoneGroup { Bone = bone };
            group.Members.Add(bone);
            groups.Add(bone, group);
            return group;
        }

        // 점군을 중심 0·최대 변 1로 정규화하고 되돌리는 변환을 준다(극소 본 좌표 대응).
        private static OutputMap Normalize(List<D3> points, out D3[] work)
        {
            D3 min = points[0], max = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                min = D3.Min(min, points[i]);
                max = D3.Max(max, points[i]);
            }

            D3 size = max - min;
            double extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (!(extent > 0)) extent = 1;
            D3 center = (min + max) * 0.5;
            work = new D3[points.Count];
            for (int i = 0; i < points.Count; i++) work[i] = (points[i] - center) / extent;
            return new OutputMap { Rotation = Frame3.Identity, Translation = center, Scale = extent };
        }

        internal static D3 Transform(Matrix4x4 m, D3 p)
        {
            return new D3(
                m.m00 * p.X + m.m01 * p.Y + m.m02 * p.Z + m.m03,
                m.m10 * p.X + m.m11 * p.Y + m.m12 * p.Z + m.m13,
                m.m20 * p.X + m.m21 * p.Y + m.m22 * p.Z + m.m23);
        }

        private static double Determinant3(Matrix4x4 m)
        {
            return (double)m.m00 * ((double)m.m11 * m.m22 - (double)m.m12 * m.m21)
                   - (double)m.m01 * ((double)m.m10 * m.m22 - (double)m.m12 * m.m20)
                   + (double)m.m02 * ((double)m.m10 * m.m21 - (double)m.m11 * m.m20);
        }

        private static bool HasNonUniformScale(Matrix4x4 m)
        {
            double x = new D3(m.m00, m.m10, m.m20).Length;
            double y = new D3(m.m01, m.m11, m.m21).Length;
            double z = new D3(m.m02, m.m12, m.m22).Length;
            double largest = Math.Max(x, Math.Max(y, z));
            double smallest = Math.Min(x, Math.Min(y, z));
            return !(smallest > 1e-12) || largest / smallest > 1.01;
        }

        /// <summary>
        /// 제외 키워드가 단어 시작 위치(이름 처음, 영문자가 아닌 문자 뒤, 대문자 시작)에 나오면 제외.
        /// "Detail"처럼 단어 중간에 든 "tail"은 제외하지 않는다.
        /// </summary>
        internal static bool IsExcluded(string name, string[] keywords)
        {
            if (string.IsNullOrEmpty(name) || keywords == null || keywords.Length == 0) return false;
            string lower = name.ToLowerInvariant();
            for (int k = 0; k < keywords.Length; k++)
            {
                string keyword = keywords[k];
                if (string.IsNullOrEmpty(keyword)) continue;
                int index = lower.IndexOf(keyword, StringComparison.Ordinal);
                while (index >= 0)
                {
                    if (index == 0 || !char.IsLetter(name[index - 1]) || char.IsUpper(name[index])) return true;
                    index = lower.IndexOf(keyword, index + 1, StringComparison.Ordinal);
                }
            }

            return false;
        }

        private static int ChooseBone(BoneWeight weight, Vector3 vertex, Matrix4x4[] skin, D3[] positions)
        {
            int i0 = weight.boneIndex0, i1 = weight.boneIndex1, i2 = weight.boneIndex2, i3 = weight.boneIndex3;
            float w0 = weight.weight0, w1 = weight.weight1, w2 = weight.weight2, w3 = weight.weight3;
            D3 v = D3.From(vertex);
            D3 skinned = D3.Zero;
            double total = 0;
            Accumulate(i0, w0);
            Accumulate(i1, w1);
            Accumulate(i2, w2);
            Accumulate(i3, w3);
            if (total <= 0) return Dominant();
            skinned /= total;

            int best = -1;
            double bestDistance = double.PositiveInfinity;
            Nearest(i0, w0);
            Nearest(i1, w1);
            Nearest(i2, w2);
            Nearest(i3, w3);
            return best >= 0 ? best : Dominant();

            void Accumulate(int index, float w)
            {
                if (w <= 0 || index < 0 || index >= skin.Length) return;
                skinned += Transform(skin[index], v) * w;
                total += w;
            }

            void Nearest(int index, float w)
            {
                if (w < 0.05f || index < 0 || index >= positions.Length) return;
                double distance = (positions[index] - skinned).SqrLength;
                if (distance >= bestDistance) return;
                bestDistance = distance;
                best = index;
            }

            int Dominant()
            {
                int index = i0;
                float bestWeight = w0;
                if (w1 > bestWeight)
                {
                    bestWeight = w1;
                    index = i1;
                }

                if (w2 > bestWeight)
                {
                    bestWeight = w2;
                    index = i2;
                }

                if (w3 > bestWeight) index = i3;
                return bestWeight > 0 || w3 > 0 ? index : -1;
            }
        }

        private static double GroupVolume(Context ctx, int bone, List<D3> points)
        {
            if (points.Count < 4) return 0;
            OutputMap map = Normalize(points, out D3[] work);
            HullShape hull = HullShape.FromPoints(work, work.Length);
            if (hull == null) return 0;
            return hull.Volume * map.Scale * map.Scale * map.Scale * ctx.Determinant[bone];
        }

        private static void MergeSmallBoneGroups(Context ctx, List<BoneGroup> list, Dictionary<int, BoneGroup> groups, int maxGroups)
        {
            bool namedRig = ctx.Settings.GroupBonesByRole && MergeBoneRoles(ctx, list);
            double total = 0;
            for (int i = 0; i < list.Count; i++)
            {
                list[i].Volume = GroupVolume(ctx, list[i].Bone, list[i].Points);
                list[i].BaseVolume = list[i].Volume;
                total += list[i].Volume;
            }

            double minimum = total * ctx.Settings.MinBoneFraction;
            int guard = 0;
            while (list.Count > 1 && guard++ < 1024)
            {
                BoneGroup smallest = null;
                double smallestVolume = double.PositiveInfinity;
                for (int i = 0; i < list.Count; i++)
                {
                    double volume = namedRig ? MirroredVolume(ctx, list[i], list) : list[i].Volume;
                    if (volume >= smallestVolume) continue;
                    smallestVolume = volume;
                    smallest = list[i];
                }

                if (smallest == null || smallestVolume >= minimum) break;
                FindBestBoneMerge(ctx, smallest, list, groups, out BoneGroup destination, out List<D3> mergedPoints, out double mergedVolume, out double bloat);
                if (destination == null || bloat > BoneMergeBloatLimit)
                {
                    list.Remove(smallest);
                    continue;
                }

                ApplyBoneMerge(destination, smallest, mergedPoints, mergedVolume, list);
            }

            if (!namedRig) MergeBoneChains(ctx, list);

            guard = 0;
            while (list.Count > maxGroups && guard++ < 1024)
            {
                BoneGroup source = null, destination = null;
                List<D3> points = null;
                double volume = 0, bestBloat = double.PositiveInfinity;
                for (int i = 0; i < list.Count; i++)
                {
                    FindBestBoneMerge(ctx, list[i], list, groups, out BoneGroup candidate, out List<D3> candidatePoints, out double candidateVolume, out double candidateBloat);
                    if (candidate == null || candidateBloat >= bestBloat) continue;
                    source = list[i];
                    destination = candidate;
                    points = candidatePoints;
                    volume = candidateVolume;
                    bestBloat = candidateBloat;
                }

                if (destination == null) break;
                ApplyBoneMerge(destination, source, points, volume, list);
            }
        }

        private static double MirroredVolume(Context ctx, BoneGroup group, List<BoneGroup> list)
        {
            BoneRole role = ClassifyBone(ctx.Bones[group.Bone].Name, out int side);
            double volume = group.Volume;
            if (role == BoneRole.Unknown || side == 0) return volume;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == group) continue;
                if (ClassifyBone(ctx.Bones[list[i].Bone].Name, out int otherSide) != role || otherSide == side || otherSide == 0) continue;
                volume = Math.Max(volume, list[i].Volume);
            }

            return volume;
        }

        private static bool MergeBoneRoles(Context ctx, List<BoneGroup> list)
        {
            var buckets = new Dictionary<string, List<BoneGroup>>();
            int recognized = 0;
            for (int i = 0; i < list.Count; i++)
            {
                BoneRole role = ClassifyBone(ctx.Bones[list[i].Bone].Name, out int side);
                if (role == BoneRole.Unknown) continue;
                recognized++;
                string key = role == BoneRole.Arm || role == BoneRole.LegUpper || role == BoneRole.LegLower ? role + "." + side : role.ToString();
                if (!buckets.TryGetValue(key, out List<BoneGroup> bucket))
                {
                    bucket = new List<BoneGroup>();
                    buckets.Add(key, bucket);
                }

                bucket.Add(list[i]);
            }

            if (recognized * 2 < list.Count) return false;

            foreach (KeyValuePair<string, List<BoneGroup>> pair in buckets)
            {
                List<BoneGroup> bucket = pair.Value;
                if (bucket.Count < 2) continue;
                BoneGroup destination = bucket[0];
                for (int i = 1; i < bucket.Count; i++)
                {
                    if (ctx.Bones[bucket[i].Bone].Depth < ctx.Bones[destination.Bone].Depth) destination = bucket[i];
                }

                for (int i = 0; i < bucket.Count; i++)
                {
                    BoneGroup source = bucket[i];
                    if (source == destination) continue;
                    Matrix4x4 toDestination = ctx.WorldToLocal[destination.Bone] * ctx.LocalToWorld[source.Bone];
                    for (int p = 0; p < source.Points.Count; p++) destination.Points.Add(Transform(toDestination, source.Points[p]));
                    destination.Members.AddRange(source.Members);
                    list.Remove(source);
                }
            }

            return true;
        }

        /// <summary>
        /// EMBER 역할 분류. "forearm"에 "ear"가 들어 있어 arm을 head보다 먼저 검사한다.
        /// 꼬리 제외는 사용자 키워드 목록(excludedBoneKeywords)에서 처리한다.
        /// </summary>
        private static BoneRole ClassifyBone(string name, out int side)
        {
            side = 0;
            if (string.IsNullOrEmpty(name)) return BoneRole.Unknown;
            string lower = name.ToLowerInvariant();
            string[] tokens = lower.Split(BoneNameSeparators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (token == "l" || token == "left" || token.StartsWith("left", StringComparison.Ordinal) || token.EndsWith("left", StringComparison.Ordinal)) side = 1;
                else if (token == "r" || token == "right" || token.StartsWith("right", StringComparison.Ordinal) || token.EndsWith("right", StringComparison.Ordinal)) side = 2;
            }

            if (ContainsAny(lower, ArmKeywords)) return BoneRole.Arm;
            if (ContainsAny(lower, LegUpperKeywords)) return BoneRole.LegUpper;
            if (ContainsAny(lower, LegLowerKeywords)) return BoneRole.LegLower;
            if (ContainsAny(lower, HeadKeywords)) return BoneRole.Head;
            if (ContainsAny(lower, BodyKeywords)) return BoneRole.Body;
            return BoneRole.Unknown;
        }

        private static bool ContainsAny(string text, string[] keywords)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                if (text.Contains(keywords[i])) return true;
            }

            return false;
        }

        private static void ApplyBoneMerge(BoneGroup destination, BoneGroup source, List<D3> mergedPoints, double mergedVolume, List<BoneGroup> list)
        {
            destination.Points = mergedPoints;
            destination.Volume = mergedVolume;
            destination.BaseVolume += source.BaseVolume;
            destination.Members.AddRange(source.Members);
            list.Remove(source);
        }

        private static List<D3> MergedPoints(Context ctx, BoneGroup destination, BoneGroup source)
        {
            Matrix4x4 toDestination = ctx.WorldToLocal[destination.Bone] * ctx.LocalToWorld[source.Bone];
            var merged = new List<D3>(destination.Points.Count + source.Points.Count);
            merged.AddRange(destination.Points);
            for (int i = 0; i < source.Points.Count; i++) merged.Add(Transform(toDestination, source.Points[i]));
            return merged;
        }

        private static void MergeBoneChains(Context ctx, List<BoneGroup> list)
        {
            bool merged = true;
            int guard = 0;
            while (merged && list.Count > 1 && guard++ < 1024)
            {
                merged = false;
                List<BoneJoint> joints = FindBoneJoints(ctx, list);
                joints.Sort((a, b) => ctx.Bones[b.ChildBone].Depth.CompareTo(ctx.Bones[a.ChildBone].Depth));
                for (int j = 0; j < joints.Count && !merged; j++)
                {
                    BoneJoint joint = joints[j];
                    BoneGroup source = joint.Child;
                    BoneGroup destination = joint.Parent;
                    double bloatLimit = BoneMergeBloatLimit;
                    double ratio = source.Volume / Math.Max(1e-300, destination.Volume);
                    if (ratio >= BoneAppendageFraction)
                    {
                        if (ratio < BoneChainSizeRatio || ratio > 1.0 / BoneChainSizeRatio) continue;
                        D3 parentDirection = BoneDirection(ctx, joint.ParentBone, destination);
                        D3 childDirection = BoneDirection(ctx, joint.ChildBone, source);
                        double cos = ConcaveMath.Clamp(D3.Dot(parentDirection, childDirection), -1, 1);
                        if (Math.Acos(cos) * 180.0 / Math.PI > BoneChainMaxAngle) continue;
                        bloatLimit = double.PositiveInfinity;
                    }

                    List<D3> mergedPoints = MergedPoints(ctx, destination, source);
                    double mergedVolume = GroupVolume(ctx, destination.Bone, mergedPoints);
                    double bloat = mergedVolume / Math.Max(1e-300, destination.BaseVolume + source.BaseVolume);
                    if (bloat > bloatLimit) continue;
                    ApplyBoneMerge(destination, source, mergedPoints, mergedVolume, list);
                    merged = true;
                }
            }
        }

        private static D3 BoneDirection(Context ctx, int bone, BoneGroup group)
        {
            D3 sum = D3.Zero;
            int count = 0;
            for (int b = 0; b < ctx.Bones.Length; b++)
            {
                if (ctx.Bones[b] == null || ctx.Bones[b].ParentIndex != bone) continue;
                sum += ctx.Positions[b] - ctx.Positions[bone];
                count++;
            }

            if (count > 0 && sum.SqrLength > 0) return sum.NormalizedOr(new D3(0, 1, 0));

            var world = new D3[group.Points.Count];
            for (int i = 0; i < world.Length; i++) world[i] = Transform(ctx.LocalToWorld[group.Bone], group.Points[i]);
            if (world.Length == 0) return new D3(0, 1, 0);
            ConcaveMath.PrincipalAxes(world, world.Length, out D3 mean, out Frame3 axes, out D3 _);
            D3 axis = axes.AxisX;
            if (D3.Dot(axis, mean - ctx.Positions[bone]) < 0) axis = -axis;
            return axis;
        }

        private static void FindBestBoneMerge(
            Context ctx,
            BoneGroup source,
            List<BoneGroup> list,
            Dictionary<int, BoneGroup> groups,
            out BoneGroup bestDestination,
            out List<D3> bestPoints,
            out double bestVolume,
            out double bestBloat)
        {
            bestDestination = null;
            bestPoints = null;
            bestVolume = 0;
            bestBloat = double.PositiveInfinity;

            var candidates = new List<BoneGroup>();
            int current = ctx.Bones[source.Bone].ParentIndex;
            int hops = 0;
            while (current >= 0 && hops++ < 256)
            {
                if (groups.TryGetValue(current, out BoneGroup ancestor) && ancestor != source && list.Contains(ancestor))
                {
                    candidates.Add(ancestor);
                    break;
                }

                current = ctx.Bones[current] != null ? ctx.Bones[current].ParentIndex : -1;
            }

            BoneGroup nearest = null;
            double nearestDistance = double.PositiveInfinity;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == source || candidates.Contains(list[i])) continue;
                double distance = (ctx.Positions[list[i].Bone] - ctx.Positions[source.Bone]).SqrLength;
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = list[i];
            }

            if (nearest != null) candidates.Add(nearest);

            for (int c = 0; c < candidates.Count; c++)
            {
                BoneGroup destination = candidates[c];
                List<D3> mergedPoints = MergedPoints(ctx, destination, source);
                double mergedVolume = GroupVolume(ctx, destination.Bone, mergedPoints);
                double bloat = mergedVolume / Math.Max(1e-300, destination.BaseVolume + source.BaseVolume);
                if (bloat >= bestBloat) continue;
                bestBloat = bloat;
                bestDestination = destination;
                bestPoints = mergedPoints;
                bestVolume = mergedVolume;
            }
        }

        private static List<BoneJoint> FindBoneJoints(Context ctx, List<BoneGroup> list)
        {
            var owner = new Dictionary<int, BoneGroup>();
            for (int i = 0; i < list.Count; i++)
            {
                for (int m = 0; m < list[i].Members.Count; m++) owner[list[i].Members[m]] = list[i];
            }

            var joints = new List<BoneJoint>();
            var seen = new HashSet<BoneGroup>();
            for (int i = 0; i < list.Count; i++)
            {
                BoneGroup group = list[i];
                for (int m = 0; m < group.Members.Count; m++)
                {
                    int member = group.Members[m];
                    int current = ctx.Bones[member] != null ? ctx.Bones[member].ParentIndex : -1;
                    int hops = 0;
                    while (current >= 0 && !owner.ContainsKey(current) && hops++ < 256)
                        current = ctx.Bones[current] != null ? ctx.Bones[current].ParentIndex : -1;
                    if (current < 0 || !owner.ContainsKey(current)) continue;
                    BoneGroup parentGroup = owner[current];
                    if (parentGroup == group || seen.Contains(group)) continue;
                    seen.Add(group);
                    joints.Add(new BoneJoint
                    {
                        Child = group,
                        Parent = parentGroup,
                        ChildBone = member,
                        ParentBone = current,
                        Position = ctx.Positions[member]
                    });
                }
            }

            return joints;
        }

        // 월드 평면(점, 법선) → 본 로컬 → 그룹 작업 공간에서 자른다. 남는 부피가 5% 미만이면 자르지 않는다.
        private static void ClipGroupHull(Context ctx, Dictionary<BoneGroup, HullShape> hulls, Dictionary<BoneGroup, OutputMap> maps, BoneGroup group, D3 worldPoint, D3 worldNormal)
        {
            Matrix4x4 l2w = ctx.LocalToWorld[group.Bone];
            D3 localPoint = Transform(ctx.WorldToLocal[group.Bone], worldPoint);
            var localNormal = new D3(
                l2w.m00 * worldNormal.X + l2w.m10 * worldNormal.Y + l2w.m20 * worldNormal.Z,
                l2w.m01 * worldNormal.X + l2w.m11 * worldNormal.Y + l2w.m21 * worldNormal.Z,
                l2w.m02 * worldNormal.X + l2w.m12 * worldNormal.Y + l2w.m22 * worldNormal.Z).NormalizedOr(D3.Zero);
            if (localNormal.SqrLength == 0) return;
            OutputMap map = maps[group];
            D3 workPoint = (localPoint - map.Translation) / map.Scale;
            HullShape hull = hulls[group];
            HullShape clipped = HullOps.Clip(hull, workPoint, localNormal);
            if (clipped == null || !(clipped.Volume >= hull.Volume * ClipMinimumVolumeFraction)) return;
            hulls[group] = clipped;
        }

        private static void MeasureSkinned(Context ctx, ConcaveSkinnedInput input, WorkPiece[] pieces, ConcaveDecompositionResult result)
        {
            int boneCount = ctx.Bones.Length;
            var skin = new Matrix4x4[boneCount];
            for (int b = 0; b < boneCount; b++) skin[b] = ctx.LocalToWorld[b] * input.Bindposes[b];
            Vector3[] vertices = input.Vertices;
            var world = new D3[vertices.Length];
            for (int v = 0; v < vertices.Length; v++)
            {
                BoneWeight w = input.BoneWeights[v];
                D3 p = D3.From(vertices[v]);
                D3 sum = D3.Zero;
                double total = 0;
                Add(w.boneIndex0, w.weight0);
                Add(w.boneIndex1, w.weight1);
                Add(w.boneIndex2, w.weight2);
                Add(w.boneIndex3, w.weight3);
                world[v] = total > 0 ? sum / total : p;

                void Add(int index, float weight)
                {
                    if (weight <= 0 || index < 0 || index >= boneCount) return;
                    sum += Transform(skin[index], p) * weight;
                    total += weight;
                }
            }

            int[] triangles = input.Triangles;
            for (int i = 0; i < triangles.Length; i++)
            {
                if (triangles[i] < 0 || triangles[i] >= world.Length) return;
            }

            D3 min = world[triangles[0]], max = min;
            for (int i = 0; i < triangles.Length; i++)
            {
                min = D3.Min(min, world[triangles[i]]);
                max = D3.Max(max, world[triangles[i]]);
            }

            D3 size = max - min;
            double extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (!(extent > 0)) return;
            D3 center = (min + max) * 0.5;
            var work = new D3[world.Length];
            for (int i = 0; i < world.Length; i++) work[i] = (world[i] - center) / extent;
            VoxelModel model = VoxelModel.Build(work, triangles, 60000, 200, () => false);

            var inverse = new Matrix4x4[pieces.Length];
            for (int i = 0; i < pieces.Length; i++) inverse[i] = ctx.WorldToLocal[pieces[i].BoneIndex];

            Metrics.Measure((min - center) / extent, (max - center) / extent, ctx.Settings.MetricSamples, (p, scratch) => model.InsideSolid(p, scratch), p =>
            {
                D3 worldPoint = p * extent + center;
                for (int k = 0; k < pieces.Length; k++)
                {
                    WorkPiece piece = pieces[k];
                    D3 local = Transform(inverse[k], worldPoint);
                    D3 w = (local - piece.Map.Translation) / piece.Map.Scale;
                    if (piece.Contains(w)) return true;
                }

                return false;
            }, out float coverage, out float outside);
            result.Coverage = coverage;
            result.OutsideFraction = outside;
            result.MetricsApproximate = !model.Watertight;
        }
    }
}
