using System.Linq;
using JoyJoey.Game.Display;
using JoyJoey.Game.Editor;
using JoyJoey.Game.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class DisplayAuthoringTests
    {
        [Test]
        public void DisplayServiceHasAnEditableFifteenSecondConfirmationWithoutAnotherCamera()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DisplayBuilder.RigPrefabPath);
            Assert.That(prefab, Is.Not.Null, "Generate stage 6 assets before checking authoring.");
            var rig = prefab.GetComponent<GameDisplay>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.ConfirmationSeconds, Is.EqualTo(15));
            Assert.That(prefab.GetComponentsInChildren<Camera>(true), Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<AudioListener>(true), Is.Empty);
        }

        [Test]
        public void BothPagesAndTheConfirmationHavePersistentEditableNavigableControls()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DisplayBuilder.OptionsPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            var page = prefab.GetComponent<DisplayOptionsController>();
            Assert.That(page, Is.Not.Null);
            Assert.That(page.Content, Is.Not.Null);
            Assert.That(page.Content.parent, Is.SameAs(prefab.transform));
            Assert.That(page.Content.sizeDelta, Is.EqualTo(new Vector2(1440, 900)));
            Assert.That(page.Content.anchorMin, Is.EqualTo(Vector2.one * .5f));
            Assert.That(page.Content.anchorMax, Is.EqualTo(Vector2.one * .5f));
            Assert.That(page.ConfirmPanel.transform.IsChildOf(page.Content), Is.True);
            Assert.That(prefab.activeSelf, Is.False);
            Assert.That(page.BasicPanel.activeSelf, Is.True);
            Assert.That(page.AdvancedPanel.activeSelf, Is.False);
            Assert.That(page.ConfirmPanel.activeSelf, Is.False);
            foreach (var dropdown in new[] { page.ModeDropdown, page.ResolutionDropdown, page.FpsDropdown,
                page.VSyncDropdown, page.RenderScaleDropdown })
            {
                Assert.That(dropdown, Is.Not.Null);
                Assert.That(dropdown.template, Is.Not.Null);
                Assert.That(dropdown.options, Is.Not.Empty);
                Assert.That(dropdown.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
                Assert.That(dropdown.navigation.selectOnUp, Is.Not.Null);
                Assert.That(dropdown.navigation.selectOnDown, Is.Not.Null);
            }
            Assert.That(page.CalibrationPattern, Is.Not.Null);
            foreach (var text in new[] { page.BrightnessValue, page.ContrastValue, page.GammaValue,
                page.ResolutionInfo, page.RenderInfo, page.VSyncInfo, page.StatusText, page.ConfirmationText })
                Assert.That(text, Is.Not.Null);
            foreach (var button in new[] { page.BasicButton, page.AdvancedButton, page.RevertButton,
                page.KeepButton, page.CancelPreviewButton, page.ResetImageButton })
            {
                Assert.That(button, Is.Not.Null);
                Assert.That(button.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
                Assert.That(button.navigation.selectOnUp, Is.Not.Null);
                Assert.That(button.navigation.selectOnDown, Is.Not.Null);
            }
            Assert.That(page.KeepButton.navigation.selectOnDown, Is.SameAs(page.CancelPreviewButton));
            Assert.That(page.ApplyButton.gameObject.activeSelf, Is.False, "Apply is available through the contextual action.");
            Assert.That(page.BackButton.gameObject.activeSelf, Is.False, "Back is available through the contextual action.");
            Assert.That(page.CancelPreviewButton.navigation.selectOnDown, Is.SameAs(page.KeepButton));
            Assert.That(page.ConfirmPanel.GetComponent<Image>().raycastTarget, Is.True);
            Assert.That(page.BasicPanel.GetComponent<Image>().raycastTarget, Is.False);
            Assert.That(page.AdvancedPanel.GetComponent<Image>().raycastTarget, Is.False);
            foreach (var selectable in prefab.GetComponentsInChildren<Selectable>(true))
                Assert.That(selectable.transform.IsChildOf(page.Content), Is.True, selectable.name);
        }

        [TestCase(3440, 1440)]
        [TestCase(5120, 1440)]
        [TestCase(1920, 1080)]
        [TestCase(1024, 768)]
        public void DisplayContentFitsBothAxesIncludingUltrawideWindows(int width, int height)
        {
            // Reproduce the shared canvas's .5 width/height match, then check the local
            // display-page fit independently of the pixel resolution of the desktop.
            float canvasScale = Mathf.Sqrt(width / 1440f * (height / 900f));
            float availableWidth = width / canvasScale, availableHeight = height / canvasScale;
            float scale = DisplayOptionsController.CalculateContentScale(availableWidth, availableHeight);
            Assert.That(scale, Is.GreaterThan(0).And.LessThanOrEqualTo(1));
            Assert.That(1440 * scale, Is.LessThanOrEqualTo(availableWidth + .001f));
            Assert.That(900 * scale, Is.LessThanOrEqualTo(availableHeight + .001f));
            if (width > height * 2) Assert.That(scale, Is.LessThan(1));
        }

        [Test]
        public void CalibrationSlidersSupportNeutralValuesAndBothDirectionsOfAdjustment()
        {
            var page = AssetDatabase.LoadAssetAtPath<GameObject>(DisplayBuilder.OptionsPrefabPath).GetComponent<DisplayOptionsController>();
            CheckSlider(page.BrightnessSlider, -.5f, .5f, 0);
            CheckSlider(page.ContrastSlider, .5f, 1.5f, 1);
            CheckSlider(page.GammaSlider, .6f, 1.6f, 1);
        }
        static void CheckSlider(Slider slider, float min, float max, float neutral)
        {
            Assert.That(slider, Is.Not.Null);
            Assert.That(slider.minValue, Is.EqualTo(min));
            Assert.That(slider.maxValue, Is.EqualTo(max));
            Assert.That(slider.value, Is.EqualTo(neutral));
            Assert.That(slider.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void SwitchingTabsKeepsHiddenControlsOutOfKeyboardNavigation(bool advanced)
        {
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DisplayBuilder.OptionsPrefabPath));
            try
            {
                var page = instance.GetComponent<DisplayOptionsController>();
                // The prefab stays inactive, so this verifies navigation without creating a display
                // service or reading the person's saved preferences in an EditMode test.
                page.ShowAdvanced(advanced);
                Assert.That(page.BasicPanel.activeSelf, Is.EqualTo(!advanced));
                Assert.That(page.AdvancedPanel.activeSelf, Is.EqualTo(advanced));
                Selectable first = advanced ? page.RenderScaleDropdown : page.ModeDropdown;
                var current = first;
                var hidden = advanced ? page.BasicPanel.transform : page.AdvancedPanel.transform;
                int visited = 0;
                do
                {
                    Assert.That(current, Is.Not.Null);
                    Assert.That(current.transform.IsChildOf(hidden), Is.False, current.name);
                    Assert.That(current.gameObject.activeSelf, Is.True, current.name);
                    current = current.navigation.selectOnDown;
                    visited++;
                } while (current != first && visited < 20);
                Assert.That(current, Is.SameAs(first), "The visible controls must form a complete navigation loop.");
                Assert.That(visited, Is.GreaterThanOrEqualTo(7));
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MenuAndPauseKeepAudioAndAddANavigableDisplayPage(bool mainMenu)
        {
            string path = mainMenu ? MainMenuBuilder.MenuPrefabPath : PauseMenuBuilder.PrefabPath;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var component = mainMenu ? (Component)prefab.GetComponentInChildren<MainMenuController>(true) :
                prefab.GetComponentInChildren<PauseMenuController>(true);
            Assert.That(component, Is.Not.Null);
            var serialized = new SerializedObject(component);
            var page = serialized.FindProperty("DisplayPage").objectReferenceValue as DisplayOptionsController;
            var button = serialized.FindProperty("DisplayButton").objectReferenceValue as Button;
            Assert.That(page, Is.Not.Null);
            Assert.That(button, Is.Not.Null);
            Assert.That(serialized.FindProperty("AudioPage").objectReferenceValue, Is.Not.Null);
            Assert.That(serialized.FindProperty("AudioButton").objectReferenceValue, Is.Not.Null);
            Assert.That(page.gameObject.activeSelf, Is.False);
            Assert.That(button.navigation.mode, Is.EqualTo(Navigation.Mode.Explicit));
            Assert.That(button.navigation.selectOnUp, Is.Not.Null);
            Assert.That(button.navigation.selectOnDown, Is.Not.Null);
        }

        [TestCase("Assets/Settings/PC_Renderer.asset")]
        [TestCase("Assets/Settings/Mobile_Renderer.asset")]
        public void EachRendererHasOnePersistentCalibrationFeatureAndItsShaderIsIncluded(string path)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            Assert.That(renderer, Is.Not.Null);
            var features = renderer.rendererFeatures.OfType<CalibrationRendererFeature>().ToArray();
            Assert.That(features.Length, Is.EqualTo(1));
            Assert.That(AssetDatabase.Contains(features[0]), Is.True);
            Assert.That(features[0].isActive, Is.True);
            Assert.That(features[0].Material, Is.SameAs(AssetDatabase.LoadAssetAtPath<Material>(CalibrationBuilder.MaterialPath)));
            Assert.That(features[0].Material.shader, Is.SameAs(AssetDatabase.LoadAssetAtPath<Shader>(CalibrationBuilder.ShaderPath)));
            Assert.That(features[0].Material.shader.name, Is.EqualTo("Hidden/JoyJoey/DisplayCalibration"));
        }
    }

    public sealed class DisplayCalibrationMathTests
    {
        [TestCase(0)]
        [TestCase(.18f)]
        [TestCase(.5f)]
        [TestCase(1)]
        public void NeutralDisplayCorrectionPreservesTheImage(float value)
            => Assert.That(CalibrationMath.ApplyDisplay(value, 0, 1, 1), Is.EqualTo(value).Within(.00001f));

        [Test]
        public void ControlsHaveTheExpectedDirectionAndDoNotOverflowTheDisplayRange()
        {
            Assert.That(CalibrationMath.ApplyDisplay(.3f, .2f, 1, 1), Is.GreaterThan(.3f));
            Assert.That(CalibrationMath.ApplyDisplay(.3f, -.2f, 1, 1), Is.LessThan(.3f));
            Assert.That(CalibrationMath.ApplyDisplay(.8f, 0, 1.5f, 1), Is.GreaterThan(.8f));
            Assert.That(CalibrationMath.ApplyDisplay(.2f, 0, 1.5f, 1), Is.LessThan(.2f));
            Assert.That(CalibrationMath.ApplyDisplay(.25f, 0, 1, 1.5f), Is.GreaterThan(.25f));
            Assert.That(CalibrationMath.ApplyDisplay(1, .5f, 1.5f, .6f), Is.EqualTo(1));
            Assert.That(CalibrationMath.ApplyDisplay(0, -.5f, 1.5f, 1.6f), Is.EqualTo(0));
        }

        [Test]
        public void LinearImageCorrectionPreservesNeutralColorAndAlpha()
        {
            var source = new Color(.1f, .35f, .8f, .6f);
            var corrected = CalibrationMath.ApplyLinear(source, 0, 1, 1);
            Assert.That(corrected.r, Is.EqualTo(source.r).Within(.0001f));
            Assert.That(corrected.g, Is.EqualTo(source.g).Within(.0001f));
            Assert.That(corrected.b, Is.EqualTo(source.b).Within(.0001f));
            Assert.That(corrected.a, Is.EqualTo(source.a));
        }
    }
}
