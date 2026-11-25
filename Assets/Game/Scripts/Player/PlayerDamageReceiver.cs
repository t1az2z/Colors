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
    /// При смерти куб раскалывается и через пару секунд игрок появляется на старте.
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

        public bool Dead { get; private set; }

        protected override void OnConstruct(IResolver resolver)
        {
            session = resolver.Resolve<GameSession>();
            config = resolver.Resolve<ConfigService>().Load<PlayerConfig>(ConfigKeys.Player);
            events = resolver.Resolve<IEventBus>();
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
                    controller.Frozen = false;
                    Dead = false;
                }

                return;
            }

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
