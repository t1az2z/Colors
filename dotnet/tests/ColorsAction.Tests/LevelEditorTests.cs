using System.Collections.Generic;
using System.IO;
using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Level;
using ColorsAction.Level.Navigation;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Логика редактора уровней: кисть, отмена, формат файла, карта пути и A*.</summary>
    public class LevelEditorTests
    {
        private static MovementProfile Player() => new MovementProfile
        {
            Id = "player", StepHeight = 0.3f, JumpHeight = 1.25f, MaxDrop = 4f, Height = 1f,
            Specials = new List<InteractionAction> { InteractionAction.ShieldSlide, InteractionAction.Lift }
        };

        private static MovementProfile Walker() => new MovementProfile
            { Id = "walker", StepHeight = 0.3f, JumpHeight = 0f, MaxDrop = 1f, Height = 1f };

        /// <summary>Пол из кубов на y = 0, x,z ∈ [0, size).</summary>
        private static VoxelLevel Flat(int size)
        {
            var level = new VoxelLevel();
            var cells = new List<KeyValuePair<Int3, Block?>>();
            for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                cells.Add(new KeyValuePair<Int3, Block?>(new Int3(x, 0, z), Block.Cube()));
            level.SetMany(cells);
            return level;
        }

        [Test]
        public void BrushStroke_IsOneUndoStep_AndRedoRestores()
        {
            var level = Flat(4);
            var history = new LevelHistory(level);
            var brush = new LevelBrush(level, history) { Size = 2 };
            brush.Paint(1, 1, 1, Block.Cube("red"), 0);
            brush.Paint(3, 1, 3, Block.Cube("red"), 0);
            Assert.That(brush.End(), Is.True);
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(level.BlockCount, Is.EqualTo(16 + 8));

            history.Undo();
            Assert.That(level.BlockCount, Is.EqualTo(16));
            history.Redo();
            Assert.That(level.TryGet(new Int3(1, 1, 1), out var b) && b.Color == "red", Is.True);
        }

        [Test]
        public void Erase_RemovesOnlyHeightH()
        {
            var level = new VoxelLevel();
            var history = new LevelHistory(level);
            var brush = new LevelBrush(level, history) { Mode = BrushMode.Column };
            brush.Paint(0, 3, 0, Block.Cube(), 0);
            brush.End();
            Assert.That(level.BlockCount, Is.EqualTo(4));

            brush.Mode = BrushMode.Single;
            brush.Erase(0, 1, 0);
            brush.End();
            Assert.That(level.Has(new Int3(0, 1, 0)), Is.False);
            Assert.That(level.Has(new Int3(0, 0, 0)) && level.Has(new Int3(0, 2, 0)) && level.Has(new Int3(0, 3, 0)), Is.True);
        }

        [Test]
        public void Recolor_KeepsShape()
        {
            var level = new VoxelLevel();
            var history = new LevelHistory(level);
            level.Set(new Int3(0, 0, 0), new Block(BlockShape.Slope, 2, "blue"));
            var brush = new LevelBrush(level, history);
            brush.Recolor(0, 0, 0, "yellow");
            brush.End();
            level.TryGet(Int3.Zero, out var b);
            Assert.That(b.Shape, Is.EqualTo(BlockShape.Slope));
            Assert.That(b.Rotation, Is.EqualTo(2));
            Assert.That(b.Color, Is.EqualTo("yellow"));
        }

        [Test]
        public void MarkupCommands_UndoRestoresObjects()
        {
            var level = new VoxelLevel();
            var history = new LevelHistory(level);
            var barrier = new LevelObject { Id = "b1", Kind = LevelObjectKind.Barrier, Cell = new Int3(1, 1, 1), Color = "red" };
            history.Execute(MarkupCommand.PutObject(null, barrier));
            var moved = barrier.Clone();
            moved.Cell = new Int3(2, 1, 1);
            history.Execute(MarkupCommand.PutObject(barrier, moved));
            Assert.That(level.FindObject("b1").Cell, Is.EqualTo(new Int3(2, 1, 1)));
            history.Undo();
            Assert.That(level.FindObject("b1").Cell, Is.EqualTo(new Int3(1, 1, 1)));
            history.Undo();
            Assert.That(level.Objects, Is.Empty);
            history.SeekRevision(2);
            Assert.That(level.FindObject("b1").Cell, Is.EqualTo(new Int3(2, 1, 1)));
        }

        [Test]
        public void DataRoundTrip_ThroughJson()
        {
            var level = Flat(2);
            level.Set(new Int3(0, 1, 0), new Block(BlockShape.SteepSlope, 3, "lilac"));
            level.AddFloor("Второй", 5);
            level.PutObject(new LevelObject { Id = "b1", Kind = LevelObjectKind.Barrier, Cell = new Int3(1, 1, 1), Color = "red", Barrier = BarrierKind.Grate });
            var p = InteractionPoint.Create("s1", InteractionAction.ShieldSlide, new Int3(0, 2, 0));
            p.TargetPoint = "s2";
            level.PutPoint(p);

            var serializer = new NewtonsoftConfigSerializer();
            var json = serializer.Serialize(VoxelLevelData.From(level));
            var data = (VoxelLevelData)serializer.Deserialize(json, typeof(VoxelLevelData));
            var loaded = data.ToLevel();

            Assert.That(loaded.BlockCount, Is.EqualTo(level.BlockCount));
            loaded.TryGet(new Int3(0, 1, 0), out var b);
            Assert.That(b, Is.EqualTo(new Block(BlockShape.SteepSlope, 3, "lilac")));
            Assert.That(loaded.Floors.Count, Is.EqualTo(2));
            Assert.That(loaded.FindObject("b1").Barrier, Is.EqualTo(BarrierKind.Grate));
            Assert.That(loaded.FindPoint("s1").Condition.RequiredItem, Is.EqualTo("shield"));
        }

        [Test]
        public void Path_WalksStepsAndSlopes_ButWalkerCannotJump()
        {
            var level = Flat(6);
            // Ступенька 0.25 и полный куб-уступ (подъём на 0.75 от ступеньки) в ряду z = 0.
            level.Set(new Int3(2, 1, 0), new Block(BlockShape.Step));
            for (int z = 0; z < 6; z++)
                level.Set(new Int3(4, 1, z), Block.Cube());
            for (int z = 0; z < 6; z++)
                level.Set(new Int3(5, 1, z), Block.Cube());

            var graph = NavGraph.Build(level);
            var finder = new PathFinder(graph);
            var start = graph.NodeOnBlock(new Int3(0, 0, 0));
            var goal = graph.NodeOnBlock(new Int3(5, 1, 0));
            var path = new List<PathStep>();
            var ctx = new NavContext();

            Assert.That(finder.TryFind(start, goal, Player(), ctx, path), Is.True);
            Assert.That(path.Any(s => s.Move == NavMove.Jump), Is.True, "player jumps onto the 1-cube ledge");
            Assert.That(finder.TryFind(start, goal, Walker(), ctx, path), Is.False, "walker cannot jump");

            // Пологий слоп + верх слопа дают подъём на куб пешком.
            level.Set(new Int3(2, 1, 3), new Block(BlockShape.Slope, 1));
            level.Set(new Int3(3, 1, 3), new Block(BlockShape.SlopeTop, 1));
            graph = NavGraph.Build(level);
            finder = new PathFinder(graph);
            Assert.That(finder.TryFind(graph.NodeOnBlock(new Int3(0, 0, 0)), graph.NodeOnBlock(new Int3(5, 1, 0)), Walker(), ctx, path),
                Is.True, "walker climbs the slope");
            Assert.That(path.All(s => s.Move == NavMove.None || s.Move == NavMove.Walk), Is.True);
        }

        [Test]
        public void ClosedBarrier_BlocksPath_UntilOpen()
        {
            var level = Flat(5);
            // Стена по x = 2 с проходом в z = 2, проход закрыт барьером.
            for (int z = 0; z < 5; z++)
            {
                if (z == 2)
                    continue;
                level.Set(new Int3(2, 1, z), Block.Cube());
                level.Set(new Int3(2, 2, z), Block.Cube());
            }

            level.PutObject(new LevelObject { Id = "gate", Kind = LevelObjectKind.Barrier, Cell = new Int3(2, 1, 2), Color = "red", Height = 2 });
            var graph = NavGraph.Build(level);
            var finder = new PathFinder(graph);
            var start = graph.NodeOnBlock(new Int3(0, 0, 2));
            var goal = graph.NodeOnBlock(new Int3(4, 0, 2));
            var ctx = new NavContext();
            var path = new List<PathStep>();
            var walker = Walker();

            Assert.That(finder.TryFind(start, goal, walker, ctx, path), Is.False);
            ctx.Open.Add("gate");
            Assert.That(finder.TryFind(start, goal, walker, ctx, path), Is.True);
        }

        [Test]
        public void ShieldSlide_NeedsShield_AndProfileSupport()
        {
            // Верхняя площадка (y = 3) и нижняя (y = 0), между ними крутой слоп и обрыв выше спрыгивания игрока.
            var level = new VoxelLevel();
            for (int x = 0; x < 3; x++)
            {
                level.Set(new Int3(x, 0, 0), Block.Cube());
                for (int y = 0; y < 6; y++)
                    level.Set(new Int3(x, y, 4), Block.Cube());
            }

            var top = InteractionPoint.Create("slide_top", InteractionAction.ShieldSlide, new Int3(1, 6, 4));
            top.TargetPoint = "slide_bottom";
            level.PutPoint(top);
            level.PutPoint(InteractionPoint.Create("slide_bottom", InteractionAction.Lever, new Int3(1, 1, 0)));

            var graph = NavGraph.Build(level);
            Assert.That(graph.Warnings, Is.Empty);
            var finder = new PathFinder(graph);
            var start = graph.NodeOnBlock(new Int3(1, 5, 4));
            var goal = graph.NodeOnBlock(new Int3(1, 0, 0));
            var ctx = new NavContext();
            var path = new List<PathStep>();

            Assert.That(finder.TryFind(start, goal, Player(), ctx, path), Is.False, "no shield");
            ctx.Items.Add("shield");
            Assert.That(finder.TryFind(start, goal, Player(), ctx, path), Is.True);
            Assert.That(path.Last().Move, Is.EqualTo(NavMove.Special));
            Assert.That(finder.TryFind(start, goal, Walker(), ctx, path), Is.False, "walker has no specials");
        }

        [Test]
        public void HeadroomAndDiagonals()
        {
            var level = Flat(3);
            // Низкий потолок над (1,0,1): 0.5 свободного места.
            level.Set(new Int3(1, 1, 1), new Block(BlockShape.Half));
            level.Set(new Int3(1, 2, 1), Block.Cube());
            var graph = NavGraph.Build(level);
            var node = graph.NodeOnBlock(new Int3(1, 1, 1));
            Assert.That(node.Clearance, Is.EqualTo(0.5f).Within(1e-4));
            Assert.That(NavGraph.CanStand(node, Walker(), new NavContext()), Is.False);

            var flat = NavGraph.Build(Flat(3));
            var corner = flat.NodeOnBlock(new Int3(0, 0, 0));
            Assert.That(corner.Links.Any(l => l.Diagonal), Is.True);
        }

        [Test]
        public void HeightmapImport_AndMovementConfigLoad()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "StreamingAssets", "Configs")))
                dir = dir.Parent;
            var configs = new ConfigService(new FileConfigSource(Path.Combine(dir!.FullName, "Assets", "StreamingAssets", "Configs")),
                new NewtonsoftConfigSerializer());

            var profiles = configs.LoadTable<MovementProfile>("Movement");
            Assert.That(profiles.Get("player").CanUse(InteractionAction.ShieldSlide), Is.True);
            Assert.That(profiles.Get("walker").CanJump, Is.False);

            var heightmap = configs.Load<LevelConfig>("Levels/Test");
            var level = VoxelLevelData.FromHeightmap(heightmap);
            Assert.That(level.BlockCount, Is.GreaterThan(heightmap.Width * heightmap.Length));
            var graph = NavGraph.Build(level);
            var spawn = level.Objects.First(o => o.Kind == LevelObjectKind.PlayerSpawn);
            var start = graph.NodeAtStand(spawn.Cell);
            Assert.That(start, Is.Not.Null);
            var reachable = new PathFinder(graph).Reachable(start, profiles.Get("player"), new NavContext());
            Assert.That(reachable.Count, Is.GreaterThan(graph.Nodes.Count / 2));
        }
    
        [Test]
        public void SmallSteps_BecomeSlopes_BigOnesStayWalls()
        {
            var level = Flat(4);
            // Ступенька 0.25 в (2,1,1): соседи на высоте 1.0 наклоняются к ней.
            level.Set(new Int3(2, 1, 1), new Block(BlockShape.Step));
            Assert.That(SurfaceSmoothing.TryGetCorners(level, new Int3(1, 0, 1), out var c), Is.True);
            Assert.That(c.X1Z0, Is.EqualTo(1.25f).Within(1e-4));
            Assert.That(c.X1Z1, Is.EqualTo(1.25f).Within(1e-4));
            Assert.That(c.X0Z0, Is.EqualTo(1f).Within(1e-4));
            Assert.That(SurfaceSmoothing.HeightAt(c, 0.5f, 0.5f), Is.EqualTo(1.125f).Within(1e-4));

            // Сама ступенька — самый верх, ей тянуться некуда.
            Assert.That(SurfaceSmoothing.TryGetCorners(level, new Int3(2, 1, 1), out _), Is.False);

            // Полублок (перепад 0.5) — стенка, не слоп.
            level.Set(new Int3(2, 1, 1), new Block(BlockShape.Half));
            Assert.That(SurfaceSmoothing.TryGetCorners(level, new Int3(1, 0, 1), out _), Is.False);

            // Над клеткой блок — это не поверхность, не сглаживается.
            level.Set(new Int3(2, 1, 1), new Block(BlockShape.Step));
            level.Set(new Int3(1, 1, 1), Block.Cube());
            level.Set(new Int3(1, 2, 1), Block.Cube());
            Assert.That(SurfaceSmoothing.TryGetCorners(level, new Int3(1, 0, 1), out _), Is.False);
        }

        [Test]
        public void SmoothedRamp_IsWalkable_WithoutRise()
        {
            var level = Flat(6);
            for (int z = 0; z < 6; z++)
            for (int x = 3; x < 6; x++)
                level.Set(new Int3(x, 1, z), new Block(BlockShape.Step));
            var graph = NavGraph.Build(level);
            var low = graph.NodeOnBlock(new Int3(2, 0, 2));
            Assert.That(low.Smoothed, Is.True);
            var link = low.Links.First(l => graph.Nodes[l.To].Block == new Int3(3, 1, 2));
            Assert.That(link.Rise, Is.EqualTo(0f).Within(1e-4), "ramp meets the step at the shared edge");
            Assert.That(graph.Classify(link, Walker(), new NavContext()), Is.EqualTo(NavMove.Walk));
        }
}
}
