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
    /// Вёрстка — префаб Resources/UI/Hud (Canvas + CanvasScaler 1920×1080), его создаёт <see cref="GameBootstrap"/>.
    /// Ширина полос берётся из префаба (размер Fill/Lag при полном значении).
    /// </summary>
    public sealed class Hud : GameBehaviour
    {
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private ItemIcons icons;

        [Header("Здоровье и выносливость")]
        [SerializeField] private Image healthFill;
        [SerializeField] private Image healthLag;
        [SerializeField] private Text healthText;
        [SerializeField] private Image staminaFill;
        [SerializeField] private Image staminaLag;

        [Header("Тексты")]
        [SerializeField] private Text xpText;
        [SerializeField] private Text prompt;
        [SerializeField] private Text toast;
        [SerializeField] private Text schemeLabel;

        [Header("Оружие и предметы")]
        [SerializeField] private HudItemSlot primaryWeapon;
        [SerializeField] private HudItemSlot secondaryWeapon;
        [Tooltip("Крестовина предметов 1–4: видна, только когда есть предметы.")]
        [SerializeField] private GameObject quickCross;
        [SerializeField] private HudItemSlot[] quick = new HudItemSlot[4];

        [Header("Цифры урона")]
        [SerializeField] private RectTransform numbersRoot;
        [SerializeField] private Text damageNumberPrefab;
        [SerializeField] private int damageFontSize = 34;
        [SerializeField] private int bigDamageFontSize = 46;

        private GameSession session;
        private GameInput input;
        private Palette palette;
        private IEventBus events;
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();

        private bool started;
        private float healthWidth;
        private float healthHeight;
        private float staminaWidth;
        private float staminaHeight;
        private float toastTimer;
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
            if (session == null)
                return;
            if (interactor == null)
                interactor = FindAnyObjectByType<PlayerInteractor>();

            var hp = healthFill.rectTransform.sizeDelta;
            healthWidth = hp.x;
            healthHeight = hp.y;
            var st = staminaFill.rectTransform.sizeDelta;
            staminaWidth = st.x;
            staminaHeight = st.y;
            toast.text = string.Empty;
            started = true;

            subscriptions.Add(session.ControlScheme.Subscribe(scheme =>
                ShowToast(scheme == ColorsAction.Configs.CameraScheme.FixedAngles
                    ? "Схема Б: 4 угла камеры, взгляд — мышь / правый стик"
                    : "Схема А: свободная камера"), false));

            subscriptions.Add(events.Subscribe<ItemPickedUp>(e =>
                ShowToast($"{e.Item.Config.Name} в инвентаре  ·  [{input.Label("inventory")}] открыть")));
            subscriptions.Add(events.Subscribe<InventoryFull>(_ => ShowToast("Инвентарь полон")));
            subscriptions.Add(events.Subscribe<DamageDealt>(OnDamage));
            subscriptions.Add(session.Vitals.Health.Current.Subscribe(_ => healthShake = 0.35f, false));
            session.Inventory.Changed += RefreshHands;
            RefreshHands();
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
                var slot = i == (int)Hand.Right ? primaryWeapon : secondaryWeapon;
                if (slot != null)
                    slot.Set(item, item != null ? palette.Get(item.Config.Color) : palette.Get("none"), icons);
            }
        }

        private void Update()
        {
            // session == null — после перекомпиляции во время игры зависимости не восстанавливаются.
            if (!started || session == null)
                return;
            var dt = Time.unscaledDeltaTime;

            // Здоровье: заполнение сразу, светлый "хвост" догоняет; при уроне полоса дрожит.
            var health = session.Vitals.Health;
            var hp = health.Fraction;
            healthFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, healthWidth * hp), healthHeight);
            healthFill.enabled = hp > 0.001f;
            healthShown = hp > healthShown ? hp : Mathf.MoveTowards(healthShown, hp, dt * 0.5f);
            healthLag.rectTransform.sizeDelta = new Vector2(healthWidth * healthShown, healthHeight);
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
            staminaFill.rectTransform.sizeDelta = new Vector2(staminaWidth * fraction, staminaHeight);
            staminaFill.enabled = fraction > 0.001f;
            staminaShown = fraction > staminaShown ? fraction : Mathf.MoveTowards(staminaShown, fraction, dt * 0.6f);
            staminaLag.rectTransform.sizeDelta = new Vector2(staminaWidth * staminaShown, staminaHeight);
            staminaLag.enabled = staminaShown > 0.001f;

            xpText.text = $"Опыт  {session.Experience.Value}";

            if (primaryWeapon != null)
                primaryWeapon.SetKey(input.Label("attack"));
            if (secondaryWeapon != null)
                secondaryWeapon.SetKey(input.Label("secondary"));

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
            if (started && healthFill != null)
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
                if (quick[i] != null)
                    quick[i].Set(item, item != null ? palette.Get(item.Config.Color) : Color.clear, icons);
            }

            quickCross.SetActive(any);
        }

        // ------------------------------------------------------------------ цифры урона

        private void OnDamage(DamageDealt e)
        {
            if (damageNumberPrefab == null || numbersRoot == null)
                return;
            var color = e.ToPlayer ? new Color(1f, 0.35f, 0.3f) : e.ColorBonus ? Color.Lerp(e.Color, Color.white, 0.25f) : UiFactory.TextColor;
            var text = Instantiate(damageNumberPrefab, numbersRoot, false);
            text.gameObject.SetActive(true);
            text.text = Mathf.RoundToInt(e.Amount).ToString();
            text.fontSize = e.ColorBonus || e.ToPlayer ? bigDamageFontSize : damageFontSize;
            text.color = color;
            text.raycastTarget = false;
            if (e.ColorBonus)
                text.text += "!";
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
                RectTransformUtility.ScreenPointToLocalPointInRectangle(numbersRoot, screen, null, out var local);
                // Точка относительно пивота контейнера: якорь цифры совпадает с пивотом.
                n.Text.rectTransform.anchorMin = n.Text.rectTransform.anchorMax = numbersRoot.pivot;
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
