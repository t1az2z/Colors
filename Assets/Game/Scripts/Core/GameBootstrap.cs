using ColorsAction.States;
using ColorsAction.UI;
using GameCore.DI;
using GameCore.Hosting;
using GameCore.Newtonsoft;
using GameCore.UI;
using GameCore.Unity;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Точка входа сцены. Собирает <see cref="GameHost"/> шаблона: логгер Unity, конфиги из StreamingAssets/Configs,
    /// логика игры (<see cref="ColorsActionInstaller"/>) и сервисы движка (ввод, палитра).
    /// Затем раздаёт зависимости всем <see cref="GameBehaviour"/> сцены и запускает <see cref="BootState"/>.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(GameInput))]
    public sealed class GameBootstrap : GameHostBehaviour
    {
        [SerializeField] private Transform uiRoot;

        private ScreenViewBinder<GameObject> views;
        private GameInput input;

        protected override GameHost CreateHost()
        {
            input = GetComponent<GameInput>();
            return new GameHost(new UnityLogger(),
                new ConfigInstaller(UnityConfigSources.StreamingAssets(), new NewtonsoftConfigSerializer()),
                new ColorsActionInstaller(),
                new ActionInstaller(c => c
                    .RegisterInstance(input)
                    .RegisterInstance(this)
                    .Register(r => new Palette(r.Resolve<GameCore.Config.ConfigService>()))));
        }

        protected override void OnHostCreated(GameHost host)
        {
            var canvas = UiFactory.EnsureCanvas(uiRoot != null ? uiRoot : transform);
            views = new ScreenViewBinder<GameObject>(host.Screens, Destroy, host.Logger)
                .Register<InventoryScreenViewModel>(vm => InventoryView.Create(canvas, vm, host.Container));

            // BootState читает все конфиги и переводит игру в PlayState.
            host.Start<BootState>();

            foreach (var behaviour in FindObjectsByType<GameBehaviour>(FindObjectsInactive.Include))
                behaviour.Construct(host.Container);
        }

        /// <summary>Раздать зависимости объекту, созданному во время игры (предметы, манекены уровня).</summary>
        public void Inject(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<GameBehaviour>(true))
                behaviour.Construct(Host.Container);
        }

        public IResolver Resolver => Host.Container;

        protected override void Update()
        {
            base.Update();
            // TAB / Y открывает и закрывает инвентарь: состояния FSM сами решают, что делать.
            if (Host != null && input != null && input.Pressed(GameAction.Inventory))
                Host.Events.Publish(new InventoryToggleRequested());

            // F1 / Select: переключить схему управления А ↔ Б прямо в игре, чтобы сравнить по ощущениям.
            if (Host != null && input != null && input.Pressed(GameAction.SwitchScheme))
            {
                var session = Host.Container.Resolve<GameSession>();
                if (session.PlayerControl.Value)
                    session.ToggleControlScheme();
            }
        }

        protected override void OnDestroy()
        {
            views?.Dispose();
            base.OnDestroy();
        }
    }

    /// <summary>
    /// MonoBehaviour, которому нужны сервисы игры. <see cref="Construct"/> вызывается один раз до его Start:
    /// для объектов сцены — бутстрапом, для созданных в игре — через <see cref="GameBootstrap.Inject"/>.
    /// Настройку мира делайте в Start, здесь только забирайте зависимости.
    /// </summary>
    public abstract class GameBehaviour : MonoBehaviour
    {
        public bool IsConstructed { get; private set; }

        public void Construct(IResolver resolver)
        {
            if (IsConstructed)
                return;
            IsConstructed = true;
            OnConstruct(resolver);
        }

        protected abstract void OnConstruct(IResolver resolver);
    }
}
