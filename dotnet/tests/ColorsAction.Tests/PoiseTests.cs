using System.Collections.Generic;
using System.Linq;
using ColorsAction.Combat;
using ColorsAction.Configs;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Шкала выносливости врагов (poise), поглощение парированием, серии ударов и удар по области.</summary>
    public sealed class PoiseTests
    {
        private static readonly PoiseConfig Rules = new PoiseConfig { Hit = 25f, Parry = 100f, DecayDelay = 3f, DecayRate = 10f };

        private static EnemyAttackConfig Swing(float windup = 1f) =>
            new EnemyAttackConfig { Motion = EnemyAttackMotion.Sweep, Windup = windup, Strike = 0.1f, Hold = 0.3f, Recovery = 0.5f, Range = 2f };

        private static EnemyConfig Enemy(float poise = 100f, bool hyperArmor = false, EnemyAttackConfig attack = null)
        {
            var config = new EnemyConfig
            {
                Id = "test", MaxHealth = 1000, LoseRadius = 13, AlertDuration = 0.1f, MoveSpeed = 4f, WaitDistance = 3f,
                AttackCooldown = 0.5f, StaggerDuration = 0.3f, ParriedDuration = 1f, DeflectDuration = 0.4f,
                Poise = poise, HyperArmor = hyperArmor,
                Attacks = new List<EnemyAttackConfig> { attack ?? Swing() }
            };
            config.OnConfigLoaded();
            return config;
        }

        private static EnemyBrain Brain(EnemyConfig config) =>
            new EnemyBrain(1, config, new CombatDirector(new CombatConfig { MaxAttackers = 1, AttackerGap = 0.4f }), 1,
                StatusEffectsConfig.CreateDefault(), poiseConfig: Rules);

        private static void Run(EnemyBrain brain, float seconds, float distance = 1f)
        {
            for (float t = 0; t < seconds - 0.0001f; t += 0.02f)
                brain.Tick(0.02f, distance);
        }

        /// <summary>Враг в замахе первой атаки.</summary>
        private static EnemyBrain InWindup(EnemyConfig config)
        {
            var brain = Brain(config);
            Run(brain, 0.2f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Windup));
            return brain;
        }

        // ------------------------------------------------------------------ шкала

        [Test]
        public void Poise_FillsAndBreaks_ThenDecaysAfterPause()
        {
            var poise = new Poise(100f, Rules);
            Assert.That(poise.Add(25f), Is.False);
            Assert.That(poise.Fraction, Is.EqualTo(0.25f).Within(1e-5));
            poise.Tick(2.9f);
            Assert.That(poise.Current, Is.EqualTo(25f).Within(1e-5), "no decay before the pause");
            poise.Tick(1f);
            Assert.That(poise.Current, Is.EqualTo(15f).Within(1e-4), "10 per second after 3 s without hits");
            Assert.That(poise.Add(100f), Is.True);
            Assert.That(poise.Broken, Is.True);
            poise.Tick(10f);
            Assert.That(poise.Fraction, Is.EqualTo(1f), "a full bar does not decay");
            Assert.That(poise.Add(25f), Is.False, "already broken");
            poise.Reset();
            Assert.That(poise.Current, Is.Zero);
            Assert.That(poise.Broken, Is.False);
        }

        [Test]
        public void Poise_ZeroVolume_NeverBreaks()
        {
            var poise = new Poise(0f, Rules);
            Assert.That(poise.Add(1000f), Is.False);
            Assert.That(poise.Broken, Is.False);
        }

        [Test]
        public void HitAmount_UsesWeaponAndMoveMultipliers()
        {
            var shotgun = new ItemConfig { Id = "shotgun", Poise = 2f };
            Assert.That(Poise.HitAmount(Rules, null), Is.EqualTo(25f));
            Assert.That(Poise.HitAmount(Rules, shotgun), Is.EqualTo(50f));
            Assert.That(Poise.HitAmount(Rules, shotgun, 1.5f), Is.EqualTo(75f));
            Assert.That(Poise.HitAmount(Rules, shotgun, 1f, 0.5f), Is.EqualTo(25f), "half of the pellets — half of the volley");
        }

        // ------------------------------------------------------------------ обычный враг: 4 удара или 1 парирование

        [Test]
        public void RegularEnemy_FourHitsStun()
        {
            var brain = Brain(Enemy());
            Run(brain, 1.2f, 10f); // отошли: враг не атакует, просто получает удары
            for (int i = 0; i < 3; i++)
            {
                brain.OnDamaged(25f);
                Assert.That(brain.State, Is.Not.EqualTo(EnemyState.Stunned), $"hit {i + 1}");
                Run(brain, 0.4f, 10f);
            }

            Assert.That(brain.OnDamaged(25f), Is.True);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Stunned));
            Assert.That(brain.Staggering, Is.True);
            Assert.That(brain.Status.IsStunned, Is.True);
            brain.OnDamaged(25f);
            Assert.That(brain.Poise.Broken, Is.True, "bar stays full while stunned");

            Run(brain, 1.1f, 10f);
            Assert.That(brain.State, Is.Not.EqualTo(EnemyState.Stunned), "stun lasts ParriedDuration");
            Assert.That(brain.Poise.Current, Is.Zero, "bar empties after the stun");
        }

        [Test]
        public void RegularEnemy_OneParryStuns_AndMakesVulnerable()
        {
            var brain = InWindup(Enemy());
            Assert.That(brain.Parry(), Is.True);
            Assert.That(brain.ParryStunned, Is.True);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Parried));
            Assert.That(brain.Status.IsStunned, Is.True);
            brain.ApplyParryEffects();
            Assert.That(brain.Status.DamageTakenMultiplier, Is.GreaterThan(1f));
        }

        // ------------------------------------------------------------------ элитный: 3 парирования

        [Test]
        public void EliteEnemy_ParryAbsorbsWithoutStun_UntilThirdParry()
        {
            var brain = InWindup(Enemy(300f));
            for (int i = 0; i < 2; i++)
            {
                Assert.That(brain.Parry(), Is.True, $"parry {i + 1}");
                Assert.That(brain.ParryStunned, Is.False);
                Assert.That(brain.Status.IsStunned, Is.False, "not full — no stun");
                Assert.That(brain.Attack, Is.Null, "the blow is absorbed");
                brain.ApplyParryEffects();
                Assert.That(brain.Status.DamageTakenMultiplier, Is.EqualTo(1f), "no parry effects without the stun");
                Run(brain, 0.45f);
                Assert.That(brain.State, Is.Not.EqualTo(EnemyState.Parried), "recovers after DeflectDuration");
                for (int t = 0; t < 150 && brain.State != EnemyState.Windup; t++)
                    brain.Tick(0.02f, 1f);
                Assert.That(brain.State, Is.EqualTo(EnemyState.Windup), "attacks again");
            }

            Assert.That(brain.Parry(), Is.True);
            Assert.That(brain.ParryStunned, Is.True, "third parry fills 300");
            Assert.That(brain.Status.IsStunned, Is.True);
        }

        [Test]
        public void HyperArmor_HitsDoNotInterruptWindup()
        {
            var plain = InWindup(Enemy());
            Assert.That(plain.OnDamaged(10f), Is.True, "a regular enemy is knocked out of the windup");
            Assert.That(plain.State, Is.EqualTo(EnemyState.Staggered));

            var armored = InWindup(Enemy(300f, true));
            Assert.That(armored.OnDamaged(25f), Is.False);
            Assert.That(armored.State, Is.EqualTo(EnemyState.Windup), "hyper armor keeps the swing going");
            Assert.That(armored.Poise.Current, Is.EqualTo(25f));
        }

        // ------------------------------------------------------------------ серия ударов

        private static EnemyAttackConfig Cross()
        {
            var first = new EnemyAttackConfig
            {
                Motion = EnemyAttackMotion.DiagonalRight, Windup = 0.4f, Strike = 0.1f, Hold = 0.1f, Recovery = 0.4f, Range = 2f,
                Combo = new List<EnemyAttackConfig>
                {
                    new EnemyAttackConfig { Motion = EnemyAttackMotion.DiagonalLeft, Windup = 0.3f, Strike = 0.1f, Hold = 0.2f, Recovery = 0.5f, Range = 2f }
                }
            };
            return first;
        }

        [Test]
        public void Combo_SecondStrikeFollowsWithoutRecovery()
        {
            var attack = Cross();
            Assert.That(attack.Steps, Is.EqualTo(2));
            Assert.That(attack.StrikeSpan, Is.EqualTo(0.1f + 0.1f + 0.3f + 0.1f).Within(1e-5));

            var brain = InWindup(Enemy(300f, true, attack));
            var states = new List<EnemyState>();
            brain.StateChanged += s => states.Add(s);
            Run(brain, 1.6f);
            Assert.That(states.Take(6), Is.EqualTo(new[]
            {
                EnemyState.Strike, EnemyState.Hold, EnemyState.Windup, EnemyState.Strike, EnemyState.Hold, EnemyState.Recovery
            }), "recovery only after the second strike");
        }

        [Test]
        public void Combo_ParriedFirstStrike_SecondStillComes()
        {
            var brain = InWindup(Enemy(300f, true, Cross()));
            Assert.That(brain.Parry(), Is.True);
            Assert.That(brain.ParryStunned, Is.False);
            Run(brain, 0.42f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Windup), "the cross continues with the second strike");
            Assert.That(brain.ComboIndex, Is.EqualTo(1));
            Assert.That(brain.Attack.Motion, Is.EqualTo(EnemyAttackMotion.DiagonalLeft));
        }

        [Test]
        public void Combo_ParryThatFillsTheBar_EndsTheSeries()
        {
            var brain = InWindup(Enemy(100f, false, Cross()));
            Assert.That(brain.Parry(), Is.True);
            Assert.That(brain.ParryStunned, Is.True);
            Run(brain, 0.5f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Parried), "stunned, no second strike");
        }

        // ------------------------------------------------------------------ конфиги

        [Test]
        public void Config_ComboOnlyOnFirstStrike_AndAreaFlag()
        {
            var attack = Cross();
            attack.Combo[0].Combo = new List<EnemyAttackConfig> { Swing() };
            var config = Enemy(300f, true, attack);
            Assert.That(config.Attacks[0].Combo[0].Combo, Is.Empty);
            Assert.That(new EnemyAttackConfig { AreaRadius = 1.8f }.IsArea, Is.True);
            Assert.That(new EnemyAttackConfig().IsArea, Is.False);
        }
    }
}
