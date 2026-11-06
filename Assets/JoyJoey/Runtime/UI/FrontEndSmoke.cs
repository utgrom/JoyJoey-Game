using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoyJoey.Sandbox;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    /// <summary>Opt-in native UI verification with a separate save profile supplied by the caller.</summary>
    public sealed class FrontEndSmoke : MonoBehaviour
    {
        Keyboard _keyboard;
        Gamepad _gamepad;
        MainMenuController _menu;
        LevelRunner _level;
        GameSession _session;
        string _output;
        int _phase = 1;
        bool _failed, _quitRequested, _stopped;
        float _deadline;
        string TracePath => Path.Combine(_output, "phase" + _phase + "-trace.txt");
        string ErrorsPath => Path.Combine(_output, "phase" + _phase + "-errors.txt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-frontEndSmoke") < 0) return;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var root = new GameObject("Stage 2 native verification");
            DontDestroyOnLoad(root);
            root.AddComponent<FrontEndSmoke>();
        }

        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            _output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Validation/Stage2"));
            int arg = Array.IndexOf(args, "-frontEndSmokeOutput");
            if (arg >= 0 && arg + 1 < args.Length) _output = args[arg + 1];
            arg = Array.IndexOf(args, "-frontEndSmokePhase");
            if (arg >= 0 && arg + 1 < args.Length) int.TryParse(args[arg + 1], out _phase);
            Directory.CreateDirectory(_output);
            File.WriteAllText(TracePath, "");
            File.WriteAllText(ErrorsPath, "");
            _deadline = Time.realtimeSinceStartup + 120;
            Application.logMessageReceived += OnLog;
            _keyboard = InputSystem.AddDevice<Keyboard>("FrontEndSmokeKeyboard");
            _gamepad = InputSystem.AddDevice<Gamepad>("FrontEndSmokeGamepad");
            Check(_phase == 1 || _phase == 2, "Valid verification phase");
            yield return WaitForMenu();
            _session = GameSession.Instance;
            Check(_session != null, "One persistent game session initialized");
            Check(_session.HasSavedGame == (_phase == 2), _phase == 1 ? "Fresh isolated profile" : "Saved campaign detected after restart");
            if (_phase == 1) yield return FirstRun();
            else yield return RestartRun();
        }

        IEnumerator FirstRun()
        {
            Check(!_menu.IsRevealed, "Title gate initially hides menu options");
            Capture("title.png");
            Keys(Key.Enter);
            yield return Frames(5);
            Check(_menu && _menu.IsRevealed && !_menu.ReadyForInput && !_session.IsLoading,
                "Holding the first button reveals the menu without starting the game");
            Check(FindAnyObjectByType<LevelRunner>() == null, "First press does not enter the level");
            Keys();
            yield return Until(() => _menu && _menu.ReadyForInput, "Menu accepts input after release");
            Check(_menu.StartLabel.text == "Empezar juego", "New profile shows Empezar juego");
            Capture("menu.png");

            yield return KeyTap(Key.DownArrow);
            yield return KeyTap(Key.DownArrow);
            Check(EventSystem.current && EventSystem.current.currentSelectedGameObject == _menu.ExtrasButton.gameObject,
                "Keyboard navigation reaches Extras");
            yield return KeyTap(Key.Enter);
            yield return Until(() => _menu.CurrentPage == "Extras", "Extras opens through UI Submit");
            Capture("extras.png");
            yield return KeyTap(Key.Escape);
            yield return Until(() => _menu.CurrentPage == "Principal", "Escape returns from Extras");

            yield return PadTap(GamepadButton.DpadDown);
            Check(EventSystem.current && EventSystem.current.currentSelectedGameObject == _menu.OptionsButton.gameObject,
                "Gamepad navigation reaches Opciones");
            yield return PadTap(GamepadButton.South);
            yield return Until(() => _menu.CurrentPage == "Opciones", "Gamepad Submit opens Opciones");
            Capture("options.png");
            yield return PadTap(GamepadButton.East);
            yield return Until(() => _menu.CurrentPage == "Principal", "Gamepad Back returns from Opciones");

            yield return Submit(_menu.StartButton);
            yield return WaitForLevel();
            float startX = _level.Simulation.Player.Position.x;
            Keys(Key.D);
            yield return Until(() => _level.Simulation.Player.Position.x > startX + 3, "Keyboard moves Joey in the campaign");
            Keys();
            yield return Until(() => _level.Simulation.Player.Grounded && Mathf.Abs(_level.Simulation.Player.Velocity.x) < .01f,
                "Joey rests at a safe checkpoint");
            Capture("level.png");
            yield return KeyTap(Key.Escape);
            yield return Until(() => _session.IsPaused, "Escape opens pause");
            int tick = _level.Simulation.Tick;
            yield return Seconds(.25f);
            Check(_level.Simulation.Tick == tick, "Simulation does not advance while paused");
            Check(_session.Store.TryLoad(out SavedGameData expected, out string error), "Pause writes a readable campaign checkpoint: " + error);
            Check(Vector2.Distance(expected.Position, _level.Simulation.Player.Position) < .05f, "Saved checkpoint matches the paused safe position");
            File.WriteAllText(Path.Combine(_output, "expected.txt"), JsonUtility.ToJson(expected, true));
            Capture("pause.png");

            var pause = FindAnyObjectByType<PauseMenuController>();
            Check(pause != null, "Reusable pause controller exists");
            Focus(pause.ResumeButton);
            yield return Frames(2);
            Pad(new GamepadState().WithButton(GamepadButton.South));
            yield return Until(() => !_session.IsPaused, "Gamepad Submit resumes the game");
            yield return Frames(8);
            Check(_session.BlocksGameplay && _level.Simulation.Player.Grounded &&
                Mathf.Abs(_level.Simulation.Player.Position.y - expected.Position.y) < .05f,
                "Held Resume button does not become a gameplay jump");
            Pad(new GamepadState());
            yield return Until(() => !_session.BlocksGameplay, "Gameplay unlocks after Resume button release");
            yield return Seconds(.15f);
            Check(_level.Simulation.Player.Grounded, "Resume leaves Joey grounded");

            yield return KeyTap(Key.Escape);
            yield return Until(() => _session.IsPaused, "Campaign pauses again");
            yield return Submit(pause.MainMenuButton);
            yield return WaitForMenu();
            yield return RevealMenu();
            Check(_menu.StartLabel.text == "Continuar", "Main menu now offers Continuar");
            byte[] beforePractice = File.ReadAllBytes(_session.Store.FilePath);
            yield return Submit(_menu.SandboxButton);
            yield return Until(() => !_session.IsLoading && FindAnyObjectByType<SandboxRunner>()?.Simulation != null,
                "Circo onírico opens through the menu");
            Keys(Key.D);
            yield return Seconds(.15f);
            Keys();
            yield return PadTap(GamepadButton.Start);
            yield return Until(() => _session.IsPaused, "Gamepad Start pauses the sandbox");
            Capture("sandbox-pause.png");
            CheckSingleServices();
            yield return Submit(FindAnyObjectByType<PauseMenuController>().MainMenuButton);
            yield return WaitForMenu();
            yield return RevealMenu();
            Check(beforePractice.SequenceEqual(File.ReadAllBytes(_session.Store.FilePath)),
                "Sandbox practice and return preserve campaign bytes");
            CheckSingleServices();
            yield return QuitThroughUI(_menu.QuitButton);
        }

        IEnumerator RestartRun()
        {
            yield return RevealMenu();
            Check(_menu.StartLabel.text == "Continuar", "Restarted application shows Continuar");
            string path = Path.Combine(_output, "expected.txt");
            Check(File.Exists(path), "Previous process recorded its expected checkpoint");
            var expected = JsonUtility.FromJson<SavedGameData>(File.ReadAllText(path));
            yield return Submit(_menu.StartButton);
            yield return WaitForLevel();
            Check(Vector2.Distance(_level.Simulation.Player.Position, expected.Position) < .1f,
                "Continuar restores the checkpoint saved by the previous process");
            Check(_level.Simulation.Player.Facing == expected.Facing, "Continuar restores facing direction");
            Check(_level.Simulation.Player.Grounded, "Continued checkpoint is supported by the floor");
            Capture("continued-level.png");
            CheckSingleServices();
            yield return PadTap(GamepadButton.Start);
            yield return Until(() => _session.IsPaused, "Controller can pause after application restart");
            yield return QuitThroughUI(FindAnyObjectByType<PauseMenuController>().QuitButton);
        }

        IEnumerator WaitForMenu()
        {
            yield return Until(() => (_menu = FindAnyObjectByType<MainMenuController>()) &&
                GameSession.Instance && !GameSession.Instance.IsLoading, "Main menu scene initialized");
            yield return Frames(3);
        }

        IEnumerator RevealMenu()
        {
            if (!_menu.IsRevealed) yield return KeyTap(Key.Enter);
            yield return Until(() => _menu && _menu.ReadyForInput, "Menu revealed and ready");
        }

        IEnumerator WaitForLevel()
        {
            yield return Until(() => (_level = FindAnyObjectByType<LevelRunner>()) && _level.Simulation != null &&
                GameSession.Instance && !GameSession.Instance.IsLoading && !GameSession.Instance.BlocksGameplay,
                "Campaign level initialized and accepts gameplay input");
            yield return Frames(3);
        }

        void CheckSingleServices()
        {
            Check(FindObjectsByType<GameSession>().Length == 1, "Only one persistent game session");
            Check(FindObjectsByType<AudioListener>().Count(x => x.enabled) == 1, "Only one active audio listener");
            Check(FindObjectsByType<EventSystem>().Count(x => x.isActiveAndEnabled) == 1, "Only one active EventSystem");
        }

        IEnumerator Submit(Button button)
        {
            Focus(button);
            yield return Frames(2);
            yield return KeyTap(Key.Enter);
        }

        void Focus(Button button)
        {
            Check(button && button.gameObject.activeInHierarchy && button.interactable && EventSystem.current,
                "UI target is available: " + (button ? button.name : "missing"));
            if (EventSystem.current && button) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        IEnumerator QuitThroughUI(Button button)
        {
            Focus(button);
            yield return Frames(2);
            _quitRequested = true;
            _deadline = Time.realtimeSinceStartup + 5;
            Trace("PASS Invoking the real Salir UI button; process must exit itself");
            WriteResult();
            Keys(Key.Enter);
            // The production QuitGame callback must terminate the process. Only failure watchdogs force exit.
            while (true) yield return null;
        }

        IEnumerator KeyTap(Key key)
        {
            Keys(key);
            yield return Frames(2);
            Keys();
            yield return Frames(3);
        }

        IEnumerator PadTap(GamepadButton button)
        {
            Pad(new GamepadState().WithButton(button));
            yield return Frames(2);
            Pad(new GamepadState());
            yield return Frames(3);
        }

        void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        void Pad(GamepadState state) => InputSystem.QueueStateEvent(_gamepad, state);
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static IEnumerator Seconds(float duration)
        {
            float end = Time.realtimeSinceStartup + duration;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        IEnumerator Until(Func<bool> predicate, string description)
        {
            float end = Time.realtimeSinceStartup + 15;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Check(predicate(), description);
        }

        void Check(bool passed, string description)
        {
            Trace((passed ? "PASS " : "FAIL ") + description);
            if (passed) return;
            _failed = true;
            FailAndQuit();
        }

        void Trace(string description) => File.AppendAllText(TracePath, description + "\n");

        void Update()
        {
            if (_stopped || string.IsNullOrEmpty(_output) || Time.realtimeSinceStartup <= _deadline) return;
            _failed = true;
            Trace("FAIL " + (_quitRequested ? "Salir did not close the process" : "Verification watchdog timed out"));
            FailAndQuit();
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            _failed = true;
            File.AppendAllText(ErrorsPath, message + "\n" + stack + "\n");
            FailAndQuit();
        }

        void FailAndQuit()
        {
            if (_stopped) return;
            _stopped = true;
            WriteResult();
            StopAllCoroutines();
            Application.Quit(2);
        }

        void WriteResult() => File.WriteAllText(Path.Combine(_output, "phase" + _phase + "-result.txt"),
            (_failed ? "FAIL" : "PASS") + "\nStage 2 phase " + _phase +
            ": native keyboard/gamepad UI, scene flow, pause, isolated campaign persistence and real UI exit.\n");

        sealed class CanvasState
        {
            public Canvas Canvas;
            public RenderMode Mode;
            public Camera Camera;
            public float Distance;
        }

        void Capture(string filename)
        {
            var camera = Camera.main;
            if (!camera) camera = FindAnyObjectByType<SandboxRunner>()?.ArenaCamera;
            if (!camera) { Check(false, "A camera exists for " + filename); return; }
            var states = new List<CanvasState>();
            var target = RenderTexture.GetTemporary(1440, 900, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                // Overlay UI is absent from a camera-only render request. Borrow camera mode for this capture.
                foreach (var canvas in FindObjectsByType<Canvas>())
                {
                    if (!canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    states.Add(new CanvasState { Canvas = canvas, Mode = canvas.renderMode, Camera = canvas.worldCamera, Distance = canvas.planeDistance });
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = camera.nearClipPlane + 1;
                }
                Canvas.ForceUpdateCanvases();
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(_output, "phase" + _phase + "-" + filename), pixels.EncodeToPNG());
            }
            finally
            {
                foreach (var state in states)
                {
                    if (!state.Canvas) continue;
                    state.Canvas.renderMode = state.Mode;
                    state.Canvas.worldCamera = state.Camera;
                    state.Canvas.planeDistance = state.Distance;
                }
                Canvas.ForceUpdateCanvases();
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (pixels) Destroy(pixels);
            }
        }

        void OnApplicationQuit()
        {
            if (!_quitRequested && !_failed)
            {
                _failed = true;
                Trace("FAIL Process closed before the UI exit check");
                WriteResult();
            }
            Application.logMessageReceived -= OnLog;
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            if (_gamepad != null) InputSystem.RemoveDevice(_gamepad);
        }
    }
}
