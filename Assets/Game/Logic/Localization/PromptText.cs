using System;
using System.Collections.Generic;
using System.Text;

namespace ColorsAction.Localization
{
    public enum PromptSegmentKind
    {
        /// <summary>Обычный текст.</summary>
        Text,

        /// <summary>Кнопка действия: <see cref="PromptSegment.Value"/> — id действия ("interact", "jump"…).</summary>
        Action
    }

    public readonly struct PromptSegment : IEquatable<PromptSegment>
    {
        public readonly PromptSegmentKind Kind;
        public readonly string Value;

        public PromptSegment(PromptSegmentKind kind, string value)
        {
            Kind = kind;
            Value = value;
        }

        public static PromptSegment Text(string value) => new PromptSegment(PromptSegmentKind.Text, value);

        public static PromptSegment Action(string id) => new PromptSegment(PromptSegmentKind.Action, id);

        public bool Equals(PromptSegment other) => Kind == other.Kind && Value == other.Value;

        public override bool Equals(object obj) => obj is PromptSegment other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ (Value?.GetHashCode() ?? 0);

        public override string ToString() => Kind == PromptSegmentKind.Action ? "{action:" + Value + "}" : Value;
    }

    /// <summary>
    /// Разметка текста подсказок: <c>{0}</c>… — аргументы (<see cref="Substitute"/>), <c>{action:id}</c> — кнопка действия
    /// (<see cref="Parse"/> разбивает текст на куски для отрисовки: текст и кнопки), <c>{{</c> / <c>}}</c> — скобки.
    /// Незнакомые <c>{…}</c> остаются текстом как есть.
    /// </summary>
    public static class PromptText
    {
        public const string ActionPrefix = "action:";

        /// <summary>Подставить аргументы <c>{0}</c>, <c>{1}</c>…; остальная разметка не трогается.</summary>
        public static string Substitute(string template, object[] args)
        {
            if (string.IsNullOrEmpty(template) || args == null || args.Length == 0)
                return template ?? string.Empty;
            var sb = new StringBuilder(template.Length + 16);
            for (int i = 0; i < template.Length; i++)
            {
                var c = template[i];
                if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    sb.Append("{{");
                    i++;
                    continue;
                }

                if (c == '{')
                {
                    var close = template.IndexOf('}', i + 1);
                    if (close > i + 1 && int.TryParse(template.Substring(i + 1, close - i - 1), out var index)
                                      && index >= 0 && index < args.Length)
                    {
                        sb.Append(Convert.ToString(args[index], System.Globalization.CultureInfo.InvariantCulture));
                        i = close;
                        continue;
                    }
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>Разбить текст на куски: текст и кнопки действий. Соседние куски текста склеиваются.</summary>
        public static List<PromptSegment> Parse(string text)
        {
            var result = new List<PromptSegment>();
            if (string.IsNullOrEmpty(text))
                return result;
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if ((c == '{' || c == '}') && i + 1 < text.Length && text[i + 1] == c)
                {
                    sb.Append(c);
                    i++;
                    continue;
                }

                if (c == '{' && string.CompareOrdinal(text, i + 1, ActionPrefix, 0, ActionPrefix.Length) == 0)
                {
                    var close = text.IndexOf('}', i + 1);
                    var start = i + 1 + ActionPrefix.Length;
                    if (close > start)
                    {
                        if (sb.Length > 0)
                        {
                            result.Add(PromptSegment.Text(sb.ToString()));
                            sb.Clear();
                        }

                        result.Add(PromptSegment.Action(text.Substring(start, close - start).Trim()));
                        i = close;
                        continue;
                    }
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                result.Add(PromptSegment.Text(sb.ToString()));
            return result;
        }

        /// <summary>Текст без иконок: кнопки — подписью в квадратных скобках, "[F] взять" (логи, тесты, простые Text).</summary>
        public static string Plain(string text, Func<string, string> actionLabel)
        {
            var sb = new StringBuilder();
            foreach (var segment in Parse(text))
            {
                if (segment.Kind == PromptSegmentKind.Text)
                    sb.Append(segment.Value);
                else
                    sb.Append('[').Append(actionLabel != null ? actionLabel(segment.Value) : segment.Value).Append(']');
            }

            return sb.ToString();
        }
    }
}
