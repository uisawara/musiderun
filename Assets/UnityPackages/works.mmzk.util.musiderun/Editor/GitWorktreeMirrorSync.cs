using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public sealed class GitWorktreeMirrorSync
    {
        internal static readonly SemaphoreSlim RepositoryGitLock = new(1, 1);

        private readonly Action<string> _log;

        public GitWorktreeMirrorSync(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        public async Task<MirrorWorktreeStatus> GetMirrorStatusAsync(
            MusiderunSettingsData data,
            string jobId,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetContext(data, jobId, out var context))
            {
                return MirrorWorktreeStatus.NotCreated;
            }

            if (await IsWorktreeRegisteredAsync(
                    context.GitExecutable,
                    context.RepositoryRoot,
                    context.MirrorPath,
                    cancellationToken)
                .ConfigureAwait(false))
            {
                return MirrorWorktreeStatus.Ready;
            }

            return Directory.Exists(context.MirrorPath)
                ? MirrorWorktreeStatus.Orphaned
                : MirrorWorktreeStatus.NotCreated;
        }

        /// <summary>
        /// メインリポジトリの HEAD コミットの内容を、detached なミラー worktree へ反映する。
        /// メイン作業ツリー・index には一切触れない（コミット済みの内容のみが対象）。
        /// </summary>
        public async Task SyncJobAsync(
            MusiderunSettingsData data,
            BatchJobDefinitionData job,
            CancellationToken cancellationToken = default)
        {
            await RepositoryGitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var context = RequireContext(data, job.id);

                var commit = await ResolveHeadCommitAsync(
                        context.GitExecutable,
                        context.RepositoryRoot,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrEmpty(commit))
                {
                    throw new InvalidOperationException(
                        "コミットがありません。最初のコミットを作成してから Job を実行してください。");
                }

                _log($"[{job.id}] === ミラー同期 ===");
                _log($"[{job.id}] リポジトリ: {context.RepositoryRoot}");
                _log($"[{job.id}] ミラー: {context.MirrorPath}");
                _log($"[{job.id}] コミット: {ShortCommit(commit)}");

                await SyncMirrorToCommitAsync(
                        context.GitExecutable,
                        context.RepositoryRoot,
                        context.MirrorPath,
                        commit,
                        _log,
                        cancellationToken)
                    .ConfigureAwait(false);

                _log($"[{job.id}] 同期完了。");
            }
            finally
            {
                RepositoryGitLock.Release();
            }
        }

        public static bool TrySaveProjectStateOnMainThread()
        {
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return false;
            }

            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// ミラー worktree を、指定コミット（通常はメインの HEAD）の内容へ同期する。
        /// worktree が無ければ detached HEAD で作成し、その後 reset --hard する。
        /// メインリポジトリの作業ツリー・index・ブランチには影響しない。
        /// </summary>
        internal static async Task SyncMirrorToCommitAsync(
            string gitExecutable,
            string repositoryRoot,
            string mirrorPath,
            string commit,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            log ??= _ => { };

            await EnsureDetachedWorktreeAsync(
                    gitExecutable,
                    repositoryRoot,
                    mirrorPath,
                    commit,
                    log,
                    cancellationToken)
                .ConfigureAwait(false);

            await RunGitOrThrowAsync(
                    gitExecutable,
                    mirrorPath,
                    log,
                    cancellationToken,
                    "reset",
                    "--hard",
                    commit)
                .ConfigureAwait(false);
        }

        private bool TryGetContext(
            MusiderunSettingsData data,
            string jobId,
            out MirrorContext context)
        {
            context = null;

            if (data == null || string.IsNullOrEmpty(jobId))
            {
                return false;
            }

            if (!PlatformUtility.TryResolveGitExecutable(out var gitExecutable, out _))
            {
                return false;
            }

            var repositoryRoot = PlatformUtility.GetRepositoryRoot();
            if (!Directory.Exists(Path.Combine(repositoryRoot, ".git")) &&
                !File.Exists(Path.Combine(repositoryRoot, ".git")))
            {
                return false;
            }

            if (!PlatformUtility.TryValidateMirrorPath(data, jobId, out var mirrorPath, out _))
            {
                return false;
            }

            context = new MirrorContext(gitExecutable, repositoryRoot, mirrorPath);
            return true;
        }

        private MirrorContext RequireContext(MusiderunSettingsData data, string jobId)
        {
            if (!PlatformUtility.TryResolveGitExecutable(out var gitExecutable, out var errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }

            var repositoryRoot = PlatformUtility.GetRepositoryRoot();
            if (!Directory.Exists(Path.Combine(repositoryRoot, ".git")) &&
                !File.Exists(Path.Combine(repositoryRoot, ".git")))
            {
                throw new InvalidOperationException("git リポジトリではありません。プロジェクトルートで git init してください。");
            }

            PlatformUtility.ValidateMirrorPathOrThrow(data, jobId);

            return new MirrorContext(
                gitExecutable,
                repositoryRoot,
                PlatformUtility.ResolveMirrorPath(data, jobId));
        }

        private static async Task<string> ResolveHeadCommitAsync(
            string gitExecutable,
            string repositoryRoot,
            CancellationToken cancellationToken)
        {
            var result = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    new[] { "rev-parse", "HEAD" },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return result.Succeeded ? result.StandardOutput.Trim() : string.Empty;
        }

        private static async Task EnsureDetachedWorktreeAsync(
            string gitExecutable,
            string repositoryRoot,
            string mirrorPath,
            string commit,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            if (await IsWorktreeRegisteredAsync(gitExecutable, repositoryRoot, mirrorPath, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            await PruneStaleWorktreesAsync(gitExecutable, repositoryRoot, cancellationToken)
                .ConfigureAwait(false);

            if (await IsWorktreeRegisteredAsync(gitExecutable, repositoryRoot, mirrorPath, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            if (Directory.Exists(mirrorPath))
            {
                log($"[WARN] 未登録のミラーディレクトリを削除します: {mirrorPath}");
                if (!PlatformUtility.TryDeleteMirrorDirectory(mirrorPath, out var deleteError))
                {
                    throw new InvalidOperationException(
                        $"ミラーパス '{mirrorPath}' は worktree 未登録で削除もできません: {deleteError}");
                }
            }

            log($"worktree を追加します: {mirrorPath}");
            await RunGitOrThrowAsync(
                    gitExecutable,
                    repositoryRoot,
                    log,
                    cancellationToken,
                    "worktree",
                    "add",
                    "--detach",
                    mirrorPath,
                    commit)
                .ConfigureAwait(false);
        }

        private static async Task PruneStaleWorktreesAsync(
            string gitExecutable,
            string repositoryRoot,
            CancellationToken cancellationToken)
        {
            await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    new[] { "worktree", "prune" },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<bool> IsWorktreeRegisteredAsync(
            string gitExecutable,
            string repositoryRoot,
            string mirrorPath,
            CancellationToken cancellationToken)
        {
            var normalizedMirror = Path.GetFullPath(mirrorPath);
            var result = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    new[] { "worktree", "list", "--porcelain" },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                return false;
            }

            foreach (var line in result.StandardOutput.Split('\n'))
            {
                if (!line.StartsWith("worktree ", StringComparison.Ordinal))
                {
                    continue;
                }

                var path = line["worktree ".Length..].Trim();
                if (string.Equals(Path.GetFullPath(path), normalizedMirror, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task<ProcessResult> RunGitOrThrowAsync(
            string gitExecutable,
            string workingDirectory,
            Action<string> log,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            log($"> git -c core.autocrlf=false -c core.safecrlf=false {string.Join(" ", arguments)}");

            var result = await PlatformUtility.RunGitSnapshotProcessAsync(
                    gitExecutable,
                    arguments,
                    workingDirectory,
                    line => log(line),
                    line => log($"[stderr] {line}"),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                var details = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                throw new InvalidOperationException(
                    $"git コマンドが失敗しました (exit {result.ExitCode}): " +
                    $"git -c core.autocrlf=false -c core.safecrlf=false {string.Join(" ", arguments)}\n{details}");
            }

            return result;
        }

        private static string ShortCommit(string commit)
        {
            return string.IsNullOrEmpty(commit)
                ? commit
                : commit[..Math.Min(8, commit.Length)];
        }

        private sealed class MirrorContext
        {
            public MirrorContext(
                string gitExecutable,
                string repositoryRoot,
                string mirrorPath)
            {
                GitExecutable = gitExecutable;
                RepositoryRoot = repositoryRoot;
                MirrorPath = mirrorPath;
            }

            public string GitExecutable { get; }
            public string RepositoryRoot { get; }
            public string MirrorPath { get; }
        }
    }
}
