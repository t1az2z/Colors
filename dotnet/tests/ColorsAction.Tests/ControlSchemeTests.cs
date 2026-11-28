using ColorsAction.Configs;
using ColorsAction.States;
using GameCore;
using GameCore.Config;
using GameCore.Hosting;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    public class ControlSchemeTests
    {
        private static GameHost CreateHost(string cameraJson)
        {
            var root = TestPaths.ConfigsRoot();
            IConfigSource source = new FileConfigSource(root);
            if (cameraJson != null)
                source = new LayeredConfigSource(source, new InMemoryConfigSource().Set("Camera", cameraJson));
            return new GameHost(new ConsoleLogger(), new ConfigInstaller(source, new NewtonsoftConfigSerializer()), new ColorsActionInstaller());
        }

        [Test]
        public void SchemeStartsFromConfig()
        {
            using var host = CreateHost("{ \"scheme\": \"FixedAngles\" }");
            host.Start<BootState>();
            var session = host.Container.Resolve<GameSession>();
            Assert.That(session.ControlScheme.Value, Is.EqualTo(CameraScheme.FixedAngles));
        }

        [Test]
        public void ProjectScheme_IsFixedAngles()
        {
            // Схема А (свободная камера) из игры убрана: в проекте только 4 угла камеры.
            using var host = CreateHost(null);
            host.Start<BootState>();
            Assert.That(host.Container.Resolve<GameSession>().ControlScheme.Value, Is.EqualTo(CameraScheme.FixedAngles));
        }

        [Test]
        public void ProjectCameraConfig_MatchesGdd()
        {
            using var host = CreateHost(null);
            var camera = host.Container.Resolve<ConfigService>().Load<CameraConfig>(ConfigKeys.Camera);
            Assert.That(camera.FixedAngleStep, Is.EqualTo(90f));
            Assert.That(camera.FixedRotateTime, Is.EqualTo(0.25f).Within(0.001f));
        }
    }
}
