using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using GameCore.Config;
using GameCore.Events;
using GameCore.UI;

namespace ColorsAction
{
    /// <summary>Игрок подобрал предмет.</summary>
    public readonly struct ItemPickedUp
    {
        public readonly Item Item;

        public ItemPickedUp(Item item)
        {
            Item = item;
        }
    }

    /// <summary>Не удалось подобрать предмет: инвентарь полон.</summary>
    public readonly struct InventoryFull
    {
        public readonly string ItemId;

        public InventoryFull(string itemId)
        {
            ItemId = itemId;
        }
    }

    /// <summary>Запрос открыть/закрыть инвентарь (TAB / View).</summary>
    public sealed class InventoryToggleRequested
    {
    }

    /// <summary>
    /// Состояние текущей игры, общее для состояний FSM и адаптеров движка:
    /// инвентарь игрока и флаг "игрок управляет персонажем".
    /// </summary>
    public sealed class GameSession
    {
        private readonly ItemDatabase items;
        private readonly IEventBus events;
        private readonly InventoryConfig inventoryConfig;

        public PlayerInventory Inventory { get; }

        public Stamina Stamina { get; }

        /// <summary>false, когда открыт экран (инвентарь): движение и атаки игнорируются.</summary>
        public ReactiveProperty<bool> PlayerControl { get; } = new ReactiveProperty<bool>(false);

        /// <summary>
        /// Текущая схема управления камерой и взглядом (GDD, раздел 3): А — свободная камера, Б — 4 фиксированных угла.
        /// Стартует со значения из Camera.json, в игре переключается для сравнения по ощущениям.
        /// </summary>
        public ReactiveProperty<CameraScheme> ControlScheme { get; }

        public GameSession(ConfigService configs, ItemDatabase items, IEventBus events)
        {
            this.items = items;
            this.events = events;
            inventoryConfig = configs.Load<InventoryConfig>(ConfigKeys.Inventory);
            Inventory = new PlayerInventory(inventoryConfig);
            Stamina = new Stamina(configs.Load<PlayerConfig>(ConfigKeys.Player).Stamina);
            ControlScheme = new ReactiveProperty<CameraScheme>(configs.Load<CameraConfig>(ConfigKeys.Camera).Scheme);
        }

        /// <summary>Переключить схему А ↔ Б.</summary>
        public CameraScheme ToggleControlScheme()
        {
            ControlScheme.Value = ControlScheme.Value == CameraScheme.FreeOrbit ? CameraScheme.FixedAngles : CameraScheme.FreeOrbit;
            return ControlScheme.Value;
        }

        /// <summary>Подбор предмета с земли. false — инвентарь полон, предмет остаётся лежать.</summary>
        public bool PickUp(string itemId)
        {
            var item = items.Create(itemId);
            if (!Inventory.TryAdd(item))
            {
                events.Publish(new InventoryFull(itemId));
                return false;
            }

            // Автоэкипировка (Inventory.json): оружие встаёт в свою руку, если она свободна.
            if (inventoryConfig.AutoEquipOnPickup && item.Config.IsWeapon)
            {
                var hand = Inventory.ChooseHand(item);
                var free = item.Config.IsTwoHanded
                    ? Inventory.Left == null && Inventory.Right == null
                    : Inventory.InHand(hand) == null;
                if (free)
                    Inventory.Equip(item, hand);
            }

            events.Publish(new ItemPickedUp(item));
            return true;
        }
    }
}
