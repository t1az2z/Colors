using GameCore.Hosting;
using UnityEngine;

namespace GameCore.Unity
{
    /// <summary>
    /// Мост между циклом Unity и <see cref="GameHost"/>: Update → Tick, LateUpdate → LateTick,
    /// FixedUpdate → FixedTick, OnDestroy → Dispose. Наследник собирает хост и запускает начальное состояние.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public abstract class GameHostBehaviour : MonoBehaviour
    {
        [SerializeField] private bool dontDestroyOnLoad = true;

        public GameHost Host { get; private set; }

        protected abstract GameHost CreateHost();

        /// <summary>Хост создан: подключите представления и вызовите Host.Start.</summary>
        protected abstract void OnHostCreated(GameHost host);

        protected virtual void Awake()
        {
            if (dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);

            Host = CreateHost();
            OnHostCreated(Host);
        }

        protected virtual void Update() => Host?.Tick(Time.deltaTime);

        protected virtual void LateUpdate() => Host?.LateTick(Time.deltaTime);

        protected virtual void FixedUpdate() => Host?.FixedTick(Time.fixedDeltaTime);

        protected virtual void OnDestroy()
        {
            Host?.Dispose();
            Host = null;
        }
    }
}
