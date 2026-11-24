using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    public class EnemyTests
    {
        private static EnemyConfig Club() => new EnemyConfig
        {
            Id = "club_cube", MaxHealth = 40, AggroRadius = 7, LoseRadius = 13, AlertDuration = 2f,
            MoveSpeed = 4f, WaitDistance = 3f, AttackCooldown = 0.5f, StaggerDuration = 0.3f,
            Attacks = new List<EnemyAttackConfig>
            {
                new EnemyAttackConfig { Motion = EnemyAttackMotion.Sweep, Windup = 1.5f, Strike = 0.1f, Hold = 0.3f, Recovery = 0.5f, Range = 1.5f },
                new EnemyAttackConfig { Motion = EnemyAttackMotion.Overhead, Windup = 1.5f, Strike = 0.1f, Hold = 0.3f, Recovery = 0.5f, Range = 2.3f }
            }
        };

        private static CombatDirector Director() => new CombatDirector(new CombatConfig { MaxAttackers = 1, AttackerGap = 0.4f });

        private static void Run(EnemyBrain brain, float seconds, float distance, CombatDirector director = null)
        {
            for (float t = 0; t < seconds; t += 0.02f)
            {
                director?.Tick(0.02f);
                brain.Tick(0.02f, distance);
            }
        }

        [Test]
        public void Idle_UntilPlayerEntersAggroZone_ThenAlertForTwoSeconds()
        {
            var config = Club();
            config.OnConfigLoaded();
            var brain = new EnemyBrain(1, config, Director(), 1);
            Run(brain, 1f, 10f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Idle));

            Run(brain, 0.1f, 6f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Alerted));
            Run(brain, 1.8f, 6f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Alerted), "the '!' reaction lasts ~2 s");
            Run(brain, 0.3f, 6f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Chase), "after the alert a far player is chased");
        }

        [Test]
        public void AfterAlert_CloseEnough_AttacksWithLongWindup()
        {
            var config = Club();
            config.OnConfigLoaded();
            var brain = new EnemyBrain(1, config, Director(), 1);
            Run(brain, 2.2f, 1.2f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Windup));
            Run(brain, 1.3f, 1.2f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Windup), "wind-up is ~1.5 s");
            Run(brain, 0.25f, 1.2f);
            Assert.That(brain.State, Is.Not.EqualTo(EnemyState.Windup));
        }

        [Test]
        public void OnlyFarAttack_IsPickedAtLongerRange()
        {
            var config = Club();
            config.OnConfigLoaded();
            var brain = new EnemyBrain(1, config, Director(), 7);
            Run(brain, 2.2f, 2f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Windup));
            Assert.That(brain.Attack.Motion, Is.EqualTo(EnemyAttackMotion.Overhead));
        }

        [Test]
        public void Director_LetsOnlyOneEnemyAttackAtATime()
        {
            var config = Club();
            config.OnConfigLoaded();
            var director = Director();
            var a = new EnemyBrain(1, config, director, 1);
            var b = new EnemyBrain(2, config, director, 2);
            for (float t = 0; t < 2.3f; t += 0.02f)
            {
                director.Tick(0.02f);
                a.Tick(0.02f, 1.2f);
                b.Tick(0.02f, 1.2f);
            }

            var attacking = (a.IsAttacking ? 1 : 0) + (b.IsAttacking ? 1 : 0);
            Assert.That(attacking, Is.EqualTo(1));
            Assert.That(director.Attackers.Count, Is.EqualTo(1));
            var waiting = a.IsAttacking ? b : a;
            Assert.That(waiting.State, Is.EqualTo(EnemyState.Waiting));

            // Пока первый не закончил, второй не нападает; потом — его очередь.
            var sawBoth = false;
            var secondAttacked = false;
            for (float t = 0; t < 6f; t += 0.02f)
            {
                director.Tick(0.02f);
                a.Tick(0.02f, 1.2f);
                b.Tick(0.02f, 1.2f);
                sawBoth |= a.IsAttacking && b.IsAttacking;
                secondAttacked |= waiting.IsAttacking;
            }

            Assert.That(sawBoth, Is.False, "two enemies never attack at the same time");
            Assert.That(secondAttacked, Is.True, "the waiting enemy gets its turn");
        }

        [Test]
        public void HitDuringWindup_InterruptsAttack_AndReleasesDirector()
        {
            var config = Club();
            config.OnConfigLoaded();
            var director = Director();
            var brain = new EnemyBrain(1, config, director, 1);
            Run(brain, 2.5f, 1.2f, director);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Windup));
            brain.Health.TakeDamage(5);
            Assert.That(brain.OnDamaged(), Is.True);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Staggered));
            Assert.That(director.Attackers, Is.Empty);
        }

        [Test]
        public void HitDuringStrike_DoesNotInterrupt()
        {
            var config = Club();
            config.OnConfigLoaded();
            var brain = new EnemyBrain(1, config, Director(), 1);
            Run(brain, 3.62f, 1.2f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Strike).Or.EqualTo(EnemyState.Hold));
            Assert.That(brain.OnDamaged(), Is.False);
            Assert.That(brain.IsAttacking, Is.True);
        }

        [Test]
        public void Death_ReleasesDirector()
        {
            var config = Club();
            config.OnConfigLoaded();
            var director = Director();
            var brain = new EnemyBrain(1, config, director, 1);
            Run(brain, 2.5f, 1.2f, director);
            brain.Health.TakeDamage(100);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Dead));
            Assert.That(director.Attackers, Is.Empty);
        }

        [Test]
        public void PlayerVitals_TwoSecondsOfInvulnerabilityAfterHit()
        {
            var vitals = new PlayerVitals(100, 2f);
            Assert.That(vitals.TryHit(10), Is.True);
            Assert.That(vitals.Health.Current.Value, Is.EqualTo(90f));
            Assert.That(vitals.TryHit(10), Is.False, "invulnerable right after a hit");
            vitals.Tick(1.9f);
            Assert.That(vitals.TryHit(10), Is.False);
            vitals.Tick(0.2f);
            Assert.That(vitals.TryHit(10), Is.True);
            Assert.That(vitals.Health.Current.Value, Is.EqualTo(80f));
        }

        [Test]
        public void ProjectEnemyConfig_Loads()
        {
            var root = TestPaths.ConfigsRoot();
            var service = new GameCore.Config.ConfigService(new GameCore.Config.FileConfigSource(root), new GameCore.Newtonsoft.NewtonsoftConfigSerializer());
            var club = service.LoadTable<EnemyConfig>(ConfigKeys.Enemies).Get("club_cube");
            Assert.That(club.Attacks.Count, Is.EqualTo(2));
            var sweep = club.Attacks.Find(a => a.Motion == EnemyAttackMotion.Sweep);
            var overhead = club.Attacks.Find(a => a.Motion == EnemyAttackMotion.Overhead);
            Assert.That(sweep.Windup, Is.EqualTo(1.5f).Within(0.3f), "readable wind-up");
            Assert.That(sweep.Strike, Is.LessThan(0.2f), "sharp strike");
            Assert.That(overhead.Range, Is.GreaterThan(sweep.Range), "overhead reaches further");
            Assert.That(overhead.Arc, Is.LessThan(sweep.Arc), "but is narrower");
        }
    }
}
