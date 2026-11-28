using System;
using ColorsAction.Animation;
using ColorsAction.Configs;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>
    /// Процедурная анимация кубов (Animations.json): шаги со сжатием-вытягиванием, сохранение объёма, дыхание,
    /// моргание, осмотр по сторонам игрока, верчение оружия врага и прерывание всего покоя действием.
    /// </summary>
    public class CubeAnimationTests
    {
        private const float Dt = 1f / 60f;

        private static ConfigTable<CubeAnimationConfig> Table()
        {
            var configs = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            return configs.LoadTable<CubeAnimationConfig>(CubeAnimationConfig.Key);
        }

        private static CubeAnimationConfig Player() =>
            CubeAnimationConfig.Resolve(Table(), CubeAnimationConfig.PlayerId, CubeAnimationConfig.PlayerId);

        private static CubeAnimationConfig Enemy() =>
            CubeAnimationConfig.Resolve(Table(), "club_cube", CubeAnimationConfig.EnemyId);

        private static void Run(CubeIdleAnimator idle, float seconds, bool standing, bool action = false, Action<CubeIdleAnimator> each = null)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                idle.Tick(Dt, standing, action);
                each?.Invoke(idle);
            }
        }

        [Test]
        public void AnimationsJson_Loads_PlayerLooksAround_EnemyInspects()
        {
            var player = Player();
            var enemy = Enemy();
            Assert.That(player.LookAround, Is.Not.Null, "у игрока есть осмотр по сторонам");
            Assert.That(player.Inspect, Is.Null);
            Assert.That(enemy.Inspect, Is.Not.Null, "тип без своей записи берёт общую запись enemy — с верчением оружия");
            Assert.That(enemy.LookAround, Is.Null);
            Assert.That(player.Move.RunSquash, Is.GreaterThan(player.Move.Squash), "на бегу сжатие сильнее, чем шагом");
            Assert.That(player.Breath.Amount, Is.GreaterThan(0f));
        }

        [Test]
        public void Resolve_WithoutFile_GivesDefaults()
        {
            var player = CubeAnimationConfig.Resolve(null, CubeAnimationConfig.PlayerId, CubeAnimationConfig.PlayerId);
            var enemy = CubeAnimationConfig.Resolve(null, "anything", CubeAnimationConfig.EnemyId);
            Assert.That(player.LookAround, Is.Not.Null);
            Assert.That(enemy.Inspect, Is.Not.Null);
        }

        [TestCase(-0.5f)]
        [TestCase(-0.2f)]
        [TestCase(0f)]
        [TestCase(0.08f)]
        [TestCase(0.4f)]
        public void SquashStretch_KeepsVolume(float squash)
        {
            Assert.That(CubeShape.Volume(squash), Is.EqualTo(1f).Within(1e-4f));
            if (squash < 0f)
                Assert.That(CubeShape.WidthScale(squash), Is.GreaterThan(1f), "сплющенный куб шире");
            if (squash > 0f)
                Assert.That(CubeShape.WidthScale(squash), Is.LessThan(1f), "вытянутый куб уже");
        }

        [Test]
        public void StepCycle_SquashesOnStep_StretchesBetween_StrongerWhenRunning()
        {
            var move = Player().Move;
            float Extremes(float speed, out float max)
            {
                var steps = new StepCycle(move);
                float min = 0f;
                max = 0f;
                for (int i = 0; i < 240; i++)
                {
                    steps.Tick(Dt, speed, 6.4f, 9.6f, true);
                    if (i < 30)
                        continue; // проявление после старта
                    min = Math.Min(min, steps.Squash);
                    max = Math.Max(max, steps.Squash);
                }

                return min;
            }

            var walkMin = Extremes(6.4f, out var walkMax);
            var runMin = Extremes(9.6f, out var runMax);
            Assert.That(walkMin, Is.EqualTo(-move.Squash).Within(0.005f), "на шаге сжат на squash");
            Assert.That(walkMax, Is.EqualTo(move.Stretch).Within(0.005f), "между шагами вытянут на stretch");
            Assert.That(runMin, Is.EqualTo(-move.RunSquash).Within(0.005f));
            Assert.That(runMax, Is.EqualTo(move.RunStretch).Within(0.005f));

            var cycle = new StepCycle(move);
            Assert.That(cycle.StepRate(9.6f, 6.4f, 9.6f), Is.GreaterThan(cycle.StepRate(6.4f, 6.4f, 9.6f)), "на бегу шаги чаще");
        }

        [Test]
        public void StepCycle_SquashAtSwayExtreme_StretchAtMiddle()
        {
            var steps = new StepCycle(Player().Move);
            for (int i = 0; i < 400; i++)
            {
                steps.Tick(Dt, 6.4f, 6.4f, 9.6f, true);
                if (i < 30)
                    continue;
                var sway = Math.Abs((float)Math.Sin(steps.Phase));
                if (sway > 0.98f)
                    Assert.That(steps.Squash, Is.LessThan(0f), "в крайней точке покачивания — шаг, куб сжат");
                if (sway < 0.1f)
                    Assert.That(steps.Squash, Is.GreaterThan(0f), "посередине — между шагами, куб вытянут");
            }
        }

        [Test]
        public void StepCycle_FadesOut_WhenStoppedOrAirborne()
        {
            var move = Player().Move;
            var steps = new StepCycle(move);
            for (int i = 0; i < 60; i++)
                steps.Tick(Dt, 9.6f, 6.4f, 9.6f, true);
            for (int i = 0; i < 30; i++)
                steps.Tick(Dt, 9.6f, 6.4f, 9.6f, false);
            Assert.That(steps.Squash, Is.EqualTo(0f).Within(1e-5f), "в воздухе шагов нет");
            Assert.That(steps.Sway, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Breath_AppearsStanding_StaysWithinAmount_FadesOnMove()
        {
            var config = Player();
            var idle = new CubeIdleAnimator(config, 1);
            float max = 0f, min = 0f;
            Run(idle, config.Breath.Period * 2f, true, false, a =>
            {
                max = Math.Max(max, a.BreathSquash);
                min = Math.Min(min, a.BreathSquash);
            });
            Assert.That(max, Is.GreaterThan(config.Breath.Amount * 0.9f), "вдох — вытянут");
            Assert.That(min, Is.LessThan(-config.Breath.Amount * 0.9f), "выдох — осел");
            Assert.That(max, Is.LessThanOrEqualTo(config.Breath.Amount + 1e-5f));

            Run(idle, config.Breath.FadeTime, false);
            Assert.That(idle.BreathSquash, Is.EqualTo(0f).Within(1e-5f), "пошёл — дыхание погасло");
        }

        [Test]
        public void BreathWave_IsContinuous_PeaksAtHalfInhale()
        {
            const float inhale = 0.42f;
            Assert.That(CubeIdleAnimator.BreathWave(0f, inhale), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(CubeIdleAnimator.BreathWave(inhale * 0.5f, inhale), Is.EqualTo(1f).Within(1e-4f));
            // Дно выдоха — за полвдоха до конца периода: от дна до пика вдоха проходит ровно доля inhale.
            Assert.That(CubeIdleAnimator.BreathWave(1f - inhale * 0.5f, inhale), Is.EqualTo(-1f).Within(1e-4f));
            var prev = CubeIdleAnimator.BreathWave(0f, inhale);
            for (float u = 0.001f; u <= 2f; u += 0.001f)
            {
                var v = CubeIdleAnimator.BreathWave(u, inhale);
                Assert.That(Math.Abs(v - prev), Is.LessThan(0.03f), $"скачок на {u}");
                prev = v;
            }
        }

        [Test]
        public void Blink_HappensWhileStanding_ActionOpensEyes()
        {
            var config = Player();
            var idle = new CubeIdleAnimator(config, 7);
            var blinked = false;
            Run(idle, config.Blink.IntervalMax + config.Blink.Duration * 3f, true, false, a => blinked |= a.Blink > 0.95f);
            Assert.That(blinked, Is.True, "за самую долгую паузу моргнул хотя бы раз");

            // Дождаться следующего моргания и прервать его действием.
            var guard = 0;
            while (idle.Blink < 0.5f && guard++ < 2000)
                idle.Tick(Dt, true, false);
            Assert.That(idle.Blink, Is.GreaterThan(0.5f));
            idle.Tick(Dt, false, true);
            Assert.That(idle.Blink, Is.EqualTo(0f), "любое действие открывает глаза");
        }

        [Test]
        public void LookAround_StartsAfterDelay_TwistsBothWays_ActionCancels()
        {
            var config = Player();
            var look = config.LookAround;
            var idle = new CubeIdleAnimator(config, 3);
            Run(idle, look.Delay - 0.2f, true);
            Assert.That(idle.LookingAround, Is.False, "до задержки не осматривается");
            Assert.That(idle.Twist, Is.EqualTo(0f));

            float maxTwist = 0f, minTwist = 0f, maxEye = 0f, minEye = 0f;
            Run(idle, 0.3f + look.Duration * 0.7f, true, false, a =>
            {
                maxTwist = Math.Max(maxTwist, a.Twist);
                minTwist = Math.Min(minTwist, a.Twist);
                maxEye = Math.Max(maxEye, a.EyeX);
                minEye = Math.Min(minEye, a.EyeX);
            });
            Assert.That(maxTwist, Is.EqualTo(look.Twist).Within(look.Twist * 0.05f), "повернулся в одну сторону");
            Assert.That(minTwist, Is.EqualTo(-look.Twist).Within(look.Twist * 0.05f), "и в другую");
            Assert.That(maxEye, Is.GreaterThan(look.EyeShift * 0.8f), "глаза ушли в сторону");
            Assert.That(minEye, Is.LessThan(-look.EyeShift * 0.8f));
            Assert.That(idle.LookingAround, Is.True);

            idle.Tick(Dt, true, true);
            Assert.That(idle.LookingAround, Is.False, "действие прерывает осмотр");
            Assert.That(idle.IdleTime, Is.EqualTo(0f), "и обнуляет счётчик покоя");
            Run(idle, CubeIdleAnimator.CancelTime + 0.05f, true);
            Assert.That(idle.Twist, Is.EqualTo(0f).Within(1e-4f), "скручивание быстро гаснет");
            Assert.That(idle.EyeX, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void LookAround_Repeats_WhileStillStanding()
        {
            var config = Player();
            var look = config.LookAround;
            var idle = new CubeIdleAnimator(config, 11);
            var starts = 0;
            var was = false;
            Run(idle, look.Delay + look.Duration * 2f + look.RepeatMax + 0.5f, true, false, a =>
            {
                if (a.LookingAround && !was)
                    starts++;
                was = a.LookingAround;
            });
            Assert.That(starts, Is.GreaterThanOrEqualTo(2), "стоит дальше — осматривается снова");
        }

        [Test]
        public void Inspect_EnemyRaisesWeapon_FullCircle_InterruptedByState()
        {
            var config = Enemy();
            var inspect = config.Inspect;
            var idle = new CubeIdleAnimator(config, 5);
            var started = false;
            Run(idle, inspect.Delay + inspect.IntervalMax * 0.5f + 0.1f, true, false, a => started |= a.Inspecting);
            Assert.That(started, Is.True, "через задержку + половину паузы начал вертеть");

            // Довести до середины верчения и проверить подъём и поклон.
            var guard = 0;
            while (idle.InspectProgress < 0.5f && guard++ < 2000)
                idle.Tick(Dt, true, false);
            Assert.That(CubeIdleAnimator.InspectLift(inspect, idle.InspectProgress), Is.EqualTo(1f));
            Assert.That(idle.Pitch, Is.EqualTo(inspect.Pitch).Within(1e-3f), "кланяется к оружию");
            Assert.That(CubeIdleAnimator.InspectAngle(inspect, 1f), Is.EqualTo(inspect.Turns * 2f * Math.PI).Within(1e-3f));
            Assert.That(CubeIdleAnimator.InspectLift(inspect, 1f), Is.EqualTo(0f), "в конце оружие вернулось в стойку");

            // Заметил игрока (состояние не Idle): верчение быстро гаснет.
            idle.Tick(Dt, false, false);
            Assert.That(idle.Inspecting, Is.False);
            Run(idle, CubeIdleAnimator.CancelTime + 0.05f, false);
            Assert.That(idle.InspectWeight, Is.EqualTo(0f));
            Assert.That(idle.Pitch, Is.EqualTo(0f).Within(1e-4f));
        }
    }
}
