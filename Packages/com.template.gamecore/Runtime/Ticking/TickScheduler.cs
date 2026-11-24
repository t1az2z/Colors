using System;
using System.Collections.Generic;

namespace GameCore.Ticking
{
    /// <summary>
    /// Раздаёт кадры логике (TickTackManager из Sparta2 плюс пауза и масштаб времени из TimescaleService).
    /// Движок вызывает <see cref="Tick"/>, <see cref="LateTick"/> и <see cref="FixedTick"/> из своего цикла,
    /// а в тестах их вызывают руками с любым шагом.
    /// Добавлять и убирать объекты можно прямо во время тика: убранный больше не вызывается,
    /// добавленный начнёт получать тики со следующего вызова.
    /// </summary>
    public sealed class TickScheduler
    {
        private readonly TickList<ITickable> ticks = new TickList<ITickable>();
        private readonly TickList<ILateTickable> lateTicks = new TickList<ILateTickable>();
        private readonly TickList<IFixedTickable> fixedTicks = new TickList<IFixedTickable>();
        private float timeScale = 1f;

        public bool Paused { get; set; }

        public float TimeScale
        {
            get => timeScale;
            set
            {
                if (value < 0f)
                    throw new ArgumentOutOfRangeException(nameof(value), "Time scale can't be negative");
                timeScale = value;
            }
        }

        /// <summary>Игровое время с учётом паузы и масштаба.</summary>
        public double Time { get; private set; }

        /// <summary>Реальное время с момента старта.</summary>
        public double UnscaledTime { get; private set; }

        public long FrameCount { get; private set; }

        private float Scale => Paused ? 0f : timeScale;

        /// <summary>
        /// Подписывает объект на все интерфейсы тиков, которые он реализует.
        /// </summary>
        public void Add(object item, TickMode mode = TickMode.Scaled)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            var added = false;
            if (item is ITickable tickable)
                added |= ticks.Add(tickable, mode);
            if (item is ILateTickable late)
                added |= lateTicks.Add(late, mode);
            if (item is IFixedTickable fixedTickable)
                added |= fixedTicks.Add(fixedTickable, mode);

            if (!added)
                throw new ArgumentException($"{item.GetType().Name} doesn't implement any tick interface or is already added");
        }

        public void Remove(object item)
        {
            if (item is ITickable tickable)
                ticks.Remove(tickable);
            if (item is ILateTickable late)
                lateTicks.Remove(late);
            if (item is IFixedTickable fixedTickable)
                fixedTicks.Remove(fixedTickable);
        }

        public void Tick(float deltaTime)
        {
            var scale = Scale;
            FrameCount++;
            UnscaledTime += deltaTime;
            Time += deltaTime * scale;
            ticks.Run(deltaTime, scale, (t, dt) => t.Tick(dt));
        }

        public void LateTick(float deltaTime)
        {
            lateTicks.Run(deltaTime, Scale, (t, dt) => t.LateTick(dt));
        }

        public void FixedTick(float fixedDeltaTime)
        {
            fixedTicks.Run(fixedDeltaTime, Scale, (t, dt) => t.FixedTick(dt));
        }

        private sealed class TickList<T> where T : class
        {
            private readonly List<T> items = new List<T>();
            private readonly List<TickMode> modes = new List<TickMode>();
            private int running;
            private bool hasRemoved;

            public bool Add(T item, TickMode mode)
            {
                if (items.Contains(item))
                    return false;
                items.Add(item);
                modes.Add(mode);
                return true;
            }

            public void Remove(T item)
            {
                var index = items.IndexOf(item);
                if (index < 0)
                    return;

                if (running > 0)
                {
                    items[index] = null;
                    hasRemoved = true;
                }
                else
                {
                    items.RemoveAt(index);
                    modes.RemoveAt(index);
                }
            }

            public void Run(float deltaTime, float scale, Action<T, float> call)
            {
                running++;
                try
                {
                    var count = items.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var item = items[i];
                        if (item == null)
                            continue;

                        if (modes[i] == TickMode.Unscaled)
                            call(item, deltaTime);
                        else if (scale > 0f)
                            call(item, deltaTime * scale);
                    }
                }
                finally
                {
                    running--;
                    if (running == 0 && hasRemoved)
                        Compact();
                }
            }

            private void Compact()
            {
                hasRemoved = false;
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (items[i] != null)
                        continue;
                    items.RemoveAt(i);
                    modes.RemoveAt(i);
                }
            }
        }
    }
}
