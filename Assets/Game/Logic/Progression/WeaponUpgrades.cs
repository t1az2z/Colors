using System;
using System.Collections.Generic;

namespace ColorsAction.Progression
{
    /// <summary>
    /// Прокачка оружия на костре: у каждого оружия (по id предмета из Items.json, например "sword")
    /// до <see cref="MaxTier"/> последовательных апгрейдов. Апгрейд N открывается только после N-1.
    /// Оружие спрашивает <see cref="Has"/>(id, tier), чтобы включить свою способность.
    /// Покупку (цена, UI костра) делает костёр; здесь только состояние.
    /// </summary>
    public sealed class WeaponUpgrades
    {
        public const int MaxTier = 3;

        private readonly Dictionary<string, int> levels = new Dictionary<string, int>();

        /// <summary>Уровень оружия изменился: (id предмета, новый уровень).</summary>
        public event Action<string, int> Changed;

        /// <summary>Сколько апгрейдов куплено (0..MaxTier).</summary>
        public int Level(string itemId)
        {
            return itemId != null && levels.TryGetValue(itemId, out var level) ? level : 0;
        }

        /// <summary>Открыт ли апгрейд номер <paramref name="tier"/> (1..MaxTier).</summary>
        public bool Has(string itemId, int tier)
        {
            return Level(itemId) >= tier;
        }

        public bool CanUpgrade(string itemId)
        {
            return !string.IsNullOrEmpty(itemId) && Level(itemId) < MaxTier;
        }

        /// <summary>Следующий апгрейд. false — уже максимум.</summary>
        public bool Upgrade(string itemId)
        {
            if (!CanUpgrade(itemId))
                return false;
            Set(itemId, Level(itemId) + 1);
            return true;
        }

        /// <summary>Задать уровень напрямую (отладка: Game.json debugWeaponUpgrades, будущие сохранения).</summary>
        public void Set(string itemId, int level)
        {
            if (string.IsNullOrEmpty(itemId))
                throw new ArgumentException("item id is required", nameof(itemId));
            level = Math.Max(0, Math.Min(MaxTier, level));
            if (Level(itemId) == level)
                return;
            levels[itemId] = level;
            Changed?.Invoke(itemId, level);
        }
    }
}
