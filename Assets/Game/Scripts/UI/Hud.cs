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
    /// HUD: выносливость, что в руках, подсказка взаимодействия, всплывающие сообщения и цифры урона.
    /// </summary>
    public sealed class Hud : GameBehaviour
    {
        [SerializeField] private PlayerInteractor interactor;

        private GameSession session;
        private GameInput input;
        private Palette palette;
        private IEventBus events;
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();

        private RectTransform root;
        private Image staminaFill;
        private Image staminaLag;
        private Text prompt;
        private Text toast;
        private Text schemeLabel;
        private float toastTimer;
        private readonly Text[] handLabels = new Text[2];
        private readonly Image[] handColors = new Image[2];
        private readonly List<FloatingNumber> numbers = new List<FloatingNumber>();
        private float staminaShown = 1f;

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

            // Выносливость (слева снизу).
            var bar = UiFactory.Image(root, "Stamina", new Color(0, 0, 0, 0.45f), new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 40), new Vector2(360, 18));
            staminaLag = UiFactory.Image(bar.transform, "Lag", new Color(1f, 0.55f, 0.3f, 0.9f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(356, 14));
            staminaLag.rectTransform.anchoredPosition = new Vector2(2, 0);
            staminaFill = UiFactory.Image(bar.transform, "Fill", new Color(0.9f, 0.9f, 0.84f, 1f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(2, 0), new Vector2(356, 14));
            var label = UiFactory.Text(bar.transform, "Label", "Выносливость", 18, TextAnchor.LowerLeft, UiFactory.MutedText);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, 1);
            label.rectTransform.pivot = new Vector2(0, 0);
            label.rectTransform.anchoredPosition = new Vector2(0, 4);
            UiFactory.Outline(label);

            // Руки (по центру снизу): Л и П.
            for (int i = 0; i < 2; i++)
            {
                var hand = (Hand)i;
                var x = hand == Hand.Left ? -90f : 90f;
                var box = UiFactory.Image(root, hand + "Hand", new Color(0, 0, 0, 0.45f), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(x, 36), new Vector2(160, 56));
                handColors[i] = UiFactory.Image(box.transform, "Color", Color.gray, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(10, 40));
                handLabels[i] = UiFactory.Text(box.transform, "Name", "", 20, TextAnchor.MiddleLeft, UiFactory.TextColor);
                handLabels[i].rectTransform.anchorMin = Vector2.zero;
                handLabels[i].rectTransform.anchorMax = Vector2.one;
                handLabels[i].rectTransform.offsetMin = new Vector2(26, 0);
                handLabels[i].rectTransform.offsetMax = new Vector2(-6, 0);
                var key = UiFactory.Text(box.transform, "Key", "", 16, TextAnchor.LowerCenter, UiFactory.MutedText);
                key.rectTransform.anchorMin = key.rectTransform.anchorMax = new Vector2(0.5f, 1);
                key.rectTransform.pivot = new Vector2(0.5f, 0f);
                key.rectTransform.anchoredPosition = new Vector2(0, 4);
                key.name = "Key" + i;
                UiFactory.Outline(key);
            }

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
            session.Inventory.Changed += RefreshHands;
            subscriptions.Add(session.PlayerControl.Subscribe(v => root.gameObject.SetActive(true)));
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
            for (int i = 0; i < 2; i++)
            {
                var item = session.Inventory.InHand((Hand)i);
                handLabels[i].text = item != null ? item.Config.Name : "Пусто";
                handColors[i].color = item != null ? palette.Get(item.Config.Color) : palette.Get("none");
            }
        }

        private void Update()
        {
            if (root == null)
                return;
            var dt = Time.unscaledDeltaTime;

            // Выносливость: заполнение сразу, "хвост" догоняет медленно.
            var fraction = session.Stamina.Fraction;
            staminaFill.rectTransform.sizeDelta = new Vector2(356 * fraction, 14);
            staminaShown = fraction > staminaShown ? fraction : Mathf.MoveTowards(staminaShown, fraction, dt * 0.6f);
            staminaLag.rectTransform.sizeDelta = new Vector2(356 * staminaShown, 14);
            staminaFill.color = fraction <= 0.001f ? new Color(0.9f, 0.35f, 0.3f) : new Color(0.9f, 0.9f, 0.84f);

            // Подсказки кнопок рук.
            for (int i = 0; i < 2; i++)
            {
                var key = handLabels[i].transform.parent.Find("Key" + i)?.GetComponent<Text>();
                if (key != null)
                    key.text = input.Label(i == (int)Hand.Left ? "secondary" : "attack");
            }

            schemeLabel.text = session.ControlScheme.Value == ColorsAction.Configs.CameraScheme.FixedAngles
                ? $"Схема Б  ·  [{input.Label("cameraTurn")}] поворот камеры  ·  [{input.Label("switchScheme")}] схема А"
                : $"Схема А  ·  [{input.Label("switchScheme")}] схема Б";

            var target = interactor != null ? interactor.Target : null;
            prompt.text = target != null ? $"[{input.Label("interact")}]  взять: {target.DisplayName}" : string.Empty;

            if (toastTimer > 0f)
            {
                toastTimer -= dt;
                var c = toast.color;
                c.a = Mathf.Clamp01(toastTimer / 0.5f);
                toast.color = c;
            }

            UpdateNumbers(dt);
        }

        // ------------------------------------------------------------------ цифры урона

        private void OnDamage(DamageDealt e)
        {
            var text = UiFactory.Text(root, "Damage", Mathf.RoundToInt(e.Amount).ToString(), e.ColorBonus ? 46 : 34,
                TextAnchor.MiddleCenter, e.ColorBonus ? Color.Lerp(e.Color, Color.white, 0.25f) : UiFactory.TextColor);
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
