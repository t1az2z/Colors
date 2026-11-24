using ColorsAction.Configs;
using GameCore.Config;
using GameCore.DI;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>Ближайший предмет в радиусе взаимодействия; F / X подбирает его. HUD показывает подсказку.</summary>
    public sealed class PlayerInteractor : GameBehaviour
    {
        private GameInput input;
        private GameSession session;
        private PlayerConfig config;

        private PlayerController controller;

        public Pickup Target { get; private set; }

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
            if (!session.PlayerControl.Value)
                return;
            if (controller == null)
                controller = GetComponent<PlayerController>();

            var best = config.InteractRadius * config.InteractRadius;
            var position = transform.position;
            foreach (var pickup in Pickup.All)
            {
                if (!pickup.Available)
                    continue;
                var d = pickup.transform.position - position;
                if (Mathf.Abs(d.y) > 1.2f)
                    continue;
                d.y = 0;
                if (d.sqrMagnitude < best)
                {
                    best = d.sqrMagnitude;
                    Target = pickup;
                }
            }

            if (Target != null && controller != null && controller.HasControl && input.Pressed(GameAction.Interact))
                Target.TryTake();
        }
    }
}
