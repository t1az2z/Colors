using System;
using System.Collections.Generic;
using ColorsAction.Configs;
using ColorsAction.Inventory;
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

        /// <summary>Следующая по очереди, но не хватает опыта или ресурса (что именно — <see cref="WeaponUpgradeShop.Missing"/>).</summary>
        TooExpensive,

        /// <summary>Сначала нужно купить предыдущую.</summary>
        Locked
    }

    /// <summary>Чего не хватает на следующую ступень.</summary>
    [Flags]
    public enum UpgradeShortage
    {
        None = 0,
        Experience = 1,
        Resource = 2
    }

    /// <summary>
    /// Покупка апгрейдов оружия у костра за опыт и ресурс оружия (GDD: опыт копится, у костра тратится на прокачку).
    /// Цены и тексты — Configs/Upgrades.json, состояние — <see cref="WeaponUpgrades"/>. Ресурс (металл, заклёпки,
    /// шестерёнки) лежит в инвентаре стопками и списывается при покупке; без инвентаря ресурсы не требуются.
    /// Апгрейды идут строго по порядку: купить можно только следующую ступень.
    /// </summary>
    public sealed class WeaponUpgradeShop
    {
        private readonly WeaponUpgrades upgrades;
        private readonly ReactiveProperty<int> experience;
        private readonly IReadOnlyDictionary<string, WeaponUpgradeConfig> configs;
        private readonly PlayerInventory inventory;
        private readonly Func<string, string> names;

        /// <param name="inventory">Откуда списываются ресурсы. null — ресурсы не требуются (только опыт).</param>
        /// <param name="names">Название ресурса по id для текстов (Items.json); null — сам id.</param>
        public WeaponUpgradeShop(WeaponUpgrades upgrades, ReactiveProperty<int> experience,
            IReadOnlyDictionary<string, WeaponUpgradeConfig> configs, PlayerInventory inventory = null,
            Func<string, string> names = null)
        {
            this.upgrades = upgrades ?? throw new ArgumentNullException(nameof(upgrades));
            this.experience = experience ?? throw new ArgumentNullException(nameof(experience));
            this.configs = configs ?? throw new ArgumentNullException(nameof(configs));
            this.inventory = inventory;
            this.names = names;
        }

        public WeaponUpgrades Upgrades => upgrades;

        public ReactiveProperty<int> Experience => experience;

        /// <summary>Инвентарь, из которого платятся ресурсы (null — ресурсы не нужны).</summary>
        public PlayerInventory Inventory => inventory;

        /// <summary>Id ресурса оружия или null.</summary>
        public string ResourceId(string itemId) =>
            itemId != null && configs.TryGetValue(itemId, out var config) && !string.IsNullOrEmpty(config.Resource) ? config.Resource : null;

        /// <summary>Название ресурса оружия для текстов («Металл»); пусто — ресурса нет.</summary>
        public string ResourceName(string itemId)
        {
            var id = ResourceId(itemId);
            if (id == null)
                return string.Empty;
            return names?.Invoke(id) ?? id;
        }

        /// <summary>Сколько ресурса оружия нужно на ступень (0 — ресурс не нужен).</summary>
        public int ResourceCost(string itemId, int tier) =>
            inventory != null && ResourceId(itemId) != null ? Math.Max(0, Tier(itemId, tier)?.Resources ?? 0) : 0;

        /// <summary>Сколько ресурса оружия у игрока.</summary>
        public int ResourceOwned(string itemId)
        {
            var id = ResourceId(itemId);
            return id != null && inventory != null ? inventory.CountOf(id) : 0;
        }

        /// <summary>Чего не хватает на ступень <paramref name="tier"/> (по ценам этой ступени).</summary>
        public UpgradeShortage Missing(string itemId, int tier)
        {
            var config = Tier(itemId, tier);
            if (config == null)
                return UpgradeShortage.None;
            var missing = UpgradeShortage.None;
            if (experience.Value < config.Cost)
                missing |= UpgradeShortage.Experience;
            if (ResourceOwned(itemId) < ResourceCost(itemId, tier))
                missing |= UpgradeShortage.Resource;
            return missing;
        }

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
            return cost >= 0 && Missing(itemId, tier) == UpgradeShortage.None ? UpgradeTierStatus.Available : UpgradeTierStatus.TooExpensive;
        }

        public bool CanBuy(string itemId) => Status(itemId, upgrades.Level(itemId) + 1) == UpgradeTierStatus.Available;

        /// <summary>Купить следующую ступень: списывает опыт и ресурс, повышает уровень оружия. false — нельзя.</summary>
        public bool TryBuy(string itemId)
        {
            if (!HasUpgrades(itemId) || !CanBuy(itemId))
                return false;
            var tier = upgrades.Level(itemId) + 1;
            var cost = NextCost(itemId);
            var resources = ResourceCost(itemId, tier);
            if (resources > 0 && !inventory.TryConsume(ResourceId(itemId), resources))
                return false;
            if (!upgrades.Upgrade(itemId))
                return false;
            experience.Value -= cost;
            return true;
        }
    }
}
