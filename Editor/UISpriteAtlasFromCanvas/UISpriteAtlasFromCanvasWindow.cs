#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
#if TELLER_UGUI
using UnityEngine.UI;
#endif
using Object = UnityEngine.Object;

namespace TelleR
{
    public class UIAtlasDragDropWindow : EditorWindow
    {
        private const string ToolName = "UI Atlas Builder";
        private const string LogPrefix = "[TelleR/UI Atlas Builder] ";
        private const string ExtV1 = ".spriteatlas";
        private const string ExtV2 = ".spriteatlasv2";

        /// <summary>기존 아틀라스가 있을 때 packable 처리 방식.</summary>
        public enum UpdateMode
        {
            Merge = 0,
            Replace = 1
        }

        /// <summary>드롭한 폴더를 아틀라스에 넣는 방식.</summary>
        public enum FolderHandling
        {
            AddFolderAsPackable = 0,
            CollectSprites = 1
        }

        [SerializeField] private string atlasAssetPath = "Assets/UI/UIAtlas.spriteatlas";
        [SerializeField] private bool packAfterBuild = true;
        [SerializeField] private UpdateMode updateMode = UpdateMode.Merge;
        [SerializeField] private FolderHandling folderHandling = FolderHandling.AddFolderAsPackable;
        [SerializeField] private bool skipSpritesInOtherAtlases = true;
        // 도메인 리로드(컴파일) 후에도 목록이 남도록 직렬화한다. 씬 오브젝트는 씬 전환·플레이 진입 시 Missing이 될 수 있다.
        [SerializeField] private List<Object> entries = new List<Object>();

        private Vector2 scroll;
        private readonly HashSet<Object> entrySet = new HashSet<Object>();
        private string lastReport;
        private MessageType lastReportType = MessageType.Info;

        // 경로별 기존 아틀라스 조회 캐시 (OnGUI마다 AssetDatabase 조회하지 않도록)
        private string cachedAtlasPath;
        private SpriteAtlas cachedAtlas;

        private static GUIContent updateModeContent;
        private static GUIContent folderHandlingContent;
        private static GUIContent skipOthersContent;
        private static GUIContent packAfterBuildContent;
        private static GUIContent existingAtlasContent;
        private static GUIContent removeContent;

        [MenuItem("Tools/TelleR/UI Atlas Builder", false, 121)]
        private static void Open()
        {
            GetWindow<UIAtlasDragDropWindow>(ToolName);
        }

        private void OnEnable()
        {
            if (entries == null) entries = new List<Object>();
            entries.RemoveAll(o => o == null);
            entrySet.Clear();
            for (int i = 0; i < entries.Count; i++) entrySet.Add(entries[i]);
        }

        private void OnProjectChange()
        {
            cachedAtlasPath = null;
            Repaint();
        }

        private static void EnsureContents()
        {
            if (updateModeContent != null) return;
            updateModeContent = new GUIContent("Update Mode",
                "Merge: 기존 packable(폴더 포함)은 그대로 두고 새 항목만 추가합니다.\nReplace: 기존 packable을 모두 제거하고 현재 목록으로 교체합니다.");
            folderHandlingContent = new GUIContent("Folder Entries",
                "AddFolderAsPackable: 폴더 자체를 packable로 등록합니다(나중에 폴더에 추가한 스프라이트도 자동 포함).\nCollectSprites: 지금 폴더 안에 있는 스프라이트만 개별 등록합니다.");
            skipOthersContent = new GUIContent("Skip Sprites In Other Atlases",
                "다른 SpriteAtlas에 이미 포함된 스프라이트는 추가하지 않습니다.\n다른 아틀라스와 겹치는 폴더는 폴더 대신 겹치지 않는 스프라이트만 개별 등록합니다.\n한 스프라이트가 여러 아틀라스에 들어가면 어느 아틀라스가 쓰일지 보장되지 않습니다.");
            packAfterBuildContent = new GUIContent("Pack After Build", "빌드 직후 현재 빌드 타겟으로 아틀라스를 패킹합니다.");
            existingAtlasContent = new GUIContent("Existing Atlas", "현재 경로의 아틀라스입니다. 다른 아틀라스를 지정하면 경로가 바뀝니다.");
            removeContent = new GUIContent("×", "목록에서 제거");
        }

        private void OnGUI()
        {
            EnsureContents();

            EditorGUILayout.HelpBox(
                "캔버스(또는 UI 프리팹)를 통째로 드래그하면 하위 Image·Button 상태 스프라이트·SpriteRenderer가 쓰는 스프라이트를 모아 SpriteAtlas로 묶어 드로우콜을 줄입니다.\n" +
                "1) 드롭 영역에 Canvas / 프리팹 / 스프라이트 / 텍스처 / 폴더 드래그   2) Atlas Asset Path 확인   3) Build / Update Atlas\n" +
                "※ 텍스처는 Sprite 타입으로 임포트돼 있어야 수집됩니다. Assets/ 밖의 스프라이트(내장 UISprite 등)는 제외됩니다.",
                MessageType.Info);
#if !TELLER_UGUI
            EditorGUILayout.HelpBox(
                "uGUI 패키지(com.unity.ugui)가 설치되지 않아 Canvas의 Image/Button 스캔은 사용할 수 없습니다.\n" +
                "스프라이트·텍스처·폴더 드롭과 SpriteRenderer 수집은 정상 동작합니다.",
                MessageType.Warning);
#endif
            DrawPackerModeStatus();

            Rect dropRect = GUILayoutUtility.GetRect(0f, 64f, GUILayout.ExpandWidth(true));
            if (TelleRGUI.DropZone(dropRect, "Drop Canvas / Prefab / Sprite / Texture / Folder", out Object[] dropped, out _))
            {
                for (int i = 0; i < dropped.Length; i++)
                    AddEntry(dropped[i]);
            }

            TelleRGUI.Section("Target");
            atlasAssetPath = EditorGUILayout.TextField("Atlas Asset Path", atlasAssetPath);

            bool v2 = IsV2Mode();
            string resolved = NormalizeAtlasPath(atlasAssetPath, v2, out string pathError);
            if (pathError != null)
            {
                EditorGUILayout.LabelField(pathError, TelleRGUI.Hint);
            }
            else
            {
                if (!string.Equals(resolved, (atlasAssetPath ?? string.Empty).Trim().Replace('\\', '/'), StringComparison.Ordinal))
                    EditorGUILayout.LabelField($"실제 저장 경로: {resolved}", TelleRGUI.Hint);

                SpriteAtlas existing = GetCachedAtlas(resolved);
                EditorGUI.BeginChangeCheck();
                SpriteAtlas picked = (SpriteAtlas)EditorGUILayout.ObjectField(existingAtlasContent, existing, typeof(SpriteAtlas), false);
                if (EditorGUI.EndChangeCheck() && picked != null)
                {
                    atlasAssetPath = AssetDatabase.GetAssetPath(picked);
                    cachedAtlasPath = null;
                    GUI.FocusControl(null);
                }
                if (existing == null)
                    EditorGUILayout.LabelField("이 경로에 아틀라스가 없어 새로 생성합니다.", TelleRGUI.Hint);
            }

            TelleRGUI.Section("Options");
            updateMode = (UpdateMode)EditorGUILayout.EnumPopup(updateModeContent, updateMode);
            folderHandling = (FolderHandling)EditorGUILayout.EnumPopup(folderHandlingContent, folderHandling);
            skipSpritesInOtherAtlases = EditorGUILayout.Toggle(skipOthersContent, skipSpritesInOtherAtlases);
            using (new EditorGUI.DisabledScope(v2))
                packAfterBuild = EditorGUILayout.Toggle(packAfterBuildContent, packAfterBuild);
            if (v2)
                EditorGUILayout.LabelField(IsV2BuildOnlyMode()
                    ? "Sprite Atlas V2 - Enabled for Builds 모드에서는 플레이어 빌드 시에만 패킹됩니다(에디터에서는 원본 스프라이트 사용)."
                    : "Sprite Atlas V2는 임포트 시 자동으로 패킹됩니다.", TelleRGUI.Hint);

            GUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = updateMode == UpdateMode.Replace ? TelleRGUI.DangerButton : TelleRGUI.AccentButton;
                using (new EditorGUI.DisabledScope(entries.Count == 0))
                {
                    if (GUILayout.Button(updateMode == UpdateMode.Replace ? "Build / Replace Atlas" : "Build / Update Atlas", GUILayout.Height(26f)))
                        BuildOrUpdateAtlas();
                }
                GUI.backgroundColor = prevBg;

                if (GUILayout.Button("Clear", GUILayout.Width(80f), GUILayout.Height(26f)))
                    ClearEntries();
            }

            if (!string.IsNullOrEmpty(lastReport))
                EditorGUILayout.HelpBox(lastReport, lastReportType);

            GUILayout.Space(6f);
            DrawEntryList();
        }

        private static void DrawPackerModeStatus()
        {
            if (EditorSettings.spritePackerMode != SpritePackerMode.Disabled) return;

            EditorGUILayout.HelpBox(
                "Sprite Packer Mode가 Disabled라 아틀라스가 패킹되지 않습니다(드로우콜이 줄지 않음).\n" +
                "Project Settings > Editor > Sprite Packer > Mode를 Sprite Atlas V1/V2 중 하나로 바꿔 주세요.",
                MessageType.Warning);
            if (GUILayout.Button("Open Editor Settings", GUILayout.Width(170f)))
                SettingsService.OpenProjectSettings("Project/Editor");
        }

        private SpriteAtlas GetCachedAtlas(string path)
        {
            if (!string.Equals(cachedAtlasPath, path, StringComparison.Ordinal))
            {
                cachedAtlasPath = path;
                cachedAtlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            }
            return cachedAtlas;
        }

        private void AddEntry(Object obj)
        {
            if (obj == null)
                return;

            if (entrySet.Add(obj))
                entries.Add(obj);
        }

        private void ClearEntries()
        {
            entries.Clear();
            entrySet.Clear();
            lastReport = null;
        }

        private void DrawEntryList()
        {
            EditorGUILayout.LabelField($"Entries ({entries.Count})", TelleRGUI.SubHeader);

            int removeAt = -1;
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(120f));
            if (entries.Count == 0)
                EditorGUILayout.LabelField("드롭한 항목이 여기에 표시됩니다.", TelleRGUI.HintCentered);

            for (int i = 0; i < entries.Count; i++)
            {
                Object obj = entries[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    // 클릭 시 Ping은 되도록 활성 상태로 두고, 필드 변경은 반영하지 않는다(목록 편집은 드롭/× 버튼으로만)
                    EditorGUILayout.ObjectField(obj, typeof(Object), true);

                    if (GUILayout.Button(removeContent, GUILayout.Width(22f)))
                        removeAt = i;
                }
            }
            EditorGUILayout.EndScrollView();

            if (removeAt >= 0)
            {
                entrySet.Remove(entries[removeAt]);
                entries.RemoveAt(removeAt);
            }
        }

        private void BuildOrUpdateAtlas()
        {
            entries.RemoveAll(o => o == null);

            BuildReport report = BuildAtlasCore(entries, atlasAssetPath,
                updateMode == UpdateMode.Replace,
                folderHandling == FolderHandling.AddFolderAsPackable,
                skipSpritesInOtherAtlases,
                packAfterBuild,
                (title, message, ok) => TelleRGUI.Confirm(title, message, ok, "취소"));

            cachedAtlasPath = null;

            if (report.cancelled)
                return;

            if (report.error != null)
            {
                lastReport = report.error;
                lastReportType = MessageType.Warning;
                TelleRGUI.Info("아틀라스 빌드 불가", report.error);
                return;
            }

            atlasAssetPath = report.path;
            lastReport = report.Summary();
            lastReportType = (report.inOtherAtlases.Count > 0 || report.folderOverlaps.Count > 0) && !report.skippedOthers
                ? MessageType.Warning
                : MessageType.Info;
            TelleRGUI.Info(report.isNew ? "아틀라스 생성 완료" : "아틀라스 업데이트 완료", lastReport);
        }

        // ─────────────────────────────────────────────
        // Core (UI 없이 호출 가능 — 다이얼로그는 confirm 콜백으로만)
        // ─────────────────────────────────────────────

        internal sealed class BuildReport
        {
            public string error;
            public bool cancelled;
            public string path;
            public bool isNew;
            public bool v2;
            public bool replace;
            public int spritesAdded;
            public int foldersAdded;
            public int alreadyIncluded;
            public int removed;
            public int missingKept; // Replace에서 제거하지 못하고 남은 Missing 참조 packable 수
            public int unsupported;
            public bool packed;
            public bool skippedOthers;
            public readonly List<string> excluded = new List<string>();
            public readonly List<string> inOtherAtlases = new List<string>();
            // 다른 아틀라스와 겹치는 드롭 폴더 (skippedOthers면 폴더 대신 개별 스프라이트로 풀어 등록, 아니면 폴더째 중복 등록)
            public readonly List<string> folderOverlaps = new List<string>();

            public string Summary()
            {
                var sb = new StringBuilder();
                sb.Append($"'{Path.GetFileName(path)}' {(isNew ? "생성" : "업데이트")} 완료 ({(v2 ? "Sprite Atlas V2" : "Sprite Atlas V1")}, {(replace ? "Replace" : "Merge")})\n");
                sb.Append($"추가: 스프라이트 {spritesAdded}개, 폴더 {foldersAdded}개");
                if (removed > 0) sb.Append($" / 제거된 기존 packable {removed}개");
                if (missingKept > 0) sb.Append($"\nMissing 참조 packable {missingKept}개는 제거되지 않았습니다. 아틀라스 인스펙터에서 직접 지워 주세요.");
                if (alreadyIncluded > 0) sb.Append($"\n이미 포함돼 건너뜀: {alreadyIncluded}개");
                if (excluded.Count > 0) sb.Append($"\nAssets/ 밖이라 제외(내장·패키지 스프라이트): {excluded.Count}개 — {Preview(excluded, 3)}");
                if (inOtherAtlases.Count > 0)
                    sb.Append($"\n다른 아틀라스에 이미 포함{(skippedOthers ? "돼 건너뜀" : " (중복 등록됨)")}: {inOtherAtlases.Count}개 — {Preview(inOtherAtlases, 3)}");
                if (folderOverlaps.Count > 0)
                    sb.Append(skippedOthers
                        ? $"\n다른 아틀라스와 겹쳐 폴더 대신 개별 스프라이트로 등록한 폴더: {folderOverlaps.Count}개 — {Preview(folderOverlaps, 3)} (이후 폴더에 추가하는 스프라이트는 자동 포함되지 않음)"
                        : $"\n다른 아틀라스와 겹치는 폴더 (중복 등록됨): {folderOverlaps.Count}개 — {Preview(folderOverlaps, 3)}");
                if (unsupported > 0) sb.Append($"\n지원하지 않는 항목 {unsupported}개는 무시했습니다.");
                if (packed) sb.Append("\n패킹 완료.");
                return sb.ToString();
            }
        }

        private static string Preview(List<string> list, int max)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count && i < max; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(list[i]);
            }
            if (list.Count > max) sb.Append($" 외 {list.Count - max}개");
            return sb.ToString();
        }

        internal static bool IsV2Mode()
        {
            // SpriteAtlasV2 / SpriteAtlasV2Build(2022.2+) 모두 V2. 신규 enum 값을 직접 참조하지 않도록 이름으로 판별한다.
            return EditorSettings.spritePackerMode.ToString().StartsWith("SpriteAtlasV2", StringComparison.Ordinal);
        }

        /// <summary>Sprite Atlas V2 - Enabled for Builds: 플레이어 빌드 때만 패킹되고 에디터에서는 원본 스프라이트를 쓴다.</summary>
        internal static bool IsV2BuildOnlyMode()
        {
            return string.Equals(EditorSettings.spritePackerMode.ToString(), "SpriteAtlasV2Build", StringComparison.Ordinal);
        }

        internal static string NormalizeAtlasPath(string raw, bool v2, out string error)
        {
            error = null;
            string path = (raw ?? string.Empty).Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(path))
            {
                error = "Atlas Asset Path를 입력해 주세요. 예: Assets/UI/UIAtlas" + (v2 ? ExtV2 : ExtV1);
                return path;
            }

            if (path.EndsWith(ExtV2, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - ExtV2.Length);
            else if (path.EndsWith(ExtV1, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - ExtV1.Length);
            path += v2 ? ExtV2 : ExtV1;

            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains("../"))
                error = "Atlas Asset Path는 Assets/ 폴더 아래여야 합니다.";
            return path;
        }

        internal static BuildReport BuildAtlasCore(IList<Object> entries, string rawPath, bool replace, bool folderAsPackable,
            bool skipInOtherAtlases, bool pack, Func<string, string, string, bool> confirm)
        {
            var report = new BuildReport { v2 = IsV2Mode(), replace = replace, skippedOthers = skipInOtherAtlases };

            string path = NormalizeAtlasPath(rawPath, report.v2, out string pathError);
            report.path = path;
            if (pathError != null)
            {
                report.error = pathError;
                return report;
            }

            // 수집·스캔 단계(아직 아무것도 바꾸지 않음)는 취소 가능, 저장 이후는 취소 불가
            bool Cancelled(string info, float progress)
            {
                if (!EditorUtility.DisplayCancelableProgressBar(ToolName, info, progress)) return false;
                report.cancelled = true;
                return true;
            }

            try
            {
                // 1) 수집
                if (Cancelled("스프라이트 수집 중...", 0f)) return report;
                var folders = new List<string>();
                var sprites = new List<Sprite>();
                CollectCandidates(entries, folderAsPackable, folders, sprites, report);

                if (folders.Count == 0 && sprites.Count == 0)
                {
                    // 목록이 비었거나 전부 제외된 상태에서 기존 아틀라스가 빈 깡통이 되는 사고 방지
                    report.error = report.excluded.Count > 0
                        ? $"수집된 스프라이트가 모두 Assets/ 밖(내장·패키지)이라 제외됐습니다: {Preview(report.excluded, 3)}"
                        : "수집된 스프라이트/폴더가 없습니다. 드롭 목록을 확인해 주세요.";
                    return report;
                }

                // 2) 기존 아틀라스
                SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
                if (atlas == null && File.Exists(path))
                {
                    report.error = $"'{path}'에 SpriteAtlas로 읽을 수 없는 파일이 있습니다. 다른 경로를 지정해 주세요.";
                    return report;
                }
                report.isNew = atlas == null;
                Object[] existing = atlas != null ? SpriteAtlasExtensions.GetPackables(atlas) : Array.Empty<Object>();
                if (existing == null) existing = Array.Empty<Object>();
                // 삭제된 에셋(Missing 참조) 항목은 Remove로 매칭되지 않으므로 제거 대상·보고에서 분리한다
                var removableList = new List<Object>(existing.Length);
                for (int i = 0; i < existing.Length; i++)
                    if (existing[i] != null) removableList.Add(existing[i]);
                Object[] removable = removableList.ToArray();
                int missingCount = existing.Length - removable.Length;

                // 3) 다른 아틀라스 스캔 (중복 검사용 — 폴더·스프라이트 모두 이 결과로 검사)
                List<KeyValuePair<string, Coverage>> others = ScanOtherAtlases(path,
                    (i, n) => Cancelled($"다른 아틀라스 스캔 중... ({i + 1}/{n})", 0.1f + 0.3f * i / n));
                if (others == null) return report;

                // 4) 필터링 (Merge면 기존 packable이 이미 덮는 항목은 건너뜀)
                var targetCoverage = new Coverage();
                if (!replace)
                    for (int i = 0; i < existing.Length; i++) targetCoverage.Add(existing[i]);

                var toAdd = new List<Object>();
                var newCoverage = new Coverage();
                var spriteSet = new HashSet<Sprite>(sprites);
                var reportedOwned = new HashSet<Sprite>();

                // 상위 폴더를 먼저 처리해야 하위 폴더가 CoversFolder로 걸러진다 (접두사가 같으면 Ordinal 정렬에서 앞선다)
                folders.Sort(StringComparer.Ordinal);
                for (int i = 0; i < folders.Count; i++)
                {
                    string folder = folders[i];
                    if (targetCoverage.CoversFolder(folder) || newCoverage.CoversFolder(folder))
                    {
                        report.alreadyIncluded++;
                        continue;
                    }
                    Object folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
                    if (folderAsset == null) continue;

                    // 폴더 packable도 다른 아틀라스와 겹치는지 검사: 폴더끼리의 상하위 관계 + 지금 폴더 안에 있는 스프라이트
                    string overlapOwner = null;
                    List<Sprite> folderSprites = null;
                    if (others.Count > 0)
                    {
                        if (Cancelled($"폴더 검사 중... {folder}", 0.4f + 0.2f * i / folders.Count)) return report;
                        overlapOwner = FindFolderOverlap(others, folder);
                        folderSprites = ListSpritesInFolder(folder);
                        for (int j = 0; j < folderSprites.Count; j++)
                        {
                            Sprite s = folderSprites[j];
                            if (targetCoverage.Covers(s)) continue; // 대상 아틀라스에 원래 있던 중복은 이번 작업이 만든 것이 아님
                            string owner = FindOwner(others, s);
                            if (owner == null) continue;
                            if (overlapOwner == null) overlapOwner = owner;
                            if (reportedOwned.Add(s)) report.inOtherAtlases.Add($"{s.name} ({owner}, 폴더 경유)");
                        }
                    }

                    if (overlapOwner != null)
                    {
                        report.folderOverlaps.Add($"{folder} ({overlapOwner})");
                        if (skipInOtherAtlases)
                        {
                            // 폴더 packable은 일부 스프라이트만 뺄 수 없으므로 폴더 대신 스프라이트를 하나씩 등록한다.
                            // 겹치는 스프라이트는 아래 스프라이트 루프에서 건너뛴다.
                            for (int j = 0; j < folderSprites.Count; j++)
                                if (spriteSet.Add(folderSprites[j])) sprites.Add(folderSprites[j]);
                            continue;
                        }
                    }

                    toAdd.Add(folderAsset);
                    newCoverage.Add(folderAsset);
                    report.foldersAdded++;
                }

                for (int i = 0; i < sprites.Count; i++)
                {
                    Sprite s = sprites[i];
                    if (targetCoverage.Covers(s))
                    {
                        report.alreadyIncluded++;
                        continue;
                    }

                    // 이번에 추가하는 폴더가 덮는 스프라이트도 다른 아틀라스 중복이면 보고한다 (폴더 단계에서 보고한 것은 제외)
                    string owner = FindOwner(others, s);
                    if (owner != null && reportedOwned.Add(s))
                        report.inOtherAtlases.Add($"{s.name} ({owner})");

                    if (newCoverage.Covers(s))
                    {
                        report.alreadyIncluded++;
                        continue;
                    }
                    if (owner != null && skipInOtherAtlases) continue;

                    toAdd.Add(s);
                    newCoverage.Add(s);
                    report.spritesAdded++;
                }

                if (toAdd.Count == 0)
                {
                    // 변경 없이 종료 (Replace에서 빈 아틀라스가 되는 사고 방지 포함)
                    report.error = replace
                        ? "교체할 항목이 모두 걸러져(다른 아틀라스에 포함 등) 아틀라스가 비게 되므로 중단했습니다."
                        : $"추가할 새 항목이 없습니다. (이미 포함 {report.alreadyIncluded}개, 다른 아틀라스에 포함 {report.inOtherAtlases.Count}개)";
                    return report;
                }

                // 5) Replace 확인 — 사라지는 packable(특히 폴더)을 나열
                if (replace && removable.Length > 0 && confirm != null)
                {
                    EditorUtility.ClearProgressBar(); // 진행 표시가 확인 다이얼로그를 가리지 않도록
                    if (!confirm("아틀라스 교체 확인", BuildReplaceMessage(path, removable, missingCount, toAdd.Count, report.v2), "교체"))
                    {
                        report.cancelled = true;
                        return report;
                    }
                }

                // 6) 적용
                EditorUtility.DisplayProgressBar(ToolName, "아틀라스 저장 중...", 0.7f);
                if (!EnsureDirectory(path, out string dirError))
                {
                    report.error = dirError;
                    return report;
                }

                if (report.v2)
                    atlas = ApplyV2(path, report.isNew, replace ? removable : null, toAdd);
                else
                    atlas = ApplyV1(path, atlas, replace ? removable : null, toAdd);

                if (atlas == null)
                {
                    report.error = $"'{path}' 저장 후 아틀라스를 다시 읽지 못했습니다. 콘솔을 확인해 주세요.";
                    return report;
                }
                if (replace)
                {
                    report.removed = removable.Length;
                    report.missingKept = missingCount;
                }

                // V2는 임포트 과정에서 패킹된다(V2 - Enabled for Builds 모드는 플레이어 빌드 때만). V1만 수동 패킹.
                if (pack && !report.v2 && EditorSettings.spritePackerMode != SpritePackerMode.Disabled)
                {
                    EditorUtility.DisplayProgressBar(ToolName, "아틀라스 패킹 중...", 0.9f);
                    SpriteAtlasUtility.PackAtlases(new[] { atlas }, EditorUserBuildSettings.activeBuildTarget);
                    report.packed = true;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log(LogPrefix + report.Summary().Replace("\n", " / "));
            if (report.inOtherAtlases.Count > 0 || report.folderOverlaps.Count > 0)
            {
                var warn = new StringBuilder(LogPrefix);
                if (report.inOtherAtlases.Count > 0)
                {
                    warn.Append(skipInOtherAtlases ? "다른 아틀라스에 이미 포함돼 건너뛴 스프라이트:" : "다른 아틀라스와 중복 등록된 스프라이트:");
                    for (int i = 0; i < report.inOtherAtlases.Count; i++) warn.Append("\n - ").Append(report.inOtherAtlases[i]);
                }
                if (report.folderOverlaps.Count > 0)
                {
                    if (report.inOtherAtlases.Count > 0) warn.Append('\n');
                    warn.Append(skipInOtherAtlases
                        ? "다른 아틀라스와 겹쳐 폴더 대신 개별 스프라이트로 등록한 폴더(이후 추가하는 스프라이트는 자동 포함되지 않음):"
                        : "다른 아틀라스와 겹치는 채로 등록한 폴더:");
                    for (int i = 0; i < report.folderOverlaps.Count; i++) warn.Append("\n - ").Append(report.folderOverlaps[i]);
                }
                Debug.LogWarning(warn.ToString());
            }
            return report;
        }

        private static string BuildReplaceMessage(string path, Object[] removable, int missingCount, int newCount, bool v2)
        {
            var folderNames = new List<string>();
            int spriteCount = 0, textureCount = 0, otherCount = 0;
            for (int i = 0; i < removable.Length; i++)
            {
                Object p = removable[i];
                if (p == null) continue;
                string ap = AssetDatabase.GetAssetPath(p);
                if (p is DefaultAsset && AssetDatabase.IsValidFolder(ap)) folderNames.Add(ap);
                else if (p is Sprite) spriteCount++;
                else if (p is Texture2D) textureCount++;
                else otherCount++;
            }

            var sb = new StringBuilder();
            sb.Append($"'{Path.GetFileName(path)}'의 기존 packable {removable.Length}개를 모두 제거하고 현재 목록 {newCount}개로 교체합니다.\n");
            if (folderNames.Count > 0)
            {
                sb.Append($"\n제거되는 폴더 packable {folderNames.Count}개:\n");
                for (int i = 0; i < folderNames.Count && i < 8; i++) sb.Append(" - ").Append(folderNames[i]).Append('\n');
                if (folderNames.Count > 8) sb.Append($" - 외 {folderNames.Count - 8}개\n");
            }
            sb.Append($"\n제거되는 기타 packable: 스프라이트 {spriteCount}개, 텍스처 {textureCount}개");
            if (otherCount > 0) sb.Append($", 기타 {otherCount}개");
            if (missingCount > 0) sb.Append($"\nMissing 참조 {missingCount}개(삭제된 에셋)는 제거되지 않고 남습니다.");
            sb.Append(v2
                ? "\n\nSprite Atlas V2(.spriteatlasv2) 파일은 Undo로 되돌릴 수 없습니다."
                : "\n\nEdit > Undo로 되돌릴 수 있습니다.");
            return sb.ToString();
        }

        private static bool EnsureDirectory(string path, out string error)
        {
            error = null;
            string dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(dir) || AssetDatabase.IsValidFolder(dir)) return true;

            try
            {
                Directory.CreateDirectory(dir);
                // 새 폴더를 AssetDatabase가 인식하기 전에 CreateAsset하면 실패하므로 먼저 임포트
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception e)
            {
                error = $"폴더를 만들 수 없습니다: {dir}\n{e.Message}";
                return false;
            }
        }

        private static SpriteAtlas ApplyV1(string path, SpriteAtlas atlas, Object[] removeList, List<Object> toAdd)
        {
            Object[] addArray = toAdd.ToArray();
            if (atlas == null)
            {
                atlas = new SpriteAtlas();
                // 기본 설정은 새 아틀라스에만 적용 — 기존 아틀라스의 수동 튜닝(패딩·회전 등)을 리셋하지 않음
                SpriteAtlasPackingSettings packing = atlas.GetPackingSettings();
                ApplyDefaultPacking(ref packing);
                atlas.SetPackingSettings(packing);

                SpriteAtlasTextureSettings texture = atlas.GetTextureSettings();
                texture.generateMipMaps = false;
                atlas.SetTextureSettings(texture);

                SpriteAtlasExtensions.SetIncludeInBuild(atlas, true);
                SpriteAtlasExtensions.Add(atlas, addArray);
                AssetDatabase.CreateAsset(atlas, path);
            }
            else
            {
                Undo.RegisterCompleteObjectUndo(atlas, "UI Atlas Builder");
                if (removeList != null && removeList.Length > 0)
                    SpriteAtlasExtensions.Remove(atlas, removeList);
                SpriteAtlasExtensions.Add(atlas, addArray);
                EditorUtility.SetDirty(atlas);
            }

            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
        }

        private static SpriteAtlas ApplyV2(string path, bool isNew, Object[] removeList, List<Object> toAdd)
        {
            SpriteAtlasAsset asset = isNew ? new SpriteAtlasAsset() : SpriteAtlasAsset.Load(path);
            if (asset == null)
            {
                Debug.LogError(LogPrefix + $"'{path}'를 SpriteAtlasAsset으로 불러오지 못했습니다.");
                return null;
            }

            // Load/new로 얻은 SpriteAtlasAsset은 AssetDatabase에 속하지 않는 임시 객체라 저장 후 직접 해제한다
            try
            {
                if (removeList != null && removeList.Length > 0)
                    asset.Remove(removeList);
                asset.Add(toAdd.ToArray());

#if !UNITY_2022_1_OR_NEWER
                // 2021.3: 패킹/텍스처 설정은 SpriteAtlasAsset에 있다 (2022.1+는 SpriteAtlasImporter로 이동)
                // 이 API들은 2021.3에서는 정식이며, 2022.1+에서만 Obsolete(이 분기는 컴파일되지 않음)
#pragma warning disable CS0618
                if (isNew)
                {
                    var temp = new SpriteAtlas();
                    try
                    {
                        SpriteAtlasPackingSettings packing = temp.GetPackingSettings();
                        ApplyDefaultPacking(ref packing);
                        asset.SetPackingSettings(packing);
                        SpriteAtlasTextureSettings texture = temp.GetTextureSettings();
                        texture.generateMipMaps = false;
                        asset.SetTextureSettings(texture);
                        asset.SetIncludeInBuild(true);
                    }
                    finally
                    {
                        DestroyImmediate(temp);
                    }
                }
#pragma warning restore CS0618
#endif

                SpriteAtlasAsset.Save(asset, path);
            }
            finally
            {
                if (asset != null && !EditorUtility.IsPersistent(asset)) DestroyImmediate(asset);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

#if UNITY_2022_1_OR_NEWER
            if (isNew && AssetImporter.GetAtPath(path) is SpriteAtlasImporter importer)
            {
                SpriteAtlasPackingSettings packing = importer.packingSettings;
                ApplyDefaultPacking(ref packing);
                importer.packingSettings = packing;
                SpriteAtlasTextureSettings texture = importer.textureSettings;
                texture.generateMipMaps = false;
                importer.textureSettings = texture;
                importer.includeInBuild = true;
                importer.SaveAndReimport();
            }
#endif
            return AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
        }

        private static void ApplyDefaultPacking(ref SpriteAtlasPackingSettings packing)
        {
            packing.enableRotation = false;
            packing.enableTightPacking = false;
            packing.padding = 2;
        }

        // ─────────────────────────────────────────────
        // Collection
        // ─────────────────────────────────────────────

        private static void CollectCandidates(IList<Object> list, bool folderAsPackable, List<string> folders, List<Sprite> sprites, BuildReport report)
        {
            var spriteSet = new HashSet<Sprite>();
            var folderSet = new HashSet<string>(StringComparer.Ordinal);
            var excludedSet = new HashSet<string>(StringComparer.Ordinal);

            void AddSprite(Sprite s)
            {
                if (s == null || !spriteSet.Add(s)) return;
                if (IsProjectAsset(AssetDatabase.GetAssetPath(s)))
                    sprites.Add(s);
                else if (excludedSet.Add(s.name))
                    report.excluded.Add(s.name);
            }

            void AddSpritesAtPath(string assetPath)
            {
                if (string.IsNullOrEmpty(assetPath)) return;
                Object[] sub = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                for (int j = 0; j < sub.Length; j++)
                    if (sub[j] is Sprite s) AddSprite(s);
            }

            for (int i = 0; i < list.Count; i++)
            {
                Object obj = list[i];
                if (obj == null)
                    continue;

                string assetPath = AssetDatabase.GetAssetPath(obj);

                if (obj is DefaultAsset && AssetDatabase.IsValidFolder(assetPath))
                {
                    if (!IsProjectAsset(assetPath + "/"))
                    {
                        if (excludedSet.Add(assetPath)) report.excluded.Add(assetPath);
                        continue;
                    }

                    if (folderAsPackable)
                    {
                        if (folderSet.Add(assetPath)) folders.Add(assetPath);
                    }
                    else
                    {
                        ForEachSpriteInFolder(assetPath, AddSprite);
                    }
                    continue;
                }

                if (obj is Sprite sprite)
                {
                    AddSprite(sprite);
                    continue;
                }

                if (obj is Texture2D)
                {
                    AddSpritesAtPath(assetPath);
                    continue;
                }

                if (obj is GameObject go)
                {
                    CollectFromGameObject(go, true, AddSprite);
                    continue;
                }

                if (obj is Component component)
                {
                    CollectFromGameObject(component.gameObject, false, AddSprite);
                    continue;
                }

                report.unsupported++;
            }
        }

        private static void CollectFromGameObject(GameObject go, bool recursive, Action<Sprite> add)
        {
            SpriteRenderer[] renderers = recursive ? go.GetComponentsInChildren<SpriteRenderer>(true) : go.GetComponents<SpriteRenderer>();
            for (int i = 0; i < renderers.Length; i++)
                add(renderers[i].sprite);

#if TELLER_UGUI
            Image[] images = recursive ? go.GetComponentsInChildren<Image>(true) : go.GetComponents<Image>();
            for (int i = 0; i < images.Length; i++)
                add(images[i].sprite);

            // Button 등 Selectable의 상태 스프라이트 — SpriteSwap일 때만 실제로 쓰이므로 그때만 수집
            Selectable[] selectables = recursive ? go.GetComponentsInChildren<Selectable>(true) : go.GetComponents<Selectable>();
            for (int i = 0; i < selectables.Length; i++)
            {
                Selectable sel = selectables[i];
                if (sel.transition != Selectable.Transition.SpriteSwap) continue;
                SpriteState st = sel.spriteState;
                add(st.highlightedSprite);
                add(st.pressedSprite);
                add(st.selectedSprite);
                add(st.disabledSprite);
            }
#endif
        }

        private static void ForEachSpriteInFolder(string folder, Action<Sprite> visit)
        {
            // t:Sprite는 스프라이트를 가진 메인 에셋(텍스처 등) 경로를 돌려주므로 서브 에셋까지 펼친다. 하위 폴더 포함.
            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { folder });
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (int g = 0; g < guids.Length; g++)
            {
                string p = AssetDatabase.GUIDToAssetPath(guids[g]);
                if (!visited.Add(p)) continue;
                Object[] sub = AssetDatabase.LoadAllAssetsAtPath(p);
                for (int j = 0; j < sub.Length; j++)
                    if (sub[j] is Sprite s) visit(s);
            }
        }

        private static List<Sprite> ListSpritesInFolder(string folder)
        {
            var list = new List<Sprite>();
            var set = new HashSet<Sprite>();
            ForEachSpriteInFolder(folder, s =>
            {
                if (s != null && set.Add(s) && IsProjectAsset(AssetDatabase.GetAssetPath(s))) list.Add(s);
            });
            return list;
        }

        private static bool IsProjectAsset(string assetPath)
        {
            // 내장 리소스(Resources/unity_builtin_extra 등)와 패키지 에셋은 아틀라스에 넣을 수 없거나 넣으면 안 된다
            return !string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets/", StringComparison.Ordinal);
        }

        // ─────────────────────────────────────────────
        // Coverage (packable 목록이 어떤 스프라이트를 덮는지)
        // ─────────────────────────────────────────────

        private sealed class Coverage
        {
            private readonly HashSet<Object> objects = new HashSet<Object>();
            private readonly HashSet<string> texturePaths = new HashSet<string>(StringComparer.Ordinal);
            private readonly List<string> folderPrefixes = new List<string>();

            public void Add(Object packable)
            {
                if (packable == null) return;
                string ap = AssetDatabase.GetAssetPath(packable);
                if (packable is DefaultAsset && AssetDatabase.IsValidFolder(ap))
                    folderPrefixes.Add(ap.TrimEnd('/') + "/");
                else if (packable is Texture2D)
                    texturePaths.Add(ap);
                else
                    objects.Add(packable);
            }

            public bool Covers(Sprite s)
            {
                return Covers(s, AssetDatabase.GetAssetPath(s));
            }

            public bool Covers(Sprite s, string ap)
            {
                if (objects.Contains(s)) return true;
                if (string.IsNullOrEmpty(ap)) return false;
                if (texturePaths.Contains(ap)) return true;
                for (int i = 0; i < folderPrefixes.Count; i++)
                    if (ap.StartsWith(folderPrefixes[i], StringComparison.Ordinal)) return true;
                return false;
            }

            public bool CoversFolder(string folder)
            {
                string prefix = folder.TrimEnd('/') + "/";
                for (int i = 0; i < folderPrefixes.Count; i++)
                    if (prefix.StartsWith(folderPrefixes[i], StringComparison.Ordinal)) return true;
                return false;
            }

            /// <summary>folder와 같거나 상위·하위 관계인 폴더 packable이 있는지 (지금은 비어 있어도 나중에 들어올 스프라이트가 겹친다).</summary>
            public bool OverlapsFolder(string folder)
            {
                string prefix = folder.TrimEnd('/') + "/";
                for (int i = 0; i < folderPrefixes.Count; i++)
                {
                    if (prefix.StartsWith(folderPrefixes[i], StringComparison.Ordinal) ||
                        folderPrefixes[i].StartsWith(prefix, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
        }

        /// <summary>다른 아틀라스 목록. shouldCancel(i, n)이 true를 돌려주면 null.</summary>
        private static List<KeyValuePair<string, Coverage>> ScanOtherAtlases(string targetPath, Func<int, int, bool> shouldCancel)
        {
            var result = new List<KeyValuePair<string, Coverage>>();
            string targetBase = StripAtlasExtension(targetPath);
            string[] guids = AssetDatabase.FindAssets("t:SpriteAtlas");
            for (int i = 0; i < guids.Length; i++)
            {
                if (shouldCancel != null && (i % 10) == 0 && shouldCancel(i, guids.Length)) return null;
                string p = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.Equals(StripAtlasExtension(p), targetBase, StringComparison.Ordinal)) continue;

                SpriteAtlas other = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(p);
                if (other == null || other.isVariant) continue; // 변형(Variant)은 마스터의 packable을 공유하므로 중복이 아니다

                Object[] packables = SpriteAtlasExtensions.GetPackables(other);
                if (packables == null || packables.Length == 0) continue;

                var cov = new Coverage();
                for (int j = 0; j < packables.Length; j++) cov.Add(packables[j]);
                result.Add(new KeyValuePair<string, Coverage>(Path.GetFileName(p), cov));
            }
            return result;
        }

        private static string FindOwner(List<KeyValuePair<string, Coverage>> others, Sprite s)
        {
            if (others.Count == 0) return null;
            string ap = AssetDatabase.GetAssetPath(s);
            for (int i = 0; i < others.Count; i++)
                if (others[i].Value.Covers(s, ap)) return others[i].Key;
            return null;
        }

        private static string FindFolderOverlap(List<KeyValuePair<string, Coverage>> others, string folder)
        {
            for (int i = 0; i < others.Count; i++)
                if (others[i].Value.OverlapsFolder(folder)) return others[i].Key;
            return null;
        }

        private static string StripAtlasExtension(string p)
        {
            if (p.EndsWith(ExtV2, StringComparison.OrdinalIgnoreCase)) return p.Substring(0, p.Length - ExtV2.Length);
            if (p.EndsWith(ExtV1, StringComparison.OrdinalIgnoreCase)) return p.Substring(0, p.Length - ExtV1.Length);
            return p;
        }
    }
}
#endif
