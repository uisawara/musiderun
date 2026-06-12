using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    [InitializeOnLoad]
    public static class MusiderunSession
    {
        private static readonly List<MusiderunWindowBinding> Bindings = new();
        private static BatchJobOrchestrator _orchestrator;

        static MusiderunSession()
        {
            EditorApplication.delayCall += TryResumeFromSessionState;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static BatchJobOrchestrator Orchestrator
        {
            get
            {
                EnsureOrchestrator();
                return _orchestrator;
            }
        }

        public static void Subscribe(MusiderunWindowBinding binding)
        {
            if (binding == null)
            {
                return;
            }

            EnsureOrchestrator();
            Unsubscribe(binding.Window);
            Bindings.Add(binding);
            SyncBinding(binding);
            TailLogFilesForBinding(binding);
        }

        public static void Unsubscribe(EditorWindow window)
        {
            if (window == null)
            {
                return;
            }

            Bindings.RemoveAll(binding => binding.Window == window);
        }

        private static void EnsureOrchestrator()
        {
            if (_orchestrator != null)
            {
                return;
            }

            _orchestrator = new BatchJobOrchestrator(
                BroadcastLog,
                BroadcastJobsChanged,
                BroadcastBatchCompleted,
                RequestRepaint);
        }

        private static void TryResumeFromSessionState()
        {
            EditorMainThreadDispatcher.EnsureRegistered();
            EnsureOrchestrator();

            if (_orchestrator.IsBusy)
            {
                _orchestrator.EnsureUpdateRegistered();
                return;
            }

            if (!MusiderunSessionState.TryLoadAll(out var persistedJobs))
            {
                return;
            }

            _orchestrator.TryResumeFromPersistedJobs(persistedJobs);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (_orchestrator == null)
            {
                return;
            }

            if (state is PlayModeStateChange.EnteredEditMode or PlayModeStateChange.EnteredPlayMode)
            {
                EditorApplication.delayCall += () =>
                {
                    TryResumeFromSessionState();
                    _orchestrator?.EnsureUpdateRegistered();
                };
            }
        }

        private static void BroadcastLog(string message)
        {
            for (var i = Bindings.Count - 1; i >= 0; i--)
            {
                var binding = Bindings[i];
                if (binding.Window == null)
                {
                    Bindings.RemoveAt(i);
                    continue;
                }

                binding.OnLog(message);
            }
        }

        private static void BroadcastJobsChanged()
        {
            for (var i = Bindings.Count - 1; i >= 0; i--)
            {
                var binding = Bindings[i];
                if (binding.Window == null)
                {
                    Bindings.RemoveAt(i);
                    continue;
                }

                binding.OnJobsChanged();
            }
        }

        private static void BroadcastBatchCompleted(BatchJobBatchResult result)
        {
            for (var i = Bindings.Count - 1; i >= 0; i--)
            {
                var binding = Bindings[i];
                if (binding.Window == null)
                {
                    Bindings.RemoveAt(i);
                    continue;
                }

                binding.OnBatchCompleted(result);
            }
        }

        private static void RequestRepaint()
        {
            for (var i = Bindings.Count - 1; i >= 0; i--)
            {
                var binding = Bindings[i];
                if (binding.Window == null)
                {
                    Bindings.RemoveAt(i);
                    continue;
                }

                binding.OnRepaintRequested();
            }
        }

        private static void SyncBinding(MusiderunWindowBinding binding)
        {
            if (_orchestrator.LastBatchResult != null)
            {
                binding.OnBatchCompleted(_orchestrator.LastBatchResult);
            }

            binding.OnJobsChanged();
        }

        private static void TailLogFilesForBinding(MusiderunWindowBinding binding)
        {
            foreach (var execution in _orchestrator.Executions.Values)
            {
                if (string.IsNullOrEmpty(execution.LogFilePath) || !File.Exists(execution.LogFilePath))
                {
                    continue;
                }

                try
                {
                    var lines = File.ReadAllLines(execution.LogFilePath);
                    var start = Math.Max(0, lines.Length - 50);
                    for (var i = start; i < lines.Length; i++)
                    {
                        var line = lines[i].TrimEnd('\r');
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            binding.OnLog(line);
                        }
                    }
                }
                catch (Exception ex)
                {
                    binding.OnLog($"[WARN] ログファイルの読み込みに失敗: {ex.Message}");
                }
            }
        }
    }

    public sealed class MusiderunWindowBinding
    {
        public MusiderunWindowBinding(
            EditorWindow window,
            Action<string> onLog,
            Action onJobsChanged,
            Action<BatchJobBatchResult> onBatchCompleted,
            Action onRepaintRequested)
        {
            Window = window;
            OnLog = onLog ?? (_ => { });
            OnJobsChanged = onJobsChanged ?? (() => { });
            OnBatchCompleted = onBatchCompleted ?? (_ => { });
            OnRepaintRequested = onRepaintRequested ?? (() => { });
        }

        public EditorWindow Window { get; }
        public Action<string> OnLog { get; }
        public Action OnJobsChanged { get; }
        public Action<BatchJobBatchResult> OnBatchCompleted { get; }
        public Action OnRepaintRequested { get; }
    }
}
