using System;
using GameCore.DI;
using GameCore.Events;
using GameCore.Fsm;
using GameCore.Ticking;
using GameCore.UI;

namespace GameCore.Hosting
{
    /// <summary>
    /// Часть регистрации сервисов. Игра состоит из нескольких установщиков: платформа (движок), конфиги, геймплей.
    /// </summary>
    public interface IInstaller
    {
        void Install(Container container);
    }

    /// <summary>
    /// Корень композиции игры (Bootstrap + TheGame из Sparta2, но без статики).
    /// Регистрирует базовые сервисы ядра, запускает установщики и раздаёт кадры.
    /// Движок создаёт хост, вызывает Start, затем каждый кадр Tick/LateTick/FixedTick, а при выходе — Dispose.
    /// <para>Базовые сервисы: ILogger, IEventBus, TickScheduler, TimerService, StateMachine, ScreenStack.</para>
    /// </summary>
    public sealed class GameHost : IDisposable
    {
        public Container Container { get; }
        public ILogger Logger { get; }
        public IEventBus Events { get; }
        public TickScheduler Ticks { get; }
        public StateMachine States { get; }
        public ScreenStack Screens { get; }
        public bool IsDisposed { get; private set; }

        public GameHost(ILogger logger, params IInstaller[] installers)
        {
            Logger = logger ?? NullLogger.Instance;
            Container = new Container();

            Events = new EventBus(Logger);
            Ticks = new TickScheduler();
            var timers = new TimerService();
            Ticks.Add(timers);
            States = new StateMachine(Logger, type => (IState)Container.Resolve(type));
            Screens = new ScreenStack();

            Container
                .RegisterInstance(Logger)
                .RegisterInstance(Events)
                .RegisterInstance(Ticks)
                .RegisterInstance(timers)
                .RegisterInstance(States)
                .RegisterInstance(Screens);

            foreach (var installer in installers)
                installer.Install(Container);
        }

        /// <summary>Переводит игру в начальное состояние. Состояние должно быть зарегистрировано в контейнере.</summary>
        public void Start<TState>() where TState : IState
        {
            States.SetState<TState>();
        }

        public void Tick(float deltaTime)
        {
            Ticks.Tick(deltaTime);
            States.Tick(Ticks.Paused ? 0f : deltaTime * Ticks.TimeScale);
        }

        public void LateTick(float deltaTime) => Ticks.LateTick(deltaTime);

        public void FixedTick(float fixedDeltaTime) => Ticks.FixedTick(fixedDeltaTime);

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;

            States.Stop();
            Screens.CloseAll();
            Container.Dispose();
        }
    }

    /// <summary>
    /// Установщик из лямбды: удобно для небольших регистраций и тестов.
    /// </summary>
    public sealed class ActionInstaller : IInstaller
    {
        private readonly Action<Container> install;

        public ActionInstaller(Action<Container> install)
        {
            this.install = install ?? throw new ArgumentNullException(nameof(install));
        }

        public void Install(Container container) => install(container);
    }
}
