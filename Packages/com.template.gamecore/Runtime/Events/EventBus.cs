using System;
using System.Collections.Generic;

namespace GameCore.Events
{
    /// <summary>
    /// Однопоточная шина событий.
    /// <list type="bullet">
    /// <item>Подписка и отписка во время рассылки безопасны: отписанный обработчик больше не вызывается,
    /// новый получит только следующие события.</item>
    /// <item>Исключение в обработчике не прерывает рассылку. С логгером оно уходит в лог,
    /// без логгера пробрасывается после того, как все обработчики отработали.</item>
    /// <item>Повторная подписка того же делегата на то же событие игнорируется.</item>
    /// </list>
    /// </summary>
    public sealed class EventBus : IEventBus
    {
        private const string LogTag = nameof(EventBus);

        private readonly Dictionary<Type, Channel> channels = new Dictionary<Type, Channel>();
        private readonly ILogger logger;

        public EventBus(ILogger logger = null)
        {
            this.logger = logger;
        }

        public IDisposable Subscribe<T>(Action<T> handler, EventPriority priority = EventPriority.Normal)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            return GetOrCreateChannel<T>().Add(handler, priority, logger);
        }

        public IDisposable Subscribe<T>(Action handler, EventPriority priority = EventPriority.Normal)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            return GetOrCreateChannel<T>().Add(handler, priority, logger);
        }

        public void Unsubscribe<T>(Action<T> handler)
        {
            if (handler != null && channels.TryGetValue(typeof(T), out var channel))
                channel.Remove(handler);
        }

        public void Unsubscribe<T>(Action handler)
        {
            if (handler != null && channels.TryGetValue(typeof(T), out var channel))
                channel.Remove(handler);
        }

        public void Publish<T>(T evt)
        {
            if (!channels.TryGetValue(typeof(T), out var channel))
                return;

            List<Exception> errors = null;
            var entries = channel.Entries;
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (!entry.Active)
                    continue;

                try
                {
                    if (entry.Handler is Action<T> typed)
                        typed(evt);
                    else
                        ((Action)entry.Handler)();
                }
                catch (Exception e)
                {
                    if (logger != null)
                    {
                        logger.LogException(e, LogTag);
                    }
                    else
                    {
                        errors ??= new List<Exception>();
                        errors.Add(e);
                    }
                }
            }

            if (errors != null)
                throw new AggregateException($"Event {typeof(T).Name} handlers threw", errors);
        }

        public bool HasSubscribers<T>()
        {
            return channels.TryGetValue(typeof(T), out var channel) && channel.Entries.Length > 0;
        }

        /// <summary>
        /// Все подписки: тип события и делегат. Нужен для отладочных проверок,
        /// например поиска обработчиков уничтоженных объектов движка.
        /// </summary>
        public IEnumerable<KeyValuePair<Type, Delegate>> GetSubscriptions()
        {
            foreach (var pair in channels)
            {
                foreach (var entry in pair.Value.Entries)
                    yield return new KeyValuePair<Type, Delegate>(pair.Key, entry.Handler);
            }
        }

        public void Clear()
        {
            foreach (var channel in channels.Values)
                channel.RemoveAll();
            channels.Clear();
        }

        private Channel GetOrCreateChannel<T>()
        {
            if (!channels.TryGetValue(typeof(T), out var channel))
            {
                channel = new Channel(typeof(T));
                channels.Add(typeof(T), channel);
            }

            return channel;
        }

        private sealed class Entry
        {
            public readonly Delegate Handler;
            public readonly EventPriority Priority;
            public bool Active = true;

            public Entry(Delegate handler, EventPriority priority)
            {
                Handler = handler;
                Priority = priority;
            }
        }

        /// <summary>
        /// Подписчики одного типа события. Массив копируется при изменении,
        /// поэтому рассылка идёт по снимку без аллокаций.
        /// </summary>
        private sealed class Channel
        {
            private static readonly Entry[] None = new Entry[0];

            private readonly Type eventType;

            public Entry[] Entries { get; private set; } = None;

            public Channel(Type eventType)
            {
                this.eventType = eventType;
            }

            public IDisposable Add(Delegate handler, EventPriority priority, ILogger logger)
            {
                foreach (var existing in Entries)
                {
                    if (existing.Handler.Equals(handler))
                    {
                        logger?.Warning(LogTag, $"{handler.Method.DeclaringType?.Name}.{handler.Method.Name} " +
                                                $"is already subscribed to {eventType.Name}");
                        return Disposable.Create(() => Remove(handler));
                    }
                }

                var entry = new Entry(handler, priority);
                var index = Entries.Length;
                while (index > 0 && Entries[index - 1].Priority > priority)
                    index--;

                var next = new Entry[Entries.Length + 1];
                Array.Copy(Entries, 0, next, 0, index);
                next[index] = entry;
                Array.Copy(Entries, index, next, index + 1, Entries.Length - index);
                Entries = next;

                return Disposable.Create(() => Remove(entry));
            }

            public void Remove(Delegate handler)
            {
                foreach (var entry in Entries)
                {
                    if (entry.Handler.Equals(handler))
                    {
                        Remove(entry);
                        return;
                    }
                }
            }

            public void RemoveAll()
            {
                foreach (var entry in Entries)
                    entry.Active = false;
                Entries = None;
            }

            private void Remove(Entry entry)
            {
                var index = Array.IndexOf(Entries, entry);
                if (index < 0)
                    return;

                entry.Active = false;
                var next = new Entry[Entries.Length - 1];
                Array.Copy(Entries, 0, next, 0, index);
                Array.Copy(Entries, index + 1, next, index, Entries.Length - index - 1);
                Entries = next;
            }
        }
    }
}
