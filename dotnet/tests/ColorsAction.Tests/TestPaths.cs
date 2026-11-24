using System.IO;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    internal static class TestPaths
    {
        /// <summary>Assets/StreamingAssets/Configs проекта, найденная вверх от папки тестов.</summary>
        public static string ConfigsRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "StreamingAssets", "Configs")))
                dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "project root with Assets/StreamingAssets/Configs not found");
            return Path.Combine(dir!.FullName, "Assets", "StreamingAssets", "Configs");
        }
    }
}
