using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ColorsAction.Unity
{
    /// <summary>Кнопочные действия игры (для чтения через <see cref="GameInput.Pressed"/> / <see cref="GameInput.Held"/>).</summary>
    public enum GameAction
    {
        Run,
        Jump,
        Roll,
        Primary,
        Secondary,
        Interact,
        Inventory,
        Submit,
        Cancel,
        CameraLeft,
        CameraRight,
        SwitchScheme
    }

    /// <summary>
    /// Весь ввод игры по GDD: клавиатура+мышь и геймпад. Привязки описаны здесь, в одном месте.
    /// <para>Геймплей: WASD/левый стик — ходьба, LShift/L3 — бег, Пробел/A — прыжок, Alt/B — перекат,
    /// ЛКМ/RT — правая рука, ПКМ/LT — левая рука, F/X — взаимодействие, TAB/View — инвентарь,
    /// 1–4/D-pad — предметы, мышь/правый стик — вращение камеры (FreeOrbit), Q/E и LB/RB — поворот камеры на 90° (FixedAngles), колесо — зум.</para>
    /// <para>Интерфейс: стрелки/D-pad/левый стик — навигация, Enter/A — выбрать, Esc/B — назад.</para>
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    public sealed class GameInput : MonoBehaviour
    {
        private InputActionMap player;
        private InputActionMap ui;

        public InputAction Move { get; private set; }
        public InputAction Run { get; private set; }
        public InputAction Jump { get; private set; }
        public InputAction Roll { get; private set; }
        public InputAction Primary { get; private set; }
        public InputAction Secondary { get; private set; }
        public InputAction Interact { get; private set; }
        public InputAction Inventory { get; private set; }
        public InputAction CameraStick { get; private set; }
        public InputAction CameraLeft { get; private set; }
        public InputAction CameraRight { get; private set; }

        /// <summary>Переключение схемы управления А/Б для сравнения в прототипе: F1 / Start.</summary>
        public InputAction SwitchScheme { get; private set; }
        public InputAction Zoom { get; private set; }
        public InputAction[] QuickItems { get; private set; }

        public InputAction Navigate { get; private set; }
        public InputAction Submit { get; private set; }
        public InputAction Cancel { get; private set; }

        /// <summary>Последнее устройство — геймпад: подсказки показывают кнопки геймпада.</summary>
        public bool UsingGamepad { get; private set; }

        public event Action<bool> DeviceChanged;

        // Скриптовый ввод: автотесты и демо-прогоны без фокуса окна. Складывается с реальным.
        private readonly System.Collections.Generic.HashSet<GameAction> scriptedHeld = new System.Collections.Generic.HashSet<GameAction>();
        private readonly System.Collections.Generic.HashSet<GameAction> scriptedQueued = new System.Collections.Generic.HashSet<GameAction>();
        private readonly System.Collections.Generic.HashSet<GameAction> scriptedPressed = new System.Collections.Generic.HashSet<GameAction>();
        private int scriptedPressedFrame = -1;

        /// <summary>Скриптовое движение (как левый стик), null — нет.</summary>
        public Vector2? ScriptedMove { get; set; }

        /// <summary>Нажать действие на один кадр (следующий).</summary>
        public void ScriptPress(GameAction action) => scriptedQueued.Add(action);

        /// <summary>Держать/отпустить действие.</summary>
        public void ScriptHold(GameAction action, bool held)
        {
            if (held)
            {
                if (scriptedHeld.Add(action))
                    scriptedQueued.Add(action);
            }
            else
            {
                scriptedHeld.Remove(action);
            }
        }

        public void ClearScripted()
        {
            scriptedHeld.Clear();
            scriptedQueued.Clear();
            ScriptedMove = null;
            ScriptedAim = null;
            ScriptedNavigate = null;
        }

        public InputAction ActionOf(GameAction action)
        {
            switch (action)
            {
                case GameAction.Run: return Run;
                case GameAction.Jump: return Jump;
                case GameAction.Roll: return Roll;
                case GameAction.Primary: return Primary;
                case GameAction.Secondary: return Secondary;
                case GameAction.Interact: return Interact;
                case GameAction.Inventory: return Inventory;
                case GameAction.Submit: return Submit;
                case GameAction.CameraLeft: return CameraLeft;
                case GameAction.CameraRight: return CameraRight;
                case GameAction.SwitchScheme: return SwitchScheme;
                default: return Cancel;
            }
        }

        /// <summary>Нажато в этом кадре.</summary>
        public bool Pressed(GameAction action)
        {
            if (scriptedPressedFrame == Time.frameCount && scriptedPressed.Contains(action))
                return true;
            var a = ActionOf(action);
            return a != null && a.WasPressedThisFrame();
        }

        /// <summary>Удерживается.</summary>
        public bool Held(GameAction action)
        {
            if (scriptedHeld.Contains(action))
                return true;
            var a = ActionOf(action);
            return a != null && a.IsPressed();
        }

        /// <summary>Движение (WASD / левый стик), длина до 1.</summary>
        public Vector2 MoveVector
        {
            get
            {
                var v = Move.ReadValue<Vector2>();
                if (ScriptedMove.HasValue)
                    v += ScriptedMove.Value;
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        /// <summary>Правый стик (камера в FreeOrbit, взгляд в FixedAngles).</summary>
        public Vector2 AimStick => CameraStick.ReadValue<Vector2>() + (ScriptedAim ?? Vector2.zero);

        public Vector2? ScriptedAim { get; set; }

        public Vector2 NavigateVector => Navigate.ReadValue<Vector2>() + (ScriptedNavigate ?? Vector2.zero);

        public Vector2? ScriptedNavigate { get; set; }

        private void Awake()
        {
            player = new InputActionMap("Player");
            Move = player.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddBinding("<Gamepad>/leftStick");

            Run = player.AddAction("Run", InputActionType.Button);
            Run.AddBinding("<Keyboard>/leftShift");
            Run.AddBinding("<Gamepad>/leftStickPress");

            Jump = player.AddAction("Jump", InputActionType.Button);
            Jump.AddBinding("<Keyboard>/space");
            Jump.AddBinding("<Gamepad>/buttonSouth");

            Roll = player.AddAction("Roll", InputActionType.Button);
            Roll.AddBinding("<Keyboard>/leftAlt");
            Roll.AddBinding("<Keyboard>/rightAlt");
            Roll.AddBinding("<Gamepad>/buttonEast");

            Primary = player.AddAction("Primary", InputActionType.Button);
            Primary.AddBinding("<Mouse>/leftButton");
            Primary.AddBinding("<Gamepad>/rightTrigger");

            Secondary = player.AddAction("Secondary", InputActionType.Button);
            Secondary.AddBinding("<Mouse>/rightButton");
            Secondary.AddBinding("<Gamepad>/leftTrigger");

            Interact = player.AddAction("Interact", InputActionType.Button);
            Interact.AddBinding("<Keyboard>/f");
            Interact.AddBinding("<Gamepad>/buttonWest");

            CameraStick = player.AddAction("CameraStick", InputActionType.Value, expectedControlLayout: "Vector2");
            CameraStick.AddBinding("<Gamepad>/rightStick");

            CameraLeft = player.AddAction("CameraLeft", InputActionType.Button);
            CameraLeft.AddBinding("<Gamepad>/leftShoulder");
            CameraLeft.AddBinding("<Keyboard>/q");
            CameraRight = player.AddAction("CameraRight", InputActionType.Button);
            CameraRight.AddBinding("<Gamepad>/rightShoulder");
            CameraRight.AddBinding("<Keyboard>/e");

            SwitchScheme = player.AddAction("SwitchScheme", InputActionType.Button);
            SwitchScheme.AddBinding("<Keyboard>/f1");
            SwitchScheme.AddBinding("<Gamepad>/start");

            Zoom = player.AddAction("Zoom", InputActionType.Value, expectedControlLayout: "Vector2");
            Zoom.AddBinding("<Mouse>/scroll");

            QuickItems = new InputAction[4];
            string[] pads = { "<Gamepad>/dpad/up", "<Gamepad>/dpad/right", "<Gamepad>/dpad/down", "<Gamepad>/dpad/left" };
            for (int i = 0; i < 4; i++)
            {
                QuickItems[i] = player.AddAction("Item" + (i + 1), InputActionType.Button);
                QuickItems[i].AddBinding($"<Keyboard>/{i + 1}");
                QuickItems[i].AddBinding(pads[i]);
            }

            ui = new InputActionMap("UI");
            Inventory = ui.AddAction("Inventory", InputActionType.Button);
            Inventory.AddBinding("<Keyboard>/tab");
            Inventory.AddBinding("<Gamepad>/select");

            Navigate = ui.AddAction("Navigate", InputActionType.Value, expectedControlLayout: "Vector2");
            Navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Navigate.AddBinding("<Gamepad>/dpad");
            Navigate.AddBinding("<Gamepad>/leftStick");

            Submit = ui.AddAction("Submit", InputActionType.Button);
            Submit.AddBinding("<Keyboard>/enter");
            Submit.AddBinding("<Gamepad>/buttonSouth");

            Cancel = ui.AddAction("Cancel", InputActionType.Button);
            Cancel.AddBinding("<Keyboard>/escape");
            Cancel.AddBinding("<Gamepad>/buttonEast");

            player.Enable();
            ui.Enable();
        }

        private void Update()
        {
            // Скриптовые нажатия видны ровно один кадр — как WasPressedThisFrame.
            scriptedPressed.Clear();
            scriptedPressed.UnionWith(scriptedQueued);
            scriptedQueued.Clear();
            scriptedPressedFrame = Time.frameCount;

            var gamepad = Gamepad.current;
            bool padActive = gamepad != null && gamepad.wasUpdatedThisFrame && IsActuated(gamepad);
            bool kbmActive = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                             || (Mouse.current != null && (Mouse.current.delta.ReadValue().sqrMagnitude > 4f
                                                           || Mouse.current.leftButton.wasPressedThisFrame
                                                           || Mouse.current.rightButton.wasPressedThisFrame));
            if (padActive && !UsingGamepad)
                SetGamepad(true);
            else if (kbmActive && UsingGamepad)
                SetGamepad(false);
        }

        private static bool IsActuated(Gamepad pad)
        {
            foreach (var control in pad.allControls)
            {
                if (control is UnityEngine.InputSystem.Controls.ButtonControl b && b.isPressed)
                    return true;
                if (control is UnityEngine.InputSystem.Controls.StickControl s && s.ReadValue().sqrMagnitude > 0.1f)
                    return true;
            }

            return false;
        }

        private void SetGamepad(bool value)
        {
            UsingGamepad = value;
            DeviceChanged?.Invoke(value);
        }

        /// <summary>Подпись кнопки действия для подсказок с учётом устройства.</summary>
        public string Label(string action)
        {
            switch (action)
            {
                case "interact": return UsingGamepad ? "X" : "F";
                case "jump": return UsingGamepad ? "A" : "Пробел";
                case "roll": return UsingGamepad ? "B" : "Alt";
                case "inventory": return UsingGamepad ? "View" : "TAB";
                case "attack": return UsingGamepad ? "RT" : "ЛКМ";
                case "secondary": return UsingGamepad ? "LT" : "ПКМ";
                case "run": return UsingGamepad ? "L3" : "Shift";
                case "cameraTurn": return UsingGamepad ? "LB / RB" : "Q / E";
                case "switchScheme": return UsingGamepad ? "Start" : "F1";
                default: return action;
            }
        }

        private void OnDestroy()
        {
            player?.Dispose();
            ui?.Dispose();
        }
    }
}
