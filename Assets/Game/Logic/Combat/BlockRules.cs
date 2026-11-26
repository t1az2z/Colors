using System;

namespace ColorsAction.Combat
{
    /// <summary>Итог удара по блоку: сколько урона прошло и сколько выносливости списать.</summary>
    public readonly struct BlockOutcome
    {
        /// <summary>Урон, который всё же получает игрок.</summary>
        public readonly float Damage;

        /// <summary>Выносливость за принятый удар.</summary>
        public readonly float StaminaCost;

        /// <summary>Блок выдержал (выносливости хватило); false — блок пробит.</summary>
        public readonly bool Held;

        public BlockOutcome(float damage, float staminaCost, bool held)
        {
            Damage = damage;
            StaminaCost = staminaCost;
            Held = held;
        }
    }

    /// <summary>
    /// Блок удара спереди за выносливость. Щит гасит удар целиком (<c>absorb</c> = 1), дробовик — только часть
    /// (<c>absorb</c> &lt; 1). Цена — (начальная цена блока + погашенный урон × <see cref="CostPerDamage"/>) × множитель
    /// апгрейда. Выносливости не хватило — блок пробит: гасится только половина того, что погасил бы целый блок.
    /// </summary>
    public static class BlockRules
    {
        /// <summary>Выносливости за единицу погашенного урона.</summary>
        public const float CostPerDamage = 1.2f;

        public static BlockOutcome Resolve(float damage, float absorb, float stamina, float startCost, float costMultiplier)
        {
            damage = Math.Max(0f, damage);
            absorb = Math.Max(0f, Math.Min(1f, absorb));
            var blocked = damage * absorb;
            var cost = (startCost + blocked * CostPerDamage) * Math.Max(0f, costMultiplier);
            return stamina >= cost
                ? new BlockOutcome(damage - blocked, cost, true)
                : new BlockOutcome(damage - blocked * 0.5f, cost, false);
        }
    }
}
