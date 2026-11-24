using System;

namespace GameCore.Events
{
    /// <summary>
    /// Порядок вызова подписчиков одного события. Внутри одного приоритета порядок подписки сохраняется.
    /// </summary>
    public enum EventPriority
    {
        High = 0,
        Normal = 1,
        Low = 2
    }

    /// <summary>
    /// Шина событий с ключом по типу события (EventDispatcher из Sparta2).
    /// Событие — обычный класс или структура с данными.
    /// </summary>
    public interface IEventBus
    {
        IDisposable Subscribe<T>(Action<T> handler, EventPriority priority = EventPriority.Normal);
        IDisposable Subscribe<T>(Action handler, EventPriority priority = EventPriority.Normal);
        void Unsubscribe<T>(Action<T> handler);
        void Unsubscribe<T>(Action handler);
        void Publish<T>(T evt);
        bool HasSubscribers<T>();
    }

    public static class EventBusExtensions
    {
        /// <summary>
        /// Публикует событие без данных: создаёт экземпляр конструктором по умолчанию.
        /// </summary>
        public static void Publish<T>(this IEventBus bus) where T : new()
        {
            bus.Publish(new T());
        }
    }
}
