using System;
using System.Collections.Generic;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    /// <summary>
    /// Круг цветов (<see cref="CombatConfig.ColorWheel"/>): положение цвета, расстояние между цветами по кругу
    /// и множитель урона по расстоянию (<see cref="CombatConfig.ColorDistanceMultipliers"/>).
    /// </summary>
    public sealed class ColorWheel
    {
        private readonly List<string> order;
        private readonly List<float> multipliers;
        private readonly float max;
        private readonly float min;

        public ColorWheel(CombatConfig config)
        {
            order = config.ColorWheel ?? new List<string>();
            multipliers = config.ColorDistanceMultipliers ?? new List<float>();
            max = 1f;
            min = 1f;
            foreach (var m in multipliers)
            {
                max = Math.Max(max, m);
                min = Math.Min(min, m);
            }
        }

        /// <summary>Цвета по часовой стрелке.</summary>
        public IReadOnlyList<string> Colors => order;

        public int Count => order.Count;

        /// <summary>Позиция цвета на круге, -1 — цвета на круге нет.</summary>
        public int IndexOf(string color)
        {
            if (string.IsNullOrEmpty(color))
                return -1;
            for (int i = 0; i < order.Count; i++)
                if (string.Equals(order[i], color, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        /// <summary>Шагов по кругу между цветами (0..Count/2), -1 — хотя бы одного цвета нет на круге.</summary>
        public int Distance(string a, string b)
        {
            var i = IndexOf(a);
            var j = IndexOf(b);
            if (i < 0 || j < 0)
                return -1;
            var d = Math.Abs(i - j);
            return Math.Min(d, order.Count - d);
        }

        /// <summary>Цвет напротив на круге (null — цвета на круге нет).</summary>
        public string Opposite(string color)
        {
            var i = IndexOf(color);
            return i < 0 || order.Count == 0 ? null : order[(i + order.Count / 2) % order.Count];
        }

        /// <summary>Множитель урона оружием цвета <paramref name="weapon"/> по цели цвета <paramref name="target"/>.</summary>
        public float Multiplier(string weapon, string target)
        {
            var d = Distance(weapon, target);
            if (d < 0 || multipliers.Count == 0)
                return 1f;
            return multipliers[Math.Min(d, multipliers.Count - 1)];
        }

        /// <summary>Вид попадания по множителю: максимальный — антипод, выше 1 — сильный, ниже 1 — слабый.</summary>
        public ColorEffect Effect(float multiplier)
        {
            const float eps = 1e-4f;
            if (max > 1f + eps && multiplier >= max - eps)
                return ColorEffect.Antipode;
            if (multiplier > 1f + eps)
                return ColorEffect.Strong;
            if (multiplier < 1f - eps)
                return ColorEffect.Weak;
            return ColorEffect.Neutral;
        }
    }
}
