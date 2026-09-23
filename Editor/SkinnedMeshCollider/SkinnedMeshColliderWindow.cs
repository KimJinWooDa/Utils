using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;

namespace TelleR
{
    public class SkinnedMeshColliderWindow : EditorWindow
    {
        private enum SimplificationPreset
        {
            VeryLow,
            Low,
            Medium,
            High,
            VeryHigh,
            Custom
        }

        private const string LogPrefix = "[TelleR/SkinnedMeshCollider] ";
        private const string ConvexPrefKey = "TelleR.SkinnedMeshCollider.Convex";
        private const string ConcaveToolMenuPath = "Tools/TelleR/Concave Mesh Collider";
        private const double PreviewDebounceSeconds = 0.35;
        private const double PreviewMaxHoldSeconds = 3.0;
        private const int PreviewProgressThreshold = 30000; // 이 삼각형 수 이상이면 미리보기에도 진행바 표시
        private const float WeldRelativeEpsilon = 1e-5f;    // 바운드 대각선 대비 용접 거리
        private const float WeldMinNormalDot = -0.7f;       // 노말이 약 135° 이상 어긋나면(앞/뒷면) 합치지 않음

        // 창 상태는 도메인 리로드(스크립트 재컴파일) 후에도 유지되도록 직렬화한다.
        [SerializeField] private GameObject targetObject;
        [SerializeField] private SkinnedMeshRenderer selectedRenderer;
        [SerializeField] private List<SkinnedMeshRenderer> skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
        [SerializeField] private string saveFolderPath = "Assets/Mesh/ColliderMesh";
        [SerializeField] private string meshName = "";
        [SerializeField] private bool replaceExistingColliders = true;
        [SerializeField] private bool combineAllMeshes = false;
        [SerializeField] private bool enableSimplification = false;
        [SerializeField] private SimplificationPreset selectedPreset = SimplificationPreset.Medium;
        [SerializeField] private float quality = 0.5f;
        [SerializeField] private bool recalculateNormals = true;

        // Convex는 EditorPrefs로 기억(기본 OFF). OnEnable에서 읽는다.
        private bool makeConvex = false;

        private Vector2 scrollPos;
        private Vector2 mainScrollPos;

        private Mesh previewMesh;
        private int previewTriCount;
        private Editor meshPreviewEditor;
        private bool previewPending;
        private bool previewCanceled;
        private bool previewRestorePending; // 도메인 리로드 후 첫 OnGUI에서 미리보기 복원 여부를 결정
        private bool previewDeferred;       // 큰 메시라 리로드 후 자동 재생성을 건너뜀(Refresh 대기)
        private double previewDueTime;
        private double previewRequestTime;

        private int targetTriCount;
        private int originalTriCount;

        private readonly float[] presetValues = { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f };

        private readonly string[] presetLabels =
        {
            "Very Low (-90%)",
            "Low (-75%)",
            "Medium (-50%)",
            "High (-25%)",
            "Very High (-10%)",
            "Custom"
        };

        private static GUIStyle savedPercentStyle;
        private static bool savedPercentStyleForPro;

        private static GUIStyle SavedPercentStyle
        {
            get
            {
                if (savedPercentStyle == null || savedPercentStyleForPro != TelleRGUI.IsPro)
                {
                    savedPercentStyleForPro = TelleRGUI.IsPro;
                    savedPercentStyle = new GUIStyle(TelleRGUI.CenteredBold) { fontSize = 13 };
                    savedPercentStyle.normal.textColor = TelleRGUI.Success;
                }

                return savedPercentStyle;
            }
        }

        [MenuItem("Tools/TelleR/Skinned Mesh Collider", false, 101)]
        public static void ShowWindow()
        {
            var window = GetWindow<SkinnedMeshColliderWindow>("Skinned Mesh Collider");
            window.minSize = new Vector2(360, 320);
        }

        private void OnEnable()
        {
            makeConvex = EditorPrefs.GetBool(ConvexPrefKey, false);
            if (skinnedMeshRenderers == null) skinnedMeshRenderers = new List<SkinnedMeshRenderer>();
            skinnedMeshRenderers.RemoveAll(r => r == null);

            UpdateOriginalTriCount();

            // OnEnable은 재컴파일·도메인 리로드마다(보이지 않는 도킹 탭이어도) 호출된다.
            // 여기서 바로 베이크하지 않고, 창이 실제로 그려질 때(첫 OnGUI) 결정한다.
            previewRestorePending = HasValidTarget();
            previewDeferred = false;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            previewPending = false;
            ClearPreview();
        }

        private void OnGUI()
        {
            if (previewRestorePending && Event.current.type == EventType.Layout)
            {
                RestorePreviewAfterReload();
            }

            mainScrollPos = EditorGUILayout.BeginScrollView(mainScrollPos);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Skinned Mesh Collider", TelleRGUI.Header);
            GUILayout.Label("SkinnedMeshRenderer의 현재 포즈를 MeshCollider용 메시로 굽습니다.", TelleRGUI.Hint);

            EditorGUILayout.Space(6);

            DrawTargetSelection();

            if (HasValidTarget())
            {
                EditorGUILayout.Space(6);
                DrawSimplificationSettings();

                EditorGUILayout.Space(6);
                DrawOutputSettings();

                EditorGUILayout.Space(6);
                DrawPreview();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.EndScrollView();
        }

        private bool HasValidTarget()
        {
            if (combineAllMeshes)
            {
                foreach (var smr in skinnedMeshRenderers)
                {
                    if (smr != null && smr.sharedMesh != null) return true;
                }

                return false;
            }

            return selectedRenderer != null && selectedRenderer.sharedMesh != null;
        }

        private void DrawTargetSelection()
        {
            TelleRGUI.Section("Target");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            selectedRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                new GUIContent("SkinnedMeshRenderer", "콜라이더로 만들 SkinnedMeshRenderer. 여기서 직접 고르면 이 렌더러의 오브젝트에, Use Selection으로 찾았으면 선택한 오브젝트에 콜라이더가 추가됩니다(아래 Collider Target 참고)."),
                selectedRenderer,
                typeof(SkinnedMeshRenderer),
                true);

            if (EditorGUI.EndChangeCheck())
            {
                combineAllMeshes = false;
                skinnedMeshRenderers.Clear();

                if (selectedRenderer != null)
                {
                    targetObject = selectedRenderer.gameObject;
                    meshName = $"{selectedRenderer.name}_Collider";
                }
                else
                {
                    targetObject = null;
                }

                UpdateOriginalTriCount();
                SchedulePreview(true);
                // 대상이 바뀌면 아래 섹션 구성이 달라지므로 이번 이벤트의 나머지 레이아웃을 건너뛴다.
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.Space(3);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Use Selection", "Hierarchy에서 선택한 오브젝트와 그 자식의 SkinnedMeshRenderer를 모두 찾습니다."),
                    GUILayout.Height(24)))
            {
                if (Selection.activeGameObject != null)
                {
                    SetTargetFromGameObject(Selection.activeGameObject);
                }
                else
                {
                    TelleRGUI.Info("선택 없음", "Hierarchy에서 SkinnedMeshRenderer가 있는 오브젝트를 선택해주세요.");
                }

                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button(new GUIContent("×", "대상 선택 해제"), GUILayout.Width(28), GUILayout.Height(24)))
            {
                ClearTarget();
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            if (skinnedMeshRenderers.Count > 0)
            {
                EditorGUILayout.Space(3);
                DrawMeshList();
            }

            if (HasValidTarget())
            {
                DrawCompactInfo();
            }
        }

        private void DrawMeshList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField($"Found Meshes ({skinnedMeshRenderers.Count})", EditorStyles.miniBoldLabel);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos,
                GUILayout.Height(Mathf.Min(100, skinnedMeshRenderers.Count * 22 + 4)));

            Color prevBg = GUI.backgroundColor;

            for (int i = 0; i < skinnedMeshRenderers.Count; i++)
            {
                var smr = skinnedMeshRenderers[i];
                if (smr == null || smr.sharedMesh == null) continue;

                bool isSelected = !combineAllMeshes && smr == selectedRenderer;

                EditorGUILayout.BeginHorizontal();

                GUI.backgroundColor = isSelected ? TelleRGUI.SuccessButton : prevBg;

                if (GUILayout.Button(isSelected ? "✓" : " ", GUILayout.Width(24)))
                {
                    selectedRenderer = smr;
                    meshName = $"{smr.name}_Collider";
                    combineAllMeshes = false;
                    UpdateOriginalTriCount();
                    SchedulePreview(true);
                }

                GUI.backgroundColor = prevBg;

                EditorGUILayout.LabelField(smr.name, GUILayout.MinWidth(100));
                EditorGUILayout.LabelField($"{TriangleCount(smr.sharedMesh):N0} tris", EditorStyles.miniLabel,
                    GUILayout.Width(80));

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(3);

            EditorGUI.BeginChangeCheck();
            combineAllMeshes = EditorGUILayout.ToggleLeft(
                new GUIContent("Combine All Meshes", "발견된 모든 메시를 하나의 콜라이더 메시로 합칩니다."), combineAllMeshes);

            if (EditorGUI.EndChangeCheck())
            {
                if (combineAllMeshes)
                {
                    selectedRenderer = null;
                    if (targetObject != null) meshName = $"{targetObject.name}_Combined_Collider";
                }
                else
                {
                    SelectLargestRenderer();
                }

                UpdateOriginalTriCount();
                SchedulePreview(true);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCompactInfo()
        {
            int verts = 0, tris = 0;

            if (combineAllMeshes)
            {
                foreach (var smr in skinnedMeshRenderers)
                {
                    if (smr != null && smr.sharedMesh != null)
                    {
                        verts += smr.sharedMesh.vertexCount;
                        tris += TriangleCount(smr.sharedMesh);
                    }
                }
            }
            else if (selectedRenderer != null && selectedRenderer.sharedMesh != null)
            {
                verts = selectedRenderer.sharedMesh.vertexCount;
                tris = TriangleCount(selectedRenderer.sharedMesh);
            }

            Transform space = GetColliderSpace();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Source: {verts:N0} vertices, {tris:N0} triangles", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                new GUIContent($"Collider Target: {(space != null ? space.name : "-")}", "MeshCollider가 추가될 오브젝트. 정점은 이 오브젝트의 로컬 공간으로 변환됩니다."),
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawSimplificationSettings()
        {
            TelleRGUI.Section("Simplification");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUI.BeginChangeCheck();
            enableSimplification = EditorGUILayout.ToggleLeft(
                new GUIContent("Enable Simplification", "삼각형 수를 줄여 물리 비용을 낮춥니다. UV·노말 이음새는 위치 기준으로 용접한 뒤 단순화하므로 구멍이 생기지 않습니다."),
                enableSimplification);
            if (EditorGUI.EndChangeCheck())
            {
                if (enableSimplification)
                {
                    ApplyPreset(selectedPreset);
                }

                SchedulePreview(true);
            }

            if (enableSimplification && originalTriCount > 0)
            {
                EditorGUILayout.Space(6);

                EditorGUILayout.LabelField("Quality Preset", EditorStyles.miniBoldLabel);

                EditorGUI.BeginChangeCheck();
                selectedPreset = (SimplificationPreset)GUILayout.SelectionGrid(
                    (int)selectedPreset,
                    presetLabels,
                    2,
                    GUILayout.Height(66));

                if (EditorGUI.EndChangeCheck())
                {
                    ApplyPreset(selectedPreset);
                    SchedulePreview(false);
                }

                if (selectedPreset == SimplificationPreset.Custom)
                {
                    EditorGUILayout.Space(4);

                    EditorGUI.BeginChangeCheck();
                    quality = EditorGUILayout.Slider(
                        new GUIContent("Quality", "남길 삼각형 비율 (1 = 원본 유지)"), quality, 0.01f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        targetTriCount = Mathf.Max(4, Mathf.RoundToInt(originalTriCount * quality));
                        // 드래그 중에는 매 이벤트마다 다시 계산하지 않고, 멈춘 뒤(또는 놓은 뒤) 한 번만 갱신
                        SchedulePreview(false);
                    }
                }

                EditorGUILayout.Space(6);

                EditorGUI.BeginChangeCheck();
                recalculateNormals = EditorGUILayout.ToggleLeft("Recalculate Normals", recalculateNormals);
                if (EditorGUI.EndChangeCheck())
                {
                    SchedulePreview(false);
                }

                EditorGUILayout.Space(6);
                DrawSimplificationResult();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawSimplificationResult()
        {
            if (previewMesh == null) return;

            int resultTris = previewTriCount;
            float savedPercent = originalTriCount > 0 ? (1f - (float)resultTris / originalTriCount) * 100f : 0f;

            Rect rect = EditorGUILayout.BeginVertical();
            TelleRGUI.DrawBackground(rect, TelleRGUI.RowBg);

            EditorGUILayout.Space(4);

            string arrow = savedPercent > 0 ? "→" : "=";
            EditorGUILayout.LabelField($"{originalTriCount:N0} {arrow} {resultTris:N0} triangles", TelleRGUI.CenteredBold);

            if (savedPercent > 0)
            {
                EditorGUILayout.LabelField($"{savedPercent:F1}% 감소", SavedPercentStyle);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        private void ApplyPreset(SimplificationPreset preset)
        {
            if (preset != SimplificationPreset.Custom)
            {
                quality = presetValues[(int)preset];
            }

            targetTriCount = Mathf.Max(4, Mathf.RoundToInt(originalTriCount * quality));
        }

        private void DrawOutputSettings()
        {
            TelleRGUI.Section("Output");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            meshName = EditorGUILayout.TextField(new GUIContent("Name", "저장할 메시 에셋 파일명 (.asset)"), meshName);

            EditorGUILayout.BeginHorizontal();
            saveFolderPath = EditorGUILayout.TextField(new GUIContent("Folder", "Assets 폴더 안의 저장 경로"), saveFolderPath);
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                string selected = EditorUtility.OpenFolderPanel("저장 폴더 선택", "Assets", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    string relative = ToProjectRelativeAssetsPath(selected);
                    if (relative != null)
                    {
                        saveFolderPath = relative;
                        GUI.FocusControl(null);
                    }
                    else
                    {
                        TelleRGUI.Info("저장 경로 오류", "프로젝트의 Assets 폴더 안에 있는 폴더만 선택할 수 있습니다.");
                    }
                }

                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            replaceExistingColliders = EditorGUILayout.ToggleLeft(
                new GUIContent("Replace Existing MeshCollider", "대상 오브젝트에 이미 있는 MeshCollider만 제거하고 새로 추가합니다. 다른 종류의 Collider는 건드리지 않습니다."),
                replaceExistingColliders);

            EditorGUI.BeginChangeCheck();
            makeConvex = EditorGUILayout.ToggleLeft(
                new GUIContent("Convex", "Rigidbody(비키네마틱)에 붙일 때 필요합니다. PhysX 제한으로 최대 255 폴리곤의 볼록 껍질 하나가 됩니다."),
                makeConvex);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(ConvexPrefKey, makeConvex);
            }

            if (makeConvex)
            {
                EditorGUILayout.HelpBox(
                    "Convex를 켜면 PhysX가 최대 255 폴리곤의 볼록 껍질 하나로 다시 만들기 때문에 단순화 설정이 거의 의미가 없고 오목한 부분(팔 사이, 겨드랑이 등)이 모두 메워집니다.\n" +
                    "Rigidbody에 오목한 형태가 필요하면 Concave Mesh Collider 도구로 볼록 조각 여러 개를 만드세요.",
                    MessageType.Warning);
                if (GUILayout.Button(new GUIContent("Open Concave Mesh Collider",
                        "볼록 조각 여러 개로 콜라이더를 만드는 Concave Mesh Collider 창을 엽니다.")))
                {
                    OpenConcaveTool();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("현재 포즈 기준으로 베이크됩니다. 바인드 포즈가 필요하면 애니메이션 프리뷰를 끄고 실행하세요.", MessageType.None);
            EditorGUILayout.Space(4);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = TelleRGUI.SuccessButton;
            if (GUILayout.Button("Create MeshCollider", GUILayout.Height(32)))
            {
                // 다이얼로그·진행바는 OnGUI 밖에서 띄워 레이아웃 오류를 피한다.
                EditorApplication.delayCall += CreateMeshCollider;
            }

            GUI.backgroundColor = prevBg;

            if (GUILayout.Button("Save Mesh Only"))
            {
                EditorApplication.delayCall += BakeMeshOnly;
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawPreview()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Preview", TelleRGUI.SubHeader);
            if (GUILayout.Button(new GUIContent("Refresh", "현재 포즈로 미리보기를 다시 만듭니다."), EditorStyles.miniButton, GUILayout.Width(60)))
            {
                SchedulePreview(true);
            }

            EditorGUILayout.EndHorizontal();
            TelleRGUI.DrawSeparator(1f, 1f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (previewMesh != null)
            {
                if (meshPreviewEditor == null)
                {
                    meshPreviewEditor = Editor.CreateEditor(previewMesh);
                }

                if (meshPreviewEditor != null)
                {
                    meshPreviewEditor.OnInteractivePreviewGUI(
                        GUILayoutUtility.GetRect(200, 180),
                        EditorStyles.helpBox);
                }

                EditorGUILayout.LabelField(
                    $"{previewMesh.vertexCount:N0} verts, {previewTriCount:N0} tris",
                    EditorStyles.centeredGreyMiniLabel);
            }
            else if (previewPending)
            {
                EditorGUILayout.HelpBox("미리보기를 준비하는 중입니다...", MessageType.None);
            }
            else if (previewCanceled)
            {
                EditorGUILayout.HelpBox("미리보기 생성을 취소했습니다. Refresh를 누르면 다시 만듭니다.", MessageType.Info);
            }
            else if (previewDeferred)
            {
                EditorGUILayout.HelpBox("스크립트 컴파일 후에는 큰 메시의 미리보기를 자동으로 다시 만들지 않습니다. Refresh를 누르면 다시 만듭니다.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("대상을 선택하면 미리보기가 표시됩니다.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void UpdateOriginalTriCount()
        {
            originalTriCount = 0;

            if (combineAllMeshes)
            {
                foreach (var smr in skinnedMeshRenderers)
                {
                    if (smr != null && smr.sharedMesh != null)
                    {
                        originalTriCount += TriangleCount(smr.sharedMesh);
                    }
                }
            }
            else if (selectedRenderer != null && selectedRenderer.sharedMesh != null)
            {
                originalTriCount = TriangleCount(selectedRenderer.sharedMesh);
            }

            targetTriCount = Mathf.Max(4, Mathf.RoundToInt(originalTriCount * quality));
        }

        // ─── Preview scheduling (debounce) ───

        /// <summary>
        /// 미리보기 갱신을 예약한다. OnGUI 안에서 무거운 베이크·단순화를 돌리지 않고,
        /// 입력이 멈춘 뒤(immediate면 다음 에디터 틱) EditorApplication.update에서 한 번만 실행한다.
        /// </summary>
        private void SchedulePreview(bool immediate)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!previewPending) previewRequestTime = now;
            previewPending = true;
            previewCanceled = false;
            previewRestorePending = false;
            previewDeferred = false;
            previewDueTime = now + (immediate ? 0.0 : PreviewDebounceSeconds);

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            Repaint();
        }

        /// <summary>
        /// 도메인 리로드 후 창이 처음 그려질 때 호출된다. 진행바가 뜰 만큼 무거운 단순화는
        /// 사용자가 요청하지 않았으므로 자동으로 돌리지 않고 Refresh를 기다린다.
        /// </summary>
        private void RestorePreviewAfterReload()
        {
            previewRestorePending = false;
            if (!HasValidTarget() || previewMesh != null || previewPending) return;

            if (enableSimplification && originalTriCount >= PreviewProgressThreshold)
            {
                previewDeferred = true;
                return;
            }

            SchedulePreview(true);
        }

        private void OnEditorUpdate()
        {
            if (this == null || !previewPending)
            {
                EditorApplication.update -= OnEditorUpdate;
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < previewDueTime) return;

            // 슬라이더를 잡고 있는 동안은 미룬다(너무 오래 잡혀 있으면 그냥 실행).
            if (GUIUtility.hotControl != 0 && now - previewRequestTime < PreviewMaxHoldSeconds) return;

            EditorApplication.update -= OnEditorUpdate;
            previewPending = false;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            ClearPreview();
            previewCanceled = false;

            if (!HasValidTarget())
            {
                Repaint();
                return;
            }

            try
            {
                string progressTitle = originalTriCount >= PreviewProgressThreshold ? "Skinned Mesh Collider 미리보기" : null;
                previewMesh = BuildColliderMesh("Preview", progressTitle, out bool canceled);
                previewCanceled = canceled;
                previewTriCount = previewMesh != null ? TriangleCount(previewMesh) : 0;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + $"미리보기 생성 실패: {e}");
                ClearPreview();
            }

            if (previewMesh != null)
            {
                meshPreviewEditor = Editor.CreateEditor(previewMesh);
            }

            Repaint();
        }

        private void ClearPreview()
        {
            if (meshPreviewEditor != null)
            {
                DestroyImmediate(meshPreviewEditor);
                meshPreviewEditor = null;
            }

            if (previewMesh != null)
            {
                DestroyImmediate(previewMesh);
                previewMesh = null;
            }

            previewTriCount = 0;
        }

        // ─── Mesh building (미리보기와 결과가 같은 경로를 쓴다) ───

        /// <summary>콜라이더가 붙을 오브젝트. 정점은 이 Transform의 로컬 공간으로 변환된다.</summary>
        private Transform GetColliderSpace()
        {
            if (targetObject != null) return targetObject.transform;
            if (selectedRenderer != null) return selectedRenderer.transform.root;
            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr != null) return smr.transform.root;
            }

            return null;
        }

        /// <summary>
        /// 현재 설정으로 콜라이더 메시를 만든다(HideAndDontSave 임시 메시). 저장은 호출자가 한다.
        /// progressTitle이 있으면 취소 가능한 진행바를 띄우고, 취소하면 null과 canceled=true를 반환한다.
        /// </summary>
        private Mesh BuildColliderMesh(string name, string progressTitle, out bool canceled)
        {
            canceled = false;

            Transform space = GetColliderSpace();
            if (space == null) return null;

            Mesh baked = combineAllMeshes ? BakeCombined(space) : BakeSingle(space);
            if (baked == null) return null;

            baked.name = name;

            if (enableSimplification && targetTriCount < TriangleCount(baked))
            {
                Mesh simplified;
                try
                {
                    simplified = SimplifyMesh(baked, targetTriCount, progressTitle, out canceled);
                }
                finally
                {
                    DestroyImmediate(baked);
                }

                if (canceled || simplified == null)
                {
                    if (simplified != null) DestroyImmediate(simplified);
                    return null;
                }

                simplified.name = name;
                baked = simplified;
            }

            return baked;
        }

        private Mesh BakeSingle(Transform space)
        {
            if (selectedRenderer == null || selectedRenderer.sharedMesh == null) return null;

            Mesh bakedMesh = CreateTempMesh("Baked", selectedRenderer.sharedMesh.vertexCount);
            // useScale:true — 기본값(false)은 본 체인 스케일이 정점에 남아 스케일된 캐릭터에서
            // 콜라이더가 스케일 제곱 크기가 됨
            selectedRenderer.BakeMesh(bakedMesh, true);

            // 콜라이더가 붙는 대상(targetObject/루트) 공간으로 정점 변환 — 콤바인 경로와 동일한 보정.
            // 없으면 렌더러가 루트와 위치/회전이 다른 리그에서 콜라이더가 어긋남.
            Matrix4x4 toSpace = space.worldToLocalMatrix * selectedRenderer.transform.localToWorldMatrix;
            if (toSpace != Matrix4x4.identity)
            {
                Vector3[] verts = bakedMesh.vertices;
                for (int i = 0; i < verts.Length; i++) verts[i] = toSpace.MultiplyPoint3x4(verts[i]);
                bakedMesh.vertices = verts;

                // 미러(음수 스케일) 변환이면 감김 방향을 뒤집어야 면이 바깥을 향한다.
                if (toSpace.determinant < 0f) FlipWinding(bakedMesh);
            }

            bakedMesh.RecalculateBounds();
            bakedMesh.RecalculateNormals();
            return bakedMesh;
        }

        private Mesh BakeCombined(Transform space)
        {
            // CombineMeshes는 CombineInstance마다 서브메시 하나(subMeshIndex)만 넣으므로
            // 머티리얼이 여러 개인 렌더러의 나머지 서브메시가 빠진다. 정점·인덱스를 직접 이어 붙여
            // 모든 Triangles/Quads 서브메시를 하나의 서브메시로 합친다(정점 중복 없음).
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            Mesh bakedMesh = null;

            try
            {
                foreach (var smr in skinnedMeshRenderers)
                {
                    if (smr == null || smr.sharedMesh == null) continue;

                    bakedMesh = CreateTempMesh("Baked", smr.sharedMesh.vertexCount);
                    smr.BakeMesh(bakedMesh, true);

                    Matrix4x4 toSpace = space.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                    int baseVertex = vertices.Count;

                    Vector3[] bakedVertices = bakedMesh.vertices;
                    for (int i = 0; i < bakedVertices.Length; i++)
                    {
                        vertices.Add(toSpace.MultiplyPoint3x4(bakedVertices[i]));
                    }

                    // 미러(음수 스케일) 렌더러는 감김 방향을 뒤집어야 면이 바깥을 향한다(CombineMeshes와 동일).
                    bool flip = toSpace.determinant < 0f;
                    int[] bakedTriangles = GetTriangleIndices(bakedMesh);
                    for (int i = 0; i + 2 < bakedTriangles.Length; i += 3)
                    {
                        triangles.Add(baseVertex + bakedTriangles[i]);
                        triangles.Add(baseVertex + bakedTriangles[flip ? i + 2 : i + 1]);
                        triangles.Add(baseVertex + bakedTriangles[flip ? i + 1 : i + 2]);
                    }

                    DestroyImmediate(bakedMesh);
                    bakedMesh = null;
                }

                if (triangles.Count == 0) return null;

                // 65535 정점 초과 시 32비트 인덱스를 먼저 지정해야 합친 메시가 잘리지 않는다.
                Mesh combinedMesh = CreateTempMesh("Combined", vertices.Count);
                combinedMesh.SetVertices(vertices);
                combinedMesh.SetTriangles(triangles, 0);
                combinedMesh.RecalculateBounds();
                combinedMesh.RecalculateNormals();
                return combinedMesh;
            }
            finally
            {
                if (bakedMesh != null) DestroyImmediate(bakedMesh);
            }
        }

        private Mesh SimplifyMesh(Mesh original, int targetTris, string progressTitle, out bool canceled)
        {
            canceled = false;
            if (original == null) return null;

            Vector3[] vertices = original.vertices;
            int[] triangles = GetTriangleIndices(original);

            if (targetTris >= triangles.Length / 3)
            {
                Mesh copy = Instantiate(original);
                copy.name = "Simplified";
                copy.hideFlags = HideFlags.HideAndDontSave;
                return copy;
            }

            bool showProgress = !string.IsNullOrEmpty(progressTitle);

            try
            {
                if (showProgress && EditorUtility.DisplayCancelableProgressBar(progressTitle, "이음새 정점 용접 중...", 0f))
                {
                    canceled = true;
                    return null;
                }

                // UV·노말 이음새로 쪼개진 정점을 위치 기준으로 합쳐야 단순화 중 이음새가 따로 줄어들며
                // 구멍·틈이 생기지 않는다. 콜라이더에는 UV가 필요 없으므로 버린다.
                // 양면 카드처럼 같은 위치에서 반대를 향하는 표면은 노말로 구분해 합치지 않는다.
                Vector3[] normals = original.normals;
                if (normals != null && normals.Length != vertices.Length) normals = null;

                float epsilon = Mathf.Max(original.bounds.size.magnitude * WeldRelativeEpsilon, 1e-7f);
                int[] remap = MeshDecimator.WeldByPosition(vertices, normals, triangles, epsilon, WeldMinNormalDot,
                    out Vector3[] weldedVertices, out int[] weldedTriangles);

                Vector3[] weldedNormals = null;
                if (!recalculateNormals)
                {
                    if (normals != null)
                    {
                        weldedNormals = new Vector3[weldedVertices.Length];
                        for (int i = 0; i < normals.Length; i++) weldedNormals[remap[i]] += normals[i];
                        for (int i = 0; i < weldedNormals.Length; i++) weldedNormals[i] = weldedNormals[i].normalized;
                    }
                }

                if (showProgress && EditorUtility.DisplayCancelableProgressBar(progressTitle, "연결 정보 구성 중...", 0.05f))
                {
                    canceled = true;
                    return null;
                }

                var simplifier = new MeshDecimator(weldedVertices, weldedTriangles, weldedNormals, null);

                Func<float, bool> onProgress = null;
                if (showProgress)
                {
                    onProgress = p => EditorUtility.DisplayCancelableProgressBar(progressTitle,
                        $"메시 단순화 중... {Mathf.RoundToInt(p * 100f)}%", 0.05f + 0.95f * p);
                }

                if (!simplifier.Simplify(targetTris, onProgress))
                {
                    canceled = true;
                    return null;
                }

                Vector3[] resultVertices = simplifier.GetVertices();
                Mesh result = CreateTempMesh("Simplified", resultVertices.Length);
                result.vertices = resultVertices;
                result.triangles = simplifier.GetTriangles();

                Vector3[] resultNormals = recalculateNormals ? null : simplifier.GetNormals();
                if (resultNormals != null && resultNormals.Length == resultVertices.Length)
                {
                    result.normals = resultNormals;
                }
                else
                {
                    result.RecalculateNormals();
                }

                result.RecalculateBounds();
                return result;
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Concave Mesh Collider 창을 연다. 메뉴가 없으면 오류 대신 안내만 한다.</summary>
        private static bool OpenConcaveTool()
        {
            // 없는 메뉴를 ExecuteMenuItem에 넘기면 Unity가 오류를 찍으므로 먼저 확인한다(ToolHub와 동일).
            if (Menu.GetEnabled(ConcaveToolMenuPath) && EditorApplication.ExecuteMenuItem(ConcaveToolMenuPath)) return true;

            Debug.LogWarning(LogPrefix + $"'{ConcaveToolMenuPath}' 메뉴를 찾을 수 없습니다.");
            TelleRGUI.Info("창 열기 실패",
                $"Concave Mesh Collider 창을 열지 못했습니다.\n메뉴를 찾을 수 없습니다: {ConcaveToolMenuPath}\n\n" +
                "패키지를 최신 버전으로 업데이트했는지, 콘솔에 컴파일 오류가 없는지 확인하세요.");
            return false;
        }

        /// <summary>임시 메시. 씬/에셋에 저장되지 않고, 65535 정점 초과 시 32비트 인덱스를 쓴다.</summary>
        private static Mesh CreateTempMesh(string name, int vertexCount)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            if (vertexCount > 65535) mesh.indexFormat = IndexFormat.UInt32;
            return mesh;
        }

        /// <summary>삼각형 수(Triangles/Quads 토폴로지 서브메시 합). 인덱스 배열을 할당하지 않는다.</summary>
        private static int TriangleCount(Mesh mesh)
        {
            if (mesh == null) return 0;

            long tris = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                MeshTopology topology = mesh.GetTopology(s);
                if (topology == MeshTopology.Triangles) tris += mesh.GetIndexCount(s) / 3;
                else if (topology == MeshTopology.Quads) tris += mesh.GetIndexCount(s) / 4 * 2;
            }

            return (int)Math.Min(tris, int.MaxValue);
        }

        /// <summary>Triangles/Quads 서브메시의 감김 방향을 뒤집는다(선·점 서브메시는 그대로).</summary>
        private static void FlipWinding(Mesh mesh)
        {
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                MeshTopology topology = mesh.GetTopology(s);
                int stride = topology == MeshTopology.Triangles ? 3 : topology == MeshTopology.Quads ? 4 : 0;
                if (stride == 0) continue;

                int[] indices = mesh.GetIndices(s);
                for (int i = 0; i + stride - 1 < indices.Length; i += stride)
                {
                    // (a,b,c) → (a,c,b), (a,b,c,d) → (a,d,c,b)
                    int last = i + stride - 1;
                    int tmp = indices[i + 1];
                    indices[i + 1] = indices[last];
                    indices[last] = tmp;
                }

                mesh.SetIndices(indices, topology, s, false);
            }
        }

        /// <summary>모든 서브메시의 삼각형 인덱스를 하나로 모은다(Quads는 삼각형 2개로 분할, 선·점은 제외).</summary>
        private static int[] GetTriangleIndices(Mesh mesh)
        {
            var result = new List<int>(TriangleCount(mesh) * 3);
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                MeshTopology topology = mesh.GetTopology(s);
                if (topology == MeshTopology.Triangles)
                {
                    result.AddRange(mesh.GetIndices(s));
                }
                else if (topology == MeshTopology.Quads)
                {
                    int[] q = mesh.GetIndices(s);
                    for (int i = 0; i + 3 < q.Length; i += 4)
                    {
                        result.Add(q[i]);
                        result.Add(q[i + 1]);
                        result.Add(q[i + 2]);
                        result.Add(q[i]);
                        result.Add(q[i + 2]);
                        result.Add(q[i + 3]);
                    }
                }
            }

            return result.ToArray();
        }

        // ─── Target management ───

        private void SetTargetFromGameObject(GameObject go)
        {
            targetObject = go;
            skinnedMeshRenderers.Clear();
            selectedRenderer = null;
            combineAllMeshes = false;

            // 비활성 자식(모듈형 캐릭터의 꺼진 의상·헤어 변형 등)은 제외한다.
            // 선택한 오브젝트 자체가 비활성이면 그 아래를 기준으로 활성 여부를 본다.
            var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (var renderer in renderers)
            {
                if (renderer.sharedMesh != null && IsActiveUnder(renderer.transform, go.transform))
                {
                    skinnedMeshRenderers.Add(renderer);
                }
            }

            if (skinnedMeshRenderers.Count == 0)
            {
                TelleRGUI.Info("대상 없음", $"'{go.name}'과(와) 그 자식에서 메시가 할당된 SkinnedMeshRenderer를 찾을 수 없습니다.");
                ClearTarget();
                return;
            }

            SelectLargestRenderer();
            UpdateOriginalTriCount();
            SchedulePreview(true);
        }

        /// <summary>t부터 root 직전까지 모든 오브젝트가 activeSelf인지(root 자신의 활성 상태는 보지 않는다).</summary>
        private static bool IsActiveUnder(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
            {
                if (!t.gameObject.activeSelf) return false;
            }

            return true;
        }

        private void SelectLargestRenderer()
        {
            selectedRenderer = null;
            int maxVertices = -1;
            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr == null || smr.sharedMesh == null) continue;
                if (smr.sharedMesh.vertexCount > maxVertices)
                {
                    maxVertices = smr.sharedMesh.vertexCount;
                    selectedRenderer = smr;
                }
            }

            if (selectedRenderer != null)
            {
                meshName = $"{selectedRenderer.name}_Collider";
            }
        }

        private void ClearTarget()
        {
            targetObject = null;
            selectedRenderer = null;
            skinnedMeshRenderers.Clear();
            meshName = "";
            combineAllMeshes = false;
            originalTriCount = 0;
            previewPending = false;
            previewCanceled = false;
            previewRestorePending = false;
            previewDeferred = false;
            ClearPreview();
        }

        // ─── Actions ───

        private void CreateMeshCollider()
        {
            if (this == null) return;

            if (!HasValidTarget())
            {
                TelleRGUI.Info("대상 없음", "SkinnedMeshRenderer를 선택해주세요.");
                return;
            }

            Transform space = GetColliderSpace();
            GameObject colliderTarget = space != null ? space.gameObject : null;
            if (colliderTarget == null)
            {
                TelleRGUI.Info("대상 없음", "Collider를 추가할 대상 오브젝트가 없습니다.");
                return;
            }

            if (!TryPrepareOutputPath(out string meshPath, out Mesh existingMesh)) return;

            Mesh built = null;
            try
            {
                built = BuildColliderMesh(Path.GetFileNameWithoutExtension(meshPath), "Skinned Mesh Collider 생성", out bool canceled);
                if (canceled)
                {
                    Debug.Log(LogPrefix + "생성을 취소했습니다. 아무것도 저장하지 않았습니다.");
                    return;
                }

                if (built == null)
                {
                    TelleRGUI.Info("생성 실패", "베이크할 메시가 없습니다.");
                    return;
                }

                // delayCall에서 실행되므로 이전 작업과 섞이지 않게 새 Undo 그룹을 연다.
                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Create Skinned Mesh Collider");
                int undoGroup = Undo.GetCurrentGroup();

                Mesh savedMesh = SaveMesh(built, meshPath, existingMesh);
                if (savedMesh == null) return;

                if (replaceExistingColliders)
                {
                    // MeshCollider만 교체 — Collider 전체를 지우면 CharacterController·WheelCollider까지 파괴됨
                    var existingColliders = colliderTarget.GetComponents<MeshCollider>();
                    foreach (var col in existingColliders)
                    {
                        Undo.DestroyObjectImmediate(col);
                    }
                }

                var meshCollider = Undo.AddComponent<MeshCollider>(colliderTarget);
                meshCollider.sharedMesh = savedMesh;
                meshCollider.convex = makeConvex;

                Undo.CollapseUndoOperations(undoGroup);
                EditorUtility.SetDirty(colliderTarget);

                Debug.Log(LogPrefix + $"MeshCollider 생성: {colliderTarget.name} ({TriangleCount(savedMesh):N0} tris) → {meshPath}");
                TelleRGUI.Info(
                    "생성 완료",
                    "MeshCollider를 추가했습니다.\n\n" +
                    $"대상: {colliderTarget.name}\n" +
                    $"메시: {meshPath}\n" +
                    $"삼각형: {TriangleCount(savedMesh):N0}");
            }
            catch (Exception e)
            {
                TelleRGUI.Info("생성 실패", $"MeshCollider를 만들지 못했습니다.\n\n{e.Message}");
                Debug.LogError(LogPrefix + e);
            }
            finally
            {
                // 저장 전에 실패했으면 임시 메시를 정리(저장 후에는 에셋이거나 이미 파기됨)
                if (built != null && !EditorUtility.IsPersistent(built)) DestroyImmediate(built);
            }
        }

        private void BakeMeshOnly()
        {
            if (this == null) return;

            if (!HasValidTarget())
            {
                TelleRGUI.Info("대상 없음", "SkinnedMeshRenderer를 선택해주세요.");
                return;
            }

            if (!TryPrepareOutputPath(out string meshPath, out Mesh existingMesh)) return;

            Mesh built = null;
            try
            {
                built = BuildColliderMesh(Path.GetFileNameWithoutExtension(meshPath), "Skinned Mesh Collider 메시 저장", out bool canceled);
                if (canceled)
                {
                    Debug.Log(LogPrefix + "저장을 취소했습니다. 아무것도 저장하지 않았습니다.");
                    return;
                }

                if (built == null)
                {
                    TelleRGUI.Info("저장 실패", "베이크할 메시가 없습니다.");
                    return;
                }

                Undo.IncrementCurrentGroup();
                Undo.SetCurrentGroupName("Save Skinned Collider Mesh");

                Mesh savedMesh = SaveMesh(built, meshPath, existingMesh);
                if (savedMesh == null) return;

                Selection.activeObject = savedMesh;
                EditorGUIUtility.PingObject(savedMesh);

                Debug.Log(LogPrefix + $"메시 저장: {meshPath} ({TriangleCount(savedMesh):N0} tris)");
                TelleRGUI.Info(
                    "저장 완료",
                    "메시를 저장했습니다.\n\n" +
                    $"경로: {meshPath}\n" +
                    $"삼각형: {TriangleCount(savedMesh):N0}");
            }
            catch (Exception e)
            {
                TelleRGUI.Info("저장 실패", $"메시를 저장하지 못했습니다.\n\n{e.Message}");
                Debug.LogError(LogPrefix + e);
            }
            finally
            {
                if (built != null && !EditorUtility.IsPersistent(built)) DestroyImmediate(built);
            }
        }

        // ─── Saving ───

        /// <summary>
        /// 파일명·저장 경로를 검증하고, 기존 에셋이 있으면 교체 확인을 받는다(베이크 전에 묻는다).
        /// '메시 생성'과 '메시만 저장'이 같은 검증을 쓴다.
        /// </summary>
        private bool TryPrepareOutputPath(out string meshPath, out Mesh existingMesh)
        {
            meshPath = null;
            existingMesh = null;

            string safeName = string.Join("_", (meshName ?? "").Trim().Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(safeName))
            {
                TelleRGUI.Info("파일명 없음", "저장할 메시 파일명(Name)을 입력해주세요.");
                return false;
            }

            string folder = NormalizeAssetsFolder(saveFolderPath);
            if (folder == null)
            {
                TelleRGUI.Info("저장 경로 오류",
                    "저장 경로(Folder)는 프로젝트의 Assets 폴더 안이어야 합니다.\n예: Assets/Mesh/ColliderMesh\n\n" +
                    $"현재 값: {saveFolderPath}");
                return false;
            }

            meshPath = $"{folder}/{safeName}.asset";

            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(meshPath);
            if (existing != null)
            {
                existingMesh = existing as Mesh;
                if (existingMesh == null)
                {
                    TelleRGUI.Info("저장 경로 오류",
                        $"'{meshPath}'에 메시가 아닌 에셋({existing.GetType().Name})이 이미 있습니다.\n다른 파일명을 입력해주세요.");
                    meshPath = null;
                    return false;
                }

                if (!TelleRGUI.Confirm(
                        "메시 교체",
                        $"'{meshPath}' 메시가 이미 있습니다.\n\n" +
                        "기존 메시의 정점·삼각형 데이터가 새 메시로 교체되며, 이전 내용은 파일에서 사라집니다.\n" +
                        "이 에셋을 쓰는 씬·프리팹의 참조(GUID)는 그대로 유지됩니다.",
                        "교체", "취소"))
                {
                    meshPath = null;
                    existingMesh = null;
                    return false;
                }
            }

            return true;
        }

        private Mesh SaveMesh(Mesh mesh, string meshPath, Mesh existingMesh)
        {
            string folder = Path.GetDirectoryName(meshPath)?.Replace('\\', '/');
            EnsureAssetFolder(folder);

            // 임시 메시 플래그(HideAndDontSave)가 에셋에 복사·저장되지 않도록 해제
            mesh.hideFlags = HideFlags.None;
            mesh.name = Path.GetFileNameWithoutExtension(meshPath);

            if (existingMesh == null)
            {
                existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            }

            if (existingMesh != null)
            {
                // DeleteAsset + CreateAsset은 GUID가 바뀌어 기존 씬/프리팹 참조가 전부 Missing이 됨.
                // 내용만 교체해 GUID를 보존한다.
                Undo.RegisterCompleteObjectUndo(existingMesh, "Replace Collider Mesh");
                existingMesh.Clear();
                EditorUtility.CopySerialized(mesh, existingMesh);
                DestroyImmediate(mesh);
                EditorUtility.SetDirty(existingMesh);
                AssetDatabase.SaveAssets();
                return existingMesh;
            }

            AssetDatabase.CreateAsset(mesh, meshPath);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        }

        /// <summary>"Assets" 또는 "Assets/..." 형태로 정규화한다. Assets 밖이거나 잘못된 경로면 null.</summary>
        private static string NormalizeAssetsFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string p = path.Trim().Replace('\\', '/');
            while (p.EndsWith("/")) p = p.Substring(0, p.Length - 1);

            if (p != "Assets" && !p.StartsWith("Assets/", StringComparison.Ordinal)) return null;

            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (string segment in p.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..") return null;
                if (segment.Trim() != segment || segment.EndsWith(".")) return null;
                if (segment.IndexOfAny(invalid) >= 0) return null;
            }

            return p;
        }

        private static string ToProjectRelativeAssetsPath(string absolutePath)
        {
            string abs = absolutePath.Replace('\\', '/').TrimEnd('/');
            string dataPath = Application.dataPath.Replace('\\', '/').TrimEnd('/');

            if (string.Equals(abs, dataPath, StringComparison.OrdinalIgnoreCase)) return "Assets";
            if (abs.StartsWith(dataPath + "/", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeAssetsFolder("Assets" + abs.Substring(dataPath.Length));
            }

            return null;
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>
    /// 이차 오차(Quadric Error Metric) 기반 엣지 붕괴 메시 단순화.
    /// 링크 조건(정점·엣지)·삼각형 뒤집힘 검사로 구멍·비다양체 엣지를 만들지 않고, 열린 경계는 경계 평면으로 보존한다.
    /// UV·노말 이음새로 쪼개진 정점은 먼저 WeldByPosition(노말 구분 버전 권장)으로 합친 뒤 넘기는 것을 권장한다.
    /// </summary>
    public class MeshDecimator
    {
        private const double BoundaryWeight = 100.0;
        private const float MinNormalDot = 0.2f; // 붕괴 후 면 방향이 이 값(cos) 미만으로 바뀌면 거부
        private const int ProgressInterval = 1024;

        private readonly Vector3[] positions;
        private readonly Vector3[] normals;
        private readonly Vector2[] uvs;
        private readonly int[] tris;

        private readonly bool[] removedTriangles;
        private readonly bool[] removedVertices;
        private readonly bool[] boundaryVertices;
        private readonly List<int>[] vertexTriangles;
        private readonly Quadric[] quadrics;
        private readonly int[] vertexVersion;
        private readonly int[] mark;
        private int markStamp;
        private int activeTriangleCount;

        private readonly EdgeHeap edgeQueue = new EdgeHeap();
        private int sequence;

        private int[] compactOrder; // 출력 정점 순서 → 내부 정점 인덱스
        private int[] compactMap;   // 내부 정점 인덱스 → 출력 정점 인덱스 (-1 = 미사용)

        /// <summary>현재 남아 있는 삼각형 수.</summary>
        public int TriangleCount => activeTriangleCount;

        private struct EdgeCost : IComparable<EdgeCost>
        {
            public float Cost;
            public int V1;
            public int V2;
            public int V1Version;
            public int V2Version;
            public int Sequence; // 동일 비용 항목도 구분되도록 하는 고유 순번
            public Vector3 Target;

            public int CompareTo(EdgeCost other)
            {
                int costCompare = Cost.CompareTo(other.Cost);
                if (costCompare != 0) return costCompare;
                return Sequence.CompareTo(other.Sequence);
            }
        }

        private sealed class EdgeHeap
        {
            private EdgeCost[] items = new EdgeCost[256];
            public int Count { get; private set; }

            public void Push(EdgeCost item)
            {
                if (Count == items.Length) Array.Resize(ref items, items.Length * 2);
                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (items[parent].CompareTo(item) <= 0) break;
                    items[i] = items[parent];
                    i = parent;
                }

                items[i] = item;
            }

            public EdgeCost Pop()
            {
                EdgeCost top = items[0];
                EdgeCost last = items[--Count];
                int i = 0;
                while (true)
                {
                    int child = i * 2 + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && items[child + 1].CompareTo(items[child]) < 0) child++;
                    if (last.CompareTo(items[child]) <= 0) break;
                    items[i] = items[child];
                    i = child;
                }

                if (Count > 0) items[i] = last;
                return top;
            }
        }

        private struct Quadric
        {
            public double A2, AB, AC, AD, B2, BC, BD, C2, CD, D2;

            public static Quadric FromPlane(double a, double b, double c, double d, double w)
            {
                return new Quadric
                {
                    A2 = w * a * a, AB = w * a * b, AC = w * a * c, AD = w * a * d,
                    B2 = w * b * b, BC = w * b * c, BD = w * b * d,
                    C2 = w * c * c, CD = w * c * d,
                    D2 = w * d * d
                };
            }

            public void Add(in Quadric q)
            {
                A2 += q.A2; AB += q.AB; AC += q.AC; AD += q.AD;
                B2 += q.B2; BC += q.BC; BD += q.BD;
                C2 += q.C2; CD += q.CD;
                D2 += q.D2;
            }

            public double Evaluate(Vector3 p)
            {
                double x = p.x, y = p.y, z = p.z;
                return A2 * x * x + 2 * AB * x * y + 2 * AC * x * z + 2 * AD * x
                       + B2 * y * y + 2 * BC * y * z + 2 * BD * y
                       + C2 * z * z + 2 * CD * z + D2;
            }

            public bool TryOptimum(out Vector3 result)
            {
                double det = A2 * (B2 * C2 - BC * BC) - AB * (AB * C2 - BC * AC) + AC * (AB * BC - B2 * AC);
                double trace = A2 + B2 + C2;
                if (trace <= 0 || Math.Abs(det) <= 1e-6 * trace * trace * trace)
                {
                    result = default;
                    return false;
                }

                double inv = 1.0 / det;
                // A^-1 * (-b)
                double m00 = (B2 * C2 - BC * BC) * inv;
                double m01 = (AC * BC - AB * C2) * inv;
                double m02 = (AB * BC - AC * B2) * inv;
                double m11 = (A2 * C2 - AC * AC) * inv;
                double m12 = (AB * AC - A2 * BC) * inv;
                double m22 = (A2 * B2 - AB * AB) * inv;

                double x = -(m00 * AD + m01 * BD + m02 * CD);
                double y = -(m01 * AD + m11 * BD + m12 * CD);
                double z = -(m02 * AD + m12 * BD + m22 * CD);
                if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z))
                {
                    result = default;
                    return false;
                }

                result = new Vector3((float)x, (float)y, (float)z);
                return true;
            }
        }

        public MeshDecimator(Vector3[] verts, int[] tris, Vector3[] norms, Vector2[] uv)
        {
            if (verts == null) throw new ArgumentNullException(nameof(verts));
            if (tris == null) throw new ArgumentNullException(nameof(tris));

            int vertexCount = verts.Length;
            positions = (Vector3[])verts.Clone();
            normals = norms != null && norms.Length == vertexCount ? (Vector3[])norms.Clone() : null;
            uvs = uv != null && uv.Length == vertexCount ? (Vector2[])uv.Clone() : null;

            int triCount = tris.Length / 3;
            this.tris = new int[triCount * 3];
            Array.Copy(tris, this.tris, triCount * 3);

            removedTriangles = new bool[triCount];
            removedVertices = new bool[vertexCount];
            boundaryVertices = new bool[vertexCount];
            vertexTriangles = new List<int>[vertexCount];
            quadrics = new Quadric[vertexCount];
            vertexVersion = new int[vertexCount];
            mark = new int[vertexCount];

            for (int t = 0; t < triCount; t++)
            {
                int i0 = this.tris[t * 3], i1 = this.tris[t * 3 + 1], i2 = this.tris[t * 3 + 2];
                bool invalid = (uint)i0 >= (uint)vertexCount || (uint)i1 >= (uint)vertexCount || (uint)i2 >= (uint)vertexCount
                               || i0 == i1 || i1 == i2 || i2 == i0;
                if (invalid)
                {
                    removedTriangles[t] = true;
                    continue;
                }

                activeTriangleCount++;
                AddVertexTriangle(i0, t);
                AddVertexTriangle(i1, t);
                AddVertexTriangle(i2, t);
            }

            BuildQuadrics();
            BuildEdgeQueue();
        }

        private void AddVertexTriangle(int v, int t)
        {
            var list = vertexTriangles[v];
            if (list == null)
            {
                list = new List<int>(6);
                vertexTriangles[v] = list;
            }

            list.Add(t);
        }

        private bool TriangleContains(int t, int v)
        {
            int b = t * 3;
            return tris[b] == v || tris[b + 1] == v || tris[b + 2] == v;
        }

        private Vector3 FaceNormal(int t)
        {
            int b = t * 3;
            Vector3 p0 = positions[tris[b]];
            return Vector3.Cross(positions[tris[b + 1]] - p0, positions[tris[b + 2]] - p0);
        }

        private void BuildQuadrics()
        {
            int triCount = removedTriangles.Length;

            for (int t = 0; t < triCount; t++)
            {
                if (removedTriangles[t]) continue;

                int b = t * 3;
                Vector3 n = FaceNormal(t);
                double len = Math.Sqrt((double)n.x * n.x + (double)n.y * n.y + (double)n.z * n.z);
                if (len <= 0) continue;

                double a = n.x / len, bb = n.y / len, c = n.z / len;
                Vector3 p0 = positions[tris[b]];
                double d = -(a * p0.x + bb * p0.y + c * p0.z);
                Quadric q = Quadric.FromPlane(a, bb, c, d, len * 0.5); // 면적 가중
                quadrics[tris[b]].Add(q);
                quadrics[tris[b + 1]].Add(q);
                quadrics[tris[b + 2]].Add(q);
            }

            // 엣지별 삼각형 수를 정점 인접 목록으로 센다(해시 없이 O(삼각형)).
            // 열린 경계(삼각형 1개)는 면에 수직인 평면으로 붙잡아 가장자리가 안쪽으로 줄어들지 않게 하고,
            // 비다양체 엣지(3개 이상)의 정점은 경계로 취급해 붕괴 대상에서 보수적으로 다룬다.
            int[] edgeUse = new int[positions.Length];
            for (int v = 0; v < positions.Length; v++)
            {
                var list = vertexTriangles[v];
                if (list == null) continue;

                int stamp = NextStamp();
                foreach (int t in list)
                {
                    int b = t * 3;
                    for (int k = 0; k < 3; k++)
                    {
                        int o = tris[b + k];
                        if (o <= v) continue; // 각 엣지를 작은 쪽 정점에서 한 번만 센다
                        if (mark[o] != stamp)
                        {
                            mark[o] = stamp;
                            edgeUse[o] = 0;
                        }

                        edgeUse[o]++;
                    }
                }

                foreach (int t in list)
                {
                    int b = t * 3;
                    for (int k = 0; k < 3; k++)
                    {
                        int o = tris[b + k];
                        if (o <= v) continue;
                        int count = edgeUse[o];
                        if (count == 2) continue;

                        boundaryVertices[v] = true;
                        boundaryVertices[o] = true;
                        if (count != 1) continue;

                        Vector3 edge = positions[o] - positions[v];
                        Vector3 bn = Vector3.Cross(edge, FaceNormal(t));
                        double bl = Math.Sqrt((double)bn.x * bn.x + (double)bn.y * bn.y + (double)bn.z * bn.z);
                        if (bl <= 0) continue;

                        double a = bn.x / bl, bb = bn.y / bl, c = bn.z / bl;
                        Vector3 pv = positions[v];
                        double d = -(a * pv.x + bb * pv.y + c * pv.z);
                        Quadric q = Quadric.FromPlane(a, bb, c, d, BoundaryWeight * edge.sqrMagnitude);
                        quadrics[v].Add(q);
                        quadrics[o].Add(q);
                    }
                }
            }
        }

        private int NextStamp()
        {
            if (markStamp == int.MaxValue)
            {
                Array.Clear(mark, 0, mark.Length);
                markStamp = 0;
            }

            return ++markStamp;
        }

        private void BuildEdgeQueue()
        {
            for (int v = 0; v < positions.Length; v++)
            {
                var list = vertexTriangles[v];
                if (list == null) continue;

                int stamp = NextStamp();
                foreach (int t in list)
                {
                    if (removedTriangles[t]) continue;
                    int b = t * 3;
                    for (int k = 0; k < 3; k++)
                    {
                        int o = tris[b + k];
                        if (o == v || mark[o] == stamp) continue;
                        mark[o] = stamp;
                        if (v < o) PushEdge(v, o);
                    }
                }
            }
        }

        private void PushEdge(int a, int b)
        {
            if (a > b) (a, b) = (b, a);

            Quadric q = quadrics[a];
            q.Add(quadrics[b]);

            Vector3 pa = positions[a];
            Vector3 pb = positions[b];
            Vector3 mid = (pa + pb) * 0.5f;

            Vector3 best = mid;
            double bestError = q.Evaluate(mid);

            double e = q.Evaluate(pa);
            if (e < bestError) { bestError = e; best = pa; }
            e = q.Evaluate(pb);
            if (e < bestError) { bestError = e; best = pb; }

            // 최적점이 엣지에서 너무 멀면(거의 특이 행렬) 쓰지 않는다.
            if (q.TryOptimum(out Vector3 opt) && (opt - mid).sqrMagnitude <= (pb - pa).sqrMagnitude * 4f)
            {
                e = q.Evaluate(opt);
                if (e < bestError) { bestError = e; best = opt; }
            }

            edgeQueue.Push(new EdgeCost
            {
                Cost = (float)Math.Max(0.0, bestError),
                V1 = a,
                V2 = b,
                V1Version = vertexVersion[a],
                V2Version = vertexVersion[b],
                Sequence = sequence++,
                Target = best
            });
        }

        public void Simplify(int targetTriCount)
        {
            Simplify(targetTriCount, null);
        }

        /// <summary>
        /// 삼각형 수가 targetTriCount 이하가 될 때까지(또는 더 줄일 수 있는 엣지가 없을 때까지) 단순화한다.
        /// onProgress(0..1)가 true를 반환하면 중단하고 false를 반환한다(그때까지의 결과는 유지).
        /// </summary>
        public bool Simplify(int targetTriCount, Func<float, bool> onProgress)
        {
            InvalidateCompaction();

            int start = activeTriangleCount;
            float span = Mathf.Max(1, start - targetTriCount);
            int iterations = 0;

            while (activeTriangleCount > targetTriCount && edgeQueue.Count > 0)
            {
                if (onProgress != null && ++iterations % ProgressInterval == 0)
                {
                    if (onProgress(Mathf.Clamp01((start - activeTriangleCount) / span))) return false;
                }

                EdgeCost best = edgeQueue.Pop();

                if (removedVertices[best.V1] || removedVertices[best.V2]) continue;

                // 두 정점 중 하나라도 이 항목을 넣은 뒤 바뀌었으면 오래된 항목
                if (best.V1Version != vertexVersion[best.V1] || best.V2Version != vertexVersion[best.V2]) continue;

                TryCollapse(best.V1, best.V2, best.Target);
            }

            return true;
        }

        private bool TryCollapse(int a, int b, Vector3 target)
        {
            var listA = vertexTriangles[a];
            var listB = vertexTriangles[b];
            if (listA == null || listB == null) return false;

            // 링크 조건: 두 정점의 공통 이웃 수 == 엣지를 공유하는 삼각형 수여야 붕괴 후에도 다양체가 유지된다.
            int stamp = NextStamp();
            int shared = 0;
            int opp0 = -1, opp1 = -1; // 엣지 a-b를 공유하는 삼각형의 맞은편 정점
            foreach (int t in listA)
            {
                if (removedTriangles[t]) continue;
                int tb = t * 3;
                bool hasB = false;
                int opp = -1;
                for (int k = 0; k < 3; k++)
                {
                    int o = tris[tb + k];
                    if (o == b) hasB = true;
                    else if (o != a) opp = o;
                    if (o != a) mark[o] = stamp;
                }

                if (hasB)
                {
                    if (shared == 0) opp0 = opp;
                    else if (shared == 1) opp1 = opp;
                    shared++;
                }
            }

            if (shared == 0 || shared > 2) return false;
            // 경계 두 점을 잇는 내부 엣지를 접으면 메시가 한 점에서 조여진다.
            if (shared == 2 && boundaryVertices[a] && boundaryVertices[b]) return false;

            int common = 0;
            foreach (int t in listB)
            {
                if (removedTriangles[t]) continue;
                int tb = t * 3;
                for (int k = 0; k < 3; k++)
                {
                    int o = tris[tb + k];
                    if (o == a || o == b) continue;
                    if (mark[o] == stamp)
                    {
                        mark[o] = -stamp; // 중복 계산 방지
                        common++;
                    }
                }
            }

            if (common != shared) return false;

            // 엣지 링크 조건: 공통 이웃은 맞은편 두 정점 c·d뿐이므로, a-c-d와 b-c-d 삼각형이 모두 있으면
            // (사면체 같은 작은 닫힌 조각) 붕괴 후 같은 세 정점의 앞뒤 삼각형 두 장(부피 0)만 남는다.
            if (shared == 2 && HasTriangleWith(listA, opp0, opp1) && HasTriangleWith(listB, opp0, opp1)) return false;

            if (FlipsOrDegenerates(listA, a, b, target) || FlipsOrDegenerates(listB, b, a, target)) return false;

            // ─── 붕괴 적용: b → a ───
            positions[a] = target;
            quadrics[a].Add(quadrics[b]);
            boundaryVertices[a] |= boundaryVertices[b];

            if (normals != null)
            {
                normals[a] = (normals[a] + normals[b]).normalized;
            }

            if (uvs != null)
            {
                uvs[a] = (uvs[a] + uvs[b]) * 0.5f;
            }

            removedVertices[b] = true;
            vertexVersion[a]++;
            vertexVersion[b]++;

            int write = 0;
            for (int i = 0; i < listA.Count; i++)
            {
                int t = listA[i];
                if (removedTriangles[t]) continue;
                if (TriangleContains(t, b))
                {
                    removedTriangles[t] = true;
                    activeTriangleCount--;
                    continue;
                }

                listA[write++] = t;
            }

            listA.RemoveRange(write, listA.Count - write);

            foreach (int t in listB)
            {
                if (removedTriangles[t]) continue;
                int tb = t * 3;
                for (int k = 0; k < 3; k++)
                {
                    if (tris[tb + k] == b) tris[tb + k] = a;
                }

                listA.Add(t);
            }

            listB.Clear();
            vertexTriangles[b] = null;

            // a에 붙은 모든 엣지의 비용을 새 버전으로 다시 넣는다(이전 항목은 버전 불일치로 버려짐).
            stamp = NextStamp();
            foreach (int t in listA)
            {
                int tb = t * 3;
                for (int k = 0; k < 3; k++)
                {
                    int o = tris[tb + k];
                    if (o == a || mark[o] == stamp) continue;
                    mark[o] = stamp;
                    PushEdge(a, o);
                }
            }

            return true;
        }

        /// <summary>list 안에 x와 y를 모두 포함하는 활성 삼각형이 있으면 true.</summary>
        private bool HasTriangleWith(List<int> list, int x, int y)
        {
            foreach (int t in list)
            {
                if (!removedTriangles[t] && TriangleContains(t, x) && TriangleContains(t, y)) return true;
            }

            return false;
        }

        /// <summary>v를 target으로 옮겼을 때 (other를 포함하지 않는) 주변 삼각형이 뒤집히거나 퇴화하면 true.</summary>
        private bool FlipsOrDegenerates(List<int> list, int v, int other, Vector3 target)
        {
            foreach (int t in list)
            {
                if (removedTriangles[t] || TriangleContains(t, other)) continue;

                int tb = t * 3;
                Vector3 p0 = positions[tris[tb]];
                Vector3 p1 = positions[tris[tb + 1]];
                Vector3 p2 = positions[tris[tb + 2]];
                Vector3 before = Vector3.Cross(p1 - p0, p2 - p0);

                if (tris[tb] == v) p0 = target;
                else if (tris[tb + 1] == v) p1 = target;
                else p2 = target;

                Vector3 after = Vector3.Cross(p1 - p0, p2 - p0);
                float afterSq = after.sqrMagnitude;
                float beforeSq = before.sqrMagnitude;
                if (afterSq <= 1e-12f * Mathf.Max(beforeSq, 1e-30f)) return true;
                if (beforeSq <= 0f) continue;
                if (Vector3.Dot(before, after) < MinNormalDot * Mathf.Sqrt(beforeSq * afterSq)) return true;
            }

            return false;
        }

        private void InvalidateCompaction()
        {
            compactOrder = null;
            compactMap = null;
        }

        private void EnsureCompaction()
        {
            if (compactMap != null) return;

            compactMap = new int[positions.Length];
            for (int i = 0; i < compactMap.Length; i++) compactMap[i] = -1;

            var order = new List<int>();
            for (int t = 0; t < removedTriangles.Length; t++)
            {
                if (removedTriangles[t]) continue;
                int tb = t * 3;
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[tb + k];
                    if (compactMap[v] >= 0) continue;
                    compactMap[v] = order.Count;
                    order.Add(v);
                }
            }

            compactOrder = order.ToArray();
        }

        public Vector3[] GetVertices()
        {
            EnsureCompaction();
            var result = new Vector3[compactOrder.Length];
            for (int i = 0; i < result.Length; i++) result[i] = positions[compactOrder[i]];
            return result;
        }

        public int[] GetTriangles()
        {
            EnsureCompaction();
            var result = new int[activeTriangleCount * 3];
            int w = 0;
            for (int t = 0; t < removedTriangles.Length; t++)
            {
                if (removedTriangles[t]) continue;
                int tb = t * 3;
                result[w++] = compactMap[tris[tb]];
                result[w++] = compactMap[tris[tb + 1]];
                result[w++] = compactMap[tris[tb + 2]];
            }

            return result;
        }

        public Vector3[] GetNormals()
        {
            if (normals == null) return null;

            EnsureCompaction();
            var result = new Vector3[compactOrder.Length];
            for (int i = 0; i < result.Length; i++) result[i] = normals[compactOrder[i]];
            return result;
        }

        public Vector2[] GetUVs()
        {
            if (uvs == null) return null;

            EnsureCompaction();
            var result = new Vector2[compactOrder.Length];
            for (int i = 0; i < result.Length; i++) result[i] = uvs[compactOrder[i]];
            return result;
        }

        // ─── Welding ───

        private struct CellKey : IEquatable<CellKey>
        {
            public long X, Y, Z;

            public bool Equals(CellKey other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is CellKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    long h = X * 73856093L ^ Y * 19349663L ^ Z * 83492791L;
                    return (int)h ^ (int)(h >> 32);
                }
            }
        }

        private struct TriKey : IEquatable<TriKey>
        {
            public int A, B, C;

            /// <summary>감김 순서를 유지한 채 가장 작은 인덱스가 앞에 오도록 회전한다(같은 방향 중복만 같은 키).</summary>
            public static TriKey Canonical(int a, int b, int c)
            {
                if (a < b && a < c) return new TriKey { A = a, B = b, C = c };
                if (b < c) return new TriKey { A = b, B = c, C = a };
                return new TriKey { A = c, B = a, C = b };
            }

            public bool Equals(TriKey other) => A == other.A && B == other.B && C == other.C;
            public override bool Equals(object obj) => obj is TriKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (A * 73856093) ^ (B * 19349663) ^ (C * 83492791);
                }
            }
        }

        /// <summary>
        /// 거리 epsilon 이내의 정점을 하나로 합친다(UV·노말 이음새로 쪼개진 정점 용접).
        /// 반환값은 원래 정점 → 합쳐진 정점 인덱스 매핑이며, 용접 후 퇴화한 삼각형은 제거된다.
        /// </summary>
        public static int[] WeldByPosition(Vector3[] vertices, int[] triangles, float epsilon,
            out Vector3[] weldedVertices, out int[] weldedTriangles)
        {
            return WeldByPosition(vertices, null, triangles, epsilon, -1f, out weldedVertices, out weldedTriangles);
        }

        /// <summary>
        /// 거리 epsilon 이내이면서 노말이 minNormalDot(cos) 이상으로 같은 쪽을 향하는 정점만 합친다.
        /// 양면 카드(뒷면이 뒤집힌 복제 정점)처럼 같은 위치에서 반대를 향하는 표면은 따로 남겨야
        /// 엣지 하나를 삼각형 4개가 공유하는 비다양체가 되지 않아 단순화가 멈추지 않는다.
        /// 용접 후 같은 감김 방향으로 완전히 겹치는 중복 삼각형(같은 위치의 옷·몸 레이어 등)은 하나만 남긴다.
        /// normals가 null이면 위치만 본다.
        /// </summary>
        public static int[] WeldByPosition(Vector3[] vertices, Vector3[] normals, int[] triangles, float epsilon,
            float minNormalDot, out Vector3[] weldedVertices, out int[] weldedTriangles)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            if (normals != null && normals.Length != vertices.Length) normals = null;

            double cellSize = Math.Max(epsilon, 1e-9);
            float epsilonSq = epsilon * epsilon;

            int[] remap = new int[vertices.Length];
            var representatives = new List<Vector3>(vertices.Length);
            var representativeNormals = normals != null ? new List<Vector3>(vertices.Length) : null;
            var chainNext = new List<int>(vertices.Length);
            var cells = new Dictionary<CellKey, int>(vertices.Length);

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                Vector3 n = normals != null ? normals[i].normalized : Vector3.zero;
                long cx = (long)Math.Floor(p.x / cellSize);
                long cy = (long)Math.Floor(p.y / cellSize);
                long cz = (long)Math.Floor(p.z / cellSize);

                int found = -1;
                for (int dx = -1; dx <= 1 && found < 0; dx++)
                for (int dy = -1; dy <= 1 && found < 0; dy++)
                for (int dz = -1; dz <= 1 && found < 0; dz++)
                {
                    if (!cells.TryGetValue(new CellKey { X = cx + dx, Y = cy + dy, Z = cz + dz }, out int r)) continue;
                    for (; r >= 0; r = chainNext[r])
                    {
                        if ((representatives[r] - p).sqrMagnitude > epsilonSq) continue;

                        if (representativeNormals != null)
                        {
                            // 노말이 없는(0) 정점은 어느 쪽과도 합친다.
                            Vector3 rn = representativeNormals[r];
                            bool hasBoth = rn.sqrMagnitude > 0.5f && n.sqrMagnitude > 0.5f;
                            if (hasBoth && Vector3.Dot(rn, n) < minNormalDot) continue;
                            if (!hasBoth && n.sqrMagnitude > 0.5f) representativeNormals[r] = n;
                        }

                        found = r;
                        break;
                    }
                }

                if (found < 0)
                {
                    found = representatives.Count;
                    representatives.Add(p);
                    representativeNormals?.Add(n);
                    var key = new CellKey { X = cx, Y = cy, Z = cz };
                    chainNext.Add(cells.TryGetValue(key, out int head) ? head : -1);
                    cells[key] = found;
                }

                remap[i] = found;
            }

            var resultTris = new List<int>(triangles.Length);
            var seenTris = new HashSet<TriKey>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int i0 = triangles[t], i1 = triangles[t + 1], i2 = triangles[t + 2];
                if ((uint)i0 >= (uint)remap.Length || (uint)i1 >= (uint)remap.Length || (uint)i2 >= (uint)remap.Length) continue;

                int a = remap[i0], b = remap[i1], c = remap[i2];
                if (a == b || b == c || c == a) continue;

                // 같은 방향으로 완전히 겹치는 삼각형은 콜라이더 형태에 기여하지 않고 비다양체만 만든다.
                // 반대 방향 중복(정점을 공유하는 양면)은 뒷면 충돌이 사라지지 않도록 그대로 둔다.
                if (!seenTris.Add(TriKey.Canonical(a, b, c))) continue;

                resultTris.Add(a);
                resultTris.Add(b);
                resultTris.Add(c);
            }

            weldedVertices = representatives.ToArray();
            weldedTriangles = resultTris.ToArray();
            return remap;
        }
    }
}
