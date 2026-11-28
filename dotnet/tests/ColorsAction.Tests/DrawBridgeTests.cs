using System.Collections.Generic;
using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Level;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Разводной мост 8×3, створка 4 в высоту: форма по повороту, стена поднятой створки, конфиг, загрузка из карты.</summary>
    public class DrawBridgeTests
    {
        [TestCase(0f, 1, 0, 0, 1)]
        [TestCase(90f, 0, -1, 1, 0)]
        [TestCase(180f, -1, 0, 0, -1)]
        [TestCase(270f, 0, 1, -1, 0)]
        public void Cells_FollowYaw(float yaw, int ax, int az, int cx, int cz)
        {
            // Якорь — на берегу сразу за дальним краем: начало моста в 8 клетках от него против yaw.
            var anchor = new Int3(10, 3, 10);
            var origin = new Int3(10 - 8 * ax, 3, 10 - 8 * az);
            Assert.That(DrawBridgeShape.Origin(anchor, yaw), Is.EqualTo(origin));
            Assert.That(DrawBridgeShape.AnchorOf(origin, yaw), Is.EqualTo(anchor));
            Assert.That(DrawBridgeShape.Cell(anchor, yaw, 0, 0), Is.EqualTo(origin));
            Assert.That(DrawBridgeShape.Cell(anchor, yaw, 7, 0), Is.EqualTo(new Int3(10 - ax, 3, 10 - az)), "last cell is next to the anchor");
            Assert.That(DrawBridgeShape.Cell(anchor, yaw, 0, 2), Is.EqualTo(new Int3(origin.X + 2 * cx, 3, origin.Z + 2 * cz)));
            Assert.That(DrawBridgeShape.CoversColumn(anchor, yaw, anchor.X, anchor.Z), Is.False, "the anchor is outside the bridge");
            // Ширина — слева по ходу: поворот направления «вдоль» на 90° против часовой (вид сверху, +Z вверх).
            Assert.That((cx, cz), Is.EqualTo((-az, ax)));
        }

        [Test]
        public void Center_IsMiddleOfFootprint()
        {
            Assert.That((DrawBridgeShape.Length, DrawBridgeShape.Width, DrawBridgeShape.Height), Is.EqualTo((8, 3, 4)));
            Assert.That(DrawBridgeShape.Center(8, 0, 0f), Is.EqualTo((4f, 1.5f)));
            Assert.That(DrawBridgeShape.Center(0, 8, 270f), Is.EqualTo((-0.5f, 4f)));
        }

        [Test]
        public void RaisedLeaf_BlocksOnlyItsColumn_FourHigh()
        {
            var o = new LevelObject { Kind = LevelObjectKind.DrawBridge, Cell = new Int3(10, 1, 2), Yaw = 0f, Height = 4 };
            Assert.That(o.BlocksPath, Is.True);
            Assert.That(o.Covers(new Int3(9, 1, 2)), Is.True, "raised leaf at the far end, near row");
            Assert.That(o.Covers(new Int3(9, 4, 4)), Is.True, "raised leaf, far row, top");
            Assert.That(o.Covers(new Int3(9, 5, 2)), Is.False, "above the leaf");
            Assert.That(o.Covers(new Int3(2, 1, 2)), Is.False, "lowered leaf is walkable");
            Assert.That(o.Covers(new Int3(8, 1, 3)), Is.False, "the gap in front of the raised leaf");
            Assert.That(o.Covers(new Int3(10, 1, 2)), Is.False, "behind the bridge");
            Assert.That(o.Covers(new Int3(9, 1, 5)), Is.False, "beside the bridge");
        }

        [Test]
        public void BridgesConfig_Loads_ShotgunOnly_RedMechanism()
        {
            var service = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            var bridges = service.Load<BridgeConfig>(ConfigKeys.Bridges);
            var shotgun = service.LoadTable<ItemConfig>(ConfigKeys.Items).Get("shotgun");
            Assert.That(bridges.BreakableBy, Is.EquivalentTo(new[] { "shotgun" }));
            Assert.That(bridges.Color, Is.EqualTo(shotgun.Color), "gears and chain have the shotgun's color");
            Assert.That(bridges.RaisedAngle, Is.InRange(10f, 89f), "the leaf must lean forward to fall");
        }

        [Test]
        public void VoxelLevel_BridgeObject_GoesToLevelConfig_AndRoundTrips()
        {
            var level = new VoxelLevel();
            var cells = new List<KeyValuePair<Int3, Block?>>();
            for (int x = 0; x < 2; x++)
            for (int z = 0; z < 4; z++)
                cells.Add(new KeyValuePair<Int3, Block?>(new Int3(x, 0, z), Block.Cube()));
            // Берег за дальним краем — полублок: настил на высоте его верха.
            cells.Add(new KeyValuePair<Int3, Block?>(new Int3(8, 1, 1), new Block(BlockShape.Half)));
            level.SetMany(cells);
            level.PutObject(new LevelObject { Id = "drawbridge_1", Kind = LevelObjectKind.DrawBridge, Cell = new Int3(8, 1, 1), Yaw = 0f, Height = 4 });

            var skipped = new List<string>();
            var config = VoxelPlayLayout.ToLevelConfig(level, skipped);
            Assert.That(skipped.Any(s => s.Contains("drawbridge_1")), Is.False);
            Assert.That(config.Bridges, Has.Count.EqualTo(1));
            var b = config.Bridges[0];
            Assert.That((b.Id, b.X, b.Z, b.Y), Is.EqualTo(("drawbridge_1", 0, 1, (float?)1.5f)), "game gets the bridge start, deck at the anchor's support");
            Assert.That(level.BarrierAt(new Int3(7, 1, 3))?.Id, Is.EqualTo("drawbridge_1"), "raised leaf at the far end");

            var round = VoxelLevelData.From(level).ToLevel();
            Assert.That(round.FindObject("drawbridge_1").Kind, Is.EqualTo(LevelObjectKind.DrawBridge));
            Assert.That(round.FindObject("drawbridge_1").Cell, Is.EqualTo(new Int3(8, 1, 1)));
        }

        [Test]
        public void Format1_BridgeCell_IsBridgeStart_MovedToAnchorOnLoad()
        {
            // В файлах версии 1 клетка моста — ближний край опущенной створки; при чтении она переезжает на якорь.
            var old = new VoxelLevelData
            {
                Format = 1,
                Objects = { new VoxelLevelData.ObjectData { Id = "b", Kind = LevelObjectKind.DrawBridge, Cell = "28,1,18", Yaw = 180f, Height = 4 } }
            };
            Assert.That(old.ToLevel().FindObject("b").Cell, Is.EqualTo(new Int3(20, 1, 18)));

            var current = new VoxelLevelData
            {
                Format = VoxelLevelData.CurrentFormat,
                Objects = { new VoxelLevelData.ObjectData { Id = "b", Kind = LevelObjectKind.DrawBridge, Cell = "20,1,18", Yaw = 180f, Height = 4 } }
            };
            var level = current.ToLevel();
            Assert.That(level.FindObject("b").Cell, Is.EqualTo(new Int3(20, 1, 18)));
            var saved = VoxelLevelData.From(level);
            Assert.That((saved.Format, saved.Objects[0].Cell), Is.EqualTo((2, "20,1,18")), "saved in the current format, cell unchanged");
        }
    }
}
