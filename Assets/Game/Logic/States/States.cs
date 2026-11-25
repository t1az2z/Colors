using System;
using ColorsAction.Configs;
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

        public BootState(ConfigService configs, StateMachine fsm)
        {
            this.configs = configs;
            this.fsm = fsm;
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
            configs.Load<LevelConfig>(game.Level);
            fsm.SetState<PlayState>();
        }
    }

    /// <summary>Игрок управляет персонажем. TAB открывает инвентарь.</summary>
    public sealed class PlayState : State
    {
        private readonly GameSession session;
        private readonly StateMachine fsm;
        private readonly IEventBus events;
        private IDisposable subscription;

        public PlayState(GameSession session, StateMachine fsm, IEventBus events)
        {
            this.session = session;
            this.fsm = fsm;
            this.events = events;
            AllowTransitionsTo(typeof(InventoryState));
        }

        public override void Enter()
        {
            session.PlayerControl.Value = true;
            subscription = events.Subscribe<InventoryToggleRequested>(() => fsm.SetState<InventoryState>());
        }

        public override void Exit()
        {
            subscription?.Dispose();
            subscription = null;
            session.PlayerControl.Value = false;
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
