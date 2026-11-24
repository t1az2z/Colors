using System;
using System.Collections.Generic;

namespace GameCore.UI
{
    /// <summary>
    /// Связывает <see cref="ScreenStack"/> с представлениями движка. TView — что угодно:
    /// GameObject в Unity, Control в Godot, заглушка в тестах.
    /// Для каждого типа модели регистрируется функция, создающая вью; при закрытии экрана вью уничтожается.
    /// </summary>
    public sealed class ScreenViewBinder<TView> : IDisposable where TView : class
    {
        private readonly ScreenStack stack;
        private readonly Action<TView> destroyView;
        private readonly Dictionary<Type, Func<ViewModel, TView>> factories = new Dictionary<Type, Func<ViewModel, TView>>();
        private readonly Dictionary<ViewModel, TView> views = new Dictionary<ViewModel, TView>();
        private readonly ILogger logger;

        public ScreenViewBinder(ScreenStack stack, Action<TView> destroyView, ILogger logger = null)
        {
            this.stack = stack ?? throw new ArgumentNullException(nameof(stack));
            this.destroyView = destroyView ?? throw new ArgumentNullException(nameof(destroyView));
            this.logger = logger ?? NullLogger.Instance;

            stack.Opened += OnOpened;
            stack.Closed += OnClosed;
        }

        public ScreenViewBinder<TView> Register<TViewModel>(Func<TViewModel, TView> createView) where TViewModel : ViewModel
        {
            factories.Add(typeof(TViewModel), vm => createView((TViewModel)vm));
            return this;
        }

        /// <summary>Создаёт вью для уже открытых экранов: если биндер подключили позже, чем открыли экран.</summary>
        public void SyncExisting()
        {
            foreach (var screen in stack.Screens)
            {
                if (!views.ContainsKey(screen))
                    OnOpened(screen);
            }
        }

        public bool TryGetView(ViewModel screen, out TView view) => views.TryGetValue(screen, out view);

        public void Dispose()
        {
            stack.Opened -= OnOpened;
            stack.Closed -= OnClosed;
            foreach (var view in views.Values)
                destroyView(view);
            views.Clear();
        }

        private void OnOpened(ViewModel screen)
        {
            if (!factories.TryGetValue(screen.GetType(), out var factory))
            {
                logger.Error(nameof(ScreenViewBinder<TView>), $"No view registered for {screen.GetType().Name}");
                return;
            }

            views[screen] = factory(screen);
        }

        private void OnClosed(ViewModel screen)
        {
            if (views.TryGetValue(screen, out var view))
            {
                views.Remove(screen);
                destroyView(view);
            }
        }
    }
}
