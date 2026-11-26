using ColorsAction.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Ячейка HUD с предметом (часть префаба Resources/UI/Hud): кружок оружия в руке или слот крестовины предметов.
    /// Кружок оружия без предмета прячется целиком; слот крестовины остаётся и только бледнеет.
    /// </summary>
    public sealed class HudItemSlot : MonoBehaviour
    {
        [SerializeField] private Image background;
        [Tooltip("Кольцо цвета оружия (только у кружков оружия).")]
        [SerializeField] private Image ring;
        [SerializeField] private Image icon;
        [Tooltip("Подпись клавиши (у кружков оружия меняется по схеме ввода).")]
        [SerializeField] private Text key;
        [Tooltip("Прятать ячейку, когда предмета нет (кружки оружия).")]
        [SerializeField] private bool hideWhenEmpty;
        [SerializeField] private Color filledBackground = new Color(0, 0, 0, 0.55f);
        [SerializeField] private Color emptyBackground = new Color(0, 0, 0, 0.25f);

        public void Set(Item item, Color color, ItemIcons icons)
        {
            if (hideWhenEmpty)
                gameObject.SetActive(item != null);
            else if (background != null)
                background.color = item != null ? filledBackground : emptyBackground;
            if (ring != null)
                ring.color = item != null ? color : new Color(1f, 1f, 1f, 0.25f);
            if (icons != null)
                icons.Apply(icon, item, color);
            else if (icon != null)
                icon.enabled = false;
            SetCharges(item);
        }

        private Text charges;

        /// <summary>Заряды лечилки «1/2» в углу ячейки; пустая лечилка бледнеет.</summary>
        private void SetCharges(Item item)
        {
            var show = item != null && item.MaxCharges > 0;
            if (!show)
            {
                if (charges != null)
                    charges.gameObject.SetActive(false);
                return;
            }

            if (charges == null)
            {
                charges = UiFactory.Text(transform, "Charges", string.Empty, 18, TextAnchor.UpperLeft, UiFactory.TextColor);
                UiFactory.Stretch(charges.rectTransform, 4);
                UiFactory.Outline(charges);
            }

            charges.gameObject.SetActive(true);
            charges.text = $"{item.Charges}/{item.MaxCharges}";
            charges.color = item.Charges > 0 ? UiFactory.TextColor : UiFactory.MutedText;
            if (icon != null)
            {
                var c = icon.color;
                c.a = item.Charges > 0 ? 1f : 0.35f;
                icon.color = c;
            }
        }

        public void SetKey(string label)
        {
            if (key != null)
                key.text = label;
        }
    }
}
