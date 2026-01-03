#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(SceneAssetRef))]
public class SceneAssetRefEditor : Editor
{
    private SerializedProperty sceneAssetProp;
    private SerializedProperty previousSceneProp;
    private SerializedProperty nextSceneProp;
    private SerializedProperty scenePathProp;

    private GUIStyle headerStyle;
    private GUIStyle infoStyle;
    private GUIStyle sectionHeaderStyle;
    private GUIStyle pathStyle;

    private void OnEnable()
    {
        sceneAssetProp = serializedObject.FindProperty("sceneAsset");
        previousSceneProp = serializedObject.FindProperty("previousScene");
        nextSceneProp = serializedObject.FindProperty("nextScene");
        scenePathProp = serializedObject.FindProperty("scenePath");
    }

    private void InitStyles()
    {
        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 5, 5)
            };
        }

        if (sectionHeaderStyle == null)
        {
            sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                padding = new RectOffset(0, 0, 2, 5)
            };
        }

        if (infoStyle == null)
        {
            infoStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 2, 2)
            };
        }

        if (pathStyle == null)
        {
            pathStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
                wordWrap = true,
                fontSize = 10,
                padding = new RectOffset(2, 2, 2, 2)
            };
        }
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        InitStyles();

        var sceneRef = (SceneAssetRef)target;

        // Header
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Scene Asset Reference", headerStyle);
        EditorGUILayout.Space(5);

        // Scene Configuration Section
        EditorGUILayout.BeginVertical("box");
        {
            EditorGUILayout.LabelField("Scene Configuration", sectionHeaderStyle);
            EditorGUILayout.Space(2);

            // Scene Asset Field
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(sceneAssetProp, new GUIContent("Scene Asset"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
            }

            // Build Info
            if (sceneRef.SceneAsset != null)
            {
                EditorGUILayout.Space(5);
                
                int buildIndex = sceneRef.CurrentSceneIndex;
                if (buildIndex >= 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Build Index:", GUILayout.Width(80));
                    EditorGUILayout.LabelField(buildIndex.ToString(), EditorStyles.boldLabel);
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.HelpBox("Scene is not in Build Settings!", MessageType.Warning);
                    if (GUILayout.Button("Add to Build Settings"))
                    {
                        AddToBuildSettings();
                    }
                }

                // Path info
                if (!string.IsNullOrEmpty(scenePathProp.stringValue))
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField("Path:", pathStyle);
                    
                    var pathRect = EditorGUILayout.GetControlRect(GUILayout.Height(EditorGUIUtility.singleLineHeight));
                    EditorGUI.SelectableLabel(pathRect, scenePathProp.stringValue, pathStyle);
                }
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(5);

        // Scene Flow Section
        EditorGUILayout.BeginVertical("box");
        {
            EditorGUILayout.LabelField("Scene Flow", sectionHeaderStyle);
            EditorGUILayout.Space(2);

            // Previous Scene
            EditorGUILayout.PropertyField(previousSceneProp, new GUIContent("← Previous"));
            EditorGUILayout.Space(2);

            // Next Scene
            EditorGUILayout.PropertyField(nextSceneProp, new GUIContent("→ Next"));
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(10);

        // Scene Flow Editor Button
        var buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            fixedHeight = 30
        };
        
        if (GUILayout.Button("Open Scene Flow Editor", buttonStyle))
        {
            TelleR.SceneFlow.SceneFlowEditorWindow.ShowWindow();
            
            var window = EditorWindow.GetWindow<TelleR.SceneFlow.SceneFlowEditorWindow>();
            if (window != null)
            {
                Selection.activeObject = target;
            }
        }

        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField("Tip: Press F2 to quickly open Scene Flow Editor", infoStyle);

        serializedObject.ApplyModifiedProperties();
    }

    private void AddToBuildSettings()
    {
        var sceneRef = (SceneAssetRef)target;
        if (sceneRef.SceneAsset == null) return;

        string path = AssetDatabase.GetAssetPath(sceneRef.SceneAsset);
        if (string.IsNullOrEmpty(path)) return;

        var buildScenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        
        bool exists = buildScenes.Any(s => s.path == path);
        if (!exists)
        {
            buildScenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            
            Debug.Log($"Added '{sceneRef.SceneName}' to Build Settings");
            EditorUtility.DisplayDialog("Success", $"'{sceneRef.SceneName}' has been added to Build Settings.", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Info", $"'{sceneRef.SceneName}' is already in Build Settings.", "OK");
        }
    }
}
#endif
