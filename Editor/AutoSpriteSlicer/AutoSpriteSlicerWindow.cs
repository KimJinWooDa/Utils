#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TelleR
{
    public class AutoSpriteSlicerWindow : EditorWindow
    {
        [Serializable]
        private class Entry
        {
            public string path;
            public bool include = true;

            // 원본 확인 결과(전체 디코드가 필요)는 직렬화해 도메인 리로드 뒤에도 유지하고, 파일 시각이 바뀐 항목만 다시 읽는다.
            public bool opaque;              // 원본이 완전 불투명 (배경 제거 시 전면 이미지가 깎일 수 있음)
            public bool opaqueChecked;
            public long opaqueStampTicks;    // 확인 당시 파일 수정 시각(UTC ticks)
            public string sourceError;       // 원본을 읽지 못한 이유 (없으면 비어 있음)

            // 나머지 상태는 임포터·헤더에서 싸게 다시 읽으므로 직렬화하지 않는다.
            [NonSerialized] public string skipReason;
            [NonSerialized] public bool isSprite;
            [NonSerialized] public bool rewritable;   // 원본을 다시 쓸 수 있는 포맷·상태
            [NonSerialized] public Texture icon;
            [NonSerialized] public GUIContent label;
            [NonSerialized] public GUIContent badgeTip;
            [NonSerialized] public GUIContent errorTip;

            public bool HasSourceError => !string.IsNullOrEmpty(sourceError);

            public void ResetSourceCheck()
            {
                opaque = false;
                opaqueChecked = false;
                opaqueStampTicks = 0;
                sourceError = string.Empty;
            }
        }

        [SerializeField] private List<Entry> _entries = new List<Entry>();
        [SerializeField] private AutoSpriteSlicer.Settings _settings = new AutoSpriteSlicer.Settings();
        [SerializeField] private bool _includeNonSpriteInFolders;
        [SerializeField] private bool _showAdvanced;
        [SerializeField] private int _selected = -1;
        [SerializeField] private string _lastReport = string.Empty;
        [SerializeField] private bool _lastReportHasFailures;
        [SerializeField] private string _folderNote = string.Empty;

        private Vector2 _scroll;
        private Vector2 _listScroll;
        private bool _statusDirty = true;

        // ── 미리보기 캐시: 파일 디코드는 선택이 바뀌거나 파일이 바뀔 때만, 옵션 변경은 캐시된 픽셀로 지연 재계산 ──
        private string _cachePath;
        private long _cacheStamp;
        private AutoSpriteSlicer.DecodedImage _cacheImg;
        private string _cacheError;
        private AutoSpriteSlicer.TrimPlan _previewPlan;
        private string _previewKey;   // 마지막 계산의 (파일, 시각, 픽셀 옵션). 같으면 다시 계산하지 않는다.
        private Texture2D _previewSource;
        private Texture2D _previewResult;
        private string _previewNote;
        private double _previewDue = -1;
        private const double PreviewDelay = 0.25;

        private const int MaxDialogLines = 12;

        private static readonly GUIContent GcModify = new GUIContent("Modify Image Files", "켜면 PNG/JPG/TGA 원본을 배경 제거·트림 결과로 덮어씁니다. 끄면 파일은 그대로 두고 임포터만 Sprite로 바꿉니다.");
        private static readonly GUIContent GcRemoveBg = new GUIContent("Remove Background", "네 모서리(또는 지정한 키 색)와 이어진 배경을 투명하게 만듭니다. 자동 키 모드에서는 완전히 불투명한 이미지에만 적용합니다(Opaque 배지). 끄면 투명한 여백만 트림합니다.");
        private static readonly GUIContent GcBackup = new GUIContent("Backup Originals", "덮어쓰기 전에 원본 파일과 .meta를 프로젝트 루트의 " + AutoSpriteSlicer.BackupRootRelative + " 폴더에 복사합니다. 이 폴더는 .gitignore로 버전 관리에서 제외됩니다.");
        private static readonly GUIContent GcKeepPivot = new GUIContent("Keep Pivot Position", "이미 Sprite인 텍스처가 트림되면 피벗이 같은 픽셀 위치에 남도록 Custom 피벗으로 옮깁니다. 씬에 배치된 오브젝트가 움직이지 않습니다.");
        private static readonly GUIContent GcNewPpu = new GUIContent("New Sprite PPU", "새로 Sprite로 바꾸는 텍스처의 Pixels Per Unit. 이미 Sprite인 텍스처는 기존 값을 유지합니다.");
        private static readonly GUIContent GcNewPivot = new GUIContent("New Sprite Pivot", "새로 Sprite로 바꾸는 텍스처의 피벗. 이미 Sprite인 텍스처는 기존 값을 유지합니다.");
        private static readonly GUIContent GcCustomPivot = new GUIContent("Custom Pivot", "0~1 정규화 좌표 (0,0 = 왼쪽 아래)");
        private static readonly GUIContent GcIncludeAll = new GUIContent("Include Non-Sprite Textures From Folders", "끄면 폴더를 드롭할 때 이미 Sprite인 텍스처만 추가합니다. 켜면 Default 타입도 추가합니다. 노멀맵·라이트맵·큐브맵 등은 항상 제외됩니다.");
        private static readonly GUIContent GcAlpha = new GUIContent("Alpha Threshold", "이 값 이상의 알파를 '불투명'으로 보고 트림 경계를 정합니다.");
        private static readonly GUIContent GcPadding = new GUIContent("Padding (px)", "잘라낸 영역 주위에 남길 여백");
        private static readonly GUIContent GcAutoKeys = new GUIContent("Auto Detect Key Colors", "네 모서리의 불투명 픽셀 색을 배경 키로 씁니다.");
        private static readonly GUIContent GcKey1 = new GUIContent("Key Color 1");
        private static readonly GUIContent GcKey2 = new GUIContent("Key Color 2");
        private static readonly GUIContent GcTolerance = new GUIContent("Color Tolerance", "키 색과의 허용 색차 (0~0.5)");
        private static readonly GUIContent GcJpg = new GUIContent("JPG Quality", "JPG를 트림해 다시 저장할 때의 품질");
        private static readonly GUIContent GcRemove = new GUIContent("×", "목록에서 제거");
        private static readonly GUIContent GcOpaqueTip = new GUIContent(string.Empty,
            "완전히 불투명한 이미지입니다. Remove Background가 켜져 있으면 모서리(키) 색과 이어진 영역을 배경으로 보고 투명하게 만든 뒤 잘라냅니다. " +
            "전면 배경·패널·타일이라면 체크를 끄세요. 폴더에서 추가된 이런 이미지는 체크가 꺼진 채로 들어옵니다.");

        [MenuItem("Tools/TelleR/Auto Sprite Slicer", false, 120)]
        private static void Open()
        {
            var win = GetWindow<AutoSpriteSlicerWindow>(false, "Auto Sprite Slicer");
            win.minSize = new Vector2(440, 520);
        }

        private void OnEnable()
        {
            if (_entries == null) _entries = new List<Entry>();
            if (_settings == null) _settings = new AutoSpriteSlicer.Settings();
            _statusDirty = true;
            RequestPreview(0);
        }

        private void OnDisable() => ClearPreviewCache();

        private void OnFocus()
        {
            _statusDirty = true;
            RequestPreview(0); // 바깥에서 파일이 바뀌었을 수 있음 (바뀌지 않았으면 RebuildPreview가 바로 돌아간다)
        }

        private void Update()
        {
            if (_previewDue >= 0 && EditorApplication.timeSinceStartup >= _previewDue)
            {
                _previewDue = -1;
                RebuildPreview();
                Repaint();
            }
        }

        private void OnGUI()
        {
            if (_statusDirty && Event.current.type == EventType.Layout) RefreshStatuses();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Auto Sprite Slicer", TelleRGUI.Header);
            EditorGUILayout.LabelField("이미지를 넣으면 배경 제거 + 투명 여백 트림 + Sprite(Single) 설정을 한 번에 합니다. " +
                                       "PNG/JPG/TGA는 원본 파일을 덮어쓰므로 Backup Originals를 켜 두세요.", TelleRGUI.Hint);

            DrawDropAndList();
            DrawPreview();
            DrawOptions();
            DrawActions();
            DrawReport();

            EditorGUILayout.EndScrollView();
        }

        private bool BackgroundRemovalActive => _settings.modifyImageFiles && _settings.removeBackground;

        // ───────── 목록 ─────────

        private void DrawDropAndList()
        {
            EditorGUILayout.Space(4f);
            Rect dropRect = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
            if (TelleRGUI.DropZone(dropRect, "이미지 또는 폴더를 여기로 드래그\n<size=10>현재 " + _entries.Count + "개</size>", out var objs, out _)
                && objs != null)
            {
                AddPathsFromObjects(objs);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Selection")) AddPathsFromObjects(Selection.objects);
                using (new EditorGUI.DisabledScope(_entries.Count == 0))
                {
                    if (GUILayout.Button("Clear List"))
                    {
                        _entries.Clear();
                        _selected = -1;
                        _folderNote = string.Empty;
                        RequestPreview(0);
                    }
                }
            }
            _includeNonSpriteInFolders = EditorGUILayout.ToggleLeft(GcIncludeAll, _includeNonSpriteInFolders);
            if (!string.IsNullOrEmpty(_folderNote)) EditorGUILayout.LabelField(_folderNote, TelleRGUI.Hint);

            TelleRGUI.Section("Targets");
            if (_entries.Count == 0)
            {
                EditorGUILayout.LabelField("목록이 비어 있습니다. 이미지나 폴더를 위 영역에 드래그하세요.", TelleRGUI.HintCentered);
                return;
            }

            bool bgActive = BackgroundRemovalActive;
            int removeAt = -1;
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.MaxHeight(180f));
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                Rect row = GUILayoutUtility.GetRect(0, 20f, GUILayout.ExpandWidth(true));

                Color bg = i == _selected
                    ? new Color(TelleRGUI.Accent.r, TelleRGUI.Accent.g, TelleRGUI.Accent.b, 0.25f)
                    : (i % 2 == 0 ? TelleRGUI.RowBg : TelleRGUI.RowBgAlt);
                TelleRGUI.DrawBackground(row, bg);

                bool showOpaque = bgActive && e.skipReason == null && e.rewritable && e.opaque;
                bool showReadError = _settings.modifyImageFiles && e.errorTip != null;
                var toggleRect = new Rect(row.x + 4f, row.y + 1f, 18f, 18f);
                var iconRect = new Rect(toggleRect.xMax + 2f, row.y + 1f, 18f, 18f);
                var removeRect = new Rect(row.xMax - 24f, row.y + 1f, 22f, 18f);
                var badgeRect = new Rect(removeRect.x - 50f, row.y + 3f, 46f, 14f);
                var opaqueRect = new Rect(badgeRect.x - 58f, row.y + 3f, 54f, 14f);
                float labelEnd = showOpaque || showReadError ? opaqueRect.x : badgeRect.x;
                var labelRect = new Rect(iconRect.xMax + 4f, row.y + 1f, labelEnd - iconRect.xMax - 8f, 18f);

                using (new EditorGUI.DisabledScope(e.skipReason != null))
                {
                    bool inc = EditorGUI.Toggle(toggleRect, e.skipReason == null && e.include);
                    if (e.skipReason == null) e.include = inc;
                }
                if (e.icon != null && Event.current.type == EventType.Repaint) GUI.DrawTexture(iconRect, e.icon, ScaleMode.ScaleToFit);

                if (GUI.Button(labelRect, e.label ?? GUIContent.none, EditorStyles.label))
                {
                    _selected = i;
                    RequestPreview(0);
                }

                if (showOpaque)
                {
                    TelleRGUI.DrawBadge(opaqueRect, "Opaque", TelleRGUI.Warning);
                    GUI.Label(opaqueRect, GcOpaqueTip);
                }
                else if (showReadError)
                {
                    TelleRGUI.DrawBadge(opaqueRect, "Error", TelleRGUI.Danger);
                    GUI.Label(opaqueRect, e.errorTip);
                }

                if (e.skipReason != null) TelleRGUI.DrawBadge(badgeRect, "Skip", TelleRGUI.Danger);
                else if (e.isSprite) TelleRGUI.DrawBadge(badgeRect, "Sprite", TelleRGUI.Accent);
                else TelleRGUI.DrawBadge(badgeRect, "New", TelleRGUI.Success);
                GUI.Label(badgeRect, e.badgeTip ?? GUIContent.none); // 배지 툴팁

                if (GUI.Button(removeRect, GcRemove, EditorStyles.miniButton)) removeAt = i;
            }
            EditorGUILayout.EndScrollView();

            if (removeAt >= 0)
            {
                _entries.RemoveAt(removeAt);
                if (_selected == removeAt) _selected = Mathf.Min(removeAt, _entries.Count - 1);
                else if (_selected > removeAt) _selected--;
                RequestPreview(0);
            }

            int skipped = 0;
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].skipReason != null) skipped++;
            if (skipped > 0)
                EditorGUILayout.LabelField($"Skip {skipped}개는 처리하지 않습니다. 이름을 누르면 이유가 미리보기에 표시됩니다.", TelleRGUI.Hint);
        }

        private void AddPathsFromObjects(UnityEngine.Object[] objs)
        {
            if (objs == null) return;
            int before = _entries.Count;
            int excluded = 0;
            bool anyFolder = false;
            var fromFolder = new List<Entry>();

            foreach (var obj in objs)
            {
                if (obj == null) continue;
                string path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    anyFolder = true;
                    var found = AutoSpriteSlicer.CollectFromFolder(path, _includeNonSpriteInFolders, out int ex);
                    excluded += ex;
                    foreach (var p in found)
                    {
                        Entry added = AddEntry(p);
                        if (added != null) fromFolder.Add(added);
                    }
                }
                else if (AutoSpriteSlicer.IsSupportedImage(path)) AddEntry(path);
            }

            if (_entries.Count > before) RefreshStatuses();

            // 폴더에서 한꺼번에 들어온 불투명 이미지(전면 배경·패널·타일일 수 있음)는 직접 체크해야 처리된다.
            int opaqueOff = 0;
            if (BackgroundRemovalActive)
            {
                foreach (var e in fromFolder)
                {
                    if (e.skipReason == null && e.rewritable && e.opaque)
                    {
                        e.include = false;
                        opaqueOff++;
                    }
                }
            }

            if (anyFolder)
            {
                var sb = new StringBuilder();
                if (excluded > 0)
                    sb.Append($"폴더에서 {excluded}개를 제외했습니다 (Sprite가 아닌 텍스처{(_includeNonSpriteInFolders ? "" : "는 위 옵션으로 포함 가능")}, 노멀맵·라이트맵·큐브맵·Multiple 시트 등은 항상 제외).");
                if (opaqueOff > 0)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append($"완전히 불투명한 {opaqueOff}개는 배경 제거로 가장자리가 지워질 수 있어 체크를 꺼 두었습니다(Opaque 배지). 처리하려면 직접 체크하세요.");
                }
                _folderNote = sb.ToString();
            }

            if (_entries.Count > before)
            {
                if (_selected < 0 || _selected >= _entries.Count) _selected = before;
                RequestPreview(0);
            }
        }

        private Entry AddEntry(string path)
        {
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].path == path) return null;
            var e = new Entry { path = path, include = true };
            _entries.Add(e);
            return e;
        }

        private void RefreshStatuses()
        {
            _statusDirty = false;
            // 불투명 여부는 배경 제거가 켜져 있을 때만 쓰인다(Opaque 배지·폴더 체크 해제). 꺼져 있으면 원본을 디코드하지 않는다.
            bool checkSource = BackgroundRemovalActive;
            int pending = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                e.skipReason = AutoSpriteSlicer.GetSkipReason(e.path);
                var ti = AssetImporter.GetAtPath(e.path) as TextureImporter;
                e.isSprite = ti != null && ti.textureType == TextureImporterType.Sprite;
                e.rewritable = e.skipReason == null && AutoSpriteSlicer.GetRewriteBlocker(e.path) == null;
                // 파일이 바뀌었거나 더 이상 다시 쓸 수 없으면 저장된 확인 결과를 버린다.
                if (e.opaqueChecked && (!e.rewritable || e.opaqueStampTicks != FileStampTicks(e.path))) e.ResetSourceCheck();
                if (checkSource && e.rewritable && !e.opaqueChecked) pending++;
            }

            if (pending > 0)
            {
                bool progress = pending > 8;
                try
                {
                    int done = 0;
                    for (int i = 0; i < _entries.Count; i++)
                    {
                        Entry e = _entries[i];
                        if (!e.rewritable || e.opaqueChecked) continue;
                        if (progress && EditorUtility.DisplayCancelableProgressBar("Auto Sprite Slicer",
                                $"원본 확인 중 ({done + 1}/{pending}): {e.path}", (float)done / pending))
                            break; // 남은 항목은 다음 갱신(창 포커스 등) 때 확인한다. 처리 전 확인 대화상자는 항상 정확하다.
                        CheckSource(e);
                        done++;
                    }
                }
                finally
                {
                    if (progress) EditorUtility.ClearProgressBar();
                }
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                bool readError = e.skipReason == null && e.HasSourceError;
                e.icon = AssetDatabase.GetCachedIcon(e.path);
                e.label = new GUIContent(e.path, e.skipReason ?? (readError ? "원본을 읽지 못했습니다: " + e.sourceError : e.path));
                e.badgeTip = new GUIContent(string.Empty, e.skipReason ?? (e.isSprite
                    ? "이미 Sprite — PPU 유지, 트림 시 피벗은 Keep Pivot Position 설정을 따름"
                    : "Sprite로 새로 변환 — New Sprite PPU/Pivot 적용"));
                e.errorTip = readError
                    ? new GUIContent(string.Empty, "원본을 읽지 못해 이미지 파일은 다시 쓰지 않습니다: " + e.sourceError)
                    : null;
            }
        }

        // OnGUI(Layout) 안에서 불리므로 손상되거나 비정상적인 파일이 와도 예외를 밖으로 내보내지 않는다.
        private static void CheckSource(Entry e)
        {
            try
            {
                e.opaqueStampTicks = FileStampTicks(e.path);
                e.opaque = AutoSpriteSlicer.IsSourceOpaque(e.path, out string error);
                e.sourceError = error ?? string.Empty;
            }
            catch (Exception ex)
            {
                e.opaque = false;
                e.sourceError = ex.GetType().Name + ": " + ex.Message;
            }
            e.opaqueChecked = true;
        }

        private static long FileStampTicks(string assetPath)
        {
            try
            {
                string full = AutoSpriteSlicer.ToFullPath(assetPath);
                return File.Exists(full) ? File.GetLastWriteTimeUtc(full).Ticks : 0L;
            }
            catch (Exception) { return 0L; }
        }

        // ───────── 미리보기 ─────────

        private void RequestPreview(double delay)
        {
            double due = EditorApplication.timeSinceStartup + delay;
            if (_previewDue < 0 || due < _previewDue || delay > 0) _previewDue = due;
        }

        private Entry SelectedEntry => _selected >= 0 && _selected < _entries.Count ? _entries[_selected] : null;

        // 픽셀 결과에 영향을 주는 값만 넣는다 (백업·피벗·PPU·JPG 품질은 미리보기와 무관).
        private static string MakePreviewKey(string path, long stamp, AutoSpriteSlicer.Settings s) =>
            $"{path}|{stamp}|{s.removeBackground}|{s.autoDetectKeys}|{(Color32)s.keyColor1}|{(Color32)s.keyColor2}|{s.colorTolerance}|{s.alphaThreshold}|{s.padding}";

        private void RebuildPreview()
        {
            Entry e = SelectedEntry;
            if (e == null) { ClearPreviewCache(); return; }

            string skip = AutoSpriteSlicer.GetSkipReason(e.path);
            if (skip != null) { SetPreviewNote("건너뜀: " + skip); return; }
            if (!_settings.modifyImageFiles) { SetPreviewNote("Modify Image Files가 꺼져 있어 파일은 그대로 두고 임포터만 바꿉니다."); return; }
            string blocker = AutoSpriteSlicer.GetRewriteBlocker(e.path);
            if (blocker != null) { SetPreviewNote(blocker); return; }

            long stamp = FileStampTicks(e.path);
            string key = MakePreviewKey(e.path, stamp, _settings);
            if (key == _previewKey && _previewNote == null) return; // 파일도 픽셀 옵션도 그대로

            _previewNote = null;
            DestroyTex(ref _previewResult);
            _previewPlan = null;

            // Update에서 불리므로 손상된 파일이 와도 예외를 밖으로 내보내지 않고 미리보기에 이유를 표시한다.
            if (_cachePath != e.path || _cacheStamp != stamp)
            {
                ClearDecoded();
                _cachePath = e.path;
                _cacheStamp = stamp;
                try
                {
                    _cacheImg = AutoSpriteSlicer.Decode(AutoSpriteSlicer.ToFullPath(e.path), AutoSpriteSlicer.GetEncodeFormat(e.path), out _cacheError);
                    if (_cacheImg != null) _previewSource = MakeTexture(_cacheImg.Pixels, _cacheImg.Width, _cacheImg.Height);
                }
                catch (Exception ex)
                {
                    _cacheImg = null;
                    DestroyTex(ref _previewSource);
                    _cacheError = ex.GetType().Name + ": " + ex.Message;
                }
            }
            _previewKey = key;
            if (_cacheImg == null) return;

            try
            {
                _previewPlan = AutoSpriteSlicer.ComputePlan(_cacheImg, _settings);
                if (!_previewPlan.FullyTransparent)
                {
                    var px = AutoSpriteSlicer.ResultPixels(_previewPlan, _cacheImg, AutoSpriteSlicer.GetEncodeFormat(e.path), out int w, out int h);
                    _previewResult = MakeTexture(px, w, h);
                }
            }
            catch (Exception ex)
            {
                SetPreviewNote("미리보기를 계산하지 못했습니다: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void SetPreviewNote(string note)
        {
            _previewNote = note;
            _previewKey = null;
            _previewPlan = null;
            DestroyTex(ref _previewResult);
            ClearDecoded();
        }

        private void DrawPreview()
        {
            Entry e = SelectedEntry;
            TelleRGUI.Section(e != null ? "Preview — " + Path.GetFileName(e.path) : "Preview");
            if (e == null)
            {
                EditorGUILayout.LabelField("목록에서 이름을 누르면 미리보기가 표시됩니다.", TelleRGUI.HintCentered);
                return;
            }
            if (_previewNote != null)
            {
                EditorGUILayout.HelpBox(_previewNote, e.skipReason != null ? MessageType.Warning : MessageType.Info);
                return;
            }
            if (_cacheError != null && _cachePath == e.path)
            {
                EditorGUILayout.HelpBox("원본을 읽지 못했습니다: " + _cacheError, MessageType.Warning);
                return;
            }
            if (_previewSource == null || _previewPlan == null || _cachePath != e.path)
            {
                EditorGUILayout.LabelField("미리보기 준비 중...", TelleRGUI.HintCentered);
                return;
            }

            var p = _previewPlan;
            if (p.FullyTransparent)
            {
                EditorGUILayout.HelpBox("배경 제거 후 남는 픽셀이 없습니다. Color Tolerance나 Key Color를 확인하세요. 이 파일은 다시 쓰지 않습니다.", MessageType.Warning);
            }
            else
            {
                string info = $"원본 {p.SourceWidth} x {p.SourceHeight}";
                if (p.Trimmed)
                {
                    int srcArea = Mathf.Max(1, p.SourceWidth * p.SourceHeight);
                    int savedPct = Mathf.RoundToInt((1f - (float)(p.Bounds.width * p.Bounds.height) / srcArea) * 100f);
                    info += $"  →  결과 {p.Bounds.width} x {p.Bounds.height}  (-{savedPct}%)";
                }
                else info += "  →  트림 없음";
                info += $"   ·   배경 제거 {p.AlphaChanged:N0}px   ·   가장자리 최대 알파 {p.MaxEdgeAlpha}";
                EditorGUILayout.LabelField(info, TelleRGUI.Hint);
                if (p.SourceOpaque && p.AlphaChanged > 0)
                    EditorGUILayout.HelpBox("완전히 불투명한 이미지입니다. 모서리 색과 이어진 영역을 배경으로 보고 지운 뒤 잘라냅니다. 전면 배경·패널·타일이라면 이 파일의 체크를 끄세요.", MessageType.Warning);
                if (p.KeyRemovalSkippedHasAlpha)
                    EditorGUILayout.LabelField("이미 투명 픽셀이 있는 이미지라 자동 배경 색 제거는 건너뛰고 투명 여백만 트림합니다. 색 제거가 필요하면 Auto Detect Key Colors를 끄고 키 색을 지정하세요.", TelleRGUI.Hint);
                if (!p.Trimmed && p.AlphaChanged == 0)
                    EditorGUILayout.LabelField("바뀌는 픽셀이 없어 이 파일은 다시 쓰지 않습니다.", TelleRGUI.Hint);
            }

            Rect r = GUILayoutUtility.GetRect(0, 200f, GUILayout.ExpandWidth(true));
            float half = r.width * 0.5f - 4f;
            DrawTextureFitted(new Rect(r.x, r.y, half, r.height), _previewSource, "Before", p.Trimmed ? p.Bounds : (RectInt?)null);
            DrawTextureFitted(new Rect(r.x + half + 8f, r.y, half, r.height), _previewResult, "After", null);
        }

        private static void DrawTextureFitted(Rect rect, Texture2D tex, string label, RectInt? bounds)
        {
            TelleRGUI.DrawBackground(rect, TelleRGUI.PanelBg);
            GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, rect.width - 8f, 16f), label, EditorStyles.miniLabel);
            if (tex == null || tex.width <= 0 || tex.height <= 0) return;
            Rect inner = new Rect(rect.x + 4f, rect.y + 18f, rect.width - 8f, rect.height - 22f);
            if (inner.width <= 0f || inner.height <= 0f) return;
            float scale = Mathf.Min(inner.width / tex.width, inner.height / tex.height);
            float w = tex.width * scale;
            float h = tex.height * scale;
            Rect draw = new Rect(inner.x + (inner.width - w) * 0.5f, inner.y + (inner.height - h) * 0.5f, w, h);
            if (Event.current.type != EventType.Repaint) return;

            EditorGUI.DrawTextureTransparent(draw, tex, ScaleMode.StretchToFill); // 체커보드 위에 그려 투명 영역이 보이게

            if (bounds.HasValue)
            {
                RectInt b = bounds.Value;
                // 텍스처 좌표는 아래가 y=0, GUI는 위가 y=0
                var br = new Rect(draw.x + b.x * scale, draw.yMax - (b.y + b.height) * scale, b.width * scale, b.height * scale);
                Color c = TelleRGUI.Accent;
                EditorGUI.DrawRect(new Rect(br.x, br.y, br.width, 1f), c);
                EditorGUI.DrawRect(new Rect(br.x, br.yMax - 1f, br.width, 1f), c);
                EditorGUI.DrawRect(new Rect(br.x, br.y, 1f, br.height), c);
                EditorGUI.DrawRect(new Rect(br.xMax - 1f, br.y, 1f, br.height), c);
            }
        }

        private static Texture2D MakeTexture(Color32[] px, int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                t.SetPixels32(px);
                t.Apply(false, false);
            }
            catch (Exception)
            {
                DestroyImmediate(t); // HideAndDontSave 텍스처가 남지 않게
                throw;
            }
            return t;
        }

        private static void DestroyTex(ref Texture2D t)
        {
            if (t != null) DestroyImmediate(t);
            t = null;
        }

        private void ClearDecoded()
        {
            DestroyTex(ref _previewSource);
            _cacheImg = null;
            _cacheError = null;
            _cachePath = null;
            _cacheStamp = 0;
        }

        private void ClearPreviewCache()
        {
            ClearDecoded();
            DestroyTex(ref _previewResult);
            _previewPlan = null;
            _previewNote = null;
            _previewKey = null;
        }

        // ───────── 옵션 ─────────

        private void DrawOptions()
        {
            TelleRGUI.Section("Options");
            var s = _settings;
            bool pixelChanged = false; // 미리보기 픽셀에 영향을 주는 옵션만 재계산을 요청한다

            EditorGUI.BeginChangeCheck();
            s.modifyImageFiles = EditorGUILayout.Toggle(GcModify, s.modifyImageFiles);
            bool modeChanged = EditorGUI.EndChangeCheck();
            using (new EditorGUI.DisabledScope(!s.modifyImageFiles))
            {
                EditorGUI.BeginChangeCheck();
                s.removeBackground = EditorGUILayout.Toggle(GcRemoveBg, s.removeBackground);
                modeChanged |= EditorGUI.EndChangeCheck();
                s.backupOriginals = EditorGUILayout.Toggle(GcBackup, s.backupOriginals);
                s.keepPivotPosition = EditorGUILayout.Toggle(GcKeepPivot, s.keepPivotPosition);
            }
            if (s.modifyImageFiles && !s.backupOriginals)
                EditorGUILayout.HelpBox("백업이 꺼져 있습니다. 덮어쓴 원본은 되돌릴 수 없습니다.", MessageType.Warning);

            s.newSpritePixelsPerUnit = Mathf.Max(0.0001f, EditorGUILayout.FloatField(GcNewPpu, s.newSpritePixelsPerUnit));
            s.newSpriteAlignment = (SpriteAlignment)EditorGUILayout.EnumPopup(GcNewPivot, s.newSpriteAlignment);
            if (s.newSpriteAlignment == SpriteAlignment.Custom)
            {
                using (new EditorGUI.IndentLevelScope())
                    s.newSpriteCustomPivot = EditorGUILayout.Vector2Field(GcCustomPivot, s.newSpriteCustomPivot);
            }

            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced", true);
            if (_showAdvanced)
            {
                using (new EditorGUI.IndentLevelScope())
                using (new EditorGUI.DisabledScope(!s.modifyImageFiles))
                {
                    EditorGUI.BeginChangeCheck();
                    s.alphaThreshold = EditorGUILayout.IntSlider(GcAlpha, s.alphaThreshold, 0, 255);
                    s.padding = EditorGUILayout.IntSlider(GcPadding, s.padding, 0, 64);
                    using (new EditorGUI.DisabledScope(!s.removeBackground))
                    {
                        s.autoDetectKeys = EditorGUILayout.Toggle(GcAutoKeys, s.autoDetectKeys);
                        using (new EditorGUI.DisabledScope(s.autoDetectKeys))
                        {
                            s.keyColor1 = EditorGUILayout.ColorField(GcKey1, s.keyColor1);
                            s.keyColor2 = EditorGUILayout.ColorField(GcKey2, s.keyColor2);
                        }
                        s.colorTolerance = EditorGUILayout.Slider(GcTolerance, s.colorTolerance, 0f, 0.5f);
                    }
                    pixelChanged |= EditorGUI.EndChangeCheck();
                    s.jpgQuality = EditorGUILayout.IntSlider(GcJpg, s.jpgQuality, 50, 100);
                    if (GUILayout.Button("Reset Advanced"))
                    {
                        var d = new AutoSpriteSlicer.Settings();
                        s.alphaThreshold = d.alphaThreshold;
                        s.padding = d.padding;
                        s.autoDetectKeys = d.autoDetectKeys;
                        s.keyColor1 = d.keyColor1;
                        s.keyColor2 = d.keyColor2;
                        s.colorTolerance = d.colorTolerance;
                        s.jpgQuality = d.jpgQuality;
                        pixelChanged = true;
                    }
                }
            }

            // 원본 불투명 확인은 배경 제거가 켜질 때만 하므로 모드가 바뀌면 목록 상태를 다시 계산한다.
            if (modeChanged)
            {
                pixelChanged = true;
                _statusDirty = true;
            }
            // 슬라이더 드래그마다 전체 재계산하지 않도록 잠깐 멈춘 뒤 캐시된 픽셀로 다시 계산
            if (pixelChanged) RequestPreview(PreviewDelay);
        }

        // ───────── 실행 ─────────

        private void DrawActions()
        {
            EditorGUILayout.Space(8f);
            int count = 0;
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].skipReason == null && _entries[i].include) count++;

            using (new EditorGUI.DisabledScope(count == 0))
            {
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = TelleRGUI.AccentButton;
                bool clicked = GUILayout.Button($"Process {count} Image(s)", GUILayout.Height(32f));
                GUI.backgroundColor = prev;
                if (clicked)
                {
                    RunProcess();
                    GUIUtility.ExitGUI();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Backup Folder"))
                {
                    string root = AutoSpriteSlicer.BackupRoot;
                    if (Directory.Exists(root)) EditorUtility.RevealInFinder(root);
                    else TelleRGUI.Info("백업 없음", "아직 만든 백업이 없습니다.\n" + root);
                }
                if (GUILayout.Button("Restore Last Backup..."))
                {
                    RunRestore();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void RunProcess()
        {
            RefreshStatuses();
            var targets = new List<string>();
            int skipped = 0;
            foreach (var e in _entries)
            {
                if (e.skipReason != null) { skipped++; continue; }
                if (e.include) targets.Add(e.path);
            }
            if (targets.Count == 0) return;

            // 파일만 건드리지 않는 모드에서도 .meta는 백업한다.
            var run = _settings.Clone();
            if (!run.modifyImageFiles) run.backupOriginals = true;

            // 실제로 무엇이 바뀌는지 먼저 계산해 대화상자에 그대로 보여 준다(쓰기 없음).
            var dry = AutoSpriteSlicer.DryRun(targets, run, true);
            if (dry.Cancelled) return;

            int nRewrite = dry.Count(AutoSpriteSlicer.PlannedAction.Rewrite);
            int nImporter = dry.Count(AutoSpriteSlicer.PlannedAction.ImporterOnly);
            if (nRewrite + nImporter == 0)
            {
                TelleRGUI.Info("변경 없음", $"바꿀 것이 없습니다. 선택한 {targets.Count}개 모두 현재 옵션으로 처리한 결과와 같습니다." +
                                        ErrorLines(dry));
                return;
            }

            if (!TelleRGUI.Confirm("Auto Sprite Slicer 처리 확인", BuildConfirmMessage(dry, run, skipped), "처리", "취소")) return;

            var result = AutoSpriteSlicer.Process(targets, run, true);
            _lastReport = result.ToReport();
            _lastReportHasFailures = result.Failed.Count > 0 || result.Cancelled;

            string log = AutoSpriteSlicer.LogPrefix + result.ToReport(200);
            if (result.Failed.Count > 0) Debug.LogWarning(log); else Debug.Log(log);

            ClearPreviewCache();
            _statusDirty = true;
            RequestPreview(0);
        }

        private static string BuildConfirmMessage(AutoSpriteSlicer.DryRunResult dry, AutoSpriteSlicer.Settings s, int skippedInList)
        {
            int nRewrite = dry.Count(AutoSpriteSlicer.PlannedAction.Rewrite);
            int nImporter = dry.Count(AutoSpriteSlicer.PlannedAction.ImporterOnly);
            int nUnchanged = dry.Count(AutoSpriteSlicer.PlannedAction.Unchanged);
            int nSkip = dry.Count(AutoSpriteSlicer.PlannedAction.Skip) + skippedInList;
            int nConvert = 0, nOpaque = 0, nNotes = 0;
            foreach (var f in dry.Files)
            {
                if (f.ConvertToSprite) nConvert++;
                if (f.OpaqueSourceLosesPixels) nOpaque++;
                if (f.Action == AutoSpriteSlicer.PlannedAction.Rewrite || f.Action == AutoSpriteSlicer.PlannedAction.ImporterOnly) nNotes += f.Notes.Count;
            }

            var sb = new StringBuilder();
            sb.Append($"파일 수정 {nRewrite}개 · 임포터만 변경 {nImporter}개 · 변경 없음 {nUnchanged}개");
            if (nSkip > 0) sb.Append($" · 건너뜀 {nSkip}개");
            sb.Append('\n');
            if (!s.modifyImageFiles) sb.Append("Modify Image Files가 꺼져 있어 이미지 파일은 수정하지 않습니다.\n");

            if (nRewrite > 0)
            {
                sb.Append("\n[다시 쓰는 파일] 해상도는 원본 기준으로 잘라내기만 합니다.\n");
                int shown = 0;
                foreach (var f in dry.Files)
                {
                    if (f.Action != AutoSpriteSlicer.PlannedAction.Rewrite) continue;
                    if (shown++ >= MaxDialogLines) continue;
                    sb.Append("• ").Append(Path.GetFileName(f.Path)).Append("  ")
                      .Append(f.SourceWidth).Append('x').Append(f.SourceHeight);
                    if (f.ResultWidth != f.SourceWidth || f.ResultHeight != f.SourceHeight)
                        sb.Append(" → ").Append(f.ResultWidth).Append('x').Append(f.ResultHeight);
                    else sb.Append(" (크기 유지)");
                    if (f.BackgroundPixelsRemoved > 0) sb.Append($", 배경 {f.BackgroundPixelsRemoved:N0}px 제거");
                    if (f.Lossy) sb.Append($", JPG 품질 {s.jpgQuality}로 재압축");
                    if (f.OpaqueSourceLosesPixels) sb.Append(" [불투명 원본]");
                    sb.Append('\n');
                }
                if (shown > MaxDialogLines) sb.Append($"• 외 {shown - MaxDialogLines}개\n");
                if (nOpaque > 0)
                    sb.Append($"※ [불투명 원본] {nOpaque}개는 모서리 색과 이어진 영역이 배경으로 지워집니다. 전면 배경·패널·타일이면 취소하고 체크를 끄세요.\n");
            }

            if (nImporter > 0)
            {
                sb.Append("\n[임포터만 변경] 파일 내용은 그대로입니다.\n");
                int shown = 0;
                foreach (var f in dry.Files)
                {
                    if (f.Action != AutoSpriteSlicer.PlannedAction.ImporterOnly) continue;
                    if (shown++ >= MaxDialogLines / 2) continue;
                    sb.Append("• ").Append(Path.GetFileName(f.Path)).Append(f.ConvertToSprite ? " — Sprite로 변환\n" : " — 임포트 설정 변경\n");
                }
                if (shown > MaxDialogLines / 2) sb.Append($"• 외 {shown - MaxDialogLines / 2}개\n");
            }

            sb.Append("\n[임포트 설정]\n");
            if (nConvert > 0)
                sb.Append($"• 새로 Sprite로 바꿀 {nConvert}개: Sprite(Single), PPU {s.newSpritePixelsPerUnit:0.##}, Pivot {PivotLabel(s)}, Sprite 기본 임포트 설정(밉맵 끔 등).\n");
            sb.Append("• 이미 Sprite인 파일은 PPU를 유지합니다.");
            if (s.modifyImageFiles)
                sb.Append(" 트림되면 9-slice 보더와 커스텀 Outline/Physics Shape를 잘린 만큼 옮기고, 피벗은 ")
                  .Append(s.keepPivotPosition ? "같은 픽셀 위치(Custom)로 옮깁니다." : "정규화 값 그대로 둡니다.");
            sb.Append('\n');
            if (nNotes > 0) sb.Append($"• 참고 사항 {nNotes}건은 처리 후 보고서에 표시됩니다.\n");

            sb.Append("\n[백업]\n");
            if (s.backupOriginals)
                sb.Append($"• 바뀌는 파일과 .meta를 {AutoSpriteSlicer.BackupRootRelative}/<시각>/ 에 복사합니다. Restore Last Backup으로 되돌릴 수 있습니다.");
            else
                sb.Append("• 꺼짐 — 덮어쓴 파일은 되돌릴 수 없습니다.");

            sb.Append(ErrorLines(dry));
            return sb.ToString();
        }

        private static string ErrorLines(AutoSpriteSlicer.DryRunResult dry)
        {
            int n = dry.Count(AutoSpriteSlicer.PlannedAction.Error);
            if (n == 0) return string.Empty;
            var sb = new StringBuilder($"\n\n[확인 실패 {n}개] 처리할 때 실패로 보고됩니다.\n");
            int shown = 0;
            foreach (var f in dry.Files)
            {
                if (f.Action != AutoSpriteSlicer.PlannedAction.Error) continue;
                if (shown++ >= 5) continue;
                sb.Append("• ").Append(Path.GetFileName(f.Path)).Append(" — ").Append(f.Reason).Append('\n');
            }
            if (shown > 5) sb.Append($"• 외 {shown - 5}개\n");
            return sb.ToString();
        }

        private static string PivotLabel(AutoSpriteSlicer.Settings s) =>
            s.newSpriteAlignment == SpriteAlignment.Custom
                ? $"Custom ({s.newSpriteCustomPivot.x:0.##}, {s.newSpriteCustomPivot.y:0.##})"
                : s.newSpriteAlignment.ToString();

        private void RunRestore()
        {
            string dir = AutoSpriteSlicer.FindLatestBackup();
            if (dir == null)
            {
                TelleRGUI.Info("백업 없음", "되돌릴 백업이 없습니다.\n" + AutoSpriteSlicer.BackupRoot);
                return;
            }
            var entries = AutoSpriteSlicer.ReadBackupEntries(dir);
            int ok = 0;
            foreach (var en in entries) if (en.Problem == null) ok++;

            var sb = new StringBuilder();
            if (ok > 0)
            {
                sb.Append($"'{Path.GetFileName(dir)}' 백업의 {ok}개 에셋을 백업 시점으로 되돌립니다.\n");
                sb.Append("지금의 파일 내용과 임포트 설정(.meta)은 덮어써집니다.\n\n");
                int shown = 0;
                foreach (var en in entries)
                {
                    if (en.Problem != null) continue;
                    if (shown++ >= MaxDialogLines) continue;
                    sb.Append("• ").Append(en.CurrentPath);
                    if (en.Moved) sb.Append("  (이동됨, 백업 당시 ").Append(en.RecordedPath).Append(')');
                    sb.Append('\n');
                }
                if (shown > MaxDialogLines) sb.Append($"• 외 {shown - MaxDialogLines}개\n");
            }
            else sb.Append($"'{Path.GetFileName(dir)}' 백업에서 되돌릴 수 있는 에셋이 없습니다.\n");

            int bad = entries.Count - ok;
            if (bad > 0)
            {
                sb.Append($"\n복원하지 않는 {bad}개:\n");
                int shown = 0;
                foreach (var en in entries)
                {
                    if (en.Problem == null) continue;
                    if (shown++ >= 5) continue;
                    sb.Append("• ").Append(en.RecordedPath).Append(" — ").Append(en.Problem).Append('\n');
                }
                if (shown > 5) sb.Append($"• 외 {shown - 5}개\n");
            }

            if (ok == 0)
            {
                TelleRGUI.Info("백업 복원", sb.ToString());
                return;
            }
            if (!TelleRGUI.Confirm("백업 복원", sb.ToString(), "복원", "취소")) return;

            var failures = new List<AutoSpriteSlicer.Issue>();
            int restored = AutoSpriteSlicer.RestoreBackup(dir, failures);
            var report = new StringBuilder($"백업 복원: {restored}/{entries.Count}개 ({dir})");
            foreach (var f in failures) report.Append("\n • ").Append(f.Path).Append(" — ").Append(f.Reason);
            _lastReport = report.ToString();
            _lastReportHasFailures = failures.Count > 0;
            if (failures.Count > 0) Debug.LogWarning(AutoSpriteSlicer.LogPrefix + _lastReport);
            else Debug.Log(AutoSpriteSlicer.LogPrefix + _lastReport);

            ClearPreviewCache();
            _statusDirty = true;
            RequestPreview(0);
        }

        private void DrawReport()
        {
            if (string.IsNullOrEmpty(_lastReport)) return;
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(_lastReport, _lastReportHasFailures ? MessageType.Warning : MessageType.Info);
            if (GUILayout.Button("Clear Report", EditorStyles.miniButton, GUILayout.Width(90f))) _lastReport = string.Empty;
        }
    }
}
#endif
