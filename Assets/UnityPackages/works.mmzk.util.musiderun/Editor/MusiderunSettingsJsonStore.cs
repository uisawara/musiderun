using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Works.Mmzk.Util.Musiderun;

namespace Works.Mmzk.Util.Musiderun.Editor
{
    public static class MusiderunSettingsJsonStore
    {
        public const string JsonAssetPath = "Assets/Settings/MusiderunSettings.json";

        public static MusiderunSettingsData CreateDefaultData()
        {
            return new MusiderunSettingsData
            {
                version = 1,
                mirrorBranchPrefix = "musiderun/mirror",
                jobs = new[]
                {
                    new BatchJobDefinitionData
                    {
                        id = "build",
                        displayName = "Build",
                        targetOS = nameof(BatchJobTargetOS.macOS),
                        batchArguments =
                            "-executeMethod Works.Mmzk.Util.Musiderun.Editor.BatchBuildEntry.Execute"
                    },
                    new BatchJobDefinitionData
                    {
                        id = "tests",
                        displayName = "Run Tests",
                        targetOS = nameof(BatchJobTargetOS.Any),
                        batchArguments = "-runTests -testPlatform editmode"
                    }
                }
            };
        }

        public static string ResolveAbsolutePath()
        {
            return Path.GetFullPath(Path.Combine(PlatformUtility.GetRepositoryRoot(), JsonAssetPath));
        }

        public static bool JsonExists()
        {
            return File.Exists(ResolveAbsolutePath());
        }

        public static bool TryLoad(out MusiderunSettingsData data, out string error)
        {
            data = null;
            error = string.Empty;

            var absolutePath = ResolveAbsolutePath();
            if (!File.Exists(absolutePath))
            {
                error = $"JSON 設定ファイルが見つかりません: {JsonAssetPath}";
                return false;
            }

            try
            {
                var json = File.ReadAllText(absolutePath);
                data = JsonUtility.FromJson<MusiderunSettingsData>(json);
                if (data == null)
                {
                    error = "JSON の解析に失敗しました。";
                    return false;
                }

                if (data.jobs == null)
                {
                    data.jobs = Array.Empty<BatchJobDefinitionData>();
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static MusiderunSettingsData LoadOrDefault()
        {
            if (TryLoad(out var data, out _))
            {
                return data;
            }

            return CreateDefaultData();
        }

        public static void Save(MusiderunSettingsData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var absolutePath = ResolveAbsolutePath();
            var directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonUtility.ToJson(data, true);
            File.WriteAllText(absolutePath, json);
            AssetDatabase.ImportAsset(JsonAssetPath);
            AssetDatabase.Refresh();
        }

        public static void EnsureJsonExists()
        {
            if (TryLoad(out _, out _))
            {
                return;
            }

            Save(CreateDefaultData());
        }

        public static BatchJobDefinitionData FindJob(MusiderunSettingsData data, string jobId)
        {
            if (data?.jobs == null || string.IsNullOrEmpty(jobId))
            {
                return null;
            }

            foreach (var job in data.jobs)
            {
                if (string.Equals(job.id, jobId, StringComparison.Ordinal))
                {
                    return job;
                }
            }

            return null;
        }
    }
}
