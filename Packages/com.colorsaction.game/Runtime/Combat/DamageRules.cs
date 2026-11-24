using System;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    /// <summary>Результат попадания: итоговый урон и сработал ли бонус цвета.</summary>
    public readonly struct HitResult
    {
        public readonly float Damage;
        public readonly bool ColorBonus;

        public HitResult(float damage, bool colorBonus)
        {
            Damage = damage;
            ColorBonus = colorBonus;
        }
    }

    /// <summary>Правила урона: базовый урон оружия × множитель удара × бонус цвета.</summary>
    public sealed class DamageRules
    {
        private readonly CombatConfig config;

        public DamageRules(CombatConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public HitResult Calculate(ItemConfig weapon, AttackStepConfig step, string targetColor)
        {
            if (weapon == null)
                throw new ArgumentNullException(nameof(weapon));
            var damage = weapon.Damage * (step?.DamageMultiplier ?? 1f);
            var bonus = !string.IsNullOrEmpty(weapon.Color) && string.Equals(weapon.Color, targetColor, StringComparison.OrdinalIgnoreCase);
            if (bonus)
                damage *= config.SameColorMultiplier;
            return new HitResult(damage, bonus);
        }
    }
}
