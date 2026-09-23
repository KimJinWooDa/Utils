using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TelleR.ConcaveCollider
{
    /// <summary>
    /// 계산된 조각을 씬 뷰에 반투명 면 + 외곽선으로 그리는 미리보기. 씬을 바꾸지 않으며 메시·재질은 HideAndDontSave로
    /// 만들어 Dispose에서 지운다. 조각은 부모 Transform(대상 루트 또는 본)을 따라 그리므로 대상을 옮겨도 붙어 다닌다.
    /// </summary>
    public sealed class ConcaveColliderPreview : IDisposable
    {
        private const float FaceAlpha = 0.2f;
        private const int CircleSegments = 32;

        private sealed class Item
        {
            public Transform Parent;
            public Matrix4x4 Local;
            public Mesh Faces;
            public Vector3[] Lines;
            public Color Face;
            public Color Line;
        }

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly List<Item> items = new List<Item>();
        private Material material;

        public int Count => items.Count;
        public bool IsEmpty => items.Count == 0;

        /// <summary>조각 index의 표시 색(황금비 색상환).</summary>
        public static Color PieceColor(int index)
        {
            float hue = Mathf.Repeat(0.08f + index * 0.618034f, 1f);
            return Color.HSVToRGB(hue, 0.7f, 1f);
        }

        public void Build(IList<ConcaveTargetResult> results)
        {
            Clear();
            if (results == null) return;
            int colorIndex = 0;
            for (int r = 0; r < results.Count; r++)
            {
                ConcaveTargetResult result = results[r];
                if (result == null || !result.Succeeded) continue;
                for (int i = 0; i < result.Pieces.Count; i++)
                {
                    ConcavePlacedPiece placed = result.Pieces[i];
                    if (placed.Parent == null) continue;
                    Item item = CreateItem(placed.Piece);
                    if (item == null) continue;
                    item.Parent = placed.Parent;
                    Color color = PieceColor(colorIndex++);
                    item.Line = color;
                    item.Face = new Color(color.r, color.g, color.b, FaceAlpha);
                    items.Add(item);
                }
            }
        }

        public void Clear()
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Faces != null) Object.DestroyImmediate(items[i].Faces);
            }

            items.Clear();
        }

        public void Dispose()
        {
            Clear();
            if (material != null) Object.DestroyImmediate(material);
            material = null;
        }

        /// <summary>SceneView.duringSceneGui에서 부른다. Repaint 이벤트에서만 그린다.</summary>
        public void Draw()
        {
            if (items.Count == 0 || Event.current == null || Event.current.type != EventType.Repaint) return;
            EnsureMaterial();

            if (material != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    Item item = items[i];
                    if (item.Parent == null || item.Faces == null) continue;
                    material.SetColor(ColorId, item.Face);
                    material.SetPass(0);
                    Graphics.DrawMeshNow(item.Faces, item.Parent.localToWorldMatrix * item.Local);
                }
            }

            Matrix4x4 previousMatrix = Handles.matrix;
            Color previousColor = Handles.color;
            CompareFunction previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.Always;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item.Parent == null || item.Lines == null || item.Lines.Length < 2) continue;
                Handles.matrix = item.Parent.localToWorldMatrix * item.Local;
                Handles.color = item.Line;
                Handles.DrawLines(item.Lines);
            }

            Handles.matrix = previousMatrix;
            Handles.color = previousColor;
            Handles.zTest = previousZTest;
        }

        private void EnsureMaterial()
        {
            if (material != null) return;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) return;
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)CompareFunction.Always);
        }

        // ─── 조각별 메시·선 ───

        private static Item CreateItem(ConcavePiece piece)
        {
            // 콜라이더 GameObject 로컬 = TRS(Center, Rotation, LocalScale). 볼록 메시는 Center 0, identity.
            var item = new Item
            {
                Local = Matrix4x4.TRS(piece.Center, SafeRotation(piece.Rotation), Vector3.one * piece.LocalScale)
            };

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var lines = new List<Vector3>();
            switch (piece.Kind)
            {
                case ConcavePieceKind.Box:
                    BuildBox(piece.Size * 0.5f, vertices, triangles, lines);
                    break;
                case ConcavePieceKind.Sphere:
                    BuildCapsule(piece.Radius, 0f, 1, vertices, triangles, lines);
                    break;
                case ConcavePieceKind.Capsule:
                    BuildCapsule(piece.Radius, Mathf.Max(0f, piece.Height * 0.5f - piece.Radius), piece.Direction, vertices, triangles, lines);
                    break;
                default:
                    if (piece.Vertices == null || piece.Triangles == null || piece.Vertices.Length < 3) return null;
                    vertices.AddRange(piece.Vertices);
                    triangles.AddRange(piece.Triangles);
                    BuildFeatureEdges(piece.Vertices, piece.Triangles, lines);
                    break;
            }

            if (vertices.Count == 0 || triangles.Count == 0) return null;
            var mesh = new Mesh { name = "ConcavePreview", hideFlags = HideFlags.HideAndDontSave };
            if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            var colors = new Color32[vertices.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(255, 255, 255, 255);
            mesh.colors32 = colors;
            item.Faces = mesh;
            item.Lines = lines.ToArray();
            return item;
        }

        private static void BuildBox(Vector3 h, List<Vector3> vertices, List<int> triangles, List<Vector3> lines)
        {
            for (int i = 0; i < 8; i++)
            {
                vertices.Add(new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y, (i & 4) != 0 ? h.z : -h.z));
            }

            int[] faces =
            {
                0, 2, 3, 1, // -z
                4, 5, 7, 6, // +z
                0, 1, 5, 4, // -y
                2, 6, 7, 3, // +y
                0, 4, 6, 2, // -x
                1, 3, 7, 5 // +x
            };
            for (int f = 0; f < faces.Length; f += 4)
            {
                triangles.Add(faces[f]);
                triangles.Add(faces[f + 1]);
                triangles.Add(faces[f + 2]);
                triangles.Add(faces[f]);
                triangles.Add(faces[f + 2]);
                triangles.Add(faces[f + 3]);
            }

            for (int i = 0; i < 8; i++)
            {
                for (int bit = 1; bit < 8; bit <<= 1)
                {
                    int j = i | bit;
                    if (j == i) continue;
                    lines.Add(vertices[i]);
                    lines.Add(vertices[j]);
                }
            }
        }

        /// <summary>반지름 r, 축 방향 반선분 길이 half의 캡슐(half = 0이면 구).</summary>
        private static void BuildCapsule(float r, float half, int direction, List<Vector3> vertices, List<int> triangles, List<Vector3> lines)
        {
            const int longitude = 24;
            const int latitudeHalf = 8;
            // 로컬 (u, axis, w) → 콜라이더 축으로
            Vector3 Map(float u, float a, float w)
            {
                switch (direction)
                {
                    case 0: return new Vector3(a, u, w);
                    case 2: return new Vector3(u, w, a);
                    default: return new Vector3(u, a, w);
                }
            }

            // 위 반구(북극 → 적도) + 아래 반구(적도 → 남극) 고리
            var ringHeights = new List<float>();
            var ringRadii = new List<float>();
            for (int i = 0; i <= latitudeHalf; i++)
            {
                float t = i / (float)latitudeHalf * Mathf.PI * 0.5f;
                ringHeights.Add(half + r * Mathf.Cos(t));
                ringRadii.Add(r * Mathf.Sin(t));
            }

            for (int i = 0; i <= latitudeHalf; i++)
            {
                float t = i / (float)latitudeHalf * Mathf.PI * 0.5f;
                ringHeights.Add(-half - r * Mathf.Sin(t));
                ringRadii.Add(r * Mathf.Cos(t));
            }

            int ringCount = ringHeights.Count;
            for (int ring = 0; ring < ringCount; ring++)
            {
                for (int j = 0; j < longitude; j++)
                {
                    float angle = j / (float)longitude * Mathf.PI * 2f;
                    vertices.Add(Map(Mathf.Cos(angle) * ringRadii[ring], ringHeights[ring], Mathf.Sin(angle) * ringRadii[ring]));
                }
            }

            for (int ring = 0; ring < ringCount - 1; ring++)
            {
                int a = ring * longitude;
                int b = (ring + 1) * longitude;
                for (int j = 0; j < longitude; j++)
                {
                    int j1 = (j + 1) % longitude;
                    triangles.Add(a + j);
                    triangles.Add(a + j1);
                    triangles.Add(b + j);
                    triangles.Add(a + j1);
                    triangles.Add(b + j1);
                    triangles.Add(b + j);
                }
            }

            // 외곽선: 적도 고리(위/아래), 축을 지나는 두 평면의 윤곽
            AddCircle(lines, half, r, Map);
            if (half > 0f) AddCircle(lines, -half, r, Map);
            AddProfile(lines, r, half, Map, true);
            AddProfile(lines, r, half, Map, false);
        }

        private static void AddCircle(List<Vector3> lines, float height, float radius, Func<float, float, float, Vector3> map)
        {
            for (int i = 0; i < CircleSegments; i++)
            {
                float a0 = i / (float)CircleSegments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)CircleSegments * Mathf.PI * 2f;
                lines.Add(map(Mathf.Cos(a0) * radius, height, Mathf.Sin(a0) * radius));
                lines.Add(map(Mathf.Cos(a1) * radius, height, Mathf.Sin(a1) * radius));
            }
        }

        /// <summary>축을 포함한 평면(u 또는 w)의 캡슐 윤곽: 위·아래 반원 + 옆 직선 두 개.</summary>
        private static void AddProfile(List<Vector3> lines, float r, float half, Func<float, float, float, Vector3> map, bool uPlane)
        {
            Vector3 Point(float side, float height)
            {
                return uPlane ? map(side, height, 0f) : map(0f, height, side);
            }

            int segments = CircleSegments / 2;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI;
                float a1 = (i + 1) / (float)segments * Mathf.PI;
                lines.Add(Point(Mathf.Cos(a0) * r, half + Mathf.Sin(a0) * r));
                lines.Add(Point(Mathf.Cos(a1) * r, half + Mathf.Sin(a1) * r));
                lines.Add(Point(Mathf.Cos(a0) * r, -half - Mathf.Sin(a0) * r));
                lines.Add(Point(Mathf.Cos(a1) * r, -half - Mathf.Sin(a1) * r));
            }

            if (half > 0f)
            {
                lines.Add(Point(r, half));
                lines.Add(Point(r, -half));
                lines.Add(Point(-r, half));
                lines.Add(Point(-r, -half));
            }
        }

        /// <summary>볼록 껍질의 외곽 모서리만(평면 위 삼각형 대각선은 뺀다).</summary>
        private static void BuildFeatureEdges(Vector3[] vertices, int[] triangles, List<Vector3> lines)
        {
            var edges = new Dictionary<long, Vector3>();
            var flat = new HashSet<long>();
            var keep = new HashSet<long>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length) continue;
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                float length = normal.magnitude;
                normal = length > 0f ? normal / length : Vector3.zero;
                Edge(a, b, normal);
                Edge(b, c, normal);
                Edge(c, a, normal);
            }

            foreach (long key in keep)
            {
                lines.Add(vertices[(int)(key >> 32)]);
                lines.Add(vertices[(int)(key & 0xffffffff)]);
            }

            void Edge(int i, int j, Vector3 normal)
            {
                long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
                if (edges.TryGetValue(key, out Vector3 other))
                {
                    // 두 면이 거의 같은 평면이면 대각선이므로 그리지 않는다.
                    if (Vector3.Dot(other, normal) > 0.9995f && !flat.Contains(key))
                    {
                        flat.Add(key);
                        keep.Remove(key);
                    }

                    return;
                }

                edges.Add(key, normal);
                keep.Add(key);
            }
        }

        private static Quaternion SafeRotation(Quaternion q)
        {
            float magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (!(magnitude > 1e-6f) || float.IsNaN(magnitude) || float.IsInfinity(magnitude)) return Quaternion.identity;
            return new Quaternion(q.x / magnitude, q.y / magnitude, q.z / magnitude, q.w / magnitude);
        }
    }
}
