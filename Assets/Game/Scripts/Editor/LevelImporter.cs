using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ColorsAction.Level;
using GameCore.Newtonsoft;
using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Импорт карт из веб-редактора уровней (Tools/LevelEditorWeb, файлы .json или .json.txt) в
    /// StreamingAssets/Configs/VoxelLevels. Импортированную карту можно выбрать в окне «Параметры запуска» и сразу играть.
    /// </summary>
    public static class LevelImporter
    {
        public static string ConfigsRoot => Path.Combine(Application.streamingAssetsPath, "Configs");
        public static string VoxelFolder => Path.Combine(ConfigsRoot, LevelKeys.VoxelFolder);
        public static string HeightmapFolder => Path.Combine(ConfigsRoot, LevelKeys.HeightmapFolder);

        private const string LastFolderPref = "ColorsAction.LevelImport.LastFolder";

        [MenuItem("ColorsAction/Уровни/Импорт карты из веб-редактора…", priority = 20)]
        public static void ImportFileMenu() => ImportFileDialog();

        [MenuItem("ColorsAction/Уровни/Импорт всех карт из папки…", priority = 21)]
        public static void ImportFolderMenu() => ImportFolderDialog();

        /// <summary>Выбор файла и импорт. Возвращает ключ уровня ("VoxelLevels/Имя") или null.</summary>
        public static string ImportFileDialog()
        {
            var path = EditorUtility.OpenFilePanelWithFilters("Карта из веб-редактора", StartFolder(),
                new[] { "Карта уровня", "json,txt", "Все файлы", "*" });
            if (string.IsNullOrEmpty(path))
                return null;
            EditorPrefs.SetString(LastFolderPref, Path.GetDirectoryName(path));
            return Import(path, askOverwrite: true);
        }

        /// <summary>Импорт всех карт папки (файлы, которые не читаются как карта, пропускаются). Возвращает ключи.</summary>
        public static List<string> ImportFolderDialog()
        {
            var folder = EditorUtility.OpenFolderPanel("Папка с картами из веб-редактора", StartFolder(), "");
            var keys = new List<string>();
            if (string.IsNullOrEmpty(folder))
                return keys;
            EditorPrefs.SetString(LastFolderPref, folder);
            var files = Directory.GetFiles(folder).Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".json.txt", StringComparison.OrdinalIgnoreCase)).ToList();
            var overwriteAll = false;
            foreach (var f in files)
            {
                if (!TryRead(f, out _, out _))
                    continue;
                var name = LevelName(f);
                if (File.Exists(TargetPath(name)) && !overwriteAll)
                {
                    var choice = EditorUtility.DisplayDialogComplex("Импорт карт",
                        $"Карта «{name}» уже есть в проекте. Заменить?", "Заменить", "Пропустить", "Заменить все");
                    if (choice == 1)
                        continue;
                    if (choice == 2)
                        overwriteAll = true;
                }

                var key = Import(f, askOverwrite: false);
                if (key != null)
                    keys.Add(key);
            }

            EditorUtility.DisplayDialog("Импорт карт",
                keys.Count == 0 ? "В папке не нашлось карт веб-редактора." : "Импортировано: " + string.Join(", ", keys), "OK");
            return keys;
        }

        /// <summary>
        /// Проверяет, что файл — карта веб-редактора (блоки читаются), и копирует его в VoxelLevels под именем файла.
        /// Возвращает ключ уровня или null (ошибка или отказ заменить).
        /// </summary>
        public static string Import(string path, bool askOverwrite)
        {
            if (!TryRead(path, out var text, out var error))
            {
                EditorUtility.DisplayDialog("Импорт карты", $"{Path.GetFileName(path)} не похож на карту веб-редактора:\n{error}", "OK");
                return null;
            }

            var name = LevelName(path);
            var target = TargetPath(name);
            if (Path.GetFullPath(target) == Path.GetFullPath(path))
                return LevelKeys.VoxelFolder + "/" + name;
            if (askOverwrite && File.Exists(target) &&
                !EditorUtility.DisplayDialog("Импорт карты", $"Карта «{name}» уже есть в проекте. Заменить?", "Заменить", "Отмена"))
                return null;

            Directory.CreateDirectory(VoxelFolder);
            File.WriteAllText(target, text);
            AssetDatabase.Refresh();
            var key = LevelKeys.VoxelFolder + "/" + name;
            Debug.Log($"Карта импортирована: {key} ← {path}");
            return key;
        }

        /// <summary>Читает и проверяет карту: формат VoxelLevels, разбор всех блоков и клеток объектов.</summary>
        public static bool TryRead(string path, out string text, out string error)
        {
            text = null;
            error = null;
            try
            {
                text = File.ReadAllText(path);
                var data = (VoxelLevelData)new NewtonsoftConfigSerializer().Deserialize(text, typeof(VoxelLevelData));
                if (data == null || data.Blocks == null || data.Blocks.Count == 0)
                {
                    error = "нет блоков (\"blocks\")";
                    return false;
                }

                data.ToLevel();
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>Все уровни проекта: воксельные (VoxelLevels) и карты высот (Levels), ключами конфигов.</summary>
        public static List<string> ListLevels()
        {
            var keys = new List<string>();
            Collect(VoxelFolder, LevelKeys.VoxelFolder, keys);
            Collect(HeightmapFolder, LevelKeys.HeightmapFolder, keys);
            return keys;
        }

        private static void Collect(string folder, string prefix, List<string> keys)
        {
            if (!Directory.Exists(folder))
                return;
            foreach (var f in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                keys.Add(prefix + "/" + Path.GetFileNameWithoutExtension(f));
        }

        /// <summary>Имя уровня из имени файла: без .json / .json.txt и без «(1)», которые браузер дописывает к повторным загрузкам.</summary>
        public static string LevelName(string path)
        {
            var name = Path.GetFileName(path);
            name = Regex.Replace(name, @"(\.json)?(\.txt)?$", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\s*\(\d+\)$", "");
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Imported" : name.Trim();
        }

        private static string TargetPath(string name) => Path.Combine(VoxelFolder, name + ".json");

        private static string StartFolder()
        {
            var last = EditorPrefs.GetString(LastFolderPref, "");
            if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
                return last;
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            return Directory.Exists(downloads) ? downloads : "";
        }
    }
}
