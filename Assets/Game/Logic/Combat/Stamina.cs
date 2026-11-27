using System;
using ColorsAction.Configs;
using GameCore.Ticking;
using GameCore.UI;

namespace ColorsAction.Combat
{
    /// <summary>
    /// Выносливость — основной ресурс. Тратится сразу при действии (атака, блок, перекат),
    /// восстанавливается медленно и только после паузы <see cref="StaminaConfig.RegenDelay"/>.
    /// Действие разрешено, пока выносливость больше нуля (как в Dark Souls): последнее действие может увести её в ноль.
    /// </summary>
    public sealed class Stamina : ITickable
    {
        private readonly StaminaConfig config;
        private float regenPause;

        public ReactiveProperty<float> Current { get; }
        public float Max => config.Max;
        public float Fraction => config.Max > 0 ? Current.Value / config.Max : 0f;

        /// <summary>Держится блок: восстановление замедлено.</summary>
        public bool Blocking { get; set; }

        public Stamina(StaminaConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            Current = new ReactiveProperty<float>(config.Max);
        }

        public bool CanSpend => Current.Value > 0f;

        /// <summary>Списывает cost, если выносливость больше нуля. Возвращает, разрешено ли действие.</summary>
        public bool TrySpend(float cost)
        {
            if (cost <= 0f)
                return true;
            if (!CanSpend)
                return false;
            Current.Value = Math.Max(0f, Current.Value - cost);
            regenPause = config.RegenDelay;
            return true;
        }

        public void Restore() => Current.Value = config.Max;

        /// <summary>Вернуть потраченное (успешное парирование не тратит выносливость), не больше максимума.</summary>
        public void Refund(float amount)
        {
            if (amount > 0f)
                Current.Value = Math.Min(config.Max, Current.Value + amount);
        }

        public void Tick(float deltaTime)
        {
            if (regenPause > 0f)
            {
                regenPause -= deltaTime;
                return;
            }

            if (Current.Value >= config.Max)
                return;
            var rate = config.RegenPerSecond * (Blocking ? config.BlockRegenMultiplier : 1f);
            Current.Value = Math.Min(config.Max, Current.Value + rate * deltaTime);
        }
    }
}
