using System.Collections.Generic;
using ColorsAction.Configs;
using ColorsAction.Status;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Подсветка носителя статус-эффектами: у каждого эффекта свой цвет и пульс (<see cref="StatusEffectConfig.Tint"/>,
    /// <see cref="StatusEffectConfig.TintStrength"/>, <see cref="StatusEffectConfig.TintPulse"/>). Последние 0.3 с эффект гаснет.
    /// </summary>
    public static class StatusTint
    {
        private static readonly Dictionary<string, Color?> parsed = new Dictionary<string, Color?>();

        /// <summary>Есть ли эффект с подсветкой.</summary>
        public static bool Any(StatusEffects status)
        {
            if (status == null)
                return false;
            foreach (var effect in status.Active)
                if (effect.Config.TintStrength > 0f && Parse(effect.Config.Tint).HasValue)
                    return true;
            return false;
        }

        /// <summary>Цвет подсветки сейчас (rgb) и её сила (a, может быть больше 1). Нет подсветки — прозрачный.</summary>
        public static Color Glow(StatusEffects status, float time)
        {
            if (status == null)
                return Color.clear;
            var sum = Color.clear;
            foreach (var effect in status.Active)
            {
                var c = effect.Config;
                var tint = Parse(c.Tint);
                if (!tint.HasValue || c.TintStrength <= 0f)
                    continue;
                var pulse = c.TintPulse > 0f ? 0.6f + 0.4f * Mathf.Sin(time * c.TintPulse * Mathf.PI * 2f) : 1f;
                var fade = effect.Permanent ? 1f : Mathf.Clamp01(effect.Remaining / 0.3f);
                var k = c.TintStrength * pulse * fade;
                sum.r += tint.Value.r * k;
                sum.g += tint.Value.g * k;
                sum.b += tint.Value.b * k;
                sum.a += k;
            }

            if (sum.a <= 0f)
                return Color.clear;
            return new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a);
        }

        private static Color? Parse(string hex)
        {
            if (string.IsNullOrEmpty(hex))
                return null;
            if (!parsed.TryGetValue(hex, out var color))
            {
                color = ColorUtility.TryParseHtmlString(hex, out var c) ? c : (Color?)null;
                parsed[hex] = color;
            }

            return color;
        }
    }
}
