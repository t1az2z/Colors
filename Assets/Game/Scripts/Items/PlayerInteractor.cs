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

            var best = config.InteractRadius * config.InteractRadius;
            var position = transform.position;
            foreach (var item in Interactables.All)
            {
                if (!item.Available)
                    continue;
                var d = item.InteractPoint - position;
                if (Mathf.Abs(d.y) > 1.2f)
                    continue;
                d.y = 0;
                if (d.sqrMagnitude < best)
                {
                    best = d.sqrMagnitude;
                    Target = item;
                }
            }

            if (Target == null)
                return;
            TargetUsable = Target.CanInteract(position);
            if (TargetUsable && controller != null && controller.HasControl && input.Pressed(GameAction.Interact))
                Target.Interact(position);
        }
    }
}
