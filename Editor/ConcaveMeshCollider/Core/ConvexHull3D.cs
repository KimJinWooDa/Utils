using System;
using System.Collections.Generic;

namespace TelleR.ConcaveCollider
{
    /// <summary>
    /// 순수 C# QuickHull(double). 입력은 내부에서 단위 크기로 정규화한 뒤 상대 허용오차로 판정하므로
    /// 0.002 스케일 본 공간이나 500 스케일 좌표에서도 같은 결과를 낸다. 동일/공면/공선 입력은 실패(false)로 알린다.
    /// 인스턴스는 버퍼를 재사용하며 스레드 간 공유하지 않는다(<see cref="ForCurrentThread"/>).
    /// 출력 삼각형은 cross(b-a, c-a)가 바깥을 향한다(Unity 메시 앞면 규칙과 같다).
    /// </summary>
    internal sealed class ConvexHull3D
    {
        [ThreadStatic] private static ConvexHull3D threadInstance;

        public static ConvexHull3D ForCurrentThread => threadInstance ?? (threadInstance = new ConvexHull3D());

        private D3[] source;
        private D3[] work = new D3[64];
        private int pointCount;
        private double tolerance;

        private int faceCount;
        private int[] faceVertex = new int[192];
        private int[] faceNeighbor = new int[192];
        private D3[] faceNormal = new D3[64];
        private double[] faceOffset = new double[64];
        private bool[] faceAlive = new bool[64];
        private int[] faceOutside = new int[64];
        private int[] faceVisit = new int[64];
        private int visitStamp;

        private int[] nextOutside = new int[64];
        private int[] startAt = new int[64];
        private int[] endAt = new int[64];
        private int[] vertexStamp = new int[64];
        private int stampCounter;

        private readonly List<int> visible = new List<int>();
        private readonly List<int> horizonFace = new List<int>();
        private readonly List<int> horizonEdge = new List<int>();
        private readonly List<int> horizonNeighbor = new List<int>();
        private readonly List<int> orphans = new List<int>();
        private readonly List<int> newFaces = new List<int>();
        private readonly Stack<int> stack = new Stack<int>();
        private readonly Stack<int> pending = new Stack<int>();
        private readonly Dictionary<long, int> edgeMap = new Dictionary<long, int>();

        /// <summary>진단용 실패 사유 카운터(코드별 누적).</summary>
        internal static readonly int[] FailureCounts = new int[32];

        private static bool Fail(int code)
        {
            System.Threading.Interlocked.Increment(ref FailureCounts[code]);
            return false;
        }

        /// <summary>마지막 Build 결과의 부피(원래 좌표 단위).</summary>
        public double Volume { get; private set; }

        /// <summary>
        /// 볼록 껍질을 만든다. relativeTolerance는 정규화된(최대 크기 1) 좌표 기준 거리 허용오차.
        /// 실패하면 결정적 미세 흔들기로 두 번 더 시도한다.
        /// </summary>
        public bool Build(D3[] points, int count, double relativeTolerance = 1e-10)
        {
            Volume = 0;
            faceCount = 0;
            if (points == null || count < 4) return false;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (!Prepare(points, count, attempt)) return false;
                tolerance = Math.Max(relativeTolerance, 1e-13);
                bool ok;
                try
                {
                    ok = Run();
                }
                catch (IndexOutOfRangeException)
                {
                    ok = false;
                }

                if (ok)
                {
                    ComputeVolume();
                    if (Volume > 0 && ConcaveMath.IsFinite(Volume)) return true;
                }
            }

            faceCount = 0;
            Volume = 0;
            return false;
        }

        private bool Prepare(D3[] points, int count, int attempt)
        {
            source = points;
            pointCount = count;
            if (work.Length < count) work = new D3[Math.Max(count, work.Length * 2)];

            D3 min = points[0], max = points[0];
            for (int i = 1; i < count; i++)
            {
                min = D3.Min(min, points[i]);
                max = D3.Max(max, points[i]);
            }

            D3 size = max - min;
            double extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (!(extent > 0) || !ConcaveMath.IsFinite(extent)) return false;

            D3 center = (min + max) * 0.5;
            double inverse = 1.0 / extent;
            for (int i = 0; i < count; i++)
            {
                D3 p = (points[i] - center) * inverse;
                if (attempt > 0)
                {
                    // 공면 퇴화 탈출용 결정적 흔들기(1e-9 상대).
                    uint hash = (uint)(i * 2654435761u + attempt * 97u);
                    double jx = ((hash & 1023) / 1023.0 - 0.5) * 2e-9 * attempt;
                    double jy = (((hash >> 10) & 1023) / 1023.0 - 0.5) * 2e-9 * attempt;
                    double jz = (((hash >> 20) & 1023) / 1023.0 - 0.5) * 2e-9 * attempt;
                    p = new D3(p.X + jx, p.Y + jy, p.Z + jz);
                }

                work[i] = p;
            }

            if (nextOutside.Length < count)
            {
                int size2 = Math.Max(count, nextOutside.Length * 2);
                nextOutside = new int[size2];
                startAt = new int[size2];
                endAt = new int[size2];
                vertexStamp = new int[size2];
                stampCounter = 0;
            }

            return true;
        }

        private bool Run()
        {
            faceCount = 0;
            int n = pointCount;
            D3[] p = work;

            // 초기 사면체
            int[] extremes = new int[6];
            for (int axis = 0; axis < 3; axis++)
            {
                int lo = 0, hi = 0;
                for (int i = 1; i < n; i++)
                {
                    if (p[i][axis] < p[lo][axis]) lo = i;
                    if (p[i][axis] > p[hi][axis]) hi = i;
                }

                extremes[axis * 2] = lo;
                extremes[axis * 2 + 1] = hi;
            }

            int i0 = -1, i1 = -1;
            double best = -1;
            for (int a = 0; a < 6; a++)
            {
                for (int b = a + 1; b < 6; b++)
                {
                    double d = (p[extremes[a]] - p[extremes[b]]).SqrLength;
                    if (d <= best) continue;
                    best = d;
                    i0 = extremes[a];
                    i1 = extremes[b];
                }
            }

            if (best <= tolerance * tolerance) return Fail(1);

            D3 dir = (p[i1] - p[i0]).NormalizedOr(D3.Zero);
            int i2 = -1;
            best = -1;
            for (int i = 0; i < n; i++)
            {
                double d = D3.Cross(p[i] - p[i0], dir).SqrLength;
                if (d <= best) continue;
                best = d;
                i2 = i;
            }

            if (best <= tolerance * tolerance) return Fail(2);

            D3 planeNormal = D3.Cross(p[i1] - p[i0], p[i2] - p[i0]).NormalizedOr(D3.Zero);
            int i3 = -1;
            best = -1;
            for (int i = 0; i < n; i++)
            {
                double d = Math.Abs(D3.Dot(p[i] - p[i0], planeNormal));
                if (d <= best) continue;
                best = d;
                i3 = i;
            }

            if (best <= tolerance) return Fail(3);

            int va = i0, vb = i1, vc = i2, vd = i3;
            if (D3.Dot(p[vd] - p[va], planeNormal) > 0)
            {
                int t = vb;
                vb = vc;
                vc = t;
            }

            EnsureFaceCapacity(8);
            int f0 = AddFace(va, vb, vc);
            int f1 = AddFace(vb, va, vd);
            int f2 = AddFace(vc, vb, vd);
            int f3 = AddFace(va, vc, vd);
            if (f0 < 0 || f1 < 0 || f2 < 0 || f3 < 0) return Fail(4);

            edgeMap.Clear();
            for (int f = 0; f < 4; f++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int u = faceVertex[f * 3 + e];
                    int v = faceVertex[f * 3 + (e + 1) % 3];
                    edgeMap[((long)u << 32) | (uint)v] = f * 3 + e;
                }
            }

            for (int f = 0; f < 4; f++)
            {
                for (int e = 0; e < 3; e++)
                {
                    int u = faceVertex[f * 3 + e];
                    int v = faceVertex[f * 3 + (e + 1) % 3];
                    if (!edgeMap.TryGetValue(((long)v << 32) | (uint)u, out int twin)) return Fail(5);
                    faceNeighbor[f * 3 + e] = twin / 3;
                }
            }

            pending.Clear();
            for (int i = 0; i < n; i++)
            {
                if (i == va || i == vb || i == vc || i == vd) continue;
                for (int f = 0; f < 4; f++)
                {
                    if (Distance(f, p[i]) <= tolerance) continue;
                    nextOutside[i] = faceOutside[f];
                    faceOutside[f] = i;
                    break;
                }
            }

            for (int f = 0; f < 4; f++)
            {
                if (faceOutside[f] >= 0) pending.Push(f);
            }

            // 각 점은 최대 한 번 eye가 되므로 AddPoint 호출 수는 n을 넘을 수 없다.
            int guard = 0;
            while (pending.Count > 0)
            {
                int face = pending.Pop();
                if (!faceAlive[face] || faceOutside[face] < 0) continue;
                if (++guard > n + 8) return Fail(6);
                if (!AddPoint(face)) return Fail(7);
            }

            return true;
        }

        private double Distance(int face, D3 point) => D3.Dot(faceNormal[face], point) - faceOffset[face];

        private void EnsureFaceCapacity(int extra)
        {
            int needed = faceCount + extra;
            if (needed <= faceAlive.Length) return;
            int size = Math.Max(needed, faceAlive.Length * 2);
            Array.Resize(ref faceVertex, size * 3);
            Array.Resize(ref faceNeighbor, size * 3);
            Array.Resize(ref faceNormal, size);
            Array.Resize(ref faceOffset, size);
            Array.Resize(ref faceAlive, size);
            Array.Resize(ref faceOutside, size);
            Array.Resize(ref faceVisit, size);
        }

        private int AddFace(int a, int b, int c)
        {
            D3 pa = work[a];
            D3 normal = D3.Cross(work[b] - pa, work[c] - pa);
            double length = normal.Length;
            if (!(length > 1e-300)) return -1;
            normal /= length;
            int f = faceCount++;
            faceVertex[f * 3] = a;
            faceVertex[f * 3 + 1] = b;
            faceVertex[f * 3 + 2] = c;
            faceNeighbor[f * 3] = faceNeighbor[f * 3 + 1] = faceNeighbor[f * 3 + 2] = -1;
            // 세 꼭짓점 평균으로 오프셋을 잡아 긴 삼각형에서의 반올림 편향을 줄인다.
            faceNormal[f] = normal;
            faceOffset[f] = (D3.Dot(normal, pa) + D3.Dot(normal, work[b]) + D3.Dot(normal, work[c])) / 3.0;
            faceAlive[f] = true;
            faceOutside[f] = -1;
            faceVisit[f] = 0;
            return f;
        }

        private bool AddPoint(int face)
        {
            D3[] p = work;
            int eye = -1;
            double eyeDistance = -1;
            for (int i = faceOutside[face]; i >= 0; i = nextOutside[i])
            {
                double d = Distance(face, p[i]);
                if (d <= eyeDistance) continue;
                eyeDistance = d;
                eye = i;
            }

            if (eye < 0) return Fail(8);
            D3 eyePoint = p[eye];

            // 보이는 면 집합(DFS)과 지평선
            visitStamp++;
            if (visitStamp == int.MaxValue)
            {
                Array.Clear(faceVisit, 0, faceVisit.Length);
                visitStamp = 1;
            }

            visible.Clear();
            horizonFace.Clear();
            horizonEdge.Clear();
            horizonNeighbor.Clear();
            stack.Clear();
            stack.Push(face);
            faceVisit[face] = visitStamp;
            while (stack.Count > 0)
            {
                int g = stack.Pop();
                visible.Add(g);
                for (int e = 0; e < 3; e++)
                {
                    int nb = faceNeighbor[g * 3 + e];
                    if (nb < 0) return Fail(9);
                    if (faceVisit[nb] == visitStamp) continue;
                    if (Distance(nb, eyePoint) > tolerance)
                    {
                        faceVisit[nb] = visitStamp;
                        stack.Push(nb);
                    }
                    else
                    {
                        horizonFace.Add(g);
                        horizonEdge.Add(e);
                        horizonNeighbor.Add(nb);
                    }
                }
            }

            // 지평선 가장자리가 보이는 면으로 다시 표시되었으면 제외(늦게 방문된 이웃)
            int horizonCount = 0;
            for (int h = 0; h < horizonFace.Count; h++)
            {
                if (faceVisit[horizonNeighbor[h]] == visitStamp) continue;
                horizonFace[horizonCount] = horizonFace[h];
                horizonEdge[horizonCount] = horizonEdge[h];
                horizonNeighbor[horizonCount] = horizonNeighbor[h];
                horizonCount++;
            }

            if (horizonCount < 3) return Fail(10);

            orphans.Clear();
            for (int v = 0; v < visible.Count; v++)
            {
                int g = visible[v];
                faceAlive[g] = false;
                for (int i = faceOutside[g]; i >= 0; i = nextOutside[i])
                {
                    if (i != eye) orphans.Add(i);
                }

                faceOutside[g] = -1;
            }

            stampCounter++;
            if (stampCounter == int.MaxValue)
            {
                Array.Clear(vertexStamp, 0, vertexStamp.Length);
                stampCounter = 1;
            }

            EnsureFaceCapacity(horizonCount);
            newFaces.Clear();
            for (int h = 0; h < horizonCount; h++)
            {
                int g = horizonFace[h];
                int e = horizonEdge[h];
                int u = faceVertex[g * 3 + e];
                int v = faceVertex[g * 3 + (e + 1) % 3];
                int nb = horizonNeighbor[h];
                int nf = AddFace(u, v, eye);
                if (nf < 0) return Fail(11);
                newFaces.Add(nf);
                faceNeighbor[nf * 3] = nb;

                bool linked = false;
                for (int k = 0; k < 3; k++)
                {
                    if (faceNeighbor[nb * 3 + k] != g) continue;
                    if (faceVertex[nb * 3 + k] != v || faceVertex[nb * 3 + (k + 1) % 3] != u) continue;
                    faceNeighbor[nb * 3 + k] = nf;
                    linked = true;
                    break;
                }

                if (!linked) return Fail(12);
                if (StartOf(u) >= 0) return Fail(13); // 지평선이 단순 고리가 아니다
                MarkStart(u, nf);
                MarkEnd(v, nf);
            }

            // 새 면끼리 연결: (u,v,eye)의 (v→eye) 이웃 = v에서 시작하는 면, (eye→u) 이웃 = u에서 끝나는 면
            for (int k = 0; k < newFaces.Count; k++)
            {
                int nf = newFaces[k];
                int u = faceVertex[nf * 3];
                int v = faceVertex[nf * 3 + 1];
                int next = StartOf(v);
                int prev = EndOf(u);
                if (next < 0 || prev < 0) return Fail(14);
                faceNeighbor[nf * 3 + 1] = next;
                faceNeighbor[nf * 3 + 2] = prev;
            }

            // 지평선이 단일 고리인지 확인
            {
                int first = newFaces[0];
                int current = first;
                int steps = 0;
                do
                {
                    current = faceNeighbor[current * 3 + 1];
                    steps++;
                    if (steps > newFaces.Count) return Fail(15);
                }
                while (current != first);

                if (steps != newFaces.Count) return Fail(16);
            }

            for (int o = 0; o < orphans.Count; o++)
            {
                int i = orphans[o];
                D3 point = p[i];
                for (int k = 0; k < newFaces.Count; k++)
                {
                    int nf = newFaces[k];
                    if (Distance(nf, point) <= tolerance) continue;
                    nextOutside[i] = faceOutside[nf];
                    faceOutside[nf] = i;
                    break;
                }
            }

            for (int k = 0; k < newFaces.Count; k++)
            {
                if (faceOutside[newFaces[k]] >= 0) pending.Push(newFaces[k]);
            }

            return true;
        }

        // 꼭짓점별 시작/끝 면 기록(스탬프로 초기화 비용 제거)
        private void MarkStart(int vertex, int face)
        {
            if (vertexStamp[vertex] != stampCounter)
            {
                vertexStamp[vertex] = stampCounter;
                startAt[vertex] = -1;
                endAt[vertex] = -1;
            }

            startAt[vertex] = face;
        }

        private void MarkEnd(int vertex, int face)
        {
            if (vertexStamp[vertex] != stampCounter)
            {
                vertexStamp[vertex] = stampCounter;
                startAt[vertex] = -1;
                endAt[vertex] = -1;
            }

            endAt[vertex] = face;
        }

        private int StartOf(int vertex) => vertexStamp[vertex] == stampCounter ? startAt[vertex] : -1;
        private int EndOf(int vertex) => vertexStamp[vertex] == stampCounter ? endAt[vertex] : -1;

        private void ComputeVolume()
        {
            // 원래 좌표로 부피(정규화 전 source 기준). 원점 대신 첫 점 기준으로 상쇄 오차를 줄인다.
            double volume = 0;
            D3 origin = D3.Zero;
            bool hasOrigin = false;
            for (int f = 0; f < faceCount; f++)
            {
                if (!faceAlive[f]) continue;
                D3 a = source[faceVertex[f * 3]];
                if (!hasOrigin)
                {
                    origin = a;
                    hasOrigin = true;
                }

                D3 b = source[faceVertex[f * 3 + 1]] - origin;
                D3 c = source[faceVertex[f * 3 + 2]] - origin;
                volume += D3.Dot(a - origin, D3.Cross(b, c));
            }

            Volume = volume / 6.0;
        }

        /// <summary>마지막 Build 껍질의 겉넓이(원래 좌표 단위).</summary>
        public double ComputeArea()
        {
            double area = 0;
            for (int f = 0; f < faceCount; f++)
            {
                if (!faceAlive[f]) continue;
                D3 a = source[faceVertex[f * 3]];
                area += D3.Cross(source[faceVertex[f * 3 + 1]] - a, source[faceVertex[f * 3 + 2]] - a).Length * 0.5;
            }

            return area;
        }

        /// <summary>마지막 Build의 껍질을 원래 좌표 정점/삼각형으로 꺼낸다.</summary>
        public void GetHull(out D3[] vertices, out int[] triangles)
        {
            var remap = new Dictionary<int, int>();
            var verts = new List<D3>();
            var tris = new List<int>();
            for (int f = 0; f < faceCount; f++)
            {
                if (!faceAlive[f]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = faceVertex[f * 3 + k];
                    if (!remap.TryGetValue(v, out int index))
                    {
                        index = verts.Count;
                        remap.Add(v, index);
                        verts.Add(source[v]);
                    }

                    tris.Add(index);
                }
            }

            vertices = verts.ToArray();
            triangles = tris.ToArray();
        }

        /// <summary>마지막 Build의 껍질 정점만(원래 좌표).</summary>
        public D3[] GetHullVertices()
        {
            int stamp = ++stampCounter;
            var verts = new List<D3>();
            for (int f = 0; f < faceCount; f++)
            {
                if (!faceAlive[f]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = faceVertex[f * 3 + k];
                    if (vertexStamp[v] == stamp) continue;
                    vertexStamp[v] = stamp;
                    verts.Add(source[v]);
                }
            }

            return verts.ToArray();
        }

        /// <summary>편의 함수: 새 배열로 껍질을 만든다.</summary>
        public static bool TryBuild(D3[] points, int count, out D3[] vertices, out int[] triangles, out double volume, double relativeTolerance = 1e-10)
        {
            ConvexHull3D hull = ForCurrentThread;
            if (!hull.Build(points, count, relativeTolerance))
            {
                vertices = null;
                triangles = null;
                volume = 0;
                return false;
            }

            hull.GetHull(out vertices, out triangles);
            volume = hull.Volume;
            return true;
        }
    }
}
