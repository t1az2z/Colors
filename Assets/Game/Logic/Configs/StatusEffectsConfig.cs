using System;
using System.Collections.Generic;
using ColorsAction.Status;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Configs/StatusEffects.json — статус-эффекты (<see cref="StatusEffects"/>): что каждый делает, сколько длится,
    /// как складывается при повторном наложении, и какие эффекты получает враг после парирования.
    /// Эффект накладывается и на врага, и на игрока. Новый вид эффекта — класс-наследник <see cref="StatusEffect"/>,
    /// регистрация в <see cref="StatusEffectKinds"/> и запись здесь.
    /// </summary>
    [Preserve]
    public sealed class StatusEffectsConfig : IConfigPostLoad
    {
        /// <summary>Ключ файла конфига (Configs/StatusEffects.json).</summary>
        public const string Key = "StatusEffects";

        /// <summary>Id эффекта оглушения: им же оглушается враг после парирования.</summary>
        public const string StunId = "stun";

        /// <summary>Эффекты по id. На id ссылаются <see cref="OnParry"/> и код (<see cref="StunId"/>).</summary>
        public Dictionary<string, StatusEffectConfig> Effects { get; set; } = new Dictionary<string, StatusEffectConfig>();

        /// <summary>
        /// Что враг получает в момент, когда его оружие встретило щит (парирование щитом, ударом щита или выстрелом),
        /// кроме самого оглушения: оно длится <see cref="EnemyConfig.ParriedDuration"/> врага, как и раньше.
        /// </summary>
        public List<StatusApplication> OnParry { get; set; } = new List<StatusApplication>();

        /// <summary>Встроенные значения, когда файла нет (тесты, врага создали без конфига эффектов).</summary>
        public static StatusEffectsConfig CreateDefault()
        {
            var config = new StatusEffectsConfig
            {
                Effects = new Dictionary<string, StatusEffectConfig>
                {
                    [StunId] = new StatusEffectConfig { Kind = StatusEffectKinds.Stun, Duration = 1f, Stacking = StatusStacking.Refresh },
                    ["vulnerable"] = new StatusEffectConfig
                    {
                        Kind = StatusEffectKinds.DamageTaken, Duration = 1.5f, Value = 1.5f, Stacking = StatusStacking.Refresh
                    }
                },
                OnParry = new List<StatusApplication> { new StatusApplication { Effect = "vulnerable" } }
            };
            config.OnConfigLoaded();
            return config;
        }

        public bool TryGet(string id, out StatusEffectConfig effect)
        {
            effect = null;
            return !string.IsNullOrEmpty(id) && Effects != null && Effects.TryGetValue(id, out effect) && effect != null;
        }

        public void OnConfigLoaded()
        {
            Effects ??= new Dictionary<string, StatusEffectConfig>();
            OnParry ??= new List<StatusApplication>();
            foreach (var pair in Effects)
            {
                if (pair.Value == null)
                    throw new InvalidOperationException($"StatusEffects: effect '{pair.Key}' is empty");
                pair.Value.Validate(pair.Key);
            }

            // Оглушение после парирования код накладывает по StunId: без него парирование бросало бы исключение.
            if (!TryGet(StunId, out var stun) || stun.Kind != StatusEffectKinds.Stun)
                throw new InvalidOperationException($"StatusEffects: effect '{StunId}' with kind '{StatusEffectKinds.Stun}' is required (parry stun)");

            foreach (var application in OnParry)
            {
                if (application == null || !TryGet(application.Effect, out _))
                    throw new InvalidOperationException($"StatusEffects.onParry: unknown effect '{application?.Effect}'");
                if (application.Duration < 0f)
                    throw new InvalidOperationException($"StatusEffects.onParry '{application.Effect}': duration must not be negative");
            }
        }
    }

    /// <summary>Как эффект складывается, если его накладывают, пока он ещё действует.</summary>
    public enum StatusStacking
    {
        /// <summary>Время начинается заново (берётся большее из оставшегося и нового), сила не растёт.</summary>
        Refresh,

        /// <summary>Новое время прибавляется к оставшемуся.</summary>
        Extend,

        /// <summary>+1 стак (до <see cref="StatusEffectConfig.MaxStacks"/>), время начинается заново. Сила растёт со стаками.</summary>
        Stack,

        /// <summary>Повторное наложение ничего не меняет, пока эффект действует.</summary>
        Ignore
    }

    /// <summary>Один статус-эффект: вид (поведение в коде), длительность, сила и складывание.</summary>
    [Preserve]
    public sealed class StatusEffectConfig
    {
        /// <summary>Отображаемое имя (редактор конфигов, позже — интерфейс).</summary>
        public string Name { get; set; } = "";

        /// <summary>Вид эффекта — какой класс его исполняет (<see cref="StatusEffectKinds"/>): Stun, DamageTaken, DamageDealt.</summary>
        public string Kind { get; set; } = StatusEffectKinds.Stun;

        /// <summary>Длительность по умолчанию, секунды. 0 — пока не снимут вручную.</summary>
        public float Duration { get; set; } = 1f;

        /// <summary>
        /// Сила эффекта, смысл зависит от вида: DamageTaken / DamageDealt — множитель урона (1.5 = +50%);
        /// у оглушения не используется.
        /// </summary>
        public float Value { get; set; } = 1f;

        public StatusStacking Stacking { get; set; } = StatusStacking.Refresh;

        /// <summary>Предел стаков для <see cref="StatusStacking.Stack"/>.</summary>
        public int MaxStacks { get; set; } = 1;

        /// <summary>Цвет подсветки носителя в hex ("#FFB13B"). Пусто — без подсветки.</summary>
        public string Tint { get; set; } = "";

        /// <summary>Сила пульсирующей подсветки носителя (0 — нет).</summary>
        public float TintStrength { get; set; } = 0.5f;

        /// <summary>Частота пульса подсветки, Гц.</summary>
        public float TintPulse { get; set; } = 3f;

        public void Validate(string id)
        {
            if (!StatusEffectKinds.IsKnown(Kind))
                throw new InvalidOperationException($"StatusEffects '{id}': unknown kind '{Kind}' (known: {string.Join(", ", StatusEffectKinds.Known)})");
            if (Duration < 0f)
                throw new InvalidOperationException($"StatusEffects '{id}': duration must not be negative");
            if (MaxStacks < 1)
                throw new InvalidOperationException($"StatusEffects '{id}': maxStacks >= 1");
            if (TintStrength < 0f || TintPulse < 0f)
                throw new InvalidOperationException($"StatusEffects '{id}': tintStrength and tintPulse must not be negative");
            StatusEffectKinds.Validate(id, this);
        }
    }

    /// <summary>Наложить эффект <see cref="Effect"/>; длительность — своя или (0) по умолчанию эффекта.</summary>
    [Preserve]
    public sealed class StatusApplication
    {
        public string Effect { get; set; } = "";

        /// <summary>Длительность, секунды. 0 — <see cref="StatusEffectConfig.Duration"/> эффекта.</summary>
        public float Duration { get; set; }
    }
}
