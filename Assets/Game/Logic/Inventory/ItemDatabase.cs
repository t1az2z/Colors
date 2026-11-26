using ColorsAction.Configs;
using GameCore.Config;

namespace ColorsAction.Inventory
{
    /// <summary>Конфиги предметов и выдача экземпляров с уникальным Uid.</summary>
    public sealed class ItemDatabase
    {
        private readonly ConfigService configs;
        private int nextUid = 1;

        public ItemDatabase(ConfigService configs)
        {
            this.configs = configs;
        }

        public ConfigTable<ItemConfig> Table => configs.LoadTable<ItemConfig>(ConfigKeys.Items);

        public ItemConfig Get(string id) => Table.Get(id);

        public bool Exists(string id) => Table.ContainsKey(id);

        public Item Create(string id) => new Item(nextUid++, Get(id));

        /// <summary>Предмет — вторая рука другого оружия (<see cref="ItemConfig.Offhand"/>): отдельно не подбирается.</summary>
        public bool IsOffhandOnly(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            foreach (var pair in Table)
                if (string.Equals(pair.Value.Offhand, id, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>Предмет второй руки оружия (null — нет или не найден).</summary>
        public ItemConfig OffhandOf(ItemConfig weapon)
        {
            if (weapon == null || string.IsNullOrEmpty(weapon.Offhand) || !Exists(weapon.Offhand))
                return null;
            return Get(weapon.Offhand);
        }
    }
}
