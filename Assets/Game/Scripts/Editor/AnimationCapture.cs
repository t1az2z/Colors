using System.IO;
using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Снимки анимаций кубов крупным планом в Play Mode (для проверки глазами): своя камера смотрит на игрока или
    /// врага спереди-сбоку и каждые N кадров сохраняет PNG в папку. Гифка из кадров — Tools/Dev/frames_to_gif.py.
    /// <para>Запуск через Unity CLI в Play Mode (окно может быть не в фокусе — включается <see cref="PlayModeStepper"/>):
    /// <c>ColorsAction.Unity.Editor.AnimationCapture.Begin("player", 45, 4, "Temp/AnimCapture/idle");</c>, потом
    /// ждать <see cref="Done"/>. Цель — "player" или "enemy" (ближайший к игроку враг; "enemy:far" — самый дальний).</para>
    /// </summary>
    public static class AnimationCapture
    {
        private const int Size = 480;

        private static Transform target;
        private static Transform body;
        private static float height;
        private static int framesLeft;
        private static int every;
        private static int lastFrame;
        private static int index;
        private static string folder;
        private static Camera camera;
        private static RenderTexture texture;
        private static Texture2D readback;
        private static float yawOffset;
        private static float distance;
        private static float pitch;
        private static float fixedYaw;

        /// <summary>Съёмка закончена (или не началась).</summary>
        public static bool Done => framesLeft <= 0;

        public static string LastReport { get; private set; } = "";

        /// <summary>Цель съёмки (для сценария: например, враг, на которого навели камеру).</summary>
        public static Transform Target => target;

        /// <param name="who">"player", "enemy" (ближайший к игроку) или "enemy:far" (самый дальний).</param>
        /// <param name="frames">Сколько кадров снять.</param>
        /// <param name="everyFrames">Через сколько игровых кадров снимать (при шаге 1/60: 4 → 15 кадров/с).</param>
        /// <param name="outFolder">Папка от корня проекта (лучше в Temp/).</param>
        /// <param name="sideYaw">Угол камеры от лица куба, градусы (плюс — справа от куба).</param>
        public static string Begin(string who, int frames, int everyFrames, string outFolder, float sideYaw = 35f, float dist = 3.6f,
            float pitchDeg = 22f)
        {
            if (!EditorApplication.isPlaying)
                return LastReport = "not playing";
            target = FindTarget(who, out body, out height);
            if (target == null)
                return LastReport = "target not found: " + who;
            PlayModeStepper.Enable();
            framesLeft = Mathf.Max(1, frames);
            every = Mathf.Max(1, everyFrames);
            lastFrame = Time.frameCount;
            index = 0;
            yawOffset = sideYaw;
            distance = dist;
            pitch = pitchDeg;
            fixedYaw = body != null ? body.eulerAngles.y : target.eulerAngles.y;
            folder = Path.Combine(Directory.GetCurrentDirectory(), outFolder);
            if (Directory.Exists(folder))
                foreach (var f in Directory.GetFiles(folder, "*.png"))
                    File.Delete(f);
            Directory.CreateDirectory(folder);
            EnsureCamera();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return LastReport = $"capturing {target.name} → {folder}";
        }

        /// <summary>Повернуть камеру вслед за телом цели заново (после разворота).</summary>
        public static void Reframe()
        {
            if (body != null)
                fixedYaw = body.eulerAngles.y;
        }

        private static Transform FindTarget(string who, out Transform targetBody, out float targetHeight)
        {
            targetBody = null;
            targetHeight = 1f;
            var player = Object.FindAnyObjectByType<PlayerController>();
            if (who == "player")
            {
                if (player == null)
                    return null;
                targetBody = player.Body;
                return player.transform;
            }

            EnemyController best = null;
            var bestScore = 0f;
            var far = who == "enemy:far";
            foreach (var e in EnemyController.All)
            {
                if (e == null || e.Dead)
                    continue;
                var d = player != null ? Vector3.Distance(player.transform.position, e.transform.position) : 0f;
                var score = far ? d : -d;
                if (best == null || score > bestScore)
                {
                    best = e;
                    bestScore = score;
                }
            }

            if (best == null)
                return null;
            targetBody = best.transform.Find("Body");
            targetHeight = best.HitPoint.y - best.transform.position.y + 0.2f;
            return best.transform;
        }

        private static void EnsureCamera()
        {
            if (camera == null)
            {
                var go = new GameObject("AnimationCaptureCamera") { hideFlags = HideFlags.DontSave };
                camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.fieldOfView = 30f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 80f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.85f, 0.85f, 0.83f, 1f);
            }

            if (texture == null)
                texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            if (readback == null)
                readback = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            camera.targetTexture = texture;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || target == null)
            {
                Stop(target == null ? "target lost" : "play mode ended");
                return;
            }

            if (Time.frameCount == lastFrame || (Time.frameCount - lastFrame) % every != 0)
                return;
            lastFrame = Time.frameCount;
            Shoot();
            if (--framesLeft <= 0)
                Stop($"done: {index} frames in {folder}");
        }

        private static void Shoot()
        {
            var look = target.position + Vector3.up * (height * 0.55f);
            var rotation = Quaternion.Euler(pitch, fixedYaw + 180f + yawOffset, 0f);
            camera.transform.position = look - rotation * Vector3.forward * distance;
            camera.transform.rotation = rotation;
            camera.ResetCullingMatrix();
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            readback.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            readback.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(folder, $"frame_{index:000}.png"), readback.EncodeToPNG());
            index++;
        }

        private static void Stop(string report)
        {
            EditorApplication.update -= Tick;
            framesLeft = 0;
            LastReport = report;
        }
    }
}
