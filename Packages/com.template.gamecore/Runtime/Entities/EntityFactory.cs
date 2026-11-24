using System;
using System.Collections.Generic;
using GameCore.Config;

namespace GameCore.Entities
{
    /// <summary>
    /// Шаблон сущности из конфига: теги и список модулей.
    /// <code>
    /// "player": { "tags": ["player"], "modules": [ { "type": "Health", "id": "player" } ] }
    /// </code>
    /// </summary>
    [Preserve]
    public sealed class EntityTemplate : IIdentified
    {
        public string Id { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public List<ModuleRef> Modules { get; set; } = new List<ModuleRef>();
    }

    /// <summary>
    /// Собирает сущности по шаблонам из таблицы конфигов (по умолчанию "Entities").
    /// </summary>
    public sealed class EntityFactory
    {
        private readonly EntityWorld world;
        private readonly ModuleFactory modules;
        private readonly ConfigService configs;
        private readonly string templatesKey;

        public EntityFactory(EntityWorld world, ModuleFactory modules, ConfigService configs, string templatesKey = "Entities")
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.modules = modules ?? throw new ArgumentNullException(nameof(modules));
            this.configs = configs ?? throw new ArgumentNullException(nameof(configs));
            this.templatesKey = templatesKey;
        }

        public EntityTemplate GetTemplate(string templateId) => configs.LoadTable<EntityTemplate>(templatesKey).Get(templateId);

        /// <summary>
        /// Создаёт сущность по шаблону. <paramref name="extraModules"/> — модули, которых нет в шаблоне
        /// (например, контроллер игрока); <paramref name="id"/> = 0 — следующий свободный id.
        /// </summary>
        public Entity Create(string templateId, IEnumerable<IModule> extraModules = null, long id = 0)
        {
            var template = GetTemplate(templateId);
            var created = modules.Create(template.Modules);
            if (extraModules != null)
                created.AddRange(extraModules);

            var tags = new List<string>(template.Tags ?? new List<string>());
            return world.Create(created, tags, id);
        }
    }
}
