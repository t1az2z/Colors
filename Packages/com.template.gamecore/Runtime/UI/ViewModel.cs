using System;

namespace GameCore.UI
{
    /// <summary>
    /// Модель экрана или виджета. Держит реактивные свойства и команды, ничего не знает о движке.
    /// Всё, что модель создаёт или на что подписывается, добавляйте через <see cref="Own{T}"/> —
    /// оно освободится вместе с моделью.
    /// </summary>
    public abstract class ViewModel : IDisposable
    {
        private readonly CompositeDisposable disposables = new CompositeDisposable();

        public bool IsDisposed => disposables.IsDisposed;

        protected T Own<T>(T disposable) where T : IDisposable
        {
            disposables.Add(disposable);
            return disposable;
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;
            OnDispose();
            disposables.Dispose();
        }

        protected virtual void OnDispose()
        {
        }
    }

    /// <summary>
    /// Экран, который при открытии закрывает другие такие же экраны
    /// (excludingWindows в UIController из Sparta2: штаб, бюджет, магазин...).
    /// </summary>
    public interface IExclusiveScreen
    {
    }
}
