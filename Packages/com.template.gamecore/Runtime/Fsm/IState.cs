using System;
using System.Collections.Generic;

namespace GameCore.Fsm
{
    public interface IState
    {
        void Enter();
        void Exit();

        /// <summary>
        /// Разрешён ли переход из этого состояния в состояние типа <paramref name="next"/>.
        /// </summary>
        bool CanTransitionTo(Type next);
    }

    /// <summary>
    /// Состояние, которому нужен кадр. Машина вызывает Tick только у текущего состояния.
    /// </summary>
    public interface ITickableState
    {
        void Tick(float deltaTime);
    }

    /// <summary>
    /// Базовое состояние. Пока не вызван <see cref="AllowTransitionsTo"/>, разрешены переходы в любое состояние.
    /// После вызова разрешены только перечисленные (белый список, как в Sparta2).
    /// </summary>
    public abstract class State : IState
    {
        private HashSet<Type> allowed;

        protected void AllowTransitionsTo(params Type[] states)
        {
            allowed ??= new HashSet<Type>();
            foreach (var state in states)
                allowed.Add(state);
        }

        public virtual bool CanTransitionTo(Type next)
        {
            return allowed == null || allowed.Contains(next);
        }

        public virtual void Enter()
        {
        }

        public virtual void Exit()
        {
        }
    }
}
