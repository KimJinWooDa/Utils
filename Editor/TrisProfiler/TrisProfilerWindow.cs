#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TelleR
{
    /// <summary>
    /// 카메라 절두체 안에 보이는 MeshRenderer·SkinnedMeshRenderer의 삼각형·정점 수를 합산하고,
    /// 가장 비싼 오브젝트를 순위로 보여 주며 선택할 수 있게 한다. 씬을 바꾸지 않는 읽기 전용 도구다.
    /// </summary>
    public class TrisProfilerWindow : EditorWindow
    {
        const string MenuPath = "Tools/TelleR/Tris Profiler";
        const double MinRefreshInterval = 0.5;
        const double MaxRefreshInterval = 5.0;
        const float RowHeight = 18f;
        const float IndexWidth = 30f, TrisWidth = 78f, PercentWidth = 46f, VertsWidth = 78f, LodWidth = 40f, DistWidth = 52f;

        class Row
        {
            public string label;
            public Object pingTarget;
            public Object[] selectTargets;
            public long tris;
            public long verts;
            public string lod;
            public float distance;
            public GUIContent labelContent;
            public string indexText, trisText, percentText, vertsText, distText;
        }

        struct Entry
        {
            public Renderer renderer;
            public Mesh mesh;
            public long tris;
            public long verts;
            public int lodIndex;
            public int lodCount;
            public float distance;
        }

        [SerializeField] Camera cameraOverride;
        [SerializeField] bool useSceneViewCamera;
        [SerializeField] bool autoRefresh = true;
        [SerializeField] bool groupByMesh;
        [SerializeField] int topCount = 10;
        [SerializeField] Vector2 scroll;

        [System.NonSerialized] double nextRefreshTime;
        // 도메인 리로드 뒤 다른 탭 뒤에 복원된 창은 OnBecameInvisible을 받지 못하므로 false로 시작하고,
        // OnBecameVisible이나 실제로 그려질 때(OnGUI Repaint) true로 바꾼다.
        [System.NonSerialized] bool isVisible;
        [System.NonSerialized] bool hasCounted;
        [System.NonSerialized] bool hasCamera;
        [System.NonSerialized] string statusText = "";
        [System.NonSerialized] string selectTopLabel = "Select Top";
        readonly List<Row> rows = new List<Row>();
        long totalTris;
        long totalVerts;
        int visibleCount;

        // Refresh에서 재사용하는 버퍼 (갱신마다 새로 만들지 않는다)
        readonly List<Entry> entries = new List<Entry>();
        readonly List<Renderer> rendererBuffer = new List<Renderer>();
        readonly List<LODGroup> lodGroupBuffer = new List<LODGroup>();
        readonly List<Material> materialBuffer = new List<Material>();
        readonly Dictionary<Renderer, Vector2Int> activeLod = new Dictionary<Renderer, Vector2Int>();
        readonly HashSet<Renderer> lodExcluded = new HashSet<Renderer>();
        readonly Plane[] frustumPlanes = new Plane[6];

        // ─── 스타일 (스킨이 바뀔 때만 다시 만든다) ───
        static bool? stylesBuiltForPro;
        static GUIStyle numberStyle, headerNumberStyle, headerLabelStyle, linkStyle;

        static readonly GUIContent CameraContent = new GUIContent("Camera", "합산 기준 카메라. 비워 두면 MainCamera 태그가 달린 카메라를 씁니다.");
        static readonly GUIContent SceneViewContent = new GUIContent("Scene View", "켜면 마지막으로 활성화된 씬 뷰 카메라를 기준으로 셉니다. 프리팹 모드에서는 프리팹 내용만 셉니다.");
        static readonly GUIContent AutoContent = new GUIContent("Auto", "창이 보이는 동안 자동으로 다시 셉니다. 씬이 크면 갱신 간격이 자동으로 늘어납니다(0.5~5초).");
        static readonly GUIContent GroupContent = new GUIContent("Group by Mesh", "같은 메시를 쓰는 렌더러를 한 줄로 묶어 합산합니다.");
        static readonly GUIContent TopContent = new GUIContent("Top", "표에 보여 줄 행 수");
        static readonly GUIContent RefreshContent = new GUIContent("Refresh", "지금 다시 셉니다.");
        static readonly GUIContent SelectTopTooltip = new GUIContent("", "표에 보이는 상위 행의 오브젝트를 모두 선택합니다.");
        static readonly GUIContent HeaderIndex = new GUIContent("#");
        static readonly GUIContent HeaderObject = new GUIContent("Object", "클릭하면 선택, Ctrl(⌘)+클릭하면 선택에 추가·제외합니다.");
        static readonly GUIContent HeaderTris = new GUIContent("Tris", "실제로 그려지는 삼각형 수 (머티리얼 슬롯 기준, 쿼드는 2개로 계산)");
        static readonly GUIContent HeaderPercent = new GUIContent("%", "전체 삼각형 중 비율");
        static readonly GUIContent HeaderVerts = new GUIContent("Verts", "메시 정점 수");
        static readonly GUIContent HeaderLod = new GUIContent("LOD", "활성 LOD 단계 / 전체 단계 수. LODGroup이 없으면 -, 묶음 안에서 서로 다르면 ~");
        static readonly GUIContent HeaderDist = new GUIContent("Dist", "카메라에서 바운드 중심까지 거리(m)");

        const string HelpText =
            "카메라 절두체 안에 있는 MeshRenderer·SkinnedMeshRenderer만 셉니다. 가림(Occlusion) 컬링과 레이어별 컬링 거리는 반영하지 않습니다.\n" +
            "LODGroup은 카메라 거리와 LOD Bias로 활성 단계를 추정합니다. Terrain·파티클·UI·스프라이트·라인은 세지 않습니다.";

        [MenuItem(MenuPath, false, 160)]
        public static void ShowWindow()
        {
            var w = GetWindow<TrisProfilerWindow>("Tris Profiler");
            w.minSize = new Vector2(460, 300);
            w.Show();
        }

        void OnEnable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            nextRefreshTime = 0;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnBecameVisible()
        {
            isVisible = true;
            nextRefreshTime = 0;
        }

        void OnBecameInvisible()
        {
            isVisible = false;
        }

        void OnEditorUpdate()
        {
            // 창이 가려져 있으면(다른 탭 뒤) 씬 전체를 훑지 않는다.
            if (!isVisible) return;
            // Auto가 꺼져 있어도 창을 연 뒤(또는 스크립트 리컴파일 뒤) 한 번은 센다.
            if (!hasCounted)
            {
                Refresh();
                Repaint();
                return;
            }
            if (!autoRefresh) return;
            if (EditorApplication.timeSinceStartup < nextRefreshTime) return;
            Refresh();
            Repaint();
        }

        Camera ResolveCamera(out bool isSceneView)
        {
            isSceneView = false;
            if (useSceneViewCamera)
            {
                SceneView sv = SceneView.lastActiveSceneView;
                if (sv == null || sv.camera == null) return null;
                isSceneView = true;
                return sv.camera;
            }
            if (cameraOverride != null) return cameraOverride;
            return Camera.main;
        }

        void Refresh()
        {
            double start = EditorApplication.timeSinceStartup;
            try
            {
                Recount();
            }
            finally
            {
                // 씬이 커서 한 번 세는 데 오래 걸리면 자동 갱신 간격을 늘려 에디터가 버벅이지 않게 한다.
                double cost = EditorApplication.timeSinceStartup - start;
                double interval = System.Math.Min(MaxRefreshInterval, System.Math.Max(MinRefreshInterval, cost * 20.0));
                nextRefreshTime = EditorApplication.timeSinceStartup + interval;
            }
        }

        void Recount()
        {
            hasCounted = true;
            rows.Clear();
            entries.Clear();
            totalTris = 0;
            totalVerts = 0;
            visibleCount = 0;

            Camera cam = ResolveCamera(out bool isSceneView);
            hasCamera = cam != null;
            if (cam == null)
            {
                statusText = "";
                UpdateSelectTopLabel();
                return;
            }

            GeometryUtility.CalculateFrustumPlanes(cam, frustumPlanes);
            Vector3 camPos = cam.transform.position;
            int layerMask = isSceneView ? UnityEditor.Tools.visibleLayers : cam.cullingMask;
            SceneVisibilityManager visibility = isSceneView ? SceneVisibilityManager.instance : null;

            CollectSceneObjects(isSceneView);

            activeLod.Clear();
            lodExcluded.Clear();
            for (int g = 0; g < lodGroupBuffer.Count; g++)
            {
                LODGroup group = lodGroupBuffer[g];
                if (group == null || !group.enabled || !group.gameObject.activeInHierarchy) continue;
                LOD[] lods = group.GetLODs();
                if (lods == null || lods.Length == 0) continue;
                int active = ActiveLodIndex(group, lods, cam);
                for (int i = 0; i < lods.Length; i++)
                {
                    Renderer[] lodRenderers = lods[i].renderers;
                    if (lodRenderers == null) continue;
                    for (int j = 0; j < lodRenderers.Length; j++)
                    {
                        Renderer r = lodRenderers[j];
                        if (r == null) continue;
                        if (i == active) activeLod[r] = new Vector2Int(i, lods.Length);
                        else lodExcluded.Add(r);
                    }
                }
            }
            // 여러 LOD 단계에 동시에 들어 있는 렌더러는 활성 단계 쪽을 따른다.
            foreach (Renderer r in activeLod.Keys) lodExcluded.Remove(r);

            for (int i = 0; i < rendererBuffer.Count; i++)
            {
                Renderer r = rendererBuffer[i];
                if (r == null || !r.enabled || r.forceRenderingOff) continue;
                GameObject go = r.gameObject;
                if (!go.activeInHierarchy) continue;
                if ((layerMask & (1 << go.layer)) == 0) continue;
                if (lodExcluded.Contains(r)) continue;
                if (visibility != null && visibility.IsHidden(go)) continue;
                if (!TryGetDrawCost(r, materialBuffer, out Mesh mesh, out long tris, out long verts)) continue;
                Bounds bounds = r.bounds;
                if (!GeometryUtility.TestPlanesAABB(frustumPlanes, bounds)) continue;

                activeLod.TryGetValue(r, out Vector2Int lod);
                entries.Add(new Entry
                {
                    renderer = r,
                    mesh = mesh,
                    tris = tris,
                    verts = verts,
                    lodIndex = lod.x,
                    lodCount = lod.y,
                    distance = Vector3.Distance(camPos, bounds.center)
                });
                visibleCount++;
                totalTris += tris;
                totalVerts += verts;
            }

            if (groupByMesh) BuildGroupedRows();
            else BuildRows();
            rows.Sort((a, b) => b.tris.CompareTo(a.tris));
            FormatRows();

            statusText = cam.name + (isSceneView ? " (Scene View)" : "") +
                         "  |  Visible: " + visibleCount.ToString("N0") +
                         "  |  Tris: " + totalTris.ToString("N0") +
                         "  |  Verts: " + totalVerts.ToString("N0");
            UpdateSelectTopLabel();
        }

        void CollectSceneObjects(bool isSceneView)
        {
            rendererBuffer.Clear();
            lodGroupBuffer.Clear();

            // 프리팹 모드에서 씬 뷰가 보여 주는 것은 프리팹 내용뿐이다.
            PrefabStage stage = isSceneView ? PrefabStageUtility.GetCurrentPrefabStage() : null;
            if (stage != null && stage.prefabContentsRoot != null)
            {
                stage.prefabContentsRoot.GetComponentsInChildren(false, rendererBuffer);
                stage.prefabContentsRoot.GetComponentsInChildren(false, lodGroupBuffer);
                return;
            }
            rendererBuffer.AddRange(FindAll<Renderer>());
            lodGroupBuffer.AddRange(FindAll<LODGroup>());
        }

        static T[] FindAll<T>() where T : Object
        {
#if UNITY_6000_6_OR_NEWER
            return FindObjectsByType<T>(FindObjectsInactive.Exclude);
#elif UNITY_2022_2_OR_NEWER
            return FindObjectsByType<T>(FindObjectsSortMode.None);
#else
#pragma warning disable CS0618 // 2022.2 미만에는 FindObjectsByType이 없을 수 있음 (2021.3 초기 패치)
            return FindObjectsOfType<T>();
#pragma warning restore CS0618
#endif
        }

        void BuildRows()
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                GameObject go = e.renderer.gameObject;
                rows.Add(new Row
                {
                    label = go.name,
                    pingTarget = go,
                    selectTargets = new Object[] { go },
                    tris = e.tris,
                    verts = e.verts,
                    lod = LodLabel(e.lodIndex, e.lodCount),
                    distance = e.distance
                });
            }
        }

        void BuildGroupedRows()
        {
            var groups = new Dictionary<Mesh, int>();
            var targets = new List<List<Object>>();
            for (int i = 0; i < entries.Count; i++)
            {
                Entry e = entries[i];
                string lod = LodLabel(e.lodIndex, e.lodCount);
                if (!groups.TryGetValue(e.mesh, out int index))
                {
                    index = rows.Count;
                    groups.Add(e.mesh, index);
                    rows.Add(new Row
                    {
                        label = string.IsNullOrEmpty(e.mesh.name) ? "(unnamed mesh)" : e.mesh.name,
                        pingTarget = e.mesh,
                        lod = lod,
                        distance = e.distance
                    });
                    targets.Add(new List<Object>());
                }
                Row row = rows[index];
                row.tris += e.tris;
                row.verts += e.verts;
                if (row.lod != lod) row.lod = "~";
                if (e.distance < row.distance) row.distance = e.distance;
                targets[index].Add(e.renderer.gameObject);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].selectTargets = targets[i].ToArray();
                rows[i].label += "  x" + targets[i].Count;
            }
        }

        void FormatRows()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                row.labelContent = new GUIContent(row.label, row.label);
                row.indexText = (i + 1).ToString();
                row.trisText = row.tris.ToString("N0");
                row.percentText = totalTris > 0 ? (row.tris * 100.0 / totalTris).ToString("0.0") : "0.0";
                row.vertsText = row.verts.ToString("N0");
                row.distText = row.distance.ToString("0.0");
            }
        }

        void UpdateSelectTopLabel()
        {
            int n = Mathf.Min(topCount, rows.Count);
            selectTopLabel = "Select Top " + n;
        }

        static string LodLabel(int index, int count) => count > 0 ? index + "/" + count : "-";

        /// <summary>
        /// 렌더러가 실제로 그리는 삼각형·정점 수. 인덱스 버퍼를 읽지 않으므로(GetIndexCount) Read/Write가 꺼진 메시도 된다.
        /// 머티리얼 슬롯 i는 서브메시 i를 그리고, 슬롯이 서브메시보다 많으면 마지막 서브메시를 다시 그린다.
        /// 슬롯이 없는 서브메시는 그려지지 않는다.
        /// </summary>
        static bool TryGetDrawCost(Renderer r, List<Material> materials, out Mesh mesh, out long tris, out long verts)
        {
            mesh = null;
            tris = 0;
            verts = 0;
            int firstSubMesh = 0;
            bool staticBatched = false;

            if (r is SkinnedMeshRenderer skinned)
            {
                mesh = skinned.sharedMesh;
            }
            else if (r is MeshRenderer meshRenderer)
            {
                if (!r.TryGetComponent(out MeshFilter filter)) return false;
                mesh = filter.sharedMesh;
                if (r.isPartOfStaticBatch)
                {
                    // 정적 배칭된 렌더러는 합쳐진 메시 안의 자기 서브메시 구간만 센다.
                    staticBatched = true;
                    firstSubMesh = meshRenderer.subMeshStartIndex;
                }
            }
            if (mesh == null) return false;

            int subMeshCount = mesh.subMeshCount;
            if (subMeshCount <= 0 || firstSubMesh >= subMeshCount) return false;

            r.GetSharedMaterials(materials);
            int slots = materials.Count;
            materials.Clear();
            if (slots == 0) return false;

            if (staticBatched)
            {
                int end = Mathf.Min(firstSubMesh + slots, subMeshCount);
                int vertexStart = int.MaxValue, vertexEnd = 0;
                for (int i = firstSubMesh; i < end; i++)
                {
                    tris += TrianglesOf(mesh, i);
                    UnityEngine.Rendering.SubMeshDescriptor d = mesh.GetSubMesh(i);
                    vertexStart = Mathf.Min(vertexStart, d.firstVertex);
                    vertexEnd = Mathf.Max(vertexEnd, d.firstVertex + d.vertexCount);
                }
                verts = vertexEnd > vertexStart ? vertexEnd - vertexStart : 0;
            }
            else
            {
                for (int i = 0; i < slots; i++)
                    tris += TrianglesOf(mesh, Mathf.Min(i, subMeshCount - 1));
                verts = mesh.vertexCount;
            }
            return tris > 0;
        }

        static long TrianglesOf(Mesh mesh, int subMesh)
        {
            switch (mesh.GetTopology(subMesh))
            {
                case MeshTopology.Triangles: return (long)mesh.GetIndexCount(subMesh) / 3;
                case MeshTopology.Quads: return (long)mesh.GetIndexCount(subMesh) / 4 * 2;
                default: return 0;
            }
        }

        static int ActiveLodIndex(LODGroup group, LOD[] lods, Camera cam)
        {
            Transform t = group.transform;
            Vector3 scale = t.lossyScale;
            float maxScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float size = group.size * maxScale;

            float relativeHeight;
            if (cam.orthographic)
            {
                relativeHeight = size * 0.5f / Mathf.Max(cam.orthographicSize, 1e-5f);
            }
            else
            {
                float dist = Vector3.Distance(t.TransformPoint(group.localReferencePoint), cam.transform.position);
                float denom = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                relativeHeight = denom > 1e-5f ? size * 0.5f / denom : float.MaxValue;
            }
            relativeHeight *= QualitySettings.lodBias;

            int active = -1;
            for (int i = 0; i < lods.Length; i++)
            {
                if (relativeHeight > lods[i].screenRelativeTransitionHeight)
                {
                    active = i;
                    break;
                }
            }
            // Quality 설정의 Maximum LOD Level보다 높은 품질 단계는 쓰지 않는다.
            int maxLevel = QualitySettings.maximumLODLevel;
            if (active >= 0 && active < maxLevel && maxLevel < lods.Length) active = maxLevel;
            return active;
        }

        // ─── Selection ───

        void SelectTopRows()
        {
            int shown = Mathf.Min(topCount, rows.Count);
            var list = new List<Object>();
            for (int i = 0; i < shown; i++)
                AddAlive(rows[i].selectTargets, list);
            Selection.objects = list.ToArray();
        }

        /// <returns>대상이 모두 지워져 목록을 다시 셌으면 true.</returns>
        bool SelectRow(Row row, bool additive)
        {
            var targets = new List<Object>();
            AddAlive(row.selectTargets, targets);
            if (targets.Count == 0)
            {
                // 목록을 만든 뒤 오브젝트가 지워졌다. 다시 센다.
                Refresh();
                Repaint();
                return true;
            }

            if (additive)
            {
                var current = new List<Object>(Selection.objects);
                bool allSelected = true;
                for (int i = 0; i < targets.Count; i++)
                    if (!current.Contains(targets[i])) { allSelected = false; break; }
                for (int i = 0; i < targets.Count; i++)
                {
                    if (allSelected) current.Remove(targets[i]);
                    else if (!current.Contains(targets[i])) current.Add(targets[i]);
                }
                Selection.objects = current.ToArray();
            }
            else
            {
                Selection.objects = targets.ToArray();
            }
            if (row.pingTarget != null) EditorGUIUtility.PingObject(row.pingTarget);
            return false;
        }

        static void AddAlive(Object[] source, List<Object> destination)
        {
            if (source == null) return;
            for (int i = 0; i < source.Length; i++)
                if (source[i] != null && !destination.Contains(source[i])) destination.Add(source[i]);
        }

        // ─── GUI ───

        static void EnsureStyles()
        {
            if (stylesBuiltForPro == TelleRGUI.IsPro && numberStyle != null) return;
            stylesBuiltForPro = TelleRGUI.IsPro;

            numberStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
            headerNumberStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight };
            headerLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
            linkStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            linkStyle.normal.textColor = TelleRGUI.Accent;
            linkStyle.hover.textColor = TelleRGUI.Accent;
        }

        void OnGUI()
        {
            if (!isVisible && Event.current.type == EventType.Repaint)
            {
                isVisible = true;
                nextRefreshTime = 0;
            }
            EnsureStyles();

            bool recount = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(CameraContent, GUILayout.Width(52f));
                EditorGUI.BeginChangeCheck();
                using (new EditorGUI.DisabledScope(useSceneViewCamera))
                    cameraOverride = (Camera)EditorGUILayout.ObjectField(cameraOverride, typeof(Camera), true);
                useSceneViewCamera = GUILayout.Toggle(useSceneViewCamera, SceneViewContent, EditorStyles.miniButton, GUILayout.Width(80f));
                recount |= EditorGUI.EndChangeCheck();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                autoRefresh = GUILayout.Toggle(autoRefresh, AutoContent, EditorStyles.miniButton, GUILayout.Width(44f));
                groupByMesh = GUILayout.Toggle(groupByMesh, GroupContent, EditorStyles.miniButton, GUILayout.Width(100f));
                recount |= EditorGUI.EndChangeCheck();
                GUILayout.Label(TopContent, GUILayout.Width(28f));
                EditorGUI.BeginChangeCheck();
                topCount = EditorGUILayout.IntSlider(topCount, 5, 50);
                if (EditorGUI.EndChangeCheck()) UpdateSelectTopLabel();
                recount |= GUILayout.Button(RefreshContent, EditorStyles.miniButton, GUILayout.Width(60f));
            }
            if (recount)
            {
                // 설정이 바뀌거나 Refresh를 누르면 다음 자동 갱신을 기다리지 않고 한 번 다시 센다.
                // 목록 길이가 바뀌므로 이번 이벤트의 나머지 그리기는 건너뛴다(레이아웃 불일치 방지).
                Refresh();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space(2f);

            if (hasCounted && !hasCamera)
            {
                EditorGUILayout.HelpBox(useSceneViewCamera
                        ? "열려 있는 씬 뷰가 없습니다. 씬 뷰를 한 번 클릭한 뒤 Refresh를 누르세요."
                        : "기준 카메라가 없습니다. Camera 칸에 카메라를 넣거나, 카메라에 MainCamera 태그를 달거나, Scene View를 켜세요.",
                    MessageType.Info);
            }

            DrawHeader();

            // 세로 스크롤바를 항상 두어 헤더와 행의 열 위치가 어긋나지 않게 한다.
            scroll = EditorGUILayout.BeginScrollView(scroll, false, true);
            int shown = Mathf.Min(topCount, rows.Count);
            for (int i = 0; i < shown; i++) DrawRow(i, rows[i]);
            if (shown == 0 && hasCamera)
                EditorGUILayout.LabelField("카메라에 보이는 메시 렌더러가 없습니다.", TelleRGUI.HintCentered);
            EditorGUILayout.EndScrollView();

            TelleRGUI.DrawSeparator(1f, 2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(statusText, EditorStyles.boldLabel, GUILayout.MinWidth(60f));
                using (new EditorGUI.DisabledScope(shown == 0))
                {
                    SelectTopTooltip.text = selectTopLabel;
                    if (GUILayout.Button(SelectTopTooltip, EditorStyles.miniButton, GUILayout.Width(96f)))
                        SelectTopRows();
                }
            }
            GUILayout.Label(HelpText, TelleRGUI.Hint);
        }

        void DrawHeader()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            TelleRGUI.DrawBackground(rect, TelleRGUI.HeaderBg);
            rect.xMin += 4f;
            rect.xMax -= 4f + GUI.skin.verticalScrollbar.fixedWidth;
            LayoutColumns(rect, out Rect index, out Rect label, out Rect tris, out Rect percent, out Rect verts, out Rect lod, out Rect dist);
            GUI.Label(index, HeaderIndex, headerLabelStyle);
            GUI.Label(label, HeaderObject, headerLabelStyle);
            GUI.Label(tris, HeaderTris, headerNumberStyle);
            GUI.Label(percent, HeaderPercent, headerNumberStyle);
            GUI.Label(verts, HeaderVerts, headerNumberStyle);
            GUI.Label(lod, HeaderLod, headerNumberStyle);
            GUI.Label(dist, HeaderDist, headerNumberStyle);
        }

        void DrawRow(int i, Row row)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            TelleRGUI.DrawBackground(rect, (i & 1) == 0 ? TelleRGUI.RowBg : TelleRGUI.RowBgAlt);
            rect.xMin += 4f;
            rect.xMax -= 4f;
            LayoutColumns(rect, out Rect index, out Rect label, out Rect tris, out Rect percent, out Rect verts, out Rect lod, out Rect dist);

            GUI.Label(index, row.indexText, EditorStyles.label);
            EditorGUIUtility.AddCursorRect(label, MouseCursor.Link);
            if (GUI.Button(label, row.labelContent, linkStyle) && SelectRow(row, Event.current.control || Event.current.command))
                GUIUtility.ExitGUI();
            GUI.Label(tris, row.trisText, numberStyle);
            GUI.Label(percent, row.percentText, numberStyle);
            GUI.Label(verts, row.vertsText, numberStyle);
            GUI.Label(lod, row.lod, numberStyle);
            GUI.Label(dist, row.distText, numberStyle);
        }

        static void LayoutColumns(Rect r, out Rect index, out Rect label, out Rect tris, out Rect percent, out Rect verts, out Rect lod, out Rect dist)
        {
            float x = r.xMax;
            dist = new Rect(x -= DistWidth, r.y, DistWidth, r.height);
            lod = new Rect(x -= LodWidth, r.y, LodWidth, r.height);
            verts = new Rect(x -= VertsWidth, r.y, VertsWidth, r.height);
            percent = new Rect(x -= PercentWidth, r.y, PercentWidth, r.height);
            tris = new Rect(x -= TrisWidth, r.y, TrisWidth, r.height);
            index = new Rect(r.x, r.y, IndexWidth, r.height);
            label = new Rect(r.x + IndexWidth, r.y, Mathf.Max(0f, x - (r.x + IndexWidth) - 4f), r.height);
        }
    }
}
#endif
