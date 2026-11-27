using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using GameCore.Config;
using GameCore.DI;
using GameCore.Events;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Урон по игроку: перекат уворачивает (i-frames), щит в блоке гасит удар спереди за выносливость,
    /// иначе — урон, отбрасывание, сжатие куба, толчок камеры, красная цифра и 2 секунды неуязвимости с миганием.
    /// При смерти куб раскалывается и через пару секунд игрок появляется у последнего костра (или на старте),
    /// публикуется <see cref="PlayerRespawned"/>. Упав ниже <see cref="PlayerController.KillHeight"/>, игрок погибает сразу.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerDamageReceiver : GameBehaviour
    {
        private GameSession session;
        private PlayerConfig config;
        private IEventBus events;
        private PlayerController controller;
        private PlayerCombat combat;
        private Renderer[] renderers;
        private float respawnTimer = -1f;
        private GameInput input;
        private float quickCooldown;

        public bool Dead { get; private set; }

        /// <summary>Сколько раз игрок вылечился лечилкой (для автотестов).</summary>
        public int HealCount { get; private set; }

        protected override void OnConstruct(IResolver resolver)
        {
            session = resolver.Resolve<GameSession>();
            config = resolver.Resolve<ConfigService>().Load<PlayerConfig>(ConfigKeys.Player);
            events = resolver.Resolve<IEventBus>();
            input = resolver.Resolve<GameInput>();
        }

        /// <summary>
        /// Быстрые слоты 1–4 (геймпад — крестовина): лечилка восстанавливает долю здоровья и тратит заряд;
        /// после использования — пауза <see cref="HealConfig.Cooldown"/>.
        /// </summary>
        private void UpdateQuickItems()
        {
            if (quickCooldown > 0f)
                quickCooldown -= Time.deltaTime;
            if (input?.QuickItems == null || !session.PlayerControl.Value || !controller.HasControl || quickCooldown > 0f)
                return;
            for (int i = 0; i < input.QuickItems.Length; i++)
            {
                if (input.QuickItems[i] == null || !input.QuickItems[i].WasPressedThisFrame())
                    continue;
                var item = session.Inventory.QuickCount > i ? session.Inventory.Get(SlotRef.Quick(i)) : null;
                if (session.UseQuickItem(i) != QuickUseResult.Healed)
                    return;
                HealCount++;
                quickCooldown = item?.Config.Heal?.Cooldown ?? 0.5f;
                // Лечение: куб вытягивается вверх и пружинит, вспышка у центра.
                controller.Deformer.SetSquash(0.2f, 2.5f);
                controller.Deformer.Impulse(0.6f);
                ParticleFx.Flash(transform.position + Vector3.up * (config.ColliderHeight * 0.5f), 0.4f);
                return;
            }
        }

        private void Start()
        {
            controller = GetComponent<PlayerController>();
            combat = GetComponent<PlayerCombat>();
        }

        /// <summary>Удар врага. true — урон прошёл.</summary>
        public bool ReceiveEnemyHit(float damage, Vector3 from, float knockback)
        {
            if (Dead || controller == null)
                return false;
            if (controller.Invulnerable)
                return false; // увернулся перекатом

            var away = transform.position - from;
            away.y = 0;
            away = away.sqrMagnitude > 0.001f ? away.normalized : -controller.Forward;

            // Блок: удар спереди гасится за выносливость (щит — целиком, дробовик — часть); не хватило — блок пробит.
            if (combat != null && combat.Blocking && BlockCovers(-away))
            {
                // Апгрейд 1: блок дешевле.
                var block = BlockRules.Resolve(damage, combat.BlockAbsorb, session.Stamina.Current.Value,
                    config.Stamina.BlockStartCost, combat.BlockCostMultiplier);
                session.Stamina.TrySpend(block.StaminaCost);
                damage = block.Damage;
                if (block.Held)
                {
                    controller.ApplyKnockback(away * 0.25f * knockback);
                    controller.Deformer.SetSquash(-0.18f, -1f);
                    combat.Shake(0.15f);
                    if (damage <= 0f)
                        return false;
                }
            }

            if (!session.Vitals.TryHit(damage, out var dealt))
                return false;

            controller.ApplyKnockback(away * config.HitKnockback * knockback);
            controller.Deformer.SetSquash(-0.3f, -2f);
            var local = Quaternion.Inverse(controller.Body.rotation) * away;
            controller.Deformer.Impulse(-0.5f, new Vector2(local.x, local.z) * 4f);
            combat?.Shake(0.45f);
            events.Publish(new DamageDealt(transform.position + Vector3.up * 1.1f, dealt, ColorsAction.Combat.ColorEffect.Neutral, Color.red, true));

            if (session.Vitals.Health.IsDead)
                Die();
            return true;
        }

        /// <summary>
        /// Урон от падения с высоты <paramref name="height"/> по ступеням <see cref="FallDamageConfig"/>:
        /// ни перекат, ни щит, ни неуязвимость после удара его не гасят. true — урон прошёл.
        /// </summary>
        public bool ReceiveFallDamage(float height)
        {
            if (Dead || controller == null)
                return false;
            var fraction = config.FallDamage.DamageFraction(height);
            if (fraction <= 0f)
                return false;

            var health = session.Vitals.Health;
            var damage = fraction >= 1f ? health.Current.Value : health.Max * fraction;
            if (session.Vitals.TakeFallDamage(damage) <= 0f)
                return false;

            controller.Deformer.SetSquash(-0.4f, -3f);
            combat?.Shake(0.35f + 0.3f * fraction);
            events.Publish(new DamageDealt(transform.position + Vector3.up * 1.1f, damage, ColorsAction.Combat.ColorEffect.Neutral, Color.red, true));

            if (health.IsDead)
                Die();
            return true;
        }

        private bool BlockCovers(Vector3 towardAttacker)
        {
            // Щит может быть второй рукой меча и не лежать в инвентаре: угол блока знает бой.
            var angle = combat != null ? combat.BlockAngle : null;
            if (angle == null)
                return false;
            return Vector3.Angle(controller.Forward, towardAttacker) <= angle.Value * 0.5f;
        }

        private void Die()
        {
            Dead = true;
            controller.Frozen = true;
            // Начатый удар не доигрывается: иначе расколотый куб продолжал бы наносить урон.
            if (combat != null)
                combat.CancelAttack();
            SetVisible(false);
            Debris.Burst(transform.position + Vector3.up * 0.5f, new Color(0.62f, 0.63f, 0.65f), 16, 0.24f, 5f, config.RespawnDelay);
            respawnTimer = config.RespawnDelay;
        }

        private void Update()
        {
            if (session == null)
                return;
            if (respawnTimer >= 0f)
            {
                respawnTimer -= Time.deltaTime;
                if (respawnTimer < 0f)
                {
                    controller.Teleport(controller.SpawnPoint ?? Vector3.zero, controller.SpawnYaw);
                    session.Vitals.Revive();
                    session.Stamina.Restore();
                    session.RefillConsumables();
                    controller.Frozen = false;
                    Dead = false;
                    events.Publish(new PlayerRespawned());
                }

                return;
            }

            if (!Dead && controller != null && transform.position.y < controller.KillHeight)
            {
                session.Vitals.Kill();
                Die();
            }

            if (!Dead && controller != null)
                UpdateQuickItems();

            SetVisible(!Dead);
            // Мигание во время неуязвимости после урона: тело пульсирует белым свечением.
            var invulnerable = session.Vitals.Invulnerable && !Dead;
            ApplyBlink(invulnerable ? config.Health.BlinkAmount(Time.time) : 0f);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private Renderer bodyRenderer;
        private MaterialPropertyBlock blinkBlock;
        private Color bodyColor;
        private Color bodyEmission;
        private float blinkApplied;

        /// <summary>
        /// Белое свечение тела (подматериал 0, глаза не трогаем) и оружия в руках: цвет тянется к белому,
        /// emission даёт bloom. Материалам нужен _EMISSION — берём светящиеся копии (<see cref="GlowMaterials"/>).
        /// </summary>
        private void ApplyBlink(float amount)
        {
            var glow = config.Health.BlinkGlow;
            if (amount > 0f || blinkApplied > 0f)
                foreach (var visual in controller.Body.GetComponentsInChildren<ItemVisual>(true))
                    visual.SetBlink(amount, glow);

            if (bodyRenderer == null)
            {
                bodyRenderer = controller.Deformer != null ? controller.Deformer.GetComponent<Renderer>() : null;
                if (bodyRenderer == null)
                    return;
                var material = bodyRenderer.sharedMaterial;
                bodyColor = material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.gray;
                // Своё свечение материала (если включено) — вспышка добавляется к нему, а не затирает.
                bodyEmission = material != null && material.IsKeywordEnabled("_EMISSION") && material.HasProperty(EmissionColorId)
                    ? material.GetColor(EmissionColorId)
                    : Color.black;
                GlowMaterials.EnsureEmission(bodyRenderer);
                blinkBlock = new MaterialPropertyBlock();
            }

            if (Mathf.Approximately(amount, blinkApplied))
                return;
            blinkApplied = amount;
            bodyRenderer.GetPropertyBlock(blinkBlock, 0);
            blinkBlock.SetColor(BaseColorId, Color.Lerp(bodyColor, Color.white, amount));
            blinkBlock.SetColor(EmissionColorId, bodyEmission + Color.white * (amount * glow));
            bodyRenderer.SetPropertyBlock(blinkBlock, 0);
        }

        private bool shown = true;

        private void SetVisible(bool visible)
        {
            if (visible == shown && renderers != null)
                return;
            shown = visible;
            renderers = controller.Body.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
                r.enabled = visible;
        }
    }
}
