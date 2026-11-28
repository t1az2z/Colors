using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Подсказка, "выгравированная" на полу: проявляется, когда игрок рядом; кнопка подстраивается под устройство ввода
    /// (текст — ключ <c>hint.&lt;действие&gt;</c> в файле языка, кнопка — <see cref="PromptLine"/>).
    /// Подсказка предмета или двери (<see cref="BindTargets"/>) гаснет навсегда, когда предмет подобран / дверь открыта.
    /// </summary>
    public sealed class HintMarker : MonoBehaviour
    {
        private const float ShowDistance = 4.5f;

        /// <summary>Ключ текста "открыть" для подсказки взаимодействия, привязанной только к дверям.</summary>
        public const string OpenKey = "hint.interact.open";

        private string action;
        private InputGlyphs glyphs;
        private PromptLine line;
        private Transform player;
        private string textKey;
        private System.Func<bool> done;
        private System.Action completed;
        private bool finished;

        /// <summary>Ключ текста подсказки действия в файле языка: "hint.jump", "hint.interact"…</summary>
        public static string KeyFor(string action) => "hint." + action;

        /// <summary>
        /// Привязать подсказку к делу (подобрать предмет, открыть дверь): когда <paramref name="isDone"/> вернёт true,
        /// подсказка гаснет навсегда и вызывает <paramref name="onDone"/> (запомнить в сессии).
        /// <paramref name="key"/> — другой ключ текста (например <see cref="OpenKey"/>).
        /// </summary>
        public void BindTargets(System.Func<bool> isDone, System.Action onDone, string key = null)
        {
            done = isDone;
            completed = onDone;
            if (!string.IsNullOrEmpty(key))
                textKey = key;
            Refresh();
        }

        public static HintMarker Create(Transform parent, Vector3 position, string action, InputGlyphs glyphs, Font font)
        {
            var go = new GameObject($"Hint_{action}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(400, 120);
            rect.position = position + Vector3.up * 0.02f;
            rect.rotation = Quaternion.Euler(90f, 0f, 0f);
            rect.localScale = Vector3.one * 0.006f;

            var marker = go.AddComponent<HintMarker>();
            marker.action = action;
            marker.glyphs = glyphs;
            marker.textKey = KeyFor(action);

            // "Выгравировано" на полу: текст и клавиши — тёмно-серые, подписи клавиш — светлые.
            var ink = new Color(0.32f, 0.33f, 0.37f, 1f);
            var style = new PromptStyle
            {
                Font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),
                FontSize = 64,
                FontStyle = FontStyle.Bold,
                TextColor = ink,
                Alignment = TextAnchor.MiddleCenter,
                KeyColor = ink,
                KeyTextColor = new Color(0.93f, 0.93f, 0.92f, 1f),
                PadColor = ink,
                PadTextColor = new Color(0.93f, 0.93f, 0.92f, 1f)
            };
            marker.line = PromptLine.Create(go.transform, "Text", rect.sizeDelta, style, glyphs);
            UiFactory.Stretch((RectTransform)marker.line.transform);
            marker.line.Alpha = 0f;
            marker.Refresh();
            return marker;
        }

        private void Refresh()
        {
            var strings = glyphs?.Strings;
            // Нет строки для действия — хотя бы кнопка.
            var text = strings != null && strings.Has(textKey) ? strings.Get(textKey) : "{action:" + action + "}";
            line.SetText(text);
        }

        private void Update()
        {
            if (player == null)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                if (pc == null)
                    return;
                player = pc.transform;
            }

            if (!finished && done != null && done())
            {
                finished = true;
                completed?.Invoke();
            }

            var d = Vector3.Distance(player.position, transform.position);
            var target = finished ? 0f : Mathf.Clamp01((ShowDistance - d) / 1.5f);
            line.Alpha = Mathf.MoveTowards(line.Alpha, target, Time.deltaTime * 2f);
            // Дело сделано и подсказка погасла — больше не появится.
            if (finished && line.Alpha <= 0f)
                gameObject.SetActive(false);
        }
    }
}
