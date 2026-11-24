using System.Collections.Generic;
using ColorsAction.Configs;
using GameCore.Config;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>Цвета игры из Configs/Colors.json: id цвета ("blue") → Color.</summary>
    public sealed class Palette
    {
        private readonly ConfigService configs;
        private readonly Dictionary<string, Color> cache = new Dictionary<string, Color>();

        public Palette(ConfigService configs)
        {
            this.configs = configs;
        }

        public Color Get(string id)
        {
            if (string.IsNullOrEmpty(id))
                return Color.gray;
            if (cache.TryGetValue(id, out var color))
                return color;
            var table = configs.LoadTable<ColorConfig>(ConfigKeys.Colors);
            color = table.TryGet(id, out var entry) ? Parse(entry.Hex) : Color.magenta;
            cache[id] = color;
            return color;
        }

        public static Color Parse(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }
    }
}
