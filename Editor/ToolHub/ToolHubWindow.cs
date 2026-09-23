#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace TelleR
{
    // 패키지에 포함된 모든 도구의 목록·설명·진입점을 한곳에 모은 허브.
    // 컴포넌트/컨텍스트 메뉴로만 접근되는 도구들의 발견성을 위해 존재한다.
    public class ToolHubWindow : EditorWindow
    {
        private const string LogPrefix = "[TelleR/ToolHub] ";

        private enum ToolKind { Window, Component, Context }

        private sealed class ToolEntry
        {
            public string Category;
            public string Name;
            public string Description;
            public ToolKind Kind;
            public string MenuPath;      // Window
            public Type ComponentType;   // Component
            public string HowTo;         // 추가 안내(선택)
            public string Requirement;   // 필요한 선택 패키지(선택)
            public bool RequirementMissing;
            public string SearchText;

            // 스킨별 색이 들어간 요구 사항 문구 캐시
            public string RequirementRich;
            public bool RequirementRichForPro;
        }

        // 선택 패키지 설치 여부(asmdef versionDefines). const 대신 readonly로 두어 도달 불가 코드 경고를 피한다.
#if TELLER_UGUI
        private static readonly bool HasUGUI = true;
#else
        private static readonly bool HasUGUI = false;
#endif
#if TELLER_URP
        private static readonly bool HasURP = true;
#else
        private static readonly bool HasURP = false;
#endif
#if TELLER_FBX
        private static readonly bool HasFBX = true;
#else
        private static readonly bool HasFBX = false;
#endif
#if TELLER_XR && UNITY_2022_2_OR_NEWER
        private static readonly bool HasFoveationSupport = true;
#else
        private static readonly bool HasFoveationSupport = false;
#endif

        private static ToolEntry[] entries;

        private Vector2 scroll;
        [SerializeField] private string search = "";
        [NonSerialized] private SearchField searchField;
        [NonSerialized] private string filteredFor;
        [NonSerialized] private readonly List<ToolEntry> filtered = new List<ToolEntry>();
        [NonSerialized] private readonly List<GameObject> selectedTargets = new List<GameObject>();

        private static readonly GUIContent OpenContent = new GUIContent("Open", "이 도구의 창을 엽니다.");
        private static readonly GUIContent AddContent = new GUIContent("Add to Selection", "선택한 오브젝트에 컴포넌트를 추가합니다 (Undo 가능).");
        private static readonly GUIContent AddDisabledContent = new GUIContent("Add to Selection", "Hierarchy에서 오브젝트를 먼저 선택하세요.");

        [MenuItem("Tools/TelleR/Tool Hub", false, 0)]
        private static void Open()
        {
            var win = GetWindow<ToolHubWindow>(false, "TelleR Tool Hub");
            win.minSize = new Vector2(460, 520);
        }

        private void OnEnable()
        {
            RefreshSelection();
        }

        private void OnSelectionChange()
        {
            RefreshSelection();
            Repaint();
        }

        // Selection.gameObjects는 호출마다 배열을 만들므로 선택이 바뀔 때만 읽는다.
        private void RefreshSelection()
        {
            selectedTargets.Clear();
            foreach (GameObject go in Selection.gameObjects)
            {
                // 프로젝트 창의 프리팹 에셋은 제외하고 씬/프리팹 모드의 오브젝트만 대상으로 한다.
                if (go != null && !EditorUtility.IsPersistent(go)) selectedTargets.Add(go);
            }
        }

        private static ToolEntry[] Entries => entries ?? (entries = BuildEntries());

        private static ToolEntry[] BuildEntries()
        {
            const string mesh = "Mesh & Collider";
            const string ui = "2D & UI";
            const string comp = "Scene Components";
            const string project = "Project & Workflow";
            const string xr = "XR (Meta Quest)";

            var list = new List<ToolEntry>
            {
                WindowEntry(mesh, "Concave Mesh Collider",
                    "오목한(Concave) 메시를 볼록 조각 여러 개로 나눠 모양에 맞는 복합 콜라이더를 만듭니다.",
                    "Tools/TelleR/Concave Mesh Collider",
                    "MeshFilter / SkinnedMeshRenderer 컴포넌트 우클릭 → Generate Concave Collider로 바로 생성할 수도 있습니다."),
                WindowEntry(mesh, "Skinned Mesh Collider",
                    "SkinnedMeshRenderer의 현재 포즈로 MeshCollider를 만들고 폴리곤을 간소화합니다.",
                    "Tools/TelleR/Skinned Mesh Collider"),
                ContextEntry(mesh, "Mesh Pivot Tool",
                    "메쉬 피벗 위치·회전을 수정합니다 (프리셋, 버텍스 스냅, Undo 지원).",
                    "여는 법: MeshFilter 또는 SkinnedMeshRenderer 우클릭 → Edit Mesh Pivot"),
                ContextEntry(mesh, "FBX Backup",
                    "수정한 메시를 FBX로 백업해 리임포트 시 유실을 방지합니다.",
                    "여는 법: MeshFilter / SkinnedMeshRenderer 인스펙터에 버튼이 자동 표시됩니다.",
                    "FBX Exporter (com.unity.formats.fbx)", !HasFBX),

                WindowEntry(ui, "Auto Sprite Slicer",
                    "이미지 배경 제거·트림 후 Sprite(Single)로 일괄 변환합니다.",
                    "Tools/TelleR/Auto Sprite Slicer"),
                WindowEntry(ui, "UI Atlas Builder",
                    "캔버스가 쓰는 스프라이트를 모아 SpriteAtlas로 묶어 UI 드로우콜을 줄입니다.",
                    "Tools/TelleR/UI Atlas Builder", null,
                    "Unity UI (com.unity.ugui)", !HasUGUI),

                ComponentEntry(comp, "Audio Volume 3D", typeof(AudioVolume3D),
                    "3D 사운드 볼륨 영역을 시각화하고 페이드·차폐(Occlusion)를 구성합니다."),
                ComponentEntry(comp, "Animation Inspector Controller", typeof(AnimationInspectorController),
                    "Play 없이 애니메이션을 미리보고, 프레임 이벤트와 자동 전환을 설정합니다.",
                    "Animator가 없으면 함께 추가됩니다."),
                ComponentEntry(comp, "Trail Effect", typeof(TrailEffect),
                    "GPU Instancing 기반의 잔상·텍스처 스탬프 트레일 이펙트입니다."),

                WindowEntry(project, "Fast Clone",
                    "멀티플레이어 테스트용 프로젝트 클론을 만들고 관리합니다 (Library만 복사, 나머지는 원본과 공유).",
                    "Tools/TelleR/Fast Clone",
                    "클론에서 수정한 에셋·설정은 원본에도 바로 반영됩니다."),
                WindowEntry(project, "UPM Package Creator",
                    "내 코드를 UPM 패키지로 만들어 git URL로 배포합니다 (asmdef·meta 자동 생성).",
                    "Tools/TelleR/UPM Package Creator"),

                ComponentEntry(xr, "Device Manager", typeof(DeviceManager),
                    "Quest 기종을 감지해 URP MSAA 프로필을 자동 적용합니다.",
                    null, "URP (com.unity.render-pipelines.universal)", !HasURP),
                ComponentEntry(xr, "Foveation Starter", typeof(FoveationStarter),
                    "실행 시 포비티드 렌더링을 자동 활성화해 Quest 성능을 확보합니다.",
                    null, "XR 모듈 (com.unity.modules.xr) + Unity 2022.2 이상", !HasFoveationSupport),
            };

            foreach (var e in list)
            {
                e.SearchText = string.Join("\n", e.Category, e.Name, e.Description, e.MenuPath ?? "",
                    e.HowTo ?? "", e.Requirement ?? "", e.ComponentType != null ? e.ComponentType.Name : "").ToLowerInvariant();
            }
            return list.ToArray();
        }

        private static ToolEntry WindowEntry(string category, string name, string desc, string menuPath,
            string howTo = null, string requirement = null, bool missing = false)
            => new ToolEntry
            {
                Category = category, Name = name, Description = desc, Kind = ToolKind.Window, MenuPath = menuPath,
                HowTo = howTo, Requirement = requirement, RequirementMissing = missing
            };

        private static ToolEntry ComponentEntry(string category, string name, Type type, string desc,
            string howTo = null, string requirement = null, bool missing = false)
            => new ToolEntry
            {
                Category = category, Name = name, Description = desc, Kind = ToolKind.Component, ComponentType = type,
                HowTo = howTo, Requirement = requirement, RequirementMissing = missing
            };

        private static ToolEntry ContextEntry(string category, string name, string desc, string howTo,
            string requirement = null, bool missing = false)
            => new ToolEntry
            {
                Category = category, Name = name, Description = desc, Kind = ToolKind.Context,
                HowTo = howTo, Requirement = requirement, RequirementMissing = missing
            };

        // 검색어가 바뀔 때만 목록을 다시 거른다.
        private void UpdateFilter()
        {
            string key = (search ?? "").Trim().ToLowerInvariant();
            if (filteredFor == key) return;
            filteredFor = key;
            filtered.Clear();
            foreach (var e in Entries)
                if (key.Length == 0 || e.SearchText.Contains(key)) filtered.Add(e);
        }

        private void OnGUI()
        {
            if (searchField == null) searchField = new SearchField();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("TelleR Utilities", TelleRGUI.Header);
            EditorGUILayout.LabelField("이 패키지의 모든 도구와 여는 방법입니다. 필요한 것만 골라 쓰세요.", TelleRGUI.Hint);
            EditorGUILayout.Space(4);

            Rect searchRect = GUILayoutUtility.GetRect(1f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            search = searchField.OnGUI(searchRect, search);
            UpdateFilter();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            if (filtered.Count == 0)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.LabelField("검색 결과가 없습니다.", TelleRGUI.HintCentered);
            }

            string currentCategory = null;
            foreach (var entry in filtered)
            {
                if (entry.Category != currentCategory)
                {
                    currentCategory = entry.Category;
                    EditorGUILayout.Space(6);
                    TelleRGUI.Section(currentCategory);
                    EditorGUILayout.Space(2);
                }
                DrawEntry(entry);
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(selectedTargets.Count > 0
                    ? $"컴포넌트 추가 대상: 선택한 오브젝트 {selectedTargets.Count}개"
                    : "Add to Selection 버튼은 Hierarchy에서 오브젝트를 선택하면 활성화됩니다.",
                TelleRGUI.Hint);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("Open README (GitHub)", GUILayout.Height(26)))
                Application.OpenURL("https://github.com/KimJinWooDa/Utils#readme");
            EditorGUILayout.Space(6);
        }

        private void DrawEntry(ToolEntry entry)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(entry.Name, TelleRGUI.SubHeader);
                    EditorGUILayout.LabelField(entry.Description, TelleRGUI.WrappedLabel);
                    if (!string.IsNullOrEmpty(entry.HowTo))
                        EditorGUILayout.LabelField(entry.HowTo, TelleRGUI.Hint);
                    if (!string.IsNullOrEmpty(entry.Requirement))
                        EditorGUILayout.LabelField(GetRequirementText(entry), TelleRGUI.Hint);
                }

                switch (entry.Kind)
                {
                    case ToolKind.Window:
                        if (GUILayout.Button(OpenContent, GUILayout.Width(120), GUILayout.Height(30)))
                        {
                            // 다른 창을 여는 동작은 현재 OnGUI 레이아웃이 끝난 뒤 실행한다.
                            EditorApplication.delayCall += () => OpenWindowTool(entry);
                        }
                        break;
                    case ToolKind.Component:
                        using (new EditorGUI.DisabledScope(selectedTargets.Count == 0))
                        {
                            if (GUILayout.Button(selectedTargets.Count == 0 ? AddDisabledContent : AddContent,
                                    GUILayout.Width(120), GUILayout.Height(30)))
                                AddComponentToSelection(entry);
                        }
                        break;
                }
            }
        }

        private static string GetRequirementText(ToolEntry entry)
        {
            bool pro = TelleRGUI.IsPro;
            if (entry.RequirementRich == null || entry.RequirementRichForPro != pro)
            {
                entry.RequirementRichForPro = pro;
                entry.RequirementRich = entry.RequirementMissing
                    ? $"<color=#{ColorUtility.ToHtmlStringRGB(TelleRGUI.Warning)}>필요: {entry.Requirement} — 설치되지 않아 이 도구가 동작하지 않거나 기능이 제한됩니다.</color>"
                    : $"필요: {entry.Requirement} (설치됨)";
            }
            return entry.RequirementRich;
        }

        private static void OpenWindowTool(ToolEntry entry)
        {
            // 없는 메뉴를 ExecuteMenuItem에 넘기면 Unity가 스택 트레이스와 함께 오류를 찍으므로 먼저 확인한다.
            if (Menu.GetEnabled(entry.MenuPath) && EditorApplication.ExecuteMenuItem(entry.MenuPath)) return;

            Debug.LogWarning(LogPrefix + $"'{entry.Name}' 메뉴를 찾지 못했습니다: {entry.MenuPath}");
            string reason = entry.RequirementMissing
                ? $"필요한 패키지가 설치되지 않았을 수 있습니다: {entry.Requirement}\n\n"
                : "";
            TelleRGUI.Info("Tool Hub",
                $"'{entry.Name}' 창을 열지 못했습니다.\n메뉴를 찾을 수 없습니다: {entry.MenuPath}\n\n" + reason +
                "패키지를 최신 버전으로 업데이트했는지, 콘솔에 컴파일 오류가 없는지 확인하세요.");
        }

        private void AddComponentToSelection(ToolEntry entry)
        {
            int added = 0, skipped = 0, failed = 0;
            GameObject first = null;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Add {entry.Name}");

            foreach (GameObject go in selectedTargets)
            {
                if (go == null) continue;
                if (go.GetComponent(entry.ComponentType) != null)
                {
                    skipped++;
                    continue;
                }
                if (Undo.AddComponent(go, entry.ComponentType) != null)
                {
                    added++;
                    if (first == null) first = go;
                }
                else failed++;
            }
            Undo.CollapseUndoOperations(group);

            if (first != null) EditorGUIUtility.PingObject(first);

            if (added == 0 && failed == 0 && skipped > 0)
            {
                TelleRGUI.Info("Tool Hub", skipped == 1
                    ? $"선택한 오브젝트에 이미 {entry.Name} 컴포넌트가 있습니다."
                    : $"선택한 오브젝트 {skipped}개 모두 이미 {entry.Name} 컴포넌트가 있습니다.");
                return;
            }
            if (failed > 0)
            {
                TelleRGUI.Info("Tool Hub",
                    $"{failed}개 오브젝트에는 {entry.Name}을(를) 추가하지 못했습니다.\n자세한 원인은 콘솔을 확인하세요.");
            }
            Debug.Log(LogPrefix + $"{entry.Name} 추가: {added}개" + (skipped > 0 ? $", 이미 있음: {skipped}개" : ""));
        }
    }
}
#endif
