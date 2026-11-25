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
    }
}
