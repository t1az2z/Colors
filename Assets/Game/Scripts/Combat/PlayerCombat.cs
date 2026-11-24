using System.Collections;
using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using GameCore.Config;
using GameCore.DI;
using GameCore.Events;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>Нанесён урон: HUD показывает всплывающую цифру.</summary>
    public readonly struct DamageDealt
    {
        public readonly Vector3 Position;
        public readonly float Amount;
        public readonly bool ColorBonus;
        public readonly Color Color;

        public DamageDealt(Vector3 position, float amount, bool colorBonus, Color color)
        {
            Position = position;
            Amount = amount;
            ColorBonus = colorBonus;
            Color = color;
        }
    }

    /// <summary>
    /// Две руки: ЛКМ/RT — правая, ПКМ/LT — левая. Оружие в руке атакует серией ударов из конфига,
    /// щит в руке ставит блок на её кнопку, пустая рука бьёт кулаком. Двуручное оружие отвечает на обе кнопки.
    /// Позы рук считаются процедурно и проходят через ту же деформацию, что и куб.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PlayerCombat : GameBehaviour
    {
        [SerializeField] private Transform leftSocket;
        [SerializeField] private Transform rightSocket;
        [SerializeField] private global::Unity.Cinemachine.CinemachineImpulseSource impulse;

        private GameSession session;
        private GameInput input;
        private ItemDatabase items;
        private DamageRules rules;
        private CombatConfig combatConfig;
        private PlayerConfig playerConfig;
        private Palette palette;
        private IEventBus events;
        private PlayerController controller;

        private readonly HandState[] hands = new HandState[2];
        private readonly HashSet<IHittable> hitThisSwing = new HashSet<IHittable>();
        private readonly Collider[] overlap = new Collider[32];

        private HandState attacking;
        private AttackStepConfig step;
        private int comboIndex;
        private float attackTime;
        private bool queued;
        private float comboTimer;
        private float lungeDone;
        private float hitStopTimer;
        private float timeScaleBeforeHitStop = 1f;

        public bool IsBusy => attacking != null;
        public bool Blocking { get; private set; }
        public bool LockFacing => attacking != null && attackTime > step.Duration * step.ActiveFrom * 0.5f;
        public Vector3? FacingOverride => null;

        public bool CanInterruptForRoll => attacking == null || attackTime >= step.Duration * step.ActiveTo;

        public float MoveMultiplier
        {
            get
            {
                if (attacking != null)
                    return playerConfig.AttackMoveMultiplier;
                if (Blocking)
                    return playerConfig.BlockMoveMultiplier;
                return 1f;
            }
        }

        protected override void OnConstruct(IResolver resolver)
        {
            session = resolver.Resolve<GameSession>();
            input = resolver.Resolve<GameInput>();
            items = resolver.Resolve<ItemDatabase>();
            rules = resolver.Resolve<DamageRules>();
            palette = resolver.Resolve<Palette>();
            events = resolver.Resolve<IEventBus>();
            var configs = resolver.Resolve<ConfigService>();
            combatConfig = configs.Load<CombatConfig>(ConfigKeys.Combat);
            playerConfig = configs.Load<PlayerConfig>(ConfigKeys.Player);
        }

        private void Start()
        {
            controller = GetComponent<PlayerController>();
            hands[(int)Hand.Left] = new HandState(Hand.Left, leftSocket, -1f);
            hands[(int)Hand.Right] = new HandState(Hand.Right, rightSocket, 1f);
            session.Inventory.Changed += RebuildVisuals;
            RebuildVisuals();
            // Тряска камеры идёт и во время фриз-кадров (time scale = 0).
            if (global::Unity.Cinemachine.CinemachineImpulseManager.Instance != null)
                global::Unity.Cinemachine.CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
        }

        private void OnDestroy()
        {
            if (session != null)
                session.Inventory.Changed -= RebuildVisuals;
        }

        // ------------------------------------------------------------------ предметы в руках

        private ItemConfig ItemFor(Hand hand)
        {
            var item = session.Inventory.InHand(hand);
            return item != null ? item.Config : items.Get(combatConfig.UnarmedItem);
        }

        private void RebuildVisuals()
        {
            if (attacking != null)
                CancelAttack();
            foreach (var hand in hands)
            {
                if (hand.Visual != null)
                    Destroy(hand.Visual);
                hand.Visual = null;

                var item = session.Inventory.InHand(hand.Hand);
                hand.Item = ItemFor(hand.Hand);
                // Двуручное оружие рисуется один раз, в правой руке.
                if (item != null && item.Config.IsTwoHanded && hand.Hand == Hand.Left)
                {
                    hand.Visual = null;
                    continue;
                }

                // Пустая рука ничего не держит: удар "кулаком" идёт самим кубом, без отдельной модели.
                hand.Visual = item != null ? SpawnItem(item.Config, hand.Socket) : null;
                hand.Trail = null;
                if (hand.Visual != null && (item.Config.Kind == ItemKind.OneHanded || item.Config.Kind == ItemKind.TwoHanded))
                    hand.Trail = WeaponTrail.Attach(hand.Visual, hand.Socket.parent != null ? hand.Socket.parent.parent : transform,
                        palette.Get(item.Config.Color));
            }
        }

        private GameObject SpawnItem(ItemConfig config, Transform socket)
        {
            var prefab = string.IsNullOrEmpty(config.Prefab) ? null : Resources.Load<GameObject>(config.Prefab);
            if (prefab == null)
            {
                Debug.LogWarning($"No prefab '{config.Prefab}' for item '{config.Id}'");
                return null;
            }

            var go = Instantiate(prefab, socket, false);
            if (go.TryGetComponent<ItemVisual>(out var visual))
            {
                visual.SetColor(palette.Get(config.Color), combatConfig.HeldWeaponGlow);
                visual.StripColliders();
            }

            return go;
        }

        // ------------------------------------------------------------------ ввод

        private void Update()
        {
            if (playerConfig == null)
                return;
            UpdateHitStop();
            var dt = Time.deltaTime;
            comboTimer -= dt;
            var control = controller.HasControl && !controller.Rolling;

            var right = hands[(int)Hand.Right];
            var left = hands[(int)Hand.Left];
            UpdateHand(right, control, input.Pressed(GameAction.Primary), input.Held(GameAction.Primary));
            UpdateHand(left, control, input.Pressed(GameAction.Secondary), input.Held(GameAction.Secondary));

            var wasBlocking = Blocking;
            Blocking = control && attacking == null &&
                       ((right.Item.Kind == ItemKind.Shield && right.Holding) || (left.Item.Kind == ItemKind.Shield && left.Holding));
            session.Stamina.Blocking = Blocking;
            if (Blocking && !wasBlocking && !session.Stamina.TrySpend(playerConfig.Stamina.BlockStartCost))
                Blocking = false;

            if (attacking != null)
                UpdateAttack(dt);
            UpdateBodyTwist();
        }

        private void UpdateHand(HandState hand, bool control, bool pressed, bool held)
        {
            hand.Holding = control && held;
            if (!control || !pressed || hand.Item.Kind == ItemKind.Shield || hand.Item.Combo.Count == 0)
                return;

            // Двуручное: обе кнопки управляют правой рукой.
            var actor = hand;
            if (hand.Hand == Hand.Left && hand.Item.IsTwoHanded)
                actor = hands[(int)Hand.Right];

            if (attacking != null)
            {
                // Следующий удар серии ставится в очередь, если нажали в той же руке до конца удара.
                if (attacking == actor)
                    queued = true;
                return;
            }

            StartAttack(actor, comboTimer > 0f && lastHand == actor ? comboIndex + 1 : 0);
        }

        private HandState lastHand;

        private void StartAttack(HandState hand, int index)
        {
            var combo = hand.Item.Combo;
            index %= combo.Count;
            var next = combo[index];
            if (!session.Stamina.TrySpend(next.StaminaCost))
                return;
            attacking = hand;
            lastHand = hand;
            comboIndex = index;
            step = next;
            attackTime = 0f;
            queued = false;
            lungeDone = 0f;
            hitThisSwing.Clear();
            controller.Deformer.Impulse(-0.6f, new Vector2(0f, 0.6f));
        }

        public void CancelAttack()
        {
            attacking = null;
            queued = false;
            foreach (var hand in hands)
                hand.ResetPose = true;
        }

        private void UpdateAttack(float dt)
        {
            attackTime += dt;
            var t = attackTime / step.Duration;
            if (t >= step.ActiveFrom && t <= step.ActiveTo)
                DetectHits();

            if (t >= 1f)
            {
                var hand = attacking;
                attacking = null;
                comboTimer = hand.Item.ComboWindow;
                if (queued)
                    StartAttack(hand, comboIndex + 1);
            }
        }

        /// <summary>
        /// Куб скручивается вслед за взмахом: на замахе закручивается против взмаха, в ударе — по взмаху
        /// (пружина даёт перехлёст), потом раскручивается. Рубящий удар — наклон назад на замахе и вперёд в ударе.
        /// </summary>
        private void UpdateBodyTwist()
        {
            var deformer = controller.Deformer;
            if (deformer == null)
                return;
            if (attacking == null || step == null)
            {
                deformer.TwistTarget = 0f;
                deformer.AttackLeanTarget = 0f;
                return;
            }

            var d = playerConfig.Deformation;
            var t = Mathf.Clamp01(attackTime / step.Duration);
            var a = Mathf.Max(0.01f, step.ActiveFrom);
            var b = Mathf.Max(a, step.ActiveTo);
            // 0..1 замах, 1 удар, затем возврат к 0.
            float wind, strike;
            if (t < a)
            {
                wind = EaseOut(t / a);
                strike = 0f;
            }
            else if (t <= b)
            {
                wind = 0f;
                strike = 1f;
            }
            else
            {
                wind = 0f;
                strike = 1f - EaseInOut((t - b) / Mathf.Max(0.01f, 1f - b));
            }

            switch (step.Motion)
            {
                case AttackMotion.SlashLeft:   // слева направо: замах — верх влево, удар — вправо
                case AttackMotion.SlashRight:  // справа налево: наоборот
                {
                    var sign = step.Motion == AttackMotion.SlashLeft ? 1f : -1f;
                    deformer.TwistTarget = sign * d.AttackTwist * (strike - wind * 0.7f);
                    deformer.AttackLeanTarget = d.ChopLean * 0.25f * strike;
                    break;
                }
                case AttackMotion.Chop:
                    deformer.TwistTarget = 0f;
                    deformer.AttackLeanTarget = d.ChopLean * (strike - wind * 0.6f);
                    break;
                default:
                    deformer.TwistTarget = 0f;
                    deformer.AttackLeanTarget = d.ChopLean * 0.5f * strike;
                    break;
            }
        }

        /// <summary>Рывок вперёд при ударе: отдаётся контроллеру движения по кадрам до конца активной фазы.</summary>
        public Vector3 ConsumeLunge(Vector3 forward)
        {
            if (attacking == null || step.Lunge <= 0f)
                return Vector3.zero;
            var t = Mathf.Clamp01(attackTime / (step.Duration * step.ActiveTo));
            var target = step.Lunge * (1f - (1f - t) * (1f - t));
            var delta = target - lungeDone;
            lungeDone = target;
            return forward * delta;
        }

        // ------------------------------------------------------------------ попадания

        private void DetectHits()
        {
            var center = transform.position + Vector3.up * playerConfig.ColliderHeight * 0.5f;
            var forward = controller.Forward;
            var count = Physics.OverlapSphereNonAlloc(center, step.Range + 0.5f, overlap, ~(1 << gameObject.layer),
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var target = overlap[i].GetComponentInParent<IHittable>();
                if (target == null || hitThisSwing.Contains(target))
                    continue;
                var to = overlap[i].ClosestPoint(center) - center;
                to.y = 0;
                if (to.magnitude > step.Range)
                    continue;
                if (to.sqrMagnitude > 0.0001f && Vector3.Angle(forward, to) > step.Arc * 0.5f)
                    continue;

                hitThisSwing.Add(target);
                var weapon = attacking.Item;
                var result = rules.Calculate(weapon, step, target.ColorId);
                var color = palette.Get(weapon.Color);
                target.ReceiveHit(new HitInfo
                {
                    Damage = result.Damage,
                    ColorBonus = result.ColorBonus,
                    WeaponColor = color,
                    Direction = to.sqrMagnitude > 0.0001f ? to.normalized : forward,
                    Point = target.HitPoint,
                    Knockback = step.Knockback
                });
                events.Publish(new DamageDealt(target.HitPoint, result.Damage, result.ColorBonus, color));
                // Импакт: толчок камеры по направлению удара и фриз-кадры, у финального удара сильнее.
                var weight = Mathf.Max(1f, step.Knockback);
                if (impulse != null && combatConfig.HitShake > 0f)
                    impulse.GenerateImpulseWithVelocity((controller.Forward + Vector3.down * 0.5f) * (combatConfig.HitShake * weight));
                controller.Deformer.Impulse(-1.2f * weight);
                if (combatConfig.HitStop > 0f)
                    StartHitStop(combatConfig.HitStop * weight);
            }
        }

        /// <summary>Фриз-кадры: время стоит, пока не истечёт таймер по реальному времени кадров.</summary>
        private void StartHitStop(float duration)
        {
            if (hitStopTimer <= 0f)
                timeScaleBeforeHitStop = Time.timeScale > 0.05f ? Time.timeScale : 1f;
            hitStopTimer = Mathf.Max(hitStopTimer, duration);
            Time.timeScale = 0f;
        }

        private void UpdateHitStop()
        {
            if (hitStopTimer <= 0f)
                return;
            hitStopTimer -= Time.unscaledDeltaTime;
            if (hitStopTimer <= 0f)
                Time.timeScale = timeScaleBeforeHitStop;
        }

        private void OnDisable()
        {
            if (hitStopTimer > 0f)
            {
                hitStopTimer = 0f;
                Time.timeScale = timeScaleBeforeHitStop;
            }
        }

        // ------------------------------------------------------------------ позы рук

        private void LateUpdate()
        {
            if (playerConfig == null || controller == null)
                return;
            var deformer = controller.Deformer;
            var dt = Time.deltaTime;
            foreach (var hand in hands)
            {
                if (hand.Socket == null)
                    continue;
                Pose pose;
                if (attacking == hand)
                    pose = AttackPose(hand, Mathf.Clamp01(attackTime / step.Duration));
                else if (Blocking && hand.Item.Kind == ItemKind.Shield && hand.Holding)
                    pose = BlockPose(hand);
                else
                    pose = RestPose(hand);

                // Плавный возврат после отмены удара и при смене позы.
                var k = attacking == hand ? 1f : 1f - Mathf.Exp(-dt * 25f);
                if (hand.ResetPose)
                {
                    k = 1f;
                    hand.ResetPose = false;
                }

                hand.Current = new Pose(Vector3.Lerp(hand.Current.position, pose.position, k),
                    Quaternion.Slerp(hand.Current.rotation, pose.rotation, k));
                hand.Socket.localPosition = deformer.DeformPoint(hand.Current.position);
                hand.Socket.localRotation = deformer.DeformRotation(hand.Current.position) * hand.Current.rotation;
                if (hand.Trail != null)
                {
                    var t = attacking == hand ? attackTime / step.Duration : -1f;
                    hand.Trail.Emitting = t >= step?.ActiveFrom - 0.1f && t <= step?.ActiveTo + 0.12f;
                }
            }
        }

        private Pose RestPose(HandState hand)
        {
            var s = hand.Side;
            switch (hand.Item.Kind)
            {
                case ItemKind.Shield:
                    return new Pose(new Vector3(0.68f * s, 0.58f, 0.12f), Quaternion.Euler(0f, 55f * s, 0f));
                case ItemKind.OneHanded when hand.Visual != null:
                    return new Pose(new Vector3(0.7f * s, 0.42f, 0.12f), Quaternion.Euler(18f, 0f, -24f * s));
                case ItemKind.TwoHanded:
                    return new Pose(new Vector3(0.5f * s, 0.45f, 0.35f), Quaternion.Euler(40f, 0f, 30f * -s));
                default:
                    return new Pose(new Vector3(0.64f * s, 0.38f, 0.12f), Quaternion.identity);
            }
        }

        private static Pose BlockPose(HandState hand)
        {
            return new Pose(new Vector3(0.12f * hand.Side, 0.56f, 0.72f), Quaternion.identity);
        }

        private Pose AttackPose(HandState hand, float t)
        {
            var rest = RestPose(hand);
            var a = step.ActiveFrom;
            var b = step.ActiveTo;
            if (t < a)
            {
                var start = ArcPose(hand, 0f);
                var u = EaseOut(t / Mathf.Max(0.0001f, a));
                return new Pose(Vector3.Lerp(rest.position, start.position, u), Quaternion.Slerp(rest.rotation, start.rotation, u));
            }

            if (t <= b)
                return ArcPose(hand, EaseInOut((t - a) / Mathf.Max(0.0001f, b - a)));

            var end = ArcPose(hand, 1f);
            var r = EaseInOut((t - b) / Mathf.Max(0.0001f, 1f - b));
            return new Pose(Vector3.Lerp(end.position, rest.position, r), Quaternion.Slerp(end.rotation, rest.rotation, r));
        }

        /// <summary>Траектория удара, p = 0..1 — от начала замаха до конца удара. Координаты меша: +Z вперёд, +X вправо.</summary>
        private Pose ArcPose(HandState hand, float p)
        {
            var s = hand.Side;
            switch (step.Motion)
            {
                case AttackMotion.SlashRight:
                case AttackMotion.SlashLeft:
                {
                    // Горизонтальный взмах широкой дугой перед кубом. SlashLeft — слева направо, SlashRight — справа налево.
                    // Tilt наклоняет плоскость: взмах начинается выше и заканчивается ниже.
                    var leftToRight = step.Motion == AttackMotion.SlashLeft;
                    var from = leftToRight ? -115f : 115f;
                    var to = leftToRight ? 100f : -100f;
                    var yaw = Mathf.Lerp(from, to, p) * Mathf.Deg2Rad;
                    var tilt = Mathf.Sin(step.Tilt * Mathf.Deg2Rad) * (1f - 2f * p);
                    var dir = new Vector3(Mathf.Sin(yaw), -0.04f + tilt * 0.8f, Mathf.Cos(yaw)).normalized;
                    var pos = new Vector3(0f, 0.5f + tilt * 0.25f, 0.05f) + new Vector3(dir.x, 0f, dir.z) * 0.55f;
                    return new Pose(pos, Quaternion.FromToRotation(Vector3.up, dir));
                }
                case AttackMotion.Chop:
                {
                    var angle = Mathf.Lerp(-25f, 125f, p) * Mathf.Deg2Rad;
                    var dir = new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
                    var pos = new Vector3(0.15f * s, 0.6f, 0.1f) + dir * 0.45f;
                    return new Pose(pos, Quaternion.FromToRotation(Vector3.up, dir));
                }
                case AttackMotion.Thrust:
                {
                    var pos = new Vector3(0.3f * s, 0.45f, Mathf.Lerp(0.1f, 0.95f, p));
                    return new Pose(pos, Quaternion.FromToRotation(Vector3.up, Vector3.forward));
                }
                default: // Punch
                {
                    var back = new Vector3(0.62f * s, 0.4f, -0.12f);
                    var hit = new Vector3(0.22f * s, 0.48f, 0.95f);
                    return new Pose(Vector3.Lerp(back, hit, p), Quaternion.identity);
                }
            }
        }

        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
        private static float EaseInOut(float x) => x * x * (3f - 2f * x);

        private sealed class HandState
        {
            public readonly Hand Hand;
            public readonly Transform Socket;
            public readonly float Side;
            public ItemConfig Item;
            public GameObject Visual;
            public WeaponTrail Trail;
            public bool Holding;
            public bool ResetPose = true;
            public Pose Current = new Pose(Vector3.zero, Quaternion.identity);

            public HandState(Hand hand, Transform socket, float side)
            {
                Hand = hand;
                Socket = socket;
                Side = side;
            }
        }
    }
}
