using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace TelleR
{
    /// <summary>
    /// 로우폴리 풀잎 메쉬(Triangle / Quad / QuadCross / TriCross)를 만들고 .asset으로 저장한다.
    /// 피벗은 풀잎 뿌리(바닥 중앙), +Y가 위쪽이다. 면은 한쪽만 있으므로 양면(Cull Off) 셰이더와 함께 쓴다.
    /// </summary>
    public static class GrassBladeMeshGenerator
    {
        public enum BladeShape
        {
            Triangle,
            Quad,
            QuadCross,
            TriCross
        }

        public const string DefaultFolder = "Assets/TelleR/Generated/GrassBlades";
        internal const string LogPrefix = "[TelleR/GrassBladeMeshGenerator] ";

        public const float DefaultWidth = 0.05f;
        public const float DefaultHeight = 0.3f;
        public const float DefaultTipTaper = 0.3f;
        public const float DefaultCurve = 0.1f;

        /// <summary>폭·높이 허용 범위(미터). 0 이하·NaN이면 면적이 없는 메쉬가 되므로 보정한다.</summary>
        public const float MinSize = 0.001f;
        public const float MaxSize = 100f;
        /// <summary>끝 기울기 허용 범위(높이 대비 비율).</summary>
        public const float MaxCurve = 1f;

        public static int VertexCount(BladeShape shape)
        {
            switch (shape)
            {
                case BladeShape.Triangle: return 3;
                case BladeShape.Quad: return 4;
                case BladeShape.QuadCross: return 8;
                case BladeShape.TriCross: return 12;
                default: return 0;
            }
        }

        public static int TriangleCount(BladeShape shape)
        {
            switch (shape)
            {
                case BladeShape.Triangle: return 1;
                case BladeShape.Quad: return 2;
                case BladeShape.QuadCross: return 4;
                case BladeShape.TriCross: return 6;
                default: return 0;
            }
        }

        /// <summary>입력값을 생성 가능한 범위로 보정한다. NaN·무한대는 기본값으로 바꾼다.</summary>
        public static void Sanitize(ref float width, ref float height, ref float tipTaper, ref float curve)
        {
            width = Mathf.Clamp(Finite(width, DefaultWidth), MinSize, MaxSize);
            height = Mathf.Clamp(Finite(height, DefaultHeight), MinSize, MaxSize);
            tipTaper = Mathf.Clamp01(Finite(tipTaper, DefaultTipTaper));
            curve = Mathf.Clamp(Finite(curve, DefaultCurve), -MaxCurve, MaxCurve);
        }

        private static float Finite(float value, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        /// <summary>메모리에만 있는 메쉬를 만든다. 저장하지 않으면 호출한 쪽이 파괴해야 한다.</summary>
        public static Mesh Generate(BladeShape shape, float width, float height, float tipTaper, float curve)
        {
            Sanitize(ref width, ref height, ref tipTaper, ref curve);

            int vertexCount = VertexCount(shape);
            if (vertexCount == 0)
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "알 수 없는 풀잎 모양입니다.");

            var verts = new List<Vector3>(vertexCount);
            var normals = new List<Vector3>(vertexCount);
            var uvs = new List<Vector2>(vertexCount);
            var tris = new List<int>(TriangleCount(shape) * 3);

            switch (shape)
            {
                case BladeShape.Triangle:
                    AddTriangle(verts, normals, uvs, tris, 0f, width, height, curve);
                    break;
                case BladeShape.Quad:
                    AddQuad(verts, normals, uvs, tris, 0f, width, height, tipTaper, curve);
                    break;
                case BladeShape.QuadCross:
                    AddQuad(verts, normals, uvs, tris, 0f, width, height, tipTaper, curve);
                    AddQuad(verts, normals, uvs, tris, 90f, width, height, tipTaper, curve);
                    break;
                case BladeShape.TriCross:
                    AddQuad(verts, normals, uvs, tris, 0f, width, height, tipTaper, curve);
                    AddQuad(verts, normals, uvs, tris, 60f, width, height, tipTaper, curve);
                    AddQuad(verts, normals, uvs, tris, 120f, width, height, tipTaper, curve);
                    break;
            }

            Mesh mesh = new Mesh
            {
                name = $"GrassBlade_{shape}",
                indexFormat = IndexFormat.UInt16
            };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }

        // 삼각형 감김은 Unity 앞면 규칙(앞에서 볼 때 시계 방향)에 맞춰, 앞면이 저장된 법선(+Z) 쪽을 향하게 한다.
        private static void AddQuad(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris,
            float yawDegrees, float width, float height, float tipTaper, float curve)
        {
            int baseIndex = verts.Count;
            Quaternion rot = Quaternion.Euler(0f, yawDegrees, 0f);

            float halfBase = width * 0.5f;
            float halfTip = halfBase * tipTaper;
            float curveZ = curve * height;

            verts.Add(rot * new Vector3(-halfBase, 0f, 0f));
            verts.Add(rot * new Vector3(halfBase, 0f, 0f));
            verts.Add(rot * new Vector3(halfTip, height, curveZ));
            verts.Add(rot * new Vector3(-halfTip, height, curveZ));

            Vector3 n = rot * Vector3.forward;
            normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));

            tris.Add(baseIndex + 0); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
            tris.Add(baseIndex + 0); tris.Add(baseIndex + 2); tris.Add(baseIndex + 3);
        }

        private static void AddTriangle(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris,
            float yawDegrees, float width, float height, float curve)
        {
            int baseIndex = verts.Count;
            Quaternion rot = Quaternion.Euler(0f, yawDegrees, 0f);

            float halfBase = width * 0.5f;
            float curveZ = curve * height;

            verts.Add(rot * new Vector3(-halfBase, 0f, 0f));
            verts.Add(rot * new Vector3(halfBase, 0f, 0f));
            verts.Add(rot * new Vector3(0f, height, curveZ));

            Vector3 n = rot * Vector3.forward;
            normals.Add(n); normals.Add(n); normals.Add(n);

            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(0.5f, 1f));

            tris.Add(baseIndex + 0); tris.Add(baseIndex + 1); tris.Add(baseIndex + 2);
        }

        /// <summary>
        /// 기본 폴더에 GrassBlade_&lt;Shape&gt;.asset으로 저장한다. 같은 이름이 있으면 덮어쓰지 않고 새 이름(… 1.asset)으로 저장한다.
        /// </summary>
        public static Mesh GenerateAndSave(BladeShape shape, float width, float height, float tipTaper, float curve)
            => GenerateAndSave(shape, width, height, tipTaper, curve, DefaultAssetPath(shape), false);

        /// <summary>
        /// assetPath(Assets/…/이름.asset)에 저장한다. overwriteExisting이 true이고 그 경로의 메인 에셋이 Mesh면
        /// 파일(GUID)은 유지한 채 내용만 바꾼다(Ctrl+Z로 되돌릴 수 있음). 그 외에는 겹치지 않는 새 이름으로 저장한다.
        /// </summary>
        public static Mesh GenerateAndSave(BladeShape shape, float width, float height, float tipTaper, float curve,
            string assetPath, bool overwriteExisting)
        {
            string path = ValidateAssetPath(assetPath, out string error);
            if (path == null) throw new ArgumentException(error, nameof(assetPath));

            Mesh generated = Generate(shape, width, height, tipTaper, curve);
            try
            {
                EnsureFolder(path.Substring(0, path.LastIndexOf('/')));

                Mesh existing = overwriteExisting ? AssetDatabase.LoadMainAssetAtPath(path) as Mesh : null;
                if (existing != null)
                {
                    Undo.RegisterCompleteObjectUndo(existing, "Overwrite Grass Blade Mesh");
                    generated.name = existing.name;
                    EditorUtility.CopySerialized(generated, existing);
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssetIfDirty(existing);
                    Debug.Log($"{LogPrefix}기존 메쉬 내용을 교체했습니다(GUID 유지): {path}", existing);
                    return existing;
                }

                string unique = AssetDatabase.GenerateUniqueAssetPath(path);
                if (string.IsNullOrEmpty(unique))
                    throw new InvalidOperationException($"저장 경로를 만들 수 없습니다: {path}");
                generated.name = Path.GetFileNameWithoutExtension(unique);
                AssetDatabase.CreateAsset(generated, unique);
                Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(unique);
                if (saved == null)
                    throw new InvalidOperationException($"메쉬 에셋을 저장하지 못했습니다: {unique}");
                Debug.Log($"{LogPrefix}풀잎 메쉬를 저장했습니다: {unique}", saved);
                return saved;
            }
            finally
            {
                // 에셋이 되지 못한(덮어쓰기 원본으로만 쓰였거나 저장에 실패한) 임시 메쉬는 메모리에 남기지 않는다.
                if (generated != null && !EditorUtility.IsPersistent(generated))
                    UnityEngine.Object.DestroyImmediate(generated);
            }
        }

        public static string DefaultAssetPath(BladeShape shape) => $"{DefaultFolder}/GrassBlade_{shape}.asset";

        /// <summary>
        /// Assets 아래의 .asset 경로로 정규화한다. 확장자가 없으면 붙인다. 쓸 수 없는 경로면 null과 이유를 돌려준다.
        /// </summary>
        public static string ValidateAssetPath(string assetPath, out string error)
        {
            error = null;
            string path = (assetPath ?? string.Empty).Trim().Replace('\\', '/');
            while (path.Contains("//")) path = path.Replace("//", "/");

            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "저장 경로는 Assets 폴더 아래여야 합니다.";
                return null;
            }
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) path += ".asset";

            string[] parts = path.Split('/');
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 1; i < parts.Length; i++)
            {
                string part = parts[i];
                string trimmed = part.Trim();
                if (trimmed.Length == 0 || trimmed == "." || trimmed == ".." || part != trimmed ||
                    part.IndexOfAny(invalid) >= 0 || part.EndsWith(".", StringComparison.Ordinal))
                {
                    error = $"경로에 쓸 수 없는 이름이 있습니다: '{part}'";
                    return null;
                }
            }
            if (Path.GetFileNameWithoutExtension(path).Trim().Length == 0)
            {
                error = "파일 이름이 비어 있습니다.";
                return null;
            }
            return path;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                    // 같은 이름의 파일이 있으면 Unity가 다른 이름으로 폴더를 만들므로, 원하는 경로가 생겼는지 확인한다.
                    if (!AssetDatabase.IsValidFolder(next))
                        throw new InvalidOperationException($"폴더를 만들 수 없습니다(같은 이름의 파일이 있는지 확인하세요): {next}");
                }
                current = next;
            }
        }
    }

    public sealed class GrassBladeMeshGeneratorWindow : EditorWindow
    {
        private const string WindowTitle = "Grass Blade Mesh Generator";

        [SerializeField] private GrassBladeMeshGenerator.BladeShape _shape = GrassBladeMeshGenerator.BladeShape.TriCross;
        [SerializeField] private float _width = GrassBladeMeshGenerator.DefaultWidth;
        [SerializeField] private float _height = GrassBladeMeshGenerator.DefaultHeight;
        [SerializeField] private float _tipTaper = GrassBladeMeshGenerator.DefaultTipTaper;
        [SerializeField] private float _curve = GrassBladeMeshGenerator.DefaultCurve;
        [SerializeField] private string _folder = GrassBladeMeshGenerator.DefaultFolder;
        [SerializeField] private string _fileName = string.Empty;
        [SerializeField] private string _status = string.Empty;

        // 콜백은 직렬화되지 않으므로 도메인 리로드 뒤에는 단독 창으로 동작한다.
        [NonSerialized] private Action<Mesh> _onCreated;

        [NonSerialized] private bool _pathDirty = true;
        [NonSerialized] private string _targetPath;
        [NonSerialized] private string _pathError;
        [NonSerialized] private GUIContent _targetPathContent = new GUIContent();
        private Vector2 _scroll;

        private static readonly GUIContent GcIntro = new GUIContent(
            "로우폴리 풀잎 메쉬를 만들어 .asset으로 저장합니다. 피벗은 풀잎 뿌리(바닥 중앙), +Y가 위쪽입니다. " +
            "면이 한쪽뿐이므로 양면 렌더링(Cull Off) 셰이더와 함께 쓰세요.");
        private static readonly GUIContent GcShape = new GUIContent("Shape",
            "Triangle: 삼각형 1장 / Quad: 사각형 1장 / QuadCross: 사각형 2장을 90° 교차 / TriCross: 사각형 3장을 60° 간격으로 교차");
        private static readonly GUIContent GcWidth = new GUIContent("Width (m)", "풀잎 뿌리 쪽 폭(미터)");
        private static readonly GUIContent GcHeight = new GUIContent("Height (m)", "풀잎 높이(미터)");
        private static readonly GUIContent GcTaper = new GUIContent("Tip Taper",
            "끝 폭 ÷ 뿌리 폭. 0이면 끝이 뾰족하고 1이면 직사각형입니다. Triangle은 항상 뾰족해서 쓰지 않습니다.");
        private static readonly GUIContent GcCurve = new GUIContent("Curve (Z lean)",
            "끝부분을 각 면의 앞(+Z) 방향으로 기울이는 정도(높이 대비 비율). 음수면 반대쪽으로 기울어집니다. " +
            "정점이 뿌리·끝 두 줄뿐이라 휘어지지 않고 곧게 기울어집니다.");
        private static readonly GUIContent GcFolder = new GUIContent("Folder", "저장할 폴더. Assets 아래여야 하며 없으면 만듭니다.");
        private static readonly GUIContent GcBrowse = new GUIContent("...", "폴더 선택");
        private static readonly GUIContent GcName = new GUIContent("File Name", "파일 이름(.asset 제외). 비워 두면 GrassBlade_<Shape>");
        private static readonly GUIContent GcReset = new GUIContent("Reset", "모양·크기 값을 기본값으로 되돌립니다.");
        private static readonly GUIContent GcCreate = new GUIContent("Create", "메쉬를 만들어 저장하고 Project 창에서 선택합니다.");
        private static readonly GUIContent GcCancel = new GUIContent("Cancel");

        private static readonly string[] StatsText = BuildStatsText();

        private static string[] BuildStatsText()
        {
            var values = (GrassBladeMeshGenerator.BladeShape[])Enum.GetValues(typeof(GrassBladeMeshGenerator.BladeShape));
            var result = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
                result[i] = $"풀잎 1개당 정점 {GrassBladeMeshGenerator.VertexCount(values[i])}개 · 삼각형 {GrassBladeMeshGenerator.TriangleCount(values[i])}개";
            return result;
        }

        /// <summary>창을 연다. onCreated가 있으면 생성 직후 호출하고 창을 닫는다.</summary>
        public static void Open(Action<Mesh> onCreated)
        {
            var win = GetWindow<GrassBladeMeshGeneratorWindow>(false, WindowTitle, true);
            win._onCreated = onCreated;
            win.minSize = new Vector2(300f, 330f);
            win.Show();
        }

        [MenuItem("Tools/TelleR/Grass Blade Mesh Generator", false, 102)]
        private static void OpenStandalone()
        {
            Open(null);
        }

        private void OnEnable()
        {
            _pathDirty = true;
        }

        private void OnGUI()
        {
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Clamp(position.width * 0.38f, 90f, 150f);

            bool createClicked = false;
            bool cancelClicked = false;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField(GcIntro, TelleRGUI.Hint);

            TelleRGUI.Section("Blade");
            EditorGUI.BeginChangeCheck();
            _shape = (GrassBladeMeshGenerator.BladeShape)EditorGUILayout.EnumPopup(GcShape, _shape);
            if (EditorGUI.EndChangeCheck()) _pathDirty = true;

            _width = EditorGUILayout.Slider(GcWidth, _width, 0.005f, 0.5f);
            _height = EditorGUILayout.Slider(GcHeight, _height, 0.05f, 2f);
            using (new EditorGUI.DisabledScope(_shape == GrassBladeMeshGenerator.BladeShape.Triangle))
                _tipTaper = EditorGUILayout.Slider(GcTaper, _tipTaper, 0f, 1f);
            _curve = EditorGUILayout.Slider(GcCurve, _curve, -0.5f, 0.5f);

            int shapeIndex = (int)_shape;
            if (shapeIndex >= 0 && shapeIndex < StatsText.Length)
                EditorGUILayout.LabelField(StatsText[shapeIndex], TelleRGUI.Hint);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(GcReset, EditorStyles.miniButton, GUILayout.Width(60f))) ResetBlade();
            }

            TelleRGUI.Section("Output");
            bool browseClicked;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                _folder = EditorGUILayout.TextField(GcFolder, _folder);
                browseClicked = GUILayout.Button(GcBrowse, EditorStyles.miniButton, GUILayout.Width(28f));
            }
            _fileName = EditorGUILayout.TextField(GcName, _fileName);
            if (EditorGUI.EndChangeCheck()) _pathDirty = true;

            RefreshTargetPath();
            if (_pathError != null)
                EditorGUILayout.HelpBox(_pathError, MessageType.Warning);
            else
                EditorGUILayout.LabelField(_targetPathContent, TelleRGUI.Hint);

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField(_status, TelleRGUI.Hint);
            }
            EditorGUILayout.EndScrollView();

            TelleRGUI.DrawSeparator(1f, 2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (_onCreated != null && GUILayout.Button(GcCancel, GUILayout.Height(26f))) cancelClicked = true;
                using (new EditorGUI.DisabledScope(_pathError != null))
                {
                    Color prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = TelleRGUI.SuccessButton;
                    if (GUILayout.Button(GcCreate, GUILayout.Height(26f))) createClicked = true;
                    GUI.backgroundColor = prevBg;
                }
            }
            EditorGUILayout.Space(4f);

            EditorGUIUtility.labelWidth = prevLabelWidth;

            // 대화상자·창 닫기는 레이아웃 그룹을 모두 닫은 뒤에 처리해 GUILayout 불일치 오류를 피한다.
            if (browseClicked) BrowseFolder();
            else if (cancelClicked) Close();
            else if (createClicked) CreateFromWindow();
        }

        private void ResetBlade()
        {
            // 기본 파일 이름이 Shape를 따르므로 저장 경로도 다시 계산한다.
            _shape = GrassBladeMeshGenerator.BladeShape.TriCross;
            _pathDirty = true;
            _width = GrassBladeMeshGenerator.DefaultWidth;
            _height = GrassBladeMeshGenerator.DefaultHeight;
            _tipTaper = GrassBladeMeshGenerator.DefaultTipTaper;
            _curve = GrassBladeMeshGenerator.DefaultCurve;
            GUI.FocusControl(null);
        }

        private void RefreshTargetPath()
        {
            if (!_pathDirty) return;
            _pathDirty = false;

            string name = (_fileName ?? string.Empty).Trim();
            if (name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 6);
            if (name.Length == 0) name = $"GrassBlade_{_shape}";
            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
            {
                _targetPath = null;
                _pathError = "파일 이름에는 / 나 \\ 를 쓸 수 없습니다. 폴더는 Folder 칸에 적으세요.";
                return;
            }

            string folder = (_folder ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
            _targetPath = GrassBladeMeshGenerator.ValidateAssetPath(folder + "/" + name + ".asset", out _pathError);
            _targetPathContent.text = _targetPath == null ? string.Empty : "저장 위치: " + _targetPath;
            _targetPathContent.tooltip = _targetPathContent.text;
        }

        private void BrowseFolder()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/') ?? string.Empty;
            string start = AssetDatabase.IsValidFolder(_folder) ? _folder : "Assets";
            string picked = EditorUtility.OpenFolderPanel("저장 폴더 선택", Path.Combine(projectRoot, start), string.Empty);
            if (!string.IsNullOrEmpty(picked))
            {
                picked = picked.Replace('\\', '/').TrimEnd('/');
                string assets = Application.dataPath.Replace('\\', '/');
                if (string.Equals(picked, assets, StringComparison.OrdinalIgnoreCase))
                    _folder = "Assets";
                else if (picked.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase))
                    _folder = "Assets" + picked.Substring(assets.Length);
                else
                    TelleRGUI.Info("폴더 선택", "이 프로젝트의 Assets 폴더 안에서 골라 주세요.");
                _pathDirty = true;
                GUI.FocusControl(null);
            }
            GUIUtility.ExitGUI();
        }

        private void CreateFromWindow()
        {
            RefreshTargetPath();
            if (_targetPath == null) return;

            bool overwrite = false;
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(_targetPath);
            if (existing is Mesh)
            {
                int choice = EditorUtility.DisplayDialogComplex("풀잎 메쉬 덮어쓰기",
                    $"같은 이름의 메쉬가 이미 있습니다.\n{_targetPath}\n\n" +
                    "덮어쓰기: 파일(GUID)은 그대로 두고 내용만 바꿉니다. 이 메쉬를 쓰는 씬·프리팹이 모두 새 모양으로 바뀌며, Ctrl+Z로 되돌릴 수 있습니다.\n" +
                    "새 이름으로 저장: 기존 파일은 건드리지 않고 번호를 붙여 저장합니다.",
                    "덮어쓰기", "취소", "새 이름으로 저장");
                if (choice == 1) return;
                overwrite = choice == 0;
            }

            Mesh mesh;
            try
            {
                mesh = GrassBladeMeshGenerator.GenerateAndSave(_shape, _width, _height, _tipTaper, _curve, _targetPath, overwrite);
            }
            catch (Exception e)
            {
                Debug.LogError($"{GrassBladeMeshGenerator.LogPrefix}메쉬를 저장하지 못했습니다: {e.Message}");
                TelleRGUI.Info("풀잎 메쉬 생성 실패", e.Message);
                return;
            }

            string savedPath = AssetDatabase.GetAssetPath(mesh);
            _status = (overwrite ? "덮어씀: " : "저장함: ") + savedPath;
            _pathDirty = true;

            Action<Mesh> callback = _onCreated;
            if (callback != null)
            {
                try { callback(mesh); }
                catch (Exception e) { Debug.LogException(e); }
            }

            Selection.activeObject = mesh;
            EditorGUIUtility.PingObject(mesh);
            if (callback != null) Close();
        }
    }
}
