using ColorsAction.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>Один слот (префаб Resources/UI/InventorySlot): фон, иконка, зелёная подсветка, рамка курсора геймпада.</summary>
    public sealed class SlotView : MonoBehaviour, IPointerEnterHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        [SerializeField] private Image background;
        [Tooltip("Зелёная рамка: сюда можно надеть выбранный предмет.")]
        [SerializeField] private GameObject highlight;
        [Tooltip("Белая рамка курсора геймпада.")]
        [SerializeField] private GameObject cursor;
        [SerializeField] private Image icon;
        [SerializeField] private Text caption;
        [SerializeField] private Color normalColor = new Color(0.22f, 0.22f, 0.25f, 0.95f);
        [SerializeField] private Color heldColor = new Color(0.32f, 0.32f, 0.36f, 0.95f);

        private InventoryView owner;
        private Item shown;
        private bool shownShadow;
        private bool hasShown;

        public SlotRef Slot { get; private set; }

        public void Init(SlotRef slot, Vector2 pos, float size, string captionText, InventoryView view)
        {
            owner = view;
            Slot = slot;
            name = slot.ToString();
            var rect = (RectTransform)transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(size, size);
            highlight.SetActive(false);
            cursor.SetActive(false);
            if (caption != null)
            {
                caption.text = captionText ?? string.Empty;
                caption.gameObject.SetActive(!string.IsNullOrEmpty(captionText));
            }

            if (icon != null)
            {
                icon.raycastTarget = false;
                icon.enabled = false;
            }
        }

        public void SetItem(Item item, Color color, bool shadow, ItemIcons icons)
        {
            if (hasShown && item == shown && shadow == shownShadow)
                return;
            hasShown = true;
            shown = item;
            shownShadow = shadow;
            if (icons != null)
                icons.Apply(icon, item, color, shadow ? 0.3f : 1f);
        }

        public void SetState(bool highlighted, bool underCursor, bool held)
        {
            highlight.SetActive(highlighted);
            cursor.SetActive(underCursor);
            background.color = held ? heldColor : normalColor;
            if (icon != null)
                icon.rectTransform.localScale = held ? Vector3.one * 0.85f : Vector3.one;
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
