using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public sealed class TestCaseEntry
    {
        public string FullName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public double Duration { get; set; }
        public string FailureMessage { get; set; } = string.Empty;
        public string StackTrace { get; set; } = string.Empty;
    }

    public sealed class TestSuiteNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public List<TestSuiteNode> Suites { get; } = new();
        public List<TestCaseEntry> Cases { get; } = new();
    }

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

        public static IReadOnlyList<TestCaseEntry> ParseTestCases(string resultsXmlPath)
        {
            var root = ParseTestTree(resultsXmlPath);
            if (root == null)
            {
                return Array.Empty<TestCaseEntry>();
            }

            return CollectTestCases(root);
        }

        public static TestSuiteNode ParseTestTree(string resultsXmlPath)
        {
            if (string.IsNullOrWhiteSpace(resultsXmlPath) || !File.Exists(resultsXmlPath))
            {
                return null;
            }

            try
            {
                var document = XDocument.Load(resultsXmlPath);
                var root = document.Root;
                if (root == null)
                {
                    return null;
                }

                return ParseSuiteElement(root);
            }
            catch
            {
                return null;
            }
        }

        public static IReadOnlyList<TestCaseEntry> CollectTestCases(TestSuiteNode node)
        {
            if (node == null)
            {
                return Array.Empty<TestCaseEntry>();
            }

            var cases = new List<TestCaseEntry>(node.Cases);
            foreach (var child in node.Suites)
            {
                cases.AddRange(CollectTestCases(child));
            }

            return cases;
        }

        private static TestSuiteNode ParseSuiteElement(XElement element)
        {
            var node = new TestSuiteNode
            {
                Name = element.Attribute("name")?.Value ?? element.Name.LocalName,
                FullName = element.Attribute("fullname")?.Value ?? string.Empty,
                Type = element.Attribute("type")?.Value ?? string.Empty,
                Result = element.Attribute("result")?.Value ?? string.Empty,
                Total = ReadIntAttribute(element, "total"),
                Passed = ReadIntAttribute(element, "passed"),
                Failed = ReadFailedCount(element),
                Skipped = ReadIntAttribute(element, "skipped") + ReadIntAttribute(element, "inconclusive")
            };

            foreach (var child in element.Elements())
            {
                if (child.Name.LocalName == "test-suite")
                {
                    node.Suites.Add(ParseSuiteElement(child));
                }
                else if (child.Name.LocalName == "test-case")
                {
                    node.Cases.Add(ParseTestCase(child));
                }
            }

            return node;
        }

        private static TestCaseEntry ParseTestCase(XElement element)
        {
            var failure = element.Elements()
                .FirstOrDefault(child => child.Name.LocalName == "failure");

            return new TestCaseEntry
            {
                FullName = element.Attribute("fullname")?.Value ?? string.Empty,
                Name = element.Attribute("name")?.Value ?? string.Empty,
                ClassName = element.Attribute("classname")?.Value ?? string.Empty,
                Result = element.Attribute("result")?.Value ?? string.Empty,
                Duration = ReadDoubleAttribute(element, "duration"),
                FailureMessage = ReadChildElementText(failure, "message"),
                StackTrace = ReadChildElementText(failure, "stack-trace")
            };
        }

        private static string ReadChildElementText(XElement parent, string localName)
        {
            if (parent == null)
            {
                return string.Empty;
            }

            return parent.Elements()
                .FirstOrDefault(element => element.Name.LocalName == localName)
                ?.Value ?? string.Empty;
        }

        private static double ReadDoubleAttribute(XElement element, string attributeName)
        {
            var value = element.Attribute(attributeName)?.Value;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0d;
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
