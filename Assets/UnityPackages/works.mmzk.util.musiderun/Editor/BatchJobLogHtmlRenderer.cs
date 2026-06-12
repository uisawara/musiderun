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
            builder.AppendLine($"<title>{Escape(request.DisplayName)} — Musiderun Log</title>");
            builder.AppendLine("<style>");
            builder.AppendLine(GetStyles());
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

        private static string GetStyles()
        {
            return @"
@import url('https://fonts.googleapis.com/css2?family=Roboto:wght@400;500;700&family=Roboto+Mono:wght@400&display=swap');

:root {
  --md-primary: #90caf9;
  --md-primary-dark: #42a5f5;
  --md-primary-light: #1976d2;
  --md-surface: #1e1e1e;
  --md-background: #121212;
  --md-on-surface: rgba(255, 255, 255, 0.87);
  --md-on-surface-medium: rgba(255, 255, 255, 0.60);
  --md-on-surface-disabled: rgba(255, 255, 255, 0.38);
  --md-divider: rgba(255, 255, 255, 0.12);
  --md-error: #ef5350;
  --md-error-bg: rgba(239, 83, 80, 0.16);
  --md-error-text: #ff8a80;
  --md-warning: #ffa726;
  --md-warning-bg: rgba(255, 167, 38, 0.16);
  --md-warning-text: #ffcc80;
  --md-success: #66bb6a;
  --md-success-bg: rgba(102, 187, 106, 0.16);
  --md-info-bg: rgba(144, 202, 249, 0.16);
  --md-code-bg: #2d2d2d;
  --md-chip-muted-bg: #2d2d2d;
  --md-tag-bg: #2d2d2d;
  --md-tag-hover: #383838;
  --md-log-bg: #181818;
  --md-line-hover: #252525;
  --md-header-bg: #2d2d2d;
  --md-elevation-1: 0 1px 3px rgba(0,0,0,.4), 0 1px 2px rgba(0,0,0,.5);
  --md-elevation-2: 0 3px 6px rgba(0,0,0,.5), 0 3px 6px rgba(0,0,0,.6);
  --md-radius: 8px;
}

* { box-sizing: border-box; }

body {
  font-family: 'Roboto', -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
  margin: 0;
  background: var(--md-background);
  color: var(--md-on-surface);
  line-height: 1.5;
}

.app-bar {
  background: var(--md-header-bg);
  color: var(--md-on-surface);
  box-shadow: var(--md-elevation-2);
  padding: 20px 24px;
}

.app-bar__inner {
  max-width: 1120px;
  margin: 0 auto;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  flex-wrap: wrap;
}

.app-bar__title {
  margin: 0;
  font-size: 22px;
  font-weight: 500;
}

.card {
  background: var(--md-surface);
  border-radius: var(--md-radius);
  box-shadow: var(--md-elevation-1);
  margin: 16px auto;
  max-width: 1120px;
  overflow: hidden;
}

.meta-card { padding: 20px 24px; }

.meta {
  display: grid;
  grid-template-columns: 120px 1fr;
  gap: 8px 16px;
  margin: 0;
}

.meta dt {
  color: var(--md-on-surface-medium);
  font-size: 12px;
  font-weight: 500;
  text-transform: uppercase;
}

.meta dd { margin: 0; font-size: 14px; }

.meta code {
  font-family: 'Roboto Mono', monospace;
  font-size: 13px;
  background: var(--md-code-bg);
  padding: 2px 8px;
  border-radius: 4px;
}

.file-links {
  margin-top: 16px;
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}

.md-button {
  display: inline-flex;
  align-items: center;
  padding: 8px 16px;
  border-radius: 4px;
  font-size: 13px;
  font-weight: 500;
  text-decoration: none;
  border: none;
  cursor: pointer;
  background: transparent;
}

.md-button--outlined {
  color: var(--md-primary);
  border: 1px solid var(--md-primary-light);
  background: var(--md-surface);
}

.md-button--text {
  color: var(--md-primary);
}

.chip {
  display: inline-flex;
  align-items: center;
  padding: 4px 12px;
  border-radius: 16px;
  font-size: 12px;
  font-weight: 500;
}

.chip-state-completed { background: var(--md-success-bg); color: var(--md-success); }
.chip-state-failed { background: var(--md-error-bg); color: var(--md-error); }
.chip-state-skipped { background: var(--md-warning-bg); color: var(--md-warning); }
.chip-state-running, .chip-state-syncing, .chip-state-queued { background: var(--md-info-bg); color: var(--md-primary); }
.chip-muted { background: var(--md-chip-muted-bg); color: var(--md-on-surface-medium); }

.tag-cloud-card { padding: 16px 20px 20px; }

.tag-cloud-card__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.tag-cloud-card__title {
  font-size: 15px;
  font-weight: 500;
}

.tag-cloud__group { margin-bottom: 14px; }
.tag-cloud__group:last-child { margin-bottom: 0; }

.tag-cloud__group-title {
  font-size: 11px;
  font-weight: 500;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--md-on-surface-medium);
  margin-bottom: 8px;
}

.tag-cloud__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.tag {
  border: 1px solid var(--md-divider);
  background: var(--md-tag-bg);
  color: var(--md-on-surface);
  border-radius: 16px;
  padding: 6px 12px;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s, box-shadow 0.15s;
}

.tag:hover:not(.tag--disabled):not(:disabled) {
  background: var(--md-tag-hover);
}

.tag--active {
  background: var(--md-info-bg);
  border-color: var(--md-primary);
  box-shadow: inset 0 0 0 1px var(--md-primary);
}

.tag--disabled,
.tag:disabled {
  opacity: 0.45;
  cursor: not-allowed;
}

.tag--severity.tag--active { background: rgba(244, 143, 177, 0.2); border-color: #f48fb1; }
.tag--section.tag--active { background: rgba(159, 168, 218, 0.2); border-color: #9fa8da; }
.tag--source.tag--active { background: rgba(128, 203, 196, 0.2); border-color: #80cbc4; }

main { padding-bottom: 32px; }

.log-panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--md-divider);
}

.log-panel__title { font-size: 15px; font-weight: 500; }

.log-list {
  max-height: 70vh;
  overflow: auto;
  padding: 8px 12px 16px;
  font-family: 'Roboto Mono', ui-monospace, monospace;
  font-size: 12px;
  line-height: 1.55;
  background: var(--md-log-bg);
}

.log-line {
  display: grid;
  grid-template-columns: 48px minmax(120px, 220px) 1fr;
  gap: 8px;
  align-items: start;
  padding: 4px 8px;
  margin: 1px 0;
  border-radius: 4px;
  border-left: 3px solid transparent;
}

.line-no {
  color: var(--md-on-surface-disabled);
  user-select: none;
  text-align: right;
}

.log-line__labels {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.label-chip {
  display: inline-flex;
  padding: 1px 6px;
  border-radius: 10px;
  font-size: 10px;
  font-weight: 500;
  font-family: 'Roboto', sans-serif;
  white-space: nowrap;
}

.label-chip--error { background: var(--md-error-bg); color: var(--md-error); }
.label-chip--warning { background: var(--md-warning-bg); color: var(--md-warning); }
.label-chip--info { background: var(--md-chip-muted-bg); color: var(--md-on-surface-medium); }
.label-chip--section { background: rgba(159, 168, 218, 0.2); color: #9fa8da; }
.label-chip--source { background: rgba(128, 203, 196, 0.2); color: #80cbc4; }

.log-line__text {
  white-space: pre-wrap;
  word-break: break-word;
}

.line-error {
  background: var(--md-error-bg);
  border-left-color: var(--md-error);
  color: var(--md-error-text);
}

.line-warning {
  background: var(--md-warning-bg);
  border-left-color: var(--md-warning);
  color: var(--md-warning-text);
}

.line-info:hover { background: var(--md-line-hover); }

.error-text { color: var(--md-error); font-weight: 500; }

@media (max-width: 720px) {
  .log-line {
    grid-template-columns: 40px 1fr;
  }

  .log-line__labels {
    grid-column: 2;
  }

  .log-line__text {
    grid-column: 1 / -1;
    padding-left: 48px;
  }
}
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
