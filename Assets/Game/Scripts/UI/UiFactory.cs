using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>Построение uGUI из кода (как SampleViews шаблона): канвас, панели, тексты.</summary>
    public static class UiFactory
    {
        private static Canvas canvas;
        private static Font font;

        public static readonly Color PanelColor = new Color(0.1f, 0.1f, 0.12f, 0.95f);
        public static readonly Color SlotColor = new Color(0.22f, 0.22f, 0.25f, 0.95f);
        public static readonly Color TextColor = new Color(0.93f, 0.93f, 0.9f, 1f);
        public static readonly Color MutedText = new Color(0.65f, 0.66f, 0.7f, 1f);
        public static readonly Color Highlight = new Color(0.3f, 0.95f, 0.4f, 1f);

        public static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static Canvas EnsureCanvas(Transform parent)
        {
            if (canvas != null)
                return canvas;

            var go = new GameObject("UI", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();

            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(parent, false);
            }

            return canvas;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        public static Image Image(Transform parent, string name, Color color, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = Rect(parent, name, anchor, pivot, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text Text(Transform parent, string name, string value, int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.text = value;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>Рамка из четырёх полос: подсветка слота.</summary>
        public static GameObject Frame(Transform parent, string name, Color color, float thickness)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            Stretch(rect, -thickness);
            void Bar(string n, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
            {
                var r = new GameObject(n, typeof(RectTransform)).GetComponent<RectTransform>();
                r.SetParent(rect, false);
                r.anchorMin = min;
                r.anchorMax = max;
                r.offsetMin = offMin;
                r.offsetMax = offMax;
                var img = r.gameObject.AddComponent<Image>();
                img.color = color;
                img.raycastTarget = false;
            }

            Bar("Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -thickness), Vector2.zero);
            Bar("Bottom", new Vector2(0, 0), new Vector2(1, 0), Vector2.zero, new Vector2(0, thickness));
            Bar("Left", new Vector2(0, 0), new Vector2(0, 1), Vector2.zero, new Vector2(thickness, 0));
            Bar("Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-thickness, 0), Vector2.zero);
            return root;
        }

        public static void Outline(Text text)
        {
            var o = text.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.8f);
            o.effectDistance = new Vector2(2, -2);
        }
    }
}
