using System;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    /// <summary>Как цвет оружия лёг на цвет цели: по нему HUD выбирает вид цифры урона.</summary>
    public enum ColorEffect
    {
        /// <summary>Множитель ×1: цвет не на круге или множители ровные.</summary>
        Neutral,

        /// <summary>Урон ниже обычного (свой цвет, сосед).</summary>
        Weak,

        /// <summary>Урон выше обычного, но не максимальный (через один).</summary>
        Strong,

        /// <summary>Максимальный множитель: цвет напротив на круге.</summary>
        Antipode
    }

    /// <summary>Результат попадания: итоговый урон, множитель цвета и его вид.</summary>
    public readonly struct HitResult
    {
        public readonly float Damage;
        public readonly float ColorMultiplier;
        public readonly ColorEffect Effect;

        public HitResult(float damage, float colorMultiplier, ColorEffect effect)
        {
            Damage = damage;
            ColorMultiplier = colorMultiplier;
            Effect = effect;
        }

        /// <summary>Цвет усилил удар (через один или напротив).</summary>
        public bool ColorBonus => Effect == ColorEffect.Strong || Effect == ColorEffect.Antipode;

        public HitResult WithDamage(float damage) => new HitResult(damage, ColorMultiplier, Effect);
    }

    /// <summary>Правила урона: базовый урон оружия × множитель удара × множитель цвета по кругу.</summary>
    public sealed class DamageRules
    {
        private readonly ColorWheel wheel;

        public DamageRules(CombatConfig config)
        {
            wheel = new ColorWheel(config ?? throw new ArgumentNullException(nameof(config)));
        }

        public ColorWheel Wheel => wheel;

        public HitResult Calculate(ItemConfig weapon, AttackStepConfig step, string targetColor)
        {
            if (weapon == null)
                throw new ArgumentNullException(nameof(weapon));
            return Apply(weapon.Damage * (step?.DamageMultiplier ?? 1f), weapon.Color, targetColor);
        }

        /// <summary>Урон <paramref name="baseDamage"/> цветом <paramref name="weaponColor"/> по цели цвета <paramref name="targetColor"/>.</summary>
        public HitResult Apply(float baseDamage, string weaponColor, string targetColor)
        {
            var multiplier = wheel.Multiplier(weaponColor, targetColor);
            return new HitResult(baseDamage * multiplier, multiplier, wheel.Effect(multiplier));
        }
    }
}
