using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.UI;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    public class InventoryTests
    {
        private int uid;

        private Item Make(string id, ItemKind kind) =>
            new Item(++uid, new ItemConfig { Id = id, Name = id, Kind = kind, Damage = 5 });

        private Item Sword() => Make("sword", ItemKind.OneHanded);
        private Item Shield() => Make("shield", ItemKind.Shield);
        private Item Greatsword() => Make("greatsword", ItemKind.TwoHanded);
        private Item Potion() => Make("potion", ItemKind.Consumable);

        [Test]
        public void TryAdd_FillsFirstFreeCell_AndFailsWhenFull()
        {
            var inv = new PlayerInventory(2, 1, 0);
            Assert.That(inv.TryAdd(Sword()), Is.True);
            Assert.That(inv.TryAdd(Shield()), Is.True);
            Assert.That(inv.TryAdd(Potion()), Is.False);
            Assert.That(inv.Get(SlotRef.Grid(0)).Id, Is.EqualTo("sword"));
        }

        [Test]
        public void Move_GridToHand_Equips_AndFreesCell()
        {
            var inv = new PlayerInventory();
            var sword = Sword();
            inv.TryAdd(sword);
            Assert.That(inv.Move(SlotRef.Grid(0), SlotRef.Of(Hand.Right)), Is.True);
            Assert.That(inv.Right, Is.SameAs(sword));
            Assert.That(inv.Get(SlotRef.Grid(0)), Is.Null);
        }

        [Test]
        public void Move_ToOccupiedHand_SwapsWithOrigin()
        {
            var inv = new PlayerInventory();
            var a = Sword();
            var b = Sword();
            inv.TryAdd(a);
            inv.TryAdd(b);
            inv.Move(SlotRef.Grid(0), SlotRef.Of(Hand.Right));
            Assert.That(inv.Move(SlotRef.Grid(1), SlotRef.Of(Hand.Right)), Is.True);
            Assert.That(inv.Right, Is.SameAs(b));
            Assert.That(inv.Get(SlotRef.Grid(1)), Is.SameAs(a));
        }

        [Test]
        public void TwoHanded_TakesBothHands_AndReturnsBothItemsToGrid()
        {
            var inv = new PlayerInventory();
            var sword = Sword();
            var shield = Shield();
            var great = Greatsword();
            inv.TryAdd(sword);
            inv.TryAdd(shield);
            inv.TryAdd(great);
            inv.Equip(sword, Hand.Right);
            inv.Equip(shield, Hand.Left);

            Assert.That(inv.Equip(great, Hand.Left), Is.True);
            Assert.That(inv.Left, Is.SameAs(great));
            Assert.That(inv.Right, Is.SameAs(great));
            Assert.That(inv.Contains(sword) && inv.Find(sword)!.Value.Kind == SlotKind.Grid, Is.True);
            Assert.That(inv.Contains(shield) && inv.Find(shield)!.Value.Kind == SlotKind.Grid, Is.True);
        }

        [Test]
        public void OneHanded_OverTwoHanded_FreesBothHands()
        {
            var inv = new PlayerInventory();
            var great = Greatsword();
            var sword = Sword();
            inv.TryAdd(great);
            inv.TryAdd(sword);
            inv.AutoEquip(great);
            Assert.That(inv.Equip(sword, Hand.Right), Is.True);
            Assert.That(inv.Right, Is.SameAs(sword));
            Assert.That(inv.Left, Is.Null);
            Assert.That(inv.Find(great)!.Value.Kind, Is.EqualTo(SlotKind.Grid));
        }

        [Test]
        public void Equip_FailsAtomically_WhenNoRoomForDisplaced()
        {
            var inv = new PlayerInventory(1, 1, 0);
            var sword = Sword();
            var shield = Shield();
            inv.TryAdd(sword);
            inv.Equip(sword, Hand.Right);
            inv.TryAdd(shield);
            inv.Equip(shield, Hand.Left);
            var great = Greatsword();
            inv.TryAdd(great); // the only cell

            // Two displaced items, only one cell (the origin) — must fail without changes.
            Assert.That(inv.Equip(great, Hand.Right), Is.False);
            Assert.That(inv.Right, Is.SameAs(sword));
            Assert.That(inv.Left, Is.SameAs(shield));
            Assert.That(inv.Get(SlotRef.Grid(0)), Is.SameAs(great));
        }

        [Test]
        public void HandToHand_SwapsOneHandedItems()
        {
            var inv = new PlayerInventory();
            var sword = Sword();
            var shield = Shield();
            inv.TryAdd(sword);
            inv.TryAdd(shield);
            inv.Equip(sword, Hand.Right);
            inv.Equip(shield, Hand.Left);
            Assert.That(inv.Move(SlotRef.Of(Hand.Right), SlotRef.Of(Hand.Left)), Is.True);
            Assert.That(inv.Left, Is.SameAs(sword));
            Assert.That(inv.Right, Is.SameAs(shield));
        }

        [Test]
        public void HandToGridWeapon_SwapsEquipment()
        {
            var inv = new PlayerInventory();
            var a = Sword();
            var b = Sword();
            inv.TryAdd(a);
            inv.TryAdd(b);
            inv.Equip(a, Hand.Right);
            var bSlot = inv.Find(b)!.Value;
            Assert.That(inv.Move(SlotRef.Of(Hand.Right), bSlot), Is.True);
            Assert.That(inv.Right, Is.SameAs(b));
            Assert.That(inv.Get(bSlot), Is.SameAs(a));
        }

        [Test]
        public void ConsumablesCantGoToHands_WeaponsCantGoToQuickSlots()
        {
            var inv = new PlayerInventory();
            var potion = Potion();
            var sword = Sword();
            inv.TryAdd(potion);
            inv.TryAdd(sword);
            Assert.That(inv.Move(SlotRef.Grid(0), SlotRef.Of(Hand.Left)), Is.False);
            Assert.That(inv.Move(SlotRef.Grid(1), SlotRef.Quick(0)), Is.False);
            Assert.That(inv.Move(SlotRef.Grid(0), SlotRef.Quick(2)), Is.True);
        }

        [Test]
        public void EquipTargets_AreBothHandsForWeapons_AndQuickSlotsForConsumables()
        {
            var inv = new PlayerInventory();
            Assert.That(inv.EquipTargets(Shield()), Is.EquivalentTo(new[] { SlotRef.Of(Hand.Left), SlotRef.Of(Hand.Right) }));
            Assert.That(inv.EquipTargets(Potion()).Count, Is.EqualTo(4));
            Assert.That(inv.EquipTargets(Potion()).All(s => s.Kind == SlotKind.Quick), Is.True);
        }

        [Test]
        public void AutoEquip_PrefersRightForWeapons_LeftForShields()
        {
            var inv = new PlayerInventory();
            var sword = Sword();
            var shield = Shield();
            inv.TryAdd(sword);
            inv.TryAdd(shield);
            inv.AutoEquip(shield);
            inv.AutoEquip(sword);
            Assert.That(inv.Left, Is.SameAs(shield));
            Assert.That(inv.Right, Is.SameAs(sword));
        }

        [Test]
        public void ScreenModel_HighlightsHands_ForWeaponUnderCursor_AndGamepadConfirmEquips()
        {
            var inv = new PlayerInventory();
            var sword = Sword();
            inv.TryAdd(sword);
            var closed = false;
            using var vm = new InventoryScreenViewModel(inv, () => closed = true);

            vm.Cursor.Value = SlotRef.Grid(0);
            Assert.That(vm.IsHighlighted(SlotRef.Of(Hand.Left)), Is.True);
            Assert.That(vm.IsHighlighted(SlotRef.Of(Hand.Right)), Is.True);
            Assert.That(vm.IsHighlighted(SlotRef.Grid(1)), Is.False);

            vm.Confirm();
            Assert.That(inv.Right, Is.SameAs(sword));
            Assert.That(vm.Cursor.Value, Is.EqualTo(SlotRef.Of(Hand.Right)));

            vm.Confirm(); // A on a hand unequips
            Assert.That(inv.Right, Is.Null);

            vm.Cancel();
            Assert.That(closed, Is.True);
        }

        [Test]
        public void ScreenModel_DragAndDrop()
        {
            var inv = new PlayerInventory();
            var shield = Shield();
            inv.TryAdd(shield);
            using var vm = new InventoryScreenViewModel(inv, () => { });
            Assert.That(vm.BeginDrag(SlotRef.Grid(0)), Is.True);
            Assert.That(vm.IsHighlighted(SlotRef.Of(Hand.Left)), Is.True);
            Assert.That(vm.Drop(SlotRef.Of(Hand.Left)), Is.True);
            Assert.That(inv.Left, Is.SameAs(shield));
            Assert.That(vm.Held.Value, Is.Null);
        }

        [Test]
        public void ScreenModel_Navigate_MovesBetweenGridAndHands()
        {
            var inv = new PlayerInventory();
            using var vm = new InventoryScreenViewModel(inv, () => { });
            vm.Cursor.Value = SlotRef.Grid(0);
            vm.Navigate(1, 0);
            Assert.That(vm.Cursor.Value, Is.EqualTo(SlotRef.Grid(1)));
            vm.Navigate(-1, 0);
            vm.Navigate(-1, 0);
            Assert.That(vm.Cursor.Value, Is.EqualTo(SlotRef.Of(Hand.Right)));
            vm.Navigate(-1, 0);
            Assert.That(vm.Cursor.Value, Is.EqualTo(SlotRef.Of(Hand.Left)));
            vm.Navigate(0, 1);
            Assert.That(vm.Cursor.Value.Kind, Is.EqualTo(SlotKind.Quick));
        }
    }
}
