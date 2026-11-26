using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Стрелковое оружие (дробовик). Выстрел — залп пуль-снарядов (<see cref="Pellet"/>, из пула): веер с разбросом
    /// угла и скорости, пуля попадает при касании и останавливается о стены. Параметры залпа — <see cref="ShotParams"/>:
    /// обычный выстрел — <see cref="ShotParams.From"/>, заряженный — <see cref="ShotParamsExtensions.Charged"/>.
    /// Основная атака (ЛКМ / RT) — выстрел. Доп. атака (ПКМ / LT) — блок (<see cref="RangedConfig.Block"/>): дробовик
    /// поднят перед собой и гасит часть урона за выносливость; блок, поставленный в окно парирования (как у щита),
    /// парирует атаку, и дробовик сразу стреляет (<see cref="GunParryShot"/>).
    /// <list type="bullet">
    /// <item>апгрейд 1 — дешевле выстрел и блок (<see cref="RangedUpgradesConfig.StaminaCostMultiplier"/>);</item>
    /// <item>апгрейд 2 — заряд удержанием (<see cref="RangedConfig.Charge"/>, общий <see cref="AttackCharge"/>):
    /// выстрел уходит при отпускании, полный заряд — залп <see cref="RangedConfig.ChargedShot"/>;</item>
    /// <item>апгрейд 3 — выстрел после парирования полностью заряжен (<see cref="GunBlockConfig.ChargedParryUpgrade"/>).</item>
    /// </list>
    /// </summary>
    public sealed partial class PlayerCombat
    {
        /// <summary>Дуло в координатах модели оружия (ствол вдоль +Z).</summary>
        private static readonly Vector3 MuzzleOffset = new Vector3(0f, 0.05f, 0.84f);

        private readonly Collider[] aimOverlap = new Collider[32];
        private readonly AttackCharge gunCharge = new AttackCharge();
        private HandState gunChargeHand;

        // Плавный подъём ствола на прицел при заряде (0 — поза покоя, 1 — на прицеле).
        private float gunRaise;

        // Плавный подъём оружия в блок (0 — нет, 1 — поднято перед собой).
        private float gunBlockRaise;

        /// <summary>Заряженных выстрелов и парирований блоком дробовика за игру (для автотестов).</summary>
        public int ChargedShotCount { get; private set; }
        public int GunParryCount { get; private set; }

        /// <summary>Рука со стрелковым оружием, которое блокирует на доп. атаке (null — нет такого).</summary>
        private HandState GunBlockHand
        {
            get
            {
                var right = hands[(int)Hand.Right];
                return right?.Item != null && right.Item.IsRanged && right.Item.Ranged.Block != null && TwoHanded(right.Item) ? right : null;
            }
        }

        /// <summary>Блок стрелковым оружием поднят.</summary>
        public bool GunBlocking => Blocking && GunBlockHand != null;

        /// <summary>Копится заряд выстрела.</summary>
        public bool GunCharging => gunChargeHand != null && gunCharge.Active;

        /// <summary>
        /// Стрелковое оружие: двуручное стреляет на основную атаку (доп. атака — блок, если он есть у оружия; без блока —
        /// тоже выстрел), одноручное — на кнопку своей руки. Во время блока не стреляет.
        /// Курок держит паузу между выстрелами и запоминает раннее нажатие. С зарядом (апгрейд) нажатие начинает
        /// копить заряд, выстрел — при отпускании.
        /// </summary>
        private void UpdateGun(bool control, float dt)
        {
            foreach (var hand in hands)
            {
                hand.ShotTime += dt;
                // Двуручное держит и стреляет правая рука, левая ничего не делает.
                if (hand.Item == null || !hand.Item.IsRanged || (hand.Hand == Hand.Left && TwoHanded(hands[(int)Hand.Right].Item)))
                {
                    hand.Gun.Reset();
                    if (gunChargeHand == hand)
                        CancelGunCharge();
                    continue;
                }

                bool pressed, held;
                if (TwoHanded(hand.Item) && hand.Item.Ranged.Block != null)
                {
                    pressed = input.Pressed(GameAction.Primary);
                    held = hand.Holding;
                }
                else if (TwoHanded(hand.Item))
                {
                    pressed = input.Pressed(GameAction.Primary) || input.Pressed(GameAction.Secondary);
                    held = hand.Holding || hands[(int)Hand.Left].Holding;
                }
                else
                {
                    pressed = input.Pressed(hand.Hand == Hand.Right ? GameAction.Primary : GameAction.Secondary);
                    held = hand.Holding;
                }

                var config = hand.Item.Ranged;
                if (gunChargeHand == hand)
                {
                    // Перекат, уворот, смена экрана — заряд сгорает без выстрела.
                    if (!control || attacking != null)
                        CancelGunCharge();
                    else
                        UpdateGunCharge(hand, config, held, dt);
                    continue;
                }

                var canShoot = control && attacking == null && !Blocking;
                if (!hand.Gun.Tick(dt, canShoot && pressed, config) || !canShoot)
                    continue;
                if (CanChargeShot(hand.Item))
                {
                    // Нажатие принято: дальше решает удержание (отпустил раньше заряда — обычный выстрел).
                    hand.Gun.Failed();
                    gunCharge.Begin(config.Charge);
                    gunChargeHand = hand;
                    continue;
                }

                TryFire(hand, ShotParams.From(config), false);
            }
        }

        private bool CanChargeShot(ItemConfig item)
        {
            var charge = item.Ranged.Charge;
            return charge != null && HasUpgrade(item, charge.Upgrade);
        }

        /// <summary>Выносливость за выстрел с учётом апгрейда 1.</summary>
        private float ShotStaminaCost(ItemConfig item) => item.Ranged.StaminaCost * GunCostMultiplier(item);

        /// <summary>Множитель выносливости за выстрел и блок стрелковым оружием (апгрейд 1).</summary>
        private float GunCostMultiplier(ItemConfig item) =>
            session.Upgrades.Has(item.Id, 1) ? item.Ranged.Upgrades.StaminaCostMultiplier : 1f;

        /// <summary>Заряд выстрела: ствол поднят на прицел, оружие разгорается; отпустил — выстрел.</summary>
        private void UpdateGunCharge(HandState hand, RangedConfig config, bool held, float dt)
        {
            var result = gunCharge.Tick(dt, held, session.Stamina.TrySpend);
            var flash = gunCharge.Config != null ? gunCharge.Config.Flash : 0f;
            var glow = flash > 0f ? WeaponGlow.For(hand.Visual, palette.Get(hand.Item.Color), combatConfig.HeldWeaponGlow) : null;
            switch (result)
            {
                case ChargeEvent.Charged:
                    glow?.Flash();
                    if (flash > 0f)
                        ChargeFlashFx(MuzzleWorld(hand, controller.Forward), hand.Item.Color, 0.5f * flash);
                    controller.Deformer.Impulse(-1.4f);
                    return;
                case ChargeEvent.Released:
                {
                    var full = gunCharge.Full;
                    var shot = ShotParams.From(config);
                    if (full)
                    {
                        shot = shot.Charged(config.ChargedShot, gunCharge.DamageMultiplier, gunCharge.KnockbackMultiplier);
                        if (gunCharge.Perfect && flash > 0f)
                            ChargeFlashFx(MuzzleWorld(hand, controller.Forward), hand.Item.Color, 0.4f * flash);
                    }
                    gunCharge.Reset();
                    gunChargeHand = null;
                    TryFire(hand, shot, full);
                    return;
                }
            }

            // Заряд копится: свечение растёт, куб мелко дрожит.
            var level = gunCharge.Level;
            glow?.Hold(gunCharge.Full ? 0.5f + 0.5f * gunCharge.Bonus : level * 0.6f);
            controller.Deformer.Impulse(Random.Range(-1f, 1f) * 0.3f * level);
        }

        private void CancelGunCharge()
        {
            gunCharge.Reset();
            gunChargeHand = null;
        }

        /// <summary>Списать выносливость и выстрелить залпом <paramref name="shot"/>.</summary>
        private void TryFire(HandState hand, ShotParams shot, bool charged)
        {
            if (!session.Stamina.TrySpend(ShotStaminaCost(hand.Item)))
            {
                hand.Gun.Failed();
                return;
            }

            hand.Gun.Fired(hand.Item.Ranged);
            FireShot(hand, shot, charged);
        }

        /// <summary>
        /// Выстрел: в <paramref name="target"/> (выстрел после парирования), иначе в цель фиксации (СКМ/R3), без неё —
        /// прицел схемы Б (правый стик/мышь), иначе — куда смотрит персонаж. Пули вылетают из дула веером и летят сами;
        /// попадания учитывает залп (<see cref="ShotVolley"/>).
        /// </summary>
        private void FireShot(HandState hand, ShotParams shot, bool charged, Vector3? target = null)
        {
            var weapon = hand.Item;
            var origin = transform.position + Vector3.up * playerConfig.ColliderHeight * 0.5f;
            var lockOn = controller.LockOn;
            var locked = target.HasValue || (lockOn != null && lockOn.Active);
            var targetPoint = target ?? (locked ? lockOn.TargetPoint : Vector3.zero);
            var dir = locked ? targetPoint - origin : controller.LookAim ?? controller.Forward;
            if (dir.sqrMagnitude < 0.0001f)
                dir = controller.Forward;
            var flat = new Vector3(dir.x, 0f, dir.z);
            flat = flat.sqrMagnitude > 0.0001f ? flat.normalized : controller.Forward;
            // Дуло — в позе прицела (ствол поднимается в момент выстрела), направление считается от него.
            var muzzle = MuzzleWorld(hand, flat);
            if (locked)
            {
                dir = targetPoint - muzzle;
                dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : flat;
            }
            else
            {
                // Без фиксации прицел только по горизонтали: наклоняем залп к ближайшей цели в веере,
                // чтобы попадать по врагам выше или ниже (уступ, склон).
                dir = PitchToTarget(muzzle, flat, shot.Range, shot.Spread);
            }

            controller.FaceNow(flat);
            hand.ShotTime = 0f;
            ShotCount++;
            if (charged)
                ChargedShotCount++;

            var color = palette.Get(weapon.Color);
            var glowColor = Color.Lerp(color, Color.white, 0.35f) * shot.Glow;
            glowColor.a = 1f;
            var mask = ~((1 << gameObject.layer) | (1 << Debris.Layer));
            var volley = new ShotVolley(this, weapon, shot, color, flat);

            var right = Vector3.Cross(Vector3.up, flat);
            foreach (var launch in ShotPattern.Launches(shot, () => Random.value))
            {
                var pelletDir = Quaternion.AngleAxis(launch.Yaw, Vector3.up) * Quaternion.AngleAxis(-launch.Pitch, right) * dir;
                Pellet.Launch(origin, muzzle, pelletDir, launch.Speed, shot.Range, shot.PelletSize, shot.HitRadius, glowColor, mask, volley);
            }

            // Отдача: персонаж отъезжает назад, куб сжимается и откидывается, камера толкается от выстрела.
            recoilVector = -flat * shot.Recoil;
            recoilDuration = shot.RecoilTime;
            recoilElapsed = 0f;
            recoilDone = 0f;
            var power = charged ? 1.6f : 1f;
            ParticleFx.Flash(muzzle, 0.3f * power);
            controller.Deformer.Impulse(-0.9f * power, new Vector2(0f, -2.2f * power));
            if (impulse != null && combatConfig.HitShake > 0f)
                impulse.GenerateImpulseWithVelocity((-flat + Vector3.down * 0.3f) * (combatConfig.HitShake * 1.2f * power));
        }

        /// <summary>Дуло в мире: поза прицела из конфига (<see cref="RangedConfig.AimHold"/>), без деформации куба.</summary>
        private Vector3 MuzzleWorld(HandState hand, Vector3 flat)
        {
            var hold = hand.Item.Ranged.AimHold;
            var right = Vector3.Cross(Vector3.up, flat);
            return transform.position + Vector3.up * (hold.Height + MuzzleOffset.y) + right * (hold.Side * hand.Side) +
                   flat * (hold.Forward + MuzzleOffset.z);
        }

        /// <summary>
        /// Направление залпа без фиксации: горизонталь <paramref name="flat"/>, наклонённая к ближайшей цели внутри веера
        /// (по горизонтали в пределах разброса, по высоте не дальше 3 м). Целей нет — горизонтально.
        /// </summary>
        private Vector3 PitchToTarget(Vector3 origin, Vector3 flat, float range, float spread)
        {
            var count = Physics.OverlapSphereNonAlloc(origin, range, aimOverlap, ~((1 << gameObject.layer) | (1 << Debris.Layer)),
                QueryTriggerInteraction.Collide);
            var best = float.MaxValue;
            var result = flat;
            for (int i = 0; i < count; i++)
            {
                var target = aimOverlap[i].GetComponentInParent<IHittable>();
                if (target == null || target is EnemyController { Dead: true })
                    continue;
                var to = target.HitPoint - origin;
                var horizontal = new Vector3(to.x, 0f, to.z);
                var distance = horizontal.magnitude;
                if (distance < 0.01f || distance > range || Mathf.Abs(to.y) > 3f)
                    continue;
                if (Vector3.Angle(flat, horizontal) > spread * 0.5f + 5f || distance >= best)
                    continue;
                best = distance;
                result = (flat * distance + Vector3.up * to.y).normalized;
            }

            return result;
        }

        /// <summary>
        /// Поза рук со стрелковым оружием (<see cref="RangedConfig.IdleHold"/> / <see cref="RangedConfig.AimHold"/> /
        /// <see cref="GunBlockConfig.Hold"/>): в покое — перед кубом стволом чуть вниз, после выстрела и при заряде —
        /// на прицеле, в блоке — поднято перед собой; выстрел отбрасывает ствол вверх-назад.
        /// </summary>
        private Pose GunPose(HandState hand)
        {
            var ranged = hand.Item.Ranged;
            var s = hand.Side;
            var charging = gunChargeHand == hand && gunCharge.Active;
            gunRaise = Mathf.MoveTowards(gunRaise, charging ? 1f : 0f, Time.deltaTime * 8f);
            var aim = Mathf.Max(gunRaise, 1f - Mathf.Clamp01((hand.ShotTime - 0.45f) / 0.3f));
            // Блок: оружие вскидывается быстро; выстрел после парирования на миг уводит его на прицел.
            var blocking = ranged.Block != null && GunBlocking && GunBlockHand == hand;
            gunBlockRaise = Mathf.MoveTowards(gunBlockRaise, blocking ? 1f : 0f, Time.deltaTime * 14f);
            var block = ranged.Block != null ? gunBlockRaise * (1f - aim) : 0f;
            var kick = Mathf.Exp(-hand.ShotTime * 14f);
            var pos = Vector3.Lerp(HoldPosition(ranged.IdleHold, s), HoldPosition(ranged.AimHold, s), aim)
                      + new Vector3(0f, 0.06f * kick, -0.1f * kick);
            var pitch = Mathf.Lerp(ranged.IdleHold.Pitch, ranged.AimHold.Pitch, aim) - 30f * kick;
            if (block > 0f)
            {
                pos = Vector3.Lerp(pos, HoldPosition(ranged.Block.Hold, s), block);
                pitch = Mathf.Lerp(pitch, ranged.Block.Hold.Pitch, block);
            }

            if (charging)
                pos += Random.insideUnitSphere * (0.008f * gunCharge.Level);
            hand.GunAim = aim;
            hand.GunBlock = block;
            return new Pose(pos, Quaternion.Euler(pitch, 0f, 0f));
        }

        /// <summary>
        /// Модель стрелкового оружия в руке: в покое — по <see cref="GunHoldConfig.ModelPosition"/>/<see cref="GunHoldConfig.ModelRotation"/>
        /// позы покоя (дробовик вдоль тела), на прицеле — по позе прицела; между ними — плавно по доле прицела.
        /// </summary>
        private void UpdateGunModel(HandState hand)
        {
            var ranged = hand.Item.Ranged;
            var grip = Quaternion.Euler(0f, hand.Item.GripRoll, 0f);
            var idlePos = ModelPosition(ranged.IdleHold);
            var aimPos = ModelPosition(ranged.AimHold);
            var idleRot = ModelRotation(ranged.IdleHold, grip);
            var aimRot = ModelRotation(ranged.AimHold, grip);
            var t = hand.Visual.transform;
            var pos = Vector3.Lerp(idlePos, aimPos, hand.GunAim);
            var rot = Quaternion.Slerp(idleRot, aimRot, hand.GunAim);
            if (ranged.Block != null && hand.GunBlock > 0f)
            {
                pos = Vector3.Lerp(pos, ModelPosition(ranged.Block.Hold), hand.GunBlock);
                rot = Quaternion.Slerp(rot, ModelRotation(ranged.Block.Hold, grip), hand.GunBlock);
            }

            t.localPosition = pos;
            t.localRotation = rot;
        }

        private static Vector3 ModelPosition(GunHoldConfig hold)
        {
            var p = hold.ModelPosition;
            return p != null && p.Length >= 3 ? new Vector3(p[0], p[1], p[2]) : Vector3.zero;
        }

        private static Quaternion ModelRotation(GunHoldConfig hold, Quaternion grip)
        {
            var r = hold.ModelRotation;
            return r != null && r.Length >= 3 ? Quaternion.Euler(r[0], r[1], r[2]) : grip;
        }

        private static Vector3 HoldPosition(GunHoldConfig hold, float side) => new Vector3(hold.Side * side, hold.Height, hold.Forward);

        /// <summary>
        /// Парирование блоком дробовика удалось (оружие врага встретило ствол): дробовик сразу стреляет в парированного
        /// врага, без паузы курка. С апгрейдом <see cref="GunBlockConfig.ChargedParryUpgrade"/> — полностью заряженным
        /// залпом (<see cref="RangedConfig.ChargedShot"/> с множителями заряда).
        /// </summary>
        private void GunParryShot(HandState hand, EnemyController enemy)
        {
            if (this == null || hand?.Item?.Ranged?.Block == null || hand.Visual == null)
                return;
            var item = hand.Item;
            var config = item.Ranged;
            var shot = ShotParams.From(config);
            var charged = HasUpgrade(item, config.Block.ChargedParryUpgrade);
            if (charged)
            {
                shot = shot.Charged(config.ChargedShot, config.Charge);
                ChargeFlashFx(MuzzleWorld(hand, controller.Forward), item.Color, 0.5f);
            }

            // Выстрел после парирования не срывается от нехватки выносливости.
            session.Stamina.TrySpend(config.Block.ParryShotStaminaCost * GunCostMultiplier(item));
            if (gunChargeHand == hand)
                CancelGunCharge();
            hand.Gun.Fired(config);
            GunParryCount++;
            FireShot(hand, shot, charged, enemy != null && !enemy.Dead ? enemy.HitPoint : (Vector3?)null);
        }

        /// <summary>
        /// Один залп: получает попадания пуль, наносит урон (<see cref="PelletVolley{T}"/>),
        /// когда исчезнет последняя пуля — одна цифра урона на цель.
        /// </summary>
        private sealed class ShotVolley : IPelletListener
        {
            private readonly PlayerCombat owner;
            private readonly ItemConfig weapon;
            private readonly ShotParams shot;
            private readonly Color color;
            private readonly Vector3 flat;
            private readonly PelletVolley<IHittable> hits;
            private readonly Dictionary<IHittable, Tally> tally = new Dictionary<IHittable, Tally>();
            private bool hitStopDone;

            private struct Tally
            {
                public float Damage;
                public ColorEffect Effect;
                public Vector3 Point;
            }

            public ShotVolley(PlayerCombat owner, ItemConfig weapon, ShotParams shot, Color color, Vector3 flat)
            {
                this.owner = owner;
                this.weapon = weapon;
                this.shot = shot;
                this.color = color;
                this.flat = flat;
                hits = new PelletVolley<IHittable>(shot.Pellets, 0, 1f);
            }

            public void OnPelletHit(IHittable target, Vector3 point, Vector3 direction)
            {
                if (owner == null)
                    return;
                var outcome = hits.Hit(target, false, false);
                owner.PelletHitCount++;
                tally.TryGetValue(target, out var t);
                t.Point = target.HitPoint;
                tally[target] = t;
                if (outcome.Pellets > 0)
                    Apply(target, outcome.Pellets, outcome.DamageMultiplier, direction);
            }

            public void OnPelletDone()
            {
                hits.PelletFinished();
                if (!hits.Done || owner == null)
                    return;
                // Одна цифра урона на цель за весь залп.
                foreach (var pair in tally)
                    if (pair.Value.Damage > 0f && HitGate.ShowsDamage(pair.Key))
                        owner.events.Publish(new DamageDealt(pair.Value.Point, pair.Value.Damage, pair.Value.Effect, color));
            }

            private void Apply(IHittable target, int pellets, float multiplier, Vector3 direction)
            {
                if (target is Object unityObject && unityObject == null)
                    return; // цель уже уничтожена
                direction.y = 0f;
                if (HitGate.Deflects(target, weapon.Id, direction.sqrMagnitude > 0.0001f ? direction.normalized : flat, color))
                    return;
                var result = owner.rules.Calculate(weapon, null, target.ColorId);
                var damage = result.Damage * shot.DamageMultiplier * multiplier * pellets;
                target.ReceiveHit(new HitInfo
                {
                    Damage = damage,
                    ColorBonus = result.ColorBonus,
                    WeaponColor = color,
                    Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : flat,
                    Point = target.HitPoint,
                    Knockback = shot.Knockback * pellets / Mathf.Max(1, shot.Pellets),
                    WeaponId = weapon.Id
                });
                tally.TryGetValue(target, out var t);
                t.Damage += damage;
                t.Effect = result.Effect;
                tally[target] = t;
                if (!hitStopDone && owner.combatConfig.HitStop > 0f)
                {
                    hitStopDone = true;
                    owner.StartHitStop(owner.combatConfig.HitStop * 0.6f);
                }
            }
        }
    }
}
