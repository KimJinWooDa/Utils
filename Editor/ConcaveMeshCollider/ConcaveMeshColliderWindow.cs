using System.Collections.Generic;
using System.Globalization;
using TelleR.ConcaveCollider;
using UnityEditor;
using UnityEngine;

namespace TelleR
{
    /// <summary>
    /// Concave Mesh Collider 창. 대상 목록(선택 동기화 + 드롭 영역), 분해 설정(Auto 기본), 출력 옵션,
    /// Preview(씬 뷰 미리보기) / Generate / Remove Generated, 대상별 결과 표를 제공한다.
    /// </summary>
    public sealed class ConcaveMeshColliderWindow : EditorWindow
    {
        private const string MenuPath = "Tools/TelleR/Concave Mesh Collider";
        private const string WindowTitle = "Concave Mesh Collider";

        [SerializeField] private List<GameObject> targets = new List<GameObject>();
        [SerializeField] private bool followSelection = true;
        [SerializeField] private ConcaveColliderSettings settings;
        [SerializeField] private ConcaveColliderOutputOptions options;
        [SerializeField] private Vector2 scroll;

        // SerializedObject 적용 사이에 직접 바꾸는 값이라 직렬화하지 않는다(Apply가 덮어쓰지 않게).
        private bool showAdvanced;
        private bool showTargets = true;

        private SerializedObject serializedWindow;
        private readonly List<ConcaveTargetInfo> infos = new List<ConcaveTargetInfo>();
        private bool infosDirty = true;
        private readonly List<ReportRow> report = new List<ReportRow>();
        private bool reportIsPreview;
        private bool reportStale;
        private ConcaveColliderPreview preview;
        private bool busy;

        private enum Tone
        {
            Normal,
            Hint,
            Good,
            Caution,
            Bad
        }

        private sealed class ReportRow
        {
            public string Name;
            public GameObject Target;
            public string Pieces, Primitives, Hulls, Vertices, Coverage, Outside, Time;
            public Tone CoverageTone, OutsideTone;
            public string Status;
            public Tone StatusTone;
            public string[] Warnings;
        }

        private static GUIStyle cellStyle, statusStyle;

        // 캐시된 GUIContent (OnGUI마다 할당하지 않음)
        private static readonly GUIContent FollowSelectionContent = new GUIContent("Follow Selection", "켜면 Hierarchy에서 선택한 오브젝트가 대상 목록이 됩니다. 드롭 영역에 끌어다 놓으면 꺼집니다.");
        private static readonly GUIContent AddSelectionContent = new GUIContent("Add Selection", "현재 선택한 오브젝트를 목록에 추가합니다.");
        private static readonly GUIContent ClearContent = new GUIContent("Clear", "대상 목록을 비웁니다.");
        private static readonly GUIContent RemoveTargetContent = new GUIContent("×", "목록에서 뺍니다(씬은 바뀌지 않습니다).");
        private static readonly GUIContent AdvancedContent = new GUIContent("Advanced", "허용치·정점 수·프리미티브·패딩·스킨드 메시 옵션");
        private static readonly GUIContent ResetContent = new GUIContent("Reset To Defaults", "분해 설정을 기본값(Auto)으로 되돌립니다.");
        private static readonly GUIContent AssetFolderContent = new GUIContent("Asset Folder", "볼록 메시 조각의 헐 메시를 저장할 폴더(Assets 아래). 대상마다 .asset 하나를 만들고, 다시 만들 때는 같은 파일(GUID)을 갱신합니다. 다른 씬·프리팹도 그 파일을 쓰고 있으면 새 파일로 나눠 저장합니다.");
        private static readonly GUIContent BrowseContent = new GUIContent("...", "폴더를 고릅니다(Assets 아래만).");
        private static readonly GUIContent IsTriggerContent = new GUIContent("Is Trigger", "생성할 콜라이더를 Trigger로 만듭니다.");
        private static readonly GUIContent MaterialContent = new GUIContent("Physics Material", "생성할 콜라이더에 지정할 물리 재질(비우면 기본값).");
        private static readonly GUIContent OverrideLayerContent = new GUIContent("Override Layer", "끄면 조각이 대상 오브젝트의 레이어를 따릅니다.");
        private static readonly GUIContent LayerContent = new GUIContent("Layer", "생성할 조각의 레이어.");
        private static readonly GUIContent DisableExistingContent = new GUIContent("Disable Existing Colliders", "대상에 켜져 있는 기존 콜라이더(Trigger 제외)를 삭제하지 않고 끕니다. Remove Generated로 다시 켜집니다.");
        private static readonly GUIContent PreviewContent = new GUIContent("Preview", "씬을 바꾸지 않고 조각을 계산해 씬 뷰에 미리 보여 줍니다.");
        private static readonly GUIContent GenerateContent = new GUIContent("Generate", "콜라이더를 만들어 적용합니다. 이미 만든 대상은 교체합니다(Undo 가능).");
        private static readonly GUIContent RemoveContent = new GUIContent("Remove Generated", "생성된 콜라이더를 지우고 끈 기존 콜라이더를 다시 켭니다(Undo 가능).");
        private static readonly GUIContent ClearPreviewContent = new GUIContent("Clear Preview", "씬 뷰 미리보기를 지웁니다.");
        private static readonly GUIContent GeneratedBadge = new GUIContent("Generated", "이미 생성된 콜라이더가 있습니다. Generate를 누르면 교체합니다.");
        private static readonly GUIContent NonUniformBadge = new GUIContent("Non-uniform", "스케일이 비균일해서 Sphere/Capsule 대신 축 정렬 Box와 볼록 메시만 씁니다.");
        private static readonly GUIContent NoMeshBadge = new GUIContent("No Mesh", "메시가 지정된 활성 MeshFilter/SkinnedMeshRenderer가 없습니다.");
        private static readonly GUIContent PrefabAssetBadge = new GUIContent("Prefab Asset", "Project 창의 프리팹 에셋은 처리하지 않습니다. 프리팹을 열거나 씬에 배치하세요.");
        private static readonly GUIContent GeneratedTip = new GUIContent(string.Empty, GeneratedBadge.tooltip);
        private static readonly GUIContent NonUniformTip = new GUIContent(string.Empty, NonUniformBadge.tooltip);
        private static readonly GUIContent NoMeshTip = new GUIContent(string.Empty, NoMeshBadge.tooltip);
        private static readonly GUIContent PrefabAssetTip = new GUIContent(string.Empty, PrefabAssetBadge.tooltip);
        private static readonly GUIContent[] ReportHeaderContents =
        {
            new GUIContent("Pieces", "조각(콜라이더) 수"),
            new GUIContent("Prim", "Box/Sphere/Capsule 프리미티브 수"),
            new GUIContent("Hull", "볼록 MeshCollider 수"),
            new GUIContent("Verts", "볼록 메시 정점 합계"),
            new GUIContent("Cover", "원본 내부 중 콜라이더에 덮인 비율(높을수록 좋음)"),
            new GUIContent("Out", "콜라이더 중 원본 밖으로 나온 비율(낮을수록 좋음)"),
            new GUIContent("Time", "계산 시간(초)")
        };

        private const float ColumnWidth = 40f;

        [MenuItem(MenuPath, false, 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<ConcaveMeshColliderWindow>(WindowTitle);
            window.minSize = new Vector2(380f, 460f);
        }

        /// <summary>대상과 설정을 채워 창을 연다(인스펙터의 Edit Settings / Open Window).</summary>
        public static void Open(IList<GameObject> openTargets, ConcaveColliderSettings openSettings, ConcaveColliderOutputOptions openOptions)
        {
            var window = GetWindow<ConcaveMeshColliderWindow>(WindowTitle);
            window.minSize = new Vector2(380f, 460f);
            if (openTargets != null && openTargets.Count > 0)
            {
                window.followSelection = false;
                window.targets.Clear();
                for (int i = 0; i < openTargets.Count; i++)
                {
                    if (openTargets[i] != null && !window.targets.Contains(openTargets[i])) window.targets.Add(openTargets[i]);
                }
            }

            if (openSettings != null) window.settings = openSettings.Clone();
            if (openOptions != null) window.options = openOptions.Clone();
            window.serializedWindow = null;
            window.OnTargetsChanged();
            window.Focus();
        }

        private void OnEnable()
        {
            if (settings == null) settings = ConcaveColliderPrefs.LoadSettings();
            if (options == null) options = ConcaveColliderPrefs.LoadOptions();
            if (targets == null) targets = new List<GameObject>();
            targets.RemoveAll(t => t == null);
            preview = new ConcaveColliderPreview();
            SceneView.duringSceneGui += OnSceneGUI;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            if (followSelection) SyncSelection();
            infosDirty = true;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            if (preview != null) preview.Dispose();
            preview = null;
            SceneView.RepaintAll();
        }

        private void OnFocus()
        {
            infosDirty = true;
        }

        private void OnSelectionChanged()
        {
            if (!followSelection || busy) return;
            SyncSelection();
            Repaint();
        }

        private void OnHierarchyChanged()
        {
            infosDirty = true;
            Repaint();
        }

        private void SyncSelection()
        {
            GameObject[] selected = Selection.gameObjects;
            bool same = selected.Length == targets.Count;
            for (int i = 0; same && i < selected.Length; i++) same = targets.Contains(selected[i]);
            if (same) return;
            targets.Clear();
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] != null && !targets.Contains(selected[i])) targets.Add(selected[i]);
            }

            OnTargetsChanged();
        }

        private void OnTargetsChanged()
        {
            infosDirty = true;
            ClearPreview();
            if (report.Count > 0) reportStale = true;
        }

        private void OnSceneGUI(SceneView view)
        {
            if (preview != null) preview.Draw();
        }

        private void ClearPreview()
        {
            if (preview != null && !preview.IsEmpty)
            {
                preview.Clear();
                SceneView.RepaintAll();
            }
        }

        private void RefreshInfos()
        {
            infosDirty = false;
            infos.Clear();
            targets.RemoveAll(t => t == null);
            for (int i = 0; i < targets.Count; i++) infos.Add(ConcaveColliderGenerator.Inspect(targets[i]));
        }

        // ─── GUI ───

        private void OnGUI()
        {
            if (settings == null) settings = ConcaveColliderPrefs.LoadSettings();
            if (options == null) options = ConcaveColliderPrefs.LoadOptions();
            if (infosDirty && Event.current.type == EventType.Layout) RefreshInfos();
            if (serializedWindow == null || serializedWindow.targetObject == null) serializedWindow = new SerializedObject(this);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space(5f);
            EditorGUILayout.LabelField(WindowTitle, TelleRGUI.Header);
            GUILayout.Label("오목한 메시를 볼록 조각 여러 개(볼록 MeshCollider, 꽉 맞으면 Box/Sphere/Capsule)로 나눠 모양을 따라가는 복합 콜라이더를 만듭니다. Rigidbody에도 쓸 수 있습니다.", TelleRGUI.Hint);
            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("플레이 모드에서 만든 콜라이더는 플레이를 끝내면 사라집니다.", MessageType.Warning);

            DrawTargets();
            DrawSettings();
            DrawOutput();
            DrawActions();
            DrawReport();

            EditorGUILayout.Space(6f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawTargets()
        {
            TelleRGUI.Section("Targets");
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            followSelection = EditorGUILayout.ToggleLeft(FollowSelectionContent, followSelection, GUILayout.Width(130f));
            if (EditorGUI.EndChangeCheck() && followSelection) SyncSelection();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(AddSelectionContent, EditorStyles.miniButtonLeft, GUILayout.Width(92f)))
            {
                followSelection = false;
                AddTargets(Selection.gameObjects);
                GUIUtility.ExitGUI();
            }

            using (new EditorGUI.DisabledScope(targets.Count == 0))
            {
                if (GUILayout.Button(ClearContent, EditorStyles.miniButtonRight, GUILayout.Width(48f)))
                {
                    followSelection = false;
                    targets.Clear();
                    OnTargetsChanged();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();

            Rect drop = GUILayoutUtility.GetRect(0f, 38f, GUILayout.ExpandWidth(true));
            if (TelleRGUI.DropZone(drop, "여기에 오브젝트를 끌어다 놓으세요 (자식의 MeshFilter / SkinnedMeshRenderer 포함)", out Object[] dropped, out _))
            {
                var list = new List<GameObject>();
                for (int i = 0; i < dropped.Length; i++)
                {
                    if (dropped[i] is GameObject go) list.Add(go);
                    else if (dropped[i] is Component component) list.Add(component.gameObject);
                }

                followSelection = false;
                AddTargets(list);
                GUIUtility.ExitGUI();
            }

            if (targets.Count == 0)
            {
                EditorGUILayout.HelpBox("대상이 없습니다. Hierarchy에서 오브젝트를 선택하거나 위 영역에 끌어다 놓으세요. 선택한 오브젝트와 그 활성 자식의 메시를 합쳐 하나의 복합 콜라이더를 만듭니다.", MessageType.Info);
                return;
            }

            showTargets = EditorGUILayout.Foldout(showTargets, $"Target List ({targets.Count})", true);
            if (!showTargets) return;

            int removeIndex = -1;
            int count = Mathf.Min(targets.Count, infos.Count);
            for (int i = 0; i < count; i++)
            {
                ConcaveTargetInfo info = infos[i];
                Rect row = EditorGUILayout.BeginHorizontal(GUILayout.MinHeight(20f));
                TelleRGUI.DrawBackground(row, i % 2 == 0 ? TelleRGUI.RowBg : TelleRGUI.RowBgAlt);
                EditorGUI.BeginChangeCheck();
                var picked = (GameObject)EditorGUILayout.ObjectField(targets[i], typeof(GameObject), true, GUILayout.MinWidth(110f));
                if (EditorGUI.EndChangeCheck())
                {
                    followSelection = false;
                    if (picked == null || targets.Contains(picked))
                    {
                        removeIndex = i;
                    }
                    else
                    {
                        targets[i] = picked;
                        OnTargetsChanged();
                        GUIUtility.ExitGUI();
                    }
                }

                GUILayout.Label(info.Summary, TelleRGUI.Hint, GUILayout.MinWidth(60f));
                if (info.IsPrefabAsset) Badge(PrefabAssetBadge, PrefabAssetTip, TelleRGUI.Danger);
                else if (!info.HasMesh) Badge(NoMeshBadge, NoMeshTip, TelleRGUI.Danger);
                if (info.NonUniformScale) Badge(NonUniformBadge, NonUniformTip, TelleRGUI.Warning);
                if (info.Generated) Badge(GeneratedBadge, GeneratedTip, TelleRGUI.Success);
                if (GUILayout.Button(RemoveTargetContent, EditorStyles.miniButton, GUILayout.Width(20f))) removeIndex = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0 && removeIndex < targets.Count)
            {
                followSelection = false;
                targets.RemoveAt(removeIndex);
                OnTargetsChanged();
                GUIUtility.ExitGUI();
            }
        }

        private static void Badge(GUIContent content, GUIContent tooltip, Color color)
        {
            Vector2 size = TelleRGUI.Badge.CalcSize(content);
            Rect rect = GUILayoutUtility.GetRect(size.x, 16f, GUILayout.ExpandWidth(false));
            rect.y += 2f;
            rect.height = 16f;
            TelleRGUI.DrawBadge(rect, content.text, color);
            GUI.Label(rect, tooltip);
        }

        private void AddTargets(IList<GameObject> list)
        {
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null || targets.Contains(list[i])) continue;
                targets.Add(list[i]);
                changed = true;
            }

            if (changed) OnTargetsChanged();
        }

        private void DrawSettings()
        {
            TelleRGUI.Section("Decomposition");
            serializedWindow.Update();
            SerializedProperty root = serializedWindow.FindProperty(nameof(settings));
            SerializedProperty auto = root.FindPropertyRelative(nameof(ConcaveColliderSettings.auto));
            EditorGUILayout.PropertyField(auto);
            if (auto.boolValue)
            {
                GUILayout.Label("Auto: Balanced 품질로 고정하고, 오목한 정도에 맞춰 조각 수(4~12)만 정합니다. 컵·링 안쪽처럼 깊은 오목부가 메워지면 Auto를 끄고 Precise로 올리세요.", TelleRGUI.Hint);
            }
            else
            {
                Field(root, nameof(ConcaveColliderSettings.quality));
                Field(root, nameof(ConcaveColliderSettings.maxPieces));
            }

            showAdvanced = EditorGUILayout.Foldout(showAdvanced, AdvancedContent, true);
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                if (!auto.boolValue)
                {
                    Field(root, nameof(ConcaveColliderSettings.concavityTolerance));
                    Field(root, nameof(ConcaveColliderSettings.minPieceVolumeFraction));
                    Field(root, nameof(ConcaveColliderSettings.maxHullVertices));
                    Field(root, nameof(ConcaveColliderSettings.fitBoxes));
                    Field(root, nameof(ConcaveColliderSettings.fitSpheres));
                    Field(root, nameof(ConcaveColliderSettings.fitCapsules));
                    Field(root, nameof(ConcaveColliderSettings.primitiveFillThreshold));
                    Field(root, nameof(ConcaveColliderSettings.padding));
                    Field(root, nameof(ConcaveColliderSettings.mergeTightPieces));
                    Field(root, nameof(ConcaveColliderSettings.voxelResolution));
                }
                else
                {
                    GUILayout.Label("수치 항목은 Auto가 정합니다. 직접 바꾸려면 Auto를 끄세요.", TelleRGUI.Hint);
                }

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Skinned Mesh", TelleRGUI.SubHeader);
                if (!auto.boolValue)
                {
                    Field(root, nameof(ConcaveColliderSettings.maxBonePieces));
                    Field(root, nameof(ConcaveColliderSettings.minBoneVolumeFraction));
                }

                Field(root, nameof(ConcaveColliderSettings.groupBonesByRole));
                Field(root, nameof(ConcaveColliderSettings.clipAtJoints));
                Field(root, nameof(ConcaveColliderSettings.excludedBoneKeywords));
                EditorGUI.indentLevel--;

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(ResetContent, EditorStyles.miniButton, GUILayout.Width(120f)))
                {
                    settings = new ConcaveColliderSettings();
                    serializedWindow = null;
                    OnSettingsChanged();
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }

            if (serializedWindow.hasModifiedProperties)
            {
                serializedWindow.ApplyModifiedPropertiesWithoutUndo();
                settings.Validate();
                OnSettingsChanged();
            }
        }

        private static void Field(SerializedProperty root, string name)
        {
            SerializedProperty property = root.FindPropertyRelative(name);
            if (property != null) EditorGUILayout.PropertyField(property);
        }

        private void OnSettingsChanged()
        {
            ConcaveColliderPrefs.SaveSettings(settings);
            ClearPreview();
            if (report.Count > 0 && reportIsPreview) reportStale = true;
        }

        private void DrawOutput()
        {
            TelleRGUI.Section("Output");
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            string folder = EditorGUILayout.DelayedTextField(AssetFolderContent, options.assetFolder);
            if (GUILayout.Button(BrowseContent, EditorStyles.miniButton, GUILayout.Width(26f)))
            {
                string picked = PickFolder(options.assetFolder);
                if (picked != null) folder = picked;
            }

            EditorGUILayout.EndHorizontal();
            if (folder != options.assetFolder)
            {
                if (ConcaveColliderOutputOptions.TryNormalizeAssetFolder(folder, out string normalized)) options.assetFolder = normalized;
                else Debug.LogWarning(ConcaveColliderGenerator.LogPrefix + $"Asset Folder는 Assets 아래 폴더여야 합니다: {folder}");
            }

            options.isTrigger = EditorGUILayout.Toggle(IsTriggerContent, options.isTrigger);
            options.physicsMaterial = ConcavePhysicsMaterials.Filter(
                EditorGUILayout.ObjectField(MaterialContent, options.physicsMaterial, ConcavePhysicsMaterials.MaterialType, false));
            bool overrideLayer = EditorGUILayout.Toggle(OverrideLayerContent, options.layer >= 0);
            if (overrideLayer)
            {
                EditorGUI.indentLevel++;
                options.layer = EditorGUILayout.LayerField(LayerContent, Mathf.Clamp(options.layer, 0, 31));
                EditorGUI.indentLevel--;
            }
            else
            {
                options.layer = -1;
            }

            options.disableExistingColliders = EditorGUILayout.Toggle(DisableExistingContent, options.disableExistingColliders);
            int existing = 0;
            for (int i = 0; i < infos.Count; i++) existing += infos[i].ExistingColliderCount;
            if (existing > 0)
            {
                GUILayout.Label(options.disableExistingColliders
                        ? $"대상에 켜진 기존 콜라이더 {existing}개를 끕니다(삭제하지 않음). Remove Generated로 다시 켜집니다."
                        : $"대상에 켜진 기존 콜라이더가 {existing}개 있습니다. 새 콜라이더와 겹치면 위 옵션으로 끌 수 있습니다.",
                    TelleRGUI.Hint);
            }

            if (EditorGUI.EndChangeCheck()) ConcaveColliderPrefs.SaveOptions(options);
        }

        private static string PickFolder(string current)
        {
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/') ?? string.Empty;
            string start = ConcaveColliderOutputOptions.TryNormalizeAssetFolder(current, out string normalized) && AssetDatabase.IsValidFolder(normalized)
                ? projectRoot + "/" + normalized
                : Application.dataPath;
            string absolute = EditorUtility.OpenFolderPanel("헐 메시 저장 폴더 선택", start, string.Empty);
            if (string.IsNullOrEmpty(absolute)) return null;
            absolute = absolute.Replace('\\', '/');
            if (absolute.StartsWith(projectRoot + "/", System.StringComparison.OrdinalIgnoreCase))
            {
                string relative = absolute.Substring(projectRoot.Length + 1);
                if (ConcaveColliderOutputOptions.TryNormalizeAssetFolder(relative, out string result)) return result;
            }

            TelleRGUI.Info("폴더 선택", "헐 메시는 이 프로젝트의 Assets 폴더 아래에만 저장할 수 있습니다.");
            return null;
        }

        private bool HasRunnableTarget()
        {
            for (int i = 0; i < infos.Count; i++)
            {
                if (!infos[i].IsPrefabAsset && infos[i].HasMesh) return true;
            }

            return false;
        }

        private bool HasGeneratedTarget()
        {
            for (int i = 0; i < infos.Count; i++)
            {
                if (infos[i].Target != null && infos[i].Target.GetComponent<ConcaveColliderRoot>() != null) return true;
            }

            return false;
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(8f);
            bool runnable = HasRunnableTarget() && !busy;
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!runnable))
            {
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = TelleRGUI.AccentButton;
                if (GUILayout.Button(PreviewContent, GUILayout.Height(28f))) EditorApplication.delayCall += RunPreview;
                GUI.backgroundColor = TelleRGUI.SuccessButton;
                if (GUILayout.Button(GenerateContent, GUILayout.Height(28f))) EditorApplication.delayCall += RunGenerate;
                GUI.backgroundColor = previous;
            }

            using (new EditorGUI.DisabledScope(busy || !HasGeneratedTarget()))
            {
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = TelleRGUI.DangerButton;
                if (GUILayout.Button(RemoveContent, GUILayout.Height(28f))) EditorApplication.delayCall += RunRemove;
                GUI.backgroundColor = previous;
            }

            EditorGUILayout.EndHorizontal();

            if (preview != null && !preview.IsEmpty)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"씬 뷰에 조각 {preview.Count}개를 미리 보여 주는 중입니다(창을 닫으면 사라집니다).", TelleRGUI.Hint);
                if (GUILayout.Button(ClearPreviewContent, EditorStyles.miniButton, GUILayout.Width(90f))) ClearPreview();
                EditorGUILayout.EndHorizontal();
            }
        }

        private List<GameObject> RunnableTargets()
        {
            var list = new List<GameObject>();
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null) list.Add(targets[i]);
            }

            return list;
        }

        private void RunPreview()
        {
            if (this == null || busy) return;
            busy = true;
            try
            {
                List<ConcaveTargetResult> results = ConcaveColliderGenerator.Preview(RunnableTargets(), settings);
                if (preview == null) preview = new ConcaveColliderPreview();
                preview.Build(results);
                SetReport(results, true);
                SceneView.RepaintAll();
            }
            finally
            {
                busy = false;
            }

            Repaint();
        }

        private void RunGenerate()
        {
            if (this == null || busy) return;
            busy = true;
            try
            {
                ConcaveColliderPrefs.SaveSettings(settings);
                ConcaveColliderPrefs.SaveOptions(options);
                ClearPreview();
                List<ConcaveTargetResult> results = ConcaveColliderGenerator.Generate(RunnableTargets(), settings, options);
                SetReport(results, false);
            }
            finally
            {
                busy = false;
                infosDirty = true;
            }

            Repaint();
        }

        private void RunRemove()
        {
            if (this == null || busy) return;
            busy = true;
            try
            {
                ClearPreview();
                int removed = ConcaveColliderGenerator.RemoveGenerated(RunnableTargets(), ConcaveAssetRemoval.Ask);
                if (removed > 0)
                {
                    report.Clear();
                    reportStale = false;
                }
            }
            finally
            {
                busy = false;
                infosDirty = true;
            }

            Repaint();
        }

        private void SetReport(List<ConcaveTargetResult> results, bool isPreview)
        {
            report.Clear();
            reportIsPreview = isPreview;
            reportStale = false;
            for (int i = 0; i < results.Count; i++) report.Add(MakeRow(results[i], isPreview));
        }

        private static ReportRow MakeRow(ConcaveTargetResult result, bool isPreview)
        {
            var row = new ReportRow
            {
                Name = string.IsNullOrEmpty(result.Name) ? "(없음)" : result.Name,
                Target = result.Target,
                Warnings = new string[result.Warnings.Count]
            };
            for (int i = 0; i < result.Warnings.Count; i++) row.Warnings[i] = "• " + result.Warnings[i];
            if (result.Cancelled)
            {
                row.Status = "취소됨 — 바뀐 것이 없습니다.";
                row.StatusTone = Tone.Caution;
                return row;
            }

            if (result.Error != null)
            {
                row.Status = result.Error;
                row.StatusTone = Tone.Bad;
                return row;
            }

            row.Pieces = result.Pieces.Count.ToString(CultureInfo.InvariantCulture);
            row.Primitives = result.PrimitiveCount.ToString(CultureInfo.InvariantCulture);
            row.Hulls = result.CountOf(ConcavePieceKind.ConvexMesh).ToString(CultureInfo.InvariantCulture);
            row.Vertices = result.TotalHullVertices.ToString(CultureInfo.InvariantCulture);
            row.Coverage = ConcaveColliderGenerator.Percent(result.Coverage) + (result.MetricsApproximate && result.Coverage >= 0f ? "~" : string.Empty);
            row.Outside = ConcaveColliderGenerator.Percent(result.OutsideFraction);
            row.Time = result.Seconds.ToString("0.00", CultureInfo.InvariantCulture) + "s";
            row.CoverageTone = CoverageTone(result.Coverage);
            row.OutsideTone = OutsideTone(result.OutsideFraction);
            if (!isPreview && result.Applied)
            {
                row.Status = string.IsNullOrEmpty(result.HullAssetPath) ? "적용됨" : "적용됨 · " + result.HullAssetPath;
                row.StatusTone = Tone.Good;
            }

            return row;
        }

        private static Tone CoverageTone(float coverage)
        {
            if (coverage < 0f) return Tone.Hint;
            return coverage >= 0.95f ? Tone.Good : coverage >= 0.85f ? Tone.Caution : Tone.Bad;
        }

        private static Tone OutsideTone(float outside)
        {
            if (outside < 0f) return Tone.Hint;
            return outside <= 0.1f ? Tone.Good : outside <= 0.25f ? Tone.Caution : Tone.Bad;
        }

        /// <summary>커버리지(0~1)에 맞는 표시 색. 인스펙터도 같은 기준을 쓴다.</summary>
        public static Color CoverageColor(float coverage) => ToneColor(CoverageTone(coverage));

        /// <summary>바깥 비율(0~1)에 맞는 표시 색.</summary>
        public static Color OutsideColor(float outside) => ToneColor(OutsideTone(outside));

        private static Color ToneColor(Tone tone)
        {
            switch (tone)
            {
                case Tone.Hint: return TelleRGUI.HintText;
                case Tone.Good: return TelleRGUI.Success;
                case Tone.Caution: return TelleRGUI.Warning;
                case Tone.Bad: return TelleRGUI.Danger;
                default: return TelleRGUI.StrongText;
            }
        }

        private void DrawReport()
        {
            if (report.Count == 0) return;
            TelleRGUI.Section(reportIsPreview ? "Preview Results" : "Results");
            if (reportStale)
                EditorGUILayout.HelpBox("대상이나 설정이 바뀌었습니다. 아래 결과는 이전 계산입니다. Preview를 다시 누르세요.", MessageType.None);

            Rect header = EditorGUILayout.BeginHorizontal();
            TelleRGUI.DrawBackground(header, TelleRGUI.HeaderBg);
            GUILayout.Label("Target", EditorStyles.miniBoldLabel, GUILayout.MinWidth(80f));
            for (int c = 0; c < ReportHeaderContents.Length; c++) GUILayout.Label(ReportHeaderContents[c], EditorStyles.miniBoldLabel, GUILayout.Width(ColumnWidth));
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < report.Count; i++)
            {
                ReportRow row = report[i];
                Rect rect = EditorGUILayout.BeginVertical();
                TelleRGUI.DrawBackground(rect, i % 2 == 0 ? TelleRGUI.RowBg : TelleRGUI.RowBgAlt);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(row.Name, EditorStyles.label, GUILayout.MinWidth(80f)) && row.Target != null)
                    EditorGUIUtility.PingObject(row.Target);
                if (row.Pieces != null)
                {
                    Cell(row.Pieces, Tone.Normal);
                    Cell(row.Primitives, Tone.Normal);
                    Cell(row.Hulls, Tone.Normal);
                    Cell(row.Vertices, Tone.Normal);
                    Cell(row.Coverage, row.CoverageTone);
                    Cell(row.Outside, row.OutsideTone);
                    Cell(row.Time, Tone.Hint);
                }

                EditorGUILayout.EndHorizontal();
                if (row.Status != null) StatusLine(row.Status, row.StatusTone);
                for (int w = 0; w < row.Warnings.Length; w++) GUILayout.Label(row.Warnings[w], TelleRGUI.Hint);
                EditorGUILayout.EndVertical();
            }

            GUILayout.Label("Cover: 원본 내부 중 콜라이더에 덮인 비율, Out: 콜라이더 중 원본 밖 비율(오목한 틈을 메운 정도). ~는 열린 메시라 근사값입니다.", TelleRGUI.Hint);
        }

        private static void Cell(string text, Tone tone)
        {
            if (cellStyle == null) cellStyle = new GUIStyle(EditorStyles.miniLabel);
            cellStyle.normal.textColor = ToneColor(tone);
            GUILayout.Label(text, cellStyle, GUILayout.Width(ColumnWidth));
        }

        private static void StatusLine(string text, Tone tone)
        {
            if (statusStyle == null) statusStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            statusStyle.normal.textColor = ToneColor(tone);
            GUILayout.Label(text, statusStyle);
        }
    }
}
