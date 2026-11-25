using System;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    /// <summary>Что произошло с зарядом за кадр.</summary>
    public enum ChargeEvent
    {
        None,

        /// <summary>Заряд только что стал полным: вспышка, выносливость уже списана.</summary>
        Charged,

        /// <summary>Заряд закончен (кнопку отпустили, полный заряд передержан или не хватило выносливости): атака идёт дальше.</summary>
        Released
    }

    /// <summary>
    /// Заряд атаки удержанием кнопки — общий механизм для любого оружия (удар серии, заряженный выстрел).
    /// <para>
    /// Порядок: <see cref="Begin"/> в момент, когда атака может начать копить заряд (меч — конец замаха),
    /// затем каждый кадр <see cref="Tick"/> с состоянием кнопки, пока <see cref="Active"/>. Пока заряд активен,
    /// атака стоит на месте. После <see cref="ChargeEvent.Released"/> множители <see cref="DamageMultiplier"/> и
    /// <see cref="KnockbackMultiplier"/> действуют до следующего <see cref="Begin"/>/<see cref="Reset"/>.
    /// </para>
    /// </summary>
    public sealed class AttackCharge
    {
        public ChargeConfig Config { get; private set; }

        /// <summary>Идёт заряд: атака стоит на конце замаха.</summary>
        public bool Active { get; private set; }

        /// <summary>Заряд полный (выносливость списана): удар будет усиленным.</summary>
        public bool Full { get; private set; }

        /// <summary>Сколько секунд копится заряд.</summary>
        public float Time { get; private set; }

        /// <summary>Доля заряда 0..1 (для свечения, дрожи).</summary>
        public float Level => Config == null ? 0f : Config.Time <= 0f ? (Full ? 1f : 0f) : Math.Min(1f, Time / Config.Time);

        public float DamageMultiplier => Full ? Config.DamageMultiplier : 1f;
        public float KnockbackMultiplier => Full ? Config.KnockbackMultiplier : 1f;

        /// <summary>Начать копить заряд. Прежний результат (бонус) сбрасывается.</summary>
        public void Begin(ChargeConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Active = true;
            Full = false;
            Time = 0f;
        }

        /// <param name="held">Кнопка атаки всё ещё зажата.</param>
        /// <param name="trySpend">Списать выносливость за полный заряд; false — не хватило, удар без бонуса.</param>
        public ChargeEvent Tick(float dt, bool held, Func<float, bool> trySpend)
        {
            if (!Active)
                return ChargeEvent.None;
            if (!held)
            {
                Active = false;
                return ChargeEvent.Released;
            }

            Time += Math.Max(0f, dt);
            if (!Full)
            {
                if (Time < Config.Time)
                    return ChargeEvent.None;
                if (trySpend != null && !trySpend(Config.StaminaCost))
                {
                    Active = false;
                    return ChargeEvent.Released;
                }

                Full = true;
                return ChargeEvent.Charged;
            }

            if (Time >= Config.Time + Config.MaxHold)
            {
                Active = false;
                return ChargeEvent.Released;
            }

            return ChargeEvent.None;
        }

        /// <summary>Сбросить заряд и бонус (новая атака, отмена).</summary>
        public void Reset()
        {
            Config = null;
            Active = false;
            Full = false;
            Time = 0f;
        }
    }
}
