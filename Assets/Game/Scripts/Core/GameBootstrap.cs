using ColorsAction.States;
using ColorsAction.UI;
using GameCore.DI;
using GameCore.Hosting;
using GameCore.Newtonsoft;
using GameCore.UI;
using GameCore.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

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
        private const string HudResource = "UI/Hud";
        private const string InventoryResource = "UI/InventoryScreen";

        [SerializeField] private Transform uiRoot;
        [Tooltip("Префаб HUD; пусто — Resources/UI/Hud.")]
        [SerializeField] private Hud hudPrefab;
        [Tooltip("Префаб экрана инвентаря; пусто — Resources/UI/InventoryScreen.")]
        [SerializeField] private InventoryView inventoryPrefab;

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
                    .RegisterInstance(LaunchOptionsStore.Load())
                    .Register(r => new Palette(r.Resolve<GameCore.Config.ConfigService>()))));
        }

        protected override void OnHostCreated(GameHost host)
        {
            // Интерфейс — префабы со своими Canvas (Resources/UI): HUD живёт всю игру, экраны создаются по требованию.
            var parent = uiRoot != null ? uiRoot : transform;
            EnsureEventSystem(parent);
            if (hudPrefab == null)
                hudPrefab = Resources.Load<Hud>(HudResource);
            if (FindAnyObjectByType<Hud>() == null && hudPrefab != null)
                Instantiate(hudPrefab, parent, false).name = "Hud";
            if (FindAnyObjectByType<WeaponWheel>() == null)
                WeaponWheel.Create(parent, host.Container);
            if (inventoryPrefab == null)
                inventoryPrefab = Resources.Load<InventoryView>(InventoryResource);
            // Меню костра: префаб Resources/UI/BonfireMenu, пока его не собрали в редакторе — строится кодом.
            var bonfirePrefab = Resources.Load<BonfireView>(BonfireView.Resource);
            views = new ScreenViewBinder<GameObject>(host.Screens, Destroy, host.Logger)
                .Register<InventoryScreenViewModel>(vm => InventoryView.Create(inventoryPrefab, parent, vm, host.Container))
                .Register<BonfireScreenViewModel>(vm => BonfireView.Create(bonfirePrefab, parent, vm, host.Container));

            // BootState читает все конфиги и переводит игру в PlayState.
            host.Start<BootState>();

            foreach (var behaviour in FindObjectsByType<GameBehaviour>(FindObjectsInactive.Include))
                behaviour.Construct(host.Container);
        }

        private static void EnsureEventSystem(Transform parent)
        {
            if (FindAnyObjectByType<EventSystem>() != null)
                return;
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(parent, false);
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
            // I / Select открывает и закрывает инвентарь: состояния FSM сами решают, что делать.
            if (Host != null && input != null && input.Pressed(GameAction.Inventory))
                Host.Events.Publish(new InventoryToggleRequested());

            // F1: переключить схему управления А ↔ Б прямо в игре, чтобы сравнить по ощущениям.
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
