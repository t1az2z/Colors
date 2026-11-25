using ColorsAction.Configs;
using ColorsAction.Level;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Форма ¾ блока (код Q): формат файла и импорт старых уровней с шагом 0.25.</summary>
    public class ThreeQuarterBlockTests
    {
        [Test]
        public void ThreeQuarter_FormatsAsQ_AndParsesBack()
        {
            var text = VoxelLevelData.FormatBlock(new Int3(1, 2, 3), new Block(BlockShape.ThreeQuarter, 0, "red"));
            Assert.That(text, Is.EqualTo("1,2,3,Q,0,red"));

            var parsed = VoxelLevelData.ParseBlock(text);
            Assert.That(parsed.Key, Is.EqualTo(new Int3(1, 2, 3)));
            Assert.That(parsed.Value, Is.EqualTo(new Block(BlockShape.ThreeQuarter, 0, "red")));

            // старые коды не сдвинулись
            Assert.That(VoxelLevelData.ParseBlock("0,0,0,D").Value.Shape, Is.EqualTo(BlockShape.Decor));
            Assert.That(BlockShapes.CenterTop(BlockShape.ThreeQuarter), Is.EqualTo(0.75f));
        }

        [Test]
        public void HeightmapImport_ThreeSteps_BecomesThreeQuarter()
        {
            var config = new LevelConfig { Step = 0.25f };
            config.Rows.Add(new string(LevelConfig.Encode(3), 1));
            config.OnConfigLoaded();

            var data = VoxelLevelData.From(VoxelLevelData.FromHeightmap(config));

            Assert.That(data.Blocks, Does.Contain("0,0,0,Q,0"));
        }
    }
}
