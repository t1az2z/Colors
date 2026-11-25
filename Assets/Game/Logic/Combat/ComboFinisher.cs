using System;
using System.Collections.Generic;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    /// <summary>
    /// Отложенный добивающий удар — общий механизм для оружия: считает удары серии подряд и паузу после
    /// конца окна комбо. Нажатие атаки в окне паузы <see cref="FinisherConfig.DelayMin"/>..<see cref="FinisherConfig.DelayMax"/>
    /// после не меньше <see cref="FinisherConfig.MinHits"/> ударов запускает добивающий.
    /// <para>
    /// Порядок: <see cref="ComboAttack"/> при каждом ударе серии, <see cref="ComboWindowClosed"/> в момент, когда
    /// закончилась фиксация позы (дальше нажатие уже не продолжает серию), <see cref="TryTrigger"/> на нажатие.
    /// Время — любое монотонное (игровые секунды).
    /// </para>
    /// </summary>
    public sealed class ComboFinisher
    {
        private float? closedAt;

        /// <summary>Ударов серии подряд.</summary>
        public int Chain { get; private set; }

        /// <summary>Окно комбо закрыто и идёт отсчёт паузы.</summary>
        public bool Waiting => closedAt.HasValue;

        /// <summary>Начат удар серии: <paramref name="continuing"/> — продолжение текущей серии, иначе новая серия.</summary>
        public void ComboAttack(bool continuing)
        {
            Chain = continuing ? Chain + 1 : 1;
            closedAt = null;
        }

        /// <summary>Окно продолжения серии закончилось в момент <paramref name="now"/>: отсюда считается пауза.</summary>
        public void ComboWindowClosed(float now)
        {
            if (Chain > 0)
                closedAt = now;
        }

        /// <summary>Пауза после конца окна комбо, секунды; меньше нуля — отсчёта нет.</summary>
        public float Pause(float now) => closedAt.HasValue ? now - closedAt.Value : -1f;

        /// <summary>Нажатие в момент <paramref name="now"/> запустит добивающий.</summary>
        public bool Ready(FinisherConfig config, float now)
        {
            if (config == null || !closedAt.HasValue || Chain < config.MinHits)
                return false;
            var pause = now - closedAt.Value;
            return pause >= config.DelayMin && pause <= config.DelayMax;
        }

        /// <summary>Нажатие атаки: true — запускать добивающий (серия сбрасывается).</summary>
        public bool TryTrigger(FinisherConfig config, float now)
        {
            if (!Ready(config, now))
                return false;
            Reset();
            return true;
        }

        public void Reset()
        {
            Chain = 0;
            closedAt = null;
        }
    }

    /// <summary>Выбор удара серии с учётом апгрейдов.</summary>
    public static class ComboSteps
    {
        /// <summary>
        /// Индекс удара серии, начиная с <paramref name="index"/> (по кругу), пропуская закрытые апгрейдами.
        /// -1 — открытых ударов нет. <paramref name="hasUpgrade"/>(tier) — открыт ли апгрейд оружия.
        /// </summary>
        public static int NextUnlocked(IReadOnlyList<AttackStepConfig> combo, int index, Func<int, bool> hasUpgrade)
        {
            if (combo == null || combo.Count == 0)
                return -1;
            index = ((index % combo.Count) + combo.Count) % combo.Count;
            for (int i = 0; i < combo.Count; i++)
            {
                var candidate = (index + i) % combo.Count;
                var step = combo[candidate];
                if (step != null && Unlocked(step.Upgrade, hasUpgrade))
                    return candidate;
            }

            return -1;
        }

        /// <summary>Способность с порогом апгрейда <paramref name="tier"/> открыта (0 — всегда).</summary>
        public static bool Unlocked(int tier, Func<int, bool> hasUpgrade) => tier <= 0 || (hasUpgrade != null && hasUpgrade(tier));
    }
}
