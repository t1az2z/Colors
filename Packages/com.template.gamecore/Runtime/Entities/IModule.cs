using System;

namespace GameCore.Entities
{
    /// <summary>
    /// Модуль сущности: здоровье, оружие, передвижение и т. п. (IModule из Sparta2).
    /// Порядок жизни: Attach (сущность известна, соседи могут быть ещё не добавлены) →
    /// Start (все модули на месте, можно искать соседей) → Dispose (сущность уничтожена).
    /// </summary>
    public interface IModule : IDisposable
    {
        Entity Owner { get; }
        void Attach(Entity owner);
        void Start();
    }

    public abstract class Module : IModule
    {
        public Entity Owner { get; private set; }
        public bool IsStarted { get; private set; }
        public bool IsDisposed { get; private set; }

        void IModule.Attach(Entity owner)
        {
            if (Owner != null)
                throw new InvalidOperationException($"{GetType().Name} is already attached to entity {Owner.Id}");
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            OnAttach();
        }

        void IModule.Start()
        {
            if (IsStarted)
                return;
            IsStarted = true;
            OnStart();
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            OnDispose();
        }

        protected virtual void OnAttach()
        {
        }

        protected virtual void OnStart()
        {
        }

        protected virtual void OnDispose()
        {
        }
    }

    /// <summary>
    /// Модуль с конфигом. Конфиг общий для всех модулей с тем же id и только для чтения.
    /// </summary>
    public abstract class Module<TConfig> : Module where TConfig : class
    {
        public TConfig Config { get; }

        protected Module(TConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }
    }
}
