using System;
using System.Collections.Generic;
using ColorsAction.Configs;
using GameCore.Ticking;

namespace ColorsAction.Combat
{
    /// <summary>
    /// Оркестратор врагов: очередь на атаку игрока.
    /// <list type="bullet">
    /// <item>Удары разных врагов никогда не совпадают: удар следующего начинается не раньше, чем закончится удар
    /// предыдущего плюс <see cref="CombatConfig.AttackerGap"/>.</item>
    /// <item>Следующий в очереди начинает замах заранее, рассчитав его так, чтобы ударить сразу после текущего.</item>
    /// <item>Одновременно в атаке (замах или удар) — не больше <see cref="CombatConfig.MaxAttackers"/> врагов.</item>
    /// <item>Остальные ждут в очереди и заходят игроку за спину (это решает адаптер движка по <see cref="QueueIndex"/>).</item>
    /// </list>
    /// </summary>
    public sealed class CombatDirector : ITickable
    {
        private readonly CombatConfig config;
        private readonly List<Slot> attackers = new List<Slot>();
        private readonly List<int> attackerIds = new List<int>();
        private readonly List<int> queue = new List<int>();
        private double now;

        private struct Slot
        {
            public int Id;
            public double StrikeStart;
            public double StrikeEnd;
        }

        public CombatDirector(CombatConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>Враги, которые сейчас в атаке (замах, удар, фиксация, возврат).</summary>
        public IReadOnlyList<int> Attackers => attackerIds;

        /// <summary>Очередь ждущих права атаки, первый — следующий.</summary>
        public IReadOnlyList<int> Queue => queue;

        public bool IsAttacking(int enemyId) => attackerIds.Contains(enemyId);

        /// <summary>Место в очереди (0 — следующий), -1 — не в очереди.</summary>
        public int QueueIndex(int enemyId) => queue.IndexOf(enemyId);

        /// <summary>Встать в очередь на атаку (повторный вызов ничего не меняет).</summary>
        public void Enqueue(int enemyId)
        {
            if (!queue.Contains(enemyId) && !attackerIds.Contains(enemyId))
                queue.Add(enemyId);
        }

        /// <summary>Выйти из очереди (потерял игрока, погиб).</summary>
        public void Leave(int enemyId) => queue.Remove(enemyId);

        /// <summary>
        /// Запросить право начать замах. Учитывает очередь (раньше пропускаем стоящих впереди) и время:
        /// удар этого врага начнётся через <paramref name="windup"/> и не должен пересечься с чужими ударами.
        /// </summary>
        public bool TryAcquire(int enemyId, float windup, float strike)
        {
            if (attackerIds.Contains(enemyId))
                return true;
            if (attackers.Count >= Math.Max(1, config.MaxAttackers))
                return false;
            var place = queue.IndexOf(enemyId);
            if (place > 0 || (place < 0 && queue.Count > 0))
                return false; // впереди кто-то стоит

            var strikeStart = now + windup;
            foreach (var a in attackers)
            {
                if (strikeStart < a.StrikeEnd + config.AttackerGap)
                    return false; // ударил бы одновременно с текущим — подождём
            }

            attackers.Add(new Slot { Id = enemyId, StrikeStart = strikeStart, StrikeEnd = strikeStart + strike });
            attackerIds.Add(enemyId);
            queue.Remove(enemyId);
            return true;
        }

        /// <summary>Вернуть право атаки (атака закончилась, прервана или враг погиб).</summary>
        public void Release(int enemyId)
        {
            for (int i = attackers.Count - 1; i >= 0; i--)
            {
                if (attackers[i].Id == enemyId)
                    attackers.RemoveAt(i);
            }

            attackerIds.Remove(enemyId);
        }

        public void Tick(float deltaTime) => now += deltaTime;
    }
}
