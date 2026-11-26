using System.Collections.Generic;
using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Level;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Разводной мост 4×2×2: форма по повороту, стена поднятой створки, конфиг, загрузка из карты.</summary>
    public class DrawBridgeTests
    {
        [TestCase(0f, 1, 0, 0, 1)]
        [TestCase(90f, 0, -1, 1, 0)]
        [TestCase(180f, -1, 0, 0, -1)]
        [TestCase(270f, 0, 1, -1, 0)]
        public void Cells_FollowYaw(float yaw, int ax, int az, int cx, int cz)
        {
            var anchor = new Int3(10, 3, 10);
            Assert.That(DrawBridgeShape.Cell(anchor, yaw, 0, 0), Is.EqualTo(anchor));
            Assert.That(DrawBridgeShape.Cell(anchor, yaw, 3, 0), Is.EqualTo(new Int3(10 + 3 * ax, 3, 10 + 3 * az)));
            Assert.That(DrawBridgeShape.Cell(anchor, yaw, 0, 1), Is.EqualTo(new Int3(10 + cx, 3, 10 + cz)));
            // Ширина — слева по ходу: поворот направления «вдоль» на 90° против часовой (вид сверху, +Z вверх).
            Assert.That((cx, cz), Is.EqualTo((-az, ax)));
        }

        [Test]
        public void Center_IsMiddleOfFootprint()
        {
            Assert.That(DrawBridgeShape.Center(0, 0, 0f), Is.EqualTo((2f, 1f)));
            Assert.That(DrawBridgeShape.Center(0, 0, 270f), Is.EqualTo((0f, 2f)));
        }

        [Test]
        public void RaisedLeaf_BlocksOnlyItsColumn_TwoHigh()
        {
            var o = new LevelObject { Kind = LevelObjectKind.DrawBridge, Cell = new Int3(2, 1, 2), Yaw = 0f, Height = 2 };
            Assert.That(o.BlocksPath, Is.True);
            Assert.That(o.Covers(new Int3(4, 1, 2)), Is.True, "raised leaf, near row");
            Assert.That(o.Covers(new Int3(4, 2, 3)), Is.True, "raised leaf, far row, top");
            Assert.That(o.Covers(new Int3(4, 3, 2)), Is.False, "above the leaf");
            Assert.That(o.Covers(new Int3(2, 1, 2)), Is.False, "lowered leaf is walkable");
            Assert.That(o.Covers(new Int3(5, 1, 2)), Is.False, "behind the raised leaf");
            Assert.That(o.Covers(new Int3(4, 1, 4)), Is.False, "beside the bridge");
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
            for (int z = 0; z < 3; z++)
                cells.Add(new KeyValuePair<Int3, Block?>(new Int3(x, 0, z), Block.Cube()));
            level.SetMany(cells);
            level.PutObject(new LevelObject { Id = "drawbridge_1", Kind = LevelObjectKind.DrawBridge, Cell = new Int3(0, 1, 1), Yaw = 0f, Height = 2 });

            var skipped = new List<string>();
            var config = VoxelPlayLayout.ToLevelConfig(level, skipped);
            Assert.That(skipped.Any(s => s.Contains("drawbridge_1")), Is.False);
            Assert.That(config.Bridges, Has.Count.EqualTo(1));
            var b = config.Bridges[0];
            Assert.That((b.Id, b.X, b.Z, b.Y), Is.EqualTo(("drawbridge_1", 0, 1, (float?)1f)));
            Assert.That(level.BarrierAt(new Int3(2, 1, 2))?.Id, Is.EqualTo("drawbridge_1"), "raised leaf over the gap");

            var round = VoxelLevelData.From(level).ToLevel();
            Assert.That(round.FindObject("drawbridge_1").Kind, Is.EqualTo(LevelObjectKind.DrawBridge));
        }
    }
}
