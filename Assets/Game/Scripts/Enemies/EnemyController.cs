using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Level.Navigation;
using ColorsAction.Status;
using GameCore.Config;
using GameCore.DI;
using GameCore.Events;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Враг-куб с оружием (первый — синий куб с дубиной). Решения принимает <see cref="EnemyBrain"/> (логика без движка),
    /// здесь — тело: движение на <see cref="BoxMotor"/>, деформация, позы дубины, попадания, реакция на урон,
    /// трещины и смерть с разлётом на осколки, часть которых летит к игроку как опыт.
    /// </summary>
    public sealed class EnemyController : GameBehaviour, IHittable, IStatusHolder
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly List<EnemyController> all = new List<EnemyController>();
        private static int nextId = 1;

        public static IReadOnlyList<EnemyController> All => all;

        private string typeId;
        private float aggroRadius = LevelEnemy.DefaultAggroRadius;

        /// <summary>Проверка "дойдёт ли до игрока" (воксельные уровни); null — не проверяем.</summary>
        private EnemyReach reach;

        private MovementProfile movement;

        /// <summary>Ведёт врага по карте пути (A*); null — без карты, враг идёт к игроку напрямую.</summary>
        private EnemyNavigator navigator;
        private Mesh bodyMesh;
        private Material[] bodyMaterials;

        private EnemyConfig config;
        private EnemyBrain brain;
        private GameSession session;
        private Palette palette;
        private IEventBus events;
        private PlayerController player;
        private PlayerDamageReceiver playerDamage;
        private PlayerConfig playerConfig;

        private BoxMotor motor;
        private Transform body;
        private Transform pivot;
        private CubeDeformer deformer;
        private Transform hand;
        private Renderer bodyRenderer;
        private MaterialPropertyBlock block;
        private AlertMarker marker;
        private PoiseBar poiseBar;
        private float hitShake;
        private CrackVisual cracks;
        private Color color;

        private float yaw;
        private Vector3 velocity;
        private Vector3 push;
        private float flash;
        private float swayPhase;
        private bool hitThisAttack;
        private bool strikeEndedThisFrame;
        private float lungeDone;
        private Pose handPose = new Pose(new Vector3(0.66f, 0.4f, 0.1f), Quaternion.identity);
        private Pose windupFrom;
        private float hopTimer = -1f;

        public EnemyBrain Brain => brain;

        /// <summary>Статус-эффекты врага (живут в <see cref="EnemyBrain.Status"/>).</summary>
        public StatusEffects Status => brain?.Status;
        public string ColorId => config != null ? config.Color : "blue";
        public Vector3 HitPoint => transform.position + Vector3.up * (config != null ? config.Size * 0.6f : 0.5f);
        public bool Dead => brain != null && brain.State == EnemyState.Dead;

        /// <summary>Id двери на ключ, ключ от которой враг роняет при смерти (задаёт <see cref="LevelBuilder"/>); null — без ключа.</summary>
        public string KeyDoor { get; set; }

        /// <summary>Враг погиб (до разлёта на осколки): позиция ещё на месте смерти.</summary>
        public event System.Action<EnemyController> Died;

        /// <summary>Создать врага на сцене. Тело — тот же скруглённый куб с глазами, что у игрока, другого цвета и размера.</summary>
        /// <param name="aggroRadius">Радиус агро этого врага (из уровня).</param>
        /// <param name="reach">Проверка достижимости игрока; null — враг замечает игрока по одной дистанции.</param>
        public static EnemyController Spawn(string type, Vector3 position, float yaw, Transform parent, GameBootstrap bootstrap,
            Mesh mesh, Material[] materials, float aggroRadius = LevelEnemy.DefaultAggroRadius, EnemyReach reach = null)
        {
            var go = new GameObject($"Enemy_{type}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.layer = EnemyLayer;
            var enemy = go.AddComponent<EnemyController>();
            enemy.typeId = type;
            enemy.bodyMesh = mesh;
            enemy.bodyMaterials = materials;
            enemy.yaw = yaw;
            enemy.aggroRadius = aggroRadius;
            enemy.reach = reach;
            bootstrap.Inject(go);
            return enemy;
        }

        /// <summary>Профили перемещения (Configs/Movement.json).</summary>
        private const string MovementProfileKey = "Movement";

        /// <summary>Слой врагов (создаётся сборщиком сцены). Если его нет — слой по умолчанию.</summary>
        public static int EnemyLayer
        {
            get
            {
                var layer = LayerMask.NameToLayer("Enemy");
                return layer >= 0 ? layer : 0;
            }
        }

        protected override void OnConstruct(IResolver resolver)
        {
            var configs = resolver.Resolve<ConfigService>();
            if (!configs.LoadTable<EnemyConfig>(ConfigKeys.Enemies).TryGet(typeId ?? string.Empty, out config))
            {
                // Опечатка или переименованный тип в уровне: не роняем остальных врагов, просто не создаём этого.
                Debug.LogError($"[Enemy] Unknown enemy type '{typeId}' at {transform.position} — not in Enemies.json, skipped.");
                config = null;
                Destroy(gameObject);
                return;
            }

            playerConfig = configs.Load<PlayerConfig>(ConfigKeys.Player);
            session = resolver.Resolve<GameSession>();
            palette = resolver.Resolve<Palette>();
            events = resolver.Resolve<IEventBus>();
            var id = nextId++;
            var combat = configs.Load<CombatConfig>(ConfigKeys.Combat);
            hitShake = combat?.HitShake ?? 0f;
            brain = new EnemyBrain(id, config, resolver.Resolve<CombatDirector>(), id * 7919, session.StatusConfig, aggroRadius,
                combat?.Poise);
            if (reach != null)
            {
                var profiles = configs.LoadTable<MovementProfile>(MovementProfileKey);
                if (!profiles.TryGet(config.Movement ?? string.Empty, out movement))
                    Debug.LogWarning($"[Enemy] '{typeId}': movement profile '{config.Movement}' not in Movement.json — reachability not checked.");
                else
                    navigator = new EnemyNavigator(reach, movement);
            }

            brain.StateChanged += OnStateChanged;
            BuildBody();
            if (isActiveAndEnabled && !all.Contains(this))
                all.Add(this);
        }

        // В общий список попадает только полностью собранный враг (с конфигом и мозгом).
        private void OnEnable()
        {
            if (brain != null && !all.Contains(this))
                all.Add(this);
        }

        private void OnDisable() => all.Remove(this);

        // ------------------------------------------------------------------ тело

        private void BuildBody()
        {
            var size = config.Size;
            motor = gameObject.AddComponent<BoxMotor>();
            motor.Configure(0.9f * size, size, 0.3f);
            // Не сталкиваемся с другими врагами (их разводит мягкое отталкивание) и с осколками.
            motor.CollisionMask = ~((1 << EnemyLayer) | (1 << Debris.Layer));

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            pivot = new GameObject("Pivot").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(0, size * 0.5f, 0);
            var meshGo = new GameObject("Mesh");
            meshGo.layer = gameObject.layer;
            meshGo.transform.SetParent(pivot, false);
            meshGo.transform.localPosition = new Vector3(0, -size * 0.5f, 0);
            meshGo.transform.localScale = Vector3.one * size;
            meshGo.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
            bodyRenderer = meshGo.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterials = bodyMaterials;
            deformer = meshGo.AddComponent<CubeDeformer>();
            deformer.Configure(200f, 9f);

            color = palette.Get(config.Color);
            block = new MaterialPropertyBlock();
            ApplyColor();

            hand = new GameObject("Hand").transform;
            hand.SetParent(meshGo.transform, false);
            var prefab = Resources.Load<GameObject>(config.WeaponPrefab);
            if (prefab != null)
            {
                var weapon = Instantiate(prefab, hand, false);
                foreach (var c in weapon.GetComponentsInChildren<Collider>())
                    Destroy(c);
            }

            cracks = meshGo.AddComponent<CrackVisual>();
            cracks.Init(bodyRenderer, deformer, color);
            marker = AlertMarker.Create(transform, size + 0.75f);
            if (brain.Poise.Max > 0f)
                poiseBar = PoiseBar.Create(transform, size + 0.32f, Mathf.Max(0.7f, size * 0.95f));
            body.rotation = Quaternion.Euler(0, yaw, 0);
        }

        private void ApplyColor()
        {
            var white = Mathf.Clamp01(flash / 0.12f);
            var tint = StatusTint.Glow(brain?.Status, Time.time);
            bodyRenderer.GetPropertyBlock(block, 0);
            var baseColor = Color.Lerp(color, new Color(tint.r, tint.g, tint.b), Mathf.Clamp01(tint.a) * 0.35f);
            block.SetColor(BaseColorId, Color.Lerp(baseColor, Color.white, white * 0.85f));
            block.SetColor(EmissionColorId, Color.white * (white * 1.2f) + new Color(tint.r, tint.g, tint.b) * tint.a);
            bodyRenderer.SetPropertyBlock(block, 0);
        }

        private bool tinted;

        // ------------------------------------------------------------------ кадр

        private void Update()
        {
            if (brain == null)
                return;
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerController>();
                playerDamage = player != null ? player.GetComponent<PlayerDamageReceiver>() : null;
                playerCombat = player != null ? player.GetComponent<PlayerCombat>() : null;
            }

            var dt = Time.deltaTime;
            var toPlayer = player != null ? player.transform.position - transform.position : Vector3.forward * 1000f;
            var vertical = Mathf.Abs(toPlayer.y);
            toPlayer.y = 0f;
            var distance = toPlayer.magnitude;
            var available = player != null && (playerDamage == null || !playerDamage.Dead) && vertical < 3f;

            // Путь к игроку по карте (A*): пока враг обходит препятствие, до игрока «далеко» по пути —
            // так он не бьёт через пропасть или стену. Предел — чуть меньше радиуса потери, чтобы не терять игрока из-за обхода.
            var chaseSteer = ChaseSteer(dt, available);
            var brainDistance = chaseSteer.Mode == NavSteerMode.Path
                ? Mathf.Max(distance, Mathf.Min(chaseSteer.Remaining, brain.LoseRadius - 0.05f))
                : distance;
            var intent = brain.Tick(dt, brainDistance, available, PlayerReachable(available, distance));
            var dir = distance > 0.001f ? toPlayer / distance : transform.forward;

            // Движение: к игроку / от игрока, а в очереди — к точке за спиной игрока; по пути A*, если прямо не пройти;
            // плюс мягкое расталкивание с соседями и отбрасывание от ударов.
            var pathing = false;
            Vector3 desired;
            if (intent.Flank)
                desired = FlankVelocity(intent, toPlayer, distance, dt, ref pathing);
            else if (intent.MoveSpeed > 0f && navigator != null && chaseSteer.Mode != NavSteerMode.Direct)
            {
                pathing = true;
                desired = SteerVelocity(chaseSteer, intent.MoveSpeed);
            }
            else
                desired = dir * intent.MoveSpeed;

            desired = GuardLedges(desired + Separation());
            velocity = Vector3.MoveTowards(velocity, desired, 30f * dt);
            push = Vector3.MoveTowards(push, Vector3.zero, 6f * dt);
            var lunge = Lunge(dir);

            var vBefore = motor.VerticalVelocity;
            motor.VerticalVelocity = Mathf.Max(motor.VerticalVelocity - 30f * (motor.VerticalVelocity < 0 ? 1.4f : 1f) * dt, -25f);
            motor.Move((velocity + push) * dt + lunge, dt, (vBefore + motor.VerticalVelocity) * 0.5f * dt);

            if (hopTimer >= 0f)
            {
                hopTimer -= dt;
                if (hopTimer < 0f && motor.Grounded)
                {
                    motor.VerticalVelocity = 5f;
                    deformer.SetSquash(0.3f, 3f);
                }
            }

            if (intent.FacePlayer && distance > 0.01f && !pathing)
                yaw =Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, config.TurnSpeed * dt);
            else if (velocity.sqrMagnitude > 0.2f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg, config.TurnSpeed * dt);
            body.rotation = Quaternion.Euler(0, yaw, 0);

            // Удар засчитывается во второй половине Strike. Если кадр длинный и Strike целиком «проскочил»
            // (переход в Hold в этом же кадре), проверяем попадание в момент перехода — иначе удар зависел бы от FPS.
            if (brain.State == EnemyState.Strike)
                TryHitPlayer(distance, dir, false);
            else if (strikeEndedThisFrame)
                TryHitPlayer(distance, dir, true);
            else if (hitDeferred)
                ResolveDeferredHit(distance, dir);
            strikeEndedThisFrame = false;
            UpdateParryWindow();
            UpdateParryContact();

            // Подсветка статус-эффектов (уязвимость пульсирует своим цветом) и белая вспышка попадания.
            var tintNow = StatusTint.Any(brain.Status);
            if (flash > 0f || tintNow || tinted)
            {
                flash = Mathf.Max(0f, flash - dt);
                ApplyColor();
            }

            tinted = tintNow;
            UpdateDeformation(dt);
            if (poiseBar != null)
                poiseBar.Set(brain.Poise.Fraction, brain.Staggering,
                    brain.Status.Get(StatusEffectsConfig.StunId)?.RemainingFraction ?? 0f);
        }

        /// <summary>
        /// Спокойный враг замечает игрока, только если может до него дойти (ящики, поднятый мост, закрытая дверь-срезка
        /// отрезают путь). Путь считаем лишь тогда, когда игрок уже в зоне агро.
        /// </summary>
        private bool PlayerReachable(bool available, float distance)
        {
            if (reach == null || movement == null || !available || brain.State != EnemyState.Idle || distance > brain.AggroRadius)
                return true;
            var from = transform.position;
            var to = player.transform.position;
            return reach.CanReach(movement, from.x, from.y + 0.1f, from.z, to.x, to.y + 0.1f, to.z);
        }

        /// <summary>Путь к игроку в погоне; вне погони (и без карты пути) — «иди напрямую».</summary>
        private NavSteer ChaseSteer(float dt, bool available)
        {
            if (navigator == null || !available || brain.State != EnemyState.Chase)
                return new NavSteer(NavSteerMode.Direct);
            var p = transform.position;
            var t = player.transform.position;
            return navigator.Steer(dt, p.x, p.y, p.z, t.x, t.y, t.z);
        }

        /// <summary>Скорость к точке пути; перед уступом, на который надо запрыгнуть, — прыжок.</summary>
        private Vector3 SteerVelocity(NavSteer steer, float speed)
        {
            if (steer.Mode != NavSteerMode.Path)
                return Vector3.zero;
            var to = new Vector3(steer.X - transform.position.x, 0f, steer.Z - transform.position.z);
            var d = to.magnitude;
            if (d < 0.05f)
                return Vector3.zero;
            if (steer.Jump && motor.Grounded && d < 1.1f && motor.VerticalVelocity <= 0.01f)
            {
                // Высота прыжка с запасом на край опоры; гравитация врага на подъёме — 30 м/с².
                motor.VerticalVelocity = Mathf.Sqrt(2f * 30f * (steer.JumpRise + 0.35f));
                deformer.SetSquash(0.3f, 3f);
            }

            return to / d * speed;
        }

        /// <summary>
        /// Не даёт шагнуть с обрыва (глубже, чем враг спрыгивает), в стену или закрытый проход: по каждой оси
        /// проверяем клетку впереди по карте пути. Отбрасывание от ударов (<see cref="push"/>) не ограничивается.
        /// </summary>
        private Vector3 GuardLedges(Vector3 desired)
        {
            if (navigator == null || !motor.Grounded)
                return desired;
            var p = transform.position;
            if (Mathf.Abs(desired.x) > 0.01f && !navigator.CanMove(p.x, p.y, p.z, desired.x, 0f))
                desired.x = 0f;
            if (Mathf.Abs(desired.z) > 0.01f && !navigator.CanMove(p.x, p.y, p.z, 0f, desired.z))
                desired.z = 0f;
            return desired;
        }

        /// <summary>
        /// Ждущий в очереди обходит игрока по дуге к точке за его спиной (свои места для каждого в очереди),
        /// следующий по очереди подходит на дистанцию удара.
        /// </summary>
        private Vector3 FlankVelocity(EnemyIntent intent, Vector3 toPlayer, float distance, float dt, ref bool pathing)
        {
            var playerForward = player != null ? player.Forward : Vector3.forward;
            var behind = -playerForward;
            // Места: 0 — точно сзади, дальше — по очереди слева и справа от спины.
            var index = Mathf.Max(0, intent.QueueIndex);
            var side = index == 0 ? 0f : (index % 2 == 1 ? 1f : -1f) * (40f * ((index + 1) / 2));
            var fromPlayer = distance > 0.001f ? -toPlayer / distance : behind;
            var wanted = Vector3.Slerp(fromPlayer, Quaternion.Euler(0, side, 0) * behind, Mathf.Clamp01(config.FlankBias)).normalized;
            var target = player.transform.position + wanted * intent.FlankDistance;
            var to = target - transform.position;
            to.y = 0f;
            var d = to.magnitude;
            if (d < 0.25f)
                return Vector3.zero;

            // Место за спиной не видно напрямую (стена, уступ) — идём к нему по пути.
            if (navigator != null)
            {
                var p = transform.position;
                var steer = navigator.Steer(dt, p.x, p.y, p.z, target.x, player.transform.position.y, target.z);
                if (steer.Mode == NavSteerMode.Stay)
                    return Vector3.zero;
                if (steer.Mode == NavSteerMode.Path)
                {
                    pathing = true;
                    return SteerVelocity(steer, Mathf.Min(intent.MoveSpeed, steer.Remaining * 4f));
                }
            }

            // Обходим по дуге: не проходим сквозь игрока, а огибаем его на дистанции ожидания.
            var radial = -fromPlayer;
            var tangent = Vector3.Cross(Vector3.up, radial);
            if (Vector3.Dot(tangent, to) < 0f)
                tangent = -tangent;
            var dirTo = to / d;
            if (Vector3.Dot(dirTo, radial) > 0.3f && distance < intent.FlankDistance + 1f)
                dirTo = Vector3.Slerp(dirTo, tangent, 0.7f).normalized;
            return dirTo * Mathf.Min(intent.MoveSpeed, d * 4f);
        }

        /// <summary>Открылось окно парирования текущей атаки — сюда цепляются звук и VFX.</summary>
        public event System.Action<EnemyController> ParryWindowOpened;

        private bool parryWindowWasOpen;

        private void UpdateParryWindow()
        {
            var open = brain.InParryWindow;
            if (open && !parryWindowWasOpen)
            {
                ParryWindowOpened?.Invoke(this);
                parryGlint = 0.12f; // временная подсказка: блик на оружии
            }

            parryWindowWasOpen = open;
        }

        private float parryGlint;
        private Pose parriedFrom;

        // Подача парирования: удар ещё идёт до столкновения (ParryContactTime), потом отскок.
        private bool parryContactPending;
        private float parryArcFrom;
        private float parryArcTo;
        private Vector3 parryFromPoint;
        private System.Action<Vector3> parryContact;

        /// <summary>
        /// Игрок поставил блок щитом (или ударил щитом): если сейчас окно парирования (расширенное на долю
        /// <paramref name="widen"/> апгрейдом щита), атака парирована. Враг ещё доводит удар до середины дуги,
        /// оружие сталкивается со щитом (вспышка, <paramref name="onContact"/> — фриз-кадры у игрока) и отскакивает.
        /// Парирование заполнило шкалу выносливости — враг ошеломлён и стоит с опущенным оружием
        /// (<see cref="EnemyConfig.ParriedDuration"/>); нет — удар поглощён, враг собирается и продолжает серию.
        /// </summary>
        /// <returns>true — парировано.</returns>
        public bool TryParry(Vector3 from, float widen = 0f, System.Action<Vector3> onContact = null)
        {
            if (brain == null || Dead || !brain.IsInParryWindow(widen))
                return false;
            return Parry(from, onContact);
        }

        /// <summary>
        /// Парировать без проверки окна (окно проверено раньше — выстрел дробовика в окно, пули долетели позже).
        /// Срабатывает, пока атака идёт (<see cref="EnemyBrain.CanBeParried"/>).
        /// </summary>
        public bool Parry(Vector3 from, System.Action<Vector3> onContact = null)
        {
            if (brain == null || Dead || !brain.CanBeParried)
                return false;
            // Где по дуге сейчас оружие: на замахе — в начале, в ударе — по прогрессу удара.
            var arcNow = brain.State == EnemyState.Windup ? 0f
                : brain.State == EnemyState.Strike ? StrikeArc(brain.PhaseProgress)
                : 1f;
            if (!brain.Parry())
                return false;
            parryArcFrom = arcNow;
            parryArcTo = Mathf.Max(arcNow, config.ParryContactArc);
            parryFromPoint = from;
            parryContact = onContact;
            parryContactPending = true;
            if (config.ParryContactTime <= 0f)
                ParryContact();
            return true;
        }

        /// <summary>Доля дуги удара по прогрессу фазы удара (удар резкий: быстро набирает ход).</summary>
        private static float StrikeArc(float p) => 1f - (1f - p) * (1f - p) * (1f - p);

        private void UpdateParryContact()
        {
            if (!parryContactPending)
                return;
            if (brain.State != EnemyState.Parried)
            {
                parryContactPending = false;
                parryContact = null;
                return;
            }

            if (brain.StateTime >= config.ParryContactTime)
                ParryContact();
        }

        /// <summary>Оружие встретило щит: вспышка у оружия, отдача и отскок назад.</summary>
        private void ParryContact()
        {
            parryContactPending = false;
            var away = transform.position - parryFromPoint;
            away.y = 0f;
            away = away.sqrMagnitude > 0.001f ? away.normalized : -(Quaternion.Euler(0, yaw, 0) * Vector3.forward);
            // Удар щитом мог уже толкнуть сильнее — не ослабляем.
            push = away * Mathf.Max(2.2f, push.magnitude);
            parriedFrom = handPose;
            deformer.SetSquash(-0.3f, -2f);
            deformer.Impulse(-1f, new Vector2(0f, -3.5f));
            flash = 0.08f;
            var tip = WeaponTip();
            ParticleFx.Flash(tip, 0.9f);
            // Оружие встретило щит: эффекты парирования (уязвимость и др. из StatusEffects.json).
            brain.ApplyParryEffects();
            var callback = parryContact;
            parryContact = null;
            callback?.Invoke(tip);
        }

        /// <summary>Время с парирования, считая от столкновения оружия (до него — отрицательное).</summary>
        private float ParryRecoilTime => brain.StateTime - Mathf.Max(0f, config.ParryContactTime);

        /// <summary>Точка у головы дубины в мире (для вспышки).</summary>
        private Vector3 WeaponTip() => hand != null ? hand.TransformPoint(Vector3.up * 0.55f) : HitPoint;

        private Vector3 Separation()
        {
            var result = Vector3.zero;
            foreach (var other in all)
            {
                if (other == this || other.Dead)
                    continue;
                var d = transform.position - other.transform.position;
                d.y = 0;
                var min = (config.Size + other.config.Size) * 0.6f;
                var m = d.magnitude;
                if (m < min && m > 0.001f)
                    result += d / m * (min - m) * 6f;
            }

            return result;
        }

        private Vector3 Lunge(Vector3 dir)
        {
            if (brain.State != EnemyState.Strike || brain.Attack == null)
                return Vector3.zero;
            var target = brain.Attack.Lunge * brain.PhaseProgress;
            var delta = target - lungeDone;
            lungeDone = target;
            var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            return forward * delta;
        }

        // Удар достал игрока, пока его ещё можно парировать: урон ждёт конца окна парирования
        // (или долёта пуль парирующего выстрела). Парировали — урона нет.
        private bool hitDeferred;
        private EnemyAttackConfig deferredAttack;
        private float parryHoldUntil = -1f;
        private PlayerCombat playerCombat;

        /// <summary>
        /// Парирующий выстрел уже в полёте: удар по игроку подождёт <paramref name="seconds"/>, чтобы пули успели
        /// парировать атаку до урона.
        /// </summary>
        public void HoldHitForParry(float seconds)
        {
            parryHoldUntil = Mathf.Max(parryHoldUntil, Time.time + seconds);
        }

        /// <summary>Атаку ещё может парировать игрок: окно парирования (с апгрейдом щита) или летящие пули.</summary>
        private bool ParryStillPossible()
        {
            if (!brain.CanBeParried)
                return false;
            var widen = playerCombat != null ? playerCombat.ParryWidenNow : 0f;
            return brain.IsInParryWindow(widen) || Time.time < parryHoldUntil;
        }

        /// <summary>Отложенный удар: парирован — отменён, окно прошло — наносится (если игрок ещё в зоне удара).</summary>
        private void ResolveDeferredHit(float distance, Vector3 dirToPlayer)
        {
            if (hitThisAttack || brain.State == EnemyState.Parried || brain.Attack == null || Dead)
            {
                hitDeferred = false;
                return;
            }

            if (ParryStillPossible())
                return;
            hitDeferred = false;
            TryHitPlayer(distance, dirToPlayer, true);
        }

        private void TryHitPlayer(float distance, Vector3 dirToPlayer, bool strikeEnded)
        {
            if (hitThisAttack || playerDamage == null || (!strikeEnded && brain.PhaseProgress < 0.4f))
                return;
            var attack = brain.Attack;
            if (attack == null)
                return;
            // Удар по области бьёт, когда оружие дошло до земли.
            if (attack.IsArea && !strikeEnded)
                return;
            if (!InStrikeZone(attack, distance, dirToPlayer))
                return;
            if (ParryStillPossible())
            {
                hitDeferred = true;
                deferredAttack = attack;
                return;
            }

            DealHit(attack);
        }

        /// <summary>Игрок в зоне удара: в круге удара по области или в секторе на дальности удара.</summary>
        private bool InStrikeZone(EnemyAttackConfig attack, float distance, Vector3 dirToPlayer)
        {
            if (player == null)
                return false;
            if (attack.IsArea)
            {
                // Удар по области: всех в круге вокруг точки удара.
                var toPlayer = player.transform.position - AreaCenter(attack);
                toPlayer.y = 0f;
                return toPlayer.magnitude - 0.45f <= attack.AreaRadius;
            }

            // Дальность до тела игрока (его полуширина ~0.45).
            if (distance - 0.45f > attack.Range)
                return false;
            var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            return distance <= 0.5f || Vector3.Angle(forward, dirToPlayer) <= attack.Arc * 0.5f;
        }

        private void DealHit(EnemyAttackConfig attack)
        {
            hitThisAttack = true;
            // Усиление врага статус-эффектом; уязвимость игрока учитывает PlayerVitals.
            playerDamage.ReceiveEnemyHit(attack.Damage * brain.Status.DamageDealtMultiplier, transform.position, attack.Knockback);
        }

        /// <summary>
        /// Серия: следующий удар начал замах, пока прошлый ждал конца окна парирования. Прошлый удар уже не парировать —
        /// наносим его, если игрок ещё в зоне удара (иначе он молча терялся бы).
        /// </summary>
        private void FlushDeferredHit()
        {
            var attack = deferredAttack;
            deferredAttack = null;
            if (!hitDeferred || hitThisAttack || attack == null || playerDamage == null || player == null || Dead)
                return;
            var toPlayer = player.transform.position - transform.position;
            toPlayer.y = 0f;
            var distance = toPlayer.magnitude;
            var dir = distance > 0.001f ? toPlayer / distance : transform.forward;
            if (InStrikeZone(attack, distance, dir))
                DealHit(attack);
        }

        // ------------------------------------------------------------------ состояния

        private void OnStateChanged(EnemyState state)
        {
            switch (state)
            {
                case EnemyState.Alerted:
                    // "Заметил!": сжался, подпрыгнул, над головой "!".
                    deformer.SetSquash(-0.35f, -1f);
                    hopTimer = 0.12f;
                    marker.Show(config.AlertDuration);
                    break;
                case EnemyState.Windup:
                    FlushDeferredHit();
                    windupFrom = handPose;
                    hitThisAttack = false;
                    hitDeferred = false;
                    lungeDone = 0f;
                    break;
                case EnemyState.Strike:
                    deformer.Impulse(-1.2f);
                    break;
                case EnemyState.Hold:
                    strikeEndedThisFrame = true;
                    deformer.Impulse(-1.6f);
                    if (brain.Attack != null && brain.Attack.IsArea)
                        GroundSlam(brain.Attack);
                    break;
                case EnemyState.Staggered:
                    marker.Hide();
                    break;
                case EnemyState.Idle:
                    marker.Hide();
                    navigator?.Reset();
                    break;
                case EnemyState.Stunned:
                    // Оглушён со стороны: вздрогнул и обмяк, оружие опускается (поза — StunnedPose).
                    marker.Hide();
                    hitDeferred = false;
                    deformer.SetSquash(-0.25f, -1.5f);
                    break;
                case EnemyState.Parried:
                    // Удар ещё идёт (до столкновения со щитом): отдача и отскок — в ParryContact.
                    parriedFrom = handPose;
                    deformer.Impulse(-1.2f);
                    break;
                case EnemyState.Dead:
                    Die();
                    break;
            }
        }

        // ------------------------------------------------------------------ урон

        public void ReceiveHit(HitInfo hit)
        {
            if (brain == null || Dead)
                return;
            brain.Health.TakeDamage(hit.Damage);
            if (Dead)
                return;
            var interrupted = brain.OnDamaged(hit.Poise);
            flash = 0.12f;
            ApplyColor();
            var dir = hit.Direction;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.001f)
                dir = -(Quaternion.Euler(0, yaw, 0) * Vector3.forward);
            dir.Normalize();
            // Отшатывание: толчок от удара, сжатие и наклон назад от удара (стойкого врага в атаке не сдвинуть).
            var pushback = config.HyperArmor && brain.IsAttacking ? config.HitPushback * 0.25f : config.HitPushback;
            push = dir * (pushback * Mathf.Max(0.5f, hit.Knockback) / Mathf.Max(0.1f, config.StaggerDuration));
            deformer.SetSquash(-0.22f, -1.5f);
            var local = Quaternion.Inverse(body.rotation) * dir;
            deformer.Impulse(-0.8f, new Vector2(local.x, local.z) * 3.5f);
            if (interrupted)
                deformer.TwistTarget = 0f;
            cracks.Refresh(brain.Health.Fraction);
        }

        private void Die()
        {
            marker.Hide();
            Died?.Invoke(this);
            var size = config.Size;
            var center = transform.position + Vector3.up * size * 0.5f;
            var pieces = Debris.Burst(center, color, config.DeathPieces, 0.24f * size, 4.5f, 2.4f);
            // Часть осколков после разлёта летит к игроку как опыт (осколки — из пула).
            var shards = Mathf.Min(config.ExperiencePieces, pieces.Count);
            var per = shards > 0 ? Mathf.Max(1, config.Experience / shards) : 0;
            var rest = config.Experience - per * shards;
            var experience = session.Experience;
            for (int i = 0; i < shards; i++)
                pieces[i].FlyToAsExperience(player != null ? player.transform : null, per + (i == 0 ? rest : 0), 0.7f + i * 0.06f,
                    amount => experience.Value += amount);

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (marker != null)
                Destroy(marker.gameObject);
            if (poiseBar != null)
                Destroy(poiseBar.gameObject);
        }

        // ------------------------------------------------------------------ удар по области

        /// <summary>Центр круга удара по области: перед врагом на <see cref="EnemyAttackConfig.AreaOffset"/>.</summary>
        private Vector3 AreaCenter(EnemyAttackConfig attack) =>
            transform.position + Quaternion.Euler(0, yaw, 0) * Vector3.forward * attack.AreaOffset;

        /// <summary>
        /// Оружие врезалось в землю: волна по земле (как удар игрока в прыжке) в точке удара, куб сплющивается.
        /// Над обрывом волна — на уровне ног врага.
        /// </summary>
        private void GroundSlam(EnemyAttackConfig attack)
        {
            var center = AreaCenter(attack);
            var mask = ~((1 << EnemyLayer) | (1 << Debris.Layer));
            if (player != null)
                mask &= ~(1 << player.gameObject.layer);
            var point = Physics.Raycast(center + Vector3.up * 1.2f, Vector3.down, out var ground, 2.6f, mask, QueryTriggerInteraction.Ignore)
                ? ground.point
                : new Vector3(center.x, transform.position.y, center.z);
            ParticleFx.Play(ParticleFx.GroundSlam, point, Quaternion.Euler(0, yaw, 0), Color.Lerp(color, Color.white, 0.3f), attack.AreaRadius);
            deformer.SetSquash(-0.4f, -3f);
            // Толчок камеры — тем сильнее, чем ближе игрок к точке удара.
            if (playerCombat != null && hitShake > 0f)
            {
                var near = 1f - Mathf.Clamp01(Vector3.Distance(player.transform.position, point) / Mathf.Max(1f, attack.AreaRadius * 3f));
                playerCombat.Shake(hitShake * 1.5f * near);
            }
        }

        // ------------------------------------------------------------------ анимация

        private void LateUpdate()
        {
            if (brain == null || hand == null)
                return;
            var pose = HandPose();
            var k = brain.IsAttacking || brain.State == EnemyState.Parried ? 1f : 1f - Mathf.Exp(-Time.deltaTime * 14f);
            handPose = k >= 1f ? pose : PlayerCombat.AroundBody(handPose, pose, k);
            hand.localPosition = deformer.DeformPoint(handPose.position);
            if (parryGlint > 0f)
                parryGlint -= Time.deltaTime;
            hand.localScale = Vector3.one * (1f + Mathf.Clamp01(parryGlint / 0.12f) * 0.25f);
            hand.localRotation = deformer.DeformRotation(handPose.position) * handPose.rotation;
        }

        private static Pose Rest => new Pose(new Vector3(0.68f, 0.42f, 0.12f), Quaternion.Euler(22f, 0f, -20f));

        /// <summary>Отскок после парирования: оружие выбито вверх-назад.</summary>
        private static Pose ParryRecoil => new Pose(new Vector3(0.62f, 0.95f, -0.28f),
            Quaternion.FromToRotation(Vector3.up, new Vector3(0.25f, 0.75f, -0.62f).normalized));

        /// <summary>Опущенное оружие: дубина у бедра концом вниз-вперёд.</summary>
        private static Pose Lowered => new Pose(new Vector3(0.64f, 0.24f, 0.18f),
            Quaternion.FromToRotation(Vector3.up, new Vector3(0.18f, -0.72f, 0.67f).normalized));

        /// <param name="t">Время с момента столкновения оружия со щитом (до него — отрицательное).</param>
        private Pose ParriedPose(float t)
        {
            const float bounce = 0.14f, lower = 0.5f;
            if (t < 0f)
            {
                // Удар не закончен: оружие идёт по дуге парированной атаки до точки столкновения (до середины удара),
                // с ускорением, как настоящий удар.
                var attack = brain.ParriedAttack;
                if (attack == null)
                    return parriedFrom;
                var u = Mathf.Clamp01(1f + t / Mathf.Max(0.001f, config.ParryContactTime));
                var arc = Mathf.Lerp(parryArcFrom, parryArcTo, u * u);
                return PlayerCombat.AroundBody(parriedFrom, ArcPose(attack.Motion, arc), Mathf.Clamp01(u * 2f));
            }

            if (t < bounce)
            {
                var u = t / bounce;
                return PlayerCombat.AroundBody(parriedFrom, ParryRecoil, 1f - (1f - u) * (1f - u));
            }

            if (!brain.ParryStunned)
            {
                // Удар поглощён, враг не ошеломлён: оружие сразу возвращается в стойку (дальше — следующий удар серии).
                var rest = Mathf.Max(0.05f, config.DeflectDuration - Mathf.Max(0f, config.ParryContactTime) - bounce);
                var u = Mathf.Clamp01((t - bounce) / rest);
                return PlayerCombat.AroundBody(ParryRecoil, Rest, u * u * (3f - 2f * u));
            }

            if (t < lower)
            {
                var u = (t - bounce) / (lower - bounce);
                return PlayerCombat.AroundBody(ParryRecoil, Lowered, u * u * (3f - 2f * u));
            }

            // Стоит с опущенным оружием, дубина чуть покачивается.
            var pose = Lowered;
            pose.position += new Vector3(0f, Mathf.Sin(t * 5f) * 0.015f, 0f);
            return pose;
        }

        private Pose HandPose()
        {
            if (brain.State == EnemyState.Parried)
                return ParriedPose(ParryRecoilTime);
            if (brain.State == EnemyState.Stunned)
            {
                var stunned = Lowered;
                stunned.position += new Vector3(0f, Mathf.Sin(brain.StateTime * 5f) * 0.015f, 0f);
                return stunned;
            }
            var attack = brain.Attack;
            if (attack == null)
                return Rest;
            var p = brain.PhaseProgress;
            switch (brain.State)
            {
                case EnemyState.Windup:
                    // Долгий читаемый замах: рука уходит в начало дуги, в конце — дрожь напряжения
                    // (у тяжёлого удара сверху — сильнее).
                    var u = Mathf.Clamp01(p / 0.6f);
                    var pose = PlayerCombat.AroundBody(windupFrom, ArcPose(attack.Motion, 0f), u * u * (3f - 2f * u));
                    if (p > 0.6f)
                        pose.position += Random.insideUnitSphere * (attack.Motion == EnemyAttackMotion.Slam ? 0.022f : 0.012f);
                    return pose;
                case EnemyState.Strike:
                    return ArcPose(attack.Motion, 1f - (1f - p) * (1f - p) * (1f - p));
                case EnemyState.Hold:
                    return ArcPose(attack.Motion, 1f);
                case EnemyState.Recovery:
                    var r = p * p * (3f - 2f * p);
                    return PlayerCombat.AroundBody(ArcPose(attack.Motion, 1f), Rest, r);
                default:
                    return Rest;
            }
        }

        /// <summary>
        /// Дуги оружия (координаты меша, куб 1×1): размашистая горизонтальная, узкая сверху вниз, тяжёлая в землю
        /// из-за головы и косые «накрест» из-за правого и левого плеча.
        /// </summary>
        private static Pose ArcPose(EnemyAttackMotion motion, float p)
        {
            if (motion == EnemyAttackMotion.Sweep)
            {
                var yaw = Mathf.Lerp(125f, -105f, p) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(yaw), -0.05f, Mathf.Cos(yaw)).normalized;
                var pos = new Vector3(0f, 0.5f, 0f) + new Vector3(dir.x, 0f, dir.z) * 0.78f;
                return new Pose(pos, Quaternion.FromToRotation(Vector3.up, dir));
            }

            if (motion == EnemyAttackMotion.DiagonalRight || motion == EnemyAttackMotion.DiagonalLeft)
            {
                // Косой удар: из-за плеча высоко сзади-сбоку вниз через перед к противоположному бедру.
                var side = motion == EnemyAttackMotion.DiagonalRight ? 1f : -1f;
                var yaw = Mathf.Lerp(120f * side, -95f * side, p) * Mathf.Deg2Rad;
                var flat = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
                var dir = new Vector3(flat.x, Mathf.Lerp(0.85f, -0.55f, p), flat.z).normalized;
                var pos = new Vector3(0f, Mathf.Lerp(1.05f, 0.28f, p), 0f) + flat * 0.8f;
                return new Pose(pos, Quaternion.FromToRotation(Vector3.up, dir));
            }

            if (motion == EnemyAttackMotion.Slam)
            {
                // Как 4-й удар меча игрока, но тяжелее: рукоять заведена высоко за голову, клинок врезается в землю впереди.
                var g = Mathf.Lerp(-55f, 140f, p) * Mathf.Deg2Rad;
                var gp = new Vector3(0.12f, 0.85f, 0.4f) + new Vector3(0f, Mathf.Cos(g), Mathf.Sin(g)) * 0.78f;
                var b = Mathf.Lerp(-40f, 105f, p) * Mathf.Deg2Rad;
                return new Pose(gp, Quaternion.FromToRotation(Vector3.up, new Vector3(0f, Mathf.Cos(b), Mathf.Sin(b))));
            }

            var grip = Mathf.Lerp(-30f, 130f, p) * Mathf.Deg2Rad;
            var gpos = new Vector3(0.18f, 0.8f, 0.45f) + new Vector3(0f, Mathf.Cos(grip), Mathf.Sin(grip)) * 0.7f;
            var blade = Mathf.Lerp(-15f, 95f, p) * Mathf.Deg2Rad;
            return new Pose(gpos, Quaternion.FromToRotation(Vector3.up, new Vector3(0f, Mathf.Cos(blade), Mathf.Sin(blade))));
        }

        private void UpdateDeformation(float dt)
        {
            var state = brain.State;
            var attack = brain.Attack;
            var p = brain.PhaseProgress;
            var speed = velocity.magnitude;

            // Ходьба: покачивание из стороны в сторону, как у игрока; наклон вперёд по ходу.
            if (motor.Grounded && speed > 0.3f && !brain.IsAttacking)
            {
                swayPhase += speed * 1.8f * Mathf.PI * dt;
                deformer.BendTarget = new Vector2(Mathf.Sin(swayPhase) * 0.09f, 0f);
                deformer.SquashTarget = -Mathf.Abs(Mathf.Sin(swayPhase)) * 0.05f;
            }
            else
            {
                deformer.BendTarget = Vector2.zero;
                deformer.SquashTarget = state == EnemyState.Windup ? -0.12f * p : 0f;
            }

            var local = Quaternion.Inverse(body.rotation) * velocity;
            deformer.LeanTarget = new Vector2(local.x, local.z) * 0.012f;

            // Замах: отклон назад и закрутка; удар — поклон и раскрутка.
            float twist = 0f, pitch = 0f;
            if (attack != null)
            {
                BodyPose(attack.Motion, out var windTwist, out var windPitch, out var hitTwist, out var hitPitch);
                switch (state)
                {
                    case EnemyState.Windup:
                        var u = Mathf.Clamp01(p / 0.6f);
                        twist = windTwist * u;
                        pitch = windPitch * u;
                        if (attack.Motion == EnemyAttackMotion.Slam)
                            deformer.SquashTarget = -0.2f * u; // тяжёлый замах: присел
                        break;
                    case EnemyState.Strike:
                    case EnemyState.Hold:
                        twist = hitTwist;
                        pitch = hitPitch;
                        break;
                    case EnemyState.Recovery:
                        var k = 1f - p;
                        twist = hitTwist * k;
                        pitch = hitPitch * k;
                        break;
                }
            }

            if (state == EnemyState.Parried)
            {
                var t = ParryRecoilTime;
                var parried = brain.ParriedAttack;
                if (t < 0f && parried != null)
                {
                    // До столкновения — тело ещё бьёт: поклон и раскрутка удара.
                    BodyPose(parried.Motion, out _, out _, out twist, out pitch);
                }
                else if (!brain.ParryStunned)
                {
                    // Удар поглощён: отшатнулся и сразу собрался.
                    pitch = t < 0.15f ? -14f : 0f;
                    twist = 0f;
                    deformer.SquashTarget = 0f;
                }
                else
                {
                    // Отшатнулся назад, потом обмяк: поклон вперёд с опущенным оружием.
                    pitch = t < 0.2f ? -16f : 7f;
                    twist = 0f;
                    deformer.SquashTarget = t < 0.2f ? 0f : -0.06f;
                }
            }

            if (state == EnemyState.Stunned)
            {
                // Оглушён: обмяк, поклон вперёд и лёгкое покачивание.
                pitch = 7f;
                twist = Mathf.Sin(brain.StateTime * 4f) * 6f;
                deformer.SquashTarget = -0.06f;
            }

            deformer.TwistTarget = twist;
            deformer.PitchTarget = pitch;
        }

        /// <summary>Закрутка и наклон тела на замахе (в конце замаха) и в ударе для движения атаки, градусы.</summary>
        private static void BodyPose(EnemyAttackMotion motion, out float windTwist, out float windPitch, out float hitTwist,
            out float hitPitch)
        {
            switch (motion)
            {
                case EnemyAttackMotion.Sweep:
                    windTwist = -26f; windPitch = -8f; hitTwist = 24f; hitPitch = 10f;
                    break;
                case EnemyAttackMotion.Slam:
                    windTwist = 0f; windPitch = -26f; hitTwist = 0f; hitPitch = 36f;
                    break;
                case EnemyAttackMotion.DiagonalRight:
                    windTwist = -22f; windPitch = -14f; hitTwist = 20f; hitPitch = 20f;
                    break;
                case EnemyAttackMotion.DiagonalLeft:
                    windTwist = 22f; windPitch = -14f; hitTwist = -20f; hitPitch = 20f;
                    break;
                default: // Overhead
                    windTwist = 0f; windPitch = -16f; hitTwist = 0f; hitPitch = 28f;
                    break;
            }
        }
    }
}
