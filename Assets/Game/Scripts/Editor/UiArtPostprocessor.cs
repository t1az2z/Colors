using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Импорт спрайтов нового интерфейса (Art/UI/Atlas и Art/UI/Wheel — их рисует Tools/UI/make_ui_sprites.py):
    /// спрайт без мипов, без сжатия, бордеры 9-slice из Tools/UI/borders.json, пивот сектора колеса (центр колеса)
    /// из Tools/UI/wheel.json.
    /// </summary>
    public sealed class UiArtPostprocessor : AssetPostprocessor
    {
        private const string AtlasFolder = "Assets/Game/Art/UI/Atlas/";
        private const string WheelFolder = "Assets/Game/Art/UI/Wheel/";

        private void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(AtlasFolder) && !path.StartsWith(WheelFolder))
                return;
            var importer = (TextureImporter)assetImporter;
            var name = Path.GetFileNameWithoutExtension(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            if (Borders().TryGetValue(name, out var border))
                settings.spriteBorder = border;
            if (Pivots().TryGetValue(name, out var pivot))
            {
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = pivot;
            }
            else
            {
                settings.spriteAlignment = (int)SpriteAlignment.Center;
            }

            importer.SetTextureSettings(settings);
        }

        private static Dictionary<string, Vector4> Borders()
        {
            var result = new Dictionary<string, Vector4>();
            var json = Read("borders.json");
            if (json == null)
                return result;
            foreach (var p in json.Properties())
            {
                var a = (JArray)p.Value;
                result[p.Name] = new Vector4((float)a[0], (float)a[1], (float)a[2], (float)a[3]);
            }

            return result;
        }

        private static Dictionary<string, Vector2> Pivots()
        {
            var result = new Dictionary<string, Vector2>();
            var json = Read("wheel.json");
            if (json == null)
                return result;
            foreach (var p in json.Properties())
            {
                if (p.Value is JArray a && a.Count == 2)
                    result[p.Name] = new Vector2((float)a[0], (float)a[1]);
            }

            return result;
        }

        private static JObject Read(string file)
        {
            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "Tools", "UI", file);
            return File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : null;
        }
    }
}
