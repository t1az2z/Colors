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
    /// справа вверху — активное оружие кружком (подпись — кнопка колеса оружия), без оружия кружка нет;
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
        [SerializeField] private int weakDamageFontSize = 26;
        [SerializeField] private Color weakDamageColor = new Color(0.62f, 0.63f, 0.66f);

        /// <summary>Иконки предметов (их берёт и колесо оружия).</summary>
        public ItemIcons Icons => icons;

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
                ShowToast(e.Item.Config.IsWeapon
                    ? $"{e.Item.Config.Name} на колесе  ·  [{input.Label("wheel")}] сменить оружие"
                    : $"{e.Item.Config.Name} в инвентаре  ·  [{input.Label("inventory")}] открыть")));
            subscriptions.Add(events.Subscribe<InventoryFull>(_ => ShowToast("Инвентарь полон")));
            subscriptions.Add(events.Subscribe<ResourcesGained>(e =>
                ShowToast($"{e.Item.Name} +{e.Count}  (всего {e.Total})")));
            subscriptions.Add(events.Subscribe<DamageDealt>(OnDamage));
            subscriptions.Add(events.Subscribe<QuickItemUsed>(OnQuickItemUsed));
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
            // Активное оружие — один кружок справа вверху (без оружия прячется); второго кружка рук больше нет.
            var item = session.Inventory.Active;
            if (primaryWeapon != null)
                primaryWeapon.Set(item, item != null ? palette.Get(item.Config.Color) : palette.Get("none"), icons);
            if (secondaryWeapon != null)
                secondaryWeapon.gameObject.SetActive(false);
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

            // Подпись кружка — кнопка колеса: им оружие и меняется.
            if (primaryWeapon != null)
                primaryWeapon.SetKey(input.Label("wheel"));

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

        private PlayerController healTarget;

        /// <summary>Быстрый слот: лечилка — зелёная цифра «+N» над игроком; пустая — подсказка про костёр.</summary>
        private void OnQuickItemUsed(QuickItemUsed e)
        {
            switch (e.Result)
            {
                case QuickUseResult.Empty:
                    ShowToast($"{e.Item.Config.Name}: заряды кончились  ·  отдохните у костра");
                    return;
                case QuickUseResult.NotNeeded:
                    ShowToast("Здоровье и так полное");
                    return;
                case QuickUseResult.Healed:
                    break;
                default:
                    return;
            }

            if (damageNumberPrefab == null || numbersRoot == null)
                return;
            if (healTarget == null)
                healTarget = FindAnyObjectByType<PlayerController>();
            if (healTarget == null)
                return;
            var text = Instantiate(damageNumberPrefab, numbersRoot, false);
            text.gameObject.SetActive(true);
            text.text = "+" + Mathf.RoundToInt(e.Healed);
            text.fontSize = bigDamageFontSize;
            text.color = new Color(0.45f, 0.95f, 0.5f);
            text.raycastTarget = false;
            numbers.Add(new FloatingNumber { Text = text, World = healTarget.transform.position + Vector3.up * 1.1f, Time = 0f });
        }

        private void OnDamage(DamageDealt e)
        {
            if (damageNumberPrefab == null || numbersRoot == null)
                return;
            // Цвет по кругу: слабый удар — мелкая серая цифра, через один — цифра цвета оружия,
            // антипод — крупная цифра с «!».
            var effect = e.ToPlayer ? ColorsAction.Combat.ColorEffect.Neutral : e.Effect;
            var color = e.ToPlayer ? new Color(1f, 0.35f, 0.3f)
                : effect == ColorsAction.Combat.ColorEffect.Weak ? weakDamageColor
                : e.ColorBonus ? Color.Lerp(e.Color, Color.white, 0.25f)
                : UiFactory.TextColor;
            var text = Instantiate(damageNumberPrefab, numbersRoot, false);
            text.gameObject.SetActive(true);
            text.text = Mathf.RoundToInt(e.Amount).ToString();
            text.fontSize = effect == ColorsAction.Combat.ColorEffect.Antipode || e.ToPlayer ? bigDamageFontSize
                : effect == ColorsAction.Combat.ColorEffect.Weak ? weakDamageFontSize
                : damageFontSize;
            text.color = color;
            text.raycastTarget = false;
            if (effect == ColorsAction.Combat.ColorEffect.Antipode)
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
                var screen = cam.WorldToScreenPoint(CurvedWorld.Bend(world));
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
