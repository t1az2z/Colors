using System;
using ColorsAction.Configs;

namespace ColorsAction.Combat
{
    /// <summary>
    /// Серия отскоков (без движка): какой шаг запустит следующее нажатие, буфер нажатия, окно комбо и пауза после шага.
    /// <para>Порядок за кадр: <see cref="Tick"/> → при нажатии <see cref="Press"/> → если <see cref="CanBegin"/> и
    /// внешние условия выполнены (на земле, удар можно прервать, хватает выносливости на <see cref="PendingStep"/>) —
    /// <see cref="Begin"/>.</para>
    /// </summary>
    public sealed class DodgeCombo
    {
        private readonly DodgeConfig config;
        private bool buffered;
        private float bufferTimer;
        private float cooldown;
        private float window;
        private int lastStep = -1;

        public DodgeCombo(DodgeConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            if (config.Steps == null || config.Steps.Count == 0)
                throw new ArgumentException("dodge config has no steps", nameof(config));
        }

        /// <summary>Идёт шаг отскока.</summary>
        public bool Active { get; private set; }

        /// <summary>Индекс текущего шага, а без отскока — последнего завершённого (-1 — ещё не было).</summary>
        public int StepIndex => lastStep;

        /// <summary>Текущий шаг (null, если отскока нет).</summary>
        public DodgeStepConfig Step => Active ? config.Steps[lastStep] : null;

        /// <summary>Время от начала текущего шага, секунды.</summary>
        public float Time { get; private set; }

        /// <summary>Доля пройденного шага 0..1 (0 без отскока).</summary>
        public float Progress => Active ? Math.Min(1f, Time / Math.Max(0.0001f, Step.Duration)) : 0f;

        /// <summary>Неуязвимость: внутри окна InvulnerableFrom..InvulnerableTo текущего шага.</summary>
        public bool Invulnerable
        {
            get
            {
                if (!Active)
                    return false;
                var t = Progress;
                return t >= Step.InvulnerableFrom && t <= Step.InvulnerableTo;
            }
        }

        /// <summary>Нажатие запомнено и ещё не истекло.</summary>
        public bool Buffered => buffered;

        /// <summary>Сколько ещё длится окно комбо после последнего шага, секунды (0 — серия начнётся заново).</summary>
        public float WindowLeft => Active ? 0f : Math.Max(0f, window);

        /// <summary>Индекс шага, который запустит нажатие сейчас.</summary>
        public int NextStep
        {
            get
            {
                var next = lastStep + 1;
                if (lastStep < 0 || next >= config.Steps.Count)
                    return 0;
                if (Active)
                    return next;
                return window > 0f ? next : 0;
            }
        }

        /// <summary>Шаг, который запустит нажатие сейчас (для проверки выносливости до <see cref="Begin"/>).</summary>
        public DodgeStepConfig PendingStep => config.Steps[NextStep];

        /// <summary>Есть запомненное нажатие, и серия позволяет начать шаг (пауза прошла или текущий шаг можно оборвать).</summary>
        public bool CanBegin
        {
            get
            {
                if (!buffered)
                    return false;
                if (Active)
                    return Step.ChainFrom < 1f && Progress >= Step.ChainFrom && lastStep + 1 < config.Steps.Count;
                return cooldown <= 0f;
            }
        }

        /// <summary>Нажатие отскока: помнится <see cref="DodgeConfig.InputBuffer"/> секунд.</summary>
        public void Press()
        {
            buffered = true;
            bufferTimer = config.InputBuffer;
        }

        /// <summary>Начинает шаг <see cref="NextStep"/> и тратит нажатие. Возвращает индекс шага.</summary>
        public int Begin()
        {
            var index = NextStep;
            buffered = false;
            bufferTimer = 0f;
            lastStep = index;
            Active = true;
            Time = 0f;
            window = 0f;
            cooldown = 0f;
            return index;
        }

        public void Tick(float dt)
        {
            if (buffered)
            {
                bufferTimer -= dt;
                if (bufferTimer < 0f)
                    buffered = false;
            }

            if (Active)
            {
                Time += dt;
                var step = Step;
                if (Time >= step.Duration)
                {
                    // Перелёт за конец шага засчитывается в паузу и окно — тайминг не зависит от FPS.
                    var over = Time - step.Duration;
                    Active = false;
                    cooldown = step.Cooldown - over;
                    // После последнего шага серия начинается заново.
                    window = lastStep + 1 < config.Steps.Count ? config.ComboWindow - over : 0f;
                }

                return;
            }

            cooldown -= dt;
            window -= dt;
        }

        /// <summary>Прервать отскок и сбросить серию (смерть, телепорт).</summary>
        public void Reset()
        {
            Active = false;
            buffered = false;
            bufferTimer = 0f;
            cooldown = 0f;
            window = 0f;
            lastStep = -1;
            Time = 0f;
        }
    }
}
