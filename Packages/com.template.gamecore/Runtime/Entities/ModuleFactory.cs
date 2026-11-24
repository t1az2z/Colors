using System;
using System.Collections.Generic;
using GameCore.Config;

namespace GameCore.Entities
{
    /// <summary>
    /// Ссылка на модуль в шаблоне: тип модуля и id записи в его таблице конфигов.
    /// </summary>
    [Preserve]
    public sealed class ModuleRef
    {
        public string Type { get; set; }
        public string Id { get; set; }

        public ModuleRef()
        {
        }

        public ModuleRef(string type, string id)
        {
            Type = type;
            Id = id;
        }

        public override string ToString() => $"{Type}:{Id}";
    }

    /// <summary>
    /// Создаёт модули по <see cref="ModuleRef"/> (ModuleFactory из Sparta2, но без сканирования сборок:
    /// каждый тип модуля регистрируется явно, поэтому всё работает под IL2CPP и видно в одном месте).
    /// Конфиги модуля типа X лежат в таблице "{modulesFolder}/X".
    /// </summary>
    public sealed class ModuleFactory
    {
        private readonly ConfigService configs;
        private readonly string modulesFolder;
        private readonly Dictionary<string, Func<string, IModule>> creators =
            new Dictionary<string, Func<string, IModule>>(StringComparer.Ordinal);

        public IEnumerable<string> RegisteredTypes => creators.Keys;

        public ModuleFactory(ConfigService configs, string modulesFolder = "Modules")
        {
            this.configs = configs;
            this.modulesFolder = modulesFolder;
        }

        /// <summary>
        /// Регистрирует модуль с конфигом: при создании берётся запись id из таблицы "Modules/{type}".
        /// </summary>
        public ModuleFactory Register<TConfig, TModule>(string type, Func<TConfig, TModule> create)
            where TConfig : class
            where TModule : IModule
        {
            if (configs == null)
                throw new InvalidOperationException("ModuleFactory was created without ConfigService");
            var key = $"{modulesFolder}/{type}";
            return Add(type, id => create(configs.LoadTable<TConfig>(key).Get(id)));
        }

        /// <summary>
        /// Регистрирует модуль без конфига. Id из шаблона игнорируется.
        /// </summary>
        public ModuleFactory Register<TModule>(string type, Func<TModule> create) where TModule : IModule
        {
            return Add(type, _ => create());
        }

        public IModule Create(ModuleRef moduleRef)
        {
            if (moduleRef == null)
                throw new ArgumentNullException(nameof(moduleRef));
            if (string.IsNullOrEmpty(moduleRef.Type) || !creators.TryGetValue(moduleRef.Type, out var creator))
                throw new InvalidOperationException($"Module type '{moduleRef.Type}' is not registered");

            return creator(moduleRef.Id) ??
                   throw new InvalidOperationException($"Module factory for '{moduleRef.Type}' returned null");
        }

        public List<IModule> Create(IEnumerable<ModuleRef> moduleRefs)
        {
            var result = new List<IModule>();
            foreach (var moduleRef in moduleRefs)
                result.Add(Create(moduleRef));
            return result;
        }

        private ModuleFactory Add<TModule>(string type, Func<string, TModule> creator) where TModule : IModule
        {
            if (string.IsNullOrEmpty(type))
                throw new ArgumentException("Module type is empty", nameof(type));
            if (creator == null)
                throw new ArgumentNullException(nameof(creator));
            if (creators.ContainsKey(type))
                throw new InvalidOperationException($"Module type '{type}' is already registered");

            creators.Add(type, id => creator(id));
            return this;
        }
    }
}
