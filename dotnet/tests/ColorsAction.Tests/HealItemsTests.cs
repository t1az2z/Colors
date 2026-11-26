using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Лечилка: заряды, лечение долей максимума, восполнение у костра.</summary>
    public class HealItemsTests
    {
        private static Item Flask(int charges = 2, float fraction = 0.25f) => new Item(1, new ItemConfig
        {
            Id = "flask", Kind = ItemKind.Consumable, Heal = new HealConfig { Charges = charges, HealFraction = fraction }
        });

        [Test]
        public void Use_HealsQuarterOfMax_AndSpendsCharge()
        {
            var flask = Flask();
            var health = new Health(100f);
            health.TakeDamage(60f);
            Assert.That(flask.Charges, Is.EqualTo(2));
            Assert.That(HealItems.TryUse(flask, health, out var healed), Is.EqualTo(QuickUseResult.Healed));
            Assert.That(healed, Is.EqualTo(25f));
            Assert.That(health.Current.Value, Is.EqualTo(65f));
            Assert.That(flask.Charges, Is.EqualTo(1));
        }

        [Test]
        public void Use_NeverOverheals_AndFullHealthKeepsCharge()
        {
            var flask = Flask();
            var health = new Health(100f);
            Assert.That(HealItems.TryUse(flask, health, out _), Is.EqualTo(QuickUseResult.NotNeeded));
            Assert.That(flask.Charges, Is.EqualTo(2));
            health.TakeDamage(10f);
            Assert.That(HealItems.TryUse(flask, health, out var healed), Is.EqualTo(QuickUseResult.Healed));
            Assert.That(healed, Is.EqualTo(10f));
            Assert.That(health.Current.Value, Is.EqualTo(100f));
        }

        [Test]
        public void Use_TwoChargesThenEmpty_RefillRestores()
        {
            var inventory = new PlayerInventory();
            var flask = Flask();
            Assert.That(inventory.TryAdd(flask), Is.True);
            var health = new Health(100f);
            health.TakeDamage(90f);
            Assert.That(HealItems.TryUse(flask, health, out _), Is.EqualTo(QuickUseResult.Healed));
            Assert.That(HealItems.TryUse(flask, health, out _), Is.EqualTo(QuickUseResult.Healed));
            Assert.That(HealItems.TryUse(flask, health, out _), Is.EqualTo(QuickUseResult.Empty));
            Assert.That(flask.Charges, Is.Zero);

            var changed = 0;
            inventory.Changed += () => changed++;
            Assert.That(HealItems.Refill(inventory), Is.True);
            Assert.That(flask.Charges, Is.EqualTo(2));
            Assert.That(changed, Is.EqualTo(1), "views refresh after refill");
            Assert.That(HealItems.Refill(inventory), Is.False, "already full");
        }

        [Test]
        public void NonHealingItem_DoesNothing()
        {
            var sword = new Item(2, new ItemConfig { Id = "sword", Kind = ItemKind.OneHanded });
            Assert.That(sword.Charges, Is.Zero);
            Assert.That(HealItems.TryUse(sword, new Health(10f), out _), Is.EqualTo(QuickUseResult.Nothing));
        }
    }
}
