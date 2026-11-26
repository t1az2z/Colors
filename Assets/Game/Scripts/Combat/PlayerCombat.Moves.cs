using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Особые приёмы оружия поверх серии ударов (меч, дальше — другое оружие), всё из конфига предмета:
    /// <list type="bullet">
    /// <item>закрытые апгрейдом удары серии (<see cref="AttackStepConfig.Upgrade"/>) пропускаются;</item>
    /// <item>заряд удержанием кнопки (<see cref="AttackStepConfig.Charge"/>): удар стоит на конце замаха, пока кнопка зажата;</item>
    /// <item>отложенный добивающий (<see cref="ItemConfig.Finisher"/>): пауза после серии и ещё одно нажатие;</item>
    /// <item>удар в прыжке (<see cref="ItemConfig.Plunge"/>): падение вниз и удар по площади при приземлении;</item>
    /// <item>подскок в начале удара (<see cref="AttackStepConfig.Hop"/>) и окно эффекта (FxFrom/FxTo).</item>
    /// </list>
    /// </summary>
    public sealed partial class PlayerCombat
    {
        private readonly AttackCharge charge = new AttackCharge();
        private readonly ComboFinisher finisher = new ComboFinisher();
        private readonly PlungeAttack plunge = new PlungeAttack();

        // Текущий удар не из серии (добивающий, удар в прыжке): окно добивающего после него не открывается.
        private bool specialStep;
        private PlungeConfig plungeStepSource;
        private AttackStepConfig plungeStep;

        /// <summary>Идёт заряд удара (атака стоит на конце замаха).</summary>
        public bool Charging => attacking != null && charge.Active;

        /// <summary>Текущий удар заряжен полностью.</summary>
        public bool ChargedStrike => attacking != null && charge.Full;

        /// <summary>Идёт удар в прыжке (от зависания до конца возврата).</summary>
        public bool Plunging => attacking != null && plunge.Active;

        /// <summary>Счётчики для автотестов: добивающие, удары в прыжке (приземления), полные заряды.</summary>
        public int FinisherCount { get; private set; }
        public int PlungeCount { get; private set; }
        public int ChargeCount { get; private set; }
        public int PerfectChargeCount { get; private set; }

        private bool HasUpgrade(ItemConfig item, int tier)
        {
            return ComboSteps.Unlocked(tier, t => item != null && session.Upgrades.Has(item.Id, t));
        }

        /// <summary>Кнопка атаки текущей руки зажата (двуручное — любая из двух).</summary>
        private bool AttackHeld => attacking != null &&
                                   (attacking.Holding || (attacking.Item.IsTwoHanded && hands[(int)Hand.Left].Holding));

        /// <summary>Индекс удара серии с учётом апгрейдов: закрытые удары пропускаются, -1 — нечем бить.</summary>
        private int UnlockedComboIndex(ItemConfig item, int index)
        {
            return ComboSteps.NextUnlocked(item.Combo, index, t => session.Upgrades.Has(item.Id, t));
        }

        /// <summary>Начат любой удар: сброс заряда и удара в прыжке, подскок.</summary>
        private void OnStepStarted(AttackStepConfig next)
        {
            charge.Reset();
            plunge.Reset();
            specialStep = false;
            if (next.Hop > 0f)
                controller.Leap(next.Hop, controller.Forward * next.Lunge);
        }

        /// <summary>Удар с подскоком: выпад отдаётся полётом (<see cref="PlayerController.Leap"/>), а не рывком по кадрам.</summary>
        private bool LeapStep => step != null && step.Hop > 0f;

        /// <summary>Отмена удара (перекат, смена оружия, урон): заряд, добивающий и удар в прыжке сбрасываются.</summary>
        private void ResetMoves()
        {
            charge.Reset();
            plunge.Reset();
            finisher.Reset();
        }

        /// <summary>След клинка: внутри окна эффекта удара, не во время заряда.</summary>
        private bool TrailEmitting(HandState hand)
        {
            return attacking == hand && step != null && !charge.Active && step.FxActive(attackTime);
        }

        /// <summary>Урон с учётом заряда.</summary>
        private HitResult WithCharge(HitResult result)
        {
            return charge.Full ? new HitResult(result.Damage * charge.DamageMultiplier, result.ColorBonus) : result;
        }

        private float ChargeKnockback => charge.Full ? charge.KnockbackMultiplier : 1f;

        // ------------------------------------------------------------------ фазы удара

        /// <summary>
        /// Вызывается каждый кадр удара до обычного хода времени. true — время удара в этом кадре ведёт приём
        /// (заряд держит удар на конце замаха, удар в прыжке — до приземления), обычный ход пропускается.
        /// </summary>
        private bool UpdateMoves(float dt)
        {
            // Конец окна комбо (фиксации позы): отсюда считается пауза до добивающего.
            var holdEnd = StrikeEnd + step.Hold;
            if (!specialStep && attackTime < holdEnd && attackTime + dt >= holdEnd)
                finisher.ComboWindowClosed(Time.time - (attackTime + dt - holdEnd));

            if (plunge.Active)
                return UpdatePlunge(dt);
            if (charge.Active)
                return UpdateCharge(dt);

            // Удар дошёл до конца замаха, а кнопка всё ещё зажата — копим заряд.
            var strikeStart = step.Windup;
            var chargeConfig = attacking.Item.ChargeFor(step);
            if (chargeConfig != null && charge.Config == null && attackTime <= strikeStart && attackTime + dt >= strikeStart &&
                AttackHeld && HasUpgrade(attacking.Item, chargeConfig.Upgrade))
            {
                attackTime = strikeStart;
                charge.Begin(chargeConfig);
                return true;
            }

            return false;
        }

        private bool UpdateCharge(float dt)
        {
            var result = charge.Tick(dt, AttackHeld, session.Stamina.TrySpend);
            var flash = charge.Config != null ? charge.Config.Flash : 0f;
            var glow = flash > 0f ? WeaponGlow.For(attacking.Visual, palette.Get(attacking.Item.Color), combatConfig.HeldWeaponGlow) : null;
            switch (result)
            {
                case ChargeEvent.Charged:
                    ChargeCount++;
                    glow?.Flash();
                    if (flash > 0f)
                        ParryFlash.Spawn(BladeTip(attacking), 0.55f * flash);
                    controller.Deformer.Impulse(-1.6f);
                    return true;
                case ChargeEvent.Released:
                    // Заряженный удар срывается резче: короткий толчок куба вперёд; во вспышку — ещё одна вспышка.
                    if (charge.Full)
                    {
                        controller.Deformer.Impulse(charge.Perfect ? -1.1f : -0.8f, new Vector2(0f, charge.Perfect ? 2f : 1.5f));
                        if (charge.Perfect)
                        {
                            PerfectChargeCount++;
                            glow?.Flash();
                            if (flash > 0f)
                                ParryFlash.Spawn(BladeTip(attacking), 0.4f * flash);
                        }
                    }

                    return false;
            }

            // Заряд копится: оружие разгорается, куб мелко дрожит; передержанный заряд гаснет вместе с бонусом.
            var level = charge.Level;
            glow?.Hold(charge.Full ? 0.5f + 0.5f * charge.Bonus : level * 0.6f);
            controller.Deformer.Impulse(Random.Range(-1f, 1f) * 0.35f * level);
            return true;
        }

        /// <summary>Остриё оружия в мире (для вспышки заряда).</summary>
        private Vector3 BladeTip(HandState hand)
        {
            if (hand.Visual == null)
                return transform.position + Vector3.up * playerConfig.ColliderHeight * 0.5f + controller.Forward * 0.8f;
            var filter = hand.Visual.GetComponentInChildren<MeshFilter>();
            var length = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.max.y : 1f;
            var t = filter != null ? filter.transform : hand.Visual.transform;
            return t.TransformPoint(Vector3.up * length * 0.85f);
        }

        // ------------------------------------------------------------------ добивающий

        /// <summary>Нажатие атаки после паузы в серии: запустить добивающий удар оружия, если окно паузы открыто.</summary>
        private bool TryStartFinisher(HandState hand)
        {
            var item = hand.Item;
            var config = item?.Finisher;
            if (config == null || lastHand != hand || !HasUpgrade(item, config.Upgrade) || !finisher.Ready(config, Time.time))
                return false;
            if (!session.Stamina.TrySpend(config.Step.StaminaCost))
                return false;
            finisher.Reset();
            BeginStep(hand, config.Step, -1);
            specialStep = true;
            FinisherCount++;
            return true;
        }

        // ------------------------------------------------------------------ удар в прыжке

        /// <summary>Нажатие атаки в воздухе: удар в прыжке, если он есть у оружия и до земли достаточно высоко.</summary>
        private bool TryStartPlunge(HandState hand)
        {
            var item = hand.Item;
            var config = item?.Plunge;
            if (config == null || controller.Grounded || controller.InJumpWindup || !HasUpgrade(item, config.Upgrade))
                return false;
            if (!PlungeAttack.CanStart(config, controller.Grounded, HeightAboveGround()))
                return false;
            if (!session.Stamina.TrySpend(config.StaminaCost))
                return false;
            if (plungeStepSource != config)
            {
                plungeStep = config.ToStep();
                plungeStepSource = config;
            }

            BeginStep(hand, plungeStep, -1);
            specialStep = true;
            finisher.Reset();
            plunge.Start(config);
            // Полёт не прерывается: прыжок идёт своим ходом (см. MoveMultiplier), удар — при приземлении.
            return true;
        }

        private float HeightAboveGround()
        {
            var mask = ~((1 << gameObject.layer) | (1 << Debris.Layer));
            var origin = transform.position + Vector3.up * 0.05f;
            return Physics.Raycast(origin, Vector3.down, out var hit, 50f, mask, QueryTriggerInteraction.Ignore)
                ? hit.distance - 0.05f
                : float.MaxValue;
        }

        /// <summary>
        /// Время удара в прыжке: замах — меч поднимается над головой и так летит до земли;
        /// приземление — удар по площади и быстрый взмах вниз, дальше обычные фиксация и возврат.
        /// </summary>
        private bool UpdatePlunge(float dt)
        {
            if (plunge.Phase == PlungePhase.Landed)
                return false;
            var landed = plunge.Tick(dt, controller.Grounded);
            if (landed)
            {
                attackTime = step.Windup + step.Strike * 0.4f;
                PlungeImpact();
                return true;
            }

            attackTime = Mathf.Min(attackTime + dt, step.Windup);
            return true;
        }

        /// <summary>Приземление удара в прыжке: урон всем вокруг точки приземления и отбрасывание от неё.</summary>
        private void PlungeImpact()
        {
            PlungeCount++;
            var config = plunge.Config;
            var weapon = attacking.Item;
            var color = palette.Get(weapon.Color);
            var origin = transform.position;
            var center = origin + Vector3.up * playerConfig.ColliderHeight * 0.5f;
            var count = Physics.OverlapSphereNonAlloc(center, config.Radius + 0.5f, overlap, ~(1 << gameObject.layer),
                QueryTriggerInteraction.Collide);
            var hits = 0;
            for (int i = 0; i < count; i++)
            {
                var target = overlap[i].GetComponentInParent<IHittable>();
                if (target == null || hitThisSwing.Contains(target))
                    continue;
                var to = overlap[i].ClosestPoint(center) - center;
                // Только те, кто стоит рядом по высоте: удар идёт по земле.
                if (Mathf.Abs(to.y) > 1.2f)
                    continue;
                to.y = 0f;
                if (to.magnitude > config.Radius)
                    continue;

                hitThisSwing.Add(target);
                var away = target.HitPoint - origin;
                away.y = 0f;
                var direction = away.sqrMagnitude > 0.0001f ? away.normalized : controller.Forward;
                if (HitGate.Deflects(target, weapon.Id, direction, color))
                    continue;
                var result = rules.Calculate(weapon, step, target.ColorId);
                target.ReceiveHit(new HitInfo
                {
                    Damage = result.Damage,
                    ColorBonus = result.ColorBonus,
                    WeaponColor = color,
                    Direction = direction,
                    Point = target.HitPoint,
                    Knockback = config.Knockback,
                    WeaponId = weapon.Id
                });
                if (HitGate.ShowsDamage(target))
                    events.Publish(new DamageDealt(target.HitPoint, result.Damage, result.ColorBonus, color));
                hits++;
            }

            // Удар о землю: куб сплющивается, волна по земле, толчок камеры вниз, фриз-кадры при попадании.
            controller.Deformer.SetSquash(-0.45f, -3f);
            controller.Deformer.Impulse(-1.5f);
            ShockRingFx.Spawn(origin, config.Radius, color);
            ParryFlash.Spawn(origin + controller.Forward * 0.7f + Vector3.up * 0.2f, 0.7f);
            if (impulse != null && combatConfig.HitShake > 0f)
                impulse.GenerateImpulseWithVelocity((Vector3.down + controller.Forward * 0.3f) * (combatConfig.HitShake * 2f));
            if (hits > 0 && combatConfig.HitStop > 0f)
                StartHitStop(combatConfig.HitStop * Mathf.Max(1f, config.Knockback * 0.6f));
        }
    }
}
