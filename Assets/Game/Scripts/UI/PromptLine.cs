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
    /// <para>Вёрстка — горизонтальный ряд: куски текста (uGUI <see cref="Text"/>) и клавиши (<see cref="Image"/> + подпись).</para>
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class PromptLine : MonoBehaviour
    {
        private InputGlyphs glyphs;
        private PromptStyle style;
        private CanvasGroup group;
        private HorizontalLayoutGroup layout;
        private string text = string.Empty;
        private bool builtForGamepad;
        private string builtFamily;
        private bool built;
        private readonly List<GameObject> parts = new List<GameObject>();

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
                layout = gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = style.Alignment;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = Mathf.Max(2f, style.FontSize * 0.25f);
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
            foreach (var part in parts)
                if (part != null)
                    Destroy(part);
            parts.Clear();
            builtForGamepad = glyphs != null && glyphs.UsingGamepad;
            builtFamily = builtForGamepad ? glyphs.Input.GamepadFamily : null;
            built = true;

            foreach (var segment in PromptText.Parse(text))
            {
                if (segment.Kind == PromptSegmentKind.Text)
                {
                    var value = segment.Value.Trim();
                    if (value.Length > 0)
                        parts.Add(TextPart(value));
                    continue;
                }

                var keys = glyphs != null ? glyphs.Resolve(segment.Value) : new List<InputGlyph> { new InputGlyph(segment.Value, null, GlyphShape.Key, null) };
                // Несколько кнопок одного действия ("Q / E") — через косую черту, клавиши WASD — подряд.
                var separated = keys.Count > 1 && keys.Count <= 2;
                for (int i = 0; i < keys.Count; i++)
                {
                    if (i > 0 && separated)
                        parts.Add(TextPart("/"));
                    parts.Add(KeyPart(keys[i]));
                }
            }
        }

        private GameObject TextPart(string value)
        {
            var t = UiFactory.Text(transform, "Text", value, style.FontSize, TextAnchor.MiddleCenter, style.TextColor);
            t.font = style.Font;
            t.fontStyle = style.FontStyle;
            CopyEffect(t.gameObject);
            return t.gameObject;
        }

        private GameObject KeyPart(InputGlyph glyph)
        {
            var height = Mathf.Round(style.FontSize * 1.3f);
            var go = new GameObject("Key", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var element = go.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;

            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            if (glyph.Icon != null)
            {
                image.sprite = glyph.Icon;
                image.preserveAspect = true;
                var aspect = glyph.Icon.rect.height > 0 ? glyph.Icon.rect.width / glyph.Icon.rect.height : 1f;
                element.preferredWidth = height * aspect;
                return go;
            }

            var round = glyph.Shape == GlyphShape.FaceButton;
            UiSprites.Apply(image, round ? UiSprites.Circle : UiSprites.Rounded, !round);
            var pad = glyph.Shape == GlyphShape.FaceButton || glyph.Shape == GlyphShape.PadButton;
            image.color = glyph.Tint ?? (pad ? style.PadColor : style.KeyColor);

            var labelSize = Mathf.RoundToInt(style.FontSize * (glyph.Label.Length > 2 ? 0.72f : 0.85f));
            var label = UiFactory.Text(go.transform, "Label", glyph.Label, labelSize, TextAnchor.MiddleCenter,
                glyph.Tint.HasValue ? Color.white : pad ? style.PadTextColor : style.KeyTextColor);
            label.font = style.Font;
            label.fontStyle = FontStyle.Bold;
            UiFactory.Stretch(label.rectTransform);
            element.preferredWidth = round ? height : Mathf.Max(height, label.preferredWidth + style.FontSize * 0.6f);
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
