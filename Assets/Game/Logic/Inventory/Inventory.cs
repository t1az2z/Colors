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

        public Item(int uid, ItemConfig config)
        {
            Uid = uid;
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Charges = MaxCharges;
        }

        public override string ToString() => $"{Id}#{Uid}";
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

        /// <summary>Кладёт предмет в первую свободную клетку сетки.</summary>
        public bool TryAdd(Item item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
            if (Contains(item))
                return false;
            var free = FreeGridSlot();
            if (free < 0)
                return false;
            grid[free] = item;
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

        private Item[][] Snapshot() => new[] { (Item[])grid.Clone(), (Item[])hands.Clone(), (Item[])quick.Clone() };

        private void Restore(Item[][] snapshot)
        {
            Array.Copy(snapshot[0], grid, grid.Length);
            Array.Copy(snapshot[1], hands, hands.Length);
            Array.Copy(snapshot[2], quick, quick.Length);
        }
    }
}
