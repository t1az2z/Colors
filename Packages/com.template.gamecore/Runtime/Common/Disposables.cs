using System;
using System.Collections.Generic;

namespace GameCore
{
    public static class Disposable
    {
        public static readonly IDisposable Empty = new EmptyDisposable();

        public static IDisposable Create(Action dispose) => new ActionDisposable(dispose);

        private sealed class EmptyDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }

        private sealed class ActionDisposable : IDisposable
        {
            private Action dispose;

            public ActionDisposable(Action dispose)
            {
                this.dispose = dispose ?? throw new ArgumentNullException(nameof(dispose));
            }

            public void Dispose()
            {
                var action = dispose;
                dispose = null;
                action?.Invoke();
            }
        }
    }

    /// <summary>
    /// Собирает подписки и освобождает их разом, в обратном порядке добавления.
    /// После Dispose всё добавленное освобождается сразу.
    /// </summary>
    public sealed class CompositeDisposable : IDisposable
    {
        private readonly List<IDisposable> items = new List<IDisposable>();

        public bool IsDisposed { get; private set; }
        public int Count => items.Count;

        public void Add(IDisposable disposable)
        {
            if (disposable == null)
                throw new ArgumentNullException(nameof(disposable));

            if (IsDisposed)
            {
                disposable.Dispose();
                return;
            }

            items.Add(disposable);
        }

        public void Clear()
        {
            var snapshot = items.ToArray();
            items.Clear();
            for (int i = snapshot.Length - 1; i >= 0; i--)
                snapshot[i].Dispose();
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            Clear();
        }
    }

    public static class DisposableExtensions
    {
        public static T AddTo<T>(this T disposable, CompositeDisposable composite) where T : IDisposable
        {
            composite.Add(disposable);
            return disposable;
        }
    }
}
