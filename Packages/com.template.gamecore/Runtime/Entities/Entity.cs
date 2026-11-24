using System;
using System.Collections.Generic;

namespace GameCore.Entities
{
    /// <summary>
    /// Сущность — идентификатор, теги и набор модулей. Логики в ней нет, она вся в модулях.
    /// Создаётся и уничтожается через <see cref="EntityWorld"/>.
    /// </summary>
    public sealed class Entity
    {
        private static readonly string[] NoTags = new string[0];

        private readonly List<IModule> modules = new List<IModule>();
        private readonly HashSet<string> tags;
        private bool started;

        public long Id { get; }
        public bool IsAlive { get; private set; } = true;
        public IReadOnlyList<IModule> Modules => modules;
        public IEnumerable<string> Tags => tags;

        internal Entity(long id, IEnumerable<string> tags)
        {
            Id = id;
            this.tags = new HashSet<string>(tags ?? NoTags, StringComparer.Ordinal);
        }

        public bool HasTag(string tag) => tags.Contains(tag);

        public bool Has<T>() where T : class => TryGet<T>(out _);

        public T Get<T>() where T : class
        {
            if (TryGet<T>(out var module))
                return module;
            throw new InvalidOperationException($"Entity {Id} has no module {typeof(T).Name}");
        }

        public bool TryGet<T>(out T module) where T : class
        {
            foreach (var item in modules)
            {
                if (item is T typed)
                {
                    module = typed;
                    return true;
                }
            }

            module = null;
            return false;
        }

        public List<T> GetAll<T>() where T : class
        {
            var result = new List<T>();
            foreach (var item in modules)
            {
                if (item is T typed)
                    result.Add(typed);
            }

            return result;
        }

        /// <summary>
        /// Добавляет модуль. Если сущность уже запущена, модуль сразу получает Attach и Start.
        /// </summary>
        public void Add(IModule module)
        {
            if (module == null)
                throw new ArgumentNullException(nameof(module));
            if (!IsAlive)
                throw new InvalidOperationException($"Entity {Id} is destroyed");

            modules.Add(module);
            module.Attach(this);
            if (started)
                module.Start();
        }

        /// <summary>
        /// Убирает модуль и освобождает его.
        /// </summary>
        public bool Remove(IModule module)
        {
            if (!modules.Remove(module))
                return false;
            module.Dispose();
            return true;
        }

        public override string ToString() => $"Entity({Id})";

        internal void Start()
        {
            started = true;
            for (int i = 0; i < modules.Count; i++)
                modules[i].Start();
        }

        internal void Destroy()
        {
            IsAlive = false;
            for (int i = modules.Count - 1; i >= 0; i--)
                modules[i].Dispose();
        }
    }
}
