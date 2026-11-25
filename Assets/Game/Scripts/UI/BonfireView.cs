using System.Collections.Generic;
using ColorsAction.Progression;
using ColorsAction.UI;
using GameCore.DI;
using GameCore.Unity;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Меню костра (префаб Resources/UI/BonfireMenu со своим Canvas + CanvasScaler 1920×1080): оружие игрока строками,
    /// по 3 апгрейда в строке. Геймпад: D-pad/стик — выбор, A — изучить, B — уйти от костра.
    /// Мышь: наведение — описание, щелчок — изучить; Esc — уйти. Логика — <see cref="BonfireScreenViewModel"/>.
    /// Префаб собирает <c>ColorsAction → UI → Build Bonfire Menu Prefab</c>; пока его нет, меню строится кодом
    /// (<see cref="BuildDefault"/>, та же раскладка).
    /// </summary>
    public sealed class BonfireView : MonoBehaviour
    {
        public const string Resource = "UI/BonfireMenu";

        [SerializeField] private RectTransform rowsRoot;
        [Tooltip("Строка-образец (выключена): Name (Text), Level (Text), Tier1..Tier3 (BonfireCell).")]
        [SerializeField] private GameObject rowTemplate;
        [SerializeField] private UnityEngine.UI.Text experience;
        [SerializeField] private UnityEngine.UI.Text description;
        [SerializeField] private UnityEngine.UI.Text message;
        [SerializeField] private UnityEngine.UI.Text help;
        [SerializeField] private float rowHeight = 130f;

        [SerializeField] private Color ownedColor = new Color(0.32f, 0.55f, 0.36f, 0.95f);
        [SerializeField] private Color availableColor = new Color(0.36f, 0.36f, 0.41f, 0.95f);
        [SerializeField] private Color expensiveColor = new Color(0.25f, 0.25f, 0.28f, 0.95f);
        [SerializeField] private Color lockedColor = new Color(0.17f, 0.17f, 0.19f, 0.95f);

        private BonfireScreenViewModel vm;
        private GameInput input;
        private Palette palette;
        private readonly List<BonfireCell> cells = new List<BonfireCell>();
        private Vector2 navHeld;
        private float navRepeat;

        /// <summary>Создать меню из префаба (null — построить кодом) и привязать к модели.</summary>
        public static GameObject Create(BonfireView prefab, Transform parent, BonfireScreenViewModel vm, IResolver resolver)
        {
            BonfireView view;
            if (prefab != null)
            {
                view = Instantiate(prefab, parent, false);
            }
            else
            {
                view = BuildDefault();
                view.transform.SetParent(parent, false);
            }

            view.name = "BonfireMenu";
            view.Bind(vm, resolver);
            return view.gameObject;
        }

        private void Bind(BonfireScreenViewModel model, IResolver resolver)
        {
            vm = model;
            input = resolver.Resolve<GameInput>();
            palette = resolver.Resolve<Palette>();
            rowTemplate.SetActive(false);

            for (int r = 0; r < vm.Rows.Count; r++)
            {
                var row = vm.Rows[r];
                var go = Instantiate(rowTemplate, rowsRoot, false);
                go.name = "Row_" + row.ItemId;
                go.SetActive(true);
                var rect = (RectTransform)go.transform;
                rect.anchoredPosition = new Vector2(0f, -r * rowHeight);
                var title = go.transform.Find("Name")?.GetComponent<UnityEngine.UI.Text>();
                if (title != null)
                {
                    title.text = row.Name;
                    title.color = Color.Lerp(palette.Get(row.Color), Color.white, 0.35f);
                }

                foreach (var cell in go.GetComponentsInChildren<BonfireCell>(true))
                {
                    cell.Init(this, new UpgradeCell(r, cell.Tier));
                    cells.Add(cell);
                }
            }

            vm.Revision.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Cursor.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Description.Subscribe(t => description.text = t).DisposeWith(gameObject);
            vm.Message.Subscribe(t => message.text = t).DisposeWith(gameObject);
            vm.Experience.Subscribe(xp => experience.text = $"Опыт  {xp}").DisposeWith(gameObject);
        }

        private void RefreshAll()
        {
            if (vm == null || vm.IsDisposed)
                return;
            foreach (var cell in cells)
            {
                var tier = vm.TierConfig(cell.Cell);
                var status = vm.Status(cell.Cell);
                Color color;
                string state;
                switch (status)
                {
                    case UpgradeTierStatus.Owned: color = ownedColor; state = "изучено"; break;
                    case UpgradeTierStatus.Available: color = availableColor; state = $"{tier?.Cost} опыта"; break;
                    case UpgradeTierStatus.TooExpensive: color = expensiveColor; state = $"{tier?.Cost} опыта"; break;
                    default: color = lockedColor; state = "закрыто"; break;
                }

                cell.Show($"{cell.Tier}. {tier?.Name}", state, color, status == UpgradeTierStatus.Owned || status == UpgradeTierStatus.Available,
                    vm.Cursor.Value.Equals(cell.Cell));
            }
        }

        private void Update()
        {
            if (vm == null || vm.IsDisposed)
                return;

            help.text = input.UsingGamepad
                ? "D-pad — выбор   ·   A — изучить   ·   B — уйти от костра"
                : "Стрелки / мышь — выбор   ·   Enter / щелчок — изучить   ·   Esc — уйти от костра";

            var nav = input.NavigateVector;
            var dir = new Vector2(Mathf.Abs(nav.x) > 0.5f ? Mathf.Sign(nav.x) : 0f, Mathf.Abs(nav.y) > 0.5f ? Mathf.Sign(nav.y) : 0f);
            if (dir != Vector2.zero)
            {
                if (dir != navHeld)
                {
                    navHeld = dir;
                    navRepeat = 0.35f;
                    vm.Navigate((int)dir.x, -(int)dir.y);
                }
                else if ((navRepeat -= Time.unscaledDeltaTime) <= 0f)
                {
                    navRepeat = 0.12f;
                    vm.Navigate((int)dir.x, -(int)dir.y);
                }
            }
            else
            {
                navHeld = Vector2.zero;
            }

            if (input.Pressed(GameAction.Submit))
                vm.Confirm();
            else if (input.Pressed(GameAction.Cancel))
                vm.Cancel();
        }

        internal void OnHover(UpgradeCell cell) => vm?.Hover(cell);

        internal void OnClick(UpgradeCell cell) => vm?.Select(cell);

        // ------------------------------------------------------------------ раскладка кодом

        /// <summary>
        /// Меню целиком из кода: Canvas (Screen Space Overlay) + CanvasScaler (Scale With Screen Size, 1920×1080) +
        /// GraphicRaycaster, затемнение, панель, заголовок, опыт, список строк, описание, подсказки.
        /// Используется сборщиком префаба в редакторе и как запасной вариант, если префаба нет.
        /// </summary>
        public static BonfireView BuildDefault()
        {
            var go = new GameObject("BonfireMenu", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var dim = UiFactory.Image(go.transform, "Dim", new Color(0.05f, 0.03f, 0.02f, 0.55f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            UiFactory.Stretch(dim.rectTransform);
            dim.raycastTarget = true; // мышь не кликает сквозь меню

            var panel = UiFactory.Image(go.transform, "Panel", UiFactory.PanelColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 760)).rectTransform;
            var content = UiFactory.Rect(panel, "Content", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);

            var title = UiFactory.Text(content, "Title", "Костёр", 40, TextAnchor.UpperLeft, new Color(1f, 0.7f, 0.35f));
            Place(title.rectTransform, new Vector2(40, -24));
            var subtitle = UiFactory.Text(content, "Subtitle", "Здоровье восстановлено, враги вернулись. Улучшения оружия:", 22, TextAnchor.UpperLeft, UiFactory.MutedText);
            Place(subtitle.rectTransform, new Vector2(40, -80));

            var view = go.AddComponent<BonfireView>();
            view.experience = UiFactory.Text(content, "Experience", "Опыт  0", 28, TextAnchor.UpperRight, UiFactory.TextColor);
            view.experience.rectTransform.anchorMin = view.experience.rectTransform.anchorMax = new Vector2(0, 1);
            view.experience.rectTransform.pivot = new Vector2(1, 1);
            view.experience.rectTransform.anchoredPosition = new Vector2(1200, -30);

            view.rowsRoot = UiFactory.Rect(content, "Rows", new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -130), new Vector2(1160, 400));
            view.rowTemplate = BuildRow(view.rowsRoot);

            view.description = UiFactory.Text(content, "Description", "", 24, TextAnchor.UpperLeft, UiFactory.TextColor);
            Place(view.description.rectTransform, new Vector2(40, -540));
            view.message = UiFactory.Text(content, "Message", "", 24, TextAnchor.UpperLeft, new Color(1f, 0.75f, 0.4f));
            Place(view.message.rectTransform, new Vector2(40, -650));
            view.help = UiFactory.Text(content, "Help", "", 20, TextAnchor.UpperLeft, UiFactory.MutedText);
            Place(view.help.rectTransform, new Vector2(40, -700));
            return view;
        }

        private static GameObject BuildRow(RectTransform parent)
        {
            var row = UiFactory.Rect(parent, "RowTemplate", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(1160, 116));
            var name = UiFactory.Text(row, "Name", "Оружие", 28, TextAnchor.MiddleLeft, UiFactory.TextColor);
            name.rectTransform.anchorMin = name.rectTransform.anchorMax = new Vector2(0, 0.5f);
            name.rectTransform.pivot = new Vector2(0, 0.5f);
            name.rectTransform.anchoredPosition = new Vector2(0, 0);
            for (int t = 1; t <= WeaponUpgrades.MaxTier; t++)
            {
                var bg = UiFactory.Image(row, "Tier" + t, new Color(0.3f, 0.3f, 0.34f, 0.95f), new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(220 + (t - 1) * 315, 0), new Vector2(300, 116));
                bg.raycastTarget = true;
                var outline = bg.gameObject.AddComponent<UnityEngine.UI.Outline>();
                outline.effectColor = new Color(1f, 0.75f, 0.35f, 1f);
                outline.effectDistance = new Vector2(4, -4);
                var label = UiFactory.Text(bg.transform, "Label", "", 21, TextAnchor.UpperLeft, UiFactory.TextColor);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                UiFactory.Stretch(label.rectTransform, 12f);
                var state = UiFactory.Text(bg.transform, "State", "", 19, TextAnchor.LowerRight, UiFactory.MutedText);
                UiFactory.Stretch(state.rectTransform, 12f);
                var cell = bg.gameObject.AddComponent<BonfireCell>();
                cell.Setup(t, bg, label, state, outline);
            }

            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        private static void Place(RectTransform rect, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position;
        }
    }
}
