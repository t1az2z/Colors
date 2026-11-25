using System;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    public enum PlungePhase
    {
        None,

        /// <summary>Зависание в воздухе, оружие над головой.</summary>
        Hang,

        /// <summary>Быстрое падение.</summary>
        Fall,

        /// <summary>Приземлились: удар по площади нанесён, поза удара и возврат.</summary>
        Landed
    }

    /// <summary>
    /// Удар в прыжке без движка: фазы зависание → падение → приземление и вертикальная скорость,
    /// которую оружие навязывает контроллеру. Кадр приземления — <see cref="Tick"/> возвращает true.
    /// </summary>
    public sealed class PlungeAttack
    {
        public PlungeConfig Config { get; private set; }
        public PlungePhase Phase { get; private set; }
        public float Time { get; private set; }
        public bool Active => Phase != PlungePhase.None;

        /// <summary>Вертикальная скорость, которую задаёт удар (null — обычная гравитация).</summary>
        public float? VerticalVelocity
        {
            get
            {
                switch (Phase)
                {
                    case PlungePhase.Hang: return 0f;
                    case PlungePhase.Fall: return -Config.FallSpeed;
                    default: return null;
                }
            }
        }

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
