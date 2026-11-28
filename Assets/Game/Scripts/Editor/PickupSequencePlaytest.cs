using System.Collections;
using System.IO;
using System.Text;
using ColorsAction.Inventory;
using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Автопрогон сцены подбора нового предмета (<see cref="PickupSequence"/>) на любой карте: перед игроком кладётся
    /// оружие, которого у него ещё нет, F / X — мир застывает, камера подъезжает, предмет поднимается над местом,
    /// влетает в персонажа и надевается в руки, после 5 секунд время и камера возвращаются.
    /// Меню: ColorsAction → Debug → Run Pickup Sequence Playtest (в Play Mode). Итог — <c>Logs/pickup_playtest_result.txt</c>,
    /// кадры сцены — Temp/PickupSequence_&lt;время&gt;.
    /// </summary>
    public static class PickupSequencePlaytest
    {
        private static IEnumerator routine;
        private static int waitUntil;
        private static float waitUntilReal;
        private static int lastFrame;
        private static string shotDir;
        private static readonly StringBuilder report = new StringBuilder();
        private static readonly PlaytestResultFile result = new PlaytestResultFile("PickupSequencePlaytest", "pickup_playtest_result.txt");

        public static string LastReport { get; private set; } = "not run";

        /// <summary>Какое оружие класть (null — первое из shotgun, sword, club, которого у игрока нет).</summary>
        public static string Item { get; set; }

        /// <summary>Папка кадров последнего прогона.</summary>
        public static string ShotDir => shotDir;

        [MenuItem("ColorsAction/Debug/Run Pickup Sequence Playtest")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Pickup playtest: enter Play Mode first");
                return;
            }

            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
                PlayModeStepper.Enable();
            report.Clear();
            result.Begin();
            LastReport = "running";
            shotDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp",
                "PickupSequence_" + System.DateTime.Now.ToString("HHmmss")));
            routine = Sequence();
            waitUntil = 0;
            waitUntilReal = 0f;
            lastFrame = -1;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (routine == null || !EditorApplication.isPlaying)
            {
                if (routine != null)
                {
                    result.Abort("play mode exited before the playtest finished");
                    LastReport = "aborted";
                }

                EditorApplication.update -= Tick;
                routine = null;
                return;
            }

            // Ожидания — в кадрах или в секундах реального времени: в сцене подбора игровое время стоит.
            if (Time.frameCount == lastFrame || Time.frameCount < waitUntil || Time.realtimeSinceStartup < waitUntilReal)
                return;
            lastFrame = Time.frameCount;
            bool more;
            try
            {
                more = routine.MoveNext();
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Check(false, "exception: " + e.Message, "no exception");
                more = false;
            }

            if (more && routine.Current is int frames)
                waitUntil = Time.frameCount + frames;
            if (more && routine.Current is float seconds)
                waitUntilReal = Time.realtimeSinceStartup + seconds;
            if (more)
                return;
            routine = null;
            EditorApplication.update -= Tick;
            Object.FindAnyObjectByType<GameInput>()?.ClearScripted();
            result.Finish();
            var failures = result.Failures;
            LastReport = (failures == 0 ? $"PICKUP PLAYTEST PASSED {result.Total}/{result.Total}\n" : $"PICKUP PLAYTEST FAILED ({failures}/{result.Total})\n")
                         + report + $"result file: {result.Path}\nshots: {shotDir}\n";
            if (failures == 0)
                Debug.Log(LastReport);
            else
                Debug.LogError(LastReport);
        }

        private static void Check(bool ok, string what, string expected = null)
        {
            result.Add(ok, what, expected);
            report.AppendLine((ok ? "  ok   " : "  FAIL ") + what);
        }

        /// <summary>
        /// Кадр игровой камеры в PNG: рендер в свою текстуру (ScreenCapture не пишет, пока окно редактора не в фокусе).
        /// </summary>
        private static void Shot(string name)
        {
            Directory.CreateDirectory(shotDir);
            var camera = Camera.main;
            if (camera == null)
                return;
            const int width = 1280, height = 720;
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previousTarget;
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(shotDir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        private static IEnumerator Sequence()
        {
            var input = Object.FindAnyObjectByType<GameInput>();
            var player = Object.FindAnyObjectByType<PlayerController>();
            var bootstrap = Object.FindAnyObjectByType<GameBootstrap>();
            var session = bootstrap.Resolver.Resolve<GameSession>();
            var items = bootstrap.Resolver.Resolve<ItemDatabase>();
            var interactor = player.GetComponent<PlayerInteractor>();
            var sequence = interactor != null ? interactor.Sequence : null;
            Check(sequence != null, "player has the pickup scene", "PlayerInteractor.Sequence != null");
            if (sequence == null)
                yield break;

            // Оружие, которого ещё нет: подбор — сцена.
            string id = null;
            foreach (var candidate in Item != null ? new[] { Item } : new[] { "shotgun", "sword", "club" })
                if (items.Exists(candidate) && !session.Inventory.HasWeapon(candidate))
                {
                    id = candidate;
                    break;
                }

            Check(id != null, "found a weapon the player does not have", "shotgun, sword or club not on the wheel");
            if (id == null)
                yield break;

            yield return 0.5f;
            for (int i = 0; i < 300 && !player.Grounded; i++)
                yield return 1;
            var at = player.transform.position + player.Forward * 1.1f;
            var go = new GameObject("Pickup_Playtest_" + id);
            go.transform.SetPositionAndRotation(at, Quaternion.identity);
            var pickup = go.AddComponent<Pickup>();
            pickup.Init(id);
            bootstrap.Inject(go);
            yield return 10;

            var cam = Camera.main.transform;
            var camStart = cam.position;
            var camDistance = Vector3.Distance(cam.position, player.transform.position);
            var config = sequence.Config;

            input.ScriptPress(GameAction.Interact);
            yield return 2;
            Check(PickupSequence.Playing, "F on a new weapon starts the pickup scene", "PickupSequence.Playing");
            Check(Time.timeScale == 0f, $"the world freezes: timeScale={Time.timeScale}", "0");
            Check(session.Inventory.Active?.Id != id, "the weapon is not in hands before it flies in", $"active != {id}");
            var model = sequence.Model;
            Check(model != null, "the item model is detached and driven by the scene", "Model != null");
            var modelStart = model != null ? model.position : Vector3.zero;

            // Ожидание по часам самой сцены: кадры снимаются ровно в нужные её моменты при любом fps редактора.
            bool Before(float moment) => PickupSequence.Playing && sequence.Elapsed < moment;

            while (Before(0.8f))
                yield return 1;
            Shot("1_rise");
            while (Before(config.Item.RiseEnd + 0.1f))
                yield return 1;
            Shot("2_hover");
            model = sequence.Model;
            Check(model != null && model.position.y > modelStart.y + config.Item.RiseHeight * 0.8f,
                $"the item rose: {(model != null ? model.position.y - modelStart.y : -1f):F2} m", $">= {config.Item.RiseHeight * 0.8f:F2} m");
            var closer = Vector3.Distance(cam.position, player.transform.position);
            Check(closer < camDistance * 0.7f, $"camera moved in: {camDistance:F1} → {closer:F1}", "distance < 70% of normal");
            Check(Mathf.Abs(player.Deformer.EyeOffset.y) > 0.03f, $"the player looks up at the item: eyes y={player.Deformer.EyeOffset.y:F3}", "eyes moved up");
            Check(Time.timeScale == 0f, "the world is still frozen while the item hovers", "timeScale 0");

            while (Before(config.Item.FlyStart + (config.Item.HitTime - config.Item.FlyStart) * 0.5f))
                yield return 1;
            Shot("3_fly");
            while (Before(config.Item.HitTime + 0.02f))
                yield return 1;
            Shot("4a_punch");
            while (Before(config.Item.HitTime + 0.08f))
                yield return 1;
            Shot("4_hit");
            Check(sequence.Model == null, "the item flew into the player", "Model == null after hitTime");
            Check(session.Inventory.Active?.Id == id, $"the weapon is put on automatically: active={session.Inventory.Active?.Id}", id);

            while (Before(config.Player.CheerStart + 0.35f))
                yield return 1;
            Shot("5_cheer");
            var close = Vector3.Distance(cam.position, player.transform.position);
            Check(close < config.Camera.CloseUpDistance * 1.3f, $"close-up: camera {close:F1} m from the player", $"< {config.Camera.CloseUpDistance * 1.3f:F1}");
            Check(ParticleFx.AnyPlaying(ParticleFx.PickupBurst), "the item-coloured burst plays around the player", "PickupBurst playing");
            while (Before(config.Player.CheerStart + config.Player.HopTime * 1.4f))
                yield return 1;
            Shot("6_cheer2");
            while (Before(config.Camera.ReturnStart + (config.Duration - config.Camera.ReturnStart) * 0.5f))
                yield return 1;
            Shot("7_return");

            while (PickupSequence.Playing)
                yield return 1;
            yield return 0.3f;
            Check(!PickupSequence.Playing, "the scene ends", "PickupSequence.Playing == false");
            Check(Mathf.Approximately(Time.timeScale, 1f), $"the world moves again: timeScale={Time.timeScale}", "1");
            Check(player.HasControl, "control is back", "PlayerController.HasControl");
            var back = Vector3.Distance(cam.position, camStart);
            Check(back < 0.5f, $"camera returned to its place: {back:F2} m off", "< 0.5 m");
            Shot("8_after");
            yield return 5;
        }
    }
}
