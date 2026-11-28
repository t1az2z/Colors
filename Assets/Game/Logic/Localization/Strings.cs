using System.Collections.Generic;
using GameCore.Config;

namespace ColorsAction.Localization
{
    /// <summary>
    /// Строки игры на текущем языке: <c>Configs/Localization/&lt;язык&gt;.json</c> (<see cref="LocalizationConfig"/>),
    /// недостающие — из языка <see cref="LocalizationConfig.Fallback"/>, совсем нет — сам ключ.
    /// Кнопки в тексте (<c>{action:id}</c>) остаются разметкой: их рисует интерфейс под текущее устройство.
    /// </summary>
    public sealed class Strings
    {
        public const string DefaultLanguage = "ru";

        private readonly List<LocalizationConfig> chain = new List<LocalizationConfig>();

        public Strings(LocalizationConfig primary, LocalizationConfig fallback = null)
        {
            if (primary != null)
                chain.Add(primary);
            if (fallback != null && fallback != primary)
                chain.Add(fallback);
        }

        /// <summary>Код текущего языка.</summary>
        public string Language => chain.Count > 0 ? chain[0].Language : string.Empty;

        /// <summary>Загрузить язык (и его запасной). Файла нет — пробуется <see cref="DefaultLanguage"/>, иначе пустая таблица.</summary>
        public static Strings Load(ConfigService configs, string language)
        {
            if (string.IsNullOrEmpty(language))
                language = DefaultLanguage;
            if (!configs.TryLoad<LocalizationConfig>(LocalizationConfig.Folder + language, out var primary)
                && language != DefaultLanguage)
                configs.TryLoad(LocalizationConfig.Folder + DefaultLanguage, out primary);
            LocalizationConfig fallback = null;
            if (primary != null && !string.IsNullOrEmpty(primary.Fallback) && primary.Fallback != primary.Language)
                configs.TryLoad(LocalizationConfig.Folder + primary.Fallback, out fallback);
            return new Strings(primary, fallback);
        }

        public bool Has(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            foreach (var table in chain)
                if (table.Strings.ContainsKey(key))
                    return true;
            return false;
        }

        /// <summary>Текст по ключу; ключа нет — сам ключ.</summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;
            foreach (var table in chain)
                if (table.Strings.TryGetValue(key, out var value) && value != null)
                    return value;
            return key;
        }

        /// <summary>Текст по ключу с аргументами <c>{0}</c>, <c>{1}</c>…</summary>
        public string Format(string key, params object[] args) => PromptText.Substitute(Get(key), args);

        public string Get(LocText text) => text.IsEmpty ? string.Empty : PromptText.Substitute(Get(text.Key), text.Args);

        /// <summary>
        /// Подпись кнопки по пути Input System (<c>&lt;Keyboard&gt;/space</c>). Сначала — для семейства геймпадов
        /// (<c>"ps:&lt;Gamepad&gt;/buttonSouth"</c>), потом общая; нет — null (интерфейс возьмёт имя из Input System).
        /// </summary>
        public string ControlLabel(string controlPath, string family = null)
        {
            if (string.IsNullOrEmpty(controlPath))
                return null;
            if (!string.IsNullOrEmpty(family))
            {
                var familyKey = family + ":" + controlPath;
                foreach (var table in chain)
                    if (table.Controls.TryGetValue(familyKey, out var label) && !string.IsNullOrEmpty(label))
                        return label;
            }

            foreach (var table in chain)
                if (table.Controls.TryGetValue(controlPath, out var label) && !string.IsNullOrEmpty(label))
                    return label;
            return null;
        }
    }
}
