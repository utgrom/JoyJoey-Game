using System.Globalization;
using JoyJoey.Game.Display;
using JoyJoey.Game.Editor;
using JoyJoey.Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class DisplayRefinementsTests
    {
        [TestCase(false)] [TestCase(true)]
        public void BothTabsRemainReachableWithHorizontalNavigation(bool advanced)
        {
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DisplayBuilder.OptionsPrefabPath));
            try
            {
                var page = root.GetComponent<DisplayOptionsController>();
                page.ShowAdvanced(advanced);
                Assert.That(page.BasicButton.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
                Assert.That(page.AdvancedButton.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
                Assert.That(page.BasicButton.navigation.selectOnRight, Is.SameAs(page.AdvancedButton));
                Assert.That(page.AdvancedButton.navigation.selectOnLeft, Is.SameAs(page.BasicButton));
                Assert.That(page.IsAdvanced, Is.EqualTo(advanced), "Configuring tab focus must not activate another page.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MovingHorizontallyFocusesTheOtherTabWithoutActivatingIt()
        {
            var previousSystem = EventSystem.current;
            var events = new GameObject("Display tab navigation", typeof(EventSystem));
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DisplayBuilder.OptionsPrefabPath));
            try
            {
                root.SetActive(true);
                var page = root.GetComponent<DisplayOptionsController>();
                page.ShowAdvanced(false);
                var system = events.GetComponent<EventSystem>();
                EventSystem.current = system;
                system.SetSelectedGameObject(page.BasicButton.gameObject);
                page.BasicButton.OnMove(new AxisEventData(system) { moveDir = UnityEngine.EventSystems.MoveDirection.Right, moveVector = Vector2.right });
                Assert.That(system.currentSelectedGameObject, Is.SameAs(page.AdvancedButton.gameObject));
                Assert.That(page.IsAdvanced, Is.False, "A horizontal navigation input only changes selection; Submit activates the tab.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(events);
                if (previousSystem) EventSystem.current = previousSystem;
            }
        }

        [Test]
        public void VSyncShowsMonitorTargetWithoutReplacingTheManualFrameCap()
        {
            var root = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            try
            {
                var dropdown = root.GetComponent<Dropdown>();
                var preferences = DisplayPreferences.Defaults();
                preferences.FpsCap = 165;
                DisplayOptionsController.ConfigureFpsPresentation(dropdown, preferences, 59.94);
                Assert.That(dropdown.value, Is.EqualTo(6));
                preferences.VSync = true;
                DisplayOptionsController.ConfigureFpsPresentation(dropdown, preferences, 59.94);
                Assert.That(dropdown.interactable, Is.False);
                Assert.That(dropdown.options.Count, Is.EqualTo(1));
                Assert.That(dropdown.options[0].text, Does.Contain("VSync"));
                Assert.That(preferences.FpsCap, Is.EqualTo(165));
                Assert.That(DisplayPreferences.IsValid(preferences), Is.True, "The monitor's fractional rate is display information, not a new saved cap.");
                preferences.VSync = false;
                DisplayOptionsController.ConfigureFpsPresentation(dropdown, preferences, 59.94);
                Assert.That(dropdown.interactable, Is.True);
                Assert.That(dropdown.options.Count, Is.EqualTo(8));
                Assert.That(dropdown.options[dropdown.value].text, Is.EqualTo("165"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void VSyncLabelPreservesFractionalRefreshAndHandlesUnavailableData()
        {
            var format = CultureInfo.GetCultureInfo("es-AR");
            Assert.That(DisplayOptionsController.FormatVSyncRate(60, format), Is.EqualTo("60 FPS (VSync)"));
            Assert.That(DisplayOptionsController.FormatVSyncRate(60000.0 / 1001, format), Is.EqualTo("59,94 FPS (VSync)"));
            Assert.That(DisplayOptionsController.FormatVSyncRate(0, format), Is.EqualTo("Controlado por VSync"));
            Assert.That(DisplayOptionsController.FormatVSyncRate(double.NaN, format), Is.EqualTo("Controlado por VSync"));
        }

        [Test]
        public void UpdatingMonitorReferenceDoesNotEmitAUserCapChange()
        {
            var root = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            try
            {
                var dropdown = root.GetComponent<Dropdown>();
                var preferences = DisplayPreferences.Defaults(); preferences.VSync = true;
                int edits = 0;
                dropdown.onValueChanged.AddListener(_ => edits++);
                DisplayOptionsController.ConfigureFpsPresentation(dropdown, preferences, 60);
                DisplayOptionsController.ConfigureFpsPresentation(dropdown, preferences, 144);
                Assert.That(edits, Is.Zero);
                Assert.That(dropdown.options[0].text, Is.EqualTo("144 FPS (VSync)"));
                Assert.That(preferences.FpsCap, Is.EqualTo(120));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
