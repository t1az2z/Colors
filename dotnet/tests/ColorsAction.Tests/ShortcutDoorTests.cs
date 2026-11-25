using System.Collections.Generic;
using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Level;
using ColorsAction.Level.Navigation;
using GameCore.Config;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Дверь-срезка: открывается только с лицевой стороны, остаётся открытой, блокирует путь, пока закрыта.</summary>
    public class ShortcutDoorTests
    {
        [TestCase(0f, 0f, 1f)]
        [TestCase(90f, 1f, 0f)]
        [TestCase(180f, 0f, -1f)]
        [TestCase(270f, -1f, 0f)]
        public void OpensOnlyFromFront(float yaw, float fx, float fz)
        {
            Assert.That(ShortcutDoorRules.CanOpenFrom(5, 5, yaw, 5 + fx * 0.8f, 5 + fz * 0.8f), Is.True, "front");
            Assert.That(ShortcutDoorRules.CanOpenFrom(5, 5, yaw, 5 - fx * 0.8f, 5 - fz * 0.8f), Is.False, "back");
        }

        [Test]
        public void SideOfDoor_DoesNotOpen()
        {
            // Вплотную вдоль стены (в плоскости двери) — не считается лицевой стороной.
            Assert.That(ShortcutDoorRules.CanOpenFrom(5, 5, 0, 6, 5.05f), Is.False);
        }

        [Test]
        public void State_OpensOnce_AndStaysOpen()
        {
            var door = new ShortcutDoorState("d", 5, 5, 0);
            Assert.That(door.TryOpen(5, 4), Is.EqualTo(DoorOpenResult.WrongSide));
            Assert.That(door.IsOpen, Is.False);
            Assert.That(door.TryOpen(5, 6), Is.EqualTo(DoorOpenResult.Opened));
            Assert.That(door.IsOpen, Is.True);
            Assert.That(door.TryOpen(5, 4), Is.EqualTo(DoorOpenResult.AlreadyOpen));
            Assert.That(door.CanOpenFrom(5, 6), Is.False, "nothing left to open");
        }

        [Test]
        public void ClosedDoor_BlocksPath_UntilOpen()
        {
            var level = new VoxelLevel();
            var cells = new List<KeyValuePair<Int3, Block?>>();
            for (int x = 0; x < 5; x++)
            for (int z = 0; z < 5; z++)
                cells.Add(new KeyValuePair<Int3, Block?>(new Int3(x, 0, z), Block.Cube()));
            // Стена по x = 2 с проёмом в z = 2, в проёме дверь.
            for (int z = 0; z < 5; z++)
            {
                if (z == 2)
                    continue;
                for (int y = 1; y <= 3; y++)
                    cells.Add(new KeyValuePair<Int3, Block?>(new Int3(2, y, z), Block.Cube()));
            }

            level.SetMany(cells);
            level.PutObject(new LevelObject { Id = "door", Kind = LevelObjectKind.ShortcutDoor, Cell = new Int3(2, 1, 2), Yaw = 90, Height = 3 });
            Assert.That(level.BarrierAt(new Int3(2, 1, 2))?.Id, Is.EqualTo("door"));

            var graph = NavGraph.Build(level);
            var finder = new PathFinder(graph);
            var start = graph.NodeOnBlock(new Int3(0, 0, 2));
            var goal = graph.NodeOnBlock(new Int3(4, 0, 2));
            var walker = new MovementProfile { Id = "walker", StepHeight = 0.3f, JumpHeight = 0f, MaxDrop = 1f, Height = 1f };
            var ctx = new NavContext();
            var path = new List<PathStep>();
            Assert.That(finder.TryFind(start, goal, walker, ctx, path), Is.False);
            ctx.Open.Add("door");
            Assert.That(finder.TryFind(start, goal, walker, ctx, path), Is.True);
        }

        [Test]
        public void TestLevel_HasDoor_OpeningIntoCourtyard()
        {
            var configs = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new GameCore.Newtonsoft.NewtonsoftConfigSerializer());
            var level = configs.Load<LevelConfig>("Levels/Test");
            var door = level.Doors.Single();
            Assert.That(door.Id, Is.EqualTo("courtyard_shortcut"));
            Assert.That(level.HeightSteps(door.X, door.Z), Is.EqualTo(0), "doorway is at ground level");
            // Стены по бокам проёма не перепрыгнуть.
            Assert.That(level.HeightSteps(door.X - 1, door.Z), Is.GreaterThanOrEqualTo(8));
            Assert.That(level.HeightSteps(door.X + 1, door.Z), Is.GreaterThanOrEqualTo(8));

            var voxel = VoxelLevelData.FromHeightmap(level);
            var obj = voxel.Objects.Single(o => o.IsDoor);
            Assert.That(obj.Id, Is.EqualTo("courtyard_shortcut"));
            Assert.That(obj.Height, Is.EqualTo(3));
        }
    }
}
