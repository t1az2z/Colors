using System.Collections.Generic;
using System.IO;
using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Progression;
using GameCore.Config;
using GameCore.Newtonsoft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Окно «Параметры запуска»: какую карту грузить, стартовое оружие с уровнем апгрейда и опыт. Сохраняется в
    /// UserSettings/ColorsActionLaunch.json (<see cref="LaunchOptionsStore"/>), игра читает его при входе в Play Mode.
    /// </summary>
    public sealed class LaunchOptionsWindow : EditorWindow
    {
        private const string ScenePath = "Assets/Game/Scenes/Prototype.unity";

        private LaunchOptions options;
        private List<string> levels = new List<string>();
        private readonly List<(string id, string name, bool twoHanded)> weapons = new List<(string, string, bool)>();
        private string defaultLevel;
        private Vector2 scroll;

        [MenuItem("ColorsAction/Параметры запуска", priority = 1)]
        public static void Open()
        {
            var w = GetWindow<LaunchOptionsWindow>();
            w.titleContent = new GUIContent("Параметры запуска");
            w.minSize = new Vector2(340, 260);
            w.Show();
        }

        private void OnEnable()
        {
            options = LaunchOptionsStore.Load();
            options.Weapons ??= new List<StartWeapon>();
            Refresh();
        }

        private void OnFocus() => Refresh();

        private void Refresh()
        {
            levels = LevelImporter.ListLevels();
            weapons.Clear();
            try
            {
                var configs = new ConfigService(new FileConfigSource(LevelImporter.ConfigsRoot), new NewtonsoftConfigSerializer());
                defaultLevel = configs.Load<GameConfig>(ConfigKeys.Game).Level;
                foreach (var item in configs.LoadTable<ItemConfig>(ConfigKeys.Items).Values.Where(i => i.IsWeapon))
                    weapons.Add((item.Id, string.IsNullOrEmpty(item.Name) ? item.Id : item.Name, item.IsTwoHanded));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Параметры запуска: конфиги не прочитаны — " + e.Message);
            }

            Repaint();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();

            options.Enabled = EditorGUILayout.ToggleLeft("Применять при запуске (Play)", options.Enabled, EditorStyles.boldLabel);
            if (!options.Enabled)
                EditorGUILayout.HelpBox("Выключено: игра запускается по Game.json, без стартового оружия и опыта.", MessageType.None);

            using (new EditorGUI.DisabledScope(!options.Enabled))
            {
                DrawLevel();
                EditorGUILayout.Space(8);
                DrawWeapons();
                EditorGUILayout.Space(8);
                DrawExperience();
            }

            if (EditorGUI.EndChangeCheck())
                LaunchOptionsStore.Save(options);

            EditorGUILayout.Space(12);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("▶  Играть", GUILayout.Height(28)))
                    Play();
            }

            if (EditorApplication.isPlaying)
                EditorGUILayout.HelpBox("Изменения применятся при следующем запуске.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void DrawLevel()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Карта", EditorStyles.boldLabel);
            var labels = new List<string> { $"Из Game.json ({defaultLevel})" };
            labels.AddRange(levels.Select(k => k.Replace("/", " / ")));
            var index = string.IsNullOrEmpty(options.Level) ? 0 : levels.IndexOf(options.Level) + 1;
            if (index == 0 && !string.IsNullOrEmpty(options.Level))
            {
                EditorGUILayout.HelpBox($"Карта {options.Level} не найдена — будет взята из Game.json.", MessageType.Warning);
            }

            var picked = EditorGUILayout.Popup(index, labels.ToArray());
            if (picked != index)
                options.Level = picked == 0 ? null : levels[picked - 1];

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Импорт карты…"))
                {
                    var key = LevelImporter.ImportFileDialog();
                    Refresh();
                    if (key != null)
                    {
                        options.Level = key;
                        GUI.changed = true;
                    }

                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Импорт папки…"))
                {
                    LevelImporter.ImportFolderDialog();
                    Refresh();
                    GUIUtility.ExitGUI();
                }

                if (GUILayout.Button("Обновить", GUILayout.Width(80)))
                    Refresh();
            }
        }

        private void DrawWeapons()
        {
            EditorGUILayout.LabelField("Стартовое оружие", EditorStyles.boldLabel);
            if (weapons.Count == 0)
            {
                EditorGUILayout.HelpBox("В Items.json нет оружия.", MessageType.Warning);
                return;
            }

            var names = weapons.Select(w => $"{w.name} ({w.id})").ToArray();
            int remove = -1;
            for (int i = 0; i < options.Weapons.Count; i++)
            {
                var w = options.Weapons[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    var index = weapons.FindIndex(x => x.id == w.Item);
                    if (index < 0)
                    {
                        EditorGUILayout.LabelField($"нет в Items.json: {w.Item}", GUILayout.MinWidth(120));
                    }
                    else
                    {
                        var picked = EditorGUILayout.Popup(index, names, GUILayout.MinWidth(120));
                        if (picked != index)
                            w.Item = weapons[picked].id;
                    }

                    EditorGUILayout.LabelField("Апгрейд", GUILayout.Width(52));
                    w.Upgrade = EditorGUILayout.IntSlider(w.Upgrade, 0, WeaponUpgrades.MaxTier, GUILayout.MinWidth(110));
                    if (GUILayout.Button("✕", GUILayout.Width(22)))
                        remove = i;
                }
            }

            if (remove >= 0)
            {
                options.Weapons.RemoveAt(remove);
                GUI.changed = true;
            }

            if (GUILayout.Button("+ Добавить оружие"))
            {
                var free = weapons.FirstOrDefault(x => options.Weapons.All(w => w.Item != x.id));
                options.Weapons.Add(new StartWeapon { Item = free.id ?? weapons[0].id });
                GUI.changed = true;
            }

            EditorGUILayout.HelpBox("Оружие кладётся в инвентарь и надевается в свободную руку по порядку списка; " +
                                    "остальное остаётся в инвентаре. Апгрейд заменяет debugWeaponUpgrades из Game.json.",
                MessageType.None);
        }

        private void DrawExperience()
        {
            EditorGUILayout.LabelField("Опыт", EditorStyles.boldLabel);
            var set = options.Experience >= 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                var newSet = EditorGUILayout.ToggleLeft("Задать опыт", set, GUILayout.Width(100));
                if (newSet != set)
                    options.Experience = newSet ? 0 : -1;
                using (new EditorGUI.DisabledScope(!newSet))
                {
                    var value = EditorGUILayout.IntField(Mathf.Max(0, options.Experience));
                    if (newSet)
                        options.Experience = Mathf.Max(0, value);
                }
            }
        }

        private void Play()
        {
            LaunchOptionsStore.Save(options);
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath && File.Exists(ScenePath))
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                EditorSceneManager.OpenScene(ScenePath);
            }

            EditorApplication.isPlaying = true;
        }
    }
}
