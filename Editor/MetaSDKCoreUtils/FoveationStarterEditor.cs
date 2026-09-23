#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace TelleR
{
    [CustomEditor(typeof(FoveationStarter))]
    public class FoveationStarterEditor : Editor
    {
        SerializedProperty levelProp;
        SerializedProperty timeoutProp;

        static readonly string[] levelLabels = { "None (0.0)", "Low (0.33)", "Medium (0.67)", "High (1.0)" };
        static readonly float[] levelValues = { 0f, 0.33f, 0.67f, 1.0f };

        void OnEnable()
        {
            levelProp = serializedObject.FindProperty("foveatedRenderingLevel");
            timeoutProp = serializedObject.FindProperty("xrWaitTimeout");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Foveation Starter", TelleRGUI.Header);
#if !TELLER_XR
            EditorGUILayout.HelpBox(
                "XR 모듈(com.unity.modules.xr)이 없는 프로젝트라 Foveated Rendering이 적용되지 않습니다.\n" +
                "Package Manager > Built-in에서 XR 모듈을 켜거나 XR Plug-in Management를 설치하세요.",
                MessageType.Warning);
#elif !UNITY_2022_2_OR_NEWER
            EditorGUILayout.HelpBox(
                "Foveated Rendering API(XRDisplaySubsystem.foveatedRenderingLevel)는 Unity 2022.2 이상에서만 제공되어 이 버전에서는 적용되지 않습니다.",
                MessageType.Warning);
#endif
            EditorGUILayout.Space(4);

            // Slider
            EditorGUILayout.PropertyField(levelProp, new GUIContent("Foveation Level"));
            EditorGUILayout.Space(2);

            // Preset buttons
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < levelLabels.Length; i++)
            {
                bool isActive = Mathf.Approximately(levelProp.floatValue, levelValues[i]);
                var prevColor = GUI.backgroundColor;
                if (isActive) GUI.backgroundColor = TelleRGUI.AccentButton;

                if (GUILayout.Button(levelLabels[i], GUILayout.Height(24)))
                    levelProp.floatValue = levelValues[i];

                GUI.backgroundColor = prevColor;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            if (timeoutProp != null)
                EditorGUILayout.PropertyField(timeoutProp, new GUIContent("XR Wait Timeout (s)",
                    "XR 디스플레이가 늦게 초기화되는 경우를 위해 이 시간(초) 동안 재시도합니다."));

            EditorGUILayout.Space(6);

            // Level description
            string description = GetLevelDescription(levelProp.floatValue);
            EditorGUILayout.HelpBox(description, MessageType.Info);

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Foveated Rendering은 시선 중심부는 고해상도로,\n" +
                "주변부는 저해상도로 렌더링하여 GPU 부하를 줄이는 기술입니다.\n" +
                "레벨이 높을수록 성능은 좋아지지만 주변부 화질이 저하됩니다.",
                MessageType.None);

            serializedObject.ApplyModifiedProperties();
        }

        static string GetLevelDescription(float level)
        {
            if (level < 0.1f)
                return "None — Foveation 비활성. 전체 화면을 동일한 해상도로 렌더링합니다.\n성능 절약 없음, 최고 화질.";
            if (level < 0.5f)
                return "Low — 주변부 해상도를 약간 낮춥니다.\n화질 저하가 거의 느껴지지 않으면서 소폭의 성능 향상을 얻습니다.";
            if (level < 0.85f)
                return "Medium — 주변부 해상도를 중간 수준으로 감소시킵니다.\n화질과 성능의 균형 잡힌 설정입니다.";
            return "High — 주변부 해상도를 최대한 감소시킵니다.\n최고 성능이지만 주변부에서 화질 저하가 눈에 띌 수 있습니다.";
        }
    }
}
#endif
