using UnityEngine;
using UnityEditor;
using System.IO;

namespace TelleR
{
    [CustomEditor(typeof(MeshFilter))]
    [CanEditMultipleObjects]
    public class MeshFilterFBXBackupEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor defaultEditor;

        private void OnEnable()
        {
            System.Type inspectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.MeshFilterEditor");
            if (inspectorType != null)
            {
                defaultEditor = CreateEditor(targets, inspectorType);
            }
        }

        private void OnDisable()
        {
            if (defaultEditor != null)
            {
                DestroyImmediate(defaultEditor);
            }
        }

        public override void OnInspectorGUI()
        {
            MeshFilter meshFilter = target as MeshFilter;
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                DrawFBXBackupUI(meshFilter.sharedMesh, meshFilter.gameObject, false);
            }

            if (defaultEditor != null)
            {
                defaultEditor.OnInspectorGUI();
            }
            else
            {
                DrawDefaultInspector();
            }
        }

        private void DrawFBXBackupUI(Mesh mesh, GameObject go, bool isSkinned)
        {
            MeshFBXBackupUtility.DrawBackupUI(mesh, go, isSkinned);
        }
    }

    [CustomEditor(typeof(SkinnedMeshRenderer))]
    [CanEditMultipleObjects]
    public class SkinnedMeshRendererFBXBackupEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor defaultEditor;

        private void OnEnable()
        {
            System.Type inspectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.SkinnedMeshRendererEditor");
            if (inspectorType != null)
            {
                defaultEditor = CreateEditor(targets, inspectorType);
            }
        }

        private void OnDisable()
        {
            if (defaultEditor != null)
            {
                DestroyImmediate(defaultEditor);
            }
        }

        public override void OnInspectorGUI()
        {
            SkinnedMeshRenderer smr = target as SkinnedMeshRenderer;
            if (smr != null && smr.sharedMesh != null)
            {
                DrawFBXBackupUI(smr.sharedMesh, smr.gameObject, true);
            }

            if (defaultEditor != null)
            {
                defaultEditor.OnInspectorGUI();
            }
            else
            {
                DrawDefaultInspector();
            }
        }

        private void DrawFBXBackupUI(Mesh mesh, GameObject go, bool isSkinned)
        {
            MeshFBXBackupUtility.DrawBackupUI(mesh, go, isSkinned);
        }
    }

    public static class MeshFBXBackupUtility
    {
        private const string DefaultFBXFolder = "Assets/GeneratedFBX";

        public static void DrawBackupUI(Mesh mesh, GameObject go, bool isSkinned)
        {
            if (mesh == null) return;

            string assetPath = AssetDatabase.GetAssetPath(mesh);
            if (HasFBXBackup(assetPath)) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(EditorGUIUtility.IconContent("console.warnicon.sml"), GUILayout.Width(20), GUILayout.Height(18));
                    EditorGUILayout.LabelField("No FBX backup - edits may be lost on reimport", EditorStyles.wordWrappedMiniLabel);
                }

                GUILayout.Space(2);

                if (GUILayout.Button("Create FBX Backup", GUILayout.Height(22)))
                {
                    CreateFBXBackup(mesh, go, isSkinned);
                }
            }

            GUILayout.Space(2);
        }

        private static bool HasFBXBackup(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            string ext = Path.GetExtension(assetPath).ToLower();
            return ext == ".fbx" || ext == ".obj" || ext == ".blend";
        }

        private static void CreateFBXBackup(Mesh mesh, GameObject go, bool isSkinned)
        {
#if UNITY_EDITOR
            if (!Directory.Exists(DefaultFBXFolder))
            {
                Directory.CreateDirectory(DefaultFBXFolder);
                AssetDatabase.Refresh();
            }

            string meshName = string.IsNullOrEmpty(mesh.name) ? go.name : mesh.name;
            meshName = SanitizeFileName(meshName);
            string fbxPath = AssetDatabase.GenerateUniqueAssetPath(DefaultFBXFolder + "/" + meshName + ".fbx");

            bool success = ExportToFBX(go, fbxPath);

            if (success)
            {
                AssetDatabase.Refresh();

                ModelImporter importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
                if (importer != null)
                {
                    importer.materialImportMode = ModelImporterMaterialImportMode.None;
                    importer.SaveAndReimport();
                }

                GameObject fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                Mesh fbxMesh = FindMeshInAsset(fbxPath);

                if (fbxMesh != null)
                {
                    Undo.RecordObject(go, "Replace Mesh with FBX");

                    if (isSkinned)
                    {
                        SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
                        if (smr != null)
                        {
                            Undo.RecordObject(smr, "Replace Mesh with FBX");
                            smr.sharedMesh = fbxMesh;
                        }
                    }
                    else
                    {
                        MeshFilter mf = go.GetComponent<MeshFilter>();
                        if (mf != null)
                        {
                            Undo.RecordObject(mf, "Replace Mesh with FBX");
                            mf.sharedMesh = fbxMesh;
                        }
                    }

                    EditorUtility.SetDirty(go);
                    Debug.Log("FBX backup created and applied: " + fbxPath);
                }
                else
                {
                    Debug.LogWarning("FBX created but mesh not found in asset: " + fbxPath);
                }
            }
            else
            {
                Debug.LogError("Failed to export FBX. Make sure FBX Exporter package is installed.");
            }
#endif
        }

        private static bool ExportToFBX(GameObject go, string path)
        {
#if UNITY_EDITOR
            System.Type exporterType = System.Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");

            if (exporterType == null)
            {
                EditorUtility.DisplayDialog("FBX Exporter Required",
                    "Please install 'FBX Exporter' package from Package Manager.\n\nWindow > Package Manager > FBX Exporter",
                    "OK");
                return false;
            }

            System.Reflection.MethodInfo exportMethod = exporterType.GetMethod("ExportObject",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new System.Type[] { typeof(string), typeof(UnityEngine.Object) },
                null);

            if (exportMethod != null)
            {
                object result = exportMethod.Invoke(null, new object[] { path, go });
                return result != null && !string.IsNullOrEmpty(result.ToString());
            }

            System.Reflection.MethodInfo[] methods = exporterType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            foreach (var method in methods)
            {
                if (method.Name.Contains("Export"))
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length >= 2)
                    {
                        try
                        {
                            object result = method.Invoke(null, new object[] { path, go });
                            if (result != null) return true;
                        }
                        catch { }
                    }
                }
            }
#endif
            return false;
        }

        private static Mesh FindMeshInAsset(string assetPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (Object asset in assets)
            {
                if (asset is Mesh mesh)
                {
                    return mesh;
                }
            }
            return null;
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}