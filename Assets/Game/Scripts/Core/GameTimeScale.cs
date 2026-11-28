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
        private static bool cinematic;

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

        /// <summary>
        /// Мир застыл на время сцены (подбор нового предмета): время стоит, пока флаг поднят. Отдельно от фриз-кадров,
        /// чтобы конец фриза удара не запустил время посреди сцены.
        /// </summary>
        public static void SetCinematic(bool value)
        {
            cinematic = value;
            Apply();
        }

        public static bool Cinematic => cinematic;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            slowMotion = 1f;
            frozen = false;
            cinematic = false;
        }

        private static void Apply() => Time.timeScale = frozen || cinematic ? 0f : slowMotion;
    }
}
