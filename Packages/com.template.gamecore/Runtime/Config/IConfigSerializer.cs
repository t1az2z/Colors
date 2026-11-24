using System;

namespace GameCore.Config
{
    /// <summary>
    /// Разбор текста конфига. Ядро не зависит от JSON-библиотеки:
    /// готовая реализация на Newtonsoft лежит в сборке GameCore.Newtonsoft.
    /// </summary>
    public interface IConfigSerializer
    {
        object Deserialize(string text, Type type);
    }

    /// <summary>
    /// Конфиг, которому нужно достроить себя после разбора: индексы, ссылки, проверки (IPostParser из Sparta2).
    /// Исключение из <see cref="OnConfigLoaded"/> считается ошибкой загрузки.
    /// </summary>
    public interface IConfigPostLoad
    {
        void OnConfigLoaded();
    }

    /// <summary>
    /// Запись таблицы конфигов. Id проставляется из ключа словаря при загрузке через LoadTable.
    /// </summary>
    public interface IIdentified
    {
        string Id { get; set; }
    }

    public sealed class ConfigException : Exception
    {
        public string Key { get; }

        public ConfigException(string key, string message, Exception inner = null)
            : base($"Config '{key}': {message}", inner)
        {
            Key = key;
        }
    }
}
