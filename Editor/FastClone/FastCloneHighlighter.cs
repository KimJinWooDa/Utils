using UnityEditor;
using UnityEngine;

namespace TelleR.Util.FastClone
{
    [InitializeOnLoad]
    public class FastCloneHighlighter
    {
        private static GUIStyle boxStyle;
        private static bool boxStyleForPro;

        static FastCloneHighlighter()
        {
            if (FastCloneCore.IsClone())
            {
                EditorApplication.delayCall += () =>
                {
                    SceneView.duringSceneGui += OnSceneGUI;
                };
            }
        }

        // SceneGUI마다 스타일을 만들지 않도록 캐시한다. 스킨이 바뀌면 글자색만 다시 맞춘다.
        private static GUIStyle BoxStyle
        {
            get
            {
                if (boxStyle == null || boxStyleForPro != EditorGUIUtility.isProSkin)
                {
                    boxStyleForPro = EditorGUIUtility.isProSkin;
                    boxStyle = new GUIStyle("HelpBox")
                    {
                        fontSize = 12,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };
                    boxStyle.normal.textColor = TelleRGUI.Warning;
                }
                return boxStyle;
            }
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (Event.current.type != EventType.Repaint) return;

            Handles.BeginGUI();
            var rect = new Rect(10, 10, 200, 30);
            GUI.Box(rect, "Running as CLONE Mode", BoxStyle);
            Handles.EndGUI();
        }
    }
}
