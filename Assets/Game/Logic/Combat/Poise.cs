using System;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    /// <summary>
    /// Шкала выносливости врага (poise) над головой. Удары и парирования игрока заполняют её одинаково у всех врагов
    /// (<see cref="PoiseConfig"/>), объём у каждого типа свой (<see cref="EnemyConfig.Poise"/>). Заполнилась —
    /// <see cref="Broken"/>: враг ошеломлён, шкала стоит полной, пока <see cref="EnemyBrain"/> не сбросит её
    /// по окончании ошеломления. Без ударов шкала после паузы спадает.
    /// </summary>
    public sealed class Poise
    {
        private readonly PoiseConfig config;
        private float idle;

        public Poise(float max, PoiseConfig config)
        {
            Max = Math.Max(0f, max);
            this.config = config ?? new PoiseConfig();
        }

        /// <summary>Объём шкалы. 0 — шкалы нет, враг не ошеломляется.</summary>
        public float Max { get; }

        public float Current { get; private set; }

        public float Fraction => Max > 0f ? Current / Max : 0f;

        /// <summary>Шкала заполнилась — враг ошеломлён.</summary>
        public bool Broken { get; private set; }

        public PoiseConfig Config => config;

        /// <summary>Заполнить шкалу на <paramref name="amount"/>. Возвращает true, если именно это её заполнило.</summary>
        public bool Add(float amount)
        {
            if (Broken || Max <= 0f || amount <= 0f)
                return false;
            idle = 0f;
            Current = Math.Min(Max, Current + amount);
            if (Current < Max - 0.0001f)
                return false;
            Current = Max;
            Broken = true;
            return true;
        }

        /// <summary>Спад шкалы после <see cref="PoiseConfig.DecayDelay"/> без ударов. Заполненная не спадает.</summary>
        public void Tick(float deltaTime)
        {
            if (Broken || Current <= 0f)
                return;
            idle += deltaTime;
            if (idle >= config.DecayDelay)
                Current = Math.Max(0f, Current - Math.Max(0f, config.DecayRate) * deltaTime);
        }

        /// <summary>Пустая шкала (ошеломление кончилось, враг возрождён).</summary>
        public void Reset()
        {
            Current = 0f;
            Broken = false;
            idle = 0f;
        }

        /// <summary>
        /// Сколько шкалы заполняет удар: <see cref="PoiseConfig.Hit"/> × множитель оружия × множитель удара
        /// × доля (часть пуль залпа дробовика).
        /// </summary>
        public static float HitAmount(PoiseConfig config, ItemConfig weapon, float moveMultiplier = 1f, float share = 1f)
        {
            if (config == null)
                return 0f;
            var weaponMultiplier = weapon != null ? weapon.Poise : 1f;
            return Math.Max(0f, config.Hit * weaponMultiplier * moveMultiplier * share);
        }
    }
}
