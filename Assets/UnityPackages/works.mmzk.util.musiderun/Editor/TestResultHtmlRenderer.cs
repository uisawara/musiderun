using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Works.Mmzk.Util.Musiderun;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal sealed class TestResultHtmlRequest
    {
        public string JobId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public BatchJobState FinalState { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string ResultsXmlPath { get; set; } = string.Empty;
        public string OutputHtmlPath { get; set; } = string.Empty;
        public TestResultSummary TestSummary { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    internal static class TestResultHtmlRenderer
    {
        private static readonly string[] ResultFilterIds =
        {
            "passed",
            "failed",
            "skipped"
        };

        public static bool TryRender(TestResultHtmlRequest request, out string error)
        {
            error = string.Empty;
            if (request == null)
            {
                error = "request is null";
                return false;
            }

            if (string.IsNullOrEmpty(request.OutputHtmlPath))
            {
                error = "OutputHtmlPath is empty";
                return false;
            }

            if (string.IsNullOrEmpty(request.ResultsXmlPath) || !File.Exists(request.ResultsXmlPath))
            {
                error = "ResultsXmlPath not found";
                return false;
            }

            try
            {
                var root = TestResultParser.ParseTestTree(request.ResultsXmlPath);
                var testCases = TestResultParser.CollectTestCases(root);
                var summary = request.TestSummary ?? TestResultParser.Parse(request.ResultsXmlPath);
                var html = BuildHtml(request, summary, root, testCases);
                var directory = Path.GetDirectoryName(request.OutputHtmlPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(request.OutputHtmlPath, html, Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string BuildHtml(
            TestResultHtmlRequest request,
            TestResultSummary summary,
            TestSuiteNode root,
            IReadOnlyList<TestCaseEntry> testCases)
        {
            var builder = new StringBuilder();
            var duration = request.FinishedAt.HasValue
                ? request.FinishedAt.Value - request.StartedAt
                : (TimeSpan?)null;

            builder.AppendLine("<!DOCTYPE html>");
            builder.AppendLine("<html lang=\"ja\">");
            builder.AppendLine("<head>");
            builder.AppendLine("<meta charset=\"utf-8\">");
            builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            builder.AppendLine("<meta name=\"color-scheme\" content=\"dark\">");
            builder.AppendLine($"<title>{Escape(request.DisplayName)} — musiderun tests</title>");
            builder.AppendLine("<style>");
            builder.AppendLine(BatchJobHtmlStyles.GetCommonStyles());
            builder.AppendLine(BatchJobHtmlStyles.GetTestResultStyles());
            builder.AppendLine("</style>");
            builder.AppendLine("</head>");
            builder.AppendLine("<body>");

            builder.AppendLine("<header class=\"app-bar\">");
            builder.AppendLine("<div class=\"app-bar__inner\">");
            builder.AppendLine($"<h1 class=\"app-bar__title\">{Escape(request.DisplayName)}</h1>");
            builder.AppendLine(
                $"<span class=\"chip chip-state chip-state-{request.FinalState.ToString().ToLowerInvariant()}\">" +
                $"{Escape(request.FinalState.ToString())}</span>");
            builder.AppendLine("</div>");
            builder.AppendLine("</header>");

            builder.AppendLine("<section class=\"card meta-card\">");
            builder.AppendLine("<dl class=\"meta\">");
            builder.AppendLine($"<dt>Job ID</dt><dd><code>{Escape(request.JobId)}</code></dd>");
            if (summary is { Parsed: true })
            {
                builder.AppendLine($"<dt>Total</dt><dd>{summary.Total}</dd>");
                builder.AppendLine($"<dt>Passed</dt><dd>{summary.Passed}</dd>");
                builder.AppendLine($"<dt>Failed</dt><dd>{summary.Failed}</dd>");
                builder.AppendLine($"<dt>Skipped</dt><dd>{summary.Skipped}</dd>");
            }

            if (duration.HasValue)
            {
                builder.AppendLine($"<dt>Duration</dt><dd>{duration.Value:mm\\:ss}</dd>");
            }

            builder.AppendLine($"<dt>Started</dt><dd>{Escape(request.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"))}</dd>");
            if (!string.IsNullOrEmpty(request.ErrorMessage))
            {
                builder.AppendLine($"<dt>Error</dt><dd class=\"error-text\">{Escape(request.ErrorMessage)}</dd>");
            }

            builder.AppendLine("</dl>");
            builder.AppendLine("<nav class=\"file-links\">");
            builder.AppendLine(
                $"<a class=\"md-button md-button--outlined\" href=\"{EscapeAttribute(ToFileUri(request.ResultsXmlPath))}\">" +
                "results.xml</a>");
            builder.AppendLine("</nav>");
            builder.AppendLine("</section>");

            AppendResultFilter(builder, testCases);

            builder.AppendLine("<main>");
            builder.AppendLine("<section class=\"card test-panel\">");
            builder.AppendLine(
                $"<div class=\"test-panel__header\">" +
                $"<span class=\"test-panel__title\">Test Cases</span>" +
                $"<span class=\"chip chip-muted\" id=\"visible-count\">{testCases.Count} tests</span>" +
                $"</div>");
            builder.AppendLine("<div class=\"test-list\" id=\"test-list\">");
            if (root != null)
            {
                AppendTestTree(builder, root, depth: 0, renderRoot: true);
            }

            builder.AppendLine("</div>");
            builder.AppendLine("</section>");
            builder.AppendLine("</main>");

            builder.AppendLine("<script>");
            builder.AppendLine(GetFilterScript());
            builder.AppendLine("</script>");
            builder.AppendLine("</body>");
            builder.AppendLine("</html>");
            return builder.ToString();
        }

        private static void AppendTestTree(StringBuilder builder, TestSuiteNode node, int depth, bool renderRoot)
        {
            if (node == null)
            {
                return;
            }

            if (renderRoot && IsImplicitRoot(node))
            {
                foreach (var childSuite in node.Suites)
                {
                    AppendTestSuite(builder, childSuite, depth);
                }

                foreach (var testCase in node.Cases)
                {
                    AppendTestCase(builder, testCase);
                }

                return;
            }

            if (renderRoot && ShouldRenderAsSuiteContainer(node))
            {
                AppendTestSuite(builder, node, depth);
                return;
            }

            foreach (var childSuite in node.Suites)
            {
                AppendTestSuite(builder, childSuite, depth);
            }

            foreach (var testCase in node.Cases)
            {
                AppendTestCase(builder, testCase);
            }
        }

        private static bool IsImplicitRoot(TestSuiteNode node)
        {
            return string.Equals(node.Name, "test-run", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldRenderAsSuiteContainer(TestSuiteNode node)
        {
            return node.Suites.Count > 0 || node.Cases.Count > 0;
        }

        private static void AppendTestSuite(StringBuilder builder, TestSuiteNode suite, int depth)
        {
            if (!ShouldRenderAsSuiteContainer(suite))
            {
                return;
            }

            var displayName = GetSuiteDisplayName(suite);
            var summaryText = BuildSuiteSummaryText(suite);
            var openAttribute = ShouldOpenSuiteByDefault(suite, depth) ? " open" : string.Empty;
            var resultChip = string.IsNullOrEmpty(suite.Result)
                ? string.Empty
                : $"<span class=\"chip {GetResultChipClass(suite.Result)}\">{Escape(suite.Result)}</span>";

            builder.AppendLine(
                $"<details class=\"test-suite\" data-depth=\"{depth}\"{openAttribute}>");
            builder.AppendLine("<summary class=\"test-suite__summary\">");
            builder.AppendLine("<span class=\"test-suite__toggle\" aria-hidden=\"true\"></span>");
            builder.AppendLine("<span class=\"test-suite__title\">");
            builder.AppendLine($"<span class=\"test-suite__name\">{Escape(displayName)}</span>");
            if (!string.IsNullOrEmpty(suite.Type))
            {
                builder.AppendLine($"<span class=\"test-suite__type\">{Escape(suite.Type)}</span>");
            }

            builder.AppendLine("</span>");
            builder.AppendLine("<span class=\"test-suite__meta\">");
            if (!string.IsNullOrEmpty(resultChip))
            {
                builder.AppendLine(resultChip);
            }

            builder.AppendLine($"<span class=\"test-suite__counts\">{Escape(summaryText)}</span>");
            builder.AppendLine("</span>");
            builder.AppendLine("</summary>");
            builder.AppendLine("<div class=\"test-suite__body\">");

            foreach (var childSuite in suite.Suites)
            {
                AppendTestSuite(builder, childSuite, depth + 1);
            }

            foreach (var testCase in suite.Cases)
            {
                AppendTestCase(builder, testCase);
            }

            builder.AppendLine("</div>");
            builder.AppendLine("</details>");
        }

        private static string GetSuiteDisplayName(TestSuiteNode suite)
        {
            if (!string.IsNullOrEmpty(suite.Name))
            {
                return suite.Name;
            }

            if (!string.IsNullOrEmpty(suite.FullName))
            {
                return suite.FullName;
            }

            return suite.Type switch
            {
                "TestRun" => "Test Run",
                "Assembly" => "Assembly",
                "TestSuite" => "Test Suite",
                "TestFixture" => "Test Fixture",
                _ => "Suite"
            };
        }

        private static string BuildSuiteSummaryText(TestSuiteNode suite)
        {
            if (suite.Total > 0)
            {
                return $"{suite.Passed}/{suite.Total} passed";
            }

            var cases = TestResultParser.CollectTestCases(suite);
            if (cases.Count == 0)
            {
                return "0 tests";
            }

            var passed = cases.Count(testCase =>
                string.Equals(testCase.Result, "Passed", StringComparison.OrdinalIgnoreCase));
            return $"{passed}/{cases.Count} passed";
        }

        private static bool ShouldOpenSuiteByDefault(TestSuiteNode suite, int depth)
        {
            if (depth == 0 || SuiteContainsFailure(suite))
            {
                return true;
            }

            return false;
        }

        private static bool SuiteContainsFailure(TestSuiteNode suite)
        {
            if (string.Equals(suite.Result, "Failed", StringComparison.OrdinalIgnoreCase) ||
                suite.Failed > 0)
            {
                return true;
            }

            if (suite.Cases.Any(testCase =>
                    string.Equals(testCase.Result, "Failed", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return suite.Suites.Any(SuiteContainsFailure);
        }

        private static void AppendResultFilter(StringBuilder builder, IReadOnlyList<TestCaseEntry> testCases)
        {
            builder.AppendLine("<section class=\"card tag-cloud-card\">");
            builder.AppendLine("<div class=\"tag-cloud-card__header\">");
            builder.AppendLine("<span class=\"tag-cloud-card__title\">Result</span>");
            builder.AppendLine(
                "<button type=\"button\" class=\"md-button md-button--text\" id=\"clear-filters\">Clear filters</button>");
            builder.AppendLine("</div>");
            builder.AppendLine("<div class=\"tag-cloud\" id=\"tag-cloud\">");
            builder.AppendLine("<div class=\"tag-cloud__group\">");
            builder.AppendLine("<div class=\"tag-cloud__group-title\">Filter</div>");
            builder.AppendLine("<div class=\"tag-cloud__tags\">");

            foreach (var filterId in ResultFilterIds)
            {
                var count = CountByFilter(testCases, filterId);
                var disabled = count == 0 ? " tag--disabled" : string.Empty;
                builder.AppendLine(
                    $"<button type=\"button\" class=\"tag tag--result{disabled}\" " +
                    $"data-result=\"{EscapeAttribute(filterId)}\" " +
                    $"{(count == 0 ? "disabled" : string.Empty)}>" +
                    $"{Escape(GetFilterDisplayName(filterId))} ({count})</button>");
            }

            builder.AppendLine("</div>");
            builder.AppendLine("</div>");
            builder.AppendLine("</div>");
            builder.AppendLine("</section>");
        }

        private static void AppendTestCase(StringBuilder builder, TestCaseEntry testCase)
        {
            var filterId = GetResultFilterId(testCase.Result);
            var displayName = string.IsNullOrEmpty(testCase.FullName) ? testCase.Name : testCase.FullName;
            var chipClass = GetResultChipClass(testCase.Result);
            var caseClass = string.Equals(testCase.Result, "Failed", StringComparison.OrdinalIgnoreCase)
                ? " test-case--failed"
                : string.Empty;
            var durationText = testCase.Duration > 0d
                ? $"{testCase.Duration.ToString("0.###", CultureInfo.InvariantCulture)}s"
                : string.Empty;

            builder.AppendLine(
                $"<article class=\"test-case{caseClass}\" data-result=\"{EscapeAttribute(filterId)}\">");
            builder.AppendLine("<div class=\"test-case__header\">");
            builder.AppendLine($"<span class=\"chip {chipClass}\">{Escape(testCase.Result)}</span>");
            builder.AppendLine("<div>");
            builder.AppendLine($"<div class=\"test-case__name\">{Escape(displayName)}</div>");
            if (!string.IsNullOrEmpty(testCase.ClassName))
            {
                builder.AppendLine($"<div class=\"test-case__class\">{Escape(testCase.ClassName)}</div>");
            }

            builder.AppendLine("</div>");
            builder.AppendLine($"<span class=\"test-case__duration\">{Escape(durationText)}</span>");
            builder.AppendLine("</div>");

            if (string.Equals(testCase.Result, "Failed", StringComparison.OrdinalIgnoreCase) &&
                (!string.IsNullOrEmpty(testCase.FailureMessage) || !string.IsNullOrEmpty(testCase.StackTrace)))
            {
                builder.AppendLine("<details class=\"test-case__failure\" open>");
                builder.AppendLine("<summary class=\"test-case__failure-summary\">Failure details</summary>");
                builder.AppendLine("<div class=\"test-case__failure-body\">");
                if (!string.IsNullOrEmpty(testCase.FailureMessage))
                {
                    builder.AppendLine("<div class=\"test-case__failure-title\">Message</div>");
                    builder.AppendLine($"<pre>{Escape(testCase.FailureMessage)}</pre>");
                }

                if (!string.IsNullOrEmpty(testCase.StackTrace))
                {
                    builder.AppendLine("<div class=\"test-case__failure-title\">Stack Trace</div>");
                    builder.AppendLine($"<pre>{Escape(testCase.StackTrace)}</pre>");
                }

                builder.AppendLine("</div>");
                builder.AppendLine("</details>");
            }

            builder.AppendLine("</article>");
        }

        private static int CountByFilter(IReadOnlyList<TestCaseEntry> testCases, string filterId)
        {
            return testCases.Count(testCase => GetResultFilterId(testCase.Result) == filterId);
        }

        private static string GetFilterDisplayName(string filterId)
        {
            return filterId switch
            {
                "passed" => "Passed",
                "failed" => "Failed",
                "skipped" => "Skipped",
                _ => filterId
            };
        }

        private static string GetResultFilterId(string result)
        {
            if (string.Equals(result, "Passed", StringComparison.OrdinalIgnoreCase))
            {
                return "passed";
            }

            if (string.Equals(result, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return "failed";
            }

            return "skipped";
        }

        private static string GetResultChipClass(string result)
        {
            if (string.Equals(result, "Passed", StringComparison.OrdinalIgnoreCase))
            {
                return "chip-result-passed";
            }

            if (string.Equals(result, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                return "chip-result-failed";
            }

            return "chip-result-skipped";
        }

        private static string GetFilterScript()
        {
            return @"
(function () {
  var activeResults = new Set();
  var tagButtons = Array.prototype.slice.call(document.querySelectorAll('.tag--result:not(.tag--disabled)'));
  var testCases = Array.prototype.slice.call(document.querySelectorAll('.test-case'));
  var suites = Array.prototype.slice.call(document.querySelectorAll('details.test-suite'));
  var visibleCount = document.getElementById('visible-count');
  var clearButton = document.getElementById('clear-filters');

  function caseMatches(testCase) {
    if (activeResults.size === 0) {
      return true;
    }

    var result = testCase.getAttribute('data-result') || '';
    return activeResults.has(result);
  }

  function suiteHasVisibleContent(suite) {
    var body = suite.querySelector(':scope > .test-suite__body');
    if (!body) {
      return false;
    }

    var cases = body.querySelectorAll(':scope > .test-case');
    for (var i = 0; i < cases.length; i++) {
      if (cases[i].style.display !== 'none') {
        return true;
      }
    }

    var childSuites = body.querySelectorAll(':scope > details.test-suite');
    for (var j = 0; j < childSuites.length; j++) {
      if (childSuites[j].style.display !== 'none') {
        return true;
      }
    }

    return false;
  }

  function updateSuiteVisibility() {
    suites.sort(function (a, b) {
      return (parseInt(b.getAttribute('data-depth') || '0', 10) || 0) -
        (parseInt(a.getAttribute('data-depth') || '0', 10) || 0);
    });

    for (var i = 0; i < suites.length; i++) {
      var suite = suites[i];
      suite.style.display = suiteHasVisibleContent(suite) ? '' : 'none';
    }
  }

  function applyFilter() {
    var visible = 0;
    for (var testCase of testCases) {
      var show = caseMatches(testCase);
      testCase.style.display = show ? '' : 'none';
      if (show) {
        visible++;
      }
    }

    updateSuiteVisibility();

    if (visibleCount) {
      visibleCount.textContent = visible + ' tests';
    }
  }

  for (var button of tagButtons) {
    button.addEventListener('click', function () {
      var result = this.getAttribute('data-result');
      if (!result) {
        return;
      }

      if (activeResults.has(result)) {
        activeResults.delete(result);
        this.classList.remove('tag--active');
      } else {
        activeResults.add(result);
        this.classList.add('tag--active');
      }

      applyFilter();
    });
  }

  if (clearButton) {
    clearButton.addEventListener('click', function () {
      activeResults.clear();
      for (var btn of tagButtons) {
        btn.classList.remove('tag--active');
      }

      applyFilter();
    });
  }

  applyFilter();
})();
";
        }

        private static string ToFileUri(string path)
        {
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal);
        }

        private static string EscapeAttribute(string text)
        {
            return Escape(text);
        }
    }
}
