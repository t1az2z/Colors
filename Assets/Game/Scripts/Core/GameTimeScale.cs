using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Единственный владелец Time.timeScale. Замедление (колесо оружия) и фриз-кадры удара складываются здесь,
    /// а не перезаписывают друг друга: пока идёт фриз, время стоит; после него — скорость замедления
    /// (или 1, если замедления нет).
    /// </summary>
    public static class GameTimeScale
    {
        private static float slowMotion = 1f;
        private static bool frozen;

        /// <summary>Замедление времени (1 — нормальная скорость).</summary>
        public static void SetSlowMotion(float scale)
        {
            slowMotion = Mathf.Clamp(scale, 0.01f, 1f);
            Apply();
        }

        /// <summary>Фриз-кадры: время стоит, пока флаг поднят.</summary>
        public static void SetFrozen(bool value)
        {
            frozen = value;
            Apply();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            slowMotion = 1f;
            frozen = false;
        }

        private static void Apply() => Time.timeScale = frozen ? 0f : slowMotion;
    }
}
