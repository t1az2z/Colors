using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.Progression;
using GameCore;
using GameCore.Config;
using GameCore.Hosting;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Приёмы оружия: заряд, отложенный добивающий, удар в прыжке, удары за апгрейдами, окно эффекта.</summary>
    public class WeaponMovesTests
    {
        private static ChargeConfig Charge() => new ChargeConfig
        {
            Time = 0.3f, MaxHold = 0.2f, DamageMultiplier = 2f, KnockbackMultiplier = 1.5f, StaminaCost = 10f
        };

        [Test]
        public void Charge_TapReleasesWithoutBonus()
        {
            var charge = new AttackCharge();
            charge.Begin(Charge());
            Assert.That(charge.Tick(0.016f, held: false, _ => true), Is.EqualTo(ChargeEvent.Released));
            Assert.That(charge.Active, Is.False);
            Assert.That(charge.Full, Is.False);
            Assert.That(charge.DamageMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void Charge_HoldReachesFull_SpendsStamina_ThenReleasesOnButtonUp()
        {
            var charge = new AttackCharge();
            var spent = 0f;
            charge.Begin(Charge());
            Assert.That(charge.Tick(0.2f, true, c => { spent += c; return true; }), Is.EqualTo(ChargeEvent.None));
            Assert.That(charge.Level, Is.EqualTo(0.2f / 0.3f).Within(1e-4f));
            Assert.That(spent, Is.Zero, "stamina is paid only for a full charge");
            Assert.That(charge.Tick(0.15f, true, c => { spent += c; return true; }), Is.EqualTo(ChargeEvent.Charged));
            Assert.That(spent, Is.EqualTo(10f));
            Assert.That(charge.Full && charge.Active, Is.True);
            Assert.That(charge.Tick(0.05f, false, _ => true), Is.EqualTo(ChargeEvent.Released));
            Assert.That(charge.DamageMultiplier, Is.EqualTo(2f));
            Assert.That(charge.KnockbackMultiplier, Is.EqualTo(1.5f));
            charge.Reset();
            Assert.That(charge.DamageMultiplier, Is.EqualTo(1f), "bonus lasts until the next attack");
        }

        [Test]
        public void Charge_FullChargeHeldTooLong_ReleasesItself()
        {
            var charge = new AttackCharge();
            charge.Begin(Charge());
            Assert.That(charge.Tick(0.3f, true, _ => true), Is.EqualTo(ChargeEvent.Charged));
            Assert.That(charge.Tick(0.1f, true, _ => true), Is.EqualTo(ChargeEvent.None));
            Assert.That(charge.Tick(0.11f, true, _ => true), Is.EqualTo(ChargeEvent.Released));
            Assert.That(charge.Full, Is.True);
        }

        [Test]
        public void Charge_NoStamina_ReleasesWithoutBonus()
        {
            var charge = new AttackCharge();
            charge.Begin(Charge());
            Assert.That(charge.Tick(0.5f, true, _ => false), Is.EqualTo(ChargeEvent.Released));
            Assert.That(charge.Full, Is.False);
            Assert.That(charge.DamageMultiplier, Is.EqualTo(1f));
        }

        private static FinisherConfig Finisher() => new FinisherConfig
        {
            MinHits = 2, DelayMin = 0.1f, DelayMax = 0.5f, Step = new AttackStepConfig()
        };

        [Test]
        public void Finisher_NeedsEnoughHitsAndAPauseInsideTheWindow()
        {
            var config = Finisher();
            var f = new ComboFinisher();
            f.ComboAttack(continuing: false);
            f.ComboWindowClosed(1f);
            Assert.That(f.Ready(config, 1.2f), Is.False, "one hit is not enough");

            f.ComboAttack(continuing: false);
            f.ComboAttack(continuing: true);
            Assert.That(f.Chain, Is.EqualTo(2));
            Assert.That(f.Ready(config, 1.2f), Is.False, "combo window still open");
            f.ComboWindowClosed(2f);
            Assert.That(f.Ready(config, 2.05f), Is.False, "too early — that is just a late combo press");
            Assert.That(f.Ready(config, 2.6f), Is.False, "too late — the pause is over");
            Assert.That(f.TryTrigger(config, 2.3f), Is.True);
            Assert.That(f.Chain, Is.Zero);
            Assert.That(f.TryTrigger(config, 2.31f), Is.False, "finisher resets the chain");
        }

        [Test]
        public void Finisher_NewChainRestartsCounting()
        {
            var config = Finisher();
            var f = new ComboFinisher();
            f.ComboAttack(false);
            f.ComboAttack(true);
            f.ComboAttack(false);
            f.ComboWindowClosed(0f);
            Assert.That(f.Ready(config, 0.2f), Is.False);
        }

        [Test]
        public void ComboSteps_SkipLockedSteps_AndWrap()
        {
            var combo = new List<AttackStepConfig>
            {
                new AttackStepConfig(), new AttackStepConfig(), new AttackStepConfig { Upgrade = 2 }, new AttackStepConfig { Upgrade = 1 }
            };
            var level = 0;
            bool Has(int tier) => level >= tier;
            Assert.That(ComboSteps.NextUnlocked(combo, 2, Has), Is.EqualTo(0), "locked third and fourth — back to the first");
            level = 1;
            Assert.That(ComboSteps.NextUnlocked(combo, 2, Has), Is.EqualTo(3));
            Assert.That(ComboSteps.NextUnlocked(combo, 4, Has), Is.EqualTo(0));
            level = 2;
            Assert.That(ComboSteps.NextUnlocked(combo, 2, Has), Is.EqualTo(2));
            Assert.That(ComboSteps.NextUnlocked(new List<AttackStepConfig> { new AttackStepConfig { Upgrade = 3 } }, 0, Has), Is.EqualTo(-1));
        }

        [Test]
        public void Plunge_HangsThenFallsUntilGround()
        {
            var config = new PlungeConfig { Hang = 0.1f, FallSpeed = 16f, MinHeight = 0.3f };
            Assert.That(PlungeAttack.CanStart(config, grounded: false, heightAboveGround: 0.2f), Is.False);
            Assert.That(PlungeAttack.CanStart(config, grounded: true, heightAboveGround: 5f), Is.False);
            Assert.That(PlungeAttack.CanStart(config, grounded: false, heightAboveGround: 0.6f), Is.True);

            var plunge = new PlungeAttack();
            plunge.Start(config);
            Assert.That(plunge.VerticalVelocity, Is.EqualTo(0f), "hang in the air");
            Assert.That(plunge.Tick(0.05f, false), Is.False);
            Assert.That(plunge.Phase, Is.EqualTo(PlungePhase.Hang));
            Assert.That(plunge.Tick(0.06f, false), Is.False);
            Assert.That(plunge.Phase, Is.EqualTo(PlungePhase.Fall));
            Assert.That(plunge.VerticalVelocity, Is.EqualTo(-16f));
            Assert.That(plunge.Tick(0.02f, true), Is.True, "landing frame");
            Assert.That(plunge.Phase, Is.EqualTo(PlungePhase.Landed));
            Assert.That(plunge.VerticalVelocity, Is.Null, "gravity is back after landing");
            Assert.That(plunge.Tick(0.02f, true), Is.False, "impact happens once");
        }

        [Test]
        public void Plunge_ToStep_IsAnAreaChop()
        {
            var step = new PlungeConfig { Hang = 0.1f, Radius = 2f, DamageMultiplier = 1.5f, LandHold = 0.2f, Recovery = 0.3f }.ToStep();
            Assert.That(step.Motion, Is.EqualTo(AttackMotion.Chop));
            Assert.That(step.Windup, Is.EqualTo(0.1f));
            Assert.That(step.Range, Is.EqualTo(2f));
            Assert.That(step.Arc, Is.EqualTo(360f));
            Assert.That(step.Hold, Is.EqualTo(0.2f));
            Assert.That(step.Duration, Is.EqualTo(0.1f + step.Strike + 0.3f).Within(1e-5f));
        }

        [Test]
        public void FxWindow_DefaultsToOldBehaviour_AndCanBeTrimmed()
        {
            var step = new AttackStepConfig { Windup = 0.2f, Strike = 0.1f, Hold = 0.2f, Recovery = 0.1f };
            step.Normalize();
            step.FxWindow(out var from, out var to);
            Assert.That(from, Is.EqualTo(0.2f - 0.04f).Within(1e-5f));
            Assert.That(to, Is.EqualTo(0.3f + 0.2f + 0.048f).Within(1e-5f));

            step.FxFrom = 0.25f;
            step.FxTo = 0.5f;
            step.Normalize();
            Assert.That(step.FxActive(0.14f), Is.False);
            Assert.That(step.FxActive(0.16f), Is.True);
            Assert.That(step.FxActive(0.31f), Is.False, "total 0.6 s: effect ends at 0.3 s");

            step.FxFrom = 0.8f;
            step.FxTo = 0.4f;
            step.Normalize();
            Assert.That(step.FxTo, Is.EqualTo(0.8f), "reversed range collapses forward");
        }

        private static GameHost CreateHost() =>
            new GameHost(new ConsoleLogger(),
                new ConfigInstaller(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer()),
                new ColorsActionInstaller());

        [Test]
        public void Sword_ThrustThird_ChopBehindUpgrade1_FinisherAndChargeBehindUpgrades()
        {
            using var host = CreateHost();
            var sword = host.Container.Resolve<ItemDatabase>().Get("sword");
            Assert.That(sword.Combo.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(sword.Combo[2].Motion, Is.EqualTo(AttackMotion.Thrust));
            Assert.That(sword.Combo[2].Upgrade, Is.Zero);
            Assert.That(sword.Combo[2].Charge, Is.Not.Null);
            Assert.That(sword.Combo[2].Charge.Upgrade, Is.EqualTo(3));
            Assert.That(sword.Combo[3].Motion, Is.EqualTo(AttackMotion.Chop));
            Assert.That(sword.Combo[3].Upgrade, Is.EqualTo(1));
            Assert.That(sword.Finisher, Is.Not.Null);
            Assert.That(sword.Finisher.Upgrade, Is.EqualTo(2));
            Assert.That(sword.Finisher.Step.Hop, Is.GreaterThan(0f));
            Assert.That(sword.Finisher.Step.Lunge, Is.LessThan(0f), "finisher hops back");
            Assert.That(sword.Plunge, Is.Not.Null);

            var upgrades = new WeaponUpgrades();
            Assert.That(ComboSteps.NextUnlocked(sword.Combo, 3, t => upgrades.Has("sword", t)), Is.EqualTo(0));
            upgrades.Set("sword", 1);
            Assert.That(ComboSteps.NextUnlocked(sword.Combo, 3, t => upgrades.Has("sword", t)), Is.EqualTo(3));
        }
    }
}
