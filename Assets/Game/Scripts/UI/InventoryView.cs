using System.Collections.Generic;
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
    /// Вью экрана инвентаря (GDD v0.3, раздел 9): слева — 3D-превью персонажа с оружием в руках (правый стик или
    /// перетаскивание мышью крутят его) и крестовина предметов 1–4, как в HUD (↑ → ↓ ←); справа — сетка предметов.
    /// Оружие здесь не меняется — только колесом. Мышь: перетаскивание, ПКМ/двойной щелчок — в слот 1–4 и обратно.
    /// Геймпад: D-pad/левый стик — выбор, A — взять/положить, B — закрыть. Вся логика — в <see cref="InventoryScreenViewModel"/>.
    /// Вёрстка — префаб Resources/UI/InventoryScreen (свой Canvas + CanvasScaler 1920×1080), слоты — из префаба InventorySlot.
    /// Превью и подпись оружия, если их нет в префабе (старый префаб с портретом), создаются здесь же.
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        [SerializeField] private ItemIcons icons;
        [Tooltip("Префаб одного слота (Resources/UI/InventorySlot).")]
        [SerializeField] private SlotView slotPrefab;
        [Tooltip("Куда ставить слоты; координаты ниже — от его левого верхнего угла.")]
        [SerializeField] private RectTransform slotsRoot;
        [Tooltip("Иконка, которая тянется за мышью при перетаскивании (последний дочерний объект корня).")]
        [SerializeField] private Image dragGhost;
        [SerializeField] private Text description;
        [SerializeField] private Text help;
        [Tooltip("Картинка 3D-превью персонажа (пусто — создаётся в previewPosition).")]
        [SerializeField] private RawImage previewImage;
        [Tooltip("Подпись под превью: оружие в руках и уровень прокачки (пусто — создаётся).")]
        [SerializeField] private Text weaponCaption;

        [Header("Раскладка")]
        [SerializeField] private float slotSize = 100f;
        [SerializeField] private float gap = 12f;
        [SerializeField] private Vector2 previewPosition = new Vector2(40, -70);
        [SerializeField] private Vector2 previewSize = new Vector2(260, 300);
        [Tooltip("Центр крестовины предметов 1–4.")]
        [SerializeField] private Vector2 crossCenter = new Vector2(470, -220);
        [SerializeField] private float crossSlotSize = 88f;
        [SerializeField] private float crossGap = 28f;
        [SerializeField] private Vector2 gridOrigin = new Vector2(660, -70);

        private InventoryScreenViewModel vm;
        private GameInput input;
        private InputGlyphs glyphs;
        private PromptLine helpLine;
        private Palette palette;
        private CharacterPreview preview;
        private readonly Dictionary<SlotRef, SlotView> slots = new Dictionary<SlotRef, SlotView>();
        private bool dragging;
        private Vector2 navHeld;
        private float navRepeat;

        /// <summary>Создать экран из префаба и привязать к модели.</summary>
        public static GameObject Create(InventoryView prefab, Transform parent, InventoryScreenViewModel vm, IResolver resolver)
        {
            var view = Instantiate(prefab, parent, false);
            view.name = "InventoryScreen";
            view.Bind(vm, resolver);
            return view.gameObject;
        }

        /// <summary>Превью персонажа (для автотестов).</summary>
        public CharacterPreview Preview => preview;

        private void Bind(InventoryScreenViewModel model, IResolver resolver)
        {
            vm = model;
            input = resolver.Resolve<GameInput>();
            glyphs = resolver.Resolve<InputGlyphs>();
            palette = resolver.Resolve<Palette>();
            if (dragGhost != null)
            {
                dragGhost.raycastTarget = false;
                dragGhost.gameObject.SetActive(false);
            }

            BuildCharacterPanel();

            foreach (var slot in vm.Slots)
            {
                Vector2 pos;
                float size;
                string caption = null;
                if (slot.Kind == SlotKind.Quick)
                {
                    var (col, row) = vm.PositionOf(slot);
                    // Клетки крестовины вокруг её центра — клетки (1, 4) раскладки модели.
                    var step = crossSlotSize + crossGap;
                    var center = crossCenter + new Vector2((col - 1) * step, -(row - 4) * step);
                    pos = center + new Vector2(-crossSlotSize * 0.5f, crossSlotSize * 0.5f);
                    size = crossSlotSize;
                    caption = (slot.Index + 1).ToString();
                }
                else
                {
                    var (col, row) = vm.PositionOf(slot);
                    col -= InventoryScreenViewModel.GridColumnOffset;
                    pos = gridOrigin + new Vector2(col * (slotSize + gap), -row * (slotSize + gap));
                    size = slotSize;
                }

                var view = Instantiate(slotPrefab, slotsRoot, false);
                view.Init(slot, pos, size, caption, this);
                slots.Add(slot, view);
            }

            vm.Revision.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Highlights.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Cursor.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Held.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Description.Subscribe(t => description.text = t).DisposeWith(gameObject);
            vm.WeaponCaption.Subscribe(t => weaponCaption.text = t).DisposeWith(gameObject);
        }

        /// <summary>Превью и подпись оружия; от старого префаба прячутся портрет, слоты рук и строка статов.</summary>
        private void BuildCharacterPanel()
        {
            var content = slotsRoot != null ? slotsRoot.parent : transform;
            foreach (var legacy in new[] { "Portrait", "QuickTitle" })
            {
                var t = content.Find(legacy);
                if (t != null)
                    t.gameObject.SetActive(false);
            }

            if (previewImage == null)
            {
                var rect = UiFactory.Rect(content, "CharacterPreview", new Vector2(0, 1), new Vector2(0, 1), previewPosition, previewSize);
                previewImage = rect.gameObject.AddComponent<RawImage>();
                rect.SetSiblingIndex(slotsRoot != null ? slotsRoot.GetSiblingIndex() : 0);
            }

            if (weaponCaption == null)
            {
                // Строка статов старого префаба («Уровень 1 · Очки навыков 0») становится подписью оружия.
                var stats = content.Find("Stats");
                weaponCaption = stats != null ? stats.GetComponent<Text>() : null;
                if (weaponCaption == null)
                    weaponCaption = UiFactory.Text(content, "WeaponCaption", string.Empty, 22, TextAnchor.UpperLeft, UiFactory.TextColor);
                var r = weaponCaption.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0, 1);
                r.pivot = new Vector2(0, 1);
                r.sizeDelta = new Vector2(560, 32);
                r.anchoredPosition = previewPosition + new Vector2(0, -previewSize.y - 12);
            }

            preview = CharacterPreview.Attach(previewImage);
        }

        private void RefreshAll()
        {
            foreach (var pair in slots)
            {
                var item = vm.ItemAt(pair.Key);
                pair.Value.SetItem(item, item != null ? palette.Get(item.Config.Color) : Color.clear, false, icons);
                pair.Value.SetState(vm.IsHighlighted(pair.Key), vm.Cursor.Value == pair.Key && input.UsingGamepad,
                    vm.Held.Value.HasValue && vm.Held.Value.Value == pair.Key);
            }
        }

        private void Update()
        {
            if (vm == null || vm.IsDisposed)
                return;

            if (helpLine == null && help != null && glyphs != null)
                helpLine = PromptLine.Replace(help, glyphs);
            helpLine?.SetText(glyphs.Strings.Get(input.UsingGamepad ? "inventory.help.gamepad" : "inventory.help.keyboard"));

            if (preview != null)
                preview.Rotate(input.PreviewStick.x, Time.unscaledDeltaTime);

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
            dragging = true;
            if (dragGhost != null)
            {
                dragGhost.gameObject.SetActive(true);
                dragGhost.rectTransform.SetAsLastSibling();
                icons.Apply(dragGhost, item, palette.Get(item.Config.Color));
            }

            MoveGhost(e);
        }

        internal void OnDrag(PointerEventData e) => MoveGhost(e);

        internal void OnEndDrag(PointerEventData e)
        {
            if (dragGhost != null)
                dragGhost.gameObject.SetActive(false);
            if (!dragging)
                return;
            dragging = false;
            var target = e.pointerCurrentRaycast.gameObject != null
                ? e.pointerCurrentRaycast.gameObject.GetComponentInParent<SlotView>()
                : null;
            vm.Drop(target != null ? target.Slot : (SlotRef?)null);
            RefreshAll();
        }

        internal void OnQuickUse(SlotRef slot)
        {
            vm.QuickUse(slot);
            RefreshAll();
        }

        private void MoveGhost(PointerEventData e)
        {
            if (dragGhost == null || !dragging)
                return;
            var parent = (RectTransform)dragGhost.rectTransform.parent;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, null, out var local);
            dragGhost.rectTransform.anchoredPosition = local;
        }
    }
}
