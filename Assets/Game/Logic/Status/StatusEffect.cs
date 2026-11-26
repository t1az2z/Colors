using System;
using ColorsAction.Configs;

namespace ColorsAction.Status
{
    /// <summary>
    /// Что эффекты меняют у носителя прямо сейчас — сумма всех активных эффектов (<see cref="StatusEffects.Modifiers"/>).
    /// Новый вид влияния (шанс крита, уворота, замедление) — новое поле здесь и его чтение там, где оно нужно.
    /// </summary>
    public struct StatusModifiers
    {
        /// <summary>Оглушён: не ходит, не атакует (враг стоит, игрок теряет управление).</summary>
        public bool Stunned;

        /// <summary>Множитель получаемого урона (уязвимость).</summary>
        public float DamageTakenMultiplier;

        /// <summary>Множитель наносимого урона (усиление).</summary>
        public float DamageDealtMultiplier;

        public static StatusModifiers None => new StatusModifiers { DamageTakenMultiplier = 1f, DamageDealtMultiplier = 1f };
    }

    /// <summary>
    /// Носитель эффектов (враг, игрок) — для эффектов, которые что-то делают с ним сами (яд, горение — урон за тик).
    /// </summary>
    public interface IStatusTarget
    {
        /// <summary>Урон от эффекта: мимо неуязвимости после удара и без отшатывания.</summary>
        void TakeStatusDamage(float amount, StatusEffect source);
    }

    /// <summary>
    /// Один наложенный статус-эффект. Поведение — в наследниках (<see cref="StatusEffectKinds"/>), числа — в
    /// <see cref="StatusEffectConfig"/>. Время и стаки ведёт контроллер <see cref="StatusEffects"/>.
    /// </summary>
    public abstract class StatusEffect
    {
        /// <summary>Id из StatusEffects.json.</summary>
        public string Id { get; private set; }

        public StatusEffectConfig Config { get; private set; }

        /// <summary>Длительность текущего наложения, секунды. 0 — бессрочно (до снятия).</summary>
        public float Duration { get; internal set; }

        /// <summary>Сколько ещё действует, секунды (у бессрочного — 0).</summary>
        public float Remaining { get; internal set; }

        /// <summary>Сколько прошло с последнего наложения, секунды.</summary>
        public float Elapsed { get; internal set; }

        public int Stacks { get; internal set; } = 1;

        public bool Permanent => Duration <= 0f;

        /// <summary>Доля оставшегося времени 1..0 (бессрочный — всегда 1).</summary>
        public float RemainingFraction => Permanent ? 1f : Math.Max(0f, Remaining / Duration);

        internal void Init(string id, StatusEffectConfig config)
        {
            Id = id;
            Config = config;
        }

        /// <summary>Эффект наложен впервые (повторные наложения меняют только время и стаки).</summary>
        protected internal virtual void OnApplied(IStatusTarget target)
        {
        }

        /// <summary>Эффект закончился или снят.</summary>
        protected internal virtual void OnRemoved(IStatusTarget target)
        {
        }

        /// <summary>Каждый кадр, пока эффект действует (яд, горение).</summary>
        protected internal virtual void OnTick(float deltaTime, IStatusTarget target)
        {
        }

        /// <summary>Вклад эффекта в <see cref="StatusModifiers"/>.</summary>
        protected internal virtual void Modify(ref StatusModifiers modifiers)
        {
        }
    }
}
