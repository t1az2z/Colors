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
        LockOn,

        /// <summary>Колесо оружия: TAB / Y (удержание открывает колесо и замедляет время).</summary>
        Wheel
    }

    /// <summary>
    /// Весь ввод игры по GDD: клавиатура+мышь и геймпад. Привязки описаны здесь, в одном месте.
    /// <para>Геймплей (GDD v0.3, раздел 4): WASD/левый стик — ходьба, LShift/L3 — бег, Пробел/A — прыжок, Alt/B — перекат,
    /// ЛКМ/RT — атака активным оружием, ПКМ/LT — блок и парирование этим оружием, TAB/Y — колесо оружия,
    /// F/X — взаимодействие, I/Select — инвентарь, 1–4/D-pad ↑ → ↓ ← — предметы крестовины, СКМ/R3 — фиксация на цели,
    /// мышь/правый стик — вращение камеры (схема А), Q/E и LB/RB — поворот камеры на 90° (схема Б), колесо мыши — зум,
    /// F1 — переключить схему камеры А/Б (только для прототипа).</para>
    /// <para>Интерфейс: стрелки/D-pad/левый стик — навигация, Enter/A — выбрать, Esc/B — назад, правый стик — вращать
    /// персонажа в превью инвентаря (<see cref="PreviewStick"/>).</para>
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

        /// <summary>Инвентарь: I / Select (View).</summary>
        public InputAction Inventory { get; private set; }

        /// <summary>Колесо оружия: TAB / Y.</summary>
        public InputAction Wheel { get; private set; }
        public InputAction CameraStick { get; private set; }
        public InputAction CameraLeft { get; private set; }
        public InputAction CameraRight { get; private set; }

        /// <summary>Фиксация на цели: средняя кнопка мыши / R3.</summary>
        public InputAction LockOn { get; private set; }

        /// <summary>
        /// Смена цели при фиксации: правый стик / рывок мышью в сторону. Только для подсказок (кнопки под устройство):
        /// само переключение читает <see cref="PlayerLockOn"/> из <see cref="AimStick"/> и мыши.
        /// </summary>
        public InputAction SwitchTarget { get; private set; }

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
                case GameAction.LockOn: return LockOn;
                case GameAction.Wheel: return Wheel;
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
                // Открытое колесо оружия забирает левый стик под выбор сектора: персонаж от него не ходит (WASD — ходит).
                if (WeaponWheel.IsOpen)
                    v -= WheelStick;
                if (ScriptedMove.HasValue)
                    v += ScriptedMove.Value;
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        /// <summary>Правый стик (камера в FreeOrbit, взгляд в FixedAngles).</summary>
        public Vector2 AimStick => CameraStick.ReadValue<Vector2>() + (ScriptedAim ?? Vector2.zero);

        public Vector2? ScriptedAim { get; set; }

        /// <summary>Левый стик геймпада — выбор сектора колеса оружия.</summary>
        public Vector2 WheelStick => Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;

        public Vector2 NavigateVector => Navigate.ReadValue<Vector2>() + (ScriptedNavigate ?? Vector2.zero);

        public Vector2? ScriptedNavigate { get; set; }

        /// <summary>Правый стик в интерфейсе (вращение персонажа в превью инвентаря).</summary>
        public Vector2 PreviewStick => PreviewRotate.ReadValue<Vector2>() + (ScriptedAim ?? Vector2.zero);

        private InputAction PreviewRotate { get; set; }

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

            // На геймпаде поворот инвертирован относительно Q/E: LB крутит камеру вправо, RB — влево.
            CameraLeft = player.AddAction("CameraLeft", InputActionType.Button);
            CameraLeft.AddBinding("<Gamepad>/rightShoulder");
            CameraLeft.AddBinding("<Keyboard>/q");
            CameraRight = player.AddAction("CameraRight", InputActionType.Button);
            CameraRight.AddBinding("<Gamepad>/leftShoulder");
            CameraRight.AddBinding("<Keyboard>/e");

            LockOn = player.AddAction("LockOn", InputActionType.Button);
            LockOn.AddBinding("<Mouse>/middleButton");
            LockOn.AddBinding("<Gamepad>/rightStickPress");

            SwitchTarget = player.AddAction("SwitchTarget", InputActionType.Value, expectedControlLayout: "Vector2");
            SwitchTarget.AddBinding("<Gamepad>/rightStick");
            SwitchTarget.AddBinding("<Mouse>/delta");

            Wheel = player.AddAction("Wheel", InputActionType.Button);
            Wheel.AddBinding("<Keyboard>/tab");
            Wheel.AddBinding("<Gamepad>/buttonNorth");

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
            Inventory.AddBinding("<Keyboard>/i");
            Inventory.AddBinding("<Gamepad>/select");

            PreviewRotate = ui.AddAction("PreviewRotate", InputActionType.Value, expectedControlLayout: "Vector2");
            PreviewRotate.AddBinding("<Gamepad>/rightStick");

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

        /// <summary>
        /// Семейство подключённого геймпада для подписей кнопок: "ps" (DualShock / DualSense) или "xbox" (остальные).
        /// </summary>
        public string GamepadFamily => Gamepad.current is UnityEngine.InputSystem.DualShock.DualShockGamepad ? "ps" : "xbox";

        /// <summary>
        /// Действия Input System за id действия из подсказок ("interact", "attack", "items"…): обычно одно, у составных —
        /// несколько ("cameraTurn" — влево и вправо, "items" — слоты 1–4). Неизвестный id — пусто.
        /// </summary>
        public System.Collections.Generic.List<InputAction> ActionsFor(string id)
        {
            var result = new System.Collections.Generic.List<InputAction>();
            if (string.IsNullOrEmpty(id))
                return result;
            switch (id.ToLowerInvariant())
            {
                case "attack": result.Add(Primary); break;
                case "block":
                case "parry": result.Add(Secondary); break;
                case "cameraturn": result.Add(CameraLeft); result.Add(CameraRight); break;
                case "items": result.AddRange(QuickItems); break;
                case "camera": result.Add(CameraStick); break;
                default:
                    var action = player?.FindAction(id) ?? ui?.FindAction(id);
                    if (action != null)
                        result.Add(action);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Пути кнопок действия для устройства (<c>&lt;Keyboard&gt;/f</c>, <c>&lt;Gamepad&gt;/buttonWest</c>): первая подходящая привязка
        /// (с учётом переназначения — <c>effectivePath</c>); у составной привязки (WASD) — все её части.
        /// </summary>
        public System.Collections.Generic.List<string> ControlPaths(InputAction action, bool gamepad)
        {
            var result = new System.Collections.Generic.List<string>();
            if (action == null)
                return result;
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b.isComposite)
                {
                    var parts = new System.Collections.Generic.List<string>();
                    for (int j = i + 1; j < bindings.Count && bindings[j].isPartOfComposite; j++)
                        if (!string.IsNullOrEmpty(bindings[j].effectivePath) && IsGamepadPath(bindings[j].effectivePath) == gamepad)
                            parts.Add(bindings[j].effectivePath);
                    if (parts.Count > 0)
                        return parts;
                    continue;
                }

                if (b.isPartOfComposite || string.IsNullOrEmpty(b.effectivePath))
                    continue;
                if (IsGamepadPath(b.effectivePath) == gamepad)
                {
                    result.Add(b.effectivePath);
                    return result;
                }
            }

            return result;
        }

        private static bool IsGamepadPath(string path) =>
            !(path.StartsWith("<Keyboard>", StringComparison.Ordinal) || path.StartsWith("<Mouse>", StringComparison.Ordinal)
                                                                       || path.StartsWith("<Pointer>", StringComparison.Ordinal));

        private void OnDestroy()
        {
            player?.Dispose();
            ui?.Dispose();
        }
    }
}
