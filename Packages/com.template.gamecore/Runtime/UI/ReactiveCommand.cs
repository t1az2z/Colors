using System;
using System.Collections.Generic;

namespace GameCore.UI
{
    /// <summary>
    /// Действие модели, которое вызывает представление (кнопка, пункт меню).
    /// <see cref="CanExecute"/> вью использует, чтобы включать и выключать кнопку.
    /// </summary>
    public sealed class ReactiveCommand : IDisposable
    {
        private static readonly IReadOnlyReactiveProperty<bool> AlwaysTrue = new ReactiveProperty<bool>(true);

        private readonly List<Action> handlers = new List<Action>();

        public IReadOnlyReactiveProperty<bool> CanExecute { get; }

        public ReactiveCommand(IReadOnlyReactiveProperty<bool> canExecute = null)
        {
            CanExecute = canExecute ?? AlwaysTrue;
        }

        public ReactiveCommand(Action handler, IReadOnlyReactiveProperty<bool> canExecute = null) : this(canExecute)
        {
            Subscribe(handler);
        }

        /// <summary>Выполняет команду, если она доступна. Возвращает, была ли она выполнена.</summary>
        public bool Execute()
        {
            if (!CanExecute.Value)
                return false;
            foreach (var handler in handlers.ToArray())
                handler();
            return true;
        }

        public IDisposable Subscribe(Action handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            handlers.Add(handler);
            return Disposable.Create(() => handlers.Remove(handler));
        }

        public void Dispose() => handlers.Clear();
    }

    /// <summary>Команда с параметром: выбор элемента списка, слот инвентаря и т. п.</summary>
    public sealed class ReactiveCommand<T> : IDisposable
    {
        private static readonly IReadOnlyReactiveProperty<bool> AlwaysTrue = new ReactiveProperty<bool>(true);

        private readonly List<Action<T>> handlers = new List<Action<T>>();

        public IReadOnlyReactiveProperty<bool> CanExecute { get; }

        public ReactiveCommand(IReadOnlyReactiveProperty<bool> canExecute = null)
        {
            CanExecute = canExecute ?? AlwaysTrue;
        }

        public ReactiveCommand(Action<T> handler, IReadOnlyReactiveProperty<bool> canExecute = null) : this(canExecute)
        {
            Subscribe(handler);
        }

        public bool Execute(T parameter)
        {
            if (!CanExecute.Value)
                return false;
            foreach (var handler in handlers.ToArray())
                handler(parameter);
            return true;
        }

        public IDisposable Subscribe(Action<T> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            handlers.Add(handler);
            return Disposable.Create(() => handlers.Remove(handler));
        }

        public void Dispose() => handlers.Clear();
    }
}
