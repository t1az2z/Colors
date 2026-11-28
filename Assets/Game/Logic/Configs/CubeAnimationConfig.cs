using System;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Configs/Animations.json — процедурная анимация кубов деформацией: шаги при ходьбе и беге (покачивание,
    /// сжатие-вытягивание с сохранением объёма), дыхание и моргание в покое, осмотр по сторонам (игрок),
    /// перебрасывание оружия из руки в руку (враги). Файл — словарь по id: <see cref="PlayerId"/> — игрок, <see cref="EnemyId"/> —
    /// все враги; запись с id типа врага (как в Enemies.json) заменяет общую для этого типа целиком.
    /// Отклик на удары, прыжки и перекаты (пружины) — в Player.json → "deformation".
    /// </summary>
    [Preserve]
    public sealed class CubeAnimationConfig : IConfigPostLoad
    {
        public const string Key = "Animations";
        public const string PlayerId = "player";
        public const string EnemyId = "enemy";

        /// <summary>Шаги: покачивание, сжатие-вытягивание, наклон по ходу.</summary>
        public StepCycleConfig Move { get; set; } = new StepCycleConfig();

        /// <summary>Дыхание стоя на месте.</summary>
        public BreathConfig Breath { get; set; } = new BreathConfig();

        /// <summary>Моргание.</summary>
        public BlinkConfig Blink { get; set; } = new BlinkConfig();

        /// <summary>Осмотр по сторонам после нескольких секунд покоя; null — нет.</summary>
        public LookAroundConfig LookAround { get; set; }

        /// <summary>Редкий idle: перебрасывает оружие перед собой из руки в руку; null — нет.</summary>
        public WeaponInspectConfig Inspect { get; set; }

        public void OnConfigLoaded()
        {
            Move ??= new StepCycleConfig();
            Breath ??= new BreathConfig();
            Blink ??= new BlinkConfig();
            Move.Validate();
            Breath.Validate();
            Blink.Validate();
            LookAround?.Validate();
            Inspect?.Validate();
        }

        /// <summary>Запись для <paramref name="id"/>, иначе для <paramref name="fallbackId"/>, иначе значения по умолчанию.</summary>
        public static CubeAnimationConfig Resolve(ConfigTable<CubeAnimationConfig> table, string id, string fallbackId)
        {
            if (table != null)
            {
                if (!string.IsNullOrEmpty(id) && table.TryGet(id, out var own))
                    return own;
                if (!string.IsNullOrEmpty(fallbackId) && table.TryGet(fallbackId, out var common))
                    return common;
            }

            var defaults = new CubeAnimationConfig();
            if (fallbackId == PlayerId)
                defaults.LookAround = new LookAroundConfig();
            else
                defaults.Inspect = new WeaponInspectConfig();
            defaults.OnConfigLoaded();
            return defaults;
        }

        internal static float NonNegative(float v) => Math.Max(0f, v);
    }

    /// <summary>
    /// Цикл шагов: два шага на одно покачивание из стороны в сторону. В крайней точке покачивания куб «встаёт на ногу» —
    /// сжимается вниз и расширяется в стороны, посередине (между шагами) вытягивается вверх и сужается.
    /// Частота — шагов в секунду, амплитуды — на скорости ходьбы и бега (между ними — плавно, медленнее ходьбы — меньше).
    /// </summary>
    [Preserve]
    public sealed class StepCycleConfig
    {
        /// <summary>Шагов в секунду на скорости ходьбы.</summary>
        public float WalkStepRate { get; set; } = 3.2f;

        /// <summary>Шагов в секунду на скорости бега.</summary>
        public float RunStepRate { get; set; } = 4.2f;

        /// <summary>Покачивание из стороны в сторону при ходьбе: смещение верха, доля куба.</summary>
        public float Bend { get; set; } = 0.07f;

        /// <summary>Покачивание при беге.</summary>
        public float RunBend { get; set; } = 0.09f;

        /// <summary>Сжатие на шаге при ходьбе (вниз, шире в стороны), доля высоты.</summary>
        public float Squash { get; set; } = 0.03f;

        /// <summary>Вытягивание между шагами при ходьбе (вверх, уже), доля высоты.</summary>
        public float Stretch { get; set; } = 0.02f;

        /// <summary>Сжатие на шаге при беге.</summary>
        public float RunSquash { get; set; } = 0.08f;

        /// <summary>Вытягивание между шагами при беге.</summary>
        public float RunStretch { get; set; } = 0.06f;

        /// <summary>Наклон верха по ходу движения на скорости ходьбы, доля куба (на бегу — пропорционально больше).</summary>
        public float Lean { get; set; } = 0.06f;

        /// <summary>За сколько секунд цикл шагов появляется после старта/приземления и гаснет при остановке.</summary>
        public float FadeTime { get; set; } = 0.12f;

        internal void Validate()
        {
            WalkStepRate = CubeAnimationConfig.NonNegative(WalkStepRate);
            RunStepRate = CubeAnimationConfig.NonNegative(RunStepRate);
            Bend = CubeAnimationConfig.NonNegative(Bend);
            RunBend = CubeAnimationConfig.NonNegative(RunBend);
            Squash = Math.Min(0.5f, CubeAnimationConfig.NonNegative(Squash));
            Stretch = Math.Min(0.5f, CubeAnimationConfig.NonNegative(Stretch));
            RunSquash = Math.Min(0.5f, CubeAnimationConfig.NonNegative(RunSquash));
            RunStretch = Math.Min(0.5f, CubeAnimationConfig.NonNegative(RunStretch));
            Lean = CubeAnimationConfig.NonNegative(Lean);
            FadeTime = CubeAnimationConfig.NonNegative(FadeTime);
        }
    }

    /// <summary>Дыхание стоя: вдох — куб вытягивается вверх и сужается, выдох — оседает и расширяется (объём тот же).</summary>
    [Preserve]
    public sealed class BreathConfig
    {
        /// <summary>Длительность одного вдоха-выдоха, секунды.</summary>
        public float Period { get; set; } = 3.2f;

        /// <summary>Амплитуда сжатия-вытягивания, доля высоты.</summary>
        public float Amount { get; set; } = 0.03f;

        /// <summary>Доля периода на вдох (остальное — выдох): меньше 0.5 — короткий вдох и долгий выдох.</summary>
        public float Inhale { get; set; } = 0.42f;

        /// <summary>За сколько секунд дыхание появляется, когда куб встал; гаснет при любом действии втрое быстрее.</summary>
        public float FadeTime { get; set; } = 0.4f;

        internal void Validate()
        {
            Period = Math.Max(0.2f, Period);
            Amount = Math.Min(0.3f, CubeAnimationConfig.NonNegative(Amount));
            Inhale = Math.Max(0.1f, Math.Min(0.9f, Inhale));
            FadeTime = CubeAnimationConfig.NonNegative(FadeTime);
        }
    }

    /// <summary>Моргание: глаза сплющиваются по высоте и раскрываются.</summary>
    [Preserve]
    public sealed class BlinkConfig
    {
        /// <summary>Моргать ли вообще.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Пауза между морганиями — случайная в этих пределах, секунды.</summary>
        public float IntervalMin { get; set; } = 2.4f;
        public float IntervalMax { get; set; } = 5.5f;

        /// <summary>Длительность одного моргания (закрыть — открыть), секунды.</summary>
        public float Duration { get; set; } = 0.16f;

        /// <summary>Вероятность двойного моргания 0..1.</summary>
        public float DoubleChance { get; set; } = 0.2f;

        /// <summary>Высота закрытого глаза, доля открытого (0 — щёлка в линию).</summary>
        public float Closed { get; set; } = 0.12f;

        /// <summary>Моргать и в движении. false — только стоя: любое действие открывает глаза.</summary>
        public bool WhileMoving { get; set; }

        internal void Validate()
        {
            IntervalMin = Math.Max(0.2f, IntervalMin);
            IntervalMax = Math.Max(IntervalMin, IntervalMax);
            Duration = Math.Max(0.02f, Duration);
            DoubleChance = Math.Max(0f, Math.Min(1f, DoubleChance));
            Closed = Math.Max(0f, Math.Min(1f, Closed));
        }
    }

    /// <summary>
    /// Осмотр по сторонам: после <see cref="Delay"/> секунд покоя глаза уходят в сторону, следом скручивается верх
    /// куба (низ стоит), пауза, потом то же в другую сторону и обратно. Повторяется, пока игрок стоит.
    /// </summary>
    [Preserve]
    public sealed class LookAroundConfig
    {
        /// <summary>Сколько секунд стоять без действий до первого осмотра.</summary>
        public float Delay { get; set; } = 8f;

        /// <summary>Пауза до следующего осмотра — случайная в этих пределах, секунды.</summary>
        public float RepeatMin { get; set; } = 15f;
        public float RepeatMax { get; set; } = 25f;

        /// <summary>Длительность одного осмотра (влево и вправо), секунды.</summary>
        public float Duration { get; set; } = 3.4f;

        /// <summary>Скручивание верха куба в сторону, градусы у верха.</summary>
        public float Twist { get; set; } = 32f;

        /// <summary>Сдвиг глаз по передней грани в сторону взгляда, доля куба.</summary>
        public float EyeShift { get; set; } = 0.1f;

        /// <summary>Лёгкий наклон назад на поворотах (как будто приподнимает голову), градусы.</summary>
        public float Pitch { get; set; } = 3f;

        internal void Validate()
        {
            Delay = CubeAnimationConfig.NonNegative(Delay);
            RepeatMin = CubeAnimationConfig.NonNegative(RepeatMin);
            RepeatMax = Math.Max(RepeatMin, RepeatMax);
            Duration = Math.Max(0.5f, Duration);
            Twist = Math.Min(80f, Twist);
            EyeShift = Math.Max(0f, Math.Min(0.2f, EyeShift));
        }
    }

    /// <summary>
    /// Редкий idle врага: берёт оружие перед собой и перебрасывает его из руки в руку — оружие летит дугой
    /// перед кубом, переворачиваясь в воздухе, куб «ловит» его (чуть приседает), глаза и верх тела следят за ним.
    /// После последнего броска оружие снова в правой руке и возвращается в стойку.
    /// </summary>
    [Preserve]
    public sealed class WeaponInspectConfig
    {
        /// <summary>Сколько секунд стоять в покое до первого раза (плюс случайная пауза).</summary>
        public float Delay { get; set; } = 6f;

        /// <summary>Пауза между разами — случайная в этих пределах, секунды покоя.</summary>
        public float IntervalMin { get; set; } = 18f;
        public float IntervalMax { get; set; } = 30f;

        /// <summary>Длительность целиком (взять перед собой, перебросить, вернуть в стойку), секунды.</summary>
        public float Duration { get; set; } = 4.2f;

        /// <summary>Доли длительности на подъём перед собой и на возврат в стойку.</summary>
        public float Raise { get; set; } = 0.15f;
        public float Lower { get; set; } = 0.15f;

        /// <summary>Сколько бросков из руки в руку (чётное — оружие вернётся в правую руку).</summary>
        public int Tosses { get; set; } = 4;

        /// <summary>Доля каждого броска, когда оружие лежит в руке (поймал, подкинул), остальное — полёт.</summary>
        public float Hold { get; set; } = 0.35f;

        /// <summary>Высота дуги броска над руками, доля куба.</summary>
        public float TossHeight { get; set; } = 0.22f;

        /// <summary>Оборотов оружия в полёте за бросок (в плоскости перед кубом); 0 — летит стоя, рукоятью вниз.</summary>
        public float Spin { get; set; } = 0f;

        /// <summary>Расстояние между «руками» по бокам куба (откуда и куда летит оружие), доля куба; больше 1 — руки снаружи куба.</summary>
        public float Span { get; set; } = 1.2f;

        /// <summary>Приседание куба, когда он ловит оружие, доля высоты.</summary>
        public float CatchSquash { get; set; } = 0.04f;

        /// <summary>Поклон куба к оружию, градусы у верха.</summary>
        public float Pitch { get; set; } = 6f;

        /// <summary>Скручивание вслед за оружием, градусы.</summary>
        public float FollowTwist { get; set; } = 8f;

        /// <summary>Насколько глаза следят за оружием, доля куба (макс. сдвиг).</summary>
        public float EyeShift { get; set; } = 0.08f;

        internal void Validate()
        {
            Delay = CubeAnimationConfig.NonNegative(Delay);
            IntervalMin = CubeAnimationConfig.NonNegative(IntervalMin);
            IntervalMax = Math.Max(IntervalMin, IntervalMax);
            Duration = Math.Max(0.5f, Duration);
            Raise = Math.Max(0.02f, Math.Min(0.45f, Raise));
            Lower = Math.Max(0.02f, Math.Min(0.45f, Lower));
            Tosses = Math.Max(1, Math.Min(12, Tosses));
            Hold = Math.Max(0f, Math.Min(0.9f, Hold));
            TossHeight = Math.Max(0f, Math.Min(1.5f, TossHeight));
            Span = Math.Max(0f, Math.Min(1.6f, Span));
            CatchSquash = Math.Max(0f, Math.Min(0.2f, CatchSquash));
            EyeShift = Math.Max(0f, Math.Min(0.2f, EyeShift));
        }
    }
}
