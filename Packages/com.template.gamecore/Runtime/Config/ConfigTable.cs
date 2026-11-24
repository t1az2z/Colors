using System;
using System.Collections;
using System.Collections.Generic;

namespace GameCore.Config
{
    /// <summary>
    /// Таблица конфигов "id → запись" из одного файла, например Modules/Health.json.
    /// Записи общие для всех, кто их взял: не меняйте их во время игры, состояние держите в рантайм-объектах.
    /// </summary>
    public sealed class ConfigTable<T> : IReadOnlyDictionary<string, T>
    {
        private readonly Dictionary<string, T> items;

        public string Key { get; }

        public ConfigTable(string key, IDictionary<string, T> items)
        {
            Key = key;
            this.items = new Dictionary<string, T>(items, StringComparer.Ordinal);
        }

        public T Get(string id)
        {
            if (id == null)
                throw new ArgumentNullException(nameof(id));
            if (!items.TryGetValue(id, out var item))
                throw new ConfigException(Key, $"no entry '{id}'. Known: {string.Join(", ", items.Keys)}");
            return item;
        }

        public bool TryGet(string id, out T item) => items.TryGetValue(id, out item);

        public T this[string key] => Get(key);
        public int Count => items.Count;
        public IEnumerable<string> Keys => items.Keys;
        public IEnumerable<T> Values => items.Values;
        public bool ContainsKey(string key) => items.ContainsKey(key);
        public bool TryGetValue(string key, out T value) => items.TryGetValue(key, out value);
        public IEnumerator<KeyValuePair<string, T>> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
