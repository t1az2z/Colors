using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
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
    public sealed class EnemyController : GameBehaviour, IHittable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly List<EnemyController> all = new List<EnemyController>();
        private static int nextId = 1;

        public static IReadOnlyList<EnemyController> All => all;

        private string typeId;
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
        private CrackVisual cracks;
        private Color color;

        private float yaw;
        private Vector3 velocity;
        private Vector3 push;
        private float flash;
        private float swayPhase;
        private bool hitThisAttack;
        private float lungeDone;
        private Pose handPose = new Pose(new Vector3(0.66f, 0.4f, 0.1f), Quaternion.identity);
        private Pose windupFrom;
        private float hopTimer = -1f;

        public EnemyBrain Brain => brain;
        public string ColorId => config != null ? config.Color : "blue";
        public Vector3 HitPoint => transform.position + Vector3.up * (config != null ? config.Size * 0.6f : 0.5f);
        public bool Dead => brain != null && brain.State == EnemyState.Dead;

        /// <summary>Создать врага на сцене. Тело — тот же скруглённый куб с глазами, что у игрока, другого цвета и размера.</summary>
        public static EnemyController Spawn(string type, Vector3 position, float yaw, Transform parent, GameBootstrap bootstrap,
            Mesh mesh, Material[] materials)
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
            bootstrap.Inject(go);
            return enemy;
        }

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
            config = configs.LoadTable<EnemyConfig>(ConfigKeys.Enemies).Get(typeId);
            playerConfig = configs.Load<PlayerConfig>(ConfigKeys.Player);
            session = resolver.Resolve<GameSession>();
            palette = resolver.Resolve<Palette>();
            events = resolver.Resolve<IEventBus>();
            var id = nextId++;
            brain = new EnemyBrain(id, config, resolver.Resolve<CombatDirector>(), id * 7919);
            brain.StateChanged += OnStateChanged;
            BuildBody();
        }

        private void OnEnable() => all.Add(this);
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
            body.rotation = Quaternion.Euler(0, yaw, 0);
        }

        private void ApplyColor()
        {
            var white = Mathf.Clamp01(flash / 0.12f);
            bodyRenderer.GetPropertyBlock(block, 0);
            block.SetColor(BaseColorId, Color.Lerp(color, Color.white, white * 0.85f));
            block.SetColor(EmissionColorId, Color.white * (white * 1.2f));
            bodyRenderer.SetPropertyBlock(block, 0);
        }

        // ------------------------------------------------------------------ кадр

        private void Update()
        {
            if (brain == null)
                return;
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerController>();
                playerDamage = player != null ? player.GetComponent<PlayerDamageReceiver>() : null;
            }

            var dt = Time.deltaTime;
            var toPlayer = player != null ? player.transform.position - transform.position : Vector3.forward * 1000f;
            var vertical = Mathf.Abs(toPlayer.y);
            toPlayer.y = 0f;
            var distance = toPlayer.magnitude;
            var available = player != null && (playerDamage == null || !playerDamage.Dead) && vertical < 3f;
            var intent = brain.Tick(dt, distance, available);
            var dir = distance > 0.001f ? toPlayer / distance : transform.forward;

            // Движение: к игроку / от игрока + мягкое расталкивание с соседями + отбрасывание от ударов.
            var desired = dir * intent.MoveSpeed + Separation();
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

            if (intent.FacePlayer && distance > 0.01f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, config.TurnSpeed * dt);
            else if (velocity.sqrMagnitude > 0.2f)
                yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg, config.TurnSpeed * dt);
            body.rotation = Quaternion.Euler(0, yaw, 0);

            if (brain.State == EnemyState.Strike)
                TryHitPlayer(distance, dir);

            if (flash > 0f)
            {
                flash -= dt;
                ApplyColor();
            }

            UpdateDeformation(dt);
        }

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

        private void TryHitPlayer(float distance, Vector3 dirToPlayer)
        {
            if (hitThisAttack || playerDamage == null || brain.PhaseProgress < 0.4f)
                return;
            var attack = brain.Attack;
            var forward = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            // Дальность до тела игрока (его полуширина ~0.45).
            if (distance - 0.45f > attack.Range)
                return;
            if (distance > 0.5f && Vector3.Angle(forward, dirToPlayer) > attack.Arc * 0.5f)
                return;
            hitThisAttack = true;
            playerDamage.ReceiveEnemyHit(attack.Damage, transform.position, attack.Knockback);
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
                    windupFrom = handPose;
                    hitThisAttack = false;
                    lungeDone = 0f;
                    break;
                case EnemyState.Strike:
                    deformer.Impulse(-1.2f);
                    break;
                case EnemyState.Hold:
                    deformer.Impulse(-1.6f);
                    break;
                case EnemyState.Staggered:
                case EnemyState.Idle:
                    marker.Hide();
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
            var interrupted = brain.OnDamaged();
            flash = 0.12f;
            ApplyColor();
            var dir = hit.Direction;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.001f)
                dir = -(Quaternion.Euler(0, yaw, 0) * Vector3.forward);
            dir.Normalize();
            // Отшатывание: толчок от удара, сжатие и наклон назад от удара.
            push = dir * (config.HitPushback * Mathf.Max(0.5f, hit.Knockback) / Mathf.Max(0.1f, config.StaggerDuration));
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
            var size = config.Size;
            var center = transform.position + Vector3.up * size * 0.5f;
            var pieces = Debris.Burst(center, color, config.DeathPieces, 0.24f * size, 4.5f, 2.4f);
            // Часть осколков после разлёта летит к игроку как опыт.
            var shards = Mathf.Min(config.ExperiencePieces, pieces.Count);
            var per = shards > 0 ? Mathf.Max(1, config.Experience / shards) : 0;
            var rest = config.Experience - per * shards;
            for (int i = 0; i < shards; i++)
            {
                var piece = pieces[i];
                Destroy(piece.GetComponent<FadeAndDie>());
                var shard = piece.gameObject.AddComponent<ExperienceShard>();
                shard.Target = player != null ? player.transform : null;
                shard.Amount = per + (i == 0 ? rest : 0);
                shard.Delay = 0.7f + i * 0.06f;
                shard.Collected = amount => session.Experience.Value += amount;
            }

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (marker != null)
                Destroy(marker.gameObject);
        }

        // ------------------------------------------------------------------ анимация

        private void LateUpdate()
        {
            if (brain == null || hand == null)
                return;
            var pose = HandPose();
            var k = brain.IsAttacking ? 1f : 1f - Mathf.Exp(-Time.deltaTime * 14f);
            handPose = k >= 1f ? pose : PlayerCombat.AroundBody(handPose, pose, k);
            hand.localPosition = deformer.DeformPoint(handPose.position);
            hand.localRotation = deformer.DeformRotation(handPose.position) * handPose.rotation;
        }

        private static Pose Rest => new Pose(new Vector3(0.68f, 0.42f, 0.12f), Quaternion.Euler(22f, 0f, -20f));

        private Pose HandPose()
        {
            var attack = brain.Attack;
            if (attack == null)
                return Rest;
            var p = brain.PhaseProgress;
            switch (brain.State)
            {
                case EnemyState.Windup:
                    // Долгий читаемый замах: рука уходит в начало дуги, в конце — дрожь напряжения.
                    var u = Mathf.Clamp01(p / 0.6f);
                    var pose = PlayerCombat.AroundBody(windupFrom, ArcPose(attack.Motion, 0f), u * u * (3f - 2f * u));
                    if (p > 0.6f)
                        pose.position += Random.insideUnitSphere * 0.012f;
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

        /// <summary>Дуги дубины (координаты меша, куб 1×1): размашистая горизонтальная и узкая сверху вниз.</summary>
        private static Pose ArcPose(EnemyAttackMotion motion, float p)
        {
            if (motion == EnemyAttackMotion.Sweep)
            {
                var yaw = Mathf.Lerp(125f, -105f, p) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(yaw), -0.05f, Mathf.Cos(yaw)).normalized;
                var pos = new Vector3(0f, 0.5f, 0f) + new Vector3(dir.x, 0f, dir.z) * 0.78f;
                return new Pose(pos, Quaternion.FromToRotation(Vector3.up, dir));
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
                var sweep = attack.Motion == EnemyAttackMotion.Sweep;
                switch (state)
                {
                    case EnemyState.Windup:
                        var u = Mathf.Clamp01(p / 0.6f);
                        twist = sweep ? -26f * u : 0f;
                        pitch = (sweep ? -8f : -16f) * u;
                        break;
                    case EnemyState.Strike:
                    case EnemyState.Hold:
                        twist = sweep ? 24f : 0f;
                        pitch = sweep ? 10f : 28f;
                        break;
                    case EnemyState.Recovery:
                        var k = 1f - p;
                        twist = (sweep ? 24f : 0f) * k;
                        pitch = (sweep ? 10f : 28f) * k;
                        break;
                }
            }

            deformer.TwistTarget = twist;
            deformer.PitchTarget = pitch;
        }
    }
}
