using System.Collections.Generic;
using System.Linq;
using ColorsAction.Level;
using ColorsAction.Level.Navigation;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Враг идёт к игроку по пути A* с учётом высоты, стен, пропастей и закрытых объектов.</summary>
    public class EnemyNavigatorTests
    {
        private static readonly MovementProfile Walker = new MovementProfile
        {
            Id = "walker", StepHeight = 0.3f, JumpHeight = 0f, MaxDrop = 1f, Height = 1f, Diagonal = true
        };

        private static readonly MovementProfile Jumper = new MovementProfile
        {
            Id = "jumper", StepHeight = 0.3f, JumpHeight = 1.25f, MaxDrop = 4f, Height = 1f, Diagonal = true
        };

        /// <summary>Пол <paramref name="w"/>×<paramref name="d"/> на y = -1 (поверхность — 0), кроме <paramref name="holes"/>.</summary>
        private static VoxelLevel Floor(int w, int d, params (int x, int z)[] holes)
        {
            var level = new VoxelLevel();
            var blocks = new List<KeyValuePair<Int3, Block?>>();
            for (int x = 0; x < w; x++)
            for (int z = 0; z < d; z++)
            {
                if (holes.Contains((x, z)))
                    continue;
                blocks.Add(new KeyValuePair<Int3, Block?>(new Int3(x, -1, z), Block.Cube()));
            }

            level.SetMany(blocks);
            return level;
        }

        private static void Put(VoxelLevel level, params Int3[] cells) =>
            level.SetMany(cells.Select(c => new KeyValuePair<Int3, Block?>(c, Block.Cube())).ToList());

        private static EnemyReach Reach(VoxelLevel level, HashSet<string> open = null)
        {
            open ??= new HashSet<string>();
            return new EnemyReach(level, open.Contains, () => open.Count);
        }

        [Test]
        public void WalksAroundWall()
        {
            // Стена в 2 блока по x = 3, z = 0..4; обход — через z = 5..6.
            var level = Floor(7, 7);
            var wall = new List<Int3>();
            for (int z = 0; z <= 4; z++)
            {
                wall.Add(new Int3(3, 0, z));
                wall.Add(new Int3(3, 1, z));
            }

            Put(level, wall.ToArray());
            var nav = new EnemyNavigator(Reach(level), Walker);
            var steer = nav.Steer(0.02f, 1.5f, 0f, 1.5f, 5.5f, 0f, 1.5f);
            Assert.That(nav.Result, Is.EqualTo(PathResult.Found));
            Assert.That(steer.Mode, Is.EqualTo(NavSteerMode.Path));
            Assert.That(nav.Path.Any(s => s.Node.Block.X == 3 && s.Node.Block.Z <= 4), Is.False, "path goes through the wall");
            Assert.That(nav.Path.Any(s => s.Node.Block.Z >= 5), Is.True, "path takes the detour");
            Assert.That(steer.Remaining, Is.GreaterThan(4f), "detour is longer than the straight line");
        }

        [Test]
        public void StraightRun_IsCollapsed()
        {
            var level = Floor(12, 1);
            Put(level, new Int3(11, 0, 0), new Int3(11, 1, 0));
            var nav = new EnemyNavigator(Reach(level), Walker);
            var steer = nav.Steer(0.02f, 0.5f, 0f, 0.5f, 9.5f, 0f, 0.5f);
            Assert.That(steer.Mode, Is.EqualTo(NavSteerMode.Path));
            Assert.That(steer.X, Is.GreaterThan(3f), "steers to the far end of the straight run, not the next cell");
        }

        [Test]
        public void TargetAdjacent_GoesDirect()
        {
            var nav = new EnemyNavigator(Reach(Floor(5, 5)), Walker);
            Assert.That(nav.Steer(0.02f, 1.5f, 0f, 1.5f, 1.7f, 0f, 1.5f).Mode, Is.EqualTo(NavSteerMode.Direct), "same cell");
            Assert.That(nav.Steer(0.02f, 1.5f, 0f, 1.5f, 2.5f, 0f, 1.5f).Mode, Is.EqualTo(NavSteerMode.Direct), "next cell");
        }

        [Test]
        public void UnreachableAcrossPit_StopsAtEdge()
        {
            // Пропасть x = 4..5 поперёк всего пола.
            var level = Floor(10, 2, (4, 0), (4, 1), (5, 0), (5, 1));
            var nav = new EnemyNavigator(Reach(level), Walker);
            var steer = nav.Steer(0.02f, 0.5f, 0f, 0.5f, 8.5f, 0f, 0.5f);
            Assert.That(nav.Result, Is.EqualTo(PathResult.Partial));
            Assert.That(steer.Mode, Is.EqualTo(NavSteerMode.Path));
            Assert.That(nav.Path.Last().Node.Block.X, Is.EqualTo(3), "closest reachable cell is the edge");

            nav.Reset();
            Assert.That(nav.Steer(0.02f, 3.5f, 0f, 0.5f, 8.5f, 0f, 0.5f).Mode, Is.EqualTo(NavSteerMode.Stay), "at the edge — wait");
        }

        [Test]
        public void LedgeGuard_BlocksPitAndHighDrop_AllowsShortDrop()
        {
            // Пол 0..2 на высоте 0; x = 3 — пропасть; второй уровень: x = 0..2 ступень вверх на z = 2.
            var level = Floor(6, 3, (3, 0), (3, 1), (3, 2));
            Put(level, new Int3(0, 0, 2), new Int3(1, 0, 2), new Int3(2, 0, 2));
            var nav = new EnemyNavigator(Reach(level), Walker);
            Assert.That(nav.CanMove(2.5f, 0f, 0.5f, 1f, 0f), Is.False, "pit ahead");
            Assert.That(nav.CanMove(2.5f, 0f, 0.5f, -1f, 0f), Is.True, "back along the floor");
            Assert.That(nav.CanMove(1.5f, 0f, 1.5f, 0f, 1f), Is.False, "1-block step up, walker does not jump");
            Assert.That(nav.CanMove(1.5f, 1f, 2.5f, 0f, -1f), Is.True, "1-block drop is within MaxDrop");
        }

        [Test]
        public void Jumper_JumpsUpLedge_WalkerGoesAround()
        {
            // Уступ в 1 блок на x = 3..5; у ходока есть пандус-обход? Нет — только прыгун попадёт наверх.
            var level = Floor(6, 1);
            Put(level, new Int3(3, 0, 0), new Int3(4, 0, 0), new Int3(5, 0, 0));
            var jumper = new EnemyNavigator(Reach(level), Jumper);
            var steer = jumper.Steer(0.02f, 1.5f, 0f, 0.5f, 4.5f, 1f, 0.5f);
            Assert.That(jumper.Result, Is.EqualTo(PathResult.Found));
            Assert.That(jumper.Path.Any(s => s.Move == NavMove.Jump), Is.True);

            // Стоя у уступа — следующий шаг прыжок на 1 м.
            jumper.Reset();
            steer = jumper.Steer(0.02f, 2.5f, 0f, 0.5f, 4.5f, 1f, 0.5f);
            Assert.That(steer.Mode, Is.EqualTo(NavSteerMode.Path));
            Assert.That(steer.Jump, Is.True);
            Assert.That(steer.JumpRise, Is.EqualTo(1f).Within(0.01f));

            var walker = new EnemyNavigator(Reach(level), Walker);
            walker.Steer(0.02f, 1.5f, 0f, 0.5f, 4.5f, 1f, 0.5f);
            Assert.That(walker.Result, Is.EqualTo(PathResult.Partial), "walker cannot climb");
        }

        [Test]
        public void OpenedCrates_Repath()
        {
            // Коридор, ящики посередине: сначала только до ящиков, после разрушения — до цели.
            var level = Floor(10, 2);
            level.PutObject(new LevelObject { Id = "crates_1", Kind = LevelObjectKind.CrateStack, Cell = new Int3(5, 0, 0), Yaw = CrateStackShape.AlongZ, Height = 2 });
            var open = new HashSet<string>();
            var nav = new EnemyNavigator(Reach(level, open), Walker);
            nav.Steer(0.02f, 1.5f, 0f, 0.5f, 8.5f, 0f, 0.5f);
            Assert.That(nav.Result, Is.EqualTo(PathResult.Partial));
            open.Add("crates_1");
            nav.Steer(0.02f, 1.5f, 0f, 0.5f, 8.5f, 0f, 0.5f);
            Assert.That(nav.Result, Is.EqualTo(PathResult.Found), "world changed — path recalculated");
        }

        [Test]
        public void SameGoal_DoesNotRepathEveryFrame()
        {
            var nav = new EnemyNavigator(Reach(Floor(12, 3)), Walker);
            for (int i = 0; i < 10; i++)
                nav.Steer(0.02f, 0.5f, 0f, 0.5f, 10.5f, 0f, 1.5f);
            Assert.That(nav.Repaths, Is.EqualTo(1));
        }
    }
}
