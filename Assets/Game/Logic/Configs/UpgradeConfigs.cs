using System;
using System.Collections.Generic;
using ColorsAction.Progression;
using GameCore;
using GameCore.Config;

namespace ColorsAction.Configs
{
    /// <summary>
    /// Configs/Upgrades.json — апгрейды оружия у костра: id предмета из Items.json → ровно
    /// <see cref="WeaponUpgrades.MaxTier"/> последовательных ступеней (название, описание, цена в опыте).
    /// Сами способности включает оружие по <see cref="WeaponUpgrades.Has"/>.
    /// </summary>
    [Preserve]
    public sealed class WeaponUpgradeConfig : IIdentified, IConfigPostLoad
    {
        public string Id { get; set; }

        public List<UpgradeTierConfig> Tiers { get; set; } = new List<UpgradeTierConfig>();

        /// <summary>Ступень 1..MaxTier.</summary>
        public UpgradeTierConfig Tier(int tier) => tier >= 1 && tier <= Tiers.Count ? Tiers[tier - 1] : null;

        public void OnConfigLoaded()
        {
            Tiers ??= new List<UpgradeTierConfig>();
            if (Tiers.Count != WeaponUpgrades.MaxTier)
                throw new InvalidOperationException($"upgrades of '{Id}': need exactly {WeaponUpgrades.MaxTier} tiers, got {Tiers.Count}");
            for (int i = 0; i < Tiers.Count; i++)
            {
                if (Tiers[i] == null)
                    throw new InvalidOperationException($"upgrades of '{Id}': tier {i + 1} is empty");
                if (Tiers[i].Cost < 0)
                    throw new InvalidOperationException($"upgrades of '{Id}': tier {i + 1} cost is negative");
                Tiers[i].Name ??= string.Empty;
                Tiers[i].Description ??= string.Empty;
            }
        }
    }

    /// <summary>Одна ступень апгрейда оружия.</summary>
    [Preserve]
    public sealed class UpgradeTierConfig
    {
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>Цена в опыте (<see cref="GameSession.Experience"/>).</summary>
        public int Cost { get; set; }
    }
}
