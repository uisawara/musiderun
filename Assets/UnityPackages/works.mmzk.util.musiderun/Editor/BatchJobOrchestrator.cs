using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using Works.Mmzk.Util.Musiderun;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public sealed class BatchJobExecution
    {
        public int JobIndex { get; set; } = -1;
        public BatchJobDefinitionData Definition { get; set; }
        public BatchJobState State { get; set; } = BatchJobState.Idle;
        public UnityBatchRunner Runner { get; set; }
        public BatchJobResult Result { get; set; }
        public string MirrorPath { get; set; } = string.Empty;
        public string MirrorLogFilePath { get; set; } = string.Empty;
        public string LogFilePath { get; set; } = string.Empty;
        public string LogHtmlFilePath { get; set; } = string.Empty;
        public string TestResultsPath { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
    }

    public sealed class BatchJobOrchestrator
    {
        private readonly Action<string> _log;
        private readonly Action _onJobsChanged;
        private readonly Action<BatchJobBatchResult> _onBatchCompleted;
        private readonly Action _onRepaintRequested;

        private readonly Dictionary<string, BatchJobExecution> _executions = new();
        private readonly MusiderunLogWriter _mirrorLogWriter = new();
        private CancellationTokenSource _operationCts;
        private DateTime _batchStartedAt;
        private bool _updateRegistered;
        private bool _sequentialBatchActive;
        private TaskCompletionSource<bool> _currentJobCompletion;

        public BatchJobOrchestrator(
            Action<string> log,
            Action onJobsChanged,
            Action<BatchJobBatchResult> onBatchCompleted,
            Action onRepaintRequested)
        {
            _log = line => EditorMainThreadDispatcher.Enqueue(() => (log ?? (_ => { }))(line));
            _onJobsChanged = onJobsChanged ?? (() => { });
            _onBatchCompleted = onBatchCompleted ?? (_ => { });
            _onRepaintRequested = onRepaintRequested ?? (() => { });
        }

        public BatchJobBatchResult LastBatchResult { get; private set; }

        public bool IsBusy =>
            _sequentialBatchActive ||
            _executions.Values.Any(execution =>
                execution.State is BatchJobState.Syncing or BatchJobState.Running);

        public IReadOnlyDictionary<string, BatchJobExecution> Executions => _executions;

        public BatchJobState GetJobState(int jobIndex)
        {
            return _executions.TryGetValue(GetRunKey(jobIndex), out var execution)
                ? execution.State
                : BatchJobState.Idle;
        }

        public bool TryGetJobStartedAt(int jobIndex, out DateTime startedAt)
        {
            if (_executions.TryGetValue(GetRunKey(jobIndex), out var execution) &&
                execution.StartedAt != default)
            {
                startedAt = execution.StartedAt;
                return true;
            }

            startedAt = default;
            return false;
        }

        public void StartJobsAsync(
            MusiderunSettingsData data,
            IReadOnlyList<int> jobIndices)
        {
            if (IsBusy)
            {
                _log("[ERROR] 別のバッチが実行中です。");
                return;
            }

            if (data?.jobs == null || jobIndices == null || jobIndices.Count == 0)
            {
                _log("[ERROR] 実行対象の Job がありません。");
                return;
            }

            if (!GitWorktreeMirrorSync.TrySaveProjectStateOnMainThread())
            {
                _log("[ERROR] Cannot run jobs during Play mode. Exit Play mode and save your work first.");
                return;
            }

            _operationCts = new CancellationTokenSource();
            _batchStartedAt = DateTime.Now;
            LastBatchResult = null;
            _executions.Clear();

            var runnableIndices = new List<int>();
            foreach (var jobIndex in jobIndices)
            {
                if (jobIndex < 0 || jobIndex >= data.jobs.Length)
                {
                    continue;
                }

                var job = data.jobs[jobIndex];
                if (job == null || string.IsNullOrEmpty(job.id))
                {
                    continue;
                }

                var runKey = GetRunKey(jobIndex);
                if (!BatchJobTargetOSUtility.MatchesCurrentOS(job.GetTargetOS()))
                {
                    _executions[runKey] = new BatchJobExecution
                    {
                        JobIndex = jobIndex,
                        Definition = job,
                        State = BatchJobState.Skipped,
                        StartedAt = _batchStartedAt,
                        Result = CreateSkippedResult(
                            job,
                            $"対象 OS ({job.targetOS}) と現在の OS ({BatchJobTargetOSUtility.GetCurrentOSLabel()}) が一致しません。")
                    };
                    continue;
                }

                runnableIndices.Add(jobIndex);
            }

            for (var i = 0; i < runnableIndices.Count; i++)
            {
                var jobIndex = runnableIndices[i];
                var job = data.jobs[jobIndex];
                _executions[GetRunKey(jobIndex)] = new BatchJobExecution
                {
                    JobIndex = jobIndex,
                    Definition = job,
                    State = i == 0 ? BatchJobState.Syncing : BatchJobState.Queued,
                    StartedAt = _batchStartedAt
                };
            }

            NotifyJobsChanged();
            _sequentialBatchActive = true;
            _ = RunJobsSequentiallyAsync(data, runnableIndices);
        }

        private static string GetRunKey(int jobIndex) => jobIndex.ToString();

        private static int FindJobIndex(
            MusiderunSettingsData data,
            string jobId,
            string batchArguments)
        {
            if (data?.jobs == null || string.IsNullOrEmpty(jobId))
            {
                return -1;
            }

            for (var i = 0; i < data.jobs.Length; i++)
            {
                var job = data.jobs[i];
                if (job == null)
                {
                    continue;
                }

                if (!string.Equals(job.id, jobId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(batchArguments) &&
                    !string.Equals(job.batchArguments, batchArguments, StringComparison.Ordinal))
                {
                    continue;
                }

                return i;
            }

            return -1;
        }

        public void CancelActiveJobs()
        {
            _operationCts?.Cancel();
            _currentJobCompletion?.TrySetResult(false);
            _currentJobCompletion = null;
            _sequentialBatchActive = false;

            foreach (var execution in _executions.Values)
            {
                execution.Runner?.Dispose(killProcess: true);
                execution.Runner = null;
            }

            UnregisterUpdate();
            MusiderunSessionState.Clear();
            _operationCts?.Dispose();
            _operationCts = null;
            _executions.Clear();
            NotifyJobsChanged();
        }

        internal void TryResumeFromPersistedJobs(MusiderunPersistedJob[] persistedJobs)
        {
            if (persistedJobs == null || persistedJobs.Length == 0)
            {
                return;
            }

            _executions.Clear();
            foreach (var persisted in persistedJobs)
            {
                if (persisted.processId <= 0)
                {
                    continue;
                }

                DateTime.TryParse(persisted.startedAt, out var startedAt);
                var settings = MusiderunSettingsJsonStore.LoadOrDefault();
                var jobIndex = FindJobIndex(settings, persisted.jobId, persisted.batchArguments);
                var runKey = jobIndex >= 0 ? GetRunKey(jobIndex) : persisted.jobId;
                var execution = new BatchJobExecution
                {
                    JobIndex = jobIndex,
                    Definition = new BatchJobDefinitionData
                    {
                        id = persisted.jobId,
                        displayName = persisted.displayName,
                        batchArguments = persisted.batchArguments
                    },
                    State = BatchJobState.Running,
                    MirrorPath = persisted.mirrorPath,
                    LogFilePath = persisted.logFilePath,
                    TestResultsPath = persisted.testResultsPath,
                    StartedAt = startedAt == default ? DateTime.Now : startedAt,
                    Runner = new UnityBatchRunner(message => _log($"[{persisted.jobId}] {message}"))
                };
                execution.Runner.StartMonitoring(persisted.processId, persisted.lastLogPosition);
                _executions[runKey] = execution;
            }

            if (_executions.Count > 0)
            {
                RegisterUpdate();
                NotifyJobsChanged();
            }
        }

        public void EnsureUpdateRegistered()
        {
            if (_executions.Values.Any(execution => execution.State == BatchJobState.Running))
            {
                RegisterUpdate();
            }
        }

        private async Task RunJobsSequentiallyAsync(
            MusiderunSettingsData data,
            IReadOnlyList<int> runnableIndices)
        {
            try
            {
                EnsureGitignoreEntries(data);

                if (GitWorktreeMirrorSync.HasUncommittedChanges(out _))
                {
                    _log("[WARN] 未コミットの変更があります。ミラーはコミット済み (HEAD) のみを対象とするため、" +
                         "これらの変更はビルド/テストに反映されません。");
                }

                foreach (var jobIndex in runnableIndices)
                {
                    _operationCts.Token.ThrowIfCancellationRequested();

                    var job = data.jobs[jobIndex];
                    var execution = _executions[GetRunKey(jobIndex)];
                    if (execution.State == BatchJobState.Queued)
                    {
                        execution.State = BatchJobState.Syncing;
                        NotifyJobsChanged();
                    }

                    if (!await TrySyncSingleJobAsync(data, jobIndex, job).ConfigureAwait(false))
                    {
                        continue;
                    }

                    var completion = new TaskCompletionSource<bool>();
                    _currentJobCompletion = completion;

                    EditorMainThreadDispatcher.Enqueue(() =>
                    {
                        try
                        {
                            if (execution.State != BatchJobState.Syncing)
                            {
                                completion.TrySetResult(false);
                                return;
                            }

                            StartRunnerForJob(data, job, execution);
                            PersistRunningJobs();
                            RegisterUpdate();
                        }
                        catch (Exception ex)
                        {
                            FailJob(execution, ex.Message);
                            completion.TrySetResult(false);
                        }
                    });

                    var runnerStarted = await completion.Task.ConfigureAwait(false);
                    _currentJobCompletion = null;

                    if (!runnerStarted || execution.State != BatchJobState.Running)
                    {
                        continue;
                    }

                    await WaitForJobCompletionAsync().ConfigureAwait(false);
                }

                EditorMainThreadDispatcher.Enqueue(FinishSequentialBatch);
            }
            catch (OperationCanceledException)
            {
                _log("[WARN] バッチジョブがキャンセルされました。");
                EditorMainThreadDispatcher.Enqueue(CancelActiveJobs);
            }
            catch (Exception ex)
            {
                _log($"[ERROR] {ex.Message}");
                EditorMainThreadDispatcher.Enqueue(() =>
                {
                    FailPendingJobs(ex.Message);
                    FinishSequentialBatch();
                });
            }
        }

        private void EnsureGitignoreEntries(MusiderunSettingsData data)
        {
            try
            {
                var result = MusiderunGitignoreGuard.Ensure(data);
                if (!result.Changed)
                {
                    return;
                }

                if (result.Created)
                {
                    _log($"[INFO] .gitignore を作成しました: {result.GitignorePath}");
                }

                if (result.Added.Count > 0)
                {
                    _log($"[INFO] musiderun が依存する {result.Added.Count} 件のエントリを .gitignore に追加しました: " +
                         string.Join(", ", result.Added));
                }
            }
            catch (Exception ex)
            {
                _log($"[WARN] .gitignore の検査・更新に失敗しました（処理は続行します）: {ex.Message}");
            }
        }

        private void FailPendingJobs(string message)
        {
            foreach (var execution in _executions.Values)
            {
                if (execution.State is BatchJobState.Syncing or BatchJobState.Queued)
                {
                    FailJob(execution, message);
                }
            }
        }

        private Task WaitForJobCompletionAsync()
        {
            var completion = new TaskCompletionSource<bool>();
            _currentJobCompletion = completion;
            return completion.Task;
        }

        private async Task<bool> TrySyncSingleJobAsync(
            MusiderunSettingsData data,
            int jobIndex,
            BatchJobDefinitionData job)
        {
            var execution = _executions[GetRunKey(jobIndex)];
            execution.MirrorPath = PlatformUtility.ResolveMirrorPath(data, job.id);
            execution.StartedAt = DateTime.Now;

            var syncLogBuffer = new List<string>();
            void CaptureSyncLog(string line)
            {
                syncLogBuffer.Add(line);
                _log(line);
            }

            var sync = new GitWorktreeMirrorSync(CaptureSyncLog);
            syncLogBuffer.Add($"[{job.id}] === {job.displayName} 開始 ===");

            try
            {
                await sync.SyncJobAsync(data, job, _operationCts.Token).ConfigureAwait(false);
                AssignLogPaths(data, job.id, execution, mirrorWorktreeReady: true);
                FlushSyncLogBuffer(job.id, syncLogBuffer);
                return true;
            }
            catch (OperationCanceledException)
            {
                EditorMainThreadDispatcher.Enqueue(() =>
                {
                    AssignLogPaths(data, job.id, execution, mirrorWorktreeReady: false);
                    FlushSyncLogBuffer(job.id, syncLogBuffer);
                    FailJob(execution, "バッチジョブがキャンセルされました。");
                });
                return false;
            }
            catch (Exception ex)
            {
                EditorMainThreadDispatcher.Enqueue(() =>
                {
                    AssignLogPaths(data, job.id, execution, mirrorWorktreeReady: false);
                    FlushSyncLogBuffer(job.id, syncLogBuffer);
                    FailJob(execution, ex.Message);
                });
                return false;
            }
        }

        private void FlushSyncLogBuffer(string jobId, IReadOnlyList<string> lines)
        {
            foreach (var line in lines)
            {
                _mirrorLogWriter.AppendMirrorLine(jobId, line);
            }
        }

        private void StartRunnerForJob(
            MusiderunSettingsData data,
            BatchJobDefinitionData job,
            BatchJobExecution execution)
        {
            var unityExecutable = PlatformUtility.ResolveUnityExecutable(data);
            if (!PlatformUtility.UnityExecutableExists(unityExecutable))
            {
                throw new FileNotFoundException($"Unity 実行ファイルが見つかりません: {unityExecutable}");
            }

            var runner = new UnityBatchRunner(message => _log($"[{job.id}] {message}"));
            runner.Start(new UnityBatchRunRequest
            {
                JobId = job.id,
                UnityExecutable = unityExecutable,
                ProjectPath = execution.MirrorPath,
                LogFilePath = execution.LogFilePath,
                TestResultsPath = execution.TestResultsPath,
                BatchArguments = job.batchArguments
            });

            execution.Runner = runner;
            execution.State = BatchJobState.Running;
            NotifyJobsChanged();
            _currentJobCompletion?.TrySetResult(true);
        }

        private void PersistRunningJobs()
        {
            var persisted = _executions.Values
                .Where(execution => execution.State == BatchJobState.Running && execution.Runner != null)
                .Select(execution => new MusiderunPersistedJob
                {
                    jobId = execution.Definition.id,
                    displayName = execution.Definition.displayName,
                    processId = execution.Runner.ProcessId,
                    logFilePath = execution.LogFilePath,
                    mirrorPath = execution.MirrorPath,
                    testResultsPath = execution.TestResultsPath,
                    batchArguments = execution.Definition.batchArguments,
                    startedAt = execution.StartedAt.ToString("o"),
                    lastLogPosition = (int)Math.Min(execution.Runner.LastLogPosition, int.MaxValue)
                })
                .ToArray();

            MusiderunSessionState.SaveAll(persisted);
        }

        private void RegisterUpdate()
        {
            if (_updateRegistered)
            {
                return;
            }

            EditorApplication.update += OnEditorUpdate;
            _updateRegistered = true;
        }

        private void UnregisterUpdate()
        {
            if (!_updateRegistered)
            {
                return;
            }

            EditorApplication.update -= OnEditorUpdate;
            _updateRegistered = false;
        }

        private void OnEditorUpdate()
        {
            var anyRunning = false;

            foreach (var execution in _executions.Values.ToArray())
            {
                if (execution.State != BatchJobState.Running || execution.Runner == null)
                {
                    continue;
                }

                anyRunning = true;
                execution.Runner.PollLogFile(execution.LogFilePath);
                MusiderunSessionState.UpdateLastLogPosition(
                    execution.Definition.id,
                    execution.Runner.LastLogPosition);

                if (execution.Runner.IsRunning)
                {
                    continue;
                }

                execution.Runner.PollLogFile(execution.LogFilePath);
                var exitCode = execution.Runner.WaitForExit();
                var batchArguments = execution.Definition.batchArguments;
                var parsedArguments = BatchJobCommandLineParser.Parse(batchArguments);
                var isTests = BatchJobCommandLineParser.ContainsArgument(parsedArguments, "-runTests");
                var isBuild = BatchJobCommandLineParser.ContainsArgument(parsedArguments, "-executeMethod");
                if (BatchJobLogAnalyzer.TryInferExitCode(
                        execution.LogFilePath,
                        batchArguments,
                        out var inferredExitCode))
                {
                    if (exitCode < 0 || (isTests && exitCode == 0 && inferredExitCode != 0))
                    {
                        exitCode = inferredExitCode;
                    }
                    else if (isBuild && inferredExitCode == 0)
                    {
                        // Windows ではビルド成功後のシャットダウン時にメモリリーク検出等で
                        // プロセス終了コードが非ゼロになることがある。ログ上の成功を優先する。
                        exitCode = 0;
                    }
                }

                CompleteJob(execution, exitCode);
                _currentJobCompletion?.TrySetResult(true);
            }

            if (anyRunning)
            {
                PersistRunningJobs();
                return;
            }

            UnregisterUpdate();
            MusiderunSessionState.Clear();

            if (!_sequentialBatchActive)
            {
                CompleteBatch();
            }
        }

        private void FinishSequentialBatch()
        {
            _sequentialBatchActive = false;
            _currentJobCompletion?.TrySetResult(false);
            _currentJobCompletion = null;
            UnregisterUpdate();
            MusiderunSessionState.Clear();
            CompleteBatch();
        }

        private void CompleteJob(BatchJobExecution execution, int exitCode)
        {
            execution.Runner?.Dispose(killProcess: false);
            execution.Runner = null;
            execution.Result = BuildJobResult(execution, exitCode);
            execution.State = execution.Result.FinalState;
            if (execution.Result.FinishedAt.HasValue)
            {
                BatchJobLastRunStore.SetLastFinishedAt(
                    execution.Definition.id,
                    execution.Result.FinishedAt.Value);
            }

            TryGenerateHtmlReport(execution, execution.Result);
            _mirrorLogWriter.Unregister(execution.Definition.id);
            MusiderunSessionState.Remove(execution.Definition.id);
            NotifyJobsChanged();
        }

        private void FailJob(BatchJobExecution execution, string message)
        {
            execution.Runner?.Dispose(killProcess: false);
            execution.Runner = null;
            execution.Result = new BatchJobResult
            {
                JobId = execution.Definition.id,
                DisplayName = execution.Definition.displayName,
                FinalState = BatchJobState.Failed,
                ExitCode = -1,
                MirrorPath = execution.MirrorPath,
                MirrorLogFilePath = execution.MirrorLogFilePath,
                LogFilePath = execution.LogFilePath,
                LogHtmlFilePath = execution.LogHtmlFilePath,
                TestResultsPath = execution.TestResultsPath,
                BatchArguments = execution.Definition.batchArguments,
                ErrorMessage = message,
                StartedAt = execution.StartedAt,
                FinishedAt = DateTime.Now
            };
            BatchJobLastRunStore.SetLastFinishedAt(execution.Definition.id, execution.Result.FinishedAt.Value);
            execution.State = BatchJobState.Failed;
            TryGenerateHtmlReport(execution, execution.Result);
            _mirrorLogWriter.Unregister(execution.Definition.id);
            MusiderunSessionState.Remove(execution.Definition.id);
            NotifyJobsChanged();
            _currentJobCompletion?.TrySetResult(false);
        }

        private BatchJobResult CreateSkippedResult(BatchJobDefinitionData job, string reason)
        {
            return new BatchJobResult
            {
                JobId = job.id,
                DisplayName = job.displayName,
                FinalState = BatchJobState.Skipped,
                SkippedReason = reason,
                BatchArguments = job.batchArguments,
                StartedAt = _batchStartedAt,
                FinishedAt = DateTime.Now
            };
        }

        private BatchJobResult BuildJobResult(BatchJobExecution execution, int exitCode)
        {
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var isBuild = BatchJobCommandLineParser.ContainsArgument(
                BatchJobCommandLineParser.Parse(execution.Definition.batchArguments),
                "-executeMethod");
            var isTests = BatchJobCommandLineParser.ContainsArgument(
                BatchJobCommandLineParser.Parse(execution.Definition.batchArguments),
                "-runTests");

            var buildOutput = isBuild
                ? Path.Combine(execution.MirrorPath, PlatformUtility.GetBuildOutputLocation(buildTarget))
                : string.Empty;

            TestResultSummary testSummary = null;
            var testResultsPath = execution.TestResultsPath;
            if (isTests)
            {
                testResultsPath = TestResultParser.ResolveResultsXmlPath(execution);
                testSummary = TestResultParser.Parse(testResultsPath);
            }

            var failed = IsJobFailed(exitCode, buildOutput, testSummary, execution);
            var errorMessage = string.Empty;

            // -runTests なのに結果 XML が生成されなかった場合、テストが実行されていない
            // 可能性が高い。exitCode が 0 でも「成功」とは見なさず Failed として明示する。
            if (isTests && (testSummary == null || !testSummary.Parsed))
            {
                failed = true;
                errorMessage =
                    "テスト結果 XML が生成されませんでした。テストが実行されていない可能性があります。" +
                    "バッチモードで起動時にドメインリロード（再コンパイル/再インポート）が発生すると" +
                    "テスト実行がスキップされることがあります。ログを確認してください。";
            }
            // 結果 XML はあるがテストが 1 件も実行されていない場合も Failed とする。
            // テストアセンブリ (asmdef) の設定不備などで検出 0 件のまま「成功」と
            // 誤判定されるのを防ぐ。
            else if (isTests && testSummary.Total <= 0)
            {
                failed = true;
                errorMessage =
                    "テストが 1 件も実行されませんでした。テストアセンブリ (asmdef) の設定や" +
                    "テストの検出条件を確認してください。";
            }

            return new BatchJobResult
            {
                JobId = execution.Definition.id,
                DisplayName = execution.Definition.displayName,
                FinalState = failed ? BatchJobState.Failed : BatchJobState.Completed,
                ExitCode = exitCode,
                MirrorPath = execution.MirrorPath,
                MirrorLogFilePath = execution.MirrorLogFilePath,
                LogFilePath = execution.LogFilePath,
                LogHtmlFilePath = execution.LogHtmlFilePath,
                BuildOutputPath = buildOutput,
                TestResultsPath = testResultsPath,
                BatchArguments = execution.Definition.batchArguments,
                TestSummary = testSummary,
                ErrorMessage = errorMessage,
                StartedAt = execution.StartedAt,
                FinishedAt = DateTime.Now
            };
        }

        private void AssignLogPaths(
            MusiderunSettingsData data,
            string jobId,
            BatchJobExecution execution,
            bool mirrorWorktreeReady = true)
        {
            var logDirectory = PlatformUtility.ResolveLogOutputDirectory(
                data,
                execution.MirrorPath,
                mirrorWorktreeReady);
            Directory.CreateDirectory(logDirectory);

            var timestamp = execution.StartedAt.ToString("yyyyMMdd-HHmmss");
            var baseName = $"{jobId}-{timestamp}";
            execution.MirrorLogFilePath = Path.Combine(logDirectory, $"{baseName}-mirror.log");
            execution.LogFilePath = Path.Combine(logDirectory, $"{baseName}.log");
            execution.LogHtmlFilePath = Path.Combine(logDirectory, $"{baseName}.html");

            var resultsFileName = $"{baseName}-results.xml";
            var isTests = BatchJobCommandLineParser.ContainsArgument(
                BatchJobCommandLineParser.Parse(execution.Definition.batchArguments),
                "-runTests");
            if (isTests && !string.IsNullOrWhiteSpace(execution.Definition.artifactFolder))
            {
                var artifactDirectory = PlatformUtility.ResolveArtifactFolder(data, execution.Definition);
                Directory.CreateDirectory(artifactDirectory);
                execution.TestResultsPath = Path.Combine(artifactDirectory, resultsFileName);
            }
            else
            {
                execution.TestResultsPath = Path.Combine(logDirectory, resultsFileName);
            }

            _mirrorLogWriter.Register(execution);
        }

        private void TryGenerateHtmlReport(BatchJobExecution execution, BatchJobResult result)
        {
            if (execution == null || result == null || string.IsNullOrEmpty(execution.LogHtmlFilePath))
            {
                return;
            }

            var request = new BatchJobLogHtmlRequest
            {
                JobId = result.JobId,
                DisplayName = result.DisplayName,
                FinalState = result.FinalState,
                ExitCode = result.ExitCode,
                StartedAt = result.StartedAt,
                FinishedAt = result.FinishedAt,
                MirrorLogFilePath = execution.MirrorLogFilePath,
                UnityLogFilePath = execution.LogFilePath,
                OutputHtmlPath = execution.LogHtmlFilePath,
                TestSummary = result.TestSummary,
                ErrorMessage = result.ErrorMessage
            };

            const int maxAttempts = 5;
            var lastError = string.Empty;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (BatchJobLogHtmlRenderer.TryRender(request, out lastError))
                {
                    return;
                }

                if (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(150);
                }
            }

            _log($"[WARN] [{result.JobId}] HTML ログの生成に失敗: {lastError}");
        }

        private bool IsJobFailed(
            int exitCode,
            string buildOutput,
            TestResultSummary testSummary,
            BatchJobExecution execution)
        {
            if (testSummary is { HasFailures: true })
            {
                return true;
            }

            if (exitCode == 0)
            {
                return false;
            }

            var hasInferredExitCode = BatchJobLogAnalyzer.TryInferExitCode(
                execution.LogFilePath,
                execution.Definition.batchArguments,
                out var inferredExitCode);

            var isBuild = BatchJobCommandLineParser.ContainsArgument(
                BatchJobCommandLineParser.Parse(execution.Definition.batchArguments),
                "-executeMethod");
            if (isBuild &&
                !string.IsNullOrEmpty(buildOutput) &&
                File.Exists(buildOutput) &&
                hasInferredExitCode &&
                inferredExitCode == 0)
            {
                return false;
            }

            if (exitCode > 0)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(buildOutput) && File.Exists(buildOutput))
            {
                return false;
            }

            if (testSummary is { Parsed: true })
            {
                return false;
            }

            if (hasInferredExitCode)
            {
                return inferredExitCode != 0;
            }

            return exitCode < 0;
        }

        private void CompleteBatch()
        {
            var batchResult = new BatchJobBatchResult
            {
                StartedAt = _batchStartedAt,
                FinishedAt = DateTime.Now,
                Results = _executions.Values
                    .Where(execution => execution.Result != null)
                    .Select(execution => execution.Result)
                    .ToList()
            };

            foreach (var result in batchResult.Results)
            {
                switch (result.FinalState)
                {
                    case BatchJobState.Completed:
                        batchResult.CompletedCount++;
                        break;
                    case BatchJobState.Failed:
                        batchResult.FailedCount++;
                        break;
                    case BatchJobState.Skipped:
                        batchResult.SkippedCount++;
                        break;
                }
            }

            LastBatchResult = batchResult;
            _operationCts?.Dispose();
            _operationCts = null;

            EditorMainThreadDispatcher.Enqueue(() =>
            {
                _onBatchCompleted(batchResult);
                _onRepaintRequested();
            });
        }

        private void NotifyJobsChanged()
        {
            EditorMainThreadDispatcher.Enqueue(() =>
            {
                _onJobsChanged();
                _onRepaintRequested();
            });
        }

    }
}
