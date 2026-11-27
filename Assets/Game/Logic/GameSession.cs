using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.Level;
using ColorsAction.Progression;
using ColorsAction.Status;
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

    /// <summary>Игрок получил ресурсы (из сундука): id предмета-ресурса, сколько и сколько теперь всего.</summary>
    public readonly struct ResourcesGained
    {
        public readonly ItemConfig Item;
        public readonly int Count;
        public readonly int Total;

        public ResourcesGained(ItemConfig item, int count, int total)
        {
            Item = item;
            Count = count;
            Total = total;
        }
    }

    /// <summary>Запрос открыть/закрыть инвентарь (I / Select).</summary>
    public sealed class InventoryToggleRequested
    {
    }

    /// <summary>Нажат быстрый слот 1–4: что произошло (лечилка использована, пуста, не нужна).</summary>
    public readonly struct QuickItemUsed
    {
        public readonly Item Item;
        public readonly QuickUseResult Result;

        /// <summary>Сколько здоровья восстановлено.</summary>
        public readonly float Healed;

        public QuickItemUsed(Item item, QuickUseResult result, float healed)
        {
            Item = item;
            Result = result;
            Healed = healed;
        }
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

        /// <summary>Статус-эффекты игрока (оглушение, уязвимость, усиление…). Тикает TickScheduler, смерть снимает всё.</summary>
        public StatusEffects PlayerStatus { get; }

        /// <summary>Конфиг статус-эффектов (StatusEffects.json; нет файла — встроенные значения).</summary>
        public StatusEffectsConfig StatusConfig { get; }

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

        /// <summary>Id разбитых стопок ящиков: как открытые срезки, больше не появляются (позже уйдёт в сохранение).</summary>
        public HashSet<string> BrokenCrates { get; } = new HashSet<string>();

        /// <summary>Id открытых сундуков: открытый сундук остаётся пустым навсегда (позже уйдёт в сохранение).</summary>
        public HashSet<string> OpenedChests { get; } = new HashSet<string>();

        /// <summary>Id опущенных разводных мостов: цепь порвана, створка лежит, проход открыт навсегда (позже уйдёт в сохранение).</summary>
        public HashSet<string> OpenedBridges { get; } = new HashSet<string>();

        /// <summary>Id открытых дверей на ключ: ключ потрачен, дверь открыта навсегда (позже уйдёт в сохранение).</summary>
        public HashSet<string> OpenedKeyDoors { get; } = new HashSet<string>();

        /// <summary>
        /// Id дверей, ключ от которых уже выпал из врага (лежит на земле, у игрока или потрачен): возрождённый враг его
        /// больше не носит. Позже уйдёт в сохранение вместе с лежащими ключами.
        /// </summary>
        public HashSet<string> DroppedKeys { get; } = new HashSet<string>();

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
            Vitals = new PlayerVitals(player.Health.Max, player.Health.HitInvulnerability);
            StatusConfig = configs.TryLoad<StatusEffectsConfig>(StatusEffectsConfig.Key, out var status)
                ? status
                : StatusEffectsConfig.CreateDefault();
            PlayerStatus = new StatusEffects(StatusConfig, new HealthStatusTarget(Vitals.Health));
            Vitals.Status = PlayerStatus;
            Vitals.Health.Died += PlayerStatus.Clear;
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
            // Ключ без двери ничего не открывает: ключи подбираются через PickUpKey.
            if (items.Exists(itemId) && items.Get(itemId).IsKey)
                return false;
            // Вторая рука другого оружия (щит меча) отдельным оружием не бывает: приходит только вместе с ним.
            if (items.IsOffhandOnly(itemId))
                return false;
            var item = items.Create(itemId);
            // То же оружие второй раз: на колесе оно уже есть, подбор просто убирает его с земли.
            if (item.Config.IsWeapon && Inventory.HasWeapon(itemId))
                return true;
            if (!Inventory.TryAdd(item))
            {
                events.Publish(new InventoryFull(itemId));
                return false;
            }

            // Автоэкипировка (Inventory.json): новое оружие берётся в руки, если в руках ничего нет.
            if (inventoryConfig.AutoEquipOnPickup)
                EquipIfHandsEmpty(item);

            events.Publish(new ItemPickedUp(item));
            return true;
        }

        /// <summary>
        /// Подбор ключа от двери <paramref name="doorId"/> (выпал из врага): ключ — отдельный предмет в сетке инвентаря.
        /// false — инвентарь полон (<see cref="InventoryFull"/>) или в Items.json нет предмета-ключа; ключ остаётся лежать.
        /// </summary>
        public bool PickUpKey(string doorId)
        {
            if (string.IsNullOrEmpty(doorId) || !items.Exists(KeyDoorRules.KeyItemId))
                return false;
            if (Inventory.FindKey(doorId) != null || OpenedKeyDoors.Contains(doorId))
                return true;
            var key = items.Create(KeyDoorRules.KeyItemId);
            key.KeyFor = doorId;
            if (!Inventory.TryAdd(key))
            {
                events.Publish(new InventoryFull(key.Id));
                return false;
            }

            events.Publish(new ItemPickedUp(key));
            return true;
        }

        /// <summary>Есть ли у игрока ключ от двери.</summary>
        public bool HasKey(string doorId) => Inventory.FindKey(doorId) != null;

        /// <summary>
        /// Открыть дверь на ключ: ключ уходит из инвентаря, дверь запоминается открытой. false — ключа нет
        /// (уже открытая дверь — true, ключ не нужен).
        /// </summary>
        public bool TryOpenKeyDoor(string doorId)
        {
            if (string.IsNullOrEmpty(doorId))
                return false;
            if (OpenedKeyDoors.Contains(doorId))
                return true;
            var key = Inventory.FindKey(doorId);
            if (key == null)
                return false;
            Inventory.Remove(key);
            OpenedKeyDoors.Add(doorId);
            return true;
        }

        /// <summary>
        /// Выдать предмет без подбора (стартовое снаряжение): в инвентарь (оружие — на колесо) и, если <paramref name="equip"/>,
        /// в руки, когда они пусты.
        /// Неизвестный id, вторая рука другого оружия (щит) или полный инвентарь — false, событий не публикует.
        /// </summary>
        public bool GiveItem(string itemId, bool equip)
        {
            // Ключ без двери ничего не открывает — выдаётся только через PickUpKey.
            if (string.IsNullOrEmpty(itemId) || !items.Exists(itemId) || items.IsOffhandOnly(itemId) || items.Get(itemId).IsKey)
                return false;
            var item = items.Create(itemId);
            if (!Inventory.TryAdd(item))
                return false;
            if (equip)
                EquipIfHandsEmpty(item);
            return true;
        }

        /// <summary>
        /// Выдать <paramref name="count"/> штук ресурса (сундук): досыпается в стопки инвентаря.
        /// Не помещается целиком — <see cref="InventoryFull"/> и false, ничего не выдаётся.
        /// </summary>
        public bool GiveResources(string itemId, int count)
        {
            if (string.IsNullOrEmpty(itemId) || !items.Exists(itemId))
                return false;
            if (count <= 0)
                return true;
            var config = items.Get(itemId);
            if (!Inventory.TryAddCount(config, count, () => items.Create(itemId)))
            {
                events.Publish(new InventoryFull(itemId));
                return false;
            }

            events.Publish(new ResourcesGained(config, count, Inventory.CountOf(itemId)));
            return true;
        }

        /// <summary>Поместится ли столько ресурса в инвентарь.</summary>
        public bool CanGiveResources(string itemId, int count) =>
            !string.IsNullOrEmpty(itemId) && items.Exists(itemId) && Inventory.Capacity(items.Get(itemId)) >= count;

        /// <summary>
        /// Нажат быстрый слот <paramref name="index"/> (клавиши 1–4): лечилка лечит и тратит заряд.
        /// Публикует <see cref="QuickItemUsed"/>, если в слоте что-то лежит.
        /// </summary>
        public QuickUseResult UseQuickItem(int index)
        {
            if (index < 0 || index >= Inventory.QuickCount)
                return QuickUseResult.Nothing;
            var item = Inventory.Get(SlotRef.Quick(index));
            if (item == null)
                return QuickUseResult.Nothing;
            var result = HealItems.TryUse(item, Vitals.Health, out var healed);
            if (result == QuickUseResult.Healed)
                Inventory.NotifyItemChanged();
            events.Publish(new QuickItemUsed(item, result, healed));
            return result;
        }

        /// <summary>Отдых у костра (и возрождение у него): заряды лечилок восполнены.</summary>
        public void RefillConsumables() => HealItems.Refill(Inventory);

        private void EquipIfHandsEmpty(Item item)
        {
            if (item.Config.IsWeapon && Inventory.Active == null)
                Inventory.SetActive(item);
        }
    }
}
