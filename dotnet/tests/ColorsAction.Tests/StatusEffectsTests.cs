using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Status;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    public class StatusEffectsTests
    {
        private static StatusEffectsConfig Config(StatusStacking stacking = StatusStacking.Refresh, int maxStacks = 1)
        {
            var config = new StatusEffectsConfig
            {
                Effects = new Dictionary<string, StatusEffectConfig>
                {
                    ["stun"] = new StatusEffectConfig { Kind = StatusEffectKinds.Stun, Duration = 1f },
                    ["vulnerable"] = new StatusEffectConfig
                    {
                        Kind = StatusEffectKinds.DamageTaken, Duration = 2f, Value = 1.5f, Stacking = stacking, MaxStacks = maxStacks
                    },
                    ["empowered"] = new StatusEffectConfig { Kind = StatusEffectKinds.DamageDealt, Duration = 3f, Value = 2f }
                },
                OnParry = new List<StatusApplication> { new StatusApplication { Effect = "vulnerable" } }
            };
            config.OnConfigLoaded();
            return config;
        }

        private static void Run(StatusEffects status, float seconds)
        {
            for (float t = 0; t < seconds - 0.0001f; t += 0.02f)
                status.Tick(0.02f);
        }

        [Test]
        public void Apply_LastsForConfiguredDuration_ThenExpires()
        {
            var status = new StatusEffects(Config());
            StatusEffect removed = null;
            status.Removed += e => removed = e;
            status.Apply("stun");
            Assert.That(status.IsStunned, Is.True);
            Run(status, 0.9f);
            Assert.That(status.IsStunned, Is.True);
            Run(status, 0.2f);
            Assert.That(status.IsStunned, Is.False);
            Assert.That(removed?.Id, Is.EqualTo("stun"));
            Assert.That(status.Active, Is.Empty);
        }

        [Test]
        public void DurationOverride_And_Permanent()
        {
            var status = new StatusEffects(Config());
            status.Apply("stun", 0.3f);
            Run(status, 0.4f);
            Assert.That(status.IsStunned, Is.False, "own duration overrides the config");

            status.Apply("stun", 0f);
            Run(status, 10f);
            Assert.That(status.IsStunned, Is.True, "0 = until removed");
            Assert.That(status.Remove("stun"), Is.True);
            Assert.That(status.IsStunned, Is.False);
        }

        [Test]
        public void DamageModifiers_MultiplyIndependently()
        {
            var status = new StatusEffects(Config());
            Assert.That(status.DamageTakenMultiplier, Is.EqualTo(1f));
            status.Apply("vulnerable");
            status.Apply("empowered");
            Assert.That(status.DamageTakenMultiplier, Is.EqualTo(1.5f).Within(1e-5));
            Assert.That(status.DamageDealtMultiplier, Is.EqualTo(2f).Within(1e-5));
            status.Clear();
            Assert.That(status.DamageTakenMultiplier, Is.EqualTo(1f));
            Assert.That(status.DamageDealtMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void Stacking_Refresh_Extend_Stack_Ignore()
        {
            var refresh = new StatusEffects(Config(StatusStacking.Refresh));
            refresh.Apply("vulnerable");
            Run(refresh, 1.5f);
            refresh.Apply("vulnerable");
            Assert.That(refresh.Get("vulnerable").Remaining, Is.EqualTo(2f).Within(1e-4));
            Assert.That(refresh.DamageTakenMultiplier, Is.EqualTo(1.5f).Within(1e-5), "refresh does not grow the bonus");

            var extend = new StatusEffects(Config(StatusStacking.Extend));
            extend.Apply("vulnerable");
            Run(extend, 0.5f);
            extend.Apply("vulnerable");
            Assert.That(extend.Get("vulnerable").Remaining, Is.EqualTo(3.5f).Within(1e-3));

            var stack = new StatusEffects(Config(StatusStacking.Stack, maxStacks: 2));
            stack.Apply("vulnerable");
            stack.Apply("vulnerable");
            stack.Apply("vulnerable");
            Assert.That(stack.Get("vulnerable").Stacks, Is.EqualTo(2));
            Assert.That(stack.DamageTakenMultiplier, Is.EqualTo(2f).Within(1e-5), "+50% per stack");

            var ignore = new StatusEffects(Config(StatusStacking.Ignore));
            ignore.Apply("vulnerable");
            Run(ignore, 1.5f);
            ignore.Apply("vulnerable");
            Assert.That(ignore.Get("vulnerable").Remaining, Is.EqualTo(0.5f).Within(1e-3));
        }

        [Test]
        public void CustomKind_CanBeRegistered_AndTicksTheTarget()
        {
            StatusEffectKinds.Register("TestPoison", () => new TestPoison());
            var config = Config();
            config.Effects["poison"] = new StatusEffectConfig { Kind = "TestPoison", Duration = 1f, Value = 10f };
            config.OnConfigLoaded();
            var health = new Health(100f);
            var status = new StatusEffects(config, new HealthStatusTarget(health));
            status.Apply("poison");
            Run(status, 1f);
            Assert.That(health.Current.Value, Is.EqualTo(90f).Within(0.3f), "10 damage per second for 1 s");
            Assert.That(status.Has("poison"), Is.False);
        }

        [Test]
        public void UnknownKind_FailsOnLoad()
        {
            var config = new StatusEffectsConfig
            {
                Effects = new Dictionary<string, StatusEffectConfig> { ["x"] = new StatusEffectConfig { Kind = "Nope" } }
            };
            Assert.Throws<System.InvalidOperationException>(() => config.OnConfigLoaded());
            var parry = new StatusEffectsConfig { OnParry = new List<StatusApplication> { new StatusApplication { Effect = "missing" } } };
            Assert.Throws<System.InvalidOperationException>(() => parry.OnConfigLoaded());
        }

        [Test]
        public void RealConfig_Loads_WithStunAndParryEffects()
        {
            var configs = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            var config = configs.Load<StatusEffectsConfig>(StatusEffectsConfig.Key);
            Assert.That(config.TryGet(StatusEffectsConfig.StunId, out var stun), Is.True);
            Assert.That(stun.Kind, Is.EqualTo(StatusEffectKinds.Stun));
            Assert.That(config.OnParry, Is.Not.Empty);
        }

        // ------------------------------------------------------------------ враг

        private static EnemyConfig Enemy()
        {
            var config = new EnemyConfig
            {
                Id = "club_cube", MaxHealth = 40, AggroRadius = 7, LoseRadius = 13, AlertDuration = 0.1f,
                MoveSpeed = 4f, WaitDistance = 3f, AttackCooldown = 0.5f, StaggerDuration = 0.3f, ParriedDuration = 1f,
                Attacks = new List<EnemyAttackConfig>
                {
                    new EnemyAttackConfig { Motion = EnemyAttackMotion.Sweep, Windup = 1f, Strike = 0.1f, Hold = 0.3f, Recovery = 0.5f, Range = 2f }
                }
            };
            config.OnConfigLoaded();
            return config;
        }

        private static EnemyBrain Brain(StatusEffectsConfig status = null) =>
            new EnemyBrain(1, Enemy(), new CombatDirector(new CombatConfig { MaxAttackers = 1, AttackerGap = 0.4f }), 1, status ?? Config());

        private static void Run(EnemyBrain brain, float seconds, float distance = 1f)
        {
            for (float t = 0; t < seconds - 0.0001f; t += 0.02f)
                brain.Tick(0.02f, distance);
        }

        [Test]
        public void Parry_StunsForParriedDuration_AndMakesVulnerable()
        {
            var brain = Brain();
            Run(brain, 0.2f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Windup));
            Assert.That(brain.Parry(), Is.True);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Parried));
            Assert.That(brain.Status.IsStunned, Is.True);
            Assert.That(brain.Status.DamageTakenMultiplier, Is.EqualTo(1f), "vulnerability starts when the weapon meets the shield");
            brain.ApplyParryEffects();
            Assert.That(brain.Status.DamageTakenMultiplier, Is.EqualTo(1.5f).Within(1e-5));

            Run(brain, 0.9f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Parried), "stun lasts EnemyConfig.ParriedDuration as before");
            Run(brain, 0.2f);
            Assert.That(brain.State, Is.Not.EqualTo(EnemyState.Parried));
            Assert.That(brain.Status.DamageTakenMultiplier, Is.EqualTo(1.5f).Within(1e-5), "vulnerability (2 s) outlasts the stun");
            Run(brain, 1f);
            Assert.That(brain.Status.DamageTakenMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void StunFromOutside_CancelsAttack_ThenResumes()
        {
            var brain = Brain();
            Run(brain, 0.2f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Windup));
            brain.Status.Apply("stun", 0.5f);
            Run(brain, 0.02f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Stunned));
            Assert.That(brain.Attack, Is.Null);
            Assert.That(brain.OnDamaged(), Is.False, "hits do not break the stun");
            Assert.That(brain.State, Is.EqualTo(EnemyState.Stunned));
            Run(brain, 0.5f);
            Assert.That(brain.State, Is.Not.EqualTo(EnemyState.Stunned));
        }

        [Test]
        public void Death_ClearsEffects()
        {
            var brain = Brain();
            brain.Status.Apply("vulnerable");
            brain.Health.TakeDamage(1000f);
            Assert.That(brain.Status.Active, Is.Empty);
        }

        // ------------------------------------------------------------------ игрок

        [Test]
        public void PlayerVitals_VulnerableTakesMoreDamage_ReviveClears()
        {
            var vitals = new PlayerVitals(100f, 0f) { Status = new StatusEffects(Config()) };
            vitals.Status.Apply("vulnerable");
            Assert.That(vitals.TryHit(10f), Is.True);
            Assert.That(vitals.Health.Current.Value, Is.EqualTo(85f).Within(1e-4));
            vitals.Revive();
            Assert.That(vitals.Status.Active, Is.Empty);
        }

        private sealed class TestPoison : StatusEffect
        {
            protected override void OnTick(float deltaTime, IStatusTarget target) =>
                target?.TakeStatusDamage(Config.Value * deltaTime, this);
        }
    }
}
