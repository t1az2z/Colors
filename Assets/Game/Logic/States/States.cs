using System;
using ColorsAction.Configs;
using ColorsAction.Progression;
using ColorsAction.UI;
using GameCore.Config;
using GameCore.Events;
using GameCore.Fsm;
using GameCore.UI;

namespace ColorsAction.States
{
    /// <summary>Проверяет, что все конфиги читаются, и запускает игру.</summary>
    public sealed class BootState : State
    {
        private readonly ConfigService configs;
        private readonly StateMachine fsm;
        private readonly LaunchOptions launch;

        public BootState(ConfigService configs, StateMachine fsm, LaunchOptions launch = null)
        {
            this.configs = configs;
            this.fsm = fsm;
            this.launch = launch ?? LaunchOptions.None;
            AllowTransitionsTo(typeof(PlayState));
        }

        public override void Enter()
        {
            var game = configs.Load<GameConfig>(ConfigKeys.Game);
            configs.Load<PlayerConfig>(ConfigKeys.Player);
            configs.Load<CameraConfig>(ConfigKeys.Camera);
            configs.Load<CombatConfig>(ConfigKeys.Combat);
            configs.Load<DummyConfig>(ConfigKeys.Dummy);
            configs.Load<InventoryConfig>(ConfigKeys.Inventory);
            configs.LoadTable<ColorConfig>(ConfigKeys.Colors);
            configs.LoadTable<ItemConfig>(ConfigKeys.Items);
            configs.LoadTable<EnemyConfig>(ConfigKeys.Enemies);
            configs.LoadTable<WeaponUpgradeConfig>(ConfigKeys.Upgrades);
            var level = launch.LevelKey(game);
            if (LevelKeys.IsVoxel(level))
                configs.Load<Level.VoxelLevelData>(level);
            else
                configs.Load<LevelConfig>(level);
            fsm.SetState<PlayState>();
        }
    }

    /// <summary>Игрок управляет персонажем. TAB открывает инвентарь, F / X у костра — меню костра.</summary>
    public sealed class PlayState : State
    {
        private readonly GameSession session;
        private readonly StateMachine fsm;
        private readonly IEventBus events;
        private IDisposable subscription;
        private IDisposable bonfireSubscription;

        public PlayState(GameSession session, StateMachine fsm, IEventBus events)
        {
            this.session = session;
            this.fsm = fsm;
            this.events = events;
            AllowTransitionsTo(typeof(InventoryState), typeof(BonfireState));
        }

        public override void Enter()
        {
            session.PlayerControl.Value = true;
            subscription = events.Subscribe<InventoryToggleRequested>(() => fsm.SetState<InventoryState>());
            bonfireSubscription = events.Subscribe<BonfireRestRequested>(e =>
            {
                session.LastBonfire = e.BonfireId;
                fsm.SetState<BonfireState>();
            });
        }

        public override void Exit()
        {
            subscription?.Dispose();
            subscription = null;
            bonfireSubscription?.Dispose();
            bonfireSubscription = null;
            session.PlayerControl.Value = false;
        }
    }

    /// <summary>
    /// Отдых у костра: здоровье и выносливость восстанавливаются, враги уровня возрождаются (<see cref="BonfireRested"/>),
    /// открывается меню апгрейдов оружия. Как и в инвентаре, мир не на паузе, но персонаж не управляется.
    /// </summary>
    public sealed class BonfireState : State
    {
        private readonly GameSession session;
        private readonly StateMachine fsm;
        private readonly IEventBus events;
        private readonly ScreenStack screens;
        private readonly WeaponUpgradeShop shop;
        private BonfireScreenViewModel screen;

        public BonfireState(GameSession session, StateMachine fsm, IEventBus events, ScreenStack screens, WeaponUpgradeShop shop)
        {
            this.session = session;
            this.fsm = fsm;
            this.events = events;
            this.screens = screens;
            this.shop = shop;
            AllowTransitionsTo(typeof(PlayState));
        }

        public BonfireScreenViewModel Screen => screen;

        public override void Enter()
        {
            session.Vitals.Revive();
            session.Stamina.Restore();
            events.Publish(new BonfireRested(session.LastBonfire));
            screen = screens.Open(new BonfireScreenViewModel(shop, session.Inventory, () => fsm.SetState<PlayState>()));
        }

        public override void Exit()
        {
            if (screen != null)
                screens.Close(screen);
            screen = null;
        }
    }

    /// <summary>Открыт экран инвентаря. Мир не на паузе, но персонаж не управляется.</summary>
    public sealed class InventoryState : State
    {
        private readonly GameSession session;
        private readonly StateMachine fsm;
        private readonly IEventBus events;
        private readonly ScreenStack screens;
        private InventoryScreenViewModel screen;
        private IDisposable subscription;

        public InventoryState(GameSession session, StateMachine fsm, IEventBus events, ScreenStack screens)
        {
            this.session = session;
            this.fsm = fsm;
            this.events = events;
            this.screens = screens;
            AllowTransitionsTo(typeof(PlayState));
        }

        public InventoryScreenViewModel Screen => screen;

        public override void Enter()
        {
            screen = screens.Open(new InventoryScreenViewModel(session.Inventory, () => fsm.SetState<PlayState>()));
            subscription = events.Subscribe<InventoryToggleRequested>(() => fsm.SetState<PlayState>());
        }

        public override void Exit()
        {
            subscription?.Dispose();
            subscription = null;
            if (screen != null)
                screens.Close(screen);
            screen = null;
        }
    }
}
