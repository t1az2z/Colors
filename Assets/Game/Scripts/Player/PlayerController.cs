using ColorsAction.Animation;
using ColorsAction.Combat;
using ColorsAction.Configs;
using GameCore.Config;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Управление кубом игрока: ходьба/бег относительно камеры, прыжок (с подготовкой-сжатием),
    /// серия отскоков (два коротких прыжка и перекат через верх в два переворота, i-frames) и анимация деформацией.
    /// Числа — из Configs/Player.json.
    /// <para>Иерархия: Player (BoxMotor) → Body (поворот по Y) → RollPivot (центр куба) → Mesh (CubeDeformer).</para>
    /// </summary>
    [RequireComponent(typeof(BoxMotor))]
    public sealed class PlayerController : GameBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform rollPivot;
        [SerializeField] private CubeDeformer deformer;

        private PlayerConfig config;
        private CameraConfig cameraConfig;
        private GameInput input;
        private GameSession session;
        private BoxMotor motor;
        private PlayerCombat combat;
        private PlayerLockOn lockOn;
        private Transform cameraTransform;

        private Vector3 horizontalVelocity;
        private float yaw;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private float windupTimer = -1f;
        private float airTime;

        // Отскок: логика серии (какой шаг, окно комбо, буфер, пауза) — в DodgeCombo, здесь движение и анимация.
        private DodgeCombo dodge;
        private bool rolling;
        private DodgeStepConfig dodgeStep;
        private Vector3 rollDirection;
        // Направление отскока в осях куба (x — вправо, z — вперёд): задаёт ось переворота и изгиб.
        private Vector3 rollLocal = Vector3.forward;
        private int rollHalvesLanded;
        private PlayerDamageReceiver damageReceiver;

        private int controlResumedFrame;

        /// <summary>Игрок управляет персонажем. Кадр, в котором закрыли инвентарь, пропускается, чтобы B/A не сработали дважды.</summary>
        public bool HasControl => session != null && session.PlayerControl.Value && !Frozen && !Stunned && Time.frameCount > controlResumedFrame;

        /// <summary>Оглушён статус-эффектом (<see cref="GameSession.PlayerStatus"/>): ни движения, ни атак.</summary>
        public bool Stunned => session != null && session.PlayerStatus.IsStunned;

        /// <summary>Персонаж не управляется (погиб и ждёт появления).</summary>
        public bool Frozen { get; set; }

        private Vector3 knockback;
        private IResolver resolver;

        /// <summary>Отбросить на расстояние (метры), гасится за ~0.25 с.</summary>
        public void ApplyKnockback(Vector3 distance) => knockback += distance * 4f;

        public bool Invulnerable { get; private set; }

        /// <summary>Идёт отскок (любой шаг серии).</summary>
        public bool Rolling => rolling;

        /// <summary>Индекс шага серии отскоков: текущего или последнего (-1 — ещё не было).</summary>
        public int DodgeStepIndex => dodge != null ? dodge.StepIndex : -1;
        public bool Grounded => motor != null && motor.Grounded;
        public Vector3 Forward => Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        public CubeDeformer Deformer => deformer;
        public PlayerLockOn LockOn => lockOn;
        public Transform Body => body;
        public PlayerConfig Config => config;

        protected override void OnConstruct(IResolver resolver)
        {
            this.resolver = resolver;
            config = resolver.Resolve<ConfigService>().Load<PlayerConfig>(ConfigKeys.Player);
            cameraConfig = resolver.Resolve<ConfigService>().Load<CameraConfig>(ConfigKeys.Camera);
            input = resolver.Resolve<GameInput>();
            session = resolver.Resolve<GameSession>();
            dodge = new DodgeCombo(config.Dodge);
            session.PlayerControl.Subscribe(on =>
            {
                if (on)
                    controlResumedFrame = Time.frameCount;
            }, false);
        }

        private void Start()
        {
            motor = GetComponent<BoxMotor>();
            combat = GetComponent<PlayerCombat>();
            motor.Configure(config.ColliderWidth, config.ColliderHeight, config.StepHeight);
            // Свой слой и осколки (Ignore Raycast) не мешают движению.
            motor.CollisionMask = ~((1 << gameObject.layer) | (1 << Debris.Layer));
            damageReceiver = GetComponent<PlayerDamageReceiver>();
            if (damageReceiver == null)
            {
                damageReceiver = gameObject.AddComponent<PlayerDamageReceiver>();
                damageReceiver.Construct(resolver);
            }
            lockOn = GetComponent<PlayerLockOn>();
            if (lockOn == null)
            {
                lockOn = gameObject.AddComponent<PlayerLockOn>();
                lockOn.Construct(resolver);
            }
            motor.Landed += OnLanded;
            deformer.Configure(config.Deformation.Stiffness, config.Deformation.Damping);
            deformer.Width = config.BodyWidth;
            yaw = body.eulerAngles.y;
            if (Camera.main != null)
                cameraTransform = Camera.main.transform;
            if (FindAnyObjectByType<AimMarker>() == null)
                AimMarker.Create(this, deformer.GetComponent<Renderer>()?.sharedMaterial);
        }

        /// <summary>Точка появления (старт уровня, потом — после смерти).</summary>
        public Vector3? SpawnPoint { get; private set; }
        public float SpawnYaw { get; private set; }

        /// <summary>
        /// Ниже этой высоты игрок погибает от падения (провалился за дно мира). Задаёт <see cref="LevelBuilder"/>
        /// по дну уровня (<see cref="ColorsAction.Level.LevelEdgeWalls.KillHeight(float)"/>), читает
        /// <see cref="PlayerDamageReceiver"/>. Лежит здесь, а не в приёмнике урона: тот добавляется в <see cref="Start"/>
        /// и может ещё не существовать, когда уровень строится.
        /// </summary>
        public float KillHeight { get; set; } = float.NegativeInfinity;

        public void SetSpawn(Vector3 position, float facingYaw)
        {
            SpawnPoint = position;
            SpawnYaw = facingYaw;
        }

        public void Teleport(Vector3 position, float facingYaw)
        {
            if (motor == null)
                motor = GetComponent<BoxMotor>();
            motor.Teleport(position);
            if (rolling)
                FinishRoll();
            dodge?.Reset();
            yaw = facingYaw;
            horizontalVelocity = Vector3.zero;
            leaping = false;
            if (body != null)
                body.rotation = Quaternion.Euler(0, yaw, 0);
        }

        private void Update()
        {
            if (config == null)
                return;
            var dt = Time.deltaTime;
            var control = HasControl;

            var move = control ? ReadMoveInput() : Vector3.zero;
            var running = control && UpdateRunState(move);

            if (control && input.Pressed(GameAction.Jump))
                jumpBufferTimer = config.JumpBuffer;
            else
                jumpBufferTimer -= dt;

            coyoteTimer = motor.Grounded ? config.CoyoteTime : coyoteTimer - dt;

            // Серия отскоков: нажатие во время удара, в воздухе или в конце отскока запоминается буфером
            // и срабатывает, как только станет можно; в окне комбо — следующий шаг серии.
            dodge.Tick(dt);
            if (rolling && !dodge.Active)
                UpdateRoll(1f);
            if (control && input.Pressed(GameAction.Roll))
                dodge.Press();
            if (control && dodge.CanBegin)
                TryStartRoll(move);

            if (rolling)
                UpdateRoll(dodge.Progress);
            else
                UpdateWalk(move, running, dt);

            UpdateJump(dt);
            // Интегрирование по средней скорости кадра: высота прыжка точно равна JumpHeight при любом FPS.
            var vBefore = motor.VerticalVelocity;
            ApplyGravity(dt);
            var verticalDelta = (vBefore + motor.VerticalVelocity) * 0.5f * dt;

            var lunge = combat != null ? combat.ConsumeLunge(Forward) : Vector3.zero;
            knockback = Vector3.MoveTowards(knockback, Vector3.zero, 16f * dt);
            motor.Move((horizontalVelocity + knockback) * dt + lunge, dt, verticalDelta);

            airTime = motor.Grounded ? 0f : airTime + dt;
            UpdateGroundAlign(dt);
            body.rotation = groundTilt * Quaternion.Euler(0, yaw, 0);
            UpdateDeformation(dt);
        }

        // ------------------------------------------------------------------ ходьба

        private Vector3 ReadMoveInput()
        {
            var raw = input.MoveVector;
            if (raw.sqrMagnitude < 0.01f)
                return Vector3.zero;
            raw = Vector2.ClampMagnitude(raw, 1f);
            var camYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
            return Quaternion.Euler(0, camYaw, 0) * new Vector3(raw.x, 0, raw.y);
        }

        private bool runToggled;

        /// <summary>
        /// Бег: при RunToggle кнопка (Shift / L3) включает и выключает бег, а остановка (нет ввода движения)
        /// сбрасывает его. Без RunToggle — бег, пока кнопка зажата.
        /// </summary>
        private bool UpdateRunState(Vector3 move)
        {
            if (!config.RunToggle)
                return input.Held(GameAction.Run);
            if (input.Pressed(GameAction.Run))
                runToggled = !runToggled;
            if (move.sqrMagnitude < 0.01f)
                runToggled = false;
            return runToggled;
        }

        public bool Running => runToggled;

        private void UpdateWalk(Vector3 move, bool running, float dt)
        {
            var speed = config.WalkSpeed * (running ? config.RunMultiplier : 1f);
            if (combat != null)
                speed *= combat.MoveMultiplier;

            var target = move * speed;
            var accel = move.sqrMagnitude > 0.01f ? config.Acceleration : config.Deceleration;
            if (!motor.Grounded)
                accel *= config.AirControl;
            if (leaping)
            {
                // Приземлились (или так и не оторвались от земли) — полёт закончен.
                leapTime += dt;
                if (motor.Grounded && leapTime > 0.1f)
                    leaping = false;
            }

            if (!leaping)
                horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, accel * dt);

            // Фиксация на цели: всегда лицом к цели, движение — стрейфом.
            // Иначе схема FixedAngles: взгляд и атака — по правому стику/мыши, независимо от движения.
            if (!HasControl)
                AimPoint = null;
            var locked = LockedFacing();
            if (locked.HasValue)
                AimPoint = null;
            var aim = locked ?? (HasControl ? AimDirection() : null);
            LookAim = locked.HasValue ? null : aim;
            var facing = aim ?? move;
            var turn = locked.HasValue ? lockOn.Config.LockOnTurnSpeed
                : aim.HasValue ? config.TurnSpeed * 2f : config.TurnSpeed;
            if (facing.sqrMagnitude > 0.01f && (combat == null || !combat.LockFacing))
            {
                var targetYaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, turn * dt);
            }
        }

        /// <summary>
        /// Направление взгляда с прицела (схема Б: правый стик или мышь) в этом кадре; null — прицела нет,
        /// персонаж смотрит по движению (<see cref="Forward"/>).
        /// </summary>
        public Vector3? LookAim { get; private set; }

        /// <summary>Мгновенно развернуть персонажа по горизонтали (выстрел в сторону прицела).</summary>
        public void FaceNow(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return;
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            // Сразу, а не в следующем кадре: дуло и следы пуль выстрела уже смотрят в новую сторону.
            body.rotation = groundTilt * Quaternion.Euler(0, yaw, 0);
        }

        /// <summary>Направление на цель фиксации по горизонтали; null — фиксации нет.</summary>
        private Vector3? LockedFacing()
        {
            if (lockOn == null || !lockOn.Active)
                return null;
            var to = lockOn.TargetPoint - transform.position;
            to.y = 0f;
            return to.sqrMagnitude > 0.0025f ? to.normalized : (Vector3?)null;
        }

        /// <summary>Схема Б: точка на земле, куда смотрит и бьёт персонаж (для маркера прицела). null — прицела нет.</summary>
        public Vector3? AimPoint { get; private set; }

        public bool FixedAnglesScheme => session != null && session.ControlScheme.Value == CameraScheme.FixedAngles;

        /// <summary>
        /// Схема Б (GDD, раздел 3): направление взгляда и атаки — правый стик или курсор мыши на земле.
        /// Без ввода правым стиком на геймпаде персонаж смотрит по направлению движения (возвращается null).
        /// </summary>
        private Vector3? AimDirection()
        {
            AimPoint = null;
            if (!FixedAnglesScheme || cameraConfig == null)
                return null;
            var camYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
            var stick = input.AimStick;
            if (stick.magnitude > cameraConfig.AimDeadzone)
            {
                var dir = Quaternion.Euler(0, camYaw, 0) * new Vector3(stick.x, 0, stick.y).normalized;
                AimPoint = GroundBelow(transform.position + dir * cameraConfig.StickAimMarkerDistance);
                return dir;
            }

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (input.UsingGamepad || mouse == null || cameraTransform == null)
                return null;
            var cam = cameraTransform.GetComponent<Camera>();
            if (cam == null)
                return null;
            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            Vector3 point;
            // Сначала по геометрии уровня (маркер ложится на ступени), иначе — плоскость на высоте игрока.
            // Луч идёт по изогнутой картинке мира (CurvedWorld), а проверяется в плоском.
            // Невидимые стены по краю карты курсор не ловят: камера часто стоит за ними.
            if (CurvedWorld.Raycast(ray, 200f, motor.CollisionMask, out var hit, EdgeWall.Is))
                point = hit.point;
            else if (!CurvedWorld.RaycastPlane(ray, transform.position.y, 200f, out point))
                return null;

            AimPoint = point;
            var flat = point - transform.position;
            flat.y = 0;
            return flat.sqrMagnitude > 0.04f ? flat.normalized : (Vector3?)null;
        }

        private Vector3 GroundBelow(Vector3 position)
        {
            var origin = position + Vector3.up * 3f;
            return Physics.Raycast(origin, Vector3.down, out var hit, 10f, motor.CollisionMask, QueryTriggerInteraction.Ignore)
                ? hit.point
                : position;
        }

        // ------------------------------------------------------------------ прыжок

        private void UpdateJump(float dt)
        {
            if (windupTimer >= 0f)
            {
                windupTimer -= dt;
                deformer.SquashTarget = config.Deformation.JumpWindupSquash;
                if (windupTimer < 0f)
                {
                    motor.VerticalVelocity = config.JumpVelocity;
                    deformer.SetSquash(config.Deformation.JumpTakeoffStretch, 4f);
                }

                return;
            }

            if (rolling || jumpBufferTimer <= 0f || coyoteTimer <= 0f)
                return;
            if (combat != null && combat.IsBusy)
                return;
            if (!session.Stamina.TrySpend(config.Stamina.JumpCost))
                return;

            jumpBufferTimer = 0f;
            coyoteTimer = 0f;
            if (config.JumpWindup > 0f)
            {
                windupTimer = config.JumpWindup;
                deformer.SetSquash(config.Deformation.JumpWindupSquash * 0.6f);
            }
            else
            {
                motor.VerticalVelocity = config.JumpVelocity;
                deformer.SetSquash(config.Deformation.JumpTakeoffStretch, 4f);
            }
        }

        public bool InJumpWindup => windupTimer >= 0f;

        /// <summary>
        /// Прыжок удара (отскок добивающего): подскок на <paramref name="height"/> и полёт на <paramref name="distance"/>
        /// по горизонтали. Скорость полёта держится до приземления — попадания, фриз-кадры и ввод её не гасят.
        /// </summary>
        public void Leap(float height, Vector3 distance)
        {
            if (height <= 0f || motor == null)
                return;
            var g = config.Gravity;
            var up = Mathf.Sqrt(2f * height / g);
            var down = Mathf.Sqrt(2f * height / (g * Mathf.Max(0.01f, config.FallGravityMultiplier)));
            distance.y = 0f;
            motor.VerticalVelocity = Mathf.Sqrt(2f * g * height);
            horizontalVelocity = distance / (up + down);
            leaping = true;
            leapTime = 0f;
        }

        // Полёт прыжка удара: горизонтальная скорость не меняется до приземления.
        private bool leaping;
        private float leapTime;

        private void ApplyGravity(float dt)
        {
            if (windupTimer >= 0f)
            {
                motor.VerticalVelocity = 0f;
                return;
            }

            var g = config.Gravity * (motor.VerticalVelocity < 0f ? config.FallGravityMultiplier : 1f);
            motor.VerticalVelocity = Mathf.Max(motor.VerticalVelocity - g * dt, -config.MaxFallSpeed);
        }

        private void OnLanded()
        {
            if (leaping && leapTime > 0f)
                leaping = false;
            damageReceiver?.ReceiveFallDamage(motor.LastFallHeight);
            if (rolling)
                return;
            var d = config.Deformation;
            var squash = Mathf.Max(d.MaxLandSquash, d.LandSquashPerSpeed * motor.LastLandingSpeed);
            if (airTime > 0.08f)
                deformer.SetSquash(Mathf.Min(deformer.Squash, squash), -2f);
        }

        // ------------------------------------------------------------------ отскок

        private bool TryStartRoll(Vector3 move)
        {
            if (!motor.Grounded || windupTimer >= 0f)
                return false;
            if (combat != null && !combat.CanInterruptForRoll)
                return false;
            if (!session.Stamina.TrySpend(dodge.PendingStep.StaminaCost))
                return false;

            // Без направления движения — отскок назад, лицом к прежнему направлению.
            // С фиксацией на цели куб остаётся лицом к цели и прыгает в сторону ввода: вбок — через бок, назад — назад.
            var backward = move.sqrMagnitude <= 0.01f;
            rollDirection = backward ? -Forward : move.normalized;
            var locked = lockOn != null && lockOn.Active;
            if (!backward && !locked)
                yaw = Mathf.Atan2(rollDirection.x, rollDirection.z) * Mathf.Rad2Deg;
            rollLocal = Quaternion.Inverse(Quaternion.Euler(0, yaw, 0)) * rollDirection;
            rollLocal.y = 0f;
            // Только через грани куба: вперёд, назад, влево, вправо — без перекатов через рёбра по диагонали.
            rollLocal = SnapToAxis(rollLocal);
            rollDirection = Quaternion.Euler(0, yaw, 0) * rollLocal;
            dodgeStep = config.Dodge.Steps[dodge.Begin()];
            rolling = true;
            leaping = false;
            rollHalvesLanded = 0;
            rollPivot.localRotation = Quaternion.identity;
            combat?.CancelAttack();
            // Подготовка к прыжку: сжатие, изгиб и сжатие в сторону отскока.
            // Короткий прыжок: мгновенно сплющен и сразу распрямляется в вытягивание; перекат — толчок пружины вниз.
            var takeoff = dodgeStep.TakeoffSquash < 0f;
            if (takeoff)
                deformer.SetSquash(dodgeStep.TakeoffSquash, 2f);
            deformer.Impulse(takeoff ? 0f : -1.5f, RollPlane * (dodgeStep.Bend * 12f));
            deformer.ImpulseDirectional(RollPlane, -dodgeStep.DirSquash * 14f);
            return true;
        }

        /// <summary>Поза отскока в момент <paramref name="t"/> (доля шага 0..1); при t ≥ 1 — приземление и конец.</summary>
        private void UpdateRoll(float t)
        {
            var step = dodgeStep;
            var pose = step.Sample(t);
            Invulnerable = dodge.Invulnerable;

            // Перекат: прыжок-переворот 1 → стоит "на голове" (пауза, сплющен) → прыжок-переворот 2 → на "ноги".
            // Короткий отскок: один прыжок без переворота.
            horizontalVelocity = rollDirection * pose.Speed;

            var u = pose.PhaseProgress;
            var arc = Mathf.Sin(u * Mathf.PI);
            if (pose.Phase == 1)
            {
                deformer.BendTarget = Vector2.zero;
                deformer.DirSquashTarget = 0f;
                deformer.SquashTarget = step.LandSquash;
            }
            else
            {
                deformer.BendTarget = RollPlane * (arc * step.Bend);
                // Короткий прыжок вытягивается в полёте; перекат держит форму.
                deformer.SquashTarget = arc * step.Stretch;
                // В полёте куб чуть сжат вдоль направления отскока.
                deformer.DirSquashAxis = RollPlane;
                deformer.DirSquashTarget = -arc * step.DirSquash;
            }

            // Переворот вокруг оси, перпендикулярной направлению: вперёд — через "лицо",
            // назад — через спину, вбок (с фиксацией) — через бок. Куб при этом смотрит туда же, куда смотрел.
            rollPivot.localRotation = Quaternion.AngleAxis(pose.Angle, Vector3.Cross(Vector3.up, rollLocal));
            rollPivot.localPosition = new Vector3(0, config.ColliderHeight * 0.5f + pose.Hop, 0);

            // Удары о землю: приземление на "голову", толчок во второй прыжок, приземление на "ноги".
            if (pose.Stage > rollHalvesLanded)
            {
                if (pose.Stage == 1 || pose.Stage == 3)
                    deformer.SetSquash(step.LandSquash * 1.4f, -1.5f);
                else if (pose.Stage == 2)
                    deformer.SetSquash(config.Deformation.JumpTakeoffStretch, 3f);
                rollHalvesLanded = pose.Stage;
            }

            if (t >= 1f)
                FinishRoll();
        }

        private void FinishRoll()
        {
            rolling = false;
            Invulnerable = false;
            rollPivot.localRotation = Quaternion.identity;
            rollPivot.localPosition = new Vector3(0, config.ColliderHeight * 0.5f, 0);
            horizontalVelocity *= 0.1f;
            deformer.BendTarget = Vector2.zero;
            deformer.SquashTarget = 0f;
            deformer.DirSquashTarget = 0f;
        }

        /// <summary>Направление отскока в осях изгиба куба (x — вбок, y — вперёд).</summary>
        private Vector2 RollPlane => new Vector2(rollLocal.x, rollLocal.z);

        /// <summary>Направление текущего отскока в осях куба (для автотестов).</summary>
        public Vector3 RollLocalDirection => rollLocal;

        /// <summary>Ближайшая из четырёх осей куба (±X, ±Z) на плоскости.</summary>
        private static Vector3 SnapToAxis(Vector3 v)
        {
            if (v.sqrMagnitude < 0.0001f)
                return Vector3.forward;
            return Mathf.Abs(v.x) > Mathf.Abs(v.z)
                ? new Vector3(Mathf.Sign(v.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        // ------------------------------------------------------------------ слопы

        private Quaternion groundTilt = Quaternion.identity;
        private float groundOffset;

        /// <summary>
        /// На слопе куб ложится на поверхность: наклон по нормали и опускание до высоты под центром
        /// (коллизия — выровненный бокс, он касается слопа только верхней кромкой).
        /// </summary>
        private void UpdateGroundAlign(float dt)
        {
            var targetTilt = Quaternion.identity;
            var targetOffset = 0f;
            if (motor.Grounded && !rolling)
            {
                var origin = transform.position + Vector3.up * (config.ColliderHeight * 0.5f);
                if (Physics.Raycast(origin, Vector3.down, out var hit, config.ColliderHeight, motor.CollisionMask,
                        QueryTriggerInteraction.Ignore) && hit.normal.y > 0.7f)
                {
                    targetTilt = Quaternion.FromToRotation(Vector3.up, hit.normal);
                    targetOffset = Mathf.Clamp(hit.point.y - transform.position.y, -config.StepHeight, 0f);
                }
            }

            var k = 1f - Mathf.Exp(-dt * 18f);
            groundTilt = Quaternion.Slerp(groundTilt, targetTilt, k);
            groundOffset = Mathf.Lerp(groundOffset, targetOffset, k);
            body.localPosition = new Vector3(0f, groundOffset, 0f);
        }

        // ------------------------------------------------------------------ деформация

        /// <summary>
        /// Форма куба каждый кадр. Цикл шагов (покачивание, сжатие на шаге и вытягивание между шагами) и дыхание —
        /// прямые добавки <see cref="CubeDeformer.DirectSquash"/>/<see cref="CubeDeformer.DirectBend"/>; осмотр по
        /// сторонам — добавки к скручиванию и поклону; глаза — взгляд и моргание. Числа — Configs/Animations.json → "player".
        /// Отклик на прыжки, приземления, удары и перекаты — пружины деформера (Player.json → "deformation").
        /// </summary>
        private void UpdateDeformation(float dt)
        {
            EnsureAnimation();
            UpdateIdle(dt);
            var speed = horizontalVelocity.magnitude;
            steps.Tick(dt, speed, config.WalkSpeed, config.WalkSpeed * config.RunMultiplier,
                motor.Grounded && !rolling && windupTimer < 0f && !leaping);
            deformer.DirectBend = new Vector2(steps.Sway, 0f);
            deformer.DirectSquash = steps.Squash + idle.BreathSquash;
            deformer.TwistOffset = idle.Twist;
            deformer.PitchOffset = idle.Pitch;
            deformer.EyeOffset = new Vector2(idle.EyeX, idle.EyeY);
            deformer.EyeBlink = idle.Blink;
            deformer.EyeClosedScale = animConfig.Blink.Closed;
            if (rolling)
                return;

            if (windupTimer < 0f)
            {
                // На земле форму ведёт цикл шагов; в воздухе чуть вытянут при подъёме и сплющен при падении.
                deformer.BendTarget = Vector2.zero;
                deformer.SquashTarget = motor.Grounded ? 0f : Mathf.Clamp(motor.VerticalVelocity * 0.015f, -0.08f, 0.12f);
            }

            // Наклон вперёд по ходу движения; на бегу сильнее (скорость больше).
            var local = Quaternion.Inverse(body.rotation) * horizontalVelocity;
            deformer.LeanTarget = new Vector2(local.x, local.z) * animConfig.Move.Lean / Mathf.Max(0.1f, config.WalkSpeed);
        }

        // ------------------------------------------------------------------ анимации покоя

        private CubeAnimationConfig animConfig;
        private StepCycle steps;
        private CubeIdleAnimator idle;
        private float idleYaw;

        /// <summary>Анимации покоя (дыхание, моргание, осмотр): для автотестов и снимков.</summary>
        public CubeIdleAnimator Idle
        {
            get
            {
                EnsureAnimation();
                return idle;
            }
        }

        /// <summary>Цикл шагов (покачивание и сжатие-вытягивание): для автотестов и снимков.</summary>
        public StepCycle Steps
        {
            get
            {
                EnsureAnimation();
                return steps;
            }
        }

        /// <summary>Прервать анимации покоя (по игроку ударили).</summary>
        public void InterruptIdle() => idle?.Interrupt();

        private void EnsureAnimation()
        {
            if (idle != null)
                return;
            ConfigTable<CubeAnimationConfig> table = null;
            var configs = resolver?.Resolve<ConfigService>();
            if (configs != null && configs.Exists(CubeAnimationConfig.Key))
                table = configs.LoadTable<CubeAnimationConfig>(CubeAnimationConfig.Key);
            animConfig = CubeAnimationConfig.Resolve(table, CubeAnimationConfig.PlayerId, CubeAnimationConfig.PlayerId);
            steps = new StepCycle(animConfig.Move);
            idle = new CubeIdleAnimator(animConfig, GetHashCode());
            idleYaw = yaw;
        }

        /// <summary>
        /// Покой: стоит на земле и ничего не делает (не идёт, не бьёт, не блокирует, не в отскоке и не в прыжке) — дышит,
        /// моргает, через несколько секунд осматривается. Любое действие (нажатие, ввод движения, поворот, фиксация на
        /// цели, колесо оружия, удар по игроку) прерывает осмотр и моргание и обнуляет счётчик покоя.
        /// </summary>
        private void UpdateIdle(float dt)
        {
            var standing = !Frozen && !Stunned && motor.Grounded && !rolling && windupTimer < 0f && !leaping
                           && horizontalVelocity.sqrMagnitude < 0.04f && knockback.sqrMagnitude < 0.01f
                           && (combat == null || combat.Calm);
            var turned = Mathf.Abs(Mathf.DeltaAngle(idleYaw, yaw)) > 0.5f;
            idleYaw = yaw;
            var action = turned || WeaponWheel.IsOpen || (lockOn != null && lockOn.Active) || (HasControl && ActionPressed());
            idle.Tick(dt, standing, action);
        }

        private static readonly GameAction[] IdleBreakers =
        {
            GameAction.Jump, GameAction.Roll, GameAction.Primary, GameAction.Secondary, GameAction.Interact,
            GameAction.Run, GameAction.LockOn, GameAction.Wheel, GameAction.Inventory
        };

        private bool ActionPressed()
        {
            if (input == null)
                return false;
            if (input.MoveVector.sqrMagnitude > 0.01f)
                return true;
            foreach (var a in IdleBreakers)
                if (input.Pressed(a) || input.Held(a))
                    return true;
            return false;
        }
    }
}
