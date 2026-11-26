using System.Collections.Generic;
using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.Level;
using ColorsAction.Progression;
using GameCore.Config;
using GameCore.Newtonsoft;
using GameCore.UI;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Ресурсы прокачки (стопки в инвентаре), цена апгрейда «опыт + ресурс», сундуки в уровне.</summary>
    public class ResourcesTests
    {
        private static ConfigService Configs() =>
            new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());

        private static ItemConfig Metal(int maxStack = 99)
        {
            var c = new ItemConfig { Id = "metal", Name = "Металл", Kind = ItemKind.Resource, MaxStack = maxStack };
            c.OnConfigLoaded();
            return c;
        }

        private static int uid;
        private static Item Make(ItemConfig config, int count = 1) => new Item(++uid, config) { Count = count };

        [Test]
        public void ItemsConfig_HasResourcesForEveryUpgradableWeapon()
        {
            var configs = Configs();
            var items = configs.LoadTable<ItemConfig>(ConfigKeys.Items);
            var upgrades = configs.LoadTable<WeaponUpgradeConfig>(ConfigKeys.Upgrades);
            var expected = new Dictionary<string, string> { ["sword"] = "metal", ["shotgun"] = "gears" };
            Assert.That(items.ContainsKey("rivets"), Is.False, "заклёпки убраны: щит — часть синего оружия");
            foreach (var pair in expected)
            {
                Assert.That(upgrades.Get(pair.Key).Resource, Is.EqualTo(pair.Value));
                var resource = items.Get(pair.Value);
                Assert.That(resource.Kind, Is.EqualTo(ItemKind.Resource));
                Assert.That(resource.IsStackable, Is.True);
                Assert.That(resource.IsWeapon, Is.False);
                Assert.That(upgrades.Get(pair.Key).Tiers.All(t => t.Resources == 1), Is.True, "по 1 ресурсу на уровень");
            }

            Assert.That(configs.Load<ChestConfig>(ConfigKeys.Chests).MaxPieces, Is.GreaterThan(0));
        }

        [Test]
        public void Inventory_StacksResources_AndConsumesThem()
        {
            var metal = Metal(5);
            var inv = new PlayerInventory(2, 1, 0);
            Assert.That(inv.TryAddCount(metal, 3, () => Make(metal)), Is.True);
            Assert.That(inv.CountOf("metal"), Is.EqualTo(3));
            Assert.That(inv.FreeGridSlot(), Is.EqualTo(1), "одна клетка на стопку");

            Assert.That(inv.TryAddCount(metal, 4, () => Make(metal)), Is.True);
            Assert.That(inv.CountOf("metal"), Is.EqualTo(7));
            Assert.That(inv.Get(SlotRef.Grid(0)).Count, Is.EqualTo(5));
            Assert.That(inv.Get(SlotRef.Grid(1)).Count, Is.EqualTo(2));

            // Не помещается целиком — ничего не добавляется.
            Assert.That(inv.TryAddCount(metal, 4, () => Make(metal)), Is.False);
            Assert.That(inv.CountOf("metal"), Is.EqualTo(7));

            Assert.That(inv.TryConsume("metal", 8), Is.False);
            Assert.That(inv.TryConsume("metal", 3), Is.True);
            Assert.That(inv.CountOf("metal"), Is.EqualTo(4));
            Assert.That(inv.Get(SlotRef.Grid(1)), Is.Null, "пустая стопка убрана");

            // Подбор одного экземпляра досыпается в стопку.
            Assert.That(inv.TryAdd(Make(metal)), Is.True);
            Assert.That(inv.CountOf("metal"), Is.EqualTo(5));
            Assert.That(inv.Get(SlotRef.Grid(1)), Is.Null);
            Assert.That(inv.EquipTargets(inv.Get(SlotRef.Grid(0))), Is.Empty, "ресурс не надевается");
        }

        [Test]
        public void Inventory_DragStackOntoSameStack_Merges()
        {
            var metal = Metal(3);
            var inv = new PlayerInventory(3, 1, 0);
            Assert.That(inv.TryAddCount(metal, 5, () => Make(metal)), Is.True);
            Assert.That((inv.Get(SlotRef.Grid(0)).Count, inv.Get(SlotRef.Grid(1)).Count), Is.EqualTo((3, 2)));
            inv.Get(SlotRef.Grid(0)).Count = 1;

            Assert.That(inv.Move(SlotRef.Grid(1), SlotRef.Grid(0)), Is.True);
            Assert.That(inv.Get(SlotRef.Grid(0)).Count, Is.EqualTo(3));
            Assert.That(inv.Get(SlotRef.Grid(1)), Is.Null, "вся стопка влилась");
        }

        [Test]
        public void Shop_NeedsExperienceAndWeaponResource()
        {
            var metal = Metal();
            var config = new WeaponUpgradeConfig
            {
                Id = "sword", Resource = "metal",
                Tiers = new List<UpgradeTierConfig>
                {
                    new UpgradeTierConfig { Name = "a", Cost = 10, Resources = 1 },
                    new UpgradeTierConfig { Name = "b", Cost = 20, Resources = 1 },
                    new UpgradeTierConfig { Name = "c", Cost = 30, Resources = 1 }
                }
            };
            config.OnConfigLoaded();
            var inv = new PlayerInventory(5, 5, 4);
            var xp = new ReactiveProperty<int>(100);
            var upgrades = new WeaponUpgrades();
            var shop = new WeaponUpgradeShop(upgrades, xp, new Dictionary<string, WeaponUpgradeConfig> { ["sword"] = config }, inv,
                id => id == "metal" ? "Металл" : id);

            Assert.That(shop.Status("sword", 1), Is.EqualTo(UpgradeTierStatus.TooExpensive));
            Assert.That(shop.Missing("sword", 1), Is.EqualTo(UpgradeShortage.Resource));
            Assert.That(shop.TryBuy("sword"), Is.False);

            inv.TryAddCount(metal, 1, () => Make(metal));
            Assert.That(shop.Status("sword", 1), Is.EqualTo(UpgradeTierStatus.Available));
            Assert.That(shop.TryBuy("sword"), Is.True);
            Assert.That((upgrades.Level("sword"), xp.Value, inv.CountOf("metal")), Is.EqualTo((1, 90, 0)));

            inv.TryAddCount(metal, 2, () => Make(metal));
            xp.Value = 5;
            Assert.That(shop.Missing("sword", 2), Is.EqualTo(UpgradeShortage.Experience));
            Assert.That(shop.TryBuy("sword"), Is.False);
            Assert.That(inv.CountOf("metal"), Is.EqualTo(2), "при нехватке опыта ресурс не тратится");
            Assert.That(shop.ResourceName("sword"), Is.EqualTo("Металл"));
        }

        [Test]
        public void VoxelLevel_Chest_GoesToLevelConfig_AndSurvivesSave()
        {
            var level = new VoxelLevel();
            var cells = new List<KeyValuePair<Int3, Block?>>();
            for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                cells.Add(new KeyValuePair<Int3, Block?>(new Int3(x, 0, z), Block.Cube()));
            level.SetMany(cells);
            level.PutObject(new LevelObject { Id = "chest_1", Kind = LevelObjectKind.Chest, Cell = new Int3(1, 1, 1), Item = "gears", Count = 3 });

            var skipped = new List<string>();
            var config = VoxelPlayLayout.ToLevelConfig(level, skipped);
            Assert.That(skipped.Any(s => s.Contains("chest_1")), Is.False);
            Assert.That(config.Chests, Has.Count.EqualTo(1));
            var chest = config.Chests[0];
            Assert.That((chest.Id, chest.Item, chest.Count, chest.X, chest.Z, chest.Y), Is.EqualTo(("chest_1", "gears", 3, 1, 1, (float?)1f)));

            var round = VoxelLevelData.From(level).ToLevel().FindObject("chest_1");
            Assert.That((round.Kind, round.Item, round.Count), Is.EqualTo((LevelObjectKind.Chest, "gears", 3)));
        }
    }
}
