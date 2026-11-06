using System.Linq;
using System.Reflection;
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
            var controlsOrder = (DefaultExecutionOrder)System.Attribute.GetCustomAttribute(typeof(ControlsOptionsController), typeof(DefaultExecutionOrder));
            Assert.That(coordinatorOrder.order, Is.LessThan(unityOrder), "Device changes must be prepared before the same-frame Submit is processed.");
            Assert.That(pauseOrder.order, Is.LessThan(coordinatorOrder.order), "Pause must suppress held input before navigation is prepared.");
            Assert.That(controlsOrder.order, Is.InRange(coordinatorOrder.order + 1, unityOrder - 1), "Controls show the active device before the same-frame Submit.");
        }

        [TestCase(true)] [TestCase(false)]
        public void ControlsCategoryAttachesToExistingOptionsAndEntersVerticalNavigation(bool main)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(main ? MainMenuBuilder.MenuPrefabPath : PauseMenuBuilder.PrefabPath);
            var root = Object.Instantiate(prefab);
            try
            {
                var component = main ? (Component)root.GetComponent<MainMenuController>() : root.GetComponent<PauseMenuController>();
                var data = new SerializedObject(component);
                var options = (GameObject)data.FindProperty("OptionsPanel").objectReferenceValue;
                var audio = (Button)data.FindProperty("AudioButton").objectReferenceValue;
                var display = (Button)data.FindProperty("DisplayButton").objectReferenceValue;
                var back = (Button)data.FindProperty(main ? "OptionsBackButton" : "BackButton").objectReferenceValue;
                bool clicked = false;
                var parameters = new object[] { options, audio, display, back, (System.Action)(() => clicked = true), null };
                var attach = typeof(ControlsOptionsController).GetMethod("AddTo", BindingFlags.NonPublic | BindingFlags.Static);
                var page = (ControlsOptionsController)attach.Invoke(null, parameters);
                var category = (Button)parameters[5];

                Assert.That(category.transform.parent, Is.SameAs(options.transform));
                Assert.That(category.GetComponentInChildren<Text>(true).text, Is.EqualTo("Controles"));
                Assert.That(display.navigation.selectOnDown, Is.SameAs(category));
                Assert.That(category.navigation.selectOnUp, Is.SameAs(display));
                var displayRect = (RectTransform)display.transform;
                var categoryRect = (RectTransform)category.transform;
                Assert.That(displayRect.anchoredPosition.y - categoryRect.anchoredPosition.y,
                    Is.GreaterThan((displayRect.rect.height + categoryRect.rect.height) * .5f));
                Assert.That(page.transform.GetSiblingIndex(), Is.GreaterThan(category.transform.GetSiblingIndex()));
                Assert.That(page.GetComponent<Image>().raycastTarget, Is.True);
                Assert.That(page.gameObject.activeSelf, Is.False);

                category.onClick.Invoke();
                Assert.That(clicked, Is.True);
            }
            finally { Object.DestroyImmediate(root); }
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
