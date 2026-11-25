using System;
using System.Linq;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    public class ShotgunTests
    {
        private static ConfigService Configs() =>
            new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());

        [Test]
        public void ShotgunConfig_MatchesSpec()
        {
            var shotgun = Configs().LoadTable<ItemConfig>(ConfigKeys.Items).Get("shotgun");
            Assert.That(shotgun.IsRanged && shotgun.IsTwoHanded, Is.True, "two-handed ranged weapon");
            Assert.That(shotgun.Color, Is.EqualTo("red"));
            Assert.That(shotgun.Damage, Is.EqualTo(3f), "damage per pellet");
            Assert.That(shotgun.Ranged.Pellets, Is.EqualTo(8));
            Assert.That(shotgun.Ranged.StaminaCost, Is.EqualTo(20f));
            Assert.That(shotgun.Ranged.Cooldown, Is.EqualTo(0.3f).Within(1e-4));
            Assert.That(shotgun.Ranged.Range, Is.GreaterThan(0f));
            Assert.That(shotgun.Ranged.Recoil, Is.GreaterThan(0f), "recoil pushes the player back");
        }

        [Test]
        public void Shotgun_TakesBothHands()
        {
            var configs = Configs();
            var items = new ItemDatabase(configs);
            var inv = new PlayerInventory();
            var shotgun = items.Create("shotgun");
            Assert.That(inv.TryAdd(shotgun), Is.True);
            Assert.That(inv.AutoEquip(shotgun), Is.True);
            Assert.That(inv.Right, Is.SameAs(shotgun));
            Assert.That(inv.InHand(Hand.Left), Is.SameAs(shotgun));
        }

        [Test]
        public void SameColorBonus_RedPelletsOnRedEnemy()
        {
            var configs = Configs();
            var combat = configs.Load<CombatConfig>(ConfigKeys.Combat);
            var rules = new DamageRules(combat);
            var shotgun = configs.LoadTable<ItemConfig>(ConfigKeys.Items).Get("shotgun");
            var enemies = configs.LoadTable<EnemyConfig>(ConfigKeys.Enemies);
            var red = enemies.Get("club_cube_red");
            var blue = enemies.Get("club_cube");
            Assert.That(red.Color, Is.EqualTo("red"));
            Assert.That(rules.Calculate(shotgun, null, red.Color).Damage, Is.EqualTo(3f * combat.SameColorMultiplier).Within(1e-4));
            Assert.That(rules.Calculate(shotgun, null, blue.Color).ColorBonus, Is.False);
        }

        [Test]
        public void PelletAngles_CoverTheSpreadEvenly()
        {
            var rnd = new Random(1);
            var angles = ShotPattern.PelletAngles(8, 30f, rnd.NextDouble);
            Assert.That(angles.Length, Is.EqualTo(8));
            Assert.That(angles.All(a => a >= -15f && a <= 15f), Is.True);
            for (int i = 0; i < 8; i++)
                Assert.That(angles[i], Is.InRange(-15f + i * 3.75f, -15f + (i + 1) * 3.75f), "one pellet per sector");
            Assert.That(ShotPattern.PelletAngles(1, 0f)[0], Is.EqualTo(0f));
        }

        [Test]
        public void Trigger_KeepsCooldown_AndBuffersEarlyPress()
        {
            var config = new RangedConfig { Cooldown = 0.3f, InputBuffer = 0.12f };
            var trigger = new GunTrigger();
            Assert.That(trigger.Tick(0.016f, true, config), Is.True);
            trigger.Fired(config);
            Assert.That(trigger.Tick(0.2f, false, config), Is.False);
            Assert.That(trigger.Tick(0.05f, true, config), Is.False, "cooldown, press buffered");
            Assert.That(trigger.Tick(0.06f, false, config), Is.True, "buffered press fires right after the cooldown");
            trigger.Fired(config);
            Assert.That(trigger.Tick(0.31f, false, config), Is.False, "no press, no shot");
            Assert.That(trigger.Tick(0.016f, true, config), Is.True);
            trigger.Failed();
            Assert.That(trigger.Tick(0.016f, false, config), Is.False, "failed shot (no stamina) drops the press");
        }

        [Test]
        public void TestLevel_HasShootingField_With4Red3Blue()
        {
            var configs = Configs();
            var level = configs.Load<LevelConfig>("Levels/Test");
            var field = level.Enemies.Where(e => e.Z >= 64).ToList();
            Assert.That(field.Count(e => e.Type == "club_cube_red"), Is.EqualTo(4));
            Assert.That(field.Count(e => e.Type == "club_cube"), Is.EqualTo(3));
            Assert.That(level.Items.Any(i => i.Item == "shotgun"), Is.True);
        }
    }
}
