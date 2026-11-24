using System;
using System.Collections;
using System.Collections.Generic;

namespace GameCore.UI
{
    /// <summary>
    /// Список с событиями изменений — для списков в интерфейсе (инвентарь, лог, отряд).
    /// Вью при подписке строит элементы по текущему содержимому, затем реагирует на события.
    /// </summary>
    public sealed class ReactiveList<T> : IReadOnlyList<T>
    {
        private readonly List<T> items = new List<T>();
        private readonly ReactiveProperty<int> count = new ReactiveProperty<int>();

        /// <summary>(индекс, элемент)</summary>
        public event Action<int, T> Added;

        /// <summary>(индекс, элемент)</summary>
        public event Action<int, T> Removed;

        /// <summary>(индекс, старый элемент, новый элемент)</summary>
        public event Action<int, T, T> Replaced;

        /// <summary>Список очищен.</summary>
        public event Action Cleared;

        public IReadOnlyReactiveProperty<int> CountProperty => count;
        public int Count => items.Count;

        public ReactiveList()
        {
        }

        public ReactiveList(IEnumerable<T> initial)
        {
            items.AddRange(initial);
            count.Value = items.Count;
        }

        public T this[int index]
        {
            get => items[index];
            set
            {
                var old = items[index];
                items[index] = value;
                Replaced?.Invoke(index, old, value);
            }
        }

        public void Add(T item) => Insert(items.Count, item);

        public void Insert(int index, T item)
        {
            items.Insert(index, item);
            count.Value = items.Count;
            Added?.Invoke(index, item);
        }

        public bool Remove(T item)
        {
            var index = items.IndexOf(item);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            var item = items[index];
            items.RemoveAt(index);
            count.Value = items.Count;
            Removed?.Invoke(index, item);
        }

        public void Clear()
        {
            if (items.Count == 0)
                return;
            items.Clear();
            count.Value = 0;
            Cleared?.Invoke();
        }

        public int IndexOf(T item) => items.IndexOf(item);
        public bool Contains(T item) => items.Contains(item);
        public IEnumerator<T> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
