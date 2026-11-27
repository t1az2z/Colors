using System.Collections.Generic;
using System.Linq;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Level;
using ColorsAction.Level.Navigation;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Радиус агро — у каждого врага на карте; враг не замечает игрока, до которого не может дойти.</summary>
    public class EnemyAggroTests
    {
        private static EnemyConfig Club() => new EnemyConfig
        {
            Id = "club_cube", MaxHealth = 40, LoseRadius = 13, AlertDuration = 0.1f,
            Attacks = new List<EnemyAttackConfig> { new EnemyAttackConfig { Windup = 1f, Strike = 0.1f, Range = 1.5f } }
        };

        private static CombatDirector Director() => new CombatDirector(new CombatConfig { MaxAttackers = 1, AttackerGap = 0.4f });

        private static readonly MovementProfile Walker = new MovementProfile
        {
            Id = "walker", StepHeight = 0.3f, JumpHeight = 0f, MaxDrop = 1f, Height = 1f, Diagonal = true
        };

        [Test]
        public void AggroRadius_IsPerEnemy()
        {
            var near = new EnemyBrain(1, Club(), Director(), 1, aggroRadius: 3f);
            var far = new EnemyBrain(2, Club(), Director(), 2, aggroRadius: 10f);
            near.Tick(0.02f, 5f);
            far.Tick(0.02f, 5f);
            Assert.That(near.State, Is.EqualTo(EnemyState.Idle));
            Assert.That(far.State, Is.EqualTo(EnemyState.Alerted));
        }

        [Test]
        public void AggroRadius_DefaultsAndLoseRadiusNotSmaller()
        {
            Assert.That(new EnemyBrain(1, Club(), Director(), 1).AggroRadius, Is.EqualTo(LevelEnemy.DefaultAggroRadius));
            Assert.That(new EnemyBrain(1, Club(), Director(), 1, aggroRadius: 20f).LoseRadius, Is.EqualTo(20f));
            Assert.That(new EnemyBrain(1, Club(), Director(), 1, aggroRadius: 5f).LoseRadius, Is.EqualTo(13f));
        }

        [Test]
        public void UnreachablePlayer_DoesNotAggro_ButChaseContinues()
        {
            var brain = new EnemyBrain(1, Club(), Director(), 1);
            brain.Tick(0.02f, 3f, true, false);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Idle));
            brain.Tick(0.02f, 3f, true, true);
            Assert.That(brain.State, Is.EqualTo(EnemyState.Alerted));
            for (int i = 0; i < 10; i++)
                brain.Tick(0.02f, 5f, true, false);
            Assert.That(brain.IsAggro, Is.True, "already aggroed enemy is not reset by reachability");
        }

        [Test]
        public void VoxelLevel_SavesAggroRadiusOnEnemiesOnly()
        {
            var level = new VoxelLevel();
            level.PutObject(new LevelObject { Id = "e1", Kind = LevelObjectKind.EnemySpawn, Cell = new Int3(1, 0, 1), Item = "club_cube", AggroRadius = 4.5f });
            level.PutObject(new LevelObject { Id = "d1", Kind = LevelObjectKind.EnemySpawn, Cell = new Int3(2, 0, 1), Item = VoxelPlayLayout.DummyType });
            var data = VoxelLevelData.From(level);
            Assert.That(data.Objects.Single(o => o.Id == "e1").AggroRadius, Is.EqualTo(4.5f));
            Assert.That(data.Objects.Single(o => o.Id == "d1").AggroRadius, Is.Null);

            var loaded = data.ToLevel();
            Assert.That(loaded.FindObject("e1").AggroRadius, Is.EqualTo(4.5f));
            var config = VoxelPlayLayout.ToLevelConfig(loaded);
            Assert.That(config.Enemies.Single().AggroRadius, Is.EqualTo(4.5f));
        }

        [Test]
        public void OldLevelWithoutAggroRadius_GetsDefault()
        {
            var data = new VoxelLevelData
            {
                Objects = { new VoxelLevelData.ObjectData { Id = "e1", Kind = LevelObjectKind.EnemySpawn, Cell = "0,0,0", Item = "club_cube" } }
            };
            Assert.That(data.ToLevel().FindObject("e1").AggroRadius, Is.EqualTo(LevelEnemy.DefaultAggroRadius));
        }

        /// <summary>Коридор вдоль X: 0..length-1, ширина 1, пол на y = -1.</summary>
        private static VoxelLevel Corridor(int length, params int[] gaps)
        {
            var level = new VoxelLevel();
            var blocks = new List<KeyValuePair<Int3, Block?>>();
            for (int x = 0; x < length; x++)
            {
                if (gaps.Contains(x))
                    continue;
                for (int z = 0; z < 2; z++)
                    blocks.Add(new KeyValuePair<Int3, Block?>(new Int3(x, -1, z), Block.Cube()));
            }

            level.SetMany(blocks);
            return level;
        }

        [Test]
        public void CratesBlockUntilBroken()
        {
            var level = Corridor(10);
            level.PutObject(new LevelObject { Id = "crates_1", Kind = LevelObjectKind.CrateStack, Cell = new Int3(5, 0, 0), Yaw = CrateStackShape.AlongZ, Height = 2 });
            var open = new HashSet<string>();
            var reach = new EnemyReach(level, open.Contains, () => open.Count);
            Assert.That(reach.CanReach(Walker, 1.5f, 0.1f, 0.5f, 3.5f, 0.1f, 0.5f), Is.True, "same side");
            Assert.That(reach.CanReach(Walker, 1.5f, 0.1f, 0.5f, 8.5f, 0.1f, 0.5f), Is.False, "behind crates");
            open.Add("crates_1");
            Assert.That(reach.CanReach(Walker, 1.5f, 0.1f, 0.5f, 8.5f, 0.1f, 0.5f), Is.True, "crates broken");
        }

        [Test]
        public void ClosedShortcutDoorBlocks()
        {
            var level = Corridor(10);
            level.PutObject(new LevelObject { Id = "door_1", Kind = LevelObjectKind.ShortcutDoor, Cell = new Int3(5, 0, 0), Height = 3 });
            level.PutObject(new LevelObject { Id = "door_2", Kind = LevelObjectKind.ShortcutDoor, Cell = new Int3(5, 0, 1), Height = 3 });
            var open = new HashSet<string>();
            var reach = new EnemyReach(level, open.Contains, () => open.Count);
            Assert.That(reach.CanReach(Walker, 1.5f, 0.1f, 0.5f, 8.5f, 0.1f, 0.5f), Is.False);
            open.Add("door_1");
            Assert.That(reach.CanReach(Walker, 1.5f, 0.1f, 0.5f, 8.5f, 0.1f, 0.5f), Is.True);
        }

        [Test]
        public void RaisedBridgeBlocks_LoweredBridgeConnectsBanks()
        {
            // Берег 0..2, пропасть 3..10 под мостом 8×3, берег 11..13. Мост от x=3 вдоль +X.
            var level = Corridor(14, 3, 4, 5, 6, 7, 8, 9, 10);
            level.PutObject(new LevelObject { Id = "bridge_1", Kind = LevelObjectKind.DrawBridge, Cell = new Int3(3, 0, 0), Yaw = 0f });
            var open = new HashSet<string>();
            var reach = new EnemyReach(level, open.Contains, () => open.Count);
            Assert.That(reach.CanReach(Walker, 12.5f, 0.1f, 0.5f, 1.5f, 0.1f, 0.5f), Is.False, "raised leaf");
            open.Add("bridge_1");
            Assert.That(reach.CanReach(Walker, 12.5f, 0.1f, 0.5f, 1.5f, 0.1f, 0.5f), Is.True, "lowered bridge");
        }

        [Test]
        public void RaisedBridge_PitUnderFarLeafIsNotFloor()
        {
            // Пропасть под мостом; поднятая дальняя створка стоит в клетке 7 (x=10), под клетками 4–6 (x=7..9) пусто.
            var level = Corridor(14, 3, 4, 5, 6, 7, 8, 9, 10);
            level.PutObject(new LevelObject { Id = "bridge_1", Kind = LevelObjectKind.DrawBridge, Cell = new Int3(3, 0, 0), Yaw = 0f });
            var open = new HashSet<string>();
            var reach = new EnemyReach(level, open.Contains, () => open.Count);
            Assert.That(reach.CanReach(Walker, 1.5f, 0.1f, 0.5f, 5.5f, 0.1f, 0.5f), Is.True, "near leaf is always down");
            Assert.That(reach.CanReach(Walker, 1.5f, 0.1f, 0.5f, 8.5f, 0.1f, 0.5f), Is.False, "pit under the raised leaf");
            open.Add("bridge_1");
            Assert.That(reach.CanReach(Walker, 1.5f, 0.1f, 0.5f, 8.5f, 0.1f, 0.5f), Is.True, "lowered far leaf");
        }
    }
}
