using UnityEngine;
using UnityEditor;
using System.IO;
using System.Reflection;

namespace TelleR
{
    [CustomEditor(typeof(MeshFilter))]
    [CanEditMultipleObjects]
    public class MeshFilterFBXBackupEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor defaultEditor;
        private MethodInfo defaultSceneGUI;

        private void OnEnable()
        {
            System.Type inspectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.MeshFilterEditor");
            if (inspectorType != null)
            {
                defaultEditor = CreateEditor(targets, inspectorType);
                defaultSceneGUI = DefaultEditorForwarder.FindSceneGUI(defaultEditor);
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
                MeshFBXBackupUtility.DrawBackupUI(meshFilter.sharedMesh, meshFilter.gameObject, false);
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

        // 기본 인스펙터의 씬 GUI·프리뷰 기능을 그대로 유지한다
        private void OnSceneGUI() => DefaultEditorForwarder.SceneGUI(this, defaultEditor, defaultSceneGUI);
        public override bool HasPreviewGUI() => defaultEditor != null ? defaultEditor.HasPreviewGUI() : base.HasPreviewGUI();
        public override void OnPreviewGUI(Rect r, GUIStyle background) { if (defaultEditor != null) defaultEditor.OnPreviewGUI(r, background); else base.OnPreviewGUI(r, background); }
        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background) { if (defaultEditor != null) defaultEditor.OnInteractivePreviewGUI(r, background); else base.OnInteractivePreviewGUI(r, background); }
        public override void OnPreviewSettings() { if (defaultEditor != null) defaultEditor.OnPreviewSettings(); else base.OnPreviewSettings(); }
        public override GUIContent GetPreviewTitle() => defaultEditor != null ? defaultEditor.GetPreviewTitle() : base.GetPreviewTitle();
        public override string GetInfoString() => defaultEditor != null ? defaultEditor.GetInfoString() : base.GetInfoString();
        public override bool RequiresConstantRepaint() => defaultEditor != null ? defaultEditor.RequiresConstantRepaint() : base.RequiresConstantRepaint();
    }

    [CustomEditor(typeof(SkinnedMeshRenderer))]
    [CanEditMultipleObjects]
    public class SkinnedMeshRendererFBXBackupEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor defaultEditor;
        private MethodInfo defaultSceneGUI;

        private void OnEnable()
        {
            System.Type inspectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.SkinnedMeshRendererEditor");
            if (inspectorType != null)
            {
                defaultEditor = CreateEditor(targets, inspectorType);
                defaultSceneGUI = DefaultEditorForwarder.FindSceneGUI(defaultEditor);
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
                MeshFBXBackupUtility.DrawBackupUI(smr.sharedMesh, smr.gameObject, true);
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

        // 기본 인스펙터의 씬 GUI(Edit Bounds 등)·프리뷰 기능을 그대로 유지한다
        private void OnSceneGUI() => DefaultEditorForwarder.SceneGUI(this, defaultEditor, defaultSceneGUI);
        public override bool HasPreviewGUI() => defaultEditor != null ? defaultEditor.HasPreviewGUI() : base.HasPreviewGUI();
        public override void OnPreviewGUI(Rect r, GUIStyle background) { if (defaultEditor != null) defaultEditor.OnPreviewGUI(r, background); else base.OnPreviewGUI(r, background); }
        public override void OnInteractivePreviewGUI(Rect r, GUIStyle background) { if (defaultEditor != null) defaultEditor.OnInteractivePreviewGUI(r, background); else base.OnInteractivePreviewGUI(r, background); }
        public override void OnPreviewSettings() { if (defaultEditor != null) defaultEditor.OnPreviewSettings(); else base.OnPreviewSettings(); }
        public override GUIContent GetPreviewTitle() => defaultEditor != null ? defaultEditor.GetPreviewTitle() : base.GetPreviewTitle();
        public override string GetInfoString() => defaultEditor != null ? defaultEditor.GetInfoString() : base.GetInfoString();
        public override bool RequiresConstantRepaint() => defaultEditor != null ? defaultEditor.RequiresConstantRepaint() : base.RequiresConstantRepaint();
    }

    internal static class DefaultEditorForwarder
    {
        // OnSceneGUI는 가상 메서드가 아니라 Unity가 이름으로 호출하는 메시지라 리플렉션으로 전달한다
        public static MethodInfo FindSceneGUI(UnityEditor.Editor editor)
        {
            if (editor == null) return null;
            return editor.GetType().GetMethod("OnSceneGUI",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null);
        }

        // Editor.referenceTargetIndex는 internal이라 리플렉션으로 접근한다 (없으면 동기화만 건너뜀)
        private static readonly PropertyInfo ReferenceTargetIndex = typeof(UnityEditor.Editor).GetProperty(
            "referenceTargetIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static void SceneGUI(UnityEditor.Editor wrapper, UnityEditor.Editor editor, MethodInfo method)
        {
            if (editor == null || method == null) return;
            SyncTargetIndex(wrapper, editor);
            try
            {
                method.Invoke(editor, null);
            }
            catch (TargetInvocationException e) when (e.InnerException is ExitGUIException)
            {
                throw e.InnerException;
            }
        }

        // 다중 선택 시 SceneView는 래퍼의 referenceTargetIndex만 대상별로 바꿔 OnSceneGUI를 부른다.
        // 내부 에디터도 같은 대상을 가리키게 맞추지 않으면 첫 대상만 N번 그려진다.
        private static void SyncTargetIndex(UnityEditor.Editor wrapper, UnityEditor.Editor editor)
        {
            if (wrapper == null || ReferenceTargetIndex == null || !ReferenceTargetIndex.CanRead || !ReferenceTargetIndex.CanWrite) return;
            try
            {
                ReferenceTargetIndex.SetValue(editor, ReferenceTargetIndex.GetValue(wrapper));
            }
            catch (System.Exception)
            {
                // 내부 API가 바뀐 버전에서는 기존 동작(첫 대상 기준)으로 둔다
            }
        }
    }

    public static class MeshFBXBackupUtility
    {
        private const string DefaultFBXFolder = "Assets/GeneratedFBX";
        private const string FbxPackageId = "com.unity.formats.fbx";
        private const string LogPrefix = "[TelleR/FBX Backup] ";

        private static readonly GUIContent SaveAsAssetContent = new GUIContent("Save as .asset", "메시를 .asset 파일로 저장하고 이 컴포넌트가 저장된 메시를 쓰도록 바꿉니다.");
#if TELLER_FBX
        private static readonly GUIContent SceneFbxBackupContent = new GUIContent("Create FBX Backup", "FBX Exporter로 FBX 파일을 만듭니다.");
        private static readonly GUIContent AssetFbxBackupContent = new GUIContent("Create FBX Backup", "이 메시 에셋을 FBX Exporter로 FBX 파일로도 내보내 둡니다.");
#endif

        public static void DrawBackupUI(Mesh mesh, GameObject go, bool isSkinned)
        {
            if (mesh == null || go == null) return;

            // 에셋(.fbx/.asset/내장 프리미티브)에 저장된 메시는 씬과 독립적으로 보존되므로 경고하지 않는다.
            // 모델 파일이 아닌 에셋 메시(.asset 등)는 FBX로 내보낼 수 있도록 버튼만 조용히 둔다.
            if (EditorUtility.IsPersistent(mesh))
            {
#if TELLER_FBX
                if (IsNonModelMeshAsset(mesh)) DrawAssetBackupButton(mesh, go, isSkinned);
#endif
                return;
            }
            // Play 모드의 비영구 메시는 런타임 인스턴스(.mesh 접근 사본·절차 생성)라 씬에 저장되지 않고 종료 시 버려진다
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // 저장되지 않는 생성 메시(텍스트 메시 등)와 다른 컴포넌트가 관리하는 메시는 건드리지 않는다
            if ((mesh.hideFlags & HideFlags.DontSaveInEditor) != 0) return;
            if (go.GetComponent("ProBuilderMesh") != null) return;
            // 피벗 편집 중인 메시는 Mesh Pivot Tool의 Apply에서 저장 방식을 고른다
            if (go.GetComponent<TelleR.Tools.MeshPivotTool>() != null) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(EditorGUIUtility.IconContent("console.warnicon.sml"), GUILayout.Width(20), GUILayout.Height(18));
                    EditorGUILayout.LabelField("씬에만 저장된 메시입니다. 이 씬 밖(프리팹·다른 씬)에서는 쓸 수 없습니다. " +
                                               ".asset 또는 FBX 파일로 저장해 두면 재사용·백업할 수 있습니다.",
                        EditorStyles.wordWrappedMiniLabel);
                }

                GUILayout.Space(2);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(SaveAsAssetContent, GUILayout.Height(22)))
                    {
                        SaveSceneMeshAsAsset(mesh, go);
                        GUIUtility.ExitGUI();
                    }
#if TELLER_FBX
                    if (GUILayout.Button(SceneFbxBackupContent, GUILayout.Height(22)))
                    {
                        CreateFBXBackup(mesh, go, isSkinned);
                        GUIUtility.ExitGUI();
                    }
#endif
                }

#if !TELLER_FBX
                EditorGUILayout.LabelField("FBX로 내보내려면 FBX Exporter 패키지(" + FbxPackageId + ")가 필요합니다.", EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Open Package Manager", GUILayout.Height(20)))
                {
                    OpenPackageManager();
                    GUIUtility.ExitGUI();
                }
#endif
            }

            GUILayout.Space(2);
        }

#if TELLER_FBX
        // .fbx/.obj/.blend 등 모델 파일에서 임포트된 메시와 내장 리소스(큐브 등)는 이미 원본 파일이 있으므로 제외한다
        private static bool IsNonModelMeshAsset(Mesh mesh)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path)) return false;
            if (path.Contains("unity default resources") || path.Contains("unity_builtin_extra")) return false;
            return !(AssetImporter.GetAtPath(path) is ModelImporter);
        }

        private static void DrawAssetBackupButton(Mesh mesh, GameObject go, bool isSkinned)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(AssetFbxBackupContent, EditorStyles.miniButton))
                {
                    CreateFBXBackup(mesh, go, isSkinned);
                    GUIUtility.ExitGUI();
                }
            }
            GUILayout.Space(2);
        }
#endif

        private static void OpenPackageManager()
        {
            // UI.Window.Open은 버전마다 들어 있는 어셈블리가 달라 리플렉션으로 찾아 호출하고, 실패하면 메뉴로 연다
            System.Type window = null;
            foreach (Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                window = asm.GetType("UnityEditor.PackageManager.UI.Window", false);
                if (window != null) break;
            }
            MethodInfo open = window?.GetMethod("Open", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (open != null)
            {
                try
                {
                    open.Invoke(null, new object[] { FbxPackageId });
                    return;
                }
                catch (System.Exception) { }
            }
            EditorApplication.ExecuteMenuItem("Window/Package Manager");
        }

        private static void SaveSceneMeshAsAsset(Mesh mesh, GameObject go)
        {
            string path = EditorUtility.SaveFilePanelInProject("메시 에셋 저장", SanitizeFileName(string.IsNullOrEmpty(mesh.name) ? go.name : mesh.name),
                "asset", "메시를 저장할 위치를 고르세요.", "Assets");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null &&
                !TelleRGUI.Confirm("메시 에셋 덮어쓰기",
                    $"'{path}'의 기존 메시 데이터를 이 메시로 덮어씁니다.\n이 메시를 쓰는 다른 오브젝트·프리팹도 함께 바뀝니다.",
                    "덮어쓰기", "취소"))
                return;

            Mesh saved = SaveMeshAsAsset(mesh, path);
            if (saved == null) return;

            const string undoName = "Save Mesh Asset";
            foreach (MeshFilter mf in go.GetComponents<MeshFilter>())
                if (mf.sharedMesh == mesh) { Undo.RecordObject(mf, undoName); mf.sharedMesh = saved; EditorUtility.SetDirty(mf); }
            foreach (SkinnedMeshRenderer smr in go.GetComponents<SkinnedMeshRenderer>())
                if (smr.sharedMesh == mesh) { Undo.RecordObject(smr, undoName); smr.sharedMesh = saved; EditorUtility.SetDirty(smr); }
            foreach (MeshCollider mc in go.GetComponents<MeshCollider>())
                if (mc.sharedMesh == mesh) { Undo.RecordObject(mc, undoName); mc.sharedMesh = saved; EditorUtility.SetDirty(mc); }
            Debug.Log(LogPrefix + "메시를 에셋으로 저장했습니다: " + path, saved);
        }

        /// <summary>
        /// 메시 사본을 .asset으로 저장한다. 같은 경로에 메시 에셋이 있으면 GUID를 유지한 채 내용을 덮어쓴다.
        /// 덮어쓰기 확인은 호출하는 쪽에서 한다. 실패하면 null.
        /// </summary>
        internal static Mesh SaveMeshAsAsset(Mesh source, string assetPath)
        {
            if (source == null || string.IsNullOrEmpty(assetPath)) return null;

            Object existingMain = AssetDatabase.LoadMainAssetAtPath(assetPath);
            Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existingMain != null && existingMesh == null)
            {
                TelleRGUI.Info("메시 저장 실패", $"'{assetPath}'에 메시가 아닌 에셋이 이미 있습니다.\n다른 이름으로 저장하세요.");
                return null;
            }

            Mesh copy = Object.Instantiate(source);
            copy.name = Path.GetFileNameWithoutExtension(assetPath);

            if (existingMesh != null)
            {
                Undo.RecordObject(existingMesh, "Overwrite Mesh Asset");
                EditorUtility.CopySerialized(copy, existingMesh);
                existingMesh.name = copy.name;
                Object.DestroyImmediate(copy);
                EditorUtility.SetDirty(existingMesh);
                AssetDatabase.SaveAssets();
                return existingMesh;
            }

            AssetDatabase.CreateAsset(copy, assetPath);
            AssetDatabase.SaveAssets();
            return copy;
        }

        private static void CreateFBXBackup(Mesh mesh, GameObject go, bool isSkinned)
        {
            bool replaceMesh = false;
            if (isSkinned)
            {
                // SMR은 스켈레톤이 FBX에 포함된다는 보장이 없고 본 순서·bindpose 정합성을 검증할 수 없어
                // 참조 교체 없이 백업 파일만 생성한다 (교체 시 캐릭터 렌더링이 파손될 수 있음).
                if (!TelleRGUI.Confirm("FBX 백업", "FBX 백업 파일을 생성합니다.\n\n스킨드 메시는 캐릭터 파손을 막기 위해 메시 참조를 교체하지 않고 파일만 만듭니다.", "생성", "취소"))
                    return;
            }
            else
            {
                int choice = EditorUtility.DisplayDialogComplex("FBX 백업",
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
                    Debug.Log(LogPrefix + "FBX 백업을 생성했습니다(참조 교체 없음): " + fbxPath);
                }
                else
                {
                    // 계층에 메시가 여러 개일 수 있으므로 반드시 이름으로 매칭 — 첫 메시를 무조건 쓰면
                    // 엉뚱한 자식 메시로 교체될 수 있음. FBX 임포트 메시 이름은 노드(GameObject) 이름을 따르는 경우가 많다.
                    Mesh fbxMesh = FindMeshInAsset(fbxPath, mesh.name, go.name);

                    if (fbxMesh != null)
                    {
                        MeshFilter mf = go.GetComponent<MeshFilter>();
                        if (mf != null)
                        {
                            Undo.RecordObject(mf, "Replace Mesh with FBX");
                            mf.sharedMesh = fbxMesh;
                            EditorUtility.SetDirty(mf);
                        }

                        Debug.Log(LogPrefix + "FBX 백업을 생성하고 메시 참조를 교체했습니다: " + fbxPath);
                    }
                    else
                    {
                        Debug.LogWarning(LogPrefix + $"FBX는 생성했지만 '{mesh.name}' 또는 '{go.name}'과 일치하는 메시를 찾지 못해 참조 교체를 생략했습니다: {fbxPath}");
                    }
                }
            }
            else
            {
                Debug.LogError(LogPrefix + "FBX 내보내기에 실패했습니다. FBX Exporter 패키지(" + FbxPackageId + ")가 설치되어 있는지 확인하세요.");
            }
        }

        private static bool ExportToFBX(GameObject go, string path)
        {
            // FBX Exporter는 선택 패키지이므로 컴파일 타임 참조 없이 리플렉션으로만 접근한다
            System.Type exporterType = System.Type.GetType("UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor");

            if (exporterType == null)
            {
                TelleRGUI.Info("FBX Exporter 필요",
                    "FBX Exporter 패키지(" + FbxPackageId + ")를 찾을 수 없습니다.\n\nWindow > Package Manager에서 'FBX Exporter'를 설치하세요.");
                return false;
            }

            MethodInfo exportMethod = exporterType.GetMethod("ExportObject",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new System.Type[] { typeof(string), typeof(UnityEngine.Object) },
                null);

            if (exportMethod != null)
            {
                object result = exportMethod.Invoke(null, new object[] { path, go });
                return result != null && !string.IsNullOrEmpty(result.ToString());
            }

            MethodInfo[] methods = exporterType.GetMethods(BindingFlags.Public | BindingFlags.Static);
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
            return false;
        }

        private static Mesh FindMeshInAsset(string assetPath, string meshName, string gameObjectName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            Mesh firstMesh = null;
            Mesh byObjectName = null;
            int meshCount = 0;
            foreach (Object asset in assets)
            {
                if (asset is Mesh mesh)
                {
                    if (mesh.name == meshName) return mesh;
                    if (byObjectName == null && mesh.name == gameObjectName) byObjectName = mesh;
                    if (firstMesh == null) firstMesh = mesh;
                    meshCount++;
                }
            }
            if (byObjectName != null) return byObjectName;
            // 이름 매칭 실패 시 메시가 정확히 1개일 때만 그 메시를 신뢰
            return meshCount == 1 ? firstMesh : null;
        }

        internal static string SanitizeFileName(string name)
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
