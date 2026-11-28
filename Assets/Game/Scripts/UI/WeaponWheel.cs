using System;
using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using GameCore.Config;
using GameCore.DI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Колесо выбора оружия (GDD, раздел 7): пока кнопка колеса (TAB / Y) зажата, время сильно замедлено, на экране —
    /// круг из 6 цветов (<see cref="CombatConfig.ColorWheel"/>, жёлтый сверху, по часовой). Найденное оружие стоит
    /// в секторе своего цвета; сектор без оружия виден сразу, но тусклый, со знаком «?». Сектор выбирается движением
    /// мыши (виртуальный курсор от центра, курсор захвачен) или левым стиком геймпада; отпускание кнопки делает оружие
    /// активным. У смены есть кулдаун (<see cref="WeaponWheelConfig"/>, Combat.json → weaponWheel).
    /// Вёрстка — префаб Resources/UI/WeaponWheel (собирает WeaponWheelPrefabBuilder по макету Tools/UI/ref_weapon_wheel.png,
    /// спрайты Art/UI/Wheel рисует Tools/UI/make_ui_sprites.py). Выбранный сектор ярче остальных, в белой обводке и
    /// с ромбом на рамке; середина залита его цветом, в ней название оружия и подпись.
    /// </summary>
    public sealed class WeaponWheel : GameBehaviour
    {
        public const string Resource = "UI/WeaponWheel";

        /// <summary>Геометрия колеса в пикселях 1080p — как WHEEL в Tools/UI/make_ui_sprites.py.</summary>
        public const float Radius = 392f;
        public const float SectorInner = 168f;
        public const float SectorOuter = 350f;

        /// <summary>Радиус ромба выбранного сектора — середина рамки.</summary>
        public const float MarkerRadius = Radius - 10f;

        private static readonly Color HubIdle = new Color(0.27f, 0.2f, 0.34f, 1f);
        private static readonly Color Muted = new Color(0.2f, 0.15f, 0.26f, 1f);

        [Serializable]
        public sealed class SectorView
        {
            public RectTransform Rect;
            public Image Fill;
            public Image Outline;
            public Image Icon;
            public Text Label;
        }

        [SerializeField] private RectTransform wheel;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image hub;
        [SerializeField] private Text title;
        [SerializeField] private Text subtitle;
        [Tooltip("Ромб на рамке напротив выбранного сектора.")]
        [SerializeField] private RectTransform marker;
        [Tooltip("Секторы по часовой от верха; цвет i-го — i-й цвет круга Combat.json colorWheel.")]
        [SerializeField] private SectorView[] sectorViews = Array.Empty<SectorView>();
        [SerializeField] private Color titleLight = new Color(0.97f, 0.92f, 0.92f, 1f);
        [SerializeField] private Color subtitleLight = new Color(0.93f, 0.75f, 0.74f, 1f);

        /// <summary>Колесо открыто: камера и атаки не читают мышь и стик.</summary>
        public static bool IsOpen { get; private set; }

        /// <summary>Сколько раз оружие сменено через колесо (для автотестов).</summary>
        public int Switches { get; private set; }

        private GameSession session;
        private GameInput input;
        private Palette palette;
        private ItemIcons icons;
        private DamageRules rules;
        private WeaponWheelConfig config;

        private readonly List<Sector> sectors = new List<Sector>();

        private float openT;
        private int hovered = -1;

        /// <summary>
        /// Виртуальный курсор колеса: сдвиг мыши от центра, накопленный из mouse.delta. Курсор в игре захвачен
        /// (свободная камера), его позиция не меняется, поэтому выбирать сектор по Mouse.position нельзя.
        /// </summary>
        private Vector2 virtualCursor;
        private float cooldownUntil;
        private bool wasOpen;

        private sealed class Sector
        {
            public string Color;
            public SectorView View;
            public Item Weapon;
        }

        /// <summary>Создать колесо поверх HUD из префаба Resources/UI/WeaponWheel.</summary>
        public static WeaponWheel Create(Transform parent, IResolver resolver)
        {
            var prefab = Resources.Load<WeaponWheel>(Resource);
            if (prefab == null)
            {
                Debug.LogError($"WeaponWheel: нет префаба Resources/{Resource} (ColorsAction/UI/Rebuild Weapon Wheel)");
                return null;
            }

            var wheel = Instantiate(prefab, parent, false);
            wheel.name = "WeaponWheel";
            wheel.Construct(resolver);
            return wheel;
        }

        protected override void OnConstruct(IResolver resolver)
        {
            session = resolver.Resolve<GameSession>();
            input = resolver.Resolve<GameInput>();
            palette = resolver.Resolve<Palette>();
            rules = resolver.Resolve<DamageRules>();
            config = resolver.Resolve<ConfigService>().Load<CombatConfig>(ConfigKeys.Combat).WeaponWheel ?? new WeaponWheelConfig();
            Bind();
        }

        private void OnDisable() => Close();

        /// <summary>Цвета круга → секторы префаба (по порядку, по часовой от верха).</summary>
        private void Bind()
        {
            sectors.Clear();
            var colors = rules.Wheel.Colors;
            if (colors.Count != sectorViews.Length)
                Debug.LogWarning($"WeaponWheel: цветов в круге {colors.Count}, секторов в префабе {sectorViews.Length}");
            for (int i = 0; i < Mathf.Min(colors.Count, sectorViews.Length); i++)
                sectors.Add(new Sector { Color = colors[i], View = sectorViews[i] });
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;
            if (marker != null)
                marker.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ инвентарь

        private Item ActiveWeapon => session.Inventory.Active;

        private bool Activate(Item item) => session.Inventory.SetActive(item);

        private Item WeaponOfColor(string color) => session.Inventory.WeaponOfColor(color);

        // ------------------------------------------------------------------ ход

        private bool WheelHeld => input.Held(GameAction.Wheel);

        private void Update()
        {
            if (session == null || wheel == null)
                return;
            if (icons == null)
            {
                var hud = FindAnyObjectByType<Hud>();
                icons = hud != null ? hud.Icons : null;
            }

            var wantOpen = WheelHeld && session.PlayerControl.Value && !PickupSequence.Playing;
            if (wantOpen && !wasOpen)
                Open();
            else if (!wantOpen && wasOpen)
            {
                Choose();
                Close();
            }

            wasOpen = wantOpen;
            var speed = 1f / Mathf.Max(0.01f, config.OpenTime);
            openT = Mathf.MoveTowards(openT, IsOpen ? 1f : 0f, Time.unscaledDeltaTime * speed);
            group.alpha = openT;
            wheel.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, openT);
            if (!IsOpen)
                return;

            hovered = PickSector();
            Refresh();
        }

        private void Open()
        {
            IsOpen = true;
            hovered = -1;
            virtualCursor = Vector2.zero;
            GameTimeScale.SetSlowMotion(config.TimeScale);
            Refresh();
        }

        private void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            // Фриз-кадры удара, если идут, держат время сами (GameTimeScale).
            GameTimeScale.SetSlowMotion(1f);
        }

        private void Choose()
        {
            if (hovered < 0 || hovered >= sectors.Count)
                return;
            var weapon = sectors[hovered].Weapon;
            if (weapon == null || weapon == ActiveWeapon || Time.unscaledTime < cooldownUntil)
                return;
            if (Activate(weapon))
            {
                Switches++;
                cooldownUntil = Time.unscaledTime + config.SwitchCooldown;
            }
        }

        /// <summary>Сектор по виртуальному курсору мыши (от центра колеса) или левым стиком геймпада; -1 — в мёртвой зоне.</summary>
        private int PickSector()
        {
            var dir = Vector2.zero;
            var stick = input.WheelStick;
            if (stick.magnitude > config.DeadZone)
                dir = stick;
            else
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    // Курсор захвачен — копим движение мыши; дальше края колеса не уходит, чтобы обратный ход сразу менял сектор.
                    var radius = Mathf.Max(1f, SectorOuter * (Screen.height / 1080f));
                    virtualCursor = Vector2.ClampMagnitude(virtualCursor + mouse.delta.ReadValue(), radius);
                    var offset = virtualCursor / radius;
                    if (offset.magnitude > config.DeadZone)
                        dir = offset;
                }
            }

            if (dir == Vector2.zero || sectors.Count == 0)
                return hovered;
            var angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg; // 0 — верх, по часовой
            if (angle < 0f)
                angle += 360f;
            var step = 360f / sectors.Count;
            return Mathf.FloorToInt((angle + step * 0.5f) / step) % sectors.Count;
        }

        private void Refresh()
        {
            var active = ActiveWeapon;
            var cooling = Time.unscaledTime < cooldownUntil;
            for (int i = 0; i < sectors.Count; i++)
            {
                var s = sectors[i];
                var v = s.View;
                s.Weapon = WeaponOfColor(s.Color);
                var baseColor = palette.Get(s.Color);
                var owned = s.Weapon != null;
                var hover = i == hovered;
                // Сектор — приглушённый цвет круга; пустой — ещё глуше (игрок видит, сколько оружия ещё найти);
                // выбранный — ярче остальных и в белой обводке, чтобы бросался в глаза.
                Color color;
                if (hover)
                    color = Color.Lerp(baseColor, Color.white, 0.3f);
                else if (owned)
                    color = Color.Lerp(baseColor, Muted, s.Weapon == active ? 0.15f : 0.3f);
                else
                    color = Color.Lerp(baseColor, Muted, 0.5f);
                color.a = 1f;
                v.Fill.color = color;
                var scale = Vector3.one * (hover ? 1.03f : 1f);
                v.Rect.localScale = scale;
                if (v.Outline != null)
                {
                    v.Outline.enabled = hover;
                    v.Outline.rectTransform.localScale = scale;
                }

                if (owned && icons != null)
                {
                    icons.Apply(v.Icon, s.Weapon, baseColor);
                    v.Label.text = "";
                }
                else
                {
                    v.Icon.enabled = false;
                    v.Label.text = owned ? s.Weapon.Config.Name : "?";
                }
            }

            var h = hovered >= 0 && hovered < sectors.Count ? sectors[hovered] : null;
            PlaceMarker();
            PaintHub(h, active);
            if (h?.Weapon != null)
            {
                title.text = h.Weapon.Config.Name;
                subtitle.text = h.Weapon == active ? "в руках" : cooling ? "перезарядка" : "отпусти — взять";
            }
            else if (h != null)
            {
                title.text = "—";
                subtitle.text = "оружие не найдено";
            }
            else
            {
                title.text = active != null ? active.Config.Name : "Без оружия";
                subtitle.text = "выбери цвет";
            }
        }

        /// <summary>Ромб на рамке — посередине выбранного сектора.</summary>
        private void PlaceMarker()
        {
            if (marker == null)
                return;
            var show = hovered >= 0 && hovered < sectors.Count;
            marker.gameObject.SetActive(show);
            if (!show)
                return;
            var angle = hovered * (360f / sectors.Count) * Mathf.Deg2Rad;
            marker.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * MarkerRadius;
        }

        /// <summary>
        /// Середина колеса — цвет выбранного сектора (пустой сектор — приглушённый), без выбора — цвет оружия в руках
        /// или тёмная. Текст тёмный на светлых цветах (жёлтый), светлый на тёмных.
        /// </summary>
        private void PaintHub(Sector h, Item active)
        {
            Color fill;
            if (h != null)
                fill = h.Weapon != null ? palette.Get(h.Color) : Color.Lerp(HubIdle, palette.Get(h.Color), 0.5f);
            else if (active != null && !string.IsNullOrEmpty(active.Config.Color))
                fill = Color.Lerp(HubIdle, palette.Get(active.Config.Color), 0.6f);
            else
                fill = HubIdle;
            fill.a = 1f;
            hub.color = fill;

            var light = 0.299f * fill.r + 0.587f * fill.g + 0.114f * fill.b > 0.55f;
            title.color = light ? new Color(0.12f, 0.08f, 0.14f) : titleLight;
            subtitle.color = light ? new Color(0.12f, 0.08f, 0.14f, 0.75f) : subtitleLight;
        }
    }
}
