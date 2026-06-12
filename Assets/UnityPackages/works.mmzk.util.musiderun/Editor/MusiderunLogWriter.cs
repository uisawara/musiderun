using System;
using System.Collections.Generic;
using System.IO;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal sealed class MusiderunLogWriter
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, string> _mirrorLogPaths = new(StringComparer.Ordinal);

        public void Register(BatchJobExecution execution)
        {
            if (execution?.Definition == null ||
                string.IsNullOrEmpty(execution.Definition.id) ||
                string.IsNullOrEmpty(execution.MirrorLogFilePath))
            {
                return;
            }

            lock (_lock)
            {
                _mirrorLogPaths[execution.Definition.id] = execution.MirrorLogFilePath;
            }
        }

        public void Unregister(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                return;
            }

            lock (_lock)
            {
                _mirrorLogPaths.Remove(jobId);
            }
        }

        public void TryAppendFromMessage(string message)
        {
            if (!TryExtractJobId(message, out var jobId))
            {
                return;
            }

            string path;
            lock (_lock)
            {
                if (!_mirrorLogPaths.TryGetValue(jobId, out path))
                {
                    return;
                }
            }

            AppendLine(path, message);
        }

        public void AppendMirrorLine(string jobId, string line)
        {
            if (string.IsNullOrEmpty(jobId) || string.IsNullOrEmpty(line))
            {
                return;
            }

            string path;
            lock (_lock)
            {
                if (!_mirrorLogPaths.TryGetValue(jobId, out path))
                {
                    return;
                }
            }

            AppendLine(path, line);
        }

        private static bool TryExtractJobId(string message, out string jobId)
        {
            jobId = string.Empty;
            if (string.IsNullOrEmpty(message) || message[0] != '[')
            {
                return false;
            }

            var closeIndex = message.IndexOf(']');
            if (closeIndex <= 1)
            {
                return false;
            }

            jobId = message.Substring(1, closeIndex - 1);
            return !string.IsNullOrEmpty(jobId);
        }

        private void AppendLine(string path, string line)
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                lock (_lock)
                {
                    File.AppendAllText(path, line.TrimEnd('\r', '\n') + Environment.NewLine);
                }
            }
            catch
            {
                // ignored — mirror log failure must not break job execution
            }
        }
    }
}
