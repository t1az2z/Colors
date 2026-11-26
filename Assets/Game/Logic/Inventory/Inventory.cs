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

    public enum Hand
    {
        Left = 0,
        Right = 1
    }

    public enum SlotKind
    {
        Grid,
        Hand,
        Quick
    }

    /// <summary>Адрес слота: клетка сетки, рука или быстрый слот 1–4.</summary>
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
        public static SlotRef Of(Hand hand) => new SlotRef(SlotKind.Hand, (int)hand);
        public static SlotRef Quick(int index) => new SlotRef(SlotKind.Quick, index);

        public Hand Hand => (Hand)Index;

        public bool Equals(SlotRef other) => Kind == other.Kind && Index == other.Index;
        public override bool Equals(object obj) => obj is SlotRef other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ Index;
        public static bool operator ==(SlotRef a, SlotRef b) => a.Equals(b);
        public static bool operator !=(SlotRef a, SlotRef b) => !a.Equals(b);
        public override string ToString() => $"{Kind}[{Index}]";
    }

    /// <summary>
    /// Инвентарь игрока: сетка (5×5 по умолчанию), две руки и быстрые слоты.
    /// <list type="bullet">
    /// <item>Одноручное оружие и щит встают в любую руку, двуручное занимает обе (одна и та же ссылка в обеих руках).</item>
    /// <item>Вытесненные из рук предметы встают на место перетаскиваемого или в первую свободную клетку.</item>
    /// <item>Любая операция атомарна: при неудаче состояние не меняется.</item>
    /// </list>
    /// </summary>
    public sealed class PlayerInventory
    {
        private readonly Item[] grid;
        private readonly Item[] hands = new Item[2];
        private readonly Item[] quick;

        public int Columns { get; }
        public int Rows { get; }
        public int GridSize => grid.Length;
        public int QuickCount => quick.Length;

        /// <summary>Любое изменение содержимого.</summary>
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

        public Item Left => hands[(int)Hand.Left];
        public Item Right => hands[(int)Hand.Right];
        public Item InHand(Hand hand) => hands[(int)hand];

        public Item Get(SlotRef slot)
        {
            switch (slot.Kind)
            {
                case SlotKind.Grid: return slot.Index >= 0 && slot.Index < grid.Length ? grid[slot.Index] : null;
                case SlotKind.Hand: return slot.Index >= 0 && slot.Index < 2 ? hands[slot.Index] : null;
                case SlotKind.Quick: return slot.Index >= 0 && slot.Index < quick.Length ? quick[slot.Index] : null;
                default: return null;
            }
        }

        public bool IsValid(SlotRef slot)
        {
            switch (slot.Kind)
            {
                case SlotKind.Grid: return slot.Index >= 0 && slot.Index < grid.Length;
                case SlotKind.Hand: return slot.Index == 0 || slot.Index == 1;
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
            yield return SlotRef.Of(Hand.Left);
            yield return SlotRef.Of(Hand.Right);
            for (int i = 0; i < quick.Length; i++)
                yield return SlotRef.Quick(i);
        }

        /// <summary>Где сейчас лежит предмет.</summary>
        public SlotRef? Find(Item item)
        {
            if (item == null)
                return null;
            for (int i = 0; i < grid.Length; i++)
                if (grid[i] == item)
                    return SlotRef.Grid(i);
            if (hands[(int)Hand.Right] == item)
                return SlotRef.Of(Hand.Right);
            if (hands[(int)Hand.Left] == item)
                return SlotRef.Of(Hand.Left);
            for (int i = 0; i < quick.Length; i++)
                if (quick[i] == item)
                    return SlotRef.Quick(i);
            return null;
        }

        public bool Contains(Item item) => Find(item).HasValue;

        public int FreeGridSlot()
        {
            for (int i = 0; i < grid.Length; i++)
                if (grid[i] == null)
                    return i;
            return -1;
        }

        /// <summary>
        /// Кладёт предмет в первую свободную клетку сетки. Складываемый (ресурс) сначала досыпается в стопки того же
        /// предмета, остаток (не больше одной стопки) ложится в свободную клетку этим же экземпляром.
        /// </summary>
        public bool TryAdd(Item item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (Contains(item))
                return false;
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
            foreach (var item in Unique())
                if (item.Id == itemId)
                    total += item.Count;
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
            for (int i = hands.Length - 1; i >= 0 && left > 0; i--)
            {
                var item = hands[i];
                if (item == null || item.Id != itemId)
                    continue;
                var taken = Math.Min(left, item.Count);
                item.Count -= taken;
                left -= taken;
                if (item.Count <= 0)
                    ClearFromHands(item);
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>Может ли предмет в принципе лежать в слоте такого вида.</summary>
        public static bool Accepts(SlotRef slot, Item item)
        {
            if (item == null)
                return true;
            switch (slot.Kind)
            {
                case SlotKind.Grid: return true;
                case SlotKind.Hand: return item.Config.IsWeapon && AllowedIn(item, slot.Hand);
                case SlotKind.Quick: return item.Config.Kind == ItemKind.Consumable;
                default: return false;
            }
        }

        /// <summary>Можно ли надеть предмет в эту руку (ограничение из конфига; двуручное — в любую).</summary>
        public static bool AllowedIn(Item item, Hand hand)
        {
            if (item == null || !item.Config.IsWeapon)
                return false;
            switch (item.Config.Hand)
            {
                case HandRestriction.Left: return hand == Hand.Left;
                case HandRestriction.Right: return hand == Hand.Right;
                default: return true;
            }
        }

        /// <summary>Слоты, в которые предмет можно надеть: их интерфейс подсвечивает зелёным.</summary>
        public List<SlotRef> EquipTargets(Item item)
        {
            var result = new List<SlotRef>();
            if (item == null)
                return result;
            if (item.Config.IsWeapon)
            {
                if (item.Config.IsTwoHanded)
                {
                    result.Add(SlotRef.Of(Hand.Left));
                    result.Add(SlotRef.Of(Hand.Right));
                }
                else
                {
                    if (AllowedIn(item, Hand.Left))
                        result.Add(SlotRef.Of(Hand.Left));
                    if (AllowedIn(item, Hand.Right))
                        result.Add(SlotRef.Of(Hand.Right));
                }
            }
            else if (item.Config.Kind == ItemKind.Consumable)
            {
                for (int i = 0; i < quick.Length; i++)
                    result.Add(SlotRef.Quick(i));
            }

            return result;
        }

        /// <summary>
        /// Переносит предмет из слота в слот (перетаскивание мышью). Если в целевом слоте что-то есть,
        /// предметы меняются местами, когда это возможно, иначе вытесненный уходит в свободную клетку.
        /// </summary>
        public bool Move(SlotRef from, SlotRef to)
        {
            if (!IsValid(from) || !IsValid(to) || from == to)
                return false;
            var item = Get(from);
            if (item == null || !Accepts(to, item))
                return false;

            var snapshot = Snapshot();
            bool ok = to.Kind == SlotKind.Hand ? EquipInternal(item, to.Hand) : PlaceInternal(item, from, to);
            if (!ok)
            {
                Restore(snapshot);
                return false;
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>Надевает предмет в указанную руку.</summary>
        public bool Equip(Item item, Hand hand)
        {
            if (item == null || !item.Config.IsWeapon || !Contains(item) || !AllowedIn(item, hand))
                return false;
            var snapshot = Snapshot();
            if (!EquipInternal(item, hand))
            {
                Restore(snapshot);
                return false;
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Экипировка одной кнопкой (геймпад, A): оружие само выбирает руку.
        /// Двуручное — обе; щит — левая; одноручное — правая. Если предпочтительная рука занята, а другая свободна, берётся другая.
        /// </summary>
        public bool AutoEquip(Item item)
        {
            if (item == null || !item.Config.IsWeapon)
                return false;
            return Equip(item, ChooseHand(item));
        }

        public Hand ChooseHand(Item item)
        {
            if (item.Config.Hand == HandRestriction.Left)
                return Hand.Left;
            if (item.Config.Hand == HandRestriction.Right)
                return Hand.Right;
            var preferred = item.Config.Kind == ItemKind.Shield ? Hand.Left : Hand.Right;
            var other = preferred == Hand.Left ? Hand.Right : Hand.Left;
            if (item.Config.IsTwoHanded)
                return Hand.Right;
            if (InHand(preferred) != null && InHand(other) == null)
                return other;
            return preferred;
        }

        /// <summary>Снимает предмет из руки в первую свободную клетку.</summary>
        public bool Unequip(Hand hand)
        {
            var item = InHand(hand);
            if (item == null)
                return false;
            var free = FreeGridSlot();
            if (free < 0)
                return false;
            ClearFromHands(item);
            grid[free] = item;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Убирает предмет из инвентаря совсем (выброс, расход).</summary>
        public bool Remove(Item item)
        {
            if (!Contains(item))
                return false;
            RemoveEverywhere(item);
            Changed?.Invoke();
            return true;
        }

        // ------------------------------------------------------------------ internals

        private bool EquipInternal(Item item, Hand hand)
        {
            var origin = Find(item);
            if (!origin.HasValue || !AllowedIn(item, hand))
                return false;

            // Что окажется вытеснено.
            var displaced = new List<Item>();
            if (item.Config.IsTwoHanded)
            {
                AddUnique(displaced, Left);
                AddUnique(displaced, Right);
            }
            else
            {
                var current = InHand(hand);
                AddUnique(displaced, current);
            }

            displaced.Remove(item);
            RemoveEverywhere(item);
            foreach (var d in displaced)
                ClearFromHands(d);

            if (item.Config.IsTwoHanded)
            {
                hands[0] = item;
                hands[1] = item;
            }
            else
            {
                hands[(int)hand] = item;
            }

            // Вытесненные: сначала на место, откуда взяли новый предмет, затем в свободные клетки.
            var originSlot = origin.Value;
            foreach (var d in displaced)
            {
                if (originSlot.Kind == SlotKind.Hand && originSlot.Hand != hand && !item.Config.IsTwoHanded
                    && !d.Config.IsTwoHanded && InHand(originSlot.Hand) == null && AllowedIn(d, originSlot.Hand))
                {
                    hands[originSlot.Index] = d; // поменять руки местами
                    originSlot = SlotRef.Grid(-1);
                    continue;
                }

                if (originSlot.Kind == SlotKind.Grid && originSlot.Index >= 0 && grid[originSlot.Index] == null)
                {
                    grid[originSlot.Index] = d;
                    originSlot = SlotRef.Grid(-1);
                    continue;
                }

                var free = FreeGridSlot();
                if (free < 0)
                    return false;
                grid[free] = d;
            }

            return true;
        }

        private bool PlaceInternal(Item item, SlotRef from, SlotRef to)
        {
            var target = Get(to);
            if (target == item)
                return false;

            if (target == null)
            {
                RemoveEverywhere(item);
                Set(to, item);
                return true;
            }

            // Стопка на стопку того же ресурса: досыпать, остаток остаётся на месте.
            if (item.Config.IsStackable && target.Id == item.Id && target.Room > 0)
            {
                var moved = Math.Min(item.Count, target.Room);
                target.Count += moved;
                item.Count -= moved;
                if (item.Count <= 0)
                    RemoveEverywhere(item);
                return true;
            }

            // Из руки на занятую клетку: если там оружие, надеваем его в эту руку (обмен).
            if (from.Kind == SlotKind.Hand)
            {
                if (target.Config.IsWeapon && AllowedIn(target, from.Hand))
                {
                    RemoveEverywhere(item);
                    Set(to, null);
                    if (!EquipFromNowhere(target, from.Hand))
                        return false;
                    Set(to, item);
                    return true;
                }

                // Не оружие: снятый предмет — на клетку, а то, что там было, — в свободную.
                RemoveEverywhere(item);
                Set(to, item);
                var free = FreeGridSlot();
                if (free < 0)
                    return false;
                grid[free] = target;
                return true;
            }

            if (!Accepts(from, target))
                return false;
            Set(from, target);
            Set(to, item);
            return true;
        }

        /// <summary>Надеть предмет, которого сейчас нет ни в одном слоте.</summary>
        private bool EquipFromNowhere(Item item, Hand hand)
        {
            var free = FreeGridSlot();
            if (free < 0)
                return false;
            grid[free] = item;
            return EquipInternal(item, hand);
        }

        private void Set(SlotRef slot, Item item)
        {
            switch (slot.Kind)
            {
                case SlotKind.Grid: grid[slot.Index] = item; break;
                case SlotKind.Hand: hands[slot.Index] = item; break;
                case SlotKind.Quick: quick[slot.Index] = item; break;
            }
        }

        private void RemoveEverywhere(Item item)
        {
            for (int i = 0; i < grid.Length; i++)
                if (grid[i] == item)
                    grid[i] = null;
            ClearFromHands(item);
            for (int i = 0; i < quick.Length; i++)
                if (quick[i] == item)
                    quick[i] = null;
        }

        private void ClearFromHands(Item item)
        {
            if (item == null)
                return;
            if (hands[0] == item)
                hands[0] = null;
            if (hands[1] == item)
                hands[1] = null;
        }

        private static void AddUnique(List<Item> list, Item item)
        {
            if (item != null && !list.Contains(item))
                list.Add(item);
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

        /// <summary>Каждый предмет один раз (двуручное в обеих руках — одна ссылка).</summary>
        private IEnumerable<Item> Unique()
        {
            var seen = new HashSet<Item>();
            foreach (var slot in AllSlots())
            {
                var item = Get(slot);
                if (item != null && seen.Add(item))
                    yield return item;
            }
        }

        private Item[][] Snapshot() => new[] { (Item[])grid.Clone(), (Item[])hands.Clone(), (Item[])quick.Clone() };

        private void Restore(Item[][] snapshot)
        {
            Array.Copy(snapshot[0], grid, grid.Length);
            Array.Copy(snapshot[1], hands, hands.Length);
            Array.Copy(snapshot[2], quick, quick.Length);
        }
    }
}
