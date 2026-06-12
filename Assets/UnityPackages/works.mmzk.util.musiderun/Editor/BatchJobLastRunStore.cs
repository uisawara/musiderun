using System;
using UnityEditor;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal static class BatchJobLastRunStore
    {
        private const string PrefPrefix = "musiderun.lastFinished.";

        public static void SetLastFinishedAt(string jobId, DateTime finishedAt)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                return;
            }

            EditorPrefs.SetString(PrefPrefix + jobId, finishedAt.ToString("o"));
        }

        public static bool TryGetLastFinishedAt(string jobId, out DateTime finishedAt)
        {
            finishedAt = default;
            if (string.IsNullOrEmpty(jobId))
            {
                return false;
            }

            var value = EditorPrefs.GetString(PrefPrefix + jobId, string.Empty);
            return !string.IsNullOrEmpty(value) && DateTime.TryParse(value, out finishedAt);
        }

        public static string FormatRoughElapsed(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            if (elapsed < TimeSpan.FromMinutes(1))
            {
                return "just now";
            }

            if (elapsed < TimeSpan.FromHours(1))
            {
                return $"{(int)elapsed.TotalMinutes}m ago";
            }

            if (elapsed < TimeSpan.FromDays(1))
            {
                return $"{(int)elapsed.TotalHours}h ago";
            }

            if (elapsed < TimeSpan.FromDays(7))
            {
                return $"{(int)elapsed.TotalDays}d ago";
            }

            return $"{(int)(elapsed.TotalDays / 7)}w ago";
        }
    }
}
