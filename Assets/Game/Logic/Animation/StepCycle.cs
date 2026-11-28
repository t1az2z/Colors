using System;
using ColorsAction.Configs;

namespace ColorsAction.Animation
{
    /// <summary>
    /// Цикл шагов куба (игрок и враги): фаза идёт по времени с частотой шагов, зависящей от скорости.
    /// Одно покачивание из стороны в сторону — два шага. В крайней точке покачивания куб «встаёт на ногу» —
    /// сжимается (<see cref="Squash"/> &lt; 0), посередине, между шагами, вытягивается (&gt; 0). Объём сохраняет
    /// деформация: сжатый куб шире, вытянутый — уже (<see cref="CubeShape"/>).
    /// <para>Выход — прямые добавки к форме (без пружин), поэтому амплитуды в конфиге точные при любой частоте шагов.</para>
    /// </summary>
    public sealed class StepCycle
    {
        private const float TwoPi = (float)(Math.PI * 2.0);

        private StepCycleConfig config;
        private float weight;

        public StepCycle(StepCycleConfig config) => this.config = config ?? new StepCycleConfig();

        public StepCycleConfig Config
        {
            get => config;
            set => config = value ?? new StepCycleConfig();
        }

        /// <summary>Фаза покачивания, радианы: sin — сторона наклона, два шага на 2π.</summary>
        public float Phase { get; private set; }

        /// <summary>Покачивание: смещение верха вбок (доля куба), квадратично по высоте.</summary>
        public float Sway { get; private set; }

        /// <summary>Сжатие (&lt;0) на шаге и вытягивание (&gt;0) между шагами, доля высоты.</summary>
        public float Squash { get; private set; }

        /// <summary>Насколько цикл виден: 0 — стоит/в воздухе, 1 — идёт.</summary>
        public float Weight => weight;

        /// <summary>Доля бега: 0 — скорость ходьбы и ниже, 1 — скорость бега.</summary>
        public float RunBlend { get; private set; }

        /// <summary>Шагов в секунду при скорости <paramref name="speed"/> (медленнее ходьбы — реже, но не до нуля).</summary>
        public float StepRate(float speed, float walkSpeed, float runSpeed)
        {
            var walk = Math.Max(0.01f, walkSpeed);
            if (speed <= walk)
                return config.WalkStepRate * Lerp(0.55f, 1f, Clamp01(speed / walk));
            var run = Clamp01((speed - walk) / Math.Max(0.01f, runSpeed - walk));
            return Lerp(config.WalkStepRate, config.RunStepRate, run);
        }

        /// <summary>
        /// Кадр: <paramref name="stepping"/> — куб на земле и сам идёт (не в отскоке, не в замахе прыжка).
        /// <paramref name="speed"/> — горизонтальная скорость, м/с.
        /// </summary>
        public void Tick(float dt, float speed, float walkSpeed, float runSpeed, bool stepping)
        {
            var walk = Math.Max(0.01f, walkSpeed);
            var moving = stepping && speed > walk * 0.05f;
            var fade = config.FadeTime;
            var target = moving ? 1f : 0f;
            weight = fade <= 0f ? target : MoveTowards(weight, target, dt / fade);

            if (moving)
            {
                // Одно покачивание (2π) — два шага: фаза растёт на π за шаг.
                Phase += (float)Math.PI * StepRate(speed, walk, runSpeed) * dt;
                if (Phase > TwoPi * 64f)
                    Phase -= TwoPi * 64f;
            }
            else if (weight <= 0f)
                Phase = 0f; // следующий старт — с середины, без рывка

            var amount = Clamp01(speed / walk) * weight;
            RunBlend = Clamp01((speed - walk) / Math.Max(0.01f, runSpeed - walk));
            var bend = Lerp(config.Bend, config.RunBend, RunBlend);
            var squash = Lerp(config.Squash, config.RunSquash, RunBlend);
            var stretch = Lerp(config.Stretch, config.RunStretch, RunBlend);

            Sway = (float)Math.Sin(Phase) * bend * amount;
            // cos(2·фаза): +1 посередине между шагами (вытянут), -1 в крайней точке покачивания (шаг, сжат).
            var wave = (float)Math.Cos(2f * Phase);
            Squash = (wave >= 0f ? wave * stretch : wave * squash) * amount;
        }

        public void Reset()
        {
            weight = 0f;
            Phase = 0f;
            Sway = 0f;
            Squash = 0f;
            RunBlend = 0f;
        }

        internal static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        internal static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        internal static float MoveTowards(float value, float target, float step) =>
            value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);
    }
}
