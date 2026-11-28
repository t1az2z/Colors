using ColorsAction.Configs;
using ColorsAction.Inventory;
using GameCore.Config;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Сцена подбора нового оружия или лечилки (Configs/PickupSequence.json, <see cref="PickupSequenceConfig"/>), 5 секунд:
    /// мир застывает (<see cref="GameTimeScale.SetCinematic"/>), камера подъезжает к персонажу (<see cref="CameraRig.SetShot"/>),
    /// предмет плавно поднимается, персонаж поворачивается к нему и следит глазами (поднимает взгляд), затем предмет
    /// с замахом и панчем влетает в персонажа и сам надевается (оружие — в руки); камера показывает персонажа совсем
    /// близко, он радуется (подпрыгивает, поднимает оружие, прищуривается), вокруг — эффект как у лечения без плюсиков
    /// в цвет предмета (<see cref="ParticleFx.PickupBurst"/>) и свечение тела; потом камера возвращается.
    /// <para>Всё идёт по реальному времени: пружины куба, позы рук и частицы на это время переводятся на unscaled.
    /// Компонент добавляет на игрока <see cref="PlayerInteractor"/>; сцену запускает его F / X по предмету.</para>
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class PickupSequence : GameBehaviour
    {
        /// <summary>Идёт ли сейчас сцена (инвентарь и колесо оружия в это время не открываются).</summary>
        public static bool Playing { get; private set; }

        private PickupSequenceConfig config;
        private GameSession session;
        private ItemDatabase itemsDb;
        private Palette palette;

        private PlayerController controller;
        private PlayerCombat combat;
        private PlayerDamageReceiver damage;
        private CameraRig rig;

        private bool active;
        private float time;
        private string itemId;
        private bool isWeapon;
        private Color itemColor;

        // Предмет
        private Transform model;
        private Vector3 modelStart;
        private Quaternion modelRotation;
        private Vector3 modelScale;
        private Vector3 hoverPoint;
        private float spin;
        private bool hit;
        private bool burst;

        // Персонаж
        private float startYaw;
        private float faceYaw;
        private float lastHop;

        // Камера: обычный вид в начале сцены и два кадра — план подъёма и крупный план.
        private float baseYaw;
        private float basePitch;
        private float baseDistance;
        private float riseYaw;
        private float closeYaw;

        public PickupSequenceConfig Config => config;

        /// <summary>Сколько секунд идёт текущая сцена (для автотестов).</summary>
        public float Elapsed => active ? time : 0f;

        /// <summary>Летящий предмет (null — ещё не начали или уже влетел в персонажа). Для автотестов.</summary>
        public Transform Model => model;

        protected override void OnConstruct(IResolver resolver)
        {
            var configs = resolver.Resolve<ConfigService>();
            config = configs.Exists(PickupSequenceConfig.Key)
                ? configs.Load<PickupSequenceConfig>(PickupSequenceConfig.Key)
                : new PickupSequenceConfig();
            session = resolver.Resolve<GameSession>();
            itemsDb = resolver.Resolve<ItemDatabase>();
            palette = resolver.Resolve<Palette>();
        }

        /// <summary>
        /// Начать сцену для предмета <paramref name="pickup"/>, если он того стоит: новое оружие или новая лечилка,
        /// персонаж стоит на земле. false — сцены нет, предмет подбирается как обычно.
        /// </summary>
        public bool TryBegin(Pickup pickup)
        {
            if (active || config == null || pickup == null || !pickup.Available || pickup.KeyFor != null)
                return false;
            if (!itemsDb.Exists(pickup.ItemId))
                return false;
            var item = itemsDb.Get(pickup.ItemId);
            var owned = item.IsWeapon ? session.Inventory.HasWeapon(item.Id) : session.Inventory.CountOf(item.Id) > 0;
            if (!config.Plays(item, owned))
                return false;
            // Лечилка без места в инвентаре подбирается обычным путём (там и сообщение «инвентарь полон»).
            if (!item.IsWeapon && session.Inventory.Capacity(item) <= 0)
                return false;

            if (controller == null)
            {
                controller = GetComponent<PlayerController>();
                combat = GetComponent<PlayerCombat>();
                damage = GetComponent<PlayerDamageReceiver>();
            }

            if (controller == null || !controller.HasControl || !controller.Grounded || controller.Rolling)
                return false;
            if (rig == null)
                rig = FindAnyObjectByType<CameraRig>();

            itemId = item.Id;
            isWeapon = item.IsWeapon;
            itemColor = palette.Get(item.Color);
            model = pickup.BeginSequence(config.Item.RiseEnd);
            if (model != null)
            {
                modelStart = model.position;
                modelRotation = model.rotation;
                modelScale = model.localScale;
            }
            else
            {
                modelStart = pickup.transform.position;
            }

            var player = transform.position;
            var toItem = modelStart - player;
            toItem.y = 0f;
            var forward = toItem.sqrMagnitude > 0.01f ? toItem.normalized : controller.Forward;
            startYaw = YawOf(controller.Forward);
            faceYaw = YawOf(forward);
            var it = config.Item;
            hoverPoint = Vector3.Lerp(modelStart, new Vector3(player.x, modelStart.y, player.z), it.RiseApproach)
                         + Vector3.up * it.RiseHeight;

            // Камера смотрит персонажу в лицо (против его взгляда) чуть сбоку — с той стороны, что ближе к нынешней.
            baseYaw = rig != null ? rig.Yaw : startYaw;
            basePitch = rig != null ? rig.Pitch : 35f;
            baseDistance = rig != null ? rig.Distance : 20f;
            var front = faceYaw + 180f;
            var side = Mathf.DeltaAngle(front, baseYaw) >= 0f ? 1f : -1f;
            riseYaw = front + side * config.Camera.RiseYaw;
            closeYaw = front + side * config.Camera.CloseUpYaw;
            // Поворот к кадру — кратчайшим путём от обычного угла.
            riseYaw = baseYaw + Mathf.DeltaAngle(baseYaw, riseYaw);
            closeYaw = riseYaw + Mathf.DeltaAngle(riseYaw, closeYaw);

            time = 0f;
            spin = 0f;
            hit = false;
            burst = false;
            lastHop = 0f;
            active = true;
            Playing = true;

            if (combat != null)
                combat.CancelAttack();
            controller.InterruptIdle();
            controller.Frozen = true;
            if (controller.Deformer != null)
                controller.Deformer.UnscaledTime = true;
            if (combat != null)
                combat.UnscaledTime = true;
            GameTimeScale.SetCinematic(true);
            return true;
        }

        private static float YawOf(Vector3 direction) => Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        private void Update()
        {
            if (!active)
                return;
            // Реальное время, но без провалов: длинный кадр не перескакивает удар.
            var dt = Mathf.Min(UnityEngine.Time.unscaledDeltaTime, 1f / 20f);
            time += dt;
            UpdateItem(dt);
            UpdateFacing();
            UpdateCamera();
            UpdateEvents();
            if (time >= config.Duration)
                Finish();
        }

        // ------------------------------------------------------------------ предмет

        private void UpdateItem(float dt)
        {
            if (model == null)
                return;
            var it = config.Item;
            var t = time;
            var rise = config.Rise(t);
            // Вращение замедляется к замаху и стоит в полёте.
            spin += dt * it.Spin * (1f - PickupSequenceConfig.Window(t, it.ThrowStart, it.FlyStart));
            var bob = Mathf.Sin(t * it.BobSpeed) * it.Bob * rise;
            var hover = Vector3.Lerp(modelStart, hoverPoint, rise) + Vector3.up * bob;

            var hitPoint = HitPoint();
            var away = hover - hitPoint;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.up;
            var windup = config.Windup(t);
            hover += (away + Vector3.up * 0.5f) * (it.WindupDistance * windup);

            var flight = config.Flight(t);
            model.position = Vector3.Lerp(hover, hitPoint, flight);
            model.rotation = Quaternion.AngleAxis(spin, Vector3.up) * modelRotation;
            model.localScale = modelScale * Mathf.Lerp(1f, it.HitScale, flight);
        }

        private Vector3 HitPoint() => transform.position + Vector3.up * config.Item.HitHeight;

        // ------------------------------------------------------------------ персонаж

        private void UpdateFacing()
        {
            var p = config.Player;
            var turn = PickupSequenceConfig.Smooth(PickupSequenceConfig.Window(time, 0f, p.TurnTime));
            var yaw = Mathf.LerpAngle(startYaw, faceYaw, turn);
            // На радости — полуоборот к камере крупного плана.
            var toCamera = closeYaw + 180f;
            var face = PickupSequenceConfig.Smooth(PickupSequenceConfig.Window(time, p.CheerStart, p.CheerStart + 0.3f));
            yaw = Mathf.LerpAngle(yaw, toCamera, face * p.FaceCamera);
            controller.FaceNow(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
        }

        /// <summary>Форма куба поверх того, что пишет контроллер (тот в этот кадр уже отработал): взгляд, панч, радость.</summary>
        private void LateUpdate()
        {
            if (!active || controller == null)
                return;
            var deformer = controller.Deformer;
            var body = controller.Body;
            if (deformer == null || body == null)
                return;
            var p = config.Player;
            var t = time;

            // Взгляд за предметом: глаза по грани, при взгляде вверх куб чуть запрокидывается.
            var look = Vector2.zero;
            var lookUp = 0f;
            if (model != null)
            {
                var eye = transform.position + Vector3.up * 0.6f;
                var local = Quaternion.Inverse(body.rotation) * (model.position - eye);
                var flat = new Vector2(local.x, local.z).magnitude;
                var elevation = Mathf.Atan2(local.y, Mathf.Max(0.05f, flat)) * Mathf.Rad2Deg;
                var sideways = Mathf.Atan2(local.x, Mathf.Max(0.05f, local.z)) * Mathf.Rad2Deg;
                lookUp = Mathf.Clamp(elevation / 55f, -0.5f, 1f);
                look = new Vector2(Mathf.Clamp(sideways / 50f, -1f, 1f) * p.EyeSide, lookUp * p.EyeUp);
            }

            var cheer = config.Cheer(t);
            deformer.EyeOffset = Vector2.Lerp(look, new Vector2(0f, 0.03f), cheer);
            deformer.EyeBlink = p.HappyEyes * cheer;
            deformer.PitchOffset = -p.LookBack * Mathf.Max(0f, lookUp) * (t < config.Item.HitTime ? 1f : 0f);
            deformer.TwistOffset = Mathf.Sin(t * p.WiggleSpeed) * p.Wiggle * cheer;
            deformer.DirectBend = Vector2.zero;
            deformer.DirectSquash = 0f;
            deformer.LeanTarget = Vector2.zero;
            if (combat != null)
                combat.Cheer = isWeapon ? cheer : 0f;

            // Прыжки радости: присед, вытягивание в полёте, сплющивание при приземлении.
            var phase = config.HopPhase(t, out _);
            var hop = phase >= 0f ? config.HopHeight(phase) : 0f;
            if (phase >= 0f && phase < p.HopCrouch)
                deformer.SquashTarget = p.HopSquash * Mathf.Sin(phase / p.HopCrouch * Mathf.PI);
            else if (hop > 0f)
                deformer.SquashTarget = p.HopStretch * (phase < 0.6f ? 1f : 0.3f);
            else if (hit)
                deformer.SquashTarget = 0f;
            if (lastHop <= 0f && hop > 0f)
                deformer.Impulse(p.HopKick);
            else if (lastHop > 0f && hop <= 0f)
                deformer.SetSquash(p.HopSquash, -1.5f);
            lastHop = hop;
            body.localPosition += Vector3.up * (hop * p.HopHeight);

            if (damage != null)
            {
                var glow = config.Glow(t);
                damage.SetPickupGlow(itemColor, glow * config.Fx.GlowTint, glow * config.Fx.GlowEmission);
            }
        }

        // ------------------------------------------------------------------ удар, эффект

        private void UpdateEvents()
        {
            if (!hit && time >= config.Item.HitTime)
                Hit();
            if (!burst && time >= config.Fx.BurstTime)
            {
                burst = true;
                var tint = Color.Lerp(itemColor, Color.white, config.Fx.TintToWhite);
                ParticleFx.Play(ParticleFx.PickupBurst, transform.position, tint, config.Fx.BurstScale)?.Follow(transform);
            }
        }

        /// <summary>Предмет влетел в персонажа: панч, вспышка, толчок камеры; предмет в инвентарь, оружие — в руки.</summary>
        private void Hit()
        {
            hit = true;
            var p = config.Player;
            var point = model != null ? model.position : HitPoint();
            if (model != null)
            {
                Destroy(model.gameObject);
                model = null;
            }

            if (config.Fx.HitFlash > 0f)
                ParticleFx.Flash(point, config.Fx.HitFlash);
            var deformer = controller.Deformer;
            if (deformer != null)
            {
                deformer.SetSquash(p.PunchSquash, p.PunchImpulse);
                // Прогиб назад от удара спереди.
                deformer.Impulse(0f, new Vector2(0f, -p.PunchBend));
            }

            if (combat != null)
                combat.Shake(p.PunchShake);
            if (!session.PickUp(itemId))
            {
                Debug.LogWarning($"PickupSequence: '{itemId}' did not fit into the inventory");
                return;
            }

            if (isWeapon)
                session.Inventory.SetActive(itemId);
        }

        // ------------------------------------------------------------------ камера

        private void UpdateCamera()
        {
            if (rig == null)
                return;
            var c = config.Camera;
            config.CameraWeights(time, out var approach, out var closeUp, out var hold);
            var player = transform.position;
            var toItem = Quaternion.Euler(0f, faceYaw, 0f) * Vector3.forward;
            var riseLook = player + Vector3.up * c.RiseLookHeight + toItem * 0.35f;
            var closeLook = player + Vector3.up * c.CloseUpLookHeight;

            var shotYaw = Mathf.Lerp(riseYaw, closeYaw, closeUp);
            var shotPitch = Mathf.Lerp(c.RisePitch, c.CloseUpPitch, closeUp);
            var shotDistance = Mathf.Lerp(c.RiseDistance, c.CloseUpDistance, closeUp);
            var shotLook = Vector3.Lerp(riseLook, closeLook, closeUp);

            var w = approach * hold;
            // Расстояние — по логарифму: подъезд с 20 м до 3 м идёт равномерно на глаз, без рывка в конце.
            var distance = Mathf.Exp(Mathf.Lerp(Mathf.Log(Mathf.Max(0.1f, baseDistance)), Mathf.Log(shotDistance), w));
            rig.SetShot(Mathf.Lerp(baseYaw, shotYaw, w), Mathf.Lerp(basePitch, shotPitch, w), distance,
                Vector3.Lerp(rig.FollowPoint, shotLook, w));
        }

        // ------------------------------------------------------------------ конец

        private void Finish()
        {
            if (!active)
                return;
            if (!hit)
                Hit();
            active = false;
            Playing = false;
            if (controller != null)
            {
                controller.Frozen = false;
                if (controller.Deformer != null)
                {
                    controller.Deformer.UnscaledTime = false;
                    controller.Deformer.EyeBlink = 0f;
                }
            }

            if (combat != null)
            {
                combat.UnscaledTime = false;
                combat.Cheer = 0f;
            }

            if (damage != null)
                damage.SetPickupGlow(Color.black, 0f, 0f);
            if (rig != null)
                rig.ClearShot();
            GameTimeScale.SetCinematic(false);
        }

        private void OnDisable() => Finish();
    }
}
