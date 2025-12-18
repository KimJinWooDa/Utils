using UnityEditor;
using UnityEngine;

namespace TelleR.Util.FastClone
{
    [InitializeOnLoad]
    public class FastCloneHighlighter
    {
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

        private static void OnSceneGUI(SceneView sceneView)
        {
            Handles.BeginGUI();
            var rect = new Rect(10, 10, 200, 30);

            GUI.Box(rect, "Running as CLONE Mode", new GUIStyle("HelpBox")
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.5f, 0f) }
            });
            Handles.EndGUI();
        }
    }
}
