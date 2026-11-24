using System;
using System.Collections.Generic;
using System.Linq;
using ColorsAction.Level;
using ColorsAction.Unity.Voxel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ColorsAction.Unity.Editor.LevelEditor
{
    /// <summary>
    /// Редактор воксельных уровней (раздел 15 GDD). Окно — панель инструментов, работа идёт мышью в Scene View
    /// (см. LevelEditorWindow.Scene.cs): рисование и стирание на высоте H, покраска, объекты и барьеры,
    /// точки взаимодействия, этажи, наложение карты пути.
    /// </summary>
    public sealed partial class LevelEditorWindow : EditorWindow
    {
        private enum Tool
        {
            Paint,
            Erase,
            Recolor,
            Objects,
            Points,
            Select
        }

        private static readonly string[] ToolNames = { "Рисовать", "Стереть", "Красить", "Объекты", "Точки", "Выбор" };
        private static readonly string[] RotationNames = { "+Z", "+X", "−Z", "−X" };

        private const string BackupKey = "ColorsAction.LevelEditor.Backup";
        private const string BackupLevelKey = "ColorsAction.LevelEditor.BackupKey";
        private const string BackupDirtyKey = "ColorsAction.LevelEditor.BackupDirty";

        private LevelEditorSession session;

        [SerializeField] private Tool tool = Tool.Paint;
        [SerializeField] private BlockShape shape = BlockShape.Full;
        [SerializeField] private int rotation;
        [SerializeField] private string color;
        [SerializeField] private int height;
        [SerializeField] private int activeFloor;
        [SerializeField] private UpperFloorsMode upperMode = UpperFloorsMode.Ghost;
        [SerializeField] private bool darkenBelow = true;
        [SerializeField] private int brushSize = 1;
        [SerializeField] private bool column;

        [SerializeField] private LevelObjectKind objectKind = LevelObjectKind.Barrier;
        [SerializeField] private BarrierKind barrierKind = BarrierKind.Wall;
        [SerializeField] private string objectColor = "red";
        [SerializeField] private int barrierHeight = 2;
        [SerializeField] private string objectItem = "";
        [SerializeField] private InteractionAction pointAction = InteractionAction.ShieldSlide;

        [SerializeField] private bool showNav = true;
        [SerializeField] private int profileIndex;
        [SerializeField] private bool navEverything;
        [SerializeField] private bool navReachable;

        [SerializeField] private string newLevelName = "Biome1";
        private string selectedObject;
        private string selectedPoint;
        private bool pickingTarget;
        private Vector2 scroll;

        [MenuItem("ColorsAction/Level Editor %#l")]
        public static void Open()
        {
            var w = GetWindow<LevelEditorWindow>();
            w.titleContent = new GUIContent("Редактор уровней");
            w.Show();
        }

        /// <summary>Открытый уровень (для автопроверок через MCP).</summary>
        public static LevelEditorSession ActiveSession => HasOpenInstances<LevelEditorWindow>() ? GetWindow<LevelEditorWindow>(false, null, false).session : null;

        // ------------------------------------------------------------------ жизненный цикл

        private void OnEnable()
        {
            titleContent = new GUIContent("Редактор уровней");
            session = new LevelEditorSession();
            var backup = SessionState.GetString(BackupKey, null);
            if (!string.IsNullOrEmpty(backup))
            {
                session.Restore(SessionState.GetString(BackupLevelKey, LevelEditorSession.Folder + "/Test"), backup,
                    SessionState.GetBool(BackupDirtyKey, false));
                SessionState.EraseString(BackupKey);
            }
            else
            {
                var first = LevelEditorSession.ListLevels().FirstOrDefault();
                if (first != null)
                    session.Load(first);
                else
                    session.ImportHeightmap("Levels/Test", LevelEditorSession.Folder + "/Test");
            }

            ClampFloor();
            SceneView.duringSceneGui += OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.update += OnEditorUpdate;
            session.Level.BlocksChanged += _ => Repaint();
            session.Level.MarkupChanged += Repaint;
            ApplyFloorDisplay(true);
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            EditorApplication.update -= OnEditorUpdate;
            session?.Dispose();
            session = null;
        }

        private void OnBeforeReload()
        {
            if (session == null)
                return;
            SessionState.SetString(BackupKey, session.Backup());
            SessionState.SetString(BackupLevelKey, session.Key ?? "");
            SessionState.SetBool(BackupDirtyKey, session.Dirty);
        }

        private void OnEditorUpdate()
        {
            if (session == null)
                return;
            if (session.EnsureView())
                ApplyFloorDisplay(true);
            if (session.View.HasPendingChanges)
            {
                session.View.Flush();
                ApplyFloorDisplay(true);
                SceneView.RepaintAll();
            }
        }

        private int FloorBase => session.Level.Floors[activeFloor].BaseY;
        private int LayerY => FloorBase + height;

        private void ClampFloor() => activeFloor = Mathf.Clamp(activeFloor, 0, session.Level.Floors.Count - 1);

        private void ApplyFloorDisplay(bool force = false)
        {
            if (force)
                session.View.SetFloorDisplay(-2, upperMode, darkenBelow);
            session.View.SetFloorDisplay(activeFloor, upperMode, darkenBelow);
        }

        // ------------------------------------------------------------------ окно

        private void OnGUI()
        {
            if (session == null)
                return;
            ClampFloor();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawFileSection();
            DrawHistorySection();
            EditorGUILayout.Space();
            tool = (Tool)GUILayout.Toolbar((int)tool, ToolNames);
            if (tool != Tool.Points)
                pickingTarget = false;
            EditorGUILayout.Space();
            DrawFloorSection();
            EditorGUILayout.Space();
            DrawToolSection();
            EditorGUILayout.Space();
            DrawSelectionSection();
            EditorGUILayout.Space();
            DrawNavSection();
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "ЛКМ в Scene View — применить инструмент; мазок с зажатой кнопкой — одна отмена.\n" +
                "Shift+колесо — высота H. PgUp/PgDn — этаж. R — поворот слопа. Ctrl+ЛКМ — пипетка (форма, цвет, H).\n" +
                "Ctrl+Z / Ctrl+Y — отмена и повтор. Delete — удалить выбранное. Esc — снять выбор.",
                MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void DrawFileSection()
        {
            EditorGUILayout.LabelField("Уровень", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var levels = LevelEditorSession.ListLevels().ToList();
                var label = (session.Key ?? "—") + (session.Dirty ? " *" : "");
                if (EditorGUILayout.DropdownButton(new GUIContent(label), FocusType.Passive))
                {
                    var menu = new GenericMenu();
                    foreach (var key in levels)
                        menu.AddItem(new GUIContent(key.Replace('/', '∕')), key == session.Key, () => LoadLevel(key));
                    if (levels.Count == 0)
                        menu.AddDisabledItem(new GUIContent("Нет сохранённых уровней"));
                    menu.ShowAsContext();
                }

                GUI.backgroundColor = session.Dirty ? new Color(1f, 0.85f, 0.4f) : Color.white;
                if (GUILayout.Button("Сохранить", GUILayout.Width(90)))
                    session.Save();
                GUI.backgroundColor = Color.white;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                newLevelName = EditorGUILayout.TextField(newLevelName);
                if (GUILayout.Button("Новый", GUILayout.Width(60)) && ConfirmDiscard())
                {
                    session.New(LevelEditorSession.Folder + "/" + newLevelName.Trim());
                    activeFloor = 0;
                    height = 0;
                    selectedObject = selectedPoint = null;
                }

                if (GUILayout.Button("Из карты высот", GUILayout.Width(110)) && ConfirmDiscard())
                {
                    session.ImportHeightmap("Levels/Test", LevelEditorSession.Folder + "/" + newLevelName.Trim());
                    selectedObject = selectedPoint = null;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Пустая сцена для редактирования"))
                    OpenEditingScene();
                if (GUILayout.Button("Показать уровень", GUILayout.Width(120)))
                    FrameLevel();
            }

            EditorGUILayout.LabelField(
                $"Блоков: {session.Level.BlockCount}, объектов: {session.Level.Objects.Count}, точек: {session.Level.Points.Count}",
                EditorStyles.miniLabel);
        }

        private void DrawHistorySection()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = session.History.CanUndo;
                if (GUILayout.Button("↶ " + (session.History.UndoName ?? "Отмена")))
                    Undo.PerformUndo();
                GUI.enabled = session.History.CanRedo;
                if (GUILayout.Button("↷ " + (session.History.RedoName ?? "Повтор")))
                    Undo.PerformRedo();
                GUI.enabled = true;
            }
        }

        private void DrawFloorSection()
        {
            EditorGUILayout.LabelField("Этажи", EditorStyles.boldLabel);
            var floors = session.Level.Floors;
            for (int i = floors.Count - 1; i >= 0; i--)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var f = floors[i];
                    if (GUILayout.Button(i == activeFloor ? "●" : "○", GUILayout.Width(24)))
                        SetFloor(i);
                    EditorGUILayout.LabelField($"{f.Name}  (низ Y = {f.BaseY})", i == activeFloor ? EditorStyles.boldLabel : EditorStyles.label);
                }
            }

            var active = floors[activeFloor];
            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.DelayedTextField("Имя", active.Name);
            var baseY = EditorGUILayout.DelayedIntField("Низ этажа (Y)", active.BaseY);
            if (EditorGUI.EndChangeCheck())
            {
                var after = active.Clone();
                after.Name = name;
                after.BaseY = activeFloor == 0 ? baseY : Mathf.Max(baseY, floors[activeFloor - 1].BaseY + 1);
                session.Execute(MarkupCommand.ChangeFloor(active, after));
                activeFloor = Mathf.Max(0, session.Level.Floors.ToList().FindIndex(f => f.Id == after.Id));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Этаж выше"))
                {
                    var top = floors[floors.Count - 1];
                    var maxY = session.Level.BlockCount > 0 ? session.Level.Bounds().max.Y : 0;
                    var ids = new HashSet<string>(floors.Select(f => f.Id));
                    var n = floors.Count;
                    while (ids.Contains("f" + n))
                        n++;
                    var floor = new Floor { Id = "f" + n, Name = "Этаж " + floors.Count, BaseY = Math.Max(top.BaseY + 4, maxY + 1) };
                    session.Execute(MarkupCommand.AddFloor(floor));
                    SetFloor(session.Level.Floors.Count - 1);
                    height = 0;
                }

                GUI.enabled = floors.Count > 1;
                if (GUILayout.Button("Удалить этаж") && EditorUtility.DisplayDialog("Удалить этаж",
                        $"Удалить этаж «{active.Name}»? Блоки останутся и отойдут нижнему этажу.", "Удалить", "Отмена"))
                {
                    session.Execute(MarkupCommand.RemoveFloor(active));
                    SetFloor(Mathf.Max(0, activeFloor - 1));
                }

                GUI.enabled = true;
            }

            EditorGUI.BeginChangeCheck();
            upperMode = (UpperFloorsMode)EditorGUILayout.Popup("Этажи выше", (int)upperMode, new[] { "Видны", "Полупрозрачные", "Скрыты" });
            darkenBelow = EditorGUILayout.Toggle("Затемнять нижние", darkenBelow);
            if (EditorGUI.EndChangeCheck())
            {
                ApplyFloorDisplay();
                SceneView.RepaintAll();
            }
        }

        private void DrawToolSection()
        {
            if (tool == Tool.Paint || tool == Tool.Erase)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    height = EditorGUILayout.IntField("Высота H", height);
                    if (GUILayout.Button("−", GUILayout.Width(24)))
                        height--;
                    if (GUILayout.Button("+", GUILayout.Width(24)))
                        height++;
                }

                EditorGUILayout.LabelField($"Абсолютная Y = {LayerY}", EditorStyles.miniLabel);
            }

            switch (tool)
            {
                case Tool.Paint:
                    shape = (BlockShape)EditorGUILayout.EnumPopup("Форма", shape);
                    if (BlockShapes.IsSlope(shape))
                        rotation = EditorGUILayout.IntPopup("Подъём к", rotation, RotationNames, new[] { 0, 1, 2, 3 });
                    DrawColorPicker(ref color, true);
                    brushSize = EditorGUILayout.IntSlider("Размер кисти", brushSize, 1, 9);
                    column = EditorGUILayout.Toggle(new GUIContent("Столбец", "Заполнить клетку от пола этажа до H"), column);
                    break;
                case Tool.Erase:
                    brushSize = EditorGUILayout.IntSlider("Размер кисти", brushSize, 1, 9);
                    EditorGUILayout.HelpBox("Ластик убирает блоки только на высоте H: ниже и выше остаются.", MessageType.None);
                    break;
                case Tool.Recolor:
                    DrawColorPicker(ref color, true);
                    brushSize = EditorGUILayout.IntSlider("Размер кисти", brushSize, 1, 9);
                    EditorGUILayout.HelpBox("Покраска меняет цвет стоящих блоков на высоте блока под курсором, форма не меняется.", MessageType.None);
                    break;
                case Tool.Objects:
                    objectKind = (LevelObjectKind)EditorGUILayout.EnumPopup("Вид", objectKind);
                    if (objectKind == LevelObjectKind.Barrier)
                    {
                        barrierKind = (BarrierKind)EditorGUILayout.EnumPopup("Тип барьера", barrierKind);
                        barrierHeight = EditorGUILayout.IntSlider("Высота (клеток)", barrierHeight, 1, 6);
                    }

                    if (objectKind == LevelObjectKind.EnemySpawn)
                        objectItem = DrawEnemyPopup(objectItem);
                    else
                    {
                        if (HasColor(objectKind))
                            DrawColorPicker(ref objectColor, false);
                        objectItem = EditorGUILayout.TextField(ItemLabel(objectKind), objectItem);
                    }

                    EditorGUILayout.HelpBox("ЛКМ по блоку — поставить объект сверху. ЛКМ по объекту — выбрать.", MessageType.None);
                    break;
                case Tool.Points:
                    pointAction = (InteractionAction)EditorGUILayout.EnumPopup("Действие", pointAction);
                    EditorGUILayout.HelpBox(
                        "ЛКМ по блоку — поставить точку A, следующий щелчок — цель: пустая клетка создаёт точку B, " +
                        "щелчок по точке или объекту связывает с ним. Esc — без цели.", MessageType.None);
                    break;
                case Tool.Select:
                    EditorGUILayout.HelpBox("ЛКМ — выбрать объект или точку.", MessageType.None);
                    break;
            }
        }

        private static bool HasColor(LevelObjectKind kind) =>
            kind == LevelObjectKind.Barrier || kind == LevelObjectKind.WeaponPedestal;

        /// <summary>Выбор типа врага из Enemies.json. Неизвестный id сохраняется и помечается.</summary>
        private string DrawEnemyPopup(string current)
        {
            var enemies = session.Enemies;
            if (enemies.Count == 0)
                return EditorGUILayout.TextField("Враг (тип)", current ?? "");
            var ids = enemies.Select(e => e.id).ToList();
            var labels = enemies.Select(e => $"{e.name} ({e.id})").ToList();
            var index = string.IsNullOrEmpty(current) ? 0 : ids.IndexOf(current);
            if (index < 0)
            {
                ids.Add(current);
                labels.Add(current + " — нет в Enemies.json");
                index = ids.Count - 1;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                index = EditorGUILayout.Popup("Враг (тип)", index, labels.ToArray());
                if (GUILayout.Button("↻", GUILayout.Width(24)))
                    session.LoadEnemies();
            }

            return ids[Mathf.Clamp(index, 0, ids.Count - 1)];
        }

        private static string ItemLabel(LevelObjectKind kind)
        {
            switch (kind)
            {
                case LevelObjectKind.WeaponPedestal: return "Оружие (id)";
                case LevelObjectKind.EnemySpawn: return "Враг (тип)";
                case LevelObjectKind.Npc: return "NPC (id)";
                case LevelObjectKind.Hint: return "Подсказка (действие)";
                default: return "Метка";
            }
        }

        private void DrawColorPicker(ref string value, bool allowWorld)
        {
            EditorGUILayout.LabelField("Цвет");
            const int perRow = 4;
            var entries = new List<(string id, string name, Color c)>();
            if (allowWorld)
            {
                var m = session.View.BaseMaterial;
                entries.Add((null, "Мир", m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white));
            }

            entries.AddRange(session.Palette);
            for (int i = 0; i < entries.Count; i += perRow)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int j = i; j < Mathf.Min(i + perRow, entries.Count); j++)
                    {
                        var e = entries[j];
                        GUI.backgroundColor = e.c;
                        if (GUILayout.Button(new GUIContent(value == e.id ? "● " + e.name : e.name, e.id ?? "цвет мира"), GUILayout.MinWidth(40)))
                            value = e.id;
                    }

                    GUI.backgroundColor = Color.white;
                }
            }
        }

        private void DrawSelectionSection()
        {
            var obj = selectedObject != null ? session.Level.FindObject(selectedObject) : null;
            var point = selectedPoint != null ? session.Level.FindPoint(selectedPoint) : null;
            if (obj == null && point == null)
                return;
            EditorGUILayout.LabelField("Выбрано", EditorStyles.boldLabel);
            if (obj != null)
                DrawObjectInspector(obj);
            if (point != null)
                DrawPointInspector(point);
        }

        private void DrawObjectInspector(LevelObject obj)
        {
            var after = obj.Clone();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Id", obj.Id);
            after.Kind = (LevelObjectKind)EditorGUILayout.EnumPopup("Вид", obj.Kind);
            after.Cell = ToInt3(EditorGUILayout.Vector3IntField("Клетка", ToV(obj.Cell)));
            after.Yaw = EditorGUILayout.IntPopup("Поворот", Mathf.RoundToInt(obj.Yaw), new[] { "0°", "90°", "180°", "270°" }, new[] { 0, 90, 180, 270 });
            if (after.Kind == LevelObjectKind.Barrier)
            {
                after.Barrier = (BarrierKind)EditorGUILayout.EnumPopup("Тип барьера", obj.Barrier);
                after.Height = EditorGUILayout.IntSlider("Высота", obj.Height, 1, 6);
            }

            if (after.Kind == LevelObjectKind.EnemySpawn)
            {
                // Цвет врага задаёт его тип в Enemies.json.
                after.Item = DrawEnemyPopup(obj.Item);
                after.Color = null;
            }
            else
            {
                var c = obj.Color;
                DrawColorPicker(ref c, true);
                after.Color = c;
                var item = EditorGUILayout.DelayedTextField(ItemLabel(after.Kind), obj.Item ?? "");
                after.Item = string.IsNullOrEmpty(item) ? null : item;
            }
            if (EditorGUI.EndChangeCheck())
                session.Execute(MarkupCommand.PutObject(obj, after));
            if (GUILayout.Button("Удалить объект"))
                DeleteSelection();
        }

        private void DrawPointInspector(InteractionPoint point)
        {
            var after = point.Clone();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField("Id", point.Id);
            after.Action = (InteractionAction)EditorGUILayout.EnumPopup("Действие", point.Action);
            after.Cell = ToInt3(EditorGUILayout.Vector3IntField("Клетка", ToV(point.Cell)));
            var item = EditorGUILayout.DelayedTextField("Условие: предмет", point.Condition.RequiredItem ?? "");
            after.Condition.RequiredItem = string.IsNullOrEmpty(item) ? null : item;
            after.Condition.RequiredOpen = IdPopup("Условие: открыт", point.Condition.RequiredOpen,
                session.Level.Objects.Where(o => o.IsBarrier).Select(o => o.Id));
            if (InteractionPoint.CreatesLink(after.Action))
            {
                after.TargetPoint = IdPopup("Точка B", point.TargetPoint, session.Level.Points.Where(p => p.Id != point.Id).Select(p => p.Id));
                after.TwoWay = EditorGUILayout.Toggle("В обе стороны", point.TwoWay);
            }

            after.TargetObject = IdPopup("Объект-цель", point.TargetObject, session.Level.Objects.Select(o => o.Id));
            after.TargetFloor = IdPopup("Этаж-цель", point.TargetFloor, session.Level.Floors.Select(f => f.Id));
            if (EditorGUI.EndChangeCheck())
                session.Execute(MarkupCommand.PutPoint(point, after));

            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.backgroundColor = pickingTarget ? new Color(0.6f, 1f, 0.6f) : Color.white;
                if (GUILayout.Button(pickingTarget ? "Щёлкните цель в сцене…" : "Указать цель в сцене"))
                {
                    pickingTarget = !pickingTarget;
                    tool = Tool.Points;
                }

                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Удалить точку"))
                    DeleteSelection();
            }
        }

        private static string IdPopup(string label, string value, IEnumerable<string> ids)
        {
            var list = new List<string> { "—" };
            list.AddRange(ids);
            var index = string.IsNullOrEmpty(value) ? 0 : list.IndexOf(value);
            if (index < 0)
            {
                list.Add(value + " (нет)");
                index = list.Count - 1;
            }

            var picked = EditorGUILayout.Popup(label, index, list.ToArray());
            if (picked == index)
                return value;
            return picked == 0 ? null : list[picked];
        }

        private void DrawNavSection()
        {
            EditorGUILayout.LabelField("Карта пути", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            showNav = EditorGUILayout.Toggle("Показать", showNav);
            var names = session.Profiles.Select(p => $"{p.Name} ({p.Id})").ToArray();
            profileIndex = Mathf.Clamp(EditorGUILayout.Popup("Профиль", profileIndex, names), 0, Mathf.Max(0, names.Length - 1));
            navEverything = EditorGUILayout.Toggle(new GUIContent("Всё открыто", "Все предметы есть, все барьеры открыты"), navEverything);
            navReachable = EditorGUILayout.Toggle(new GUIContent("Только достижимое", "Только клетки, достижимые от точки появления"), navReachable);
            if (EditorGUI.EndChangeCheck())
                SceneView.RepaintAll();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Пересобрать"))
                {
                    session.RebuildNav();
                    SceneView.RepaintAll();
                }

                if (session.Graph != null)
                    EditorGUILayout.LabelField($"узлов {session.Graph.Nodes.Count}, связей {session.Graph.LinkCount}", EditorStyles.miniLabel);
            }

            if (session.NavStale)
                EditorGUILayout.HelpBox("Карта устарела: пересоберите или сохраните уровень.", MessageType.Info);
            if (session.Graph != null)
            {
                foreach (var w in session.Graph.Warnings)
                    EditorGUILayout.HelpBox(w, MessageType.Warning);
            }

            EditorGUILayout.LabelField(
                "Зелёные клетки — проходимы, синие стрелки — прыжки, оранжевые — спрыгивания, сиреневые — особые связи (спуск на щите, лифт, крюк).",
                EditorStyles.wordWrappedMiniLabel);
        }

        // ------------------------------------------------------------------ действия

        private void LoadLevel(string key)
        {
            if (!ConfirmDiscard())
                return;
            session.Load(key);
            activeFloor = 0;
            selectedObject = selectedPoint = null;
            ApplyFloorDisplay(true);
        }

        private bool ConfirmDiscard() =>
            !session.Dirty || EditorUtility.DisplayDialog("Несохранённые изменения",
                $"Уровень {session.Key} изменён. Продолжить без сохранения?", "Продолжить", "Отмена");

        private void SetFloor(int index)
        {
            activeFloor = Mathf.Clamp(index, 0, session.Level.Floors.Count - 1);
            ApplyFloorDisplay();
            Repaint();
            SceneView.RepaintAll();
        }

        private void OpenEditingScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            session.EnsureView();
            ApplyFloorDisplay(true);
            FrameLevel();
        }

        private void FrameLevel()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || session.Level.BlockCount == 0)
                return;
            var (min, max) = session.Level.Bounds();
            var b = new Bounds();
            b.SetMinMax(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X + 1, max.Y + 1, max.Z + 1));
            sv.rotation = Quaternion.Euler(35f, 45f, 0f);
            sv.Frame(b, false);
        }

        private void DeleteSelection()
        {
            var obj = selectedObject != null ? session.Level.FindObject(selectedObject) : null;
            if (obj != null)
                session.Execute(MarkupCommand.RemoveObject(obj));
            var point = selectedPoint != null ? session.Level.FindPoint(selectedPoint) : null;
            if (point != null)
                session.Execute(MarkupCommand.RemovePoint(point));
            selectedObject = selectedPoint = null;
            pickingTarget = false;
            Repaint();
        }

        private static Vector3Int ToV(Int3 c) => new Vector3Int(c.X, c.Y, c.Z);
        private static Int3 ToInt3(Vector3Int v) => new Int3(v.x, v.y, v.z);
    }
}
