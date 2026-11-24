using ColorsAction.Configs;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    public class LevelSlopeTests
    {
        private static LevelConfig Level(params string[] rows)
        {
            var level = new LevelConfig { Rows = new System.Collections.Generic.List<string>(rows) };
            level.OnConfigLoaded();
            return level;
        }

        [Test]
        public void QuarterStep_BecomesSlopeOnTheLowerCell()
        {
            // z = 0 row: heights 0, 1 (0.25 m).
            var level = Level("01");
            level.TopCorners(0, 0, out var a00, out var a01, out var a11, out var a10);
            Assert.That(a00, Is.EqualTo(0f));
            Assert.That(a01, Is.EqualTo(0f));
            Assert.That(a10, Is.EqualTo(0.25f), "edge toward the higher cell rises to its top");
            Assert.That(a11, Is.EqualTo(0.25f));

            level.TopCorners(1, 0, out var b00, out var b01, out var b11, out var b10);
            Assert.That(new[] { b00, b01, b11, b10 }, Is.All.EqualTo(0.25f), "the higher cell stays flat");
        }

        [Test]
        public void HalfMeterAndMore_StaysAStep()
        {
            var level = Level("02");
            level.TopCorners(0, 0, out var a00, out var a01, out var a11, out var a10);
            Assert.That(new[] { a00, a01, a11, a10 }, Is.All.EqualTo(0f));
        }

        [Test]
        public void QuarterStaircase_IsContinuousRamp()
        {
            var level = Level("0123");
            for (int x = 0; x < 3; x++)
            {
                level.TopCorners(x, 0, out _, out _, out var right, out _);
                level.TopCorners(x + 1, 0, out var left, out _, out _, out _);
                Assert.That(right, Is.EqualTo(left), $"no seam between {x} and {x + 1}");
            }

            Assert.That(level.SurfaceHeight(1, 0, 0.5f, 0.5f), Is.EqualTo(0.375f).Within(1e-4f));
        }

        [Test]
        public void SlopesCanBeDisabled()
        {
            var level = Level("01");
            level.SlopeMaxStep = 0f;
            level.TopCorners(0, 0, out _, out _, out _, out var a10);
            Assert.That(a10, Is.EqualTo(0f));
        }
    }
}
