using System.Collections;
using System.Collections.Generic;
using System.Text;
using ColorsAction.Inventory;
using ColorsAction.States;
using UnityEditor;
using UnityEngine;

namespace ColorsAction.Unity.Editor
{
    /// <summary>
    /// Автопрогон прототипа в Play Mode через скриптовый ввод: ступени 0.25/0.5/0.75, прыжок, перекат,
    /// подбор, инвентарь, удары по манекену, блок. Работает и без фокуса окна (через <see cref="PlayModeStepper"/>).
    /// Меню: ColorsAction → Debug → Run Prototype Playtest (в Play Mode). Итог — в консоли.
    /// </summary>
    public static class PrototypePlaytest
    {
        private static IEnumerator routine;
        private static int lastFrame;
        private static int waitUntil;
        private static readonly StringBuilder report = new StringBuilder();
        private static int failures;

        public static string LastReport { get; private set; } = "not run";
        public static bool Running => routine != null;

        [MenuItem("ColorsAction/Debug/Run Prototype Playtest")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Playtest: enter Play Mode first");
                return;
            }

            if (!UnityEditorInternal.InternalEditorUtility.isApplicationActive)
                PlayModeStepper.Enable();
            report.Clear();
            failures = 0;
            LastReport = "running";
            routine = Sequence();
            lastFrame = -1;
            waitUntil = 0;
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

            if (Time.frameCount == lastFrame || Time.frameCount < waitUntil)
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
            if (!more)
            {
                routine = null;
                EditorApplication.update -= Tick;
                var input = Object.FindAnyObjectByType<GameInput>();
                input?.ClearScripted();
                LastReport = (failures == 0 ? "PLAYTEST PASSED\n" : $"PLAYTEST FAILED ({failures})\n") + report;
                if (failures == 0)
                    Debug.Log(LastReport);
                else
                    Debug.LogError(LastReport);
            }
        }

        private static void Check(bool ok, string what)
        {
            if (!ok)
                failures++;
            report.AppendLine((ok ? "  ok   " : "  FAIL ") + what);
        }

        private static IEnumerator Sequence()
        {
            var input = Object.FindAnyObjectByType<GameInput>();
            var player = Object.FindAnyObjectByType<PlayerController>();
            var combat = player.GetComponent<PlayerCombat>();
            var bootstrap = Object.FindAnyObjectByType<GameBootstrap>();
            var session = bootstrap.Resolver.Resolve<GameSession>();
            var fsm = bootstrap.Host.States;
            var cam = Camera.main.transform;
            var pc = player.Config;
            // Шаги 1–10 — в схеме А: в схеме Б взгляд шёл бы за настоящей мышью и мешал проверкам.
            var startScheme = session.ControlScheme.Value;
            report.AppendLine($"  info start scheme: {startScheme}");
            session.ControlScheme.Value = ColorsAction.Configs.CameraScheme.FreeOrbit;

            Vector2 Dir(Vector3 world)
            {
                var local = Quaternion.Euler(0, -cam.eulerAngles.y, 0) * world;
                return new Vector2(local.x, local.z).normalized;
            }

            // 1. Ступень 0.25 — проходится шагом.
            player.Teleport(new Vector3(7.5f, 0f, 18.2f), 0f);
            input.ClearScripted();
            yield return 20;
            input.ScriptedMove = Dir(Vector3.forward);
            yield return Frames(2.2f, pc.WalkSpeed);
            input.ScriptedMove = null;
            yield return 20;
            Check(Mathf.Abs(player.transform.position.y - 0.25f) < 0.03f && player.Grounded,
                $"0.25 step walkable: y={player.transform.position.y:F3}");

            // 2. Ступень 1.0 без прыжка — упор.
            player.Teleport(new Vector3(12.5f, 0f, 18.2f), 0f);
            input.ClearScripted();
            yield return 20;
            input.ScriptedMove = Dir(Vector3.forward);
            yield return 45;
            Check(player.transform.position.y < 0.05f && player.transform.position.z < 19.6f,
                $"1.0 step blocks walking: y={player.transform.position.y:F3} z={player.transform.position.z:F2}");

            // 3. Ступень 1.0 с прыжком.
            input.ClearScripted();
            player.Teleport(new Vector3(12.5f, 0f, 18.6f), 0f);
            yield return 20;
            var peak = 0f;
            input.ScriptPress(GameAction.Jump);
            input.ScriptedMove = Dir(Vector3.forward);
            for (int i = 0; i < 70; i++)
            {
                peak = Mathf.Max(peak, player.transform.position.y);
                if (player.transform.position.z > 21f)
                    input.ScriptedMove = null;
                yield return 1;
            }

            Check(Mathf.Abs(player.transform.position.y - 1f) < 0.03f,
                $"1.0 step reachable with a jump: y={player.transform.position.y:F3}");

            // 4. Высота прыжка на ровном месте.
            player.Teleport(new Vector3(10.5f, 0f, 5.5f), 0f);
            input.ClearScripted();
            yield return 20;
            peak = 0f;
            input.ScriptPress(GameAction.Jump);
            for (int i = 0; i < 80; i++)
            {
                peak = Mathf.Max(peak, player.transform.position.y);
                yield return 1;
            }

            Check(Mathf.Abs(peak - pc.JumpHeight) < 0.05f, $"jump apex ~{pc.JumpHeight}: {peak:F3}");

            // 5. Ступень 2.0 — не запрыгнуть.
            player.Teleport(new Vector3(17.5f, 0f, 18.6f), 0f);
            input.ClearScripted();
            yield return 20;
            input.ScriptPress(GameAction.Jump);
            input.ScriptedMove = Dir(Vector3.forward);
            for (int i = 0; i < 80; i++)
            {
                if (player.transform.position.z > 21f)
                    input.ScriptedMove = null;
                yield return 1;
            }

            Check(player.transform.position.y < 0.05f, $"2.0 step not jumpable: y={player.transform.position.y:F3}");

            // 6. Перекат на 1.5 с неуязвимостью.
            player.Teleport(new Vector3(8.5f, 0f, 5.5f), 90f);
            input.ClearScripted();
            yield return 20;
            var start = player.transform.position;
            var stamina = session.Stamina.Current.Value;
            input.ScriptPress(GameAction.Roll);
            var invuln = false;
            var lowest = stamina;
            for (int i = 0; i < 60; i++)
            {
                invuln |= player.Invulnerable;
                lowest = Mathf.Min(lowest, session.Stamina.Current.Value);
                yield return 1;
            }

            var rolled = Vector3.Distance(start, player.transform.position);
            Check(Mathf.Abs(rolled - pc.RollDistance) < 0.2f, $"roll distance ~{pc.RollDistance}: {rolled:F2}");
            Check(invuln, "roll has i-frames");
            Check(lowest <= stamina - pc.Stamina.RollCost + 0.5f, $"roll spends stamina: {stamina:F0} -> {lowest:F0}");

            // 7. Подбор меча и щита на F.
            player.Teleport(new Vector3(12.5f, 0f, 12.7f), 0f);
            input.ClearScripted();
            yield return 20;
            input.ScriptPress(GameAction.Interact);
            yield return 10;
            player.Teleport(new Vector3(15.5f, 0f, 11.7f), 0f);
            input.ClearScripted();
            yield return 20;
            input.ScriptPress(GameAction.Interact);
            yield return 30;
            var inv = session.Inventory;
            Check(inv.Right?.Id == "sword" && inv.Left?.Id == "shield",
                $"picked up and auto-equipped: right={inv.Right?.Id} left={inv.Left?.Id}");

            // 8. Инвентарь: TAB, надеть оружие с геймпада (A), закрыть.
            input.ScriptPress(GameAction.Inventory);
            yield return 5;
            Check(fsm.Current is InventoryState, "TAB opens inventory");
            Check(!session.PlayerControl.Value, "player control is off while inventory is open");
            var screen = ((InventoryState)fsm.Current).Screen;
            // Снять оба (A на руке), чтобы проверить экипировку из сетки.
            screen.Cursor.Value = SlotRef.Of(Hand.Right);
            input.ScriptPress(GameAction.Submit);
            yield return 3;
            screen.Cursor.Value = SlotRef.Of(Hand.Left);
            input.ScriptPress(GameAction.Submit);
            yield return 3;
            Check(inv.Right == null && inv.Left == null, "A on a hand slot unequips into the grid");
            screen.Cursor.Value = inv.Find(FindItem(inv, "sword")).Value;
            Check(!screen.IsHighlighted(SlotRef.Of(Hand.Left)) && screen.IsHighlighted(SlotRef.Of(Hand.Right)),
                "sword under cursor highlights only the right hand");
            Check(!inv.Move(screen.Cursor.Value, SlotRef.Of(Hand.Left)), "sword can't be put into the left hand");
            input.ScriptPress(GameAction.Submit);
            yield return 3;
            screen.Cursor.Value = inv.Find(FindItem(inv, "shield")).Value;
            input.ScriptPress(GameAction.Submit);
            yield return 3;
            Check(inv.Right?.Id == "sword" && inv.Left?.Id == "shield",
                $"equipped: right={inv.Right?.Id} left={inv.Left?.Id}");
            input.ScriptPress(GameAction.Inventory);
            yield return 5;
            Check(fsm.Current is PlayState && session.PlayerControl.Value, "TAB closes inventory");

            // 9. Удары по манекену: серия, урон, наклон, вспышка.
            var dummy = Object.FindAnyObjectByType<TrainingDummy>();
            var dp = dummy.transform.position;
            player.Teleport(new Vector3(dp.x, dp.y, dp.z - 1.25f), 0f);
            yield return 60;
            var sawHold = false;
            for (int i = 0; i < 3; i++)
            {
                input.ScriptPress(GameAction.Primary);
                for (int f = 0; f < 14; f++)
                {
                    sawHold |= combat.InHold;
                    yield return 1;
                }
            }

            for (int f = 0; f < 40; f++)
            {
                sawHold |= combat.InHold;
                yield return 1;
            }

            Check(dummy.TotalDamage >= 35f, $"full 3-hit sword combo lands (chained through the hold window): total damage {dummy.TotalDamage:F0}");
            Check(sawHold, "each swing holds its final pose (combo window)");
            Check(Time.timeScale == 1f, "hit stop restores time scale");

            // 10. Блок щитом (левая рука, ПКМ).
            yield return 60;
            stamina = session.Stamina.Current.Value;
            input.ScriptHold(GameAction.Secondary, true);
            yield return 15;
            Check(combat.Blocking, "shield blocks while RMB is held");
            input.ScriptHold(GameAction.Secondary, false);
            yield return 5;
            Check(!combat.Blocking, "block released");

            // 11. Вторая схема: 4 угла камеры на Q/E (LB/RB), правый стик задаёт взгляд.
            session.ControlScheme.Value = ColorsAction.Configs.CameraScheme.FixedAngles;
            player.Teleport(new Vector3(10.5f, 0f, 5.5f), 0f);
            input.ClearScripted();
            yield return 30;
            var fixedYaw = Mathf.DeltaAngle(45f, cam.eulerAngles.y);
            Check(Mathf.Abs(Mathf.Repeat(fixedYaw + 45f, 90f) - 45f) < 1f, $"scheme B snaps camera to an iso angle: {cam.eulerAngles.y:F1}");
            var yawBefore = cam.eulerAngles.y;
            input.ScriptPress(GameAction.CameraRight);
            yield return 8;
            var midTurn = Mathf.Abs(Mathf.DeltaAngle(yawBefore, cam.eulerAngles.y));
            yield return 10;
            var turned = Mathf.Abs(Mathf.DeltaAngle(yawBefore, cam.eulerAngles.y));
            Check(midTurn > 20f && midTurn < 80f && Mathf.Abs(turned - 90f) < 1f,
                $"Q/E (LB/RB) turns the camera 90 in ~0.25 s: half-way {midTurn:F0}, after 0.3 s {turned:F1}");
            // Идём "вперёд" по экрану, а смотрим вправо по стику.
            input.ScriptedMove = new Vector2(0f, 1f);
            input.ScriptedAim = new Vector2(1f, 0f);
            yield return 30;
            var expected = Quaternion.Euler(0, cam.eulerAngles.y, 0) * Vector3.right;
            var aimError = Vector3.Angle(player.Forward, expected);
            Check(aimError < 10f, $"right stick sets facing independent of movement: error {aimError:F1}");
            input.ClearScripted();

            // 12. Переключение схем в игре (F1 / Start).
            input.ScriptPress(GameAction.SwitchScheme);
            yield return 3;
            Check(session.ControlScheme.Value == ColorsAction.Configs.CameraScheme.FreeOrbit, "F1 switches back to scheme A");

            // 13. Слоп из ступенек по 0.25: подъём без рывков (высота меняется плавно каждый кадр).
            session.ControlScheme.Value = ColorsAction.Configs.CameraScheme.FreeOrbit;
            player.Teleport(new Vector3(27.5f, 0f, 1.6f), 0f);
            input.ClearScripted();
            yield return 20;
            input.ScriptedMove = Dir(Vector3.forward);
            var maxJump = 0f;
            var lastY = player.transform.position.y;
            var climbFrames = Frames(12f, pc.WalkSpeed);
            for (int i = 0; i < climbFrames; i++)
            {
                yield return 1;
                var y = player.transform.position.y;
                maxJump = Mathf.Max(maxJump, Mathf.Abs(y - lastY));
                lastY = y;
            }

            input.ClearScripted();
            Check(lastY > 2.5f && maxJump < 0.06f,
                $"0.25 staircase is a smooth slope: reached y={lastY:F2}, max height change per frame {maxJump:F3}");

            session.ControlScheme.Value = startScheme;
        }

        private static int Frames(float meters, float speed) => Mathf.CeilToInt(meters / Mathf.Max(0.1f, speed) * 60f);

        private static Item FindItem(PlayerInventory inv, string id)
        {
            foreach (var slot in inv.AllSlots())
            {
                var item = inv.Get(slot);
                if (item != null && item.Id == id)
                    return item;
            }

            return null;
        }
    }
}
