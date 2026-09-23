using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TelleR.ConcaveCollider
{
    /// <summary>
    /// 복셀 조각 → 원본 표면을 따르는 정확한 볼록 껍질.
    /// 조각 영역 = 솔리드 ∩ (조상 절단 반공간들) ∩ (연결 성분)이고 조각 사이 경계는 모두 절단 평면 위에 있으므로
    /// 영역의 극점은 (a) 원본 정점, (b) 삼각형 ∩ 절단 평면 선분 끝점, (c) 그 선분 ∩ 다른 축 절단 평면,
    /// (d) 세 절단 평면 교점 중 솔리드 안의 점 뿐이다. 각 점을 주변 3x3x3 복셀의 조각 중 반공간 조건을
    /// 만족하는 조각에 배정한 뒤(절단 평면은 특징 평면으로 최대 1복셀 스냅될 수 있음) 조각마다 QuickHull을 돌린다.
    /// 복셀 계단만큼 튀어나오던 V-HACD 껍질과 달리 표면 밖으로 나가지 않는다.
    /// </summary>
    internal static class ExactHullBuilder
    {
        public static HullShape[] Build(VoxelModel model, List<VoxelPart> parts, SortedSet<double>[] cutPlanes, D3[] points, int[] triangles, Func<bool> cancelled)
        {
            int partCount = parts.Count;
            var label = new int[model.Count];
            for (int i = 0; i < label.Length; i++) label[i] = -1;
            for (int p = 0; p < partCount; p++)
            {
                int[] voxels = parts[p].Voxels;
                for (int k = 0; k < voxels.Length; k++) label[voxels[k]] = p;
            }

            var lists = new List<D3>[partCount];
            for (int p = 0; p < partCount; p++) lists[p] = new List<D3>();
            var candidates = new List<int>(8);
            double tolerance = model.H * 1e-6;

            // 평면 좌표(작업 공간, 정렬됨)
            var planes = new double[3][];
            for (int axis = 0; axis < 3; axis++)
            {
                planes[axis] = new double[cutPlanes[axis].Count];
                cutPlanes[axis].CopyTo(planes[axis]);
            }

            // (a) 원본 정점
            var used = new bool[points.Length];
            for (int i = 0; i < triangles.Length; i++) used[triangles[i]] = true;
            for (int v = 0; v < points.Length; v++)
            {
                if (used[v]) Assign(points[v]);
            }

            if (cancelled()) throw new OperationCanceledException();

            // (b), (c) 삼각형 ∩ 절단 평면
            var segment = new D3[3];
            bool anyPlanes = planes[0].Length + planes[1].Length + planes[2].Length > 0;
            if (anyPlanes)
            {
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    D3 a = points[triangles[t]], b = points[triangles[t + 1]], c = points[triangles[t + 2]];
                    for (int axis = 0; axis < 3; axis++)
                    {
                        double[] axisPlanes = planes[axis];
                        if (axisPlanes.Length == 0) continue;
                        double lo = Math.Min(a[axis], Math.Min(b[axis], c[axis]));
                        double hi = Math.Max(a[axis], Math.Max(b[axis], c[axis]));
                        int first = LowerBound(axisPlanes, lo);
                        for (int k = first; k < axisPlanes.Length && axisPlanes[k] < hi; k++)
                        {
                            double w = axisPlanes[k];
                            if (w <= lo) continue;
                            int count = Intersect(a, b, c, axis, w, segment);
                            if (count < 2) continue;
                            for (int s = 0; s < count; s++) Assign(segment[s]);
                            D3 e0 = segment[0], e1 = segment[1];
                            for (int other = 0; other < 3; other++)
                            {
                                if (other == axis) continue;
                                double[] otherPlanes = planes[other];
                                if (otherPlanes.Length == 0) continue;
                                double s0 = e0[other], s1 = e1[other];
                                double slo = Math.Min(s0, s1), shi = Math.Max(s0, s1);
                                if (!(shi - slo > 0)) continue;
                                int j0 = LowerBound(otherPlanes, slo);
                                for (int j = j0; j < otherPlanes.Length && otherPlanes[j] < shi; j++)
                                {
                                    double w2 = otherPlanes[j];
                                    if (w2 <= slo) continue;
                                    double f = (w2 - s0) / (s1 - s0);
                                    D3 q = e0 + (e1 - e0) * f;
                                    q = WithAxis(WithAxis(q, other, w2), axis, w);
                                    Assign(q);
                                }
                            }
                        }
                    }
                }
            }

            if (cancelled()) throw new OperationCanceledException();

            // (d) 세 평면 교점(솔리드 안쪽만)
            var scratch = new List<int>();
            for (int ix = 0; ix < planes[0].Length; ix++)
            {
                for (int iy = 0; iy < planes[1].Length; iy++)
                {
                    for (int iz = 0; iz < planes[2].Length; iz++)
                    {
                        var p = new D3(planes[0][ix], planes[1][iy], planes[2][iz]);
                        if (model.InsideSolid(p, scratch)) Assign(p);
                    }
                }
            }

            var hulls = new HullShape[partCount];
            Parallel.For(0, partCount, p =>
            {
                hulls[p] = BuildPartHull(model, parts[p], lists[p], points, triangles);
            });

            return hulls;

            // 점을 포함 복셀 주변 3x3x3의 조각 중, 조상 절단 반공간 조건을 만족하는 모든 조각에 넣는다.
            // 절단 평면이 특징 평면으로 최대 1복셀 옮겨질 수 있어서 이웃 복셀까지 본다.
            void Assign(D3 p)
            {
                if (!model.TryVoxelOf(p, out int x, out int y, out int z)) return;
                candidates.Clear();
                for (int zz = z - 1; zz <= z + 1; zz++)
                {
                    if (zz < 0 || zz >= model.NZ) continue;
                    for (int yy = y - 1; yy <= y + 1; yy++)
                    {
                        if (yy < 0 || yy >= model.NY) continue;
                        for (int xx = x - 1; xx <= x + 1; xx++)
                        {
                            if (xx < 0 || xx >= model.NX) continue;
                            int id = model.IdOf[model.Index(xx, yy, zz)];
                            if (id < 0) continue;
                            int part = label[id];
                            if (part < 0 || candidates.Contains(part)) continue;
                            candidates.Add(part);
                        }
                    }
                }

                for (int c = 0; c < candidates.Count; c++)
                {
                    int part = candidates[c];
                    CutConstraint[] constraints = parts[part].Constraints;
                    bool accepted = true;
                    for (int k = 0; k < constraints.Length; k++)
                    {
                        if (constraints[k].Accepts(p, tolerance)) continue;
                        accepted = false;
                        break;
                    }

                    if (accepted) lists[part].Add(p);
                }
            }
        }

        private static D3 WithAxis(D3 p, int axis, double value)
        {
            if (axis == 0) p.X = value;
            else if (axis == 1) p.Y = value;
            else p.Z = value;
            return p;
        }

        private static int LowerBound(double[] sorted, double value)
        {
            int lo = 0, hi = sorted.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (sorted[mid] <= value) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        // 삼각형과 평면(axis = w)의 교선 끝점. 부호가 엄격히 바뀌는 모서리 교점 + 평면 위 꼭짓점.
        private static int Intersect(D3 a, D3 b, D3 c, int axis, double w, D3[] output)
        {
            double da = a[axis] - w, db = b[axis] - w, dc = c[axis] - w;
            int count = 0;
            Edge(a, b, da, db);
            Edge(b, c, db, dc);
            Edge(c, a, dc, da);
            if (da == 0 && count < 3) output[count++] = a;
            if (db == 0 && count < 3) output[count++] = b;
            if (dc == 0 && count < 3) output[count++] = c;
            return count;

            void Edge(D3 p, D3 q, double dp, double dq)
            {
                if (count >= 3) return;
                if (!((dp < 0 && dq > 0) || (dp > 0 && dq < 0))) return;
                double f = dp / (dp - dq);
                D3 point = p + (q - p) * f;
                point = WithAxis(point, axis, w);
                output[count++] = point;
            }
        }

        private static HullShape BuildPartHull(VoxelModel model, VoxelPart part, List<D3> collected, D3[] points, int[] triangles)
        {
            double voxelVolume = part.Count * model.H * model.H * model.H;
            List<D3> unique = HullOps.Deduplicate(collected, 1e-9);
            HullShape hull = unique.Count >= 4 ? HullShape.FromPoints(unique) : null;
            if (hull != null && hull.Volume > voxelVolume * 0.01) return hull;

            // 평평한 조각(셸 메시, 한 겹 판)은 최소 두께를 준다.
            if (unique.Count >= 3)
            {
                List<D3> thick = HullOps.Thicken(unique, model.H);
                HullShape thickHull = HullShape.FromPoints(thick);
                if (thickHull != null && thickHull.Volume > 0) return thickHull;
            }

            return LatticeHull(model, part);
        }

        /// <summary>
        /// 복셀 격자점 껍질 + EMBER 표면 스냅(2복셀 이내 정점을 가장 가까운 삼각형 점으로, 부피 50% 미만으로 줄면 원래 껍질 유지).
        /// 정확한 점 수집이 실패한 조각의 대체 경로.
        /// </summary>
        internal static HullShape LatticeHull(VoxelModel model, VoxelPart part)
        {
            var corners = new List<D3>(part.Count * 2);
            var seen = new HashSet<long>();
            for (int k = 0; k < part.Voxels.Length; k++)
            {
                int id = part.Voxels[k];
                for (int c = 0; c < 8; c++)
                {
                    long x = model.VX[id] + (c & 1), y = model.VY[id] + ((c >> 1) & 1), z = model.VZ[id] + (c >> 2);
                    long key = (x * 4096 + y) * 4096 + z;
                    if (!seen.Add(key)) continue;
                    corners.Add(new D3(model.Origin.X + x * model.H, model.Origin.Y + y * model.H, model.Origin.Z + z * model.H));
                }
            }

            D3[] array = corners.ToArray();
            if (array.Length > 512) array = HullOps.ExtremeSubset(array, ConcaveMath.FibonacciDirections(512), ConcaveMath.SeedDirections);
            HullShape raw = HullShape.FromPoints(array, array.Length);
            if (raw == null) return null;

            var snapped = new List<D3>(raw.Vertices.Length);
            bool any = false;
            for (int i = 0; i < raw.Vertices.Length; i++)
            {
                D3 v = raw.Vertices[i];
                if (model.TryClosestSurfacePoint(v, 2, model.H * 2, out D3 q))
                {
                    snapped.Add(q);
                    any = true;
                }
                else
                {
                    snapped.Add(v);
                }
            }

            if (!any) return raw;
            HullShape rebuilt = HullShape.FromPoints(snapped);
            return rebuilt != null && rebuilt.Volume > raw.Volume * 0.5 ? rebuilt : raw;
        }
    }
}
