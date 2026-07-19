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

            // 내장 프리미티브(큐브 등)는 재임포트 유실 대상이 아니므로 경고를 띄우지 않음
            if (!string.IsNullOrEmpty(assetPath) &&
                (assetPath.Contains("unity default resources") || assetPath.Contains("unity_builtin_extra"))) return;

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
            bool replaceMesh = false;
            if (isSkinned)
            {
                // SMR은 스켈레톤이 FBX에 포함된다는 보장이 없고 본 순서·bindpose 정합성을 검증할 수 없어
                // 참조 교체 없이 백업 파일만 생성한다 (교체 시 캐릭터 렌더링이 파손될 수 있음).
                replaceMesh = false;
            }
            else
            {
                int choice = EditorUtility.DisplayDialogComplex("FBX Backup",
                    "FBX 백업 파일을 생성합니다.\n\n생성 후 현재 메시 참조를 FBX 메시로 교체할 수도 있습니다.\n" +
                    "(교체 시 정점 순서·노말·정밀도가 원본과 달라질 수 있습니다)",
                    "백업 + 교체", "취소", "백업만");
                if (choice == 1) return;
                replaceMesh = choice == 0;
            }

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

                if (!replaceMesh)
                {
                    Debug.Log("FBX backup created (참조 교체 없음): " + fbxPath);
                }
                else
                {
                    // 계층에 메시가 여러 개일 수 있으므로 반드시 이름으로 매칭 — 첫 메시를 무조건 쓰면
                    // 엉뚱한 자식 메시로 교체될 수 있음
                    Mesh fbxMesh = FindMeshInAsset(fbxPath, mesh.name);

                    if (fbxMesh != null)
                    {
                        MeshFilter mf = go.GetComponent<MeshFilter>();
                        if (mf != null)
                        {
                            Undo.RecordObject(mf, "Replace Mesh with FBX");
                            mf.sharedMesh = fbxMesh;
                        }

                        EditorUtility.SetDirty(go);
                        Debug.Log("FBX backup created and applied: " + fbxPath);
                    }
                    else
                    {
                        Debug.LogWarning($"FBX는 생성했지만 '{mesh.name}'과 일치하는 메시를 찾지 못해 참조 교체를 생략했습니다: {fbxPath}");
                    }
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

        private static Mesh FindMeshInAsset(string assetPath, string meshName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            Mesh firstMesh = null;
            int meshCount = 0;
            foreach (Object asset in assets)
            {
                if (asset is Mesh mesh)
                {
                    if (mesh.name == meshName) return mesh;
                    if (firstMesh == null) firstMesh = mesh;
                    meshCount++;
                }
            }
            // 이름 매칭 실패 시 메시가 정확히 1개일 때만 그 메시를 신뢰
            return meshCount == 1 ? firstMesh : null;
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