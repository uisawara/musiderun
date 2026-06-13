using System;
using System.Collections.Generic;
using Works.Mmzk.Util.Musiderun;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public sealed class BatchJobResult
    {
        public string JobId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public BatchJobState FinalState { get; set; }
        public int ExitCode { get; set; } = -1;
        public string MirrorPath { get; set; } = string.Empty;
        public string MirrorLogFilePath { get; set; } = string.Empty;
        public string LogFilePath { get; set; } = string.Empty;
        public string LogHtmlFilePath { get; set; } = string.Empty;
        public string BuildOutputPath { get; set; } = string.Empty;
        public string TestResultsPath { get; set; } = string.Empty;
        public string TestResultsHtmlFilePath { get; set; } = string.Empty;
        public string BatchArguments { get; set; } = string.Empty;
        public TestResultSummary TestSummary { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string SkippedReason { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }

        public TimeSpan? Duration =>
            FinishedAt.HasValue ? FinishedAt.Value - StartedAt : null;
    }

    public sealed class BatchJobBatchResult
    {
        public List<BatchJobResult> Results { get; set; } = new();
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }

        public int CompletedCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
    }
}
