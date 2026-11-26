using System;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    public enum PlungePhase
    {
        None,

        /// <summary>Замах в воздухе: оружие поднимается над головой.</summary>
        Hang,

        /// <summary>Полёт до земли с оружием над головой (прыжок идёт своим ходом).</summary>
        Fall,

        /// <summary>Приземлились: удар по площади нанесён, поза удара и возврат.</summary>
        Landed
    }

    /// <summary>
    /// Удар в прыжке без движка: фазы замах → полёт → приземление. Инерцию прыжка удар не трогает:
    /// куб долетает туда же, куда и без удара, а удар наносится в момент приземления
    /// (кадр приземления — <see cref="Tick"/> возвращает true).
    /// </summary>
    public sealed class PlungeAttack
    {
        public PlungeConfig Config { get; private set; }
        public PlungePhase Phase { get; private set; }
        public float Time { get; private set; }
        public bool Active => Phase != PlungePhase.None;

        /// <summary>Можно ли начать удар: в воздухе и не ниже <see cref="PlungeConfig.MinHeight"/> над землёй.</summary>
        public static bool CanStart(PlungeConfig config, bool grounded, float heightAboveGround)
        {
            return config != null && !grounded && heightAboveGround >= config.MinHeight;
        }

        public void Start(PlungeConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Phase = PlungePhase.Hang;
            Time = 0f;
        }

        /// <summary>Шаг: true — в этом кадре приземлились (наносить удар по площади).</summary>
        public bool Tick(float dt, bool grounded)
        {
            if (Phase == PlungePhase.None || Phase == PlungePhase.Landed)
                return false;
            Time += Math.Max(0f, dt);
            if (Phase == PlungePhase.Hang)
            {
                if (Time < Config.Hang)
                    return false;
                Phase = PlungePhase.Fall;
            }

            // Падение: земля под ногами — приземление (в том числе если зависание кончилось уже на земле).
            if (!grounded)
                return false;
            Phase = PlungePhase.Landed;
            return true;
        }

        public void Reset()
        {
            Phase = PlungePhase.None;
            Time = 0f;
        }
    }
}
