using System;
using System.Collections.Generic;

namespace GameCore.UI
{
    /// <summary>
    /// Стек открытых экранов как модели, без представления (логика UIController из Sparta2).
    /// Движок слушает <see cref="Opened"/>/<see cref="Closed"/> и создаёт или убирает вью — см. <see cref="ScreenViewBinder{TView}"/>.
    /// Закрытый экран освобождается (Dispose).
    /// </summary>
    public sealed class ScreenStack : IDisposable
    {
        private readonly List<ViewModel> screens = new List<ViewModel>();

        public event Action<ViewModel> Opened;
        public event Action<ViewModel> Closed;

        public IReadOnlyList<ViewModel> Screens => screens;
        public ViewModel Top => screens.Count > 0 ? screens[screens.Count - 1] : null;

        public T Open<T>(T screen) where T : ViewModel
        {
            if (screen == null)
                throw new ArgumentNullException(nameof(screen));
            if (screens.Contains(screen))
                throw new InvalidOperationException($"{screen.GetType().Name} is already open");

            if (screen is IExclusiveScreen)
            {
                for (int i = screens.Count - 1; i >= 0; i--)
                {
                    if (i < screens.Count && screens[i] is IExclusiveScreen)
                        Close(screens[i]);
                }
            }

            screens.Add(screen);
            Opened?.Invoke(screen);
            return screen;
        }

        public bool IsOpen<T>() where T : ViewModel => Find<T>() != null;

        public T Find<T>() where T : ViewModel
        {
            for (int i = screens.Count - 1; i >= 0; i--)
            {
                if (screens[i] is T typed)
                    return typed;
            }

            return null;
        }

        public bool Close(ViewModel screen)
        {
            if (screen == null || !screens.Remove(screen))
                return false;

            Closed?.Invoke(screen);
            screen.Dispose();
            return true;
        }

        public bool Close<T>() where T : ViewModel => Close(Find<T>());

        public bool CloseTop() => Close(Top);

        public void CloseAll()
        {
            while (screens.Count > 0)
                CloseTop();
        }

        public void Dispose() => CloseAll();
    }
}
