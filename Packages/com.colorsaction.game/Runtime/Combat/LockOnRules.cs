using System;
using System.Collections.Generic;

namespace ColorsAction.Combat
{
    /// <summary>Кандидат на фиксацию: позиция цели на плоскости XZ.</summary>
    public readonly struct LockOnCandidate
    {
        public readonly float X;
        public readonly float Z;

        public LockOnCandidate(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    /// <summary>
    /// Выбор цели для фиксации (без движка). Углы — как у персонажа: yaw = atan2(x, z) в градусах,
    /// поворот вправо увеличивает yaw.
    /// </summary>
    public static class LockOnRules
    {
        /// <summary>
        /// Цель при нажатии фиксации: ближайшая в радиусе <paramref name="range"/>, но цели в стороне от взгляда
        /// (направление камеры) "дальше" на <paramref name="anglePenalty"/> метров за каждые 90°.
        /// </summary>
        /// <returns>Индекс кандидата или -1.</returns>
        public static int Pick(float px, float pz, float viewYaw, IReadOnlyList<LockOnCandidate> candidates,
            float range, float anglePenalty)
        {
            var best = -1;
            var bestScore = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                var dx = candidates[i].X - px;
                var dz = candidates[i].Z - pz;
                var distance = (float)Math.Sqrt(dx * dx + dz * dz);
                if (distance > range)
                    continue;
                var angle = distance < 0.001f ? 0f : Math.Abs(DeltaAngle(viewYaw, Yaw(dx, dz)));
                var score = distance + anglePenalty * angle / 90f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// Переключение цели: ближайшая по углу вокруг игрока в сторону <paramref name="direction"/>
        /// (+1 — вправо, -1 — влево) от текущей. Текущая не выбирается.
        /// </summary>
        /// <returns>Индекс кандидата или -1, если в эту сторону никого нет.</returns>
        public static int Switch(float px, float pz, int current, IReadOnlyList<LockOnCandidate> candidates,
            int direction, float range)
        {
            if (current < 0 || current >= candidates.Count || direction == 0)
                return -1;
            var currentYaw = Yaw(candidates[current].X - px, candidates[current].Z - pz);
            var best = -1;
            var bestAngle = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i == current)
                    continue;
                var dx = candidates[i].X - px;
                var dz = candidates[i].Z - pz;
                if (dx * dx + dz * dz > range * range)
                    continue;
                var delta = DeltaAngle(currentYaw, Yaw(dx, dz)) * Math.Sign(direction);
                if (delta <= 0.5f || delta >= bestAngle)
                    continue;
                bestAngle = delta;
                best = i;
            }

            return best;
        }

        public static float Yaw(float dx, float dz) => (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);

        /// <summary>Кратчайшая разница углов в диапазоне (-180, 180].</summary>
        public static float DeltaAngle(float from, float to)
        {
            var d = (to - from) % 360f;
            if (d > 180f)
                d -= 360f;
            if (d <= -180f)
                d += 360f;
            return d;
        }
    }
}
