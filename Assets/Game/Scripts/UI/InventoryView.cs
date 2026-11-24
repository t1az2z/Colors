using System;
using System.Collections.Generic;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.UI;
using GameCore.DI;
using GameCore.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Вью экрана инвентаря (GDD, раздел 9): окно персонажа со слотами левой и правой руки и быстрыми слотами 1–4,
    /// сетка 5×5. Мышь: перетаскивание, ПКМ/двойной щелчок — надеть/снять. Геймпад: D-pad/стик — выбор, A — надеть/снять, B — закрыть.
    /// Слоты, куда можно надеть выбранный предмет, подсвечены зелёным. Вся логика — в <see cref="InventoryScreenViewModel"/>.
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        private const float Slot = 100f;
        private const float Gap = 12f;

        private InventoryScreenViewModel vm;
        private GameInput input;
        private Palette palette;
        private readonly Dictionary<SlotRef, SlotView> slots = new Dictionary<SlotRef, SlotView>();
        private RectTransform dragGhost;
        private RectTransform canvasRect;
        private Text description;
        private Text help;
        private Vector2 navHeld;
        private float navRepeat;

        public static GameObject Create(Canvas canvas, InventoryScreenViewModel vm, IResolver resolver)
        {
            var root = UiFactory.Rect(canvas.transform, "InventoryScreen", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.Stretch(root);
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0, 0, 0, 0.45f);
            var view = root.gameObject.AddComponent<InventoryView>();
            view.Build(canvas, vm, resolver);
            return root.gameObject;
        }

        private void Build(Canvas canvas, InventoryScreenViewModel model, IResolver resolver)
        {
            vm = model;
            input = resolver.Resolve<GameInput>();
            palette = resolver.Resolve<Palette>();
            canvasRect = (RectTransform)canvas.transform;

            var panel = UiFactory.Image(transform, "Panel", UiFactory.PanelColor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240, 720)).rectTransform;
            var topLeft = UiFactory.Rect(panel, "Content", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);

            Title(topLeft, "Персонаж", new Vector2(40, -22));
            Title(topLeft, "Инвентарь", new Vector2(660, -22));

            // Портрет: серый куб с глазами между слотами рук.
            var portrait = UiFactory.Image(topLeft, "Portrait", new Color(0.62f, 0.63f, 0.65f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(190, -120), new Vector2(200, 200));
            UiFactory.Image(portrait.transform, "EyeL", new Color(0.08f, 0.08f, 0.1f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-34, 24), new Vector2(20, 34));
            UiFactory.Image(portrait.transform, "EyeR", new Color(0.08f, 0.08f, 0.1f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(34, 24), new Vector2(20, 34));

            foreach (var slot in vm.Slots)
            {
                Vector2 pos;
                string caption = null;
                switch (slot.Kind)
                {
                    case SlotKind.Hand:
                        // Как в зеркале: правая рука персонажа — слева от портрета, левая — справа.
                        pos = slot.Hand == Hand.Right ? new Vector2(40, -170) : new Vector2(440, -170);
                        caption = slot.Hand == Hand.Left ? "Левая рука" : "Правая рука";
                        break;
                    case SlotKind.Quick:
                        pos = new Vector2(40 + slot.Index * (Slot + Gap), -400);
                        caption = (slot.Index + 1).ToString();
                        break;
                    default:
                        var (col, row) = vm.PositionOf(slot);
                        col -= InventoryScreenViewModel.GridColumnOffset;
                        pos = new Vector2(660 + col * (Slot + Gap), -70 - row * (Slot + Gap));
                        break;
                }

                var view = SlotView.Create(topLeft, slot, pos, Slot, caption, this);
                slots.Add(slot, view);
            }

            var quickTitle = UiFactory.Text(topLeft, "QuickTitle", "Предметы", 22, TextAnchor.LowerLeft, UiFactory.MutedText);
            quickTitle.rectTransform.anchorMin = quickTitle.rectTransform.anchorMax = new Vector2(0, 1);
            quickTitle.rectTransform.pivot = new Vector2(0, 0);
            quickTitle.rectTransform.anchoredPosition = new Vector2(40, -392);

            var stats = UiFactory.Text(topLeft, "Stats", "Уровень 1   ·   Очки навыков 0", 22, TextAnchor.UpperLeft, UiFactory.TextColor);
            stats.rectTransform.anchorMin = stats.rectTransform.anchorMax = new Vector2(0, 1);
            stats.rectTransform.pivot = new Vector2(0, 1);
            stats.rectTransform.anchoredPosition = new Vector2(40, -540);

            description = UiFactory.Text(topLeft, "Description", "", 24, TextAnchor.UpperLeft, UiFactory.TextColor);
            description.rectTransform.anchorMin = description.rectTransform.anchorMax = new Vector2(0, 1);
            description.rectTransform.pivot = new Vector2(0, 1);
            description.rectTransform.anchoredPosition = new Vector2(660, -640);

            help = UiFactory.Text(topLeft, "Help", "", 20, TextAnchor.UpperLeft, UiFactory.MutedText);
            help.rectTransform.anchorMin = help.rectTransform.anchorMax = new Vector2(0, 1);
            help.rectTransform.pivot = new Vector2(0, 1);
            help.rectTransform.anchoredPosition = new Vector2(40, -640);

            vm.Revision.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Highlights.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Cursor.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Held.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Description.Subscribe(t => description.text = t).DisposeWith(gameObject);
        }

        private static void Title(Transform parent, string text, Vector2 pos)
        {
            var t = UiFactory.Text(parent, "Title", text, 30, TextAnchor.UpperLeft, UiFactory.TextColor);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0, 1);
            t.rectTransform.pivot = new Vector2(0, 1);
            t.rectTransform.anchoredPosition = pos;
        }

        private void RefreshAll()
        {
            foreach (var pair in slots)
            {
                var item = vm.ItemAt(pair.Key);
                // Двуручное оружие показываем в правой руке, левая — "занята".
                var shadow = pair.Key == SlotRef.Of(Hand.Left) && item != null && item.Config.IsTwoHanded;
                pair.Value.SetItem(item, item != null ? palette.Get(item.Config.Color) : Color.clear, shadow);
                pair.Value.SetState(vm.IsHighlighted(pair.Key), vm.Cursor.Value == pair.Key && input.UsingGamepad,
                    vm.Held.Value.HasValue && vm.Held.Value.Value == pair.Key);
            }
        }

        private void Update()
        {
            if (vm == null || vm.IsDisposed)
                return;

            help.text = input.UsingGamepad
                ? "D-pad — выбор   ·   A — надеть / снять   ·   B — закрыть"
                : "Перетащите оружие в руку   ·   ПКМ — надеть / снять   ·   TAB — закрыть";

            var nav = input.NavigateVector;
            var dir = new Vector2(Mathf.Abs(nav.x) > 0.5f ? Mathf.Sign(nav.x) : 0f, Mathf.Abs(nav.y) > 0.5f ? Mathf.Sign(nav.y) : 0f);
            if (dir != Vector2.zero)
            {
                if (dir != navHeld)
                {
                    navHeld = dir;
                    navRepeat = 0.35f;
                    vm.Navigate((int)dir.x, -(int)dir.y);
                    RefreshAll();
                }
                else if ((navRepeat -= Time.unscaledDeltaTime) <= 0f)
                {
                    navRepeat = 0.12f;
                    vm.Navigate((int)dir.x, -(int)dir.y);
                    RefreshAll();
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

        // ------------------------------------------------------------------ события слотов

        internal void OnHover(SlotRef slot)
        {
            if (!vm.Held.Value.HasValue)
                vm.Hover(slot);
        }

        internal void OnBeginDrag(SlotRef slot, PointerEventData e)
        {
            var item = vm.ItemAt(slot);
            if (item == null || !vm.BeginDrag(slot))
                return;
            dragGhost = SlotView.CreateIcon(canvasRect, item, palette.Get(item.Config.Color), Slot * 0.9f);
            dragGhost.SetAsLastSibling();
            foreach (var g in dragGhost.GetComponentsInChildren<Graphic>())
                g.raycastTarget = false;
            MoveGhost(e);
        }

        internal void OnDrag(PointerEventData e) => MoveGhost(e);

        internal void OnEndDrag(PointerEventData e)
        {
            if (dragGhost != null)
                Destroy(dragGhost.gameObject);
            dragGhost = null;
            var target = e.pointerCurrentRaycast.gameObject != null
                ? e.pointerCurrentRaycast.gameObject.GetComponentInParent<SlotView>()
                : null;
            vm.Drop(target != null ? target.Slot : (SlotRef?)null);
            RefreshAll();
        }

        internal void OnQuickUse(SlotRef slot) => vm.QuickUse(slot);

        private void MoveGhost(PointerEventData e)
        {
            if (dragGhost == null)
                return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, e.position, null, out var local);
            dragGhost.anchoredPosition = local;
        }

        private void OnDestroy()
        {
            if (dragGhost != null)
                Destroy(dragGhost.gameObject);
        }
    }

    /// <summary>Один слот: фон, иконка, зелёная подсветка, рамка курсора геймпада.</summary>
    public sealed class SlotView : MonoBehaviour, IPointerEnterHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private InventoryView owner;
        private Image background;
        private GameObject highlight;
        private GameObject cursor;
        private RectTransform icon;
        private Item shown;
        private bool shownShadow;

        public SlotRef Slot { get; private set; }

        public static SlotView Create(Transform parent, SlotRef slot, Vector2 pos, float size, string caption, InventoryView owner)
        {
            var bg = UiFactory.Image(parent, slot.ToString(), UiFactory.SlotColor, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(size, size));
            var view = bg.gameObject.AddComponent<SlotView>();
            view.owner = owner;
            view.background = bg;
            view.Slot = slot;
            view.highlight = UiFactory.Frame(bg.transform, "Highlight", UiFactory.Highlight, 4f);
            view.highlight.SetActive(false);
            view.cursor = UiFactory.Frame(bg.transform, "Cursor", Color.white, 3f);
            view.cursor.SetActive(false);
            if (!string.IsNullOrEmpty(caption))
            {
                var t = UiFactory.Text(bg.transform, "Caption", caption, 18, TextAnchor.UpperCenter, UiFactory.MutedText);
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0);
                t.rectTransform.pivot = new Vector2(0.5f, 1);
                t.rectTransform.anchoredPosition = new Vector2(0, -6);
            }

            return view;
        }

        public void SetItem(Item item, Color color, bool shadow)
        {
            if (item == shown && shadow == shownShadow)
                return;
            shown = item;
            shownShadow = shadow;
            if (icon != null)
                Destroy(icon.gameObject);
            icon = null;
            if (item == null)
                return;
            icon = CreateIcon(transform, item, color, ((RectTransform)transform).sizeDelta.x * 0.85f);
            icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
            icon.anchoredPosition = Vector2.zero;
            if (shadow)
                foreach (var g in icon.GetComponentsInChildren<Graphic>())
                    g.color = new Color(g.color.r, g.color.g, g.color.b, 0.3f);
        }

        public void SetState(bool highlighted, bool underCursor, bool held)
        {
            highlight.SetActive(highlighted);
            cursor.SetActive(underCursor);
            background.color = held ? new Color(0.32f, 0.32f, 0.36f, 0.95f) : UiFactory.SlotColor;
            if (icon != null)
                icon.localScale = held ? Vector3.one * 0.85f : Vector3.one;
        }

        /// <summary>Простая векторная иконка предмета из прямоугольников: меч, щит или плитка.</summary>
        public static RectTransform CreateIcon(Transform parent, Item item, Color color, float size)
        {
            var root = UiFactory.Rect(parent, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            var dark = new Color(0.16f, 0.16f, 0.18f);
            var s = size / 100f;
            switch (item.Config.Kind)
            {
                case ItemKind.Shield:
                    UiFactory.Image(root, "Rim", dark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 2 * s), new Vector2(58 * s, 70 * s));
                    UiFactory.Image(root, "Face", color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 2 * s), new Vector2(46 * s, 58 * s));
                    UiFactory.Image(root, "Boss", dark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6 * s), new Vector2(12 * s, 12 * s));
                    break;
                case ItemKind.OneHanded:
                case ItemKind.TwoHanded:
                {
                    var blade = item.Config.IsTwoHanded ? 72f : 58f;
                    var sword = UiFactory.Rect(root, "Sword", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
                    sword.localRotation = Quaternion.Euler(0, 0, -45f);
                    UiFactory.Image(sword, "Blade", color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), new Vector2(0, -8 * s), new Vector2(12 * s, blade * s));
                    UiFactory.Image(sword, "Guard", dark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -10 * s), new Vector2(34 * s, 8 * s));
                    UiFactory.Image(sword, "Grip", dark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0, -12 * s), new Vector2(8 * s, 18 * s));
                    break;
                }
                default:
                    UiFactory.Image(root, "Tile", color, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(50 * s, 50 * s));
                    break;
            }

            foreach (var g in root.GetComponentsInChildren<Graphic>())
                g.raycastTarget = false;
            return root;
        }

        public void OnPointerEnter(PointerEventData e) => owner.OnHover(Slot);
        public void OnBeginDrag(PointerEventData e) => owner.OnBeginDrag(Slot, e);
        public void OnDrag(PointerEventData e) => owner.OnDrag(e);
        public void OnEndDrag(PointerEventData e) => owner.OnEndDrag(e);

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right || e.clickCount >= 2)
                owner.OnQuickUse(Slot);
            else
                owner.OnHover(Slot);
        }
    }
}
