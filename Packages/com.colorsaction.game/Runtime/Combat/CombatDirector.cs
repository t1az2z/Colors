using System;
using System.Collections.Generic;
using ColorsAction.Configs;
using GameCore.Ticking;

namespace ColorsAction.Combat
{
    /// <summary>
    /// Оркестратор врагов: выдаёт право атаковать игрока. Одновременно атакует не больше
    /// <see cref="CombatConfig.MaxAttackers"/> врагов (по умолчанию один), между атаками разных врагов —
    /// пауза <see cref="CombatConfig.AttackerGap"/>. Остальные ждут на дистанции, не нападая.
    /// </summary>
    public sealed class CombatDirector : ITickable
    {
        private readonly CombatConfig config;
        private readonly List<int> attackers = new List<int>();
        private float gap;

        public CombatDirector(CombatConfig config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public IReadOnlyList<int> Attackers => attackers;

        public bool IsAttacking(int enemyId) => attackers.Contains(enemyId);

        /// <summary>Запросить право атаки. true — можно начинать замах.</summary>
        public bool TryAcquire(int enemyId)
        {
            if (attackers.Contains(enemyId))
                return true;
            if (attackers.Count >= Math.Max(1, config.MaxAttackers) || gap > 0f)
                return false;
            attackers.Add(enemyId);
            return true;
        }

        /// <summary>Вернуть право атаки (атака закончилась, прервана или враг погиб).</summary>
        public void Release(int enemyId)
        {
            if (attackers.Remove(enemyId))
                gap = config.AttackerGap;
        }

        public void Tick(float deltaTime)
        {
            if (gap > 0f)
                gap = Math.Max(0f, gap - deltaTime);
        }
    }
}
