using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TelleR.ConcaveCollider
{
    /// <summary>조각이 조상 절단에서 물려받은 반공간 조건: Below면 좌표 ≤ W, 아니면 좌표 ≥ W(작업 공간).</summary>
    internal struct CutConstraint
    {
        public int Axis;
        public double W;
        public bool Below;

        public bool Accepts(D3 p, double tolerance) => Below ? p[Axis] <= W + tolerance : p[Axis] >= W - tolerance;
    }

    /// <summary>복셀 조각(연결 성분). 좌표는 복셀 격자 정수 좌표.</summary>
    internal sealed class VoxelPart
    {
        public CutConstraint[] Constraints = Array.Empty<CutConstraint>();

        public int[] Voxels;
        public int MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        /// <summary>복셀 격자점 볼록 껍질 부피(복셀 = 1).</summary>
        public double HullVolume;

        public double HullArea;
        public bool Unsplittable;

        /// <summary>오목도를 거의 줄이지 못한 분할이 연속된 횟수. 링/컵은 첫 분할이 오목도를 못 줄이고 두 번째에 줄인다.</summary>
        public int StaleDepth;

        public int Count => Voxels.Length;
        public double Concavity => Math.Max(0, HullVolume - Voxels.Length);
    }

    internal sealed class SplitParameters
    {
        public int MaxParts = 24;
        public double Tolerance = 0.01;
        public double BalanceWeight = 0.02;

        /// <summary>짧은 축 방향 절단 벌점(오목도가 같을 때 긴 축을 가로지르는 절단을 고르게 한다. 토러스를 납작하게 자르지 않도록).</summary>
        public double ShapeWeight = 0.01;
        public double StaircaseAllowance = 0.3;
        public int CandidatesPerAxis = 12;
    }

    /// <summary>
    /// V-HACD 방식 계층 분할. 가장 오목한 조각을 축 정렬 평면으로 자르고(비용 = 양쪽 오목도 합 + 균형 항),
    /// 잘린 양쪽을 연결 성분으로 다시 나눈다. 오목도는 복셀 격자점 껍질 부피 - 복셀 수.
    /// 격자점 껍질은 기둥 극값 + 행/열 2D 볼록 사슬 가지치기로 점 수를 줄여 빠르게 계산한다(정확값).
    /// </summary>
    internal sealed class VoxelSplitter
    {
        private readonly VoxelModel model;
        private readonly int[] stamp;
        private int stampCounter;

        /// <summary>실제로 쓰인 절단 평면(축별 작업 공간 좌표, 특징 평면에 스냅됨). 정확한 껍질 점 생성에 쓴다.</summary>
        public readonly SortedSet<double>[] CutPlanes = { new SortedSet<double>(), new SortedSet<double>(), new SortedSet<double>() };

        // 축별로 정렬된 원본 정점 좌표(특징 평면 스냅용)
        private readonly double[][] sortedCoordinate = new double[3][];
        private readonly int[][] sortedVertex = new int[3][];
        private readonly D3[] points;

        /// <summary>초기 성분 격자점 껍질 부피 합(오목도 정규화 기준).</summary>
        public double V0 { get; private set; }

        public double InitialConcavityRatio { get; private set; }

        public VoxelSplitter(VoxelModel model, D3[] points, int[] triangles)
        {
            this.model = model;
            this.points = points;
            stamp = new int[model.Count];
            var used = new bool[points.Length];
            var vertices = new List<int>();
            for (int i = 0; i < triangles.Length; i++)
            {
                if (used[triangles[i]]) continue;
                used[triangles[i]] = true;
                vertices.Add(triangles[i]);
            }

            for (int axis = 0; axis < 3; axis++)
            {
                int[] ids = vertices.ToArray();
                var keys = new double[ids.Length];
                for (int i = 0; i < ids.Length; i++) keys[i] = points[ids[i]][axis];
                Array.Sort(keys, ids);
                sortedCoordinate[axis] = keys;
                sortedVertex[axis] = ids;
            }
        }

        private int NewStamp() => ++stampCounter;

        /// <summary>초기 연결 성분.</summary>
        public List<VoxelPart> InitialParts()
        {
            var all = new int[model.Count];
            for (int i = 0; i < all.Length; i++) all[i] = i;
            int side = NewStamp();
            for (int i = 0; i < all.Length; i++) stamp[i] = side;
            var parts = new List<VoxelPart>();
            foreach (int[] component in Components(all, side)) parts.Add(MakePart(component));
            double v0 = 0, solid = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                v0 += parts[i].HullVolume;
                solid += parts[i].Count;
            }

            V0 = Math.Max(1, v0);
            InitialConcavityRatio = v0 > 0 ? Math.Max(0, 1 - solid / v0) : 0;
            return parts;
        }

        public List<VoxelPart> Split(List<VoxelPart> parts, SplitParameters prm, Func<float, bool> progress)
        {
            int guard = 0;
            while (parts.Count < prm.MaxParts && guard++ < 512)
            {
                VoxelPart target = null;
                double worst = prm.Tolerance;
                for (int i = 0; i < parts.Count; i++)
                {
                    VoxelPart part = parts[i];
                    if (part.Unsplittable || part.Count < 8) continue;
                    double effective = (part.Concavity - prm.StaircaseAllowance * part.HullArea) / V0;
                    if (effective <= worst) continue;
                    worst = effective;
                    target = part;
                }

                if (target == null) break;
                if (!progress(parts.Count / (float)prm.MaxParts)) throw new OperationCanceledException();

                List<VoxelPart> pieces = TrySplit(target, prm);
                if (ConcaveTrace.Enabled)
                    ConcaveTrace.Write($"split part n={target.Count} conc={target.Concavity / V0:0.0000} eff={worst:0.0000} stale={target.StaleDepth} -> {(pieces == null ? "refused" : pieces.Count + " parts")}");
                if (pieces == null)
                {
                    target.Unsplittable = true;
                    continue;
                }

                parts.Remove(target);
                parts.AddRange(pieces);
            }

            return parts;
        }

        private sealed class Tables
        {
            public int A, B, C;
            public int[] ZMin, ZMax; // (x, y) 기둥의 z 범위, 인덱스 x * B + y
            public int[] XMin, XMax; // (y, z) 기둥의 x 범위, 인덱스 y * C + z
            public long[] PrefixX, PrefixY, PrefixZ;
        }

        private Tables BuildTables(VoxelPart part)
        {
            var t = new Tables
            {
                A = part.MaxX - part.MinX + 1,
                B = part.MaxY - part.MinY + 1,
                C = part.MaxZ - part.MinZ + 1
            };
            t.ZMin = new int[t.A * t.B];
            t.ZMax = new int[t.A * t.B];
            t.XMin = new int[t.B * t.C];
            t.XMax = new int[t.B * t.C];
            for (int i = 0; i < t.ZMin.Length; i++)
            {
                t.ZMin[i] = int.MaxValue;
                t.ZMax[i] = int.MinValue;
            }

            for (int i = 0; i < t.XMin.Length; i++)
            {
                t.XMin[i] = int.MaxValue;
                t.XMax[i] = int.MinValue;
            }

            var cx = new long[t.A];
            var cy = new long[t.B];
            var cz = new long[t.C];
            int[] voxels = part.Voxels;
            for (int k = 0; k < voxels.Length; k++)
            {
                int id = voxels[k];
                int x = model.VX[id] - part.MinX;
                int y = model.VY[id] - part.MinY;
                int z = model.VZ[id] - part.MinZ;
                int zi = x * t.B + y;
                if (z < t.ZMin[zi]) t.ZMin[zi] = z;
                if (z > t.ZMax[zi]) t.ZMax[zi] = z;
                int xi = y * t.C + z;
                if (x < t.XMin[xi]) t.XMin[xi] = x;
                if (x > t.XMax[xi]) t.XMax[xi] = x;
                cx[x]++;
                cy[y]++;
                cz[z]++;
            }

            t.PrefixX = Prefix(cx);
            t.PrefixY = Prefix(cy);
            t.PrefixZ = Prefix(cz);
            return t;
        }

        private static long[] Prefix(long[] counts)
        {
            var prefix = new long[counts.Length + 1];
            for (int i = 0; i < counts.Length; i++) prefix[i + 1] = prefix[i] + counts[i];
            return prefix;
        }

        private VoxelPart MakePart(int[] voxels)
        {
            var part = new VoxelPart { Voxels = voxels };
            part.MinX = part.MinY = part.MinZ = int.MaxValue;
            part.MaxX = part.MaxY = part.MaxZ = int.MinValue;
            for (int k = 0; k < voxels.Length; k++)
            {
                int id = voxels[k];
                int x = model.VX[id], y = model.VY[id], z = model.VZ[id];
                if (x < part.MinX) part.MinX = x;
                if (y < part.MinY) part.MinY = y;
                if (z < part.MinZ) part.MinZ = z;
                if (x > part.MaxX) part.MaxX = x;
                if (y > part.MaxY) part.MaxY = y;
                if (z > part.MaxZ) part.MaxZ = z;
            }

            Tables t = BuildTables(part);
            part.HullVolume = ColumnHullVolume(t.ZMin, t.ZMax, t.B, 0, t.A, 0, t.B, true, out double area);
            part.HullArea = area;
            if (part.HullVolume < voxels.Length) part.HullVolume = voxels.Length;
            return part;
        }

        private struct Candidate
        {
            public int Axis;
            public int P;
            public double Cost;
            public double Concavity;
        }

        private List<VoxelPart> TrySplit(VoxelPart part, SplitParameters prm)
        {
            Tables t = BuildTables(part);
            var candidates = new List<Candidate>();
            int[] dims = { t.A, t.B, t.C };
            for (int axis = 0; axis < 3; axis++)
            {
                int n = dims[axis];
                if (n < 2) continue;
                int step = Math.Max(1, (int)Math.Ceiling((n - 1) / (double)prm.CandidatesPerAxis));
                for (int p = step; p < n; p += step)
                    candidates.Add(new Candidate { Axis = axis, P = p });
            }

            if (candidates.Count == 0) return null;
            Candidate best = EvaluateAll(t, candidates, prm, part.StaleDepth >= 2 ? part.Concavity * 0.98 : double.PositiveInfinity);

            // 선택된 축에서 한 칸 단위로 다듬기
            int bestStep = Math.Max(1, (int)Math.Ceiling((dims[best.Axis] - 1) / (double)prm.CandidatesPerAxis));
            if (bestStep > 1)
            {
                var refine = new List<Candidate>();
                for (int p = Math.Max(1, best.P - bestStep + 1); p <= Math.Min(dims[best.Axis] - 1, best.P + bestStep - 1); p++)
                {
                    if (p != best.P) refine.Add(new Candidate { Axis = best.Axis, P = p });
                }

                if (refine.Count > 0)
                {
                    Candidate refined = EvaluateAll(t, refine, prm, double.PositiveInfinity);
                    if (refined.Cost < best.Cost) best = refined;
                }
            }

            // 오목도가 거의 줄지 않는 절단은 연속 2회까지만 허용한다(토러스·컵은 반으로 잘라도 줄지 않고 4등분에서 줄어든다).
            bool stale = part.Concavity - best.Concavity < part.Concavity * 0.02;
            if (ConcaveTrace.Enabled) ConcaveTrace.Write($"  best cut axis={best.Axis} p={best.P}/{dims[best.Axis]} concSum={best.Concavity / V0:0.0000} cost={best.Cost:0.0000} stale={stale}");
            if (stale && part.StaleDepth >= 2) return null;

            List<VoxelPart> pieces = ApplyCut(part, best.Axis, best.P);
            if (pieces != null)
            {
                for (int i = 0; i < pieces.Count; i++) pieces[i].StaleDepth = stale ? part.StaleDepth + 1 : 0;
            }

            return pieces;
        }

        // maxConcavity보다 오목도 합이 작은(= 실제로 줄이는) 후보가 있으면 그중 최저 비용, 없으면 전체 최저 비용.
        private Candidate EvaluateAll(Tables t, List<Candidate> candidates, SplitParameters prm, double maxConcavity)
        {
            var results = candidates.ToArray();
            double v0 = V0;
            int maxExtent = Math.Max(t.A, Math.Max(t.B, t.C));
            Parallel.For(0, results.Length, i =>
            {
                Candidate c = results[i];
                double hullL, hullR;
                long countL, countTotal;
                switch (c.Axis)
                {
                    case 0:
                        hullL = ColumnHullVolume(t.ZMin, t.ZMax, t.B, 0, c.P, 0, t.B, false, out _);
                        hullR = ColumnHullVolume(t.ZMin, t.ZMax, t.B, c.P, t.A, 0, t.B, false, out _);
                        countL = t.PrefixX[c.P];
                        countTotal = t.PrefixX[t.A];
                        break;
                    case 1:
                        hullL = ColumnHullVolume(t.ZMin, t.ZMax, t.B, 0, t.A, 0, c.P, false, out _);
                        hullR = ColumnHullVolume(t.ZMin, t.ZMax, t.B, 0, t.A, c.P, t.B, false, out _);
                        countL = t.PrefixY[c.P];
                        countTotal = t.PrefixY[t.B];
                        break;
                    default:
                        hullL = ColumnHullVolume(t.XMin, t.XMax, t.C, 0, t.B, 0, c.P, false, out _);
                        hullR = ColumnHullVolume(t.XMin, t.XMax, t.C, 0, t.B, c.P, t.C, false, out _);
                        countL = t.PrefixZ[c.P];
                        countTotal = t.PrefixZ[t.C];
                        break;
                }

                long countR = countTotal - countL;
                double concavity = Math.Max(0, hullL - countL) + Math.Max(0, hullR - countR);
                int extent = c.Axis == 0 ? t.A : c.Axis == 1 ? t.B : t.C;
                c.Concavity = concavity;
                c.Cost = concavity / v0 + prm.BalanceWeight * Math.Abs(countL - countR) / v0 + prm.ShapeWeight * (1.0 - extent / (double)maxExtent);
                results[i] = c;
            });

            Candidate best = results[0];
            for (int i = 1; i < results.Length; i++)
            {
                if (results[i].Cost < best.Cost) best = results[i];
            }

            if (best.Concavity < maxConcavity || double.IsPositiveInfinity(maxConcavity)) return best;
            bool found = false;
            Candidate improving = best;
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].Concavity >= maxConcavity) continue;
                if (found && results[i].Cost >= improving.Cost) continue;
                improving = results[i];
                found = true;
            }

            return found ? improving : best;
        }

        private List<VoxelPart> ApplyCut(VoxelPart part, int axis, int p)
        {
            int left = NewStamp();
            int right = NewStamp();
            int[] voxels = part.Voxels;
            int origin = axis == 0 ? part.MinX : axis == 1 ? part.MinY : part.MinZ;
            int[] coords = axis == 0 ? model.VX : axis == 1 ? model.VY : model.VZ;
            var leftList = new List<int>();
            var rightList = new List<int>();
            for (int k = 0; k < voxels.Length; k++)
            {
                int id = voxels[k];
                if (coords[id] - origin < p)
                {
                    stamp[id] = left;
                    leftList.Add(id);
                }
                else
                {
                    stamp[id] = right;
                    rightList.Add(id);
                }
            }

            if (leftList.Count == 0 || rightList.Count == 0) return null;

            double w = SnapPlane(part, axis, origin + p);
            CutConstraint[] leftConstraints = With(part.Constraints, new CutConstraint { Axis = axis, W = w, Below = true });
            CutConstraint[] rightConstraints = With(part.Constraints, new CutConstraint { Axis = axis, W = w, Below = false });
            var result = new List<VoxelPart>();
            foreach (int[] component in Components(leftList.ToArray(), left))
            {
                VoxelPart child = MakePart(component);
                child.Constraints = leftConstraints;
                result.Add(child);
            }

            foreach (int[] component in Components(rightList.ToArray(), right))
            {
                VoxelPart child = MakePart(component);
                child.Constraints = rightConstraints;
                result.Add(child);
            }

            CutPlanes[axis].Add(w);
            return result;
        }

        private static CutConstraint[] With(CutConstraint[] source, CutConstraint added)
        {
            var result = new CutConstraint[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[source.Length] = added;
            return result;
        }

        /// <summary>
        /// 격자 절단 평면 ±1복셀 안에 같은 좌표를 공유하는 원본 정점이 3개 이상 있으면(상자 면, 계단 턱 같은 특징 평면)
        /// 그 좌표로 옮긴다. 복셀 경계에 걸려 조각이 이웃 형상을 얇게 물고 가던 문제를 없앤다.
        /// </summary>
        private double SnapPlane(VoxelPart part, int axis, int lattice)
        {
            double h = model.H;
            double w = model.Origin[axis] + lattice * h;
            double[] keys = sortedCoordinate[axis];
            int[] ids = sortedVertex[axis];
            int start = LowerBound(keys, w - h * 0.95);
            int u = (axis + 1) % 3, v = (axis + 2) % 3;
            int[] partMin = { part.MinX, part.MinY, part.MinZ };
            int[] partMax = { part.MaxX, part.MaxY, part.MaxZ };
            double uLo = model.Origin[u] + (partMin[u] - 1) * h, uHi = model.Origin[u] + (partMax[u] + 2) * h;
            double vLo = model.Origin[v] + (partMin[v] - 1) * h, vHi = model.Origin[v] + (partMax[v] + 2) * h;
            double eps = h * 1e-4;

            double best = w;
            int bestCount = 0;
            double bestDistance = double.PositiveInfinity;
            int i = start;
            while (i < keys.Length && keys[i] <= w + h * 0.95)
            {
                double value = keys[i];
                int count = 0;
                double sum = 0;
                int j = i;
                while (j < keys.Length && keys[j] - value <= eps)
                {
                    D3 point = points[ids[j]];
                    if (point[u] >= uLo && point[u] <= uHi && point[v] >= vLo && point[v] <= vHi)
                    {
                        count++;
                        sum += keys[j];
                    }

                    j++;
                }

                if (count >= 3)
                {
                    double mean = sum / count;
                    double distance = Math.Abs(mean - w);
                    if (count > bestCount || (count == bestCount && distance < bestDistance))
                    {
                        best = mean;
                        bestCount = count;
                        bestDistance = distance;
                    }
                }

                i = j;
            }

            return best;
        }

        private static int LowerBound(double[] sorted, double value)
        {
            int lo = 0, hi = sorted.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (sorted[mid] < value) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        /// <summary>stamp == side인 복셀들의 6-연결 성분.</summary>
        private List<int[]> Components(int[] voxels, int side)
        {
            var components = new List<int[]>();
            var queue = new List<int>();
            int strideY = model.NX, strideZ = model.NX * model.NY;
            for (int k = 0; k < voxels.Length; k++)
            {
                int seed = voxels[k];
                if (stamp[seed] != side) continue;
                int mark = NewStamp();
                queue.Clear();
                stamp[seed] = mark;
                queue.Add(seed);
                for (int head = 0; head < queue.Count; head++)
                {
                    int id = queue[head];
                    int g = model.Index(model.VX[id], model.VY[id], model.VZ[id]);
                    Visit(g - 1);
                    Visit(g + 1);
                    Visit(g - strideY);
                    Visit(g + strideY);
                    Visit(g - strideZ);
                    Visit(g + strideZ);
                }

                components.Add(queue.ToArray());

                void Visit(int grid)
                {
                    if ((uint)grid >= (uint)model.IdOf.Length) return;
                    int neighbor = model.IdOf[grid];
                    if (neighbor < 0 || stamp[neighbor] != side) return;
                    stamp[neighbor] = mark;
                    queue.Add(neighbor);
                }
            }

            return components;
        }

        [ThreadStatic] private static LatticeScratch scratch;

        private sealed class LatticeScratch
        {
            public int[] LMin = new int[256];
            public int[] LMax = new int[256];
            public byte[] KeepMin = new byte[256];
            public byte[] KeepMax = new byte[256];
            public int[] Chain = new int[256];
            public D3[] Points = new D3[512];

            public void Ensure(int n, int maxLine)
            {
                if (LMin.Length < n)
                {
                    int size = Math.Max(n, LMin.Length * 2);
                    LMin = new int[size];
                    LMax = new int[size];
                    KeepMin = new byte[size];
                    KeepMax = new byte[size];
                }

                if (Chain.Length < maxLine) Chain = new int[Math.Max(maxLine, Chain.Length * 2)];
                if (Points.Length < n * 2) Points = new D3[Math.Max(n * 2, Points.Length * 2)];
            }
        }

        /// <summary>
        /// 기둥 표(u, v) → [wmin, wmax] 중 u∈[u0,u1), v∈[v0,v1) 부분의 복셀 합집합 볼록 껍질 부피.
        /// 격자 꼭짓점 기둥 극값을 만든 뒤, 3D 극점이 되려면 행과 열 양쪽 2D 볼록 사슬 위에 있어야 한다는
        /// 필요조건으로 점을 가지치기한다.
        /// </summary>
        private static double ColumnHullVolume(int[] wmin, int[] wmax, int dv, int u0, int u1, int v0, int v1, bool wantArea, out double area)
        {
            area = 0;
            int lu = u1 - u0 + 1;
            int lv = v1 - v0 + 1;
            int n = lu * lv;
            LatticeScratch s = scratch ?? (scratch = new LatticeScratch());
            s.Ensure(n, Math.Max(lu, lv));
            int[] lmin = s.LMin, lmax = s.LMax;
            byte[] keepMin = s.KeepMin, keepMax = s.KeepMax;
            for (int i = 0; i < n; i++)
            {
                lmin[i] = int.MaxValue;
                lmax[i] = int.MinValue;
                keepMin[i] = 0;
                keepMax[i] = 0;
            }

            long voxels = 0;
            for (int u = u0; u < u1; u++)
            {
                int row = u * dv;
                for (int v = v0; v < v1; v++)
                {
                    int lo = wmin[row + v];
                    int hi = wmax[row + v];
                    if (hi < lo) continue;
                    voxels += hi - lo + 1;
                    hi += 1;
                    int baseIndex = (u - u0) * lv + (v - v0);
                    Update(baseIndex, lo, hi);
                    Update(baseIndex + 1, lo, hi);
                    Update(baseIndex + lv, lo, hi);
                    Update(baseIndex + lv + 1, lo, hi);
                }
            }

            void Update(int index, int lo, int hi)
            {
                if (lo < lmin[index]) lmin[index] = lo;
                if (hi > lmax[index]) lmax[index] = hi;
            }

            if (voxels == 0) return 0;

            int[] chain = s.Chain;
            // 행(u 고정, v 방향)
            for (int a = 0; a < lu; a++)
            {
                MarkChains(a * lv, 1, lv, lmin, lmax, keepMin, keepMax, chain, 1);
            }

            // 열(v 고정, u 방향)
            for (int b = 0; b < lv; b++)
            {
                MarkChains(b, lv, lu, lmin, lmax, keepMin, keepMax, chain, 2);
            }

            D3[] points = s.Points;
            int count = 0;
            for (int a = 0; a < lu; a++)
            {
                for (int b = 0; b < lv; b++)
                {
                    int index = a * lv + b;
                    if (lmin[index] == int.MaxValue) continue;
                    if (keepMin[index] == 3) points[count++] = new D3(u0 + a, v0 + b, lmin[index]);
                    if (keepMax[index] == 3) points[count++] = new D3(u0 + a, v0 + b, lmax[index]);
                }
            }

            ConvexHull3D hull = ConvexHull3D.ForCurrentThread;
            if (count < 4 || !hull.Build(points, count, 1e-9)) return voxels;
            if (wantArea) area = hull.ComputeArea();
            return Math.Max(voxels, hull.Volume);
        }

        // start부터 stride 간격 count개의 격자점에서 아래 사슬(lmin)과 위 사슬(lmax)을 표시한다.
        private static void MarkChains(int start, int stride, int count, int[] lmin, int[] lmax, byte[] keepMin, byte[] keepMax, int[] chain, byte bit)
        {
            int m = 0;
            for (int k = 0; k < count; k++)
            {
                int index = start + k * stride;
                if (lmin[index] == int.MaxValue) continue;
                while (m >= 2 && Cross(chain[m - 2], chain[m - 1], k, lmin, start, stride) <= 0) m--;
                chain[m++] = k;
            }

            for (int i = 0; i < m; i++) keepMin[start + chain[i] * stride] |= bit;

            m = 0;
            for (int k = 0; k < count; k++)
            {
                int index = start + k * stride;
                if (lmin[index] == int.MaxValue) continue;
                while (m >= 2 && Cross(chain[m - 2], chain[m - 1], k, lmax, start, stride) >= 0) m--;
                chain[m++] = k;
            }

            for (int i = 0; i < m; i++) keepMax[start + chain[i] * stride] |= bit;
        }

        private static long Cross(int o, int a, int b, int[] values, int start, int stride)
        {
            long yo = values[start + o * stride];
            long ya = values[start + a * stride];
            long yb = values[start + b * stride];
            return (long)(a - o) * (yb - yo) - (ya - yo) * (long)(b - o);
        }
    }
}
