using System.IO;
using GameCore.Config;
using UnityEngine;

namespace GameCore.Unity
{
    public static class UnityConfigSources
    {
        /// <summary>
        /// StreamingAssets/{folder}/{key}.json через System.IO: Windows, macOS, Linux, iOS, редактор.
        /// На Android и WebGL используйте <see cref="ResourcesConfigSource"/> или свой источник на UnityWebRequest.
        /// </summary>
        public static FileConfigSource StreamingAssets(string folder = "Configs")
        {
            return new FileConfigSource(Path.Combine(Application.streamingAssetsPath, folder));
        }
    }

    /// <summary>
    /// Конфиги как TextAsset в Resources/{folder}/{key}.json. Работает на всех платформах.
    /// </summary>
    public sealed class ResourcesConfigSource : IConfigSource
    {
        private readonly string folder;

        public ResourcesConfigSource(string folder = "Configs")
        {
            this.folder = folder;
        }

        public bool TryRead(string key, out string text)
        {
            var asset = Resources.Load<TextAsset>(string.IsNullOrEmpty(folder) ? key : $"{folder}/{key}");
            if (asset == null)
            {
                text = null;
                return false;
            }

            text = asset.text;
            Resources.UnloadAsset(asset);
            return true;
        }
    }
}
