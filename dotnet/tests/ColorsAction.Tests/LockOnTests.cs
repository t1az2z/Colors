using System.Collections.Generic;
using ColorsAction.Combat;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    public class LockOnTests
    {
        [Test]
        public void Pick_PrefersTargetInFrontOfCamera_OverSlightlyCloserOneBehind()
        {
            // Камера смотрит на +Z (yaw 0). Цель сзади на 3 м, цель впереди на 4 м.
            var c = new List<LockOnCandidate> { new LockOnCandidate(0, -3), new LockOnCandidate(0, 4) };
            Assert.That(LockOnRules.Pick(0, 0, 0, c, 12, 4), Is.EqualTo(1));
            // Без приоритета взгляда — просто ближайшая.
            Assert.That(LockOnRules.Pick(0, 0, 0, c, 12, 0), Is.EqualTo(0));
        }

        [Test]
        public void Pick_IgnoresTargetsOutOfRange()
        {
            var c = new List<LockOnCandidate> { new LockOnCandidate(0, 20) };
            Assert.That(LockOnRules.Pick(0, 0, 0, c, 12, 4), Is.EqualTo(-1));
        }

        [Test]
        public void Switch_GoesToNearestTargetInRequestedDirection()
        {
            // Текущая — прямо впереди; справа под 30° и под 90°, слева под 45°.
            var c = new List<LockOnCandidate>
            {
                new LockOnCandidate(0, 5),
                new LockOnCandidate(5, 0),
                new LockOnCandidate(2.5f, 4.33f),
                new LockOnCandidate(-3.5f, 3.5f)
            };
            Assert.That(LockOnRules.Switch(0, 0, 0, c, +1, 12), Is.EqualTo(2), "right: the 30° one first");
            Assert.That(LockOnRules.Switch(0, 0, 0, c, -1, 12), Is.EqualTo(3), "left");
            Assert.That(LockOnRules.Switch(0, 0, 3, c, -1, 12), Is.EqualTo(-1), "nothing further left");
        }
    }
}
