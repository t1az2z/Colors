using System.Linq;
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
            Assert.That(player.RunMultiplier, Is.GreaterThan(1f));
            // Отскок: сейчас один шаг — перекат через верх.
            var steps = player.Dodge.Steps;
            Assert.That(steps.Count, Is.EqualTo(1));
            Assert.That(steps[0].Flip, Is.True, "dodge rolls over the top");
            Assert.That(steps[0].Distance, Is.GreaterThan(1f));
            Assert.That(player.Dodge.ComboWindow, Is.GreaterThan(0f));
            foreach (var step in steps)
                Assert.That(step.InvulnerableTo, Is.GreaterThan(step.InvulnerableFrom));
            // 0.25 — шагом, 0.5 — уже нет.
            Assert.That(player.StepHeight, Is.GreaterThan(0.25f).And.LessThan(0.5f));
            // Здоровье: блок health в Player.json.
            Assert.That(player.Health.Max, Is.GreaterThan(0f));
            Assert.That(player.Health.MaxPerLevel, Is.GreaterThanOrEqualTo(0f));
            Assert.That(player.Health.HitInvulnerability, Is.GreaterThan(0f));
            Assert.That(player.Health.BlinkStrength, Is.InRange(0f, 1f));
        }

        [Test]
        public void PlayerHealth_MaxGrowsPerLevel_AndBlinkPulsesToStrength()
        {
            var health = new PlayerHealthConfig { Max = 100, MaxPerLevel = 15, BlinkRate = 10, BlinkStrength = 0.4f };
            Assert.That(health.MaxAt(0), Is.EqualTo(100f));
            Assert.That(health.MaxAt(3), Is.EqualTo(145f));
            Assert.That(health.MaxAt(-2), Is.EqualTo(100f));
            // Период 0.1 с: в начале периода подсветки нет, в середине — полная сила.
            Assert.That(health.BlinkAmount(0f), Is.EqualTo(0f).Within(1e-4));
            Assert.That(health.BlinkAmount(0.05f), Is.EqualTo(0.4f).Within(1e-4));
            health.BlinkRate = 0f;
            Assert.That(health.BlinkAmount(0.03f), Is.EqualTo(0.4f), "rate 0 — steady tint");
            health.BlinkStrength = 0f;
            Assert.That(health.BlinkAmount(0.05f), Is.EqualTo(0f));
        }

        [Test]
        public void Weapons_GoToWheel_SwordCarriesShield()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            var session = host.Container.Resolve<GameSession>();
            var items = host.Container.Resolve<ItemDatabase>();
            var inv = session.Inventory;
            Assert.That(session.PickUp("sword"), Is.True);
            Assert.That(session.PickUp("shotgun"), Is.True);
            Assert.That(inv.Weapons.Select(w => w.Id), Is.EqualTo(new[] { "sword", "shotgun" }));
            Assert.That(inv.Active?.Id, Is.EqualTo("sword"), "the first weapon is taken into hands");
            foreach (var slot in inv.AllSlots())
                Assert.That(inv.Get(slot), Is.Null, "weapons never lie in the grid");
            Assert.That(items.OffhandOf(inv.Active.Config)?.Id, Is.EqualTo("shield"));
            Assert.That(inv.WeaponOfColor("red")?.Id, Is.EqualTo("shotgun"));
        }

        [Test]
        public void PlayerStartsWithoutWeapons()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            var inv = host.Container.Resolve<GameSession>().Inventory;
            Assert.That(inv.Active, Is.Null);
            Assert.That(inv.Weapons, Is.Empty);
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
        public void PickUp_TakesWeaponIntoEmptyHands_AsConfigured()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            var session = host.Container.Resolve<GameSession>();
            Assert.That(host.Container.Resolve<ConfigService>().Load<InventoryConfig>(ConfigKeys.Inventory).AutoEquipOnPickup, Is.True);
            Assert.That(session.PickUp("shotgun"), Is.True);
            Assert.That(session.PickUp("sword"), Is.True);
            Assert.That(session.Inventory.Active?.Id, Is.EqualTo("shotgun"), "hands are busy — the second weapon only goes to the wheel");

            // То же оружие второй раз: подбирается (уходит с земли), но на колесе остаётся одно.
            Assert.That(session.PickUp("sword"), Is.True);
            Assert.That(session.Inventory.Weapons.Count, Is.EqualTo(2));
        }

        [Test]
        public void PickUp_WithoutAutoEquip_OnlyPutsWeaponOnWheel()
        {
            var source = new LayeredConfigSource(new FileConfigSource(ConfigsRoot()),
                new InMemoryConfigSource().Set("Inventory", "{ \"autoEquipOnPickup\": false }"));
            using var host = new GameHost(new ConsoleLogger(), new ConfigInstaller(source, new NewtonsoftConfigSerializer()),
                new ColorsActionInstaller());
            host.Start<BootState>();
            var session = host.Container.Resolve<GameSession>();
            Assert.That(session.PickUp("sword"), Is.True);
            Assert.That(session.Inventory.Active, Is.Null);
            Assert.That(session.Inventory.HasWeapon("sword"), Is.True);
            Assert.That(session.Inventory.Get(SlotRef.Grid(0)), Is.Null);
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
        public void ColorWheel_DamageByDistance()
        {
            using var host = CreateHost();
            var rules = host.Container.Resolve<DamageRules>();
            var sword = host.Container.Resolve<ItemDatabase>().Get("sword");
            Assert.That(sword.Color, Is.EqualTo("blue"));
            var step = sword.Combo[0];
            var base_ = sword.Damage * step.DamageMultiplier;
            var vsBlue = rules.Calculate(sword, step, "blue");
            var vsGreen = rules.Calculate(sword, step, "green");
            var vsRed = rules.Calculate(sword, step, "red");
            var vsOrange = rules.Calculate(sword, step, "orange");
            var vsNone = rules.Calculate(sword, step, "none");
            Assert.That(vsBlue.Damage, Is.EqualTo(base_ * 0.4f).Within(0.001f));
            Assert.That(vsBlue.Effect, Is.EqualTo(ColorEffect.Weak));
            Assert.That(vsGreen.Damage, Is.EqualTo(base_ * 0.9f).Within(0.001f));
            Assert.That(vsRed.Damage, Is.EqualTo(base_ * 1.2f).Within(0.001f));
            Assert.That(vsRed.Effect, Is.EqualTo(ColorEffect.Strong));
            Assert.That(vsOrange.Damage, Is.EqualTo(base_ * 1.6f).Within(0.001f));
            Assert.That(vsOrange.Effect, Is.EqualTo(ColorEffect.Antipode));
            Assert.That(vsNone.Damage, Is.EqualTo(base_).Within(0.001f));
            Assert.That(vsNone.Effect, Is.EqualTo(ColorEffect.Neutral));
        }

        [Test]
        public void ColorWheel_AntipodePairs()
        {
            var wheel = new ColorWheel(new CombatConfig());
            Assert.That(wheel.Opposite("yellow"), Is.EqualTo("lilac"));
            Assert.That(wheel.Opposite("orange"), Is.EqualTo("blue"));
            Assert.That(wheel.Opposite("red"), Is.EqualTo("green"));
            Assert.That(wheel.Distance("yellow", "green"), Is.EqualTo(1), "the wheel wraps around");
            Assert.That(wheel.Distance("teal", "blue"), Is.EqualTo(-1));
        }
    }
}
