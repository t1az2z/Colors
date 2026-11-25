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
    /// Вью экрана инвентаря (GDD, раздел 9): окно персонажа со слотами левой и правой руки и быстрыми слотами 1–4,
    /// сетка 5×5. Мышь: перетаскивание, ПКМ/двойной щелчок — надеть/снять. Геймпад: D-pad/стик — выбор, A — надеть/снять, B — закрыть.
    /// Слоты, куда можно надеть выбранный предмет, подсвечены зелёным. Вся логика — в <see cref="InventoryScreenViewModel"/>.
    /// Вёрстка — префаб Resources/UI/InventoryScreen (свой Canvas + CanvasScaler 1920×1080), слоты — из префаба InventorySlot.
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

        [Header("Раскладка слотов")]
        [SerializeField] private float slotSize = 100f;
        [SerializeField] private float gap = 12f;
        [Tooltip("Правая рука персонажа — слева от портрета (как в зеркале).")]
        [SerializeField] private Vector2 rightHandPosition = new Vector2(40, -170);
        [SerializeField] private Vector2 leftHandPosition = new Vector2(440, -170);
        [SerializeField] private Vector2 quickOrigin = new Vector2(40, -400);
        [SerializeField] private Vector2 gridOrigin = new Vector2(660, -70);

        private InventoryScreenViewModel vm;
        private GameInput input;
        private Palette palette;
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

        private void Bind(InventoryScreenViewModel model, IResolver resolver)
        {
            vm = model;
            input = resolver.Resolve<GameInput>();
            palette = resolver.Resolve<Palette>();
            if (dragGhost != null)
            {
                dragGhost.raycastTarget = false;
                dragGhost.gameObject.SetActive(false);
            }

            foreach (var slot in vm.Slots)
            {
                Vector2 pos;
                string caption = null;
                switch (slot.Kind)
                {
                    case SlotKind.Hand:
                        // Как в зеркале: правая рука персонажа — слева от портрета, левая — справа.
                        pos = slot.Hand == Hand.Right ? rightHandPosition : leftHandPosition;
                        caption = slot.Hand == Hand.Left ? "Левая рука" : "Правая рука";
                        break;
                    case SlotKind.Quick:
                        pos = quickOrigin + new Vector2(slot.Index * (slotSize + gap), 0f);
                        caption = (slot.Index + 1).ToString();
                        break;
                    default:
                        var (col, row) = vm.PositionOf(slot);
                        col -= InventoryScreenViewModel.GridColumnOffset;
                        pos = gridOrigin + new Vector2(col * (slotSize + gap), -row * (slotSize + gap));
                        break;
                }

                var view = Instantiate(slotPrefab, slotsRoot, false);
                view.Init(slot, pos, slotSize, caption, this);
                slots.Add(slot, view);
            }

            vm.Revision.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Highlights.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Cursor.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Held.Subscribe(_ => RefreshAll()).DisposeWith(gameObject);
            vm.Description.Subscribe(t => description.text = t).DisposeWith(gameObject);
        }

        private void RefreshAll()
        {
            foreach (var pair in slots)
            {
                var item = vm.ItemAt(pair.Key);
                // Двуручное оружие показываем в правой руке, левая — "занята".
                var shadow = pair.Key == SlotRef.Of(Hand.Left) && item != null && item.Config.IsTwoHanded;
                pair.Value.SetItem(item, item != null ? palette.Get(item.Config.Color) : Color.clear, shadow, icons);
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

        internal void OnQuickUse(SlotRef slot) => vm.QuickUse(slot);

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
