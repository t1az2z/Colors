using System;
using System.Collections.Generic;

namespace GameCore.Ticking
{
    public interface ITimerHandle
    {
        bool IsActive { get; }

        /// <summary>Доля прошедшего времени текущего интервала, от 0 до 1.</summary>
        float Progress { get; }

        void Cancel();
    }

    /// <summary>
    /// Отложенные и повторяющиеся вызовы на игровом времени. Сам является ITickable:
    /// его добавляют в <see cref="TickScheduler"/>, поэтому таймеры уважают паузу и масштаб времени.
    /// </summary>
    public sealed class TimerService : ITickable
    {
        private readonly List<Timer> timers = new List<Timer>();
        private readonly List<Timer> added = new List<Timer>();

        public int ActiveCount
        {
            get
            {
                var count = 0;
                foreach (var timer in timers)
                    if (timer.IsActive)
                        count++;
                foreach (var timer in added)
                    if (timer.IsActive)
                        count++;
                return count;
            }
        }

        public ITimerHandle After(float delay, Action callback)
        {
            return Schedule(delay, callback, 1);
        }

        /// <summary>
        /// Вызывает <paramref name="callback"/> каждые <paramref name="interval"/> секунд.
        /// <paramref name="times"/> меньше нуля означает бесконечный повтор.
        /// </summary>
        public ITimerHandle Every(float interval, Action callback, int times = -1)
        {
            if (interval <= 0f)
                throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive");
            return Schedule(interval, callback, times);
        }

        public void CancelAll()
        {
            foreach (var timer in timers)
                timer.Cancel();
            foreach (var timer in added)
                timer.Cancel();
        }

        public void Tick(float deltaTime)
        {
            if (added.Count > 0)
            {
                timers.AddRange(added);
                added.Clear();
            }

            for (int i = 0; i < timers.Count; i++)
                timers[i].Advance(deltaTime);

            timers.RemoveAll(t => !t.IsActive);
        }

        private ITimerHandle Schedule(float interval, Action callback, int times)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (times == 0)
                throw new ArgumentOutOfRangeException(nameof(times), "Timer must fire at least once");

            var timer = new Timer(Math.Max(0f, interval), callback, times);
            added.Add(timer);
            return timer;
        }

        private sealed class Timer : ITimerHandle
        {
            private readonly float interval;
            private readonly Action callback;
            private int remaining;
            private float elapsed;

            public bool IsActive { get; private set; } = true;
            public float Progress => interval <= 0f ? 1f : Math.Min(1f, elapsed / interval);

            public Timer(float interval, Action callback, int times)
            {
                this.interval = interval;
                this.callback = callback;
                remaining = times;
            }

            public void Cancel()
            {
                IsActive = false;
            }

            public void Advance(float deltaTime)
            {
                if (!IsActive)
                    return;

                elapsed += deltaTime;
                while (IsActive && elapsed >= interval)
                {
                    elapsed -= interval;
                    if (remaining > 0 && --remaining == 0)
                        IsActive = false;
                    callback();
                    if (interval <= 0f)
                        break;
                }
            }
        }
    }
}
