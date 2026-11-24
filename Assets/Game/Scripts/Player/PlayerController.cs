using ColorsAction.Combat;
using ColorsAction.Configs;
using GameCore.Config;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Управление кубом игрока: ходьба/бег относительно камеры, прыжок (с подготовкой-сжатием),
    /// перекат (два переворота по 180°, i-frames) и анимация деформацией. Числа — из Configs/Player.json.
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
        private float swayPhase;
        private float airTime;

        private bool rolling;
        private float rollTime;
        private float rollCooldown;
        private float rollBufferTimer;
        private Vector3 rollDirection;
        // Направление переката в осях куба (x — вправо, z — вперёд): задаёт ось переворота и изгиб.
        private Vector3 rollLocal = Vector3.forward;
        private int rollHalvesLanded;

        private int controlResumedFrame;

        /// <summary>Игрок управляет персонажем. Кадр, в котором закрыли инвентарь, пропускается, чтобы B/A не сработали дважды.</summary>
        public bool HasControl => session != null && session.PlayerControl.Value && !Frozen && Time.frameCount > controlResumedFrame;

        /// <summary>Персонаж не управляется (погиб и ждёт появления).</summary>
        public bool Frozen { get; set; }

        private Vector3 knockback;
        private IResolver resolver;

        /// <summary>Отбросить на расстояние (метры), гасится за ~0.25 с.</summary>
        public void ApplyKnockback(Vector3 distance) => knockback += distance * 4f;

        public bool Invulnerable { get; private set; }
        public bool Rolling => rolling;
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
            if (GetComponent<PlayerDamageReceiver>() == null)
                gameObject.AddComponent<PlayerDamageReceiver>().Construct(resolver);
            lockOn = GetComponent<PlayerLockOn>();
            if (lockOn == null)
            {
                lockOn = gameObject.AddComponent<PlayerLockOn>();
                lockOn.Construct(resolver);
            }
            motor.Landed += OnLanded;
            deformer.Configure(config.Deformation.Stiffness, config.Deformation.Damping);
            yaw = body.eulerAngles.y;
            if (Camera.main != null)
                cameraTransform = Camera.main.transform;
            if (FindAnyObjectByType<AimMarker>() == null)
                AimMarker.Create(this, deformer.GetComponent<Renderer>()?.sharedMaterial);
        }

        /// <summary>Точка появления (старт уровня, потом — после смерти).</summary>
        public Vector3? SpawnPoint { get; private set; }
        public float SpawnYaw { get; private set; }

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
            yaw = facingYaw;
            horizontalVelocity = Vector3.zero;
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
            rollCooldown -= dt;

            // Буфер переката: нажатие во время удара или в воздухе срабатывает, как только станет можно.
            if (control && input.Pressed(GameAction.Roll))
                rollBufferTimer = config.JumpBuffer * 1.5f;
            else
                rollBufferTimer -= dt;
            if (control && !rolling && rollBufferTimer > 0f && TryStartRoll(move))
                rollBufferTimer = 0f;

            if (rolling)
                UpdateRoll(dt);
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
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, accel * dt);

            // Фиксация на цели: всегда лицом к цели, движение — стрейфом.
            // Иначе схема FixedAngles: взгляд и атака — по правому стику/мыши, независимо от движения.
            if (!HasControl)
                AimPoint = null;
            var locked = LockedFacing();
            if (locked.HasValue)
                AimPoint = null;
            var aim = locked ?? (HasControl ? AimDirection() : null);
            var facing = aim ?? move;
            var turn = locked.HasValue ? lockOn.Config.LockOnTurnSpeed
                : aim.HasValue ? config.TurnSpeed * 2f : config.TurnSpeed;
            if (facing.sqrMagnitude > 0.01f && (combat == null || !combat.LockFacing))
            {
                var targetYaw = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
                yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, turn * dt);
            }
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
            if (Physics.Raycast(ray, out var hit, 500f, motor.CollisionMask, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
            }
            else
            {
                var plane = new Plane(Vector3.up, transform.position);
                if (!plane.Raycast(ray, out var enter))
                    return null;
                point = ray.GetPoint(enter);
            }

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
            if (rolling)
                return;
            var d = config.Deformation;
            var squash = Mathf.Max(d.MaxLandSquash, d.LandSquashPerSpeed * motor.LastLandingSpeed);
            if (airTime > 0.08f)
                deformer.SetSquash(Mathf.Min(deformer.Squash, squash), -2f);
        }

        // ------------------------------------------------------------------ перекат

        private bool TryStartRoll(Vector3 move)
        {
            if (rollCooldown > 0f || !motor.Grounded || windupTimer >= 0f)
                return false;
            if (combat != null && !combat.CanInterruptForRoll)
                return false;
            if (!session.Stamina.TrySpend(config.Stamina.RollCost))
                return false;

            // Без направления движения — кувырок назад (отскок), лицом к прежнему направлению.
            // С фиксацией на цели куб остаётся лицом к цели и катится в сторону ввода: вбок — через бок, назад — назад.
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
            rolling = true;
            rollTime = 0f;
            rollHalvesLanded = 0;
            combat?.CancelAttack();
            // Изгиб и сжатие в сторону переката перед прыжком.
            deformer.Impulse(-1.5f, RollPlane * (config.Deformation.RollBend * 12f));
            deformer.ImpulseDirectional(RollPlane, -config.Deformation.RollDirSquash * 14f);
            return true;
        }

        private void UpdateRoll(float dt)
        {
            rollTime += dt;
            var t = Mathf.Clamp01(rollTime / config.RollDuration);
            Invulnerable = t >= config.RollInvulnerableFrom && t <= config.RollInvulnerableTo;

            // Фазы: прыжок-переворот 1 → стоит "на голове" (пауза, сплющен) → прыжок-переворот 2 → на "ноги".
            var pause = config.RollMidPause;
            var hopLength = (1f - pause) * 0.5f;
            int phase;
            float u;
            if (t < hopLength)
            {
                phase = 0;
                u = t / hopLength;
            }
            else if (t < hopLength + pause)
            {
                phase = 1;
                u = (t - hopLength) / Mathf.Max(0.0001f, pause);
            }
            else
            {
                phase = 2;
                u = (t - hopLength - pause) / hopLength;
            }

            // Скорость: в полёте быстро, в паузе почти стоит — так перекат читается как два движения.
            // Средняя скорость подобрана так, чтобы за весь перекат пройти ровно RollDistance.
            var average = (1f - pause) + pause * config.RollMidSpeed;
            var baseSpeed = config.RollDistance / config.RollDuration / Mathf.Max(0.05f, average);
            horizontalVelocity = rollDirection * baseSpeed * (phase == 1 ? config.RollMidSpeed : 1f);

            var d = config.Deformation;
            float angle;
            float hop;
            if (phase == 1)
            {
                angle = 180f;
                hop = 0f;
                deformer.BendTarget = Vector2.zero;
                deformer.DirSquashTarget = 0f;
                deformer.SquashTarget = d.RollLandSquash;
            }
            else
            {
                angle = (phase == 2 ? 180f : 0f) + EaseInOut(u) * 180f;
                hop = Mathf.Sin(u * Mathf.PI) * config.RollHopHeight;
                deformer.BendTarget = RollPlane * (Mathf.Sin(u * Mathf.PI) * d.RollBend);
                deformer.SquashTarget = 0f;
                // В полёте куб чуть сжат вдоль направления переката.
                deformer.DirSquashAxis = RollPlane;
                deformer.DirSquashTarget = -Mathf.Sin(u * Mathf.PI) * d.RollDirSquash;
            }

            // Переворот вокруг оси, перпендикулярной направлению переката: вперёд — через "лицо",
            // назад — через спину, вбок (с фиксацией) — через бок. Куб при этом смотрит туда же, куда смотрел.
            rollPivot.localRotation = Quaternion.AngleAxis(angle, Vector3.Cross(Vector3.up, rollLocal));
            rollPivot.localPosition = new Vector3(0, config.ColliderHeight * 0.5f + hop, 0);

            // Удары о землю: приземление на "голову", толчок во второй прыжок, приземление на "ноги".
            var stage = phase == 0 ? 0 : phase == 1 ? 1 : t >= 1f ? 3 : 2;
            if (stage > rollHalvesLanded)
            {
                if (stage == 1 || stage == 3)
                    deformer.SetSquash(d.RollLandSquash * 1.4f, -1.5f);
                else if (stage == 2)
                    deformer.SetSquash(d.JumpTakeoffStretch, 3f);
                rollHalvesLanded = stage;
            }

            if (t >= 1f)
            {
                rolling = false;
                Invulnerable = false;
                rollCooldown = config.RollCooldown;
                rollPivot.localRotation = Quaternion.identity;
                rollPivot.localPosition = new Vector3(0, config.ColliderHeight * 0.5f, 0);
                horizontalVelocity *= 0.1f;
                deformer.BendTarget = Vector2.zero;
                deformer.SquashTarget = 0f;
                deformer.DirSquashTarget = 0f;
            }
        }

        /// <summary>Направление переката в осях изгиба куба (x — вбок, y — вперёд).</summary>
        private Vector2 RollPlane => new Vector2(rollLocal.x, rollLocal.z);

        /// <summary>Направление текущего переката в осях куба (для автотестов).</summary>
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

        private static float EaseInOut(float x) => x * x * (3f - 2f * x);

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

        private void UpdateDeformation(float dt)
        {
            if (rolling)
                return;
            var d = config.Deformation;
            var local = Quaternion.Inverse(body.rotation) * horizontalVelocity;
            var speed01 = Mathf.Clamp01(horizontalVelocity.magnitude / (config.WalkSpeed * config.RunMultiplier));

            if (windupTimer < 0f)
            {
                // На земле: покачивание из стороны в сторону и лёгкое "подпрыгивание" в такт шагу.
                // Верх отстаёт от низа: наклон против скорости.
                if (motor.Grounded && speed01 > 0.05f)
                {
                    swayPhase += horizontalVelocity.magnitude * d.WalkSwayPerMeter * Mathf.PI * dt;
                    deformer.BendTarget = new Vector2(Mathf.Sin(swayPhase) * d.WalkBend * speed01, 0f);
                    deformer.SquashTarget = -Mathf.Abs(Mathf.Sin(swayPhase)) * d.WalkBob * speed01;
                }
                else
                {
                    deformer.BendTarget = Vector2.zero;
                    // В воздухе чуть вытянут при подъёме и сплющен при падении.
                    deformer.SquashTarget = motor.Grounded ? 0f : Mathf.Clamp(motor.VerticalVelocity * 0.015f, -0.08f, 0.12f);
                }
            }

            // Наклон вперёд по ходу движения; на бегу сильнее (скорость больше).
            var lean = new Vector2(local.x, local.z) * d.MoveLean / Mathf.Max(0.1f, config.WalkSpeed);
            deformer.LeanTarget = lean;
        }
    }
}
