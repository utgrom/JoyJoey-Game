using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JoyJoey.Game.Display;
using JoyJoey.Sandbox;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    /// <summary>Opt-in native UI regression checks; the caller must provide isolated settings and save paths.</summary>
    public sealed class MenuRegressionSmoke : MonoBehaviour
    {
        Keyboard _keyboard;
        Gamepad _pad;
        Mouse _mouse;
        MainMenuController _menu;
        GameSession _session;
        string _output;
        Vector2 _mousePosition;
        bool _failed, _quitting, _stopped;
        float _deadline;
        string TracePath => Path.Combine(_output, "ui-trace.txt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-uiSmoke") < 0) return;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var root = new GameObject("Menu navigation regression verification");
            DontDestroyOnLoad(root);
            root.AddComponent<MenuRegressionSmoke>();
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "-uiSmokeOutput");
            _output = option >= 0 && option + 1 < args.Length ? args[option + 1] :
                Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Validation/MenuUI"));
            Directory.CreateDirectory(_output);
            File.WriteAllText(TracePath, "");
            File.WriteAllText(Path.Combine(_output, "ui-errors.txt"), "");
            Application.logMessageReceived += OnLog;
            _deadline = Time.realtimeSinceStartup + 150;
            Check(Array.IndexOf(args, "-joySettingsDirectory") >= 0 && Array.IndexOf(args, "-joySaveDirectory") >= 0,
                "Explicit isolated settings and campaign paths are provided");
            _keyboard = InputSystem.AddDevice<Keyboard>("MenuSmokeKeyboard");
            _pad = InputSystem.AddDevice<Gamepad>("MenuSmokeGamepad");
            _mouse = InputSystem.AddDevice<Mouse>("MenuSmokeMouse");
            yield return Until(() => (_menu = FindAnyObjectByType<MainMenuController>()) && GameSession.Instance && GameDisplay.Instance,
                "Menu, campaign session and display service initialize");
            _session = GameSession.Instance;
            yield return KeyTap(Key.J);
            yield return Until(() => _menu.ReadyForInput, "J reveals the menu after its release");
            Check(!_session.IsLoading && !FindAnyObjectByType<LevelRunner>(), "The reveal press does not select an option");
            var coordinator = MenuInputCoordinator.Ensure(_menu.EventSystem);
            CheckHints(coordinator, true, false);
            yield return KeyTap(Key.Escape);
            yield return Until(() => !_menu.IsRevealed, "Back on the main menu restores the title gate");
            yield return Frames(3);
            Check(_menu.PressAnyPanel.activeInHierarchy && !_menu.MainPanel.activeInHierarchy,
                "Only the unrevealed title is visible after Back");
            yield return KeyTap(Key.J);
            yield return Until(() => _menu.ReadyForInput, "The title can be revealed again without activating an option");

            // A pointer hover and a navigation selection must never remain highlighted together.
            MoveMouse(Center(_menu.OptionsButton.transform));
            yield return Frames(6);
            Check(MenuInputCoordinator.PointerMode && Cursor.visible, "Actual pointer movement enables mouse navigation and the cursor");
            Focus(_menu.StartButton);
            yield return PadTap(GamepadButton.DpadDown);
            yield return Frames(5);
            Check(MenuInputCoordinator.GamepadMode && !MenuInputCoordinator.PointerMode && !Cursor.visible,
                "Gamepad navigation hides and disables the pointer");
            Check(SelectionState(_menu.OptionsButton) != "Highlighted",
                "A button under the stationary mouse loses its old hover highlight");
            CheckHints(coordinator, false, false);
            MoveMouse(Center(_menu.OptionsButton.transform) + new Vector2(7, 0));
            yield return Frames(6);
            Check(MenuInputCoordinator.PointerMode && Cursor.visible, "Moving the mouse restores pointer interaction");
            var selected = EventSystem.current.currentSelectedGameObject;
            Check(!selected || selected == _menu.OptionsButton.gameObject || SelectionState(selected.GetComponent<Selectable>()) == "Normal",
                "Returning to mouse interaction does not retain a second navigation highlight");

            yield return Submit(_menu.OptionsButton);
            yield return Until(() => _menu.CurrentPage == "Opciones", "Keyboard Submit enters Options after returning from mouse navigation");
            yield return Submit(_menu.AudioButton);
            var audio = _menu.AudioPage;
            yield return Until(() => audio && audio.IsOpen, "Audio opens from Options");
            Check(PageCoversButton(audio.gameObject, _menu.DisplayButton),
                "Audio is above and blocks the parent Pantalla button");
            Capture("audio-fixed.png");
            yield return Submit(audio.OutputDeviceDropdown);
            yield return Until(() => OpenList(audio.OutputDeviceDropdown), "The audio output dropdown opens");
            ValidateList(audio.OutputDeviceDropdown, "Audio output");
            Capture("audio-output-dropdown.png");
            yield return KeyTap(Key.Escape);
            yield return Seconds(.25f);
            Check(audio.IsOpen && !OpenList(audio.OutputDeviceDropdown), "Back closes the dropdown before leaving Audio");
            yield return KeyTap(Key.Escape);
            yield return Until(() => !audio.IsOpen && _menu.CurrentPage == "Opciones", "Back from Audio returns to Options");

            yield return Submit(_menu.DisplayButton);
            var display = _menu.DisplayPage;
            yield return Until(() => display && display.IsOpen, "Display opens from Options");
            display.ModeDropdown.value = 0; // Edit the draft only, so window resolutions are available.
            yield return Frames(3);
            Check(display.ResolutionDropdown.interactable, "Windowed draft enables resolution selection");
            display.ResolutionDropdown.value = 0;
            yield return Submit(display.ResolutionDropdown);
            yield return Until(() => OpenList(display.ResolutionDropdown), "Resolution dropdown opens");
            ValidateList(display.ResolutionDropdown, "Resolution");
            var scroll = OpenList(display.ResolutionDropdown).GetComponent<ScrollRect>();
            var items = scroll.content.GetComponentsInChildren<Toggle>(false);
            Check(items.Length > 5, "Resolution list is longer than its visible viewport");
            for (int i = 1; i < items.Length; i++) yield return PadTap(GamepadButton.DpadDown);
            yield return Frames(5);
            var last = items[items.Length - 1];
            Check(EventSystem.current.currentSelectedGameObject == last.gameObject,
                "Gamepad reaches the last resolution without accepting it");
            Check(FullyVisible(scroll.viewport, last.GetComponent<RectTransform>()),
                "Gamepad scrolling keeps the selected last resolution fully visible");
            Check(scroll.verticalNormalizedPosition < .15f, "The resolution viewport follows selection to the bottom");
            Capture("resolution-gamepad-bottom.png");

            // Wheel scrolling and the visible track must work independently of the selected item.
            float previous = scroll.verticalNormalizedPosition;
            MoveMouse(Center(scroll.viewport));
            yield return Frames(3);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = _mousePosition, scroll = new Vector2(0, 120) });
            yield return Frames(4);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = _mousePosition });
            yield return Frames(4);
            Check(scroll.verticalNormalizedPosition > previous + .01f, "Mouse wheel scrolls the list at the increased speed");
            float beforeTrack = scroll.verticalNormalizedPosition;
            var bar = scroll.verticalScrollbar.GetComponent<RectTransform>();
            var barPoint = Point(bar, beforeTrack > .5f ? .1f : .9f);
            yield return MouseClick(barPoint);
            yield return Frames(5);
            Check(Mathf.Abs(scroll.verticalNormalizedPosition - beforeTrack) > .01f,
                "Clicking the visible scrollbar track jumps through the resolution list");
            Capture("resolution-scrollbar.png");
            yield return PadTap(GamepadButton.DpadUp);
            var selectedAfterTrack = EventSystem.current.currentSelectedGameObject;
            Check(selectedAfterTrack && selectedAfterTrack.GetComponent<Toggle>() && selectedAfterTrack.transform.IsChildOf(scroll.content),
                "Gamepad navigation returns from the scrollbar to a resolution option");
            Check(selectedAfterTrack && FullyVisible(scroll.viewport, selectedAfterTrack.GetComponent<RectTransform>()),
                "The resolution selected after scrollbar interaction is fully visible");
            yield return KeyTap(Key.Escape);
            yield return Seconds(.25f);

            CheckHints(MenuInputCoordinator.Ensure(EventSystem.current), true, true);
            yield return KeyTap(Key.K);
            yield return Until(() => GameDisplay.Instance.IsPreviewing, "K applies display changes from the page");
            yield return KeyTap(Key.Escape);
            Check(!GameDisplay.Instance.IsPreviewing && display.IsOpen, "Escape reverts a preview while keeping Display open");
            yield return PadTap(GamepadButton.West);
            yield return Until(() => GameDisplay.Instance.IsPreviewing, "Gamepad west applies display changes");
            yield return KeyTap(Key.J);
            Check(!GameDisplay.Instance.IsPreviewing && File.Exists(GameDisplay.Instance.Store.FilePath),
                "J confirms and saves the display preview");
            yield return KeyTap(Key.Escape);
            yield return Until(() => !display.IsOpen, "Back closes Display");
            yield return KeyTap(Key.Escape);
            yield return Until(() => _menu.CurrentPage == "Principal", "Back returns from Options to the main menu");

            yield return Submit(_menu.StartButton);
            yield return Until(() => FindAnyObjectByType<LevelRunner>() && !_session.BlocksGameplay,
                "Campaign starts for the pause-input checks");
            yield return Frames(4);
            yield return KeyTap(Key.P);
            Check(!_session.IsPaused, "P no longer pauses the campaign");
            InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = Vector2.down }.WithButton(GamepadButton.Start));
            yield return Until(() => _session.IsPaused, "Start pauses while the movement stick is held");
            var pause = FindAnyObjectByType<PauseMenuController>();
            var pauseSystem = pause.EventSystemRoot.GetComponent<EventSystem>();
            var pauseCoordinator = MenuInputCoordinator.Ensure(pauseSystem);
            InputSystem.QueueStateEvent(_pad, new GamepadState { leftStick = Vector2.down });
            yield return Seconds(.8f);
            Check(!pauseCoordinator.InputReady && (!pauseSystem.currentSelectedGameObject || pauseSystem.currentSelectedGameObject == pause.ResumeButton.gameObject),
                "Held gameplay movement is ignored when entering pause");
            InputSystem.QueueStateEvent(_pad, new GamepadState());
            yield return Until(() => pauseCoordinator.InputReady, "Pause navigation arms after all held controls are released");
            yield return PadTap(GamepadButton.DpadDown);
            Check(pauseSystem.currentSelectedGameObject == pause.OptionsButton.gameObject,
                "A fresh gamepad direction navigates after the pause gate");
            yield return Submit(pause.OptionsButton);
            yield return Submit(pause.AudioButton);
            yield return Until(() => pause.AudioPage.IsOpen, "Audio also opens within pause");
            Check(PageCoversButton(pause.AudioPage.gameObject, pause.DisplayButton), "Pause Audio also covers the parent Pantalla button");
            Capture("audio-pause-fixed.png");
            yield return PadTap(GamepadButton.East);
            Check(!pause.AudioPage.IsOpen && _session.IsPaused, "Gamepad Back returns from Audio while preserving pause");
            yield return PadTap(GamepadButton.East);
            Check(!pause.IsOptionsOpen && _session.IsPaused, "Gamepad Back returns from Options to pause");
            yield return PadTap(GamepadButton.Start);
            yield return Until(() => !_session.IsPaused && !_session.BlocksGameplay, "Start resumes gameplay after release");

            // Keyboard movement held during Escape must receive the same release gate.
            Keys(Key.S, Key.Escape);
            yield return Until(() => _session.IsPaused, "Escape pauses while a movement key is held");
            Keys(Key.S);
            yield return Seconds(.6f);
            Check(!pauseCoordinator.InputReady && (!pauseSystem.currentSelectedGameObject || pauseSystem.currentSelectedGameObject == pause.ResumeButton.gameObject),
                "Held keyboard movement does not navigate the newly opened pause menu");
            Keys();
            yield return Until(() => pauseCoordinator.InputReady, "Keyboard pause gate arms after release");
            yield return KeyTap(Key.Escape);
            yield return Until(() => !_session.IsPaused && !_session.BlocksGameplay, "Escape resumes and releases gameplay input");
            yield return KeyTap(Key.Escape);
            yield return Until(() => _session.IsPaused && pauseCoordinator.InputReady, "Pause opens again with fresh input");
            yield return Submit(pause.MainMenuButton);
            yield return Until(() => (_menu = FindAnyObjectByType<MainMenuController>()) && !_session.IsLoading,
                "Return to menu retains the corrected UI");
            yield return KeyTap(Key.J);
            yield return Until(() => _menu.ReadyForInput, "Returned title accepts a fresh reveal");
            yield return Submit(_menu.SandboxButton);
            SandboxRunner sandbox = null;
            yield return Until(() => (sandbox = FindAnyObjectByType<SandboxRunner>()) && sandbox.Simulation != null && !_session.BlocksGameplay,
                "Practice loads for its pause-key regression");
            yield return Frames(4);
            yield return KeyTap(Key.P);
            Check(!_session.IsPaused && !sandbox.Paused, "P no longer pauses either practice simulation or the game menu");
            yield return KeyTap(Key.Escape);
            yield return Until(() => _session.IsPaused && MenuInputCoordinator.Ensure(pauseSystem).InputReady, "Practice pauses with Escape");
            yield return Submit(pause.MainMenuButton);
            yield return Until(() => (_menu = FindAnyObjectByType<MainMenuController>()) && !_session.IsLoading, "Return from practice to main menu");
            yield return KeyTap(Key.J);
            yield return Until(() => _menu.ReadyForInput, "Final menu is ready");
            yield return QuitThroughUi(_menu.QuitButton);
        }

        void CheckHints(MenuInputCoordinator coordinator, bool keyboard, bool apply)
        {
            Check(MenuInputCoordinator.GamepadMode == !keyboard, "Footer follows the last input device");
            CheckHint(coordinator.HintLeft, keyboard ? "<Keyboard>/escape" : "<Gamepad>/buttonEast", "Volver");
            CheckHint(coordinator.HintRight, keyboard ? "<Keyboard>/j" : "<Gamepad>/buttonSouth", "Confirmar");
            if (apply) CheckHint(coordinator.ApplyHint, keyboard ? "<Keyboard>/k" : "<Gamepad>/buttonWest", "Aplicar cambios");
        }
        void CheckHint(Text label, string path, string caption)
        {
            var prompt = label ? label.GetComponentInParent<InputPromptView>() : null;
            var expected = InputPromptSprites.Resolve(path, MenuInputCoordinator.Family);
            Check(prompt && expected && prompt.DisplayedSprite == expected && prompt.ControlPath == path && label.text == caption,
                "Footer displays the glyph for " + caption);
        }
        void ValidateList(Dropdown dropdown, string name)
        {
            var list = OpenList(dropdown);
            var scroll = list ? list.GetComponent<ScrollRect>() : null;
            Check(scroll && scroll.viewport && scroll.content && scroll.verticalScrollbar && scroll.verticalScrollbar.gameObject.activeInHierarchy,
                name + " dropdown has a visible, interactive vertical scrollbar");
            Check(scroll.scrollSensitivity >= 20, name + " dropdown uses the faster wheel sensitivity");
            var rows = scroll.content.GetComponentsInChildren<Toggle>(false);
            Check(rows.Length > 0 && rows.All(row => row.GetComponentInChildren<Text>() &&
                !string.IsNullOrWhiteSpace(row.GetComponentInChildren<Text>().text) && row.GetComponentInChildren<Text>().color.a > .9f),
                name + " dropdown contains readable nonempty option labels");
        }
        static Transform OpenList(Dropdown dropdown)
        {
            var list = dropdown ? dropdown.transform.Find("Dropdown List") : null;
            return list && list.gameObject.activeInHierarchy ? list : null;
        }
        static string SelectionState(Selectable selectable)
        {
            if (!selectable) return "None";
            var state = typeof(Selectable).GetProperty("currentSelectionState", BindingFlags.Instance | BindingFlags.NonPublic);
            return state?.GetValue(selectable)?.ToString() ?? "Unknown";
        }
        static bool PageCoversButton(GameObject page, Button button)
        {
            Canvas.ForceUpdateCanvases();
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = Center(button.transform) }, results);
            var coordinator = MenuInputCoordinator.Ensure(EventSystem.current);
            var shield = coordinator ? coordinator.PointerShield : null;
            // The navigation shield intentionally intercepts pointer rays above every page.
            // Exclude only that exact control so the first real UI layer still has to be Audio.
            foreach (var hit in results)
            {
                if (shield && hit.gameObject.transform.IsChildOf(shield.transform)) continue;
                return hit.gameObject.transform.IsChildOf(page.transform);
            }
            return false;
        }
        static Vector2 Center(Transform transform) => Point((RectTransform)transform, .5f);
        static Vector2 Point(RectTransform rect, float vertical)
        {
            var local = new Vector3(rect.rect.center.x, Mathf.Lerp(rect.rect.yMin, rect.rect.yMax, vertical));
            var canvas = rect.GetComponentInParent<Canvas>();
            return RectTransformUtility.WorldToScreenPoint(canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null, rect.TransformPoint(local));
        }
        static bool FullyVisible(RectTransform viewport, RectTransform item)
        {
            var corners = new Vector3[4]; item.GetWorldCorners(corners);
            return corners.All(corner => viewport.rect.yMin - 1 <= viewport.InverseTransformPoint(corner).y && viewport.InverseTransformPoint(corner).y <= viewport.rect.yMax + 1);
        }
        IEnumerator Submit(Selectable control)
        {
            TraceSubmit("before", control);
            Focus(control);
            yield return Frames(2);
            TraceSubmit("focused", control);
            yield return KeyTap(Key.J);
            TraceSubmit("after", control);
        }
        void TraceSubmit(string phase, Selectable control)
        {
            var system = EventSystem.current;
            var selected = system ? system.currentSelectedGameObject : null;
            var coordinator = system ? MenuInputCoordinator.Ensure(system) : null;
            Trace("SUBMIT " + phase + " control=" + (control ? control.name : "<missing>") +
                " active=" + (control && control.gameObject.activeInHierarchy) +
                " interactable=" + (control && control.interactable) +
                " page=" + (_menu ? _menu.CurrentPage : "<gameplay>") +
                " selected=" + (selected ? selected.name : "<none>") +
                " inputReady=" + (coordinator && coordinator.InputReady) +
                " pointer=" + MenuInputCoordinator.PointerMode + " gamepad=" + MenuInputCoordinator.GamepadMode);
        }
        void Focus(Selectable control)
        {
            Check(control && control.gameObject.activeInHierarchy && control.interactable && EventSystem.current, "Requested UI control is available");
            if (control && EventSystem.current) EventSystem.current.SetSelectedGameObject(control.gameObject);
        }
        IEnumerator QuitThroughUi(Button button)
        {
            Focus(button); yield return Frames(2);
            _quitting = true; _deadline = Time.realtimeSinceStartup + 5;
            WriteResult(); Keys(Key.J);
            while (true) yield return null;
        }
        IEnumerator KeyTap(Key key) { Keys(key); yield return Frames(2); Keys(); yield return Frames(3); }
        IEnumerator PadTap(GamepadButton button)
        { InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button)); yield return Frames(2); InputSystem.QueueStateEvent(_pad, new GamepadState()); yield return Frames(3); }
        void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        void MoveMouse(Vector2 point) { _mousePosition = point; InputSystem.QueueStateEvent(_mouse, new MouseState { position = point }); }
        IEnumerator MouseClick(Vector2 point)
        {
            MoveMouse(point); yield return Frames(3);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = point }.WithButton(MouseButton.Left));
            yield return Frames(2);
            MoveMouse(point); yield return Frames(4);
        }
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static IEnumerator Seconds(float duration) { float end = Time.realtimeSinceStartup + duration; while (Time.realtimeSinceStartup < end) yield return null; }
        IEnumerator Until(Func<bool> predicate, string description, float timeout = 15)
        { float end = Time.realtimeSinceStartup + timeout; while (!predicate() && Time.realtimeSinceStartup < end) yield return null; Check(predicate(), description); }
        void Check(bool success, string description) { Trace((success ? "PASS " : "FAIL ") + description); if (!success) Fail(); }
        void Trace(string text) => File.AppendAllText(TracePath, text + "\n");
        void Update() { if (!_stopped && !string.IsNullOrEmpty(_output) && Time.realtimeSinceStartup > _deadline) { Trace("FAIL UI verification or real exit timed out"); Fail(); } }
        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            File.AppendAllText(Path.Combine(_output, "ui-errors.txt"), message + "\n" + stack + "\n"); Fail();
        }
        void Fail() { if (_stopped) return; _failed = _stopped = true; WriteResult(); StopAllCoroutines(); Application.Quit(2); }
        void WriteResult() => File.WriteAllText(Path.Combine(_output, "ui-result.txt"),
            (_failed ? "FAIL" : "PASS") + "\nNative menu regression: layer ordering, dropdowns, scrolling, pointer ownership, contextual prompts, title Back and pause release gates.\n");

        sealed class CanvasState { public Canvas Canvas; public Camera Camera; public float Distance; }
        void Capture(string filename)
        {
            var camera = Camera.main;
            if (!camera) camera = FindAnyObjectByType<SandboxRunner>()?.ArenaCamera;
            if (!camera) { Check(false, "Camera exists for capture"); return; }
            var states = new List<CanvasState>();
            var calibration = CalibrationSettings.Values;
            var target = RenderTexture.GetTemporary(1440, 900, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                // Match actual Overlay sharpness while using an explicit render request in a hidden player.
                CalibrationSettings.Set(0, 1, 1);
                foreach (var canvas in FindObjectsByType<Canvas>())
                {
                    if (!canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    states.Add(new CanvasState { Canvas = canvas, Camera = canvas.worldCamera, Distance = canvas.planeDistance });
                    canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + 1;
                }
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                File.WriteAllBytes(Path.Combine(_output, filename), pixels.EncodeToPNG());
            }
            finally
            {
                foreach (var state in states) if (state.Canvas)
                { state.Canvas.renderMode = RenderMode.ScreenSpaceOverlay; state.Canvas.worldCamera = state.Camera; state.Canvas.planeDistance = state.Distance; }
                CalibrationSettings.Set(calibration.x, calibration.y, calibration.z);
                Canvas.ForceUpdateCanvases(); RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); if (pixels) Destroy(pixels);
            }
        }
        void OnApplicationQuit()
        {
            if (!_quitting && !_failed) { _failed = true; Trace("FAIL Player exited before the real UI exit check"); WriteResult(); }
            Application.logMessageReceived -= OnLog;
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            if (_pad != null) InputSystem.RemoveDevice(_pad);
            if (_mouse != null) InputSystem.RemoveDevice(_mouse);
        }
    }
}
