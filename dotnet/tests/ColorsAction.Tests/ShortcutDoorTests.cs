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

        private static LevelDoor Door(string id, int x, int z, float yaw, float y = 1f, float height = 3f) =>
            new LevelDoor { Id = id, X = x, Z = z, Yaw = yaw, Y = y, Height = height };

        [Test]
        public void AdjacentDoorsInLine_FormOneWideDoor()
        {
            // Лицом в +Z: соседи по X — одна дверь шириной 3; ещё одна отдельно через клетку.
            var doors = new[] { Door("b", 6, 2, 0), Door("a", 5, 2, 0), Door("c", 7, 2, 360), Door("far", 9, 2, 0) };
            var groups = ShortcutDoorGroups.Build(doors);
            Assert.That(groups.Count, Is.EqualTo(2));
            var wide = groups.Single(g => g.Width == 3);
            Assert.That(wide.Doors.Select(i => doors[i].Id), Is.EqualTo(new[] { "a", "b", "c" }), "ordered along the slab");
            Assert.That(wide.CenterX, Is.EqualTo(6f));
            Assert.That(wide.CenterZ, Is.EqualTo(2f));
            Assert.That(wide.Yaw, Is.EqualTo(0f));
            Assert.That(groups.Single(g => g.Width == 1).Doors, Is.EqualTo(new[] { 3 }));
        }

        [Test]
        public void SideFacingDoors_GroupAlongZ()
        {
            var groups = ShortcutDoorGroups.Build(new[] { Door("a", 4, 1, 90), Door("b", 4, 2, 90) });
            Assert.That(groups.Single().Width, Is.EqualTo(2));
            Assert.That(groups[0].CenterZ, Is.EqualTo(1.5f));
        }

        [Test]
        public void DoorsNotInLine_StaySeparate()
        {
            // Соседи «в глубину» (один за другим по проходу), разные стороны, высоты и этажи — отдельные двери.
            Assert.That(ShortcutDoorGroups.Build(new[] { Door("a", 5, 2, 0), Door("b", 5, 3, 0) }).Count, Is.EqualTo(2));
            Assert.That(ShortcutDoorGroups.Build(new[] { Door("a", 5, 2, 0), Door("b", 6, 2, 180) }).Count, Is.EqualTo(2));
            Assert.That(ShortcutDoorGroups.Build(new[] { Door("a", 5, 2, 0), Door("b", 6, 2, 0, height: 4) }).Count, Is.EqualTo(2));
            Assert.That(ShortcutDoorGroups.Build(new[] { Door("a", 5, 2, 0), Door("b", 6, 2, 0, y: 4) }).Count, Is.EqualTo(2));
        }

        [Test]
        public void WideDoor_NearestPoint_StaysOnSlab()
        {
            // Дверь шириной 3 с центром (6, 2), лицом в +Z: точка взаимодействия скользит по оси от x=5 до x=7.
            var (x, z) = ShortcutDoorRules.NearestPoint(6, 2, 0, 3, 4f, 3f);
            Assert.That(x, Is.EqualTo(5f).Within(1e-4));
            Assert.That(z, Is.EqualTo(2f).Within(1e-4));
            (x, _) = ShortcutDoorRules.NearestPoint(6, 2, 0, 3, 6.6f, 3f);
            Assert.That(x, Is.EqualTo(6.6f).Within(1e-4));
            (x, z) = ShortcutDoorRules.NearestPoint(6, 2, 0, 1, 7f, 3f);
            Assert.That((x, z), Is.EqualTo((6f, 2f)), "single door: its centre");
            // Лицом в +X: ось по Z.
            (x, z) = ShortcutDoorRules.NearestPoint(4, 1.5f, 90, 2, 5f, 9f);
            Assert.That(x, Is.EqualTo(4f).Within(1e-4));
            Assert.That(z, Is.EqualTo(2f).Within(1e-4));
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
