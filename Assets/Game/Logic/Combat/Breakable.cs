using System;
using System.Collections.Generic;

namespace ColorsAction.Combat
{
    public enum BreakHitResult
    {
        /// <summary>Оружие не подходит (или объект уже разбит): урона нет.</summary>
        Ignored,

        /// <summary>Урон прошёл, объект ещё цел.</summary>
        Damaged,

        /// <summary>Этим ударом объект разбит.</summary>
        Broken
    }

    /// <summary>
    /// Здоровье разрушаемого объекта (ящики): ломается только оружием из списка, при нуле — разбит навсегда.
    /// </summary>
    public sealed class BreakableState
    {
        private readonly HashSet<string> breakableBy;

        public BreakableState(float hp, IEnumerable<string> breakableBy, bool broken = false)
        {
            MaxHp = Math.Max(0.01f, hp);
            Hp = broken ? 0f : MaxHp;
            this.breakableBy = new HashSet<string>(breakableBy ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        }

        public float MaxHp { get; }
        public float Hp { get; private set; }
        public bool Broken => Hp <= 0f;

        /// <summary>Доля оставшегося здоровья 0..1 (для трещин).</summary>
        public float Fraction => Hp / MaxHp;

        /// <summary>Ломает ли объект оружие с этим id. Пустой список — любое оружие.</summary>
        public bool Accepts(string weaponId) =>
            breakableBy.Count == 0 || (!string.IsNullOrEmpty(weaponId) && breakableBy.Contains(weaponId));

        public BreakHitResult Hit(string weaponId, float damage)
        {
            if (Broken || !Accepts(weaponId) || damage <= 0f)
                return BreakHitResult.Ignored;
            Hp = Math.Max(0f, Hp - damage);
            return Broken ? BreakHitResult.Broken : BreakHitResult.Damaged;
        }
    }
}
