using System.Linq;
using System.Reflection;
using JoyJoey.Game.Editor;
using JoyJoey.Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class MenuInputTests : InputTestFixture
    {
        GameObject _root;
        [TearDown]
        public void CleanupScene()
        { if (_root) Object.DestroyImmediate(_root); }

        [TestCase("DualSense Wireless Controller", "Sony", "DualSenseGamepadHID", MenuInputFamily.PlayStation)]
        [TestCase("Wireless Controller", "", "DualShockGamepadHID", MenuInputFamily.PlayStation)]
        [TestCase("Xbox Wireless Controller", "Microsoft", "XInputController", MenuInputFamily.Xbox)]
        [TestCase("Gamepad", "", "Gamepad", MenuInputFamily.Xbox)]
        public void ActionPromptsFollowTheDetectedControllerFamily(string product, string manufacturer, string layout, MenuInputFamily expected)
            => Assert.That(MenuInputCoordinator.DetectFamily(product, manufacturer, layout), Is.EqualTo(expected));

        [Test]
        public void HeldMovementAndPauseButtonsMustReturnToNeutralBeforeMenuInputArms()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var pad = InputSystem.AddDevice<Gamepad>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Escape)); InputSystem.Update();
            Assert.That(MenuInputCoordinator.AnyInputHeld(), Is.True);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.Start).WithButton(GamepadButton.DpadDown)); InputSystem.Update();
            Assert.That(MenuInputCoordinator.AnyInputHeld(), Is.True);
            InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = new Vector2(0, -.8f) }); InputSystem.Update();
            Assert.That(MenuInputCoordinator.AnyInputHeld(), Is.True);
            InputSystem.QueueStateEvent(pad, new GamepadState()); InputSystem.Update();
            Assert.That(MenuInputCoordinator.AnyInputHeld(), Is.False);
        }

        [Test]
        public void ReleaseGateSuspendsTheInputModuleWithoutDiscardingItsBindings()
        {
            _root = new GameObject("UI gate", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var module = _root.GetComponent<InputSystemUIInputModule>();
            var actions = module.actionsAsset;
            var coordinator = MenuInputCoordinator.Ensure(_root.GetComponent<EventSystem>());
            coordinator.BeginReleaseGate();
            Assert.That(coordinator.InputReady, Is.False);
            Assert.That(module.enabled, Is.False);
            Assert.That(module.actionsAsset, Is.SameAs(actions));
        }

        [Test]
        public void FooterActionsAreEditableAndPointerClickableWithoutJoiningDirectionalNavigation()
        {
            _root = new GameObject("Menu canvas", typeof(RectTransform), typeof(Canvas));
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(_root.transform, false);
            var coordinator = MenuInputCoordinator.Ensure(events.GetComponent<EventSystem>());
            coordinator.SetHints(true, true, true, true);
            Assert.That(coordinator.HintRoot, Is.Not.Null);
            AssertKeyboardHint(coordinator.HintLeft, "<Keyboard>/escape", "Volver");
            AssertKeyboardHint(coordinator.HintRight, "<Keyboard>/j", "Confirmar");
            AssertKeyboardHint(coordinator.ApplyHint, "<Keyboard>/k", "Aplicar cambios");
            coordinator.SetHints(true, true, true, true, controlsDefaults: true);
            AssertKeyboardHint(coordinator.ApplyHint, "<Keyboard>/h", "Aplicar cambios");
            bool back = false, apply = false;
            coordinator.BackRequested += () => back = true;
            coordinator.ApplyRequested += () => apply = true;
            coordinator.FooterBackButton.onClick.Invoke();
            coordinator.FooterApplyButton.onClick.Invoke();
            Assert.That(back && apply, Is.True);
            foreach (var button in new[] { coordinator.FooterBackButton, coordinator.FooterConfirmButton, coordinator.FooterApplyButton })
                Assert.That(button.navigation.mode, Is.EqualTo(Navigation.Mode.None));
            coordinator.SetHints(false, false, false, false);
            Assert.That(coordinator.HintRoot.activeSelf, Is.False);
        }

        static void AssertKeyboardHint(Text label, string path, string caption)
        {
            var expected = InputPromptSprites.Resolve(path, MenuInputFamily.KeyboardMouse);
            Assert.That(expected, Is.Not.Null);
            var prompt = label.GetComponentInParent<InputPromptView>();
            Assert.That(prompt, Is.Not.Null);
            Assert.That(prompt.ControlPath, Is.EqualTo(path));
            Assert.That(prompt.DisplayedSprite, Is.SameAs(expected));
            Assert.That(label.text, Is.EqualTo(caption));
        }

        [Test]
        public void InputPromptKeepsItsCaptionAndUsesTextWhenTheGlyphIsMissing()
        {
            _root = new GameObject("Prompt canvas", typeof(RectTransform), typeof(Canvas));
            var label = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(_root.transform, false);
            var prompt = InputPromptView.Attach(label);
            prompt.Set("<Keyboard>/notARealKey", MenuInputFamily.KeyboardMouse, "Tecla", "Volver");
            Assert.That(prompt.DisplayedSprite, Is.Null);
            Assert.That(label.text, Is.EqualTo("Tecla  Volver"));
            prompt.Set("<Keyboard>/escape", MenuInputFamily.KeyboardMouse, "Esc", "Volver");
            AssertKeyboardHint(label, "<Keyboard>/escape", "Volver");
        }

        [Test]
        public void PersistentUiActionsKeepJAndStandardKeyboardAndGamepadSubmission()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(MenuUIFactory.UIActionsPath);
            Assert.That(actions, Is.Not.Null);
            var submit = actions.FindAction("UI/Submit", true);
            Assert.That(submit.bindings.Any(binding => binding.path == "<Keyboard>/j"), Is.True);
            Assert.That(submit.bindings.Any(binding => binding.path == "*/{Submit}"), Is.True);
            var navigate = actions.FindAction("UI/Navigate", true);
            Assert.That(navigate.bindings.Any(binding => binding.path == "<Keyboard>/w"), Is.True);
            Assert.That(navigate.bindings.Any(binding => binding.path == "<Gamepad>/dpad"), Is.True);
        }

        [Test]
        public void ChangingPagesClearsOutsideSelectionAndControllerRestorationUsesTheVisiblePage()
        {
            _root = new GameObject("Focus canvas", typeof(RectTransform), typeof(Canvas));
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(_root.transform, false);
            var system = events.GetComponent<EventSystem>();
            var coordinator = MenuInputCoordinator.Ensure(system);
            var previous = new GameObject("Background button", typeof(RectTransform), typeof(Button));
            previous.transform.SetParent(_root.transform, false);
            var page = new GameObject("Visible page", typeof(RectTransform)); page.transform.SetParent(_root.transform, false);
            var first = new GameObject("First page control", typeof(RectTransform), typeof(Button)); first.transform.SetParent(page.transform, false);
            system.SetSelectedGameObject(previous);
            coordinator.SetFocusScope(page.transform, first.GetComponent<Button>());
            Assert.That(system.currentSelectedGameObject, Is.Null, "Pointer mode should clear background selection without creating another highlight.");
            typeof(MenuInputCoordinator).GetMethod("RestoreNavigationSelection", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(coordinator, null);
            Assert.That(system.currentSelectedGameObject, Is.SameAs(first), "Controller restoration belongs to the open page, not the background.");
        }

        [Test]
        public void ADefaultOutsideTheOpenPageCannotBecomeItsSelection()
        {
            _root = new GameObject("Focus canvas", typeof(RectTransform), typeof(Canvas));
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)); events.transform.SetParent(_root.transform, false);
            var coordinator = MenuInputCoordinator.Ensure(events.GetComponent<EventSystem>());
            var outside = new GameObject("Outside", typeof(RectTransform), typeof(Button)); outside.transform.SetParent(_root.transform, false);
            var page = new GameObject("Page", typeof(RectTransform)); page.transform.SetParent(_root.transform, false);
            coordinator.SetFocusScope(page.transform, outside.GetComponent<Button>());
            Assert.That(coordinator.FocusScope, Is.SameAs(page.transform));
            Assert.That(coordinator.DefaultSelection, Is.Null);
        }
    }
}
