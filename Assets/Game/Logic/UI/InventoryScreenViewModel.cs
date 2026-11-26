using System;
using System.Collections.Generic;
using ColorsAction.Inventory;
using ColorsAction.Progression;
using GameCore.UI;

namespace ColorsAction.UI
{
    /// <summary>
    /// Модель экрана инвентаря (I / Select, GDD v0.3, раздел 9): только предметы — сетка и быстрые слоты 1–4 крестовиной,
    /// как в HUD, — и 3D-превью персонажа с оружием в руках. Оружие здесь не меняется: оно на колесе (TAB / Y).
    /// Курсор для геймпада, перетаскивание мышью, зелёная подсветка слотов 1–4 для расходника.
    /// <para>Раскладка для навигации (столбец, ряд): крестовина под превью — слот 1 (1,3) вверху, 2 (2,4) справа,
    /// 3 (1,5) внизу, 4 (0,4) слева (как D-pad ↑ → ↓ ←); сетка — столбцы <see cref="GridColumnOffset"/>.., ряды 0...
    /// Вью рисует то же расположение.</para>
    /// </summary>
    public sealed class InventoryScreenViewModel : ViewModel, IExclusiveScreen
    {
        public const int GridColumnOffset = 4;

        /// <summary>Клетки крестовины (столбец, ряд) для слотов 1–4: ↑ → ↓ ←.</summary>
        public static readonly (int col, int row)[] CrossLayout = { (1, 3), (2, 4), (1, 5), (0, 4) };

        private readonly PlayerInventory inventory;
        private readonly WeaponUpgrades upgrades;
        private readonly Dictionary<SlotRef, (int col, int row)> layout = new Dictionary<SlotRef, (int, int)>();

        public PlayerInventory Inventory => inventory;

        /// <summary>Слот под курсором геймпада (и последний слот, на который навели мышь).</summary>
        public ReactiveProperty<SlotRef> Cursor { get; }

        /// <summary>Слот, из которого предмет "взят" (перетаскивание мышью или выбор A на геймпаде).</summary>
        public ReactiveProperty<SlotRef?> Held { get; } = new ReactiveProperty<SlotRef?>();

        /// <summary>Слоты, подсвеченные зелёным.</summary>
        public ReactiveProperty<IReadOnlyList<SlotRef>> Highlights { get; } =
            new ReactiveProperty<IReadOnlyList<SlotRef>>(Array.Empty<SlotRef>());

        /// <summary>Счётчик изменений содержимого: вью перерисовывает слоты при каждом изменении.</summary>
        public ReactiveProperty<int> Revision { get; } = new ReactiveProperty<int>();

        public ReactiveProperty<string> Description { get; } = new ReactiveProperty<string>(string.Empty);

        /// <summary>Подпись под превью: оружие в руках и его уровень прокачки.</summary>
        public ReactiveProperty<string> WeaponCaption { get; } = new ReactiveProperty<string>(string.Empty);

        public ReactiveCommand Close { get; }

        public InventoryScreenViewModel(PlayerInventory inventory, Action close, WeaponUpgrades upgrades = null)
        {
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.upgrades = upgrades;
            Close = Own(new ReactiveCommand(close));

            for (int i = 0; i < inventory.QuickCount; i++)
                layout[SlotRef.Quick(i)] = i < CrossLayout.Length ? CrossLayout[i] : (i - CrossLayout.Length, 6);
            for (int i = 0; i < inventory.GridSize; i++)
                layout[SlotRef.Grid(i)] = (GridColumnOffset + i % inventory.Columns, i / inventory.Columns);

            Cursor = Own(new ReactiveProperty<SlotRef>(SlotRef.Grid(0)));
            Own(Cursor.Subscribe(_ => Refresh(), false));
            Own(Held.Subscribe(_ => Refresh(), false));
            inventory.Changed += OnInventoryChanged;
            Refresh();
        }

        public IEnumerable<SlotRef> Slots => layout.Keys;

        public (int col, int row) PositionOf(SlotRef slot) => layout[slot];

        public Item ItemAt(SlotRef slot) => inventory.Get(slot);

        public bool IsHighlighted(SlotRef slot)
        {
            foreach (var s in Highlights.Value)
                if (s == slot)
                    return true;
            return false;
        }

        // ------------------------------------------------------------------ геймпад / клавиатура

        /// <summary>Сдвиг курсора по раскладке: ближайший слот в направлении (dx, dy), dy > 0 — вниз.</summary>
        public void Navigate(int dx, int dy)
        {
            if (dx == 0 && dy == 0)
                return;
            var (cc, cr) = layout[Cursor.Value];
            SlotRef? best = null;
            float bestScore = float.MaxValue;
            foreach (var pair in layout)
            {
                if (pair.Key == Cursor.Value)
                    continue;
                int ddx = pair.Value.col - cc;
                int ddy = pair.Value.row - cr;
                float along = ddx * dx + ddy * dy;
                if (along <= 0)
                    continue;
                float across = Math.Abs(dx != 0 ? ddy : ddx);
                float score = along + across * 2.5f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = pair.Key;
                }
            }

            if (best.HasValue)
                Cursor.Value = best.Value;
        }

        /// <summary>
        /// Кнопка A: предмет под курсором "берётся", следующее A кладёт его в выбранный слот
        /// (расходник — в слот 1–4 крестовины, или обратно в сетку).
        /// </summary>
        public void Confirm()
        {
            var slot = Cursor.Value;
            if (Held.Value.HasValue)
            {
                var from = Held.Value.Value;
                Held.Value = null;
                if (from != slot)
                    inventory.Move(from, slot);
                return;
            }

            if (inventory.Get(slot) != null)
                Held.Value = slot;
        }

        /// <summary>Кнопка B / Esc: отменить взятие или закрыть экран.</summary>
        public void Cancel()
        {
            if (Held.Value.HasValue)
            {
                Held.Value = null;
                return;
            }

            Close.Execute();
        }

        // ------------------------------------------------------------------ мышь

        public void Hover(SlotRef slot)
        {
            if (layout.ContainsKey(slot))
                Cursor.Value = slot;
        }

        public bool BeginDrag(SlotRef slot)
        {
            if (inventory.Get(slot) == null)
                return false;
            Cursor.Value = slot;
            Held.Value = slot;
            return true;
        }

        /// <summary>Отпускание над слотом. target == null — отпустили мимо слотов.</summary>
        public bool Drop(SlotRef? target)
        {
            var from = Held.Value;
            Held.Value = null;
            if (!from.HasValue || !target.HasValue)
                return false;
            var moved = inventory.Move(from.Value, target.Value);
            if (moved)
                Cursor.Value = target.Value;
            return moved;
        }

        /// <summary>
        /// Двойной щелчок или ПКМ по предмету: расходник из сетки — в первый свободный слот 1–4 (или в слот 1),
        /// из слота 1–4 — обратно в сетку.
        /// </summary>
        public bool QuickUse(SlotRef slot)
        {
            Cursor.Value = slot;
            Held.Value = null;
            var item = inventory.Get(slot);
            if (item == null)
                return false;
            if (slot.Kind == SlotKind.Quick)
            {
                var free = inventory.FreeGridSlot();
                return free >= 0 && inventory.Move(slot, SlotRef.Grid(free));
            }

            var targets = inventory.EquipTargets(item);
            if (targets.Count == 0)
                return false;
            foreach (var t in targets)
                if (inventory.Get(t) == null)
                    return inventory.Move(slot, t);
            return inventory.Move(slot, targets[0]);
        }

        // ------------------------------------------------------------------

        private void OnInventoryChanged()
        {
            Revision.Value++;
            Refresh();
        }

        private void Refresh()
        {
            var focus = Held.Value ?? Cursor.Value;
            var item = inventory.Get(focus);
            Highlights.Value = item != null ? (IReadOnlyList<SlotRef>)inventory.EquipTargets(item) : Array.Empty<SlotRef>();
            Description.Value = item == null ? string.Empty : Describe(item);
            WeaponCaption.Value = DescribeWeapon(inventory.Active);
        }

        private string DescribeWeapon(Item weapon)
        {
            if (weapon == null)
                return "Без оружия";
            var c = weapon.Config;
            var level = upgrades != null ? upgrades.Level(c.Id) : 0;
            return $"{c.Name ?? c.Id}  ·  прокачка {level}/{WeaponUpgrades.MaxTier}";
        }

        private static string Describe(Item item)
        {
            var c = item.Config;
            if (c.IsResource)
                return $"{c.Name} · ресурс для прокачки оружия у костра · {item.Count} шт.";
            if (c.Heal != null)
                return $"{c.Name} · лечит {c.Heal.HealFraction * 100f:0}% здоровья · заряды {item.Charges}/{item.MaxCharges} · в слот 1–4";
            return c.Name ?? c.Id;
        }

        protected override void OnDispose()
        {
            inventory.Changed -= OnInventoryChanged;
        }
    }
}
