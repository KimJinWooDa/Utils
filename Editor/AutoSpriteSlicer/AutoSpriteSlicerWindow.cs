#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TelleR
{
    public class AutoSpriteSlicerWindow : EditorWindow
    {
        private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".psd", ".gif", ".hdr", ".exr", ".tif", ".tiff" };

        private enum EncodeFormat { None, Png, Jpg, Tga, Exr }

        private List<string> dropPaths = new List<string>();
        private Vector2 scroll;

        private int _alphaThreshold = 1;
        private int _padding = 0;
        private bool _autoDetectKeys = true;
        private Color _keyColor1 = Color.white;
        private Color _keyColor2 = new Color(0.8f, 0.8f, 0.8f, 1f);
        private float _colorTolerance = 0.08f;
        private int _jpgQuality = 95;

        private string _previewPath;
        private Texture2D _previewSource;
        private Texture2D _previewResult;
        private RectInt _previewBounds;
        private int _previewMaxEdgeAlpha;
        private string _lastReport = string.Empty;

        [MenuItem("Tools/TelleR/Tool/Auto Sprite Slicer")]
        private static void Open()
        {
            var win = GetWindow<AutoSpriteSlicerWindow>(false, "Auto Sprite Slicer");
            win.minSize = new Vector2(460, 660);
        }

        private void OnDisable() => ClearPreview();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Auto Sprite Slicer + Border Trim/Remove", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("PNG/JPG/TGA/EXR: 배경 컬러 자동 제거 → 가장자리 트림(원본 덮어쓰기) → Sprite(Single) 임포트.\nBMP/PSD/GIF/HDR/TIF: 임포터 설정만 적용 (재인코딩 불가).", MessageType.Info);

            DrawTrimSettings();
            EditorGUILayout.Space(6f);
            DrawDropAndList();
            EditorGUILayout.Space(6f);
            DrawPreview();
            EditorGUILayout.Space(6f);
            DrawActions();

            if (!string.IsNullOrEmpty(_lastReport))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(_lastReport, MessageType.Info);
            }
        }

        private void DrawTrimSettings()
        {
            EditorGUILayout.LabelField("Trim / Background Removal", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUI.BeginChangeCheck();
                _alphaThreshold = EditorGUILayout.IntSlider(new GUIContent("Alpha Threshold", "이 값 이상의 알파를 '불투명'으로 간주"), _alphaThreshold, 0, 255);
                _padding = EditorGUILayout.IntSlider(new GUIContent("Padding (px)", "잘라낸 영역 주위에 추가 여백"), _padding, 0, 64);
                _autoDetectKeys = EditorGUILayout.Toggle(new GUIContent("Auto Detect Key Colors From Corners", "네 모서리 픽셀 색을 키 색상으로 자동 사용"), _autoDetectKeys);
                using (new EditorGUI.DisabledScope(_autoDetectKeys))
                {
                    _keyColor1 = EditorGUILayout.ColorField(new GUIContent("Key Color 1"), _keyColor1);
                    _keyColor2 = EditorGUILayout.ColorField(new GUIContent("Key Color 2"), _keyColor2);
                }
                _colorTolerance = EditorGUILayout.Slider(new GUIContent("Color Tolerance", "키 색상과의 허용 색차 (0~1)"), _colorTolerance, 0f, 0.5f);
                _jpgQuality = EditorGUILayout.IntSlider(new GUIContent("JPG Quality", "JPG 재인코딩 품질"), _jpgQuality, 50, 100);
                if (EditorGUI.EndChangeCheck()) BuildPreview();
            }
        }

        private void DrawDropAndList()
        {
            var dropRect = GUILayoutUtility.GetRect(0, 64, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "여기에 이미지(또는 폴더)를 드래그\n현재: " + dropPaths.Count + "개", EditorStyles.helpBox);
            HandleDragAndDrop(dropRect);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selected Images/Folders")) AddPathsFromObjects(Selection.objects, true);
                if (GUILayout.Button("Clear List")) { dropPaths.Clear(); ClearPreview(); }
            }

            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField("대상 목록", EditorStyles.boldLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(140));
                for (int i = 0; i < dropPaths.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(dropPaths[i], GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("X", GUILayout.Width(28)))
                        {
                            dropPaths.RemoveAt(i);
                            i--;
                            BuildPreview();
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPreview()
        {
            EditorGUILayout.LabelField("Preview (first trimmable target)", EditorStyles.boldLabel);
            if (_previewSource == null)
            {
                EditorGUILayout.HelpBox("미리볼 수 있는 타겟이 없습니다.", MessageType.None);
                return;
            }

            string info = $"Source: {_previewSource.width} x {_previewSource.height}";
            if (_previewBounds.width > 0)
            {
                int srcArea = Mathf.Max(1, _previewSource.width * _previewSource.height);
                int savedPct = Mathf.RoundToInt((1f - (float)(_previewBounds.width * _previewBounds.height) / srcArea) * 100f);
                info += $"   →   Result: {_previewBounds.width} x {_previewBounds.height}   (saved {savedPct}%)";
            }
            EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"Max edge alpha (after bg removal): {_previewMaxEdgeAlpha}    (threshold: {_alphaThreshold})", EditorStyles.miniLabel);

            Rect r = GUILayoutUtility.GetRect(0, 200f, GUILayout.ExpandWidth(true));
            DrawTextureFitted(new Rect(r.x, r.y, r.width * 0.5f - 4f, r.height), _previewSource, "Before");
            DrawTextureFitted(new Rect(r.x + r.width * 0.5f + 4f, r.y, r.width * 0.5f - 4f, r.height), _previewResult, "After");
        }

        private static void DrawTextureFitted(Rect rect, Texture2D tex, string label)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f));
            GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, rect.width, 16f), label, EditorStyles.miniLabel);
            if (tex == null || tex.width <= 0 || tex.height <= 0) return;
            Rect inner = new Rect(rect.x + 4f, rect.y + 18f, rect.width - 8f, rect.height - 22f);
            if (inner.width <= 0f || inner.height <= 0f) return;
            float scale = Mathf.Min(inner.width / tex.width, inner.height / tex.height);
            float w = tex.width * scale;
            float h = tex.height * scale;
            Rect draw = new Rect(inner.x + (inner.width - w) * 0.5f, inner.y + (inner.height - h) * 0.5f, w, h);
            GUI.DrawTexture(draw, tex, ScaleMode.StretchToFill, true);
        }

        private void DrawActions()
        {
            using (new EditorGUI.DisabledScope(dropPaths.Count == 0))
            {
                if (GUILayout.Button($"Process {dropPaths.Count} Image(s)  (Trim+Remove+Single Sprite, Overwrite)", GUILayout.Height(32f)))
                {
                    bool ok = EditorUtility.DisplayDialog(
                        "Overwrite Originals?",
                        $"{dropPaths.Count}개 중 인코딩 가능한 포맷(PNG/JPG/TGA/EXR) 원본이 덮어쓰기됩니다.\n나머지는 임포터만 변경됩니다.\n계속하시겠습니까?",
                        "Process", "Cancel");
                    if (ok) Process();
                }
            }
        }

        private void HandleDragAndDrop(Rect r)
        {
            var e = Event.current;
            if (!r.Contains(e.mousePosition)) return;
            if (e.type == EventType.DragUpdated || e.type == EventType.DragPerform)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (e.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    AddPathsFromObjects(DragAndDrop.objectReferences, true);
                }
                e.Use();
            }
        }

        private void AddPathsFromObjects(UnityEngine.Object[] objs, bool includeFolders)
        {
            var newPaths = new List<string>();
            foreach (var obj in objs)
            {
                if (obj == null) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (includeFolders && Directory.Exists(path))
                {
                    var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { path });
                    foreach (var guid in guids)
                    {
                        var p = AssetDatabase.GUIDToAssetPath(guid);
                        if (IsSupportedImage(p)) newPaths.Add(p);
                    }
                }
                else if (IsSupportedImage(path)) newPaths.Add(path);
            }

            bool added = false;
            foreach (var p in newPaths)
            {
                if (!dropPaths.Contains(p)) { dropPaths.Add(p); added = true; }
            }
            if (added) BuildPreview();
        }

        private static bool IsSupportedImage(string path)
        {
            for (int i = 0; i < SupportedExtensions.Length; i++)
                if (path.EndsWith(SupportedExtensions[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static EncodeFormat GetEncodeFormat(string path)
        {
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return EncodeFormat.Png;
            if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) return EncodeFormat.Jpg;
            if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) return EncodeFormat.Tga;
            if (path.EndsWith(".exr", StringComparison.OrdinalIgnoreCase)) return EncodeFormat.Exr;
            return EncodeFormat.None;
        }

        private void Process()
        {
            int processed = 0, trimmed = 0, skipped = 0;

            try
            {
                AssetDatabase.StartAssetEditing();

                for (int i = 0; i < dropPaths.Count; i++)
                {
                    string path = dropPaths[i];
                    EditorUtility.DisplayProgressBar("Processing", path, (float)i / dropPaths.Count);

                    if (!IsSupportedImage(path)) { skipped++; continue; }

                    EncodeFormat fmt = GetEncodeFormat(path);
                    if (fmt != EncodeFormat.None)
                    {
                        if (TryTrimAndRemove(path, fmt, (byte)_alphaThreshold, _padding, _autoDetectKeys, _keyColor1, _keyColor2, _colorTolerance, _jpgQuality))
                        {
                            trimmed++;
                            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                        }
                    }

                    if (!ApplySingleSpriteImporter(path)) { skipped++; continue; }
                    processed++;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[AutoSpriteSlicer] " + ex.Message + "\n" + ex.StackTrace);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            _lastReport = $"Imported: {processed} / Trimmed: {trimmed} / Skipped: {skipped}";
            Debug.Log("[AutoSpriteSlicer] " + _lastReport);
            BuildPreview();
        }

        private static bool ApplySingleSpriteImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return false;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = 100f;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            importer.SetTextureSettings(settings);

            importer.SaveAndReimport();
            return true;
        }

        // ───────── Trim + Background Removal ─────────

        private static bool TryTrimAndRemove(string assetPath, EncodeFormat fmt, byte alphaThreshold, int padding,
                                             bool autoDetectKeys, Color keyColor1, Color keyColor2, float tolerance, int jpgQuality)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath)) return false;

            Texture2D tex = LoadReadable(assetPath);
            if (tex == null) return false;

            try
            {
                int w = tex.width, h = tex.height;
                Color32[] px = tex.GetPixels32();

                Color32[] keys = autoDetectKeys ? GetCornerColors32(px, w, h) : new[] { (Color32)keyColor1, (Color32)keyColor2 };
                byte tolByte = (byte)Mathf.Clamp(Mathf.RoundToInt(tolerance * 255f), 0, 255);

                bool modified = RemoveBackgroundFlood(px, w, h, keys, tolByte) > 0;

                if (!FindOpaqueBounds(px, w, h, alphaThreshold, out RectInt bounds))
                {
                    Debug.LogWarning($"[AutoSpriteSlicer] 완전 투명/매칭: {assetPath}");
                    return false;
                }
                bounds = ApplyPadding(bounds, w, h, padding);
                bool boundsChanged = !(bounds.width == w && bounds.height == h);
                if (!modified && !boundsChanged) return false;

                Color32[] outPx;
                int ow, oh;
                if (boundsChanged) { outPx = Crop(px, w, bounds); ow = bounds.width; oh = bounds.height; }
                else { outPx = px; ow = w; oh = h; }

                // JPG cannot store alpha — flatten removed pixels to first key color
                if (fmt == EncodeFormat.Jpg && modified) FlattenAlphaToColor(outPx, keys[0]);

                byte[] bytes = EncodeBytes(outPx, ow, oh, fmt, jpgQuality);
                if (bytes == null || bytes.Length == 0) return false;

                File.WriteAllBytes(assetPath, bytes);
                return true;
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        private static byte[] EncodeBytes(Color32[] px, int w, int h, EncodeFormat fmt, int jpgQuality)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(px);
                tex.Apply(false, false);
                switch (fmt)
                {
                    case EncodeFormat.Png: return tex.EncodeToPNG();
                    case EncodeFormat.Jpg: return tex.EncodeToJPG(jpgQuality);
                    case EncodeFormat.Tga: return tex.EncodeToTGA();
                    case EncodeFormat.Exr: return tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
                    default: return null;
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        private static void FlattenAlphaToColor(Color32[] px, Color32 fill)
        {
            fill.a = 255;
            for (int i = 0; i < px.Length; i++)
            {
                if (px[i].a < 255) px[i] = fill;
            }
        }

        // PNG/JPG: 빠른 파일 바이트 → LoadImage 경로. 그 외: 임포트된 Texture에서 GPU Blit.
        private static Texture2D LoadReadable(string assetPath)
        {
            string ext = Path.GetExtension(assetPath).ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                byte[] bytes = File.ReadAllBytes(assetPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes, false)) return tex;
                UnityEngine.Object.DestroyImmediate(tex);
                return null;
            }

            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (src == null) return null;
            return BlitToReadable(src);
        }

        private static Texture2D BlitToReadable(Texture2D src)
        {
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            dst.Apply(false, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }

        private void BuildPreview()
        {
            ClearPreview();
            string targetPath = null;
            for (int i = 0; i < dropPaths.Count; i++)
            {
                string p = dropPaths[i];
                if (GetEncodeFormat(p) != EncodeFormat.None && File.Exists(p)) { targetPath = p; break; }
            }
            if (targetPath == null) return;

            Texture2D loaded = LoadReadable(targetPath);
            if (loaded == null) return;

            int w = loaded.width, h = loaded.height;
            Color32[] before = loaded.GetPixels32();
            Color32[] work = (Color32[])before.Clone();

            Color32[] keys = _autoDetectKeys ? GetCornerColors32(before, w, h) : new[] { (Color32)_keyColor1, (Color32)_keyColor2 };
            byte tolByte = (byte)Mathf.Clamp(Mathf.RoundToInt(_colorTolerance * 255f), 0, 255);
            RemoveBackgroundFlood(work, w, h, keys, tolByte);

            _previewMaxEdgeAlpha = ComputeMaxEdgeAlpha(work, w, h);
            _previewSource = MakeTexture(before, w, h);

            if (FindOpaqueBounds(work, w, h, (byte)_alphaThreshold, out RectInt bounds))
            {
                bounds = ApplyPadding(bounds, w, h, _padding);
                _previewBounds = bounds;
                _previewResult = MakeTexture(Crop(work, w, bounds), bounds.width, bounds.height);
            }

            _previewPath = targetPath;
            UnityEngine.Object.DestroyImmediate(loaded);
        }

        private static Texture2D MakeTexture(Color32[] px, int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        private void ClearPreview()
        {
            if (_previewSource != null) { UnityEngine.Object.DestroyImmediate(_previewSource); _previewSource = null; }
            if (_previewResult != null) { UnityEngine.Object.DestroyImmediate(_previewResult); _previewResult = null; }
            _previewBounds = default;
            _previewMaxEdgeAlpha = 0;
            _previewPath = null;
        }

        private static Color32[] Crop(Color32[] src, int srcW, RectInt b)
        {
            var dst = new Color32[b.width * b.height];
            for (int y = 0; y < b.height; y++)
            {
                int srcRow = (b.y + y) * srcW + b.x;
                int dstRow = y * b.width;
                Array.Copy(src, srcRow, dst, dstRow, b.width);
            }
            return dst;
        }

        private static RectInt ApplyPadding(RectInt b, int w, int h, int pad)
        {
            if (pad <= 0) return b;
            int x = Mathf.Max(0, b.x - pad);
            int y = Mathf.Max(0, b.y - pad);
            int xMax = Mathf.Min(w, b.x + b.width + pad);
            int yMax = Mathf.Min(h, b.y + b.height + pad);
            return new RectInt(x, y, xMax - x, yMax - y);
        }

        private static int ComputeMaxEdgeAlpha(Color32[] px, int w, int h)
        {
            int max = 0;
            for (int x = 0; x < w; x++)
            {
                if (px[x].a > max) max = px[x].a;
                if (h > 1 && px[(h - 1) * w + x].a > max) max = px[(h - 1) * w + x].a;
            }
            for (int y = 1; y < h - 1; y++)
            {
                if (px[y * w].a > max) max = px[y * w].a;
                if (w > 1 && px[y * w + (w - 1)].a > max) max = px[y * w + (w - 1)].a;
            }
            return max;
        }

        // 가장자리부터 안쪽으로 스캔, 각 변에서 첫 불투명 발견 시 조기 종료.
        private static bool FindOpaqueBounds(Color32[] px, int w, int h, byte alphaThreshold, out RectInt bounds)
        {
            int minY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                    if (px[row + x].a >= alphaThreshold) { minY = y; goto FoundTop; }
            }
            FoundTop:
            if (minY < 0) { bounds = default; return false; }

            int maxY = minY;
            for (int y = h - 1; y > minY; y--)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                    if (px[row + x].a >= alphaThreshold) { maxY = y; goto FoundBottom; }
            }
            FoundBottom:

            int minX = 0;
            for (int x = 0; x < w; x++)
            {
                for (int y = minY; y <= maxY; y++)
                    if (px[y * w + x].a >= alphaThreshold) { minX = x; goto FoundLeft; }
            }
            FoundLeft:

            int maxX = minX;
            for (int x = w - 1; x > minX; x--)
            {
                for (int y = minY; y <= maxY; y++)
                    if (px[y * w + x].a >= alphaThreshold) { maxX = x; goto FoundRight; }
            }
            FoundRight:

            bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return true;
        }

        private static Color32[] GetCornerColors32(Color32[] px, int w, int h)
        {
            Color32[] corners = { px[0], px[w - 1], px[(h - 1) * w], px[(h - 1) * w + (w - 1)] };
            var unique = new List<Color32>(4);
            for (int i = 0; i < corners.Length; i++)
            {
                Color32 c = corners[i];
                bool dup = false;
                for (int j = 0; j < unique.Count; j++)
                {
                    if (ChebyshevByte(unique[j], c) <= 5) { dup = true; break; }
                }
                if (!dup) unique.Add(c);
            }
            return unique.ToArray();
        }

        private static int ChebyshevByte(Color32 a, Color32 b)
        {
            int dr = a.r - b.r; if (dr < 0) dr = -dr;
            int dg = a.g - b.g; if (dg < 0) dg = -dg;
            int db = a.b - b.b; if (db < 0) db = -db;
            int m = dr > dg ? dr : dg;
            return m > db ? m : db;
        }

        // 스캔라인 flood fill: 가장자리 시드 → 좌우 런 확장 → 위/아래 행 enqueue. BFS 대비 큐 push 횟수 대폭 감소.
        private static int RemoveBackgroundFlood(Color32[] px, int w, int h, Color32[] keys, byte tolByte)
        {
            if (keys == null || keys.Length == 0) return 0;
            bool[] visited = new bool[w * h];
            var stack = new Stack<int>(1024);

            for (int x = 0; x < w; x++) { SeedScanline(x, 0, w, px, visited, stack, keys, tolByte); SeedScanline(x, h - 1, w, px, visited, stack, keys, tolByte); }
            for (int y = 1; y < h - 1; y++) { SeedScanline(0, y, w, px, visited, stack, keys, tolByte); SeedScanline(w - 1, y, w, px, visited, stack, keys, tolByte); }

            int removed = 0;
            while (stack.Count > 0)
            {
                int seed = stack.Pop();
                int sy = seed / w;
                int sx = seed % w;
                int rowStart = sy * w;

                // 시드 자체가 다른 경로로 처리되었으면 스킵
                if (px[seed].a == 0 && visited[seed] == false) { /* 정상 시드 */ }
                int left = sx;
                while (left > 0 && !visited[rowStart + left - 1] && MatchKey32(px[rowStart + left - 1], keys, tolByte)) left--;
                int right = sx;
                while (right < w - 1 && !visited[rowStart + right + 1] && MatchKey32(px[rowStart + right + 1], keys, tolByte)) right++;

                for (int x = left; x <= right; x++)
                {
                    int idx = rowStart + x;
                    if (visited[idx]) continue;
                    visited[idx] = true;
                    Color32 c = px[idx];
                    if (c.a != 0) { c.a = 0; px[idx] = c; }
                    removed++;
                }

                AddScanlineSeeds(sy - 1, left, right, w, h, px, visited, stack, keys, tolByte);
                AddScanlineSeeds(sy + 1, left, right, w, h, px, visited, stack, keys, tolByte);
            }
            return removed;
        }

        private static void SeedScanline(int x, int y, int w, Color32[] px, bool[] visited, Stack<int> stack, Color32[] keys, byte tolByte)
        {
            int idx = y * w + x;
            if (visited[idx]) return;
            if (!MatchKey32(px[idx], keys, tolByte)) return;
            stack.Push(idx);
        }

        private static void AddScanlineSeeds(int y, int left, int right, int w, int h, Color32[] px, bool[] visited, Stack<int> stack, Color32[] keys, byte tolByte)
        {
            if (y < 0 || y >= h) return;
            int row = y * w;
            bool inRun = false;
            for (int x = left; x <= right; x++)
            {
                int idx = row + x;
                if (!visited[idx] && MatchKey32(px[idx], keys, tolByte))
                {
                    if (!inRun) { stack.Push(idx); inRun = true; }
                }
                else inRun = false;
            }
        }

        private static bool MatchKey32(Color32 c, Color32[] keys, byte tolByte)
        {
            if (c.a == 0) return true;
            for (int i = 0; i < keys.Length; i++)
            {
                Color32 k = keys[i];
                int dr = c.r - k.r; if (dr < 0) dr = -dr; if (dr > tolByte) continue;
                int dg = c.g - k.g; if (dg < 0) dg = -dg; if (dg > tolByte) continue;
                int db = c.b - k.b; if (db < 0) db = -db; if (db > tolByte) continue;
                return true;
            }
            return false;
        }
    }
}
#endif
