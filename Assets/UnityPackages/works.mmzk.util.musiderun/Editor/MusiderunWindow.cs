using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Works.Mmzk.Util.Musiderun;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public sealed class MusiderunWindow : EditorWindow
    {
        private const int MaxLogLines = 50;
        private const string SelectedPrefPrefix = "musiderun.selected.";
        private const string LogFoldoutPrefKey = "musiderun.logFoldout";

        private MusiderunSettingsData _settingsData;
        private MusiderunWindowBinding _binding;
        private BatchJobBatchResult _lastBatchResult;
        private readonly StringBuilder _logBuilder = new();
        private readonly Dictionary<string, bool> _jobSelections = new();
        private Vector2 _jobScroll;
        private Vector2 _logScroll;
        private DateTime _operationStartedAt;
        private bool _autoScroll = true;
        private bool _logFoldoutExpanded;
        private string _settingsLoadError = string.Empty;
        private double _lastElapsedRepaintTime;

        public static void ShowWindow()
        {
            var window = GetWindow<MusiderunWindow>("musiderun");
            window.minSize = new Vector2(320f, 240f);
            window.Show();
        }

        private void OnEnable()
        {
            _logFoldoutExpanded = EditorPrefs.GetBool(LogFoldoutPrefKey, false);
            _lastElapsedRepaintTime = EditorApplication.timeSinceStartup;
            ReloadJsonSettings();
            SubscribeSession();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            if (_binding != null)
            {
                MusiderunSession.Unsubscribe(this);
                _binding = null;
            }
        }

        private void OnEditorUpdate()
        {
            var interval = Orchestrator.IsBusy ? 5.0 : 60.0;
            var now = EditorApplication.timeSinceStartup;
            if (now - _lastElapsedRepaintTime < interval)
            {
                return;
            }

            _lastElapsedRepaintTime = now;
            Repaint();
        }

        private void OnGUI()
        {
            ReloadJsonSettings();

            if (_settingsData == null)
            {
                DrawJsonMissingUI();
                return;
            }

            if (!string.IsNullOrEmpty(_settingsLoadError))
            {
                EditorGUILayout.HelpBox(_settingsLoadError, MessageType.Error);
            }

            DrawStatusBar();
            DrawJobsSection();
            DrawResultSummary();
            DrawLogView();
        }

        private void SubscribeSession()
        {
            _binding = new MusiderunWindowBinding(
                this,
                AppendLog,
                OnJobsChanged,
                result => _lastBatchResult = result,
                Repaint);

            MusiderunSession.Subscribe(_binding);
        }

        private void ReloadJsonSettings()
        {
            _settingsLoadError = string.Empty;

            if (!MusiderunSettingsJsonStore.JsonExists())
            {
                _settingsData = null;
                return;
            }

            if (!MusiderunSettingsJsonStore.TryLoad(out _settingsData, out var error))
            {
                _settingsLoadError = error;
                _settingsData = MusiderunSettingsJsonStore.CreateDefaultData();
                return;
            }

            EnsureJobSelections();
        }

        private void EnsureJobSelections()
        {
            if (_settingsData?.jobs == null)
            {
                return;
            }

            for (var i = 0; i < _settingsData.jobs.Length; i++)
            {
                var job = _settingsData.jobs[i];
                if (job == null || string.IsNullOrEmpty(job.id))
                {
                    continue;
                }

                var selectionKey = GetSelectionKey(i);
                if (!_jobSelections.ContainsKey(selectionKey))
                {
                    _jobSelections[selectionKey] = EditorPrefs.GetBool(SelectedPrefPrefix + selectionKey, true);
                }
            }
        }

        private static string GetSelectionKey(int jobIndex) => jobIndex.ToString();

        private void DrawJsonMissingUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("musiderun", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                $"JSON 設定ファイルが見つかりません。\n{MusiderunSettingsJsonStore.JsonAssetPath}",
                MessageType.Warning);

            if (GUILayout.Button("Create Settings JSON", GUILayout.Height(32f)))
            {
                MusiderunSettingsJsonStore.EnsureJsonExists();
                ReloadJsonSettings();
                SubscribeSession();
                EditorUtility.RevealInFinder(MusiderunSettingsJsonStore.ResolveAbsolutePath());
            }
        }

        private BatchJobOrchestrator Orchestrator => MusiderunSession.Orchestrator;

        private void DrawStatusBar()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Batch", GetBatchStateLabel(), EditorStyles.boldLabel);
                if (Orchestrator.IsBusy)
                {
                    var elapsed = DateTime.Now - _operationStartedAt;
                    EditorGUILayout.LabelField($"Elapsed: {elapsed:mm\\:ss}", GUILayout.Width(120f));
                }
            }
        }

        private string GetBatchStateLabel()
        {
            if (Orchestrator.IsBusy)
            {
                return "▶ Running";
            }

            if (_lastBatchResult != null)
            {
                return _lastBatchResult.FailedCount > 0 ? "❌ Failed" : "✅ Completed";
            }

            return "— Idle";
        }

        private void DrawJobsSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Jobs", EditorStyles.boldLabel);

            if (_settingsData?.jobs == null || _settingsData.jobs.Length == 0)
            {
                EditorGUILayout.HelpBox("JSON に Job 定義がありません。", MessageType.Warning);
                return;
            }

            var isPlaying = EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode;
            if (isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Jobs cannot run during Play mode. Exit Play mode and save your work first.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Orchestrator.IsBusy || isPlaying))
                {
                    if (GUILayout.Button("Run Selected", GUILayout.Height(28f)))
                    {
                        RunSelectedJobs();
                    }

                    if (GUILayout.Button("Select All", GUILayout.Width(90f), GUILayout.Height(28f)))
                    {
                        SetAllSelections(true);
                    }

                    if (GUILayout.Button("Deselect All", GUILayout.Width(90f), GUILayout.Height(28f)))
                    {
                        SetAllSelections(false);
                    }

                    if (GUILayout.Button("Reload JSON", GUILayout.Width(100f), GUILayout.Height(28f)))
                    {
                        ReloadJsonSettings();
                    }

                    if (GUILayout.Button("Open JSON", GUILayout.Width(90f), GUILayout.Height(28f)))
                    {
                        OpenJsonSettings();
                    }

                    if (GUILayout.Button("Check .gitignore", GUILayout.Width(120f), GUILayout.Height(28f)))
                    {
                        MusiderunMenu.CheckGitignoreEntries();
                    }
                }
            }

            _jobScroll = EditorGUILayout.BeginScrollView(_jobScroll, GUILayout.MaxHeight(180f));
            for (var i = 0; i < _settingsData.jobs.Length; i++)
            {
                DrawJobRow(i, _settingsData.jobs[i], isPlaying);
            }

            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear Log", GUILayout.Height(24f), GUILayout.Width(90f)))
                {
                    _logBuilder.Clear();
                    _lastBatchResult = null;
                }
            }
        }

        private void DrawJobRow(int jobIndex, BatchJobDefinitionData job, bool isPlaying)
        {
            if (job == null || string.IsNullOrEmpty(job.id))
            {
                return;
            }

            var osMatches = BatchJobTargetOSUtility.MatchesCurrentOS(job.GetTargetOS());
            var state = Orchestrator.GetJobState(jobIndex);
            if (state == BatchJobState.Idle && !osMatches)
            {
                state = BatchJobState.Skipped;
            }

            var selectionKey = GetSelectionKey(jobIndex);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(Orchestrator.IsBusy || isPlaying || !osMatches))
                    {
                        var selected = _jobSelections.TryGetValue(selectionKey, out var value) && value;
                        var newSelected = EditorGUILayout.Toggle(selected, GUILayout.Width(20f));
                        if (newSelected != selected)
                        {
                            _jobSelections[selectionKey] = newSelected;
                            EditorPrefs.SetBool(SelectedPrefPrefix + selectionKey, newSelected);
                        }
                    }

                    using (new EditorGUI.DisabledScope(Orchestrator.IsBusy || isPlaying || !osMatches))
                    {
                        if (GUILayout.Button("▶", GUILayout.Width(24f)))
                        {
                            RunSingleJob(jobIndex);
                        }
                    }

                    EditorGUILayout.LabelField(job.displayName, EditorStyles.boldLabel, GUILayout.Width(120f));
                    EditorGUILayout.LabelField(job.targetOS, GUILayout.Width(70f));
                    EditorGUILayout.LabelField(GetStateLabel(state), GUILayout.Width(120f));
                    EditorGUILayout.LabelField(GetJobElapsedLabel(jobIndex, job, state), GUILayout.Width(70f));

                    var logPath = ResolveOpenLogPath(jobIndex, job);
                    using (new EditorGUI.DisabledScope(!CanOpenLog(logPath, jobIndex, job)))
                    {
                        if (GUILayout.Button("Open Log", GUILayout.Width(80f)))
                        {
                            TryOpenLogAsHtml(jobIndex, job, logPath);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(job.artifactFolder))
                    {
                        var artifactPath = PlatformUtility.ResolveArtifactFolder(_settingsData, job);
                        using (new EditorGUI.DisabledScope(!Directory.Exists(artifactPath)))
                        {
                            if (GUILayout.Button("Open Artifact", GUILayout.Width(90f)))
                            {
                                EditorUtility.RevealInFinder(artifactPath);
                            }
                        }
                    }
                }

                EditorGUILayout.LabelField("Args", job.batchArguments, EditorStyles.miniLabel);
            }
        }

        private void RunSelectedJobs()
        {
            if (_settingsData?.jobs == null)
            {
                return;
            }

            var jobIndices = new List<int>();
            for (var i = 0; i < _settingsData.jobs.Length; i++)
            {
                var job = _settingsData.jobs[i];
                if (job == null || string.IsNullOrEmpty(job.id))
                {
                    continue;
                }

                var selectionKey = GetSelectionKey(i);
                if (_jobSelections.TryGetValue(selectionKey, out var selected) && selected)
                {
                    jobIndices.Add(i);
                }
            }

            if (!ConfirmUncommittedChanges())
            {
                return;
            }

            BeginOperation();
            Orchestrator.StartJobsAsync(_settingsData, jobIndices);
        }

        private void RunSingleJob(int jobIndex)
        {
            if (_settingsData?.jobs == null ||
                jobIndex < 0 ||
                jobIndex >= _settingsData.jobs.Length)
            {
                return;
            }

            var job = _settingsData.jobs[jobIndex];
            if (job == null || string.IsNullOrEmpty(job.id))
            {
                return;
            }

            if (!ConfirmUncommittedChanges())
            {
                return;
            }

            BeginOperation();
            Orchestrator.StartJobsAsync(_settingsData, new[] { jobIndex });
        }

        /// <summary>
        /// 未コミットの変更がある場合、ミラー（コミット済み HEAD のみ対象）に反映されない旨を
        /// 警告し、続行可否を確認する。変更が無い場合や git 不在時はそのまま true を返す。
        /// </summary>
        private bool ConfirmUncommittedChanges()
        {
            if (!GitWorktreeMirrorSync.HasUncommittedChanges(out var statusSummary))
            {
                return true;
            }

            var preview = BuildUncommittedPreview(statusSummary, maxLines: 15);
            return EditorUtility.DisplayDialog(
                "未コミットの変更があります",
                "ミラーはコミット済み (HEAD) の内容のみを対象とするため、以下の未コミット変更は" +
                "ビルド/テストに反映されません。\n\n" +
                preview +
                "\n\n反映したい場合は commit してから実行してください。このまま実行しますか？",
                "実行する",
                "キャンセル");
        }

        private static string BuildUncommittedPreview(string statusSummary, int maxLines)
        {
            if (string.IsNullOrEmpty(statusSummary))
            {
                return string.Empty;
            }

            var lines = statusSummary.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (lines.Length <= maxLines)
            {
                return string.Join("\n", lines);
            }

            var head = string.Join("\n", lines, 0, maxLines);
            return head + $"\n... 他 {lines.Length - maxLines} 件";
        }

        private void SetAllSelections(bool selected)
        {
            if (_settingsData?.jobs == null)
            {
                return;
            }

            for (var i = 0; i < _settingsData.jobs.Length; i++)
            {
                var job = _settingsData.jobs[i];
                if (string.IsNullOrEmpty(job?.id))
                {
                    continue;
                }

                if (!BatchJobTargetOSUtility.MatchesCurrentOS(job.GetTargetOS()))
                {
                    continue;
                }

                var selectionKey = GetSelectionKey(i);
                _jobSelections[selectionKey] = selected;
                EditorPrefs.SetBool(SelectedPrefPrefix + selectionKey, selected);
            }
        }

        private void BeginOperation()
        {
            _logBuilder.Clear();
            _lastBatchResult = null;
            _operationStartedAt = DateTime.Now;
        }

        private void OnJobsChanged()
        {
            Repaint();
        }

        private void DrawResultSummary()
        {
            if (_lastBatchResult == null)
            {
                return;
            }

            EditorGUILayout.Space(4f);

            var messageType = _lastBatchResult.FailedCount > 0 ? MessageType.Error : MessageType.Info;
            var summary = new StringBuilder();
            summary.AppendLine($"Completed: {_lastBatchResult.CompletedCount}");
            summary.AppendLine($"Failed: {_lastBatchResult.FailedCount}");
            summary.AppendLine($"Skipped: {_lastBatchResult.SkippedCount}");

            if (_lastBatchResult.FinishedAt.HasValue)
            {
                var duration = _lastBatchResult.FinishedAt.Value - _lastBatchResult.StartedAt;
                summary.AppendLine($"Duration: {duration:mm\\:ss}");
            }

            foreach (var result in _lastBatchResult.Results)
            {
                if (result.FinalState == BatchJobState.Failed)
                {
                    summary.AppendLine($"[FAILED] {result.DisplayName}: {result.ErrorMessage}");
                }
                else if (result.FinalState == BatchJobState.Skipped)
                {
                    summary.AppendLine($"[SKIPPED] {result.DisplayName}: {result.SkippedReason}");
                }
            }

            var summaryText = summary.ToString().TrimEnd();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy", GUILayout.Width(60f)))
                {
                    EditorGUIUtility.systemCopyBuffer = summaryText;
                    ShowNotification(new GUIContent("Last Result をコピーしました"));
                }
            }

            // 状態が一目で分かるよう色付きアイコンは HelpBox で残しつつ、
            // 本文は範囲選択＆コピーできるよう SelectableLabel で表示する。
            var icon = messageType == MessageType.Error
                ? EditorGUIUtility.IconContent("console.erroricon")
                : EditorGUIUtility.IconContent("console.infoicon");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(icon.image, GUILayout.Width(28f), GUILayout.Height(28f));

                var selectableStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true,
                    richText = false
                };
                var width = Mathf.Max(50f, EditorGUIUtility.currentViewWidth - 60f);
                var height = selectableStyle.CalcHeight(new GUIContent(summaryText), width);
                EditorGUILayout.SelectableLabel(
                    summaryText,
                    selectableStyle,
                    GUILayout.ExpandWidth(true),
                    GUILayout.Height(height));
            }
        }

        private void DrawLogView()
        {
            EditorGUILayout.Space(4f);
            var foldoutExpanded = EditorGUILayout.BeginFoldoutHeaderGroup(_logFoldoutExpanded, "Log");
            if (foldoutExpanded != _logFoldoutExpanded)
            {
                _logFoldoutExpanded = foldoutExpanded;
                EditorPrefs.SetBool(LogFoldoutPrefKey, foldoutExpanded);
            }

            if (_logFoldoutExpanded)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _autoScroll = EditorGUILayout.Toggle("Auto Scroll", _autoScroll);
                    GUILayout.FlexibleSpace();

                    var logFilePath = GetActiveLogFilePath();
                    using (new EditorGUI.DisabledScope(!CanOpenLog(logFilePath, -1, null)))
                    {
                        if (GUILayout.Button("Open Log File", GUILayout.Width(110f)))
                        {
                            TryOpenLogAsHtml(-1, null, logFilePath);
                        }
                    }
                }

                _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.ExpandHeight(true));
                EditorGUILayout.TextArea(_logBuilder.ToString(), GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();

                if (_autoScroll && Event.current.type == EventType.Repaint)
                {
                    _logScroll.y = float.MaxValue;
                }
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void AppendLog(string message)
        {
            _logBuilder.AppendLine(message);
            TrimLogToMaxLines();
            Repaint();
        }

        private void TrimLogToMaxLines()
        {
            var text = _logBuilder.ToString();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var lines = text.Split('\n');
            if (lines.Length <= MaxLogLines)
            {
                return;
            }

            _logBuilder.Clear();
            var start = lines.Length - MaxLogLines;
            for (var i = start; i < lines.Length; i++)
            {
                if (i > start)
                {
                    _logBuilder.AppendLine();
                }

                _logBuilder.Append(lines[i].TrimEnd('\r'));
            }
        }

        private string ResolveOpenLogPath(int jobIndex, BatchJobDefinitionData job)
        {
            var runKey = GetSelectionKey(jobIndex);
            if (Orchestrator.Executions.TryGetValue(runKey, out var execution))
            {
                if (!string.IsNullOrEmpty(execution.LogHtmlFilePath) && File.Exists(execution.LogHtmlFilePath))
                {
                    return execution.LogHtmlFilePath;
                }

                if (!string.IsNullOrEmpty(execution.LogFilePath) && File.Exists(execution.LogFilePath))
                {
                    return execution.LogFilePath;
                }

                if (!string.IsNullOrEmpty(execution.MirrorLogFilePath) && File.Exists(execution.MirrorLogFilePath))
                {
                    return execution.MirrorLogFilePath;
                }

                if (execution.Result != null)
                {
                    if (!string.IsNullOrEmpty(execution.Result.LogHtmlFilePath) &&
                        File.Exists(execution.Result.LogHtmlFilePath))
                    {
                        return execution.Result.LogHtmlFilePath;
                    }

                    if (!string.IsNullOrEmpty(execution.Result.LogFilePath) &&
                        File.Exists(execution.Result.LogFilePath))
                    {
                        return execution.Result.LogFilePath;
                    }

                    if (!string.IsNullOrEmpty(execution.Result.MirrorLogFilePath) &&
                        File.Exists(execution.Result.MirrorLogFilePath))
                    {
                        return execution.Result.MirrorLogFilePath;
                    }
                }
            }

            if (_lastBatchResult?.Results == null || job == null)
            {
                return string.Empty;
            }

            foreach (var result in _lastBatchResult.Results)
            {
                if (!string.Equals(result.JobId, job.id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(result.LogHtmlFilePath) && File.Exists(result.LogHtmlFilePath))
                {
                    return result.LogHtmlFilePath;
                }

                if (!string.IsNullOrEmpty(result.LogFilePath) && File.Exists(result.LogFilePath))
                {
                    return result.LogFilePath;
                }

                if (!string.IsNullOrEmpty(result.MirrorLogFilePath) && File.Exists(result.MirrorLogFilePath))
                {
                    return result.MirrorLogFilePath;
                }
            }

            return string.Empty;
        }

        private string ResolveMirrorLogPath(int jobIndex, BatchJobDefinitionData job)
        {
            var runKey = GetSelectionKey(jobIndex);
            if (Orchestrator.Executions.TryGetValue(runKey, out var execution))
            {
                if (!string.IsNullOrEmpty(execution.MirrorLogFilePath) && File.Exists(execution.MirrorLogFilePath))
                {
                    return execution.MirrorLogFilePath;
                }

                if (execution.Result != null &&
                    !string.IsNullOrEmpty(execution.Result.MirrorLogFilePath) &&
                    File.Exists(execution.Result.MirrorLogFilePath))
                {
                    return execution.Result.MirrorLogFilePath;
                }
            }

            if (_lastBatchResult?.Results == null || job == null)
            {
                return string.Empty;
            }

            foreach (var result in _lastBatchResult.Results)
            {
                if (!string.Equals(result.JobId, job.id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(result.MirrorLogFilePath) && File.Exists(result.MirrorLogFilePath))
                {
                    return result.MirrorLogFilePath;
                }
            }

            return string.Empty;
        }

        private string GetActiveLogFilePath()
        {
            if (_lastBatchResult?.Results != null)
            {
                for (var i = _lastBatchResult.Results.Count - 1; i >= 0; i--)
                {
                    var path = _lastBatchResult.Results[i].LogFilePath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        return path;
                    }
                }
            }

            foreach (var execution in Orchestrator.Executions.Values)
            {
                if (!string.IsNullOrEmpty(execution.LogFilePath))
                {
                    return execution.LogFilePath;
                }
            }

            return string.Empty;
        }

        private bool CanOpenLog(string path, int jobIndex, BatchJobDefinitionData job)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return true;
            }

            var unityLogPath = ResolveUnityLogPath(path, jobIndex, job);
            if (!string.IsNullOrEmpty(unityLogPath) && File.Exists(unityLogPath))
            {
                return true;
            }

            var mirrorLogPath = ResolveMirrorLogPath(jobIndex, job);
            return !string.IsNullOrEmpty(mirrorLogPath) && File.Exists(mirrorLogPath);
        }

        private void TryOpenLogAsHtml(int jobIndex, BatchJobDefinitionData job, string path)
        {
            try
            {
                var context = ResolveLogContext(jobIndex, job, path);
                var hasUnityLog = !string.IsNullOrEmpty(context.UnityLogFilePath) &&
                    File.Exists(context.UnityLogFilePath);
                var hasMirrorLog = !string.IsNullOrEmpty(context.Request.MirrorLogFilePath) &&
                    File.Exists(context.Request.MirrorLogFilePath);

                if (!hasUnityLog && !hasMirrorLog)
                {
                    throw new FileNotFoundException("ログファイルが見つかりません。");
                }

                var shouldRegenerate = context.IsRunning ||
                    string.IsNullOrEmpty(context.HtmlFilePath) ||
                    !File.Exists(context.HtmlFilePath);

                if (shouldRegenerate)
                {
                    if (!BatchJobLogHtmlRenderer.TryRender(context.Request, out var error))
                    {
                        throw new InvalidOperationException($"HTML ログの生成に失敗: {error}");
                    }
                }

                PlatformUtility.OpenPathWithDefaultApplication(context.HtmlFilePath);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Open Log", ex.Message, "OK");
            }
        }

        private LogOpenContext ResolveLogContext(int jobIndex, BatchJobDefinitionData job, string path)
        {
            BatchJobExecution execution = null;
            BatchJobResult result = null;
            var isRunning = false;

            if (jobIndex >= 0)
            {
                Orchestrator.Executions.TryGetValue(GetSelectionKey(jobIndex), out execution);
                result = execution?.Result;
                isRunning = execution?.State == BatchJobState.Running;
            }

            if (result == null && job != null && _lastBatchResult?.Results != null)
            {
                foreach (var batchResult in _lastBatchResult.Results)
                {
                    if (string.Equals(batchResult.JobId, job.id, StringComparison.Ordinal))
                    {
                        result = batchResult;
                        break;
                    }
                }
            }

            var unityLogPath = ResolveUnityLogPath(path, jobIndex, job, execution, result);
            var mirrorLogPath = execution?.MirrorLogFilePath ?? result?.MirrorLogFilePath;
            if (string.IsNullOrEmpty(mirrorLogPath) && !string.IsNullOrEmpty(unityLogPath))
            {
                mirrorLogPath = unityLogPath.Replace(".log", "-mirror.log");
            }

            var htmlPath = execution?.LogHtmlFilePath ?? result?.LogHtmlFilePath;
            if (string.IsNullOrEmpty(htmlPath) && !string.IsNullOrEmpty(unityLogPath))
            {
                htmlPath = Path.ChangeExtension(unityLogPath, ".html");
            }

            if (path != null && path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                htmlPath = path;
            }

            var request = new BatchJobLogHtmlRequest
            {
                JobId = job?.id ?? result?.JobId ?? "unknown",
                DisplayName = job?.displayName ?? result?.DisplayName ?? "Batch Job",
                FinalState = result?.FinalState ?? execution?.State ?? BatchJobState.Running,
                ExitCode = result?.ExitCode ?? -1,
                StartedAt = result?.StartedAt ?? execution?.StartedAt ?? DateTime.Now,
                FinishedAt = result?.FinishedAt,
                MirrorLogFilePath = mirrorLogPath,
                UnityLogFilePath = unityLogPath,
                OutputHtmlPath = htmlPath,
                TestSummary = result?.TestSummary,
                ErrorMessage = result?.ErrorMessage ?? string.Empty
            };

            return new LogOpenContext
            {
                UnityLogFilePath = unityLogPath,
                HtmlFilePath = htmlPath,
                Request = request,
                IsRunning = isRunning
            };
        }

        private static string ResolveUnityLogPath(
            string path,
            int jobIndex,
            BatchJobDefinitionData job,
            BatchJobExecution execution = null,
            BatchJobResult result = null)
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (path.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }

                if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.ChangeExtension(path, ".log");
                }
            }

            if (!string.IsNullOrEmpty(execution?.LogFilePath))
            {
                return execution.LogFilePath;
            }

            if (!string.IsNullOrEmpty(result?.LogFilePath))
            {
                return result.LogFilePath;
            }

            return string.Empty;
        }

        private sealed class LogOpenContext
        {
            public string UnityLogFilePath { get; set; } = string.Empty;
            public string HtmlFilePath { get; set; } = string.Empty;
            public BatchJobLogHtmlRequest Request { get; set; }
            public bool IsRunning { get; set; }
        }

        private void OpenJsonSettings()
        {
            if (!MusiderunSettingsJsonStore.JsonExists())
            {
                MusiderunSettingsJsonStore.EnsureJsonExists();
            }

            try
            {
                PlatformUtility.OpenPathWithDefaultApplication(MusiderunSettingsJsonStore.ResolveAbsolutePath());
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Open JSON", ex.Message, "OK");
            }
        }

        private static string GetStateLabel(BatchJobState state)
        {
            return state switch
            {
                BatchJobState.Idle => "— Idle",
                BatchJobState.Queued => "⏳ Queued",
                BatchJobState.MirrorCreating => "🔧 Creating",
                BatchJobState.MirrorDestroying => "🗑 Destroying",
                BatchJobState.Syncing => "🔄 Syncing",
                BatchJobState.Running => "▶ Running",
                BatchJobState.Completed => "✅ Completed",
                BatchJobState.Failed => "❌ Failed",
                BatchJobState.Skipped => "⏭ Skipped",
                _ => state.ToString()
            };
        }

        private static bool IsActiveJobState(BatchJobState state)
        {
            return state is BatchJobState.Queued or BatchJobState.Syncing or BatchJobState.Running;
        }

        private string GetJobElapsedLabel(int jobIndex, BatchJobDefinitionData job, BatchJobState state)
        {
            if (IsActiveJobState(state) &&
                Orchestrator.TryGetJobStartedAt(jobIndex, out var startedAt))
            {
                return BatchJobLastRunStore.FormatRoughElapsed(DateTime.Now - startedAt);
            }

            if (BatchJobLastRunStore.TryGetLastFinishedAt(job.id, out var finishedAt))
            {
                return BatchJobLastRunStore.FormatRoughElapsed(DateTime.Now - finishedAt);
            }

            return "-";
        }
    }
}
