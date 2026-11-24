using System;
using System.Collections.Generic;
using System.IO;

namespace GameCore.Config
{
    /// <summary>
    /// Откуда читать текст конфига по ключу вида "Game" или "Modules/Health".
    /// Ключ не содержит расширения и корневой папки: это знает источник.
    /// </summary>
    public interface IConfigSource
    {
        bool TryRead(string key, out string text);
    }

    /// <summary>Конфиги в памяти. Для тестов и конфигов, пришедших с сервера.</summary>
    public sealed class InMemoryConfigSource : IConfigSource
    {
        private readonly Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public InMemoryConfigSource Set(string key, string text)
        {
            texts[key] = text;
            return this;
        }

        public bool Remove(string key) => texts.Remove(key);

        public bool TryRead(string key, out string text) => texts.TryGetValue(key, out text);
    }

    /// <summary>
    /// Читает файлы через System.IO: {root}/{key}{extension}.
    /// Подходит для тестов, редакторов и десктопа (StreamingAssets в Unity).
    /// На Android и WebGL StreamingAssets лежат внутри архива — там нужен источник движка.
    /// </summary>
    public sealed class FileConfigSource : IConfigSource
    {
        private readonly string root;
        private readonly string extension;

        public FileConfigSource(string root, string extension = ".json")
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.extension = extension ?? string.Empty;
        }

        public string GetPath(string key) => Path.Combine(root, key + extension);

        public bool TryRead(string key, out string text)
        {
            var path = GetPath(key);
            if (!File.Exists(path))
            {
                text = null;
                return false;
            }

            text = File.ReadAllText(path);
            return true;
        }
    }

    /// <summary>
    /// Слои источников: побеждает последний слой, в котором есть ключ.
    /// Так подключаются моды, патчи и отладочные переопределения поверх конфигов игры.
    /// </summary>
    public sealed class LayeredConfigSource : IConfigSource
    {
        private readonly List<IConfigSource> layers = new List<IConfigSource>();

        public LayeredConfigSource(params IConfigSource[] layers)
        {
            this.layers.AddRange(layers);
        }

        public IReadOnlyList<IConfigSource> Layers => layers;

        public void AddLayer(IConfigSource layer) => layers.Add(layer ?? throw new ArgumentNullException(nameof(layer)));

        public bool RemoveLayer(IConfigSource layer) => layers.Remove(layer);

        public bool TryRead(string key, out string text)
        {
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                if (layers[i].TryRead(key, out text))
                    return true;
            }

            text = null;
            return false;
        }
    }
}
