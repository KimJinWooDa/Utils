#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TelleR.SceneFlow
{
    public class SceneFlowEditorWindow : EditorWindow
    {
        private static readonly Vector2 kFixedSize = new Vector2(1100, 720);
        private const string LAYOUT_DATA_PATH = "Assets/Editor/SceneFlowLayoutData.asset";

        private SceneFlowDataManager dataManager;
        private SceneFlowRenderer renderer;
        private SceneFlowStyleProvider styleProvider;
        private SceneFlowLayoutData layoutData;

        private Vector2 graphScrollPos;
        private Vector2 inspectorScrollPos;
        private SceneAssetRef selectedScene;
        private SceneAssetRef connectingScene;
        
        private SceneAssetRef draggingScene;
        private Vector2 dragOffset;

        [MenuItem("Tools/TelleR/Scene Flow Editor _F3")]
        public static void ShowWindow()
        {
            var window = GetWindow<SceneFlowEditorWindow>("Scene Flow Editor");

            window.minSize = kFixedSize;
            window.maxSize = kFixedSize;

            window.Show();
        }

        private void OnEnable()
        {
            minSize = kFixedSize;
            maxSize = kFixedSize;

            InitializeComponents();
            LoadOrCreateLayoutData();
            dataManager.LoadScenes();
        }

        private void OnFocus()
        {
            dataManager?.LoadScenes();
            Repaint();
        }

        private void InitializeComponents()
        {
            styleProvider = new SceneFlowStyleProvider();
            dataManager = new SceneFlowDataManager();
            renderer = new SceneFlowRenderer(styleProvider);
        }

        private void LoadOrCreateLayoutData()
        {
            layoutData = AssetDatabase.LoadAssetAtPath<SceneFlowLayoutData>(LAYOUT_DATA_PATH);
            
            if (layoutData == null)
            {
                string directory = Path.GetDirectoryName(LAYOUT_DATA_PATH);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                layoutData = ScriptableObject.CreateInstance<SceneFlowLayoutData>();
                AssetDatabase.CreateAsset(layoutData, LAYOUT_DATA_PATH);
                AssetDatabase.SaveAssets();
            }
            
            renderer.SetLayoutData(layoutData);
        }

        private void OnGUI()
        {
            if (dataManager == null || renderer == null)
            {
                InitializeComponents();
                LoadOrCreateLayoutData();
            }

            DrawHeader();

            EditorGUILayout.BeginHorizontal();
            {
                DrawGraphArea();
                DrawInspectorArea();
            }
            EditorGUILayout.EndHorizontal();
            
            if (draggingScene != null)
            {
                Repaint();
            }
        }

        private void DrawHeader()
        {
            var headerRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(40));

            for (int i = 0; i < 20; i++)
            {
                float t = i / 20f;
                var gradRect = new Rect(headerRect.x, headerRect.y + headerRect.height * t,
                    headerRect.width, headerRect.height / 20f);
                var gradColor = Color.Lerp(
                    new Color(0.12f, 0.12f, 0.14f),
                    new Color(0.09f, 0.09f, 0.11f), t);
                EditorGUI.DrawRect(gradRect, gradColor);
            }

            GUILayout.Space(10);
            GUILayout.Label("Scene Flow Editor", styleProvider.HeaderTitleStyle);
            var hotkeyStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) },
                alignment = TextAnchor.MiddleLeft
            };
            GUILayout.Label("(F2)", hotkeyStyle);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+ New Scene", styleProvider.HeaderButtonStyle))
                CreateNewSceneAssetRef();

            if (GUILayout.Button("Refresh", styleProvider.HeaderButtonStyle))
            {
                dataManager.LoadScenes();
                Repaint();
            }
            
            if (GUILayout.Button("Reset Layout", styleProvider.HeaderButtonStyle))
            {
                if (EditorUtility.DisplayDialog("Reset Layout", 
                    "Reset all node positions to default?", "Reset", "Cancel"))
                {
                    layoutData.ClearAllPositions();
                    EditorUtility.SetDirty(layoutData);
                    AssetDatabase.SaveAssets();
                    Repaint();
                }
            }

            if (GUILayout.Button("Validate Build", styleProvider.HeaderButtonStyle))
                ValidateBuildSettings();

            GUILayout.Space(10);
            GUILayout.Label($"Total Scenes: {dataManager.AllScenes.Count}", styleProvider.HeaderInfoStyle);
            GUILayout.Space(10);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawGraphArea()
        {
            EditorGUILayout.BeginVertical(styleProvider.GraphAreaStyle, GUILayout.ExpandWidth(true));
            {
                DrawGraph();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawGraph()
        {
            var graphRect = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (graphRect.width <= 0 || graphRect.height <= 0) return;

            EditorGUI.DrawRect(graphRect, styleProvider.GraphBackgroundColor);

            var scenes = dataManager.AllScenes;

            renderer.SetContentWidth(graphRect.width);

            var contentSize = renderer.CalculateContentSize(scenes.Count);

            var viewRect = new Rect(0, 0,
                Mathf.Max(contentSize.x, graphRect.width),
                Mathf.Max(contentSize.y, graphRect.height));

            graphScrollPos = GUI.BeginScrollView(graphRect, graphScrollPos, viewRect);

            Vector2 mousePos = Event.current.mousePosition;

            var result = renderer.DrawSceneFlow(
                scenes,
                dataManager.GetCoreScenes(),
                selectedScene,
                connectingScene,
                mousePos,
                draggingScene,
                dragOffset
            );

            if (result.DragStarted && result.DraggedScene != null)
            {
                draggingScene = result.DraggedScene;
                dragOffset = result.DragPosition;
                selectedScene = result.HitScene;
                Selection.activeObject = result.HitScene;
            }
            else if (result.DragEnded && result.DraggedScene != null)
            {
                SaveNodePosition(result.DraggedScene, result.DragPosition);
                draggingScene = null;
            }
            else if (result.NodeHit && result.HitScene != null && draggingScene == null)
            {
                selectedScene = result.HitScene;
                Selection.activeObject = result.HitScene;
                GUI.FocusControl(null);
                Repaint();
            }
            else if (result.EmptyHit && Event.current.type == EventType.MouseDown && draggingScene == null)
            {
                selectedScene = null;
                connectingScene = null;
                Selection.activeObject = null;
                GUI.FocusControl(null);
                Repaint();
            }

            GUI.EndScrollView();
        }

        private void SaveNodePosition(SceneAssetRef scene, Vector2 position)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(scene));
            if (string.IsNullOrEmpty(guid)) return;
            
            layoutData.SetPosition(guid, position);
            EditorUtility.SetDirty(layoutData);
            AssetDatabase.SaveAssets();
        }

        private void DrawInspectorArea()
        {
            EditorGUILayout.BeginVertical(styleProvider.InspectorAreaStyle, GUILayout.Width(350));
            {
                GUILayout.Label("Scene Details", styleProvider.InspectorHeaderStyle);

                var line = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(new Rect(line.x + 10, line.y, line.width - 20, 1),
                    new Color(0.2f, 0.2f, 0.22f));

                if (selectedScene != null)
                {
                    inspectorScrollPos = EditorGUILayout.BeginScrollView(
                        inspectorScrollPos, false, false,
                        GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
                    {
                        DrawSceneInspector(selectedScene);
                    }
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    EditorGUILayout.Space(20);
                    var messageStyle = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        wordWrap = true,
                        normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
                        padding = new RectOffset(20, 20, 0, 0)
                    };
                    GUILayout.Label("Select a scene node to view and edit its connections", messageStyle);
                }

                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSceneInspector(SceneAssetRef scene)
        {
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginVertical(styleProvider.InspectorSectionStyle);
            {
                GUILayout.Label("Scene Information", styleProvider.InspectorSectionHeaderStyle);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Name:", GUILayout.Width(80));
                EditorGUILayout.LabelField(scene.SceneName, EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Build Index:", GUILayout.Width(80));
                int buildIndex = scene.CurrentSceneIndex;
                if (buildIndex >= 0)
                    EditorGUILayout.LabelField(buildIndex.ToString(), EditorStyles.boldLabel);
                else
                    EditorGUILayout.LabelField("Not in Build",
                        new GUIStyle(EditorStyles.label) { normal = { textColor = Color.red } });
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);

                EditorGUI.BeginChangeCheck();
                var newSceneAsset = EditorGUILayout.ObjectField("Scene Asset",
                    scene.SceneAsset, typeof(SceneAsset), false) as SceneAsset;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(scene, "Change Scene Asset");
                    var so = new SerializedObject(scene);
                    so.FindProperty("sceneAsset").objectReferenceValue = newSceneAsset;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(scene);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(styleProvider.InspectorSectionStyle);
            {
                GUILayout.Label("Scene Connections", styleProvider.InspectorSectionHeaderStyle);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("← Previous:", GUILayout.Width(80));
                var prevScene = scene.PreviousAssetRef;
                EditorGUI.BeginChangeCheck();
                prevScene = EditorGUILayout.ObjectField(prevScene, typeof(SceneAssetRef), false,
                    GUILayout.Width(180)) as SceneAssetRef;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(scene, "Change Previous Scene");
                    scene.SetPreviousScene(prevScene);
                    Repaint();
                }
                if (prevScene != null && GUILayout.Button("→", GUILayout.Width(25)))
                {
                    selectedScene = prevScene;
                    Selection.activeObject = prevScene;
                    EditorGUIUtility.PingObject(prevScene);
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("→ Next:", GUILayout.Width(80));
                var nextScene = scene.NextAssetRef;
                EditorGUI.BeginChangeCheck();
                nextScene = EditorGUILayout.ObjectField(nextScene, typeof(SceneAssetRef), false,
                    GUILayout.Width(180)) as SceneAssetRef;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(scene, "Change Next Scene");
                    scene.SetNextScene(nextScene);
                    Repaint();
                }
                if (nextScene != null && GUILayout.Button("→", GUILayout.Width(25)))
                {
                    selectedScene = nextScene;
                    Selection.activeObject = nextScene;
                    EditorGUIUtility.PingObject(nextScene);
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginVertical(styleProvider.InspectorSectionStyle);
            {
                GUILayout.Label("Actions", styleProvider.InspectorSectionHeaderStyle);

                if (GUILayout.Button("Open Scene", styleProvider.ActionButtonStyle))
                {
                    if (scene.SceneAsset != null)
                    {
                        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                            EditorSceneManager.OpenScene(AssetDatabase.GetAssetPath(scene.SceneAsset));
                    }
                }

                if (Application.isPlaying)
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        GUI.enabled = scene.PreviousSceneIndex >= 0;
                        if (GUILayout.Button("Load Previous", styleProvider.ActionButtonStyle))
                            UnityEngine.SceneManagement.SceneManager.LoadScene(scene.PreviousSceneIndex);

                        GUI.enabled = scene.NextSceneIndex >= 0;
                        if (GUILayout.Button("Load Next", styleProvider.ActionButtonStyle))
                            UnityEngine.SceneManagement.SceneManager.LoadScene(scene.NextSceneIndex);
                        GUI.enabled = true;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                EditorGUILayout.Space(5);
                
                if (GUILayout.Button("Reset Position", styleProvider.ActionButtonStyle))
                {
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(scene));
                    layoutData.ClearPosition(guid);
                    EditorUtility.SetDirty(layoutData);
                    AssetDatabase.SaveAssets();
                    Repaint();
                }

                EditorGUILayout.Space(10);

                if (GUILayout.Button("Delete Scene Reference", styleProvider.DangerButtonStyle))
                {
                    if (EditorUtility.DisplayDialog("Delete Scene Reference",
                        $"Are you sure you want to delete '{scene.SceneName}'?", "Delete", "Cancel"))
                    {
                        DeleteSceneReference(scene);
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void CreateNewSceneAssetRef()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create New Scene Reference", "NewSceneRef", "asset",
                "Enter a name for the new scene reference");

            if (!string.IsNullOrEmpty(path))
            {
                var newSceneRef = ScriptableObject.CreateInstance<SceneAssetRef>();
                AssetDatabase.CreateAsset(newSceneRef, path);
                AssetDatabase.SaveAssets();

                dataManager.LoadScenes();
                selectedScene = newSceneRef;

                EditorUtility.DisplayDialog("Success", "New scene reference created!", "OK");
            }
        }

        private void DeleteSceneReference(SceneAssetRef sceneRef)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sceneRef));
            layoutData.ClearPosition(guid);
            
            foreach (var scene in dataManager.AllScenes)
            {
                if (scene.PreviousAssetRef == sceneRef) scene.SetPreviousScene(null);
                if (scene.NextAssetRef == sceneRef) scene.SetNextScene(null);
            }

            string path = AssetDatabase.GetAssetPath(sceneRef);
            if (!string.IsNullOrEmpty(path)) AssetDatabase.DeleteAsset(path);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            selectedScene = null;
            dataManager.LoadScenes();
            Repaint();
        }

        private void ValidateBuildSettings()
        {
            var missingScenes = new List<SceneAssetRef>();

            foreach (var scene in dataManager.AllScenes)
            {
                if (scene.CurrentSceneIndex < 0 && scene.SceneAsset != null)
                    missingScenes.Add(scene);
            }

            if (missingScenes.Count > 0)
            {
                string message = "The following scenes are not in Build Settings:\n\n";
                foreach (var scene in missingScenes) message += $"• {scene.SceneName}\n";
                message += "\nWould you like to add them?";

                if (EditorUtility.DisplayDialog("Missing Scenes in Build", message, "Add to Build", "Cancel"))
                    AddScenesToBuild(missingScenes);
            }
            else
            {
                EditorUtility.DisplayDialog("Validation Complete",
                    "All scenes are properly configured in Build Settings!", "OK");
            }
        }

        private void AddScenesToBuild(List<SceneAssetRef> scenes)
        {
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            foreach (var sceneRef in scenes)
            {
                if (sceneRef.SceneAsset == null) continue;
                string path = AssetDatabase.GetAssetPath(sceneRef.SceneAsset);
                if (!string.IsNullOrEmpty(path))
                    buildScenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = buildScenes.ToArray();
            EditorUtility.DisplayDialog("Success", "Scenes added to Build Settings!", "OK");
        }
    }
}
#endif