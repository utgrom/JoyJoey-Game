using JoyJoey.Game.Editor;
using JoyJoey.Game.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace JoyJoey.Sandbox.Tests
{
    public sealed class MenuDropdownTests
    {
        GameObject _root;
        Dropdown _dropdown;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Dropdown fixture", typeof(RectTransform), typeof(Canvas));
            var generated = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            generated.transform.SetParent(_root.transform, false);
            _dropdown = generated.GetComponent<Dropdown>();
            MenuDropdownAuthoring.Configure(_dropdown);
        }

        [TearDown]
        public void TearDown() { if (_root) Object.DestroyImmediate(_root); }

        [Test]
        public void PopupHasAClippedReadableListAndAnInteractiveScrollbar()
        {
            var scroll = _dropdown.template.GetComponent<ScrollRect>();
            Assert.That(scroll.content, Is.Not.Null);
            Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
            Assert.That(scroll.viewport.GetComponent<Mask>(), Is.Null, "The popup must not depend on a missing stencil sprite.");
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.scrollSensitivity, Is.GreaterThanOrEqualTo(44), "One wheel step should traverse at least one row.");
            Assert.That(scroll.verticalScrollbarVisibility, Is.EqualTo(ScrollRect.ScrollbarVisibility.Permanent));
            Assert.That(scroll.verticalScrollbar.interactable, Is.True);
            Assert.That(scroll.verticalScrollbar.gameObject.activeSelf, Is.True);
            Assert.That(scroll.verticalScrollbar.handleRect.rect.width, Is.GreaterThan(0));
            Assert.That(_dropdown.itemText.enabled && _dropdown.itemText.gameObject.activeSelf, Is.True);
            Assert.That(_dropdown.itemText.color.grayscale, Is.GreaterThan(.9f));
            Assert.That(_dropdown.itemText.font, Is.Not.Null);
            Assert.That(_dropdown.itemText.raycastTarget, Is.False);
            Assert.That(_dropdown.GetComponent<MenuDropdown>(), Is.Not.Null);
        }

        [Test]
        public void ReapplyingPopupLayoutPreservesOptionsValueAndCallbacks()
        {
            _dropdown.ClearOptions();
            _dropdown.AddOptions(new System.Collections.Generic.List<string> { "Sistema", "Auriculares", "Monitor" });
            _dropdown.value = 1;
            int changed = -1;
            _dropdown.onValueChanged.AddListener(value => changed = value);
            MenuDropdownAuthoring.Configure(_dropdown);
            MenuDropdownAuthoring.Configure(_dropdown);
            Assert.That(_dropdown.value, Is.EqualTo(1));
            Assert.That(_dropdown.options.Count, Is.EqualTo(3));
            Assert.That(_dropdown.options[1].text, Is.EqualTo("Auriculares"));
            Assert.That(_dropdown.GetComponents<MenuDropdown>().Length, Is.EqualTo(1));
            Assert.That(_dropdown.template.GetComponentsInChildren<RectMask2D>(true).Length, Is.EqualTo(1));
            _dropdown.value = 2;
            Assert.That(changed, Is.EqualTo(2));
        }

        [Test]
        public void NavigationRevealsASelectedLowerRowAndCanReturnToTheFirstRow()
        {
            var scroll = _dropdown.template.GetComponent<ScrollRect>();
            scroll.viewport.anchorMin = scroll.viewport.anchorMax = Vector2.one * .5f;
            scroll.viewport.pivot = new Vector2(.5f, 1);
            scroll.viewport.sizeDelta = new Vector2(400, 100);
            scroll.content.anchorMin = new Vector2(0, 1);
            scroll.content.anchorMax = Vector2.one;
            scroll.content.pivot = new Vector2(.5f, 1);
            scroll.content.anchoredPosition = Vector2.zero;
            scroll.content.sizeDelta = new Vector2(0, 500);
            var lower = CreateRow(scroll.content, -300);
            var first = CreateRow(scroll.content, -22);

            Assert.That(MenuDropdown.EnsureVisible(scroll, lower), Is.True);
            AssertVisible(scroll.viewport, lower);
            float preserved = scroll.content.anchoredPosition.y;
            Assert.That(MenuDropdown.EnsureVisible(scroll, lower), Is.False);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(preserved));
            Assert.That(MenuDropdown.EnsureVisible(scroll, first), Is.True);
            AssertVisible(scroll.viewport, first);
            Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0).Within(.01f));
        }

        static RectTransform CreateRow(Transform parent, float y)
        {
            var row = new GameObject("Option", typeof(RectTransform)).GetComponent<RectTransform>();
            row.SetParent(parent, false);
            row.anchorMin = new Vector2(0, 1); row.anchorMax = Vector2.one;
            row.pivot = Vector2.one * .5f; row.sizeDelta = new Vector2(0, 44);
            row.anchoredPosition = new Vector2(0, y);
            return row;
        }
        static void AssertVisible(RectTransform viewport, RectTransform row)
        {
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, row);
            Assert.That(bounds.min.y, Is.GreaterThanOrEqualTo(viewport.rect.yMin - .01f));
            Assert.That(bounds.max.y, Is.LessThanOrEqualTo(viewport.rect.yMax + .01f));
        }
    }
}
