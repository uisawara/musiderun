using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public sealed class GitWorktreeMirrorSync
    {
        private static readonly SemaphoreSlim RepositoryGitLock = new(1, 1);

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

        public async Task EnsureMirrorForJobAsync(
            MusiderunSettingsData data,
            BatchJobDefinitionData job,
            CancellationToken cancellationToken = default)
        {
            var context = RequireContext(data, job.id);
            _log($"[{job.id}] ミラー worktree を確認します: {context.MirrorPath}");

            await EnsureMirrorBranchAsync(
                    context.GitExecutable,
                    context.RepositoryRoot,
                    context.BranchName,
                    cancellationToken)
                .ConfigureAwait(false);
            await EnsureWorktreeAsync(
                    context.GitExecutable,
                    context.RepositoryRoot,
                    context.MirrorPath,
                    context.BranchName,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task SyncJobAsync(
            MusiderunSettingsData data,
            BatchJobDefinitionData job,
            CancellationToken cancellationToken = default)
        {
            await RepositoryGitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var context = RequireContext(data, job.id);
                await EnsureMirrorForJobAsync(data, job, cancellationToken).ConfigureAwait(false);

                _log($"[{job.id}] === ミラー同期 ===");
                _log($"[{job.id}] リポジトリ: {context.RepositoryRoot}");
                _log($"[{job.id}] ミラー: {context.MirrorPath}");
                _log($"[{job.id}] ブランチ: {context.BranchName}");

                await CreateSnapshotAndResetAsync(
                        context.GitExecutable,
                        context.RepositoryRoot,
                        context.MirrorPath,
                        context.BranchName,
                        job.id,
                        cancellationToken)
                    .ConfigureAwait(false);
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

            context = new MirrorContext(
                gitExecutable,
                repositoryRoot,
                mirrorPath,
                PlatformUtility.ResolveMirrorBranch(data, jobId));
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
                PlatformUtility.ResolveMirrorPath(data, jobId),
                PlatformUtility.ResolveMirrorBranch(data, jobId));
        }

        private async Task EnsureMirrorBranchAsync(
            string gitExecutable,
            string repositoryRoot,
            string branchName,
            CancellationToken cancellationToken)
        {
            if (await BranchExistsAsync(gitExecutable, repositoryRoot, branchName, cancellationToken)
                    .ConfigureAwait(false))
            {
                return;
            }

            var originalBranch = await GetCurrentBranchAsync(gitExecutable, repositoryRoot, cancellationToken)
                .ConfigureAwait(false);

            try
            {
                await RunGitAsync(
                        gitExecutable,
                        repositoryRoot,
                        cancellationToken,
                        "checkout",
                        "--orphan",
                        branchName)
                    .ConfigureAwait(false);
                await RunGitAsync(gitExecutable, repositoryRoot, cancellationToken, "reset", "--hard")
                    .ConfigureAwait(false);
                await RunGitAsync(
                        gitExecutable,
                        repositoryRoot,
                        cancellationToken,
                        "commit",
                        "--allow-empty",
                        "-m",
                        "batchjob mirror init")
                    .ConfigureAwait(false);
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalBranch) &&
                    !string.Equals(originalBranch, branchName, StringComparison.Ordinal))
                {
                    await RunGitAsync(gitExecutable, repositoryRoot, cancellationToken, "checkout", originalBranch)
                        .ConfigureAwait(false);
                }
            }
        }

        private async Task EnsureWorktreeAsync(
            string gitExecutable,
            string repositoryRoot,
            string mirrorPath,
            string branchName,
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
                _log($"[WARN] 未登録のミラーディレクトリを削除します: {mirrorPath}");
                if (!PlatformUtility.TryDeleteMirrorDirectory(mirrorPath, out var deleteError))
                {
                    throw new InvalidOperationException(
                        $"ミラーパス '{mirrorPath}' は worktree 未登録で削除もできません: {deleteError}");
                }
            }

            _log($"worktree を追加します: {mirrorPath}");
            await RunGitAsync(
                    gitExecutable,
                    repositoryRoot,
                    cancellationToken,
                    "worktree",
                    "add",
                    mirrorPath,
                    branchName)
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

        private async Task CreateSnapshotAndResetAsync(
            string gitExecutable,
            string repositoryRoot,
            string mirrorPath,
            string branchName,
            string jobId,
            CancellationToken cancellationToken)
        {
            await RunGitAsync(gitExecutable, repositoryRoot, cancellationToken, "add", "-A")
                .ConfigureAwait(false);

            var treeResult = await RunGitAsync(gitExecutable, repositoryRoot, cancellationToken, "write-tree")
                .ConfigureAwait(false);
            var treeHash = treeResult.StandardOutput.Trim();
            if (string.IsNullOrEmpty(treeHash))
            {
                throw new InvalidOperationException("git write-tree が空のハッシュを返しました。");
            }

            var parentHash = await TryGetBranchHeadAsync(
                    gitExecutable,
                    repositoryRoot,
                    branchName,
                    cancellationToken)
                .ConfigureAwait(false);
            var message = $"batchjob snapshot {jobId} {DateTime.Now:yyyy-MM-ddTHH:mm:ss}";

            ProcessResult commitResult;
            if (string.IsNullOrEmpty(parentHash))
            {
                commitResult = await RunGitAsync(
                        gitExecutable,
                        repositoryRoot,
                        cancellationToken,
                        "commit-tree",
                        treeHash,
                        "-m",
                        message)
                    .ConfigureAwait(false);
            }
            else
            {
                commitResult = await RunGitAsync(
                        gitExecutable,
                        repositoryRoot,
                        cancellationToken,
                        "commit-tree",
                        treeHash,
                        "-p",
                        parentHash,
                        "-m",
                        message)
                    .ConfigureAwait(false);
            }

            var commitHash = commitResult.StandardOutput.Trim();
            if (string.IsNullOrEmpty(commitHash))
            {
                throw new InvalidOperationException("git commit-tree が空のハッシュを返しました。");
            }

            _log($"[{jobId}] ミラー worktree を同期します: {commitHash[..Math.Min(8, commitHash.Length)]}");
            await RunGitAsync(gitExecutable, mirrorPath, cancellationToken, "reset", "--hard", commitHash)
                .ConfigureAwait(false);
            _log($"[{jobId}] 同期完了。");
        }

        private async Task<string> GetCurrentBranchAsync(
            string gitExecutable,
            string repositoryRoot,
            CancellationToken cancellationToken)
        {
            var result = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    new[] { "rev-parse", "--abbrev-ref", "HEAD" },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return result.Succeeded ? result.StandardOutput.Trim() : string.Empty;
        }

        private async Task<bool> BranchExistsAsync(
            string gitExecutable,
            string repositoryRoot,
            string branchName,
            CancellationToken cancellationToken)
        {
            var result = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    new[] { "rev-parse", "--verify", branchName },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return result.Succeeded;
        }

        private async Task<string> TryGetBranchHeadAsync(
            string gitExecutable,
            string repositoryRoot,
            string branchName,
            CancellationToken cancellationToken)
        {
            var result = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    new[] { "rev-parse", branchName },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return result.Succeeded ? result.StandardOutput.Trim() : string.Empty;
        }

        private async Task<bool> IsWorktreeRegisteredAsync(
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

        private async Task<ProcessResult> RunGitAsync(
            string gitExecutable,
            string workingDirectory,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            var args = new List<string>(arguments);
            EditorMainThreadDispatcher.Enqueue(() => _log($"> git {string.Join(" ", args)}"));

            var result = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    args,
                    workingDirectory,
                    line => _log(line),
                    line => _log($"[stderr] {line}"),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                var details = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                throw new InvalidOperationException(
                    $"git コマンドが失敗しました (exit {result.ExitCode}): git {string.Join(" ", args)}\n{details}");
            }

            return result;
        }

        private sealed class MirrorContext
        {
            public MirrorContext(
                string gitExecutable,
                string repositoryRoot,
                string mirrorPath,
                string branchName)
            {
                GitExecutable = gitExecutable;
                RepositoryRoot = repositoryRoot;
                MirrorPath = mirrorPath;
                BranchName = branchName;
            }

            public string GitExecutable { get; }
            public string RepositoryRoot { get; }
            public string MirrorPath { get; }
            public string BranchName { get; }
        }
    }
}
