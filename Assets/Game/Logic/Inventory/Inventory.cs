using System;
using System.Collections.Generic;
using ColorsAction.Configs;

namespace ColorsAction.Inventory
{
    /// <summary>Экземпляр предмета у игрока. Конфиг общий, Uid уникален.</summary>
    public sealed class Item
    {
        public int Uid { get; }
        public ItemConfig Config { get; }
        public string Id => Config.Id;

        /// <summary>Оставшиеся заряды расходуемого предмета (лечилки); у остальных 0.</summary>
        public int Charges { get; set; }

        /// <summary>Сколько зарядов у полного предмета.</summary>
        public int MaxCharges => Config.Heal?.Charges ?? 0;

        /// <summary>Сколько штук в стопке (ресурсы); у нескладываемых предметов всегда 1.</summary>
        public int Count { get; set; } = 1;

        /// <summary>Сколько ещё поместится в эту стопку.</summary>
        public int Room => Math.Max(0, Config.MaxStack - Count);

        public Item(int uid, ItemConfig config)
        {
            Uid = uid;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Charges = MaxCharges;
        }

        public override string ToString() => Count > 1 ? $"{Id}#{Uid}x{Count}" : $"{Id}#{Uid}";
    }

    /// <summary>
    /// Сторона куба, где оружие держится визуально (сокет модели). Рук как слотов больше нет:
    /// активное оружие — в правом сокете, его вторая рука (<see cref="ItemConfig.Offhand"/>, щит) — в левом.
    /// </summary>
    public enum Hand
    {
        Left = 0,
        Right = 1
    }

    public enum SlotKind
    {
        Grid,
        Quick
    }

    /// <summary>Адрес слота: клетка сетки или быстрый слот 1–4 (крестовина).</summary>
    public readonly struct SlotRef : IEquatable<SlotRef>
    {
        public readonly SlotKind Kind;
        public readonly int Index;

        public SlotRef(SlotKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        public static SlotRef Grid(int index) => new SlotRef(SlotKind.Grid, index);
        public static SlotRef Quick(int index) => new SlotRef(SlotKind.Quick, index);

        public bool Equals(SlotRef other) => Kind == other.Kind && Index == other.Index;
        public override bool Equals(object obj) => obj is SlotRef other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Index;
        public static bool operator ==(SlotRef a, SlotRef b) => a.Equals(b);
        public static bool operator !=(SlotRef a, SlotRef b) => !a.Equals(b);
        public override string ToString() => $"{Kind}[{Index}]";
    }

    /// <summary>
    /// Инвентарь игрока (GDD v0.3, раздел 9): рук нет.
    /// <list type="bullet">
    /// <item>Оружие в сетке не лежит: всё найденное оружие — в <see cref="Weapons"/> (на колесе, в секторе своего цвета),
    /// одно из них активно (<see cref="Active"/>). Сменить его можно только колесом.</item>
    /// <item>Сетка — расходники и ресурсы (стопками); быстрые слоты 1–4 — только расходники.</item>
    /// <item>Любая операция атомарна: при неудаче состояние не меняется.</item>
    /// </list>
    /// </summary>
    public sealed class PlayerInventory
    {
        private readonly Item[] grid;
        private readonly Item[] quick;
        private readonly List<Item> weapons = new List<Item>();

        public int Columns { get; }
        public int Rows { get; }
        public int GridSize => grid.Length;
        public int QuickCount => quick.Length;

        /// <summary>Любое изменение содержимого (в том числе смена активного оружия).</summary>
        public event Action Changed;

        public PlayerInventory(int columns = 5, int rows = 5, int quickSlots = 4)
        {
            if (columns <= 0 || rows <= 0 || quickSlots < 0)
                throw new ArgumentOutOfRangeException(nameof(columns));
            Columns = columns;
            Rows = rows;
            grid = new Item[columns * rows];
            quick = new Item[quickSlots];
        }

        public PlayerInventory(InventoryConfig config) : this(config.Columns, config.Rows, config.QuickSlots)
        {
        }

        // ------------------------------------------------------------------ оружие (колесо)

        /// <summary>Всё найденное оружие в порядке подбора.</summary>
        public IReadOnlyList<Item> Weapons => weapons;

        /// <summary>Оружие в руках (null — без оружия).</summary>
        public Item Active { get; private set; }

        public bool HasWeapon(string itemId)
        {
            foreach (var w in weapons)
                if (w.Id == itemId)
                    return true;
            return false;
        }

        /// <summary>Найденное оружие этого цвета (сектор колеса), null — нет.</summary>
        public Item WeaponOfColor(string color)
        {
            if (string.IsNullOrEmpty(color))
                return null;
            foreach (var w in weapons)
                if (string.Equals(w.Config.Color, color, StringComparison.OrdinalIgnoreCase))
                    return w;
            return null;
        }

        /// <summary>
        /// Оружие встаёт на колесо. То же оружие (тот же id) второй раз не добавляется — false.
        /// <paramref name="makeActive"/> — сразу взять в руки.
        /// </summary>
        public bool AddWeapon(Item item, bool makeActive)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (!item.Config.IsWeapon || weapons.Contains(item) || HasWeapon(item.Id))
                return false;
            weapons.Add(item);
            if (makeActive)
                Active = item;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Взять оружие в руки (колесо). null — убрать оружие. Чужое (не найденное) — false.</summary>
        public bool SetActive(Item weapon)
        {
            if (weapon != null && !weapons.Contains(weapon))
                return false;
            if (Active == weapon)
                return true;
            Active = weapon;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Взять в руки найденное оружие по id.</summary>
        public bool SetActive(string itemId)
        {
            foreach (var w in weapons)
                if (w.Id == itemId)
                    return SetActive(w);
            return false;
        }

        // ------------------------------------------------------------------ сетка и быстрые слоты

        public Item Get(SlotRef slot)
        {
            switch (slot.Kind)
            {
                case SlotKind.Grid: return slot.Index >= 0 && slot.Index < grid.Length ? grid[slot.Index] : null;
                case SlotKind.Quick: return slot.Index >= 0 && slot.Index < quick.Length ? quick[slot.Index] : null;
                default: return null;
            }
        }

        public bool IsValid(SlotRef slot)
        {
            switch (slot.Kind)
            {
                case SlotKind.Grid: return slot.Index >= 0 && slot.Index < grid.Length;
                case SlotKind.Quick: return slot.Index >= 0 && slot.Index < quick.Length;
                default: return false;
            }
        }

        /// <summary>Сообщить, что изменилось состояние предмета внутри (заряды): обновить экраны.</summary>
        public void NotifyItemChanged() => Changed?.Invoke();

        public IEnumerable<SlotRef> AllSlots()
        {
            for (int i = 0; i < grid.Length; i++)
                yield return SlotRef.Grid(i);
            for (int i = 0; i < quick.Length; i++)
                yield return SlotRef.Quick(i);
        }

        /// <summary>Где сейчас лежит предмет (оружие в слотах не лежит — null).</summary>
        public SlotRef? Find(Item item)
        {
            if (item == null)
                return null;
            for (int i = 0; i < grid.Length; i++)
                if (grid[i] == item)
                    return SlotRef.Grid(i);
            for (int i = 0; i < quick.Length; i++)
                if (quick[i] == item)
                    return SlotRef.Quick(i);
            return null;
        }

        /// <summary>Предмет у игрока: в слоте или на колесе.</summary>
        public bool Contains(Item item) => item != null && (weapons.Contains(item) || Find(item).HasValue);

        public int FreeGridSlot()
        {
            for (int i = 0; i < grid.Length; i++)
                if (grid[i] == null)
                    return i;
            return -1;
        }

        /// <summary>
        /// Подбор: оружие — на колесо (<see cref="AddWeapon"/>, не активным), остальное — в первую свободную клетку.
        /// Складываемый (ресурс) сначала досыпается в стопки того же предмета.
        /// </summary>
        public bool TryAdd(Item item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (Contains(item))
                return false;
            if (item.Config.IsWeapon)
                return AddWeapon(item, false);
            if (item.Config.IsStackable)
                return TryAddStackable(item);
            var free = FreeGridSlot();
            if (free < 0)
                return false;
            grid[free] = item;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Сколько всего штук предмета с этим id (сумма стопок).</summary>
        public int CountOf(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return 0;
            var total = 0;
            foreach (var slot in AllSlots())
            {
                var item = Get(slot);
                if (item != null && item.Id == itemId)
                    total += item.Count;
            }

            foreach (var w in weapons)
                if (w.Id == itemId)
                    total += w.Count;
            return total;
        }

        /// <summary>Сколько штук предмета ещё поместится: досыпать в стопки + свободные клетки.</summary>
        public int Capacity(ItemConfig config)
        {
            if (config == null)
                return 0;
            var room = 0;
            for (int i = 0; i < grid.Length; i++)
            {
                if (grid[i] == null)
                    room += config.MaxStack;
                else if (grid[i].Id == config.Id && config.IsStackable)
                    room += grid[i].Room;
            }

            return room;
        }

        /// <summary>
        /// Добавить <paramref name="count"/> штук складываемого предмета: досыпать в стопки, затем новыми стопками из
        /// <paramref name="create"/>. Не помещается целиком — false, инвентарь не меняется.
        /// </summary>
        public bool TryAddCount(ItemConfig config, int count, Func<Item> create)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (create == null)
                throw new ArgumentNullException(nameof(create));
            if (count <= 0)
                return true;
            if (Capacity(config) < count)
                return false;

            var left = Fill(config.Id, count);
            while (left > 0)
            {
                var free = FreeGridSlot();
                var stack = create();
                stack.Count = Math.Min(left, Math.Max(1, config.MaxStack));
                left -= stack.Count;
                grid[free] = stack;
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Потратить <paramref name="count"/> штук предмета (оплата прокачки). Не хватает — false, ничего не тратится.
        /// Пустые стопки убираются, тратится с последних клеток.
        /// </summary>
        public bool TryConsume(string itemId, int count)
        {
            if (count <= 0)
                return true;
            if (CountOf(itemId) < count)
                return false;
            var left = count;
            for (int i = grid.Length - 1; i >= 0 && left > 0; i--)
                left = Take(ref grid[i], itemId, left);
            for (int i = quick.Length - 1; i >= 0 && left > 0; i--)
                left = Take(ref quick[i], itemId, left);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Может ли предмет в принципе лежать в слоте такого вида. Оружие в слотах не лежит.</summary>
        public static bool Accepts(SlotRef slot, Item item)
        {
            if (item == null)
                return true;
            if (item.Config.IsWeapon)
                return false;
            switch (slot.Kind)
            {
                case SlotKind.Grid: return true;
                case SlotKind.Quick: return item.Config.Kind == ItemKind.Consumable;
                default: return false;
            }
        }

        /// <summary>Слоты, в которые предмет можно поставить: их интерфейс подсвечивает зелёным (расходник — слоты 1–4).</summary>
        public List<SlotRef> EquipTargets(Item item)
        {
            var result = new List<SlotRef>();
            if (item != null && item.Config.Kind == ItemKind.Consumable)
                for (int i = 0; i < quick.Length; i++)
                    result.Add(SlotRef.Quick(i));
            return result;
        }

        /// <summary>
        /// Переносит предмет из слота в слот (перетаскивание мышью, A на геймпаде). Занятый слот — обмен местами,
        /// если вытесненный может встать на место перенесённого, иначе он уходит в свободную клетку сетки.
        /// Стопка на стопку того же ресурса досыпается.
        /// </summary>
        public bool Move(SlotRef from, SlotRef to)
        {
            if (!IsValid(from) || !IsValid(to) || from == to)
                return false;
            var item = Get(from);
            if (item == null || !Accepts(to, item))
                return false;
            var target = Get(to);

            if (target == null)
            {
                Set(from, null);
                Set(to, item);
            }
            else if (item.Config.IsStackable && target.Id == item.Id && target.Room > 0)
            {
                var moved = Math.Min(item.Count, target.Room);
                target.Count += moved;
                item.Count -= moved;
                if (item.Count <= 0)
                    Set(from, null);
            }
            else if (Accepts(from, target))
            {
                Set(from, target);
                Set(to, item);
            }
            else
            {
                // Вытесненный не встаёт на старое место (например, ресурс в слот 1–4): в свободную клетку сетки.
                Set(from, null);
                var free = FreeGridSlot();
                if (free < 0)
                {
                    Set(from, item);
                    return false;
                }

                grid[free] = target;
                Set(to, item);
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>Убирает предмет из инвентаря совсем (выброс, расход). Активное оружие убирается из рук.</summary>
        public bool Remove(Item item)
        {
            if (!Contains(item))
                return false;
            if (weapons.Remove(item) && Active == item)
                Active = null;
            for (int i = 0; i < grid.Length; i++)
                if (grid[i] == item)
                    grid[i] = null;
            for (int i = 0; i < quick.Length; i++)
                if (quick[i] == item)
                    quick[i] = null;
            Changed?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ internals

        private void Set(SlotRef slot, Item item)
        {
            switch (slot.Kind)
            {
                case SlotKind.Grid: grid[slot.Index] = item; break;
                case SlotKind.Quick: quick[slot.Index] = item; break;
            }
        }

        private bool TryAddStackable(Item item)
        {
            var config = item.Config;
            var existing = 0;
            for (int i = 0; i < grid.Length; i++)
                if (grid[i] != null && grid[i].Id == config.Id)
                    existing += grid[i].Room;
            var rest = Math.Max(0, item.Count - existing);
            if (rest > config.MaxStack || (rest > 0 && FreeGridSlot() < 0))
                return false;
            var left = Fill(config.Id, item.Count);
            if (left > 0)
            {
                item.Count = left;
                grid[FreeGridSlot()] = item;
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>Досыпать в стопки сетки; возвращает, сколько не поместилось.</summary>
        private int Fill(string itemId, int count)
        {
            for (int i = 0; i < grid.Length && count > 0; i++)
            {
                var stack = grid[i];
                if (stack == null || stack.Id != itemId || !stack.Config.IsStackable)
                    continue;
                var add = Math.Min(count, stack.Room);
                stack.Count += add;
                count -= add;
            }

            return count;
        }

        private static int Take(ref Item slot, string itemId, int count)
        {
            if (slot == null || slot.Id != itemId)
                return count;
            var taken = Math.Min(count, slot.Count);
            slot.Count -= taken;
            if (slot.Count <= 0)
                slot = null;
            return count - taken;
        }
    }
}
