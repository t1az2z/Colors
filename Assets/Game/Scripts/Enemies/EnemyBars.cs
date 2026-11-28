using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Полосы над врагом (макет Tools/UI/ref_enemy_bars.png): красная — здоровье (со светлым «хвостом» урона),
    /// под ней белая — стойкость (<see cref="ColorsAction.Combat.Poise"/>, её заполняют парирования; есть не у всех).
    /// Повёрнуты к камере. Видны, пока враг — цель фиксации, недавно получил урон или стойкость не пуста;
    /// трещины на теле остаются как были. Спрайты — <see cref="UiAtlas"/> (Resources/UI/UiAtlas).
    /// </summary>
    public sealed class EnemyBars : MonoBehaviour
    {
        /// <summary>Сколько секунд полосы видны после удара.</summary>
        private const float ShowAfterHit = 4f;

        // Размер рамок и смещение заливок внутри — в пикселях атласа (CROPS в Tools/UI/make_ui_sprites.py).
        private static readonly Vector2 HealthFrameSize = new Vector2(190, 44);
        private static readonly Vector2 HealthFillOffset = new Vector2(12.5f, 11f);
        private static readonly Vector2 HealthFillSize = new Vector2(165, 22);
        private static readonly Vector2 PoiseFrameSize = new Vector2(144, 43);
        private static readonly Vector2 PoiseFillOffset = new Vector2(11.5f, 12f);
        private static readonly Vector2 PoiseFillSize = new Vector2(121, 19);
        private const float PoiseOverlap = 10f;

        private static readonly Color PoiseColor = new Color(1f, 0.98f, 0.95f, 1f);
        private static readonly Color StunColor = new Color(1f, 0.9f, 0.55f, 1f);
        private static readonly Color LagColor = new Color(1f, 0.86f, 0.7f, 1f);

        private static PlayerLockOn lockOn;

        private RectTransform rect;
        private CanvasGroup group;
        private Image healthFill;
        private Image healthLag;
        private Image poiseFill;
        private GameObject poiseRoot;
        private Transform anchor;
        private float height;
        private float scale;

        private float health = 1f;
        private float healthShown = 1f;
        private float poise;
        private float poiseShown;
        private bool broken;
        private float hitTimer;
        private float alpha;
        private float flash;

        /// <param name="anchor">Враг: полосы висят над ним.</param>
        /// <param name="height">Высота над опорой врага, метры.</param>
        /// <param name="worldWidth">Ширина полосы здоровья в мире, метры.</param>
        /// <param name="hasPoise">Есть ли у врага стойкость (белая полоса).</param>
        public static EnemyBars Create(Transform anchor, float height, float worldWidth, bool hasPoise)
        {
            var atlas = UiAtlas.Instance;
            var go = new GameObject("EnemyBars", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 19;
            var bars = go.AddComponent<EnemyBars>();
            bars.group = go.AddComponent<CanvasGroup>();
            bars.group.alpha = 0f;
            bars.group.interactable = false;
            bars.group.blocksRaycasts = false;
            bars.rect = (RectTransform)go.transform;
            bars.rect.sizeDelta = new Vector2(HealthFrameSize.x, HealthFrameSize.y + PoiseFrameSize.y - PoiseOverlap);
            bars.anchor = anchor;
            bars.height = height;
            bars.scale = worldWidth / HealthFrameSize.x;

            var top = new Vector2(0.5f, 1f);
            // Белая полоса — под красной, рисуется раньше: красная рамка ложится поверх её верхнего края.
            var poiseFrame = Picture(go.transform, "Poise", atlas != null ? atlas.EnemyPoiseFrame : null, new Color(0.2f, 0.15f, 0.25f, 0.85f),
                top, new Vector2(0.5f, 1f), new Vector2(0f, -(HealthFrameSize.y - PoiseOverlap)), PoiseFrameSize);
            bars.poiseRoot = poiseFrame.gameObject;
            bars.poiseFill = Fill(poiseFrame.transform, "Fill", atlas != null ? atlas.EnemyPoiseFill : null, PoiseColor, PoiseFillOffset, PoiseFillSize);
            bars.poiseRoot.SetActive(hasPoise);

            var healthFrame = Picture(go.transform, "Health", atlas != null ? atlas.EnemyHealthFrame : null, new Color(0.2f, 0.15f, 0.25f, 0.85f),
                top, new Vector2(0.5f, 1f), Vector2.zero, HealthFrameSize);
            bars.healthLag = Fill(healthFrame.transform, "Lag", atlas != null ? atlas.EnemyPoiseFill : null, LagColor, HealthFillOffset, HealthFillSize);
            bars.healthFill = Fill(healthFrame.transform, "Fill", atlas != null ? atlas.EnemyHealthFill : null,
                atlas != null ? Color.white : new Color(0.9f, 0.25f, 0.25f), HealthFillOffset, HealthFillSize);
            return bars;
        }

        private static Image Picture(Transform parent, string name, Sprite sprite, Color fallback, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var image = UiFactory.Image(parent, name, sprite != null ? Color.white : fallback, anchor, pivot, pos, size);
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static Image Fill(Transform frame, string name, Sprite sprite, Color color, Vector2 offset, Vector2 size)
        {
            var image = UiFactory.Image(frame, name, color, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(offset.x, -offset.y), size);
            image.sprite = sprite;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.raycastTarget = false;
            return image;
        }

        /// <param name="healthFraction">Здоровье 0..1.</param>
        /// <param name="poiseFraction">Стойкость 0..1.</param>
        /// <param name="isBroken">Стойкость сломана — враг ошеломлён.</param>
        /// <param name="stunLeft">Пока ошеломлён: сколько оглушения осталось, 0..1 (белая полоса тает вместе с ним).</param>
        public void Set(float healthFraction, float poiseFraction, bool isBroken, float stunLeft)
        {
            healthFraction = Mathf.Clamp01(healthFraction);
            if (healthFraction < health - 0.0001f)
                hitTimer = ShowAfterHit;
            health = healthFraction;
            if (isBroken && !broken)
                flash = 0.25f;
            broken = isBroken;
            poise = Mathf.Clamp01(isBroken ? stunLeft : poiseFraction);
        }

        private bool IsLockTarget()
        {
            if (lockOn == null)
                lockOn = FindAnyObjectByType<PlayerLockOn>();
            var target = lockOn != null ? lockOn.Target : null;
            return target != null && anchor != null && target.transform == anchor;
        }

        private void LateUpdate()
        {
            if (anchor == null)
            {
                Destroy(gameObject);
                return;
            }

            var dt = Time.deltaTime;
            hitTimer = Mathf.Max(0f, hitTimer - dt);
            flash = Mathf.Max(0f, flash - dt);

            healthShown = health > healthShown ? health : Mathf.MoveTowards(healthShown, health, dt * 0.6f);
            poiseShown = poise > poiseShown ? Mathf.MoveTowards(poiseShown, poise, dt * 4f) : Mathf.MoveTowards(poiseShown, poise, dt * 1.5f);
            if (broken)
                poiseShown = poise;
            healthFill.fillAmount = health;
            healthFill.enabled = health > 0.001f;
            healthLag.fillAmount = healthShown;
            healthLag.enabled = healthShown > 0.001f;
            if (poiseRoot.activeSelf)
            {
                poiseFill.fillAmount = poiseShown;
                poiseFill.enabled = poiseShown > 0.001f;
                var color = broken ? StunColor : PoiseColor;
                poiseFill.color = Color.Lerp(color, Color.white, Mathf.Clamp01(flash / 0.25f));
            }

            var visible = health > 0f && (IsLockTarget() || hitTimer > 0f || broken || poiseShown > 0.001f || poise > 0.001f);
            alpha = Mathf.MoveTowards(alpha, visible ? 1f : 0f, dt * (visible ? 8f : 3f));
            group.alpha = alpha;

            rect.position = CurvedWorld.Bend(anchor.position + Vector3.up * height);
            var cam = Camera.main;
            if (cam != null)
                rect.rotation = cam.transform.rotation;
            // Стойкость сломана — полосы коротко вздрагивают.
            rect.localScale = Vector3.one * (scale * (1f + Mathf.Sin(Mathf.Clamp01(flash / 0.25f) * Mathf.PI) * 0.15f));
        }
    }
}
