using System;
using Works.Mmzk.Util.Musiderun;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    [Serializable]
    public sealed class BatchJobDefinitionData
    {
        public string id = string.Empty;
        public string displayName = string.Empty;
        public string targetOS = nameof(BatchJobTargetOS.Any);
        public string batchArguments = string.Empty;
        public string artifactFolder = string.Empty;

        public BatchJobTargetOS GetTargetOS()
        {
            return Enum.TryParse(targetOS, out BatchJobTargetOS parsed)
                ? parsed
                : BatchJobTargetOS.Any;
        }
    }

    [Serializable]
    public sealed class MusiderunSettingsData
    {
        public int version = 1;
        public string unityExecutablePath = string.Empty;
        public string logOutputDirectory = string.Empty;
        public string mirrorWorktreeBasePath = string.Empty;
        public string mirrorBranchPrefix = "musiderun/mirror";
        public string defaultWorkingBranch = "main";
        public BatchJobDefinitionData[] jobs = Array.Empty<BatchJobDefinitionData>();
    }
}
