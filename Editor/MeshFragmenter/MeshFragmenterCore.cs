using System;
using System.Collections.Generic;
using UnityEngine;

namespace TelleR
{
    /// <summary>
    /// 보로노이 셀(시드 사이의 수직 이등분 평면)로 메쉬를 잘라, 단면을 막은 닫힌 조각 메쉬 데이터를 만든다.
    /// 씬과 에셋은 건드리지 않고, 전역 UnityEngine.Random 상태도 바꾸지 않는다.
    /// </summary>
    internal static class MeshFragmenterCore
    {
        internal sealed class Piece
        {
            public List<Vector3> Positions;
            public List<Vector3> Normals;
            public List<Vector2> Uvs;
            public List<Color> Colors;
            /// <summary>원본 서브메쉬 순서 그대로, 마지막 하나는 단면(cap) 서브메쉬.</summary>
            public List<int>[] SubTriangles;
            /// <summary>원본 메쉬 로컬 좌표에서의 조각 중심(바운즈 중심). Positions는 이 점 기준이다.</summary>
            public Vector3 Center;
            public Vector3 Size;
            public float Volume;
        }

        internal sealed class Result
        {
            public readonly List<Piece> Pieces = new List<Piece>();
            public bool SourceClosed;
            public bool HadNormals;
            public bool HadTangents;
            /// <summary>열린 단면(닫히지 않은 윤곽선)이 생겨 막지 못한 횟수.</summary>
            public int OpenCuts;
            public string Error;
        }

        /// <summary>진행률 콜백. true를 반환하면 취소한다.</summary>
        internal delegate bool Progress(float t, string message);

        private sealed class Work
        {
            public readonly List<Vector3> Pos;
            public readonly List<Vector3> Nrm;
            public readonly List<Vector2> Uv;
            public readonly List<Color> Col;
            public readonly List<int>[] Sub;

            public Work(int subCount, int capacity, bool hasColor)
            {
                Pos = new List<Vector3>(capacity);
                Nrm = new List<Vector3>(capacity);
                Uv = new List<Vector2>(capacity);
                Col = hasColor ? new List<Color>(capacity) : null;
                Sub = new List<int>[subCount];
                for (int i = 0; i < subCount; i++) Sub[i] = new List<int>();
            }

            public int TriangleIndexCount
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < Sub.Length; i++) n += Sub[i].Count;
                    return n;
                }
            }

            public int Add(Vector3 p, Vector3 n, Vector2 uv, Color c)
            {
                Pos.Add(p);
                Nrm.Add(n);
                Uv.Add(uv);
                if (Col != null) Col.Add(c);
                return Pos.Count - 1;
            }
        }

        private struct Segment
        {
            public Vector3 A, B;
        }

        // 부동소수 오차에 강하도록 좌표축과 어긋난 세 방향으로 광선 판정을 해 다수결로 내부 여부를 정한다.
        private static readonly Vector3[] RayDirections =
        {
            new Vector3(0.5773f, 0.5774f, 0.5773f).normalized,
            new Vector3(-0.6124f, 0.3536f, 0.7071f).normalized,
            new Vector3(0.2673f, -0.8018f, 0.5345f).normalized
        };

        internal static Result Split(Mesh source, int count, int seed, Progress progress)
        {
            var result = new Result();
            Work src;
            try
            {
                src = Read(source, result);
            }
            catch (Exception e)
            {
                result.Error = "메쉬 데이터를 읽지 못했습니다: " + e.Message;
                return result;
            }

            if (src == null) return result;

            // 삼각형이 쓰는 정점만으로 바운즈를 잡는다(떨어진 미사용 정점이 있으면 시드가 메쉬 밖에 흩어진다).
            Bounds bounds = ReferencedBounds(src, source.bounds);

            float size = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (size <= 1e-8f)
            {
                result.Error = "메쉬 크기가 0이라 자를 수 없습니다.";
                return result;
            }

            result.SourceClosed = IsClosed(src);
            count = Mathf.Max(1, count);
            Vector3[] seeds = PickSeeds(src, bounds, count, seed, result.SourceClosed);

            float eps = size * 1e-6f;
            float uvScale = 1f / size;
            var order = new List<int>(count);
            var distances = new float[count];

            for (int i = 0; i < count; i++)
            {
                if (progress != null && progress((float)i / count, "조각 " + (i + 1) + " / " + count + " 자르는 중")) return null;

                // 가까운 이웃의 평면부터 자르면 남는 삼각형이 빨리 줄어든다.
                order.Clear();
                for (int j = 0; j < count; j++)
                {
                    if (j == i) continue;
                    distances[j] = (seeds[j] - seeds[i]).sqrMagnitude;
                    order.Add(j);
                }

                order.Sort((a, b) => distances[a].CompareTo(distances[b]));

                Work cell = src;
                for (int k = 0; k < order.Count && cell != null; k++)
                {
                    int j = order[k];
                    Vector3 delta = seeds[j] - seeds[i];
                    if (delta.sqrMagnitude < 1e-20f) continue;
                    Vector3 n = delta.normalized;
                    float d = Vector3.Dot(n, (seeds[i] + seeds[j]) * 0.5f);
                    cell = Clip(cell, n, d, eps, uvScale, result);
                }

                if (cell == null || cell.TriangleIndexCount == 0) continue;
                Piece piece = ToPiece(cell);
                if (piece != null) result.Pieces.Add(piece);
            }

            return result;
        }

        private static Work Read(Mesh mesh, Result result)
        {
            int subCount = mesh.subMeshCount;
            var positions = new List<Vector3>();
            mesh.GetVertices(positions);
            if (positions.Count == 0)
            {
                result.Error = mesh.vertexCount > 0 && !mesh.isReadable
                    ? "메쉬의 Read/Write가 꺼져 있어 정점 데이터를 읽을 수 없습니다. 모델 임포트 설정에서 Read/Write를 켜고 다시 시도하세요."
                    : "메쉬에 정점이 없습니다.";
                return null;
            }

            int vc = positions.Count;
            var normals = new List<Vector3>();
            mesh.GetNormals(normals);
            result.HadNormals = normals.Count == vc;
            result.HadTangents = mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent);
            var uvs = new List<Vector2>();
            mesh.GetUVs(0, uvs);
            var colors = new List<Color>();
            mesh.GetColors(colors);
            bool hasColor = colors.Count == vc;

            var work = new Work(subCount + 1, vc, hasColor);
            for (int i = 0; i < vc; i++)
            {
                work.Add(positions[i],
                    result.HadNormals ? normals[i] : Vector3.zero,
                    uvs.Count == vc ? uvs[i] : Vector2.zero,
                    hasColor ? colors[i] : Color.white);
            }

            var indices = new List<int>();
            int triangleIndices = 0;
            for (int s = 0; s < subCount; s++)
            {
                MeshTopology topology = mesh.GetTopology(s);
                if (topology == MeshTopology.Triangles)
                {
                    mesh.GetTriangles(indices, s);
                    work.Sub[s].AddRange(indices);
                }
                else if (topology == MeshTopology.Quads)
                {
                    mesh.GetIndices(indices, s);
                    List<int> dst = work.Sub[s];
                    for (int q = 0; q + 3 < indices.Count; q += 4)
                    {
                        dst.Add(indices[q]); dst.Add(indices[q + 1]); dst.Add(indices[q + 2]);
                        dst.Add(indices[q]); dst.Add(indices[q + 2]); dst.Add(indices[q + 3]);
                    }
                }

                triangleIndices += work.Sub[s].Count;
            }

            if (triangleIndices == 0)
            {
                result.Error = mesh.vertexCount > 0 && !mesh.isReadable
                    ? "메쉬의 Read/Write가 꺼져 있어 삼각형 데이터를 읽을 수 없습니다. 모델 임포트 설정에서 Read/Write를 켜고 다시 시도하세요."
                    : "삼각형(또는 사각형) 서브메쉬가 없습니다.";
                return null;
            }

            return work;
        }

        // ─── Seeds ───

        private static Bounds ReferencedBounds(Work w, Bounds fallback)
        {
            bool any = false;
            Vector3 min = Vector3.zero, max = Vector3.zero;
            for (int s = 0; s < w.Sub.Length; s++)
            {
                List<int> tris = w.Sub[s];
                for (int t = 0; t < tris.Count; t++)
                {
                    Vector3 p = w.Pos[tris[t]];
                    if (!any)
                    {
                        min = max = p;
                        any = true;
                    }
                    else
                    {
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                }
            }

            if (!any) return fallback;
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        private static Vector3[] PickSeeds(Work src, Bounds bounds, int count, int seed, bool closed)
        {
            // 전역 UnityEngine.Random을 건드리지 않도록 System.Random을 쓴다. 같은 시드면 같은 결과.
            var rng = new System.Random(seed);
            var seeds = new Vector3[count];
            const int candidatesPerSeed = 6;
            const int maxTries = 300;
            // 바운즈 안 무작위 점이 이 비율보다 적게 메쉬 안에 들면(가는 막대·얇은 판 등) 표면을 지나는 직선 위 내부 구간에서 후보를 뽑는다.
            const int probeTries = 64;

            float[] areaSum = closed ? AreaPrefix(src) : null;
            var hits = new List<float>();
            bool useChords = false;
            int uniformTried = 0, uniformAccepted = 0;

            for (int i = 0; i < count; i++)
            {
                // Mitchell best-candidate: 메쉬 내부 후보 중 기존 시드에서 가장 먼 점을 고른다(고르게 퍼짐).
                Vector3 best = RandomIn(rng, bounds);
                float bestDist = -1f;
                int accepted = 0;
                for (int t = 0; t < maxTries && accepted < candidatesPerSeed; t++)
                {
                    Vector3 p;
                    if (!closed)
                    {
                        p = RandomIn(rng, bounds);
                    }
                    else if (!useChords)
                    {
                        p = RandomIn(rng, bounds);
                        uniformTried++;
                        bool inside = IsInside(src, p);
                        if (inside) uniformAccepted++;
                        if (areaSum != null && uniformTried >= probeTries && uniformAccepted * 8 < uniformTried) useChords = true;
                        if (!inside) continue;
                    }
                    else if (!TryChordPoint(src, areaSum, rng, hits, out p))
                    {
                        continue;
                    }

                    accepted++;
                    float dist = float.MaxValue;
                    for (int j = 0; j < i; j++) dist = Mathf.Min(dist, (seeds[j] - p).sqrMagnitude);
                    if (dist > bestDist)
                    {
                        bestDist = dist;
                        best = p;
                    }
                }

                // 내부 점을 하나도 못 찾으면 바운즈 밖 점 대신 표면 위 점을 써서 빈 셀이 생기지 않게 한다.
                if (accepted == 0 && areaSum != null) best = SurfacePoint(src, areaSum, rng);
                seeds[i] = best;
            }

            return seeds;
        }

        /// <summary>삼각형 넓이 누적합(표면 위 점을 넓이 비례로 고르기 위함). 넓이가 0이면 null.</summary>
        private static float[] AreaPrefix(Work w)
        {
            int total = 0;
            for (int s = 0; s < w.Sub.Length; s++) total += w.Sub[s].Count / 3;
            var sums = new float[total];
            float acc = 0f;
            int k = 0;
            for (int s = 0; s < w.Sub.Length; s++)
            {
                List<int> tris = w.Sub[s];
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    Vector3 a = w.Pos[tris[t]];
                    acc += Vector3.Cross(w.Pos[tris[t + 1]] - a, w.Pos[tris[t + 2]] - a).magnitude;
                    sums[k++] = acc;
                }
            }

            return acc > 0f ? sums : null;
        }

        private static Vector3 SurfacePoint(Work w, float[] areaSum, System.Random rng)
        {
            float r = (float)rng.NextDouble() * areaSum[areaSum.Length - 1];
            int index = Array.BinarySearch(areaSum, r);
            if (index < 0) index = ~index;
            index = Mathf.Clamp(index, 0, areaSum.Length - 1);

            // 누적 순서(서브메쉬 순서대로)에 맞춰 삼각형을 찾는다.
            for (int s = 0; s < w.Sub.Length; s++)
            {
                int n = w.Sub[s].Count / 3;
                if (index < n)
                {
                    List<int> tris = w.Sub[s];
                    Vector3 a = w.Pos[tris[index * 3]], b = w.Pos[tris[index * 3 + 1]], c = w.Pos[tris[index * 3 + 2]];
                    float u = (float)rng.NextDouble(), v = (float)rng.NextDouble();
                    if (u + v > 1f)
                    {
                        u = 1f - u;
                        v = 1f - v;
                    }

                    return a + (b - a) * u + (c - a) * v;
                }

                index -= n;
            }

            return w.Pos[0];
        }

        /// <summary>
        /// 표면 위 무작위 점을 지나는 무작위 방향 직선을 긋고, 교점 사이의 내부 구간(길이 비례)에서 점을 고른다.
        /// 채움 비율이 낮은 메쉬에서도 거의 항상 내부 점을 얻는다. 다수결 광선 판정으로 다시 확인한다.
        /// </summary>
        private static bool TryChordPoint(Work w, float[] areaSum, System.Random rng, List<float> hits, out Vector3 point)
        {
            point = default;
            Vector3 origin = SurfacePoint(w, areaSum, rng);
            Vector3 dir;
            do
            {
                dir = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 2f - 1f);
            } while (dir.sqrMagnitude < 1e-4f || dir.sqrMagnitude > 1f);

            dir.Normalize();
            LineHits(w, origin, dir, hits);
            if (hits.Count < 2 || (hits.Count & 1) != 0) return false;
            hits.Sort();

            float total = 0f;
            for (int k = 0; k + 1 < hits.Count; k += 2) total += hits[k + 1] - hits[k];
            if (total <= 0f) return false;

            float r = (float)rng.NextDouble() * total;
            for (int k = 0; k + 1 < hits.Count; k += 2)
            {
                float len = hits[k + 1] - hits[k];
                if (r <= len || k + 3 >= hits.Count)
                {
                    point = origin + dir * (hits[k] + Mathf.Clamp(r, 0f, len));
                    return IsInside(w, point);
                }

                r -= len;
            }

            return false;
        }

        /// <summary>직선(양방향) 위 모든 교점의 매개변수 t를 모은다.</summary>
        private static void LineHits(Work w, Vector3 origin, Vector3 dir, List<float> hits)
        {
            hits.Clear();
            List<Vector3> pos = w.Pos;
            for (int s = 0; s < w.Sub.Length; s++)
            {
                List<int> tris = w.Sub[s];
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    Vector3 a = pos[tris[t]];
                    Vector3 e1 = pos[tris[t + 1]] - a;
                    Vector3 e2 = pos[tris[t + 2]] - a;
                    Vector3 pv = Vector3.Cross(dir, e2);
                    float det = Vector3.Dot(e1, pv);
                    if (det > -1e-12f && det < 1e-12f) continue;
                    float inv = 1f / det;
                    Vector3 tv = origin - a;
                    float u = Vector3.Dot(tv, pv) * inv;
                    if (u < 0f || u > 1f) continue;
                    Vector3 qv = Vector3.Cross(tv, e1);
                    float v = Vector3.Dot(dir, qv) * inv;
                    if (v < 0f || u + v > 1f) continue;
                    hits.Add(Vector3.Dot(e2, qv) * inv);
                }
            }
        }

        private static Vector3 RandomIn(System.Random rng, Bounds b)
        {
            return new Vector3(
                b.min.x + (float)rng.NextDouble() * b.size.x,
                b.min.y + (float)rng.NextDouble() * b.size.y,
                b.min.z + (float)rng.NextDouble() * b.size.z);
        }

        private static bool IsInside(Work w, Vector3 p)
        {
            int votes = 0;
            for (int r = 0; r < RayDirections.Length; r++)
            {
                if ((CountHits(w, p, RayDirections[r]) & 1) == 1) votes++;
            }

            return votes * 2 > RayDirections.Length;
        }

        private static int CountHits(Work w, Vector3 origin, Vector3 dir)
        {
            int hits = 0;
            List<Vector3> pos = w.Pos;
            for (int s = 0; s < w.Sub.Length; s++)
            {
                List<int> tris = w.Sub[s];
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    Vector3 a = pos[tris[t]];
                    Vector3 e1 = pos[tris[t + 1]] - a;
                    Vector3 e2 = pos[tris[t + 2]] - a;
                    Vector3 pv = Vector3.Cross(dir, e2);
                    float det = Vector3.Dot(e1, pv);
                    if (det > -1e-12f && det < 1e-12f) continue;
                    float inv = 1f / det;
                    Vector3 tv = origin - a;
                    float u = Vector3.Dot(tv, pv) * inv;
                    if (u < 0f || u > 1f) continue;
                    Vector3 qv = Vector3.Cross(tv, e1);
                    float v = Vector3.Dot(dir, qv) * inv;
                    if (v < 0f || u + v > 1f) continue;
                    if (Vector3.Dot(e2, qv) * inv > 0f) hits++;
                }
            }

            return hits;
        }

        private static bool IsClosed(Work w)
        {
            // 위치 기준(UV 이음매로 복제된 정점은 같은 점)으로 모든 모서리가 짝수 번 쓰이면 닫힌 메쉬로 본다.
            var edges = new Dictionary<EdgeKey, int>();
            for (int s = 0; s < w.Sub.Length; s++)
            {
                List<int> tris = w.Sub[s];
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        var key = new EdgeKey(w.Pos[tris[t + k]], w.Pos[tris[t + (k + 1) % 3]]);
                        edges.TryGetValue(key, out int c);
                        edges[key] = c + 1;
                    }
                }
            }

            foreach (KeyValuePair<EdgeKey, int> pair in edges)
            {
                if ((pair.Value & 1) != 0) return false;
            }

            return edges.Count > 0;
        }

        private readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            private readonly Vector3 a, b;

            public EdgeKey(Vector3 p, Vector3 q)
            {
                if (Less(p, q)) { a = p; b = q; }
                else { a = q; b = p; }
            }

            public bool Equals(EdgeKey other) => a.Equals(other.a) && b.Equals(other.b);
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => a.GetHashCode() * 397 ^ b.GetHashCode();
        }

        private static bool Less(Vector3 p, Vector3 q)
        {
            if (p.x != q.x) return p.x < q.x;
            if (p.y != q.y) return p.y < q.y;
            return p.z < q.z;
        }

        // ─── Plane clip + cap ───

        /// <summary>dot(n, p) - d &lt;= 0 쪽만 남기고, 잘린 단면을 막는다. 모두 남으면 같은 Work를, 모두 잘리면 null을 반환.</summary>
        private static Work Clip(Work w, Vector3 n, float d, float eps, float uvScale, Result result)
        {
            int vc = w.Pos.Count;
            var dist = new float[vc];
            bool anyIn = false, anyOut = false;
            for (int i = 0; i < vc; i++)
            {
                float v = Vector3.Dot(n, w.Pos[i]) - d;
                if (v > -eps && v < eps) v = 0f;
                dist[i] = v;
                if (v > 0f) anyOut = true;
                else anyIn = true;
            }

            if (!anyOut) return w;
            if (!anyIn) return null;

            var o = new Work(w.Sub.Length, vc / 2 + 16, w.Col != null);
            var remap = new int[vc];
            for (int i = 0; i < vc; i++) remap[i] = -1;
            var cut = new Dictionary<long, int>();
            var segments = new List<Segment>();
            var poly = new int[4];

            for (int s = 0; s < w.Sub.Length; s++)
            {
                List<int> tris = w.Sub[s];
                List<int> dst = o.Sub[s];
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                    bool in0 = dist[i0] <= 0f, in1 = dist[i1] <= 0f, in2 = dist[i2] <= 0f;
                    if (in0 && in1 && in2)
                    {
                        dst.Add(Keep(w, o, remap, i0));
                        dst.Add(Keep(w, o, remap, i1));
                        dst.Add(Keep(w, o, remap, i2));
                        continue;
                    }

                    if (!in0 && !in1 && !in2) continue;

                    // 삼각형 순서대로 돌며 남는 다각형을 만든다. 안→밖 교점(exit) 다음에 밖→안 교점(enter)이 오므로
                    // 다각형에서 exit→enter 모서리가 절단면 위에 놓인다. 단면은 이를 반대로 지나야 하므로 enter→exit를 기록한다.
                    int count = 0;
                    int exit = -1, enter = -1;
                    for (int k = 0; k < 3; k++)
                    {
                        int a = k == 0 ? i0 : k == 1 ? i1 : i2;
                        int b = k == 0 ? i1 : k == 1 ? i2 : i0;
                        bool aIn = dist[a] <= 0f, bIn = dist[b] <= 0f;
                        if (aIn) poly[count++] = Keep(w, o, remap, a);
                        if (aIn != bIn)
                        {
                            int x = Intersect(w, o, remap, cut, dist, a, b);
                            poly[count++] = x;
                            if (aIn) exit = x;
                            else enter = x;
                        }
                    }

                    for (int k = 1; k + 1 < count; k++)
                    {
                        int a = poly[0], b = poly[k], c = poly[k + 1];
                        if (a == b || b == c || a == c) continue;
                        dst.Add(a);
                        dst.Add(b);
                        dst.Add(c);
                    }

                    if (exit >= 0 && enter >= 0)
                    {
                        Vector3 pa = o.Pos[enter], pb = o.Pos[exit];
                        if (!pa.Equals(pb)) segments.Add(new Segment { A = pa, B = pb });
                    }
                }
            }

            if (segments.Count > 0) Cap(o, segments, n, uvScale, result);
            return o.TriangleIndexCount > 0 ? o : null;
        }

        private static int Keep(Work w, Work o, int[] remap, int i)
        {
            int r = remap[i];
            if (r >= 0) return r;
            r = o.Add(w.Pos[i], w.Nrm[i], w.Uv[i], w.Col != null ? w.Col[i] : Color.white);
            remap[i] = r;
            return r;
        }

        private static int Intersect(Work w, Work o, int[] remap, Dictionary<long, int> cache, float[] dist, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int existing)) return existing;

            int r;
            int inside = dist[a] <= 0f ? a : b;
            if (dist[inside] == 0f)
            {
                // 평면 위에 있는 정점은 그 자체가 교점이다.
                r = Keep(w, o, remap, inside);
            }
            else
            {
                // 위치 순서로 방향을 고정해, 같은 위치의 모서리(UV 이음매 복제 정점 포함)는 비트 단위로 같은 교점을 얻게 한다.
                int p = Less(w.Pos[a], w.Pos[b]) ? a : b;
                int q = p == a ? b : a;
                float t = dist[p] / (dist[p] - dist[q]);
                Vector3 nrm = Vector3.Lerp(w.Nrm[p], w.Nrm[q], t);
                if (nrm.sqrMagnitude > 1e-12f) nrm.Normalize();
                r = o.Add(
                    w.Pos[p] + (w.Pos[q] - w.Pos[p]) * t,
                    nrm,
                    Vector2.Lerp(w.Uv[p], w.Uv[q], t),
                    w.Col != null ? Color.Lerp(w.Col[p], w.Col[q], t) : Color.white);
            }

            cache.Add(key, r);
            return r;
        }

        private static void Cap(Work o, List<Segment> segments, Vector3 n, float uvScale, Result result)
        {
            // 1) 방향 있는 선분을 끝점 위치로 이어 닫힌 윤곽선을 만든다.
            var byStart = new Dictionary<Vector3, List<int>>(segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                if (!byStart.TryGetValue(segments[i].A, out List<int> list))
                {
                    list = new List<int>(1);
                    byStart.Add(segments[i].A, list);
                }

                list.Add(i);
            }

            var used = new bool[segments.Count];
            var loops = new List<List<Vector3>>();
            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i]) continue;
                var loop = new List<Vector3> { segments[i].A };
                used[i] = true;
                int cur = i;
                bool closed = false;
                for (int guard = 0; guard <= segments.Count; guard++)
                {
                    Vector3 end = segments[cur].B;
                    if (end.Equals(loop[0]))
                    {
                        closed = true;
                        break;
                    }

                    int next = -1;
                    if (byStart.TryGetValue(end, out List<int> candidates))
                    {
                        for (int c = 0; c < candidates.Count; c++)
                        {
                            if (!used[candidates[c]])
                            {
                                next = candidates[c];
                                break;
                            }
                        }
                    }

                    if (next < 0) break;
                    loop.Add(end);
                    used[next] = true;
                    cur = next;
                }

                if (closed && loop.Count >= 3) loops.Add(loop);
                else if (!closed) result.OpenCuts++;
            }

            if (loops.Count == 0) return;

            // 2) 평면 좌표로 옮겨 삼각분할한다(구멍 포함).
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
            Vector3 v = Vector3.Cross(n, u);
            Vector3 origin = loops[0][0];

            var polys = new List<Poly>(loops.Count);
            int largest = 0;
            for (int i = 0; i < loops.Count; i++)
            {
                var p = new Poly(loops[i], origin, u, v);
                polys.Add(p);
                if (Math.Abs(p.Area) > Math.Abs(polys[largest].Area)) largest = i;
            }

            // 가장 큰 윤곽선은 반드시 바깥 윤곽이다. 그 부호와 같은 윤곽은 바깥, 반대는 구멍.
            bool mirror = polys[largest].Area < 0;
            if (mirror)
            {
                for (int i = 0; i < polys.Count; i++) polys[i].Mirror();
            }

            var outers = new List<Poly>();
            var holes = new List<Poly>();
            for (int i = 0; i < polys.Count; i++)
            {
                if (Math.Abs(polys[i].Area) < 1e-18) continue;
                (polys[i].Area > 0 ? outers : holes).Add(polys[i]);
            }

            var holesOf = new List<Poly>[outers.Count];
            for (int h = 0; h < holes.Count; h++)
            {
                int owner = -1;
                for (int k = 0; k < outers.Count; k++)
                {
                    if (!outers[k].Contains(holes[h].X[0], holes[h].Y[0])) continue;
                    if (owner < 0 || outers[k].Area < outers[owner].Area) owner = k;
                }

                if (owner < 0)
                {
                    result.OpenCuts++;
                    continue;
                }

                if (holesOf[owner] == null) holesOf[owner] = new List<Poly>();
                holesOf[owner].Add(holes[h]);
            }

            var capIndex = new Dictionary<Vector3, int>();
            List<int> capTris = o.Sub[o.Sub.Length - 1];
            var tris = new List<int>();
            for (int k = 0; k < outers.Count; k++)
            {
                Poly merged = holesOf[k] == null ? outers[k] : Bridge(outers[k], holesOf[k]);
                tris.Clear();
                EarClip(merged, tris);
                for (int t = 0; t < tris.Count; t++)
                {
                    Vector3 p = merged.P[tris[t]];
                    if (!capIndex.TryGetValue(p, out int index))
                    {
                        index = o.Add(p, n, new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v)) * uvScale, Color.white);
                        capIndex.Add(p, index);
                    }

                    capTris.Add(index);
                }
            }
        }

        private sealed class Poly
        {
            public readonly List<Vector3> P;
            public readonly List<double> X;
            public readonly List<double> Y;
            public double Area;

            public Poly(List<Vector3> points, Vector3 origin, Vector3 u, Vector3 v)
            {
                P = points;
                X = new List<double>(points.Count);
                Y = new List<double>(points.Count);
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 d = points[i] - origin;
                    X.Add(Vector3.Dot(d, u));
                    Y.Add(Vector3.Dot(d, v));
                }

                Area = SignedArea();
            }

            private Poly(int capacity)
            {
                P = new List<Vector3>(capacity);
                X = new List<double>(capacity);
                Y = new List<double>(capacity);
            }

            public static Poly Empty(int capacity) => new Poly(capacity);

            public void AddFrom(Poly src, int i)
            {
                P.Add(src.P[i]);
                X.Add(src.X[i]);
                Y.Add(src.Y[i]);
            }

            public int Count => P.Count;

            public void Mirror()
            {
                for (int i = 0; i < Y.Count; i++) Y[i] = -Y[i];
                Area = -Area;
            }

            private double SignedArea()
            {
                double a = 0;
                for (int i = 0, j = X.Count - 1; i < X.Count; j = i++) a += X[j] * Y[i] - X[i] * Y[j];
                return a * 0.5;
            }

            public bool Contains(double x, double y)
            {
                bool inside = false;
                for (int i = 0, j = X.Count - 1; i < X.Count; j = i++)
                {
                    if ((Y[i] > y) != (Y[j] > y) && x < (X[j] - X[i]) * (y - Y[i]) / (Y[j] - Y[i]) + X[i]) inside = !inside;
                }

                return inside;
            }
        }

        /// <summary>구멍을 바깥 윤곽선에 다리(bridge)로 이어 하나의 약한 단순 다각형으로 만든다.</summary>
        private static Poly Bridge(Poly outer, List<Poly> holes)
        {
            holes.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));
            Poly current = outer;
            for (int h = 0; h < holes.Count; h++)
            {
                Poly hole = holes[h];
                int m = 0;
                for (int i = 1; i < hole.Count; i++)
                {
                    if (hole.X[i] > hole.X[m]) m = i;
                }

                double mx = hole.X[m], my = hole.Y[m];
                var candidates = new List<int>(current.Count);
                for (int i = 0; i < current.Count; i++) candidates.Add(i);
                candidates.Sort((a, b) => Dist2(current, a, mx, my).CompareTo(Dist2(current, b, mx, my)));

                int chosen = -1;
                for (int c = 0; c < candidates.Count && chosen < 0; c++)
                {
                    int k = candidates[c];
                    if (SegmentBlocked(current, mx, my, current.X[k], current.Y[k])) continue;
                    bool blocked = false;
                    for (int o = h; o < holes.Count && !blocked; o++)
                    {
                        blocked = SegmentBlocked(holes[o], mx, my, current.X[k], current.Y[k]);
                    }

                    if (blocked) continue;
                    double midX = (mx + current.X[k]) * 0.5, midY = (my + current.Y[k]) * 0.5;
                    if (!current.Contains(midX, midY)) continue;
                    chosen = k;
                }

                if (chosen < 0) chosen = candidates[0];

                var merged = Poly.Empty(current.Count + hole.Count + 2);
                for (int i = 0; i <= chosen; i++) merged.AddFrom(current, i);
                for (int i = 0; i <= hole.Count; i++) merged.AddFrom(hole, (m + i) % hole.Count);
                for (int i = chosen; i < current.Count; i++) merged.AddFrom(current, i);
                current = merged;
            }

            return current;
        }

        private static double MaxX(Poly p)
        {
            double m = double.MinValue;
            for (int i = 0; i < p.Count; i++) m = Math.Max(m, p.X[i]);
            return m;
        }

        private static double Dist2(Poly p, int i, double x, double y)
        {
            double dx = p.X[i] - x, dy = p.Y[i] - y;
            return dx * dx + dy * dy;
        }

        /// <summary>선분 (ax,ay)-(bx,by)가 다각형 모서리와 끝점이 아닌 곳에서 교차하면 true.</summary>
        private static bool SegmentBlocked(Poly p, double ax, double ay, double bx, double by)
        {
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                double cx = p.X[j], cy = p.Y[j], dx = p.X[i], dy = p.Y[i];
                if (Same(cx, cy, ax, ay) || Same(cx, cy, bx, by) || Same(dx, dy, ax, ay) || Same(dx, dy, bx, by)) continue;
                double d1 = Cross(ax, ay, bx, by, cx, cy);
                double d2 = Cross(ax, ay, bx, by, dx, dy);
                double d3 = Cross(cx, cy, dx, dy, ax, ay);
                double d4 = Cross(cx, cy, dx, dy, bx, by);
                if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
            }

            return false;
        }

        private static bool Same(double ax, double ay, double bx, double by) => ax == bx && ay == by;

        private static double Cross(double ax, double ay, double bx, double by, double cx, double cy)
            => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

        /// <summary>반시계 방향 다각형을 귀 자르기로 삼각분할한다. 결과는 poly 인덱스 삼중쌍.</summary>
        private static void EarClip(Poly poly, List<int> output)
        {
            int n = poly.Count;
            if (n < 3) return;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                minX = Math.Min(minX, poly.X[i]); maxX = Math.Max(maxX, poly.X[i]);
                minY = Math.Min(minY, poly.Y[i]); maxY = Math.Max(maxY, poly.Y[i]);
            }

            double scale = Math.Max(maxX - minX, maxY - minY);
            double tiny = scale * scale * 1e-12;

            // 일직선 위의 점도 빼지 않는다. 빼면 옆면과 T자 이음이 생겨, 다음 평면이 같은 모서리를 다른 식으로 잘라
            // 단면 윤곽선이 닫히지 않는다. 일직선 점은 귀가 될 수 없고, 이웃이 잘리면 자연스럽게 쓰인다.
            int cursor = 0;
            int stall = 0;
            while (idx.Count > 3)
            {
                int count = idx.Count;
                if (cursor >= count) cursor = 0;
                int ia = idx[(cursor + count - 1) % count], ib = idx[cursor], ic = idx[(cursor + 1) % count];
                double cross = Cross(poly.X[ia], poly.Y[ia], poly.X[ib], poly.Y[ib], poly.X[ic], poly.Y[ic]);

                if (cross > tiny && !AnyInside(poly, idx, ia, ib, ic))
                {
                    Emit(output, ia, ib, ic);
                    idx.RemoveAt(cursor);
                    stall = 0;
                    continue;
                }

                cursor++;
                if (++stall <= count) continue;

                // 수치 오차로 귀를 못 찾으면 가장 볼록한 점을 자른다. 남은 점이 모두 일직선이면 면적 없는 부채꼴로 닫는다.
                int best = -1;
                double bestCross = tiny;
                for (int k = 0; k < count; k++)
                {
                    double c = Cross(poly.X[idx[(k + count - 1) % count]], poly.Y[idx[(k + count - 1) % count]],
                        poly.X[idx[k]], poly.Y[idx[k]], poly.X[idx[(k + 1) % count]], poly.Y[idx[(k + 1) % count]]);
                    if (c > bestCross)
                    {
                        bestCross = c;
                        best = k;
                    }
                }

                if (best < 0)
                {
                    for (int k = 1; k + 1 < count; k++) Emit(output, idx[0], idx[k], idx[k + 1]);
                    return;
                }

                Emit(output, idx[(best + count - 1) % count], idx[best], idx[(best + 1) % count]);
                idx.RemoveAt(best);
                cursor = best;
                stall = 0;
            }

            if (idx.Count == 3) Emit(output, idx[0], idx[1], idx[2]);
        }

        private static void Emit(List<int> output, int a, int b, int c)
        {
            output.Add(a);
            output.Add(b);
            output.Add(c);
        }

        /// <summary>
        /// 볼록 MeshCollider용으로 정점을 줄인다. 여러 방향의 가장 먼 점(지지점)만 남겨 PhysX 헐 다각형 한도(256)를 넘지 않게 한다.
        /// 정점이 limit 이하이면 null(원본 메쉬를 그대로 쓰면 됨).
        /// </summary>
        internal static List<Vector3> ColliderPoints(List<Vector3> points, int limit)
        {
            if (points.Count <= limit) return null;
            var picked = new HashSet<int>();
            float golden = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < limit; i++)
            {
                float y = 1f - (i + 0.5f) * 2f / limit;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                var dir = new Vector3(Mathf.Cos(golden * i) * r, y, Mathf.Sin(golden * i) * r);
                int best = 0;
                float bestDot = float.MinValue;
                for (int p = 0; p < points.Count; p++)
                {
                    float dot = Vector3.Dot(points[p], dir);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = p;
                    }
                }

                picked.Add(best);
            }

            var result = new List<Vector3>(picked.Count);
            foreach (int i in picked) result.Add(points[i]);
            return result;
        }

        private static bool AnyInside(Poly poly, List<int> idx, int ia, int ib, int ic)
        {
            double ax = poly.X[ia], ay = poly.Y[ia], bx = poly.X[ib], by = poly.Y[ib], cx = poly.X[ic], cy = poly.Y[ic];
            for (int k = 0; k < idx.Count; k++)
            {
                int i = idx[k];
                if (i == ia || i == ib || i == ic) continue;
                double px = poly.X[i], py = poly.Y[i];
                if (Same(px, py, ax, ay) || Same(px, py, bx, by) || Same(px, py, cx, cy)) continue;
                if (Cross(ax, ay, bx, by, px, py) >= 0 && Cross(bx, by, cx, cy, px, py) >= 0 && Cross(cx, cy, ax, ay, px, py) >= 0) return true;
            }

            return false;
        }

        // ─── Output ───

        private static Piece ToPiece(Work w)
        {
            var bounds = new Bounds(w.Pos[0], Vector3.zero);
            for (int i = 1; i < w.Pos.Count; i++) bounds.Encapsulate(w.Pos[i]);
            Vector3 center = bounds.center;

            double volume = 0;
            for (int s = 0; s < w.Sub.Length; s++)
            {
                List<int> tris = w.Sub[s];
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    Vector3 a = w.Pos[tris[t]] - center, b = w.Pos[tris[t + 1]] - center, c = w.Pos[tris[t + 2]] - center;
                    volume += Vector3.Dot(a, Vector3.Cross(b, c));
                }
            }

            var positions = new List<Vector3>(w.Pos.Count);
            for (int i = 0; i < w.Pos.Count; i++) positions.Add(w.Pos[i] - center);

            return new Piece
            {
                Positions = positions,
                Normals = w.Nrm,
                Uvs = w.Uv,
                Colors = w.Col,
                SubTriangles = w.Sub,
                Center = center,
                Size = bounds.size,
                Volume = (float)Math.Abs(volume / 6.0)
            };
        }
    }
}
