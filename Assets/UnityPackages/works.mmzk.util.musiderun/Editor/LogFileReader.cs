using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal static class LogFileReader
    {
        private const int DefaultMaxAttempts = 8;
        private const int DefaultRetryDelayMs = 125;

        public static bool TryReadAllText(
            string path,
            out string content,
            int maxAttempts = DefaultMaxAttempts,
            int retryDelayMs = DefaultRetryDelayMs)
        {
            content = string.Empty;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    using var stream = OpenReadStream(path);
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    content = reader.ReadToEnd();
                    return true;
                }
                catch (IOException) when (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(retryDelayMs);
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts - 1)
                {
                    Thread.Sleep(retryDelayMs);
                }
            }

            return false;
        }

        public static IReadOnlyList<string> ReadAllLines(
            string path,
            int maxAttempts = DefaultMaxAttempts,
            int retryDelayMs = DefaultRetryDelayMs)
        {
            if (!TryReadAllText(path, out var content, maxAttempts, retryDelayMs))
            {
                return Array.Empty<string>();
            }

            if (string.IsNullOrEmpty(content))
            {
                return Array.Empty<string>();
            }

            return content.Split('\n');
        }

        private static FileStream OpenReadStream(string path)
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
        }
    }
}
