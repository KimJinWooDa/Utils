#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#if TELLER_URP
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#endif

namespace TelleR
{
    [CustomEditor(typeof(DeviceManager))]
    public class DeviceManagerEditor : Editor
    {
        SerializedProperty quest2Prop;
        SerializedProperty quest3Prop;
        SerializedProperty fallbackProp;

        bool foldQuest2 = true, foldQuest3 = true, foldFallback = true;

        void OnEnable()
        {
            quest2Prop = serializedObject.FindProperty("quest2");
            quest3Prop = serializedObject.FindProperty("quest3");
            fallbackProp = serializedObject.FindProperty("fallback");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Device Manager", TelleRGUI.Header);
            EditorGUILayout.HelpBox(
                "Quest 기기별 렌더링 설정을 관리합니다.\n" +
                "Android 플레이어 실행 시 기기 모델명으로 Quest 2 / 3 시리즈를 감지해 해당 프로필의 MSAA를 URP 에셋에 적용합니다.",
                MessageType.Info);
            DrawEnvironmentStatus();
            EditorGUILayout.Space(4);

            DrawDeviceProfile("Quest 2", quest2Prop, ref foldQuest2,
                "Quest 2는 GPU 성능이 제한적이므로 MSAA를 끄거나 낮게 설정하는 것을 권장합니다.");
            DrawDeviceProfile("Quest 3 / 3S", quest3Prop, ref foldQuest3,
                "Quest 3 시리즈는 성능 여유가 있어 2x 이상의 MSAA를 사용할 수 있습니다.");
            DrawDeviceProfile("Other Devices (Fallback)", fallbackProp, ref foldFallback,
                "Quest 2/3으로 감지되지 않는 기기에 적용되는 기본 프로필입니다.");

            serializedObject.ApplyModifiedProperties();
        }

        // 이 환경에서 컴포넌트가 실제로 동작하지 않는 이유를 알려준다.
        static void DrawEnvironmentStatus()
        {
#if !TELLER_URP
            EditorGUILayout.HelpBox(
                "URP 패키지(com.unity.render-pipelines.universal)가 설치되지 않아 이 프로젝트에서는 MSAA 프로필이 적용되지 않습니다.",
                MessageType.Warning);
#else
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                EditorGUILayout.HelpBox(
                    "현재 활성 렌더 파이프라인이 URP 에셋이 아니라 MSAA 프로필이 적용되지 않습니다.\n" +
                    "Project Settings > Graphics(또는 Quality)에서 URP 에셋을 지정하세요.",
                    MessageType.Warning);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorGUILayout.HelpBox("Android 플레이어에서만 동작합니다. 에디터와 다른 플랫폼에서는 아무 설정도 바꾸지 않습니다.", MessageType.None);
#endif
        }

        void DrawDeviceProfile(string label, SerializedProperty prop, ref bool foldout, string description)
        {
            foldout = EditorGUILayout.BeginFoldoutHeaderGroup(foldout, label);
            if (foldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(description, MessageType.None);

                var msaaProp = prop.FindPropertyRelative("msaa");
                EditorGUILayout.PropertyField(msaaProp, new GUIContent("MSAA",
                    "Multi-Sample Anti-Aliasing\n" +
                    "Disabled (1x) = 안티앨리어싱 없음, 최고 성능\n" +
                    "2x = 가벼운 안티앨리어싱\n" +
                    "4x = 중간 수준 안티앨리어싱\n" +
                    "8x = 최고 품질, 성능 부담 큼"));

                EditorGUI.indentLevel--;
                EditorGUILayout.Space(2);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }
    }
}
#endif
