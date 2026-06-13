using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Works.Mmzk.Util.Musiderun;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal sealed class BatchJobLogHtmlRequest
    {
        public string JobId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public BatchJobState FinalState { get; set; }
        public int ExitCode { get; set; } = -1;
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string MirrorLogFilePath { get; set; } = string.Empty;
        public string UnityLogFilePath { get; set; } = string.Empty;
        public string OutputHtmlPath { get; set; } = string.Empty;
        public TestResultSummary TestSummary { get; set; }
        public string TestResultsHtmlFilePath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    internal static class BatchJobLogHtmlRenderer
    {
        public static bool TryRender(BatchJobLogHtmlRequest request, out string error)
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

            try
            {
                var entries = BatchJobLogClassifier.Classify(request);
                var labels = BatchJobLogClassifier.CollectLabels(entries);
                var html = BuildHtml(request, entries, labels);
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
            BatchJobLogHtmlRequest request,
            List<BatchJobLogLineEntry> entries,
            IReadOnlyList<LogLabelDefinition> labels)
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
            builder.AppendLine($"<title>{Escape(request.DisplayName)} — musiderun log</title>");
            builder.AppendLine("<style>");
            builder.AppendLine(BatchJobHtmlStyles.GetCommonStyles());
            builder.AppendLine(BatchJobHtmlStyles.GetLogStyles());
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
            builder.AppendLine($"<dt>Exit Code</dt><dd>{request.ExitCode}</dd>");
            if (duration.HasValue)
            {
                builder.AppendLine($"<dt>Duration</dt><dd>{duration.Value:mm\\:ss}</dd>");
            }

            builder.AppendLine($"<dt>Started</dt><dd>{Escape(request.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"))}</dd>");
            if (request.TestSummary is { Parsed: true })
            {
                builder.AppendLine($"<dt>Tests</dt><dd>{Escape(request.TestSummary.Message)}</dd>");
            }

            if (!string.IsNullOrEmpty(request.ErrorMessage))
            {
                builder.AppendLine($"<dt>Error</dt><dd class=\"error-text\">{Escape(request.ErrorMessage)}</dd>");
            }

            builder.AppendLine("</dl>");
            builder.AppendLine("<nav class=\"file-links\">");
            if (!string.IsNullOrEmpty(request.MirrorLogFilePath))
            {
                builder.AppendLine(
                    $"<a class=\"md-button md-button--outlined\" href=\"{EscapeAttribute(ToFileUri(request.MirrorLogFilePath))}\">" +
                    "mirror.log</a>");
            }

            if (!string.IsNullOrEmpty(request.UnityLogFilePath))
            {
                builder.AppendLine(
                    $"<a class=\"md-button md-button--outlined\" href=\"{EscapeAttribute(ToFileUri(request.UnityLogFilePath))}\">" +
                    "unity.log</a>");
            }

            if (!string.IsNullOrEmpty(request.TestResultsHtmlFilePath) &&
                File.Exists(request.TestResultsHtmlFilePath))
            {
                builder.AppendLine(
                    $"<a class=\"md-button md-button--outlined\" href=\"{EscapeAttribute(ToFileUri(request.TestResultsHtmlFilePath))}\">" +
                    "results.html</a>");
            }

            if (request.TestSummary is { Parsed: true } &&
                !string.IsNullOrEmpty(request.TestSummary.ResultsXmlPath) &&
                File.Exists(request.TestSummary.ResultsXmlPath))
            {
                builder.AppendLine(
                    $"<a class=\"md-button md-button--outlined\" href=\"{EscapeAttribute(ToFileUri(request.TestSummary.ResultsXmlPath))}\">" +
                    "results.xml</a>");
            }

            builder.AppendLine("</nav>");
            builder.AppendLine("</section>");

            AppendTagCloud(builder, labels);

            builder.AppendLine("<main>");
            builder.AppendLine("<section class=\"card log-panel\">");
            builder.AppendLine(
                $"<div class=\"log-panel__header\">" +
                $"<span class=\"log-panel__title\">Log</span>" +
                $"<span class=\"chip chip-muted\" id=\"visible-count\">{entries.Count} lines</span>" +
                $"</div>");
            builder.AppendLine("<div class=\"log-list\" id=\"log-list\">");
            foreach (var entry in entries)
            {
                AppendLogLine(builder, entry, labels);
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

        private static void AppendTagCloud(StringBuilder builder, IReadOnlyList<LogLabelDefinition> labels)
        {
            builder.AppendLine("<section class=\"card tag-cloud-card\">");
            builder.AppendLine("<div class=\"tag-cloud-card__header\">");
            builder.AppendLine("<span class=\"tag-cloud-card__title\">Labels</span>");
            builder.AppendLine(
                "<button type=\"button\" class=\"md-button md-button--text\" id=\"clear-filters\">Clear filters</button>");
            builder.AppendLine("</div>");
            builder.AppendLine("<div class=\"tag-cloud\" id=\"tag-cloud\">");

            AppendTagGroup(builder, "Severity", labels.Where(label => label.Category == LogLabelCategory.Severity));
            AppendTagGroup(builder, "Section", labels.Where(label => label.Category == LogLabelCategory.Section));
            AppendTagGroup(builder, "Source", labels.Where(label => label.Category == LogLabelCategory.Source));

            builder.AppendLine("</div>");
            builder.AppendLine("</section>");
        }

        private static void AppendTagGroup(
            StringBuilder builder,
            string groupTitle,
            IEnumerable<LogLabelDefinition> groupLabels)
        {
            builder.AppendLine("<div class=\"tag-cloud__group\">");
            builder.AppendLine($"<div class=\"tag-cloud__group-title\">{Escape(groupTitle)}</div>");
            builder.AppendLine("<div class=\"tag-cloud__tags\">");
            foreach (var label in groupLabels)
            {
                var disabled = label.Count == 0 ? " tag--disabled" : string.Empty;
                var categoryClass = $"tag--{label.Category.ToString().ToLowerInvariant()}";
                builder.AppendLine(
                    $"<button type=\"button\" class=\"tag {categoryClass}{disabled}\" " +
                    $"data-label=\"{EscapeAttribute(label.Id)}\" " +
                    $"{(label.Count == 0 ? "disabled" : string.Empty)}>" +
                    $"{Escape(label.DisplayName)} ({label.Count})</button>");
            }

            builder.AppendLine("</div>");
            builder.AppendLine("</div>");
        }

        private static void AppendLogLine(
            StringBuilder builder,
            BatchJobLogLineEntry entry,
            IReadOnlyList<LogLabelDefinition> labelDefinitions)
        {
            var labelLookup = labelDefinitions.ToDictionary(label => label.Id, label => label);
            var severityClass = entry.Severity switch
            {
                BatchJobLogLineSeverity.Error => "line-error",
                BatchJobLogLineSeverity.Warning => "line-warning",
                _ => "line-info"
            };
            var labelsAttribute = string.Join(" ", entry.Labels.OrderBy(label => label, StringComparer.Ordinal));

            builder.AppendLine(
                $"<div id=\"{entry.AnchorId}\" class=\"log-line {severityClass}\" data-labels=\"{EscapeAttribute(labelsAttribute)}\">");
            builder.AppendLine($"<span class=\"line-no\">{entry.LineNumber,5}</span>");
            builder.AppendLine("<span class=\"log-line__labels\">");
            foreach (var labelId in entry.Labels.OrderBy(label => label, StringComparer.Ordinal))
            {
                if (!labelLookup.TryGetValue(labelId, out var definition))
                {
                    continue;
                }

                var chipClass = definition.Category switch
                {
                    LogLabelCategory.Severity => $"label-chip label-chip--{labelId}",
                    LogLabelCategory.Source => "label-chip label-chip--source",
                    _ => "label-chip label-chip--section"
                };
                builder.AppendLine($"<span class=\"{chipClass}\">{Escape(definition.DisplayName)}</span>");
            }

            builder.AppendLine("</span>");
            builder.AppendLine($"<span class=\"log-line__text\">{Escape(entry.Text)}</span>");
            builder.AppendLine("</div>");
        }

        private static string GetFilterScript()
        {
            return @"
(function () {
  var categories = ['severity', 'section', 'source'];
  var activeByCategory = { severity: new Set(), section: new Set(), source: new Set() };
  var tagButtons = Array.prototype.slice.call(document.querySelectorAll('.tag:not(.tag--disabled)'));
  var logLines = Array.prototype.slice.call(document.querySelectorAll('.log-line'));
  var visibleCount = document.getElementById('visible-count');
  var clearButton = document.getElementById('clear-filters');

  function getCategory(button) {
    for (var i = 0; i < categories.length; i++) {
      if (button.classList.contains('tag--' + categories[i])) {
        return categories[i];
      }
    }

    return null;
  }

  function hasAnyActiveFilter() {
    for (var i = 0; i < categories.length; i++) {
      if (activeByCategory[categories[i]].size > 0) {
        return true;
      }
    }

    return false;
  }

  function lineMatches(line) {
    if (!hasAnyActiveFilter()) {
      return true;
    }

    var labels = (line.getAttribute('data-labels') || '').split(/\s+/).filter(Boolean);
    var labelSet = new Set(labels);
    for (var i = 0; i < categories.length; i++) {
      var category = categories[i];
      var active = activeByCategory[category];
      if (active.size === 0) {
        continue;
      }

      var categoryMatch = false;
      for (var label of active) {
        if (labelSet.has(label)) {
          categoryMatch = true;
          break;
        }
      }

      if (!categoryMatch) {
        return false;
      }
    }

    return true;
  }

  function applyFilter() {
    var visible = 0;
    for (var line of logLines) {
      var show = lineMatches(line);
      line.style.display = show ? '' : 'none';
      if (show) {
        visible++;
      }
    }

    if (visibleCount) {
      visibleCount.textContent = visible + ' lines';
    }
  }

  for (var button of tagButtons) {
    button.addEventListener('click', function () {
      var category = getCategory(this);
      var label = this.getAttribute('data-label');
      if (!category || !label) {
        return;
      }

      var active = activeByCategory[category];
      if (active.has(label)) {
        active.delete(label);
        this.classList.remove('tag--active');
      } else {
        active.add(label);
        this.classList.add('tag--active');
      }

      applyFilter();
    });
  }

  if (clearButton) {
    clearButton.addEventListener('click', function () {
      for (var i = 0; i < categories.length; i++) {
        activeByCategory[categories[i]].clear();
      }

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
