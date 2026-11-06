using System;
using JoyJoey.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace JoyJoey.Game.Editor
{
    /// <summary>Shared popup layout for every generated options list, including runtime audio devices.</summary>
    public static class MenuDropdownAuthoring
    {
        public const float RowHeight = 44;
        public const float WheelSensitivity = 48;
        public const float ScrollbarWidth = 18;
        static readonly Color Popup = new Color(.10f, .12f, .18f);
        static readonly Color Selected = new Color(.65f, .29f, .18f);

        /// <summary>Normalizes generated dropdowns in place, retaining option data and event references.</summary>
        public static void Configure(Dropdown dropdown)
        {
            if (!dropdown || !dropdown.template || !dropdown.itemText)
                throw new ArgumentException("El desplegable necesita una plantilla y un texto de opción válidos.", nameof(dropdown));
            var template = dropdown.template;
            var toggle = dropdown.itemText.GetComponentInParent<Toggle>(true);
            var scroll = template.GetComponent<ScrollRect>();
            if (!toggle || !scroll || !scroll.viewport || !scroll.content)
                throw new ArgumentException("La plantilla del desplegable necesita Toggle, ScrollRect, viewport y contenido.", nameof(dropdown));

            if (!dropdown.GetComponent<MenuDropdown>()) dropdown.gameObject.AddComponent<MenuDropdown>();
            dropdown.alphaFadeSpeed = .08f;
            StyleText(dropdown.captionText, 21);
            StyleText(dropdown.itemText, 21);
            dropdown.itemText.gameObject.SetActive(true);
            dropdown.itemText.enabled = true;
            dropdown.itemText.alignment = TextAnchor.MiddleLeft;

            template.anchorMin = new Vector2(0, 0);
            template.anchorMax = new Vector2(1, 0);
            template.pivot = new Vector2(.5f, 1);
            template.anchoredPosition = new Vector2(0, -4);
            template.sizeDelta = new Vector2(0, 264);
            template.localScale = Vector3.one;
            if (template.TryGetComponent<Image>(out var backdrop))
            { backdrop.enabled = true; backdrop.sprite = null; backdrop.type = Image.Type.Simple; backdrop.color = Popup; backdrop.raycastTarget = true; }

            // RectMask2D clips labels and backgrounds without depending on a stencil mask sprite.
            // DefaultControls was created without a Resources.mask sprite in the prototype.
            var viewport = scroll.viewport;
            var stencil = viewport.GetComponent<Mask>();
            if (stencil) UnityEngine.Object.DestroyImmediate(stencil);
            if (!viewport.GetComponent<RectMask2D>()) viewport.gameObject.AddComponent<RectMask2D>();
            if (viewport.TryGetComponent<Image>(out var viewportImage))
            { viewportImage.sprite = null; viewportImage.color = Color.clear; viewportImage.raycastTarget = true; }
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0, 1);
            viewport.offsetMin = new Vector2(4, 4);
            viewport.offsetMax = new Vector2(-ScrollbarWidth - 8, -4);

            var content = scroll.content;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, RowHeight);
            var item = (RectTransform)toggle.transform;
            item.anchorMin = new Vector2(0, .5f);
            item.anchorMax = new Vector2(1, .5f);
            item.pivot = Vector2.one * .5f;
            item.anchoredPosition = Vector2.zero;
            item.sizeDelta = new Vector2(0, RowHeight);
            toggle.navigation = new Navigation { mode = Navigation.Mode.Explicit };
            if (toggle.targetGraphic is Image itemBackground)
            {
                itemBackground.sprite = null;
                itemBackground.type = Image.Type.Simple;
                itemBackground.color = Color.white;
                itemBackground.raycastTarget = true;
                Stretch(itemBackground.rectTransform, Vector2.zero, Vector2.zero);
            }
            var itemColors = toggle.colors;
            itemColors.normalColor = Popup;
            itemColors.highlightedColor = Selected;
            itemColors.selectedColor = Selected;
            itemColors.pressedColor = MenuUIFactory.Orange;
            itemColors.disabledColor = new Color(.11f, .12f, .16f);
            itemColors.colorMultiplier = 1;
            itemColors.fadeDuration = .05f;
            toggle.colors = itemColors;
            if (toggle.graphic is Image checkmark)
            {
                checkmark.sprite = null;
                checkmark.type = Image.Type.Simple;
                checkmark.color = MenuUIFactory.Orange;
                checkmark.raycastTarget = false;
                checkmark.rectTransform.anchorMin = checkmark.rectTransform.anchorMax = new Vector2(0, .5f);
                checkmark.rectTransform.sizeDelta = new Vector2(4, RowHeight - 16);
                checkmark.rectTransform.anchoredPosition = new Vector2(7, 0);
            }
            Stretch(dropdown.itemText.rectTransform, new Vector2(20, 3), new Vector2(-10, -3));
            dropdown.itemText.transform.SetAsLastSibling();

            var scrollbar = scroll.verticalScrollbar;
            if (!scrollbar)
            {
                var created = DefaultControls.CreateScrollbar(new DefaultControls.Resources());
                created.name = "Scrollbar";
                created.transform.SetParent(template, false);
                scrollbar = created.GetComponent<Scrollbar>();
                scroll.verticalScrollbar = scrollbar;
            }
            scrollbar.gameObject.SetActive(true);
            scrollbar.interactable = true;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.numberOfSteps = 0;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            var barRect = (RectTransform)scrollbar.transform;
            barRect.anchorMin = new Vector2(1, 0);
            barRect.anchorMax = Vector2.one;
            barRect.pivot = new Vector2(1, .5f);
            barRect.anchoredPosition = new Vector2(-4, 0);
            barRect.sizeDelta = new Vector2(ScrollbarWidth, -8);
            if (scrollbar.TryGetComponent<Image>(out var track))
            { track.sprite = null; track.type = Image.Type.Simple; track.color = new Color(.035f, .045f, .08f); track.raycastTarget = true; }
            if (scrollbar.targetGraphic is Image handle)
            { handle.sprite = null; handle.type = Image.Type.Simple; handle.color = Color.white; handle.raycastTarget = true; }
            if (scrollbar.handleRect)
            {
                var slidingArea = scrollbar.handleRect.parent as RectTransform;
                if (slidingArea) Stretch(slidingArea, new Vector2(2, 2), new Vector2(-2, -2));
                scrollbar.handleRect.sizeDelta = Vector2.zero;
            }
            var barColors = scrollbar.colors;
            barColors.normalColor = new Color(.57f, .61f, .72f);
            barColors.highlightedColor = MenuUIFactory.Orange;
            barColors.selectedColor = MenuUIFactory.Orange;
            barColors.pressedColor = new Color(1, .7f, .5f);
            barColors.disabledColor = new Color(.25f, .28f, .35f);
            barColors.colorMultiplier = 1;
            barColors.fadeDuration = .05f;
            scrollbar.colors = barColors;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = WheelSensitivity;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing = 0;
            template.gameObject.SetActive(false);
        }

        static void Stretch(RectTransform rect, Vector2 minimum, Vector2 maximum)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = minimum; rect.offsetMax = maximum; }

        static void StyleText(Text text, int fontSize)
        {
            if (!text) return;
            if (!text.font) text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.color = MenuUIFactory.Paper;
            text.fontSize = fontSize;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 16;
            text.resizeTextMaxSize = fontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
        }
    }
}
