using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.Progression;
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

        /// <summary>Здоровье игрока и неуязвимость после попадания.</summary>
        public PlayerVitals Vitals { get; }

        /// <summary>Опыт: копится с осколков побеждённых врагов.</summary>
        public ReactiveProperty<int> Experience { get; } = new ReactiveProperty<int>();

        /// <summary>
        /// Id открытых дверей-срезок. Открытая срезка остаётся открытой навсегда (смерть и респаун её не закрывают);
        /// позже это уйдёт в сохранение.
        /// </summary>
        public HashSet<string> OpenedShortcuts { get; } = new HashSet<string>();

        /// <summary>
        /// Id подсказок на полу, чьё дело сделано (предмет подобран, дверь открыта): больше не показываются.
        /// Как и срезки, держится всю сессию; позже уйдёт в сохранение.
        /// </summary>
        public HashSet<string> CompletedHints { get; } = new HashSet<string>();

        /// <summary>Id костра, у которого игрок отдыхал последним (точка возрождения), null — ещё нигде.</summary>
        public string LastBonfire { get; set; }

        /// <summary>Апгрейды оружия (костёр). Оружие проверяет Upgrades.Has(itemId, tier).</summary>
        public WeaponUpgrades Upgrades { get; } = new WeaponUpgrades();

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
            var player = configs.Load<PlayerConfig>(ConfigKeys.Player);
            Stamina = new Stamina(player.Stamina);
            Vitals = new PlayerVitals(player.MaxHealth, player.HitInvulnerability);
            ControlScheme = new ReactiveProperty<CameraScheme>(configs.Load<CameraConfig>(ConfigKeys.Camera).Scheme);
            foreach (var pair in configs.Load<GameConfig>(ConfigKeys.Game).DebugWeaponUpgrades)
                Upgrades.Set(pair.Key, pair.Value);
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
            if (inventoryConfig.AutoEquipOnPickup)
                EquipIfHandFree(item);

            events.Publish(new ItemPickedUp(item));
            return true;
        }

        /// <summary>
        /// Выдать предмет без подбора (стартовое снаряжение): в инвентарь и, если <paramref name="equip"/>, в свободную руку.
        /// Неизвестный id или полный инвентарь — false, событий не публикует.
        /// </summary>
        public bool GiveItem(string itemId, bool equip)
        {
            if (string.IsNullOrEmpty(itemId) || !items.Exists(itemId))
                return false;
            var item = items.Create(itemId);
            if (!Inventory.TryAdd(item))
                return false;
            if (equip)
                EquipIfHandFree(item);
            return true;
        }

        private void EquipIfHandFree(Item item)
        {
            if (!item.Config.IsWeapon)
                return;
            var hand = Inventory.ChooseHand(item);
            var free = item.Config.IsTwoHanded
                ? Inventory.Left == null && Inventory.Right == null
                : Inventory.InHand(hand) == null;
            if (free)
                Inventory.Equip(item, hand);
        }
    }
}
