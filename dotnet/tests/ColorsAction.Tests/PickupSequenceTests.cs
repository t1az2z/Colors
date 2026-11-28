using ColorsAction.Configs;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Сцена подбора нового предмета: Configs/PickupSequence.json и кривые по времени.</summary>
    public class PickupSequenceTests
    {
        private static PickupSequenceConfig Load() =>
            new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer())
                .Load<PickupSequenceConfig>(PickupSequenceConfig.Key);

        [Test]
        public void Config_LastsFiveSeconds_AndEventsAreInOrder()
        {
            var c = Load();
            Assert.That(c.Enabled, Is.True);
            Assert.That(c.Duration, Is.EqualTo(5f).Within(1e-4f), "the whole pickup scene takes 5 seconds");
            Assert.That(c.Item.RiseEnd, Is.LessThan(c.Item.HitTime), "item rises before it flies into the player");
            Assert.That(c.Camera.CloseUpStart, Is.LessThanOrEqualTo(c.Item.HitTime + 0.1f), "close-up starts at the hit");
            Assert.That(c.Player.CheerStart, Is.GreaterThanOrEqualTo(c.Item.HitTime), "joy after the item is on");
            Assert.That(c.Fx.BurstTime, Is.GreaterThanOrEqualTo(c.Item.HitTime), "colour burst after the hit");
            Assert.That(c.Camera.ReturnStart, Is.LessThan(c.Duration), "camera returns before the end");
            Assert.That(c.Player.CheerStart + c.Player.Hops * c.Player.HopTime, Is.LessThanOrEqualTo(c.Player.CheerEnd + 1e-4f),
                "joy hops fit into the joy window");
        }

        [Test]
        public void Plays_ForNewWeaponsAndHealItems_Only()
        {
            var c = new PickupSequenceConfig();
            var sword = new ItemConfig { Kind = ItemKind.Weapon };
            var flask = new ItemConfig { Kind = ItemKind.Consumable, Heal = new HealConfig() };
            var key = new ItemConfig { Kind = ItemKind.Key };
            var metal = new ItemConfig { Kind = ItemKind.Resource };
            Assert.That(c.Plays(sword, false), Is.True);
            Assert.That(c.Plays(flask, false), Is.True);
            Assert.That(c.Plays(sword, true), Is.False, "known weapon is picked up without the scene");
            Assert.That(c.Plays(flask, true), Is.False);
            Assert.That(c.Plays(key, false), Is.False);
            Assert.That(c.Plays(metal, false), Is.False);
            c.Enabled = false;
            Assert.That(c.Plays(sword, false), Is.False);
        }

        [Test]
        public void Curves_StartAndEndAtRest()
        {
            var c = new PickupSequenceConfig();
            Assert.That(c.Rise(0f), Is.EqualTo(0f));
            Assert.That(c.Rise(c.Item.RiseEnd), Is.EqualTo(1f));
            Assert.That(c.Flight(c.Item.FlyStart), Is.EqualTo(0f));
            Assert.That(c.Flight(c.Item.HitTime), Is.EqualTo(1f));
            Assert.That(c.Windup(c.Item.ThrowStart), Is.EqualTo(0f));
            Assert.That(c.Windup((c.Item.ThrowStart + c.Item.FlyStart) * 0.5f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(c.Windup(c.Item.FlyStart), Is.EqualTo(0f));
            Assert.That(c.LookUp(c.Item.RiseEnd), Is.EqualTo(1f).Within(1e-4f), "looks up at the hovering item");
            Assert.That(c.LookUp(c.Item.HitTime), Is.EqualTo(0f));
            Assert.That(c.Cheer(c.Player.CheerStart + 0.3f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(c.Cheer(c.Player.CheerEnd), Is.EqualTo(0f));
            Assert.That(c.Glow(c.Fx.GlowStart - 0.01f), Is.EqualTo(0f));
            Assert.That(c.Glow(c.Fx.GlowEnd), Is.EqualTo(0f));

            c.CameraWeights(0f, out var approach, out _, out var hold);
            Assert.That(approach * hold, Is.EqualTo(0f), "camera starts from the normal view");
            c.CameraWeights(c.Item.HitTime + 0.5f, out approach, out var closeUp, out hold);
            Assert.That(approach * hold, Is.EqualTo(1f), "scene shot after the hit");
            Assert.That(closeUp, Is.EqualTo(1f), "close-up after the hit");
            c.CameraWeights(c.Duration, out approach, out _, out hold);
            Assert.That(approach * hold, Is.EqualTo(0f), "camera is back at the end");
        }

        [Test]
        public void Hops_HaveCrouchFlightAndLanding()
        {
            var c = new PickupSequenceConfig();
            var p = c.Player;
            Assert.That(c.HopPhase(p.CheerStart - 0.01f, out var index), Is.EqualTo(-1f));
            Assert.That(index, Is.EqualTo(-1));
            Assert.That(c.HopPhase(p.CheerStart + p.HopTime * 1.5f, out index), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(index, Is.EqualTo(1));
            Assert.That(c.HopPhase(p.CheerStart + p.HopTime * p.Hops + 0.01f, out _), Is.EqualTo(-1f), "no hops after the last");
            Assert.That(c.HopHeight(p.HopCrouch * 0.5f), Is.EqualTo(0f), "crouch on the ground");
            Assert.That(c.HopHeight((1f + p.HopCrouch) * 0.5f), Is.EqualTo(1f).Within(1e-4f), "top of the hop");
        }

        [Test]
        public void Validation_RejectsBrokenOrder()
        {
            var c = new PickupSequenceConfig();
            Assert.DoesNotThrow(c.OnConfigLoaded);
            c.Item.HitTime = c.Item.FlyStart - 0.1f;
            Assert.Throws<System.InvalidOperationException>(c.OnConfigLoaded);
        }
    }
}
