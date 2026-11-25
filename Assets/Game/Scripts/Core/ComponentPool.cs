using System;
using System.Collections.Generic;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Простой пул объектов для частых короткоживущих эффектов (пули, осколки, вспышки): вместо Instantiate/Destroy
    /// объект выключается и ждёт следующего раза. Все объекты пула лежат под общим корнем в сцене; при смене сцены
    /// корень и объекты уничтожаются вместе с ней, уничтоженные записи пул просто пропускает.
    /// </summary>
    public sealed class ComponentPool<T> where T : Component
    {
        private readonly Stack<T> free = new Stack<T>();
        private readonly Func<Transform, T> create;
        private readonly string name;
        private Transform root;

        /// <param name="name">Имя корня в иерархии (для отладки).</param>
        /// <param name="create">Создать новый объект под данным корнем (вызывается, когда свободных нет).</param>
        public ComponentPool(string name, Func<Transform, T> create)
        {
            this.name = name;
            this.create = create ?? throw new ArgumentNullException(nameof(create));
        }

        /// <summary>Корень пула в сцене (создаётся заново, если сцена сменилась).</summary>
        public Transform Root
        {
            get
            {
                if (root == null)
                    root = new GameObject($"Pool_{name}").transform;
                return root;
            }
        }

        /// <summary>Свободных объектов в пуле.</summary>
        public int FreeCount => free.Count;

        /// <summary>Взять объект: свободный из пула или новый. Возвращается включённым.</summary>
        public T Get()
        {
            while (free.Count > 0)
            {
                var item = free.Pop();
                if (item == null)
                    continue; // уничтожен вместе со сценой
                item.gameObject.SetActive(true);
                return item;
            }

            var created = create(Root);
            created.gameObject.SetActive(true);
            return created;
        }

        /// <summary>Вернуть объект в пул (выключается). Повторный возврат игнорируется.</summary>
        public void Release(T item)
        {
            if (item == null || !item.gameObject.activeSelf)
                return;
            item.gameObject.SetActive(false);
            if (item.transform.parent != Root)
                item.transform.SetParent(Root, false);
            free.Push(item);
        }
    }
}
