using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public sealed class TestResultSummary
    {
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public bool Parsed { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ResultsXmlPath { get; set; } = string.Empty;

        public bool HasFailures => Parsed && Failed > 0;
    }

    public static class TestResultParser
    {
        public static string ResolveResultsXmlPath(BatchJobExecution execution)
        {
            if (execution == null)
            {
                return string.Empty;
            }

            var candidates = new List<string>();
            var fileName = Path.GetFileName(execution.TestResultsPath);

            if (!string.IsNullOrEmpty(execution.TestResultsPath))
            {
                candidates.Add(execution.TestResultsPath);
            }

            if (!string.IsNullOrEmpty(fileName))
            {
                if (!string.IsNullOrEmpty(execution.LogFilePath))
                {
                    var logDirectory = Path.GetDirectoryName(execution.LogFilePath);
                    if (!string.IsNullOrEmpty(logDirectory))
                    {
                        candidates.Add(Path.Combine(logDirectory, fileName));
                    }
                }

                if (!string.IsNullOrWhiteSpace(execution.Definition?.artifactFolder))
                {
                    var artifactPath = execution.Definition.artifactFolder.Trim();
                    if (!Path.IsPathRooted(artifactPath))
                    {
                        artifactPath = Path.Combine(execution.MirrorPath, artifactPath);
                    }

                    candidates.Add(Path.Combine(artifactPath, fileName));
                }
            }

            var fromLog = TryParseSavingResultsPath(execution.LogFilePath, execution.MirrorPath);
            if (!string.IsNullOrEmpty(fromLog))
            {
                candidates.Add(fromLog);
            }

            foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return execution.TestResultsPath ?? string.Empty;
        }

        public static TestResultSummary Parse(string resultsXmlPath)
        {
            if (string.IsNullOrWhiteSpace(resultsXmlPath) || !File.Exists(resultsXmlPath))
            {
                return new TestResultSummary
                {
                    Parsed = false,
                    ResultsXmlPath = resultsXmlPath ?? string.Empty,
                    Message = "テスト結果 XML が見つかりません。"
                };
            }

            try
            {
                var document = XDocument.Load(resultsXmlPath);
                var root = document.Root;
                if (root == null)
                {
                    return new TestResultSummary
                    {
                        Parsed = false,
                        ResultsXmlPath = resultsXmlPath,
                        Message = "テスト結果 XML のルート要素がありません。"
                    };
                }

                var total = ReadIntAttribute(root, "total");
                var passed = ReadIntAttribute(root, "passed");
                var failed = ReadFailedCount(root);
                var skipped = ReadIntAttribute(root, "skipped") + ReadIntAttribute(root, "inconclusive");

                return new TestResultSummary
                {
                    Parsed = true,
                    ResultsXmlPath = resultsXmlPath,
                    Total = total,
                    Passed = passed,
                    Failed = failed,
                    Skipped = skipped,
                    Message = $"total={total}, passed={passed}, failed={failed}, skipped={skipped}"
                };
            }
            catch (Exception ex)
            {
                return new TestResultSummary
                {
                    Parsed = false,
                    ResultsXmlPath = resultsXmlPath,
                    Message = $"テスト結果 XML の解析に失敗: {ex.Message}"
                };
            }
        }

        private static int ReadFailedCount(XElement root)
        {
            var failed = ReadIntAttribute(root, "failed");
            if (failed > 0)
            {
                return failed;
            }

            failed = CountFailedTestCases(root);
            if (failed > 0)
            {
                return failed;
            }

            var result = root.Attribute("result")?.Value ?? string.Empty;
            if (result.IndexOf("Failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Math.Max(1, ReadIntAttribute(root, "total") - ReadIntAttribute(root, "passed"));
            }

            return 0;
        }

        private static int CountFailedTestCases(XElement root)
        {
            return root.Descendants()
                .Count(element =>
                    element.Name.LocalName == "test-case" &&
                    string.Equals(element.Attribute("result")?.Value, "Failed", StringComparison.OrdinalIgnoreCase));
        }

        private static string TryParseSavingResultsPath(string logFilePath, string mirrorPath)
        {
            if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
            {
                return string.Empty;
            }

            const string marker = "Saving results to:";
            string fallback = string.Empty;

            try
            {
                foreach (var line in File.ReadLines(logFilePath))
                {
                    var markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
                    if (markerIndex < 0)
                    {
                        continue;
                    }

                    var path = line.Substring(markerIndex + marker.Length).Trim();
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(mirrorPath) &&
                        path.StartsWith(mirrorPath, StringComparison.Ordinal))
                    {
                        return path;
                    }

                    fallback = path;
                }
            }
            catch
            {
                return string.Empty;
            }

            return fallback;
        }

        private static int ReadIntAttribute(XElement element, string attributeName)
        {
            var value = element.Attribute(attributeName)?.Value;
            return int.TryParse(value, out var parsed) ? parsed : 0;
        }
    }
}
