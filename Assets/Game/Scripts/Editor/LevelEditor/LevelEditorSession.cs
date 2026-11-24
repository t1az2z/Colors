using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Level;
using ColorsAction.Level.Navigation;
using ColorsAction.Unity.Voxel;
using GameCore.Config;
using GameCore.Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor.LevelEditor
{
    /// <summary>Номер состояния истории для Undo Unity: Ctrl+Z / Ctrl+Y откатывают его, а мы догоняем историю уровня.</summary>
    public sealed class LevelUndoState : ScriptableObject
    {
        public int revision;
    }

    /// <summary>
    /// Открытый в редакторе уровень: модель, история, кисть, превью в сцене, палитра, профили и карта пути.
    /// Живёт, пока открыто окно редактора.
    /// </summary>
    public sealed class LevelEditorSession : IDisposable
    {
        public const string Folder = "VoxelLevels";
        public const string WorldMaterialPath = "Assets/Game/Art/Materials/World.mat";

        public VoxelLevel Level { get; } = new VoxelLevel();
        public LevelHistory History { get; }
        public LevelBrush Brush { get; }
        public VoxelLevelView View { get; private set; }
        public string Key { get; private set; }
        public bool Dirty { get; private set; }

        public readonly List<(string id, string name, Color color)> Palette = new List<(string, string, Color)>();
        /// <summary>Типы врагов из Enemies.json (id, имя, цвет) — для выбора врага у объекта EnemySpawn.</summary>
        public readonly List<(string id, string name, string color)> Enemies = new List<(string, string, string)>();
        public readonly List<MovementProfile> Profiles = new List<MovementProfile>();
        public NavGraph Graph { get; private set; }

        private readonly ConfigService configs;
        private readonly LevelUndoState undoState;
        private bool syncingUndo;

        public static string ConfigsRoot => Path.Combine(Application.streamingAssetsPath, "Configs");

        public LevelEditorSession()
        {
            configs = new ConfigService(new FileConfigSource(ConfigsRoot), new NewtonsoftConfigSerializer());
            History = new LevelHistory(Level);
            Brush = new LevelBrush(Level, History);
            undoState = ScriptableObject.CreateInstance<LevelUndoState>();
            undoState.hideFlags = HideFlags.HideAndDontSave;
            Undo.undoRedoPerformed += OnUnityUndo;
            Level.BlocksChanged += _ => Dirty = true;
            Level.MarkupChanged += () => Dirty = true;
            LoadPalette();
            LoadProfiles();
            LoadEnemies();
            EnsureView();
        }

        public void Dispose()
        {
            Undo.undoRedoPerformed -= OnUnityUndo;
            Undo.ClearUndo(undoState);
            UnityEngine.Object.DestroyImmediate(undoState);
            if (View != null)
                UnityEngine.Object.DestroyImmediate(View.gameObject);
        }

        // ------------------------------------------------------------------ превью

        /// <summary>Создаёт превью, если его нет (после смены сцены); true — превью создано заново.</summary>
        public bool EnsureView()
        {
            if (View != null)
                return false;
            var existing = GameObject.Find("~VoxelLevelPreview");
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing);
            var go = new GameObject("~VoxelLevelPreview") { hideFlags = HideFlags.HideAndDontSave };
            View = go.AddComponent<VoxelLevelView>();
            View.Colliders = false;
            View.BaseMaterial = AssetDatabase.LoadAssetAtPath<Material>(WorldMaterialPath);
            View.Bind(Level, ColorOf);
            return true;
        }

        public Color ColorOf(string id)
        {
            foreach (var p in Palette)
            {
                if (p.id == id)
                    return p.color;
            }

            return Color.magenta;
        }

        private void LoadPalette()
        {
            Palette.Clear();
            try
            {
                foreach (var kv in configs.LoadTable<ColorConfig>(ConfigKeys.Colors))
                    Palette.Add((kv.Key, kv.Value.Name, ColorUtility.TryParseHtmlString(kv.Value.Hex, out var c) ? c : Color.magenta));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public void LoadEnemies()
        {
            Enemies.Clear();
            try
            {
                foreach (var kv in configs.LoadTable<EnemyConfig>(ConfigKeys.Enemies))
                    Enemies.Add((kv.Key, string.IsNullOrEmpty(kv.Value.Name) ? kv.Key : kv.Value.Name, kv.Value.Color));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Enemies.json: " + e.Message);
            }

            // Манекен — не тип из Enemies.json, но его тоже можно поставить точкой врага.
            if (!Enemies.Any(e => e.id == "dummy"))
                Enemies.Add(("dummy", "Манекен", null));
        }

        private void LoadProfiles()
        {
            Profiles.Clear();
            try
            {
                Profiles.AddRange(configs.LoadTable<MovementProfile>("Movement").Values);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Movement.json: " + e.Message);
                Profiles.Add(new MovementProfile { Id = "player", Name = "Игрок", JumpHeight = 1.25f, MaxDrop = 4f });
            }
        }

        // ------------------------------------------------------------------ файлы

        public static IEnumerable<string> ListLevels()
        {
            var dir = Path.Combine(ConfigsRoot, Folder);
            if (!Directory.Exists(dir))
                return Array.Empty<string>();
            return Directory.GetFiles(dir, "*.json").Select(f => Folder + "/" + Path.GetFileNameWithoutExtension(f)).OrderBy(s => s);
        }

        public void New(string key)
        {
            Key = key;
            var empty = new VoxelLevelData { Name = Path.GetFileName(key) };
            empty.Apply(Level);
            // Пол 16×16, чтобы было на чём начинать.
            var floor = new List<KeyValuePair<Int3, Block?>>();
            for (int x = 0; x < 16; x++)
            for (int z = 0; z < 16; z++)
                floor.Add(new KeyValuePair<Int3, Block?>(new Int3(x, -1, z), Block.Cube()));
            Level.SetMany(floor);
            ResetHistory();
        }

        public void Load(string key)
        {
            configs.Reload(key);
            var data = configs.Load<VoxelLevelData>(key);
            data.Apply(Level);
            Key = key;
            ResetHistory();
        }

        public void ImportHeightmap(string heightmapKey, string key)
        {
            var hm = configs.Load<LevelConfig>(heightmapKey);
            var imported = VoxelLevelData.FromHeightmap(hm);
            VoxelLevelData.From(imported).Apply(Level);
            Key = key;
            ResetHistory();
            Dirty = true;
        }

        public static string ToJson(VoxelLevel level)
        {
            var settings = NewtonsoftConfigSerializer.DefaultSettings();
            settings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            settings.Converters.Add(new StringEnumConverter());
            settings.DefaultValueHandling = DefaultValueHandling.Ignore;
            return JsonConvert.SerializeObject(VoxelLevelData.From(level), Formatting.Indented, settings);
        }

        /// <summary>Несохранённое состояние переживает перезагрузку скриптов (SessionState).</summary>
        public string Backup() => ToJson(Level);

        public void Restore(string key, string json, bool dirty)
        {
            var data = (VoxelLevelData)new NewtonsoftConfigSerializer().Deserialize(json, typeof(VoxelLevelData));
            data.Apply(Level);
            Key = key;
            ResetHistory();
            Dirty = dirty;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(Key))
                throw new InvalidOperationException("level key is not set");
            var json = ToJson(Level);
            var path = Path.Combine(ConfigsRoot, Key + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
            var assetPath = "Assets/StreamingAssets/Configs/" + Key + ".json";
            AssetDatabase.ImportAsset(assetPath);
            Dirty = false;
            RebuildNav();
        }

        private void ResetHistory()
        {
            History.Clear();
            Undo.ClearUndo(undoState);
            undoState.revision = 0;
            Dirty = false;
            Graph = null;
        }

        // ------------------------------------------------------------------ отмена

        /// <summary>Записывает текущее состояние истории в Undo Unity — вызывать после каждой законченной правки.</summary>
        public void Commit(string name)
        {
            if (undoState.revision == History.Revision)
                return;
            var target = History.Revision;
            syncingUndo = true;
            Undo.RegisterCompleteObjectUndo(undoState, "Уровень: " + name);
            undoState.revision = target;
            syncingUndo = false;
        }

        public bool Execute(ILevelCommand command)
        {
            if (!History.Execute(command))
                return false;
            Commit(command.Name);
            return true;
        }

        public void EndStroke()
        {
            var name = "Кисть";
            if (Brush.End())
                Commit(name);
        }

        private void OnUnityUndo()
        {
            if (syncingUndo || undoState == null)
                return;
            if (undoState.revision != History.Revision)
                History.SeekRevision(undoState.revision);
        }

        // ------------------------------------------------------------------ карта пути

        public void RebuildNav()
        {
            Graph = NavGraph.Build(Level);
            foreach (var w in Graph.Warnings)
                Debug.LogWarning("Карта пути: " + w);
        }

        public bool NavStale => Graph == null || Graph.LevelVersion != Level.Version;
    }
}
