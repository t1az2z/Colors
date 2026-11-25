using System;
using System.IO;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.States;
using GameCore;
using GameCore.Config;
using GameCore.Hosting;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Настоящие конфиги игры из Assets/StreamingAssets/Configs и проход состояний без движка.</summary>
    public class ConfigAndFlowTests
    {
        private static string ConfigsRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "StreamingAssets", "Configs")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "project root with Assets/StreamingAssets/Configs not found");
            return Path.Combine(dir!.FullName, "Assets", "StreamingAssets", "Configs");
        }

        private static GameHost CreateHost() =>
            new GameHost(new ConsoleLogger(),
                new ConfigInstaller(new FileConfigSource(ConfigsRoot()), new NewtonsoftConfigSerializer()),
                new ColorsActionInstaller());

        [Test]
        public void AllConfigsLoad_AndBootReachesPlayState()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            Assert.That(host.States.Current, Is.InstanceOf<PlayState>());
            Assert.That(host.Container.Resolve<GameSession>().PlayerControl.Value, Is.True);
        }

        [Test]
        public void PlayerConfig_MatchesPrototypeRequirements()
        {
            using var host = CreateHost();
            var player = host.Container.Resolve<ConfigService>().Load<PlayerConfig>(ConfigKeys.Player);
            // Числа крутит пользователь в редакторе конфигов, тест проверяет только соотношения.
            Assert.That(player.JumpHeight, Is.GreaterThan(1f).And.LessThan(2f), "1.0 ledge jumpable, 2.0 not");
            Assert.That(player.RollDistance, Is.GreaterThan(1f));
            Assert.That(player.RunMultiplier, Is.GreaterThan(1f));
            Assert.That(player.RollInvulnerableTo, Is.GreaterThan(player.RollInvulnerableFrom));
            // 0.25 — шагом, 0.5 — уже нет.
            Assert.That(player.StepHeight, Is.GreaterThan(0.25f).And.LessThan(0.5f));
        }

        [Test]
        public void SwordGoesOnlyToRightHand_ShieldOnlyToLeft()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            var session = host.Container.Resolve<GameSession>();
            var items = host.Container.Resolve<ItemDatabase>();
            var inv = session.Inventory;
            inv.TryAdd(items.Create("sword"));
            inv.TryAdd(items.Create("shield"));
            var sword = inv.Get(SlotRef.Grid(0));
            var shield = inv.Get(SlotRef.Grid(1));

            Assert.That(inv.EquipTargets(sword), Is.EqualTo(new[] { SlotRef.Of(Hand.Right) }));
            Assert.That(inv.EquipTargets(shield), Is.EqualTo(new[] { SlotRef.Of(Hand.Left) }));
            Assert.That(inv.Move(SlotRef.Grid(0), SlotRef.Of(Hand.Left)), Is.False);
            Assert.That(inv.Move(SlotRef.Grid(1), SlotRef.Of(Hand.Right)), Is.False);
            Assert.That(inv.AutoEquip(sword) && inv.AutoEquip(shield), Is.True);
            Assert.That(inv.Right, Is.SameAs(sword));
            Assert.That(inv.Left, Is.SameAs(shield));
            Assert.That(inv.Move(SlotRef.Of(Hand.Right), SlotRef.Of(Hand.Left)), Is.False, "hands can't be swapped");
        }

        [Test]
        public void PlayerStartsWithoutWeapons()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            var inv = host.Container.Resolve<GameSession>().Inventory;
            Assert.That(inv.Left, Is.Null);
            Assert.That(inv.Right, Is.Null);
            foreach (var slot in inv.AllSlots())
                Assert.That(inv.Get(slot), Is.Null);
        }

        [Test]
        public void TestLevel_Is64Wide_WithAllStepHeights()
        {
            using var host = CreateHost();
            var configs = host.Container.Resolve<ConfigService>();
            var level = configs.Load<LevelConfig>(configs.Load<GameConfig>(ConfigKeys.Game).Level);
            Assert.That(level.Width, Is.EqualTo(64));
            Assert.That(level.Length, Is.EqualTo(86), "64x64 square plus the shooting field in the north");

            bool walk = false, jump = false, block = false;
            for (int z = 0; z < level.Length; z++)
            for (int x = 0; x + 1 < level.Width; x++)
            {
                var d = Math.Abs(level.HeightSteps(x, z) - level.HeightSteps(x + 1, z));
                walk |= d == 1;
                jump |= d == 4;
                block |= d == 8;
            }

            Assert.That(walk && jump && block, Is.True, "level needs 0.25 / 1.0 / 2.0 height differences");
            var player = configs.Load<PlayerConfig>(ConfigKeys.Player);
            Assert.That(player.JumpHeight, Is.GreaterThan(1f).And.LessThan(2f), "1.0 must be jumpable, 2.0 not");
            foreach (var item in level.Items)
                Assert.That(host.Container.Resolve<ItemDatabase>().Exists(item.Item), Is.True, item.Item);
        }

        [Test]
        public void TabOpensInventory_AndClosesIt()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            host.Events.Publish(new InventoryToggleRequested());
            Assert.That(host.States.Current, Is.InstanceOf<InventoryState>());
            Assert.That(host.Screens.IsOpen<UI.InventoryScreenViewModel>(), Is.True);
            Assert.That(host.Container.Resolve<GameSession>().PlayerControl.Value, Is.False);

            host.Events.Publish(new InventoryToggleRequested());
            Assert.That(host.States.Current, Is.InstanceOf<PlayState>());
            Assert.That(host.Screens.IsOpen<UI.InventoryScreenViewModel>(), Is.False);
        }

        [Test]
        public void PickUp_AutoEquipsIntoFreeHand_AsConfigured()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            var session = host.Container.Resolve<GameSession>();
            Assert.That(host.Container.Resolve<ConfigService>().Load<InventoryConfig>(ConfigKeys.Inventory).AutoEquipOnPickup, Is.True);
            Assert.That(session.PickUp("sword"), Is.True);
            Assert.That(session.PickUp("shield"), Is.True);
            Assert.That(session.Inventory.Right?.Id, Is.EqualTo("sword"));
            Assert.That(session.Inventory.Left?.Id, Is.EqualTo("shield"));

            // Рука занята — второй меч ложится в сетку.
            Assert.That(session.PickUp("sword"), Is.True);
            Assert.That(session.Inventory.Get(SlotRef.Grid(0))?.Id, Is.EqualTo("sword"));
        }

        [Test]
        public void PickUp_WithoutAutoEquip_PutsItemIntoGrid()
        {
            var source = new LayeredConfigSource(new FileConfigSource(ConfigsRoot()),
                new InMemoryConfigSource().Set("Inventory", "{ \"autoEquipOnPickup\": false }"));
            using var host = new GameHost(new ConsoleLogger(), new ConfigInstaller(source, new NewtonsoftConfigSerializer()),
                new ColorsActionInstaller());
            host.Start<BootState>();
            var session = host.Container.Resolve<GameSession>();
            Assert.That(session.PickUp("sword"), Is.True);
            Assert.That(session.Inventory.Right, Is.Null);
            Assert.That(session.Inventory.Get(SlotRef.Grid(0)).Id, Is.EqualTo("sword"));
        }

        [Test]
        public void RollLockWindow_DefaultsToWholeAttack_AndIsConfigurablePerStep()
        {
            var step = new AttackStepConfig { Windup = 0.2f, Strike = 0.1f, Recovery = 0.2f, Hold = 0.2f };
            step.Normalize();
            Assert.That(step.TotalTime(), Is.EqualTo(0.7f).Within(1e-4f));
            Assert.That(step.RollLocked(0f) && step.RollLocked(0.69f), Is.True, "by default no roll during the whole attack");
            Assert.That(step.RollLocked(0.71f), Is.False);

            step.RollLockFrom = 0.1f;
            step.RollLockTo = 0.3f;
            Assert.That(step.RollLocked(0.05f), Is.False, "early wind-up can be cancelled");
            Assert.That(step.RollLocked(0.2f), Is.True);
            Assert.That(step.RollLocked(0.4f), Is.False, "recovery can be cancelled");
        }

        [Test]
        public void SameColor_GivesDamageBonus()
        {
            using var host = CreateHost();
            var rules = host.Container.Resolve<DamageRules>();
            var sword = host.Container.Resolve<ItemDatabase>().Get("sword");
            var vsRed = rules.Calculate(sword, sword.Combo[0], "red");
            var vsBlue = rules.Calculate(sword, sword.Combo[0], "blue");
            Assert.That(vsRed.ColorBonus, Is.False);
            Assert.That(vsBlue.ColorBonus, Is.True);
            Assert.That(vsBlue.Damage, Is.EqualTo(vsRed.Damage * 1.5f).Within(0.001f));
        }
    }
}
