using System;
using System.Collections.Generic;
using GameCore.Events;

namespace GameCore.Entities
{
    public sealed class EntityCreated
    {
        public readonly Entity Entity;
        public EntityCreated(Entity entity) => Entity = entity;
    }

    public sealed class EntityDestroyed
    {
        public readonly Entity Entity;
        public EntityDestroyed(Entity entity) => Entity = entity;
    }

    /// <summary>
    /// Реестр живых сущностей (EntityController из Sparta2).
    /// О создании и уничтожении сообщает через шину событий: представление движка
    /// подписывается на <see cref="EntityCreated"/> и <see cref="EntityDestroyed"/> и создаёт или убирает свои объекты.
    /// </summary>
    public sealed class EntityWorld : IDisposable
    {
        private readonly Dictionary<long, Entity> entities = new Dictionary<long, Entity>();
        private readonly IEventBus events;

        /// <summary>Id для следующей сущности. Сохраняйте его вместе с сейвом, чтобы id не повторялись.</summary>
        public long NextId { get; set; } = 1;

        public int Count => entities.Count;
        public IEnumerable<Entity> All => entities.Values;

        public EntityWorld(IEventBus events = null)
        {
            this.events = events;
        }

        /// <summary>
        /// Создаёт сущность: все модули получают Attach, затем все — Start.
        /// <paramref name="id"/> = 0 — взять следующий свободный id (не 0 — при загрузке сейва).
        /// </summary>
        public Entity Create(IEnumerable<IModule> modules, IEnumerable<string> tags = null, long id = 0)
        {
            if (id == 0)
                id = NextId;
            if (entities.ContainsKey(id))
                throw new InvalidOperationException($"Entity {id} already exists");
            if (id >= NextId)
                NextId = id + 1;

            var entity = new Entity(id, tags);
            if (modules != null)
            {
                foreach (var module in modules)
                    entity.Add(module);
            }

            entities.Add(id, entity);
            entity.Start();
            events?.Publish(new EntityCreated(entity));
            return entity;
        }

        public Entity Get(long id)
        {
            if (entities.TryGetValue(id, out var entity))
                return entity;
            throw new KeyNotFoundException($"Entity {id} doesn't exist");
        }

        public bool TryGet(long id, out Entity entity) => entities.TryGetValue(id, out entity);

        public bool Destroy(long id)
        {
            return entities.TryGetValue(id, out var entity) && Destroy(entity);
        }

        public bool Destroy(Entity entity)
        {
            if (entity == null || !entities.Remove(entity.Id))
                return false;

            entity.Destroy();
            events?.Publish(new EntityDestroyed(entity));
            return true;
        }

        public List<Entity> WithTag(string tag)
        {
            var result = new List<Entity>();
            foreach (var entity in entities.Values)
            {
                if (entity.HasTag(tag))
                    result.Add(entity);
            }

            return result;
        }

        public List<Entity> With<T>() where T : class
        {
            var result = new List<Entity>();
            foreach (var entity in entities.Values)
            {
                if (entity.Has<T>())
                    result.Add(entity);
            }

            return result;
        }

        public void Clear()
        {
            foreach (var entity in new List<Entity>(entities.Values))
                Destroy(entity);
        }

        public void Dispose() => Clear();
    }
}
