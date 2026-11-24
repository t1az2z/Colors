using System;

namespace GameCore.Rng
{
    public interface IRandom
    {
        /// <summary>Целое в диапазоне [min, maxExclusive).</summary>
        int Range(int min, int maxExclusive);

        /// <summary>Число в диапазоне [0, 1).</summary>
        float Value();
    }

    /// <summary>
    /// Детерминированный генератор (xorshift64*). Одинаков в Unity, Godot и тестах,
    /// а его состояние можно сохранить в сейв (RandomController из Sparta2) и восстановить.
    /// </summary>
    public sealed class SeededRandom : IRandom
    {
        private ulong state;

        public SeededRandom(ulong seed)
        {
            State = seed;
        }

        /// <summary>Текущее состояние. Сохраните и присвойте обратно, чтобы продолжить ту же последовательность.</summary>
        public ulong State
        {
            get => state;
            set => state = value == 0 ? 0x9E3779B97F4A7C15UL : value;
        }

        public ulong NextULong()
        {
            state ^= state >> 12;
            state ^= state << 25;
            state ^= state >> 27;
            return state * 0x2545F4914F6CDD1DUL;
        }

        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than min");
            var range = (ulong)((long)maxExclusive - min);
            return (int)((long)min + (long)(NextULong() % range));
        }

        public float Value()
        {
            return (NextULong() >> 40) / (float)(1UL << 24);
        }

        public bool Chance(float probability) => Value() < probability;
    }
}
