using System;
using System.Collections.Generic;

namespace GameCore.UI
{
    public interface IReadOnlyReactiveProperty<T>
    {
        T Value { get; }

        /// <summary>
        /// Подписка на изменения. При <paramref name="notifyCurrent"/> обработчик сразу получает текущее значение —
        /// так представление инициализируется и обновляется одним и тем же кодом.
        /// </summary>
        IDisposable Subscribe(Action<T> onChanged, bool notifyCurrent = true);
    }

    /// <summary>
    /// Значение с уведомлением об изменении. Основа моделей интерфейса: вью подписывается, модель меняет Value.
    /// Одинаковое значение (по EqualityComparer) не рассылается.
    /// </summary>
    public sealed class ReactiveProperty<T> : IReadOnlyReactiveProperty<T>, IDisposable
    {
        private readonly List<Action<T>> handlers = new List<Action<T>>();
        private readonly IEqualityComparer<T> comparer;
        private T value;

        public ReactiveProperty(T initialValue = default, IEqualityComparer<T> comparer = null)
        {
            value = initialValue;
            this.comparer = comparer ?? EqualityComparer<T>.Default;
        }

        public T Value
        {
            get => value;
            set
            {
                if (comparer.Equals(this.value, value))
                    return;
                this.value = value;
                Notify();
            }
        }

        public int SubscriberCount => handlers.Count;

        /// <summary>Записывает значение и рассылает его, даже если оно не изменилось.</summary>
        public void SetValueAndForceNotify(T newValue)
        {
            value = newValue;
            Notify();
        }

        public IDisposable Subscribe(Action<T> onChanged, bool notifyCurrent = true)
        {
            if (onChanged == null)
                throw new ArgumentNullException(nameof(onChanged));

            handlers.Add(onChanged);
            if (notifyCurrent)
                onChanged(value);
            return Disposable.Create(() => handlers.Remove(onChanged));
        }

        public void Dispose() => handlers.Clear();

        public override string ToString() => value?.ToString() ?? "null";

        private void Notify()
        {
            var current = value;
            foreach (var handler in handlers.ToArray())
                handler(current);
        }
    }

    public static class ReactivePropertyExtensions
    {
        /// <summary>
        /// Производное свойство: пересчитывается при каждом изменении источника.
        /// Освобождайте результат вместе с моделью, чтобы отписаться от источника.
        /// </summary>
        public static DerivedProperty<TResult> Select<TSource, TResult>(
            this IReadOnlyReactiveProperty<TSource> source, Func<TSource, TResult> selector)
        {
            return new DerivedProperty<TResult>(property => source.Subscribe(v => property.Value = selector(v)));
        }

        /// <summary>
        /// Свойство из двух источников, например "HP / MaxHP".
        /// </summary>
        public static DerivedProperty<TResult> CombineWith<T1, T2, TResult>(
            this IReadOnlyReactiveProperty<T1> first, IReadOnlyReactiveProperty<T2> second, Func<T1, T2, TResult> combine)
        {
            return new DerivedProperty<TResult>(property =>
            {
                var subscriptions = new CompositeDisposable();
                Action update = () => property.Value = combine(first.Value, second.Value);
                first.Subscribe(_ => update(), false).AddTo(subscriptions);
                second.Subscribe(_ => update(), false).AddTo(subscriptions);
                update();
                return subscriptions;
            });
        }
    }

    public sealed class DerivedProperty<T> : IReadOnlyReactiveProperty<T>, IDisposable
    {
        private readonly ReactiveProperty<T> inner = new ReactiveProperty<T>();
        private readonly IDisposable subscription;

        internal DerivedProperty(Func<ReactiveProperty<T>, IDisposable> connect)
        {
            subscription = connect(inner);
        }

        public T Value => inner.Value;

        public IDisposable Subscribe(Action<T> onChanged, bool notifyCurrent = true) => inner.Subscribe(onChanged, notifyCurrent);

        public void Dispose()
        {
            subscription.Dispose();
            inner.Dispose();
        }
    }
}
