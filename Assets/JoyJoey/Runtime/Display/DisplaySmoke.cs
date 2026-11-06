using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoyJoey.Game.UI;
using JoyJoey.Sandbox;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace JoyJoey.Game.Display
{
    /// <summary>Explicit native-player verification using isolated settings and campaign directories.</summary>
    public sealed class DisplaySmoke : MonoBehaviour
    {
        Keyboard _keyboard;
        Gamepad _pad;
        MainMenuController _menu;
        DisplayOptionsController _page;
        GameDisplay _display;
        string _output;
        int _phase = 1;
        bool _failed, _quitting, _stopped;
        float _deadline;
        string TracePath => Path.Combine(_output, "display-phase" + _phase + "-trace.txt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-displaySmoke") < 0) return;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var root = new GameObject("Stage 6 display verification");
            DontDestroyOnLoad(root);
            root.AddComponent<DisplaySmoke>();
        }

        IEnumerator Start()
        {
            var args = Environment.GetCommandLineArgs();
            _output = Argument(args, "-displaySmokeOutput", Path.Combine(Application.dataPath, "../../../Validation/Stage6"));
            int.TryParse(Argument(args, "-displaySmokePhase", "1"), out _phase);
            Directory.CreateDirectory(_output);
            File.WriteAllText(TracePath, "");
            File.WriteAllText(Path.Combine(_output, "display-phase" + _phase + "-errors.txt"), "");
            Application.logMessageReceived += OnLog;
            _deadline = Time.realtimeSinceStartup + 150;
            Check(Array.IndexOf(args, "-joySettingsDirectory") >= 0 && Array.IndexOf(args, "-joySaveDirectory") >= 0,
                "Native verification explicitly uses isolated profiles");
            _keyboard = InputSystem.AddDevice<Keyboard>("DisplaySmokeKeyboard");
            _pad = InputSystem.AddDevice<Gamepad>("DisplaySmokeGamepad");
            yield return Until(() => (_menu = FindAnyObjectByType<MainMenuController>()) && GameDisplay.Instance,
                "Menu and persistent display service initialize");
            _display = GameDisplay.Instance;
            yield return PadTap(GamepadButton.South);
            yield return Until(() => _menu.ReadyForInput, "Gamepad dismisses title and releases input");
            yield return OpenMainDisplay();
            if (_phase == 1) yield return FirstRun();
            else yield return RestartRun();
        }
        static string Argument(string[] args, string option, string fallback)
        { int i = Array.IndexOf(args, option); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }

        IEnumerator OpenMainDisplay()
        {
            yield return Submit(_menu.OptionsButton);
            yield return Submit(_menu.DisplayButton);
            _page = _menu.DisplayPage;
            yield return Until(() => _page && _page.IsOpen, "Display options open through the main menu");
            yield return Frames(3);
        }
        IEnumerator FirstRun()
        {
            Check(!_display.IsPreviewing && _display.Current.WindowMode == DisplayWindowMode.Borderless,
                "New profile defaults to desktop borderless mode");
            Check(Screen.fullScreenMode == FullScreenMode.FullScreenWindow && Screen.width == _display.DesktopWidth && Screen.height == _display.DesktopHeight,
                "Borderless output uses native desktop dimensions");
            var desktop = Screen.currentResolution;
            var original = _display.Preferences.Copy();
            CaptureUi("display-basic.png");

            // Use the actual dropdown with keyboard events, then select precise fixture values.
            yield return Submit(_page.ModeDropdown);
            yield return KeyTap(Key.UpArrow);
            yield return KeyTap(Key.Enter);
            Check(_page.Draft.WindowMode == DisplayWindowMode.Windowed, "Keyboard changes the window mode dropdown");
            int width = Math.Min(1280, _display.DesktopWidth), height = Math.Min(720, _display.DesktopHeight);
            var resolution = _display.GetWindowResolutions().First(r => r.Width == width && r.Height == height);
            var choices = _display.GetWindowResolutions();
            _page.ResolutionDropdown.value = Array.IndexOf(choices, resolution);
            _page.FpsDropdown.value = 2; // 60
            yield return Submit(_page.AdvancedButton);
            _page.RenderScaleDropdown.value = 3; // 50%
            float brightness = _page.Draft.Brightness;
            Focus(_page.BrightnessSlider);
            yield return KeyTap(Key.LeftArrow);
            Check(_page.Draft.Brightness < brightness, "Keyboard adjusts brightness draft");
            float gamma = _page.Draft.Gamma;
            Focus(_page.GammaSlider);
            yield return PadTap(GamepadButton.DpadRight);
            Check(_page.Draft.Gamma > gamma, "Gamepad adjusts gamma draft");
            _page.BrightnessSlider.value = .12f;
            _page.ContrastSlider.value = 1.08f;
            _page.GammaSlider.value = 1.2f;
            Check(Same(original, _display.Current) && !File.Exists(_display.Store.FilePath),
                "Editing a draft changes neither the live configuration nor saved settings");
            CaptureUi("display-advanced-draft.png");
            yield return KeyTap(Key.K);
            yield return Until(() => Screen.width == width && Screen.height == height && Screen.fullScreenMode == FullScreenMode.Windowed,
                "Applied preview switches to the requested window size");
            yield return Frames(8);
            Check(_display.IsPreviewing && _page.ConfirmPanel.activeInHierarchy && File.Exists(_display.Store.PendingPath),
                "Apply opens a timed confirmation and writes a recovery journal");
            Check(_display.RuntimePipeline && Mathf.Abs(_display.RuntimePipeline.renderScale - .5f) < .001f,
                "URP uses an independent 50 percent internal resolution");
            VerifyWorldPixels();
            Check(CalibrationSettings.RenderedFrameCount > 0 && CalibrationSettings.LastRenderWidth == width / 2 && CalibrationSettings.LastRenderHeight == height / 2,
                "Actual calibration render target is half the native UI/window dimensions: " + CalibrationSettings.LastRenderWidth + "x" + CalibrationSettings.LastRenderHeight);
            CaptureUi("display-confirmation.png");
            yield return Submit(_page.CancelPreviewButton);
            yield return Until(() => Screen.fullScreenMode == FullScreenMode.FullScreenWindow && Screen.width == _display.DesktopWidth,
                "Revert restores native borderless presentation");
            Check(Same(original, _display.Current) && !File.Exists(_display.Store.PendingPath) && !File.Exists(_display.Store.FilePath),
                "Rejected preview restores settings and removes its recovery journal");

            // Repeat with the candidate, then commit through the UI.
            _page.ModeDropdown.value = 0;
            _page.ResolutionDropdown.value = Array.IndexOf(choices, resolution);
            _page.FpsDropdown.value = 2;
            _page.RenderScaleDropdown.value = 3;
            _page.BrightnessSlider.value = .12f;
            _page.ContrastSlider.value = 1.08f;
            _page.GammaSlider.value = 1.2f;
            yield return KeyTap(Key.K);
            yield return Submit(_page.KeepButton);
            yield return Until(() => Screen.width == width && Screen.height == height, "Confirmed window size settles");
            Check(!_display.IsPreviewing && File.Exists(_display.Store.FilePath) && !File.Exists(_display.Store.PendingPath),
                "Keep commits settings and clears the journal");
            Check(Application.targetFrameRate == 60 && QualitySettings.vSyncCount == 0, "FPS cap is applied centrally");
            string confirmedJson = File.ReadAllText(_display.Store.FilePath);
            File.WriteAllText(Path.Combine(_output, "display-expected.json"), JsonUtility.ToJson(_display.Preferences, true));

            yield return Submit(_page.BasicButton);
            _page.VSyncDropdown.value = 1;
            yield return KeyTap(Key.K);
            Check(QualitySettings.vSyncCount == 1 && Application.targetFrameRate == -1 && !_page.FpsDropdown.interactable,
                "VSync takes priority and disables the FPS cap selector");
            float remaining = _display.RemainingSeconds;
            Time.timeScale = 0;
            yield return Seconds(1.2f);
            Check(_display.RemainingSeconds < remaining - .9f, "Confirmation countdown advances while simulation time is paused");
            yield return Until(() => !_display.IsPreviewing, "Unconfirmed preview reverts automatically after 15 real seconds", 18);
            Time.timeScale = 1;
            Check(File.ReadAllText(_display.Store.FilePath) == confirmedJson && Same(_display.Preferences, _display.Current) &&
                QualitySettings.vSyncCount == 0 && Application.targetFrameRate == 60,
                "Timeout preserves the previous saved settings and restores the FPS cap");
            Check(desktop.width == Screen.currentResolution.width && desktop.height == Screen.currentResolution.height,
                "Window and borderless transitions preserve desktop display resolution");

            yield return KeyTap(Key.Escape);
            yield return KeyTap(Key.Escape);
            yield return Submit(_menu.StartButton);
            yield return Until(() => FindAnyObjectByType<LevelRunner>() && !GameSession.Instance.BlocksGameplay,
                "Campaign loads with the same display service");
            yield return Frames(4);
            yield return KeyTap(Key.Escape);
            yield return Until(() => GameSession.Instance.IsPaused, "Campaign pauses");
            var pause = FindAnyObjectByType<PauseMenuController>();
            yield return Submit(pause.OptionsButton);
            yield return Submit(pause.DisplayButton);
            _page = pause.DisplayPage;
            yield return Until(() => _page.IsOpen, "Display options are available from pause");
            Check(Same(_display.Preferences, _page.Draft) && Time.timeScale == 0, "Pause page shows the saved values while gameplay stays frozen");
            yield return Submit(_page.AdvancedButton);
            _page.GammaSlider.value = 1.4f;
            yield return KeyTap(Key.K);
            yield return Seconds(.5f);
            CaptureUi("display-pause.png");
            yield return PadTap(GamepadButton.Start);
            Check(!GameSession.Instance.IsPaused && !_display.IsPreviewing && Same(_display.Current, _display.Preferences),
                "Closing pause restores an unconfirmed display preview before resuming");
            yield return KeyTap(Key.Escape);
            yield return Frames(3);
            yield return Submit(pause.MainMenuButton);
            yield return ReturnedMenu();
            yield return Submit(_menu.SandboxButton);
            yield return Until(() => FindAnyObjectByType<SandboxRunner>()?.Simulation != null && !GameSession.Instance.BlocksGameplay,
                "Combat practice loads without changing display settings");
            yield return Frames(4);
            Check(Application.targetFrameRate == 60 && Mathf.Abs(_display.RuntimePipeline.renderScale - .5f) < .001f,
                "Practice respects the selected FPS cap and internal resolution");
            yield return KeyTap(Key.Escape);
            yield return Until(() => GameSession.Instance.IsPaused, "Combat practice pauses");
            yield return Submit(pause.MainMenuButton);
            yield return ReturnedMenu();
            Check(FindObjectsByType<GameDisplay>().Length == 1, "Scene transitions preserve exactly one display service");
            var unconfirmed = _display.Preferences.Copy();
            unconfirmed.FpsCap = 30;
            Check(_display.TryPreview(unconfirmed), "Prepare an interrupted unconfirmed preview for restart recovery");
            yield return QuitThroughUi(_menu.QuitButton);
        }

        IEnumerator ReturnedMenu()
        {
            yield return Until(() => (_menu = FindAnyObjectByType<MainMenuController>()) && !GameSession.Instance.IsLoading, "Return to main menu");
            yield return KeyTap(Key.Enter);
            yield return Until(() => _menu.ReadyForInput, "Returned menu accepts input");
        }
        IEnumerator RestartRun()
        {
            var expected = JsonUtility.FromJson<DisplayPreferences>(File.ReadAllText(Path.Combine(_output, "display-expected.json")));
            Check(Same(expected, _display.Preferences) && Same(expected, _display.Current), "All confirmed display settings survive restart");
            Check(!File.Exists(_display.Store.PendingPath) && !_display.IsPreviewing,
                "Restart recovers the confirmed settings and clears the interrupted preview");
            yield return Until(() => Screen.fullScreenMode == FullScreenMode.Windowed && Screen.width == expected.WindowWidth && Screen.height == expected.WindowHeight,
                "Saved window mode and dimensions restore on startup");
            Check(Application.targetFrameRate == 60 && QualitySettings.vSyncCount == 0 && _page.ModeDropdown.value == 0 && _page.FpsDropdown.value == 2,
                "Restored UI and native frame controls match the saved configuration");
            yield return Submit(_page.AdvancedButton);
            Check(_page.RenderScaleDropdown.value == 3 && Mathf.Abs(_page.GammaSlider.value - expected.Gamma) < .001f,
                "Advanced page restores the render scale and calibration values");
            CaptureUi("display-restored.png");
            yield return KeyTap(Key.Escape);
            yield return KeyTap(Key.Escape);
            yield return QuitThroughUi(_menu.QuitButton);
        }

        static bool Same(DisplayPreferences a, DisplayPreferences b) => JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        IEnumerator Submit(Selectable control) { Focus(control); yield return Frames(2); yield return KeyTap(Key.Enter); }
        void Focus(Selectable control)
        {
            Check(control && control.gameObject.activeInHierarchy && control.interactable && EventSystem.current, "UI control is available");
            if (EventSystem.current && control) EventSystem.current.SetSelectedGameObject(control.gameObject);
        }
        IEnumerator QuitThroughUi(Button button)
        {
            Focus(button); yield return Frames(2);
            _quitting = true; _deadline = Time.realtimeSinceStartup + 5;
            WriteResult(); Keys(Key.Enter);
            while (true) yield return null;
        }
        IEnumerator KeyTap(Key key) { Keys(key); yield return Frames(2); Keys(); yield return Frames(3); }
        IEnumerator PadTap(GamepadButton button)
        { InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button)); yield return Frames(2); InputSystem.QueueStateEvent(_pad, new GamepadState()); yield return Frames(3); }
        void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static IEnumerator Seconds(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
        IEnumerator Until(Func<bool> predicate, string description, float timeout = 15)
        { float end = Time.realtimeSinceStartup + timeout; while (!predicate() && Time.realtimeSinceStartup < end) yield return null; Check(predicate(), description); }
        void Check(bool success, string description) { Trace((success ? "PASS " : "FAIL ") + description); if (!success) Fail(); }
        void Trace(string message) => File.AppendAllText(TracePath, message + "\n");
        void Update()
        { if (!_stopped && !string.IsNullOrEmpty(_output) && Time.realtimeSinceStartup > _deadline) { Trace("FAIL Native verification or real UI exit timed out"); Fail(); } }
        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            File.AppendAllText(Path.Combine(_output, "display-phase" + _phase + "-errors.txt"), message + "\n" + stack + "\n"); Fail();
        }
        void Fail() { if (_stopped) return; _failed = _stopped = true; WriteResult(); StopAllCoroutines(); Application.Quit(2); }
        void WriteResult() => File.WriteAllText(Path.Combine(_output, "display-phase" + _phase + "-result.txt"),
            (_failed ? "FAIL" : "PASS") + "\nStage 6 phase " + _phase + ": native output, independent rendering, calibration pixels, UI, timed recovery and restart preferences.\n");

        void VerifyWorldPixels()
        {
            var saved = CalibrationSettings.Values;
            CalibrationSettings.Set(0, 1, 1);
            var neutral = CapturePixels(false, "display-world-neutral.png");
            CalibrationSettings.Set(saved.x, saved.y, saved.z);
            var adjusted = CapturePixels(false, "display-world-adjusted.png");
            double difference = 0;
            for (int i = 0; i < neutral.Length; i++)
                difference += Math.Abs(neutral[i].r - adjusted[i].r) + Math.Abs(neutral[i].g - adjusted[i].g) + Math.Abs(neutral[i].b - adjusted[i].b);
            difference /= neutral.Length * 3.0 * 255;
            Check(difference > .01, "Native shader changes captured world pixels (mean RGB difference " + difference.ToString("0.0000") + ")");
        }
        void CaptureUi(string filename)
        {
            var saved = CalibrationSettings.Values;
            try { CalibrationSettings.Set(0, 1, 1); CapturePixels(true, filename); }
            finally { CalibrationSettings.Set(saved.x, saved.y, saved.z); }
        }
        sealed class CanvasState { public Canvas Canvas; public Camera Camera; public float Distance; }
        Color32[] CapturePixels(bool includeUi, string filename)
        {
            var camera = Camera.main;
            if (!camera) camera = FindAnyObjectByType<SandboxRunner>()?.ArenaCamera;
            if (!camera) { Check(false, "Camera exists for native capture"); return Array.Empty<Color32>(); }
            var states = new List<CanvasState>();
            // Hidden batch players may not present an automatic frame. Explicitly render at the
            // real output dimensions so URP's intermediate descriptor proves the internal scale.
            var target = RenderTexture.GetTemporary(includeUi ? 1440 : Screen.width, includeUi ? 900 : Screen.height, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                if (includeUi)
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
                return pixels.GetPixels32();
            }
            finally
            {
                foreach (var state in states) if (state.Canvas)
                { state.Canvas.renderMode = RenderMode.ScreenSpaceOverlay; state.Canvas.worldCamera = state.Camera; state.Canvas.planeDistance = state.Distance; }
                Canvas.ForceUpdateCanvases(); RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); if (pixels) Destroy(pixels);
            }
        }
        void OnApplicationQuit()
        {
            if (!_quitting && !_failed) { _failed = true; Trace("FAIL Process exited before the UI exit check"); WriteResult(); }
            Application.logMessageReceived -= OnLog;
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            if (_pad != null) InputSystem.RemoveDevice(_pad);
        }
    }
}
