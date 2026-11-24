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
        public void SchemeStartsFromConfig_AndToggles()
        {
            using var host = CreateHost("{ \"scheme\": \"FixedAngles\" }");
            host.Start<BootState>();
            var session = host.Container.Resolve<GameSession>();
            Assert.That(session.ControlScheme.Value, Is.EqualTo(CameraScheme.FixedAngles));
            Assert.That(session.ToggleControlScheme(), Is.EqualTo(CameraScheme.FreeOrbit));
            Assert.That(session.ToggleControlScheme(), Is.EqualTo(CameraScheme.FixedAngles));
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
