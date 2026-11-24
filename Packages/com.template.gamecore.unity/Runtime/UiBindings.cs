using System;
using GameCore.UI;
using UnityEngine;
using UnityEngine.UI;

namespace GameCore.Unity
{
    /// <summary>
    /// Привязки uGUI к реактивным моделям. Каждая возвращает подписку; удобнее всего
    /// сразу отдать её объекту через <see cref="DisposeWith"/>, чтобы она жила столько же, сколько вью.
    /// </summary>
    public static class UiBindings
    {
        public static IDisposable BindText<T>(this Text text, IReadOnlyReactiveProperty<T> property, Func<T, string> format = null)
        {
            return property.Subscribe(value => text.text = format != null ? format(value) : value?.ToString());
        }

        public static IDisposable BindCommand(this Button button, ReactiveCommand command)
        {
            UnityEngine.Events.UnityAction onClick = () => command.Execute();
            button.onClick.AddListener(onClick);
            var canExecute = command.CanExecute.Subscribe(can => button.interactable = can);
            return Disposable.Create(() =>
            {
                canExecute.Dispose();
                if (button != null)
                    button.onClick.RemoveListener(onClick);
            });
        }

        public static IDisposable BindFill(this Slider slider, IReadOnlyReactiveProperty<int> value, int max)
        {
            slider.minValue = 0;
            slider.maxValue = max;
            return value.Subscribe(v => slider.value = v);
        }

        public static IDisposable BindActive(this GameObject gameObject, IReadOnlyReactiveProperty<bool> visible)
        {
            return visible.Subscribe(v => gameObject.SetActive(v));
        }

        /// <summary>Освобождает подписку, когда объект уничтожается.</summary>
        public static T DisposeWith<T>(this T disposable, GameObject owner) where T : IDisposable
        {
            if (!owner.TryGetComponent<DisposeOnDestroy>(out var holder))
                holder = owner.AddComponent<DisposeOnDestroy>();
            holder.Add(disposable);
            return disposable;
        }

        public static T DisposeWith<T>(this T disposable, Component owner) where T : IDisposable
        {
            return disposable.DisposeWith(owner.gameObject);
        }
    }

    [DisallowMultipleComponent]
    public sealed class DisposeOnDestroy : MonoBehaviour
    {
        private readonly CompositeDisposable disposables = new CompositeDisposable();

        public void Add(IDisposable disposable) => disposables.Add(disposable);

        private void OnDestroy() => disposables.Dispose();
    }
}
