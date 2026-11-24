using System;
using GameCore.Config;
using Newtonsoft.Json;

namespace GameCore.Newtonsoft
{
    /// <summary>
    /// Сериализатор конфигов на Newtonsoft.Json: в Unity это пакет com.unity.nuget.newtonsoft-json,
    /// в Godot и тестах — NuGet-пакет Newtonsoft.Json.
    /// По умолчанию без TypeNameHandling: конфиги из модов не могут создавать произвольные типы.
    /// </summary>
    public sealed class NewtonsoftConfigSerializer : IConfigSerializer
    {
        public static JsonSerializerSettings DefaultSettings() => new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        private readonly JsonSerializerSettings settings;

        public NewtonsoftConfigSerializer(JsonSerializerSettings settings = null)
        {
            this.settings = settings ?? DefaultSettings();
        }

        public object Deserialize(string text, Type type) => JsonConvert.DeserializeObject(text, type, settings);

        public string Serialize(object value, bool indented = true) =>
            JsonConvert.SerializeObject(value, indented ? Formatting.Indented : Formatting.None, settings);
    }
}
