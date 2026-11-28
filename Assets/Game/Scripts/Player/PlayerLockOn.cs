using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using GameCore.Config;
using GameCore.DI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Фиксация на цели (СКМ / R3): персонаж всегда смотрит на цель, перекаты идут относительно неё (вбок — через бок
    /// персонажа), свободная камера доворачивается за спину. Повторное нажатие снимает фиксацию; она снимается и сама,
    /// если цель ушла дальше <see cref="CombatConfig.LockOnBreakRange"/>. Если цель-враг умерла, фиксация переходит
    /// на врага, ближайшего к погибшему (в радиусе <see cref="CombatConfig.LockOnRetargetRange"/> от игрока), а если такого нет — снимается.
    /// Цель обведена белым контуром силуэта (<see cref="LockOnOutline"/>).
    /// Цели — враги, манекены и цепи разводных мостов (<see cref="BridgeChain"/>). Переключение на соседнюю цель — рывок правым стиком или мышью влево/вправо. Числа — Configs/Combat.json.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerLockOn : GameBehaviour
    {

        private readonly List<Component> targets = new List<Component>();
        private readonly List<LockOnCandidate> candidates = new List<LockOnCandidate>();

        private CombatConfig config;
        private GameInput input;
        private GameSession session;
        private PlayerController player;
        private Transform cameraTransform;

        private Component target;
        private bool targetIsEnemy;
        private Vector3 lastTargetPosition;
        private bool switchArmed = true;
        private float mouseSwipe;
        private float mouseSwipeTimer;


        /// <summary>Текущая цель (враг или манекен), null — фиксации нет.</summary>
        public Component Target => target;

        public bool Active => target != null;

        public CombatConfig Config => config;

        /// <summary>Точка на цели, на которую смотрит персонаж.</summary>
        public Vector3 TargetPoint => target is IHittable h ? h.HitPoint : target != null ? target.transform.position : Vector3.zero;

        protected override void OnConstruct(IResolver resolver)
        {
            config = resolver.Resolve<ConfigService>().Load<CombatConfig>(ConfigKeys.Combat);
            input = resolver.Resolve<GameInput>();
            session = resolver.Resolve<GameSession>();
        }

        private void Start()
        {
            player = GetComponent<PlayerController>();
            if (Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        /// <summary>Включить/выключить фиксацию (как нажатие кнопки).</summary>
        public void Toggle()
        {
            if (target != null)
                Release();
            else
                Acquire();
        }

        public void Release()
        {
            target = null;
            targetIsEnemy = false;
        }

        private void Update()
        {
            if (config == null || player == null)
                return;

            var control = player.HasControl;
            if (control && input.Pressed(GameAction.LockOn))
                Toggle();

            // Цель-враг погибла (или уже уничтожена): фиксация переходит на врага, ближайшего к месту гибели.
            if (targetIsEnemy && (target == null || ((EnemyController)target).Dead))
            {
                if (target != null)
                    lastTargetPosition = target.transform.position;
                RetargetNear(lastTargetPosition);
            }
            else if (target != null && !IsValid(target, config.LockOnBreakRange))
                Release();

            if (target != null)
                lastTargetPosition = target.transform.position;

            if (target != null && control)
                UpdateSwitch();
        }

        // ------------------------------------------------------------------ выбор цели

        private void Acquire()
        {
            Gather();
            var p = transform.position;
            var viewYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : player.Body.eulerAngles.y;
            var index = LockOnRules.Pick(p.x, p.z, viewYaw, candidates, config.LockOnRange, config.LockOnAnglePenalty);
            if (index >= 0)
                SetTarget(targets[index]);
        }

        private void UpdateSwitch()
        {
            var direction = 0;
            var stick = input.AimStick;
            if (Mathf.Abs(stick.x) >= config.LockOnSwitchStick)
            {
                if (switchArmed)
                    direction = (int)Mathf.Sign(stick.x);
                switchArmed = false;
            }
            else if (stick.magnitude < config.LockOnSwitchStick * 0.5f)
            {
                switchArmed = true;
            }

            // Мышь — только в свободной камере (в схеме Б мышь задаёт взгляд курсором).
            var mouse = Mouse.current;
            if (direction == 0 && mouse != null && !input.UsingGamepad && !player.FixedAnglesScheme
                && Cursor.lockState == CursorLockMode.Locked)
            {
                mouseSwipe += mouse.delta.ReadValue().x;
                mouseSwipeTimer += Time.unscaledDeltaTime;
                if (Mathf.Abs(mouseSwipe) >= config.LockOnSwitchMouse)
                {
                    direction = (int)Mathf.Sign(mouseSwipe);
                    mouseSwipe = 0f;
                    mouseSwipeTimer = 0f;
                }
                else if (mouseSwipeTimer > 0.25f)
                {
                    mouseSwipe = 0f;
                    mouseSwipeTimer = 0f;
                }
            }

            if (direction != 0)
                SwitchTarget(direction);
        }

        /// <summary>Перевести фиксацию на соседнюю цель: +1 — вправо, -1 — влево (по углу вокруг игрока).</summary>
        public bool SwitchTarget(int direction)
        {
            Gather();
            var current = targets.IndexOf(target);
            var p = transform.position;
            var index = LockOnRules.Switch(p.x, p.z, current, candidates, direction, config.LockOnRange);
            if (index < 0)
                return false;
            SetTarget(targets[index]);
            return true;
        }

        private void SetTarget(Component value)
        {
            target = value;
            targetIsEnemy = value is EnemyController;
            lastTargetPosition = value.transform.position;
        }

        /// <summary>Перевести фиксацию на живого врага, ближайшего к точке (в радиусе LockOnRetargetRange от игрока); нет такого — снять.</summary>
        private void RetargetNear(Vector3 from)
        {
            Release();
            targets.Clear();
            candidates.Clear();
            foreach (var enemy in EnemyController.All)
                Add(enemy);
            var p = transform.position;
            var index = LockOnRules.Nearest(from.x, from.z, p.x, p.z, candidates, config.LockOnRetargetRange);
            if (index >= 0)
                SetTarget(targets[index]);
        }

        private void Gather()
        {
            targets.Clear();
            candidates.Clear();
            foreach (var enemy in EnemyController.All)
                Add(enemy);
            foreach (var dummy in FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None))
                Add(dummy);
            // Цепи разводных мостов: взять в фиксацию и прицелиться дробовиком.
            foreach (var chain in BridgeChain.All)
                Add(chain);
        }

        private void Add(Component c)
        {
            if (!IsValid(c, float.MaxValue))
                return;
            targets.Add(c);
            var position = c.transform.position;
            candidates.Add(new LockOnCandidate(position.x, position.z));
        }

        private bool IsValid(Component c, float range)
        {
            if (c == null || !c.gameObject.activeInHierarchy)
                return false;
            if (c is EnemyController enemy && enemy.Dead)
                return false;
            if (c is BridgeChain chain && !chain.Lockable)
                return false;
            var d = c.transform.position - transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= range * range;
        }
    }
}
