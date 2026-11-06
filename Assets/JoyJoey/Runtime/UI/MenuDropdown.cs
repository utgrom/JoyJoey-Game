using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    /// <summary>Keeps keyboard/controller selection visible without pulling against wheel or scrollbar input.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Dropdown))]
    public sealed class MenuDropdown : MonoBehaviour
    {
        Transform _popup;
        ScrollRect _scroll;
        GameObject _previousSelection;

        public bool IsExpanded => _popup && _popup.gameObject.activeInHierarchy;
        public ScrollRect OpenScrollRect => _scroll;

        void LateUpdate()
        {
            var popup = transform.Find("Dropdown List");
            bool opened = popup && popup != _popup;
            if (popup != _popup)
            {
                _popup = popup;
                _scroll = popup ? popup.GetComponent<ScrollRect>() : null;
                _previousSelection = null;
            }
            if (!_scroll || !EventSystem.current) return;
            var selected = EventSystem.current.currentSelectedGameObject;
            // A wheel/drag scroll must stay where the user moved it. Scroll only when selection
            // changes or a list first opens with its previously chosen option selected.
            if ((!opened && selected == _previousSelection) || !selected) return;
            _previousSelection = selected;
            if (!selected.transform.IsChildOf(_scroll.content) || !selected.GetComponent<Toggle>()) return;
            Canvas.ForceUpdateCanvases();
            EnsureVisible(_scroll, selected.transform as RectTransform);
        }

        public static bool EnsureVisible(ScrollRect scroll, RectTransform item)
        {
            if (!scroll || !scroll.viewport || !scroll.content || !item || !item.IsChildOf(scroll.content)) return false;
            var viewport = scroll.viewport;
            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, item);
            float offset = RequiredOffset(viewport.rect.yMin, viewport.rect.yMax, bounds.min.y, bounds.max.y);
            if (Mathf.Abs(offset) <= .01f) return false;
            scroll.StopMovement();
            var position = scroll.content.anchoredPosition;
            position.y += offset;
            scroll.content.anchoredPosition = position;
            return true;
        }

        /// <summary>Positive moves the content up to reveal a lower item; negative reveals an upper item.</summary>
        public static float RequiredOffset(float viewportBottom, float viewportTop, float itemBottom, float itemTop)
        {
            if (itemTop > viewportTop) return viewportTop - itemTop;
            if (itemBottom < viewportBottom) return viewportBottom - itemBottom;
            return 0;
        }

        void OnDisable()
        {
            _popup = null;
            _scroll = null;
            _previousSelection = null;
        }
    }
}
