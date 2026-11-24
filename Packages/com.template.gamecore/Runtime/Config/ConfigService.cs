using System;
using System.Collections.Generic;

namespace GameCore.Config
{
    /// <summary>
    /// Загрузка конфигов (ResourcesController.LoadConfigs и кэш ModuleFactory из Sparta2).
    /// <list type="bullet">
    /// <item>Источник и сериализатор подставляются снаружи: файлы, Resources, res://, память в тестах.</item>
    /// <item>Результат кэшируется по ключу и типу.</item>
    /// <item><see cref="Bind{T}"/> применяет конфиг сразу и после каждого <see cref="Reload"/>
    /// (горячая перезагрузка, включение и выключение модов).</item>
    /// <item>Ошибки — всегда <see cref="ConfigException"/> с ключом конфига.</item>
    /// </list>
    /// </summary>
    public sealed class ConfigService
    {
        private const string LogTag = nameof(ConfigService);

        private readonly IConfigSource source;
        private readonly IConfigSerializer serializer;
        private readonly ILogger logger;
        private readonly Dictionary<string, object> cache = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Action>> bindings = new Dictionary<string, List<Action>>(StringComparer.OrdinalIgnoreCase);

        public ConfigService(IConfigSource source, IConfigSerializer serializer, ILogger logger = null)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            this.logger = logger ?? NullLogger.Instance;
        }

        public bool Exists(string key) => source.TryRead(key, out _);

        public T Load<T>(string key) where T : class
        {
            var cacheKey = CacheKey(key, typeof(T));
            if (cache.TryGetValue(cacheKey, out var cached))
                return (T)cached;

            var config = Parse<T>(key, ReadText(key));
            cache[cacheKey] = config;
            return config;
        }

        public bool TryLoad<T>(string key, out T config) where T : class
        {
            if (!Exists(key))
            {
                config = null;
                return false;
            }

            config = Load<T>(key);
            return true;
        }

        /// <summary>
        /// Загружает файл-словарь { "id": { ... }, ... }. Записям с <see cref="IIdentified"/> проставляется Id.
        /// </summary>
        public ConfigTable<T> LoadTable<T>(string key) where T : class
        {
            var cacheKey = CacheKey(key, typeof(ConfigTable<T>));
            if (cache.TryGetValue(cacheKey, out var cached))
                return (ConfigTable<T>)cached;

            var dictionary = Parse<Dictionary<string, T>>(key, ReadText(key));
            foreach (var pair in dictionary)
            {
                if (pair.Value == null)
                    throw new ConfigException(key, $"entry '{pair.Key}' is null");
                if (pair.Value is IIdentified identified)
                    identified.Id = pair.Key;
                RunPostLoad(key, pair.Value);
            }

            var table = new ConfigTable<T>(key, dictionary);
            cache[cacheKey] = table;
            return table;
        }

        /// <summary>
        /// Применяет конфиг сейчас и после каждой перезагрузки этого ключа.
        /// Отписка — через возвращённый IDisposable.
        /// </summary>
        public IDisposable Bind<T>(string key, Action<T> apply) where T : class
        {
            if (apply == null)
                throw new ArgumentNullException(nameof(apply));

            Action reload = () => apply(Load<T>(key));
            reload();

            if (!bindings.TryGetValue(key, out var list))
            {
                list = new List<Action>();
                bindings.Add(key, list);
            }

            list.Add(reload);
            return Disposable.Create(() => list.Remove(reload));
        }

        /// <summary>
        /// Сбрасывает кэш ключа и заново применяет все его привязки.
        /// Ошибка одной привязки не мешает остальным и уходит в лог.
        /// </summary>
        public void Reload(string key)
        {
            var prefix = key + "|";
            var stale = new List<string>();
            foreach (var cacheKey in cache.Keys)
            {
                if (cacheKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    stale.Add(cacheKey);
            }

            foreach (var cacheKey in stale)
                cache.Remove(cacheKey);

            if (!bindings.TryGetValue(key, out var list))
                return;

            foreach (var reload in list.ToArray())
            {
                try
                {
                    reload();
                }
                catch (Exception e)
                {
                    logger.Error(LogTag, $"Can't reload config '{key}'");
                    logger.LogException(e, LogTag);
                }
            }
        }

        public void ReloadAll()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cacheKey in cache.Keys)
                keys.Add(cacheKey.Substring(0, cacheKey.IndexOf('|')));
            foreach (var key in bindings.Keys)
                keys.Add(key);

            foreach (var key in keys)
                Reload(key);
        }

        private string ReadText(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Config key is empty", nameof(key));

            string text;
            try
            {
                if (!source.TryRead(key, out text))
                    throw new ConfigException(key, "not found");
            }
            catch (ConfigException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new ConfigException(key, "can't read", e);
            }

            return text;
        }

        private T Parse<T>(string key, string text) where T : class
        {
            T config;
            try
            {
                config = serializer.Deserialize(text, typeof(T)) as T;
            }
            catch (Exception e)
            {
                throw new ConfigException(key, $"can't parse as {typeof(T).Name}: {e.Message}", e);
            }

            if (config == null)
                throw new ConfigException(key, $"parsed to null as {typeof(T).Name}");

            RunPostLoad(key, config);
            return config;
        }

        private static void RunPostLoad(string key, object config)
        {
            if (!(config is IConfigPostLoad postLoad))
                return;

            try
            {
                postLoad.OnConfigLoaded();
            }
            catch (Exception e)
            {
                throw new ConfigException(key, $"validation failed: {e.Message}", e);
            }
        }

        private static string CacheKey(string key, Type type) => key + "|" + type.FullName;
    }
}
