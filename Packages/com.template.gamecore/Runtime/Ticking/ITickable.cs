namespace GameCore.Ticking
{
    /// <summary>Вызывается каждый кадр (Update / _Process).</summary>
    public interface ITickable
    {
        void Tick(float deltaTime);
    }

    /// <summary>Вызывается после всех Tick в кадре (LateUpdate).</summary>
    public interface ILateTickable
    {
        void LateTick(float deltaTime);
    }

    /// <summary>Вызывается с фиксированным шагом (FixedUpdate / _PhysicsProcess).</summary>
    public interface IFixedTickable
    {
        void FixedTick(float fixedDeltaTime);
    }

    public enum TickMode
    {
        /// <summary>Учитывает паузу и масштаб времени.</summary>
        Scaled,

        /// <summary>Работает всегда в реальном времени: UI, меню паузы, сетевые таймауты.</summary>
        Unscaled
    }
}
