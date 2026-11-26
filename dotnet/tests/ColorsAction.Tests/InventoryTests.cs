using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.Progression;
using ColorsAction.UI;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    public class InventoryTests
    {
        private int uid;

        private Item Make(string id, ItemKind kind, string color = "none") =>
            new Item(++uid, new ItemConfig { Id = id, Name = id, Kind = kind, Damage = 5, Color = color, MaxStack = kind == ItemKind.Resource ? 99 : 1 });

        private Item Sword() => Make("sword", ItemKind.Weapon, "blue");
        private Item Shotgun() => Make("shotgun", ItemKind.Weapon, "red");
        private Item Potion() => Make("potion", ItemKind.Consumable);
        private Item Metal() => Make("metal", ItemKind.Resource);

        [Test]
        public void TryAdd_FillsFirstFreeCell_AndFailsWhenFull()
        {
            var inv = new PlayerInventory(2, 1, 0);
            Assert.That(inv.TryAdd(Potion()), Is.True);
            Assert.That(inv.TryAdd(Potion()), Is.True);
            Assert.That(inv.TryAdd(Potion()), Is.False);
            Assert.That(inv.Get(SlotRef.Grid(0)).Id, Is.EqualTo("potion"));
        }

        [Test]
        public void Weapons_GoToWheel_NotToGrid_EvenWhenGridIsFull()
        {
            var inv = new PlayerInventory(1, 1, 0);
            inv.TryAdd(Potion());
            var sword = Sword();
            Assert.That(inv.TryAdd(sword), Is.True);
            Assert.That(inv.Weapons, Is.EqualTo(new[] { sword }));
            Assert.That(inv.Find(sword), Is.Null, "weapon lies in no slot");
            Assert.That(inv.Contains(sword), Is.True);
            Assert.That(inv.Active, Is.Null, "picking up does not take it into hands by itself");
        }

        [Test]
        public void SameWeaponTwice_IsNotAdded()
        {
            var inv = new PlayerInventory();
            Assert.That(inv.TryAdd(Sword()), Is.True);
            Assert.That(inv.TryAdd(Sword()), Is.False);
            Assert.That(inv.Weapons.Count, Is.EqualTo(1));
        }

        [Test]
        public void SetActive_OnlyOwnedWeapons_AndRaisesChanged()
        {
            var inv = new PlayerInventory();
            var sword = Sword();
            var shotgun = Shotgun();
            inv.AddWeapon(sword, true);
            inv.AddWeapon(shotgun, false);
            Assert.That(inv.Active, Is.SameAs(sword));

            var changes = 0;
            inv.Changed += () => changes++;
            Assert.That(inv.SetActive("shotgun"), Is.True);
            Assert.That(inv.Active, Is.SameAs(shotgun));
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(inv.SetActive(shotgun), Is.True, "same weapon — no change");
            Assert.That(changes, Is.EqualTo(1));

            Assert.That(inv.SetActive(Sword()), Is.False, "a weapon the player has not found");
            Assert.That(inv.SetActive("greatsword"), Is.False);
            Assert.That(inv.Active, Is.SameAs(shotgun));
            Assert.That(inv.SetActive((Item)null), Is.True);
            Assert.That(inv.Active, Is.Null);
        }

        [Test]
        public void WeaponOfColor_FindsSectorWeapon()
        {
            var inv = new PlayerInventory();
            var sword = Sword();
            inv.AddWeapon(sword, false);
            Assert.That(inv.WeaponOfColor("blue"), Is.SameAs(sword));
            Assert.That(inv.WeaponOfColor("red"), Is.Null);
            Assert.That(inv.HasWeapon("sword"), Is.True);
        }

        [Test]
        public void Remove_ActiveWeapon_EmptiesHands()
        {
            var inv = new PlayerInventory();
            var sword = Sword();
            inv.AddWeapon(sword, true);
            Assert.That(inv.Remove(sword), Is.True);
            Assert.That(inv.Active, Is.Null);
            Assert.That(inv.Weapons, Is.Empty);
        }

        [Test]
        public void Consumable_MovesToQuickSlot_ResourceDoesNot()
        {
            var inv = new PlayerInventory();
            inv.TryAdd(Potion());
            inv.TryAdd(Metal());
            Assert.That(inv.Move(SlotRef.Grid(0), SlotRef.Quick(2)), Is.True);
            Assert.That(inv.Get(SlotRef.Quick(2)).Id, Is.EqualTo("potion"));
            Assert.That(inv.Move(SlotRef.Grid(1), SlotRef.Quick(0)), Is.False, "resource can't go to quick slots");
        }

        [Test]
        public void QuickToOccupiedGridCell_SwapsWhenPossible_ElseDisplacesToFreeCell()
        {
            var inv = new PlayerInventory(3, 1, 4);
            var a = Potion();
            var b = Potion();
            inv.TryAdd(a);
            inv.TryAdd(b);
            inv.Move(SlotRef.Grid(0), SlotRef.Quick(0));
            // b (расходник) и a меняются местами.
            Assert.That(inv.Move(SlotRef.Quick(0), SlotRef.Grid(1)), Is.True);
            Assert.That(inv.Get(SlotRef.Quick(0)), Is.SameAs(b));
            Assert.That(inv.Get(SlotRef.Grid(1)), Is.SameAs(a));

            // Ресурс в слот 1–4 не встаёт: расходник ложится на его клетку, ресурс — в свободную.
            var metal = Metal();
            inv.TryAdd(metal);
            var metalSlot = inv.Find(metal).Value;
            Assert.That(inv.Move(SlotRef.Quick(0), metalSlot), Is.True);
            Assert.That(inv.Get(metalSlot), Is.SameAs(b));
            Assert.That(inv.Find(metal).Value.Kind, Is.EqualTo(SlotKind.Grid));
            Assert.That(inv.Get(SlotRef.Quick(0)), Is.Null);
        }

        [Test]
        public void EquipTargets_QuickSlotsForConsumables_NothingElse()
        {
            var inv = new PlayerInventory();
            Assert.That(inv.EquipTargets(Sword()), Is.Empty);
            Assert.That(inv.EquipTargets(Metal()), Is.Empty);
            Assert.That(inv.EquipTargets(Potion()).Count, Is.EqualTo(4));
            Assert.That(inv.EquipTargets(Potion()).All(s => s.Kind == SlotKind.Quick), Is.True);
        }

        [Test]
        public void ScreenModel_HasNoWeaponSlots_ShowsActiveWeaponCaption()
        {
            var inv = new PlayerInventory();
            var upgrades = new WeaponUpgrades();
            upgrades.Set("sword", 2);
            inv.AddWeapon(Sword(), true);
            using var vm = new InventoryScreenViewModel(inv, () => { }, upgrades);
            Assert.That(vm.Slots.All(s => s.Kind == SlotKind.Grid || s.Kind == SlotKind.Quick), Is.True);
            Assert.That(vm.Slots.Count(), Is.EqualTo(inv.GridSize + inv.QuickCount));
            Assert.That(vm.WeaponCaption.Value, Does.Contain("sword").And.Contain("2/3"));

            inv.SetActive((Item)null);
            Assert.That(vm.WeaponCaption.Value, Is.EqualTo("Без оружия"));
        }

        [Test]
        public void ScreenModel_GamepadConfirm_PutsPotionIntoQuickSlot()
        {
            var inv = new PlayerInventory();
            var potion = Potion();
            inv.TryAdd(potion);
            var closed = false;
            using var vm = new InventoryScreenViewModel(inv, () => closed = true);

            vm.Cursor.Value = SlotRef.Grid(0);
            Assert.That(vm.IsHighlighted(SlotRef.Quick(0)), Is.True);
            Assert.That(vm.IsHighlighted(SlotRef.Grid(1)), Is.False);

            vm.Confirm(); // взять
            Assert.That(vm.Held.Value, Is.EqualTo(SlotRef.Grid(0)));
            vm.Cursor.Value = SlotRef.Quick(1);
            vm.Confirm(); // положить
            Assert.That(inv.Get(SlotRef.Quick(1)), Is.SameAs(potion));
            Assert.That(vm.Held.Value, Is.Null);

            vm.Cancel();
            Assert.That(closed, Is.True);
        }

        [Test]
        public void ScreenModel_DragAndDrop_AndQuickUse()
        {
            var inv = new PlayerInventory();
            var potion = Potion();
            inv.TryAdd(potion);
            using var vm = new InventoryScreenViewModel(inv, () => { });
            Assert.That(vm.BeginDrag(SlotRef.Grid(0)), Is.True);
            Assert.That(vm.Drop(SlotRef.Quick(3)), Is.True);
            Assert.That(inv.Get(SlotRef.Quick(3)), Is.SameAs(potion));
            Assert.That(vm.Held.Value, Is.Null);

            Assert.That(vm.QuickUse(SlotRef.Quick(3)), Is.True, "back to the grid");
            Assert.That(inv.Find(potion).Value.Kind, Is.EqualTo(SlotKind.Grid));
            Assert.That(vm.QuickUse(inv.Find(potion).Value), Is.True, "into the first free quick slot");
            Assert.That(inv.Get(SlotRef.Quick(0)), Is.SameAs(potion));
        }

        [Test]
        public void ScreenModel_Navigate_CrossLikeDpad_AndToGrid()
        {
            var inv = new PlayerInventory();
            using var vm = new InventoryScreenViewModel(inv, () => { });
            vm.Cursor.Value = SlotRef.Grid(0);
            vm.Navigate(1, 0);
            Assert.That(vm.Cursor.Value, Is.EqualTo(SlotRef.Grid(1)));

            // Крестовина: 1 вверху, 2 справа, 3 внизу, 4 слева.
            vm.Cursor.Value = SlotRef.Quick(0);
            vm.Navigate(0, 1);
            vm.Navigate(0, 1);
            Assert.That(vm.Cursor.Value, Is.EqualTo(SlotRef.Quick(2)));
            vm.Cursor.Value = SlotRef.Quick(3);
            vm.Navigate(1, 0);
            Assert.That(vm.Cursor.Value, Is.EqualTo(SlotRef.Quick(1)), "straight across the cross");
            vm.Navigate(1, 0);
            Assert.That(vm.Cursor.Value.Kind, Is.EqualTo(SlotKind.Grid), "right of the cross is the grid");
        }
    }
}
