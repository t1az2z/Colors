using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Автопрогон стопки ящиков на воксельной тестовой карте (VoxelLevels/Test, ящики cratestack_1 в коридоре x=30..31):
    /// стопка не пускает пешком и прыжком, чужое оружие не ломает, меч разбивает, после этого проход свободен.
    /// Нужен меч в руке (окно «Параметры запуска»). Меню: ColorsAction → Debug → Run Crate Playtest (в Play Mode).
    /// Итог — в <c>Logs/crate_playtest_result.txt</c> (первая строка PASS/FAIL N/M, дальше только упавшие проверки с ожидаемым и фактом).
    /// Скриншоты — в Temp/CratePlaytest.
    /// </summary>
    public static class CratePlaytest
    {
        private const string CrateId = "cratestack_1";

        private static IEnumerator routine;
        private static int waitUntil;
        private static float waitUntilTime;
        private static int lastFrame;
        private static readonly StringBuilder report = new StringBuilder();
        private static readonly PlaytestResultFile result = new PlaytestResultFile("CratePlaytest", "crate_playtest_result.txt");

        public static string LastReport { get; private set; } = "not run";

        [MenuItem("ColorsAction/Debug/Run Crate Playtest")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Crate playtest: enter Play Mode first");
                return;
            }

            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
                PlayModeStepper.Enable();
            report.Clear();
            result.Begin();
            LastReport = "running";
            routine = Sequence();
            waitUntil = 0;
            waitUntilTime = 0f;
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

            // Ожидания — в секундах игрового времени (fps редактора бывает и 60, и 600); фриз-кадры удара время стопят.
            if (Time.frameCount == lastFrame || Time.frameCount < waitUntil || Time.time < waitUntilTime)
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
                waitUntilTime = Time.time + seconds;
            if (more)
                return;
            routine = null;
            EditorApplication.update -= Tick;
            Object.FindAnyObjectByType<GameInput>()?.ClearScripted();
            result.Finish();
            var failures = result.Failures;
            LastReport = (failures == 0 ? $"CRATE PLAYTEST PASSED {result.Total}/{result.Total}\n" : $"CRATE PLAYTEST FAILED ({failures}/{result.Total})\n")
                         + report + $"result file: {result.Path}\n";
            if (failures == 0)
                Debug.Log(LastReport);
            else
                Debug.LogError(LastReport);
        }

        /// <param name="expected">Что ожидалось — пишется в файл итога у упавшей проверки (факт — в <paramref name="what"/> после «: »).</param>
        private static void Check(bool ok, string what, string expected = null)
        {
            result.Add(ok, what, expected);
            report.AppendLine((ok ? "  ok   " : "  FAIL ") + what);
        }

        private static void Shot(string name)
        {
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "CratePlaytest"));
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        }

        private static IEnumerator Sequence()
        {
            var input = Object.FindAnyObjectByType<GameInput>();
            var player = Object.FindAnyObjectByType<PlayerController>();
            var bootstrap = Object.FindAnyObjectByType<GameBootstrap>();
            var session = bootstrap.Resolver.Resolve<GameSession>();
            var cam = Camera.main.transform;

            Vector2 Dir(Vector3 world)
            {
                var local = Quaternion.Euler(0, -cam.eulerAngles.y, 0) * world;
                return new Vector2(local.x, local.z).normalized;
            }

            yield return 0.5f;
            CrateStack crate = null;
            foreach (var c in Object.FindObjectsByType<CrateStack>(FindObjectsInactive.Exclude))
                if (c.Id == CrateId)
                    crate = c;
            Check(crate != null, $"{CrateId} spawned from the voxel map: not found", "crate object exists");
            if (crate == null)
                yield break;
            var colliders = crate.GetComponentsInChildren<BoxCollider>();
            Check(colliders.Length == 1 && colliders[0].size.y >= 2f, $"one flat collider over the whole stack: {colliders.Length} collider(s), height {(colliders.Length > 0 ? colliders[0].size.y : 0f):F2}", "1 collider, height >= 2");
            Check(session.Inventory.Active?.Id == "sword", $"sword in hands (launch options): {session.Inventory.Active?.Id ?? "none"}", "sword");
            var hp = crate.Hp;
            report.AppendLine($"  info fps ~{1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):F0}, crate hp {hp}");

            // 1. Пешком не пройти: идём в стопку с юга.
            var start = new Vector3(30.5f, 0f, 3.2f);
            player.Teleport(start, 0f);
            input.ClearScripted();
            yield return 0.5f;
            Shot("1_intact");
            input.ScriptedMove = Dir(Vector3.forward);
            yield return 1.5f;
            input.ScriptedMove = null;
            yield return 0.2f;
            var z = player.transform.position.z;
            Check(z > 4f && z < 5f, $"walking is blocked at the stack: player z={z:F2} (crate face at 5.0)", "4 < z < 5");

            // 2. Прыжком не перебраться (2 ящика выше прыжка 1.5).
            input.ScriptedMove = Dir(Vector3.forward);
            input.ScriptPress(GameAction.Jump);
            yield return 1.2f;
            input.ScriptedMove = null;
            yield return 0.5f;
            z = player.transform.position.z;
            Check(z < 5f && player.transform.position.y < 1f, $"jump over is blocked: z={z:F2} y={player.transform.position.y:F2}", "z < 5 and y < 1");
            Check(Mathf.Approximately(crate.Hp, hp), $"walking and jumping do no damage: hp {crate.Hp}", $"hp {hp}");

            // 3. Чужое оружие ломать не должно.
            hp = crate.Hp;
            crate.ReceiveHit(new HitInfo { Damage = 100f, WeaponId = "shotgun", Direction = Vector3.forward, Knockback = 1f });
            crate.ReceiveHit(new HitInfo { Damage = 100f, WeaponId = "shield", Direction = Vector3.forward, Knockback = 1f });
            Check(Mathf.Approximately(crate.Hp, hp) && !crate.IsBroken, $"shotgun/shield do no damage: hp {hp} -> {crate.Hp}", $"hp {hp}, not broken");
            Check(!crate.Accepts("shotgun") && crate.Accepts("sword"), "gate: sword only", "accepts sword, rejects shotgun");

            // 4. Меч: удары ЛКМ, пока не разобьётся.
            player.Teleport(new Vector3(30.5f, 0f, 4.4f), 0f);
            yield return 0.4f;
            var swings = 0;
            var firstHitShot = false;
            while (!crate.IsBroken && swings < 12)
            {
                input.ScriptPress(GameAction.Primary);
                swings++;
                yield return 0.7f;
                if (!firstHitShot && crate.Hp < hp)
                {
                    firstHitShot = true;
                    Shot("2_after_first_hit");
                    report.AppendLine($"  info first sword hit: hp {hp} -> {crate.Hp:F1}");
                }
            }

            Check(crate.IsBroken, $"sword breaks the stack in {swings} swings: hp {crate.Hp:F1}", "broken within 12 swings");
            Check(session.BrokenCrates.Contains(CrateId), "broken stack remembered in session", $"BrokenCrates contains {CrateId}");
            yield return 0.15f;
            Shot("3_breaking");
            Check(crate.GetComponentsInChildren<BoxCollider>().Length == 0 || AllDebris(crate), "stack colliders removed", "only debris colliders left");
            yield return 1.5f;

            // 5. Проход свободен.
            player.Teleport(start, 0f);
            yield return 0.3f;
            input.ScriptedMove = Dir(Vector3.forward);
            yield return 1.5f;
            input.ScriptedMove = null;
            z = player.transform.position.z;
            Check(z > 6.5f, $"corridor is open after break: player z={z:F2}", "z > 6.5");
            yield return 0.3f;
            Shot("4_open");
            yield return 3f;
            Check(crate == null, "stack object removed after its debris faded", "crate object destroyed");
        }

        /// <summary>Остались только коллайдеры обломков (слой осколков), а не стопки.</summary>
        private static bool AllDebris(CrateStack crate)
        {
            foreach (var c in crate.GetComponentsInChildren<BoxCollider>())
                if (c.gameObject.layer != Debris.Layer)
                    return false;
            return true;
        }
    }
}
