using System.Collections.Generic;
using System.Text;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal static class BatchJobCommandLineParser
    {
        public static IReadOnlyList<string> Parse(string commandLine)
        {
            var arguments = new List<string>();
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return arguments;
            }

            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < commandLine.Length; i++)
            {
                var ch = commandLine[i];
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(ch))
                {
                    AppendToken(current, arguments);
                    continue;
                }

                current.Append(ch);
            }

            AppendToken(current, arguments);
            return arguments;
        }

        public static bool ContainsArgument(IReadOnlyList<string> arguments, string name)
        {
            foreach (var argument in arguments)
            {
                if (string.Equals(argument, name, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendToken(StringBuilder current, ICollection<string> arguments)
        {
            if (current.Length == 0)
            {
                return;
            }

            arguments.Add(current.ToString());
            current.Clear();
        }
    }
}
