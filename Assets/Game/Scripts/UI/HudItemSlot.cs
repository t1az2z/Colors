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
        }

        public void SetKey(string label)
        {
            if (key != null)
                key.text = label;
        }
    }
}
