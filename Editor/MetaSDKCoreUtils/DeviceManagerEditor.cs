#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

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

            EditorGUILayout.LabelField("Device Manager", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Quest 기기별 렌더링 설정을 관리합니다.\n" +
                "실행 시 기기를 자동 감지하여 해당 프로필을 적용합니다.",
                MessageType.Info);
            EditorGUILayout.Space(4);

            DrawDeviceProfile("Quest 2", quest2Prop, ref foldQuest2,
                "Quest 2는 GPU 성능이 제한적이므로 MSAA를 끄거나 낮게 설정하는 것을 권장합니다.");
            DrawDeviceProfile("Quest 3 / 3S", quest3Prop, ref foldQuest3,
                "Quest 3 시리즈는 성능 여유가 있어 2x 이상의 MSAA를 사용할 수 있습니다.");
            DrawDeviceProfile("기타 기기 (Fallback)", fallbackProp, ref foldFallback,
                "Quest 2/3으로 감지되지 않는 기기에 적용되는 기본 프로필입니다.");

            serializedObject.ApplyModifiedProperties();
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
