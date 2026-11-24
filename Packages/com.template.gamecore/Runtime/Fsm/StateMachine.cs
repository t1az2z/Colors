using System;
using System.Collections.Generic;

namespace GameCore.Fsm
{
    /// <summary>
    /// Конечный автомат с состояниями по типу.
    /// <list type="bullet">
    /// <item>Состояния добавляются заранее через <see cref="Add"/> или создаются фабрикой при первом переходе
    /// (обычно фабрика берёт их из DI-контейнера).</item>
    /// <item>Переход проверяется текущим состоянием через <see cref="IState.CanTransitionTo"/>.</item>
    /// <item>SetState из Enter/Exit не вкладывается в текущий переход, а ставится в очередь
    /// и выполняется сразу после него.</item>
    /// </list>
    /// </summary>
    public sealed class StateMachine
    {
        private const string LogTag = nameof(StateMachine);

        private readonly Dictionary<Type, IState> states = new Dictionary<Type, IState>();
        private readonly Queue<Type> pending = new Queue<Type>();
        private readonly Func<Type, IState> stateFactory;
        private readonly ILogger logger;
        private bool transitioning;

        public IState Current { get; private set; }
        public Type CurrentType => Current?.GetType();

        /// <summary>
        /// Вызывается после каждого перехода: (предыдущее состояние, новое состояние).
        /// </summary>
        public event Action<IState, IState> StateChanged;

        public StateMachine(ILogger logger = null, Func<Type, IState> stateFactory = null)
        {
            this.logger = logger ?? NullLogger.Instance;
            this.stateFactory = stateFactory;
        }

        public void Add(IState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            states.Add(state.GetType(), state);
        }

        public bool IsIn<T>() where T : IState => Current is T;

        public bool SetState<T>() where T : IState => SetState(typeof(T));

        /// <summary>
        /// Переходит в состояние. Возвращает false, если текущее состояние запрещает переход.
        /// Внутри другого перехода всегда возвращает true: запрос ставится в очередь и проверяется,
        /// когда до него дойдёт очередь.
        /// </summary>
        public bool SetState(Type stateType)
        {
            if (stateType == null)
                throw new ArgumentNullException(nameof(stateType));

            if (transitioning)
            {
                pending.Enqueue(stateType);
                return true;
            }

            var result = TrySwitch(stateType);
            while (pending.Count > 0)
                TrySwitch(pending.Dequeue());
            return result;
        }

        public void Tick(float deltaTime)
        {
            if (Current is ITickableState tickable)
                tickable.Tick(deltaTime);
        }

        /// <summary>
        /// Выходит из текущего состояния без перехода в новое.
        /// </summary>
        public void Stop()
        {
            if (Current == null)
                return;

            var previous = Current;
            transitioning = true;
            try
            {
                previous.Exit();
            }
            finally
            {
                Current = null;
                transitioning = false;
                pending.Clear();
            }
        }

        private bool TrySwitch(Type stateType)
        {
            var next = GetState(stateType);

            if (Current != null && !Current.CanTransitionTo(stateType))
            {
                logger.Error(LogTag, $"Transition {Current.GetType().Name} -> {stateType.Name} is not allowed");
                return false;
            }

            var previous = Current;
            transitioning = true;
            try
            {
                previous?.Exit();
                Current = next;
                logger.Info(LogTag, $"{previous?.GetType().Name ?? "<none>"} -> {stateType.Name}");
                next.Enter();
            }
            finally
            {
                transitioning = false;
            }

            StateChanged?.Invoke(previous, next);
            return true;
        }

        private IState GetState(Type stateType)
        {
            if (states.TryGetValue(stateType, out var state))
                return state;

            if (stateFactory == null)
                throw new InvalidOperationException($"State {stateType.Name} is not added to the state machine");

            state = stateFactory(stateType) ??
                    throw new InvalidOperationException($"State factory returned null for {stateType.Name}");
            states.Add(stateType, state);
            return state;
        }
    }
}
