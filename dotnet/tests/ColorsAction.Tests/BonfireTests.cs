using System.Collections.Generic;
using System.IO;
using System.Linq;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Level;
using ColorsAction.Progression;
using ColorsAction.States;
using ColorsAction.UI;
using GameCore;
using GameCore.Config;
using GameCore.Hosting;
using GameCore.Newtonsoft;
using GameCore.UI;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Костёр: отдых, меню апгрейдов оружия за опыт, подсказки, невидимые стены, фиксация после смерти цели.</summary>
    public class BonfireTests
    {
        private static string ConfigsRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "StreamingAssets", "Configs")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null);
            return Path.Combine(dir!.FullName, "Assets", "StreamingAssets", "Configs");
        }

        private static GameHost CreateHost() =>
            new GameHost(new ConsoleLogger(),
                new ConfigInstaller(new FileConfigSource(ConfigsRoot()), new NewtonsoftConfigSerializer()),
                new ColorsActionInstaller());

        private static WeaponUpgradeShop Shop(WeaponUpgrades upgrades, ReactiveProperty<int> xp)
        {
            var config = new WeaponUpgradeConfig
            {
                Id = "sword",
                Tiers = new List<UpgradeTierConfig>
                {
                    new UpgradeTierConfig { Name = "a", Cost = 10 },
                    new UpgradeTierConfig { Name = "b", Cost = 20 },
                    new UpgradeTierConfig { Name = "c", Cost = 30 }
                }
            };
            config.OnConfigLoaded();
            return new WeaponUpgradeShop(upgrades, xp, new Dictionary<string, WeaponUpgradeConfig> { ["sword"] = config });
        }

        [Test]
        public void UpgradesConfig_HasThreeTiersForEveryWeapon()
        {
            using var host = CreateHost();
            var table = host.Container.Resolve<ConfigService>().LoadTable<WeaponUpgradeConfig>(ConfigKeys.Upgrades);
            foreach (var id in new[] { "sword", "shotgun" })
            {
                Assert.That(table.TryGet(id, out var config), Is.True, id);
                Assert.That(config.Tiers.Count, Is.EqualTo(WeaponUpgrades.MaxTier));
                Assert.That(config.Tiers.All(t => t.Cost > 0 && t.Name.Length > 0), Is.True);
            }

            Assert.That(table.Get("sword").Tier(1).Name, Does.Contain("4-я атака"));
            Assert.That(table.Get("sword").Tier(2).Name, Does.Contain("удар щитом"), "sword and shield are one weapon");
            Assert.That(table.TryGet("shield", out _), Is.False, "the shield has no upgrades of its own");
            Assert.That(table.Get("shotgun").Tier(2).Name, Is.EqualTo("Заряженный выстрел"));
            Assert.That(table.Get("shotgun").Tier(3).Name, Is.EqualTo("Отбить и выстрелить"));
        }

        [Test]
        public void Shop_SellsTiersInOrder_ForExperience()
        {
            var upgrades = new WeaponUpgrades();
            var xp = new ReactiveProperty<int>(25);
            var shop = Shop(upgrades, xp);

            Assert.That(shop.Status("sword", 1), Is.EqualTo(UpgradeTierStatus.Available));
            Assert.That(shop.Status("sword", 2), Is.EqualTo(UpgradeTierStatus.Locked));
            Assert.That(shop.TryBuy("sword"), Is.True);
            Assert.That(xp.Value, Is.EqualTo(15));
            Assert.That(upgrades.Has("sword", 1), Is.True);
            Assert.That(shop.Status("sword", 1), Is.EqualTo(UpgradeTierStatus.Owned));
            Assert.That(shop.Status("sword", 2), Is.EqualTo(UpgradeTierStatus.TooExpensive));
            Assert.That(shop.TryBuy("sword"), Is.False, "20 needed, 15 left");
            Assert.That(xp.Value, Is.EqualTo(15));

            xp.Value = 100;
            Assert.That(shop.TryBuy("sword") && shop.TryBuy("sword"), Is.True);
            Assert.That(upgrades.Level("sword"), Is.EqualTo(WeaponUpgrades.MaxTier));
            Assert.That(xp.Value, Is.EqualTo(50));
            Assert.That(shop.TryBuy("sword"), Is.False, "max tier");
            Assert.That(shop.NextCost("sword"), Is.EqualTo(-1));
            Assert.That(shop.TryBuy("axe"), Is.False, "no upgrades in config");
        }

        [Test]
        public void Rest_HealsOpensMenu_AndCloseReturnsToPlay()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            var session = host.Container.Resolve<GameSession>();
            session.Vitals.TryHit(30f);
            session.Stamina.TrySpend(40f);
            string rested = null;
            host.Events.Subscribe<BonfireRested>(e => rested = e.BonfireId);

            host.Events.Publish(new BonfireRestRequested("bonfire_a"));

            Assert.That(host.States.Current, Is.InstanceOf<BonfireState>());
            Assert.That(session.PlayerControl.Value, Is.False, "menu blocks control");
            Assert.That(session.Vitals.Health.Fraction, Is.EqualTo(1f));
            Assert.That(session.Stamina.Current.Value, Is.EqualTo(session.Stamina.Max));
            Assert.That(rested, Is.EqualTo("bonfire_a"));
            Assert.That(session.LastBonfire, Is.EqualTo("bonfire_a"));
            var screen = host.Screens.Find<BonfireScreenViewModel>();
            Assert.That(screen, Is.Not.Null);

            host.Events.Publish(new InventoryToggleRequested());
            Assert.That(host.States.Current, Is.InstanceOf<BonfireState>(), "inventory doesn't open over the bonfire menu");

            screen.Cancel();
            Assert.That(host.States.Current, Is.InstanceOf<PlayState>());
            Assert.That(host.Screens.IsOpen<BonfireScreenViewModel>(), Is.False);
            Assert.That(session.PlayerControl.Value, Is.True);
        }

        [Test]
        public void Menu_ListsPlayerWeapons_AndBuysWithConfirm()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            var session = host.Container.Resolve<GameSession>();
            session.PickUp("shotgun");
            session.PickUp("sword");
            session.Inventory.SetActive("sword");
            session.Experience.Value = 1000;
            // Ступени стоят ещё и ресурс оружия (Upgrades.json): меч — металл, дробовик — шестерёнки.
            var shop = host.Container.Resolve<WeaponUpgradeShop>();
            Assert.That(session.GiveResources(shop.ResourceId("sword"), 10), Is.True);
            Assert.That(session.GiveResources(shop.ResourceId("shotgun"), 10), Is.True);
            // Game.json может включать отладочные апгрейды — начинаем с нуля.
            session.Upgrades.Set("sword", 0);
            session.Upgrades.Set("shotgun", 0);

            host.Events.Publish(new BonfireRestRequested("b"));
            var vm = host.Screens.Find<BonfireScreenViewModel>();
            Assert.That(vm.Rows.Select(r => r.ItemId), Is.EqualTo(new[] { "sword", "shotgun" }), "weapon in hands first, then the rest of the wheel");
            Assert.That(vm.Cursor.Value, Is.EqualTo(new UpgradeCell(0, 1)));

            Assert.That(vm.Confirm(), Is.True);
            Assert.That(session.Upgrades.Level("sword"), Is.EqualTo(1));
            Assert.That(vm.Cursor.Value, Is.EqualTo(new UpgradeCell(0, 2)), "cursor moves to the next tier");

            vm.Navigate(1, 0);
            Assert.That(vm.Status(vm.Cursor.Value), Is.EqualTo(UpgradeTierStatus.Locked));
            Assert.That(vm.Confirm(), Is.False);
            Assert.That(vm.Message.Value, Is.Not.Empty);

            vm.Navigate(0, 1);
            vm.Navigate(-1, 0);
            vm.Navigate(-1, 0);
            Assert.That(vm.Cursor.Value, Is.EqualTo(new UpgradeCell(1, 1)));
            Assert.That(vm.Select(new UpgradeCell(1, 1)), Is.True, "mouse click buys");
            Assert.That(session.Upgrades.Has("shotgun", 1), Is.True);
            Assert.That(session.Experience.Value, Is.LessThan(1000));
        }

        [Test]
        public void Menu_WithoutWeapons_IsEmpty()
        {
            using var host = CreateHost();
            host.Start<BootState>();
            host.Events.Publish(new BonfireRestRequested("b"));
            var vm = host.Screens.Find<BonfireScreenViewModel>();
            Assert.That(vm.IsEmpty, Is.True);
            Assert.That(vm.Confirm(), Is.False);
            Assert.That(vm.Description.Value, Is.Not.Empty);
        }

        [Test]
        public void TestLevel_HasBonfireNearSpawn_AndVoxelImportKeepsIt()
        {
            using var host = CreateHost();
            var configs = host.Container.Resolve<ConfigService>();
            var level = configs.Load<LevelConfig>(configs.Load<GameConfig>(ConfigKeys.Game).Level);
            Assert.That(level.Bonfires, Is.Not.Empty);
            var b = level.Bonfires[0];
            Assert.That(b.Id, Is.Not.Empty);
            var dx = b.X - level.Spawn.X;
            var dz = b.Z - level.Spawn.Z;
            Assert.That(dx * dx + dz * dz, Is.LessThan(8 * 8), "bonfire is near the start");
            Assert.That(level.HeightSteps(b.X, b.Z), Is.Not.EqualTo(LevelConfig.Hole));

            var voxel = VoxelLevelData.FromHeightmap(level);
            Assert.That(voxel.Objects.Count(o => o.Kind == LevelObjectKind.Bonfire), Is.EqualTo(level.Bonfires.Count));

            var saved = configs.Load<VoxelLevelData>("VoxelLevels/Test");
            Assert.That(saved.Objects.Any(o => o.Kind == LevelObjectKind.Bonfire), Is.True, "editor copy of the test level has the bonfire too");
        }

        [Test]
        public void InteractHints_AreTiedToNearbyPickupsAndDoors()
        {
            using var host = CreateHost();
            var configs = host.Container.Resolve<ConfigService>();
            var level = configs.Load<LevelConfig>("Levels/Test");
            var items = new List<int>();
            var doors = new List<int>();

            var start = level.Hints.First(h => h.X == 13 && h.Z == 11);
            HintTargets.Resolve(level, start, items, doors);
            Assert.That(items.Select(i => level.Items[i].Item), Is.EquivalentTo(new[] { "sword", "shield" }));
            Assert.That(doors, Is.Empty);

            var door = level.Hints.First(h => h.X == 56 && h.Z == 29);
            HintTargets.Resolve(level, door, items, doors);
            Assert.That(items, Is.Empty);
            Assert.That(doors.Select(i => level.Doors[i].Id), Is.EqualTo(new[] { "courtyard_shortcut" }));

            var jump = level.Hints.First(h => h.Action == "jump");
            HintTargets.Resolve(level, jump, items, doors);
            Assert.That(items.Count + doors.Count, Is.Zero, "movement hints stay forever");

            // Явная цель: только ближайший предмет с этим id.
            var hint = new LevelHint { X = 9, Z = 60, Action = "interact", Target = "shotgun" };
            HintTargets.Resolve(level, hint, items, doors);
            Assert.That(items.Select(i => level.Items[i].Item), Is.EqualTo(new[] { "shotgun" }));
        }

        [Test]
        public void EdgeWalls_SurroundLevel_WithoutTouchingIt()
        {
            using var host = CreateHost();
            var level = host.Container.Resolve<ConfigService>().Load<LevelConfig>("Levels/Test");
            var walls = LevelEdgeWalls.Build(level);
            Assert.That(walls.Count, Is.EqualTo(4));

            bool Blocked(float x, float y, float z) => walls.Any(w => w.Contains(x, y, z));
            Assert.That(Blocked(-0.5f, 1f, 10f), Is.True, "west");
            Assert.That(Blocked(level.Width + 0.5f, 1f, 10f), Is.True, "east");
            Assert.That(Blocked(10f, 1f, -0.5f), Is.True, "south");
            Assert.That(Blocked(10f, 1f, level.Length + 0.5f), Is.True, "north");
            Assert.That(Blocked(-0.5f, -0.5f, -0.5f), Is.True, "corner");
            Assert.That(Blocked(0.5f, 1f, 0.5f), Is.False, "level cells stay free");
            Assert.That(Blocked(level.Width - 0.5f, 1f, level.Length - 0.5f), Is.False);

            var top = walls.Max(w => w.CenterY + w.SizeY * 0.5f);
            var highest = 0f;
            for (int z = 0; z < level.Length; z++)
            for (int x = 0; x < level.Width; x++)
                highest = System.Math.Max(highest, level.HeightAt(x, z));
            Assert.That(top, Is.GreaterThan(highest + 3f), "taller than any jump from the highest point");
        }

        [Test]
        public void FallKill_BelowLevelBottom_AndKillIgnoresInvulnerability()
        {
            using var host = CreateHost();
            var level = host.Container.Resolve<ConfigService>().Load<LevelConfig>("Levels/Test");
            var kill = LevelEdgeWalls.KillHeight(level);
            Assert.That(kill, Is.LessThan(-level.Depth - 2f), "below the bottom of holes and edge walls");
            Assert.That(LevelEdgeWalls.KillHeight(-3f), Is.EqualTo(-3f - LevelEdgeWalls.FallKillDepth));

            var vitals = new ColorsAction.Combat.PlayerVitals(100f, 2f);
            vitals.TryHit(10f);
            Assert.That(vitals.Invulnerable, Is.True);
            vitals.Kill();
            Assert.That(vitals.Health.IsDead, Is.True, "falling kills even during i-frames");
        }

        [Test]
        public void FallDamage_StepsFromPlayerConfig_AndIgnoresInvulnerability()
        {
            using var host = CreateHost();
            var player = host.Container.Resolve<ConfigService>().Load<PlayerConfig>(ConfigKeys.Player);
            var fall = player.FallDamage;
            Assert.That(fall.DamageFraction(3.7f), Is.Zero, "a jump off a 3-block ledge is safe");
            Assert.That(fall.DamageFraction(3.99f), Is.EqualTo(0.2f).Within(0.02f), "exactly 4 blocks, minus collision skin");
            Assert.That(fall.DamageFraction(4.5f - FallDamageConfig.HeightTolerance), Is.EqualTo(0.45f).Within(0.001f), "halfway 4..5 grows smoothly");
            Assert.That(fall.DamageFraction(5.5f - FallDamageConfig.HeightTolerance), Is.EqualTo(0.85f).Within(0.001f), "halfway 5..6");
            Assert.That(fall.DamageFraction(5f), Is.EqualTo(0.7f).Within(0.02f));
            Assert.That(fall.DamageFraction(6f), Is.EqualTo(1f));
            Assert.That(fall.DamageFraction(40f), Is.EqualTo(1f));

            // В игре ступени подняты на высоту прыжка (1.5): сойти с уступа в 4 блока безопасно, прыгнуть с него — больно.
            Assert.That(player.JumpHeight, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(player.FallDamageFraction(4f - 0.02f), Is.Zero, "walking off a 4-block ledge is safe");
            Assert.That(player.FallDamageFraction(3f + player.JumpHeight), Is.Zero, "a jump off a 3-block ledge is safe");
            Assert.That(player.FallDamageFraction(4f + player.JumpHeight - 0.02f), Is.EqualTo(0.2f).Within(0.02f), "a jump off a 4-block ledge hurts");
            Assert.That(player.FallDamageFraction(5.5f - 0.01f), Is.EqualTo(0.2f).Within(0.02f), "walking off a 5.5-block ledge hurts");
            Assert.That(player.FallDamageFraction(6f + player.JumpHeight), Is.EqualTo(1f), "a jump off a 6-block ledge kills");

            var off = new FallDamageConfig { Enabled = false };
            Assert.That(off.DamageFraction(40f), Is.Zero);
            Assert.Throws<System.InvalidOperationException>(() => new FallDamageConfig { HeavyHeight = 3f }.Validate());

            var vitals = new ColorsAction.Combat.PlayerVitals(100f, 2f);
            vitals.TryHit(10f);
            vitals.TakeFallDamage(70f);
            Assert.That(vitals.Health.Current.Value, Is.EqualTo(20f).Within(0.001f), "lands during i-frames still hurts");
        }

        [Test]
        public void LockOn_AfterTargetDies_GoesToEnemyNearestToIt_WithinRange()
        {
            // Игрок в (0,0), цель умерла в (0,5). Кандидаты: рядом с погибшей, ближе к игроку, далеко за радиусом.
            var c = new List<LockOnCandidate> { new LockOnCandidate(-2, 1), new LockOnCandidate(1, 6), new LockOnCandidate(0, 30) };
            Assert.That(LockOnRules.Nearest(0, 5, 0, 0, c, 12), Is.EqualTo(1));
            var far = new List<LockOnCandidate> { new LockOnCandidate(0, 30) };
            Assert.That(LockOnRules.Nearest(0, 5, 0, 0, far, 12), Is.EqualTo(-1), "nobody in range — release");
        }

        [Test]
        public void CombatDirector_Clear_ForgetsAttackersAndQueue()
        {
            var director = new CombatDirector(new CombatConfig());
            Assert.That(director.TryAcquire(1, 0.5f, 0.2f), Is.True);
            director.Enqueue(2);
            director.Clear();
            Assert.That(director.Attackers, Is.Empty);
            Assert.That(director.Queue, Is.Empty);
            Assert.That(director.TryAcquire(3, 0.5f, 0.2f), Is.True, "a respawned enemy can attack right away");
        }
    }
}
