using System;
using System.Collections.Generic;

namespace TelleR.ConcaveCollider
{
    /// <summary>볼록 껍질 한 개(정점, 바깥 감김 삼각형, 부피)와 평면 캐시.</summary>
    internal sealed class HullShape
    {
        public D3[] Vertices;
        public int[] Triangles;
        public double Volume;
        public D3 Min, Max;

        // 평면 캐시는 한 객체로 묶어 한 번에 대입한다(병렬 측정에서 여러 스레드가 동시에 만들어도 안전).
        private sealed class PlaneSet
        {
            public D3[] Normals;
            public double[] Offsets;
        }

        private PlaneSet planes;

        public HullShape(D3[] vertices, int[] triangles, double volume)
        {
            Vertices = vertices;
            Triangles = triangles;
            Volume = volume;
            Min = Max = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                Min = D3.Min(Min, vertices[i]);
                Max = D3.Max(Max, vertices[i]);
            }
        }

        public D3 Size => Max - Min;
        public double Extent => Math.Max(Size.X, Math.Max(Size.Y, Size.Z));

        public D3 Centroid
        {
            get
            {
                D3 sum = D3.Zero;
                for (int i = 0; i < Vertices.Length; i++) sum += Vertices[i];
                return sum / Math.Max(1, Vertices.Length);
            }
        }

        public static HullShape FromPoints(D3[] points, int count, double relativeTolerance = 1e-10)
        {
            if (!ConvexHull3D.TryBuild(points, count, out D3[] vertices, out int[] triangles, out double volume, relativeTolerance)) return null;
            return new HullShape(vertices, triangles, volume);
        }

        public static HullShape FromPoints(List<D3> points, double relativeTolerance = 1e-10)
        {
            if (points == null || points.Count < 4) return null;
            D3[] array = points.ToArray();
            return FromPoints(array, array.Length, relativeTolerance);
        }

        /// <summary>
        /// 면 평면(단위 법선, 오프셋). 법선은 Vector3.normalized를 쓰지 않고 직접 나눈다
        /// (초소형 좌표에서 0 벡터로 뭉개지는 함정 회피). 면적이 상대적으로 0인 면은 건너뛴다.
        /// </summary>
        private PlaneSet EnsurePlanes()
        {
            PlaneSet cached = planes;
            if (cached != null) return cached;
            int faceCount = Triangles.Length / 3;
            var normals = new List<D3>(faceCount);
            var offsets = new List<double>(faceCount);
            double extent = Math.Max(Extent, 1e-300);
            double minCross = extent * extent * 1e-12;
            D3 centroid = Centroid;
            for (int t = 0; t + 2 < Triangles.Length; t += 3)
            {
                D3 a = Vertices[Triangles[t]];
                D3 n = D3.Cross(Vertices[Triangles[t + 1]] - a, Vertices[Triangles[t + 2]] - a);
                double len = n.Length;
                if (!(len > minCross)) continue;
                n /= len;
                double offset = D3.Dot(n, a);
                if (D3.Dot(n, centroid) - offset > 0)
                {
                    n = -n;
                    offset = -offset;
                }

                normals.Add(n);
                offsets.Add(offset);
            }

            cached = new PlaneSet { Normals = normals.ToArray(), Offsets = offsets.ToArray() };
            planes = cached;
            return cached;
        }

        public int PlaneCount => EnsurePlanes().Normals.Length;

        /// <summary>점이 껍질 밖으로 나간 최대 거리(음수 = 안쪽).</summary>
        public double SignedDistance(D3 p)
        {
            PlaneSet set = EnsurePlanes();
            D3[] normals = set.Normals;
            double[] offsets = set.Offsets;
            double worst = double.NegativeInfinity;
            for (int i = 0; i < normals.Length; i++)
            {
                double d = D3.Dot(normals[i], p) - offsets[i];
                if (d > worst) worst = d;
            }

            return worst;
        }

        public bool Contains(D3 p, double epsilon = 0)
        {
            if (p.X < Min.X - epsilon || p.Y < Min.Y - epsilon || p.Z < Min.Z - epsilon ||
                p.X > Max.X + epsilon || p.Y > Max.Y + epsilon || p.Z > Max.Z + epsilon)
                return false;
            PlaneSet set = EnsurePlanes();
            D3[] normals = set.Normals;
            double[] offsets = set.Offsets;
            for (int i = 0; i < normals.Length; i++)
            {
                if (D3.Dot(normals[i], p) - offsets[i] > epsilon) return false;
            }

            return true;
        }

        public double SurfaceArea
        {
            get
            {
                double area = 0;
                for (int t = 0; t + 2 < Triangles.Length; t += 3)
                {
                    D3 a = Vertices[Triangles[t]];
                    area += D3.Cross(Vertices[Triangles[t + 1]] - a, Vertices[Triangles[t + 2]] - a).Length * 0.5;
                }

                return area;
            }
        }
    }

    /// <summary>껍질 후처리(EMBER에서 이식): 정점 축소, 평면 절단, 극점 대리 껍질, 몬테카를로 덮임 비율.</summary>
    internal static class HullOps
    {
        public const double VolumeKeep = 0.97;
        private static readonly D3[] PoolDirections = ConcaveMath.FibonacciDirections(256);
        private static readonly D3[] ProxyDirections = ConcaveMath.FibonacciDirections(96);

        /// <summary>여러 방향의 극점만 뽑은 부분집합(내접 근사). 입력 좌표 스케일에 무관하다.</summary>
        public static D3[] ExtremeSubset(D3[] points, D3[] directions, D3[] extra = null)
        {
            if (points.Length <= directions.Length) return (D3[])points.Clone();
            var chosen = new HashSet<int>();
            D3 center = D3.Zero;
            for (int i = 0; i < points.Length; i++) center += points[i];
            center /= points.Length;
            for (int d = 0; d < directions.Length; d++)
            {
                AddExtreme(points, center, directions[d], chosen);
            }

            if (extra != null)
            {
                for (int d = 0; d < extra.Length; d++) AddExtreme(points, center, extra[d], chosen);
            }

            var result = new D3[chosen.Count];
            int k = 0;
            foreach (int index in chosen) result[k++] = points[index];
            return result;
        }

        private static void AddExtreme(D3[] points, D3 center, D3 direction, HashSet<int> chosen)
        {
            int best = -1;
            double bestDot = double.NegativeInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                double dot = D3.Dot(points[i] - center, direction);
                if (dot <= bestDot) continue;
                bestDot = dot;
                best = i;
            }

            if (best >= 0) chosen.Add(best);
        }

        /// <summary>병합 비용 평가용 대리 껍질(정점 ≤ 약 100).</summary>
        public static HullShape Proxy(HullShape hull)
        {
            if (hull.Vertices.Length <= ProxyDirections.Length) return hull;
            D3[] subset = ExtremeSubset(hull.Vertices, ProxyDirections, ConcaveMath.SeedDirections);
            return HullShape.FromPoints(subset, subset.Length) ?? hull;
        }

        /// <summary>
        /// EMBER 적응형 정점 축소: 14개 극점 방향으로 시드한 뒤 부피 증가가 가장 큰 정점을 탐욕적으로 추가,
        /// 원래 부피의 keep(97%)에 도달하거나 budget에 닿으면 멈춘다(내접, 절대 튀어나오지 않음).
        /// </summary>
        public static HullShape Reduce(HullShape full, int budget, double keep = VolumeKeep)
        {
            if (full == null || budget < 4) return full;
            D3[] pool = full.Vertices.Length > PoolDirections.Length
                ? ExtremeSubset(full.Vertices, PoolDirections, ConcaveMath.SeedDirections)
                : full.Vertices;
            if (pool.Length <= 4) return full;

            D3 center = (full.Min + full.Max) * 0.5;
            var used = new bool[pool.Length];
            var chosen = new List<D3>(budget);
            for (int d = 0; d < ConcaveMath.SeedDirections.Length && chosen.Count < budget; d++)
            {
                int extreme = -1;
                double extremeDot = double.NegativeInfinity;
                for (int i = 0; i < pool.Length; i++)
                {
                    double dot = D3.Dot(pool[i] - center, ConcaveMath.SeedDirections[d]);
                    if (dot <= extremeDot) continue;
                    extremeDot = dot;
                    extreme = i;
                }

                if (extreme < 0 || used[extreme]) continue;
                used[extreme] = true;
                chosen.Add(pool[extreme]);
            }

            HullShape current = null;
            var faceCross = new List<D3>();
            var faceOrigin = new List<D3>();
            while (true)
            {
                HullShape next = chosen.Count >= 4 ? HullShape.FromPoints(chosen) : null;
                if (next == null)
                {
                    // 시드가 공면이면 부피가 큰 점을 하나 더 넣어 다시 시도한다.
                    int far = FarthestUnused(pool, used, chosen);
                    if (far < 0) break;
                    used[far] = true;
                    chosen.Add(pool[far]);
                    continue;
                }

                current = next;
                if (chosen.Count >= budget) break;
                if (current.Volume >= full.Volume * keep) break;

                faceCross.Clear();
                faceOrigin.Clear();
                for (int t = 0; t + 2 < current.Triangles.Length; t += 3)
                {
                    D3 a = current.Vertices[current.Triangles[t]];
                    faceCross.Add(D3.Cross(current.Vertices[current.Triangles[t + 1]] - a, current.Vertices[current.Triangles[t + 2]] - a));
                    faceOrigin.Add(a);
                }

                int bestIndex = -1;
                double bestGain = full.Volume * 1e-6;
                for (int i = 0; i < pool.Length; i++)
                {
                    if (used[i]) continue;
                    double gain = 0;
                    D3 p = pool[i];
                    for (int f = 0; f < faceCross.Count; f++)
                    {
                        double signed = D3.Dot(faceCross[f], p - faceOrigin[f]);
                        if (signed > 0) gain += signed;
                    }

                    gain /= 6.0;
                    if (gain <= bestGain) continue;
                    bestGain = gain;
                    bestIndex = i;
                }

                if (bestIndex < 0) break;
                used[bestIndex] = true;
                chosen.Add(pool[bestIndex]);
            }

            HullShape result = current != null && current.Vertices.Length < full.Vertices.Length ? current : full;
            if (result.Vertices.Length <= budget) return result;

            // 최후 수단: 예산 개수만큼의 방향 극점으로 만든 껍질(예산 초과 방지).
            for (int directions = budget; directions >= 4; directions -= 2)
            {
                D3[] subset = ExtremeSubset(full.Vertices, ConcaveMath.FibonacciDirections(directions));
                HullShape capped = HullShape.FromPoints(subset, subset.Length);
                if (capped != null && capped.Vertices.Length <= budget) return capped;
            }

            return result;
        }

        private static int FarthestUnused(D3[] pool, bool[] used, List<D3> chosen)
        {
            int best = -1;
            double bestDistance = -1;
            for (int i = 0; i < pool.Length; i++)
            {
                if (used[i]) continue;
                double nearest = double.PositiveInfinity;
                for (int c = 0; c < chosen.Count; c++) nearest = Math.Min(nearest, (pool[i] - chosen[c]).SqrLength);
                if (chosen.Count == 0) nearest = 1;
                if (nearest <= bestDistance) continue;
                bestDistance = nearest;
                best = i;
            }

            return best;
        }

        /// <summary>
        /// 평면으로 껍질을 자른다(normal 쪽을 남김). 남는 정점 + 모서리 교차점으로 껍질을 다시 만든다.
        /// 전부 남거나 전부 사라지면 null.
        /// </summary>
        public static HullShape Clip(HullShape hull, D3 planePoint, D3 planeNormal)
        {
            D3[] vertices = hull.Vertices;
            int[] triangles = hull.Triangles;
            var distances = new double[vertices.Length];
            int inside = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                distances[i] = D3.Dot(vertices[i] - planePoint, planeNormal);
                if (distances[i] >= 0) inside++;
            }

            if (inside == vertices.Length || inside == 0) return null;

            var points = new List<D3>(vertices.Length + 16);
            for (int i = 0; i < vertices.Length; i++)
            {
                if (distances[i] >= 0) points.Add(vertices[i]);
            }

            var edges = new HashSet<long>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = triangles[t + e];
                    int b = triangles[t + (e + 1) % 3];
                    long key = (long)Math.Min(a, b) * vertices.Length + Math.Max(a, b);
                    if (!edges.Add(key)) continue;
                    if ((distances[a] >= 0) == (distances[b] >= 0)) continue;
                    double t2 = distances[a] / (distances[a] - distances[b]);
                    points.Add(vertices[a] + (vertices[b] - vertices[a]) * t2);
                }
            }

            return HullShape.FromPoints(points);
        }

        /// <summary>두 껍질 정점 합집합의 껍질.</summary>
        public static HullShape Union(HullShape a, HullShape b)
        {
            var points = new D3[a.Vertices.Length + b.Vertices.Length];
            Array.Copy(a.Vertices, 0, points, 0, a.Vertices.Length);
            Array.Copy(b.Vertices, 0, points, a.Vertices.Length, b.Vertices.Length);
            return HullShape.FromPoints(points, points.Length);
        }

        /// <summary>껍질 부피 중 다른 껍질들로 덮인 비율(몬테카를로, 결정적 시드).</summary>
        public static double CoveredFraction(HullShape hull, IList<HullShape> others, int samples, ulong seed)
        {
            var rng = new ConcaveMath.Rng(seed);
            D3 size = hull.Size;
            int inside = 0;
            int covered = 0;
            for (int i = 0; i < samples; i++)
            {
                var p = new D3(hull.Min.X + rng.NextDouble() * size.X, hull.Min.Y + rng.NextDouble() * size.Y, hull.Min.Z + rng.NextDouble() * size.Z);
                if (!hull.Contains(p)) continue;
                inside++;
                for (int o = 0; o < others.Count; o++)
                {
                    if (others[o] == null || others[o] == hull || !others[o].Contains(p)) continue;
                    covered++;
                    break;
                }
            }

            return inside > 0 ? covered / (double)inside : 1.0;
        }

        /// <summary>점들을 격자 해시로 중복 제거(상대 허용오차).</summary>
        public static List<D3> Deduplicate(List<D3> points, double relativeTolerance = 1e-7)
        {
            if (points.Count < 2) return points;
            D3 min = points[0], max = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                min = D3.Min(min, points[i]);
                max = D3.Max(max, points[i]);
            }

            D3 size = max - min;
            double extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (!(extent > 0)) return new List<D3> { points[0] };
            double cell = extent * relativeTolerance;
            var seen = new HashSet<(long, long, long)>();
            var result = new List<D3>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                D3 p = points[i];
                var key = ((long)Math.Floor((p.X - min.X) / cell), (long)Math.Floor((p.Y - min.Y) / cell), (long)Math.Floor((p.Z - min.Z) / cell));
                if (seen.Add(key)) result.Add(p);
            }

            return result;
        }

        /// <summary>얇은(거의 평평한) 점군에 최소 두께를 준다. 셸 메시·평면 조각 대응.</summary>
        public static List<D3> Thicken(List<D3> points, double thickness)
        {
            if (points.Count == 0) return points;
            D3[] array = points.ToArray();
            ConcaveMath.PrincipalAxes(array, array.Length, out D3 mean, out Frame3 frame, out D3 _);
            D3 normal = frame.AxisZ;
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            for (int i = 0; i < array.Length; i++)
            {
                double d = D3.Dot(array[i] - mean, normal);
                lo = Math.Min(lo, d);
                hi = Math.Max(hi, d);
            }

            double missing = thickness - (hi - lo);
            if (missing <= 0) return points;
            var result = new List<D3>(array.Length * 2);
            D3 offset = normal * (missing * 0.5);
            for (int i = 0; i < array.Length; i++)
            {
                result.Add(array[i] + offset);
                result.Add(array[i] - offset);
            }

            return result;
        }
    }
}
