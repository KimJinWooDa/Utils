#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace TelleR
{
    // 패키지에 포함된 모든 도구의 목록·설명·진입점을 한곳에 모은 허브.
    // 컴포넌트/컨텍스트 메뉴로만 접근되는 도구들의 발견성을 위해 존재한다.
    public class ToolHubWindow : EditorWindow
    {
        private Vector2 scroll;

        private GUIStyle titleStyle;
        private GUIStyle nameStyle;
        private GUIStyle descStyle;
        private GUIStyle howToStyle;

        [MenuItem("Tools/TelleR/Tool Hub", false, 0)]
        private static void Open()
        {
            var win = GetWindow<ToolHubWindow>(false, "TelleR Tool Hub");
            win.minSize = new Vector2(460, 520);
        }

        private void OnSelectionChange() => Repaint();

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            nameStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            descStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            howToStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, fontStyle = FontStyle.Italic };
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("TelleR Utilities — 도구 모음", titleStyle);
            EditorGUILayout.LabelField("이 패키지의 모든 도구와 여는 방법입니다. 필요한 것만 골라 쓰세요.",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true });
            EditorGUILayout.Space(4);

            scroll = EditorGUILayout.BeginScrollView(scroll);

            Section("창 도구 — Tools 메뉴에서 열기");
            WindowTool("Auto Sprite Slicer",
                "이미지 배경 제거·트림 후 Sprite(Single)로 일괄 변환합니다.",
                "Tools/TelleR/Tool/Auto Sprite Slicer");
            WindowTool("UI Atlas Builder",
                "캔버스가 쓰는 스프라이트를 모아 SpriteAtlas로 묶어 UI 드로우콜을 줄입니다.",
                "Tools/TelleR/Tool/AtlasBuilder");
            WindowTool("Skinned Mesh Collider Creator",
                "SkinnedMeshRenderer의 현재 포즈로 MeshCollider를 만들고 폴리곤을 간소화합니다.",
                "Tools/TelleR/Tool/Skinned Mesh Collider Creator");
            WindowTool("Clones Manager (Fast Clone)",
                "멀티플레이어 테스트용 프로젝트 클론을 만들고 관리합니다 (Library만 복사, 나머지는 공유).",
                "Tools/TelleR/Tool/Clones Manager");
            WindowTool("UPM Package Creator",
                "내 코드를 UPM 패키지로 만들어 git URL로 배포합니다 (asmdef·meta 자동 생성).",
                "Tools/TelleR/Tool/UPM Package Creator");

            Section("컴포넌트 도구 — 오브젝트에 Add Component");
            ComponentTool<AnimationInspectorController>("Animation Inspector Controller",
                "Play 없이 애니메이션을 미리보고, 프레임 이벤트와 자동 전환을 설정합니다.");
            ComponentTool<AudioVolume3D>("Audio Volume 3D",
                "3D 사운드 볼륨 영역을 시각화하고 페이드·차폐(Occlusion)를 구성합니다.");
            ComponentTool<TrailEffect>("Trail Effect",
                "GPU Instancing 기반의 잔상·텍스처 스탬프 트레일 이펙트입니다.");
            ComponentTool<DeviceManager>("Device Manager (Meta)",
                "Quest 기종을 감지해 URP MSAA 프로필을 자동 적용합니다.");
            ComponentTool<FoveationStarter>("Foveation Starter (Meta)",
                "실행 시 포비티드 렌더링을 자동 활성화해 Quest 성능을 확보합니다.");
            EditorGUILayout.LabelField("버튼은 Hierarchy에서 오브젝트를 선택하면 활성화됩니다.", howToStyle);

            Section("컨텍스트 도구 — 해당 컴포넌트에서 자동 표시");
            InfoTool("Mesh Pivot Tool",
                "메쉬 피벗 위치·회전을 수정합니다 (프리셋, 버텍스 스냅, Undo 지원).",
                "여는 법: MeshFilter 또는 SkinnedMeshRenderer 우클릭 → Edit Mesh Pivot");
            InfoTool("FBX Backup",
                "수정한 메시를 FBX로 백업해 리임포트 시 유실을 방지합니다.",
                "여는 법: MeshFilter / SkinnedMeshRenderer 인스펙터에 버튼이 자동 표시됩니다");

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("GitHub 문서 열기 (README)", GUILayout.Height(26)))
                Application.OpenURL("https://github.com/KimJinWooDa/Utils#readme");
            EditorGUILayout.Space(6);
        }

        private void Section(string label)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            var rect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
            EditorGUILayout.Space(4);
        }

        private void WindowTool(string toolName, string desc, string menuPath)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(toolName, nameStyle);
                    EditorGUILayout.LabelField(desc, descStyle);
                }
                if (GUILayout.Button("열기", GUILayout.Width(64), GUILayout.Height(30)))
                    EditorApplication.ExecuteMenuItem(menuPath);
            }
        }

        private void ComponentTool<T>(string toolName, string desc) where T : Component
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(toolName, nameStyle);
                    EditorGUILayout.LabelField(desc, descStyle);
                }

                GameObject go = Selection.activeGameObject;
                using (new EditorGUI.DisabledScope(go == null))
                {
                    if (GUILayout.Button("선택에 추가", GUILayout.Width(84), GUILayout.Height(30)))
                    {
                        if (go.TryGetComponent<T>(out _))
                            EditorUtility.DisplayDialog("Tool Hub", $"'{go.name}'에 이미 {toolName} 컴포넌트가 있습니다.", "OK");
                        else
                        {
                            Undo.AddComponent<T>(go);
                            EditorGUIUtility.PingObject(go);
                        }
                    }
                }
            }
        }

        private void InfoTool(string toolName, string desc, string howTo)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(toolName, nameStyle);
                EditorGUILayout.LabelField(desc, descStyle);
                EditorGUILayout.LabelField(howTo, howToStyle);
            }
        }
    }
}
#endif
