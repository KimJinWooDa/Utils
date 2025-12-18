using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace TelleR.Util.FastClone
{
    [System.Serializable]
    public class CloneMeta
    {
        public string originalProjectPath;
        public long createdTime;
    }

    public static class FastCloneCore
    {
        public const string CloneSuffix = "_Clone_";
        public const string CloneMarkerFile = ".clone_marker";
        public const int MaxCloneCount = 10;

        private static Process currentProcess;
        private static System.Action onCompleteCallback;
        private static string pendingTargetPath;
        private static string pendingSourcePath;

        public static bool IsClone()
        {
            return File.Exists(Path.Combine(GetCurrentProjectPath(), CloneMarkerFile));
        }

        public static string GetCurrentProjectPath()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        public static string GetOriginalProjectPath()
        {
            if (!IsClone()) return GetCurrentProjectPath();

            try
            {
                string json = File.ReadAllText(Path.Combine(GetCurrentProjectPath(), CloneMarkerFile));
                CloneMeta meta = JsonUtility.FromJson<CloneMeta>(json);
                if (meta != null && !string.IsNullOrEmpty(meta.originalProjectPath)) return meta.originalProjectPath;
            }
            catch { }
            return GetCurrentProjectPath();
        }

        public static List<string> GetAllClonePaths()
        {
            List<string> clones = new List<string>();
            string sourcePath = GetCurrentProjectPath();
            for (int i = 1; i <= MaxCloneCount; i++)
            {
                string path = sourcePath + CloneSuffix + i;
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, CloneMarkerFile))) clones.Add(path);
            }
            return clones;
        }

        public static void CreateNextClone(System.Action onComplete)
        {
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
                EditorUtility.DisplayDialog("Error", $"Max clone count reached: {MaxCloneCount}", "OK");
                return;
            }

            try
            {
                Directory.CreateDirectory(targetPath);

                LinkFolder(sourcePath, targetPath, "Assets");
                LinkFolder(sourcePath, targetPath, "Packages");
                LinkFolder(sourcePath, targetPath, "ProjectSettings");

                string sourceLibrary = Path.Combine(sourcePath, "Library");
                string targetLibrary = Path.Combine(targetPath, "Library");

                pendingSourcePath = sourcePath;
                pendingTargetPath = targetPath;
                onCompleteCallback = onComplete;

                ProcessStartInfo startInfo = new ProcessStartInfo();
                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    startInfo.FileName = "robocopy";
                    startInfo.Arguments = $"\"{sourceLibrary}\" \"{targetLibrary}\" /E /NFL /NDL /NJH /NJS /nc /ns /np /MT:8";
                }
                else
                {
                    startInfo.FileName = "rsync";
                    startInfo.Arguments = $"-a \"{sourceLibrary}/\" \"{targetLibrary}/\"";
                }
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;

                currentProcess = Process.Start(startInfo);
                EditorApplication.update += UpdateProcess;
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Error", $"Failed to start clone creation:\n{e.Message}", "OK");
                if (Directory.Exists(targetPath)) DeleteClone(targetPath);
            }
        }

        private static void UpdateProcess()
        {
            if (currentProcess == null)
            {
                EditorApplication.update -= UpdateProcess;
                EditorUtility.ClearProgressBar();
                return;
            }

            if (!currentProcess.HasExited)
            {
                float anim = (float)(EditorApplication.timeSinceStartup % 1.0);
                EditorUtility.DisplayProgressBar("Fast Clone", "Copying Library... (This may take a minute)", anim);
            }
            else
            {
                EditorApplication.update -= UpdateProcess;
                EditorUtility.ClearProgressBar();

                int exitCode = currentProcess.ExitCode;
                currentProcess.Dispose();
                currentProcess = null;

                bool isSuccess = (Application.platform == RuntimePlatform.WindowsEditor && exitCode < 8) ||
                                 (Application.platform != RuntimePlatform.WindowsEditor && exitCode == 0);

                if (isSuccess)
                {
                    CloneMeta meta = new CloneMeta
                    {
                        originalProjectPath = pendingSourcePath,
                        createdTime = System.DateTime.Now.Ticks
                    };
                    File.WriteAllText(Path.Combine(pendingTargetPath, CloneMarkerFile), JsonUtility.ToJson(meta, true));

                    onCompleteCallback?.Invoke();
                    EditorUtility.DisplayDialog("Success", "Clone created successfully!", "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", $"Copy failed with exit code: {exitCode}", "OK");
                    DeleteClone(pendingTargetPath);
                }
            }
        }

        public static void OpenCloneProject(string projectPath)
        {
            EditorUtility.DisplayProgressBar("Fast Clone", "Launching Unity Editor...", 1.0f);

            string unityPath = EditorApplication.applicationPath;
            string args = $"-projectPath \"{projectPath}\"";
            Process.Start(unityPath, args);

            double startTime = EditorApplication.timeSinceStartup;
            EditorApplication.CallbackFunction closer = null;
            closer = () =>
            {
                if (EditorApplication.timeSinceStartup - startTime > 0.5f)
                {
                    EditorUtility.ClearProgressBar();
                    EditorApplication.update -= closer;
                }
            };
            EditorApplication.update += closer;
        }

        public static void DeleteClone(string path)
        {
            if (!Directory.Exists(path)) return;

            EditorUtility.DisplayProgressBar("Fast Clone", "Deleting...", 1.0f);

            if (Application.platform == RuntimePlatform.WindowsEditor)
                RunCommand("cmd.exe", $"/c rmdir /s /q \"{path}\"");
            else
                RunCommand("/bin/bash", $"-c \"rm -rf '{path}'\"");

            EditorUtility.ClearProgressBar();
        }

        private static void LinkFolder(string sourceRoot, string targetRoot, string folderName)
        {
            string sourceDir = Path.Combine(sourceRoot, folderName);
            string targetDir = Path.Combine(targetRoot, folderName);
            if (!Directory.Exists(sourceDir)) return;

            int exitCode = 0;
            if (Application.platform == RuntimePlatform.WindowsEditor)
                exitCode = RunCommand("cmd.exe", $"/c mklink /J \"{targetDir}\" \"{sourceDir}\"");
            else
                exitCode = RunCommand("/bin/bash", $"-c \"ln -s '{sourceDir}' '{targetDir}'\"");

            if (exitCode != 0 || !Directory.Exists(targetDir))
                throw new System.Exception($"Failed to link {folderName}");
        }

        private static int RunCommand(string fileName, string args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var proc = Process.Start(startInfo))
            {
                proc.WaitForExit();
                return proc.ExitCode;
            }
        }
    }
}
