using System.Linq;
using JoyJoey.Game.Editor;
using JoyJoey.Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class MenuNavigationTests
    {
        [Test]
        public void PauseAndInputPreparationRunBeforeUnityProcessesUiEvents()
        {
            var eventScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/com.unity.ugui/Runtime/UGUI/EventSystem/EventSystem.cs");
            Assert.That(eventScript, Is.Not.Null);
            int unityOrder = MonoImporter.GetExecutionOrder(eventScript);
            var coordinatorOrder = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(typeof(MenuInputCoordinator), typeof(DefaultExecutionOrder));
            var pauseOrder = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(typeof(PauseMenuController), typeof(DefaultExecutionOrder));
            Assert.That(coordinatorOrder.order, Is.LessThan(unityOrder), "Device changes must be prepared before the same-frame Submit is processed.");
            Assert.That(pauseOrder.order, Is.LessThan(coordinatorOrder.order), "Pause must suppress held input before navigation is prepared.");
        }
        [TestCase(true)] [TestCase(false)]
        public void OptionsPagesCoverEveryCategoryButtonAndUseOneEditableFooter(bool main)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(main ? MainMenuBuilder.MenuPrefabPath : PauseMenuBuilder.PrefabPath);
            var component = main ? (Component)root.GetComponent<MainMenuController>() : root.GetComponent<PauseMenuController>();
            var data = new SerializedObject(component);
            var audio = (AudioOptionsController)data.FindProperty("AudioPage").objectReferenceValue;
            var display = (DisplayOptionsController)data.FindProperty("DisplayPage").objectReferenceValue;
            var button = (Button)data.FindProperty("DisplayButton").objectReferenceValue;
            Assert.That(audio.transform.parent, Is.SameAs(button.transform.parent));
            Assert.That(display.transform.parent, Is.SameAs(button.transform.parent));
            Assert.That(audio.transform.GetSiblingIndex(), Is.GreaterThan(button.transform.GetSiblingIndex()));
            Assert.That(display.transform.GetSiblingIndex(), Is.GreaterThan(button.transform.GetSiblingIndex()));
            Assert.That(audio.GetComponent<Image>().raycastTarget, Is.True);
            var coordinators = root.GetComponentsInChildren<MenuInputCoordinator>(true);
            Assert.That(coordinators.Length, Is.EqualTo(1));
            Assert.That(coordinators[0].GetComponent<EventSystem>(), Is.Not.Null);
            Assert.That(coordinators[0].HintRoot, Is.Not.Null);
            Assert.That(coordinators[0].FooterBackButton.navigation.mode, Is.EqualTo(Navigation.Mode.None));
            Assert.That(coordinators[0].HintRoot.activeSelf, Is.False);
        }

        [TestCase(AudioBuilder.OptionsPrefabPath)] [TestCase(DisplayBuilder.OptionsPrefabPath)]
        public void EveryOptionsDropdownHasTheSharedScrollingAndClippingBehavior(string path)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var dropdown in root.GetComponentsInChildren<Dropdown>(true))
            {
                Assert.That(dropdown.GetComponent<MenuDropdown>(), Is.Not.Null, dropdown.name);
                var scroll = dropdown.template.GetComponent<ScrollRect>();
                Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null, dropdown.name);
                Assert.That(scroll.verticalScrollbar.gameObject.activeSelf, Is.True, dropdown.name);
                Assert.That(scroll.scrollSensitivity, Is.GreaterThan(20), dropdown.name);
            }
        }

        [Test]
        public void ContextualActionsReplaceTheCentralBackAndApplyButtons()
        {
            var menu = AssetDatabase.LoadAssetAtPath<GameObject>(MainMenuBuilder.MenuPrefabPath).GetComponent<MainMenuController>();
            var audio = AssetDatabase.LoadAssetAtPath<GameObject>(AudioBuilder.OptionsPrefabPath).GetComponent<AudioOptionsController>();
            var display = AssetDatabase.LoadAssetAtPath<GameObject>(DisplayBuilder.OptionsPrefabPath).GetComponent<DisplayOptionsController>();
            foreach (var button in new[] { menu.ExtrasBackButton, menu.OptionsBackButton, audio.BackButton, display.BackButton, display.ApplyButton })
                Assert.That(button.gameObject.activeSelf, Is.False, button.name);
            Assert.That(display.KeepButton.gameObject.activeSelf, Is.True, "A timed display confirmation retains explicit choices.");
        }

        [Test]
        public void PracticeAndFallbackBindingsDoNotUsePForPause()
        {
            var authored = Resources.Load<InputActionAsset>("JoyJoeyControls");
            var fallback = JoyInput.CreateDefaultAsset();
            try
            {
                Assert.That(authored.FindAction("Gameplay/TogglePause").bindings.Any(binding => binding.path == "<Keyboard>/p"), Is.False);
                Assert.That(fallback.FindAction("Gameplay/TogglePause").bindings.Any(binding => binding.path == "<Keyboard>/p"), Is.False);
            }
            finally { Object.DestroyImmediate(fallback); }
        }
    }
}
