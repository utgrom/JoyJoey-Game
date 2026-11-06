using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace JoyJoey.Sandbox.Tests
{
    /// <summary>Pause cancels action intent while keeping physical movement immediately available.</summary>
    public sealed class PauseInputTests : InputTestFixture
    {
        Keyboard _keyboard;
        Gamepad _pad;
        JoyInput _input;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _pad = InputSystem.AddDevice<Gamepad>();
            _input = new JoyInput();
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
                if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
                if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
            }
            finally { base.TearDown(); }
        }

        void Keys(params Key[] keys)
        { InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys)); InputSystem.Update(); }
        void Pad(GamepadState state)
        { InputSystem.QueueStateEvent(_pad, state); InputSystem.Update(); }
        static void AssertNoActionIntent(InputFrame frame)
        {
            Assert.That(frame.Commands, Is.Empty);
            Assert.That(frame.HeldActions, Is.Empty);
            Assert.That(frame.JumpHeld || frame.BasicHeld || frame.RagHeld, Is.False);
            Assert.That(frame.BasicHeldSeconds, Is.Zero);
            Assert.That(frame.RagHeldSeconds, Is.Zero);
        }

        [Test]
        public void KeyboardMovementRemainsLiveAndResumesWithoutReleasingTheDirection()
        {
            Keys(Key.D, Key.K);
            Assert.That(_input.ConsumeFrame().RagHeld, Is.True);
            _input.SetGameplayPaused(true);
            var paused = _input.ConsumeFrame();
            AssertNoActionIntent(paused);
            Assert.That(paused.Move.x, Is.GreaterThan(.9f));
            Assert.That(_input.Asset.FindAction("Gameplay/Move").enabled, Is.True);
            _input.SetGameplayPaused(false);
            var resumed = _input.ConsumeFrame();
            Assert.That(resumed.Move.x, Is.GreaterThan(.9f));
            AssertNoActionIntent(resumed);
        }

        [Test]
        public void MovementChosenDuringPauseIsAvailableOnTheFirstResumedFrame()
        {
            _input.SetGameplayPaused(true);
            Pad(new GamepadState { leftStick = Vector2.right }.WithButton(GamepadButton.North));
            AssertNoActionIntent(_input.ConsumeFrame());
            _input.SetGameplayPaused(false);
            var resumed = _input.ConsumeFrame();
            Assert.That(resumed.Move.x, Is.GreaterThan(.9f));
            AssertNoActionIntent(resumed);
        }

        [Test]
        public void PausingACurrentChargeCancelsItWithoutCreatingARelease()
        {
            Keys(Key.K);
            _input.ConsumeFrame();
            _input.SetGameplayPaused(true);
            Keys();
            AssertNoActionIntent(_input.ConsumeFrame());
            _input.SetGameplayPaused(false);
            AssertNoActionIntent(_input.ConsumeFrame());
            Keys(Key.K);
            var fresh = _input.ConsumeFrame();
            Assert.That(fresh.RagHeld, Is.True);
            Assert.That(fresh.Commands.Single().Edge, Is.EqualTo(InputEdge.Press));
            Assert.That(fresh.Commands.Single().TapCount, Is.EqualTo(1));
        }

        [TestCase(Key.Space, ActionId.Jump)]
        [TestCase(Key.J, ActionId.Basic)]
        [TestCase(Key.K, ActionId.Rag)]
        [TestCase(Key.L, ActionId.Trick)]
        [TestCase(Key.LeftShift, ActionId.Dash)]
        [TestCase(Key.H, ActionId.Transform)]
        [TestCase(Key.Q, ActionId.PreviousSuit)]
        [TestCase(Key.E, ActionId.NextSuit)]
        public void ActionsPressedInPauseRequireAFreshPressAfterResume(Key key, ActionId action)
        {
            _input.SetGameplayPaused(true);
            Keys(key);
            AssertNoActionIntent(_input.ConsumeFrame());
            _input.SetGameplayPaused(false);
            AssertNoActionIntent(_input.ConsumeFrame());
            Keys();
            AssertNoActionIntent(_input.ConsumeFrame());
            Keys(key);
            var fresh = _input.ConsumeFrame();
            Assert.That(fresh.HeldActions.Contains(action), Is.True);
            Assert.That(fresh.Commands.Single().Action, Is.EqualTo(action));
            Assert.That(fresh.Commands.Single().Edge, Is.EqualTo(InputEdge.Press));
        }

        [Test]
        public void AQuarantinedControlDoesNotBlockAFreshControlForTheSameAction()
        {
            Keys(Key.K);
            _input.ConsumeFrame();
            _input.SetGameplayPaused(true);
            _input.SetGameplayPaused(false);
            Pad(new GamepadState().WithButton(GamepadButton.North));
            var freshPad = _input.ConsumeFrame();
            Assert.That(freshPad.RagHeld, Is.True);
            Assert.That(freshPad.Commands.Single().Edge, Is.EqualTo(InputEdge.Press));
            Keys();
            var blockedKeyboardRelease = _input.ConsumeFrame();
            Assert.That(blockedKeyboardRelease.RagHeld, Is.True);
            Assert.That(blockedKeyboardRelease.Commands, Is.Empty);
            Pad(new GamepadState());
            Assert.That(_input.ConsumeFrame().Commands.Single().Edge, Is.EqualTo(InputEdge.Release));
        }

        [Test]
        public void AnAnalogButtonHeldThroughResumeMustCrossTheReleaseThresholdBeforeRearming()
        {
            Pad(new GamepadState { leftTrigger = .8f });
            _input.ConsumeFrame();
            _input.SetGameplayPaused(true);
            _input.SetGameplayPaused(false);
            Pad(new GamepadState { leftTrigger = .48f });
            AssertNoActionIntent(_input.ConsumeFrame());
            Pad(new GamepadState { leftTrigger = .7f });
            AssertNoActionIntent(_input.ConsumeFrame());
            Pad(new GamepadState { leftTrigger = .2f });
            AssertNoActionIntent(_input.ConsumeFrame());
            Pad(new GamepadState { leftTrigger = .7f });
            var fresh = _input.ConsumeFrame();
            Assert.That(fresh.Commands.Single().Action, Is.EqualTo(ActionId.PreviousSuit));
            Assert.That(fresh.Commands.Single().Edge, Is.EqualTo(InputEdge.Press));
        }

        [Test]
        public void EnteringPauseDropsUnconsumedReleaseEdgesAndTapHistory()
        {
            Keys(Key.J); _input.ConsumeFrame();
            Keys();
            _input.SetGameplayPaused(true);
            AssertNoActionIntent(_input.ConsumeFrame());
            _input.SetGameplayPaused(false);
            Keys(Key.J);
            Assert.That(_input.ConsumeFrame().Commands.Single().TapCount, Is.EqualTo(1));
        }

        [Test]
        public void RepeatedSynchronizationDoesNotDiscardAFreshPostResumeAction()
        {
            _input.SetGameplayPaused(true);
            _input.SetGameplayPaused(true);
            _input.SetGameplayPaused(false);
            Keys(Key.J);
            _input.SetGameplayPaused(false);
            var fresh = _input.ConsumeFrame();
            Assert.That(fresh.BasicHeld, Is.True);
            Assert.That(fresh.Commands.Single().Edge, Is.EqualTo(InputEdge.Press));
        }

        [Test]
        public void DisconnectingABlockedControllerDoesNotReleaseACancelledCharge()
        {
            Pad(new GamepadState().WithButton(GamepadButton.North));
            _input.ConsumeFrame();
            _input.SetGameplayPaused(true);
            _input.SetGameplayPaused(false);
            InputSystem.RemoveDevice(_pad);
            AssertNoActionIntent(_input.ConsumeFrame());
            Keys(Key.K);
            Assert.That(_input.ConsumeFrame().Commands.Single().Edge, Is.EqualTo(InputEdge.Press));
        }

        [Test]
        public void RegainingFocusDoesNotTurnAnAlreadyHeldButtonIntoANewAction()
        {
            var focus = typeof(JoyInput).GetMethod("OnFocusChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            focus.Invoke(_input, new object[] { false });
            Keys(Key.D, Key.K);
            focus.Invoke(_input, new object[] { true });
            var frame = _input.ConsumeFrame();
            AssertNoActionIntent(frame);
            Assert.That(frame.Move.x, Is.GreaterThan(.9f));
            Keys();
            AssertNoActionIntent(_input.ConsumeFrame());
            Keys(Key.K);
            Assert.That(_input.ConsumeFrame().Commands.Single().Edge, Is.EqualTo(InputEdge.Press));
        }
    }
}
