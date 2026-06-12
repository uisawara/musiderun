using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Works.Mmzk.Util.Musiderun;
using Debug = UnityEngine.Debug;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public sealed class ProcessResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; } = string.Empty;
        public string StandardError { get; set; } = string.Empty;
        public bool Succeeded => ExitCode == 0;
    }

    public static class PlatformUtility
    {
        private static readonly string[] WindowsGitFallbackPaths =
        {
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files (x86)\Git\cmd\git.exe"
        };

        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static string GetRepositoryRoot()
        {
            var dataPath = Path.GetFullPath(Application.dataPath);
            return Path.GetDirectoryName(dataPath) ?? dataPath;
        }

        public static string GetDefaultMirrorBasePath(string repositoryRoot)
        {
            var repoName = new DirectoryInfo(repositoryRoot).Name;
            var parent = Directory.GetParent(repositoryRoot)?.FullName ?? repositoryRoot;
            return Path.GetFullPath(Path.Combine(parent, $"{repoName}-musiderun"));
        }

        public static string ResolveMirrorPath(MusiderunSettingsData data, string jobId)
        {
            var repositoryRoot = GetRepositoryRoot();
            if (!string.IsNullOrWhiteSpace(data?.mirrorWorktreeBasePath))
            {
                return Path.GetFullPath(Path.Combine(data.mirrorWorktreeBasePath, jobId));
            }

            return Path.GetFullPath($"{GetDefaultMirrorBasePath(repositoryRoot)}-{jobId}");
        }

        public static string ResolveMirrorBranch(MusiderunSettingsData data, string jobId)
        {
            var prefix = string.IsNullOrWhiteSpace(data?.mirrorBranchPrefix)
                ? "musiderun/mirror"
                : data.mirrorBranchPrefix.TrimEnd('/');
            return $"{prefix}-{jobId}";
        }

        public static bool TryValidateMirrorPath(
            MusiderunSettingsData data,
            string jobId,
            out string mirrorPath,
            out string errorMessage)
        {
            var repositoryRoot = NormalizeDirectoryPath(GetRepositoryRoot());
            mirrorPath = NormalizeDirectoryPath(ResolveMirrorPath(data, jobId));

            if (IsSameOrSubPath(mirrorPath, repositoryRoot))
            {
                errorMessage =
                    "ミラーパスを同一リポジトリ（プロジェクト）配下に設定できません。プロジェクトを破損する恐れがあります。\n" +
                    $"ミラー: {mirrorPath}\n" +
                    $"リポジトリ: {repositoryRoot}";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public static void ValidateMirrorPathOrThrow(MusiderunSettingsData data, string jobId)
        {
            if (!TryValidateMirrorPath(data, jobId, out _, out var errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }
        }

        public static bool TryDeleteMirrorDirectory(string mirrorPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            var normalizedMirror = NormalizeDirectoryPath(mirrorPath);

            if (!Directory.Exists(normalizedMirror))
            {
                return true;
            }

            var repositoryRoot = NormalizeDirectoryPath(GetRepositoryRoot());
            if (IsSameOrSubPath(normalizedMirror, repositoryRoot))
            {
                errorMessage = "安全のため、リポジトリ配下のディレクトリは削除しません。";
                return false;
            }

            try
            {
                Directory.Delete(normalizedMirror, recursive: true);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static string GetRepositoryBatchJobLogsPath()
        {
            return NormalizeDirectoryPath(Path.Combine(GetRepositoryRoot(), "BatchJobLogs"));
        }

        public static bool TryDeleteRepositoryBatchJobLogs(out string errorMessage)
        {
            errorMessage = string.Empty;
            var batchJobLogsPath = GetRepositoryBatchJobLogsPath();

            if (!Directory.Exists(batchJobLogsPath))
            {
                return true;
            }

            try
            {
                Directory.Delete(batchJobLogsPath, recursive: true);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public static string ResolveLogOutputDirectory(
            MusiderunSettingsData data,
            string mirrorPath,
            bool mirrorWorktreeReady = true)
        {
            if (!string.IsNullOrWhiteSpace(data?.logOutputDirectory))
            {
                return Path.GetFullPath(data.logOutputDirectory);
            }

            if (!mirrorWorktreeReady)
            {
                return Path.Combine(GetRepositoryRoot(), "BatchJobLogs");
            }

            return Path.Combine(mirrorPath, "BatchJobLogs");
        }

        public static string ResolveArtifactFolder(MusiderunSettingsData data, BatchJobDefinitionData job)
        {
            if (string.IsNullOrWhiteSpace(job?.artifactFolder))
            {
                return string.Empty;
            }

            var path = job.artifactFolder.Trim();
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            return Path.GetFullPath(Path.Combine(ResolveMirrorPath(data, job.id), path));
        }

        public static string ResolveUnityExecutable(MusiderunSettingsData data)
        {
            var path = !string.IsNullOrWhiteSpace(data?.unityExecutablePath)
                ? Path.GetFullPath(data.unityExecutablePath)
                : EditorApplication.applicationPath;

            return NormalizeUnityExecutablePath(path);
        }

        public static bool UnityExecutableExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return File.Exists(NormalizeUnityExecutablePath(path));
        }

        public static void OpenPathWithDefaultApplication(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("ファイルが見つかりません。", path);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return;
            }

            if (IsWindows)
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        private static string NormalizeUnityExecutablePath(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(path, "Contents", "MacOS", "Unity");
            }

            return path;
        }

        public static bool TryResolveGitExecutable(out string gitExecutable, out string errorMessage)
        {
            if (TryFindOnPath(IsWindows ? "git.exe" : "git", out gitExecutable))
            {
                errorMessage = string.Empty;
                return true;
            }

            if (IsWindows)
            {
                foreach (var candidate in WindowsGitFallbackPaths)
                {
                    if (File.Exists(candidate))
                    {
                        gitExecutable = candidate;
                        errorMessage = string.Empty;
                        return true;
                    }
                }
            }

            gitExecutable = string.Empty;
            errorMessage = IsWindows
                ? "git が見つかりません。Git for Windows をインストールし、PATH に追加してください。"
                : "git が見つかりません。Xcode Command Line Tools または Homebrew で git をインストールしてください。";
            return false;
        }

        public static Task<ProcessResult> RunProcessAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            Action<string> onStdout = null,
            Action<string> onStderr = null,
            CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<ProcessResult>();
            var stdout = new List<string>();
            var stderr = new List<string>();

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            void DispatchLog(Action<string> callback, string line)
            {
                if (callback == null)
                {
                    return;
                }

                EditorMainThreadDispatcher.Enqueue(() => callback(line));
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    return;
                }

                lock (stdout)
                {
                    stdout.Add(e.Data);
                }

                DispatchLog(onStdout, e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                {
                    return;
                }

                lock (stderr)
                {
                    stderr.Add(e.Data);
                }

                DispatchLog(onStderr, e.Data);
            };

            process.Exited += (_, _) =>
            {
                var result = new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = string.Join(Environment.NewLine, stdout),
                    StandardError = string.Join(Environment.NewLine, stderr)
                };

                process.Dispose();
                tcs.TrySetResult(result);
            };

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[musiderun] プロセス中断中にエラー: {ex.Message}");
                    }

                    tcs.TrySetCanceled(cancellationToken);
                });
            }

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                process.Dispose();
                tcs.TrySetException(ex);
            }

            return tcs.Task;
        }

        public static string GetBuildOutputLocation(BuildTarget buildTarget)
        {
            var extension = GetExecutableExtension(buildTarget);
            return Path.Combine("Builds", $"Player{extension}");
        }

        public static string GetExecutableExtension(BuildTarget buildTarget)
        {
            switch (buildTarget)
            {
                case BuildTarget.StandaloneOSX:
                    return ".app";
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return ".exe";
                case BuildTarget.StandaloneLinux64:
                    return ".x86_64";
                case BuildTarget.Android:
                    return ".apk";
                case BuildTarget.iOS:
                    return string.Empty;
                default:
                    return ".build";
            }
        }

        private static string NormalizeDirectoryPath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsSameOrSubPath(string candidatePath, string parentPath)
        {
            var comparison = IsWindows
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (string.Equals(candidatePath, parentPath, comparison))
            {
                return true;
            }

            var separator = Path.DirectorySeparatorChar;
            var parentWithSeparator = parentPath + separator;
            if (candidatePath.StartsWith(parentWithSeparator, comparison))
            {
                return true;
            }

            if (Path.AltDirectorySeparatorChar != separator)
            {
                var altParentWithSeparator = parentPath + Path.AltDirectorySeparatorChar;
                if (candidatePath.StartsWith(altParentWithSeparator, comparison))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindOnPath(string fileName, out string fullPath)
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv))
            {
                fullPath = string.Empty;
                return false;
            }

            foreach (var directory in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                try
                {
                    var candidate = Path.Combine(directory.Trim(), fileName);
                    if (File.Exists(candidate))
                    {
                        fullPath = Path.GetFullPath(candidate);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[musiderun] PATH 検索中にエラー: {ex.Message}");
                }
            }

            fullPath = string.Empty;
            return false;
        }
    }
}
