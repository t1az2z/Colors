using System;
using System.Collections.Generic;
using ColorsAction.Configs;
using GameCore.Ticking;

namespace ColorsAction.Status
{
    /// <summary>
    /// Контроллер статус-эффектов одного носителя (врага или игрока): накладывает эффекты по id из
    /// StatusEffects.json, ведёт их время и стаки, снимает по истечении и собирает их влияние в <see cref="Modifiers"/>.
    /// Логика без движка: враг тикает его из <see cref="ColorsAction.Combat.EnemyBrain"/>, игрок — через TickScheduler.
    /// </summary>
    public sealed class StatusEffects : ITickable
    {
        /// <summary>Остаток времени, ниже которого эффект считается истёкшим (ошибка накопления float).</summary>
        private const float ExpireEpsilon = 1e-4f;

        private readonly StatusEffectsConfig config;
        private readonly List<StatusEffect> active = new List<StatusEffect>();
        private StatusModifiers modifiers = StatusModifiers.None;

        /// <summary>Носитель для эффектов, которые действуют на него сами (урон за тик). Можно задать позже.</summary>
        public IStatusTarget Target { get; set; }

        public StatusEffects(StatusEffectsConfig config, IStatusTarget target = null)
        {
            this.config = config ?? StatusEffectsConfig.CreateDefault();
            Target = target;
        }

        public StatusEffectsConfig Config => config;

        public IReadOnlyList<StatusEffect> Active => active;

        /// <summary>Сумма влияния всех активных эффектов.</summary>
        public StatusModifiers Modifiers => modifiers;

        public bool IsStunned => modifiers.Stunned;
        public float DamageTakenMultiplier => modifiers.DamageTakenMultiplier;
        public float DamageDealtMultiplier => modifiers.DamageDealtMultiplier;

        /// <summary>Эффект наложен впервые.</summary>
        public event Action<StatusEffect> Applied;

        /// <summary>Эффект наложен повторно, пока действовал (время или стаки обновились).</summary>
        public event Action<StatusEffect> Refreshed;

        /// <summary>Эффект закончился или снят.</summary>
        public event Action<StatusEffect> Removed;

        public bool Has(string id) => Get(id) != null;

        public StatusEffect Get(string id)
        {
            foreach (var effect in active)
                if (effect.Id == id)
                    return effect;
            return null;
        }

        /// <summary>Есть ли эффект вида <paramref name="kind"/> (<see cref="StatusEffectKinds"/>).</summary>
        public bool HasKind(string kind)
        {
            foreach (var effect in active)
                if (effect.Config.Kind == kind)
                    return true;
            return false;
        }

        /// <summary>
        /// Наложить эффект <paramref name="id"/>. <paramref name="duration"/> — своя длительность (null — из конфига).
        /// Неизвестный id — исключение (опечатка в конфиге или коде).
        /// </summary>
        /// <returns>Эффект (новый или обновлённый).</returns>
        public StatusEffect Apply(string id, float? duration = null)
        {
            if (!config.TryGet(id, out var effectConfig))
                throw new ArgumentException($"Unknown status effect '{id}' (not in StatusEffects.json)", nameof(id));
            var time = Math.Max(0f, duration ?? effectConfig.Duration);
            var existing = Get(id);
            if (existing != null)
            {
                if (Stack(existing, time))
                {
                    Recalculate();
                    Refreshed?.Invoke(existing);
                }

                return existing;
            }

            var effect = StatusEffectKinds.Create(id, effectConfig);
            effect.Duration = time;
            effect.Remaining = time;
            effect.Elapsed = 0f;
            effect.Stacks = 1;
            active.Add(effect);
            Recalculate();
            effect.OnApplied(Target);
            Applied?.Invoke(effect);
            return effect;
        }

        /// <summary>Наложить эффект по записи из конфига (<see cref="StatusEffectsConfig.OnParry"/> и т.п.).</summary>
        public StatusEffect Apply(StatusApplication application) =>
            Apply(application.Effect, application.Duration > 0f ? application.Duration : (float?)null);

        /// <summary>Снять эффект. false — его не было.</summary>
        public bool Remove(string id)
        {
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].Id != id)
                    continue;
                RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>Снять всё (смерть, возрождение).</summary>
        public void Clear()
        {
            for (int i = active.Count - 1; i >= 0; i--)
                RemoveAt(i);
        }

        public void Tick(float deltaTime)
        {
            if (active.Count == 0 || deltaTime <= 0f)
                return;
            for (int i = 0; i < active.Count; i++)
            {
                var effect = active[i];
                effect.Elapsed += deltaTime;
                effect.OnTick(deltaTime, Target);
                if (!effect.Permanent)
                    effect.Remaining -= deltaTime;
            }

            for (int i = active.Count - 1; i >= 0; i--)
                if (i < active.Count && !active[i].Permanent && active[i].Remaining <= ExpireEpsilon)
                    RemoveAt(i);
        }

        private static bool Stack(StatusEffect effect, float time)
        {
            switch (effect.Config.Stacking)
            {
                case StatusStacking.Ignore:
                    return false;
                case StatusStacking.Extend:
                    if (effect.Permanent)
                        return false;
                    effect.Remaining += time;
                    effect.Duration = effect.Remaining;
                    break;
                case StatusStacking.Stack:
                    effect.Stacks = Math.Min(effect.Config.MaxStacks, effect.Stacks + 1);
                    effect.Duration = time;
                    effect.Remaining = time;
                    break;
                default: // Refresh
                    if (time <= 0f)
                        effect.Duration = effect.Remaining = 0f;
                    else if (!effect.Permanent && time > effect.Remaining)
                        effect.Duration = effect.Remaining = time;
                    break;
            }

            effect.Elapsed = 0f;
            return true;
        }

        private void RemoveAt(int index)
        {
            var effect = active[index];
            active.RemoveAt(index);
            Recalculate();
            effect.OnRemoved(Target);
            Removed?.Invoke(effect);
        }

        private void Recalculate()
        {
            var m = StatusModifiers.None;
            foreach (var effect in active)
                effect.Modify(ref m);
            modifiers = m;
        }
    }
}
