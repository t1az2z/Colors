using ColorsAction.Combat;
using ColorsAction.Configs;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    public class StaminaTests
    {
        private static Stamina Make() => new Stamina(new StaminaConfig
        {
            Max = 100, RegenPerSecond = 20, RegenDelay = 1f, BlockRegenMultiplier = 0.5f
        });

        [Test]
        public void SpendsInstantly_AndAllowsLastActionDownToZero()
        {
            var s = Make();
            Assert.That(s.TrySpend(70), Is.True);
            Assert.That(s.Current.Value, Is.EqualTo(30f));
            Assert.That(s.TrySpend(50), Is.True, "action allowed while stamina > 0");
            Assert.That(s.Current.Value, Is.EqualTo(0f));
            Assert.That(s.TrySpend(1), Is.False);
        }

        [Test]
        public void RegeneratesSlowly_AfterDelay()
        {
            var s = Make();
            s.TrySpend(50);
            s.Tick(0.5f);
            Assert.That(s.Current.Value, Is.EqualTo(50f), "no regen during delay");
            s.Tick(0.6f);
            s.Tick(1f);
            Assert.That(s.Current.Value, Is.EqualTo(70f).Within(0.01f));
        }

        [Test]
        public void SwappedHitWindow_IsNormalized()
        {
            var item = new ItemConfig
            {
                Id = "sword", Kind = ItemKind.Weapon,
                Combo = { new AttackStepConfig { ActiveFrom = 0.6f, ActiveTo = 0.4f } }
            };
            item.OnConfigLoaded();
            Assert.That(item.Combo[0].ActiveFrom, Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(item.Combo[0].ActiveTo, Is.EqualTo(0.6f).Within(1e-4f));
        }

        [Test]
        public void ExplicitPhases_DefineTiming_WindupDoesNotChangeOtherPhases()
        {
            var item = new ItemConfig
            {
                Id = "sword", Kind = ItemKind.Weapon,
                Combo = { new AttackStepConfig { Windup = 0.3f, Strike = 0.1f, Recovery = 0.1f, Hold = 0.2f } }
            };
            item.OnConfigLoaded();
            var step = item.Combo[0];
            Assert.That(step.Duration, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(step.ActiveFrom * step.Duration, Is.EqualTo(0.3f).Within(1e-4f), "strike starts after the wind-up");
            Assert.That((step.ActiveTo - step.ActiveFrom) * step.Duration, Is.EqualTo(0.1f).Within(1e-4f));
            Assert.That(step.Hold, Is.EqualTo(0.2f));
        }

        [Test]
        public void Blocking_SlowsRegen()
        {
            var s = Make();
            s.TrySpend(50);
            s.Tick(1.01f);
            s.Blocking = true;
            s.Tick(1f);
            Assert.That(s.Current.Value, Is.EqualTo(60f).Within(0.01f));
        }
    }
}
