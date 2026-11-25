using System;
using System.Collections.Generic;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.Progression;
using GameCore.UI;

namespace ColorsAction.UI
{
    /// <summary>Ячейка экрана костра: строка — оружие, ступень — 1..<see cref="WeaponUpgrades.MaxTier"/>.</summary>
    public readonly struct UpgradeCell : IEquatable<UpgradeCell>
    {
        public readonly int Row;
        public readonly int Tier;

        public UpgradeCell(int row, int tier)
        {
            Row = row;
            Tier = tier;
        }

        public bool Equals(UpgradeCell other) => Row == other.Row && Tier == other.Tier;
        public override bool Equals(object obj) => obj is UpgradeCell other && Equals(other);
        public override int GetHashCode() => Row * 31 + Tier;
        public override string ToString() => $"{Row}:{Tier}";
    }

    /// <summary>Оружие игрока в списке костра.</summary>
    public sealed class BonfireWeaponRow
    {
        public string ItemId { get; }
        public string Name { get; }

        /// <summary>Id цвета из Colors.json.</summary>
        public string Color { get; }

        public BonfireWeaponRow(string itemId, string name, string color)
        {
            ItemId = itemId;
            Name = name;
            Color = color;
        }
    }

    /// <summary>
    /// Модель меню костра: оружие игрока (строки) и по 3 последовательных апгрейда (столбцы).
    /// Геймпад/клавиатура: <see cref="Navigate"/> + <see cref="Confirm"/>, мышь: <see cref="Hover"/> + <see cref="Select"/>.
    /// Покупка — <see cref="WeaponUpgradeShop"/> (за опыт).
    /// </summary>
    public sealed class BonfireScreenViewModel : ViewModel, IExclusiveScreen
    {
        private readonly WeaponUpgradeShop shop;
        private readonly List<BonfireWeaponRow> rows = new List<BonfireWeaponRow>();

        public IReadOnlyList<BonfireWeaponRow> Rows => rows;

        public ReactiveProperty<UpgradeCell> Cursor { get; }

        /// <summary>Счётчик изменений (покупка, опыт): вью перерисовывает ячейки.</summary>
        public ReactiveProperty<int> Revision { get; } = new ReactiveProperty<int>();

        /// <summary>Описание ячейки под курсором.</summary>
        public ReactiveProperty<string> Description { get; } = new ReactiveProperty<string>(string.Empty);

        /// <summary>Короткое сообщение после действия: «Куплено», «Не хватает опыта».</summary>
        public ReactiveProperty<string> Message { get; } = new ReactiveProperty<string>(string.Empty);

        public IReadOnlyReactiveProperty<int> Experience => shop.Experience;

        public ReactiveCommand Close { get; }

        public BonfireScreenViewModel(WeaponUpgradeShop shop, PlayerInventory inventory, Action close)
        {
            this.shop = shop ?? throw new ArgumentNullException(nameof(shop));
            Close = Own(new ReactiveCommand(close ?? (() => { })));
            if (inventory != null)
                CollectWeapons(inventory);

            Cursor = Own(new ReactiveProperty<UpgradeCell>(new UpgradeCell(0, FirstOpenTier(0))));
            Own(Cursor.Subscribe(_ => Refresh(), false));
            Own(shop.Experience.Subscribe(_ => Changed(), false));
            shop.Upgrades.Changed += OnUpgradesChanged;
            Refresh();
        }

        public bool IsEmpty => rows.Count == 0;

        public UpgradeTierConfig TierConfig(UpgradeCell cell) => InRange(cell) ? shop.Tier(rows[cell.Row].ItemId, cell.Tier) : null;

        public UpgradeTierStatus Status(UpgradeCell cell) =>
            InRange(cell) ? shop.Status(rows[cell.Row].ItemId, cell.Tier) : UpgradeTierStatus.Locked;

        public int Level(int row) => row >= 0 && row < rows.Count ? shop.Upgrades.Level(rows[row].ItemId) : 0;

        // ------------------------------------------------------------------ ввод

        /// <summary>Сдвиг курсора: dx — по ступеням, dy > 0 — вниз по списку оружия.</summary>
        public void Navigate(int dx, int dy)
        {
            if (rows.Count == 0)
                return;
            var c = Cursor.Value;
            var row = Math.Max(0, Math.Min(rows.Count - 1, c.Row + Math.Sign(dy)));
            var tier = Math.Max(1, Math.Min(WeaponUpgrades.MaxTier, c.Tier + Math.Sign(dx)));
            Message.Value = string.Empty;
            Cursor.Value = new UpgradeCell(row, tier);
        }

        /// <summary>Мышь навелась на ячейку.</summary>
        public void Hover(UpgradeCell cell)
        {
            if (InRange(cell))
                Cursor.Value = cell;
        }

        /// <summary>Щелчок мышью: выбрать ячейку и купить.</summary>
        public bool Select(UpgradeCell cell)
        {
            if (!InRange(cell))
                return false;
            Cursor.Value = cell;
            return Confirm();
        }

        /// <summary>Купить апгрейд под курсором (A / Enter). false — нельзя, в <see cref="Message"/> причина.</summary>
        public bool Confirm()
        {
            var cell = Cursor.Value;
            if (!InRange(cell))
                return false;
            var id = rows[cell.Row].ItemId;
            switch (shop.Status(id, cell.Tier))
            {
                case UpgradeTierStatus.Owned:
                    Message.Value = "Уже изучено";
                    return false;
                case UpgradeTierStatus.Locked:
                    Message.Value = "Сначала изучите предыдущее улучшение";
                    return false;
                case UpgradeTierStatus.TooExpensive:
                    Message.Value = "Не хватает опыта";
                    return false;
            }

            if (!shop.TryBuy(id))
                return false;
            Message.Value = "Изучено: " + (shop.Tier(id, cell.Tier)?.Name ?? string.Empty);
            // Курсор — на следующую ступень этого оружия, если она есть.
            if (cell.Tier < WeaponUpgrades.MaxTier)
                Cursor.Value = new UpgradeCell(cell.Row, cell.Tier + 1);
            Changed();
            return true;
        }

        /// <summary>Esc / B — закрыть меню.</summary>
        public void Cancel() => Close.Execute();

        // ------------------------------------------------------------------ внутреннее

        private void CollectWeapons(PlayerInventory inventory)
        {
            var seen = new HashSet<string>();

            void Add(Item item)
            {
                if (item == null || !shop.HasUpgrades(item.Id) || !seen.Add(item.Id))
                    return;
                rows.Add(new BonfireWeaponRow(item.Id, item.Config.Name ?? item.Id, item.Config.Color));
            }

            // Сначала то, что в руках, потом остальное по порядку слотов.
            Add(inventory.Right);
            Add(inventory.Left);
            foreach (var slot in inventory.AllSlots())
                Add(inventory.Get(slot));
        }

        private int FirstOpenTier(int row)
        {
            if (row >= rows.Count)
                return 1;
            return Math.Min(WeaponUpgrades.MaxTier, shop.Upgrades.Level(rows[row].ItemId) + 1);
        }

        private bool InRange(UpgradeCell cell) =>
            cell.Row >= 0 && cell.Row < rows.Count && cell.Tier >= 1 && cell.Tier <= WeaponUpgrades.MaxTier;

        private void OnUpgradesChanged(string itemId, int level) => Changed();

        private void Changed()
        {
            Revision.Value++;
            Refresh();
        }

        private void Refresh()
        {
            var cell = Cursor.Value;
            var tier = TierConfig(cell);
            if (tier == null)
            {
                Description.Value = rows.Count == 0 ? "Нет оружия для улучшения. Подберите оружие и возвращайтесь к костру." : string.Empty;
                return;
            }

            string state;
            switch (Status(cell))
            {
                case UpgradeTierStatus.Owned: state = "Изучено"; break;
                case UpgradeTierStatus.Available: state = $"Цена: {tier.Cost} опыта"; break;
                case UpgradeTierStatus.TooExpensive: state = $"Цена: {tier.Cost} опыта — не хватает"; break;
                default: state = $"Цена: {tier.Cost} опыта — сначала предыдущее улучшение"; break;
            }

            Description.Value = $"{rows[cell.Row].Name} · {cell.Tier}. {tier.Name}\n{tier.Description}\n{state}";
        }

        protected override void OnDispose()
        {
            shop.Upgrades.Changed -= OnUpgradesChanged;
        }
    }
}
