using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Автопрогон разводного моста на карте VoxelLevels/BridgeTest (коридор, мост drawbridge_1 от x=4 по +X, дальняя
    /// створка на шарнире x=8, под ней поперёк коридора — пропасть x=6..7): поднятая створка не пускает пешком и прыжком, меч цепь не рвёт, фиксация
    /// берёт цепь, выстрел из дробовика её рвёт, створка падает, по мосту можно пройти над пропастью.
    /// Нужен дробовик в руке (окно «Параметры запуска»). Меню: ColorsAction → Debug → Run Bridge Playtest (в Play Mode).
    /// Скриншоты — в Temp/BridgePlaytest.
    /// </summary>
    public static class BridgePlaytest
    {
        private const string BridgeId = "drawbridge_1";

        private static IEnumerator routine;
        private static int waitUntil;
        private static float waitUntilTime;
        private static int lastFrame;
        private static readonly StringBuilder report = new StringBuilder();
        private static int failures;

        public static string LastReport { get; private set; } = "not run";

        [MenuItem("ColorsAction/Debug/Run Bridge Playtest")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Bridge playtest: enter Play Mode first");
                return;
            }

            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
                PlayModeStepper.Enable();
            report.Clear();
            failures = 0;
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
                Check(false, "exception: " + e.Message);
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
            LastReport = (failures == 0 ? "BRIDGE PLAYTEST PASSED\n" : $"BRIDGE PLAYTEST FAILED ({failures})\n") + report;
            if (failures == 0)
                Debug.Log(LastReport);
            else
                Debug.LogError(LastReport);
        }

        private static void Check(bool ok, string what)
        {
            if (!ok)
                failures++;
            report.AppendLine((ok ? "  ok   " : "  FAIL ") + what);
        }

        private static void Shot(string name)
        {
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "BridgePlaytest"));
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
            DrawBridge bridge = null;
            foreach (var b in Object.FindObjectsByType<DrawBridge>(FindObjectsInactive.Exclude))
                if (b.Id == BridgeId)
                    bridge = b;
            Check(bridge != null, $"{BridgeId} spawned from the voxel map");
            if (bridge == null)
                yield break;
            var chain = bridge.Chain;
            Check(chain != null && !chain.Broken && chain.Lockable, "chain is intact and lockable");
            Check(Mathf.Approximately(bridge.LeafAngle, bridge.Config.RaisedAngle), $"far leaf is raised: {bridge.LeafAngle:F0}°");
            Check(session.Inventory.Right?.Id == "shotgun", "shotgun in hand (launch options)");
            report.AppendLine($"  info fps ~{1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):F0}, chain length {chain?.Length:F2}");

            // 1. Пешком не пройти: впереди пропасть и поднятая створка (шарнир на x=8).
            var start = new Vector3(1.5f, 0f, 3f);
            player.Teleport(start, 90f);
            input.ClearScripted();
            yield return 0.6f;
            Shot("1_raised");
            input.ScriptedMove = Dir(Vector3.right);
            yield return 2.5f;
            input.ScriptedMove = null;
            yield return 0.2f;
            var x = player.transform.position.x;
            Check(x < 7.6f, $"walking does not get past the raised leaf: player x={x:F2} y={player.transform.position.y:F2} (hinge at 8.0)");

            // 2. Прыжком с края ближней створки не перебраться.
            player.Teleport(new Vector3(5.6f, 0f, 3f), 90f);
            yield return 0.3f;
            input.ScriptedMove = Dir(Vector3.right);
            input.ScriptPress(GameAction.Jump);
            yield return 1.2f;
            input.ScriptedMove = null;
            yield return 0.5f;
            x = player.transform.position.x;
            Check(x < 7.6f, $"jump over is blocked: x={x:F2} y={player.transform.position.y:F2}");

            // 3. Меч цепь не рвёт, только раскачивает.
            chain.ReceiveHit(new HitInfo { Damage = 100f, WeaponId = "sword", Direction = Vector3.right, Knockback = 1f });
            Check(!chain.Broken && !chain.Accepts("sword") && chain.Accepts("shotgun"), "gate: shotgun only");

            // 4. Фиксация берёт цепь.
            player.Teleport(new Vector3(4.8f, 0f, 3f), 90f);
            yield return 0.4f;
            var lockOn = player.LockOn;
            lockOn.Release();
            input.ScriptPress(GameAction.LockOn);
            yield return 0.3f;
            Check(lockOn.Target == chain, $"lock-on picks the chain: {(lockOn.Target != null ? lockOn.Target.name : "none")}");
            Shot("2_locked");

            // 5. Выстрел: цепь рвётся, мост запоминается опущенным, створка падает.
            var shots = 0;
            while (!chain.Broken && shots < 4)
            {
                input.ScriptPress(GameAction.Primary);
                shots++;
                yield return 0.8f;
            }

            Check(chain == null || chain.Broken, $"shotgun snaps the chain in {shots} shot(s)");
            Check(session.OpenedBridges.Contains(BridgeId), "lowered bridge remembered in session");
            Check(!lockOn.Active, "lock-on released after the chain snapped");
            yield return 0.25f;
            Shot("3_falling");
            yield return 2.5f;
            Check(Mathf.Approximately(bridge.LeafAngle, 0f) && !bridge.Falling, $"far leaf lies flat: {bridge.LeafAngle:F1}°");
            Shot("4_lowered");

            // 6. По мосту — над пропастью на ту сторону (под рамой).
            player.Teleport(start, 90f);
            yield return 0.3f;
            input.ScriptedMove = Dir(Vector3.right);
            yield return 3f;
            input.ScriptedMove = null;
            yield return 0.3f;
            var p = player.transform.position;
            Check(p.x > 8.3f && p.y > -0.3f, $"crossed the bridge over the pit: x={p.x:F2} y={p.y:F2}");
            Shot("5_crossed");
        }
    }
}
