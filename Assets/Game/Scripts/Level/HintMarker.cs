using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Подсказка, "выгравированная" на полу: проявляется, когда игрок рядом; клавиша подстраивается под устройство ввода.
    /// Подсказка предмета или двери (<see cref="BindTargets"/>) гаснет навсегда, когда предмет подобран / дверь открыта.
    /// </summary>
    public sealed class HintMarker : MonoBehaviour
    {
        private const float ShowDistance = 4.5f;

        private string action;
        private GameInput input;
        private Text label;
        private CanvasGroup group;
        private Transform player;
        private bool gamepad;
        private string verbOverride;
        private System.Func<bool> done;
        private System.Action completed;
        private bool finished;

        /// <summary>
        /// Привязать подсказку к делу (подобрать предмет, открыть дверь): когда <paramref name="isDone"/> вернёт true,
        /// подсказка гаснет навсегда и вызывает <paramref name="onDone"/> (запомнить в сессии).
        /// </summary>
        public void BindTargets(System.Func<bool> isDone, System.Action onDone, string verb = null)
        {
            done = isDone;
            completed = onDone;
            verbOverride = verb;
            Refresh();
        }

        public static HintMarker Create(Transform parent, Vector3 position, string action, GameInput input, Font font)
        {
            var go = new GameObject($"Hint_{action}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(400, 120);
            rect.position = position + Vector3.up * 0.02f;
            rect.rotation = Quaternion.Euler(90f, 0f, 0f);
            rect.localScale = Vector3.one * 0.006f;

            var marker = go.AddComponent<HintMarker>();
            marker.group = go.AddComponent<CanvasGroup>();
            marker.group.alpha = 0f;
            marker.action = action;
            marker.input = input;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            marker.label = textGo.AddComponent<Text>();
            marker.label.font = font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            marker.label.fontSize = 64;
            marker.label.alignment = TextAnchor.MiddleCenter;
            marker.label.color = new Color(0.32f, 0.33f, 0.37f, 1f);
            marker.label.fontStyle = FontStyle.Bold;
            marker.label.horizontalOverflow = HorizontalWrapMode.Overflow;
            marker.label.verticalOverflow = VerticalWrapMode.Overflow;
            marker.Refresh();
            return marker;
        }

        private void Refresh()
        {
            gamepad = input != null && input.UsingGamepad;
            var key = input != null ? input.Label(action) : action;
            string verb;
            switch (action)
            {
                case "jump": verb = "прыжок"; break;
                case "roll": verb = "перекат"; break;
                case "interact": verb = "взять"; break;
                case "inventory": verb = "инвентарь"; break;
                case "attack": verb = "удар"; break;
                default: verb = string.Empty; break;
            }

            if (!string.IsNullOrEmpty(verbOverride))
                verb = verbOverride;
            label.text = $"[{key}]  {verb}";
        }

        private void Update()
        {
            if (player == null)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                if (pc == null)
                    return;
                player = pc.transform;
            }

            if (input != null && input.UsingGamepad != gamepad)
                Refresh();

            if (!finished && done != null && done())
            {
                finished = true;
                completed?.Invoke();
            }

            var d = Vector3.Distance(player.position, transform.position);
            var target = finished ? 0f : Mathf.Clamp01((ShowDistance - d) / 1.5f);
            group.alpha = Mathf.MoveTowards(group.alpha, target, Time.deltaTime * 2f);
            // Дело сделано и подсказка погасла — больше не появится.
            if (finished && group.alpha <= 0f)
                gameObject.SetActive(false);
        }
    }
}
