using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public sealed class UnityBatchRunRequest
    {
        public string JobId { get; set; }
        public string UnityExecutable { get; set; }
        public string ProjectPath { get; set; }
        public string LogFilePath { get; set; }
        public string TestResultsPath { get; set; }
        public string BatchArguments { get; set; }
    }

    public sealed class UnityBatchRunner : IDisposable
    {
        private readonly Action<string> _log;
        private Process _process;
        private int _monitoredProcessId;
        private long _lastLogPosition;

        public UnityBatchRunner(Action<string> log)
        {
            _log = log ?? (_ => { });
        }

        public int ProcessId => _process != null ? _process.Id : _monitoredProcessId;

        public bool IsRunning
        {
            get
            {
                if (_process != null)
                {
                    return !_process.HasExited;
                }

                if (_monitoredProcessId <= 0)
                {
                    return false;
                }

                return TryGetProcessById(_monitoredProcessId, out var process) && !process.HasExited;
            }
        }

        public void Start(UnityBatchRunRequest request)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("バッチ Unity プロセスは既に実行中です。");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(request.LogFilePath) ?? request.ProjectPath);

            var arguments = BuildArguments(request);
            _log($"> {request.UnityExecutable} {string.Join(" ", arguments)}");

            var startInfo = new ProcessStartInfo
            {
                FileName = request.UnityExecutable,
                WorkingDirectory = request.ProjectPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            _monitoredProcessId = 0;
            _lastLogPosition = 0;
            _process.Start();
            _monitoredProcessId = _process.Id;
        }

        public void StartMonitoring(int processId, long lastLogPosition = 0)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(processId));
            }

            _process = null;
            _monitoredProcessId = processId;
            _lastLogPosition = lastLogPosition;
            _log($"[INFO] バッチ Unity プロセス (PID {processId}) の監視を再開します。");
        }

        public void PollLogFile(string logFilePath)
        {
            if (!File.Exists(logFilePath))
            {
                return;
            }

            using var stream = new FileStream(
                logFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            if (stream.Length <= _lastLogPosition)
            {
                return;
            }

            stream.Seek(_lastLogPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            var appended = reader.ReadToEnd();
            _lastLogPosition = stream.Length;

            if (string.IsNullOrWhiteSpace(appended))
            {
                return;
            }

            foreach (var line in appended.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    _log(trimmed);
                }
            }
        }

        public long LastLogPosition => _lastLogPosition;

        public int WaitForExit()
        {
            if (_process != null)
            {
                if (!_process.HasExited)
                {
                    _process.WaitForExit();
                }

                return _process.ExitCode;
            }

            return TryGetExitCode();
        }

        public int TryGetExitCode()
        {
            if (_process != null)
            {
                if (!_process.HasExited)
                {
                    return -2;
                }

                return _process.ExitCode;
            }

            if (_monitoredProcessId <= 0)
            {
                return -1;
            }

            if (!TryGetProcessById(_monitoredProcessId, out var process))
            {
                return -1;
            }

            using (process)
            {
                if (!process.HasExited)
                {
                    return -2;
                }

                return process.ExitCode;
            }
        }

        public void Dispose()
        {
            Dispose(killProcess: false);
        }

        public void Dispose(bool killProcess)
        {
            if (_process == null)
            {
                _monitoredProcessId = 0;
                return;
            }

            if (killProcess && !_process.HasExited)
            {
                try
                {
                    _process.Kill();
                }
                catch
                {
                    // ignored
                }
            }

            _process.Dispose();
            _process = null;
            _monitoredProcessId = 0;
        }

        private static bool TryGetProcessById(int processId, out Process process)
        {
            try
            {
                process = Process.GetProcessById(processId);
                process.Refresh();
                return true;
            }
            catch
            {
                process = null;
                return false;
            }
        }

        private static IReadOnlyList<string> BuildArguments(UnityBatchRunRequest request)
        {
            var arguments = new List<string>
            {
                "-batchmode",
                "-quit",
                "-nographics",
                "-projectPath",
                request.ProjectPath,
                "-logFile",
                request.LogFilePath
            };

            var extraArguments = BatchJobCommandLineParser.Parse(request.BatchArguments);
            var hasTestResults = false;

            for (var i = 0; i < extraArguments.Count; i++)
            {
                var argument = extraArguments[i];
                arguments.Add(argument);

                if (string.Equals(argument, "-testResults", StringComparison.Ordinal) && i + 1 < extraArguments.Count)
                {
                    hasTestResults = true;
                }
            }

            if (BatchJobCommandLineParser.ContainsArgument(extraArguments, "-runTests") &&
                !hasTestResults &&
                !string.IsNullOrEmpty(request.TestResultsPath))
            {
                arguments.Add("-testResults");
                arguments.Add(request.TestResultsPath);
            }

            return arguments;
        }
    }
}
