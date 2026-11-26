using System.Collections.Generic;
using ColorsAction.Configs;
using ColorsAction.Level;
using ColorsAction.States;
using GameCore;
using GameCore.Config;
using GameCore.Hosting;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Параметры запуска (окно редактора) и запуск воксельных карт из веб-редактора.</summary>
    public class LaunchOptionsTests
    {
        private static GameHost CreateHost(LaunchOptions options) =>
            new GameHost(new ConsoleLogger(),
                new ConfigInstaller(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer()),
                new ColorsActionInstaller(),
                new ActionInstaller(c => c.RegisterInstance(options)));

        [Test]
        public void Launch_GivesWeaponsUpgradesAndExperience_AndBootsVoxelLevel()
        {
            var options = new LaunchOptions
            {
                Enabled = true,
                Level = "VoxelLevels/Test",
                Weapons = new List<StartWeapon>
                {
                    new StartWeapon { Item = "sword", Upgrade = 2 },
                    new StartWeapon { Item = "shotgun", Upgrade = 9 }
                },
                Experience = 777
            };
            using var host = CreateHost(options);
            host.Start<BootState>();
            Assert.That(host.States.Current, Is.InstanceOf<PlayState>());

            var session = host.Container.Resolve<GameSession>();
            Assert.That(session.Inventory.Active?.Id, Is.EqualTo("sword"), "the first start weapon is in hands");
            Assert.That(session.Inventory.HasWeapon("shotgun"), Is.True, "the rest wait on the wheel");
            Assert.That(session.Upgrades.Level("sword"), Is.EqualTo(2));
            Assert.That(session.Upgrades.Level("shotgun"), Is.EqualTo(3), "уровень обрезается до максимума");
            Assert.That(session.Experience.Value, Is.EqualTo(777));
        }

        [Test]
        public void Launch_SkipsUnknownItems_AndStartsUnarmedByDefault()
        {
            var options = new LaunchOptions
            {
                Enabled = true,
                Weapons = new List<StartWeapon> { new StartWeapon { Item = "fists", Upgrade = 2 } }
            };
            using var host = CreateHost(options);
            host.Start<BootState>();

            var session = host.Container.Resolve<GameSession>();
            Assert.That(session.Inventory.Active, Is.Null, "кулаков нет в Items.json — без оружия");
            Assert.That(session.Inventory.Weapons, Is.Empty);
            Assert.That(session.Upgrades.Level("fists"), Is.EqualTo(0));

            var configs = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            Assert.That(configs.LoadTable<ItemConfig>(ConfigKeys.Items).ContainsKey("fists"), Is.False);
            Assert.That(configs.Load<CombatConfig>(ConfigKeys.Combat).UnarmedItem, Is.Empty);
        }

        [Test]
        public void Disabled_KeepsDefaults()
        {
            var game = new GameConfig { Level = "Levels/Test" };
            var options = new LaunchOptions { Enabled = false, Level = "VoxelLevels/Test", Experience = 5 };
            Assert.That(options.LevelKey(game), Is.EqualTo("Levels/Test"));
            using var host = CreateHost(options);
            host.Start<BootState>();
            Assert.That(host.Container.Resolve<GameSession>().Experience.Value, Is.EqualTo(0));
        }

        [Test]
        public void VoxelLayout_MapsObjectsWithStandHeight()
        {
            var configs = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            var voxel = configs.Load<VoxelLevelData>("VoxelLevels/Test").ToLevel();
            var skipped = new List<string>();
            var layout = VoxelPlayLayout.ToLevelConfig(voxel, skipped);

            var spawn = voxel.Objects[0];
            foreach (var o in voxel.Objects)
                if (o.Kind == LevelObjectKind.PlayerSpawn)
                    spawn = o;
            Assert.That(layout.Spawn.X, Is.EqualTo(spawn.Cell.X));
            Assert.That(layout.Spawn.Y, Is.Not.Null);
            Assert.That(layout.Spawn.Y!.Value, Is.EqualTo(VoxelPlayLayout.StandHeight(voxel, spawn.Cell)));

            var pedestals = 0;
            foreach (var o in voxel.Objects)
                if (o.Kind == LevelObjectKind.WeaponPedestal)
                    pedestals++;
            Assert.That(layout.Items.Count, Is.EqualTo(pedestals));
        }

        [Test]
        public void StandHeight_OnCubeAndInsideHalfBlock()
        {
            var level = new VoxelLevel();
            level.Set(new Int3(0, 0, 0), Block.Cube());
            level.Set(new Int3(1, 0, 0), Block.Cube());
            level.Set(new Int3(1, 1, 0), new Block(BlockShape.Half));
            Assert.That(VoxelPlayLayout.StandHeight(level, new Int3(0, 1, 0)), Is.EqualTo(1f));
            Assert.That(VoxelPlayLayout.StandHeight(level, new Int3(1, 1, 0)), Is.EqualTo(1.5f));
            Assert.That(VoxelPlayLayout.TryStandNear(level, 1, 0, 1f, 0.6f, out var h), Is.True);
            Assert.That(h, Is.EqualTo(1.5f));
        }
    }
}
