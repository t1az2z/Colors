using System;
using System.IO;
using GameCore.Newtonsoft;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Где лежат параметры запуска (<see cref="LaunchOptions"/>): в редакторе — UserSettings/ColorsActionLaunch.json
    /// (личный файл, не в git), его пишет окно «Параметры запуска». В сборке параметров нет — игра идёт по Game.json.
    /// </summary>
    public static class LaunchOptionsStore
    {
        public const string FileName = "ColorsActionLaunch.json";

        public static string FilePath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings", FileName));

        public static LaunchOptions Load()
        {
#if UNITY_EDITOR
            try
            {
                if (File.Exists(FilePath))
                {
                    var options = (LaunchOptions)new NewtonsoftConfigSerializer().Deserialize(File.ReadAllText(FilePath), typeof(LaunchOptions));
                    if (options != null)
                        return options;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Параметры запуска не прочитаны ({FilePath}): {e.Message}");
            }
#endif
            return LaunchOptions.None;
        }

        public static void Save(LaunchOptions options)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, new NewtonsoftConfigSerializer().Serialize(options));
        }
    }
}
