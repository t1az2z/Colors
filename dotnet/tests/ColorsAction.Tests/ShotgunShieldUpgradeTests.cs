using System;
using System.Collections.Generic;
using System.Linq;
using ColorsAction.Combat;
using ColorsAction.Configs;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Пули-снаряды дробовика, парирование выстрелом, апгрейды щита (окно парирования, удар щитом).</summary>
    public class ShotgunShieldUpgradeTests
    {
        private static ConfigService Configs() =>
            new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());

        // ------------------------------------------------------------------ залп

        [Test]
        public void Launches_AreNotAnEvenFan_AngleAndSpeedJittered()
        {
            var shot = new ShotParams { Pellets = 8, Spread = 30f, AngleJitter = 3f, VerticalJitter = 1f, Speed = 40f, SpeedJitter = 0.15f };
            var rnd = new Random(7);
            var launches = ShotPattern.Launches(shot, rnd.NextDouble);
            Assert.That(launches.Length, Is.EqualTo(8));
            Assert.That(launches.All(l => Math.Abs(l.Yaw) <= 15f + 3f + 1e-3f), Is.True, "inside the spread plus jitter");
            Assert.That(launches.All(l => Math.Abs(l.Pitch) <= 1f + 1e-3f), Is.True);
            Assert.That(launches.All(l => l.Speed >= 40f * 0.85f - 1e-3f && l.Speed <= 40f * 1.15f + 1e-3f), Is.True);
            Assert.That(launches.Select(l => l.Speed).Distinct().Count(), Is.GreaterThan(4), "pellets fly at different speeds");

            var even = ShotPattern.Launches(shot);
            var gaps = even.Zip(even.Skip(1), (a, b) => b.Yaw - a.Yaw).ToArray();
            Assert.That(gaps.All(g => Math.Abs(g - 3.75f) < 1e-3f), Is.True, "without random it is the plain even fan");
            var jitteredGaps = launches.Zip(launches.Skip(1), (a, b) => b.Yaw - a.Yaw).ToArray();
            Assert.That(jitteredGaps.Max() - jitteredGaps.Min(), Is.GreaterThan(1f), "with random the gaps are uneven");
        }

        [Test]
        public void ShotParams_FromShotgunConfig()
        {
            var shotgun = Configs().LoadTable<ItemConfig>(ConfigKeys.Items).Get("shotgun");
            var shot = ShotParams.From(shotgun.Ranged);
            Assert.That(shot.Pellets, Is.EqualTo(shotgun.Ranged.Pellets));
            Assert.That(shot.Range, Is.EqualTo(shotgun.Ranged.Range));
            Assert.That(shot.Speed, Is.GreaterThan(10f), "pellets are projectiles with real speed");
            Assert.That(shot.DamageMultiplier, Is.EqualTo(1f));
            Assert.That(shotgun.Ranged.Upgrades.StaminaCostMultiplier, Is.LessThan(1f), "upgrade 1: cheaper shot");
            Assert.That(shotgun.Ranged.IdleHold.Forward, Is.GreaterThan(0.5f), "idle: held in front of the cube");
            Assert.That(shotgun.Ranged.IdleHold.Pitch, Is.GreaterThan(0f), "idle: barrel tilted down");
        }

        [Test]
        public void ChargedShot_MorePellets_WiderFartherStrongerBrighter()
        {
            var ranged = Configs().LoadTable<ItemConfig>(ConfigKeys.Items).Get("shotgun").Ranged;
            Assert.That(ranged.Charge, Is.Not.Null, "shotgun charges with the generic charge mechanism");
            Assert.That(ranged.Charge.Upgrade, Is.EqualTo(2), "charged shot is upgrade 2");
            Assert.That(ranged.Charge.StaminaCost, Is.GreaterThan(0f), "charging costs extra stamina");
            var normal = ShotParams.From(ranged);
            var charged = normal.Charged(ranged.ChargedShot, ranged.Charge);
            Assert.That(charged.Pellets, Is.GreaterThan(normal.Pellets));
            Assert.That(charged.Spread, Is.EqualTo(normal.Spread * 1.15f).Within(1e-3), "~15% wider");
            Assert.That(charged.Range, Is.GreaterThan(normal.Range));
            Assert.That(charged.DamageMultiplier, Is.GreaterThan(1f));
            Assert.That(charged.Knockback, Is.GreaterThan(normal.Knockback));
            Assert.That(charged.Glow, Is.GreaterThan(normal.Glow));
            Assert.That(charged.PelletSize, Is.GreaterThan(normal.PelletSize));
            Assert.That(normal.Pellets, Is.EqualTo(ranged.Pellets), "the normal shot is untouched (struct copy)");
        }

        // ------------------------------------------------------------------ блок дробовиком

        [Test]
        public void ShotgunBlock_OnSecondary_PartialAbsorb_ChargedParryAtUpgrade3()
        {
            var ranged = Configs().LoadTable<ItemConfig>(ConfigKeys.Items).Get("shotgun").Ranged;
            Assert.That(ranged.Block, Is.Not.Null, "the shotgun blocks on the secondary attack");
            Assert.That(ranged.Block.Absorb, Is.GreaterThan(0f).And.LessThan(1f), "blocks only part of the damage");
            Assert.That(ranged.Block.Angle, Is.GreaterThan(0f));
            Assert.That(ranged.Block.ChargedParryUpgrade, Is.EqualTo(3), "upgrade 3: fully charged shot after a parry");
            Assert.That(ranged.Block.Hold.Height, Is.GreaterThan(ranged.IdleHold.Height), "raised in front");
        }

        [Test]
        public void BlockRules_Shield_TakesNothing_Gun_TakesTheRest()
        {
            var shield = BlockRules.Resolve(10f, 1f, 100f, 5f, 1f);
            Assert.That(shield.Held, Is.True);
            Assert.That(shield.Damage, Is.EqualTo(0f));
            Assert.That(shield.StaminaCost, Is.EqualTo(5f + 10f * BlockRules.CostPerDamage).Within(1e-4));

            var gun = BlockRules.Resolve(10f, 0.5f, 100f, 5f, 1f);
            Assert.That(gun.Held, Is.True);
            Assert.That(gun.Damage, Is.EqualTo(5f).Within(1e-4), "half of the hit gets through");
            Assert.That(gun.StaminaCost, Is.EqualTo(5f + 5f * BlockRules.CostPerDamage).Within(1e-4), "pays only for what it blocked");

            var cheaper = BlockRules.Resolve(10f, 0.5f, 100f, 5f, 0.6f);
            Assert.That(cheaper.StaminaCost, Is.EqualTo(gun.StaminaCost * 0.6f).Within(1e-4), "upgrade 1 makes the block cheaper");
        }

        [Test]
        public void BlockRules_NotEnoughStamina_BlockBroken_HalfProtection()
        {
            var shield = BlockRules.Resolve(10f, 1f, 1f, 5f, 1f);
            Assert.That(shield.Held, Is.False);
            Assert.That(shield.Damage, Is.EqualTo(5f).Within(1e-4), "broken shield still halves the hit");

            var gun = BlockRules.Resolve(10f, 0.5f, 1f, 5f, 1f);
            Assert.That(gun.Held, Is.False);
            Assert.That(gun.Damage, Is.EqualTo(7.5f).Within(1e-4));
        }

        // ------------------------------------------------------------------ парирование выстрелом

        [Test]
        public void Volley_NormalTarget_TakesEveryPelletAtOnce()
        {
            var volley = new PelletVolley<string>(8, 3, 3f);
            var hit = volley.Hit("dummy", false, false);
            Assert.That(hit.Pellets, Is.EqualTo(1));
            Assert.That(hit.DamageMultiplier, Is.EqualTo(1f));
            Assert.That(hit.Parry, Is.False);
            Assert.That(volley.HitsOn("dummy"), Is.EqualTo(1));
        }

        [Test]
        public void Volley_ThreePelletsInParryWindow_Parry_WithBonusDamage()
        {
            var volley = new PelletVolley<string>(8, 3, 3f);
            Assert.That(volley.Hit("foe", true, true).Pellets, Is.EqualTo(0), "deferred: damage would break the wind-up");
            Assert.That(volley.Hit("foe", false, true).Pellets, Is.EqualTo(0), "window is judged by the first pellet");
            var third = volley.Hit("foe", false, true);
            Assert.That(third.Parry, Is.True);
            Assert.That(third.Pellets, Is.EqualTo(3), "all stored pellets land now");
            Assert.That(third.DamageMultiplier, Is.EqualTo(3f));
            var fourth = volley.Hit("foe", false, false);
            Assert.That(fourth.Parry, Is.False, "parried once");
            Assert.That(fourth.Pellets, Is.EqualTo(1));
            Assert.That(fourth.DamageMultiplier, Is.EqualTo(3f));
            Assert.That(volley.Parried("foe"), Is.True);
        }

        [Test]
        public void Volley_TooFewPellets_DeferredHitsLandAsNormalDamage()
        {
            var volley = new PelletVolley<string>(3, 3, 3f);
            volley.Hit("foe", true, true);
            volley.Hit("foe", true, true);
            for (int i = 0; i < 3; i++)
                volley.PelletFinished();
            Assert.That(volley.Done, Is.True);
            var deferred = volley.TakeDeferred();
            Assert.That(deferred.Count, Is.EqualTo(1));
            Assert.That(deferred[0].Key, Is.EqualTo("foe"));
            Assert.That(deferred[0].Value, Is.EqualTo(2));
            Assert.That(volley.TakeDeferred(), Is.Empty, "taken once");
            Assert.That(volley.Parried("foe"), Is.False);
        }

        [Test]
        public void Volley_AttackEndsBeforeThirdPellet_NoParry()
        {
            var volley = new PelletVolley<string>(8, 3, 3f);
            volley.Hit("foe", true, true);
            var second = volley.Hit("foe", true, false);
            Assert.That(second.Parry, Is.False);
            Assert.That(second.Pellets, Is.EqualTo(2), "stored pellets released at normal damage");
            Assert.That(second.DamageMultiplier, Is.EqualTo(1f));
            Assert.That(volley.Hit("foe", true, true).Pellets, Is.EqualTo(1), "no longer a parry candidate");
        }

        [Test]
        public void Volley_ParryDisabled_WithoutUpgrade()
        {
            var volley = new PelletVolley<string>(8, 0, 3f);
            for (int i = 0; i < 5; i++)
            {
                var hit = volley.Hit("foe", true, true);
                Assert.That(hit.Pellets, Is.EqualTo(1));
                Assert.That(hit.Parry, Is.False);
            }
        }

        // ------------------------------------------------------------------ окно парирования

        private static EnemyConfig Club(float parryFrom, float parryTo) => new EnemyConfig
        {
            Id = "club_cube", MaxHealth = 40, AggroRadius = 7, LoseRadius = 13, AlertDuration = 0.1f,
            MoveSpeed = 4f, WaitDistance = 3f, AttackCooldown = 0.5f, StaggerDuration = 0.3f,
            Attacks = new List<EnemyAttackConfig>
            {
                new EnemyAttackConfig { Windup = 0.4f, Strike = 0.1f, Hold = 0.2f, Recovery = 0.5f, Range = 2f, ParryFrom = parryFrom, ParryTo = parryTo }
            }
        };

        /// <summary>Враг в замахе/ударе на время <paramref name="t"/> относительно начала удара.</summary>
        private static EnemyBrain AttackAt(float t)
        {
            var config = Club(-0.04f, 0.06f);
            config.OnConfigLoaded();
            var director = new CombatDirector(new CombatConfig { MaxAttackers = 1 });
            var brain = new EnemyBrain(1, config, director, 1);
            for (int i = 0; i < 20 && brain.State != EnemyState.Windup; i++)
            {
                director.Tick(0.01f);
                brain.Tick(0.01f, 1f);
            }

            Assert.That(brain.State, Is.EqualTo(EnemyState.Windup));
            var left = brain.Attack.Windup + t;
            while (left > 1e-5f)
            {
                var dt = Math.Min(0.001f, left);
                brain.Tick(dt, 1f);
                left -= dt;
            }

            return brain;
        }

        [Test]
        public void ParryWindow_WidensEquallyBothWays()
        {
            var attack = new EnemyAttackConfig { ParryFrom = -0.04f, ParryTo = 0.06f };
            attack.ParryWindow(0.5f, out var from, out var to);
            Assert.That(from, Is.EqualTo(-0.065f).Within(1e-5), "25% of the length earlier");
            Assert.That(to, Is.EqualTo(0.085f).Within(1e-5), "25% of the length later");

            Assert.That(AttackAt(-0.055f).InParryWindow, Is.False, "too early for the plain window");
            Assert.That(AttackAt(-0.055f).IsInParryWindow(0.5f), Is.True, "upgraded shield parries earlier");
            Assert.That(AttackAt(0.075f).InParryWindow, Is.False, "too late for the plain window");
            Assert.That(AttackAt(0.075f).IsInParryWindow(0.5f), Is.True, "upgraded shield parries later");
            Assert.That(AttackAt(0.095f).IsInParryWindow(0.5f), Is.False);
            Assert.That(AttackAt(0.075f).TryParry(0.5f), Is.True);
        }

        [Test]
        public void ForcedParry_WhileAttackGoes_RemembersWhereTheStrikeWas()
        {
            var brain = AttackAt(0.05f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Strike));
            Assert.That(brain.CanBeParried, Is.True);
            Assert.That(brain.Parry(), Is.True);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Parried));
            Assert.That(brain.ParriedFromState, Is.EqualTo(EnemyState.Strike));
            Assert.That(brain.ParriedFromProgress, Is.EqualTo(0.5f).Within(0.02f));
            Assert.That(brain.Parry(), Is.False, "nothing to parry any more");
        }

        // ------------------------------------------------------------------ щит

        [Test]
        public void ShieldConfig_LoadedWithUpgrades()
        {
            var shield = Configs().LoadTable<ItemConfig>(ConfigKeys.Items).Get("shield");
            Assert.That(shield.Shield, Is.Not.Null);
            Assert.That(shield.Shield.BlockCostMultiplier, Is.LessThan(1f), "upgrade 1: cheaper block");
            Assert.That(shield.Shield.ParryWindowBonus, Is.EqualTo(0.5f).Within(1e-5), "upgrade 2: +50% window");
            Assert.That(shield.Shield.BashMaxGap, Is.GreaterThan(0f));
            Assert.That(shield.Shield.BashParryMultiplier, Is.EqualTo(2f).Within(1e-5), "bash in the parry window: x2");
            Assert.That(shield.Shield.BashUpgrade, Is.EqualTo(2), "shield bash opens with sword tier 2");
            Assert.That(shield.Shield.BlockCostUpgrade, Is.EqualTo(0), "sword upgrades stay as they were");
            Assert.That(shield.Shield.ParryWidenUpgrade, Is.EqualTo(0));
            var items = Configs().LoadTable<ItemConfig>(ConfigKeys.Items);
            Assert.That(items.Get("sword").Offhand, Is.EqualTo("shield"));
            Assert.That(shield.Color, Is.EqualTo(items.Get("sword").Color), "one blue weapon");
            Assert.That(new ItemConfig { Id = "s", Kind = ItemKind.Shield }.Tap(c => c.OnConfigLoaded()).Shield, Is.Not.Null,
                "a shield without the section gets defaults");
        }

        [Test]
        public void Bash_ReleaseAndQuickPress()
        {
            var bash = new ShieldBashTrigger();
            Assert.That(bash.Tick(0.016f, true, 0.25f), Is.False, "first press is a normal block");
            Assert.That(bash.Tick(0.3f, true, 0.25f), Is.False);
            Assert.That(bash.Tick(0.016f, false, 0.25f), Is.False, "released");
            Assert.That(bash.Tick(0.1f, false, 0.25f), Is.False);
            Assert.That(bash.Tick(0.05f, true, 0.25f), Is.True, "pressed again quickly: bash");
            Assert.That(bash.Tick(0.016f, true, 0.25f), Is.False, "once");

            Assert.That(bash.Tick(0.016f, false, 0.25f), Is.False);
            Assert.That(bash.Tick(0.3f, false, 0.25f), Is.False);
            Assert.That(bash.Tick(0.016f, true, 0.25f), Is.False, "too slow: plain block");
        }
    }

    internal static class TestTap
    {
        public static T Tap<T>(this T value, Action<T> action)
        {
            action(value);
            return value;
        }
    }
}
