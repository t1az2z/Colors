using ColorsAction.Configs;
using ColorsAction.Level;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Вода вокруг уровня: Water.json читается, вода не топит на суше и смерть от падения только приближает.</summary>
    public class WaterTests
    {
        private static WaterConfig Load()
        {
            var configs = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            return configs.Load<WaterConfig>(WaterConfig.Key);
        }

        [Test]
        public void WaterJson_Loads_SurfaceBelowLowestFloor()
        {
            var water = Load();
            Assert.That(water.Enabled, Is.True);
            // Верх самых нижних блоков — дно + 1: он должен оставаться сушей.
            Assert.That(water.SurfaceAboveBottom, Is.LessThan(1f));
            Assert.That(water.Extent, Is.GreaterThan(0f));
        }

        [Test]
        public void Layout_CoversLevelWithMargin_AndDrownsBelowSurface()
        {
            var config = new WaterConfig { SurfaceAboveBottom = 0.35f, DrownDepth = 0.9f, Extent = 50f };
            var layout = WaterLayout.Build(config, 0f, 2f, 40f, 30f, -1f);
            Assert.That(layout.Surface, Is.EqualTo(-0.65f).Within(1e-4));
            Assert.That(layout.MinX, Is.EqualTo(-50f));
            Assert.That(layout.MinZ, Is.EqualTo(-48f));
            Assert.That(layout.MaxX, Is.EqualTo(90f));
            Assert.That(layout.MaxZ, Is.EqualTo(80f));
            Assert.That(layout.DrownHeight, Is.EqualTo(-1.55f).Within(1e-4));
        }

        [Test]
        public void KillHeight_WaterOnlyRaisesIt_NeverAboveLowestFloor()
        {
            var config = Load();
            const float bottom = 0f;
            var layout = WaterLayout.Build(config, 0f, 0f, 10f, 10f, bottom);
            var fall = LevelEdgeWalls.KillHeight(bottom);
            var kill = WaterLayout.KillHeight(fall, layout);
            Assert.That(kill, Is.GreaterThanOrEqualTo(fall));
            Assert.That(kill, Is.LessThan(bottom + 1f), "игрок на самых нижних блоках не тонет");
        }
    }
}
