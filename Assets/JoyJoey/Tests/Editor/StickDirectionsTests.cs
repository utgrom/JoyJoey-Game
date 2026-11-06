using NUnit.Framework;
using UnityEngine;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class StickDirectionsTests
    {
        static Vector2 At(float degrees, float magnitude = 1)
            => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * magnitude;
        static void Equal(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(.00001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(.00001f));
        }

        [TestCase(0f, 1, 0)] [TestCase(45f, 1, 1)] [TestCase(90f, 0, 1)] [TestCase(135f, -1, 1)]
        [TestCase(180f, -1, 0)] [TestCase(225f, -1, -1)] [TestCase(270f, 0, -1)] [TestCase(315f, 1, -1)]
        public void DashCentersHaveEightEqualSpeedDirections(float angle, int x, int y)
        {
            var direction = StickDirections.ForDash(At(angle, .8f), 22);
            Equal(direction, new Vector2(x, y).normalized);
            Assert.That(direction.magnitude, Is.EqualTo(1).Within(.00001f));
        }

        [TestCase(22.5f, 1, 1)] [TestCase(67.5f, 0, 1)] [TestCase(112.5f, -1, 1)] [TestCase(157.5f, -1, 0)]
        [TestCase(202.5f, -1, -1)] [TestCase(247.5f, 0, -1)] [TestCase(292.5f, 1, -1)] [TestCase(337.5f, 1, 0)]
        public void DashHalfwayBoundariesHaveADeterministicCounterclockwiseTie(float angle, int x, int y)
            => Equal(StickDirections.SnapDash(At(angle)), new Vector2(x, y).normalized);

        [Test]
        public void DashSnapsToTheNearestDirectionWithoutDiagonalSpeedGain()
        {
            for (int angle = 0; angle < 360; angle++)
            {
                var raw = At(angle, .9f);
                var snapped = StickDirections.ForDash(raw, 22);
                Assert.That(snapped.magnitude, Is.EqualTo(1).Within(.00001f));
                Assert.That(Vector2.Angle(raw, snapped), Is.LessThanOrEqualTo(22.5002f), "Angle " + angle);
            }
            Equal(StickDirections.SnapDash(new Vector2(20, 1)), Vector2.right);
        }

        [TestCase(0f, 1, 0)] [TestCase(45f, 1, 0)] [TestCase(89.99f, 1, 0)] [TestCase(90f, 1, 0)]
        [TestCase(90.01f, -1, 0)] [TestCase(180f, -1, 0)] [TestCase(224.99f, -1, 0)]
        [TestCase(225f, 0, -1)] [TestCase(240f, 0, -1)] [TestCase(270f, 0, -1)] [TestCase(300f, 0, -1)]
        [TestCase(315f, 0, -1)] [TestCase(315.01f, 1, 0)] [TestCase(359f, 1, 0)]
        public void MovementMatchesTheThreeRequestedSectors(float angle, int x, int y)
            => Equal(StickDirections.ForMovement(At(angle, .8f), 22), new Vector2(x, y));

        [Test]
        public void MovementIsDigitalOnceItLeavesTheRadialDeadzone()
        {
            Equal(StickDirections.ForMovement(Vector2.right * .2201f, 22), Vector2.right);
            Equal(StickDirections.ForMovement(Vector2.left, 22), Vector2.left);
            Equal(StickDirections.ForMovement(Vector2.down * .5f, 22), Vector2.down);
            Assert.That(StickDirections.Strength(Vector2.right * .4f, 22), Is.LessThan(1));
        }

        [Test]
        public void RadialDeadzoneIgnoresDriftAndIncludesItsBoundary()
        {
            Equal(StickDirections.ForMovement(new Vector2(.1f, .08f), 22), Vector2.zero);
            Equal(StickDirections.ForDash(Vector2.right * .22f, 22), Vector2.zero);
            Equal(StickDirections.ForMovement(Vector2.down * .22f, 22), Vector2.zero);
            Assert.That(StickDirections.Strength(new Vector2(.18f, .18f), 22), Is.GreaterThan(0));
        }

        [Test]
        public void OneHundredPercentDisablesEveryStickDirectionEvenAtFullTilt()
        {
            for (int angle = 0; angle < 360; angle += 15)
            {
                Equal(StickDirections.ForMovement(At(angle), 100), Vector2.zero);
                Equal(StickDirections.ForDash(At(angle), 100), Vector2.zero);
            }
            Equal(StickDirections.ForDash(Vector2.one, 100), Vector2.zero);
        }

        [Test]
        public void ZeroDeadzoneAcceptsSmallInputsButNeverInventsAZeroDirection()
        {
            Equal(StickDirections.ForMovement(Vector2.right * .0001f, 0), Vector2.right);
            Equal(StickDirections.ForDash(Vector2.up * .0001f, 0), Vector2.up);
            Equal(StickDirections.ForMovement(Vector2.zero, 0), Vector2.zero);
            Equal(StickDirections.ForDash(Vector2.zero, 0), Vector2.zero);
            Equal(StickDirections.SnapDash(Vector2.zero), Vector2.zero);
        }

        [Test]
        public void InvalidSettingsAreSanitizedAndInvalidVectorsRemainInactive()
        {
            Assert.That(StickInputSettings.SanitizeDeadzonePercent(float.NaN), Is.EqualTo(22));
            Assert.That(StickInputSettings.SanitizeDeadzonePercent(float.PositiveInfinity), Is.EqualTo(22));
            Assert.That(StickInputSettings.SanitizeDeadzonePercent(-20), Is.Zero);
            Assert.That(StickInputSettings.SanitizeDeadzonePercent(200), Is.EqualTo(100));
            foreach (var invalid in new[] { new Vector2(float.NaN, 1), new Vector2(1, float.PositiveInfinity), new Vector2(float.NegativeInfinity, 0) })
            {
                Equal(StickDirections.ForMovement(invalid, 0), Vector2.zero);
                Equal(StickDirections.ForDash(invalid, 0), Vector2.zero);
                Equal(StickDirections.SnapDash(invalid), Vector2.zero);
            }
        }
    }
}
