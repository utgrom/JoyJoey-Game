using NUnit.Framework;
using UnityEngine;

namespace JoyJoey.Sandbox.Tests
{
    /// <summary>Dash direction is an invariant of the combat model, independent of the input device.</summary>
    public sealed class DashDirectionTests
    {
        CombatSimulation _simulation;

        [SetUp]
        public void Setup()
        {
            _simulation = new CombatSimulation();
            _simulation.Player.Position = new Vector2(-10, 8);
            _simulation.Player.Grounded = false;
            _simulation.Player.Coyote = 0;
            _simulation.Player.AirDashes = 1;
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(_simulation.Catalog);
            Object.DestroyImmediate(_simulation.Tuning);
            Object.DestroyImmediate(_simulation.TargetProfile);
        }

        void Dash(Vector2 direction)
        {
            var input = new InputFrame();
            input.Commands.Add(new InputCommand(ActionId.Dash, InputEdge.Press, direction));
            _simulation.Step(input);
        }

        [TestCase(13, 1, 0)]
        [TestCase(38, 1, 1)]
        [TestCase(88, 0, 1)]
        [TestCase(128, -1, 1)]
        [TestCase(168, -1, 0)]
        [TestCase(208, -1, -1)]
        [TestCase(258, 0, -1)]
        [TestCase(303, 1, -1)]
        public void AirDashSnapsToOneOfEightDirectionsWithoutChangingItsSpeed(float angle, int x, int y)
        {
            float radians = angle * Mathf.Deg2Rad;
            Dash(new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)));
            var expected = new Vector2(x, y).normalized * _simulation.Tuning.DashSpeed;
            Assert.That(Vector2.Distance(_simulation.Player.Velocity, expected), Is.LessThan(.0001f));
            Assert.That(_simulation.Player.Velocity.magnitude, Is.EqualTo(_simulation.Tuning.DashSpeed).Within(.0001f));
            Assert.That(_simulation.Player.AirDashes, Is.Zero);
        }

        [TestCase(225, -1)]
        [TestCase(315, 1)]
        public void DownwardGroundDashKeepsTheExistingHorizontalFloorRule(float angle, int expectedSign)
        {
            _simulation.Player.Position = new Vector2(-3, 0);
            _simulation.Player.Grounded = true;
            float radians = angle * Mathf.Deg2Rad;
            Dash(new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)));
            Assert.That(_simulation.Player.Velocity.x, Is.EqualTo(expectedSign * _simulation.Tuning.DashSpeed).Within(.0001f));
            Assert.That(_simulation.Player.Velocity.y, Is.Zero);
        }

        [Test]
        public void NeutralDashUsesFacingAndTheBurstRetainsItsVelocityOnTheNextTick()
        {
            _simulation.Player.Facing = -1;
            Dash(Vector2.zero);
            var velocity = new Vector2(-_simulation.Tuning.DashSpeed, 0);
            Assert.That(_simulation.Player.Velocity, Is.EqualTo(velocity));
            _simulation.Step(new InputFrame());
            Assert.That(_simulation.Player.Velocity, Is.EqualTo(velocity));
        }
    }
}
