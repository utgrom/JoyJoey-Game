using System.Collections;
using JoyJoey.Game.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    /// <summary>Scene-authored menu with an explicit first-button gate and independent UI input.</summary>
    [DisallowMultipleComponent]
    public sealed class MainMenuController : MonoBehaviour
    {
        public GameObject PressAnyPanel, MainPanel, ExtrasPanel, OptionsPanel;
        public Button StartButton, SandboxButton, ExtrasButton, OptionsButton, QuitButton;
        public Button ExtrasBackButton, OptionsBackButton;
        public Text StartLabel, StatusLabel;
        public EventSystem EventSystem;
        public InputSystemUIInputModule InputModule;
        public AudioOptionsController AudioPage;
        public Button AudioButton;
        public DisplayOptionsController DisplayPage;
        public Button DisplayButton;
        public ControlsOptionsController ControlsPage { get; private set; }
        public Button ControlsButton { get; private set; }

        public bool IsRevealed { get; private set; }
        public bool ReadyForInput { get; private set; }
        public string CurrentPage { get; private set; } = "Inicio";
        GameSession _session;
        MenuInputCoordinator _input;
        Button _returnSelection;
        int _createdFrame;
        bool _titleNeedsRelease;

        void Awake()
        {
            _createdFrame = Time.frameCount;
            _session = GameSession.EnsureExists();
            _input = MenuInputCoordinator.Ensure(EventSystem);
            _input.BackRequested += HandleBack;
            _input.ApplyRequested += ApplyOptions;
            _input.SetHints(false, false, false, false);
            UiAudioFeedback.EnsureForCanvas(gameObject);
            StartButton.onClick.AddListener(StartGame);
            SandboxButton.onClick.AddListener(EnterSandbox);
            ExtrasButton.onClick.AddListener(ShowExtras);
            OptionsButton.onClick.AddListener(ShowOptions);
            QuitButton.onClick.AddListener(Quit);
            ExtrasBackButton.onClick.AddListener(ShowMainPage);
            OptionsBackButton.onClick.AddListener(ShowMainPage);
            if (AudioButton) AudioButton.onClick.AddListener(OpenAudio);
            if (DisplayButton) DisplayButton.onClick.AddListener(OpenDisplay);
            ControlsPage = ControlsOptionsController.AddTo(OptionsPanel, AudioButton, DisplayButton, OptionsBackButton, OpenControls, out var controls);
            ControlsButton = controls;
            SetPages(true, false, false, false);
            _input.SetFocusScope(PressAnyPanel.transform, null);
            _input.SuspendInput();
            if (EventSystem) EventSystem.SetSelectedGameObject(null);
            RefreshState();
        }

        void Update()
        {
            RefreshState();
            if (CinematicOverlay.CapturesInput) return;
            if (!IsRevealed)
            {
                if (_titleNeedsRelease) { if (!AnyButtonHeld()) _titleNeedsRelease = false; return; }
                if (Time.frameCount > _createdFrame && AnyButtonPressedThisFrame()) RevealMenu();
                return;
            }
            if (!ReadyForInput || !_input.InputReady || _session.IsLoading) return;
            UpdateHints();
            if (ControlsPage && ControlsPage.IsOpen)
            {
                if (CancelPressedThisFrame()) { UiAudioFeedback.PlayConfirm(); ControlsPage.Close(); }
                return;
            }
            if (DisplayPage && DisplayPage.IsOpen)
            {
                if (CancelPressedThisFrame()) DisplayPage.HandleBack();
                else if (MenuInputCoordinator.ApplyPressedThisFrame) ApplyOptions();
                return;
            }
            if (AudioPage && AudioPage.IsOpen)
            {
                if (CancelPressedThisFrame()) AudioPage.HandleBack();
                return;
            }
            if (CancelPressedThisFrame()) { HandleBack(); return; }
            // Restore a useful focus after controller hotplug or pointer deselection.
            if (!MenuInputCoordinator.PointerMode && EventSystem && !EventSystem.currentSelectedGameObject)
            {
                Button target = CurrentPage == "Opciones" ? AudioButton : CurrentPage == "Extras" ? null : StartButton;
                if (target && target.interactable) EventSystem.SetSelectedGameObject(target.gameObject);
            }
        }

        public void RevealMenu()
        {
            if (IsRevealed) return;
            UiAudioFeedback.PlayConfirm();
            IsRevealed = true;
            ShowMainPage();
            StartCoroutine(EnableAfterRelease());
        }

        IEnumerator EnableAfterRelease()
        {
            // UI Submit must not see the same press that dismissed the title screen.
            yield return null;
            while (AnyButtonHeld()) yield return null;
            _input.BeginReleaseGate();
            while (!_input.InputReady) yield return null;
            ReadyForInput = true;
            Select(StartButton);
            UpdateHints();
        }

        public void HideMenu()
        {
            if (_session.IsLoading) return;
            StopAllCoroutines();
            if (ControlsPage) ControlsPage.Close();
            if (DisplayPage && DisplayPage.IsOpen) DisplayPage.Close();
            if (AudioPage && AudioPage.IsOpen) AudioPage.Close();
            ReadyForInput = IsRevealed = false;
            CurrentPage = "Inicio";
            _returnSelection = null;
            _createdFrame = Time.frameCount;
            _titleNeedsRelease = true;
            SetPages(true, false, false, false);
            _input.SetFocusScope(PressAnyPanel.transform, null);
            _input.SetHints(false, false, false, false);
            _input.SuspendInput();
            if (EventSystem) EventSystem.SetSelectedGameObject(null);
        }
        public void HandleBack()
        {
            if (!ReadyForInput || !_input.InputReady || _session.IsLoading) return;
            UiAudioFeedback.PlayConfirm();
            if (ControlsPage && ControlsPage.IsOpen) ControlsPage.Close();
            else if (DisplayPage && DisplayPage.IsOpen) DisplayPage.HandleBack();
            else if (AudioPage && AudioPage.IsOpen) AudioPage.HandleBack();
            else if (CurrentPage == "Principal") HideMenu();
            else ShowMainPage();
        }
        void ApplyOptions()
        {
            if (ControlsPage && ControlsPage.IsOpen) { ControlsPage.RestoreShownDefaults(); return; }
            if (ReadyForInput && _input.InputReady && DisplayPage && DisplayPage.IsOpen &&
                !DisplayPage.HasOpenDropdown && !JoyJoey.Game.Display.GameDisplay.Instance.IsPreviewing) DisplayPage.Apply();
        }
        void UpdateHints()
        {
            if (ControlsPage && ControlsPage.IsOpen) { ControlsPage.SetMenuHints(); return; }
            bool display = DisplayPage && DisplayPage.IsOpen;
            bool preview = display && JoyJoey.Game.Display.GameDisplay.Instance.IsPreviewing;
            _input.SetHints(true, true, CurrentPage != "Extras", display && !preview && !DisplayPage.HasOpenDropdown);
        }

        public void ShowMainPage()
        {
            if (ControlsPage) ControlsPage.Close();
            if (DisplayPage && DisplayPage.IsOpen) DisplayPage.Close();
            if (AudioPage && AudioPage.IsOpen) AudioPage.Close();
            CurrentPage = "Principal";
            SetPages(false, true, false, false);
            _input.SetFocusScope(MainPanel.transform, _returnSelection ? _returnSelection : StartButton);
            Select(_returnSelection ? _returnSelection : StartButton);
            _returnSelection = null;
        }

        public void ShowExtras()
        {
            if (_session.IsLoading) return;
            _returnSelection = ExtrasButton;
            CurrentPage = "Extras";
            SetPages(false, false, true, false);
            _input.SetFocusScope(ExtrasPanel.transform, null);
            if (EventSystem) EventSystem.SetSelectedGameObject(null);
        }

        public void ShowOptions()
        {
            if (_session.IsLoading) return;
            _returnSelection = OptionsButton;
            CurrentPage = "Opciones";
            SetPages(false, false, false, true);
            _input.SetFocusScope(OptionsPanel.transform, AudioButton);
            Select(AudioButton ? AudioButton : OptionsBackButton);
        }

        public void OpenAudio()
        {
            if (!AudioPage || _session.IsLoading) return;
            if (ControlsPage) ControlsPage.Close();
            if (DisplayPage && DisplayPage.IsOpen) DisplayPage.Close();
            AudioPage.Open(() => { if (OptionsPanel && OptionsPanel.activeInHierarchy) { _input.SetFocusScope(OptionsPanel.transform, AudioButton); Select(AudioButton); } });
        }

        public void OpenDisplay()
        {
            if (!DisplayPage || _session.IsLoading) return;
            if (ControlsPage) ControlsPage.Close();
            if (AudioPage && AudioPage.IsOpen) AudioPage.Close();
            DisplayPage.Open(() => { if (OptionsPanel && OptionsPanel.activeInHierarchy) { _input.SetFocusScope(OptionsPanel.transform, DisplayButton); Select(DisplayButton); } });
        }

        public void OpenControls()
        {
            if (!ControlsPage || !OptionsPanel.activeInHierarchy || _session.IsLoading) return;
            if (AudioPage && AudioPage.IsOpen) AudioPage.Close();
            if (DisplayPage && DisplayPage.IsOpen) DisplayPage.Close();
            ControlsPage.Open(_input, () =>
            {
                if (!OptionsPanel.activeInHierarchy) return;
                _input.SetFocusScope(OptionsPanel.transform, ControlsButton);
                Select(ControlsButton);
            });
        }

        void SetPages(bool title, bool main, bool extras, bool options)
        {
            if (PressAnyPanel) PressAnyPanel.SetActive(title);
            if (MainPanel) MainPanel.SetActive(main);
            if (ExtrasPanel) ExtrasPanel.SetActive(extras);
            if (OptionsPanel) OptionsPanel.SetActive(options);
        }

        void Select(Button button)
        {
            if (!MenuInputCoordinator.PointerMode && EventSystem && button && button.gameObject.activeInHierarchy)
                EventSystem.SetSelectedGameObject(button.gameObject);
        }

        void RefreshState()
        {
            if (!_session) return;
            if (StartLabel) StartLabel.text = _session.HasSavedGame ? "Continuar" : "Empezar juego";
            bool available = !_session.IsLoading;
            foreach (var button in new[] { StartButton, SandboxButton, ExtrasButton, OptionsButton, QuitButton, ExtrasBackButton, OptionsBackButton })
                if (button) button.interactable = available;
            if (StatusLabel) StatusLabel.text = _session.IsLoading ? "Cargando…" : _session.LastError;
        }

        void StartGame() { if (ReadyForInput && !_session.IsLoading) _session.StartOrContinueGame(); }
        void EnterSandbox() { if (ReadyForInput && !_session.IsLoading) _session.EnterSandbox(); }
        void Quit() { if (ReadyForInput && !_session.IsLoading) _session.QuitGame(); }

        public static bool AnyButtonPressedThisFrame()
        {
            foreach (var device in InputSystem.devices)
            {
                if (device is Keyboard keyboard && keyboard.anyKey.wasPressedThisFrame) return true;
                if (device is Mouse mouse && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame ||
                    mouse.middleButton.wasPressedThisFrame || mouse.forwardButton.wasPressedThisFrame || mouse.backButton.wasPressedThisFrame)) return true;
                if (device is Gamepad pad && GamepadButtonPressed(pad)) return true;
            }
            return false;
        }

        public static bool CancelPressedThisFrame()
            => MenuInputCoordinator.BackPressedThisFrame;

        static bool AnyButtonHeld()
        {
            foreach (var device in InputSystem.devices)
            {
                if (device is Keyboard keyboard && keyboard.anyKey.isPressed) return true;
                if (device is Mouse mouse && (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed ||
                    mouse.forwardButton.isPressed || mouse.backButton.isPressed)) return true;
                if (device is Gamepad pad && GamepadButtonHeld(pad)) return true;
            }
            return false;
        }

        static bool GamepadButtonPressed(Gamepad p) => p.buttonSouth.wasPressedThisFrame || p.buttonNorth.wasPressedThisFrame ||
            p.buttonEast.wasPressedThisFrame || p.buttonWest.wasPressedThisFrame || p.leftShoulder.wasPressedThisFrame ||
            p.rightShoulder.wasPressedThisFrame || p.leftTrigger.wasPressedThisFrame || p.rightTrigger.wasPressedThisFrame ||
            p.startButton.wasPressedThisFrame || p.selectButton.wasPressedThisFrame || p.leftStickButton.wasPressedThisFrame ||
            p.rightStickButton.wasPressedThisFrame || p.dpad.up.wasPressedThisFrame || p.dpad.down.wasPressedThisFrame ||
            p.dpad.left.wasPressedThisFrame || p.dpad.right.wasPressedThisFrame;

        static bool GamepadButtonHeld(Gamepad p) => p.buttonSouth.isPressed || p.buttonNorth.isPressed || p.buttonEast.isPressed ||
            p.buttonWest.isPressed || p.leftShoulder.isPressed || p.rightShoulder.isPressed || p.leftTrigger.isPressed ||
            p.rightTrigger.isPressed || p.startButton.isPressed || p.selectButton.isPressed || p.leftStickButton.isPressed ||
            p.rightStickButton.isPressed || p.dpad.up.isPressed || p.dpad.down.isPressed || p.dpad.left.isPressed || p.dpad.right.isPressed;
    }
}
