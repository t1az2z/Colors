using System;
using System.Collections.Generic;
using ColorsAction.Inventory;
using GameCore.DI;
using GameCore.Events;
using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>
    /// HUD: слева вверху — здоровье (скруглённая полоса с цифрами) и выносливость под ним, опыт;
    /// справа вверху — оружие в руках кружками (основное — правая рука, дополнительное — левая);
    /// подсказка взаимодействия, всплывающие сообщения и цифры урона.
    /// </summary>
    public sealed class Hud : GameBehaviour
    {
        private const float HealthWidth = 420f;
        private const float StaminaWidth = 300f;

        [SerializeField] private PlayerInteractor interactor;

        private GameSession session;
        private GameInput input;
        private Palette palette;
        private IEventBus events;
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();

        private RectTransform root;
        private Image healthFill;
        private Image healthLag;
        private Text healthText;
        private Image staminaFill;
        private Image staminaLag;
        private Text xpText;
        private Text prompt;
        private Text toast;
        private Text schemeLabel;
        private float toastTimer;
        private readonly WeaponCircle[] weapons = new WeaponCircle[2];
        private RectTransform quickCross;
        private readonly QuickSlotView[] quick = new QuickSlotView[4];
        private readonly List<FloatingNumber> numbers = new List<FloatingNumber>();
        private float staminaShown = 1f;
        private float healthShown = 1f;
        private float healthShake;

        protected override void OnConstruct(IResolver resolver)
        {
            session = resolver.Resolve<GameSession>();
            input = resolver.Resolve<GameInput>();
            palette = resolver.Resolve<Palette>();
            events = resolver.Resolve<IEventBus>();
        }

        private void Start()
        {
            var canvas = UiFactory.EnsureCanvas(transform);
            root = UiFactory.Rect(canvas.transform, "HUD", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            UiFactory.Stretch(root);
            root.SetAsFirstSibling();

            // Здоровье (слева вверху): скруглённая полоса, "хвост" урона и цифры поверх.
            var topLeft = new Vector2(0, 1);
            var hpBack = Bar(root, "Health", topLeft, new Vector2(36, -34), new Vector2(HealthWidth, 34), new Color(0, 0, 0, 0.55f));
            healthLag = Bar(hpBack.transform, "Lag", new Vector2(0, 0.5f), new Vector2(3, 0), new Vector2(HealthWidth - 6, 28), new Color(1f, 0.85f, 0.55f, 0.95f));
            healthFill = Bar(hpBack.transform, "Fill", new Vector2(0, 0.5f), new Vector2(3, 0), new Vector2(HealthWidth - 6, 28), new Color(0.88f, 0.27f, 0.25f, 1f));
            healthText = UiFactory.Text(hpBack.transform, "Value", "", 22, TextAnchor.MiddleCenter, Color.white);
            UiFactory.Stretch(healthText.rectTransform);
            UiFactory.Outline(healthText);

            // Выносливость — под здоровьем, тоньше.
            var stBack = Bar(root, "Stamina", topLeft, new Vector2(36, -76), new Vector2(StaminaWidth, 16), new Color(0, 0, 0, 0.5f));
            staminaLag = Bar(stBack.transform, "Lag", new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(StaminaWidth - 4, 12), new Color(1f, 0.6f, 0.3f, 0.9f));
            staminaFill = Bar(stBack.transform, "Fill", new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(StaminaWidth - 4, 12), new Color(0.93f, 0.93f, 0.86f, 1f));

            xpText = UiFactory.Text(root, "Experience", "", 20, TextAnchor.UpperLeft, UiFactory.TextColor);
            xpText.rectTransform.anchorMin = xpText.rectTransform.anchorMax = topLeft;
            xpText.rectTransform.pivot = new Vector2(0, 1);
            xpText.rectTransform.anchoredPosition = new Vector2(38, -100);
            UiFactory.Outline(xpText);

            // Оружие (справа вверху): правая рука — основное, крупнее; левая — дополнительное.
            weapons[(int)Hand.Right] = WeaponCircle.Create(root, new Vector2(-36, -32), 104f, "Primary");
            weapons[(int)Hand.Left] = WeaponCircle.Create(root, new Vector2(-36 - 104 - 18, -44), 80f, "Secondary");

            // Предметы на крестовине (1–4 / D-pad): слева внизу крестом, видны, только когда есть предметы.
            quickCross = UiFactory.Rect(root, "QuickItems", new Vector2(0, 0), new Vector2(0.5f, 0.5f), new Vector2(130, 130), new Vector2(220, 220));
            var offsets = new[] { new Vector2(0, 70), new Vector2(70, 0), new Vector2(0, -70), new Vector2(-70, 0) };
            for (int i = 0; i < quick.Length; i++)
                quick[i] = QuickSlotView.Create(quickCross, offsets[i], i);

            prompt = UiFactory.Text(root, "Prompt", "", 28, TextAnchor.MiddleCenter, UiFactory.TextColor);
            prompt.rectTransform.anchorMin = prompt.rectTransform.anchorMax = new Vector2(0.5f, 0);
            prompt.rectTransform.anchoredPosition = new Vector2(0, 150);
            UiFactory.Outline(prompt);

            schemeLabel = UiFactory.Text(root, "Scheme", "", 20, TextAnchor.LowerRight, UiFactory.MutedText);
            schemeLabel.rectTransform.anchorMin = schemeLabel.rectTransform.anchorMax = new Vector2(1, 0);
            schemeLabel.rectTransform.pivot = new Vector2(1, 0);
            schemeLabel.rectTransform.anchoredPosition = new Vector2(-40, 40);
            UiFactory.Outline(schemeLabel);
            subscriptions.Add(session.ControlScheme.Subscribe(scheme =>
                ShowToast(scheme == ColorsAction.Configs.CameraScheme.FixedAngles
                    ? "Схема Б: 4 угла камеры, взгляд — мышь / правый стик"
                    : "Схема А: свободная камера"), false));

            toast = UiFactory.Text(root, "Toast", "", 30, TextAnchor.MiddleCenter, UiFactory.TextColor);
            toast.rectTransform.anchorMin = toast.rectTransform.anchorMax = new Vector2(0.5f, 1);
            toast.rectTransform.anchoredPosition = new Vector2(0, -90);
            UiFactory.Outline(toast);

            subscriptions.Add(events.Subscribe<ItemPickedUp>(e =>
                ShowToast($"{e.Item.Config.Name} в инвентаре  ·  [{input.Label("inventory")}] открыть")));
            subscriptions.Add(events.Subscribe<InventoryFull>(_ => ShowToast("Инвентарь полон")));
            subscriptions.Add(events.Subscribe<DamageDealt>(OnDamage));
            subscriptions.Add(session.Vitals.Health.Current.Subscribe(_ => healthShake = 0.35f, false));
            session.Inventory.Changed += RefreshHands;
            RefreshHands();
        }

        private static Image Bar(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var pivot = anchor.x <= 0f ? new Vector2(0, anchor.y >= 1f ? 1 : 0.5f) : new Vector2(0.5f, 0.5f);
            var image = UiFactory.Image(parent, name, color, anchor, pivot, pos, size);
            UiSprites.Apply(image, UiSprites.Rounded, true);
            image.raycastTarget = false;
            return image;
        }

        private void OnDestroy()
        {
            foreach (var s in subscriptions)
                s.Dispose();
            subscriptions.Clear();
            if (session != null)
                session.Inventory.Changed -= RefreshHands;
        }

        private void ShowToast(string text)
        {
            toast.text = text;
            toastTimer = 3f;
        }

        private void RefreshHands()
        {
            RefreshQuick();
            for (int i = 0; i < 2; i++)
            {
                var item = session.Inventory.InHand((Hand)i);
                // Двуручное оружие показываем только в основном кружке.
                if (i == (int)Hand.Left && item != null && item.Config.IsTwoHanded)
                    item = null;
                weapons[i].Set(item, item != null ? palette.Get(item.Config.Color) : palette.Get("none"));
            }
        }

        private void Update()
        {
            // session == null — после перекомпиляции во время игры зависимости не восстанавливаются.
            if (root == null || session == null)
                return;
            var dt = Time.unscaledDeltaTime;

            // Здоровье: заполнение сразу, светлый "хвост" догоняет; при уроне полоса дрожит.
            var health = session.Vitals.Health;
            var hp = health.Fraction;
            healthFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, (HealthWidth - 6) * hp), 28);
            healthFill.enabled = hp > 0.001f;
            healthShown = hp > healthShown ? hp : Mathf.MoveTowards(healthShown, hp, dt * 0.5f);
            healthLag.rectTransform.sizeDelta = new Vector2((HealthWidth - 6) * healthShown, 28);
            healthLag.enabled = healthShown > 0.001f;
            healthText.text = $"{Mathf.CeilToInt(health.Current.Value)} / {Mathf.RoundToInt(health.Max)}";
            if (healthShake > 0f)
            {
                healthShake -= dt;
                var k = Mathf.Clamp01(healthShake / 0.35f);
                healthFill.transform.parent.localPosition += new Vector3(UnityEngine.Random.Range(-4f, 4f) * k, 0f, 0f);
            }

            // Выносливость.
            var fraction = session.Stamina.Fraction;
            staminaFill.rectTransform.sizeDelta = new Vector2((StaminaWidth - 4) * fraction, 12);
            staminaFill.enabled = fraction > 0.001f;
            staminaShown = fraction > staminaShown ? fraction : Mathf.MoveTowards(staminaShown, fraction, dt * 0.6f);
            staminaLag.rectTransform.sizeDelta = new Vector2((StaminaWidth - 4) * staminaShown, 12);
            staminaLag.enabled = staminaShown > 0.001f;

            xpText.text = $"Опыт  {session.Experience.Value}";

            weapons[(int)Hand.Right].SetKey(input.Label("attack"));
            weapons[(int)Hand.Left].SetKey(input.Label("secondary"));

            schemeLabel.text = session.ControlScheme.Value == ColorsAction.Configs.CameraScheme.FixedAngles
                ? $"Схема Б  ·  [{input.Label("cameraTurn")}] поворот камеры  ·  [{input.Label("lockOn")}] фиксация  ·  [{input.Label("switchScheme")}] схема А"
                : $"Схема А  ·  [{input.Label("lockOn")}] фиксация  ·  [{input.Label("switchScheme")}] схема Б";

            var target = interactor != null ? interactor.Target : null;
            prompt.text = target == null ? string.Empty
                : interactor.TargetUsable ? $"[{input.Label("interact")}]  {target.Prompt}"
                : target.BlockedPrompt;

            if (toastTimer > 0f)
            {
                toastTimer -= dt;
                var c = toast.color;
                c.a = Mathf.Clamp01(toastTimer / 0.5f);
                toast.color = c;
            }

            UpdateNumbers(dt);
        }

        private void LateUpdate()
        {
            // Возврат полосы здоровья на место после дрожания.
            if (healthFill != null)
                healthFill.transform.parent.localPosition = Vector3.Lerp(healthFill.transform.parent.localPosition,
                    BarHome, 0.5f);
        }

        private Vector3 barHome;
        private bool barHomeSet;

        private Vector3 BarHome
        {
            get
            {
                if (!barHomeSet && healthFill != null)
                {
                    barHome = healthFill.transform.parent.localPosition;
                    barHomeSet = true;
                }

                return barHome;
            }
        }

        private void RefreshQuick()
        {
            if (quickCross == null)
                return;
            var inv = session.Inventory;
            var any = false;
            for (int i = 0; i < quick.Length; i++)
            {
                var item = i < inv.QuickCount ? inv.Get(SlotRef.Quick(i)) : null;
                any |= item != null;
                quick[i].Set(item, item != null ? palette.Get(item.Config.Color) : Color.clear);
            }

            quickCross.gameObject.SetActive(any);
        }

        /// <summary>Ячейка крестовины предметов: плашка, иконка, номер клавиши.</summary>
        private sealed class QuickSlotView
        {
            private RectTransform rect;
            private RectTransform icon;
            private Image back;

            public static QuickSlotView Create(Transform parent, Vector2 pos, int index)
            {
                var v = new QuickSlotView();
                v.back = UiFactory.Image(parent, "Quick" + (index + 1), new Color(0, 0, 0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(64, 64));
                UiSprites.Apply(v.back, UiSprites.Rounded, true);
                v.back.raycastTarget = false;
                v.rect = v.back.rectTransform;
                var label = UiFactory.Text(v.rect, "Key", (index + 1).ToString(), 16, TextAnchor.LowerRight, UiFactory.MutedText);
                UiFactory.Stretch(label.rectTransform, 4);
                UiFactory.Outline(label);
                return v;
            }

            public void Set(Item item, Color color)
            {
                if (icon != null)
                    UnityEngine.Object.Destroy(icon.gameObject);
                icon = null;
                back.color = item != null ? new Color(0, 0, 0, 0.55f) : new Color(0, 0, 0, 0.25f);
                if (item == null)
                    return;
                icon = SlotView.CreateIcon(rect, item, color, 52f);
                icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
                icon.anchoredPosition = Vector2.zero;
            }
        }

        /// <summary>Кружок оружия: тёмный фон, кольцо цвета оружия, иконка, клавиша под ним.</summary>
        private sealed class WeaponCircle
        {
            private RectTransform rootRect;
            private Image ring;
            private RectTransform icon;
            private Text key;
            private float size;

            public static WeaponCircle Create(Transform parent, Vector2 pos, float size, string name)
            {
                var c = new WeaponCircle { size = size };
                var bg = UiFactory.Image(parent, name, new Color(0, 0, 0, 0.5f), new Vector2(1, 1), new Vector2(1, 1), pos, new Vector2(size, size));
                UiSprites.Apply(bg, UiSprites.Circle, false);
                bg.raycastTarget = false;
                c.rootRect = bg.rectTransform;
                c.ring = UiFactory.Image(bg.transform, "Ring", Color.gray, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
                UiSprites.Apply(c.ring, UiSprites.Ring, false);
                c.ring.raycastTarget = false;
                c.key = UiFactory.Text(bg.transform, "Key", "", 18, TextAnchor.UpperCenter, UiFactory.TextColor);
                c.key.rectTransform.anchorMin = c.key.rectTransform.anchorMax = new Vector2(0.5f, 0);
                c.key.rectTransform.pivot = new Vector2(0.5f, 1);
                c.key.rectTransform.anchoredPosition = new Vector2(0, -4);
                UiFactory.Outline(c.key);
                return c;
            }

            public void Set(Item item, Color color)
            {
                if (icon != null)
                    UnityEngine.Object.Destroy(icon.gameObject);
                icon = null;
                // Без оружия кружок не показываем.
                rootRect.gameObject.SetActive(item != null);
                ring.color = item != null ? color : new Color(1f, 1f, 1f, 0.25f);
                if (item == null)
                    return;
                icon = SlotView.CreateIcon(rootRect, item, color, size * 0.78f);
                icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 0.5f);
                icon.anchoredPosition = Vector2.zero;
            }

            public void SetKey(string label) => key.text = label;
        }

        // ------------------------------------------------------------------ цифры урона

        private void OnDamage(DamageDealt e)
        {
            var color = e.ToPlayer ? new Color(1f, 0.35f, 0.3f) : e.ColorBonus ? Color.Lerp(e.Color, Color.white, 0.25f) : UiFactory.TextColor;
            var text = UiFactory.Text(root, "Damage", Mathf.RoundToInt(e.Amount).ToString(), e.ColorBonus || e.ToPlayer ? 46 : 34,
                TextAnchor.MiddleCenter, color);
            if (e.ColorBonus)
                text.text += "!";
            UiFactory.Outline(text);
            numbers.Add(new FloatingNumber
            {
                Text = text,
                World = e.Position + new Vector3(UnityEngine.Random.Range(-0.25f, 0.25f), 0f, UnityEngine.Random.Range(-0.25f, 0.25f)),
                Time = 0f
            });
        }

        private void UpdateNumbers(float dt)
        {
            var cam = Camera.main;
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                var n = numbers[i];
                n.Time += dt;
                const float life = 0.9f;
                if (n.Time >= life || cam == null)
                {
                    Destroy(n.Text.gameObject);
                    numbers.RemoveAt(i);
                    continue;
                }

                var world = n.World + Vector3.up * (n.Time * 1.2f);
                var screen = cam.WorldToScreenPoint(world);
                n.Text.enabled = screen.z > 0;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
                n.Text.rectTransform.anchorMin = n.Text.rectTransform.anchorMax = Vector2.zero;
                n.Text.rectTransform.anchoredPosition = local;
                var pop = n.Time < 0.1f ? 1f + (0.1f - n.Time) * 4f : 1f;
                n.Text.rectTransform.localScale = Vector3.one * pop;
                var c = n.Text.color;
                c.a = Mathf.Clamp01((life - n.Time) / 0.3f);
                n.Text.color = c;
                numbers[i] = n;
            }
        }

        private struct FloatingNumber
        {
            public Text Text;
            public Vector3 World;
            public float Time;
        }
    }
}
