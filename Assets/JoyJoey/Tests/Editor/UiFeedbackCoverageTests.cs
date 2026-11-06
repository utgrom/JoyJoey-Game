using System.Reflection;
using JoyJoey.Game.Audio;
using JoyJoey.Game.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class UiFeedbackCoverageTests
    {
        GameObject _root;

        [TearDown]
        public void Cleanup()
        { if (_root) Object.DestroyImmediate(_root); }

        [TestCase(MainMenuBuilder.MenuPrefabPath)]
        [TestCase(PauseMenuBuilder.PrefabPath)]
        [TestCase(AudioBuilder.OptionsPrefabPath)]
        [TestCase(DisplayBuilder.OptionsPrefabPath)]
        public void RuntimeCoverageIncludesEveryAuthoredPageWithoutRegeneratingItsPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            _root = Object.Instantiate(prefab);
            UiAudioFeedback.EnsureForCanvas(_root);
            UiAudioFeedback.EnsureForCanvas(_root);
            var controls = _root.GetComponentsInChildren<Selectable>(true);
            Assert.That(controls, Is.Not.Empty);
            foreach (var control in controls)
                Assert.That(control.GetComponents<UiAudioFeedback>().Length, Is.EqualTo(1), control.name);
        }

        [Test]
        public void InactiveDropdownItemTemplatesPassFeedbackToNewPopupOptions()
        {
            _root = new GameObject("Dropdown coverage", typeof(RectTransform));
            var generated = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            generated.transform.SetParent(_root.transform, false);
            var dropdown = generated.GetComponent<Dropdown>();
            UiAudioFeedback.EnsureForCanvas(_root);
            var item = dropdown.itemText.GetComponentInParent<Toggle>(true);
            Assert.That(item.GetComponent<UiAudioFeedback>(), Is.Not.Null);
            var popupOption = Object.Instantiate(item.gameObject, _root.transform);
            Assert.That(popupOption.GetComponents<UiAudioFeedback>().Length, Is.EqualTo(1));
        }

        [Test]
        public void AddingFeedbackKeepsTheExistingValueEventsAndUserCallbacks()
        {
            _root = DefaultControls.CreateSlider(new DefaultControls.Resources());
            var slider = _root.GetComponent<Slider>();
            var existingEvent = slider.onValueChanged;
            int changed = 0;
            slider.onValueChanged.AddListener(value => changed++);
            UiAudioFeedback.EnsureForCanvas(_root);
            UiAudioFeedback.EnsureForCanvas(_root);
            Assert.That(slider.onValueChanged, Is.SameAs(existingEvent));
            slider.value = slider.value > .5f ? .25f : .75f;
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public void AuthoringFeedbackInEditModeDoesNotCreateAnAudioService()
        {
            var previousService = GameAudio.Instance;
            _root = new GameObject("Authored button", typeof(RectTransform), typeof(Button));
            UiAudioFeedback.EnsureForCanvas(_root);
            var feedback = _root.GetComponent<UiAudioFeedback>();
            UiAudioFeedback.PlayMove();
            UiAudioFeedback.PlayConfirm();
            feedback.OnSubmit(new BaseEventData(null));
            Assert.That(GameAudio.Instance, Is.SameAs(previousService));
        }

        [Test]
        public void DeliveredConfirmationRemainsEligibleAfterItsPageIsHidden()
        {
            var previousService = GameAudio.Instance;
            _root = new GameObject("Transitioning button", typeof(RectTransform), typeof(Button));
            UiAudioFeedback.EnsureForCanvas(_root);
            var feedback = _root.GetComponent<UiAudioFeedback>();
            typeof(UiAudioFeedback).GetMethod("BindControls", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(feedback, null);
            _root.SetActive(false); // The Button handled its submit first and changed pages.
            var eligible = typeof(UiAudioFeedback).GetProperty("CanConfirmDeliveredEvent", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((bool)eligible.GetValue(feedback), Is.True);
            Assert.That(feedback, Is.InstanceOf<ICancelHandler>());
            feedback.OnSubmit(new BaseEventData(null));
            feedback.OnCancel(new BaseEventData(null));
            feedback.OnPointerClick(new PointerEventData(null) { button = PointerEventData.InputButton.Left });
            Assert.That(GameAudio.Instance, Is.SameAs(previousService));
            _root.GetComponent<Button>().interactable = false;
            Assert.That((bool)eligible.GetValue(feedback), Is.False);
        }
    }
}
