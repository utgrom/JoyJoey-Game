using System.Linq;
using JoyJoey.Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class MenuStickTests : InputTestFixture
    {
        InputActionAsset _actions;
        StickInputSettings _settings;
        Gamepad _pad;
        Keyboard _keyboard;
        InputAction _navigate;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            _pad = InputSystem.AddDevice<Gamepad>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _settings = ScriptableObject.CreateInstance<StickInputSettings>();
            _actions = Object.Instantiate(Resources.Load<InputActionAsset>("JoyJoeyUIControls"));
            _actions.devices = new InputDevice[] { _pad, _keyboard };
        }
        [TearDown]
        public override void TearDown()
        {
            try
            {
                if (_actions) { _actions.Disable(); Object.DestroyImmediate(_actions); }
                if (_settings) Object.DestroyImmediate(_settings);
                if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
                if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            }
            finally { base.TearDown(); }
        }
        void Configure(float percent)
        {
            _settings.LeftStickDeadzonePercent = percent;
            _navigate = StickMenuInput.Configure(_actions, _settings);
            _actions.Enable(); InputSystem.Update();
        }
        void Pad(GamepadState state)
        { InputSystem.QueueStateEvent(_pad, state); InputSystem.Update(); }
        static void Equal(Vector2 actual, Vector2 expected)
        { Assert.That(actual.x, Is.EqualTo(expected.x).Within(.0001f)); Assert.That(actual.y, Is.EqualTo(expected.y).Within(.0001f)); }

        [Test]
        public void MenuStillHasAnUpDirectionInsteadOfGameplayThreeSectorMovement()
        {
            Configure(22);
            Pad(new GamepadState { leftStick = Vector2.up });
            Equal(_navigate.ReadValue<Vector2>(), Vector2.up);
        }

        [Test]
        public void FullDeadzoneRemovesStickControlsWithoutDisablingTheDpad()
        {
            Configure(100);
            Assert.That(_navigate.controls.Any(control => StickMenuInput.IsLeftStickControl(control, _pad)), Is.False);
            Pad(new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.DpadLeft));
            Equal(_navigate.ReadValue<Vector2>(), Vector2.left);
            Pad(new GamepadState { leftStick = Vector2.up }.WithButton(GamepadButton.DpadLeft));
            Equal(_navigate.ReadValue<Vector2>(), Vector2.left);
            Pad(new GamepadState { leftStick = Vector2.one });
            Equal(_navigate.ReadValue<Vector2>(), Vector2.zero);
        }

        [Test]
        public void InactiveStickDriftDoesNotOverwriteADpadThatIsStillHeld()
        {
            Configure(22);
            Assert.That(_navigate.type, Is.EqualTo(InputActionType.Value));
            Pad(new GamepadState().WithButton(GamepadButton.DpadDown));
            Pad(new GamepadState { leftStick = new Vector2(.18f, .05f) }.WithButton(GamepadButton.DpadDown));
            Equal(_navigate.ReadValue<Vector2>(), Vector2.down);
            Pad(new GamepadState { leftStick = new Vector2(.16f, -.04f) }.WithButton(GamepadButton.DpadDown));
            Equal(_navigate.ReadValue<Vector2>(), Vector2.down);
        }

        [Test]
        public void RightStickKeyboardAndSubmitBindingsRemainAvailable()
        {
            Configure(100);
            Pad(new GamepadState { leftStick = Vector2.right, rightStick = Vector2.up });
            Equal(_navigate.ReadValue<Vector2>(), Vector2.up);
            Pad(new GamepadState { leftStick = Vector2.right });
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.LeftArrow, Key.J)); InputSystem.Update();
            Equal(_navigate.ReadValue<Vector2>(), Vector2.left);
            Assert.That(_actions.FindAction("UI/Submit").WasPerformedThisFrame(), Is.True);
        }

        [Test]
        public void DisabledStickAliasesDoNotBlockThePauseReleaseGate()
        {
            Pad(new GamepadState { leftStick = Vector2.one });
            Assert.That(StickMenuInput.AnyInputHeld(100), Is.False);
            Assert.That(StickMenuInput.AnyInputHeld(22), Is.True);
            Pad(new GamepadState { leftStick = Vector2.one }.WithButton(GamepadButton.DpadRight));
            Assert.That(StickMenuInput.AnyInputHeld(100), Is.True, "The dpad must still be released before menu navigation arms.");
            Pad(new GamepadState { leftStick = Vector2.one }.WithButton(GamepadButton.LeftStick));
            Assert.That(StickMenuInput.AnyInputHeld(100), Is.True, "Disabling the axis does not disable its separate click button.");
        }

        [Test]
        public void ReapplyingRuntimeConfigurationDoesNotModifyTheSourceOrDuplicateBindings()
        {
            var source = Resources.Load<InputActionAsset>("JoyJoeyUIControls");
            string original = source.ToJson();
            var first = StickMenuInput.Configure(_actions, _settings);
            int count = first.bindings.Count;
            var second = StickMenuInput.Configure(_actions, _settings);
            Assert.That(second, Is.SameAs(first));
            Assert.That(second.bindings.Count, Is.EqualTo(count));
            Assert.That(second.bindings.Count(binding => binding.name == StickMenuInput.BindingName), Is.EqualTo(1));
            Assert.That(source.ToJson(), Is.EqualTo(original));
        }
    }
}
