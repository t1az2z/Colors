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
        public void Director_StrikesNeverOverlap_NextStrikesRightAfterCurrent()
        {
            var config = Club();
            config.OnConfigLoaded();
            var director = new CombatDirector(new CombatConfig { MaxAttackers = 3, AttackerGap = 0.25f });
            var a = new EnemyBrain(1, config, director, 1);
            var b = new EnemyBrain(2, config, director, 2);
            var c = new EnemyBrain(3, config, director, 3);
            var brains = new[] { a, b, c };
            var overlap = false;
            var strikeEnds = new List<float>();
            var strikeStarts = new List<float>();
            var maxStriking = 0;
            var sawQueue = false;
            var time = 0f;
            var wasStriking = new bool[3];
            for (; time < 12f; time += 0.02f)
            {
                director.Tick(0.02f);
                var striking = 0;
                for (int i = 0; i < brains.Length; i++)
                {
                    brains[i].Tick(0.02f, 1.2f);
                    var s = brains[i].State == EnemyState.Strike;
                    if (s)
                        striking++;
                    if (s && !wasStriking[i])
                        strikeStarts.Add(time);
                    if (!s && wasStriking[i])
                        strikeEnds.Add(time);
                    wasStriking[i] = s;
                }

                maxStriking = System.Math.Max(maxStriking, striking);
                overlap |= striking > 1;
                sawQueue |= director.Queue.Count > 0;
            }

            Assert.That(overlap, Is.False, "two strikes never land at the same time");
            Assert.That(sawQueue, Is.True, "waiting enemies stand in a queue");
            Assert.That(strikeStarts.Count, Is.GreaterThanOrEqualTo(3), "everyone gets a turn");
            // Следующий бьёт сразу после текущего: зазор между концом одного удара и началом следующего мал.
            var gaps = new List<float>();
            foreach (var start in strikeStarts)
            {
                var before = strikeEnds.FindLast(e => e <= start + 0.001f);
                if (before > 0f)
                    gaps.Add(start - before);
            }

            // Первая волна: трое бьют друг за другом, каждый сразу после предыдущего.
            Assert.That(gaps.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(gaps[0] >= 0.2f && gaps[0] < 0.6f && gaps[1] >= 0.2f && gaps[1] < 0.6f, Is.True,
                "next strike follows the previous one closely: " + string.Join(", ", gaps));
        }

        [Test]
        public void ParryWindow_AroundStrikeStart()
        {
            var config = Club();
            config.Attacks.ForEach(a => { a.ParryFrom = -0.2f; a.ParryTo = 0.05f; });
            config.OnConfigLoaded();
            var brain = new EnemyBrain(1, config, Director(), 1);
            Run(brain, 2.1f, 1.2f);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Windup));
            Assert.That(brain.InParryWindow, Is.False, "early wind-up is not the parry window");
            var opened = false;
            for (float t = 0; t < 1.6f; t += 0.02f)
            {
                brain.Tick(0.02f, 1.2f);
                opened |= brain.InParryWindow;
            }

            Assert.That(opened, Is.True, "parry window opens right before the strike");
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
            Assert.That(sweep.Windup, Is.GreaterThanOrEqualTo(0.3f), "readable wind-up (tuned by the user in the config editor)");
            Assert.That(sweep.Strike, Is.LessThan(sweep.Windup / 3f), "sharp strike");
            Assert.That(overhead.Range, Is.GreaterThan(sweep.Range), "overhead reaches further");
            Assert.That(overhead.Arc, Is.LessThan(sweep.Arc), "but is narrower");
        }
    }
}
