using ColorsAction.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ColorsAction.Unity
{
    /// <summary>Ячейка апгрейда в меню костра: фон по состоянию, название, цена, рамка курсора.</summary>
    public sealed class BonfireCell : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        [SerializeField] private int tier = 1;
        [SerializeField] private UnityEngine.UI.Image background;
        [SerializeField] private UnityEngine.UI.Text label;
        [SerializeField] private UnityEngine.UI.Text state;
        [SerializeField] private UnityEngine.UI.Outline cursor;

        private BonfireView owner;

        public int Tier => tier;
        public UpgradeCell Cell { get; private set; }

        public void Setup(int tierNumber, UnityEngine.UI.Image bg, UnityEngine.UI.Text labelText, UnityEngine.UI.Text stateText, UnityEngine.UI.Outline frame)
        {
            tier = tierNumber;
            background = bg;
            label = labelText;
            state = stateText;
            cursor = frame;
        }

        public void Init(BonfireView view, UpgradeCell cell)
        {
            owner = view;
            Cell = cell;
        }

        public void Show(string title, string stateText, Color color, bool bright, bool selected)
        {
            background.color = color;
            label.text = title;
            label.color = bright ? UiFactory.TextColor : UiFactory.MutedText;
            state.text = stateText;
            cursor.enabled = selected;
        }

        public void OnPointerEnter(PointerEventData eventData) => owner?.OnHover(Cell);

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
                owner?.OnClick(Cell);
        }
    }
}
