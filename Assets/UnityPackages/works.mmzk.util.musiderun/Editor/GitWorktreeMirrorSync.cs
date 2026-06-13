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

        public async Task SyncJobAsync(
            MusiderunSettingsData data,
            BatchJobDefinitionData job,
            CancellationToken cancellationToken = default)
        {
            await RepositoryGitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await EditorMainThreadDispatcher.RunAsync(() =>
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.StartAssetEditing();
                }).ConfigureAwait(false);

                try
                {
                    var context = RequireContext(data, job.id);
                    await ValidateMainWorktreeBranchAsync(
                            context.GitExecutable,
                            context.RepositoryRoot,
                            data,
                            _log,
                            cancellationToken)
                        .ConfigureAwait(false);

                    var stateBefore = await CaptureWorkingTreeStateAsync(
                            context.GitExecutable,
                            context.RepositoryRoot,
                            cancellationToken)
                        .ConfigureAwait(false);

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

                    var stateAfter = await CaptureWorkingTreeStateAsync(
                            context.GitExecutable,
                            context.RepositoryRoot,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (!string.Equals(stateBefore, stateAfter, StringComparison.Ordinal))
                    {
                        await LogWorkingTreeStateDiagnosticsAsync(
                                context.GitExecutable,
                                context.RepositoryRoot,
                                job.id,
                                stateBefore,
                                stateAfter,
                                cancellationToken)
                            .ConfigureAwait(false);
                        throw new InvalidOperationException(
                            "ミラー同期後にメイン作業ツリーの状態が変化しました。安全のため Job を中断しました。");
                    }
                }
                finally
                {
                    await EditorMainThreadDispatcher.RunAsync(AssetDatabase.StopAssetEditing)
                        .ConfigureAwait(false);
                }
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

        public static async Task ValidateMainWorktreeBranchAsync(
            MusiderunSettingsData data,
            Action<string> log,
            CancellationToken cancellationToken = default)
        {
            if (!PlatformUtility.TryResolveGitExecutable(out var gitExecutable, out var errorMessage))
            {
                throw new InvalidOperationException(errorMessage);
            }

            var repositoryRoot = PlatformUtility.GetRepositoryRoot();
            await ValidateMainWorktreeBranchAsync(
                    gitExecutable,
                    repositoryRoot,
                    data,
                    log,
                    cancellationToken)
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

        private async Task CreateSnapshotAndResetAsync(
            string gitExecutable,
            string repositoryRoot,
            string mirrorPath,
            string branchName,
            string jobId,
            CancellationToken cancellationToken)
        {
            var savedIndexTreeResult = await RunGitAsync(
                    gitExecutable,
                    repositoryRoot,
                    cancellationToken,
                    "write-tree")
                .ConfigureAwait(false);
            var savedIndexTree = savedIndexTreeResult.StandardOutput.Trim();
            if (string.IsNullOrEmpty(savedIndexTree))
            {
                throw new InvalidOperationException("同期前の git write-tree が空のハッシュを返しました。");
            }

            await RunGitAsync(gitExecutable, repositoryRoot, cancellationToken, "add", "-A")
                .ConfigureAwait(false);

            var treeResult = await RunGitAsync(gitExecutable, repositoryRoot, cancellationToken, "write-tree")
                .ConfigureAwait(false);
            var treeHash = treeResult.StandardOutput.Trim();
            if (string.IsNullOrEmpty(treeHash))
            {
                throw new InvalidOperationException("git write-tree が空のハッシュを返しました。");
            }

            var branchExists = await BranchExistsAsync(
                    gitExecutable,
                    repositoryRoot,
                    branchName,
                    cancellationToken)
                .ConfigureAwait(false);
            var parentHash = branchExists
                ? await TryGetBranchHeadAsync(gitExecutable, repositoryRoot, branchName, cancellationToken)
                    .ConfigureAwait(false)
                : string.Empty;
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

            if (!branchExists)
            {
                _log($"[{jobId}] mirror ブランチを作成します: {branchName}");
                await RunGitAsync(
                        gitExecutable,
                        repositoryRoot,
                        cancellationToken,
                        "branch",
                        branchName,
                        commitHash)
                    .ConfigureAwait(false);
            }

            if (!await IsWorktreeRegisteredAsync(gitExecutable, repositoryRoot, mirrorPath, cancellationToken)
                    .ConfigureAwait(false))
            {
                await EnsureWorktreeAsync(
                        gitExecutable,
                        repositoryRoot,
                        mirrorPath,
                        branchName,
                        commitHash,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            _log($"[{jobId}] ミラー worktree を同期します: {commitHash[..Math.Min(8, commitHash.Length)]}");
            await RunGitAsync(gitExecutable, mirrorPath, cancellationToken, "reset", "--hard", commitHash)
                .ConfigureAwait(false);

            await RestoreMainIndexAsync(gitExecutable, repositoryRoot, savedIndexTree, cancellationToken)
                .ConfigureAwait(false);

            _log($"[{jobId}] 同期完了。");
        }

        private static async Task RestoreMainIndexAsync(
            string gitExecutable,
            string repositoryRoot,
            string savedIndexTree,
            CancellationToken cancellationToken)
        {
            await RunGitSnapshotOrThrowAsync(
                    gitExecutable,
                    repositoryRoot,
                    cancellationToken,
                    "read-tree",
                    savedIndexTree)
                .ConfigureAwait(false);
        }

        private static async Task ValidateMainWorktreeBranchAsync(
            string gitExecutable,
            string repositoryRoot,
            MusiderunSettingsData data,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            var currentBranch = await GetCurrentBranchAsync(gitExecutable, repositoryRoot, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(currentBranch))
            {
                return;
            }

            var mirrorPrefix = GetMirrorBranchPrefix(data);
            if (!IsMirrorBranch(currentBranch, mirrorPrefix))
            {
                MusiderunMainBranchStore.Save(repositoryRoot, currentBranch);
                return;
            }

            log?.Invoke(
                $"[WARN] メイン worktree が mirror ブランチ '{currentBranch}' です。作業ブランチへ復帰します。");

            var recoveryBranch = await ResolveRecoveryBranchAsync(
                    gitExecutable,
                    repositoryRoot,
                    data,
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(recoveryBranch))
            {
                await CreateAndCheckoutWorkingBranchAsync(
                        gitExecutable,
                        repositoryRoot,
                        data,
                        currentBranch,
                        log,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            log?.Invoke($"> git checkout {recoveryBranch}");
            await RunGitSnapshotOrThrowAsync(
                    gitExecutable,
                    repositoryRoot,
                    cancellationToken,
                    "checkout",
                    recoveryBranch)
                .ConfigureAwait(false);
            MusiderunMainBranchStore.Save(repositoryRoot, recoveryBranch);
            log?.Invoke($"[INFO] メイン worktree を '{recoveryBranch}' に切り替えました。");
        }

        private static async Task CreateAndCheckoutWorkingBranchAsync(
            string gitExecutable,
            string repositoryRoot,
            MusiderunSettingsData data,
            string currentBranch,
            Action<string> log,
            CancellationToken cancellationToken)
        {
            var mirrorPrefix = GetMirrorBranchPrefix(data);
            var newBranch = ResolveNewWorkingBranchName(data, repositoryRoot, mirrorPrefix);

            if (string.IsNullOrEmpty(newBranch) || IsMirrorBranch(newBranch, mirrorPrefix))
            {
                throw new InvalidOperationException(
                    $"メイン worktree が mirror ブランチ '{currentBranch}' を checkout したままです。\n" +
                    "復帰先の作業ブランチを特定できませんでした。手動で通常ブランチに戻してから Job を実行してください。\n" +
                    "例: git checkout main");
            }

            if (await BranchExistsAsync(gitExecutable, repositoryRoot, newBranch, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"メイン worktree が mirror ブランチ '{currentBranch}' を checkout したままです。\n" +
                    $"作業ブランチ '{newBranch}' は既に存在するため自動回復できませんでした。手動で通常ブランチに戻してから Job を実行してください。\n" +
                    $"例: git checkout {newBranch}");
            }

            log?.Invoke(
                $"[WARN] 復帰先の作業ブランチが見つかりませんでした。現在の状態から '{newBranch}' を作成して復帰します。");
            log?.Invoke($"> git checkout -b {newBranch}");

            try
            {
                await RunGitSnapshotOrThrowAsync(
                        gitExecutable,
                        repositoryRoot,
                        cancellationToken,
                        "checkout",
                        "-b",
                        newBranch)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    $"メイン worktree が mirror ブランチ '{currentBranch}' を checkout したままです。\n" +
                    $"作業ブランチ '{newBranch}' の自動作成に失敗しました。手動で通常ブランチに戻してから Job を実行してください。\n" +
                    $"例: git checkout -b {newBranch}\n{ex.Message}");
            }

            MusiderunMainBranchStore.Save(repositoryRoot, newBranch);
            log?.Invoke($"[INFO] 作業ブランチ '{newBranch}' を作成して復帰しました。");
        }

        private static string ResolveNewWorkingBranchName(
            MusiderunSettingsData data,
            string repositoryRoot,
            string mirrorPrefix)
        {
            var savedBranch = MusiderunMainBranchStore.Load(repositoryRoot);
            if (!string.IsNullOrWhiteSpace(savedBranch) && !IsMirrorBranch(savedBranch, mirrorPrefix))
            {
                return savedBranch.Trim();
            }

            var configured = data?.defaultWorkingBranch;
            if (!string.IsNullOrWhiteSpace(configured) && !IsMirrorBranch(configured.Trim(), mirrorPrefix))
            {
                return configured.Trim();
            }

            return "main";
        }

        private static async Task<string> ResolveRecoveryBranchAsync(
            string gitExecutable,
            string repositoryRoot,
            MusiderunSettingsData data,
            CancellationToken cancellationToken)
        {
            var mirrorPrefix = GetMirrorBranchPrefix(data);
            var candidates = new List<string>();

            var savedBranch = MusiderunMainBranchStore.Load(repositoryRoot);
            if (!string.IsNullOrEmpty(savedBranch))
            {
                candidates.Add(savedBranch);
            }

            candidates.AddRange(new[] { "main", "master", "develop" });

            var localBranches = await ListLocalBranchesAsync(gitExecutable, repositoryRoot, cancellationToken)
                .ConfigureAwait(false);
            foreach (var branch in localBranches)
            {
                candidates.Add(branch);
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !seen.Add(candidate))
                {
                    continue;
                }

                if (IsMirrorBranch(candidate, mirrorPrefix))
                {
                    continue;
                }

                if (await BranchExistsAsync(gitExecutable, repositoryRoot, candidate, cancellationToken)
                        .ConfigureAwait(false))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static async Task<List<string>> ListLocalBranchesAsync(
            string gitExecutable,
            string repositoryRoot,
            CancellationToken cancellationToken)
        {
            var result = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    new[] { "branch", "--format=%(refname:short)" },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var branches = new List<string>();
            if (!result.Succeeded)
            {
                return branches;
            }

            foreach (var line in result.StandardOutput.Split('\n'))
            {
                var branch = line.Trim();
                if (!string.IsNullOrEmpty(branch))
                {
                    branches.Add(branch);
                }
            }

            return branches;
        }

        private static string GetMirrorBranchPrefix(MusiderunSettingsData data)
        {
            return string.IsNullOrWhiteSpace(data?.mirrorBranchPrefix)
                ? "musiderun/mirror"
                : data.mirrorBranchPrefix.TrimEnd('/');
        }

        private static bool IsMirrorBranch(string branchName, string mirrorBranchPrefix)
        {
            return branchName.StartsWith(mirrorBranchPrefix + "-", StringComparison.Ordinal);
        }

        private async Task LogWorkingTreeStateDiagnosticsAsync(
            string gitExecutable,
            string repositoryRoot,
            string jobId,
            string stateBefore,
            string stateAfter,
            CancellationToken cancellationToken)
        {
            _log($"[{jobId}] [WARN] 作業ツリー状態が一致しません。");
            _log($"[{jobId}] stateBefore={(string.IsNullOrEmpty(stateBefore) ? "(empty)" : stateBefore)}");
            _log($"[{jobId}] stateAfter={(string.IsNullOrEmpty(stateAfter) ? "(empty)" : stateAfter)}");

            var statusResult = await PlatformUtility.RunGitSnapshotProcessAsync(
                    gitExecutable,
                    new[] { "status", "--short" },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (statusResult.Succeeded)
            {
                var status = statusResult.StandardOutput.TrimEnd();
                _log(string.IsNullOrEmpty(status)
                    ? $"[{jobId}] git status --short: (clean)"
                    : $"[{jobId}] git status --short:\n{status}");
            }

            var diffResult = await PlatformUtility.RunGitSnapshotProcessAsync(
                    gitExecutable,
                    new[] { "diff", "HEAD", "--stat" },
                    repositoryRoot,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (diffResult.Succeeded)
            {
                var diffStat = TrimDiagnosticOutput(diffResult.StandardOutput, maxLines: 20);
                _log(string.IsNullOrEmpty(diffStat)
                    ? $"[{jobId}] git diff HEAD --stat: (no diff)"
                    : $"[{jobId}] git diff HEAD --stat:\n{diffStat}");
            }
        }

        private static string TrimDiagnosticOutput(string text, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (lines.Length <= maxLines)
            {
                return string.Join("\n", lines);
            }

            var head = string.Join("\n", lines, 0, maxLines);
            return head + $"\n... ({lines.Length - maxLines} more lines)";
        }

        private async Task EnsureWorktreeAsync(
            string gitExecutable,
            string repositoryRoot,
            string mirrorPath,
            string branchName,
            string commitHash,
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
            _log($"> git -c core.autocrlf=false -c core.safecrlf=false worktree add {mirrorPath} {branchName}");
            var addResult = await PlatformUtility.RunGitSnapshotProcessAsync(
                    gitExecutable,
                    new[] { "worktree", "add", mirrorPath, branchName },
                    repositoryRoot,
                    line => _log(line),
                    line => _log($"[stderr] {line}"),
                    cancellationToken)
                .ConfigureAwait(false);

            if (addResult.Succeeded)
            {
                return;
            }

            var addError = CombineProcessOutput(addResult);
            if (IsBranchAlreadyUsedByWorktreeError(addError))
            {
                _log(
                    $"[WARN] ブランチ '{branchName}' は他の worktree で使用中のため、detach モードで追加します。");
                await RunGitAsync(
                        gitExecutable,
                        repositoryRoot,
                        cancellationToken,
                        "worktree",
                        "add",
                        "--detach",
                        mirrorPath,
                        commitHash)
                    .ConfigureAwait(false);
                return;
            }

            throw new InvalidOperationException(
                $"git コマンドが失敗しました (exit {addResult.ExitCode}): git -c core.autocrlf=false -c core.safecrlf=false worktree add {mirrorPath} {branchName}\n{addError}");
        }

        private static bool IsBranchAlreadyUsedByWorktreeError(string errorText)
        {
            return errorText.Contains("already used by worktree", StringComparison.OrdinalIgnoreCase);
        }

        private static string CombineProcessOutput(ProcessResult result)
        {
            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                return result.StandardError;
            }

            return result.StandardOutput ?? string.Empty;
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

        internal static async Task<string> CaptureWorkingTreeStateAsync(
            string gitExecutable,
            string repositoryRoot,
            CancellationToken cancellationToken)
        {
            // 以前は `git stash create --include-untracked` の結果を指紋に使っていたが、
            // 環境によっては exit 1 で失敗する（未追跡ファイルのロックや git の挙動差など）。
            // また stash create のコミット SHA は日時が埋め込まれ非決定的だった。
            //
            // 代わりに「実インデックスに触れない一時インデックス」上で
            //   read-tree HEAD → add -A → write-tree
            // を行い、内容アドレス（決定的）であるツリーハッシュを指紋として返す。
            // これにより stash create への依存をなくし、作業ツリーの内容（追跡＋未追跡）を
            // 安定して比較できる。
            var tempIndexPath = Path.Combine(
                Path.GetTempPath(),
                $"musiderun-fingerprint-{Guid.NewGuid():N}.index");
            var environment = new Dictionary<string, string>
            {
                ["GIT_INDEX_FILE"] = tempIndexPath
            };

            try
            {
                // HEAD があれば一時インデックスを HEAD ツリーで初期化（差分計算が高速になる）。
                // HEAD が無い（コミット未作成）リポジトリでは失敗するが、空インデックスのまま続行する。
                await RunGitForFingerprintAsync(
                        gitExecutable,
                        repositoryRoot,
                        environment,
                        cancellationToken,
                        allowFailure: true,
                        "read-tree",
                        "HEAD")
                    .ConfigureAwait(false);

                // 追跡ファイルの変更・未追跡ファイルの追加・削除を一時インデックスへ反映（.gitignore を尊重）。
                await RunGitForFingerprintAsync(
                        gitExecutable,
                        repositoryRoot,
                        environment,
                        cancellationToken,
                        allowFailure: false,
                        "add",
                        "-A")
                    .ConfigureAwait(false);

                var treeResult = await RunGitForFingerprintAsync(
                        gitExecutable,
                        repositoryRoot,
                        environment,
                        cancellationToken,
                        allowFailure: false,
                        "write-tree")
                    .ConfigureAwait(false);

                return treeResult.StandardOutput.Trim();
            }
            finally
            {
                try
                {
                    if (File.Exists(tempIndexPath))
                    {
                        File.Delete(tempIndexPath);
                    }
                }
                catch
                {
                    // 一時インデックスの削除失敗は致命的ではないため無視する。
                }
            }
        }

        private static async Task<ProcessResult> RunGitForFingerprintAsync(
            string gitExecutable,
            string repositoryRoot,
            IReadOnlyDictionary<string, string> environment,
            CancellationToken cancellationToken,
            bool allowFailure,
            params string[] arguments)
        {
            var result = await PlatformUtility.RunGitSnapshotProcessAsync(
                    gitExecutable,
                    arguments,
                    repositoryRoot,
                    cancellationToken: cancellationToken,
                    environment: environment)
                .ConfigureAwait(false);

            if (!result.Succeeded && !allowFailure)
            {
                var details = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                throw new InvalidOperationException(
                    $"作業ツリー状態の取得に失敗しました (exit {result.ExitCode}): " +
                    $"git {string.Join(" ", arguments)}\n{details}");
            }

            return result;
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

        private static async Task<string> GetCurrentBranchAsync(
            string gitExecutable,
            string repositoryRoot,
            CancellationToken cancellationToken)
        {
            var result = await PlatformUtility.RunProcessAsync(
                    gitExecutable,
                    new[] { "branch", "--show-current" },
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
            EditorMainThreadDispatcher.Enqueue(() =>
                _log($"> git -c core.autocrlf=false -c core.safecrlf=false {string.Join(" ", arguments)}"));

            return await RunGitSnapshotOrThrowAsync(
                    gitExecutable,
                    workingDirectory,
                    cancellationToken,
                    line => _log(line),
                    line => _log($"[stderr] {line}"),
                    arguments)
                .ConfigureAwait(false);
        }

        private static async Task<ProcessResult> RunGitSnapshotOrThrowAsync(
            string gitExecutable,
            string workingDirectory,
            CancellationToken cancellationToken,
            params string[] arguments)
        {
            return await RunGitSnapshotOrThrowAsync(
                    gitExecutable,
                    workingDirectory,
                    cancellationToken,
                    null,
                    null,
                    arguments)
                .ConfigureAwait(false);
        }

        private static async Task<ProcessResult> RunGitSnapshotOrThrowAsync(
            string gitExecutable,
            string workingDirectory,
            CancellationToken cancellationToken,
            Action<string> onStdout,
            Action<string> onStderr,
            params string[] arguments)
        {
            var result = await PlatformUtility.RunGitSnapshotProcessAsync(
                    gitExecutable,
                    arguments,
                    workingDirectory,
                    onStdout,
                    onStderr,
                    cancellationToken)
                .ConfigureAwait(false);

            if (!result.Succeeded)
            {
                var details = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;
                throw new InvalidOperationException(
                    $"git コマンドが失敗しました (exit {result.ExitCode}): git -c core.autocrlf=false -c core.safecrlf=false {string.Join(" ", arguments)}\n{details}");
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
