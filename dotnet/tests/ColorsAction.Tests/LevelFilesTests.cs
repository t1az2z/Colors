using System.Collections.Generic;
using System.IO;
using ColorsAction.Configs;
using ColorsAction.Level;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>
    /// Каждый файл уровня лежит в своей папке: воксельные карты редактора — в VoxelLevels, карты высот — в Levels.
    /// Воксельная карта в Levels не запускается («rows are empty»).
    /// </summary>
    public class LevelFilesTests
    {
        private static ConfigService Configs() =>
            new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());

        private static IEnumerable<string> Keys(string folder)
        {
            foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.ConfigsRoot(), folder), "*.json"))
                yield return folder + "/" + Path.GetFileNameWithoutExtension(file);
        }

        [Test]
        public void VoxelLevels_LoadAndLayOut()
        {
            var configs = Configs();
            foreach (var key in Keys(LevelKeys.VoxelFolder))
            {
                var voxel = configs.Load<VoxelLevelData>(key).ToLevel();
                var skipped = new List<string>();
                var layout = VoxelPlayLayout.ToLevelConfig(voxel, skipped);
                Assert.That(layout.Spawn, Is.Not.Null, key);
                foreach (var s in skipped)
                    TestContext.Out.WriteLine(key + ": " + s);
            }
        }

        [Test]
        public void HeightmapLevels_Load()
        {
            var configs = Configs();
            foreach (var key in Keys("Levels"))
                Assert.DoesNotThrow(() => configs.Load<LevelConfig>(key), key);
        }
    }
}
