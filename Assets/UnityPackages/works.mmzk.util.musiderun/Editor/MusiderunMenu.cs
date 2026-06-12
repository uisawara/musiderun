using System;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public static class MusiderunMenu
    {
        private const string MenuRoot = "Tools/musiderun/";
        private const string CleanMenuPath = MenuRoot + "Clean Mirror Worktrees...";
        private const string CleanDialogTitle = "Clean Mirror Worktrees";

        [MenuItem(MenuRoot + "Open Window")]
        public static void OpenWindow()
        {
            MusiderunWindow.ShowWindow();
        }

        [MenuItem(MenuRoot + "Create Settings JSON")]
        public static void CreateSettingsJson()
        {
            MusiderunSettingsJsonStore.EnsureJsonExists();
            EditorUtility.RevealInFinder(MusiderunSettingsJsonStore.ResolveAbsolutePath());
        }

        [MenuItem(CleanMenuPath, false, 100)]
        public static void CleanMirrorWorktrees()
        {
            if (MusiderunSession.Orchestrator.IsBusy)
            {
                EditorUtility.DisplayDialog(
                    CleanDialogTitle,
                    "Batch jobs are currently running. Wait for them to finish before cleaning.",
                    "OK");
                return;
            }

            if (!MusiderunSettingsJsonStore.TryLoad(out var data, out _))
            {
                EditorUtility.DisplayDialog(
                    CleanDialogTitle,
                    "MusiderunSettings.json was not found. Create it from the menu first.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    CleanDialogTitle,
                    "This will permanently delete all mirror worktrees, their working directories, " +
                    "associated mirror branches (musiderun/mirror-*), and the fallback BatchJobLogs " +
                    "folder in the repository root.\n\nThis cannot be undone. Continue?",
                    "Clean",
                    "Cancel"))
            {
                return;
            }

            _ = RunCleanMirrorWorktreesAsync(data);
        }

        [MenuItem(CleanMenuPath, true)]
        public static bool ValidateCleanMirrorWorktrees()
        {
            return !MusiderunSession.Orchestrator.IsBusy;
        }

        private static async Task RunCleanMirrorWorktreesAsync(MusiderunSettingsData data)
        {
            try
            {
                var result = await MusiderunMirrorCleaner.CleanAllAsync(data).ConfigureAwait(false);

                EditorMainThreadDispatcher.Enqueue(() =>
                {
                    if (result.HasWarnings)
                    {
                        var details = new StringBuilder();
                        details.AppendLine("Cleanup finished with warnings:");
                        foreach (var warning in result.Warnings)
                        {
                            details.AppendLine(warning);
                        }

                        details.AppendLine();
                        details.AppendLine($"Removed worktrees: {result.RemovedWorktrees}");
                        details.AppendLine($"Removed branches: {result.RemovedBranches}");
                        if (result.RemovedFallbackBatchJobLogs)
                        {
                            details.AppendLine("Removed fallback BatchJobLogs folder.");
                        }

                        EditorUtility.DisplayDialog(CleanDialogTitle, details.ToString(), "OK");
                        return;
                    }

                    var message = new StringBuilder();
                    message.AppendLine("Cleanup completed.");
                    message.AppendLine();
                    message.AppendLine($"Removed worktrees: {result.RemovedWorktrees}");
                    message.AppendLine($"Removed branches: {result.RemovedBranches}");
                    if (result.RemovedFallbackBatchJobLogs)
                    {
                        message.AppendLine("Removed fallback BatchJobLogs folder.");
                    }

                    EditorUtility.DisplayDialog(CleanDialogTitle, message.ToString(), "OK");
                });
            }
            catch (Exception ex)
            {
                EditorMainThreadDispatcher.Enqueue(() =>
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.DisplayDialog(CleanDialogTitle, ex.Message, "OK");
                });
            }
        }
    }
}
