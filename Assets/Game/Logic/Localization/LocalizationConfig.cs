using System.Collections.Generic;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Localization
{
    /// <summary>
    /// Configs/Localization/&lt;язык&gt;.json — строки одного языка (см. <see cref="Strings"/>).
    /// <para><c>strings</c> — ключ → текст. В тексте: <c>{0}</c>, <c>{1}</c>… — аргументы, <c>{action:interact}</c> —
    /// кнопка действия под текущее устройство (иконка или подпись, см. <see cref="PromptText"/>), <c>{{</c> / <c>}}</c> — фигурные скобки.</para>
    /// <para><c>controls</c> — подписи кнопок по пути Input System: <c>"&lt;Keyboard&gt;/space": "Пробел"</c>; для конкретного
    /// семейства геймпадов — с префиксом: <c>"ps:&lt;Gamepad&gt;/buttonSouth": "✕"</c>. Нет подписи — берётся имя из Input System.</para>
    /// </summary>
    [Preserve]
    public sealed class LocalizationConfig : IConfigPostLoad
    {
        /// <summary>Папка файлов языков внутри Configs.</summary>
        public const string Folder = "Localization/";

        /// <summary>Код языка: "ru", "en".</summary>
        public string Language { get; set; }

        /// <summary>Язык, из которого берутся строки, которых нет в этом файле. Пусто — нет.</summary>
        public string Fallback { get; set; }

        public Dictionary<string, string> Strings { get; set; } = new Dictionary<string, string>();

        public Dictionary<string, string> Controls { get; set; } = new Dictionary<string, string>();

        public void OnConfigLoaded()
        {
            Language ??= string.Empty;
            Fallback ??= string.Empty;
            Strings ??= new Dictionary<string, string>();
            Controls ??= new Dictionary<string, string>();
        }
    }
}
