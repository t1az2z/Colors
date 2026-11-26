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
    /// публикуется <see cref="PlayerRespawned"/>.
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
                ParryFlash.Spawn(transform.position + Vector3.up * (config.ColliderHeight * 0.5f), 0.4f);
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

            // Блок щитом: удар спереди гасится за выносливость; не хватило — блок пробит.
            if (combat != null && combat.Blocking && BlockCovers(-away))
            {
                // Апгрейд 1 щита: блок дешевле.
                var cost = (config.Stamina.BlockStartCost + damage * 1.2f) * combat.BlockCostMultiplier;
                var hadStamina = session.Stamina.Current.Value >= cost;
                session.Stamina.TrySpend(cost);
                if (hadStamina)
                {
                    controller.ApplyKnockback(away * 0.25f * knockback);
                    controller.Deformer.SetSquash(-0.18f, -1f);
                    combat.Shake(0.15f);
                    return false;
                }

                damage *= 0.5f; // пробитый блок всё же смягчает
            }

            if (!session.Vitals.TryHit(damage))
                return false;

            controller.ApplyKnockback(away * config.HitKnockback * knockback);
            controller.Deformer.SetSquash(-0.3f, -2f);
            var local = Quaternion.Inverse(controller.Body.rotation) * away;
            controller.Deformer.Impulse(-0.5f, new Vector2(local.x, local.z) * 4f);
            combat?.Shake(0.45f);
            events.Publish(new DamageDealt(transform.position + Vector3.up * 1.1f, damage, false, Color.red, true));

            if (session.Vitals.Health.IsDead)
                Die();
            return true;
        }

        private bool BlockCovers(Vector3 towardAttacker)
        {
            var hand = session.Inventory.Left?.Config.Kind == ItemKind.Shield ? session.Inventory.Left
                : session.Inventory.Right?.Config.Kind == ItemKind.Shield ? session.Inventory.Right : null;
            if (hand == null)
                return false;
            return Vector3.Angle(controller.Forward, towardAttacker) <= hand.Config.BlockAngle * 0.5f;
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

            if (!Dead && controller != null)
                UpdateQuickItems();

            // Мигание во время неуязвимости после урона.
            var invulnerable = session.Vitals.Invulnerable && !Dead;
            var visible = !invulnerable || Mathf.Repeat(Time.time * config.BlinkRate, 1f) < 0.55f;
            SetVisible(visible);
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
