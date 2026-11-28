using ColorsAction.Configs;
using GameCore.Config;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Ближайшее <see cref="IInteractable"/> в радиусе взаимодействия (предмет, дверь-срезка); F / X использует его.
    /// HUD показывает подсказку: с кнопкой, если можно, или текст <see cref="IInteractable.BlockedPrompt"/>.
    /// </summary>
    public sealed class PlayerInteractor : GameBehaviour
    {
        private GameInput input;
        private GameSession session;
        private PlayerConfig config;

        private PlayerController controller;

        public IInteractable Target { get; private set; }

        /// <summary>Можно ли воспользоваться <see cref="Target"/> с текущего места.</summary>
        public bool TargetUsable { get; private set; }

        protected override void OnConstruct(IResolver resolver)
        {
            input = resolver.Resolve<GameInput>();
            session = resolver.Resolve<GameSession>();
            config = resolver.Resolve<ConfigService>().Load<PlayerConfig>(ConfigKeys.Player);
        }

        private void Update()
        {
            if (config == null)
                return;
            Target = null;
            TargetUsable = false;
            if (!session.PlayerControl.Value)
                return;
            if (controller == null)
                controller = GetComponent<PlayerController>();

            // Ближайшее из тех, чем можно воспользоваться; если таких нет — ближайшее недоступное, ради подсказки
            // (например, дверь с обратной стороны не должна заслонять лежащий рядом предмет).
            var radius = config.InteractRadius * config.InteractRadius;
            var bestUsable = radius;
            var bestBlocked = radius;
            IInteractable usable = null, blocked = null;
            var position = transform.position;
            foreach (var item in Interactables.All)
            {
                if (!item.Available)
                    continue;
                var d = (item is IInteractableSpan span ? span.NearestPoint(position) : item.InteractPoint) - position;
                if (Mathf.Abs(d.y) > 1.2f)
                    continue;
                d.y = 0;
                var dist = d.sqrMagnitude;
                if (item.CanInteract(position))
                {
                    if (dist < bestUsable)
                    {
                        bestUsable = dist;
                        usable = item;
                    }
                }
                else if (dist < bestBlocked)
                {
                    bestBlocked = dist;
                    blocked = item;
                }
            }

            Target = usable ?? blocked;
            if (Target == null)
                return;
            TargetUsable = usable != null;
            if (TargetUsable && controller != null && controller.HasControl && input.Pressed(GameAction.Interact))
                Target.Interact(position);
        }
    }
}
