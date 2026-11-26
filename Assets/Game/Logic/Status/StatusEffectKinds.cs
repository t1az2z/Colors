using System;
using System.Collections.Generic;
using ColorsAction.Configs;

namespace ColorsAction.Status
{
    /// <summary>
    /// Реестр видов статус-эффектов: имя вида в конфиге (<see cref="StatusEffectConfig.Kind"/>) → класс.
    /// Новый вид: наследник <see cref="StatusEffect"/> + строка в статическом конструкторе (или <see cref="Register"/>).
    /// Регистрация фабриками, без рефлексии: безопасно для IL2CPP.
    /// </summary>
    public static class StatusEffectKinds
    {
        public const string Stun = "Stun";
        public const string DamageTaken = "DamageTaken";
        public const string DamageDealt = "DamageDealt";

        private static readonly Dictionary<string, Func<StatusEffect>> factories = new Dictionary<string, Func<StatusEffect>>();
        private static readonly Dictionary<string, Action<string, StatusEffectConfig>> validators =
            new Dictionary<string, Action<string, StatusEffectConfig>>();

        static StatusEffectKinds()
        {
            Register(Stun, () => new StunEffect());
            Register(DamageTaken, () => new DamageTakenEffect(), RequirePositiveValue);
            Register(DamageDealt, () => new DamageDealtEffect(), RequirePositiveValue);
        }

        public static IEnumerable<string> Known => factories.Keys;

        public static bool IsKnown(string kind) => !string.IsNullOrEmpty(kind) && factories.ContainsKey(kind);

        /// <param name="validate">Проверка чисел конфига для этого вида (бросает исключение), необязательно.</param>
        public static void Register(string kind, Func<StatusEffect> factory, Action<string, StatusEffectConfig> validate = null)
        {
            if (string.IsNullOrEmpty(kind))
                throw new ArgumentException("kind is empty", nameof(kind));
            factories[kind] = factory ?? throw new ArgumentNullException(nameof(factory));
            if (validate != null)
                validators[kind] = validate;
            else
                validators.Remove(kind);
        }

        internal static StatusEffect Create(string id, StatusEffectConfig config)
        {
            if (!factories.TryGetValue(config.Kind ?? string.Empty, out var factory))
                throw new InvalidOperationException($"Status effect '{id}': unknown kind '{config.Kind}'");
            var effect = factory();
            effect.Init(id, config);
            return effect;
        }

        internal static void Validate(string id, StatusEffectConfig config)
        {
            if (validators.TryGetValue(config.Kind, out var validate))
                validate(id, config);
        }

        private static void RequirePositiveValue(string id, StatusEffectConfig config)
        {
            if (config.Value <= 0f)
                throw new InvalidOperationException($"StatusEffects '{id}': value (damage multiplier) must be > 0");
        }
    }

    /// <summary>Оглушение: носитель не двигается и не атакует.</summary>
    public sealed class StunEffect : StatusEffect
    {
        protected internal override void Modify(ref StatusModifiers modifiers) => modifiers.Stunned = true;
    }

    /// <summary>Уязвимость: получаемый урон × (1 + (Value − 1) × стаки).</summary>
    public sealed class DamageTakenEffect : StatusEffect
    {
        protected internal override void Modify(ref StatusModifiers modifiers) =>
            modifiers.DamageTakenMultiplier *= Math.Max(0f, 1f + (Config.Value - 1f) * Stacks);
    }

    /// <summary>Усиление: наносимый урон × (1 + (Value − 1) × стаки).</summary>
    public sealed class DamageDealtEffect : StatusEffect
    {
        protected internal override void Modify(ref StatusModifiers modifiers) =>
            modifiers.DamageDealtMultiplier *= Math.Max(0f, 1f + (Config.Value - 1f) * Stacks);
    }
}
