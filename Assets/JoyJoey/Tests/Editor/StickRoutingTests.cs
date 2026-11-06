using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace JoyJoey.Sandbox.Tests
{
    /// <summary>Exercise stick policies through the actual gameplay map and hardware-event snapshots.</summary>
    public sealed class StickRoutingTests : InputTestFixture
    {
        Keyboard _keyboard;
        Gamepad _pad;
        JoyInput _input;
        StickInputSettings _settings;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
            _settings = ScriptableObject.CreateInstance<StickInputSettings>();
            _input = new JoyInput(_settings);
            _input.Asset.Disable();
            _input.Asset.devices = new InputDevice[] { _keyboard, _pad };
            _input.Asset.Enable();
            InputSystem.Update();
            _input.ClearPending();
        }

        [TearDown]
        public override void TearDown()
        {
            try
            {
                _input?.Dispose();
                if (_settings) Object.DestroyImmediate(_settings);
                if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
                if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
            }
            finally { base.TearDown(); }
        }

        void Pad(GamepadState state) { InputSystem.QueueStateEvent(_pad, state); InputSystem.Update(); }
        void Keys(params Key[] keys) { InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys)); InputSystem.Update(); }
        static Vector2 At(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
        static InputCommand Press(InputFrame frame, ActionId action) => frame.Commands.Single(command => command.Action == action && command.Edge == InputEdge.Press);
        static void Direction(Vector2 actual, Vector2 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(.00001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(.00001f));
        }

        [Test]
        public void VerticalStickInputAimsDashUpWithoutHorizontalMovementOrChangingAttackSectors()
        {
            Pad(new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.West).WithButton(GamepadButton.RightShoulder));
            var frame = _input.ConsumeFrame();
            Direction(frame.Move, Vector2.up);
            Direction(Press(frame, ActionId.Basic).Direction, Vector2.right);
            Direction(Press(frame, ActionId.Dash).Direction, Vector2.up);
        }

        [TestCase(15, 1, 0)]
        [TestCase(75, 1, 0)]
        [TestCase(105, -1, 0)]
        [TestCase(210, -1, 0)]
        [TestCase(240, 0, -1)]
        [TestCase(300, 0, -1)]
        [TestCase(330, 1, 0)]
        public void NormalStickAttacksFollowTheThreeSectors(float angle, float x, float y)
        {
            Pad(new GamepadState { leftStick = At(angle) }.WithButton(GamepadButton.West));
            var frame = _input.ConsumeFrame();
            Direction(Press(frame, ActionId.Basic).Direction, new Vector2(x, y));
        }

        [Test]
        public void DashAndMovementRetainTheDiagonalWhileAttackUsesTheDownSector()
        {
            Pad(new GamepadState { leftStick = At(300) }.WithButton(GamepadButton.RightShoulder).WithButton(GamepadButton.West));
            var frame = _input.ConsumeFrame();
            Direction(frame.Move, new Vector2(1, -1).normalized);
            Direction(Press(frame, ActionId.Dash).Direction, new Vector2(1, -1).normalized);
            Direction(Press(frame, ActionId.Basic).Direction, Vector2.down);
        }

        [Test]
        public void ConfiguredDeadzoneIsAppliedToRawStickInsteadOfStackingInputProcessors()
        {
            _settings.LeftStickDeadzonePercent = 22;
            Pad(new GamepadState { leftStick = new Vector2(.25f, 0) });
            Direction(_input.ConsumeFrame().Move, Vector2.right);
            _settings.LeftStickDeadzonePercent = 30;
            Direction(_input.ConsumeFrame().Move, Vector2.zero);
            _settings.LeftStickDeadzonePercent = 0;
            Pad(new GamepadState { leftStick = new Vector2(.05f, 0) });
            Direction(_input.ConsumeFrame().Move, Vector2.right);
        }

        [Test]
        public void OneHundredPercentDisablesStickButKeepsTheDpadAvailable()
        {
            _settings.LeftStickDeadzonePercent = 100;
            Pad(new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.RightShoulder));
            var disabled = _input.ConsumeFrame();
            Direction(disabled.Move, Vector2.zero);
            Direction(Press(disabled, ActionId.Dash).Direction, Vector2.zero);
            Pad(new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.DpadUp));
            var dpad = _input.ConsumeFrame();
            Direction(dpad.Move, Vector2.up);
            Pad(new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.DpadUp).WithButton(GamepadButton.West));
            Direction(Press(_input.ConsumeFrame(), ActionId.Basic).Direction, Vector2.up);
        }

        [Test]
        public void DigitalDpadDirectionWinsAgainstTheFullyTiltedStick()
        {
            Pad(new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.DpadLeft).WithButton(GamepadButton.West));
            var frame = _input.ConsumeFrame();
            Direction(frame.Move, Vector2.left);
            Direction(Press(frame, ActionId.Basic).Direction, Vector2.left);
        }

        [Test]
        public void KeyboardUpAndDiagonalBindingsRemainAvailable()
        {
            Keys(Key.W, Key.J);
            var up = _input.ConsumeFrame();
            Direction(up.Move, Vector2.up);
            Direction(Press(up, ActionId.Basic).Direction, Vector2.up);
            Keys(); _input.ConsumeFrame();
            Keys(Key.W, Key.D, Key.LeftShift);
            Direction(Press(_input.ConsumeFrame(), ActionId.Dash).Direction, new Vector2(1, 1).normalized);
        }

        [Test]
        public void CenteringTheStickAndDashingInOneEventDoesNotReuseOldAim()
        {
            Pad(new GamepadState { leftStick = At(130) }); _input.ConsumeFrame();
            Pad(new GamepadState().WithButton(GamepadButton.RightShoulder));
            var frame = _input.ConsumeFrame();
            Direction(frame.Move, Vector2.zero);
            Direction(Press(frame, ActionId.Dash).Direction, Vector2.zero);
        }

        [Test]
        public void ADisabledStickDoesNotChangeTheLastUsedDevice()
        {
            _settings.LeftStickDeadzonePercent = 100;
            Pad(new GamepadState { leftStick = Vector2.right });
            Assert.That(_input.Device, Is.EqualTo("TECLADO"));
            Pad(new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.DpadLeft));
            Assert.That(_input.Device, Is.EqualTo("MANDO"));
        }

        [Test]
        public void DiagonalMovementResumesImmediatelyWhilePausedButtonsNeedAnotherPress()
        {
            _input.SetGameplayPaused(true);
            Pad(new GamepadState { leftStick = At(130) }.WithButton(GamepadButton.RightShoulder));
            _input.SetGameplayPaused(false);
            var frame = _input.ConsumeFrame();
            Direction(frame.Move, new Vector2(-1, 1).normalized);
            Assert.That(frame.Commands, Is.Empty);
            Pad(new GamepadState { leftStick = At(130) });
            Assert.That(_input.ConsumeFrame().Commands, Is.Empty);
            Pad(new GamepadState { leftStick = At(130) }.WithButton(GamepadButton.RightShoulder));
            Direction(Press(_input.ConsumeFrame(), ActionId.Dash).Direction, new Vector2(-1, 1).normalized);
        }
    }
}
