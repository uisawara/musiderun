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

    }
}
