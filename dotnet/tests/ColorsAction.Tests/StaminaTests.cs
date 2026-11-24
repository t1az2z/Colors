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
                Id = "sword", Kind = ItemKind.OneHanded,
                Combo = { new AttackStepConfig { ActiveFrom = 0.6f, ActiveTo = 0.4f } }
            };
            item.OnConfigLoaded();
            Assert.That(item.Combo[0].ActiveFrom, Is.EqualTo(0.4f));
            Assert.That(item.Combo[0].ActiveTo, Is.EqualTo(0.6f));
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
