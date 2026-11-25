using System;
using System.Collections.Generic;
using ColorsAction.Combat;
using ColorsAction.Configs;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Серия отскоков: шаги, окно комбо, буфер нажатия, пауза, неуязвимость и движение шага.</summary>
    public class DodgeComboTests
    {
        private const float Dt = 1f / 60f;

        private static DodgeConfig Config(float chainFrom = 1f) => new DodgeConfig
        {
            ComboWindow = 0.3f,
            InputBuffer = 0.1f,
            Steps = new List<DodgeStepConfig>
            {
                new DodgeStepConfig { Distance = 1.6f, Duration = 0.25f, Cooldown = 0.05f, InvulnerableFrom = 0.2f, InvulnerableTo = 0.6f, ChainFrom = chainFrom },
                new DodgeStepConfig { Distance = 2.2f, Duration = 0.3f, Cooldown = 0.05f, ChainFrom = chainFrom },
                new DodgeStepConfig { Distance = 4f, Duration = 0.5f, Cooldown = 0.2f, Flip = true },
            }
        };

        /// <summary>Тикает, пока идёт шаг (и чуть больше, чтобы он точно завершился).</summary>
        private static void RunOut(DodgeCombo combo)
        {
            for (int i = 0; i < 200 && combo.Active; i++)
                combo.Tick(Dt);
        }

        private static void Wait(DodgeCombo combo, float seconds)
        {
            for (float t = 0; t < seconds - 1e-4f; t += Dt)
                combo.Tick(Dt);
        }

        private static int PressAndBegin(DodgeCombo combo)
        {
            combo.Press();
            Assert.That(combo.CanBegin, Is.True);
            return combo.Begin();
        }

        [Test]
        public void PressesInsideWindow_GoThroughAllSteps_ThenRestart()
        {
            var combo = new DodgeCombo(Config());
            Assert.That(PressAndBegin(combo), Is.EqualTo(0));
            RunOut(combo);
            Wait(combo, 0.1f);
            Assert.That(PressAndBegin(combo), Is.EqualTo(1));
            RunOut(combo);
            Wait(combo, 0.1f);
            Assert.That(PressAndBegin(combo), Is.EqualTo(2));
            RunOut(combo);
            Wait(combo, 0.25f);
            Assert.That(combo.NextStep, Is.EqualTo(0), "after the last step the combo starts over");
            Assert.That(PressAndBegin(combo), Is.EqualTo(0));
        }

        [Test]
        public void PauseLongerThanWindow_ResetsToFirstStep()
        {
            var combo = new DodgeCombo(Config());
            PressAndBegin(combo);
            RunOut(combo);
            Assert.That(combo.NextStep, Is.EqualTo(1));
            Wait(combo, 0.35f);
            Assert.That(combo.WindowLeft, Is.EqualTo(0f));
            Assert.That(combo.NextStep, Is.EqualTo(0));
            Assert.That(PressAndBegin(combo), Is.EqualTo(0));
        }

        [Test]
        public void Cooldown_DelaysNextStep_AndBufferedPressFiresWhenItEnds()
        {
            var combo = new DodgeCombo(Config());
            PressAndBegin(combo);
            RunOut(combo);
            combo.Press();
            Assert.That(combo.CanBegin, Is.False, "cooldown after the step");
            Wait(combo, 0.06f);
            Assert.That(combo.CanBegin, Is.True, "press is remembered through the cooldown");
            Assert.That(combo.Begin(), Is.EqualTo(1));
        }

        [Test]
        public void EarlyPress_IsBufferedOnlyForInputBuffer()
        {
            var combo = new DodgeCombo(Config());
            PressAndBegin(combo);
            // Нажали задолго до конца шага: буфер (0.1 с) истекает раньше приземления.
            combo.Press();
            Wait(combo, 0.05f);
            Assert.That(combo.CanBegin, Is.False, "no chaining mid-step with chainFrom = 1");
            RunOut(combo);
            Wait(combo, 0.06f);
            Assert.That(combo.Buffered, Is.False);
            Assert.That(combo.CanBegin, Is.False);

            // Нажали перед самым приземлением: срабатывает после паузы.
            combo.Press();
            Assert.That(combo.Begin(), Is.EqualTo(1));
            Wait(combo, 0.28f);
            combo.Press();
            RunOut(combo);
            Wait(combo, 0.05f);
            Assert.That(combo.CanBegin, Is.True);
            Assert.That(combo.Begin(), Is.EqualTo(2));
        }

        [Test]
        public void ChainFrom_LetsBufferedPressCutTheTail()
        {
            var combo = new DodgeCombo(Config(chainFrom: 0.6f));
            PressAndBegin(combo);
            Wait(combo, 0.1f);
            combo.Press();
            Assert.That(combo.CanBegin, Is.False, "before chainFrom");
            Wait(combo, 0.06f);
            Assert.That(combo.Active && combo.CanBegin, Is.True, "after chainFrom the next step starts right away");
            Assert.That(combo.Begin(), Is.EqualTo(1));
            Assert.That(combo.Progress, Is.EqualTo(0f));
        }

        [Test]
        public void Invulnerability_FollowsStepWindow()
        {
            var combo = new DodgeCombo(Config());
            PressAndBegin(combo);
            Assert.That(combo.Invulnerable, Is.False, "window starts at 0.2");
            Wait(combo, 0.1f);
            Assert.That(combo.Invulnerable, Is.True);
            Wait(combo, 0.1f);
            Assert.That(combo.Invulnerable, Is.False, "window ends at 0.6");
            RunOut(combo);
            Assert.That(combo.Invulnerable, Is.False);
        }

        [Test]
        public void Reset_DropsComboAndBuffer()
        {
            var combo = new DodgeCombo(Config());
            PressAndBegin(combo);
            combo.Press();
            combo.Reset();
            Assert.That(combo.Active || combo.Buffered, Is.False);
            Assert.That(combo.NextStep, Is.EqualTo(0));
        }

        [TestCase(false, 0f, 0f)]
        [TestCase(true, 0f, 0f)]
        [TestCase(true, 0.3f, 0.25f)]
        public void StepMotion_CoversExactlyDistance(bool flip, float midPause, float midSpeed)
        {
            var step = new DodgeStepConfig { Distance = 3f, Duration = 0.4f, HopHeight = 0.3f, Flip = flip, MidPause = midPause, MidSpeed = midSpeed };
            const int n = 4000;
            var path = 0.0;
            for (int i = 0; i < n; i++)
                path += step.Sample((i + 0.5f) / n).Speed * step.Duration / n;
            Assert.That(path, Is.EqualTo(3.0).Within(0.01));
            Assert.That(step.Sample(0f).Hop, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(step.Sample(1f).Hop, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(step.Sample(1f).Stage, Is.EqualTo(3), "lands on its feet");
        }

        [Test]
        public void Hop_DoesNotFlip_FlipGoesOverTheTopInTwoStages()
        {
            var hop = new DodgeStepConfig { Flip = false, HopHeight = 0.2f };
            Assert.That(hop.Sample(0.5f).Angle, Is.EqualTo(0f));
            Assert.That(hop.Sample(0.5f).Hop, Is.EqualTo(0.2f).Within(1e-4f));
            Assert.That(hop.Sample(0.99f).Stage, Is.EqualTo(0));

            var flip = new DodgeStepConfig { Flip = true, MidPause = 0.2f };
            Assert.That(flip.Sample(0.2f).Stage, Is.EqualTo(0));
            var head = flip.Sample(0.5f);
            Assert.That(head.Phase, Is.EqualTo(1));
            Assert.That(head.Stage, Is.EqualTo(1), "on its head");
            Assert.That(head.Angle, Is.EqualTo(180f));
            Assert.That(flip.Sample(0.8f).Stage, Is.EqualTo(2));
            Assert.That(flip.Sample(1f).Angle, Is.EqualTo(360f).Within(1e-3f));
        }

        [Test]
        public void Validate_RejectsBadSteps()
        {
            Assert.Throws<InvalidOperationException>(() => new DodgeConfig { Steps = new List<DodgeStepConfig>() }.Validate());
            Assert.Throws<InvalidOperationException>(() => new DodgeConfig { Steps = { new DodgeStepConfig { Duration = 0f } } }.Validate());
            Assert.Throws<InvalidOperationException>(() =>
                new DodgeConfig { Steps = new List<DodgeStepConfig> { new DodgeStepConfig { InvulnerableFrom = 0.8f, InvulnerableTo = 0.2f } } }.Validate());
            Assert.Throws<InvalidOperationException>(() =>
                new DodgeConfig { Steps = new List<DodgeStepConfig> { new DodgeStepConfig { ChainFrom = 1.5f } } }.Validate());
            Assert.DoesNotThrow(() => new DodgeConfig().Validate());
        }
    }
}
