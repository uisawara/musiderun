using System;
using UnityEditor;
using UnityEngine;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    [Serializable]
    internal sealed class MusiderunPersistedJob
    {
        public string jobId = string.Empty;
        public string displayName = string.Empty;
        public int processId;
        public string logFilePath = string.Empty;
        public string mirrorPath = string.Empty;
        public string testResultsPath = string.Empty;
        public string batchArguments = string.Empty;
        public string startedAt = string.Empty;
        public int lastLogPosition;
    }

    [Serializable]
    internal sealed class MusiderunPersistedJobsData
    {
        public MusiderunPersistedJob[] jobs = Array.Empty<MusiderunPersistedJob>();
    }

    internal static class MusiderunSessionState
    {
        private const string ActiveJobsKey = "musiderun.activeJobs";

        public static void SaveAll(MusiderunPersistedJob[] jobs)
        {
            EditorMainThreadDispatcher.Enqueue(() => SaveAllOnMainThread(jobs));
        }

        public static void UpdateLastLogPosition(string jobId, long position)
        {
            EditorMainThreadDispatcher.Enqueue(() => UpdateLastLogPositionOnMainThread(jobId, position));
        }

        public static bool TryLoadAll(out MusiderunPersistedJob[] jobs)
        {
            return TryLoadAllOnMainThread(out jobs);
        }

        public static void Remove(string jobId)
        {
            EditorMainThreadDispatcher.Enqueue(() => RemoveOnMainThread(jobId));
        }

        public static void Clear()
        {
            EditorMainThreadDispatcher.Enqueue(ClearOnMainThread);
        }

        private static void SaveAllOnMainThread(MusiderunPersistedJob[] jobs)
        {
            if (jobs == null || jobs.Length == 0)
            {
                ClearOnMainThread();
                return;
            }

            var wrapper = new MusiderunPersistedJobsData { jobs = jobs };
            SessionState.SetString(ActiveJobsKey, JsonUtility.ToJson(wrapper));
        }

        private static void UpdateLastLogPositionOnMainThread(string jobId, long position)
        {
            if (!TryLoadAllOnMainThread(out var jobs))
            {
                return;
            }

            for (var i = 0; i < jobs.Length; i++)
            {
                if (!string.Equals(jobs[i].jobId, jobId, StringComparison.Ordinal))
                {
                    continue;
                }

                jobs[i].lastLogPosition = (int)Math.Min(position, int.MaxValue);
                SaveAllOnMainThread(jobs);
                return;
            }
        }

        private static bool TryLoadAllOnMainThread(out MusiderunPersistedJob[] jobs)
        {
            jobs = Array.Empty<MusiderunPersistedJob>();
            var json = SessionState.GetString(ActiveJobsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            var wrapper = JsonUtility.FromJson<MusiderunPersistedJobsData>(json);
            if (wrapper?.jobs == null || wrapper.jobs.Length == 0)
            {
                ClearOnMainThread();
                return false;
            }

            jobs = wrapper.jobs;
            return true;
        }

        private static void RemoveOnMainThread(string jobId)
        {
            if (!TryLoadAllOnMainThread(out var jobs))
            {
                return;
            }

            var remaining = new System.Collections.Generic.List<MusiderunPersistedJob>();
            foreach (var job in jobs)
            {
                if (!string.Equals(job.jobId, jobId, StringComparison.Ordinal))
                {
                    remaining.Add(job);
                }
            }

            SaveAllOnMainThread(remaining.ToArray());
        }

        private static void ClearOnMainThread()
        {
            SessionState.EraseString(ActiveJobsKey);
        }
    }
}
