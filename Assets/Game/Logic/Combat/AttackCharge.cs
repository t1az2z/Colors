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

        /// <summary>Отпустили во вспышку (окно <see cref="ChargeConfig.PerfectWindow"/> после пика): полный бонус.</summary>
        public bool Perfect { get; private set; }

        /// <summary>Сколько секунд копится заряд.</summary>
        public float Time { get; private set; }

        /// <summary>Доля заряда 0..1 (для свечения, дрожи).</summary>
        public float Level => Config == null ? 0f : Config.Time <= 0f ? (Full ? 1f : 0f) : Math.Min(1f, Time / Config.Time);

        /// <summary>Секунды после пика заряда (0 до пика).</summary>
        public float SincePeak => Config == null || !Full ? 0f : Math.Max(0f, Time - Config.Time);

        /// <summary>Сейчас окно вспышки: отпускание даст полный бонус.</summary>
        public bool InPerfectWindow => Full && SincePeak <= Config.PerfectWindow;

        /// <summary>
        /// Доля бонуса 0..1 в текущий момент: 1 — во вспышку, при передержке спадает до доли
        /// <see cref="ChargeConfig.HeldMultiplier"/>. После отпускания — зафиксирована.
        /// </summary>
        public float Bonus { get; private set; }

        public float DamageMultiplier => Full ? Lerp(1f, Config.DamageMultiplier, Bonus) : 1f;
        public float KnockbackMultiplier => Full ? Lerp(1f, Config.KnockbackMultiplier, Bonus) : 1f;

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Доля бонуса через <paramref name="sincePeak"/> секунд после пика.</summary>
        public static float BonusAt(ChargeConfig config, float sincePeak)
        {
            var full = config.DamageMultiplier - 1f;
            var held = Math.Abs(full) < 1e-5f ? 1f : (config.HeldMultiplier - 1f) / full;
            if (sincePeak <= config.PerfectWindow)
                return 1f;
            if (config.Falloff <= 0f)
                return held;
            var k = Math.Min(1f, (sincePeak - config.PerfectWindow) / config.Falloff);
            return Lerp(1f, held, k);
        }

        /// <summary>Начать копить заряд. Прежний результат (бонус) сбрасывается.</summary>
        public void Begin(ChargeConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Active = true;
            Full = false;
            Perfect = false;
            Bonus = 0f;
            Time = 0f;
        }

        /// <param name="held">Кнопка атаки всё ещё зажата.</param>
        /// <param name="trySpend">Списать выносливость за полный заряд; false — не хватило, удар без бонуса.</param>
        public ChargeEvent Tick(float dt, bool held, Func<float, bool> trySpend)
        {
            if (!Active)
                return ChargeEvent.None;
            if (!held)
                return Release();

            Time += Math.Max(0f, dt);
            if (!Full)
            {
                if (Time < Config.Time)
                    return ChargeEvent.None;
                if (trySpend != null && !trySpend(Config.StaminaCost))
                    return Release();

                Full = true;
                Bonus = 1f;
                return ChargeEvent.Charged;
            }

            Bonus = BonusAt(Config, SincePeak);
            if (Time >= Config.Time + Config.MaxHold)
                return Release();

            return ChargeEvent.None;
        }

        private ChargeEvent Release()
        {
            Active = false;
            if (Full)
            {
                Bonus = BonusAt(Config, SincePeak);
                Perfect = SincePeak <= Config.PerfectWindow;
            }

            return ChargeEvent.Released;
        }

        /// <summary>Сбросить заряд и бонус (новая атака, отмена).</summary>
        public void Reset()
        {
            Config = null;
            Active = false;
            Full = false;
            Perfect = false;
            Bonus = 0f;
            Time = 0f;
        }
    }
}
