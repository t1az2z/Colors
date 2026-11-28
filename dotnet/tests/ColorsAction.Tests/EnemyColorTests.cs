using System.IO;
using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Level;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Цвет врага задаётся у каждого поставленного врага в карте, у типа — только цвет по умолчанию.</summary>
    public class EnemyColorTests
    {
        private static bool Known(string id) => id == "club_cube" || id == "elite_sword";

        [Test]
        public void VoxelLevel_KeepsEnemyColor_IntoLevelConfig()
        {
            var level = new VoxelLevel();
            level.PutObject(new LevelObject { Id = "e1", Kind = LevelObjectKind.EnemySpawn, Cell = new Int3(1, 0, 1), Item = "club_cube", Color = "lilac" });
            level.PutObject(new LevelObject { Id = "e2", Kind = LevelObjectKind.EnemySpawn, Cell = new Int3(3, 0, 1), Item = "club_cube" });
            var loaded = VoxelLevelData.From(level).ToLevel();
            var config = VoxelPlayLayout.ToLevelConfig(loaded);
            Assert.That(config.Enemies.Single(e => e.X == 1).Color, Is.EqualTo("lilac"));
            Assert.That(config.Enemies.Single(e => e.X == 3).Color, Is.Null, "no colour: type default");
        }

        [Test]
        public void ColorOf_MapColorWins_ElseTypeDefault()
        {
            var club = new EnemyConfig { Id = "club_cube", Color = "green" };
            Assert.That(EnemyTypes.ColorOf(club, "red"), Is.EqualTo("red"));
            Assert.That(EnemyTypes.ColorOf(club, null), Is.EqualTo("green"));
            Assert.That(EnemyTypes.ColorOf(club, ""), Is.EqualTo("green"));
        }

        [Test]
        public void Resolve_OldTypeWithColorSuffix_SplitsIntoTypeAndColor()
        {
            Assert.That(EnemyTypes.Resolve("club_cube_orange", null, Known, out var t, out var c), Is.True);
            Assert.That((t, c), Is.EqualTo(("club_cube", "orange")));
            Assert.That(EnemyTypes.Resolve("club_cube_red", "blue", Known, out t, out c), Is.True);
            Assert.That((t, c), Is.EqualTo(("club_cube", "blue")), "explicit colour beats the old suffix");
            Assert.That(EnemyTypes.Resolve("elite_sword", null, Known, out t, out c), Is.True);
            Assert.That((t, c), Is.EqualTo(("elite_sword", (string)null)));
            Assert.That(EnemyTypes.Resolve("club_cube_pink", null, Known, out _, out _), Is.False, "not a wheel colour");
            Assert.That(EnemyTypes.Resolve("boss_red", null, Known, out _, out _), Is.False, "unknown base type");
        }

        [Test]
        public void ShippedConfigs_OneClubType_EveryMapEnemyHasKnownTypeAndWheelColor()
        {
            var configs = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            var enemies = configs.LoadTable<EnemyConfig>(ConfigKeys.Enemies);
            Assert.That(enemies.TryGet("club_cube", out _) && enemies.TryGet("elite_sword", out _), Is.True);
            Assert.That(enemies.TryGet("club_cube_orange", out _), Is.False, "one club type, colour is on the map");
            var wheel = configs.Load<CombatConfig>(ConfigKeys.Combat).ColorWheel;
            foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.ConfigsRoot(), "VoxelLevels"), "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var data = configs.Load<VoxelLevelData>("VoxelLevels/" + name);
                foreach (var o in data.Objects.Where(o => o.Kind == LevelObjectKind.EnemySpawn && o.Item != VoxelPlayLayout.DummyType))
                {
                    Assert.That(enemies.TryGet(o.Item, out _), Is.True, $"{name} {o.Id}: type {o.Item}");
                    Assert.That(o.Color == null || wheel.Contains(o.Color), Is.True, $"{name} {o.Id}: colour {o.Color}");
                }
            }
        }
    }
}
