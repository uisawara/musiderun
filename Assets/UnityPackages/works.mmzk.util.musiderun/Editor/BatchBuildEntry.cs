using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public static class BatchBuildEntry
    {
        private const string ExecuteMethodName =
            "Works.Mmzk.Util.Musiderun.Editor.BatchBuildEntry.Execute";

        public static string ExecuteMethod => ExecuteMethodName;

        public static void Execute()
        {
            var buildTarget = EditorUserBuildSettings.activeBuildTarget;
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[Musiderun] 有効なビルドシーンがありません。");
                EditorApplication.Exit(1);
                return;
            }

            var outputLocation = PlatformUtility.GetBuildOutputLocation(buildTarget);
            var outputDirectory = Path.GetDirectoryName(outputLocation);
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            Debug.Log($"[Musiderun] Build start: target={buildTarget}, output={outputLocation}");

            var report = BuildPipeline.BuildPlayer(scenes, outputLocation, buildTarget, BuildOptions.None);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Musiderun] Build succeeded: {summary.outputPath}");
                EditorApplication.Exit(0);
                return;
            }

            Debug.LogError(
                $"[Musiderun] Build failed: result={summary.result}, errors={summary.totalErrors}");
            EditorApplication.Exit(1);
        }
    }
}
