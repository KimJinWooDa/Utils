#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace TelleR
{
    [CustomEditor(typeof(FoveationStarter))]
    public class FoveationStarterEditor : Editor
    {
        SerializedProperty levelProp;

        static readonly string[] levelLabels = { "None (0.0)", "Low (0.33)", "Medium (0.67)", "High (1.0)" };
        static readonly float[] levelValues = { 0f, 0.33f, 0.67f, 1.0f };

        void OnEnable()
        {
            levelProp = serializedObject.FindProperty("foveatedRenderingLevel");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Foveation Starter", EditorStyles.boldLabel);
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
                if (isActive) GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);

                if (GUILayout.Button(levelLabels[i], GUILayout.Height(24)))
                    levelProp.floatValue = levelValues[i];

                GUI.backgroundColor = prevColor;
            }
            EditorGUILayout.EndHorizontal();

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
