using System.Collections.Generic;
using ColorsAction.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>Вид строки подсказки: шрифт текста и цвета клавиш.</summary>
    public struct PromptStyle
    {
        public Font Font;
        public int FontSize;
        public FontStyle FontStyle;
        public Color TextColor;
        public TextAnchor Alignment;

        /// <summary>Фон клавиши и кнопок мыши.</summary>
        public Color KeyColor;

        /// <summary>Подпись на клавише.</summary>
        public Color KeyTextColor;

        /// <summary>Фон кнопок геймпада без своего цвета (LB, RT, D-pad, кнопки PlayStation).</summary>
        public Color PadColor;

        public Color PadTextColor;

        /// <summary>Скопировать с Text текст-обводку (Outline / Shadow), если есть.</summary>
        public Shadow Effect;

        public static PromptStyle From(Text text) => new PromptStyle
        {
            Font = text.font,
            FontSize = text.fontSize,
            FontStyle = text.fontStyle,
            TextColor = text.color,
            Alignment = text.alignment,
            KeyColor = new Color(0.93f, 0.93f, 0.9f, 0.95f),
            KeyTextColor = new Color(0.16f, 0.16f, 0.19f, 1f),
            PadColor = new Color(0.2f, 0.2f, 0.23f, 0.95f),
            PadTextColor = new Color(0.95f, 0.95f, 0.95f, 1f),
            Effect = text.GetComponent<Shadow>()
        };
    }

    /// <summary>
    /// Строка подсказки с кнопками: текст из файла языка с разметкой <c>{action:id}</c> (<see cref="PromptText"/>) —
    /// кнопки рисуются иконкой или клавишей с подписью под текущее устройство (<see cref="InputGlyphs"/>) и сами
    /// перерисовываются при смене клавиатуры на геймпад и обратно. Работает и в экранном Canvas, и в мировом (подсказки на полу).
    /// <para>Вёрстка — ряды друг под другом (перенос строки <c>\n</c> в тексте начинает новый ряд), в каждом ряду
    /// по горизонтали куски текста (uGUI <see cref="Text"/>) и клавиши (<see cref="Image"/> + подпись).</para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class PromptLine : MonoBehaviour
    {
        private InputGlyphs glyphs;
        private PromptStyle style;
        private CanvasGroup group;
        private VerticalLayoutGroup layout;
        private string text = string.Empty;
        private bool builtForGamepad;
        private string builtFamily;
        private bool built;
        private readonly List<GameObject> parts = new List<GameObject>();
        private readonly List<GameObject> rows = new List<GameObject>();

        /// <summary>Текущий текст с разметкой (уже переведённый).</summary>
        public string Text => text;

        /// <summary>Прозрачность всей строки (текст и клавиши).</summary>
        public float Alpha
        {
            get => group.alpha;
            set => group.alpha = value;
        }

        /// <summary>Создать строку дочерним объектом <paramref name="parent"/> с размером <paramref name="size"/>.</summary>
        public static PromptLine Create(Transform parent, string name, Vector2 size, PromptStyle style, InputGlyphs glyphs)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            var line = go.AddComponent<PromptLine>();
            line.Init(style, glyphs);
            return line;
        }

        /// <summary>
        /// Заменить обычный Text строкой с кнопками: та же позиция, размер и шрифт; сам Text прячется
        /// (его ссылки в префабах остаются, но он больше не рисуется).
        /// </summary>
        public static PromptLine Replace(Text source, InputGlyphs glyphs)
        {
            var existing = source.transform.parent != null ? source.transform.parent.Find(source.name + "_Prompt") : null;
            if (existing != null && existing.TryGetComponent<PromptLine>(out var found))
                return found;
            var src = source.rectTransform;
            var go = new GameObject(source.name + "_Prompt", typeof(RectTransform));
            go.transform.SetParent(src.parent, false);
            go.transform.SetSiblingIndex(src.GetSiblingIndex() + 1);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = src.anchorMin;
            rect.anchorMax = src.anchorMax;
            rect.pivot = src.pivot;
            rect.anchoredPosition = src.anchoredPosition;
            rect.sizeDelta = src.sizeDelta;
            rect.localRotation = src.localRotation;
            rect.localScale = src.localScale;
            var line = go.AddComponent<PromptLine>();
            line.Init(PromptStyle.From(source), glyphs);
            source.enabled = false;
            return line;
        }

        public void Init(PromptStyle promptStyle, InputGlyphs inputGlyphs)
        {
            style = promptStyle;
            if (style.Font == null)
                style.Font = UiFactory.Font;
            glyphs = inputGlyphs;
            if (!TryGetComponent(out group))
                group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            if (!TryGetComponent(out layout))
                layout = gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = style.Alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = Mathf.Round(style.FontSize * 0.1f);
            // Строка растёт под содержимое вокруг своего pivot (у Text префаба рамка может быть меньше текста).
            if (!TryGetComponent<ContentSizeFitter>(out var fitter))
                fitter = gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            built = false;
            Rebuild();
        }

        /// <summary>Показать текст (с разметкой <c>{action:id}</c>). Тот же текст — ничего не перестраивается.</summary>
        public void SetText(string value)
        {
            value ??= string.Empty;
            if (built && value == text)
                return;
            text = value;
            Rebuild();
        }

        /// <summary>Цвет текста (клавиши не меняются).</summary>
        public void SetTextColor(Color color)
        {
            style.TextColor = color;
            foreach (var part in parts)
                if (part != null && part.name == "Text" && part.TryGetComponent<Text>(out var t))
                    t.color = color;
        }

        private void Update()
        {
            if (glyphs == null)
                return;
            var gamepad = glyphs.UsingGamepad;
            var family = gamepad ? glyphs.Input.GamepadFamily : null;
            if (gamepad != builtForGamepad || family != builtFamily)
                Rebuild();
        }

        private void Rebuild()
        {
            foreach (var old in rows)
                if (old != null)
                {
                    old.SetActive(false); // Destroy — в конце кадра, а вёрстка до этого не должна учитывать старые ряды
                    Destroy(old);
                }
            rows.Clear();
            parts.Clear();
            builtForGamepad = glyphs != null && glyphs.UsingGamepad;
            builtFamily = builtForGamepad ? glyphs.Input.GamepadFamily : null;
            built = true;

            var row = NewRow();
            foreach (var segment in PromptText.Parse(text.Replace("\r", string.Empty)))
            {
                if (segment.Kind == PromptSegmentKind.Text)
                {
                    var lines = segment.Value.Split('\n');
                    for (int l = 0; l < lines.Length; l++)
                    {
                        if (l > 0)
                            row = NewRow();
                        var value = lines[l].Trim();
                        if (value.Length > 0)
                            parts.Add(TextPart(row, value));
                    }

                    continue;
                }

                var keys = glyphs != null ? glyphs.Resolve(segment.Value) : new List<InputGlyph> { new InputGlyph(segment.Value, null, GlyphShape.Key, null) };
                // Несколько кнопок одного действия ("Q / E") — через косую черту, клавиши WASD — подряд.
                var separated = keys.Count > 1 && keys.Count <= 2;
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0 && separated)
                        parts.Add(TextPart(row, "/"));
                    parts.Add(KeyPart(row, keys[i]));
                }
            }
        }

        /// <summary>Новый ряд строки: куски текста и клавиши в нём идут по горизонтали.</summary>
        private Transform NewRow()
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var row = go.AddComponent<HorizontalLayoutGroup>();
            row.childAlignment = style.Alignment;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            row.spacing = Mathf.Max(2f, style.FontSize * 0.25f);
            rows.Add(go);
            return go.transform;
        }

        private GameObject TextPart(Transform row, string value)
        {
            var t = UiFactory.Text(row, "Text", value, style.FontSize, TextAnchor.MiddleCenter, style.TextColor);
            t.font = style.Font;
            t.fontStyle = style.FontStyle;
            CopyEffect(t.gameObject);
            return t.gameObject;
        }

        private GameObject KeyPart(Transform row, InputGlyph glyph)
        {
            var height = Mathf.Round(style.FontSize * 1.3f);
            var go = new GameObject("Key", typeof(RectTransform));
            go.transform.SetParent(row, false);
            var element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;

            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            if (glyph.Icon != null)
            {
                // Кнопки из атласа: рамка занимает часть картинки, поэтому они крупнее строки текста.
                height = Mathf.Round(style.FontSize * 1.65f);
                element.preferredHeight = element.minHeight = height;
                image.sprite = glyph.Icon;
                image.preserveAspect = true;
                var aspect = glyph.Icon.rect.height > 0 ? glyph.Icon.rect.width / glyph.Icon.rect.height : 1f;
                element.preferredWidth = element.minWidth = height * aspect;
                return go;
            }

            var round = glyph.Shape == GlyphShape.FaceButton;
            var pad = glyph.Shape == GlyphShape.FaceButton || glyph.Shape == GlyphShape.PadButton;
            // Пустая клавиша / кнопка из атласа кнопок, подпись поверх; нет атласа — плашка, нарисованная кодом.
            var blank = glyphs == null ? null : round ? glyphs.BlankPad : glyphs.BlankKey;
            if (blank != null)
            {
                height = Mathf.Round(style.FontSize * 1.65f);
                element.preferredHeight = element.minHeight = height;
                image.sprite = blank;
                image.type = round ? Image.Type.Simple : Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = blank.rect.height / height;
                image.color = Color.white;
                var text = round ? new Color(0.95f, 0.9f, 0.92f, 1f) : new Color(0.2f, 0.13f, 0.22f, 1f);
                var size = Mathf.RoundToInt(style.FontSize * (glyph.Label.Length > 2 ? 0.62f : 0.75f));
                var caption = UiFactory.Text(go.transform, "Label", glyph.Label, size, TextAnchor.MiddleCenter, text);
                caption.font = style.Font;
                caption.fontStyle = FontStyle.Bold;
                UiFactory.Stretch(caption.rectTransform);
                caption.rectTransform.offsetMin = new Vector2(0, height * 0.06f);
                var aspect = blank.rect.width / blank.rect.height;
                element.preferredWidth = element.minWidth = round ? height * aspect : Mathf.Max(height * aspect, caption.preferredWidth + style.FontSize * 0.9f);
                return go;
            }

            UiSprites.Apply(image, round ? UiSprites.Circle : UiSprites.Rounded, !round);
            image.color = glyph.Tint ?? (pad ? style.PadColor : style.KeyColor);

            var labelSize = Mathf.RoundToInt(style.FontSize * (glyph.Label.Length > 2 ? 0.72f : 0.85f));
            var label = UiFactory.Text(go.transform, "Label", glyph.Label, labelSize, TextAnchor.MiddleCenter,
                glyph.Tint.HasValue ? Color.white : pad ? style.PadTextColor : style.KeyTextColor);
            label.font = style.Font;
            label.fontStyle = FontStyle.Bold;
            UiFactory.Stretch(label.rectTransform);
            element.preferredWidth = element.minWidth = round ? height : Mathf.Max(height, label.preferredWidth + style.FontSize * 0.6f);
            return go;
        }

        private void CopyEffect(GameObject target)
        {
            if (style.Effect == null)
                return;
            var effect = style.Effect is Outline ? target.AddComponent<Outline>() : target.AddComponent<Shadow>();
            effect.effectColor = style.Effect.effectColor;
            effect.effectDistance = style.Effect.effectDistance;
            effect.useGraphicAlpha = style.Effect.useGraphicAlpha;
        }
    }
}
