using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GameCore.DI
{
    public enum Lifetime
    {
        /// <summary>Один экземпляр на контейнер, создаётся при первом запросе.</summary>
        Singleton,

        /// <summary>Новый экземпляр на каждый запрос.</summary>
        Transient
    }

    public interface IResolver
    {
        T Resolve<T>();
        object Resolve(Type type);
        bool TryResolve<T>(out T instance);
        bool IsRegistered(Type type);
    }

    /// <summary>
    /// Помечает конструктор, который контейнер должен использовать, если публичных конструкторов несколько.
    /// </summary>
    [AttributeUsage(AttributeTargets.Constructor)]
    public sealed class InjectAttribute : Attribute
    {
    }

    public sealed class ResolveException : Exception
    {
        public ResolveException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Небольшой DI-контейнер вместо IoC/TheGame из Sparta2, но без глобального статического доступа:
    /// зависимости приходят через конструктор, поэтому любой класс можно собрать в тесте руками.
    /// <list type="bullet">
    /// <item>Регистрация типом (внедрение через конструктор), фабрикой или готовым экземпляром.</item>
    /// <item>Дочерние контейнеры (<see cref="CreateScope"/>) для сервисов с коротким временем жизни:
    /// бой, уровень, экран.</item>
    /// <item>Циклические зависимости дают понятную ошибку с цепочкой типов.</item>
    /// <item>Dispose освобождает созданные контейнером IDisposable-синглтоны в обратном порядке.</item>
    /// </list>
    /// Для IL2CPP: классы, которые создаются по типу, держите с атрибутом [Preserve]
    /// или регистрируйте фабрикой — она не использует рефлексию.
    /// </summary>
    public sealed class Container : IResolver, IDisposable
    {
        private readonly Container parent;
        private readonly Dictionary<Type, Registration> registrations = new Dictionary<Type, Registration>();
        private readonly List<IDisposable> owned = new List<IDisposable>();
        private readonly List<Type> resolving = new List<Type>();
        private bool disposed;

        public Container() : this(null)
        {
        }

        private Container(Container parent)
        {
            this.parent = parent;
        }

        public Container CreateScope() => new Container(this);

        public Container RegisterInstance<T>(T instance)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            Add(typeof(T), new Registration(Lifetime.Singleton, null) { Instance = instance, HasInstance = true });
            return this;
        }

        public Container Register<T>(Lifetime lifetime = Lifetime.Singleton) where T : class
        {
            return Register<T, T>(lifetime);
        }

        public Container Register<TService, TImplementation>(Lifetime lifetime = Lifetime.Singleton)
            where TImplementation : class, TService
        {
            var implementation = typeof(TImplementation);
            Add(typeof(TService), new Registration(lifetime, r => ((Container)r).Construct(implementation)));
            return this;
        }

        public Container Register<T>(Func<IResolver, T> factory, Lifetime lifetime = Lifetime.Singleton)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            Add(typeof(T), new Registration(lifetime, r => factory(r)));
            return this;
        }

        /// <summary>
        /// Делает уже зарегистрированный сервис доступным ещё и под другим типом (тот же экземпляр).
        /// </summary>
        public Container Alias<TAlias, TService>() where TService : TAlias
        {
            Add(typeof(TAlias), new Registration(Lifetime.Transient, r => r.Resolve(typeof(TService))));
            return this;
        }

        public T Resolve<T>() => (T)Resolve(typeof(T));

        public object Resolve(Type type)
        {
            if (TryResolve(type, out var instance))
                return instance;
            throw new ResolveException($"Type {type.FullName} is not registered{FormatChain(type)}");
        }

        public bool TryResolve<T>(out T instance)
        {
            if (TryResolve(typeof(T), out var obj))
            {
                instance = (T)obj;
                return true;
            }

            instance = default;
            return false;
        }

        public bool IsRegistered(Type type)
        {
            return type == typeof(IResolver) || type == typeof(Container) ||
                   registrations.ContainsKey(type) || (parent != null && parent.IsRegistered(type));
        }

        /// <summary>
        /// Создаёт незарегистрированный тип: параметры конструктора берутся из контейнера,
        /// а недостающие — из <paramref name="extraArguments"/> по типу (аналог InstantiateWithDependencies).
        /// Созданный объект контейнеру не принадлежит.
        /// </summary>
        public T Instantiate<T>(params object[] extraArguments) => (T)Instantiate(typeof(T), extraArguments);

        public object Instantiate(Type type, params object[] extraArguments)
        {
            return Construct(type, extraArguments);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            for (int i = owned.Count - 1; i >= 0; i--)
                owned[i].Dispose();
            owned.Clear();
            registrations.Clear();
        }

        private void Add(Type type, Registration registration)
        {
            ThrowIfDisposed();
            if (registrations.ContainsKey(type))
                throw new InvalidOperationException($"Type {type.FullName} is already registered");
            registrations.Add(type, registration);
        }

        private bool TryResolve(Type type, out object instance)
        {
            ThrowIfDisposed();

            if (type == typeof(IResolver) || type == typeof(Container))
            {
                instance = this;
                return true;
            }

            if (registrations.TryGetValue(type, out var registration))
            {
                instance = Produce(type, registration);
                return true;
            }

            if (parent != null)
                return parent.TryResolve(type, out instance);

            instance = null;
            return false;
        }

        private object Produce(Type type, Registration registration)
        {
            if (registration.HasInstance)
                return registration.Instance;

            if (resolving.Contains(type))
                throw new ResolveException($"Circular dependency: {Chain(type)}");

            resolving.Add(type);
            object instance;
            try
            {
                instance = registration.Factory(this);
            }
            finally
            {
                resolving.RemoveAt(resolving.Count - 1);
            }

            if (registration.Lifetime == Lifetime.Singleton)
            {
                registration.Instance = instance;
                registration.HasInstance = true;
                if (instance is IDisposable disposable)
                    owned.Add(disposable);
            }

            return instance;
        }

        private object Construct(Type type, object[] extraArguments = null)
        {
            var constructor = SelectConstructor(type);
            var parameters = constructor.GetParameters();
            var arguments = new object[parameters.Length];
            var extras = extraArguments == null ? new List<object>() : new List<object>(extraArguments);

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameterType = parameters[i].ParameterType;
                var extraIndex = extras.FindIndex(parameterType.IsInstanceOfType);
                if (extraIndex >= 0)
                {
                    arguments[i] = extras[extraIndex];
                    extras.RemoveAt(extraIndex);
                    continue;
                }

                if (TryResolve(parameterType, out var dependency))
                {
                    arguments[i] = dependency;
                    continue;
                }

                if (parameters[i].HasDefaultValue)
                {
                    arguments[i] = parameters[i].DefaultValue;
                    continue;
                }

                throw new ResolveException(
                    $"Can't create {type.Name}: parameter '{parameters[i].Name}' of type {parameterType.Name} " +
                    $"is not registered{FormatChain(type)}");
            }

            return constructor.Invoke(arguments);
        }

        private static ConstructorInfo SelectConstructor(Type type)
        {
            if (type.IsAbstract || type.IsInterface)
                throw new ResolveException($"Can't create abstract type {type.FullName}");

            var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (constructors.Length == 1)
                return constructors[0];

            var marked = constructors.Where(c => c.IsDefined(typeof(InjectAttribute), false)).ToArray();
            if (marked.Length == 1)
                return marked[0];

            throw new ResolveException(
                $"{type.Name} must have exactly one public constructor or one marked with [Inject] " +
                $"(found {constructors.Length})");
        }

        private string FormatChain(Type type)
        {
            return resolving.Count == 0 ? string.Empty : $" ({Chain(type)})";
        }

        private string Chain(Type type)
        {
            return string.Join(" -> ", resolving.Select(t => t.Name).Concat(new[] { type.Name }));
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(Container));
        }

        private sealed class Registration
        {
            public readonly Lifetime Lifetime;
            public readonly Func<IResolver, object> Factory;
            public object Instance;
            public bool HasInstance;

            public Registration(Lifetime lifetime, Func<IResolver, object> factory)
            {
                Lifetime = lifetime;
                Factory = factory;
            }
        }
    }
}
