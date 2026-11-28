using System.Collections.Generic;
using System.Linq;
using ColorsAction.Combat;
using ColorsAction.Configs;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Неблокируемый удар: удар сверху по области элитного мечника и знак «!» во время замаха.</summary>
    public sealed class UnblockableAttackTests
    {
        [Test]
        public void ShippedEliteSword_OnlyOverheadAreaSlamIsUnblockable()
        {
            var configs = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            var enemies = configs.LoadTable<EnemyConfig>(ConfigKeys.Enemies);
            Assert.That(enemies.TryGet("elite_sword", out var elite), Is.True);
            var slam = elite.Attacks.Single(a => a.Motion == EnemyAttackMotion.Slam);
            Assert.That(slam.IsArea && slam.Unblockable, Is.True, "overhead area slam goes through the block");
            Assert.That(slam.ParryTo, Is.GreaterThan(slam.ParryFrom), "it can still be parried");
            foreach (var attack in elite.Attacks.Where(a => a != slam))
                for (int i = 0; i < attack.Steps; i++)
                    Assert.That(attack.Step(i).Unblockable, Is.False, attack.Motion + " stays blockable");
            Assert.That(enemies.TryGet("club_cube", out var club) && club.Attacks.All(a => !a.Unblockable), Is.True);
        }

        [Test]
        public void BlockingUnblockableHit_FullDamage_StaminaCostAboveNormalBlock()
        {
            var normal = BlockRules.Resolve(35f, 1f, 1000f, 6f, 1f);
            var pierced = BlockRules.ResolveUnblockable(35f, 1f, 6f, 1f, 2f);
            Assert.That(normal.Damage, Is.EqualTo(0f));
            Assert.That(pierced.Damage, Is.EqualTo(35f), "damage goes through in full");
            Assert.That(pierced.Held, Is.False);
            Assert.That(pierced.StaminaCost, Is.EqualTo(normal.StaminaCost * 2f).Within(1e-4), "twice the normal block cost");
            Assert.That(BlockRules.ResolveUnblockable(35f, 1f, 6f, 0.5f, 2f).StaminaCost,
                Is.EqualTo(normal.StaminaCost).Within(1e-4), "cheaper-block upgrade still applies");
        }

        [Test]
        public void ShippedPlayerConfig_UnblockableBlockCostsMoreThanNormal()
        {
            var configs = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            Assert.That(configs.Load<PlayerConfig>(ConfigKeys.Player).Stamina.UnblockableBlockCostMultiplier, Is.GreaterThan(1f));
        }

        [Test]
        public void Brain_ShowsWarningOnlyDuringUnblockableWindup()
        {
            var attack = new EnemyAttackConfig
            {
                Motion = EnemyAttackMotion.Slam, Windup = 1f, Strike = 0.1f, Hold = 0.3f, Recovery = 0.5f, Range = 2f,
                AreaRadius = 1.5f, Unblockable = true
            };
            var config = new EnemyConfig
            {
                Id = "test", MaxHealth = 1000, LoseRadius = 13, AlertDuration = 0.1f, MoveSpeed = 4f, WaitDistance = 3f,
                AttackCooldown = 0.5f, StaggerDuration = 0.3f, ParriedDuration = 1f, DeflectDuration = 0.4f,
                Attacks = new List<EnemyAttackConfig> { attack }
            };
            config.OnConfigLoaded();
            var brain = new EnemyBrain(1, config, new CombatDirector(new CombatConfig { MaxAttackers = 1, AttackerGap = 0.4f }), 1);
            Assert.That(brain.UnblockableWindup, Is.False);

            var sawWindup = false;
            for (float t = 0; t < 3f; t += 0.02f)
            {
                brain.Tick(0.02f, 1f);
                Assert.That(brain.UnblockableWindup, Is.EqualTo(brain.State == EnemyState.Windup), brain.State.ToString());
                sawWindup |= brain.UnblockableWindup;
            }

            Assert.That(sawWindup, Is.True);
        }

        [Test]
        public void Brain_NoWarningForBlockableAttack()
        {
            var config = new EnemyConfig
            {
                Id = "test", MaxHealth = 1000, LoseRadius = 13, AlertDuration = 0.1f, MoveSpeed = 4f, WaitDistance = 3f,
                AttackCooldown = 0.5f, StaggerDuration = 0.3f, ParriedDuration = 1f, DeflectDuration = 0.4f,
                Attacks = new List<EnemyAttackConfig> { new EnemyAttackConfig { Windup = 1f, Range = 2f } }
            };
            config.OnConfigLoaded();
            var brain = new EnemyBrain(1, config, new CombatDirector(new CombatConfig { MaxAttackers = 1, AttackerGap = 0.4f }), 1);
            for (float t = 0; t < 2f; t += 0.02f)
            {
                brain.Tick(0.02f, 1f);
                Assert.That(brain.UnblockableWindup, Is.False);
            }
        }
    }
}
