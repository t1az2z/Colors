using System;
using GameCore.Config;
using GameCore.DI;
using GameCore.Entities;
using GameCore.Events;

namespace GameCore.Hosting
{
    /// <summary>
    /// Регистрирует ConfigService с источником и сериализатором, которые даёт платформа.
    /// </summary>
    public sealed class ConfigInstaller : IInstaller
    {
        private readonly IConfigSource source;
        private readonly IConfigSerializer serializer;

        public ConfigInstaller(IConfigSource source, IConfigSerializer serializer)
        {
            this.source = source;
            this.serializer = serializer;
        }

        public void Install(Container container)
        {
            container
                .RegisterInstance(source)
                .RegisterInstance(serializer)
                .Register(r => new ConfigService(r.Resolve<IConfigSource>(), r.Resolve<IConfigSerializer>(), r.Resolve<ILogger>()));
        }
    }

    /// <summary>
    /// Регистрирует EntityWorld, ModuleFactory и EntityFactory.
    /// Типы модулей регистрируются в <c>registerModules</c>: он вызывается один раз, когда фабрику впервые запросят,
    /// и получает резолвер, чтобы модули могли брать сервисы из контейнера.
    /// </summary>
    public sealed class EntitiesInstaller : IInstaller
    {
        private readonly Action<ModuleFactory, IResolver> registerModules;
        private readonly string modulesFolder;
        private readonly string templatesKey;

        public EntitiesInstaller(Action<ModuleFactory, IResolver> registerModules = null,
            string modulesFolder = "Modules", string templatesKey = "Entities")
        {
            this.registerModules = registerModules;
            this.modulesFolder = modulesFolder;
            this.templatesKey = templatesKey;
        }

        public void Install(Container container)
        {
            container
                .Register(r => new EntityWorld(r.Resolve<IEventBus>()))
                .Register(r =>
                {
                    var factory = new ModuleFactory(r.Resolve<ConfigService>(), modulesFolder);
                    registerModules?.Invoke(factory, r);
                    return factory;
                })
                .Register(r => new EntityFactory(r.Resolve<EntityWorld>(), r.Resolve<ModuleFactory>(),
                    r.Resolve<ConfigService>(), templatesKey));
        }
    }
}
