using System;
using System.IO;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal static class BatchJobLogAnalyzer
    {
        public static bool TryInferExitCode(string logFilePath, string batchArguments, out int exitCode)
        {
            exitCode = -1;
            if (string.IsNullOrEmpty(logFilePath) || !File.Exists(logFilePath))
            {
                return false;
            }

            if (!LogFileReader.TryReadAllText(logFilePath, out var content))
            {
                return false;
            }

            if (string.IsNullOrEmpty(content))
            {
                return false;
            }

            if (BatchJobCommandLineParser.ContainsArgument(
                    BatchJobCommandLineParser.Parse(batchArguments),
                    "-runTests"))
            {
                return TryInferTestExitCode(content, out exitCode);
            }

            if (content.Contains("[musiderun] Build succeeded") ||
                content.Contains("Build Finished, Result: Success"))
            {
                exitCode = 0;
                return true;
            }

            if (content.Contains("[musiderun] Build failed") ||
                content.Contains("Build Finished, Result: Failed"))
            {
                exitCode = 1;
                return true;
            }

            return false;
        }

        private static bool TryInferTestExitCode(string content, out int exitCode)
        {
            exitCode = -1;

            if (content.Contains("result=\"Failed\"") || content.Contains("Test run failed"))
            {
                exitCode = 1;
                return true;
            }

            const string exitingWithCodeMarker = "Test run completed. Exiting with code";
            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (!trimmed.Contains(exitingWithCodeMarker, StringComparison.Ordinal))
                {
                    continue;
                }

                if (trimmed.Contains("(Ok)", StringComparison.OrdinalIgnoreCase))
                {
                    exitCode = 0;
                    return true;
                }

                var codeStart = trimmed.IndexOf(exitingWithCodeMarker, StringComparison.Ordinal) +
                                exitingWithCodeMarker.Length;
                var remainder = trimmed.Substring(codeStart).Trim();
                var endIndex = remainder.Length;
                var spaceIndex = remainder.IndexOf(' ');
                var parenIndex = remainder.IndexOf('(');
                if (spaceIndex >= 0)
                {
                    endIndex = Math.Min(endIndex, spaceIndex);
                }

                if (parenIndex >= 0)
                {
                    endIndex = Math.Min(endIndex, parenIndex);
                }

                if (int.TryParse(remainder.Substring(0, endIndex).Trim(), out var parsedCode))
                {
                    exitCode = parsedCode;
                    return true;
                }

                break;
            }

            if (content.Contains("Test run completed") || content.Contains("Saving results to"))
            {
                exitCode = 0;
                return true;
            }

            return false;
        }
    }
}
