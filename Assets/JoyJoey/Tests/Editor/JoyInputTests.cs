using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace JoyJoey.Sandbox.Tests
{
    /// <summary>Exercise real Input System events against the shipped editable map.</summary>
    public class JoyInputTests : InputTestFixture
    {
        private Keyboard keyboard;
        private Gamepad pad;
        private JoyInput input;

        [SetUp]
        public override void Setup()
        {
            // The fixture substitutes a player-like runtime. Native EditMode
            // intentionally suppresses Dynamic input updates/action callbacks.
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            pad = InputSystem.AddDevice<Gamepad>();
            input = new JoyInput();
            input.Asset.Disable();
            input.Asset.devices = new InputDevice[] { keyboard, pad };
            input.Asset.Enable();
            InputSystem.Update();
            input.ClearPending();
        }

        [TearDown]
        public override void TearDown()
        {
            try
            {
                input?.Dispose();
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            }
            finally { base.TearDown(); }
        }

        private void Keyboard(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            InputSystem.Update();
        }

        private void Pad(GamepadState state)
        {
            InputSystem.QueueStateEvent(pad, state);
            InputSystem.Update();
        }

        [Test]
        public void RebindingSwapsWithHiddenActionsAndUpdatesExistingGameplayInput()
        {
            var jump = GameplayControls.GetRows(false).Single(row => row.Name == "Saltar");
            var basic = GameplayControls.GetRows(false).Single(row => row.Name == "Ataque básico");
            try
            {
                GameplayControls.SetActionRevealed(nameof(ActionId.Basic), false);
                Assert.That(basic.Name, Is.EqualTo("(UNUSED)"));
                Assert.That(GameplayControls.TryRebind(jump, keyboard.jKey, out string error), Is.True, error);

                Keyboard(Key.J);
                Assert.That(input.ConsumeFrame().Commands.Single().Action, Is.EqualTo(ActionId.Jump));
                Keyboard(); input.ConsumeFrame();
                Keyboard(Key.Space);
                Assert.That(input.ConsumeFrame().Commands.Single().Action, Is.EqualTo(ActionId.Basic));
                Keyboard(); input.ConsumeFrame();

                Pad(new GamepadState().WithButton(GamepadButton.South));
                Assert.That(input.ConsumeFrame().Commands.Single().Action, Is.EqualTo(ActionId.Jump));
                GameplayControls.SetActionRevealed(nameof(ActionId.Basic), true);
                Assert.That(basic.Name, Is.EqualTo("Ataque básico"));
            }
            finally
            {
                GameplayControls.SetActionRevealed(nameof(ActionId.Basic), true);
                GameplayControls.RestoreDefaults(false, out _);
            }
        }

        [Test]
        public void DpadDirectionAndActionButtonCanSwapWithoutRebindingThePauseButton()
        {
            var down = GameplayControls.GetRows(true).Single(row => row.Name == "Abajo");
            try
            {
                Assert.That(GameplayControls.DirectionPath("left", false), Is.EqualTo("<Keyboard>/a"));
                Assert.That(GameplayControls.DirectionPath("right", false), Is.EqualTo("<Keyboard>/d"));
                Assert.That(GameplayControls.DirectionPath("down", true), Is.EqualTo("<Gamepad>/dpad/down"));
                Assert.That(GameplayControls.TryRebind(down, pad.startButton, out _), Is.False);
                Assert.That(GameplayControls.TryRebind(down, keyboard.sKey, out _), Is.False);
                Assert.That(GameplayControls.TryRebind(down, pad.buttonWest, out string error), Is.True, error);
                Assert.That(GameplayControls.DirectionPath("down", true), Is.EqualTo("<Gamepad>/buttonWest"));
                Assert.That(GameplayControls.ActionPath(nameof(ActionId.Basic), true), Is.EqualTo("<Gamepad>/dpad/down"));

                Pad(new GamepadState().WithButton(GamepadButton.DpadDown));
                var attack = input.ConsumeFrame();
                Assert.That(attack.Move, Is.EqualTo(Vector2.zero));
                Assert.That(attack.Commands.Single().Action, Is.EqualTo(ActionId.Basic));
                Pad(new GamepadState()); input.ConsumeFrame();

                Pad(new GamepadState().WithButton(GamepadButton.West));
                var moved = input.ConsumeFrame();
                Assert.That(moved.Move, Is.EqualTo(Vector2.down));
                Assert.That(moved.Commands, Is.Empty);
            }
            finally { GameplayControls.RestoreDefaults(true, out _); }
        }

        [Test]
        public void GamepadDefaultsResetDeadzoneWithoutRemovingKeyboardOverrides()
        {
            var jump = GameplayControls.GetRows(false).Single(row => row.Name == "Saltar");
            float initialDeadzone = GameplayControls.LeftStickDeadzonePercent;
            try
            {
                Assert.That(GameplayControls.TryRebind(jump, keyboard.tKey, out string rebindError), Is.True, rebindError);
                Assert.That(GameplayControls.TrySetLeftStickDeadzone(100, out string deadzoneError), Is.True, deadzoneError);
                Pad(new GamepadState { leftStick = Vector2.right });
                Assert.That(input.ConsumeFrame().Move, Is.EqualTo(Vector2.zero));

                Assert.That(GameplayControls.RestoreDefaults(true, out string restoreError), Is.True, restoreError);
                Assert.That(GameplayControls.LeftStickDeadzonePercent, Is.EqualTo(initialDeadzone));
                Keyboard(Key.T);
                Assert.That(input.ConsumeFrame().Commands.Single().Action, Is.EqualTo(ActionId.Jump));
            }
            finally
            {
                GameplayControls.RestoreDefaults(false, out _);
                GameplayControls.RestoreDefaults(true, out _);
            }
        }

        [Test]
        public void PressAndReleaseAreConsumedExactlyOnce()
        {
            Keyboard(Key.J);
            InputFrame first = input.ConsumeFrame();
            Assert.That(first.BasicHeld, Is.True);
            Assert.That(first.Commands.Count(c => c.Action == ActionId.Basic && c.Edge == InputEdge.Press), Is.EqualTo(1));
            Assert.That(input.ConsumeFrame().Commands, Is.Empty);
            Keyboard();
            InputFrame release = input.ConsumeFrame();
            Assert.That(release.BasicHeld, Is.False);
            Assert.That(release.Commands.Count(c => c.Action == ActionId.Basic && c.Edge == InputEdge.Release), Is.EqualTo(1));
            Assert.That(input.ConsumeFrame().Commands, Is.Empty);
        }

        [Test]
        public void KeyboardReleaseDoesNotReleaseHeldPadButton()
        {
            Keyboard(Key.J);
            input.ConsumeFrame();
            Pad(new GamepadState().WithButton(GamepadButton.West));
            Assert.That(input.ConsumeFrame().Commands, Is.Empty);
            Keyboard();
            InputFrame mixed = input.ConsumeFrame();
            Assert.That(mixed.BasicHeld, Is.True);
            Assert.That(mixed.Commands, Is.Empty);
            Pad(new GamepadState());
            InputFrame released = input.ConsumeFrame();
            Assert.That(released.BasicHeld, Is.False);
            Assert.That(released.Commands.Single().Edge, Is.EqualTo(InputEdge.Release));
        }

        [Test]
        public void AnalogTriggersUseHysteresis()
        {
            Pad(new GamepadState { leftTrigger = .48f });
            Assert.That(input.ConsumeFrame().Commands, Is.Empty);
            Pad(new GamepadState { leftTrigger = .60f });
            Assert.That(input.ConsumeFrame().Commands.Single().Action, Is.EqualTo(ActionId.PreviousSuit));
            Pad(new GamepadState { leftTrigger = .48f });
            Assert.That(input.ConsumeFrame().Commands, Is.Empty);
            Pad(new GamepadState { leftTrigger = .30f });
            Assert.That(input.ConsumeFrame().Commands.Single().Edge, Is.EqualTo(InputEdge.Release));
        }

        [Test]
        public void DirectionIsRecordedWhenTheButtonIsPressed()
        {
            Keyboard(Key.S);
            input.ConsumeFrame();
            Keyboard(Key.S, Key.J);
            Keyboard();
            InputCommand attack = input.ConsumeFrame().Commands.First(c => c.Action == ActionId.Basic && c.Edge == InputEdge.Press);
            Assert.That(attack.Direction.y, Is.LessThan(-.9f));
        }

        [Test]
        public void SimultaneousDirectionAndAttackUseThatDirection()
        {
            Keyboard(Key.S, Key.J);
            InputCommand attack = input.ConsumeFrame().Commands.First(c => c.Action == ActionId.Basic && c.Edge == InputEdge.Press);
            Assert.That(attack.Direction.y, Is.LessThan(-.9f));
        }

        [Test]
        public void ReleasingMovementAndPressingAttackInOneEventIsNeutral()
        {
            Keyboard(Key.D);
            Assert.That(input.ConsumeFrame().Move.x, Is.GreaterThan(.9f));
            Keyboard(Key.J);
            InputCommand attack = input.ConsumeFrame().Commands.Single(c => c.Action == ActionId.Basic && c.Edge == InputEdge.Press);
            Assert.That(attack.Direction, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void CenteringStickAndPressingAttackInOneEventIsNeutral()
        {
            Pad(new GamepadState { leftStick = Vector2.right });
            Assert.That(input.ConsumeFrame().Move.x, Is.GreaterThan(.9f));
            Pad(new GamepadState().WithButton(GamepadButton.West));
            InputCommand attack = input.ConsumeFrame().Commands.Single(c => c.Action == ActionId.Basic && c.Edge == InputEdge.Press);
            Assert.That(attack.Direction, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void FocusLossClearsPendingEdgesAndHeldMovement()
        {
            Keyboard(Key.W, Key.Space, Key.J, Key.K);
            typeof(JoyInput).GetMethod("OnFocusChanged", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance).Invoke(input, new object[] { false });
            InputFrame frame = input.ConsumeFrame();
            Assert.That(frame.Commands, Is.Empty);
            Assert.That(frame.JumpHeld || frame.BasicHeld || frame.RagHeld, Is.False);
            Assert.That(frame.Move, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void RapidTapsAndHeldButtonsHaveDifferentCommands()
        {
            Keyboard(Key.J);
            Assert.That(input.ConsumeFrame().Commands.Single().TapCount, Is.EqualTo(1));
            Keyboard();
            input.ConsumeFrame();
            Keyboard(Key.J);
            Assert.That(input.ConsumeFrame().Commands.Single().TapCount, Is.EqualTo(2));
            Assert.That(input.ConsumeFrame().Commands, Is.Empty);
        }

        [Test]
        public void CurrentUserMappingsOverrideTheOldGdd()
        {
            Keyboard(Key.H, Key.L);
            InputFrame frame = input.ConsumeFrame();
            Assert.That(frame.Commands.Count(c => c.Action == ActionId.Transform && c.Edge == InputEdge.Press), Is.EqualTo(1));
            Assert.That(frame.Commands.Count(c => c.Action == ActionId.Trick && c.Edge == InputEdge.Press), Is.EqualTo(1));
            Assert.That(input.Asset.FindAction("Transform").bindings.Any(b => b.path == "<Keyboard>/h"), Is.True);
            Assert.That(input.Asset.FindAction("Trick").bindings.Any(b => b.path == "<Keyboard>/l"), Is.True);
        }

        [Test]
        public void RemovingControllerReleasesAHeldAction()
        {
            Pad(new GamepadState().WithButton(GamepadButton.North));
            Assert.That(input.ConsumeFrame().RagHeld, Is.True);
            InputSystem.RemoveDevice(pad);
            InputFrame frame = input.ConsumeFrame();
            Assert.That(frame.RagHeld, Is.False);
            Assert.That(frame.Commands.Any(c => c.Action == ActionId.Rag && c.Edge == InputEdge.Release), Is.True);
        }

        [Test]
        public void StickDeadzoneDoesNotCreateDrift()
        {
            Pad(new GamepadState { leftStick = new Vector2(.1f, .08f) });
            Assert.That(input.ConsumeFrame().Move, Is.EqualTo(Vector2.zero));
            Pad(new GamepadState { leftStick = new Vector2(.85f, 0) });
            Assert.That(input.ConsumeFrame().Move.x, Is.GreaterThan(.7f));
        }
    }
}
