using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using Works.Mmzk.Util.Musiderun;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public sealed class MusiderunMirrorCleanResult
    {
        public int RemovedWorktrees { get; set; }
        public int RemovedBranches { get; set; }
        public bool RemovedFallbackBatchJobLogs { get; set; }
        public List<string> Warnings { get; } = new();
        public bool HasWarnings => Warnings.Count > 0;
    }

    public static class MusiderunMirrorCleaner
    {
        public static async Task<MusiderunMirrorCleanResult> CleanAllAsync(
            MusiderunSettingsData data,
            CancellationToken cancellationToken = default)
        {
            if (data == null)
            {
                throw new InvalidOperationException("Settings data is null.");
            }

            if (!PlatformUtility.TryResolveGitExecutable(out var gitExecutable, out var gitError))
            {
                throw new InvalidOperationException(gitError);
            }

            var repositoryRoot = PlatformUtility.GetRepositoryRoot();
            if (!Directory.Exists(Path.Combine(repositoryRoot, ".git")) &&
                !File.Exists(Path.Combine(repositoryRoot, ".git")))
            {
                throw new InvalidOperationException("Not a git repository.");
            }

            var result = new MusiderunMirrorCleanResult();
            var sync = new GitWorktreeMirrorSync(_ => { });

            await GitWorktreeMirrorSync.RepositoryGitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var originalBranch = await GetCurrentBranchAsync(
                        gitExecutable,
                        repositoryRoot,
                        cancellationToken)
                    .ConfigureAwait(false);

                var jobs = data.jobs ?? Array.Empty<BatchJobDefinitionData>();
                for (var i = 0; i < jobs.Length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var job = jobs[i];
                    if (job == null || string.IsNullOrEmpty(job.id))
                    {
                        continue;
                    }

                    var progress = jobs.Length == 0 ? 1f : (float)(i + 1) / jobs.Length;
                    EditorMainThreadDispatcher.Enqueue(() =>
                        EditorUtility.DisplayProgressBar(
                            "Clean Mirror Worktrees",
                            $"Cleaning job '{job.id}'...",
                            progress));

                    if (!PlatformUtility.TryValidateMirrorPath(data, job.id, out var mirrorPath, out var pathError))
                    {
                        result.Warnings.Add($"[{job.id}] {pathError}");
                        continue;
                    }

                    var branchName = PlatformUtility.ResolveMirrorBranch(data, job.id);
                    var status = await sync.GetMirrorStatusAsync(data, job.id, cancellationToken)
                        .ConfigureAwait(false);

                    switch (status)
                    {
                        case MirrorWorktreeStatus.Ready:
                            if (await TryRemoveWorktreeAsync(
                                    gitExecutable,
                                    repositoryRoot,
                                    mirrorPath,
                                    cancellationToken)
                                .ConfigureAwait(false))
                            {
                                result.RemovedWorktrees++;
                            }
                            else
                            {
                                result.Warnings.Add(
                                    $"[{job.id}] Failed to remove registered worktree: {mirrorPath}");
                            }

                            break;

                        case MirrorWorktreeStatus.Orphaned:
                            if (PlatformUtility.TryDeleteMirrorDirectory(mirrorPath, out var deleteError))
                            {
                                result.RemovedWorktrees++;
                            }
                            else
                            {
                                result.Warnings.Add($"[{job.id}] Failed to delete orphaned directory: {deleteError}");
                            }

                            break;
                    }

                    if (await BranchExistsAsync(gitExecutable, repositoryRoot, branchName, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        var currentBranch = await GetCurrentBranchAsync(
                                gitExecutable,
                                repositoryRoot,
                                cancellationToken)
                            .ConfigureAwait(false);

                        if (string.Equals(currentBranch, branchName, StringComparison.Ordinal))
                        {
                            result.Warnings.Add(
                                $"[{job.id}] Skipped branch deletion because it is checked out: {branchName}");
                        }
                        else if (await TryDeleteBranchAsync(
                                     gitExecutable,
                                     repositoryRoot,
                                     branchName,
                                     cancellationToken)
                                 .ConfigureAwait(false))
                        {
                            result.RemovedBranches++;
                        }
                        else
                        {
                            result.Warnings.Add($"[{job.id}] Failed to delete branch: {branchName}");
                        }
                    }
                }

                var fallbackPath = PlatformUtility.GetRepositoryBatchJobLogsPath();
                var fallbackExisted = Directory.Exists(fallbackPath);
                if (PlatformUtility.TryDeleteRepositoryBatchJobLogs(out var logsError))
                {
                    if (fallbackExisted)
                    {
                        result.RemovedFallbackBatchJobLogs = true;
                    }
                }
                else
                {
                    result.Warnings.Add($"Failed to delete fallback BatchJobLogs: {logsError}");
                }

                await PruneStaleWorktreesAsync(gitExecutable, repositoryRoot, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrEmpty(originalBranch) &&
                    !string.Equals(
                        await GetCurrentBranchAsync(gitExecutable, repositoryRoot, cancellationToken)
                            .ConfigureAwait(false),
                        originalBranch,
                        StringComparison.Ordinal))
                {
                    await RunGitAsync(
                            gitExecutable,
                            repositoryRoot,
                            cancellationToken,
                            "checkout",
                            originalBranch)
                        .ConfigureAwait(false);
                }

                MusiderunSessionState.Clear();
            }
            finally
            {
                GitWorktreeMirrorSync.RepositoryGitLock.Release();
                EditorMainThreadDispatcher.Enqueue(EditorUtility.ClearProgressBar);
            }

            return result;
        }

        private static async Task<bool> TryRemoveWorktreeAsync(
            string gitExecutable,
            string repositoryRoot,
            string mirrorPath,
            CancellationToken cancellationToken)
        {
            var processResult = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    new[] { "worktree", "remove", "--force", mirrorPath },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return processResult.Succeeded;
        }

        private static async Task<bool> TryDeleteBranchAsync(
            string gitExecutable,
            string repositoryRoot,
            string branchName,
            CancellationToken cancellationToken)
        {
            var processResult = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    new[] { "branch", "-D", branchName },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return processResult.Succeeded;
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

        private static async Task<string> GetCurrentBranchAsync(
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

        private static async Task<bool> BranchExistsAsync(
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

        private static async Task RunGitAsync(
            string gitExecutable,
            string workingDirectory,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            var result = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    arguments,
                    workingDirectory,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                var details = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                throw new InvalidOperationException(
                    $"git command failed (exit {result.ExitCode}): git {string.Join(" ", arguments)}\n{details}");
            }
        }
    }
}
