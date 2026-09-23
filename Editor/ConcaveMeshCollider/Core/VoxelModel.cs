using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TelleR.ConcaveCollider
{
    /// <summary>
    /// 삼각형 → 복셀 격자. 표면은 삼각형/AABB 분리축(SAT) 검사, 내부는 바깥 테두리에서 flood fill.
    /// 열린 메시는 작은 구멍을 형태학적 닫힘(1~2복셀)으로 메워 보고, 그래도 안 되면 표면 셸만 쓰고 경고한다.
    /// 좌표는 작업 공간(단위 크기로 정규화된 좌표)이다.
    /// </summary>
    internal sealed class VoxelModel
    {
        public const byte Empty = 0;
        public const byte Surface = 1;
        public const byte Interior = 2;
        // 형태학적 닫힘 반경(최대 2) + SAT 여유 1복셀 + 바깥 시드 1복셀보다 커야 경계에 닿지 않는다.
        private const int Padding = 5;
        public const int PaddingVoxels = Padding;

        public int NX, NY, NZ;
        public double H;
        public D3 Origin;
        public byte[] State;
        public int[] IdOf;
        public int[] VX, VY, VZ;
        public int Count;
        public int SurfaceCount;
        public int InteriorCount;

        // 표면 복셀별 삼각형 목록(CSR, 복셀 id 기준)
        public int[] TriStart;
        public int[] TriList;

        public bool Watertight;
        public int OpenEdges;
        public bool Sealed;
        public bool ShellOnly;

        private D3[] points;
        private int[] triangles;

        public int Index(int x, int y, int z) => x + NX * (y + NY * z);

        public static VoxelModel Build(D3[] points, int[] triangles, int targetVoxels, int maxAxisVoxels, Func<bool> cancelled)
        {
            var model = new VoxelModel { points = points, triangles = triangles };
            model.Setup(targetVoxels, maxAxisVoxels);
            model.RasterizeSurface(cancelled);
            if (cancelled()) throw new OperationCanceledException();
            model.OpenEdges = CountOpenEdges(points, triangles);
            model.Watertight = model.OpenEdges == 0;
            model.FillInterior();
            model.AssignIds();
            return model;
        }

        private void Setup(int targetVoxels, int maxAxisVoxels)
        {
            D3 min = points[triangles[0]], max = min;
            for (int i = 0; i < triangles.Length; i++)
            {
                min = D3.Min(min, points[triangles[i]]);
                max = D3.Max(max, points[triangles[i]]);
            }

            D3 size = max - min;
            double extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (!(extent > 0)) extent = 1;
            double floorSize = extent * 1e-3;
            double sx = Math.Max(size.X, floorSize), sy = Math.Max(size.Y, floorSize), sz = Math.Max(size.Z, floorSize);
            double h = Math.Pow(sx * sy * sz / Math.Max(1000, targetVoxels), 1.0 / 3.0);
            h = Math.Max(h, extent / Math.Max(8, maxAxisVoxels));
            H = h;
            int cx = Math.Max(1, (int)Math.Ceiling(size.X / h - 1e-9));
            int cy = Math.Max(1, (int)Math.Ceiling(size.Y / h - 1e-9));
            int cz = Math.Max(1, (int)Math.Ceiling(size.Z / h - 1e-9));
            NX = cx + Padding * 2;
            NY = cy + Padding * 2;
            NZ = cz + Padding * 2;
            D3 center = (min + max) * 0.5;
            Origin = center - new D3(NX * h, NY * h, NZ * h) * 0.5;
            State = new byte[NX * NY * NZ];
        }

        private void RasterizeSurface(Func<bool> cancelled)
        {
            int triangleCount = triangles.Length / 3;
            var pairs = new List<long>();
            object gate = new object();
            double invH = 1.0 / H;
            Parallel.For(0, triangleCount, () => new List<long>(), (t, loop, local) =>
            {
                D3 a = (points[triangles[t * 3]] - Origin) * invH;
                D3 b = (points[triangles[t * 3 + 1]] - Origin) * invH;
                D3 c = (points[triangles[t * 3 + 2]] - Origin) * invH;
                RasterizeTriangle(a, b, c, t, local);
                return local;
            }, local =>
            {
                lock (gate) pairs.AddRange(local);
            });

            if (cancelled()) throw new OperationCanceledException();

            long[] sorted = pairs.ToArray();
            Array.Sort(sorted);
            pairTable = sorted;
        }

        private long[] pairTable;

        // 복셀 단위 좌표(h = 1)에서 삼각형 하나를 래스터화한다. 지배 법선 축 방향 기둥마다 평면 범위만 SAT 검사.
        private void RasterizeTriangle(D3 a, D3 b, D3 c, int triangle, List<long> output)
        {
            D3 normal = D3.Cross(b - a, c - a);
            double area2 = normal.Length;
            if (!(area2 > 1e-14)) return;

            D3 tmin = D3.Min(a, D3.Min(b, c));
            D3 tmax = D3.Max(a, D3.Max(b, c));
            const double margin = 1e-6;
            int x0 = Math.Max(0, (int)Math.Floor(tmin.X - margin)), x1 = Math.Min(NX - 1, (int)Math.Floor(tmax.X + margin));
            int y0 = Math.Max(0, (int)Math.Floor(tmin.Y - margin)), y1 = Math.Min(NY - 1, (int)Math.Floor(tmax.Y + margin));
            int z0 = Math.Max(0, (int)Math.Floor(tmin.Z - margin)), z1 = Math.Min(NZ - 1, (int)Math.Floor(tmax.Z + margin));

            double ax = Math.Abs(normal.X), ay = Math.Abs(normal.Y), az = Math.Abs(normal.Z);
            int w = ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
            int u = (w + 1) % 3, v = (w + 2) % 3;
            int[] lo = { x0, y0, z0 };
            int[] hi = { x1, y1, z1 };
            double nw = normal[w], nu = normal[u], nv = normal[v];
            double aw = a[w], au = a[u], av = a[v];
            var cell = new int[3];

            for (int iu = lo[u]; iu <= hi[u]; iu++)
            {
                for (int iv = lo[v]; iv <= hi[v]; iv++)
                {
                    // 기둥 사각형 네 모서리에서 평면의 w 좌표 범위
                    double wMin = double.PositiveInfinity, wMax = double.NegativeInfinity;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        double pu = iu + (corner & 1);
                        double pv = iv + (corner >> 1);
                        double pw = aw - (nu * (pu - au) + nv * (pv - av)) / nw;
                        if (pw < wMin) wMin = pw;
                        if (pw > wMax) wMax = pw;
                    }

                    int k0 = Math.Max(lo[w], (int)Math.Floor(wMin - margin));
                    int k1 = Math.Min(hi[w], (int)Math.Floor(wMax + margin));
                    for (int k = k0; k <= k1; k++)
                    {
                        cell[u] = iu;
                        cell[v] = iv;
                        cell[w] = k;
                        var center = new D3(cell[0] + 0.5, cell[1] + 0.5, cell[2] + 0.5);
                        if (!TriangleBoxOverlap(center, 0.5 + margin, a, b, c)) continue;
                        int index = cell[0] + NX * (cell[1] + NY * cell[2]);
                        State[index] = Surface; // 같은 값을 쓰는 경합이라 안전하다
                        output.Add(((long)index << 32) | (uint)triangle);
                    }
                }
            }
        }

        /// <summary>Akenine-Möller 삼각형/AABB 분리축 검사.</summary>
        internal static bool TriangleBoxOverlap(D3 center, double half, D3 a, D3 b, D3 c)
        {
            D3 v0 = a - center, v1 = b - center, v2 = c - center;
            D3 e0 = v1 - v0, e1 = v2 - v1, e2 = v0 - v2;

            if (!AxisTest(e0, v0, v1, v2, half)) return false;
            if (!AxisTest(e1, v0, v1, v2, half)) return false;
            if (!AxisTest(e2, v0, v1, v2, half)) return false;

            if (Math.Min(v0.X, Math.Min(v1.X, v2.X)) > half || Math.Max(v0.X, Math.Max(v1.X, v2.X)) < -half) return false;
            if (Math.Min(v0.Y, Math.Min(v1.Y, v2.Y)) > half || Math.Max(v0.Y, Math.Max(v1.Y, v2.Y)) < -half) return false;
            if (Math.Min(v0.Z, Math.Min(v1.Z, v2.Z)) > half || Math.Max(v0.Z, Math.Max(v1.Z, v2.Z)) < -half) return false;

            D3 normal = D3.Cross(e0, e1);
            double d = D3.Dot(normal, v0);
            double r = half * (Math.Abs(normal.X) + Math.Abs(normal.Y) + Math.Abs(normal.Z));
            return Math.Abs(d) <= r;
        }

        // 모서리 e와 박스 세 축의 외적 축 검사
        private static bool AxisTest(D3 e, D3 v0, D3 v1, D3 v2, double half)
        {
            double fx = Math.Abs(e.X), fy = Math.Abs(e.Y), fz = Math.Abs(e.Z);

            // X × e
            double p0 = e.Z * v0.Y - e.Y * v0.Z;
            double p1 = e.Z * v1.Y - e.Y * v1.Z;
            double p2 = e.Z * v2.Y - e.Y * v2.Z;
            double r = half * (fz + fy);
            if (Math.Min(p0, Math.Min(p1, p2)) > r || Math.Max(p0, Math.Max(p1, p2)) < -r) return false;

            // Y × e
            p0 = -e.Z * v0.X + e.X * v0.Z;
            p1 = -e.Z * v1.X + e.X * v1.Z;
            p2 = -e.Z * v2.X + e.X * v2.Z;
            r = half * (fz + fx);
            if (Math.Min(p0, Math.Min(p1, p2)) > r || Math.Max(p0, Math.Max(p1, p2)) < -r) return false;

            // Z × e
            p0 = e.Y * v0.X - e.X * v0.Y;
            p1 = e.Y * v1.X - e.X * v1.Y;
            p2 = e.Y * v2.X - e.X * v2.Y;
            r = half * (fy + fx);
            if (Math.Min(p0, Math.Min(p1, p2)) > r || Math.Max(p0, Math.Max(p1, p2)) < -r) return false;
            return true;
        }

        /// <summary>위치로 용접한 뒤 한 면에만 속한 모서리 수(열린 가장자리).</summary>
        private static int CountOpenEdges(D3[] points, int[] triangles)
        {
            D3 min = points[triangles[0]], max = min;
            for (int i = 0; i < triangles.Length; i++)
            {
                min = D3.Min(min, points[triangles[i]]);
                max = D3.Max(max, points[triangles[i]]);
            }

            D3 size = max - min;
            double cell = Math.Max(size.X, Math.Max(size.Y, size.Z)) * 1e-6;
            if (!(cell > 0)) return 0;
            var weld = new Dictionary<(long, long, long), int>();
            var welded = new int[points.Length];
            for (int i = 0; i < points.Length; i++) welded[i] = -1;
            for (int i = 0; i < triangles.Length; i++)
            {
                int v = triangles[i];
                if (welded[v] >= 0) continue;
                D3 p = points[v];
                var key = ((long)Math.Round((p.X - min.X) / cell), (long)Math.Round((p.Y - min.Y) / cell), (long)Math.Round((p.Z - min.Z) / cell));
                if (!weld.TryGetValue(key, out int id))
                {
                    id = weld.Count;
                    weld.Add(key, id);
                }

                welded[v] = id;
            }

            var edges = new Dictionary<long, int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = welded[triangles[t + e]];
                    int b = welded[triangles[t + (e + 1) % 3]];
                    if (a == b) continue;
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    edges.TryGetValue(key, out int count);
                    edges[key] = count + 1;
                }
            }

            int open = 0;
            foreach (KeyValuePair<long, int> pair in edges)
            {
                if (pair.Value == 1) open++;
            }

            return open;
        }

        private void FillInterior()
        {
            int surface = 0;
            for (int i = 0; i < State.Length; i++)
            {
                if (State[i] == Surface) surface++;
            }

            byte[] plain = FloodInterior(0, out int plainInterior);
            if (plainInterior > 0 || Watertight)
            {
                // 닫힌 메시(내부가 없어도 얇은 판일 수 있음) 또는 기하적으로 닫힌 경우
                ApplyInterior(plain);
                return;
            }

            for (int radius = 1; radius <= 2; radius++)
            {
                byte[] closed = FloodInterior(radius, out int interior);
                if (interior >= Math.Max(1, surface / 50))
                {
                    Sealed = true;
                    ApplyInterior(closed);
                    return;
                }
            }

            ShellOnly = true;
            ApplyInterior(plain);
        }

        private void ApplyInterior(byte[] interiorMask)
        {
            for (int i = 0; i < State.Length; i++)
            {
                if (State[i] != Surface && interiorMask[i] != 0) State[i] = Interior;
            }
        }

        // 표면을 radius만큼 팽창시킨 벽으로 바깥 flood fill → 바깥을 다시 radius만큼 팽창(닫힘 연산).
        private byte[] FloodInterior(int radius, out int interiorCount)
        {
            int total = State.Length;
            var barrier = new byte[total];
            for (int i = 0; i < total; i++)
            {
                if (State[i] == Surface) barrier[i] = 1;
            }

            for (int r = 0; r < radius; r++) Dilate(barrier);

            var outside = new byte[total];
            var queue = new int[total];
            int head = 0, tail = 0;
            for (int z = 0; z < NZ; z++)
            {
                for (int y = 0; y < NY; y++)
                {
                    for (int x = 0; x < NX; x++)
                    {
                        if (x != 0 && y != 0 && z != 0 && x != NX - 1 && y != NY - 1 && z != NZ - 1) continue;
                        int i = Index(x, y, z);
                        if (barrier[i] != 0 || outside[i] != 0) continue;
                        outside[i] = 1;
                        queue[tail++] = i;
                    }
                }
            }

            int strideY = NX, strideZ = NX * NY;
            while (head < tail)
            {
                int i = queue[head++];
                int x = i % NX;
                int y = (i / NX) % NY;
                int z = i / strideZ;
                if (x > 0) Visit(i - 1);
                if (x < NX - 1) Visit(i + 1);
                if (y > 0) Visit(i - strideY);
                if (y < NY - 1) Visit(i + strideY);
                if (z > 0) Visit(i - strideZ);
                if (z < NZ - 1) Visit(i + strideZ);
            }

            void Visit(int j)
            {
                if (barrier[j] != 0 || outside[j] != 0) return;
                outside[j] = 1;
                queue[tail++] = j;
            }

            // 바깥을 다시 팽창(표면 복셀은 제외)
            for (int r = 0; r < radius; r++)
            {
                byte[] grown = (byte[])outside.Clone();
                Dilate(grown);
                for (int i = 0; i < total; i++)
                {
                    if (State[i] != Surface && grown[i] != 0) outside[i] = 1;
                }
            }

            var interior = new byte[total];
            interiorCount = 0;
            for (int i = 0; i < total; i++)
            {
                if (State[i] == Surface || outside[i] != 0) continue;
                int x = i % NX, y = (i / NX) % NY, z = i / strideZ;
                if (x == 0 || y == 0 || z == 0 || x == NX - 1 || y == NY - 1 || z == NZ - 1)
                {
                    // 닫힘이 격자 경계까지 막아 생긴 가짜 내부 → 이 반경은 쓰지 않는다.
                    interiorCount = 0;
                    return new byte[total];
                }

                interior[i] = 1;
                interiorCount++;
            }

            return interior;
        }

        // 26-이웃 1복셀 팽창
        private void Dilate(byte[] mask)
        {
            byte[] source = (byte[])mask.Clone();
            for (int z = 0; z < NZ; z++)
            {
                for (int y = 0; y < NY; y++)
                {
                    for (int x = 0; x < NX; x++)
                    {
                        if (source[Index(x, y, z)] == 0) continue;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            int zz = z + dz;
                            if (zz < 0 || zz >= NZ) continue;
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                int yy = y + dy;
                                if (yy < 0 || yy >= NY) continue;
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int xx = x + dx;
                                    if (xx < 0 || xx >= NX) continue;
                                    mask[Index(xx, yy, zz)] = 1;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void AssignIds()
        {
            IdOf = new int[State.Length];
            int count = 0;
            for (int i = 0; i < State.Length; i++)
            {
                if (State[i] == Empty)
                {
                    IdOf[i] = -1;
                    continue;
                }

                IdOf[i] = count++;
            }

            Count = count;
            VX = new int[count];
            VY = new int[count];
            VZ = new int[count];
            SurfaceCount = 0;
            InteriorCount = 0;
            for (int i = 0; i < State.Length; i++)
            {
                int id = IdOf[i];
                if (id < 0) continue;
                VX[id] = i % NX;
                VY[id] = (i / NX) % NY;
                VZ[id] = i / (NX * NY);
                if (State[i] == Surface) SurfaceCount++;
                else InteriorCount++;
            }

            // 표면 복셀 삼각형 목록(CSR)
            TriStart = new int[count + 1];
            long[] table = pairTable ?? Array.Empty<long>();
            var list = new List<int>(table.Length);
            long previous = -1;
            // table은 (grid index, tri) 오름차순. grid index 순서 = id 순서이므로 한 번에 채운다.
            int currentId = 0;
            for (int k = 0; k < table.Length; k++)
            {
                long pair = table[k];
                int grid = (int)(pair >> 32);
                int tri = (int)(pair & 0xffffffff);
                int id = IdOf[grid];
                if (id < 0) continue;
                while (currentId <= id)
                {
                    TriStart[currentId] = list.Count;
                    currentId++;
                }

                if (pair == previous) continue;
                previous = pair;
                list.Add(tri);
            }

            while (currentId <= count)
            {
                TriStart[currentId] = list.Count;
                currentId++;
            }

            TriList = list.ToArray();
            pairTable = null;
        }

        public bool TryVoxelOf(D3 p, out int x, out int y, out int z)
        {
            x = (int)Math.Floor((p.X - Origin.X) / H);
            y = (int)Math.Floor((p.Y - Origin.Y) / H);
            z = (int)Math.Floor((p.Z - Origin.Z) / H);
            return x >= 0 && y >= 0 && z >= 0 && x < NX && y < NY && z < NZ;
        }

        /// <summary>
        /// 작업 공간 점이 원본 솔리드 안인지. 내부/외부 복셀은 즉시 판정하고, 표면 복셀은 같은 기둥(+Z)으로
        /// 광선 홀짝 검사를 한다(닫힌 메시에서만 정확). 열린 메시는 표면 복셀을 안쪽으로 본다.
        /// </summary>
        public bool InsideSolid(D3 p, List<int> scratch)
        {
            if (!TryVoxelOf(p, out int x, out int y, out int z)) return false;
            int index = Index(x, y, z);
            byte state = State[index];
            if (state == Empty) return false;
            if (state == Interior) return true;
            if (!Watertight) return true;

            // 모서리 정확히 지나는 광선을 피하기 위한 미세 흔들기
            double px = p.X + H * 1.2345e-6, py = p.Y + H * 2.3456e-6, pz = p.Z;
            scratch.Clear();
            int k = z;
            bool baseInside = false;
            while (k < NZ)
            {
                int i = Index(x, y, k);
                byte s = State[i];
                if (s != Surface)
                {
                    baseInside = s == Interior;
                    break;
                }

                int id = IdOf[i];
                for (int t = TriStart[id]; t < TriStart[id + 1]; t++)
                {
                    int tri = TriList[t];
                    if (!scratch.Contains(tri)) scratch.Add(tri);
                }

                k++;
            }

            double zEnd = Origin.Z + k * H;
            int crossings = 0;
            for (int s = 0; s < scratch.Count; s++)
            {
                int tri = scratch[s];
                D3 a = points[triangles[tri * 3]];
                D3 b = points[triangles[tri * 3 + 1]];
                D3 c = points[triangles[tri * 3 + 2]];
                if (VerticalHit(px, py, a, b, c, out double hz) && hz > pz && hz < zEnd) crossings++;
            }

            return baseInside ^ ((crossings & 1) == 1);
        }

        // 수직선 (px,py)와 삼각형의 교점 z
        private static bool VerticalHit(double px, double py, D3 a, D3 b, D3 c, out double hz)
        {
            hz = 0;
            double d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
            if (Math.Abs(d) < 1e-300) return false;
            double l1 = ((b.Y - c.Y) * (px - c.X) + (c.X - b.X) * (py - c.Y)) / d;
            double l2 = ((c.Y - a.Y) * (px - c.X) + (a.X - c.X) * (py - c.Y)) / d;
            double l3 = 1 - l1 - l2;
            if (l1 < 0 || l2 < 0 || l3 < 0) return false;
            hz = l1 * a.Z + l2 * b.Z + l3 * c.Z;
            return true;
        }

        /// <summary>작업 공간 점에서 가까운 원본 삼각형 위의 최근접점(검색 반경 = radiusVoxels 복셀).</summary>
        public bool TryClosestSurfacePoint(D3 p, int radiusVoxels, double maxDistance, out D3 closest)
        {
            closest = p;
            if (!TryVoxelOf(p, out int x, out int y, out int z)) return false;
            double best = maxDistance * maxDistance;
            bool found = false;
            for (int dz = -radiusVoxels; dz <= radiusVoxels; dz++)
            {
                int zz = z + dz;
                if (zz < 0 || zz >= NZ) continue;
                for (int dy = -radiusVoxels; dy <= radiusVoxels; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= NY) continue;
                    for (int dx = -radiusVoxels; dx <= radiusVoxels; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= NX) continue;
                        int i = Index(xx, yy, zz);
                        if (State[i] != Surface) continue;
                        int id = IdOf[i];
                        for (int t = TriStart[id]; t < TriStart[id + 1]; t++)
                        {
                            int tri = TriList[t];
                            D3 q = Geometry.ClosestPointOnTriangle(p, points[triangles[tri * 3]], points[triangles[tri * 3 + 1]], points[triangles[tri * 3 + 2]]);
                            double d = (q - p).SqrLength;
                            if (d >= best) continue;
                            best = d;
                            closest = q;
                            found = true;
                        }
                    }
                }
            }

            return found;
        }
    }

    internal static class Geometry
    {
        public static D3 ClosestPointOnTriangle(D3 p, D3 a, D3 b, D3 c)
        {
            D3 ab = b - a;
            D3 ac = c - a;
            D3 ap = p - a;
            double d1 = D3.Dot(ab, ap);
            double d2 = D3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return a;

            D3 bp = p - b;
            double d3 = D3.Dot(ab, bp);
            double d4 = D3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return b;

            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));

            D3 cp = p - c;
            double d5 = D3.Dot(ab, cp);
            double d6 = D3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return c;

            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));

            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

            double denominator = 1.0 / (va + vb + vc);
            return a + ab * (vb * denominator) + ac * (vc * denominator);
        }
    }
}
