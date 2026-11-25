using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Для автотестов и прогонов через Unity CLI: когда окно Unity не в фокусе, Play Mode стоит.
    /// Степпер ставит паузу и двигает игру по кадру на каждый тик редактора с фиксированным шагом 1/60.
    /// Ввод — через <see cref="GameInput.ScriptPress"/> / <see cref="GameInput.ScriptedMove"/>.
    /// </summary>
    public static class PlayModeStepper
    {
        private static bool enabled;

        public static bool Enabled => enabled;

        [MenuItem("ColorsAction/Debug/Step Play Mode While Unfocused")]
        public static void Toggle()
        {
            if (enabled)
                Disable();
            else
                Enable();
        }

        public static void Enable()
        {
            if (enabled)
                return;
            enabled = true;
            Time.captureDeltaTime = 1f / 60f;
            EditorApplication.isPaused = true;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static void Disable()
        {
            if (!enabled)
                return;
            enabled = false;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Time.captureDeltaTime = 0f;
            if (EditorApplication.isPlaying)
                EditorApplication.isPaused = false;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
                return;
            if (EditorApplication.isPaused)
                EditorApplication.Step();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
                Disable();
        }
    }
}
