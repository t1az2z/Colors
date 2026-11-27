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
    /// в секторе своего цвета; сектор без оружия виден сразу, но тусклый. Сектор выбирается мышью (от центра экрана)
    /// или правым стиком; отпускание кнопки делает оружие активным. У смены есть кулдаун
    /// (<see cref="WeaponWheelConfig"/>, Combat.json → weaponWheel).
    /// </summary>
    public sealed class WeaponWheel : GameBehaviour
    {
        private const float Radius = 230f;
        private const float InnerRadius = 120f;
        private const float SectorGap = 0.006f;

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

        private RectTransform root;
        private CanvasGroup group;
        private Text title;
        private Text subtitle;
        private readonly List<Sector> sectors = new List<Sector>();

        private float openT;
        private int hovered = -1;
        private float cooldownUntil;
        private bool wasOpen;

        private sealed class Sector
        {
            public string Color;
            public Image Fill;
            public Image Icon;
            public Text Label;
            public RectTransform Rect;
            public Item Weapon;
        }

        /// <summary>Создать колесо поверх HUD.</summary>
        public static WeaponWheel Create(Transform parent, IResolver resolver)
        {
            var go = new GameObject("WeaponWheel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var wheel = go.AddComponent<WeaponWheel>();
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
            Build();
        }

        private void OnDisable() => Close();

        // ------------------------------------------------------------------ вёрстка

        private void Build()
        {
            if (root != null)
                return;
            root = UiFactory.Rect(transform, "Wheel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                Vector2.one * Radius * 2f);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var shade = UiFactory.Image(root, "Shade", new Color(0f, 0f, 0f, 0.35f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (Radius * 2f + 40f));
            UiSprites.Apply(shade, UiSprites.Circle, false);

            var colors = rules.Wheel.Colors;
            var step = colors.Count > 0 ? 360f / colors.Count : 360f;
            for (int i = 0; i < colors.Count; i++)
            {
                var s = new Sector { Color = colors[i] };
                var fill = UiFactory.Image(root, "Sector_" + colors[i], Color.white, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * Radius * 2f);
                UiSprites.Apply(fill, UiSprites.Circle, false);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int)Image.Origin360.Top;
                fill.fillClockwise = true;
                fill.fillAmount = 1f / colors.Count - SectorGap;
                // Сектор i — от (i·step − step/2) до (i·step + step/2) по часовой от верха.
                fill.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -(i * step - step * 0.5f + SectorGap * 180f));
                fill.raycastTarget = false;
                s.Fill = fill;
                s.Rect = fill.rectTransform;

                var angle = i * step * Mathf.Deg2Rad;
                var mid = (Radius + InnerRadius) * 0.5f;
                var pos = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * mid;
                s.Icon = UiFactory.Image(root, "Icon_" + colors[i], Color.white, new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), pos, Vector2.one * 74f);
                s.Icon.raycastTarget = false;
                s.Label = UiFactory.Text(root, "Empty_" + colors[i], "", 28, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f));
                var lr = s.Label.rectTransform;
                lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0.5f);
                lr.pivot = new Vector2(0.5f, 0.5f);
                lr.anchoredPosition = pos;
                lr.sizeDelta = new Vector2(90f, 40f);
                sectors.Add(s);
            }

            // Середина — тёмный круг с названием оружия под курсором.
            var hub = UiFactory.Image(root, "Hub", new Color(0.1f, 0.1f, 0.12f, 0.92f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * InnerRadius * 2f);
            UiSprites.Apply(hub, UiSprites.Circle, false);
            hub.raycastTarget = false;
            title = UiFactory.Text(hub.transform, "Title", "", 26, TextAnchor.MiddleCenter, UiFactory.TextColor);
            Place(title.rectTransform, new Vector2(0f, 14f), new Vector2(InnerRadius * 1.8f, 40f));
            subtitle = UiFactory.Text(hub.transform, "Subtitle", "", 18, TextAnchor.MiddleCenter, UiFactory.MutedText);
            Place(subtitle.rectTransform, new Vector2(0f, -20f), new Vector2(InnerRadius * 1.8f, 30f));
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        // ------------------------------------------------------------------ инвентарь

        private Item ActiveWeapon => session.Inventory.Active;

        private bool Activate(Item item) => session.Inventory.SetActive(item);

        private Item WeaponOfColor(string color) => session.Inventory.WeaponOfColor(color);

        // ------------------------------------------------------------------ ход

        private bool WheelHeld => input.Held(GameAction.Wheel);

        private void Update()
        {
            if (session == null || root == null)
                return;
            if (icons == null)
            {
                var hud = FindAnyObjectByType<Hud>();
                icons = hud != null ? hud.Icons : null;
            }

            var wantOpen = WheelHeld && session.PlayerControl.Value;
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
            root.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, openT);
            if (!IsOpen)
                return;

            hovered = PickSector();
            Refresh();
        }

        private void Open()
        {
            IsOpen = true;
            hovered = -1;
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

        /// <summary>Сектор под мышью (от центра экрана) или правым стиком; -1 — в мёртвой зоне.</summary>
        private int PickSector()
        {
            var dir = Vector2.zero;
            var stick = input.AimStick;
            if (stick.magnitude > config.DeadZone)
                dir = stick;
            else
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    var center = new Vector2(Screen.width, Screen.height) * 0.5f;
                    var scale = Screen.height / 1080f;
                    var offset = (mouse.position.ReadValue() - center) / Mathf.Max(1f, Radius * scale);
                    if (offset.magnitude > config.DeadZone)
                        dir = offset;
                }
            }

            if (dir == Vector2.zero)
                return hovered;
            var angle = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg; // 0 — верх, по часовой
            if (angle < 0f)
                angle += 360f;
            var step = 360f / Mathf.Max(1, sectors.Count);
            return Mathf.FloorToInt((angle + step * 0.5f) / step) % sectors.Count;
        }

        private void Refresh()
        {
            var active = ActiveWeapon;
            var cooling = Time.unscaledTime < cooldownUntil;
            for (int i = 0; i < sectors.Count; i++)
            {
                var s = sectors[i];
                s.Weapon = WeaponOfColor(s.Color);
                var color = palette.Get(s.Color);
                var owned = s.Weapon != null;
                var hover = i == hovered;
                // Пустой сектор виден с самого начала, но тусклый: игрок знает, сколько оружия ещё найти.
                var alpha = owned ? (hover ? 1f : 0.75f) : (hover ? 0.4f : 0.25f);
                color.a = alpha;
                s.Fill.color = color;
                s.Rect.localScale = Vector3.one * (hover ? 1.06f : s.Weapon != null && s.Weapon == active ? 1.02f : 1f);

                if (owned && icons != null)
                {
                    icons.Apply(s.Icon, s.Weapon, palette.Get(s.Color));
                    s.Label.text = "";
                }
                else
                {
                    s.Icon.enabled = false;
                    s.Label.text = owned ? s.Weapon.Config.Name : "?";
                }
            }

            var h = hovered >= 0 && hovered < sectors.Count ? sectors[hovered] : null;
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
    }
}
