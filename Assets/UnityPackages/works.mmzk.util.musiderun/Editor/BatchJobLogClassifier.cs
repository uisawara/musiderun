using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal enum BatchJobLogLineSeverity
    {
        Info,
        Warning,
        Error
    }

    internal enum LogLabelCategory
    {
        Severity,
        Section,
        Source
    }

    internal sealed class BatchJobLogLineEntry
    {
        public int LineNumber { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string SectionId { get; set; } = string.Empty;
        public BatchJobLogLineSeverity Severity { get; set; }
        public string AnchorId { get; set; } = string.Empty;
        public HashSet<string> Labels { get; } = new(StringComparer.Ordinal);
    }

    internal sealed class LogLabelDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public LogLabelCategory Category { get; set; }
        public int Count { get; set; }
    }

    internal static class BatchJobLogClassifier
    {
        public const string LabelError = "error";
        public const string LabelWarning = "warning";
        public const string LabelInfo = "info";
        public const string LabelMirror = "mirror";
        public const string LabelUnity = "unity";

        public const string SectionMusiderun = "musiderun";
        public const string SectionCommandLine = "command-line";
        public const string SectionLicensing = "licensing";
        public const string SectionPackageManager = "package-manager";
        public const string SectionScriptCompilation = "script-compilation";
        public const string SectionTestRun = "test-run";
        public const string SectionShutdown = "shutdown";
        public const string SectionOther = "other";

        private static readonly Regex AnsiEscapePattern = new(
            @"\x1B\[[0-9;]*[A-Za-z]|\[[0-9;]*m",
            RegexOptions.Compiled);

        private static readonly Regex BracketColorPattern = new(
            @"\[(?:\d{1,3}m|40m|32m|39m|22m|49m|1m|33m)",
            RegexOptions.Compiled);

        private static readonly (string Id, string DisplayName, LogLabelCategory Category)[] KnownLabels =
        {
            (LabelError, "Error", LogLabelCategory.Severity),
            (LabelWarning, "Warning", LogLabelCategory.Severity),
            (LabelInfo, "Info", LogLabelCategory.Severity),
            (LabelMirror, "Mirror", LogLabelCategory.Source),
            (LabelUnity, "Unity", LogLabelCategory.Source),
            (SectionMusiderun, "musiderun", LogLabelCategory.Section),
            (SectionCommandLine, "Command Line", LogLabelCategory.Section),
            (SectionLicensing, "Licensing", LogLabelCategory.Section),
            (SectionPackageManager, "Package Manager", LogLabelCategory.Section),
            (SectionScriptCompilation, "Script Compilation", LogLabelCategory.Section),
            (SectionTestRun, "Test Run", LogLabelCategory.Section),
            (SectionShutdown, "Shutdown", LogLabelCategory.Section),
            (SectionOther, "Other", LogLabelCategory.Section)
        };

        public static List<BatchJobLogLineEntry> Classify(BatchJobLogHtmlRequest request)
        {
            var entries = new List<BatchJobLogLineEntry>();
            var lineNumber = 1;

            if (!string.IsNullOrEmpty(request.MirrorLogFilePath) && File.Exists(request.MirrorLogFilePath))
            {
                foreach (var rawLine in LogFileReader.ReadAllLines(request.MirrorLogFilePath))
                {
                    var text = NormalizeLine(rawLine);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    entries.Add(CreateMirrorEntry(lineNumber++, text));
                }
            }

            if (!string.IsNullOrEmpty(request.UnityLogFilePath) && File.Exists(request.UnityLogFilePath))
            {
                var unitySection = SectionOther;
                foreach (var rawLine in LogFileReader.ReadAllLines(request.UnityLogFilePath))
                {
                    var text = NormalizeLine(rawLine);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    unitySection = ResolveUnitySection(text, unitySection);
                    entries.Add(CreateUnityEntry(lineNumber++, text, unitySection));
                }
            }

            foreach (var entry in entries)
            {
                ApplyLabels(entry);
            }

            return entries;
        }

        public static IReadOnlyList<LogLabelDefinition> CollectLabels(IEnumerable<BatchJobLogLineEntry> entries)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                foreach (var label in entry.Labels)
                {
                    counts.TryGetValue(label, out var count);
                    counts[label] = count + 1;
                }
            }

            var labels = new List<LogLabelDefinition>();
            foreach (var (id, displayName, category) in KnownLabels)
            {
                counts.TryGetValue(id, out var count);
                labels.Add(new LogLabelDefinition
                {
                    Id = id,
                    DisplayName = displayName,
                    Category = category,
                    Count = count
                });
            }

            return labels;
        }

        private static BatchJobLogLineEntry CreateMirrorEntry(int lineNumber, string text)
        {
            return new BatchJobLogLineEntry
            {
                LineNumber = lineNumber,
                Text = text,
                Source = LabelMirror,
                SectionId = SectionMusiderun,
                AnchorId = $"line-{lineNumber}"
            };
        }

        private static BatchJobLogLineEntry CreateUnityEntry(int lineNumber, string text, string sectionId)
        {
            return new BatchJobLogLineEntry
            {
                LineNumber = lineNumber,
                Text = text,
                Source = LabelUnity,
                SectionId = sectionId,
                AnchorId = $"line-{lineNumber}"
            };
        }

        private static void ApplyLabels(BatchJobLogLineEntry entry)
        {
            entry.Severity = ClassifySeverity(entry.Text, entry.Source);
            entry.Labels.Clear();
            entry.Labels.Add(entry.Source);
            entry.Labels.Add(entry.SectionId);

            switch (entry.Severity)
            {
                case BatchJobLogLineSeverity.Error:
                    entry.Labels.Add(LabelError);
                    break;
                case BatchJobLogLineSeverity.Warning:
                    entry.Labels.Add(LabelWarning);
                    break;
                default:
                    entry.Labels.Add(LabelInfo);
                    break;
            }
        }

        private static string NormalizeLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return string.Empty;
            }

            var normalized = AnsiEscapePattern.Replace(line, string.Empty);
            normalized = BracketColorPattern.Replace(normalized, string.Empty);
            return normalized.TrimEnd('\r');
        }

        private static string ResolveUnitySection(string line, string currentSection)
        {
            if (line.StartsWith("COMMAND LINE ARGUMENTS:", StringComparison.Ordinal))
            {
                return SectionCommandLine;
            }

            if (line.StartsWith("[Licensing::", StringComparison.Ordinal))
            {
                return SectionLicensing;
            }

            if (line.Contains("[Package Manager]", StringComparison.Ordinal) ||
                line.Contains("Application.AssetDatabase", StringComparison.Ordinal) ||
                line.Contains("Asset Pipeline Refresh", StringComparison.Ordinal))
            {
                return SectionPackageManager;
            }

            if (line.Contains("Compiling Scripts", StringComparison.Ordinal) ||
                line.Contains("script compilation time:", StringComparison.Ordinal) ||
                line.Contains("Csc Library/Bee/", StringComparison.Ordinal) ||
                line.Contains("Tundra build", StringComparison.Ordinal) ||
                line.Contains("Unity.ILPP.Runner", StringComparison.Ordinal))
            {
                return SectionScriptCompilation;
            }

            if (line.Contains("Running tests for ExecutionSettings", StringComparison.Ordinal) ||
                line.Contains("Test run completed", StringComparison.Ordinal) ||
                line.Contains("Saving results to:", StringComparison.Ordinal) ||
                line.Contains("UnityEditor.TestTools.TestRunner", StringComparison.Ordinal) ||
                line.Contains("Executing IPrebuildSetup", StringComparison.Ordinal) ||
                line.Contains("Executing IPostBuildCleanup", StringComparison.Ordinal))
            {
                return SectionTestRun;
            }

            if (line.Contains("Batchmode quit", StringComparison.Ordinal) ||
                line.Contains("Exiting batchmode", StringComparison.Ordinal) ||
                line.Contains("Cleanup mono", StringComparison.Ordinal) ||
                line.Contains("Application is shutting down", StringComparison.Ordinal))
            {
                return SectionShutdown;
            }

            return currentSection;
        }

        private static BatchJobLogLineSeverity ClassifySeverity(string line, string source)
        {
            if (IsLicensingNoise(line))
            {
                return BatchJobLogLineSeverity.Info;
            }

            if (ContainsErrorSignal(line))
            {
                return BatchJobLogLineSeverity.Error;
            }

            if (ContainsWarningSignal(line))
            {
                return BatchJobLogLineSeverity.Warning;
            }

            return BatchJobLogLineSeverity.Info;
        }

        private static bool IsLicensingNoise(string line)
        {
            if (!line.Contains("[Licensing::", StringComparison.Ordinal))
            {
                return false;
            }

            return line.Contains("Unsupported protocol version", StringComparison.Ordinal) ||
                   line.Contains("Failed to handshake to channel", StringComparison.Ordinal) ||
                   line.Contains("Access token is unavailable", StringComparison.Ordinal) ||
                   line.Contains("Successfully connected to LicensingClient", StringComparison.Ordinal) ||
                   line.Contains("Licensing is initialized", StringComparison.Ordinal);
        }

        private static bool ContainsErrorSignal(string line)
        {
            if (line.Contains("[ERROR]", StringComparison.Ordinal) ||
                line.Contains("[stderr]", StringComparison.Ordinal) ||
                line.Contains("error CS", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Compilation failed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("No tests were executed", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Build Finished, Result: Failed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (line.Contains("Test run completed. Exiting with code", StringComparison.Ordinal) &&
                !line.Contains("(Ok)", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (line.Contains("Exception", StringComparison.Ordinal) &&
                !line.Contains("EmitExceptionAsError", StringComparison.Ordinal))
            {
                return true;
            }

            if (line.Contains(" Error:", StringComparison.Ordinal) ||
                line.Contains(": Error:", StringComparison.Ordinal))
            {
                return !IsLicensingNoise(line);
            }

            return false;
        }

        private static bool ContainsWarningSignal(string line)
        {
            return line.Contains("[WARN]", StringComparison.Ordinal) ||
                   line.Contains("LogWarning", StringComparison.Ordinal) ||
                   line.Contains(" warn:", StringComparison.OrdinalIgnoreCase) ||
                   line.Contains("Warning", StringComparison.Ordinal);
        }
    }
}
