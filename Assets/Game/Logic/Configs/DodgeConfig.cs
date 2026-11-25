using System;
using System.Collections.Generic;
using GameCore;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Player.json → "dodge": серия отскоков (комбо). Нажатие отскока в окне комбо запускает следующий шаг серии,
    /// иначе серия начинается заново с первого шага. После последнего шага серия тоже начинается сначала.
    /// </summary>
    [Preserve]
    public sealed class DodgeConfig
    {
        /// <summary>Окно комбо, секунды после конца шага: нажатие в это время запускает следующий шаг серии.</summary>
        public float ComboWindow { get; set; } = 0.35f;

        /// <summary>Сколько секунд помнится нажатие отскока (нажали чуть раньше, чем стало можно).</summary>
        public float InputBuffer { get; set; } = 0.18f;

        /// <summary>Шаги серии по порядку. Обычно: два коротких отскока и перекат через верх.</summary>
        public List<DodgeStepConfig> Steps { get; set; } = DefaultSteps();

        public static List<DodgeStepConfig> DefaultSteps() => new List<DodgeStepConfig>
        {
            new DodgeStepConfig
            {
                Distance = 1.6f, Duration = 0.26f, HopHeight = 0.22f, InvulnerableFrom = 0.05f, InvulnerableTo = 0.75f,
                StaminaCost = 14f, Cooldown = 0.05f, TakeoffSquash = -0.28f, Stretch = 0.12f, Bend = 0.1f,
                DirSquash = 0.15f, LandSquash = -0.22f
            },
            new DodgeStepConfig
            {
                Distance = 2.2f, Duration = 0.3f, HopHeight = 0.28f, InvulnerableFrom = 0.05f, InvulnerableTo = 0.75f,
                StaminaCost = 16f, Cooldown = 0.05f, TakeoffSquash = -0.3f, Stretch = 0.15f, Bend = 0.12f,
                DirSquash = 0.18f, LandSquash = -0.25f
            },
            new DodgeStepConfig
            {
                Distance = 4f, Duration = 0.5f, HopHeight = 0.29f, Flip = true, InvulnerableFrom = 0f, InvulnerableTo = 0.7f,
                StaminaCost = 22f, Cooldown = 0.15f, Bend = 0.18f, DirSquash = 0.22f, LandSquash = -0.25f
            },
        };

        public void Validate()
        {
            Steps ??= DefaultSteps();
            if (Steps.Count == 0)
                throw new InvalidOperationException("dodge.steps must contain at least one step");
            if (ComboWindow < 0 || InputBuffer < 0)
                throw new InvalidOperationException("dodge.comboWindow and dodge.inputBuffer must not be negative");
            for (int i = 0; i < Steps.Count; i++)
            {
                var s = Steps[i] ?? throw new InvalidOperationException($"dodge.steps[{i}] is null");
                if (s.Duration <= 0 || s.Distance < 0 || s.HopHeight < 0 || s.Cooldown < 0)
                    throw new InvalidOperationException($"dodge.steps[{i}]: duration must be positive, distance, hopHeight and cooldown not negative");
                if (s.MidPause < 0 || s.MidPause > 0.6f)
                    throw new InvalidOperationException($"dodge.steps[{i}].midPause must be in 0..0.6");
                if (s.InvulnerableFrom < 0 || s.InvulnerableTo > 1 || s.InvulnerableFrom > s.InvulnerableTo)
                    throw new InvalidOperationException($"dodge.steps[{i}]: invulnerability window must be inside 0..1");
                if (s.ChainFrom < 0 || s.ChainFrom > 1)
                    throw new InvalidOperationException($"dodge.steps[{i}].chainFrom must be in 0..1");
            }
        }
    }

    /// <summary>Один шаг серии отскоков: короткий прыжок или перекат через верх в два переворота.</summary>
    [Preserve]
    public sealed class DodgeStepConfig
    {
        /// <summary>Сколько метров (блоков) проходит куб за шаг.</summary>
        public float Distance { get; set; } = 1.6f;

        /// <summary>Длительность шага от толчка до приземления, секунды.</summary>
        public float Duration { get; set; } = 0.28f;

        /// <summary>Высота подскока; у переката — в каждом из двух переворотов.</summary>
        public float HopHeight { get; set; } = 0.22f;

        /// <summary>
        /// false — короткий прыжок без переворота; true — перекат через верх: переворот на «голову»,
        /// (пауза <see cref="MidPause"/>), второй переворот на «ноги».
        /// </summary>
        public bool Flip { get; set; }

        /// <summary>Перекат: доля длительности, которую куб стоит «на голове» между переворотами (0..0.6).</summary>
        public float MidPause { get; set; }

        /// <summary>Перекат: скорость скольжения в паузе относительно полёта (0 — стоит на месте).</summary>
        public float MidSpeed { get; set; }

        /// <summary>Окно неуязвимости, доли длительности шага (0..1).</summary>
        public float InvulnerableFrom { get; set; } = 0.05f;
        public float InvulnerableTo { get; set; } = 0.75f;

        public float StaminaCost { get; set; } = 15f;

        /// <summary>Пауза после шага, секунды: раньше новый отскок не начнётся (нажатие запомнится буфером).</summary>
        public float Cooldown { get; set; }

        /// <summary>
        /// С какой доли шага (0..1) запомненное нажатие сразу запускает следующий шаг серии, обрывая хвост текущего.
        /// 1 — только после приземления.
        /// </summary>
        public float ChainFrom { get; set; } = 1f;

        // Деформация куба (доли размера): отрицательное сжатие — сплющивание, положительное — вытягивание.

        /// <summary>Сжатие в момент толчка (подготовка к прыжку). 0 — без мгновенного сжатия.</summary>
        public float TakeoffSquash { get; set; }

        /// <summary>Вытягивание в полёте (короткий прыжок).</summary>
        public float Stretch { get; set; }

        /// <summary>Изгиб в сторону движения.</summary>
        public float Bend { get; set; } = 0.18f;

        /// <summary>Сжатие вдоль направления движения на толчке и в полёте.</summary>
        public float DirSquash { get; set; } = 0.22f;

        /// <summary>Сплющивание при приземлении (у переката — и на «голову», и на «ноги»).</summary>
        public float LandSquash { get; set; } = -0.25f;
    }

    /// <summary>Состояние шага отскока в момент времени: скорость, подскок, переворот.</summary>
    public struct DodgePose
    {
        /// <summary>Горизонтальная скорость, м/с.</summary>
        public float Speed;

        /// <summary>Подъём куба над землёй, метры.</summary>
        public float Hop;

        /// <summary>Поворот через грань, градусы: 0 — на «ногах», 180 — на «голове», 360 — снова на «ногах».</summary>
        public float Angle;

        /// <summary>0 — полёт (первый переворот или короткий прыжок), 1 — пауза «на голове», 2 — второй переворот.</summary>
        public int Phase;

        /// <summary>Прогресс внутри фазы, 0..1.</summary>
        public float PhaseProgress;

        /// <summary>
        /// Касания земли: 0 — первый полёт, 1 — приземлился на «голову», 2 — второй толчок, 3 — приземлился на «ноги».
        /// Короткий прыжок идёт сразу 0 → 3.
        /// </summary>
        public int Stage;
    }

    public static class DodgeMotion
    {
        /// <summary>Поза шага в момент <paramref name="t"/> (доля длительности 0..1).</summary>
        public static DodgePose Sample(this DodgeStepConfig step, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            var pose = new DodgePose();
            var duration = Math.Max(0.0001f, step.Duration);
            if (!step.Flip)
            {
                // Короткий прыжок: ровная скорость, подскок по синусу, без переворота.
                pose.Phase = 0;
                pose.PhaseProgress = t;
                pose.Speed = step.Distance / duration;
                pose.Hop = (float)Math.Sin(t * Math.PI) * step.HopHeight;
                pose.Angle = 0f;
                pose.Stage = t >= 1f ? 3 : 0;
                return pose;
            }

            // Перекат: переворот 1 → стоит «на голове» (пауза) → переворот 2 → на «ноги».
            var pause = step.MidPause;
            var hopLength = (1f - pause) * 0.5f;
            if (t < hopLength)
            {
                pose.Phase = 0;
                pose.PhaseProgress = t / hopLength;
            }
            else if (t < hopLength + pause)
            {
                pose.Phase = 1;
                pose.PhaseProgress = (t - hopLength) / Math.Max(0.0001f, pause);
            }
            else
            {
                pose.Phase = 2;
                pose.PhaseProgress = Math.Min(1f, (t - hopLength - pause) / hopLength);
            }

            // В полёте быстро, в паузе почти стоит; средняя скорость даёт ровно Distance за весь перекат.
            var average = (1f - pause) + pause * step.MidSpeed;
            var baseSpeed = step.Distance / duration / Math.Max(0.05f, average);
            pose.Speed = baseSpeed * (pose.Phase == 1 ? step.MidSpeed : 1f);

            var u = pose.PhaseProgress;
            if (pose.Phase == 1)
            {
                pose.Angle = 180f;
                pose.Hop = 0f;
            }
            else
            {
                pose.Angle = (pose.Phase == 2 ? 180f : 0f) + EaseInOut(u) * 180f;
                pose.Hop = (float)Math.Sin(u * Math.PI) * step.HopHeight;
            }

            pose.Stage = pose.Phase == 0 ? 0 : pose.Phase == 1 ? 1 : t >= 1f ? 3 : 2;
            return pose;
        }

        private static float EaseInOut(float x) => x * x * (3f - 2f * x);
    }
}
