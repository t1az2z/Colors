using System;
using ColorsAction.Configs;

namespace ColorsAction.Animation
{
    /// <summary>Положение оружия при перебрасывании (<see cref="CubeIdleAnimator.Toss"/>).</summary>
    public struct WeaponToss
    {
        /// <summary>Где оружие поперёк куба: +1 — правая рука, -1 — левая, между — в полёте.</summary>
        public float Side;

        /// <summary>Высота дуги броска 0..1 (1 — середина полёта).</summary>
        public float Arc;

        /// <summary>Доля переворота в текущем полёте 0..1.</summary>
        public float Flip;

        /// <summary>Направление переворота: +1 — бросок из правой руки, -1 — из левой.</summary>
        public float FlipDirection;

        /// <summary>Приседание в руке 0..1 (поймал — опустилось, подкинул — поднялось).</summary>
        public float Dip;

        /// <summary>Насколько оружие взято перед собой (0 — в стойке).</summary>
        public float Lift;

        public bool Flying;
    }

    /// <summary>
    /// Анимации покоя куба (игрок и враги), без движка: дыхание, моргание, осмотр по сторонам (игрок) и
    /// перебрасывание оружия из руки в руку (враги). Адаптер каждый кадр сообщает, стоит ли куб (<c>standing</c>)
    /// и было ли действие (<c>action</c>), и забирает значения для деформации.
    /// <para>Любое действие прерывает всё: счётчик покоя обнуляется, осмотр и перебрасывание быстро гаснут, моргание
    /// открывает глаза, дыхание затухает, если куб перестал стоять. Нажатие, после которого куб остался стоять
    /// (например, взаимодействие без цели), сбрасывает только счётчик покоя и редкие idle.</para>
    /// </summary>
    public sealed class CubeIdleAnimator
    {
        private const float TwoPi = (float)(Math.PI * 2.0);

        /// <summary>За сколько секунд гаснут осмотр и верчение после действия.</summary>
        public const float CancelTime = 0.15f;

        private readonly Random random;
        private CubeAnimationConfig config;

        private float breathWeight;
        private float breathTime;

        private float blinkTimer;
        // ≥ 0 — идёт моргание (секунды от начала), -1 — глаза открыты.
        private float blinkTime = -1f;
        // > 0 — пауза перед вторым морганием двойного.
        private float blinkGap;
        private bool blinkTwice;

        private float specialTime = -1f;
        private float specialWeight;
        private bool specialCancelled;
        private float nextSpecial;
        private float lookSide = 1f;

        public CubeIdleAnimator(CubeAnimationConfig config, int seed)
        {
            random = new Random(seed);
            Config = config;
            blinkTimer = NextBlinkDelay();
            nextSpecial = FirstSpecialDelay();
        }

        public CubeAnimationConfig Config
        {
            get => config;
            set => config = value ?? CubeAnimationConfig.Resolve(null, null, CubeAnimationConfig.PlayerId);
        }

        /// <summary>Сколько секунд куб стоит без действий.</summary>
        public float IdleTime { get; private set; }

        /// <summary>Добавка к сжатию от дыхания (вдох — вытянут, выдох — осел).</summary>
        public float BreathSquash { get; private set; }

        /// <summary>Закрытость глаз: 0 — открыты, 1 — закрыты.</summary>
        public float Blink { get; private set; }

        /// <summary>Добавка к скручиванию верха, градусы (осмотр, слежение за оружием).</summary>
        public float Twist { get; private set; }

        /// <summary>Добавка к поклону, градусы (плюс — вперёд).</summary>
        public float Pitch { get; private set; }

        /// <summary>Добавка к сжатию от редкого idle (присел, поймав оружие), складывается с дыханием.</summary>
        public float Squash { get; private set; }

        /// <summary>Сдвиг глаз по передней грани (доля куба): X — вправо, Y — вверх.</summary>
        public float EyeX { get; private set; }
        public float EyeY { get; private set; }

        /// <summary>Идёт осмотр по сторонам (игрок).</summary>
        public bool LookingAround => specialTime >= 0f && config.LookAround != null && !specialCancelled;

        /// <summary>Идёт верчение оружия (враг).</summary>
        public bool Inspecting => specialTime >= 0f && config.Inspect != null && config.LookAround == null && !specialCancelled;

        /// <summary>Доля верчения оружия 0..1; -1 — не вертит. После прерывания — замершая доля, пока гаснет вес.</summary>
        public float InspectProgress =>
            specialTime >= 0f && config.Inspect != null && config.LookAround == null ? Clamp01(specialTime / config.Inspect.Duration) : -1f;

        /// <summary>Вес верчения оружия 0..1: 1 — идёт, после действия быстро падает до 0.</summary>
        public float InspectWeight => config.Inspect != null && config.LookAround == null ? specialWeight : 0f;

        /// <summary>
        /// Кадр. <paramref name="standing"/> — куб стоит на земле и ничего не делает (может дышать);
        /// <paramref name="action"/> — в этом кадре было действие (нажатие, поворот, удар по кубу), прерывает idle.
        /// </summary>
        public void Tick(float dt, bool standing, bool action)
        {
            if (dt < 0f)
                dt = 0f;
            var calm = standing && !action;
            if (calm)
                IdleTime += dt;
            else
                Interrupt();

            UpdateBreath(dt, standing);
            UpdateBlink(dt, standing && !action);
            UpdateSpecial(dt, calm);
        }

        /// <summary>Прервать idle (удар по кубу, принудительный сброс): счётчик покоя с нуля, редкие idle гаснут.</summary>
        public void Interrupt()
        {
            IdleTime = 0f;
            if (specialTime >= 0f)
                specialCancelled = true;
            nextSpecial = FirstSpecialDelay();
            if ((blinkTime >= 0f || blinkGap > 0f) && !config.Blink.WhileMoving)
            {
                blinkTime = -1f;
                blinkGap = 0f;
                blinkTwice = false;
                blinkTimer = NextBlinkDelay();
            }
        }

        /// <summary>Сразу начать осмотр/верчение (отладка и снимки), если куб стоит.</summary>
        public void TriggerSpecial()
        {
            if (config.LookAround == null && config.Inspect == null)
                return;
            StartSpecial();
        }

        /// <summary>Сразу моргнуть (отладка и снимки).</summary>
        public void TriggerBlink()
        {
            blinkTime = 0f;
            blinkGap = 0f;
            blinkTwice = false;
        }

        // ------------------------------------------------------------------ дыхание

        private void UpdateBreath(float dt, bool standing)
        {
            var b = config.Breath;
            var fade = Math.Max(0.01f, b.FadeTime);
            // Встал — дыхание проявляется плавно; пошёл — гаснет втрое быстрее.
            breathWeight = standing
                ? Math.Min(1f, breathWeight + dt / fade)
                : Math.Max(0f, breathWeight - dt * 3f / fade);
            if (breathWeight <= 0f)
            {
                breathTime = 0f; // каждый раз начинает со вдоха
                BreathSquash = 0f;
                return;
            }

            breathTime += dt;
            BreathSquash = BreathWave(breathTime / b.Period, b.Inhale) * b.Amount * breathWeight;
        }

        /// <summary>
        /// Волна дыхания по доле периода: 0 → +1 за вдох (доля <paramref name="inhale"/>), потом плавно до -1 и
        /// обратно к 0 за выдох. Гладкая и непрерывная между периодами.
        /// </summary>
        public static float BreathWave(float cycles, float inhale)
        {
            var u = cycles - (float)Math.Floor(cycles);
            inhale = Math.Max(0.05f, Math.Min(0.95f, inhale));
            // Фаза синуса растягивается: на вдох уходит доля inhale, на выдох — остальное.
            var half = inhale * 0.5f;
            float phase;
            if (u < half)
                phase = u / half * 0.25f;                                   // 0 → пик вдоха
            else if (u < 1f - half)
                phase = 0.25f + (u - half) / (1f - inhale) * 0.5f;          // выдох: пик → дно
            else
                phase = 0.75f + (u - (1f - half)) / half * 0.25f;           // начало следующего вдоха
            return (float)Math.Sin(phase * TwoPi);
        }

        // ------------------------------------------------------------------ моргание

        private void UpdateBlink(float dt, bool calm)
        {
            var b = config.Blink;
            var allowed = b.Enabled && (calm || b.WhileMoving);
            if (blinkTime >= 0f)
            {
                blinkTime += dt;
                if (blinkTime >= b.Duration)
                {
                    blinkTime = -1f;
                    if (blinkTwice)
                    {
                        // Второе моргание сразу за первым, после короткой паузы.
                        blinkTwice = false;
                        blinkGap = b.Duration * 0.35f;
                    }
                    else
                        blinkTimer = NextBlinkDelay();
                }
            }
            else if (blinkGap > 0f)
            {
                blinkGap -= dt;
                if (blinkGap <= 0f)
                    blinkTime = 0f;
            }
            else if (allowed)
            {
                blinkTimer -= dt;
                if (blinkTimer <= 0f)
                {
                    blinkTime = 0f;
                    blinkTwice = random.NextDouble() < b.DoubleChance;
                }
            }

            Blink = allowed && blinkTime >= 0f ? BlinkCurve(blinkTime / b.Duration) : 0f;
        }

        /// <summary>Закрытость глаз по доле моргания: быстро закрыл, короткая пауза, раскрыл помедленнее.</summary>
        public static float BlinkCurve(float u)
        {
            if (u <= 0f || u >= 1f)
                return 0f;
            if (u < 0.35f)
                return Smooth(u / 0.35f);
            if (u < 0.5f)
                return 1f;
            return 1f - Smooth((u - 0.5f) / 0.5f);
        }

        private float NextBlinkDelay()
        {
            var b = config?.Blink ?? new BlinkConfig();
            return Range(b.IntervalMin, b.IntervalMax);
        }

        // ------------------------------------------------------------------ осмотр и верчение

        private float FirstSpecialDelay()
        {
            if (config == null)
                return float.MaxValue;
            if (config.LookAround != null)
                return config.LookAround.Delay;
            if (config.Inspect != null)
                return config.Inspect.Delay + Range(config.Inspect.IntervalMin, config.Inspect.IntervalMax) * 0.5f;
            return float.MaxValue;
        }

        private float NextSpecialDelay()
        {
            if (config.LookAround != null)
                return Range(config.LookAround.RepeatMin, config.LookAround.RepeatMax);
            if (config.Inspect != null)
                return Range(config.Inspect.IntervalMin, config.Inspect.IntervalMax);
            return float.MaxValue;
        }

        private void StartSpecial()
        {
            specialTime = 0f;
            specialCancelled = false;
            specialWeight = 1f;
            lookSide = random.NextDouble() < 0.5 ? -1f : 1f;
        }

        private float SpecialDuration =>
            config.LookAround != null ? config.LookAround.Duration : config.Inspect != null ? config.Inspect.Duration : 0f;

        private void UpdateSpecial(float dt, bool calm)
        {
            if (specialTime < 0f && calm && IdleTime >= nextSpecial && (config.LookAround != null || config.Inspect != null))
                StartSpecial();

            if (specialTime >= 0f)
            {
                if (specialCancelled)
                {
                    specialWeight = Math.Max(0f, specialWeight - dt / CancelTime);
                    if (specialWeight <= 0f)
                        EndSpecial();
                }
                else
                {
                    specialTime += dt;
                    if (specialTime >= SpecialDuration)
                    {
                        EndSpecial();
                        nextSpecial = IdleTime + NextSpecialDelay();
                    }
                }
            }

            Twist = Pitch = EyeX = EyeY = Squash = 0f;
            if (specialTime < 0f)
                return;
            var u = Clamp01(specialTime / Math.Max(0.01f, SpecialDuration));
            if (config.LookAround != null)
                LookAroundPose(config.LookAround, u, lookSide, specialWeight);
            else if (config.Inspect != null)
                InspectFollow(config.Inspect, u, specialWeight);
        }

        private void EndSpecial()
        {
            specialTime = -1f;
            specialCancelled = false;
            specialWeight = 0f;
        }

        // Осмотр: глаза ведут, тело догоняет. Ключи по доле осмотра: (время, значение -1..1).
        private static readonly float[] EyeKeys = { 0f, 0f, 0.07f, 1f, 0.44f, 1f, 0.5f, -1f, 0.84f, -1f, 0.9f, 0f, 1f, 0f };
        private static readonly float[] TwistKeys = { 0f, 0f, 0.1f, 0f, 0.27f, 1f, 0.44f, 1f, 0.64f, -1f, 0.82f, -1f, 1f, 0f };

        private void LookAroundPose(LookAroundConfig look, float u, float side, float weight)
        {
            var eye = Keyed(EyeKeys, u) * side;
            var twist = Keyed(TwistKeys, u) * side;
            // Мелкое «дрожание» взгляда на паузах — глаза не замирают.
            var drift = (float)Math.Sin(u * 23f) * 0.12f * Math.Abs(eye);
            EyeX = (eye + drift) * look.EyeShift * weight;
            EyeY = Math.Abs(twist) * look.EyeShift * 0.15f * weight;
            Twist = twist * look.Twist * weight;
            Pitch = -Math.Abs(twist) * look.Pitch * weight;
        }

        /// <summary>Насколько оружие взято перед собой: 0 — в стойке, 1 — перед кубом.</summary>
        public static float InspectLift(WeaponInspectConfig inspect, float u)
        {
            if (u < inspect.Raise)
                return Smooth(u / inspect.Raise);
            if (u > 1f - inspect.Lower)
                return 1f - Smooth((u - (1f - inspect.Lower)) / inspect.Lower);
            return 1f;
        }

        /// <summary>
        /// Перебрасывание оружия из руки в руку в момент <paramref name="u"/> (доля всего idle 0..1). Начинает и
        /// заканчивает в правой руке (при чётном числе бросков); каждый бросок — сначала оружие в руке (поймал,
        /// чуть присел, подкинул), потом полёт дугой к другой руке с переворотом.
        /// </summary>
        public static WeaponToss Toss(WeaponInspectConfig inspect, float u)
        {
            var toss = new WeaponToss { Side = 1f, FlipDirection = 1f, Lift = InspectLift(inspect, u) };
            var v = Clamp01((u - inspect.Raise) / Math.Max(0.01f, 1f - inspect.Raise - inspect.Lower));
            var n = Math.Max(1, inspect.Tosses);
            var k = Math.Min(n - 1, (int)Math.Floor(v * n));
            var w = v * n - k;
            var from = k % 2 == 0 ? 1f : -1f;
            toss.FlipDirection = from;
            if (w < inspect.Hold)
            {
                // В руке: поймал — присел и выпрямился, подкидывая.
                toss.Side = from;
                toss.Dip = inspect.Hold > 0f ? (float)Math.Sin(Math.PI * w / inspect.Hold) : 0f;
                return toss;
            }

            var f = Clamp01((w - inspect.Hold) / Math.Max(0.01f, 1f - inspect.Hold));
            toss.Side = from * (1f - 2f * f);
            toss.Arc = 4f * f * (1f - f);
            toss.Flip = f;
            toss.Flying = f > 0f && f < 1f;
            return toss;
        }

        private void InspectFollow(WeaponInspectConfig inspect, float u, float weight)
        {
            var toss = Toss(inspect, u);
            var lift = toss.Lift * weight;
            // Глаза и верх куба следят за оружием весь полёт: вбок — за рукой/полётом, вверх — по дуге броска.
            EyeX = toss.Side * inspect.EyeShift * lift;
            EyeY = (0.15f + toss.Arc * 0.85f) * inspect.EyeShift * lift;
            Twist = toss.Side * inspect.FollowTwist * lift;
            Pitch = inspect.Pitch * lift;
            Squash = -toss.Dip * inspect.CatchSquash * lift;
        }

        // ------------------------------------------------------------------ helpers

        private float Range(float min, float max) => min + (float)random.NextDouble() * Math.Max(0f, max - min);

        /// <summary>Значение по ключам (пары время-значение по возрастанию), между ключами — плавно.</summary>
        private static float Keyed(float[] keys, float u)
        {
            for (int i = 2; i < keys.Length; i += 2)
            {
                if (u > keys[i])
                    continue;
                var t0 = keys[i - 2];
                var t1 = keys[i];
                var k = t1 > t0 ? (u - t0) / (t1 - t0) : 1f;
                return keys[i - 1] + (keys[i + 1] - keys[i - 1]) * Smooth(k);
            }

            return keys[keys.Length - 1];
        }

        private static float Smooth(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
