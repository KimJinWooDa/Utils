#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TelleR
{
    /// <summary>
    /// Auto Sprite Slicer 처리 엔진. 창(AutoSpriteSlicerWindow)과 스크립트 일괄 처리가 같은 경로를 쓴다.
    /// 원본 파일은 항상 디스크 바이트에서 직접 디코드한다(임포트된 텍스처의 크기 제한·압축·NPOT 스케일이 섞이지 않게).
    /// 무손실로 다시 쓸 수 없는 포맷은 파일을 건드리지 않고 임포터만 바꾼다.
    /// </summary>
    public static class AutoSpriteSlicer
    {
        public const string LogPrefix = "[TelleR/AutoSpriteSlicer] ";

        public static readonly string[] SupportedExtensions =
            { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".psd", ".psb", ".gif", ".hdr", ".exr", ".tif", ".tiff" };

        /// <summary>백업 루트(프로젝트 루트 기준). Assets 밖이라 임포트되지 않는다.</summary>
        public const string BackupRootRelative = "TelleRBackups/AutoSpriteSlicer";
        private const string ManifestName = "manifest.txt";

        [Serializable]
        public sealed class Settings
        {
            [Tooltip("켜면 PNG/JPG/TGA 원본을 배경 제거·트림 결과로 덮어씁니다. 끄면 임포터만 바꿉니다.")]
            public bool modifyImageFiles = true;
            [Tooltip("네 모서리(또는 지정한) 색과 이어진 배경을 투명하게 만듭니다. 자동 키 모드에서는 이미 투명 픽셀이 있는 이미지에 적용하지 않습니다.")]
            public bool removeBackground = true;
            [Range(0, 255)] public int alphaThreshold = 1;
            [Range(0, 64)] public int padding = 0;
            public bool autoDetectKeys = true;
            public Color keyColor1 = Color.white;
            public Color keyColor2 = new Color(0.8f, 0.8f, 0.8f, 1f);
            [Range(0f, 0.5f)] public float colorTolerance = 0.08f;
            [Range(50, 100)] public int jpgQuality = 95;
            [Tooltip("덮어쓰기 전에 원본 파일과 .meta를 프로젝트 루트의 TelleRBackups 폴더에 복사합니다.")]
            public bool backupOriginals = true;
            [Tooltip("이미 Sprite인 텍스처가 트림되면 피벗이 같은 픽셀 위치에 남도록 Custom 피벗으로 옮깁니다.")]
            public bool keepPivotPosition = true;

            // 새로 Sprite로 바꾸는 텍스처에만 적용 (이미 Sprite인 텍스처의 PPU/피벗은 유지)
            public float newSpritePixelsPerUnit = 100f;
            public SpriteAlignment newSpriteAlignment = SpriteAlignment.Center;
            public Vector2 newSpriteCustomPivot = new Vector2(0.5f, 0.5f);

            public Settings Clone() => (Settings)MemberwiseClone();
        }

        public struct Issue
        {
            public string Path;
            public string Reason;
            public Issue(string path, string reason) { Path = path; Reason = reason; }
        }

        public sealed class Result
        {
            /// <summary>이미지 파일을 다시 쓴 항목.</summary>
            public readonly List<string> Rewritten = new List<string>();
            /// <summary>파일은 그대로 두고 임포터 설정만 바꾼 항목.</summary>
            public readonly List<string> ImporterOnly = new List<string>();
            /// <summary>Rewritten/ImporterOnly 중 새로 Sprite로 바뀐 항목.</summary>
            public readonly List<string> ConvertedToSprite = new List<string>();
            /// <summary>바꿀 것이 없던 항목.</summary>
            public readonly List<string> Unchanged = new List<string>();
            public readonly List<Issue> Skipped = new List<Issue>();
            public readonly List<Issue> Failed = new List<Issue>();
            /// <summary>처리는 됐지만 알아둘 점(보더 보정, 무손실 불가로 파일 유지 등).</summary>
            public readonly List<Issue> Notes = new List<Issue>();
            public string BackupFolder;
            public bool Cancelled;

            public string Summary =>
                $"파일 수정 {Rewritten.Count} · 임포터만 변경 {ImporterOnly.Count} (새 Sprite {ConvertedToSprite.Count}) · " +
                $"변경 없음 {Unchanged.Count} · 건너뜀 {Skipped.Count} · 실패 {Failed.Count}" + (Cancelled ? " · 사용자가 취소함" : "");

            public string ToReport(int maxLinesPerGroup = 20)
            {
                var sb = new StringBuilder(Summary);
                if (!string.IsNullOrEmpty(BackupFolder)) sb.Append("\n백업: ").Append(BackupFolder);
                AppendIssues(sb, "실패", Failed, maxLinesPerGroup);
                AppendIssues(sb, "건너뜀", Skipped, maxLinesPerGroup);
                AppendIssues(sb, "참고", Notes, maxLinesPerGroup);
                return sb.ToString();
            }

            private static void AppendIssues(StringBuilder sb, string title, List<Issue> list, int max)
            {
                if (list.Count == 0) return;
                sb.Append("\n\n").Append(title).Append(':');
                int n = Mathf.Min(list.Count, max);
                for (int i = 0; i < n; i++) sb.Append("\n • ").Append(list[i].Path).Append(" — ").Append(list[i].Reason);
                if (list.Count > n) sb.Append("\n • 외 ").Append(list.Count - n).Append("개");
            }
        }

        internal enum EncodeFormat { None, Png, Jpg, Tga }

        // ───────── 대상 판정 ─────────

        public static bool IsSupportedImage(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            for (int i = 0; i < SupportedExtensions.Length; i++)
                if (path.EndsWith(SupportedExtensions[i], StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>처리할 수 없는 이유를 돌려준다. 처리 가능하면 null.</summary>
        public static string GetSkipReason(string assetPath)
        {
            if (!IsSupportedImage(assetPath)) return "지원하지 않는 확장자입니다.";
            if (!File.Exists(ToFullPath(assetPath))) return "파일이 없습니다.";

            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null) return "임포트되지 않은 파일입니다.";
            var ti = importer as TextureImporter;
            if (ti == null) return $"TextureImporter가 아닌 전용 임포터({importer.GetType().Name})를 쓰는 파일이라 건너뜁니다.";
            if (ti.textureShape != TextureImporterShape.Texture2D)
                return $"Texture Shape이 {ti.textureShape}입니다. 2D 텍스처만 대상입니다.";

            if (ti.textureType != TextureImporterType.Default && ti.textureType != TextureImporterType.Sprite)
                return $"Texture Type이 {ti.textureType}입니다. 노멀맵·라이트맵 등은 대상이 아닙니다.";
            if (ti.textureType == TextureImporterType.Sprite)
            {
                if (ti.spriteImportMode == SpriteImportMode.Multiple) return "Sprite Mode가 Multiple입니다. 수작업 슬라이스를 보호하려고 건너뜁니다.";
                if (ti.spriteImportMode == SpriteImportMode.Polygon) return "Sprite Mode가 Polygon입니다. 커스텀 메시를 보호하려고 건너뜁니다.";
            }

            if (assetPath.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                if (pkg != null && pkg.source != UnityEditor.PackageManager.PackageSource.Embedded &&
                    pkg.source != UnityEditor.PackageManager.PackageSource.Local)
                    return "읽기 전용 패키지 안의 파일입니다.";
            }
            return null;
        }

        /// <summary>
        /// 폴더 안의 지원 이미지를 모은다. includeNonSprite가 false면 이미 Sprite인 텍스처만 모은다.
        /// 노멀맵·라이트맵 등 처리 대상이 아닌 텍스처는 항상 제외하고 excluded에 센다.
        /// </summary>
        public static List<string> CollectFromFolder(string folderAssetPath, bool includeNonSprite, out int excluded)
        {
            excluded = 0;
            var result = new List<string>();
            if (string.IsNullOrEmpty(folderAssetPath) || !AssetDatabase.IsValidFolder(folderAssetPath)) return result;

            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderAssetPath });
            var seen = new HashSet<string>();
            foreach (var guid in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (!seen.Add(p) || !IsSupportedImage(p)) continue;
                if (GetSkipReason(p) != null) { excluded++; continue; }
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (!includeNonSprite && (ti == null || ti.textureType != TextureImporterType.Sprite)) { excluded++; continue; }
                result.Add(p);
            }
            return result;
        }

        internal static EncodeFormat GetEncodeFormat(string path)
        {
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return EncodeFormat.Png;
            if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) return EncodeFormat.Jpg;
            if (path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase)) return EncodeFormat.Tga;
            return EncodeFormat.None;
        }

        /// <summary>
        /// 헤더만 읽어 원본을 무손실로 다시 쓸 수 있는지 판단한다. 불가하면 이유를 돌려준다(파일은 유지, 임포터만 변경).
        /// </summary>
        public static string GetRewriteBlocker(string assetPath)
        {
            EncodeFormat fmt = GetEncodeFormat(assetPath);
            if (fmt == EncodeFormat.None) return "이 포맷은 다시 저장하지 않습니다(파일 유지, 임포터만 변경).";
            byte[] head = ReadHead(ToFullPath(assetPath), 32);
            if (head == null) return "파일을 읽을 수 없습니다.";
            if (fmt == EncodeFormat.Png)
            {
                if (head.Length < 26 || head[0] != 0x89 || head[1] != (byte)'P') return "PNG 헤더가 올바르지 않습니다.";
                if (head[24] == 16) return "16비트 PNG는 8비트로만 다시 저장할 수 있어 파일을 유지합니다.";
            }
            else if (fmt == EncodeFormat.Tga)
            {
                string err = TgaCodec.CheckHeader(head);
                if (err != null) return err + " 파일은 유지합니다.";
            }

            // 크기가 바뀌면 함께 어긋나는 데이터가 임포터에 있으면 파일을 건드리지 않는다.
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter ti)
            {
                SpriteDataInfo g = ReadSpriteData(ti);
                if (g.SecondaryTextures > 0) return "Secondary Texture(노멀·마스크 맵 등)가 연결된 Sprite라 크기를 바꾸면 서로 어긋납니다. 파일은 유지합니다.";
                if (g.Bones > 0 || g.Vertices > 0) return "2D Animation 스킨(본·메시) 데이터가 있는 Sprite라 크기를 바꾸면 리깅이 어긋납니다. 파일은 유지합니다.";
            }
            return null;
        }

        /// <summary>
        /// 원본 픽셀이 전부 불투명(알파 255)인지. 자동 키 모드의 배경 제거는 이런 이미지에만 적용되므로
        /// 전면 배경·패널·타일이 깎일 수 있음을 미리 알리는 데 쓴다. 다시 쓸 수 없는 포맷이거나 읽지 못하면 false.
        /// </summary>
        public static bool IsSourceOpaque(string assetPath) => IsSourceOpaque(assetPath, out _);

        /// <summary>IsSourceOpaque와 같지만 원본을 읽지 못한 이유를 error로 돌려준다(읽었으면 null). 예외를 던지지 않는다.</summary>
        public static bool IsSourceOpaque(string assetPath, out string error)
        {
            error = null;
            try
            {
                EncodeFormat fmt = GetEncodeFormat(assetPath);
                if (fmt == EncodeFormat.None) return false;
                if (fmt == EncodeFormat.Jpg) return true;
                string full = ToFullPath(assetPath);
                if (fmt == EncodeFormat.Tga)
                {
                    byte[] head = ReadHead(full, 18);
                    error = TgaCodec.CheckHeader(head);
                    if (error != null) return false;
                    if (head[16] != 32) return true; // 8/24bpp는 알파 채널이 없다
                }
                else
                {
                    byte[] bytes;
                    try { bytes = File.ReadAllBytes(full); }
                    catch (Exception ex) { error = ex.Message; return false; }
                    if (!PngMayHaveAlpha(bytes)) return true;
                }
                DecodedImage img = Decode(full, fmt, out error);
                return img != null && AllOpaque(img.Pixels);
            }
            catch (Exception ex)
            {
                // 손상되거나 비정상적인 파일이 창의 OnGUI까지 예외를 올리지 않게 한다.
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        // 색 타입 4/6(알파) 또는 tRNS 청크가 있으면 true. 헤더가 이상하면 true(디코드해서 확인).
        private static bool PngMayHaveAlpha(byte[] d)
        {
            if (d == null || d.Length < 33 || d[0] != 0x89 || d[1] != (byte)'P') return true;
            int colorType = d[25];
            if (colorType == 4 || colorType == 6) return true;
            int pos = 8;
            while (pos + 8 <= d.Length)
            {
                int len = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
                if (len < 0) return true;
                if (d[pos + 4] == (byte)'t' && d[pos + 5] == (byte)'R' && d[pos + 6] == (byte)'N' && d[pos + 7] == (byte)'S') return true;
                if (d[pos + 4] == (byte)'I' && d[pos + 5] == (byte)'D' && d[pos + 6] == (byte)'A' && d[pos + 7] == (byte)'T') return false;
                if (len > d.Length - pos - 12) return true; // 파일 끝을 넘는 청크 길이(잘렸거나 손상) — 디코드해서 확인
                pos += 12 + len;
            }
            return true;
        }

        // ───────── Sprite 임포터 데이터 (Outline / Physics Shape / 스킨 / Secondary Texture) ─────────

        private struct SpriteDataInfo
        {
            public int OutlinePaths, PhysicsPaths, Bones, Vertices, SecondaryTextures;
        }

        // Single 모드 Sprite의 데이터는 TextureImporter의 m_SpriteSheet에 저장된다. 이름이 없는 버전이면 0으로 본다.
        private const string SpOutline = "m_SpriteSheet.m_Outline";
        private const string SpPhysics = "m_SpriteSheet.m_PhysicsShape";

        private static SpriteDataInfo ReadSpriteData(TextureImporter ti)
        {
            var info = new SpriteDataInfo();
            using (var so = new SerializedObject(ti))
            {
                info.OutlinePaths = ArraySize(so, SpOutline);
                info.PhysicsPaths = ArraySize(so, SpPhysics);
                info.Bones = ArraySize(so, "m_SpriteSheet.m_Bones");
                info.Vertices = ArraySize(so, "m_SpriteSheet.m_Vertices");
                info.SecondaryTextures = ArraySize(so, "m_SpriteSheet.m_SecondaryTextures");
            }
            return info;
        }

        private static int ArraySize(SerializedObject so, string path)
        {
            SerializedProperty p = so.FindProperty(path);
            return p != null && p.isArray ? p.arraySize : 0;
        }

        /// <summary>
        /// 커스텀 Outline / Physics Shape 점(스프라이트 사각형 중심 기준 픽셀 좌표)을 트림된 사각형 기준으로 옮긴다.
        /// 새 사각형 밖으로 나가는 점은 가장자리로 모으고 clamped를 true로 돌려준다.
        /// </summary>
        private static bool ShiftSpriteGeometry(TextureImporter ti, TrimPlan plan, out bool clamped)
        {
            clamped = false;
            RectInt b = plan.Bounds;
            var offset = new Vector2(plan.SourceWidth * 0.5f - (b.x + b.width * 0.5f), plan.SourceHeight * 0.5f - (b.y + b.height * 0.5f));
            var half = new Vector2(b.width * 0.5f, b.height * 0.5f);
            bool any;
            using (var so = new SerializedObject(ti))
            {
                any = ShiftPaths(so.FindProperty(SpOutline), offset, half, ref clamped);
                any |= ShiftPaths(so.FindProperty(SpPhysics), offset, half, ref clamped);
                if (any) so.ApplyModifiedPropertiesWithoutUndo();
            }
            return any;
        }

        private static bool ShiftPaths(SerializedProperty paths, Vector2 offset, Vector2 half, ref bool clamped)
        {
            if (paths == null || !paths.isArray || paths.arraySize == 0) return false;
            for (int i = 0; i < paths.arraySize; i++)
            {
                SerializedProperty path = paths.GetArrayElementAtIndex(i);
                if (!path.isArray) continue;
                for (int k = 0; k < path.arraySize; k++)
                {
                    SerializedProperty pt = path.GetArrayElementAtIndex(k);
                    Vector2 v = pt.vector2Value + offset;
                    var c = new Vector2(Mathf.Clamp(v.x, -half.x, half.x), Mathf.Clamp(v.y, -half.y, half.y));
                    if (c != v) clamped = true;
                    pt.vector2Value = c;
                }
            }
            return true;
        }

        // ───────── 처리 ─────────

        public enum PlannedAction { Rewrite, ImporterOnly, Unchanged, Skip, Error }

        /// <summary>DryRun이 돌려주는 파일 하나의 예정 변경. 처리(Process)와 같은 판정 경로를 쓴다.</summary>
        public sealed class PlannedFile
        {
            public string Path;
            public PlannedAction Action;
            /// <summary>Skip/Error일 때 이유.</summary>
            public string Reason;
            public int SourceWidth, SourceHeight, ResultWidth, ResultHeight;
            /// <summary>투명해지는 픽셀 수 (JPG는 알파를 저장하지 않으므로 0).</summary>
            public int BackgroundPixelsRemoved;
            /// <summary>완전히 불투명한 원본에서 배경 색 제거로 픽셀이 지워진다 (전면 배경·패널·타일이 깎일 수 있음).</summary>
            public bool OpaqueSourceLosesPixels;
            /// <summary>JPG를 다시 압축한다.</summary>
            public bool Lossy;
            public bool ConvertToSprite;
            public readonly List<string> Notes = new List<string>();
        }

        public sealed class DryRunResult
        {
            public readonly List<PlannedFile> Files = new List<PlannedFile>();
            public bool Cancelled;

            public int Count(PlannedAction a)
            {
                int n = 0;
                for (int i = 0; i < Files.Count; i++) if (Files[i].Action == a) n++;
                return n;
            }
        }

        // 파일 하나의 계획. DryRun과 Process가 함께 쓴다(부수 효과 없음).
        private sealed class OnePlan
        {
            public string Skip;
            public TextureImporter Importer;
            public bool WasSprite;
            public EncodeFormat Fmt;
            public DecodedImage Img;
            public TrimPlan Trim;           // 파일을 다시 쓸 때만 non-null
            public TextureImporterSettings Ts;
            public bool ImporterChanged;
            public bool ShiftGeometry;      // 커스텀 Outline/Physics Shape 이동 필요
            public readonly List<Issue> Notes = new List<Issue>();
        }

        private static OnePlan PlanOne(string path, Settings s)
        {
            var op = new OnePlan { Skip = GetSkipReason(path) };
            if (op.Skip != null) return op;

            op.Importer = (TextureImporter)AssetImporter.GetAtPath(path);
            op.WasSprite = op.Importer.textureType == TextureImporterType.Sprite;

            // 1) 픽셀 계획
            if (s.modifyImageFiles)
            {
                op.Fmt = GetEncodeFormat(path);
                string blocker = GetRewriteBlocker(path);
                if (blocker != null) op.Notes.Add(new Issue(path, blocker));
                else
                {
                    DecodedImage img = Decode(ToFullPath(path), op.Fmt, out string decodeError);
                    if (img == null) op.Notes.Add(new Issue(path, "원본을 디코드하지 못해 파일을 유지합니다: " + decodeError));
                    else
                    {
                        TrimPlan plan = ComputePlan(img, s);
                        if (plan.FullyTransparent)
                            op.Notes.Add(new Issue(path, "배경 제거 후 남는 픽셀이 없어 파일을 유지합니다. Tolerance/Key Color를 확인하세요."));
                        else if (plan.NeedsRewrite(op.Fmt))
                        {
                            op.Trim = plan;
                            op.Img = img;
                        }
                    }
                }
            }

            // 2) 임포터 계획
            op.Ts = new TextureImporterSettings();
            op.Importer.ReadTextureSettings(op.Ts);
            var before = Snapshot(op.Ts);
            BuildImporterSettings(op.Ts, op.WasSprite, op.Trim, s, path, op.Notes);
            op.ImporterChanged = !Snapshot(op.Ts).Equals(before);

            if (op.Trim != null && op.Trim.Trimmed)
            {
                SpriteDataInfo g = ReadSpriteData(op.Importer);
                if (g.OutlinePaths > 0 || g.PhysicsPaths > 0)
                {
                    op.ShiftGeometry = true;
                    op.ImporterChanged = true;
                    op.Notes.Add(new Issue(path, "커스텀 Outline/Physics Shape를 잘린 만큼 옮깁니다. Sprite Editor에서 확인하세요."));
                }
            }
            return op;
        }

        /// <summary>
        /// 아무것도 쓰지 않고 Process가 무엇을 바꿀지 계산한다. 확인 대화상자에 정확한 변경 목록을 보여 주는 데 쓴다.
        /// </summary>
        public static DryRunResult DryRun(IList<string> assetPaths, Settings settings, bool showProgress = true)
        {
            if (settings == null) settings = new Settings();
            var dr = new DryRunResult();
            if (assetPaths == null || assetPaths.Count == 0) return dr;
            var seen = new HashSet<string>();
            try
            {
                for (int i = 0; i < assetPaths.Count; i++)
                {
                    string path = assetPaths[i];
                    if (!seen.Add(path)) continue;
                    if (showProgress && EditorUtility.DisplayCancelableProgressBar("Auto Sprite Slicer",
                            $"변경 내용 확인 중 ({i + 1}/{assetPaths.Count}): {path}", (float)i / assetPaths.Count))
                    {
                        dr.Cancelled = true;
                        break;
                    }

                    var pf = new PlannedFile { Path = path };
                    try
                    {
                        OnePlan op = PlanOne(path, settings);
                        foreach (var n in op.Notes) pf.Notes.Add(n.Reason);
                        if (op.Skip != null) { pf.Action = PlannedAction.Skip; pf.Reason = op.Skip; }
                        else
                        {
                            if (op.Trim != null)
                            {
                                TrimPlan t = op.Trim;
                                pf.Action = PlannedAction.Rewrite;
                                pf.SourceWidth = t.SourceWidth;
                                pf.SourceHeight = t.SourceHeight;
                                pf.ResultWidth = t.Trimmed ? t.Bounds.width : t.SourceWidth;
                                pf.ResultHeight = t.Trimmed ? t.Bounds.height : t.SourceHeight;
                                pf.Lossy = op.Fmt == EncodeFormat.Jpg;
                                pf.BackgroundPixelsRemoved = pf.Lossy ? 0 : t.AlphaChanged;
                                pf.OpaqueSourceLosesPixels = t.SourceOpaque && t.AlphaChanged > 0;
                            }
                            else pf.Action = op.ImporterChanged ? PlannedAction.ImporterOnly : PlannedAction.Unchanged;
                            pf.ConvertToSprite = !op.WasSprite && pf.Action != PlannedAction.Unchanged;
                        }
                    }
                    catch (Exception ex)
                    {
                        pf.Action = PlannedAction.Error;
                        pf.Reason = ex.GetType().Name + ": " + ex.Message;
                    }
                    dr.Files.Add(pf);
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
            return dr;
        }

        /// <summary>
        /// 경로 목록을 처리한다. 파일마다 독립적으로 실패를 기록하고 나머지는 계속 진행한다.
        /// </summary>
        public static Result Process(IList<string> assetPaths, Settings settings, bool showProgress = true)
        {
            if (settings == null) settings = new Settings();
            var result = new Result();
            if (assetPaths == null || assetPaths.Count == 0) return result;

            string backupDir = null;
            var seen = new HashSet<string>(); // 같은 경로가 두 번 오면 두 번째 백업이 원본 사본을 덮어쓰지 않게
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < assetPaths.Count; i++)
                {
                    string path = assetPaths[i];
                    if (!seen.Add(path)) continue;
                    if (showProgress && EditorUtility.DisplayCancelableProgressBar("Auto Sprite Slicer",
                            $"처리 중 ({i + 1}/{assetPaths.Count}): {path}", (float)i / assetPaths.Count))
                    {
                        result.Cancelled = true;
                        break;
                    }

                    try
                    {
                        ProcessOne(path, settings, result, ref backupDir);
                    }
                    catch (Exception ex)
                    {
                        result.Failed.Add(new Issue(path, ex.GetType().Name + ": " + ex.Message));
                        Debug.LogError(LogPrefix + path + " 처리 실패\n" + ex);
                    }
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
                AssetDatabase.StopAssetEditing();
            }

            result.BackupFolder = backupDir;
            return result;
        }

        private static void ProcessOne(string path, Settings s, Result result, ref string backupDir)
        {
            OnePlan op = PlanOne(path, s);
            result.Notes.AddRange(op.Notes);
            if (op.Skip != null) { result.Skipped.Add(new Issue(path, op.Skip)); return; }

            byte[] newBytes = null;
            if (op.Trim != null)
            {
                newBytes = Encode(op.Trim, op.Img, op.Fmt, s.jpgQuality);
                if (newBytes == null || newBytes.Length == 0) throw new InvalidOperationException("인코딩 결과가 비어 있습니다.");
            }
            op.Img = null;

            if (newBytes == null && !op.ImporterChanged) { result.Unchanged.Add(path); return; }

            // 백업(+목록 기록) → 쓰기 → 임포트. 목록은 백업 직후 바로 추가해 도중에 에디터가 죽어도 복원할 수 있게 한다.
            if (s.backupOriginals)
            {
                if (backupDir == null) backupDir = CreateBackupDir();
                BackupAsset(backupDir, path, newBytes != null);
                AppendManifest(backupDir, path, newBytes != null);
            }

            if (newBytes != null) WriteReplacing(ToFullPath(path), newBytes);

            if (op.ImporterChanged)
            {
                op.Importer.SetTextureSettings(op.Ts);
                if (op.ShiftGeometry && ShiftSpriteGeometry(op.Importer, op.Trim, out bool clamped) && clamped)
                    result.Notes.Add(new Issue(path, "커스텀 Outline/Physics Shape 일부가 새 크기 밖이라 가장자리로 모았습니다. Sprite Editor에서 다시 확인하세요."));
                EditorUtility.SetDirty(op.Importer);
                op.Importer.SaveAndReimport();
            }
            if (newBytes != null) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (newBytes != null) result.Rewritten.Add(path);
            else result.ImporterOnly.Add(path);
            if (!op.WasSprite) result.ConvertedToSprite.Add(path);
        }

        // ───────── 임포터 ─────────

        private struct ImporterSnapshot : IEquatable<ImporterSnapshot>
        {
            public TextureImporterType Type;
            public int SpriteMode, Alignment;
            public float Ppu;
            public Vector2 Pivot;
            public Vector4 Border;
            public bool AlphaIsTransparency;

            public bool Equals(ImporterSnapshot o) =>
                Type == o.Type && SpriteMode == o.SpriteMode && Alignment == o.Alignment && Ppu.Equals(o.Ppu) &&
                Pivot == o.Pivot && Border == o.Border && AlphaIsTransparency == o.AlphaIsTransparency;
            public override bool Equals(object obj) => obj is ImporterSnapshot o && Equals(o);
            public override int GetHashCode() => Alignment ^ SpriteMode ^ (int)Type;
        }

        private static ImporterSnapshot Snapshot(TextureImporterSettings ts) => new ImporterSnapshot
        {
            Type = ts.textureType, SpriteMode = ts.spriteMode, Alignment = ts.spriteAlignment, Ppu = ts.spritePixelsPerUnit,
            Pivot = ts.spritePivot, Border = ts.spriteBorder, AlphaIsTransparency = ts.alphaIsTransparency
        };

        private static void BuildImporterSettings(TextureImporterSettings ts, bool wasSprite, TrimPlan plan, Settings s, string path, List<Issue> notes)
        {
            if (!wasSprite)
            {
                // 인스펙터에서 Texture Type을 Sprite로 바꿀 때와 같은 기본값(밉맵 끔 등)을 적용한다.
                ts.ApplyTextureType(TextureImporterType.Sprite);
                ts.textureType = TextureImporterType.Sprite;
                ts.spriteMode = (int)SpriteImportMode.Single;
                ts.spritePixelsPerUnit = Mathf.Max(0.0001f, s.newSpritePixelsPerUnit);
                ts.spriteAlignment = (int)s.newSpriteAlignment;
                ts.spritePivot = s.newSpriteAlignment == SpriteAlignment.Custom ? s.newSpriteCustomPivot : AlignmentToPivot(s.newSpriteAlignment);
                ts.alphaIsTransparency = true;
                return;
            }

            // 이미 Sprite: PPU·피벗은 사용자가 정한 값이므로 유지한다.
            if (ts.spriteMode != (int)SpriteImportMode.Single) ts.spriteMode = (int)SpriteImportMode.Single;
            if (plan == null) return;
            if (plan.AlphaChanged > 0) ts.alphaIsTransparency = true;
            if (!plan.Trimmed) return;

            RectInt b = plan.Bounds;
            int w = plan.SourceWidth, h = plan.SourceHeight;

            // 9-slice 보더: 잘려 나간 만큼 줄이고, 남는 영역보다 크면 비운다.
            Vector4 border = ts.spriteBorder; // x=left, y=bottom, z=right, w=top
            if (border != Vector4.zero)
            {
                float l = border.x - b.x, bo = border.y - b.y;
                float r = border.z - (w - b.xMax), t = border.w - (h - b.yMax);
                bool clamped = l < 0 || bo < 0 || r < 0 || t < 0;
                var nb = new Vector4(Mathf.Max(0, l), Mathf.Max(0, bo), Mathf.Max(0, r), Mathf.Max(0, t));
                if (nb.x + nb.z > b.width || nb.y + nb.w > b.height)
                {
                    ts.spriteBorder = Vector4.zero;
                    notes.Add(new Issue(path, $"9-slice 보더 {border}가 트림 후 크기({b.width}x{b.height})를 넘어 비웠습니다. 다시 설정하세요."));
                }
                else
                {
                    ts.spriteBorder = nb;
                    notes.Add(new Issue(path, clamped
                        ? $"9-slice 보더가 트림 영역에 걸려 {border} → {nb}로 보정했습니다. 확인하세요."
                        : $"9-slice 보더를 트림 오프셋만큼 옮겼습니다 ({border} → {nb})."));
                }
            }

            if (s.keepPivotPosition)
            {
                var align = (SpriteAlignment)ts.spriteAlignment;
                Vector2 pivotN = align == SpriteAlignment.Custom ? ts.spritePivot : AlignmentToPivot(align);
                var newPivot = new Vector2((pivotN.x * w - b.x) / b.width, (pivotN.y * h - b.y) / b.height);
                if ((newPivot - pivotN).sqrMagnitude > 1e-8f)
                {
                    ts.spriteAlignment = (int)SpriteAlignment.Custom;
                    ts.spritePivot = newPivot;
                    notes.Add(new Issue(path, $"피벗이 같은 픽셀 위치에 남도록 Custom ({newPivot.x:0.###}, {newPivot.y:0.###})로 옮겼습니다."));
                }
            }
        }

        public static Vector2 AlignmentToPivot(SpriteAlignment a)
        {
            switch (a)
            {
                case SpriteAlignment.TopLeft: return new Vector2(0f, 1f);
                case SpriteAlignment.TopCenter: return new Vector2(0.5f, 1f);
                case SpriteAlignment.TopRight: return new Vector2(1f, 1f);
                case SpriteAlignment.LeftCenter: return new Vector2(0f, 0.5f);
                case SpriteAlignment.RightCenter: return new Vector2(1f, 0.5f);
                case SpriteAlignment.BottomLeft: return new Vector2(0f, 0f);
                case SpriteAlignment.BottomCenter: return new Vector2(0.5f, 0f);
                case SpriteAlignment.BottomRight: return new Vector2(1f, 0f);
                default: return new Vector2(0.5f, 0.5f);
            }
        }

        // ───────── 백업 / 복원 ─────────

        public static string BackupRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupRootRelative));

        private static string CreateBackupDir()
        {
            EnsureBackupIgnoreFile();
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string dir = Path.Combine(BackupRoot, stamp);
            int n = 1;
            while (Directory.Exists(dir)) dir = Path.Combine(BackupRoot, stamp + "_" + (n++));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ManifestName), string.Empty);
            return dir;
        }

        // 백업은 원본 이미지 전체 사본이라 저장소에 올라가지 않게 한다. 프로젝트의 .gitignore는 건드리지 않고 백업 폴더 안에 둔다.
        private static void EnsureBackupIgnoreFile()
        {
            try
            {
                string top = Path.GetDirectoryName(BackupRoot); // <프로젝트>/TelleRBackups
                if (string.IsNullOrEmpty(top)) return;
                Directory.CreateDirectory(top);
                string gi = Path.Combine(top, ".gitignore");
                if (!File.Exists(gi)) File.WriteAllText(gi, "# TelleR 도구 백업(원본 사본) - 버전 관리에서 제외\n*\n");
            }
            catch (Exception ex)
            {
                Debug.LogWarning(LogPrefix + "백업 폴더의 .gitignore를 만들지 못했습니다: " + ex.Message);
            }
        }

        private static string BackupPathOf(string backupDir, string assetPath) =>
            Path.Combine(backupDir, assetPath.Replace('/', Path.DirectorySeparatorChar));

        private static void BackupAsset(string backupDir, string assetPath, bool includeFile)
        {
            string full = ToFullPath(assetPath);
            string dst = BackupPathOf(backupDir, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (includeFile) File.Copy(full, dst, true);
            if (File.Exists(full + ".meta")) File.Copy(full + ".meta", dst + ".meta", true);
        }

        private static void AppendManifest(string backupDir, string assetPath, bool withFile) =>
            File.AppendAllText(Path.Combine(backupDir, ManifestName), assetPath + "\t" + (withFile ? "file+meta" : "meta") + "\n");

        /// <summary>가장 최근 백업 폴더(목록에 항목이 있는 것). 없으면 null.</summary>
        public static string FindLatestBackup()
        {
            string root = BackupRoot;
            if (!Directory.Exists(root)) return null;
            var dirs = Directory.GetDirectories(root);
            Array.Sort(dirs, StringComparer.Ordinal);
            for (int i = dirs.Length - 1; i >= 0; i--)
                if (ReadBackupManifest(dirs[i]).Count > 0) return dirs[i];
            return null;
        }

        /// <summary>백업 폴더의 에셋 경로 목록.</summary>
        public static List<string> ReadBackupManifest(string backupDir)
        {
            var list = new List<string>();
            string mf = Path.Combine(backupDir, ManifestName);
            if (!File.Exists(mf)) return list;
            foreach (var line in File.ReadAllLines(mf))
            {
                int tab = line.IndexOf('\t');
                string p = tab >= 0 ? line.Substring(0, tab) : line;
                if (!string.IsNullOrEmpty(p)) list.Add(p);
            }
            return list;
        }

        /// <summary>백업 항목 하나. 에셋은 백업 .meta의 GUID로 찾으므로 그 사이 이동·이름 변경돼도 따라간다.</summary>
        public struct BackupEntry
        {
            /// <summary>백업할 때의 에셋 경로.</summary>
            public string RecordedPath;
            /// <summary>지금 그 GUID의 에셋이 있는 경로. 복원할 수 없으면 null.</summary>
            public string CurrentPath;
            /// <summary>복원하지 않는 이유. 복원 가능하면 null.</summary>
            public string Problem;
            public bool Moved => CurrentPath != null && CurrentPath != RecordedPath;
        }

        public static List<BackupEntry> ReadBackupEntries(string backupDir)
        {
            var list = new List<BackupEntry>();
            foreach (var p in ReadBackupManifest(backupDir))
            {
                var e = new BackupEntry { RecordedPath = p };
                string guid = ReadMetaGuid(BackupPathOf(backupDir, p) + ".meta");
                if (guid == null) e.Problem = "백업에 .meta(GUID)가 없어 원래 에셋을 확인할 수 없습니다.";
                else
                {
                    string cur = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(cur) || !File.Exists(ToFullPath(cur)))
                        e.Problem = "원래 에셋이 삭제되었거나 찾을 수 없습니다.";
                    else if (!string.Equals(Path.GetExtension(cur), Path.GetExtension(p), StringComparison.OrdinalIgnoreCase))
                        e.Problem = $"에셋 확장자가 바뀌었습니다({cur}).";
                    else e.CurrentPath = cur;
                }
                list.Add(e);
            }
            return list;
        }

        private static string ReadMetaGuid(string metaPath)
        {
            if (!File.Exists(metaPath)) return null;
            foreach (var line in File.ReadAllLines(metaPath))
            {
                if (!line.StartsWith("guid:", StringComparison.Ordinal)) continue;
                string g = line.Substring(5).Trim();
                return g.Length == 32 ? g : null;
            }
            return null;
        }

        /// <summary>
        /// 백업의 파일·.meta를 지금 그 에셋(GUID 기준)이 있는 자리로 되돌리고 다시 임포트한다. 복원한 개수를 돌려준다.
        /// 에셋이 삭제됐거나 찾을 수 없으면 쓰지 않고 failures에 남긴다(다른 경로가 가진 GUID의 .meta를 만들지 않는다).
        /// </summary>
        public static int RestoreBackup(string backupDir, List<Issue> failures)
        {
            var entries = ReadBackupEntries(backupDir);
            var imported = new List<string>();
            int restored = 0;
            try
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    BackupEntry e = entries[i];
                    if (e.Problem != null)
                    {
                        failures?.Add(new Issue(e.RecordedPath, e.Problem + " 복원하지 않았습니다."));
                        continue;
                    }
                    EditorUtility.DisplayProgressBar("Auto Sprite Slicer", $"복원 중 ({i + 1}/{entries.Count}): {e.CurrentPath}", (float)i / entries.Count);
                    try
                    {
                        string src = BackupPathOf(backupDir, e.RecordedPath);
                        string dst = ToFullPath(e.CurrentPath);
                        if (File.Exists(src)) File.Copy(src, dst, true);
                        File.Copy(src + ".meta", dst + ".meta", true);
                        imported.Add(e.CurrentPath);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        failures?.Add(new Issue(e.CurrentPath, ex.Message));
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // .meta가 바깥에서 바뀌었으므로 Refresh로 감지시키고, 확실히 하려고 개별 강제 임포트도 한다.
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var p in imported) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }
            return restored;
        }

        // ───────── 파일 입출력 ─────────

        /// <summary>에셋 경로("Assets/..", "Packages/..")를 실제 디스크 경로로. 로컬 패키지도 해석된다.</summary>
        internal static string ToFullPath(string assetPath) => Path.GetFullPath(assetPath);

        private static byte[] ReadHead(string fullPath, int count)
        {
            try
            {
                using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var buf = new byte[Math.Min(count, (int)Math.Min(int.MaxValue, fs.Length))];
                    int read = 0;
                    while (read < buf.Length)
                    {
                        int n = fs.Read(buf, read, buf.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read < buf.Length) Array.Resize(ref buf, read);
                    return buf;
                }
            }
            catch (Exception) { return null; }
        }

        // 임시 파일에 먼저 다 쓴 뒤 교체한다. 쓰기 도중 실패해도 원본이 반쯤 잘린 채 남지 않는다.
        private static void WriteReplacing(string fullPath, byte[] bytes)
        {
            string tmp = fullPath + ".slicer.tmp~"; // '~'로 끝나는 파일은 Unity가 임포트하지 않는다
            File.WriteAllBytes(tmp, bytes);
            try
            {
                File.Replace(tmp, fullPath, null);
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException || ex is IOException)
            {
                File.Copy(tmp, fullPath, true);
                File.Delete(tmp);
            }
            finally
            {
                if (File.Exists(tmp)) { try { File.Delete(tmp); } catch (Exception) { /* 무시 */ } }
            }
        }

        // ───────── 디코드 / 계획 / 인코드 ─────────

        internal sealed class DecodedImage
        {
            public Color32[] Pixels; // 아래 행부터 (Unity 규약)
            public int Width, Height;
            public bool SourceHasAlpha;   // PNG 색 타입 / TGA 32bpp
            public bool SourceGray;       // TGA 흑백
            public bool SourceRle;        // TGA RLE
        }

        internal sealed class TrimPlan
        {
            public int SourceWidth, SourceHeight;
            public Color32[] Work;      // 배경 제거가 반영된 원본 크기 픽셀
            public int AlphaChanged;    // 실제로 알파가 바뀐 픽셀 수
            public RectInt Bounds;      // 트림 영역 (원본 좌표, 아래가 y=0)
            public bool Trimmed;
            public bool FullyTransparent;
            public int MaxEdgeAlpha;
            public bool SourceOpaque;              // 원본이 완전 불투명 (배경 제거가 전면 이미지를 깎을 수 있음)
            public bool KeyRemovalSkippedHasAlpha; // 이미 투명 픽셀이 있어 자동 배경 색 제거를 건너뜀

            public bool NeedsRewrite(EncodeFormat fmt) =>
                fmt == EncodeFormat.Jpg ? Trimmed : (Trimmed || AlphaChanged > 0); // JPG는 알파가 없어 트림될 때만 다시 쓴다
        }

        /// <summary>한 번에 디코드하는 최대 픽셀 수(16384 x 16384). 헤더가 비정상적으로 크면 메모리를 잡기 전에 거부한다.</summary>
        internal const long MaxDecodePixels = 16384L * 16384L;

        internal static DecodedImage Decode(string fullPath, EncodeFormat fmt, out string error)
        {
            error = null;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(fullPath); }
            catch (Exception ex) { error = ex.Message; return null; }

            if (fmt == EncodeFormat.Tga)
            {
                var px = TgaCodec.Decode(bytes, out int w, out int h, out int bpp, out bool rle, out bool gray, out error);
                if (px == null) return null;
                return new DecodedImage { Pixels = px, Width = w, Height = h, SourceHasAlpha = bpp == 32, SourceGray = gray, SourceRle = rle };
            }

            if (fmt == EncodeFormat.Png && bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == (byte)'P')
            {
                // IHDR 크기(빅엔디언 4바이트씩)를 먼저 확인해 손상된 헤더로 거대한 텍스처를 만들지 않는다.
                long pw = ((long)bytes[16] << 24) | ((long)bytes[17] << 16) | ((long)bytes[18] << 8) | bytes[19];
                long ph = ((long)bytes[20] << 24) | ((long)bytes[21] << 16) | ((long)bytes[22] << 8) | bytes[23];
                if (pw * ph > MaxDecodePixels) { error = $"PNG 크기({pw}x{ph})가 처리 한도(픽셀 {MaxDecodePixels:N0}개)를 넘습니다."; return null; }
            }

            if (fmt == EncodeFormat.Png || fmt == EncodeFormat.Jpg)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!tex.LoadImage(bytes, false)) { error = "이미지 데이터를 읽지 못했습니다."; return null; }
                    bool hasAlpha = fmt == EncodeFormat.Png && (bytes.Length <= 25 || (bytes[25] != 0 && bytes[25] != 2)); // 0=Gray, 2=RGB
                    return new DecodedImage { Pixels = tex.GetPixels32(), Width = tex.width, Height = tex.height, SourceHasAlpha = hasAlpha };
                }
                finally { UnityEngine.Object.DestroyImmediate(tex); }
            }

            error = "지원하지 않는 포맷입니다.";
            return null;
        }

        internal static TrimPlan ComputePlan(DecodedImage img, Settings s)
        {
            int w = img.Width, h = img.Height;
            var plan = new TrimPlan { SourceWidth = w, SourceHeight = h, Work = (Color32[])img.Pixels.Clone(), SourceOpaque = AllOpaque(img.Pixels) };

            // 자동 키 모드에서 이미 알파를 쓰는 이미지는 배경이 제거된 것으로 본다.
            // (이미 트림된 스프라이트의 모서리는 내용물이라, 그 색을 키로 쓰면 다시 실행할 때마다 가장자리가 깎인다)
            if (s.removeBackground && s.autoDetectKeys && !plan.SourceOpaque) plan.KeyRemovalSkippedHasAlpha = true;
            else if (s.removeBackground)
            {
                Color32[] keys = s.autoDetectKeys ? GetCornerColors32(img.Pixels, w, h) : new[] { (Color32)s.keyColor1, (Color32)s.keyColor2 };
                byte tolByte = (byte)Mathf.Clamp(Mathf.RoundToInt(s.colorTolerance * 255f), 0, 255);
                plan.AlphaChanged = RemoveBackgroundFlood(plan.Work, w, h, keys, tolByte);
            }
            plan.MaxEdgeAlpha = ComputeMaxEdgeAlpha(plan.Work, w, h);

            if (!FindOpaqueBounds(plan.Work, w, h, (byte)Mathf.Clamp(s.alphaThreshold, 0, 255), out RectInt bounds))
            {
                plan.FullyTransparent = true;
                return plan;
            }
            plan.Bounds = ApplyPadding(bounds, w, h, s.padding);
            plan.Trimmed = !(plan.Bounds.width == w && plan.Bounds.height == h);
            return plan;
        }

        /// <summary>계획을 적용한 결과 픽셀. JPG는 알파를 저장할 수 없으므로 원본 픽셀을 자른다.</summary>
        internal static Color32[] ResultPixels(TrimPlan plan, DecodedImage img, EncodeFormat fmt, out int ow, out int oh)
        {
            Color32[] src = fmt == EncodeFormat.Jpg ? img.Pixels : plan.Work;
            if (plan.Trimmed)
            {
                ow = plan.Bounds.width; oh = plan.Bounds.height;
                return Crop(src, plan.SourceWidth, plan.Bounds);
            }
            ow = plan.SourceWidth; oh = plan.SourceHeight;
            return src;
        }

        private static byte[] Encode(TrimPlan plan, DecodedImage img, EncodeFormat fmt, int jpgQuality)
        {
            Color32[] px = ResultPixels(plan, img, fmt, out int w, out int h);
            bool allOpaque = AllOpaque(px);

            if (fmt == EncodeFormat.Tga)
            {
                int bpp = (img.SourceHasAlpha || !allOpaque) ? 32 : (img.SourceGray ? 8 : 24);
                return TgaCodec.Encode(px, w, h, bpp, img.SourceRle);
            }

            // 원본에 알파 채널이 없고 결과도 불투명이면 RGB로 저장해 임포트 결과(압축 포맷)가 바뀌지 않게 한다.
            bool rgb = fmt == EncodeFormat.Jpg || (!img.SourceHasAlpha && allOpaque);
            var tex = new Texture2D(w, h, rgb ? TextureFormat.RGB24 : TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(px);
                tex.Apply(false, false);
                return fmt == EncodeFormat.Jpg ? tex.EncodeToJPG(Mathf.Clamp(jpgQuality, 1, 100)) : tex.EncodeToPNG();
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        private static bool AllOpaque(Color32[] px)
        {
            for (int i = 0; i < px.Length; i++) if (px[i].a != 255) return false;
            return true;
        }

        internal static Color32[] Crop(Color32[] src, int srcW, RectInt b)
        {
            var dst = new Color32[b.width * b.height];
            for (int y = 0; y < b.height; y++)
                Array.Copy(src, (b.y + y) * srcW + b.x, dst, y * b.width, b.width);
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
            for (int y = 0; y < h && minY < 0; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                    if (px[row + x].a >= alphaThreshold) { minY = y; break; }
            }
            if (minY < 0) { bounds = default; return false; }

            int maxY = minY;
            for (int y = h - 1; y > minY && maxY == minY; y--)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                    if (px[row + x].a >= alphaThreshold) { maxY = y; break; }
            }

            int minX = -1;
            for (int x = 0; x < w && minX < 0; x++)
                for (int y = minY; y <= maxY; y++)
                    if (px[y * w + x].a >= alphaThreshold) { minX = x; break; }

            int maxX = minX;
            for (int x = w - 1; x > minX && maxX == minX; x--)
                for (int y = minY; y <= maxY; y++)
                    if (px[y * w + x].a >= alphaThreshold) { maxX = x; break; }

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
                if (c.a == 0) continue; // 투명 코너의 RGB는 배경키가 아님 (오브젝트 동일색 침식 방지)
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

        // 스캔라인 flood fill: 가장자리 시드 → 좌우 런 확장 → 위/아래 행 enqueue.
        // 반환값은 알파가 실제로 바뀐 픽셀 수 (이미 투명한 픽셀은 세지 않음).
        private static int RemoveBackgroundFlood(Color32[] px, int w, int h, Color32[] keys, byte tolByte)
        {
            if (keys == null || keys.Length == 0) return 0;
            bool[] visited = new bool[w * h];
            var stack = new Stack<int>(1024);

            for (int x = 0; x < w; x++) { SeedScanline(x, 0, w, px, visited, stack, keys, tolByte); SeedScanline(x, h - 1, w, px, visited, stack, keys, tolByte); }
            for (int y = 1; y < h - 1; y++) { SeedScanline(0, y, w, px, visited, stack, keys, tolByte); SeedScanline(w - 1, y, w, px, visited, stack, keys, tolByte); }

            int changed = 0;
            while (stack.Count > 0)
            {
                int seed = stack.Pop();
                int sy = seed / w;
                int sx = seed % w;
                int rowStart = sy * w;

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
                    if (c.a != 0) { c.a = 0; px[idx] = c; changed++; }
                }

                AddScanlineSeeds(sy - 1, left, right, w, h, px, visited, stack, keys, tolByte);
                AddScanlineSeeds(sy + 1, left, right, w, h, px, visited, stack, keys, tolByte);
            }
            return changed;
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

        // ───────── TGA ─────────

        /// <summary>
        /// 최소 TGA 코덱: 트루컬러 24/32bpp, 흑백 8bpp, 비압축/RLE. 원본 해상도·비트를 그대로 읽고 쓴다.
        /// </summary>
        internal static class TgaCodec
        {
            public static string CheckHeader(byte[] d)
            {
                if (d == null || d.Length < 18) return "TGA 헤더가 너무 짧습니다.";
                int cmapType = d[1], type = d[2], bpp = d[16];
                int w = d[12] | (d[13] << 8), h = d[14] | (d[15] << 8);
                if (cmapType > 1) return "알 수 없는 TGA 컬러맵 형식입니다.";
                bool gray = type == 3 || type == 11;
                bool color = type == 2 || type == 10;
                if (!gray && !color) return $"지원하지 않는 TGA 형식(type {type}, 팔레트 등)입니다.";
                if (gray ? bpp != 8 : (bpp != 24 && bpp != 32)) return $"지원하지 않는 TGA 비트 수({bpp}bpp)입니다.";
                if (w == 0 || h == 0) return "TGA 크기가 0입니다.";
                if ((long)w * h > MaxDecodePixels) return $"TGA 크기({w}x{h})가 처리 한도(픽셀 {MaxDecodePixels:N0}개)를 넘습니다.";
                return null;
            }

            public static Color32[] Decode(byte[] d, out int w, out int h, out int bpp, out bool rle, out bool gray, out string error)
            {
                w = h = bpp = 0; rle = gray = false;
                error = CheckHeader(d);
                if (error != null) return null;

                int idLen = d[0], cmapType = d[1], type = d[2];
                int cmLen = d[5] | (d[6] << 8), cmEntry = d[7];
                w = d[12] | (d[13] << 8); h = d[14] | (d[15] << 8);
                bpp = d[16];
                int desc = d[17];
                rle = type == 10 || type == 11;
                gray = type == 3 || type == 11;

                int pos = 18 + idLen + (cmapType == 1 ? cmLen * ((cmEntry + 7) / 8) : 0);
                int bytesPP = bpp / 8;
                int total = w * h; // CheckHeader가 MaxDecodePixels 이하를 보장하므로 int 범위 안

                // 메모리를 잡기 전에 파일 길이로 헤더를 검증한다. RLE 패킷은 최대 128픽셀에 최소 (1 + bytesPP)바이트.
                long minBytes = rle ? (total + 127L) / 128L * (1 + bytesPP) : (long)total * bytesPP;
                if (pos + minBytes > d.Length)
                {
                    error = rle ? "TGA RLE 데이터가 잘려 있습니다." : "TGA 데이터가 잘려 있습니다.";
                    return null;
                }
                var filePx = new Color32[total]; // 파일 저장 순서

                if (!rle)
                {
                    for (int i = 0; i < total; i++, pos += bytesPP) filePx[i] = ReadPixel(d, pos, bytesPP);
                }
                else
                {
                    int i = 0;
                    while (i < total)
                    {
                        if (pos >= d.Length) { error = "TGA RLE 데이터가 잘려 있습니다."; return null; }
                        int hdr = d[pos++];
                        int count = (hdr & 0x7F) + 1;
                        if (i + count > total) count = total - i;
                        if ((hdr & 0x80) != 0)
                        {
                            if (pos + bytesPP > d.Length) { error = "TGA RLE 데이터가 잘려 있습니다."; return null; }
                            Color32 c = ReadPixel(d, pos, bytesPP);
                            pos += bytesPP;
                            for (int k = 0; k < count; k++) filePx[i++] = c;
                        }
                        else
                        {
                            if (pos + count * bytesPP > d.Length) { error = "TGA RLE 데이터가 잘려 있습니다."; return null; }
                            for (int k = 0; k < count; k++, pos += bytesPP) filePx[i++] = ReadPixel(d, pos, bytesPP);
                        }
                    }
                }

                // 원점 정규화: Unity 규약(아래 행부터, 왼쪽→오른쪽)
                bool topOrigin = (desc & 0x20) != 0;
                bool rightToLeft = (desc & 0x10) != 0;
                if (!topOrigin && !rightToLeft) return filePx;
                var outPx = new Color32[total];
                for (int r = 0; r < h; r++)
                {
                    int dy = topOrigin ? h - 1 - r : r;
                    for (int c = 0; c < w; c++)
                    {
                        int dx = rightToLeft ? w - 1 - c : c;
                        outPx[dy * w + dx] = filePx[r * w + c];
                    }
                }
                return outPx;
            }

            private static Color32 ReadPixel(byte[] d, int p, int bytesPP)
            {
                if (bytesPP == 1) return new Color32(d[p], d[p], d[p], 255);
                if (bytesPP == 3) return new Color32(d[p + 2], d[p + 1], d[p], 255);
                return new Color32(d[p + 2], d[p + 1], d[p], d[p + 3]);
            }

            /// <summary>아래-왼쪽 원점으로 쓴다. bpp: 8(흑백) / 24 / 32.</summary>
            public static byte[] Encode(Color32[] px, int w, int h, int bpp, bool rle)
            {
                int bytesPP = bpp / 8;
                bool gray = bpp == 8;
                var ms = new MemoryStream(18 + w * h * bytesPP + (rle ? w * h / 64 + h : 0));
                ms.WriteByte(0);                                     // id length
                ms.WriteByte(0);                                     // color map type
                ms.WriteByte((byte)(gray ? (rle ? 11 : 3) : (rle ? 10 : 2)));
                for (int i = 0; i < 9; i++) ms.WriteByte(0);         // color map spec(5) + x/y origin(4)
                ms.WriteByte((byte)(w & 0xFF)); ms.WriteByte((byte)(w >> 8));
                ms.WriteByte((byte)(h & 0xFF)); ms.WriteByte((byte)(h >> 8));
                ms.WriteByte((byte)bpp);
                ms.WriteByte((byte)(bpp == 32 ? 8 : 0));             // alpha bits, bottom-left origin

                if (!rle)
                {
                    for (int i = 0; i < px.Length; i++) WritePixel(ms, px[i], bytesPP);
                    return ms.ToArray();
                }

                // RLE 패킷은 행을 넘지 않게 만든다 (TGA 2.0 권장)
                for (int y = 0; y < h; y++)
                {
                    int row = y * w;
                    int x = 0;
                    while (x < w)
                    {
                        int run = 1;
                        while (x + run < w && run < 128 && Same(px[row + x], px[row + x + run], bytesPP)) run++;
                        if (run >= 2)
                        {
                            ms.WriteByte((byte)(0x80 | (run - 1)));
                            WritePixel(ms, px[row + x], bytesPP);
                            x += run;
                            continue;
                        }
                        int start = x;
                        while (x < w && x - start < 128)
                        {
                            if (x + 1 < w && Same(px[row + x], px[row + x + 1], bytesPP)) break;
                            x++;
                        }
                        if (x == start) x++; // 안전장치
                        ms.WriteByte((byte)(x - start - 1));
                        for (int k = start; k < x; k++) WritePixel(ms, px[row + k], bytesPP);
                    }
                }
                return ms.ToArray();
            }

            private static bool Same(Color32 a, Color32 b, int bytesPP)
            {
                if (bytesPP == 1) return a.r == b.r;
                return a.r == b.r && a.g == b.g && a.b == b.b && (bytesPP == 3 || a.a == b.a);
            }

            private static void WritePixel(Stream s, Color32 c, int bytesPP)
            {
                if (bytesPP == 1) { s.WriteByte(c.r); return; }
                s.WriteByte(c.b); s.WriteByte(c.g); s.WriteByte(c.r);
                if (bytesPP == 4) s.WriteByte(c.a);
            }
        }
    }
}
#endif
