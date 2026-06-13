using System;
using System.IO;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal static class MusiderunMainBranchStore
    {
        private const string FileName = "musiderun-main-branch";

        public static void Save(string repositoryRoot, string branchName)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot) || string.IsNullOrWhiteSpace(branchName))
            {
                return;
            }

            var path = ResolveStorePath(repositoryRoot);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, branchName.Trim());
        }

        public static string Load(string repositoryRoot)
        {
            if (string.IsNullOrWhiteSpace(repositoryRoot))
            {
                return string.Empty;
            }

            var path = ResolveStorePath(repositoryRoot);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }

        private static string ResolveStorePath(string repositoryRoot)
        {
            if (!TryResolveGitDirectory(repositoryRoot, out var gitDirectory))
            {
                return Path.Combine(repositoryRoot, "Library", "Musiderun", FileName);
            }

            var commonGitDirectory = ResolveCommonGitDirectory(gitDirectory);
            return Path.Combine(commonGitDirectory, FileName);
        }

        private static bool TryResolveGitDirectory(string repositoryRoot, out string gitDirectory)
        {
            gitDirectory = string.Empty;
            var gitPath = Path.Combine(repositoryRoot, ".git");
            if (Directory.Exists(gitPath))
            {
                gitDirectory = gitPath;
                return true;
            }

            if (!File.Exists(gitPath))
            {
                return false;
            }

            var firstLine = File.ReadAllText(gitPath).Trim();
            if (!firstLine.StartsWith("gitdir:", StringComparison.Ordinal))
            {
                return false;
            }

            gitDirectory = firstLine["gitdir:".Length..].Trim().Trim('"');
            if (!Path.IsPathRooted(gitDirectory))
            {
                gitDirectory = Path.GetFullPath(Path.Combine(repositoryRoot, gitDirectory));
            }

            return true;
        }

        private static string ResolveCommonGitDirectory(string gitDirectory)
        {
            var normalized = gitDirectory.Replace('\\', '/');
            var worktreesIndex = normalized.IndexOf("/worktrees/", StringComparison.Ordinal);
            if (worktreesIndex < 0)
            {
                return gitDirectory;
            }

            return normalized[..worktreesIndex].Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
