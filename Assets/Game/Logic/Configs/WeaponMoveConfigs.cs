using System;
using GameCore;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Заряд атаки удержанием кнопки (общий механизм: удар серии, позже — заряженный выстрел).
    /// Пока кнопка зажата, атака стоит на конце замаха и копит заряд; пик заряда — вспышка оружия
    /// и дополнительная выносливость. Отпустили во вспышку (<see cref="PerfectWindow"/>) — полный бонус
    /// <see cref="DamageMultiplier"/>; держат дольше — множитель за <see cref="Falloff"/> спадает до
    /// <see cref="HeldMultiplier"/>. Отпустили до пика — обычный удар без бонуса.
    /// </summary>
    [Preserve]
    public sealed class ChargeConfig
    {
        /// <summary>Заряд доступен только с этим апгрейдом оружия (1..3); 0 — всегда.</summary>
        public int Upgrade { get; set; }

        /// <summary>Сколько секунд держать кнопку после замаха до полного заряда (на столько удлиняется замах).</summary>
        public float Time { get; set; } = 0.4f;

        /// <summary>Сколько полный заряд можно держать, секунды; потом удар срывается сам. 0 — сразу после заряда.</summary>
        public float MaxHold { get; set; } = 0.5f;

        /// <summary>Окно вспышки после пика заряда, секунды: отпустили в нём — полный множитель <see cref="DamageMultiplier"/>.</summary>
        public float PerfectWindow { get; set; } = 0.12f;

        /// <summary>За сколько секунд после окна вспышки множитель спадает до <see cref="HeldMultiplier"/>.</summary>
        public float Falloff { get; set; } = 0.25f;

        /// <summary>Множитель урона во вспышку (поверх множителя удара).</summary>
        public float DamageMultiplier { get; set; } = 2f;

        /// <summary>Множитель урона, если заряд передержали (после спада).</summary>
        public float HeldMultiplier { get; set; } = 1.3f;

        /// <summary>Множитель отбрасывания во вспышку; при спаде уменьшается вместе с уроном.</summary>
        public float KnockbackMultiplier { get; set; } = 1.5f;

        /// <summary>Дополнительная выносливость за полный заряд (списывается в момент заряда; не хватило — без бонуса).</summary>
        public float StaminaCost { get; set; } = 10f;

        /// <summary>Вспышка и свечение оружия при заряде, сила (0 — без вспышки).</summary>
        public float Flash { get; set; } = 1f;

        public void Validate(string id)
        {
            if (Time < 0f || MaxHold < 0f || PerfectWindow < 0f || Falloff < 0f || StaminaCost < 0f || DamageMultiplier < 0f ||
                HeldMultiplier < 0f || KnockbackMultiplier < 0f || Flash < 0f)
                throw new InvalidOperationException($"weapon '{id}': charge values must not be negative");
        }
    }

    /// <summary>
    /// Отложенный добивающий удар (общий механизм для оружия): после <see cref="MinHits"/> ударов серии подряд
    /// пауза и ещё одно нажатие. Пауза отсчитывается от конца окна комбо (конец фиксации позы последнего удара):
    /// нажатие через <see cref="DelayMin"/>..<see cref="DelayMax"/> секунд запускает <see cref="Step"/>.
    /// </summary>
    [Preserve]
    public sealed class FinisherConfig
    {
        /// <summary>Добивающий доступен только с этим апгрейдом оружия (1..3); 0 — всегда.</summary>
        public int Upgrade { get; set; }

        /// <summary>Сколько ударов серии подряд нужно перед паузой.</summary>
        public int MinHits { get; set; } = 2;

        /// <summary>Окно паузы, секунды от конца фиксации позы последнего удара.</summary>
        public float DelayMin { get; set; } = 0.05f;
        public float DelayMax { get; set; } = 0.6f;

        /// <summary>Сам добивающий удар (те же поля, что у удара серии; отскок — hop + отрицательный lunge).</summary>
        public AttackStepConfig Step { get; set; }

        public void Validate(string id)
        {
            if (Step == null)
                throw new InvalidOperationException($"weapon '{id}': finisher.step is required");
            if (MinHits < 1 || DelayMin < 0f || DelayMax < DelayMin)
                throw new InvalidOperationException($"weapon '{id}': finisher needs minHits >= 1 and 0 <= delayMin <= delayMax");
            Step.Normalize();
            Step.Charge?.Validate(id);
        }
    }

    /// <summary>
    /// Удар в прыжке: атака в воздухе не прерывает полёт — меч поднимается над головой, куб долетает
    /// по той же дуге, что и без удара, и при приземлении бьёт сверху вниз по площади вокруг себя с отбрасыванием.
    /// </summary>
    [Preserve]
    public sealed class PlungeConfig
    {
        /// <summary>Доступен только с этим апгрейдом оружия (1..3); 0 — всегда.</summary>
        public int Upgrade { get; set; }

        /// <summary>Наименьшая высота над землёй, с которой начинается удар (ниже — обычный удар), блоки.</summary>
        public float MinHeight { get; set; } = 0.35f;

        /// <summary>Замах в воздухе (меч поднимается над головой), секунды.</summary>
        public float Hang { get; set; } = 0.1f;

        /// <summary>Радиус удара вокруг точки приземления, блоки.</summary>
        public float Radius { get; set; } = 1.8f;

        /// <summary>Множитель урона (от базового урона оружия).</summary>
        public float DamageMultiplier { get; set; } = 1.5f;

        /// <summary>Отбрасывание врагов от точки приземления.</summary>
        public float Knockback { get; set; } = 2.5f;

        public float StaminaCost { get; set; } = 18f;

        /// <summary>Поза удара после приземления (акцент), секунды.</summary>
        public float LandHold { get; set; } = 0.25f;

        /// <summary>Возврат в стойку после приземления, секунды.</summary>
        public float Recovery { get; set; } = 0.2f;

        /// <summary>Окно эффекта (след клинка), доли всего удара; меньше нуля — автоматически.</summary>
        public float FxFrom { get; set; } = -1f;
        public float FxTo { get; set; } = -1f;

        public void Validate(string id)
        {
            if (Radius <= 0f)
                throw new InvalidOperationException($"weapon '{id}': plunge radius must be positive");
            if (Hang < 0f || MinHeight < 0f || LandHold < 0f || Recovery < 0f || StaminaCost < 0f || DamageMultiplier < 0f || Knockback < 0f)
                throw new InvalidOperationException($"weapon '{id}': plunge values must not be negative");
        }

        /// <summary>
        /// Удар в прыжке как шаг атаки (для поз и таймингов): замах — подъём меча, удар — от конца замаха до земли
        /// (время удара стоит на середине, пока куб не коснётся земли), фиксация — поза после приземления.
        /// </summary>
        public AttackStepConfig ToStep()
        {
            var step = new AttackStepConfig
            {
                Motion = AttackMotion.Chop,
                Windup = Hang,
                Strike = 0.1f,
                Hold = LandHold,
                Recovery = Recovery,
                DamageMultiplier = DamageMultiplier,
                Lunge = 0f,
                Range = Radius,
                Arc = 360f,
                Knockback = Knockback,
                StaminaCost = StaminaCost,
                RollLockFrom = 0f,
                RollLockTo = -1f,
                FxFrom = FxFrom,
                FxTo = FxTo
            };
            step.Normalize();
            return step;
        }
    }
}
