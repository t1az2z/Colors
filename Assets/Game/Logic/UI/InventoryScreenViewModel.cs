using System;
using System.Collections.Generic;
using ColorsAction.Inventory;
using GameCore.UI;

namespace ColorsAction.UI
{
    /// <summary>
    /// Модель экрана инвентаря (TAB / View): сетка, руки, быстрые слоты, курсор для геймпада,
    /// перетаскивание мышью и зелёная подсветка слотов, куда можно надеть выбранный предмет.
    /// <para>Раскладка для навигации (столбец, ряд): правая рука (0,1), левая (3,1), быстрые слоты (0..3, 3),
    /// сетка — столбцы 5.., ряды 0... Вью рисует то же расположение.</para>
    /// </summary>
    public sealed class InventoryScreenViewModel : ViewModel, IExclusiveScreen
    {
        public const int GridColumnOffset = 5;

        private readonly PlayerInventory inventory;
        private readonly Dictionary<SlotRef, (int col, int row)> layout = new Dictionary<SlotRef, (int, int)>();

        public PlayerInventory Inventory => inventory;

        /// <summary>Слот под курсором геймпада (и последний слот, на который навели мышь).</summary>
        public ReactiveProperty<SlotRef> Cursor { get; }

        /// <summary>Слот, из которого предмет "взят" (перетаскивание мышью или выбор быстрого слота на геймпаде).</summary>
        public ReactiveProperty<SlotRef?> Held { get; } = new ReactiveProperty<SlotRef?>();

        /// <summary>Слоты, подсвеченные зелёным.</summary>
        public ReactiveProperty<IReadOnlyList<SlotRef>> Highlights { get; } =
            new ReactiveProperty<IReadOnlyList<SlotRef>>(Array.Empty<SlotRef>());

        /// <summary>Счётчик изменений содержимого: вью перерисовывает слоты при каждом изменении.</summary>
        public ReactiveProperty<int> Revision { get; } = new ReactiveProperty<int>();

        public ReactiveProperty<string> Description { get; } = new ReactiveProperty<string>(string.Empty);

        public ReactiveCommand Close { get; }

        public InventoryScreenViewModel(PlayerInventory inventory, Action close)
        {
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Close = Own(new ReactiveCommand(close));

            // Персонаж смотрит на игрока, поэтому его правая рука — слева, левая — справа.
            layout[SlotRef.Of(Hand.Right)] = (0, 1);
            layout[SlotRef.Of(Hand.Left)] = (3, 1);
            for (int i = 0; i < inventory.QuickCount; i++)
                layout[SlotRef.Quick(i)] = (i, 3);
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
        /// Кнопка A: оружие из сетки надевается автоматически, из руки снимается;
        /// расходник сначала "берётся", затем A на быстром слоте кладёт его туда.
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

            var item = inventory.Get(slot);
            if (item == null)
                return;

            if (slot.Kind == SlotKind.Hand)
            {
                inventory.Unequip(slot.Hand);
                return;
            }

            if (item.Config.IsWeapon)
            {
                inventory.AutoEquip(item);
                Cursor.Value = inventory.Find(item) ?? slot;
                return;
            }

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

        /// <summary>Двойной щелчок или ПКМ по предмету: то же, что A на геймпаде.</summary>
        public void QuickUse(SlotRef slot)
        {
            Cursor.Value = slot;
            Held.Value = null;
            Confirm();
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
        }

        private static string Describe(Item item)
        {
            var c = item.Config;
            var kind = c.Kind switch
            {
                Configs.ItemKind.OneHanded => "одноручное",
                Configs.ItemKind.TwoHanded => "двуручное",
                Configs.ItemKind.Shield => "щит",
                _ => "предмет"
            };
            if (c.Heal != null)
                return $"{c.Name} · лечит {c.Heal.HealFraction * 100f:0}% здоровья · заряды {item.Charges}/{item.MaxCharges} · в слот 1–4";
            return c.Kind == Configs.ItemKind.Shield
                ? $"{c.Name} · {kind} · блок"
                : $"{c.Name} · {kind} · урон {c.Damage:0.#}";
        }

        protected override void OnDispose()
        {
            inventory.Changed -= OnInventoryChanged;
        }
    }
}
