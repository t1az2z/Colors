using System;
using System.Collections.Generic;
using ColorsAction.Configs;
using GameCore.UI;

namespace ColorsAction.Progression
{
    /// <summary>Состояние ступени апгрейда для экрана костра.</summary>
    public enum UpgradeTierStatus
    {
        /// <summary>Уже куплена.</summary>
        Owned,

        /// <summary>Следующая по очереди, опыта хватает.</summary>
        Available,

        /// <summary>Следующая по очереди, но опыта не хватает.</summary>
        TooExpensive,

        /// <summary>Сначала нужно купить предыдущую.</summary>
        Locked
    }

    /// <summary>
    /// Покупка апгрейдов оружия у костра за опыт (GDD: опыт копится, у костра тратится на прокачку).
    /// Цены и тексты — Configs/Upgrades.json, состояние — <see cref="WeaponUpgrades"/>.
    /// Апгрейды идут строго по порядку: купить можно только следующую ступень.
    /// </summary>
    public sealed class WeaponUpgradeShop
    {
        private readonly WeaponUpgrades upgrades;
        private readonly ReactiveProperty<int> experience;
        private readonly IReadOnlyDictionary<string, WeaponUpgradeConfig> configs;

        public WeaponUpgradeShop(WeaponUpgrades upgrades, ReactiveProperty<int> experience,
            IReadOnlyDictionary<string, WeaponUpgradeConfig> configs)
        {
            this.upgrades = upgrades ?? throw new ArgumentNullException(nameof(upgrades));
            this.experience = experience ?? throw new ArgumentNullException(nameof(experience));
            this.configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        public WeaponUpgrades Upgrades => upgrades;

        public ReactiveProperty<int> Experience => experience;

        /// <summary>Есть ли у предмета апгрейды в конфиге.</summary>
        public bool HasUpgrades(string itemId) => itemId != null && configs.ContainsKey(itemId);

        /// <summary>Ступень 1..MaxTier или null.</summary>
        public UpgradeTierConfig Tier(string itemId, int tier) =>
            itemId != null && configs.TryGetValue(itemId, out var config) ? config.Tier(tier) : null;

        /// <summary>Цена следующей ступени; -1 — апгрейдов нет или уже максимум.</summary>
        public int NextCost(string itemId)
        {
            if (!HasUpgrades(itemId) || !upgrades.CanUpgrade(itemId))
                return -1;
            return Tier(itemId, upgrades.Level(itemId) + 1)?.Cost ?? -1;
        }

        public UpgradeTierStatus Status(string itemId, int tier)
        {
            var level = upgrades.Level(itemId);
            if (tier <= level)
                return UpgradeTierStatus.Owned;
            if (tier > level + 1)
                return UpgradeTierStatus.Locked;
            var cost = NextCost(itemId);
            return cost >= 0 && experience.Value >= cost ? UpgradeTierStatus.Available : UpgradeTierStatus.TooExpensive;
        }

        public bool CanBuy(string itemId) => Status(itemId, upgrades.Level(itemId) + 1) == UpgradeTierStatus.Available;

        /// <summary>Купить следующую ступень: списывает опыт и повышает уровень оружия. false — нельзя.</summary>
        public bool TryBuy(string itemId)
        {
            if (!HasUpgrades(itemId) || !CanBuy(itemId))
                return false;
            var cost = NextCost(itemId);
            if (!upgrades.Upgrade(itemId))
                return false;
            experience.Value -= cost;
            return true;
        }
    }
}
