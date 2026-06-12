using System.Runtime.InteropServices;
using Works.Mmzk.Util.Musiderun;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    internal static class BatchJobTargetOSUtility
    {
        public static bool MatchesCurrentOS(BatchJobTargetOS targetOS)
        {
            return targetOS switch
            {
                BatchJobTargetOS.Any => true,
                BatchJobTargetOS.Windows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                BatchJobTargetOS.macOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                BatchJobTargetOS.Linux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
                _ => false
            };
        }

        public static string GetCurrentOSLabel()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return nameof(BatchJobTargetOS.Windows);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return nameof(BatchJobTargetOS.macOS);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return nameof(BatchJobTargetOS.Linux);
            }

            return "Unknown";
        }
    }
}
