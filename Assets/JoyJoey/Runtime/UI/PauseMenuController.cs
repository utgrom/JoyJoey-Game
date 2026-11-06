using System;
using JoyJoey.Game.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    /// <summary>Persistent presentation of pause state. GameSession owns simulation suspension.</summary>
    [DefaultExecutionOrder(-1200), DisallowMultipleComponent]
    public sealed class PauseMenuController : MonoBehaviour
    {
        public GameSession Session;
        public GameObject PausePanel, OptionsPanel, EventSystemRoot;
        public Button ResumeButton, OptionsButton, MainMenuButton, QuitButton, BackButton;
        public Text StatusText;
        public AudioOptionsController AudioPage;
        public Button AudioButton;
        public DisplayOptionsController DisplayPage;
        public Button DisplayButton;

        public bool IsOptionsOpen => _showOptions;
        bool _showOptions;
        bool _gameplayScene;
        int _ignoreInputThroughFrame;
        GameObject _focusNext;
        MenuInputCoordinator _input;
        bool _feedbackPrepared;

        void Awake()
        {
            _input = MenuInputCoordinator.Ensure(EventSystemRoot.GetComponent<EventSystem>());
            _input.BackRequested += HandleBack;
            _input.ApplyRequested += ApplyDisplay;
            if (ResumeButton) ResumeButton.onClick.AddListener(Resume);
            if (OptionsButton) OptionsButton.onClick.AddListener(ShowOptions);
            if (MainMenuButton) MainMenuButton.onClick.AddListener(ReturnToMenu);
            if (QuitButton) QuitButton.onClick.AddListener(Quit);
            if (BackButton) BackButton.onClick.AddListener(ShowPausePage);
            if (AudioButton) AudioButton.onClick.AddListener(OpenAudio);
            if (DisplayButton) DisplayButton.onClick.AddListener(OpenDisplay);
        }

        void OnEnable()
        {
            Session = Session ? Session : GameSession.EnsureExists();
            Session.StateChanged += Refresh;
            SceneManager.sceneLoaded += SceneLoaded;
            SceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        void OnDisable()
        {
            if (Session) Session.StateChanged -= Refresh;
            SceneManager.sceneLoaded -= SceneLoaded;
            if (EventSystemRoot) EventSystemRoot.SetActive(false);
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _gameplayScene = scene.name != "MenuPrincipal" && scene.name != "Arranque";
            _showOptions = false;
            _ignoreInputThroughFrame = Time.frameCount + 1;
            SynchronizeEventSystem();
            Refresh();
        }

        void SynchronizeEventSystem()
        {
            if (!EventSystemRoot) return;
            if (!_gameplayScene || Session && Session.IsLoading)
            {
                EventSystemRoot.SetActive(false);
                return;
            }
            var own = EventSystemRoot.GetComponent<EventSystem>();
            bool another = false;
            foreach (var candidate in FindObjectsByType<EventSystem>())
                if (candidate != own && candidate.isActiveAndEnabled) { another = true; break; }
            EventSystemRoot.SetActive(!another);
        }

        void Refresh()
        {
            if (!Session) return;
            // Retire the persistent input module before the next scene's EventSystem enables.
            if (Session.IsLoading || !_gameplayScene)
            {
                if (EventSystemRoot) EventSystemRoot.SetActive(false);
            }
            else if (EventSystemRoot && !EventSystemRoot.activeSelf) SynchronizeEventSystem();
            bool visible = _gameplayScene && Session.IsPaused && !Session.IsLoading;
            bool wasVisible = PausePanel && PausePanel.activeSelf || OptionsPanel && OptionsPanel.activeSelf;
            if (!visible) _showOptions = false;
            if (!visible && AudioPage && AudioPage.IsOpen) AudioPage.Close();
            if (!visible && DisplayPage && DisplayPage.IsOpen) DisplayPage.Close();
            if (PausePanel) PausePanel.SetActive(visible && !_showOptions);
            if (OptionsPanel) OptionsPanel.SetActive(visible && _showOptions);
            if (ResumeButton) ResumeButton.interactable = !Session.IsLoading;
            if (OptionsButton) OptionsButton.interactable = !Session.IsLoading;
            if (MainMenuButton) MainMenuButton.interactable = !Session.IsLoading;
            if (QuitButton) QuitButton.interactable = !Session.IsLoading;
            if (StatusText) StatusText.text = Session.LastError ?? "";
            if (visible && !wasVisible)
            {
                _ignoreInputThroughFrame = Time.frameCount + 1;
                _focusNext = ResumeButton ? ResumeButton.gameObject : null;
                _input.SetFocusScope(PausePanel.transform, ResumeButton);
                if (!MenuInputCoordinator.PointerMode && EventSystem.current && _focusNext) EventSystem.current.SetSelectedGameObject(_focusNext);
                _input.BeginReleaseGate();
            }
            else if (!visible)
            {
                _focusNext = null;
                var system = EventSystem.current;
                if (system && system.currentSelectedGameObject && system.currentSelectedGameObject.transform.IsChildOf(transform))
                    system.SetSelectedGameObject(null);
            }
            if (_input) _input.SetHints(visible, true, true, visible && DisplayPage && DisplayPage.IsOpen &&
                !DisplayPage.HasOpenDropdown && !JoyJoey.Game.Display.GameDisplay.Instance.IsPreviewing);
            if (!_feedbackPrepared)
            {
                UiAudioFeedback.EnsureForCanvas(gameObject);
                _feedbackPrepared = true;
            }
            if (!visible && _input) _input.SuspendInput();
        }

        void Update()
        {
            if (!_gameplayScene || !Session || Session.IsLoading) return;
            if (Time.frameCount <= _ignoreInputThroughFrame) return;
            if (Session.IsPaused && !_input.InputReady) return;
            bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            bool toggle = false;
            bool back = false;
            foreach (var pad in Gamepad.all)
            {
                toggle |= pad.startButton.wasPressedThisFrame;
                back |= pad.buttonEast.wasPressedThisFrame;
            }
            if (escape || toggle || Session.IsPaused && back) UiAudioFeedback.PlayConfirm();
            if (Session.IsPaused) _input.SetHints(true, true, true, DisplayPage && DisplayPage.IsOpen &&
                !DisplayPage.HasOpenDropdown && !JoyJoey.Game.Display.GameDisplay.Instance.IsPreviewing);
            if (Session.IsPaused && DisplayPage && DisplayPage.IsOpen)
            {
                if (escape || back) DisplayPage.HandleBack();
                else if (toggle) { DisplayPage.Close(); Session.SetPaused(false); }
                else if (MenuInputCoordinator.ApplyPressedThisFrame) ApplyDisplay();
                return;
            }
            if (Session.IsPaused && AudioPage && AudioPage.IsOpen)
            {
                if (escape || back) AudioPage.HandleBack();
                else if (toggle) { AudioPage.Close(); Session.SetPaused(false); }
                return;
            }
            if (Session.IsPaused && _showOptions && (escape || back)) ShowPausePage();
            else if (escape || toggle || Session.IsPaused && back)
            {
                _showOptions = false;
                Session.SetPaused(!Session.IsPaused);
            }

            if (!Session.IsPaused) return;
            var system = EventSystem.current;
            if (!system) return;
            var fallback = _showOptions ? AudioButton : ResumeButton;
            if (_focusNext)
            {
                if (!MenuInputCoordinator.PointerMode) system.SetSelectedGameObject(_focusNext);
                _focusNext = null;
            }
            else if (!MenuInputCoordinator.PointerMode && (!system.currentSelectedGameObject || !system.currentSelectedGameObject.activeInHierarchy) && fallback)
                system.SetSelectedGameObject(fallback.gameObject);
        }

        public void ShowOptions()
        {
            if (!Session || !Session.IsPaused || Session.IsLoading) return;
            _showOptions = true;
            Refresh();
            _input.SetFocusScope(OptionsPanel.transform, AudioButton);
            _focusNext = AudioButton ? AudioButton.gameObject : BackButton ? BackButton.gameObject : null;
            _ignoreInputThroughFrame = Time.frameCount + 1;
        }

        public void ShowPausePage()
        {
            if (DisplayPage && DisplayPage.IsOpen) DisplayPage.Close();
            if (AudioPage && AudioPage.IsOpen) AudioPage.Close();
            _showOptions = false;
            Refresh();
            _input.SetFocusScope(PausePanel.transform, OptionsButton);
            _focusNext = OptionsButton ? OptionsButton.gameObject : null;
            _ignoreInputThroughFrame = Time.frameCount + 1;
        }

        public void OpenAudio()
        {
            if (!AudioPage || !Session || !Session.IsPaused || Session.IsLoading) return;
            if (DisplayPage && DisplayPage.IsOpen) DisplayPage.Close();
            AudioPage.Open(() => { if (OptionsPanel && OptionsPanel.activeInHierarchy && AudioButton) { _input.SetFocusScope(OptionsPanel.transform, AudioButton); _focusNext = AudioButton.gameObject; } });
        }

        public void OpenDisplay()
        {
            if (!DisplayPage || !Session || !Session.IsPaused || Session.IsLoading) return;
            if (AudioPage && AudioPage.IsOpen) AudioPage.Close();
            DisplayPage.Open(() => { if (OptionsPanel && OptionsPanel.activeInHierarchy && DisplayButton) { _input.SetFocusScope(OptionsPanel.transform, DisplayButton); _focusNext = DisplayButton.gameObject; } });
        }

        public void Resume()
        {
            if (Session && !Session.IsLoading) Session.SetPaused(false);
        }

        public void HandleBack()
        {
            if (!Session || !Session.IsPaused || !_input.InputReady || Session.IsLoading) return;
            UiAudioFeedback.PlayConfirm();
            if (DisplayPage && DisplayPage.IsOpen) DisplayPage.HandleBack();
            else if (AudioPage && AudioPage.IsOpen) AudioPage.HandleBack();
            else if (_showOptions) ShowPausePage();
            else Resume();
        }
        void ApplyDisplay()
        {
            if (Session && Session.IsPaused && _input.InputReady && DisplayPage && DisplayPage.IsOpen &&
                !DisplayPage.HasOpenDropdown && !JoyJoey.Game.Display.GameDisplay.Instance.IsPreviewing) DisplayPage.Apply();
        }

        void ReturnToMenu()
        {
            if (Session && !Session.IsLoading) Session.ReturnToMainMenu();
        }

        void Quit()
        {
            if (Session && !Session.IsLoading) Session.QuitGame();
        }
    }
}
