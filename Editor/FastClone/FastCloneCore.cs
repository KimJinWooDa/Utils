using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TelleR.Util.FastClone
{
    [System.Serializable]
    public class CloneMeta
    {
        public string originalProjectPath;
        public long createdTime;
    }

    /// <summary>복사 진행 중 표식(.clone_pending). 도메인 리로드·에디터 재시작 뒤에도 진행 상태를 복구하는 데 쓴다.</summary>
    [System.Serializable]
    internal class PendingCloneInfo
    {
        public string originalProjectPath;
        public long createdTime;
        public int processId;
    }

    internal enum CloneState
    {
        Ready,      // 완료 표식 있음
        Copying,    // 이 에디터가 복사를 추적 중
        Incomplete  // 완료 표식 없음 (중단·실패·이전 버전 잔해)
    }

    internal struct CloneEntry
    {
        public string Path;
        public CloneState State;
        /// <summary>Fast Clone이 만든 폴더라는 흔적(표식 파일 또는 링크된 Assets)이 있는지.</summary>
        public bool HasCloneTrace;
    }

    [InitializeOnLoad]
    public static class FastCloneCore
    {
        public const string CloneSuffix = "_Clone_";
        public const string CloneMarkerFile = ".clone_marker";
        public const int MaxCloneCount = 10;

        internal const string PendingMarkerFile = ".clone_pending";
        internal const string ExitCodeFile = ".clone_exitcode";
        internal const string CopyLogFile = ".clone_copy.log";
        internal const string CopyScriptFile = ".clone_copy.cmd";

        private const string LogPrefix = "[TelleR/FastClone] ";
        private const string DialogTitle = "Fast Clone";
        private static readonly string[] LinkedFolders = { "Assets", "Packages", "ProjectSettings" };
        private static readonly string[] CopyProcessNames = { "cmd", "robocopy", "sh", "bash", "dash", "rsync", "cp" };
        private static readonly Regex RobocopyErrorLine = new Regex(@"\(0x[0-9A-Fa-f]{8}\)");
        // PID 재사용 대비: 프로세스 시작 시각이 .clone_pending 기록 시각과 이 범위 안이어야 우리가 띄운 복사 프로세스로 본다.
        private const double ProcessStartToleranceSeconds = 30.0;

        private static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor;

        // 복사 추적 상태. 도메인 리로드로 사라져도 .clone_pending(프로세스 ID)과 .clone_exitcode로 복구한다.
        private static Process currentProcess;
        private static int currentProcessId;
        private static long currentPendingTicks;
        private static System.Action onCompleteCallback;
        private static string pendingTargetPath;
        private static string pendingSourcePath;
        private static double copyStartTime;

        // 방금 연 클론을 잠금 파일이 생기기 전에 또 여는 것을 막는다.
        private static readonly Dictionary<string, double> recentLaunches = new Dictionary<string, double>();
        private const double LaunchGuardSeconds = 60.0;

        /// <summary>클론 목록·복사 상태가 바뀌면 호출된다(창 갱신용).</summary>
        internal static event System.Action StateChanged;

        static FastCloneCore()
        {
            EditorApplication.delayCall += ResumePendingCopies;
        }

        internal static bool IsCopying => pendingTargetPath != null;

        public static bool IsClone()
        {
            string current = GetCurrentProjectPath();
            return File.Exists(Path.Combine(current, CloneMarkerFile)) || File.Exists(Path.Combine(current, PendingMarkerFile));
        }

        public static string GetCurrentProjectPath()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        public static string GetOriginalProjectPath()
        {
            if (!IsClone()) return GetCurrentProjectPath();

            string current = GetCurrentProjectPath();
            try
            {
                string markerPath = Path.Combine(current, CloneMarkerFile);
                if (File.Exists(markerPath))
                {
                    CloneMeta meta = JsonUtility.FromJson<CloneMeta>(File.ReadAllText(markerPath));
                    if (meta != null && !string.IsNullOrEmpty(meta.originalProjectPath)) return meta.originalProjectPath;
                }
                PendingCloneInfo pending = ReadPending(current);
                if (pending != null && !string.IsNullOrEmpty(pending.originalProjectPath)) return pending.originalProjectPath;
            }
            catch { }
            return current;
        }

        /// <summary>완료된 클론만 반환한다(기존 동작 유지).</summary>
        public static List<string> GetAllClonePaths()
        {
            List<string> clones = new List<string>();
            foreach (var entry in GetCloneEntries())
                if (entry.State == CloneState.Ready) clones.Add(entry.Path);
            return clones;
        }

        /// <summary>클론 슬롯을 차지한 모든 폴더(완료·복사 중·불완전)를 반환한다.</summary>
        internal static List<CloneEntry> GetCloneEntries()
        {
            var entries = new List<CloneEntry>();
            string sourcePath = GetCurrentProjectPath();
            for (int i = 1; i <= MaxCloneCount; i++)
            {
                string path = sourcePath + CloneSuffix + i;
                if (!Directory.Exists(path)) continue;

                var entry = new CloneEntry { Path = path, HasCloneTrace = true };
                if (File.Exists(Path.Combine(path, CloneMarkerFile))) entry.State = CloneState.Ready;
                else if (SamePath(path, pendingTargetPath)) entry.State = CloneState.Copying;
                else
                {
                    entry.State = CloneState.Incomplete;
                    entry.HasCloneTrace = File.Exists(Path.Combine(path, PendingMarkerFile)) || IsLink(Path.Combine(path, "Assets"));
                }
                entries.Add(entry);
            }
            return entries;
        }

        // ─── 생성 ───

        public static void CreateNextClone(System.Action onComplete)
        {
            if (IsCopying)
            {
                EditorUtility.DisplayDialog(DialogTitle, "이미 클론 생성이 진행 중입니다.", "확인");
                return;
            }
            if (IsClone())
            {
                EditorUtility.DisplayDialog(DialogTitle, "클론 프로젝트에서는 클론을 만들 수 없습니다.\n원본 프로젝트에서 만드세요.", "확인");
                return;
            }

            string sourcePath = GetCurrentProjectPath();
            string targetPath = "";

            for (int i = 1; i <= MaxCloneCount; i++)
            {
                string potentialPath = sourcePath + CloneSuffix + i;
                if (!Directory.Exists(potentialPath))
                {
                    targetPath = potentialPath;
                    break;
                }
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    $"클론은 최대 {MaxCloneCount}개까지 만들 수 있습니다.\n쓰지 않는 클론(불완전 항목 포함)을 삭제한 뒤 다시 시도하세요.", "확인");
                return;
            }

            Process process = null;
            try
            {
                Directory.CreateDirectory(targetPath);
                // 링크·복사보다 먼저 진행 표식을 남긴다: 중간에 끊겨도 목록에 '불완전'으로 보이고 삭제할 수 있다.
                WritePending(targetPath, sourcePath, 0);

                foreach (string folder in LinkedFolders)
                    LinkFolder(sourcePath, targetPath, folder);

                string sourceLibrary = Path.Combine(sourcePath, "Library");
                string targetLibrary = Path.Combine(targetPath, "Library");
                process = StartCopyProcess(sourceLibrary, targetLibrary, targetPath);
                long pendingTicks = WritePending(targetPath, sourcePath, process.Id);

                BeginTracking(targetPath, sourcePath, process, process.Id, pendingTicks, EditorApplication.timeSinceStartup);
                onCompleteCallback = onComplete;
                Debug.Log(LogPrefix + $"클론 생성을 시작합니다: {targetPath}");
            }
            catch (System.Exception e)
            {
                if (process != null) KillProcessTree(process, process.Id);
                EditorUtility.ClearProgressBar();
                Debug.LogError(LogPrefix + $"클론 생성을 시작하지 못했습니다: {e.Message}");
                string cleanup = DeletePartialClone(targetPath, process != null ? 4 : 1, out string deleteError) ? "" : "\n\n만들던 폴더를 모두 지우지 못했습니다. 목록의 '불완전' 항목을 삭제하세요.\n" + deleteError;
                EditorUtility.DisplayDialog(DialogTitle, $"클론 생성을 시작하지 못했습니다.\n{e.Message}{cleanup}", "확인");
                StateChanged?.Invoke();
            }
        }

        /// <summary>Library 복사 프로세스를 시작한다. 종료 코드는 대상 폴더의 .clone_exitcode 파일로 남아 리로드 뒤에도 읽을 수 있다.</summary>
        internal static Process StartCopyProcess(string sourceLibrary, string targetLibrary, string cloneRoot)
        {
            string exitFile = Path.Combine(cloneRoot, ExitCodeFile);
            string logFile = Path.Combine(cloneRoot, CopyLogFile);
            if (File.Exists(exitFile)) File.Delete(exitFile);

            ProcessStartInfo startInfo;
            if (IsWindows)
            {
                // 경로는 명령줄이 아니라 환경 변수로 넘긴다: %, &, ', 한글 등이 섞인 경로도 cmd 해석에 걸리지 않는다.
                // /v:off: 레지스트리(Command Processor\DelayedExpansion)로 지연 확장이 켜져 있으면 경로의 '!'가 사라진다. 명령줄 스위치가 레지스트리보다 우선한다.
                // /R:1 /W:1: 에디터가 잠근 파일에서 기본값(100만 회 x 30초) 재시도로 멈추지 않게 한다.
                // /XF *-lock UnityLockfile: 실행 중 에디터의 잠금 파일은 클론에 필요 없고 잠겨 있을 수 있다.
                string script =
                    "@echo off\r\n" +
                    "robocopy \"%TELLER_FC_SRC%\" \"%TELLER_FC_DST%\" /E /R:1 /W:1 /XF *-lock UnityLockfile /MT:8 /NFL /NDL /NJH /NJS /NC /NS /NP \"/UNILOG:%TELLER_FC_LOG%\"\r\n" +
                    ">\"%TELLER_FC_EXIT%\" echo %ERRORLEVEL%\r\n";
                string scriptPath = Path.Combine(cloneRoot, CopyScriptFile);
                File.WriteAllText(scriptPath, script, Encoding.ASCII);

                startInfo = new ProcessStartInfo("cmd.exe", "/d /v:off /s /c \"\"%TELLER_FC_SCRIPT%\"\"");
                startInfo.EnvironmentVariables["TELLER_FC_SCRIPT"] = scriptPath;
            }
            else
            {
                string script =
                    "if command -v rsync >/dev/null 2>&1; then " +
                    "rsync -a \"--exclude=*-lock\" --exclude=UnityLockfile \"$TELLER_FC_SRC/\" \"$TELLER_FC_DST/\" 2>\"$TELLER_FC_LOG\"; " +
                    "else mkdir -p \"$TELLER_FC_DST\" && cp -a \"$TELLER_FC_SRC/.\" \"$TELLER_FC_DST/\" 2>\"$TELLER_FC_LOG\"; fi; " +
                    "echo $? > \"$TELLER_FC_EXIT\"";
                startInfo = ShellStartInfo(script);
            }

            startInfo.EnvironmentVariables["TELLER_FC_SRC"] = sourceLibrary;
            startInfo.EnvironmentVariables["TELLER_FC_DST"] = targetLibrary;
            startInfo.EnvironmentVariables["TELLER_FC_EXIT"] = exitFile;
            startInfo.EnvironmentVariables["TELLER_FC_LOG"] = logFile;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            var process = Process.Start(startInfo);
            if (process == null) throw new System.Exception("복사 프로세스를 시작하지 못했습니다.");
            return process;
        }

        private static void BeginTracking(string targetPath, string sourcePath, Process process, int processId, long pendingTicks, double startTime)
        {
            pendingTargetPath = targetPath;
            pendingSourcePath = sourcePath;
            currentProcess = process;
            currentProcessId = processId;
            currentPendingTicks = pendingTicks;
            copyStartTime = startTime;
            EditorApplication.update -= UpdateProcess;
            EditorApplication.update += UpdateProcess;
            StateChanged?.Invoke();
        }

        private static void EndTracking()
        {
            EditorApplication.update -= UpdateProcess;
            EditorUtility.ClearProgressBar();
            if (currentProcess != null)
            {
                try { currentProcess.Dispose(); } catch { }
            }
            currentProcess = null;
            currentProcessId = 0;
            currentPendingTicks = 0;
            pendingTargetPath = null;
            pendingSourcePath = null;
        }

        private static void UpdateProcess()
        {
            if (pendingTargetPath == null)
            {
                EndTracking();
                return;
            }

            bool finished = TryReadExitCode(pendingTargetPath, out int exitCode);
            if (!finished && IsTrackedProcessAlive())
            {
                double elapsed = EditorApplication.timeSinceStartup - copyStartTime;
                if (elapsed < 0) elapsed = 0;
                int minutes = (int)(elapsed / 60.0);
                int seconds = (int)(elapsed % 60.0);
                float anim = (float)(EditorApplication.timeSinceStartup % 1.0);
                if (EditorUtility.DisplayCancelableProgressBar(DialogTitle,
                        $"Library 복사 중... {minutes}분 {seconds:00}초 경과 (취소하면 만들던 클론을 삭제합니다)", anim))
                {
                    CancelCopy();
                }
                return;
            }

            // 프로세스가 끝났다. 종료 코드 파일은 종료 직전에 쓰이므로 한 번 더 확인한다.
            if (!finished) finished = TryReadExitCode(pendingTargetPath, out exitCode);

            string target = pendingTargetPath;
            string source = pendingSourcePath;
            System.Action callback = onCompleteCallback;
            onCompleteCallback = null;
            EndTracking();

            FinishCopy(target, source, finished ? exitCode : -1, callback);
        }

        private static bool IsTrackedProcessAlive()
        {
            if (currentProcess != null)
            {
                try { return !currentProcess.HasExited; }
                catch { }
            }
            return IsCopyProcessAlive(currentProcessId, currentPendingTicks);
        }

        /// <summary>
        /// pid가 우리가 띄운 복사 프로세스로 아직 실행 중인지. PID는 재사용되므로 이름뿐 아니라
        /// 시작 시각이 .clone_pending 기록 시각(pendingTicks, 로컬 시각)과 맞는지도 본다. 확인할 수 없으면 종료된 것으로 본다.
        /// </summary>
        internal static bool IsCopyProcessAlive(int pid, long pendingTicks)
        {
            if (pid <= 0 || pendingTicks <= 0) return false;
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (p.HasExited) return false;
                    string name = p.ProcessName.ToLowerInvariant();
                    bool nameMatches = false;
                    foreach (string n in CopyProcessNames)
                        if (name == n) { nameMatches = true; break; }
                    if (!nameMatches) return false;

                    // 이름만으로는 사용자가 연 cmd·bash 터미널과 구분되지 않는다(취소 시 그 프로세스 트리를 죽이게 된다).
                    // .clone_pending은 Process.Start 직후에 기록되므로 진짜 복사 프로세스는 시작 시각이 거의 같다.
                    System.DateTime expected = new System.DateTime(pendingTicks, System.DateTimeKind.Local).ToUniversalTime();
                    double diff = (p.StartTime.ToUniversalTime() - expected).TotalSeconds;
                    return System.Math.Abs(diff) <= ProcessStartToleranceSeconds;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void FinishCopy(string targetPath, string sourcePath, int exitCode, System.Action callback)
        {
            string name = Path.GetFileName(targetPath);
            bool fatal = exitCode < 0 || (IsWindows ? exitCode >= 16 : (exitCode != 0 && exitCode != 23 && exitCode != 24));
            bool partial = !fatal && (IsWindows ? exitCode >= 8 : exitCode != 0);
            string excerpt = ReadCopyErrors(targetPath);

            bool keep = !fatal && !partial;
            if (partial)
            {
                Debug.LogWarning(LogPrefix + $"일부 Library 파일을 복사하지 못했습니다 (exit code {exitCode}): {targetPath}\n{excerpt}");
                // Esc·창 닫기는 cancel 슬롯으로 돌아오므로, 삭제는 두 번째 확인 창의 '삭제'를 직접 눌렀을 때만 한다.
                // 그 외(Esc·닫기 포함)는 모두 유지 — 유지는 목록에서 언제든 지울 수 있지만 삭제는 되돌릴 수 없다.
                keep = Application.isBatchMode
                    || EditorUtility.DisplayDialog(DialogTitle,
                        $"'{name}' 생성 중 일부 Library 파일을 복사하지 못했습니다 (exit code {exitCode}).\n" +
                        "대부분 실행 중인 에디터가 잠근 캐시 파일입니다.\n\n" +
                        (string.IsNullOrEmpty(excerpt) ? "" : excerpt + "\n\n") +
                        "유지: 클론을 그대로 쓰고, 빠진 캐시는 클론을 처음 열 때 다시 만듭니다.\n" +
                        "삭제: 만들던 클론을 지웁니다(원본은 영향 없음).",
                        "유지", "삭제")
                    || !TelleRGUI.Confirm(DialogTitle,
                        $"'{name}' 클론 폴더를 삭제할까요?\n" +
                        "방금 복사한 Library를 포함해 폴더 전체가 사라지며 되돌릴 수 없습니다(원본 프로젝트는 영향 없음).\n\n" +
                        targetPath,
                        "삭제", "유지");
            }

            if (keep)
            {
                try
                {
                    CloneMeta meta = new CloneMeta
                    {
                        originalProjectPath = sourcePath,
                        createdTime = System.DateTime.Now.Ticks
                    };
                    File.WriteAllText(Path.Combine(targetPath, CloneMarkerFile), JsonUtility.ToJson(meta, true));
                    DeleteWorkFiles(targetPath);
                }
                catch (System.Exception e)
                {
                    Debug.LogError(LogPrefix + $"완료 표식을 쓰지 못했습니다: {e.Message}\n{targetPath}");
                    if (!Application.isBatchMode)
                        EditorUtility.DisplayDialog(DialogTitle, $"복사는 끝났지만 완료 표식을 쓰지 못했습니다.\n{e.Message}\n\n{targetPath}", "확인");
                    StateChanged?.Invoke();
                    return;
                }

                Debug.Log(LogPrefix + $"클론을 만들었습니다: {targetPath}");
                callback?.Invoke();
                StateChanged?.Invoke();
                if (!Application.isBatchMode && !partial)
                    EditorUtility.DisplayDialog(DialogTitle, $"클론을 만들었습니다.\n{targetPath}", "확인");
                return;
            }

            if (fatal)
            {
                Debug.LogError(LogPrefix + $"Library 복사에 실패했습니다 (exit code {exitCode}): {targetPath}\n{excerpt}");
            }

            bool deleted = DeletePartialClone(targetPath, 1, out string deleteError);
            StateChanged?.Invoke();
            if (Application.isBatchMode) return;

            string body = fatal
                ? $"Library 복사에 실패했습니다 (exit code {exitCode}).\n" + (string.IsNullOrEmpty(excerpt) ? "" : excerpt + "\n")
                : "만들던 클론을 삭제했습니다.\n";
            if (!deleted) body += "\n만들던 폴더를 모두 지우지 못했습니다. 목록의 '불완전' 항목을 다시 삭제하세요.\n" + deleteError;
            EditorUtility.DisplayDialog(DialogTitle, body, "확인");
        }

        private static void CancelCopy()
        {
            string target = pendingTargetPath;
            KillProcessTree(currentProcess, currentProcessId);
            onCompleteCallback = null;
            EndTracking();

            Debug.Log(LogPrefix + $"클론 생성을 취소했습니다. 만들던 폴더를 삭제합니다: {target}");
            // 강제 종료 직후에는 복사 스레드가 파일 핸들을 잠깐 쥐고 있을 수 있어 몇 번 재시도한다.
            bool deleted = DeletePartialClone(target, 4, out string error);
            StateChanged?.Invoke();
            if (!deleted && !Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    "클론 생성을 취소했지만 만들던 폴더를 모두 지우지 못했습니다.\n목록의 '불완전' 항목을 다시 삭제하세요.\n" + error, "확인");
            }
        }

        /// <summary>
        /// 만들던(취소·실패한) 클론 폴더를 진행 표시줄을 띄운 채 지운다. 수 GB Library 삭제 동안 에디터가 멈춘 것처럼 보이지 않게 한다.
        /// </summary>
        private static bool DeletePartialClone(string path, int attempts, out string error)
        {
            error = null;
            bool deleted = false;
            try
            {
                for (int attempt = 0; attempt < attempts && !deleted; attempt++)
                {
                    EditorUtility.DisplayProgressBar(DialogTitle, "만들던 클론 삭제 중...", 1f);
                    if (attempt > 0) System.Threading.Thread.Sleep(500);
                    deleted = TryDeleteCloneFolder(path, out error);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return deleted;
        }

        internal static void KillProcessTree(Process process, int pid)
        {
            if (pid <= 0 && process != null)
            {
                try { pid = process.Id; } catch { }
            }
            if (pid <= 0) return;

            try
            {
                if (IsWindows)
                {
                    // cmd 래퍼만 죽이면 robocopy가 남으므로 트리 전체를 종료한다.
                    RunProcess(new ProcessStartInfo("taskkill", $"/PID {pid} /T /F"));
                }
                else
                {
                    var psi = ShellStartInfo("pkill -TERM -P \"$TELLER_FC_PID\" 2>/dev/null; kill -TERM \"$TELLER_FC_PID\" 2>/dev/null; exit 0");
                    psi.EnvironmentVariables["TELLER_FC_PID"] = pid.ToString();
                    RunProcess(psi);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(LogPrefix + $"복사 프로세스를 종료하지 못했습니다: {e.Message}");
            }

            if (process != null)
            {
                try { process.WaitForExit(5000); } catch { }
            }
        }

        /// <summary>도메인 리로드·에디터 재시작 뒤 .clone_pending 폴더를 다시 추적하거나 결과를 마무리한다.</summary>
        internal static void ResumePendingCopies()
        {
            if (IsCopying || IsClone()) return;

            string sourcePath = GetCurrentProjectPath();
            for (int i = 1; i <= MaxCloneCount; i++)
            {
                string path = sourcePath + CloneSuffix + i;
                if (!File.Exists(Path.Combine(path, PendingMarkerFile))) continue;
                if (File.Exists(Path.Combine(path, CloneMarkerFile))) continue;

                PendingCloneInfo info = ReadPending(path);
                string source = info != null && !string.IsNullOrEmpty(info.originalProjectPath) ? info.originalProjectPath : sourcePath;

                if (TryReadExitCode(path, out int exitCode))
                {
                    Debug.Log(LogPrefix + $"중단됐던 클론 복사의 결과를 마무리합니다: {path}");
                    FinishCopy(path, source, exitCode, null);
                    continue;
                }

                int pid = info != null ? info.processId : 0;
                long pendingTicks = info != null ? info.createdTime : 0;
                if (!IsCopying && IsCopyProcessAlive(pid, pendingTicks))
                {
                    Process process = null;
                    try { process = Process.GetProcessById(pid); } catch { }
                    double startTime = EditorApplication.timeSinceStartup -
                                       (System.DateTime.Now - new System.DateTime(pendingTicks)).TotalSeconds;
                    Debug.Log(LogPrefix + $"진행 중인 클론 복사를 다시 추적합니다: {path}");
                    BeginTracking(path, source, process, pid, pendingTicks, startTime);
                }
            }
            StateChanged?.Invoke();
        }

        // ─── 열기 ───

        /// <summary>프로젝트가 다른 Unity 에디터에서 열려 있는지. certain=false면 잠금 파일만 보고 추정한 것이다.</summary>
        internal static bool IsProjectOpen(string projectPath, out bool certain)
        {
            string lockFile = Path.Combine(projectPath, "Temp", "UnityLockfile");
            certain = true;
            if (!File.Exists(lockFile)) return false;

            if (IsWindows)
            {
                // 실행 중인 에디터는 잠금 파일을 열어 둔다. 독점으로 열리면 크래시 뒤 남은 잔여 파일이다.
                try
                {
                    using (new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    return false;
                }
                catch (IOException)
                {
                    return true;
                }
                catch (System.UnauthorizedAccessException)
                {
                    certain = false;
                    return true;
                }
            }

            certain = false;
            return true;
        }

        public static void OpenCloneProject(string projectPath)
        {
            if (!Directory.Exists(projectPath))
            {
                EditorUtility.DisplayDialog(DialogTitle, $"클론 폴더가 없습니다.\n{projectPath}", "확인");
                StateChanged?.Invoke();
                return;
            }
            if (!File.Exists(Path.Combine(projectPath, CloneMarkerFile)))
            {
                EditorUtility.DisplayDialog(DialogTitle, "생성이 끝나지 않은 클론은 열 수 없습니다.\n삭제한 뒤 다시 만드세요.", "확인");
                return;
            }

            string key = NormalizePath(projectPath);
            if (recentLaunches.TryGetValue(key, out double launchedAt) &&
                EditorApplication.timeSinceStartup - launchedAt < LaunchGuardSeconds &&
                !File.Exists(Path.Combine(projectPath, "Temp", "UnityLockfile")))
            {
                EditorUtility.DisplayDialog(DialogTitle, "이 클론을 여는 중입니다. 새 에디터 창이 뜰 때까지 기다려 주세요.", "확인");
                return;
            }

            if (IsProjectOpen(projectPath, out bool certain))
            {
                if (certain)
                {
                    EditorUtility.DisplayDialog(DialogTitle,
                        $"이 클론은 이미 다른 Unity 에디터에서 열려 있습니다.\n작업 표시줄에서 해당 에디터 창을 사용하세요.\n\n{projectPath}", "확인");
                    return;
                }
                if (!EditorUtility.DisplayDialog(DialogTitle,
                        "이 클론이 이미 열려 있는 것 같습니다 (Temp/UnityLockfile 있음).\n" +
                        "비정상 종료 뒤 남은 잠금일 수도 있습니다. 그래도 여시겠습니까?", "열기", "취소"))
                    return;
            }

            try
            {
                ProcessStartInfo startInfo;
                if (IsWindows)
                {
                    startInfo = new ProcessStartInfo(EditorApplication.applicationPath, $"-projectPath \"{projectPath}\"");
                    startInfo.UseShellExecute = true; // 기존 동작 유지
                }
                else
                {
                    // macOS의 applicationPath는 Unity.app 번들이다: 번들 안 실행 파일을 직접 실행해야 -projectPath가 전달된다.
                    string exe = EditorApplication.applicationPath;
                    string bundleExe = Path.Combine(exe, "Contents", "MacOS", "Unity");
                    if (exe.EndsWith(".app") && File.Exists(bundleExe)) exe = bundleExe;
                    startInfo = ShellStartInfo("exec \"$TELLER_FC_UNITY\" -projectPath \"$TELLER_FC_PROJECT\"");
                    startInfo.EnvironmentVariables["TELLER_FC_UNITY"] = exe;
                    startInfo.EnvironmentVariables["TELLER_FC_PROJECT"] = projectPath;
                    startInfo.UseShellExecute = false; // 환경 변수 전달에 필요
                }

                using (Process.Start(startInfo)) { }
                recentLaunches[key] = EditorApplication.timeSinceStartup;
                Debug.Log(LogPrefix + $"클론 프로젝트를 새 에디터로 엽니다: {projectPath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError(LogPrefix + $"클론을 열지 못했습니다: {e.Message}");
                EditorUtility.DisplayDialog(DialogTitle, $"클론을 열지 못했습니다.\n{e.Message}", "확인");
            }
        }

        // ─── 삭제 ───

        public static void DeleteClone(string path)
        {
            if (!Directory.Exists(path)) return;

            if (SamePath(path, pendingTargetPath))
            {
                EditorUtility.DisplayDialog(DialogTitle, "복사 중인 클론입니다.\n진행 표시줄의 취소 버튼으로 중단하면 자동으로 삭제됩니다.", "확인");
                return;
            }

            // 다른 Unity 인스턴스로 열려 있는 클론을 지우면 잠긴 파일이 부분 삭제되어 수 GB 잔해가 남음
            if (IsProjectOpen(path, out bool certain))
            {
                EditorUtility.DisplayDialog(DialogTitle,
                    certain
                        ? "이 클론은 다른 Unity 에디터에서 열려 있습니다.\n그 에디터를 닫은 뒤 다시 삭제하세요."
                        : "이 클론은 다른 Unity 인스턴스에서 열려 있는 것 같습니다.\n닫은 뒤 다시 삭제하세요.\n" +
                          "(닫았는데도 이 메시지가 나오면 크래시 잔여 잠금이므로 폴더를 직접 삭제하세요)", "확인");
                return;
            }

            EditorUtility.DisplayProgressBar(DialogTitle, "클론 삭제 중...", 1.0f);
            try
            {
                if (!TryDeleteCloneFolder(path, out string error))
                {
                    EditorUtility.DisplayDialog(DialogTitle,
                        $"클론 삭제가 완료되지 않았습니다.\n잠긴 파일을 확인한 뒤 다시 시도하세요:\n{path}\n\n{error}", "확인");
                }
                else
                {
                    Debug.Log(LogPrefix + $"클론을 삭제했습니다: {path}");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                recentLaunches.Remove(NormalizePath(path));
                StateChanged?.Invoke();
            }
        }

        /// <summary>
        /// 클론 폴더를 지운다. 원본과 공유하는 링크(정션/심볼릭 링크)는 링크만 해제하고 따라 들어가지 않는다.
        /// </summary>
        internal static bool TryDeleteCloneFolder(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return true;

            string full = NormalizePath(path);
            if (SamePath(full, GetCurrentProjectPath()) || SamePath(full, GetOriginalProjectPath()) ||
                SamePath(full, Path.GetPathRoot(full)))
            {
                error = "현재 프로젝트나 원본 프로젝트, 드라이브 루트는 삭제할 수 없습니다.";
                return false;
            }

            try
            {
                int exitCode;
                if (IsWindows)
                {
                    // rmdir /s는 정션을 따라가지 않지만, 안전을 위해 공유 링크를 먼저 링크만 해제한다.
                    foreach (string folder in LinkedFolders)
                    {
                        string link = Path.Combine(path, folder);
                        if (IsLink(link)) Directory.Delete(link, false);
                    }
                    var psi = new ProcessStartInfo("cmd.exe", "/d /v:off /c rmdir /s /q \"%TELLER_FC_PATH%\"");
                    psi.EnvironmentVariables["TELLER_FC_PATH"] = path;
                    exitCode = RunProcess(psi);
                }
                else
                {
                    // rm -rf는 심볼릭 링크를 따라가지 않는다. 경로는 환경 변수로 넘겨 따옴표·공백이 섞여도 안전하다.
                    var psi = ShellStartInfo("rm -rf -- \"$TELLER_FC_PATH\"");
                    psi.EnvironmentVariables["TELLER_FC_PATH"] = path;
                    exitCode = RunProcess(psi);
                }

                // 종료 코드보다 실제 폴더 존재 여부가 신뢰할 수 있는 판정 기준
                if (Directory.Exists(path))
                {
                    error = $"exit code {exitCode}";
                    return false;
                }
                return true;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        // ─── 내부 도우미 ───

        private static void LinkFolder(string sourceRoot, string targetRoot, string folderName)
        {
            string sourceDir = Path.Combine(sourceRoot, folderName);
            string targetDir = Path.Combine(targetRoot, folderName);
            if (!Directory.Exists(sourceDir)) return;

            ProcessStartInfo psi;
            if (IsWindows)
                psi = new ProcessStartInfo("cmd.exe", "/d /v:off /c mklink /J \"%TELLER_FC_LINK%\" \"%TELLER_FC_TARGET%\"");
            else
                psi = ShellStartInfo("ln -s -- \"$TELLER_FC_TARGET\" \"$TELLER_FC_LINK\"");
            psi.EnvironmentVariables["TELLER_FC_LINK"] = targetDir;
            psi.EnvironmentVariables["TELLER_FC_TARGET"] = sourceDir;

            int exitCode = RunProcess(psi);
            if (exitCode != 0 || !Directory.Exists(targetDir))
                throw new System.Exception($"{folderName} 링크를 만들지 못했습니다 (exit code {exitCode}).");
        }

        /// <summary>
        /// 고정 스크립트를 /bin/sh로 실행한다. 사용자 경로는 환경 변수로만 넘기므로 따옴표·공백이 섞여도 셸 해석에 걸리지 않는다.
        /// </summary>
        private static ProcessStartInfo ShellStartInfo(string script)
        {
            var psi = new ProcessStartInfo("/bin/sh", "-c 'eval \"$TELLER_FC_CMD\"'");
            psi.EnvironmentVariables["TELLER_FC_CMD"] = script;
            return psi;
        }

        private static int RunProcess(ProcessStartInfo startInfo)
        {
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            using (var proc = Process.Start(startInfo))
            {
                if (proc == null) return -1;
                proc.WaitForExit();
                return proc.ExitCode;
            }
        }

        /// <summary>.clone_pending을 쓰고 기록한 시각(로컬 Ticks)을 반환한다. 복사 프로세스 확인(PID 재사용 대비)에 쓰인다.</summary>
        private static long WritePending(string targetPath, string sourcePath, int processId)
        {
            var info = new PendingCloneInfo
            {
                originalProjectPath = sourcePath,
                createdTime = System.DateTime.Now.Ticks,
                processId = processId
            };
            File.WriteAllText(Path.Combine(targetPath, PendingMarkerFile), JsonUtility.ToJson(info, true));
            return info.createdTime;
        }

        private static PendingCloneInfo ReadPending(string clonePath)
        {
            try
            {
                string file = Path.Combine(clonePath, PendingMarkerFile);
                if (!File.Exists(file)) return null;
                return JsonUtility.FromJson<PendingCloneInfo>(File.ReadAllText(file));
            }
            catch
            {
                return null;
            }
        }

        internal static bool TryReadExitCode(string clonePath, out int exitCode)
        {
            exitCode = -1;
            try
            {
                string file = Path.Combine(clonePath, ExitCodeFile);
                if (!File.Exists(file)) return false;
                return int.TryParse(File.ReadAllText(file).Trim(), out exitCode);
            }
            catch
            {
                return false; // 쓰는 중이면 다음 프레임에 다시 읽는다
            }
        }

        private static string ReadCopyErrors(string clonePath)
        {
            try
            {
                string file = Path.Combine(clonePath, CopyLogFile);
                if (!File.Exists(file)) return "";
                var sb = new StringBuilder();
                int count = 0;
                foreach (string raw in File.ReadAllLines(file))
                {
                    string line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (IsWindows && !RobocopyErrorLine.IsMatch(line)) continue;
                    if (line.Length > 200) line = line.Substring(0, 200) + "...";
                    sb.AppendLine(line);
                    if (++count >= 5) break;
                }
                return sb.ToString().TrimEnd();
            }
            catch
            {
                return "";
            }
        }

        private static void DeleteWorkFiles(string clonePath)
        {
            foreach (string file in new[] { PendingMarkerFile, ExitCodeFile, CopyLogFile, CopyScriptFile })
            {
                try
                {
                    string p = Path.Combine(clonePath, file);
                    if (File.Exists(p)) File.Delete(p);
                }
                catch { }
            }
        }

        private static bool IsLink(string path)
        {
            try
            {
                if (!Directory.Exists(path) && !File.Exists(path)) return false;
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { path = Path.GetFullPath(path); } catch { }
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            return string.Equals(NormalizePath(a), NormalizePath(b),
                IsWindows ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal);
        }
    }
}
