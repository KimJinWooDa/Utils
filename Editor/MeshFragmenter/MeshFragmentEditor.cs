using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TelleR
{
    /// <summary>생성 후 원본 오브젝트를 어떻게 둘지.</summary>
    public enum MeshFragmenterSourceHandling
    {
        Keep = 0,
        HideRenderer = 1,
        Deactivate = 2
    }

    /// <summary>Mesh Fragmenter 설정. 창은 EditorPrefs에 저장하고, 스크립트에서는 직접 채워 <see cref="MeshFragmentEditor.Fragment"/>에 넘긴다.</summary>
    [Serializable]
    public sealed class MeshFragmenterOptions
    {
        public const string DefaultAssetFolder = "Assets/TelleR/Fragments";

        [Tooltip("만들 조각 수(1~64). 창에서는 4 / 8 / 16 / 32 중에서 고릅니다.")]
        public int fragmentCount = 8;

        [Tooltip("조각 모양을 정하는 난수 시드. 같은 시드면 같은 결과가 나옵니다.")]
        public int seed = 42;

        [Tooltip("생성 후 원본을 어떻게 둘지.")]
        public MeshFragmenterSourceHandling sourceHandling = MeshFragmenterSourceHandling.Keep;

        [Tooltip("조각 루트를 꺼진 상태로 만듭니다.")]
        public bool startInactive = true;

        [Tooltip("조각 Rigidbody를 Kinematic으로 만듭니다.")]
        public bool kinematic = true;

        [Tooltip("조각 Rigidbody의 중력 사용 여부.")]
        public bool useGravity;

        [Tooltip("조각 하나의 Rigidbody 질량(kg).")]
        public float massPerFragment = 0.5f;

        [Tooltip("잘린 단면에 쓸 머티리얼. 비우면 원본의 첫 머티리얼을 씁니다.")]
        public Material interiorMaterial;

        [Tooltip("조각 메쉬를 .asset 파일로 저장합니다. 끄면 씬에만 저장됩니다(프리팹 편집 모드에서는 항상 저장).")]
        public bool saveMeshes = true;

        [Tooltip("조각 메쉬를 저장할 폴더(Assets 아래).")]
        public string assetFolder = DefaultAssetFolder;
    }

    /// <summary>
    /// Mesh Fragmenter 창. 선택한 MeshFilter 메쉬를 보로노이 셀로 잘라, 단면이 막힌 조각(볼록 MeshCollider + Rigidbody)을
    /// 원본 옆에 "{이름}_Fragments" 루트로 만든다. 모든 씬 변경은 Undo 한 번으로 되돌린다.
    /// </summary>
    public sealed class MeshFragmentEditor : EditorWindow
    {
        private const string MenuPath = "Tools/TelleR/Mesh Fragmenter";
        private const string WindowTitle = "Mesh Fragmenter";
        private const string LogPrefix = "[TelleR/Mesh Fragmenter] ";
        private const string UndoName = "Mesh Fragmenter";
        private const string ProgressTitle = "Mesh Fragmenter";
        private const int MaxListedTargets = 8;
        // 점 n개의 볼록 헐은 면이 최대 2n-4개라 128개면 PhysX 한도(256면) 안에 든다.
        private const int ColliderPointLimit = 128;

        // 이전 버전(메뉴 전용)에서 쓰던 조각 수 키를 그대로 쓴다.
        private const string PrefCount = "TelleR_FragmentCount";
        private const string PrefPrefix = "TelleR.MeshFragmenter.";

        private static readonly int[] CountValues = { 4, 8, 16, 32 };
        private static readonly GUIContent[] CountLabels =
        {
            new GUIContent("4"), new GUIContent("8"), new GUIContent("16"), new GUIContent("32")
        };

        private static readonly GUIContent CountContent = new GUIContent("Fragments", "만들 조각 수. 시드가 모두 메쉬 안쪽에 놓이므로 닫힌 메쉬는 보통 이 수만큼 조각이 나옵니다.");
        private static readonly GUIContent SeedContent = new GUIContent("Seed", "조각 모양을 정하는 난수 시드. 같은 시드면 같은 결과가 나옵니다.");
        private static readonly GUIContent InteriorContent = new GUIContent("Interior Material", "잘린 단면에 쓸 머티리얼. 비우면 원본의 첫 머티리얼을 씁니다.");
        private static readonly GUIContent StartInactiveContent = new GUIContent("Start Inactive", "조각 루트를 꺼진 상태로 만듭니다. 깨질 때 스크립트에서 SetActive(true)로 켜는 흐름에 맞습니다.");
        private static readonly GUIContent KinematicContent = new GUIContent("Kinematic", "조각 Rigidbody를 Kinematic으로 만듭니다. 깨질 때 스크립트에서 끄세요.");
        private static readonly GUIContent GravityContent = new GUIContent("Use Gravity", "조각 Rigidbody의 중력 사용 여부.");
        private static readonly GUIContent MassContent = new GUIContent("Mass Per Fragment", "조각 하나의 Rigidbody 질량(kg).");
        private static readonly GUIContent SourceContent = new GUIContent("Source Object", "생성 후 원본을 어떻게 둘지. Keep: 그대로 / Hide Renderer: MeshRenderer만 끔 / Deactivate: 오브젝트를 끔. 모두 Undo로 되돌릴 수 있습니다.");
        private static readonly GUIContent SaveContent = new GUIContent("Save Meshes As Asset", "조각 메쉬를 .asset 파일로 저장합니다. 끄면 씬에만 저장되어 프리팹으로 옮기면 메쉬가 빠집니다. 프리팹 편집 모드에서는 항상 저장합니다.");
        private static readonly GUIContent FolderContent = new GUIContent("Asset Folder", "조각 메쉬를 저장할 폴더(Assets 아래). 대상마다 새 파일을 만들고 기존 파일은 덮어쓰지 않습니다.");
        private static readonly GUIContent BrowseContent = new GUIContent("...", "폴더를 고릅니다(Assets 아래만).");
        private static readonly GUIContent ResetContent = new GUIContent("Reset", "설정을 기본값으로 되돌립니다.");
        private static readonly GUIContent[] SourceLabels =
        {
            new GUIContent("Keep"), new GUIContent("Hide Renderer"), new GUIContent("Deactivate")
        };

        private static readonly GUIContent ReadyBadge = new GUIContent("Ready", "조각을 만들 수 있습니다.");
        private static readonly GUIContent OpenMeshBadge = new GUIContent("Open Mesh", "닫히지 않은 메쉬라 단면을 막을 수 없어 조각이 속이 빈 껍데기가 될 수 있습니다.");
        private static readonly GUIContent NoMeshBadge = new GUIContent("No Mesh", "MeshFilter가 없거나 메쉬가 비어 있습니다.");
        private static readonly GUIContent PrefabAssetBadge = new GUIContent("Prefab Asset", "Project 창의 프리팹 에셋은 처리하지 않습니다. 씬에 배치하거나 프리팹을 열어 선택하세요.");

        private static readonly GUIContent ReadyTip = new GUIContent(string.Empty, ReadyBadge.tooltip);
        private static readonly GUIContent OpenMeshTip = new GUIContent(string.Empty, OpenMeshBadge.tooltip);
        private static readonly GUIContent NoMeshTip = new GUIContent(string.Empty, NoMeshBadge.tooltip);
        private static readonly GUIContent PrefabAssetTip = new GUIContent(string.Empty, PrefabAssetBadge.tooltip);

        [SerializeField] private Vector2 scroll;
        [SerializeField] private string lastResult = string.Empty;
        [SerializeField] private bool lastResultOk = true;

        [NonSerialized] private MeshFragmenterOptions options;
        [NonSerialized] private readonly List<TargetRow> rows = new List<TargetRow>();
        [NonSerialized] private int readyCount;
        [NonSerialized] private string moreLabel = string.Empty;
        [NonSerialized] private GUIContent fragmentButton = new GUIContent("Fragment");

        private enum TargetState
        {
            Ready,
            OpenMesh,
            NoMesh,
            PrefabAsset
        }

        private sealed class TargetRow
        {
            public GameObject Go;
            public GUIContent Label;
            public GUIContent Info;
            public TargetState State;
        }

        [MenuItem(MenuPath, false, 103)]
        public static void ShowWindow()
        {
            var window = GetWindow<MeshFragmentEditor>(WindowTitle);
            window.minSize = new Vector2(320f, 420f);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            options = LoadOptions();
            RefreshTargets();
        }

        private void OnDisable()
        {
            if (options != null) SaveOptions(options);
        }

        private void OnSelectionChange()
        {
            RefreshTargets();
            Repaint();
        }

        private void OnHierarchyChange()
        {
            RefreshTargets();
            Repaint();
        }

        // ─── GUI ───

        private void OnGUI()
        {
            if (options == null) options = LoadOptions();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField(WindowTitle, TelleRGUI.Header);
            EditorGUILayout.LabelField("선택한 오브젝트의 메쉬를 보로노이 셀로 잘라, 단면이 막힌 물리 조각(볼록 MeshCollider + Rigidbody)을 원본 옆에 만듭니다. 씬 변경은 Ctrl+Z 한 번으로 모두 되돌릴 수 있습니다(저장한 메쉬 .asset 파일은 남습니다).", TelleRGUI.Hint);
            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("플레이 모드에서 만든 조각은 플레이를 끝내면 사라집니다(저장한 메쉬 .asset 파일은 남습니다).", MessageType.Warning);

            DrawTargets();

            EditorGUI.BeginChangeCheck();
            TelleRGUI.Section("Fragments");
            options.fragmentCount = EditorGUILayout.IntPopup(CountContent, options.fragmentCount, CountLabels, CountValues);
            options.seed = EditorGUILayout.IntField(SeedContent, options.seed);
            options.interiorMaterial = (Material)EditorGUILayout.ObjectField(InteriorContent, options.interiorMaterial, typeof(Material), false);

            TelleRGUI.Section("Physics");
            options.massPerFragment = Mathf.Max(0.0001f, EditorGUILayout.FloatField(MassContent, options.massPerFragment));
            options.kinematic = EditorGUILayout.Toggle(KinematicContent, options.kinematic);
            options.useGravity = EditorGUILayout.Toggle(GravityContent, options.useGravity);
            options.startInactive = EditorGUILayout.Toggle(StartInactiveContent, options.startInactive);

            TelleRGUI.Section("Output");
            options.sourceHandling = (MeshFragmenterSourceHandling)EditorGUILayout.Popup(SourceContent, (int)options.sourceHandling, SourceLabels);
            options.saveMeshes = EditorGUILayout.Toggle(SaveContent, options.saveMeshes);
            using (new EditorGUI.DisabledScope(!options.saveMeshes))
            {
                EditorGUILayout.BeginHorizontal();
                options.assetFolder = EditorGUILayout.TextField(FolderContent, options.assetFolder);
                if (GUILayout.Button(BrowseContent, EditorStyles.miniButton, GUILayout.Width(26f)))
                {
                    BrowseFolder();
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();
            }

            if (!options.saveMeshes)
            {
                EditorGUILayout.HelpBox("조각 메쉬가 씬 파일에만 저장됩니다. 조각을 프리팹으로 만들 계획이면 켜 두세요.", MessageType.Info);
            }
            else if (!IsValidFolder(options.assetFolder))
            {
                EditorGUILayout.HelpBox("저장 폴더는 Assets/ 아래 경로여야 합니다.", MessageType.Warning);
            }

            if (EditorGUI.EndChangeCheck()) SaveOptions(options);

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(ResetContent, GUILayout.Width(60f), GUILayout.Height(28f)))
            {
                options = new MeshFragmenterOptions();
                SaveOptions(options);
                GUI.FocusControl(null);
                GUIUtility.ExitGUI();
            }

            bool canRun = readyCount > 0 && (!options.saveMeshes || IsValidFolder(options.assetFolder));
            using (new EditorGUI.DisabledScope(!canRun))
            {
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = TelleRGUI.AccentButton;
                bool run = GUILayout.Button(fragmentButton, GUILayout.Height(28f));
                GUI.backgroundColor = prev;
                if (run)
                {
                    RunFromWindow();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(lastResult))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(lastResult, lastResultOk ? MessageType.Info : MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTargets()
        {
            TelleRGUI.Section("Targets");
            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField("Hierarchy에서 MeshFilter가 있는 오브젝트를 선택하세요. 여러 개를 선택하면 각각 따로 자릅니다.", TelleRGUI.Hint);
                return;
            }

            for (int i = 0; i < rows.Count && i < MaxListedTargets; i++)
            {
                TargetRow row = rows[i];
                Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                TelleRGUI.DrawBackground(new Rect(rect.x - 2f, rect.y, rect.width + 4f, rect.height), i % 2 == 0 ? TelleRGUI.RowBg : TelleRGUI.RowBgAlt);

                GUIContent badge = BadgeFor(row.State);
                float badgeWidth = 78f;
                float infoWidth = Mathf.Min(120f, Mathf.Max(0f, rect.width - badgeWidth - 100f));
                var badgeRect = new Rect(rect.xMax - badgeWidth, rect.y + 1f, badgeWidth, rect.height - 2f);
                var infoRect = new Rect(badgeRect.x - infoWidth - 4f, rect.y, infoWidth, rect.height);
                var labelRect = new Rect(rect.x, rect.y, Mathf.Max(0f, infoRect.x - rect.x - 4f), rect.height);

                EditorGUI.LabelField(labelRect, row.Label);
                if (infoWidth > 30f) EditorGUI.LabelField(infoRect, row.Info, TelleRGUI.Hint);
                TelleRGUI.DrawBadge(badgeRect, badge.text, BadgeColor(row.State));
                GUI.Label(badgeRect, TipFor(row.State), GUIStyle.none);
            }

            if (rows.Count > MaxListedTargets) EditorGUILayout.LabelField(moreLabel, TelleRGUI.Hint);
        }

        private static GUIContent BadgeFor(TargetState state)
        {
            switch (state)
            {
                case TargetState.Ready: return ReadyBadge;
                case TargetState.OpenMesh: return OpenMeshBadge;
                case TargetState.PrefabAsset: return PrefabAssetBadge;
                default: return NoMeshBadge;
            }
        }

        private static GUIContent TipFor(TargetState state)
        {
            switch (state)
            {
                case TargetState.Ready: return ReadyTip;
                case TargetState.OpenMesh: return OpenMeshTip;
                case TargetState.PrefabAsset: return PrefabAssetTip;
                default: return NoMeshTip;
            }
        }

        private static Color BadgeColor(TargetState state)
        {
            switch (state)
            {
                case TargetState.Ready: return TelleRGUI.Success;
                case TargetState.OpenMesh: return TelleRGUI.Warning;
                default: return TelleRGUI.Danger;
            }
        }

        private void RefreshTargets()
        {
            rows.Clear();
            readyCount = 0;
            GameObject[] selected = Selection.gameObjects;
            for (int i = 0; i < selected.Length; i++)
            {
                GameObject go = selected[i];
                if (go == null) continue;
                var row = new TargetRow { Go = go, Label = new GUIContent(go.name, go.name) };
                Mesh mesh = GetMesh(go);
                if (EditorUtility.IsPersistent(go))
                {
                    row.State = TargetState.PrefabAsset;
                    row.Info = GUIContent.none;
                }
                else if (mesh == null || mesh.vertexCount == 0)
                {
                    row.State = TargetState.NoMesh;
                    row.Info = GUIContent.none;
                }
                else
                {
                    row.State = QuickClosedCheck(mesh) ? TargetState.Ready : TargetState.OpenMesh;
                    row.Info = new GUIContent(mesh.vertexCount.ToString("N0") + " verts", mesh.name);
                    readyCount++;
                }

                rows.Add(row);
            }

            moreLabel = rows.Count > MaxListedTargets ? "외 " + (rows.Count - MaxListedTargets) + "개" : string.Empty;
            fragmentButton = new GUIContent(readyCount > 1 ? "Fragment " + readyCount + " Objects" : "Fragment",
                "선택한 오브젝트마다 조각 루트를 만듭니다(Undo 가능).");
        }

        // 선택 목록 표시용: 큰 메쉬는 매번 전부 검사하지 않고 결과만 기억한다.
        private static readonly Dictionary<Mesh, bool> closedCache = new Dictionary<Mesh, bool>();

        private static bool QuickClosedCheck(Mesh mesh)
        {
            if (closedCache.TryGetValue(mesh, out bool closed)) return closed;
            closed = true;
            try
            {
                if (mesh.vertexCount <= 200000)
                {
                    var positions = new List<Vector3>();
                    mesh.GetVertices(positions);
                    var edges = new Dictionary<(Vector3, Vector3), int>();
                    var tris = new List<int>();
                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                        mesh.GetTriangles(tris, s);
                        for (int t = 0; t + 2 < tris.Count; t += 3)
                        {
                            for (int k = 0; k < 3; k++)
                            {
                                Vector3 a = positions[tris[t + k]], b = positions[tris[t + (k + 1) % 3]];
                                var key = Order(a, b);
                                edges.TryGetValue(key, out int c);
                                edges[key] = c + 1;
                            }
                        }
                    }

                    foreach (KeyValuePair<(Vector3, Vector3), int> pair in edges)
                    {
                        if ((pair.Value & 1) != 0)
                        {
                            closed = false;
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                closed = true;
            }

            if (closedCache.Count > 256) closedCache.Clear();
            closedCache[mesh] = closed;
            return closed;
        }

        private static (Vector3, Vector3) Order(Vector3 a, Vector3 b)
        {
            bool less = a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;
            return less ? (a, b) : (b, a);
        }

        private void BrowseFolder()
        {
            string start = IsValidFolder(options.assetFolder) && AssetDatabase.IsValidFolder(options.assetFolder) ? options.assetFolder : "Assets";
            string picked = EditorUtility.OpenFolderPanel("조각 메쉬 저장 폴더", start, string.Empty);
            if (string.IsNullOrEmpty(picked)) return;
            string root = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            picked = Path.GetFullPath(picked).Replace('\\', '/');
            bool inside = picked.Equals(root, StringComparison.OrdinalIgnoreCase)
                || picked.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
            if (!inside)
            {
                TelleRGUI.Info("저장 폴더 오류", "Assets 폴더 아래만 고를 수 있습니다.");
                return;
            }

            options.assetFolder = "Assets" + picked.Substring(root.Length);
            SaveOptions(options);
            GUI.FocusControl(null);
        }

        private void RunFromWindow()
        {
            var targets = new List<GameObject>();
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Go != null && (rows[i].State == TargetState.Ready || rows[i].State == TargetState.OpenMesh)) targets.Add(rows[i].Go);
            }

            List<GameObject> roots = Run(targets, options, true, out string summary, out bool ok);
            lastResult = summary;
            lastResultOk = ok;
            if (roots.Count > 0) Selection.objects = roots.ToArray();
            RefreshTargets();
            Repaint();
        }

        // ─── Public API ───

        /// <summary>
        /// 대상마다 조각 루트를 만들고 만든 루트 목록을 반환한다. 다이얼로그 없이 결과는 콘솔 로그로 남긴다.
        /// 씬 변경은 Undo 한 그룹으로 묶인다(저장한 .asset 파일은 Undo로 지워지지 않는다).
        /// </summary>
        public static List<GameObject> Fragment(IList<GameObject> targets, MeshFragmenterOptions options)
        {
            return Run(targets, options ?? new MeshFragmenterOptions(), false, out _, out _);
        }

        // ─── Core flow ───

        private sealed class Job
        {
            public GameObject Source;
            public Mesh Mesh;
            public MeshFragmenterCore.Result Result;
        }

        private static List<GameObject> Run(IList<GameObject> targets, MeshFragmenterOptions options, bool interactive, out string summary, out bool ok)
        {
            var roots = new List<GameObject>();
            var failures = new List<string>();
            var jobs = new List<Job>();
            int count = Mathf.Clamp(options.fragmentCount, 1, 64);

            // 1) 계산 단계: 씬을 바꾸지 않으므로 취소하면 아무것도 남지 않는다.
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    GameObject go = targets[i];
                    if (go == null) continue;
                    if (EditorUtility.IsPersistent(go))
                    {
                        failures.Add(go.name + ": Project 창의 프리팹 에셋은 처리하지 않습니다. 씬에 배치하거나 프리팹을 열어 선택하세요.");
                        continue;
                    }

                    Mesh mesh = GetMesh(go);
                    if (mesh == null)
                    {
                        failures.Add(go.name + ": MeshFilter가 없거나 메쉬가 비어 있습니다.");
                        continue;
                    }

                    int index = i;
                    string header = "'" + go.name + "' (" + (index + 1) + "/" + targets.Count + ") ";
                    MeshFragmenterCore.Result result = MeshFragmenterCore.Split(mesh, count, options.seed, (t, message) =>
                        EditorUtility.DisplayCancelableProgressBar(ProgressTitle, header + message, (index + t) / targets.Count));
                    if (result == null)
                    {
                        summary = "취소했습니다. 씬은 바뀌지 않았습니다.";
                        ok = false;
                        Debug.Log(LogPrefix + summary);
                        return new List<GameObject>();
                    }

                    if (!string.IsNullOrEmpty(result.Error) || result.Pieces.Count == 0)
                    {
                        failures.Add(go.name + ": " + (string.IsNullOrEmpty(result.Error) ? "조각이 만들어지지 않았습니다." : result.Error));
                        continue;
                    }

                    jobs.Add(new Job { Source = go, Mesh = mesh, Result = result });
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // 2) 적용 단계: Undo 한 그룹.
            var report = new StringBuilder();
            if (jobs.Count > 0)
            {
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName(UndoName);
                int group = Undo.GetCurrentGroup();
                try
                {
                    for (int i = 0; i < jobs.Count; i++)
                    {
                        EditorUtility.DisplayProgressBar(ProgressTitle, "'" + jobs[i].Source.name + "' 조각 오브젝트 만드는 중", (float)i / jobs.Count);
                        GameObject root = Apply(jobs[i], options, count, report, failures);
                        if (root != null) roots.Add(root);
                    }
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                    Undo.CollapseUndoOperations(group);
                }
            }

            for (int i = 0; i < failures.Count; i++) Debug.LogWarning(LogPrefix + failures[i]);

            ok = failures.Count == 0;
            var text = new StringBuilder();
            text.Append(roots.Count).Append("개 오브젝트를 잘랐습니다.");
            if (report.Length > 0) text.Append('\n').Append(report.ToString().TrimEnd('\n'));
            if (failures.Count > 0)
            {
                text.Append("\n실패 ").Append(failures.Count).Append("개:");
                for (int i = 0; i < failures.Count && i < 5; i++) text.Append("\n- ").Append(failures[i]);
                if (failures.Count > 5) text.Append("\n- 외 ").Append(failures.Count - 5).Append("개 (콘솔 참고)");
            }

            summary = text.ToString();
            if (interactive && failures.Count > 0) TelleRGUI.Info("조각 생성 결과", summary);
            return roots;
        }

        private static GameObject Apply(Job job, MeshFragmenterOptions options, int requested, StringBuilder report, List<string> failures)
        {
            GameObject src = job.Source;
            MeshFragmenterCore.Result result = job.Result;
            Transform srcTransform = src.transform;
            PrefabStage stage = PrefabStageUtility.GetPrefabStage(src);
            bool saveMeshes = options.saveMeshes || stage != null;

            // 프리팹 편집 모드에서 원본이 프리팹 루트면 루트를 하나 더 둘 수 없으므로 원본 아래에 만든다.
            bool underSource = srcTransform.parent == null && stage != null;
            MeshFragmenterSourceHandling handling = options.sourceHandling;
            if (underSource && handling == MeshFragmenterSourceHandling.Deactivate) handling = MeshFragmenterSourceHandling.HideRenderer;

            var meshes = new List<Mesh>(result.Pieces.Count);
            var colliderMeshes = new List<Mesh>(result.Pieces.Count);
            for (int i = 0; i < result.Pieces.Count; i++)
            {
                string name = "Fragment_" + i.ToString("00");
                meshes.Add(BuildMesh(result.Pieces[i], result, name));
                colliderMeshes.Add(BuildColliderMesh(result.Pieces[i], name + "_Collider"));
            }

            string assetPath = null;
            if (saveMeshes)
            {
                if (!TryNormalizeFolder(options.assetFolder, out string folder)) folder = MeshFragmenterOptions.DefaultAssetFolder;
                try
                {
                    EnsureFolder(folder);
                    assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + SanitizeFileName(src.name) + "_Fragments.asset");
                    var main = new Mesh { name = Path.GetFileNameWithoutExtension(assetPath) };
                    AssetDatabase.CreateAsset(main, assetPath);
                    for (int i = 0; i < meshes.Count; i++)
                    {
                        AssetDatabase.AddObjectToAsset(meshes[i], main);
                        if (colliderMeshes[i] != null) AssetDatabase.AddObjectToAsset(colliderMeshes[i], main);
                    }
                    // 프로젝트 전체가 아니라 이 파일만 저장한다.
                    AssetDatabase.SaveAssetIfDirty(main);
                }
                catch (Exception e)
                {
                    failures.Add(src.name + ": 메쉬 에셋을 저장하지 못했습니다(" + e.Message + ").");
                    for (int i = 0; i < meshes.Count; i++)
                    {
                        if (!EditorUtility.IsPersistent(meshes[i])) Object.DestroyImmediate(meshes[i]);
                        if (colliderMeshes[i] != null && !EditorUtility.IsPersistent(colliderMeshes[i])) Object.DestroyImmediate(colliderMeshes[i]);
                    }

                    return null;
                }
            }

            Material[] sourceMaterials = GetSourceMaterials(src);
            Material interior = options.interiorMaterial != null ? options.interiorMaterial : sourceMaterials[0];
            int subCount = meshes.Count > 0 ? meshes[0].subMeshCount : 1;
            var materials = new Material[subCount];
            for (int s = 0; s < subCount - 1; s++) materials[s] = sourceMaterials[Mathf.Min(s, sourceMaterials.Length - 1)];
            materials[subCount - 1] = interior;

            var root = new GameObject(src.name + "_Fragments");
            if (underSource)
            {
                root.transform.SetParent(srcTransform, false);
            }
            else
            {
                if (srcTransform.parent == null && src.scene.IsValid() && root.scene != src.scene) SceneManager.MoveGameObjectToScene(root, src.scene);
                root.transform.SetParent(srcTransform.parent, false);
                root.transform.localPosition = srcTransform.localPosition;
                root.transform.localRotation = srcTransform.localRotation;
                root.transform.localScale = srcTransform.localScale;
                root.transform.SetSiblingIndex(srcTransform.GetSiblingIndex() + 1);
            }

            root.layer = src.layer;

            float size = Mathf.Max(job.Mesh.bounds.size.x, Mathf.Max(job.Mesh.bounds.size.y, job.Mesh.bounds.size.z));
            int boxFallbacks = 0;
            for (int i = 0; i < meshes.Count; i++)
            {
                MeshFragmenterCore.Piece piece = result.Pieces[i];
                var frag = new GameObject(meshes[i].name);
                frag.layer = src.layer;
                frag.transform.SetParent(root.transform, false);
                frag.transform.localPosition = piece.Center;

                frag.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                frag.AddComponent<MeshRenderer>().sharedMaterials = materials;

                float thinnest = Mathf.Min(piece.Size.x, Mathf.Min(piece.Size.y, piece.Size.z));
                if (thinnest < size * 1e-4f)
                {
                    // 거의 평평한 조각은 볼록 메쉬를 만들 수 없으므로(PhysX 쿠킹 실패) 얇은 BoxCollider를 쓴다.
                    var box = frag.AddComponent<BoxCollider>();
                    box.center = Vector3.zero;
                    box.size = Vector3.Max(piece.Size, Vector3.one * (size * 1e-3f));
                    boxFallbacks++;
                }
                else
                {
                    var collider = frag.AddComponent<MeshCollider>();
                    collider.sharedMesh = colliderMeshes[i] != null ? colliderMeshes[i] : meshes[i];
                    collider.convex = true;
                }

                var body = frag.AddComponent<Rigidbody>();
                body.mass = Mathf.Max(0.0001f, options.massPerFragment);
                body.isKinematic = options.kinematic;
                body.useGravity = options.useGravity;
            }

            if (options.startInactive) root.SetActive(false);
            Undo.RegisterCreatedObjectUndo(root, UndoName);

            if (handling == MeshFragmenterSourceHandling.Deactivate)
            {
                Undo.RecordObject(src, UndoName);
                src.SetActive(false);
            }
            else if (handling == MeshFragmenterSourceHandling.HideRenderer)
            {
                var renderer = src.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    Undo.RecordObject(renderer, UndoName);
                    renderer.enabled = false;
                }
            }

            // 플레이 모드에서는 MarkSceneDirty가 예외를 던지고, 어차피 플레이를 끝내면 씬 변경이 사라진다.
            if (!EditorApplication.isPlaying && src.scene.IsValid() && !EditorSceneManager.IsPreviewScene(src.scene)) EditorSceneManager.MarkSceneDirty(src.scene);

            var line = new StringBuilder();
            line.Append("'").Append(src.name).Append("': 조각 ").Append(meshes.Count).Append("개");
            if (meshes.Count < requested) line.Append(" (요청 ").Append(requested).Append("개 중 일부 셀이 비어 있음)");
            line.Append(assetPath != null ? ", 메쉬 저장: " + assetPath : ", 메쉬는 씬에만 저장");
            if (!result.SourceClosed || result.OpenCuts > 0) line.Append(", 닫히지 않은 메쉬라 일부 단면이 비어 있을 수 있음");
            if (boxFallbacks > 0) line.Append(", 평평한 조각 ").Append(boxFallbacks).Append("개는 BoxCollider 사용");
            if (underSource) line.Append(", 프리팹 루트라 원본 아래에 생성");
            Debug.Log(LogPrefix + line, root);
            report.Append(line).Append('\n');
            return root;
        }

        private static Mesh BuildMesh(MeshFragmenterCore.Piece piece, MeshFragmenterCore.Result result, string name)
        {
            var mesh = new Mesh { name = name };
            if (piece.Positions.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(piece.Positions);
            mesh.SetNormals(piece.Normals);
            mesh.SetUVs(0, piece.Uvs);
            if (piece.Colors != null) mesh.SetColors(piece.Colors);
            mesh.subMeshCount = piece.SubTriangles.Length;
            for (int s = 0; s < piece.SubTriangles.Length; s++) mesh.SetTriangles(piece.SubTriangles[s], s, false);
            mesh.RecalculateBounds();
            if (!result.HadNormals) mesh.RecalculateNormals();
            if (result.HadTangents) mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>정점이 많은 조각은 볼록 헐 한도(256면)를 넘어 PhysX가 부분 헐을 쓰므로, 지지점만 남긴 콜라이더 전용 메쉬를 만든다.</summary>
        private static Mesh BuildColliderMesh(MeshFragmenterCore.Piece piece, string name)
        {
            List<Vector3> points = MeshFragmenterCore.ColliderPoints(piece.Positions, ColliderPointLimit);
            if (points == null || points.Count < 4) return null;
            var tris = new List<int>((points.Count - 2) * 3);
            for (int i = 1; i + 1 < points.Count; i++)
            {
                tris.Add(0);
                tris.Add(i);
                tris.Add(i + 1);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(points);
            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        private static Mesh GetMesh(GameObject go)
        {
            var filter = go.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        private static Material[] GetSourceMaterials(GameObject src)
        {
            var renderer = src.GetComponent<MeshRenderer>();
            Material[] mats = renderer != null ? renderer.sharedMaterials : null;
            if (mats != null && mats.Length > 0 && mats[0] != null) return mats;

            // 렌더러·머티리얼이 없으면 현재 렌더 파이프라인의 기본 머티리얼을 쓴다(Built-in이면 Default-Material).
            var probe = GameObject.CreatePrimitive(PrimitiveType.Quad);
            probe.hideFlags = HideFlags.HideAndDontSave;
            Material fallback = probe.GetComponent<MeshRenderer>().sharedMaterial;
            Object.DestroyImmediate(probe);
            return new[] { fallback };
        }

        // ─── Assets / prefs ───

        private static bool IsValidFolder(string folder) => TryNormalizeFolder(folder, out _);

        /// <summary>폴더가 Assets 아래의 올바른 경로인지 확인하고 'Assets/...' 형태로 정규화한다.</summary>
        private static bool TryNormalizeFolder(string folder, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(folder)) return false;
            string path = folder.Trim().Replace('\\', '/');
            while (path.EndsWith("/", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 1);
            if (path != "Assets" && !path.StartsWith("Assets/", StringComparison.Ordinal)) return false;
            string[] parts = path.Split('/');
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part == "." || part == ".." || part.IndexOfAny(invalid) >= 0) return false;
                if (part != part.Trim() || part.EndsWith(".", StringComparison.Ordinal)) return false;
            }

            normalized = path;
            return true;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                builder.Append(ch == '.' || Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
            }

            return builder.Length > 0 ? builder.ToString() : "Mesh";
        }

        private static MeshFragmenterOptions LoadOptions()
        {
            var o = new MeshFragmenterOptions();
            int count = EditorPrefs.GetInt(PrefCount, o.fragmentCount);
            o.fragmentCount = Array.IndexOf(CountValues, count) >= 0 ? count : 8;
            o.seed = EditorPrefs.GetInt(PrefPrefix + "Seed", o.seed);
            o.sourceHandling = (MeshFragmenterSourceHandling)Mathf.Clamp(EditorPrefs.GetInt(PrefPrefix + "Source", (int)o.sourceHandling), 0, 2);
            o.startInactive = EditorPrefs.GetBool(PrefPrefix + "StartInactive", o.startInactive);
            o.kinematic = EditorPrefs.GetBool(PrefPrefix + "Kinematic", o.kinematic);
            o.useGravity = EditorPrefs.GetBool(PrefPrefix + "UseGravity", o.useGravity);
            o.massPerFragment = Mathf.Max(0.0001f, EditorPrefs.GetFloat(PrefPrefix + "Mass", o.massPerFragment));
            o.saveMeshes = EditorPrefs.GetBool(PrefPrefix + "SaveMeshes", o.saveMeshes);
            o.assetFolder = EditorPrefs.GetString(PrefPrefix + "Folder", o.assetFolder);
            string key = EditorPrefs.GetString(PrefPrefix + "Interior", string.Empty);
            if (!string.IsNullOrEmpty(key))
            {
                o.interiorMaterial = MaterialFromKey(key);
                if (o.interiorMaterial == null)
                    Debug.LogWarning(LogPrefix + "저장해 둔 Interior Material을 찾지 못해 비웠습니다(삭제되었거나 옮겨졌을 수 있습니다).");
            }

            return o;
        }

        private static void SaveOptions(MeshFragmenterOptions o)
        {
            EditorPrefs.SetInt(PrefCount, o.fragmentCount);
            EditorPrefs.SetInt(PrefPrefix + "Seed", o.seed);
            EditorPrefs.SetInt(PrefPrefix + "Source", (int)o.sourceHandling);
            EditorPrefs.SetBool(PrefPrefix + "StartInactive", o.startInactive);
            EditorPrefs.SetBool(PrefPrefix + "Kinematic", o.kinematic);
            EditorPrefs.SetBool(PrefPrefix + "UseGravity", o.useGravity);
            EditorPrefs.SetFloat(PrefPrefix + "Mass", o.massPerFragment);
            EditorPrefs.SetBool(PrefPrefix + "SaveMeshes", o.saveMeshes);
            EditorPrefs.SetString(PrefPrefix + "Folder", o.assetFolder ?? string.Empty);
            EditorPrefs.SetString(PrefPrefix + "Interior", MaterialKey(o.interiorMaterial));
        }

        // 한 파일에 머티리얼이 여럿일 수 있어(FBX 내장, 서브 에셋, 내장 리소스) GUID와 localId를 함께 저장한다: "guid:localId".
        private static string MaterialKey(Material material)
        {
            if (material == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string guid, out long localId)) return string.Empty;
            return guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
        }

        private static Material MaterialFromKey(string key)
        {
            int sep = key.IndexOf(':');
            string guid = sep >= 0 ? key.Substring(0, sep) : key;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            // 이전 버전은 GUID만 저장했다: 그 파일의 대표 머티리얼로 복원한다.
            if (sep < 0) return AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!long.TryParse(key.Substring(sep + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long localId)) return null;

            Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is Material material && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out string g, out long id)
                    && id == localId && string.Equals(g, guid, StringComparison.OrdinalIgnoreCase))
                    return material;
            }

            return null;
        }
    }
}
