using System.Collections.Generic;
using ColorsAction.Localization;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ColorsAction.Unity
{
    /// <summary>Как рисовать кнопку: клавиша, кнопка мыши, круглая кнопка геймпада, прочие кнопки геймпада.</summary>
    public enum GlyphShape
    {
        Key,
        Mouse,
        FaceButton,
        PadButton
    }

    /// <summary>Одна кнопка для подсказки: подпись, иконка (если есть спрайт) и форма.</summary>
    public readonly struct InputGlyph
    {
        public readonly string Label;
        public readonly Sprite Icon;
        public readonly GlyphShape Shape;

        /// <summary>Цвет круглой кнопки геймпада (A зелёная, B красная…); null — цвет стиля.</summary>
        public readonly Color? Tint;

        public InputGlyph(string label, Sprite icon, GlyphShape shape, Color? tint)
        {
            Label = label;
            Icon = icon;
            Shape = shape;
            Tint = tint;
        }
    }

    /// <summary>
    /// Кнопки действий под текущее устройство (<see cref="GameInput.UsingGamepad"/>): что показать на месте
    /// <c>{action:id}</c> в тексте подсказки. Пути кнопок — из привязок <see cref="GameInput"/> (переназначение подхватится само),
    /// подписи — <c>controls</c> в файле языка (<see cref="Strings.ControlLabel"/>), иначе имя из Input System.
    /// <para>Иконки: спрайт <c>Resources/UI/InputGlyphs/&lt;семейство&gt;/&lt;кнопка&gt;</c> (семейство — keyboard, mouse, xbox, ps;
    /// кнопка — конец пути с <c>_</c> вместо <c>/</c>: <c>buttonSouth</c>, <c>dpad_up</c>, <c>leftButton</c>). Нет спрайта —
    /// интерфейс рисует клавишу с подписью.</para>
    /// </summary>
    public sealed class InputGlyphs
    {
        private const string IconFolder = "UI/InputGlyphs/";

        private readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

        public InputGlyphs(GameInput input, Strings strings)
        {
            Input = input;
            Strings = strings;
        }

        public GameInput Input { get; }

        public Strings Strings { get; }

        public bool UsingGamepad => Input != null && Input.UsingGamepad;

        /// <summary>Кнопки действия для текущего устройства (у составных — несколько, у WASD — одна на клавишу).</summary>
        public List<InputGlyph> Resolve(string actionId) => Resolve(actionId, UsingGamepad);

        public List<InputGlyph> Resolve(string actionId, bool gamepad)
        {
            var result = new List<InputGlyph>();
            if (Input == null)
            {
                result.Add(new InputGlyph(actionId, null, GlyphShape.Key, null));
                return result;
            }

            var family = gamepad ? Input.GamepadFamily : null;
            foreach (var action in Input.ActionsFor(actionId))
                foreach (var path in Input.ControlPaths(action, gamepad))
                    result.Add(Glyph(path, family));
            // У действия нет кнопки на этом устройстве (F1 — только клавиатура): показать кнопку другого устройства.
            if (result.Count == 0)
            {
                var other = gamepad ? null : Input.GamepadFamily;
                foreach (var action in Input.ActionsFor(actionId))
                    foreach (var path in Input.ControlPaths(action, !gamepad))
                        result.Add(Glyph(path, other));
            }

            if (result.Count == 0)
                result.Add(new InputGlyph(actionId, null, GlyphShape.Key, null));
            return result;
        }

        /// <summary>Подпись действия одной строкой ("F", "Q / E", "LB / RB") — для мест без иконок.</summary>
        public string Label(string actionId)
        {
            var glyphs = Resolve(actionId);
            var parts = new string[glyphs.Count];
            for (int i = 0; i < glyphs.Count; i++)
                parts[i] = glyphs[i].Label;
            return string.Join(" / ", parts);
        }

        /// <summary>Текст подсказки без иконок: кнопки — "[F]".</summary>
        public string Plain(string text) => PromptText.Plain(text, Label);

        private InputGlyph Glyph(string path, string family)
        {
            var label = Strings?.ControlLabel(path, family)
                        ?? InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
            var device = path.StartsWith("<Keyboard>") ? "keyboard" : path.StartsWith("<Mouse>") ? "mouse" : family ?? "xbox";
            var control = path.Substring(path.IndexOf('/') + 1).Replace('/', '_');
            var icon = Icon(device + "/" + control);

            if (device == "keyboard")
                return new InputGlyph(label, icon, GlyphShape.Key, null);
            if (device == "mouse")
                return new InputGlyph(label, icon, GlyphShape.Mouse, null);
            var tint = FaceTint(control, family);
            return new InputGlyph(label, icon, tint.HasValue || control.StartsWith("button") ? GlyphShape.FaceButton : GlyphShape.PadButton, tint);
        }

        private Sprite Icon(string name)
        {
            if (!icons.TryGetValue(name, out var sprite))
            {
                sprite = Resources.Load<Sprite>(IconFolder + name);
                icons[name] = sprite;
            }

            return sprite;
        }

        /// <summary>Цвета кнопок Xbox: A зелёная, B красная, X синяя, Y жёлтая; у PlayStation кнопки тёмные (символ — подпись).</summary>
        private static Color? FaceTint(string control, string family)
        {
            if (family == "ps")
                return null;
            switch (control)
            {
                case "buttonSouth": return new Color(0.36f, 0.72f, 0.28f);
                case "buttonEast": return new Color(0.86f, 0.27f, 0.25f);
                case "buttonWest": return new Color(0.22f, 0.5f, 0.9f);
                case "buttonNorth": return new Color(0.93f, 0.72f, 0.16f);
                default: return null;
            }
        }
    }
}
