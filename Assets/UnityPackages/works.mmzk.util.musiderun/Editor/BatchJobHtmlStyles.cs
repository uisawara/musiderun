namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal static class BatchJobHtmlStyles
    {
        public static string GetCommonStyles()
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
.chip-result-passed { background: var(--md-success-bg); color: var(--md-success); }
.chip-result-failed { background: var(--md-error-bg); color: var(--md-error); }
.chip-result-skipped, .chip-result-inconclusive { background: var(--md-warning-bg); color: var(--md-warning); }

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
.tag--result.tag--active { background: var(--md-info-bg); border-color: var(--md-primary); }

main { padding-bottom: 32px; }

.error-text { color: var(--md-error); font-weight: 500; }
";
        }

        public static string GetLogStyles()
        {
            return @"
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

        public static string GetTestResultStyles()
        {
            return @"
.test-panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 16px 20px;
  border-bottom: 1px solid var(--md-divider);
}

.test-panel__title { font-size: 15px; font-weight: 500; }

.test-list {
  max-height: 70vh;
  overflow: auto;
  padding: 8px 12px 16px;
  background: var(--md-log-bg);
}

.test-suite {
  border: 1px solid var(--md-divider);
  border-radius: 6px;
  margin: 8px 0;
  background: var(--md-surface);
  overflow: hidden;
}

.test-suite__summary {
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 12px;
  align-items: center;
  padding: 10px 14px;
  cursor: pointer;
  list-style: none;
  user-select: none;
}

.test-suite__summary::-webkit-details-marker {
  display: none;
}

.test-suite__toggle {
  width: 0;
  height: 0;
  border-top: 5px solid transparent;
  border-bottom: 5px solid transparent;
  border-left: 6px solid var(--md-on-surface-medium);
  transition: transform 0.15s ease;
}

.test-suite[open] > .test-suite__summary .test-suite__toggle {
  transform: rotate(90deg);
}

.test-suite__title {
  min-width: 0;
}

.test-suite__name {
  display: block;
  font-size: 13px;
  font-weight: 500;
  word-break: break-word;
}

.test-suite__type {
  display: block;
  margin-top: 2px;
  font-size: 11px;
  color: var(--md-on-surface-medium);
}

.test-suite__meta {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  justify-content: flex-end;
}

.test-suite__counts {
  font-family: 'Roboto Mono', monospace;
  font-size: 11px;
  color: var(--md-on-surface-medium);
  white-space: nowrap;
}

.test-suite__body {
  padding: 0 12px 12px 24px;
  border-top: 1px solid var(--md-divider);
}

.test-suite__body > .test-suite {
  margin-left: 0;
}

.test-case {
  border: 1px solid var(--md-divider);
  border-radius: 6px;
  margin: 8px 0;
  overflow: hidden;
  background: var(--md-surface);
}

.test-case__header {
  display: grid;
  grid-template-columns: auto 1fr auto;
  gap: 12px;
  align-items: center;
  padding: 10px 14px;
}

.test-case__name {
  font-size: 13px;
  font-weight: 500;
  word-break: break-word;
}

.test-case__class {
  font-size: 11px;
  color: var(--md-on-surface-medium);
  margin-top: 2px;
}

.test-case__duration {
  font-family: 'Roboto Mono', monospace;
  font-size: 11px;
  color: var(--md-on-surface-medium);
  white-space: nowrap;
}

.test-case--failed {
  border-color: rgba(239, 83, 80, 0.35);
}

.test-case__failure {
  border-top: 1px solid var(--md-divider);
  background: var(--md-error-bg);
}

.test-case__failure-summary {
  padding: 10px 14px;
  cursor: pointer;
  font-size: 12px;
  font-weight: 500;
  color: var(--md-error);
  list-style: none;
}

.test-case__failure-summary::-webkit-details-marker {
  display: none;
}

.test-case__failure-body {
  padding: 0 14px 12px;
}

.test-case__failure-title {
  font-size: 11px;
  font-weight: 500;
  text-transform: uppercase;
  color: var(--md-error);
  margin-bottom: 6px;
}

.test-case__failure pre {
  margin: 0 0 10px;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: 'Roboto Mono', ui-monospace, monospace;
  font-size: 11px;
  line-height: 1.5;
  color: var(--md-error-text);
}

.test-case__failure pre:last-child {
  margin-bottom: 0;
}
";
        }
    }
}
