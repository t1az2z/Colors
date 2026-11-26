using System;
using GameCore.Ticking;
using GameCore.UI;

namespace ColorsAction.Combat
{
    /// <summary>Здоровье: текущее значение, максимум, смерть.</summary>
    public sealed class Health
    {
        public ReactiveProperty<float> Current { get; }
        public float Max { get; private set; }
        public float Fraction => Max > 0 ? Current.Value / Max : 0f;
        public bool IsDead => Current.Value <= 0f;

        /// <summary>(нанесённый урон, осталось)</summary>
        public event Action<float, float> Damaged;
        public event Action Died;

        public Health(float max)
        {
            if (max <= 0)
                throw new ArgumentOutOfRangeException(nameof(max));
            Max = max;
            Current = new ReactiveProperty<float>(max);
        }

        /// <summary>Отнимает здоровье. Возвращает фактически снятое.</summary>
        public float TakeDamage(float amount)
        {
            if (amount <= 0f || IsDead)
                return 0f;
            var applied = Math.Min(amount, Current.Value);
            Current.Value -= applied;
            Damaged?.Invoke(applied, Current.Value);
            if (IsDead)
                Died?.Invoke();
            return applied;
        }

        public void Heal(float amount)
        {
            if (amount > 0f && !IsDead)
                Current.Value = Math.Min(Max, Current.Value + amount);
        }

        public void Restore() => Current.Value = Max;
    }

    /// <summary>
    /// Здоровье игрока с неуязвимостью после попадания (GDD: 2 секунды, игрок мигает).
    /// Перекат даёт свои i-frames отдельно — их проверяет контроллер движения.
    /// </summary>
    public sealed class PlayerVitals : ITickable
    {
        private readonly float invulnerabilityAfterHit;

        public Health Health { get; }

        /// <summary>Сколько ещё длится неуязвимость после попадания, секунды.</summary>
        public float InvulnerableTime { get; private set; }

        public bool Invulnerable => InvulnerableTime > 0f;

        public PlayerVitals(float maxHealth, float invulnerabilityAfterHit)
        {
            Health = new Health(maxHealth);
            this.invulnerabilityAfterHit = invulnerabilityAfterHit;
        }

        /// <summary>Попадание по игроку. false — урон не прошёл (неуязвим или уже мёртв).</summary>
        public bool TryHit(float damage)
        {
            if (Invulnerable || Health.IsDead || damage <= 0f)
                return false;
            Health.TakeDamage(damage);
            InvulnerableTime = invulnerabilityAfterHit;
            return true;
        }

        /// <summary>Мгновенная смерть (падение за границу мира): мимо неуязвимости.</summary>
        public void Kill()
        {
            if (!Health.IsDead)
                Health.TakeDamage(Health.Current.Value);
        }

        public void Revive()
        {
            Health.Restore();
            InvulnerableTime = invulnerabilityAfterHit;
        }

        public void Tick(float deltaTime)
        {
            if (InvulnerableTime > 0f)
                InvulnerableTime = Math.Max(0f, InvulnerableTime - deltaTime);
        }
    }
}
