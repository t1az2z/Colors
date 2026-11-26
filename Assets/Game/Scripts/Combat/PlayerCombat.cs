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

        /// <summary>Урон получил игрок (цифра красная).</summary>
        public readonly bool ToPlayer;

        public DamageDealt(Vector3 position, float amount, bool colorBonus, Color color, bool toPlayer = false)
        {
            Position = position;
            Amount = amount;
            ColorBonus = colorBonus;
            Color = color;
            ToPlayer = toPlayer;
        }
    }

    /// <summary>Успешное парирование щитом: сюда цепляются звук и VFX.</summary>
    public readonly struct ParrySucceeded
    {
        public readonly Vector3 Position;
        public readonly int Count;

        public ParrySucceeded(Vector3 position, int count)
        {
            Position = position;
            Count = count;
        }
    }

    /// <summary>
    /// Две руки: ЛКМ/RT — правая, ПКМ/LT — левая. Оружие в руке атакует серией ударов из конфига,
    /// щит в руке ставит блок на её кнопку. Двуручное оружие отвечает на обе кнопки.
    /// Позы рук считаются процедурно и проходят через ту же деформацию, что и куб.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed partial class PlayerCombat : GameBehaviour
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
        private bool queuedRestart;
        private bool holdAccented;
        private float comboTimer;
        private float lungeDone;
        private float hitStopTimer;
        private float timeScaleBeforeHitStop = 1f;

        // Стрелковое оружие (дробовик): выстрел, пули и заряд — PlayerCombat.Shotgun.cs
        private Vector3 recoilVector;
        private float recoilDuration;
        private float recoilElapsed;
        private float recoilDone;

        /// <summary>Сколько выстрелов сделано за игру (для автотестов).</summary>
        public int ShotCount { get; private set; }

        /// <summary>Сколько пуль попало в цели за игру (для автотестов).</summary>
        public int PelletHitCount { get; private set; }

        public bool IsBusy => attacking != null;
        public bool Blocking { get; private set; }
        public bool LockFacing => (attacking != null && attackTime > step.Duration * step.ActiveFrom * 0.5f) || hands[0]?.ShotTime < 0.25f || hands[1]?.ShotTime < 0.25f;
        public Vector3? FacingOverride => null;

        /// <summary>Перекат разрешён: нет удара или удар вне своего окна запрета переката (RollLockFrom..RollLockTo).</summary>
        public bool CanInterruptForRoll => attacking == null || Charging || !step.RollLocked(attackTime);

        public float MoveMultiplier
        {
            get
            {
                // Удар в прыжке и удар с подскоком не гасят инерцию полёта.
                if (attacking != null && (Plunging || LeapStep))
                    return 1f;
                if (attacking != null)
                    return playerConfig.AttackMoveMultiplier;
                if (Bashing)
                    return playerConfig.AttackMoveMultiplier;
                if (Blocking || GunCharging)
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
            if (item != null)
                return item.Config;
            // Кулаки — не оружие по умолчанию: без предмета рука не атакует (UnarmedItem пустой).
            return string.IsNullOrEmpty(combatConfig.UnarmedItem) || !items.Exists(combatConfig.UnarmedItem)
                ? null
                : items.Get(combatConfig.UnarmedItem);
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
                if (hand.Visual != null && !item.Config.IsRanged &&
                    (item.Config.Kind == ItemKind.OneHanded || item.Config.Kind == ItemKind.TwoHanded))
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
            // Оружие на слое игрока: его тоже видно силуэтом за блоками.
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = gameObject.layer;
            // Хват: поворот модели вокруг оси клинка (например, рубящей кромкой вниз).
            go.transform.localRotation = Quaternion.Euler(0f, config.GripRoll, 0f);
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
            UpdateGun(control, dt);
            UpdateShieldBash(control, dt);

            var wasBlocking = Blocking;
            Blocking = control && attacking == null && !Bashing &&
                       ((right.Item?.Kind == ItemKind.Shield && right.Holding) || (left.Item?.Kind == ItemKind.Shield && left.Holding));
            session.Stamina.Blocking = Blocking;
            if (Blocking && !wasBlocking && !session.Stamina.TrySpend(playerConfig.Stamina.BlockStartCost * BlockCostMultiplier))
                Blocking = false;
            // Парирование: блок поставлен ровно в окно парирования вражеской атаки.
            if (Blocking && !wasBlocking)
                TryParry();

            if (attacking != null)
                UpdateAttack(dt);
            UpdateBodyTwist();
        }

        private void UpdateHand(HandState hand, bool control, bool pressed, bool held)
        {
            hand.Holding = control && held;
            if (!control || !pressed || hand.Item == null || hand.Item.Kind == ItemKind.Shield || hand.Item.IsRanged ||
                hand.Item.Combo.Count == 0)
                return;

            // Двуручное: обе кнопки управляют правой рукой.
            var actor = hand;
            if (hand.Hand == Hand.Left && hand.Item.IsTwoHanded)
                actor = hands[(int)Hand.Right];

            if (attacking != null)
            {
                if (attacking != actor)
                    return;
                // Окно комбо — фиксация финальной позы: нажатие в ней сразу запускает следующий удар.
                if (InHold)
                {
                    StartAttack(actor, comboIndex + 1);
                    return;
                }

                // После окна комбо, на возврате: пауза в серии — добивающий удар (если он есть у оружия).
                if (attackTime >= StrikeEnd && TryStartFinisher(actor))
                    return;

                // Раньше — буфер: следующий удар начнётся, когда начнётся фиксация (после короткого акцента).
                // Позже (на возврате) — серия начнётся заново, когда удар закончится.
                if (attackTime < StrikeEnd)
                    queued = true;
                else
                    queuedRestart = true;
                return;
            }

            // В воздухе — удар в прыжке; после паузы в серии — добивающий.
            if (TryStartPlunge(actor) || TryStartFinisher(actor))
                return;
            StartAttack(actor, comboTimer > 0f && lastHand == actor ? comboIndex + 1 : 0);
        }

        private HandState lastHand;

        private void StartAttack(HandState hand, int index)
        {
            var combo = hand.Item.Combo;
            var continuing = index > 0;
            // Удары за апгрейдами серия пропускает (меч: четвёртый удар открывает апгрейд 1).
            index = UnlockedComboIndex(hand.Item, index);
            if (index < 0)
                return;
            var next = combo[index];
            if (!session.Stamina.TrySpend(next.StaminaCost))
                return;
            BeginStep(hand, next, index);
            finisher.ComboAttack(continuing);
        }

        /// <summary>Запустить удар <paramref name="next"/> (из серии или особый: index = -1, следующее нажатие начнёт серию).</summary>
        private void BeginStep(HandState hand, AttackStepConfig next, int index)
        {
            attacking = hand;
            lastHand = hand;
            comboIndex = index;
            step = next;
            attackTime = 0f;
            queued = false;
            queuedRestart = false;
            holdAccented = false;
            lungeDone = 0f;
            hitThisSwing.Clear();
            // Замах начинается оттуда, где рука сейчас (например, из финальной позы прошлого удара серии),
            // а не из стойки — иначе следующий удар делал лишний взмах через стойку.
            hand.WindupFrom = hand.Current;
            controller.Deformer.Impulse(-0.6f, new Vector2(0f, 0.6f));
            OnStepStarted(next);
        }

        public void CancelAttack()
        {
            ResetMoves();
            attacking = null;
            queued = false;
            queuedRestart = false;
            foreach (var hand in hands)
                hand.ResetPose = true;
        }

        private void UpdateAttack(float dt)
        {
            // Заряд и удар в прыжке сами ведут время удара (пауза на замахе, падение до земли).
            if (UpdateMoves(dt))
                return;
            var previousTime = attackTime;
            attackTime += dt;
            // Окно урона внутри удара (HitFrom..HitTo от длительности Strike), в секундах от начала удара.
            // Проверяем, задел ли кадр (previousTime..attackTime] это окно, а не попал ли конец кадра внутрь:
            // иначе при длинном кадре короткое окно «проскакивало» и удар не наносил урон.
            var strikeStart = step.Duration * step.ActiveFrom;
            var strikeLength = StrikeEnd - strikeStart;
            var hitStart = strikeStart + strikeLength * step.HitFrom;
            var hitEnd = strikeStart + strikeLength * step.HitTo;
            if (!Plunging && attackTime >= hitStart - 1e-4f && previousTime <= hitEnd + 1e-4f)
                DetectHits();

            if (InHold)
            {
                // Акцент на финальной позе: короткое сжатие куба в момент фиксации.
                if (!holdAccented)
                {
                    holdAccented = true;
                    controller.Deformer.Impulse(-1.4f);
                }

                // Буферизованное нажатие срабатывает, дав акценту прочитаться.
                if (queued && attackTime >= StrikeEnd + step.Hold * 0.4f)
                {
                    StartAttack(attacking, comboIndex + 1);
                    return;
                }
            }

            if (attackTime >= step.Duration + step.Hold)
            {
                var hand = attacking;
                attacking = null;
                comboTimer = hand.Item.ComboWindow;
                if (queued)
                    StartAttack(hand, comboIndex + 1);
                else if (queuedRestart)
                    StartAttack(hand, 0);
            }
        }

        /// <summary>Конец удара (начало фиксации финальной позы), секунды от начала атаки.</summary>
        private float StrikeEnd => step.Duration * step.ActiveTo;

        /// <summary>Идёт фиксация финальной позы — окно для следующего удара серии.</summary>
        public bool InHold => attacking != null && attackTime >= StrikeEnd && attackTime < StrikeEnd + step.Hold;

        /// <summary>
        /// Нормированное время удара для поз и попаданий: замах и удар как есть, на фиксации стоит на конце удара,
        /// возврат — после фиксации.
        /// </summary>
        private float StepT
        {
            get
            {
                if (attackTime < StrikeEnd)
                    return attackTime / step.Duration;
                if (attackTime < StrikeEnd + step.Hold)
                    return step.ActiveTo;
                return Mathf.Clamp01((attackTime - step.Hold) / step.Duration);
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
                deformer.PitchTarget = 0f;
                return;
            }

            var d = playerConfig.Deformation;
            var t = StepT;
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
                    // Замах — верх отклоняется назад, удар — подаётся вперёд.
                    deformer.PitchTarget = d.SwingPitchForward * strike - d.SwingPitchBack * wind;
                    break;
                }
                case AttackMotion.Chop:
                    deformer.TwistTarget = 0f;
                    // Рубящий: на замахе отклон назад, в ударе — поклон вперёд-вниз (верх загибается над передней гранью).
                    deformer.PitchTarget = d.ChopPitch * strike - d.ChopPitchBack * wind;
                    break;
                case AttackMotion.Thrust:
                {
                    // Укол: на замахе бок с оружием уходит назад, в уколе — подаётся вперёд, куб тянется за клинком.
                    var side = attacking != null ? attacking.Side : 1f;
                    deformer.TwistTarget = side * d.AttackTwist * 0.6f * (wind - strike);
                    deformer.PitchTarget = d.SwingPitchForward * 1.2f * strike - d.SwingPitchBack * wind;
                    break;
                }
                default:
                    deformer.TwistTarget = 0f;
                    deformer.PitchTarget = d.SwingPitchForward * strike - d.SwingPitchBack * 0.5f * wind;
                    break;
            }
        }

        // Стрельба — PlayerCombat.Shotgun.cs; блок, парирование и удар щитом — PlayerCombat.Shield.cs.

        /// <summary>Толчок камеры (попадание по игроку, блок).</summary>
        public void Shake(float strength)
        {
            if (impulse != null && strength > 0f)
                impulse.GenerateImpulseWithVelocity((Random.onUnitSphere + Vector3.down) * strength);
        }

        /// <summary>
        /// Рывок при ударе: отдаётся контроллеру движения по кадрам до конца активной фазы.
        /// Положительный <see cref="AttackStepConfig.Lunge"/> — вперёд, отрицательный — шаг назад.
        /// </summary>
        public Vector3 ConsumeLunge(Vector3 forward)
        {
            var recoil = ConsumeRecoil() + ConsumeBashLunge(forward);
            // Отрицательный выпад — шаг назад. У удара с подскоком выпад уже отдан полётом.
            if (attacking == null || LeapStep || Mathf.Abs(step.Lunge) < 1e-4f)
                return recoil;
            var t = Mathf.Clamp01(attackTime / (step.Duration * step.ActiveTo));
            var target = step.Lunge * (1f - (1f - t) * (1f - t));
            var delta = target - lungeDone;
            lungeDone = target;
            return forward * delta + recoil;
        }

        /// <summary>Сдвиг отдачи выстрела за кадр: быстрый толчок назад с замедлением, всего Recoil метров.</summary>
        private Vector3 ConsumeRecoil()
        {
            if (recoilElapsed >= recoilDuration || recoilVector == Vector3.zero)
                return Vector3.zero;
            recoilElapsed += Time.deltaTime;
            var k = EaseOut(Mathf.Clamp01(recoilElapsed / recoilDuration));
            var delta = recoilVector * (k - recoilDone);
            recoilDone = k;
            return delta;
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
                // Угол — на центр цели: у узкого сектора (рубящий удар) угол на ближайшую точку вплотную
                // к цели "уезжает" вбок, и удар промахивался. Цель почти вплотную задевается всегда.
                var toCenter = overlap[i].bounds.center - center;
                toCenter.y = 0;
                var veryClose = to.magnitude < 0.6f;
                if (!veryClose && toCenter.sqrMagnitude > 0.0001f && Vector3.Angle(forward, toCenter) > step.Arc * 0.5f)
                    continue;

                hitThisSwing.Add(target);
                var weapon = attacking.Item;
                var color = palette.Get(weapon.Color);
                var direction = to.sqrMagnitude > 0.0001f ? to.normalized : forward;
                // Не тем оружием (ящики — только мечом): цель лишь вздрагивает, без урона и фриз-кадров.
                if (HitGate.Deflects(target, weapon.Id, direction, color))
                {
                    controller.Deformer.Impulse(-0.6f);
                    continue;
                }

                var result = WithCharge(rules.Calculate(weapon, step, target.ColorId));
                target.ReceiveHit(new HitInfo
                {
                    Damage = result.Damage,
                    ColorBonus = result.ColorBonus,
                    WeaponColor = color,
                    Direction = direction,
                    Point = target.HitPoint,
                    Knockback = step.Knockback * ChargeKnockback,
                    WeaponId = weapon.Id
                });
                if (HitGate.ShowsDamage(target))
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
                var gunHand = attacking != hand && hand.Item != null && hand.Item.IsRanged && hand.Visual != null;
                if (attacking == hand)
                    pose = AttackPose(hand, StepT);
                else if (gunHand)
                    pose = GunPose(hand);
                else if (bashHand == hand)
                    pose = BashPose(hand);
                else if (Blocking && hand.Item?.Kind == ItemKind.Shield && hand.Holding)
                    pose = BlockPose(hand);
                else
                    pose = RestPose(hand);

                // Плавный возврат после отмены удара и при смене позы.
                var k = attacking == hand || gunHand || bashHand == hand ? 1f : 1f - Mathf.Exp(-dt * 25f);
                if (hand.ResetPose)
                {
                    k = 1f;
                    hand.ResetPose = false;
                }

                hand.Current = k >= 1f ? pose : AroundBody(hand.Current, pose, k);
                hand.Socket.localPosition = deformer.DeformPoint(hand.Current.position);
                hand.Socket.localRotation = deformer.DeformRotation(hand.Current.position) * hand.Current.rotation;
                if (gunHand)
                    UpdateGunModel(hand);
                if (hand.Trail != null)
                {
                    hand.Trail.Emitting = TrailEmitting(hand);
                    if (step != null && hand.Item != null)
                        hand.Trail.MaxRadius = hand.Item.TrailRange > 0f ? hand.Item.TrailRange : step.Range;
                }
            }
        }

        private Pose RestPose(HandState hand)
        {
            var s = hand.Side;
            if (hand.Item == null)
                return new Pose(new Vector3(0.64f * s, 0.38f, 0.12f), Quaternion.identity);
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
                return AroundBody(hand.WindupFrom, start, u);
            }

            // Удар резкий: быстро набирает ход и жёстко встаёт в финальную позу (её держит Hold).
            if (t <= b)
                return ArcPose(hand, EaseOutCubic((t - a) / Mathf.Max(0.0001f, b - a)));

            var end = ArcPose(hand, 1f);
            var r = EaseInOut((t - b) / Mathf.Max(0.0001f, 1f - b));
            return AroundBody(end, rest, r);
        }

        /// <summary>
        /// Для автотестов: насколько глубоко клинок в руке заходит внутрь куба (метры, 0 — не заходит).
        /// Проверяются точки вдоль клинка в координатах меша куба (без учёта деформации).
        /// </summary>
        public float BladePenetration(Hand which)
        {
            var hand = hands[(int)which];
            if (hand?.Visual == null || hand.Socket == null || hand.Socket.parent == null)
                return 0f;
            var mesh = hand.Socket.parent;
            var filter = hand.Visual.GetComponentInChildren<MeshFilter>();
            var length = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.max.y : 1f;
            var worst = 0f;
            for (int i = 0; i <= 10; i++)
            {
                var world = hand.Socket.TransformPoint(Vector3.up * (length * i / 10f));
                var p = mesh.InverseTransformPoint(world);
                var depth = Mathf.Min(0.5f - Mathf.Abs(p.x), 0.5f - Mathf.Abs(p.z), p.y, 1f - p.y);
                worst = Mathf.Max(worst, depth);
            }

            return worst;
        }

        /// <summary>Радиус вокруг оси куба, ближе которого рука не подходит (куб 1×1, угол — 0.71 от центра).</summary>
        public const float BodyClearance = 0.74f;

        /// <summary>
        /// Переход между позами руки по дуге вокруг куба, а не напрямую: угол вокруг вертикальной оси
        /// интерполируется линейно (путь идёт через перед куба), радиус не меньше <see cref="BodyClearance"/>,
        /// поэтому меч не проходит сквозь тело на замахе и возврате.
        /// </summary>
        public static Pose AroundBody(Pose from, Pose to, float k)
        {
            var a0 = Mathf.Atan2(from.position.x, from.position.z) * Mathf.Rad2Deg;
            var a1 = Mathf.Atan2(to.position.x, to.position.z) * Mathf.Rad2Deg;
            var r0 = new Vector2(from.position.x, from.position.z).magnitude;
            var r1 = new Vector2(to.position.x, to.position.z).magnitude;
            var angle = Mathf.Lerp(a0, a1, k) * Mathf.Deg2Rad;
            var radius = Mathf.Lerp(r0, r1, k);
            // Посередине пути держим руку снаружи тела, у концов — как задано позами.
            var bulge = Mathf.Sin(k * Mathf.PI);
            radius = Mathf.Max(radius, Mathf.Lerp(Mathf.Min(r0, r1), BodyClearance, bulge));
            var y = Mathf.Lerp(from.position.y, to.position.y, k);
            var pos = new Vector3(Mathf.Sin(angle) * radius, y, Mathf.Cos(angle) * radius);
            return new Pose(pos, Quaternion.Slerp(from.rotation, to.rotation, k));
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
                    // Радиус дуги больше полудиагонали куба (0.71): рукоять проходит снаружи углов.
                    var pos = new Vector3(0f, 0.5f + tilt * 0.25f, 0f) + new Vector3(dir.x, 0f, dir.z) * 0.78f;
                    return new Pose(pos, Quaternion.FromToRotation(Vector3.up, dir));
                }
                case AttackMotion.Chop:
                {
                    // Высоко над головой и вниз перед кубом: ось дуги над передним верхним ребром,
                    // поэтому рукоять всё время снаружи тела.
                    // Рукоять идёт по дуге вокруг точки перед верхом куба: от высоко над головой до ~0.25 над полом
                    // впереди. Клинок поворачивается быстрее рукояти и в конце смотрит вперёд, чуть вниз.
                    var gripAngle = Mathf.Lerp(-30f, 135f, p) * Mathf.Deg2Rad;
                    var pos = new Vector3(0.18f * s, 0.8f, 0.45f) + new Vector3(0f, Mathf.Cos(gripAngle), Mathf.Sin(gripAngle)) * 0.75f;
                    var bladeAngle = Mathf.Lerp(-10f, 100f, p) * Mathf.Deg2Rad;
                    var dir = new Vector3(0f, Mathf.Cos(bladeAngle), Mathf.Sin(bladeAngle));
                    return new Pose(pos, Quaternion.FromToRotation(Vector3.up, dir));
                }
                case AttackMotion.Thrust:
                {
                    // Укол: клинок отведён назад у бока куба, затем прямой выпад вперёд. Пока рука у бока,
                    // она снаружи грани (|x| ≥ 0.64), к оси куба сходится только перед ним — клинок не входит в тело.
                    var z = Mathf.Lerp(-0.3f, 1.05f, p);
                    var inward = Mathf.Clamp01((z - 0.55f) / 0.5f);
                    var pos = new Vector3(Mathf.Lerp(0.64f, 0.3f, inward) * s, Mathf.Lerp(0.5f, 0.46f, p), z);
                    // Клинок смотрит вперёд и чуть к оси куба, на отводе — немного вверх.
                    var dir = new Vector3(-0.1f * s, Mathf.Lerp(0.12f, 0f, p), 1f).normalized;
                    return new Pose(pos, Quaternion.FromToRotation(Vector3.up, dir));
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
        private static float EaseOutCubic(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
        private static float EaseInOut(float x) => x * x * (3f - 2f * x);

        private sealed class HandState
        {
            public readonly Hand Hand;
            public readonly Transform Socket;
            public readonly float Side;
            public ItemConfig Item;
            public GameObject Visual;

            /// <summary>Доля прицела стрелкового оружия 0..1 (0 — поза покоя).</summary>
            public float GunAim;
            public WeaponTrail Trail;
            public Pose WindupFrom;
            public bool Holding;
            public bool ResetPose = true;
            public Pose Current = new Pose(Vector3.zero, Quaternion.identity);

            /// <summary>Курок стрелкового оружия в этой руке и время с её последнего выстрела.</summary>
            public readonly GunTrigger Gun = new GunTrigger();
            public float ShotTime = 10f;

            public HandState(Hand hand, Transform socket, float side)
            {
                Hand = hand;
                Socket = socket;
                Side = side;
            }
        }
    }
}
