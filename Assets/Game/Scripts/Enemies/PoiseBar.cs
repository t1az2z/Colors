using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Шкала выносливости врага (<see cref="ColorsAction.Combat.Poise"/>) над головой: тонкая полоска, повёрнутая
    /// к камере. Пустая — скрыта. Растёт от бледно-жёлтого к оранжево-красному; заполнилась — вспыхивает белым и,
    /// пока враг ошеломлён, тает вместе с оглушением.
    /// </summary>
    public sealed class PoiseBar : MonoBehaviour
    {
        private const float Width = 100f;
        private const float Height = 9f;
        private const float Border = 2f;

        private static readonly Color Low = new Color(1f, 0.86f, 0.42f, 1f);
        private static readonly Color High = new Color(1f, 0.36f, 0.12f, 1f);
        private static readonly Color Stun = new Color(1f, 0.95f, 0.75f, 1f);

        private RectTransform rect;
        private RectTransform fillRect;
        private Image fill;
        private CanvasGroup group;
        private Transform anchor;
        private float height;
        private float scale;

        private float target;
        private bool broken;
        private float shown;
        private float alpha;
        private float flash;

        /// <param name="anchor">Враг: шкала висит над ним.</param>
        /// <param name="height">Высота над опорой врага, метры.</param>
        /// <param name="worldWidth">Ширина шкалы в мире, метры.</param>
        public static PoiseBar Create(Transform anchor, float height, float worldWidth)
        {
            var go = new GameObject("PoiseBar", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 19;
            var bar = go.AddComponent<PoiseBar>();
            bar.group = go.AddComponent<CanvasGroup>();
            bar.group.alpha = 0f;
            bar.group.interactable = false;
            bar.group.blocksRaycasts = false;
            bar.rect = (RectTransform)go.transform;
            bar.rect.sizeDelta = new Vector2(Width + Border * 2f, Height + Border * 2f);
            bar.anchor = anchor;
            bar.height = height;
            bar.scale = worldWidth / (Width + Border * 2f);

            var back = UiFactory.Image(go.transform, "Back", new Color(0.08f, 0.08f, 0.1f, 0.8f), Vector2.zero, Vector2.zero,
                Vector2.zero, Vector2.zero);
            UiFactory.Stretch(back.rectTransform);
            bar.fill = UiFactory.Image(back.transform, "Fill", Low, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(Border, 0f), new Vector2(0f, Height));
            bar.fillRect = bar.fill.rectTransform;
            return bar;
        }

        /// <param name="fraction">Заполнение 0..1.</param>
        /// <param name="isBroken">Шкала заполнена — враг ошеломлён.</param>
        /// <param name="stunLeft">Пока ошеломлён: сколько оглушения осталось, 0..1 (шкала тает вместе с ним).</param>
        public void Set(float fraction, bool isBroken, float stunLeft)
        {
            if (isBroken && !broken)
                flash = 0.25f;
            broken = isBroken;
            target = Mathf.Clamp01(isBroken ? stunLeft : fraction);
        }

        private void LateUpdate()
        {
            if (anchor == null)
            {
                Destroy(gameObject);
                return;
            }

            var dt = Time.deltaTime;
            // Рост — быстро (видно, сколько дал удар), спад — плавно.
            shown = target > shown ? Mathf.MoveTowards(shown, target, dt * 4f) : Mathf.MoveTowards(shown, target, dt * 1.5f);
            if (broken)
                shown = target;
            var visible = broken || shown > 0.001f || target > 0.001f;
            alpha = Mathf.MoveTowards(alpha, visible ? 1f : 0f, dt * (visible ? 8f : 3f));
            group.alpha = alpha;
            flash = Mathf.Max(0f, flash - dt);

            fillRect.sizeDelta = new Vector2(Width * shown, Height);
            var color = broken ? Stun : Color.Lerp(Low, High, shown);
            fill.color = Color.Lerp(color, Color.white, Mathf.Clamp01(flash / 0.25f));

            rect.position = CurvedWorld.Bend(anchor.position + Vector3.up * height);
            var cam = Camera.main;
            if (cam != null)
                rect.rotation = cam.transform.rotation;
            // Заполнилась — коротко вздрагивает.
            rect.localScale = Vector3.one * (scale * (1f + Mathf.Sin(Mathf.Clamp01(flash / 0.25f) * Mathf.PI) * 0.15f));
        }
    }
}
