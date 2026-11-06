using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JoyJoey.Game.Audio
{
    [DisallowMultipleComponent]
    public sealed class UiAudioFeedback : MonoBehaviour, ISelectHandler, IPointerEnterHandler, IPointerClickHandler, ISubmitHandler, ICancelHandler
    {
        static int _lastConfirmFrame = -1;
        Selectable _control;
        Slider _slider;
        Dropdown _dropdown;
        Button _button;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetFeedbackState() => _lastConfirmFrame = -1;

        /// <summary>
        /// Covers authored controls, inactive pages, dropdown item templates and runtime footers.
        /// Existing prefabs receive the same behavior without needing to regenerate their layout.
        /// </summary>
        public static void EnsureForCanvas(GameObject root)
        {
            if (!root) return;
            foreach (var control in root.GetComponentsInChildren<Selectable>(true))
                if (!control.GetComponent<UiAudioFeedback>()) control.gameObject.AddComponent<UiAudioFeedback>();
        }

        void Awake() => BindControls();
        void OnEnable() => BindControls();
        void BindControls()
        {
            if (!_control) _control = GetComponent<Selectable>();
            if (!_slider && TryGetComponent<Slider>(out var slider))
            {
                _slider = slider;
                _slider.onValueChanged.AddListener(SliderChanged);
            }
            if (!_dropdown && TryGetComponent<Dropdown>(out var dropdown))
            {
                _dropdown = dropdown;
                _dropdown.onValueChanged.AddListener(DropdownChanged);
            }
            if (!_button && TryGetComponent<Button>(out var button))
            {
                _button = button;
                _button.onClick.AddListener(ButtonConfirmed);
            }
        }

        bool CanPlay => isActiveAndEnabled && (!_control || _control.IsInteractable());
        // EventSystem snapshots its receivers before dispatching. A Button's earlier handler may
        // already have hidden this page by the time the same accepted event reaches feedback.
        bool CanConfirmDeliveredEvent => !_control || _control.IsInteractable();
        void SliderChanged(float value) { if (CanPlay) PlayMove(); }
        void DropdownChanged(int value) { if (CanPlay) PlayConfirm(); }
        // Button.onClick only runs for an accepted click/submit, even if another listener hides or
        // disables the button during a transition. The frame guard unifies it with interface events.
        void ButtonConfirmed() => PlayConfirm();

        public static void PlayMove()
        {
            // GameAudio rate-limits movement, including Select + PointerEnter on the same item.
            if (Application.isPlaying && _lastConfirmFrame != Time.frameCount) GameAudio.Instance?.PlayUiMove();
        }
        public static void PlayConfirm()
        {
            if (!Application.isPlaying || _lastConfirmFrame == Time.frameCount || !GameAudio.Instance) return;
            // A popup Toggle's Submit/click and its owning Dropdown value change are one action.
            _lastConfirmFrame = Time.frameCount;
            GameAudio.Instance.PlayUiConfirm();
        }

        public void OnSelect(BaseEventData data) { if (CanPlay) PlayMove(); }
        public void OnPointerEnter(PointerEventData data) { if (CanPlay) PlayMove(); }
        public void OnPointerClick(PointerEventData data)
        { if (data.button == PointerEventData.InputButton.Left && CanConfirmDeliveredEvent) PlayConfirm(); }
        public void OnSubmit(BaseEventData data) { if (CanConfirmDeliveredEvent) PlayConfirm(); }
        public void OnCancel(BaseEventData data) { if (CanConfirmDeliveredEvent) PlayConfirm(); }

        void OnDestroy()
        {
            if (_slider) _slider.onValueChanged.RemoveListener(SliderChanged);
            if (_dropdown) _dropdown.onValueChanged.RemoveListener(DropdownChanged);
            if (_button) _button.onClick.RemoveListener(ButtonConfirmed);
        }
    }
}
