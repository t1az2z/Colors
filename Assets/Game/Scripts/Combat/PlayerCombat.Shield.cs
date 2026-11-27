using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Щит: блок, парирование и апгрейды (<see cref="ShieldConfig"/>; ступени — BlockCostUpgrade / ParryWidenUpgrade /
    /// BashUpgrade, 0 — нет). Щит меча — его вторая рука (<see cref="ItemConfig.Offhand"/>), ступени берутся у меча:
    /// <list type="bullet">
    /// <item>блок дешевле по выносливости (<see cref="BlockCostMultiplier"/>);</item>
    /// <item>окно парирования шире поровну в обе стороны (<see cref="ShieldConfig.ParryWindowBonus"/>);</item>
    /// <item>удар щитом (у меча — ступень 2): отпустить блок и сразу нажать снова; попал в окно парирования —
    /// атака парирована, урон и отбрасывание умножены (<see cref="ShieldConfig.BashParryMultiplier"/>).</item>
    /// </list>
    /// Парирование подаётся так: враг ещё начинает удар и встречает щит на полпути (<see cref="EnemyController.TryParry"/>);
    /// фриз-кадры, толчок камеры и <see cref="ParrySucceeded"/> — в момент столкновения (<see cref="OnParryContact"/>).
    /// </summary>
    public sealed partial class PlayerCombat
    {
        private readonly ShieldBashTrigger bashTrigger = new ShieldBashTrigger();
        private readonly HashSet<IHittable> bashHits = new HashSet<IHittable>();
        private HandState bashHand;
        private float bashSpent;
        private float bashTime;
        private bool bashHitDone;
        private float bashLungeDone;

        /// <summary>Сколько атак парировано за игру (щит, удар щитом, блок дробовика; для автотестов).</summary>
        public int ParryCount { get; private set; }

        /// <summary>Ударов щитом и попаданий ими за игру (для автотестов).</summary>
        public int BashCount { get; private set; }
        public int BashHitCount { get; private set; }

        /// <summary>Идёт удар щитом.</summary>
        public bool Bashing => bashHand != null;

        /// <summary>Рука со щитом (null — щита нет).</summary>
        private HandState ShieldHand
        {
            get
            {
                var left = hands[(int)Hand.Left];
                var right = hands[(int)Hand.Right];
                return left?.Item?.Kind == ItemKind.Shield ? left : right?.Item?.Kind == ItemKind.Shield ? right : null;
            }
        }

        /// <summary>Угол блока: щит в руках или стрелковое оружие с блоком (null — блокировать нечем).</summary>
        public float? BlockAngle => ShieldHand?.Item.BlockAngle ?? GunBlockHand?.Item.Ranged.Block.Angle;

        /// <summary>Доля урона, которую гасит блок: щит — весь удар, стрелковое оружие — <see cref="GunBlockConfig.Absorb"/>.</summary>
        public float BlockAbsorb => ShieldHand == null && GunBlockHand != null ? GunBlockHand.Item.Ranged.Block.Absorb : 1f;

        /// <summary>Открыта ли ступень прокачки щита (0 — никогда); ступень — у владельца щита (меча).</summary>
        private bool HasShieldUpgrade(HandState hand, int tier)
        {
            return hand?.Item?.Shield != null && tier > 0 && !string.IsNullOrEmpty(hand.UpgradeOwner) &&
                   session.Upgrades.Has(hand.UpgradeOwner, tier);
        }

        /// <summary>Множитель выносливости на блок: подъём и принятый удар (апгрейд щита или стрелкового оружия).</summary>
        public float BlockCostMultiplier
        {
            get
            {
                var hand = ShieldHand;
                if (hand == null && GunBlockHand != null)
                    return GunCostMultiplier(GunBlockHand.Item);
                return HasShieldUpgrade(hand, hand?.Item?.Shield?.BlockCostUpgrade ?? 0) ? hand.Item.Shield.BlockCostMultiplier : 1f;
            }
        }

        /// <summary>Расширение окна парирования щитом в руках сейчас (0 — без апгрейда или без щита).</summary>
        public float ParryWidenNow => ParryWiden(ShieldHand);

        /// <summary>Расширение окна парирования щитом (доля длины окна, апгрейд щита).</summary>
        private float ParryWiden(HandState hand)
        {
            return HasShieldUpgrade(hand, hand?.Item?.Shield?.ParryWidenUpgrade ?? 0) ? hand.Item.Shield.ParryWindowBonus : 0f;
        }

        // ------------------------------------------------------------------ парирование

        /// <summary>
        /// Блок поставлен: враги в окне парирования (с учётом апгрейда щита) перед блоком — парированы.
        /// Блок стрелковым оружием в момент столкновения сразу стреляет (<see cref="GunParryShot"/>, один выстрел на блок).
        /// Удачное парирование выносливость не тратит: цена подъёма блока <paramref name="spent"/> возвращается.
        /// </summary>
        private void TryParry(float spent)
        {
            var shieldHand = ShieldHand;
            var gunHand = shieldHand == null ? GunBlockHand : null;
            var angle = BlockAngle;
            if (angle == null)
                return;
            var widen = ParryWiden(shieldHand);
            var count = 0;
            var shotDone = false;
            foreach (var enemy in EnemyController.All)
            {
                if (enemy == null || enemy.Dead || !enemy.Brain.IsInParryWindow(widen))
                    continue;
                var to = enemy.transform.position - transform.position;
                to.y = 0f;
                var reach = (enemy.Brain.Attack?.Range ?? 2f) + 0.8f;
                if (to.magnitude > reach || Vector3.Angle(controller.Forward, to) > angle.Value * 0.5f)
                    continue;
                var parried = enemy;
                System.Action<Vector3> contact = OnParryContact;
                if (gunHand != null)
                    contact = point =>
                    {
                        OnParryContact(point);
                        if (shotDone)
                            return;
                        shotDone = true;
                        GunParryShot(gunHand, parried);
                    };
                if (enemy.TryParry(transform.position, widen, contact))
                    count++;
            }

            if (count == 0)
                return;
            ParryCount += count;
            session.Stamina.Refund(spent);
            // Щит рывком навстречу удару; отдача — в момент столкновения.
            controller.Deformer.Impulse(-0.3f, new Vector2(0f, 1.2f));
        }

        /// <summary>
        /// Оружие парированного врага встретило щит (выстрел, удар щитом): отдача в куб, фриз-кадры, толчок камеры,
        /// событие для звука и VFX.
        /// </summary>
        private void OnParryContact(Vector3 point)
        {
            if (this == null || controller == null)
                return;
            controller.Deformer.SetSquash(-0.2f, -1.2f);
            controller.Deformer.Impulse(-0.5f, new Vector2(0f, -1.8f));
            Shake(combatConfig.ParryShake);
            if (combatConfig.ParryHitStop > 0f)
                StartHitStop(combatConfig.ParryHitStop);
            events.Publish(new ParrySucceeded(point, 1));
        }

        // ------------------------------------------------------------------ удар щитом

        /// <summary>Ход удара щитом и его запуск: блок отпущен и сразу нажат снова (апгрейд BashUpgrade).</summary>
        private void UpdateShieldBash(bool control, float dt)
        {
            if (bashHand != null)
            {
                if (!control || bashHand.Item?.Shield == null)
                    bashHand = null;
                else
                    TickBash(dt);
            }

            var hand = ShieldHand;
            if (hand == null)
            {
                bashTrigger.Reset();
                return;
            }

            var config = hand.Item.Shield;
            if (config == null || !bashTrigger.Tick(dt, hand.Holding, config.BashMaxGap))
                return;
            if (bashHand != null || attacking != null || !control || !HasShieldUpgrade(hand, config.BashUpgrade))
                return;
            var before = session.Stamina.Current.Value;
            if (!session.Stamina.TrySpend(config.BashStaminaCost))
                return;
            bashSpent = before - session.Stamina.Current.Value;

            bashHand = hand;
            bashTime = 0f;
            bashHitDone = false;
            bashLungeDone = 0f;
            bashHits.Clear();
            BashCount++;
            // Замах: куб подбирается перед толчком.
            controller.Deformer.SetSquash(-0.15f, -1f);
        }

        private void TickBash(float dt)
        {
            var config = bashHand.Item.Shield;
            var previous = bashTime;
            bashTime += dt;
            if (previous < config.BashWindup && bashTime >= config.BashWindup)
                controller.Deformer.Impulse(-0.8f, new Vector2(0f, 3f)); // толчок вперёд
            if (!bashHitDone && bashTime >= config.BashWindup + config.BashStrike * 0.6f)
            {
                bashHitDone = true;
                BashImpact(bashHand);
            }

            if (bashTime >= config.BashDuration)
                bashHand = null;
        }

        /// <summary>Удар щитом: урон и отбрасывание в секторе перед игроком; в окне парирования — парирование и ×множитель.</summary>
        private void BashImpact(HandState hand)
        {
            var shield = hand.Item;
            var config = shield.Shield;
            var widen = ParryWiden(hand);
            var center = transform.position + Vector3.up * playerConfig.ColliderHeight * 0.5f;
            var forward = controller.Forward;
            var color = palette.Get(shield.Color);
            var count = Physics.OverlapSphereNonAlloc(center, config.BashRange + 0.5f, overlap, ~(1 << gameObject.layer),
                QueryTriggerInteraction.Collide);
            var hits = 0;
            var strongest = 1f;
            for (int i = 0; i < count; i++)
            {
                var target = overlap[i].GetComponentInParent<IHittable>();
                if (target == null || bashHits.Contains(target) || target is EnemyController { Dead: true })
                    continue;
                var to = overlap[i].ClosestPoint(center) - center;
                to.y = 0f;
                if (to.magnitude > config.BashRange)
                    continue;
                var toCenter = overlap[i].bounds.center - center;
                toCenter.y = 0f;
                if (to.magnitude > 0.6f && toCenter.sqrMagnitude > 0.0001f && Vector3.Angle(forward, toCenter) > config.BashArc * 0.5f)
                    continue;

                bashHits.Add(target);
                if (HitGate.Deflects(target, shield.Id, toCenter.sqrMagnitude > 0.0001f ? toCenter.normalized : forward, color))
                    continue;
                var multiplier = 1f;
                if (target is EnemyController enemy && enemy.Brain.IsInParryWindow(widen))
                {
                    // Удар щитом навстречу вражескому удару: атака парирована, урон и отбрасывание сильнее.
                    multiplier = config.BashParryMultiplier;
                    if (enemy.TryParry(transform.position, widen, OnParryContact))
                    {
                        ParryCount++;
                        // Удачное парирование выносливость не тратит.
                        session.Stamina.Refund(bashSpent);
                        bashSpent = 0f;
                    }
                }

                var result = StatusDamage.Scale(session.PlayerStatus, target,
                    rules.Apply(config.BashDamage * multiplier, shield.Color, target.ColorId));
                var damage = result.Damage;
                var direction = toCenter.sqrMagnitude > 0.0001f ? toCenter.normalized : forward;
                target.ReceiveHit(new HitInfo
                {
                    Damage = damage,
                    ColorBonus = result.ColorBonus,
                    WeaponColor = color,
                    Direction = direction,
                    Point = target.HitPoint,
                    Knockback = config.BashKnockback * multiplier,
                    WeaponId = shield.Id,
                    Poise = Poise.HitAmount(combatConfig.Poise, shield, config.BashPoise)
                });
                if (HitGate.ShowsDamage(target))
                    events.Publish(new DamageDealt(target.HitPoint, damage, result.Effect, color));
                hits++;
                strongest = Mathf.Max(strongest, multiplier);
            }

            if (hits == 0)
                return;
            BashHitCount += hits;
            controller.Deformer.Impulse(-1.2f * strongest);
            if (impulse != null && combatConfig.HitShake > 0f)
                impulse.GenerateImpulseWithVelocity((forward + Vector3.down * 0.4f) * (combatConfig.HitShake * strongest));
            if (combatConfig.HitStop > 0f)
                StartHitStop(combatConfig.HitStop * strongest);
        }

        /// <summary>Рывок вперёд удара щитом за кадр (добавляется к выпаду в <see cref="ConsumeLunge"/>).</summary>
        private Vector3 ConsumeBashLunge(Vector3 forward)
        {
            if (bashHand == null || bashHand.Item?.Shield == null)
                return Vector3.zero;
            var config = bashHand.Item.Shield;
            // Рывок — только в фазе удара, на замахе щит подтягивается на месте.
            var t = Mathf.Clamp01((bashTime - config.BashWindup) / Mathf.Max(0.01f, config.BashStrike));
            var target = config.BashLunge * EaseOut(t);
            var delta = target - bashLungeDone;
            bashLungeDone = target;
            return forward * delta;
        }

        /// <summary>Поза щита при ударе: из блока подтянуть к себе, резко толкнуть вперёд, вернуть в блок.</summary>
        private Pose BashPose(HandState hand)
        {
            var config = hand.Item.Shield;
            var s = hand.Side;
            var block = BlockPose(hand).position;
            var back = new Vector3(0.16f * s, 0.54f, 0.6f);
            var hit = new Vector3(0.06f * s, 0.6f, 1.05f);
            var strikeEnd = config.BashWindup + config.BashStrike;
            Vector3 pos;
            if (bashTime < config.BashWindup)
                pos = Vector3.Lerp(block, back, EaseOut(bashTime / Mathf.Max(0.001f, config.BashWindup)));
            else if (bashTime < strikeEnd)
                pos = Vector3.Lerp(back, hit, EaseOutCubic((bashTime - config.BashWindup) / Mathf.Max(0.001f, config.BashStrike)));
            else
                pos = Vector3.Lerp(hit, block, EaseInOut(Mathf.Clamp01((bashTime - strikeEnd) / Mathf.Max(0.001f, config.BashRecovery))));
            return new Pose(pos, Quaternion.identity);
        }
    }
}
