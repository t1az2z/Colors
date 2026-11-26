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

            // 6. Отскок (первый шаг серии) с неуязвимостью, затем серия: 2-й шаг и перекат через верх.
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

            var dodge1 = pc.Dodge.Steps[0];
            var rolled = Vector3.Distance(start, player.transform.position);
            Check(Mathf.Abs(rolled - dodge1.Distance) < 0.2f, $"dodge step 1 distance ~{dodge1.Distance}: {rolled:F2}");
            Check(invuln, "dodge has i-frames");
            Check(lowest <= stamina - dodge1.StaminaCost + 0.5f, $"dodge spends stamina: {stamina:F0} -> {lowest:F0}");

            // 6б. Серия: нажатия сразу после приземления (в окне комбо) ведут к следующим шагам,
            // пауза дольше окна — серия начинается заново.
            player.Teleport(new Vector3(8.5f, 0f, 5.5f), 90f);
            session.Stamina.Restore();
            yield return 20;
            var comboSteps = new List<int>();
            for (int n = 0; n < Mathf.Min(3, pc.Dodge.Steps.Count); n++)
            {
                input.ScriptPress(GameAction.Roll);
                for (int i = 0; i < 5 && !player.Rolling; i++)
                    yield return 1;
                comboSteps.Add(player.Rolling ? player.DodgeStepIndex : -1);
                for (int i = 0; i < 120 && player.Rolling; i++)
                    yield return 1;
            }

            Check(comboSteps.Count == 3 && comboSteps[0] == 0 && comboSteps[1] == 1 && comboSteps[2] == 2,
                $"dodge combo goes through steps 1-2-3 when pressed in the window: {string.Join(",", comboSteps)}");
            yield return Mathf.CeilToInt((pc.Dodge.ComboWindow + 0.4f) * 60f);
            input.ScriptPress(GameAction.Roll);
            for (int i = 0; i < 5 && !player.Rolling; i++)
                yield return 1;
            Check(player.Rolling && player.DodgeStepIndex == 0, $"dodge combo restarts after the window: step {player.DodgeStepIndex}");
            for (int i = 0; i < 120 && player.Rolling; i++)
                yield return 1;
            session.Stamina.Restore();

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
            Check(inv.Active?.Id == "sword" && !inv.HasWeapon("shield"),
                $"picked up: sword in hands, shield comes with it: active={inv.Active?.Id} wheel={inv.Weapons.Count}");

            // 8. Инвентарь (I / Select): только предметы и превью персонажа, оружие здесь не меняется.
            input.ScriptPress(GameAction.Inventory);
            yield return 5;
            Check(fsm.Current is InventoryState, "I opens inventory");
            Check(!session.PlayerControl.Value, "player control is off while inventory is open");
            var screen = ((InventoryState)fsm.Current).Screen;
            Check(screen.WeaponCaption.Value.Contains(inv.Active.Config.Name), $"preview caption shows the weapon: {screen.WeaponCaption.Value}");
            var view = Object.FindAnyObjectByType<InventoryView>();
            Check(view != null && view.Preview != null, "character preview is built");
            var yaw0 = view != null && view.Preview != null ? view.Preview.Yaw : 0f;
            input.ScriptedAim = new Vector2(1f, 0f);
            yield return 10;
            input.ScriptedAim = null;
            Check(view != null && view.Preview != null && Mathf.Abs(Mathf.DeltaAngle(yaw0, view.Preview.Yaw)) > 1f, "right stick turns the preview");
            input.ScriptPress(GameAction.Inventory);
            yield return 5;
            Check(fsm.Current is PlayState && session.PlayerControl.Value, "I closes inventory");

            // 9. Удары по манекену: серия, урон, наклон, вспышка.
            var dummy = Object.FindAnyObjectByType<TrainingDummy>();
            var dp = dummy.transform.position;
            player.Teleport(new Vector3(dp.x, dp.y, dp.z - 1.25f), 0f);
            yield return 60;
            var sawHold = false;
            var clip = 0f;
            var damageBeforeChop = 0f;
            for (int i = 0; i < 3; i++)
            {
                if (i == 2)
                    damageBeforeChop = dummy.TotalDamage;
                input.ScriptPress(GameAction.Primary);
                for (int f = 0; f < 14; f++)
                {
                    sawHold |= combat.InHold;
                    clip = Mathf.Max(clip, combat.BladePenetration(Hand.Right));
                    yield return 1;
                }
            }

            for (int f = 0; f < 90; f++)
            {
                sawHold |= combat.InHold;
                clip = Mathf.Max(clip, combat.BladePenetration(Hand.Right));
                yield return 1;
            }

            Check(dummy.TotalDamage > damageBeforeChop + 0.5f, $"third swing (chop) deals damage: {dummy.TotalDamage - damageBeforeChop:F0}");
            Check(clip < 0.02f, $"sword never passes through the cube during the combo and recovery: max depth {clip:F3}");

            Check(dummy.TotalDamage >= 30f, $"full 3-hit sword combo lands (chained through the hold window): total damage {dummy.TotalDamage:F0}");
            Check(sawHold, "each swing holds its final pose (combo window)");
            Check(Time.timeScale == 1f, "hit stop restores time scale");

            // 9б. Во время удара перекат запрещён (окно RollLockFrom..RollLockTo, по умолчанию весь удар).
            yield return 60;
            input.ScriptPress(GameAction.Primary);
            yield return 4;
            input.ScriptPress(GameAction.Roll);
            var rolledDuringAttack = false;
            for (int f = 0; f < 90; f++)
            {
                rolledDuringAttack |= player.Rolling && combat.IsBusy;
                yield return 1;
            }

            Check(!rolledDuringAttack, "no roll during an attack (roll lock window)");
            yield return 30;

            // 10. Блок щитом (левая рука, ПКМ).
            yield return 60;
            stamina = session.Stamina.Current.Value;
            input.ScriptHold(GameAction.Secondary, true);
            yield return 15;
            Check(combat.Blocking, "shield blocks while RMB is held");
            input.ScriptHold(GameAction.Secondary, false);
            yield return 5;
            Check(!combat.Blocking, "block released");

            // 10б. Фиксация на цели (СКМ / R3): лицом к цели, камера за спину, перекат вбок — через бок.
            var lockOn = player.LockOn;
            player.Teleport(new Vector3(dp.x, dp.y, dp.z - 3f), 90f);
            input.ClearScripted();
            yield return 10;
            input.ScriptPress(GameAction.LockOn);
            yield return 60;
            Vector3 ToDummy()
            {
                var v = dummy.transform.position - player.transform.position;
                v.y = 0f;
                return v.normalized;
            }

            Check(lockOn != null && lockOn.Target == dummy, $"lock-on picks the dummy: {(lockOn != null && lockOn.Target != null ? lockOn.Target.name : "none")}");
            var lockError = Vector3.Angle(player.Forward, ToDummy());
            Check(lockError < 10f, $"locked player faces the target: error {lockError:F1}");
            var camLockError = Mathf.Abs(Mathf.DeltaAngle(cam.eulerAngles.y, Mathf.Atan2(ToDummy().x, ToDummy().z) * Mathf.Rad2Deg));
            Check(camLockError < 15f, $"free camera turns behind the player towards the target: error {camLockError:F1}");

            var toTarget = ToDummy();
            var side = Vector3.Cross(Vector3.up, toTarget);
            var rollStart = player.transform.position;
            var facingBefore = player.Forward;
            input.ScriptedMove = Dir(side);
            input.ScriptPress(GameAction.Roll);
            var facingDrift = 0f;
            var sideways = false;
            var rollEnd = rollStart;
            var sawRoll = false;
            for (int i = 0; i < 60; i++)
            {
                yield return 1;
                if (player.Rolling)
                {
                    // Направление переката задаётся в момент нажатия; дальше стик отпускаем,
                    // чтобы шаги после переката (камера доворачивается — стрейф идёт по дуге) не смешивались с ним.
                    input.ScriptedMove = null;
                    sawRoll = true;
                    facingDrift = Mathf.Max(facingDrift, Vector3.Angle(facingBefore, player.Forward));
                    sideways |= player.RollLocalDirection.x > 0.9f;
                    rollEnd = player.transform.position;
                }
                else if (sawRoll)
                {
                    break;
                }
            }

            input.ScriptedMove = null;
            var rollMove = rollEnd - rollStart;
            rollMove.y = 0f;
            Check(sideways && facingDrift < 5f,
                $"locked side roll goes over the cube's side, facing kept: local dir x>0.9 {sideways}, facing drift {facingDrift:F1}");
            Check(rollMove.magnitude > 1f && Mathf.Abs(Vector3.Dot(rollMove, toTarget)) < rollMove.magnitude * 0.3f,
                $"locked side roll moves sideways relative to the target: {rollMove.magnitude:F2} m, towards target {Vector3.Dot(rollMove, toTarget):F2}");
            yield return 30;
            lockError = Vector3.Angle(player.Forward, ToDummy());
            Check(lockError < 10f, $"after the roll the player turns back to the target: error {lockError:F1}");
            input.ScriptPress(GameAction.LockOn);
            yield return 3;
            // Диагональный ввод — перекат всё равно через грань (вперёд/назад/вбок), не через ребро.
            input.ScriptPress(GameAction.LockOn);
            yield return 40;
            var diagonal = (ToDummy() + Vector3.Cross(Vector3.up, ToDummy())).normalized;
            input.ScriptedMove = Dir(-diagonal);
            input.ScriptPress(GameAction.Roll);
            var axisRoll = true;
            var sawDiagonalRoll = false;
            for (int i = 0; i < 40; i++)
            {
                yield return 1;
                if (!player.Rolling)
                    continue;
                input.ScriptedMove = null;
                sawDiagonalRoll = true;
                var l = player.RollLocalDirection;
                axisRoll &= Mathf.Abs(Mathf.Abs(l.x) + Mathf.Abs(l.z) - 1f) < 0.01f && (Mathf.Abs(l.x) < 0.01f || Mathf.Abs(l.z) < 0.01f);
            }

            input.ScriptedMove = null;
            Check(sawDiagonalRoll && axisRoll, "locked diagonal input rolls over a face, never over an edge");
            yield return 30;
            input.ScriptPress(GameAction.LockOn);
            yield return 3;
            Check(!lockOn.Active, "second press releases the lock");

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

            // 14. Враги: агро с "!", замах, удар по игроку, айфреймы; атакует только один из трёх.
            var director = bootstrap.Resolver.Resolve<ColorsAction.Combat.CombatDirector>();
            // Только враги исходной площадки 64×64: тир с дробовиком (z >= 64) далеко и в проверку не входит.
            var enemies = new System.Collections.Generic.List<EnemyController>();
            foreach (var e in EnemyController.All)
                if (e != null && e.transform.position.z < 63f)
                    enemies.Add(e);
            Check(enemies.Count == 3, $"three enemies on the main area: {enemies.Count}");
            if (enemies.Count > 0)
            {
                session.ControlScheme.Value = ColorsAction.Configs.CameraScheme.FreeOrbit;
                var first = enemies[0];
                var ep = first.transform.position;
                player.Teleport(new Vector3(ep.x, ep.y, ep.z - 3.2f), 0f);
                input.ClearScripted();
                var hpBefore = session.Vitals.Health.Current.Value;
                var sawAlert = false;
                var sawWindup = false;
                var maxStriking = 0;
                var windupFrames = 0;
                var hitFrame = -1;
                var minChaseDistance = float.MaxValue;
                var invulnerableAfterHit = false;
                for (int f = 0; f < 60 * 7; f++)
                {
                    sawAlert |= first.Brain.State == ColorsAction.Combat.EnemyState.Alerted;
                    if (first.Brain.State == ColorsAction.Combat.EnemyState.Chase)
                    {
                        var d = first.transform.position - player.transform.position;
                        d.y = 0f;
                        minChaseDistance = Mathf.Min(minChaseDistance, d.magnitude);
                    }

                    foreach (var e in enemies)
                        if (e != null && e.Brain.State == ColorsAction.Combat.EnemyState.Windup)
                            windupFrames++;
                    sawWindup |= windupFrames > 0;
                    var striking = 0;
                    foreach (var e in enemies)
                        if (e != null && e.Brain.State == ColorsAction.Combat.EnemyState.Strike)
                            striking++;
                    maxStriking = Mathf.Max(maxStriking, striking);
                    if (hitFrame < 0 && session.Vitals.Health.Current.Value < hpBefore)
                    {
                        hitFrame = f;
                        invulnerableAfterHit = session.Vitals.InvulnerableTime > 1.5f;
                    }
                    yield return 1;
                }

                Check(sawAlert, "enemy notices the player and shows the alert");
                if (minChaseDistance < float.MaxValue)
                    Check(minChaseDistance >= first.Brain.Config.ApproachDistance - 0.35f,
                        $"chasing enemy stops at a short distance, not point-blank: min {minChaseDistance:F2} (approach {first.Brain.Config.ApproachDistance:F2})");
                Check(sawWindup && windupFrames >= 60, $"attack has a long readable wind-up: {windupFrames / 60f:F1} s of wind-up seen");
                Check(maxStriking <= 1, $"two enemies never strike at the same time: {maxStriking}");
                Check(hitFrame >= 0, $"enemy hits the player: hp {hpBefore:F0} -> {session.Vitals.Health.Current.Value:F0}");
                if (hitFrame >= 0)
                    Check(invulnerableAfterHit, "player gets ~2 s of i-frames after a hit");

                // 15. Бьём врага мечом до смерти: цифры урона, осколки летят к игроку как опыт.
                var xpBefore = session.Experience.Value;
                var target = first;
                for (int i = 0; i < 40 && target != null && !target.Dead; i++)
                {
                    var to = target.transform.position - player.transform.position;
                    to.y = 0;
                    player.Teleport(target.transform.position - to.normalized * 1.3f, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                    input.ScriptPress(GameAction.Primary);
                    yield return 20;
                }

                Check(target == null || target.Dead, "enemy dies from sword hits");
                yield return 150;
                Check(session.Experience.Value > xpBefore, $"shards fly to the player as experience: {xpBefore} -> {session.Experience.Value}");

                // 16. Парирование: блок щитом в окно парирования — вспышка, оружие отскакивает, враг стоит ~1 с.
                EnemyController foe = null;
                foreach (var e in enemies)
                    if (e != null && !e.Dead)
                    {
                        foe = e;
                        break;
                    }

                if (foe != null)
                {
                    var fp = foe.transform.position;
                    var toFoe = new Vector3(0f, 0f, 1f);
                    player.Teleport(fp - toFoe * 1.6f, 0f);
                    input.ClearScripted();
                    var parriesBefore = combat.ParryCount;
                    var parried = false;
                    var flashSeen = false;
                    for (int f = 0; f < 60 * 8 && !parried; f++)
                    {
                        // Бьёт тот, у кого право атаки; блок ставим лицом к нему ровно в окно парирования.
                        EnemyController striker = null;
                        foreach (var e in EnemyController.All)
                            if (e != null && !e.Dead && e.Brain.InParryWindow)
                                striker = e;
                        if (striker != null)
                        {
                            foe = striker;
                            var face = foe.transform.position - player.transform.position;
                            face.y = 0f;
                            player.Teleport(player.transform.position, Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg);
                            input.ScriptHold(GameAction.Secondary, true);
                            yield return 2;
                            parried = foe.Brain.State == ColorsAction.Combat.EnemyState.Parried;
                            // Вспышка — в момент столкновения: враг ещё доводит удар до середины (ParryContactTime).
                            for (int w = 0; w < 20 && !flashSeen; w++)
                            {
                                flashSeen = ParticleFx.AnyPlaying(ParticleFx.HitFlash);
                                if (!flashSeen)
                                    yield return 1;
                            }

                            break;
                        }

                        yield return 1;
                    }

                    input.ScriptHold(GameAction.Secondary, false);
                    Check(parried && combat.ParryCount > parriesBefore, $"block in the parry window parries the attack: {foe.Brain.State}");
                    Check(flashSeen, "parry shows a flash");
                    if (parried)
                    {
                        yield return 35;
                        Check(foe.Brain.State == ColorsAction.Combat.EnemyState.Parried, "parried enemy stands with the weapon lowered");
                        yield return Mathf.CeilToInt(foe.Brain.Config.ParriedDuration * 60f);
                        Check(foe.Brain.State != ColorsAction.Combat.EnemyState.Parried, "after the stun the enemy fights again");
                    }
                }
            }

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
