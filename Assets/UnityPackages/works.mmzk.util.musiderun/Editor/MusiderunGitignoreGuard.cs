using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    /// <summary>
    /// musiderun がメインリポジトリ直下に書き込むログ出力先（BatchJobLogs / logOutputDirectory）が
    /// 対象リポジトリの .gitignore に登録されているかを検査し、不足分を追記する。
    /// これらが追跡対象のままだと、Job 同期前後の作業ツリー状態比較で差分が検出され
    /// Job が中断されてしまうため、実行前に保証する。
    /// （ビルド成果物などはミラー worktree 側に生成されるため対象外）
    /// </summary>
    public static class MusiderunGitignoreGuard
    {
        private const string ManagedSectionHeader = "# === musiderun (auto-managed) ===";

        public sealed class GitignoreCheckResult
        {
            public string GitignorePath { get; set; } = string.Empty;
            public bool Exists { get; set; }
            public IReadOnlyList<string> Required { get; set; } = Array.Empty<string>();
            public IReadOnlyList<string> Missing { get; set; } = Array.Empty<string>();
            public bool IsSatisfied => Missing.Count == 0;
        }

        public sealed class GitignoreEnsureResult
        {
            public string GitignorePath { get; set; } = string.Empty;
            public bool Created { get; set; }
            public IReadOnlyList<string> Added { get; set; } = Array.Empty<string>();
            public bool Changed => Created || Added.Count > 0;
        }

        /// <summary>
        /// 設定から、メインリポジトリ内に出力される（= .gitignore 登録が必要な）相対ディレクトリ一覧を求める。
        /// </summary>
        public static IReadOnlyList<string> GetRequiredEntries(MusiderunSettingsData data)
        {
            var repositoryRoot = PlatformUtility.GetRepositoryRoot();
            var entries = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string relativeDirectory)
            {
                var normalized = NormalizeRelativeDirectory(relativeDirectory);
                if (!string.IsNullOrEmpty(normalized) && seen.Add(normalized))
                {
                    entries.Add(normalized);
                }
            }

            Add("BatchJobLogs");

            if (!string.IsNullOrWhiteSpace(data?.logOutputDirectory) &&
                TryResolveRepositoryRelativeDirectory(repositoryRoot, data.logOutputDirectory, out var logRelative))
            {
                Add(logRelative);
            }

            return entries;
        }

        public static GitignoreCheckResult Check(MusiderunSettingsData data)
        {
            var repositoryRoot = PlatformUtility.GetRepositoryRoot();
            var gitignorePath = Path.Combine(repositoryRoot, ".gitignore");
            var required = GetRequiredEntries(data);

            var existingLines = File.Exists(gitignorePath)
                ? File.ReadAllLines(gitignorePath)
                : Array.Empty<string>();

            var missing = new List<string>();
            foreach (var entry in required)
            {
                if (!IsCovered(existingLines, entry))
                {
                    missing.Add(entry);
                }
            }

            return new GitignoreCheckResult
            {
                GitignorePath = gitignorePath,
                Exists = File.Exists(gitignorePath),
                Required = required,
                Missing = missing
            };
        }

        /// <summary>
        /// 不足しているエントリを .gitignore に追記する。ファイルが無ければ作成する。
        /// </summary>
        public static GitignoreEnsureResult Ensure(MusiderunSettingsData data)
        {
            var check = Check(data);
            var result = new GitignoreEnsureResult
            {
                GitignorePath = check.GitignorePath,
                Created = !check.Exists,
                Added = Array.Empty<string>()
            };

            if (check.Missing.Count == 0)
            {
                if (!check.Exists)
                {
                    result.Created = false;
                }

                return result;
            }

            var newline = DetectNewline(check.GitignorePath);
            var builder = new StringBuilder();

            if (check.Exists)
            {
                var existing = File.ReadAllText(check.GitignorePath);
                if (existing.Length > 0)
                {
                    builder.Append(existing);
                    if (!existing.EndsWith("\n", StringComparison.Ordinal))
                    {
                        builder.Append(newline);
                    }

                    builder.Append(newline);
                }
            }

            builder.Append(ManagedSectionHeader);
            builder.Append(newline);

            foreach (var entry in check.Missing)
            {
                builder.Append($"/**/{entry}/");
                builder.Append(newline);
            }

            File.WriteAllText(check.GitignorePath, builder.ToString());

            result.Added = check.Missing;
            return result;
        }

        private static bool IsCovered(IReadOnlyList<string> existingLines, string entry)
        {
            var acceptable = BuildAcceptableForms(entry);
            foreach (var rawLine in existingLines)
            {
                var line = (rawLine ?? string.Empty).Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (acceptable.Contains(line))
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<string> BuildAcceptableForms(string entry)
        {
            var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] prefixes = { string.Empty, "/", "**/", "/**/" };
            string[] suffixes = { string.Empty, "/" };
            foreach (var prefix in prefixes)
            {
                foreach (var suffix in suffixes)
                {
                    forms.Add($"{prefix}{entry}{suffix}");
                }
            }

            return forms;
        }

        private static string NormalizeRelativeDirectory(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace('\\', '/')
                .Trim()
                .Trim('/');
        }

        private static bool TryResolveRepositoryRelativeDirectory(
            string repositoryRoot,
            string configuredPath,
            out string relativeDirectory)
        {
            relativeDirectory = string.Empty;

            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.IsPathRooted(configuredPath)
                    ? Path.GetFullPath(configuredPath)
                    : Path.GetFullPath(Path.Combine(repositoryRoot, configuredPath));
            }
            catch (Exception)
            {
                return false;
            }

            var normalizedRoot = Path.GetFullPath(repositoryRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;

            var comparison = PlatformUtility.IsWindows
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (!fullPath.StartsWith(rootWithSeparator, comparison))
            {
                return false;
            }

            var relative = fullPath.Substring(rootWithSeparator.Length);
            relativeDirectory = NormalizeRelativeDirectory(relative);
            return !string.IsNullOrEmpty(relativeDirectory);
        }

        private static string DetectNewline(string path)
        {
            if (!File.Exists(path))
            {
                return Environment.NewLine;
            }

            var content = File.ReadAllText(path);
            return content.Contains("\r\n") ? "\r\n" : "\n";
        }
    }
}
