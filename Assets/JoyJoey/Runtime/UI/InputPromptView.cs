using UnityEngine;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    /// <summary>Shows a device glyph with an optional caption and a readable text fallback.</summary>
    [DisallowMultipleComponent]
    public sealed class InputPromptView : MonoBehaviour
    {
        [SerializeField] Text _label;
        [SerializeField] Image _icon;
        [SerializeField, Min(1)] float _height = 44;

        string _fallback, _caption;
        MenuInputFamily _family;
        bool _hasPrompt;

        public string ControlPath { get; private set; }
        public Sprite DisplayedSprite => _icon && _icon.enabled ? _icon.sprite : null;

        /// <summary>Turns an existing UGUI label into a prompt without changing its hit area.</summary>
        public static InputPromptView Attach(Text label, float height = 44)
        {
            var original = label.rectTransform;
            var parent = original.parent;
            if (parent && parent.TryGetComponent<InputPromptView>(out var existing) && existing._label == label)
                return existing;

            var container = new GameObject("Indicación de control", typeof(RectTransform),
                typeof(HorizontalLayoutGroup), typeof(InputPromptView));
            var rect = (RectTransform)container.transform;
            rect.SetParent(parent, false);
            rect.SetSiblingIndex(original.GetSiblingIndex());
            rect.anchorMin = original.anchorMin;
            rect.anchorMax = original.anchorMax;
            rect.pivot = original.pivot;
            rect.sizeDelta = original.sizeDelta;
            rect.anchoredPosition3D = original.anchoredPosition3D;
            rect.localScale = original.localScale;
            rect.localRotation = original.localRotation;

            var layout = container.GetComponent<HorizontalLayoutGroup>();
            layout.childAlignment = label.alignment;
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            var icon = new GameObject("Icono", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            icon.transform.SetParent(rect, false);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            original.SetParent(rect, false);
            original.localScale = Vector3.one;
            original.localRotation = Quaternion.identity;

            var view = container.GetComponent<InputPromptView>();
            view._label = label;
            view._icon = icon;
            view._height = height;
            return view;
        }

        public void Set(string controlPath, MenuInputFamily family, string fallback, string caption = "")
        {
            if (_hasPrompt && ControlPath == controlPath && _family == family && _fallback == fallback && _caption == caption)
                return;
            _hasPrompt = true;
            ControlPath = controlPath;
            _family = family;
            _fallback = fallback;
            _caption = caption;

            var sprite = _icon ? InputPromptSprites.Resolve(controlPath, family) : null;
            if (_icon)
            {
                _icon.sprite = sprite;
                _icon.enabled = sprite;
                if (_icon.TryGetComponent<LayoutElement>(out var size))
                {
                    size.ignoreLayout = !sprite;
                    if (sprite)
                    {
                        size.preferredWidth = _height * sprite.rect.width / sprite.rect.height;
                        size.preferredHeight = _height;
                    }
                }
            }
            if (!_label) return;
            string text = sprite || string.IsNullOrEmpty(fallback) ? caption :
                string.IsNullOrEmpty(caption) ? fallback : fallback + "  " + caption;
            _label.text = text;
            _label.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }
    }
}
