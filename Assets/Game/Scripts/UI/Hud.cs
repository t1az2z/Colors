using System;
using System.Collections.Generic;
using ColorsAction.Inventory;
using ColorsAction.Localization;
using GameCore.DI;
using GameCore.Events;
using UnityEngine;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>
    /// HUD (макет Tools/UI/ref_hud.png, спрайты Art/UI/Atlas): слева вверху — здоровье (рамка с ромбом и цифрами) и
    /// выносливость под ним, слева внизу — крестовина предметов 1–4, справа внизу — опыт на плашке с кубиками
    /// (только несколько секунд после получения опыта); справа вверху — активное оружие в медальоне
    /// (подпись — кнопка колеса оружия), без оружия медальона нет;
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
        [Tooltip("Плашка опыта: видна только несколько секунд после получения опыта (пусто — видна всегда).")]
        [SerializeField] private CanvasGroup experienceGroup;
        [SerializeField] private float experienceShowTime = 3.5f;
        [SerializeField] private Text prompt;
        [SerializeField] private Text toast;
        [Tooltip("Старая строка схемы управления (схемы А/Б больше нет): прячется, если осталась в префабе.")]
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
        private InputGlyphs glyphs;
        private Strings strings;
        private PromptLine promptLine;
        private PromptLine toastLine;
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
        private float experienceTimer;
        private float experienceAlpha;

        protected override void OnConstruct(IResolver resolver)
        {
            session = resolver.Resolve<GameSession>();
            input = resolver.Resolve<GameInput>();
            glyphs = resolver.Resolve<InputGlyphs>();
            strings = resolver.Resolve<Strings>();
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
            // Тексты с кнопками ({action:id}) рисуют строки подсказок поверх прежних Text префаба.
            promptLine = PromptLine.Replace(prompt, glyphs);
            toastLine = PromptLine.Replace(toast, glyphs);
            if (schemeLabel != null)
                schemeLabel.gameObject.SetActive(false);
            started = true;

            subscriptions.Add(events.Subscribe<ItemPickedUp>(e =>
                ShowToast(strings.Format(e.Item.Config.IsWeapon ? "toast.weaponPicked" : "toast.itemPicked", e.Item.Config.Name))));
            subscriptions.Add(events.Subscribe<InventoryFull>(_ => ShowToast(strings.Get("toast.inventoryFull"))));
            subscriptions.Add(events.Subscribe<ResourcesGained>(e =>
                ShowToast(strings.Format("toast.resources", e.Item.Name, e.Count, e.Total))));
            subscriptions.Add(events.Subscribe<DamageDealt>(OnDamage));
            subscriptions.Add(events.Subscribe<QuickItemUsed>(OnQuickItemUsed));
            subscriptions.Add(session.Vitals.Health.Current.Subscribe(_ => healthShake = 0.35f, false));
            var lastExperience = session.Experience.Value;
            subscriptions.Add(session.Experience.Subscribe(xp =>
            {
                // Плашка опыта показывается, только когда опыт прибавился.
                if (xp > lastExperience)
                    experienceTimer = experienceShowTime;
                lastExperience = xp;
            }, false));
            if (experienceGroup != null)
                experienceGroup.alpha = 0f;
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
            toastLine.SetText(text);
            toastLine.Alpha = 1f;
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
            SetBar(healthFill, healthWidth, healthHeight, hp);
            healthShown = hp > healthShown ? hp : Mathf.MoveTowards(healthShown, hp, dt * 0.5f);
            SetBar(healthLag, healthWidth, healthHeight, healthShown);
            healthText.text = $"{Mathf.CeilToInt(health.Current.Value)} / {Mathf.RoundToInt(health.Max)}";
            if (healthShake > 0f)
            {
                healthShake -= dt;
                var k = Mathf.Clamp01(healthShake / 0.35f);
                healthFill.transform.parent.localPosition += new Vector3(UnityEngine.Random.Range(-4f, 4f) * k, 0f, 0f);
            }

            // Выносливость.
            var fraction = session.Stamina.Fraction;
            SetBar(staminaFill, staminaWidth, staminaHeight, fraction);
            staminaShown = fraction > staminaShown ? fraction : Mathf.MoveTowards(staminaShown, fraction, dt * 0.6f);
            SetBar(staminaLag, staminaWidth, staminaHeight, staminaShown);

            xpText.text = strings.Format("hud.experience", session.Experience.Value);
            if (experienceGroup != null)
            {
                experienceTimer = Mathf.Max(0f, experienceTimer - dt);
                // Появляется быстро, исчезает плавно за последние полсекунды.
                var shown = experienceTimer > 0f ? 1f : 0f;
                experienceAlpha = Mathf.MoveTowards(experienceAlpha, shown, dt * (shown > experienceAlpha ? 6f : 2f));
                experienceGroup.alpha = experienceAlpha;
            }

            // Подпись кружка — кнопка колеса: им оружие и меняется.
            if (primaryWeapon != null)
                primaryWeapon.SetKey(glyphs.Label("wheel"));

            var target = interactor != null ? interactor.Target : null;
            promptLine.SetText(target == null ? string.Empty
                : interactor.TargetUsable ? strings.Format("hud.interact", strings.Get(target.Prompt))
                : strings.Get(target.BlockedPrompt));

            if (toastTimer > 0f)
            {
                toastTimer -= dt;
                toastLine.Alpha = Mathf.Clamp01(toastTimer / 0.5f);
            }

            UpdateNumbers(dt);
        }

        /// <summary>
        /// Заполнение полосы: у Filled-картинки (новый HUD из атласа) — fillAmount, у старой плашки — ширина.
        /// </summary>
        private static void SetBar(Image bar, float width, float height, float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            if (bar.type == Image.Type.Filled)
                bar.fillAmount = fraction;
            else
                bar.rectTransform.sizeDelta = new Vector2(Mathf.Max(0f, width * fraction), height);
            bar.enabled = fraction > 0.001f;
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
