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

namespace JoyJoey.Game.Audio
{
    /// <summary>Opt-in software audio verification. Uses profiles supplied by the caller, never live player data.</summary>
    public sealed class AudioSmoke : MonoBehaviour
    {
        Keyboard _keyboard;
        Gamepad _pad;
        MainMenuController _menu;
        AudioOptionsController _page;
        GameAudio _audio;
        string _output;
        int _phase = 1;
        bool _failed, _quitting, _stopped;
        float _deadline;
        string TracePath => Path.Combine(_output, "audio-phase" + _phase + "-trace.txt");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-audioSmoke") < 0) return;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var root = new GameObject("Stage 3 audio verification");
            DontDestroyOnLoad(root);
            root.AddComponent<AudioSmoke>();
        }

        IEnumerator Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            _output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Validation/Stage3"));
            int option = Array.IndexOf(args, "-audioSmokeOutput");
            if (option >= 0 && option + 1 < args.Length) _output = args[option + 1];
            option = Array.IndexOf(args, "-audioSmokePhase");
            if (option >= 0 && option + 1 < args.Length) int.TryParse(args[option + 1], out _phase);
            Directory.CreateDirectory(_output);
            File.WriteAllText(TracePath, "");
            File.WriteAllText(Path.Combine(_output, "audio-phase" + _phase + "-errors.txt"), "");
            Application.logMessageReceived += OnLog;
            _deadline = Time.realtimeSinceStartup + 120;
            _keyboard = InputSystem.AddDevice<Keyboard>("AudioSmokeKeyboard");
            _pad = InputSystem.AddDevice<Gamepad>("AudioSmokeGamepad");
            yield return Until(() => (_menu = FindAnyObjectByType<MainMenuController>()) && GameAudio.Instance,
                "Menu and persistent audio initialize");
            _audio = GameAudio.Instance;
            yield return KeyTap(Key.Enter);
            yield return Until(() => _menu.ReadyForInput, "Menu accepts input after title release");
            yield return OpenMainAudio();
            if (_phase == 1) yield return FirstRun();
            else yield return RestartRun();
        }

        IEnumerator OpenMainAudio()
        {
            yield return Submit(_menu.OptionsButton);
            yield return Until(() => _menu.CurrentPage == "Opciones", "Options opens through UI");
            yield return Submit(_menu.AudioButton);
            _page = _menu.AudioPage;
            yield return Until(() => _page && _page.IsOpen, "Audio page opens through UI");
            yield return Frames(3);
        }

        IEnumerator FirstRun()
        {
            Capture("audio-options-default.png");
            float master = _audio.Preferences.Master;
            Focus(_page.MasterSlider);
            yield return KeyTap(Key.LeftArrow);
            Check(_audio.Preferences.Master < master, "Keyboard adjusts General slider");
            float music = _audio.Preferences.Music;
            Focus(_page.MusicSlider);
            yield return PadTap(GamepadButton.DpadLeft);
            Check(_audio.Preferences.Music < music, "Gamepad adjusts Music slider");
            CheckMixerVolumes();

            // The same Slider.value path is used by pointer dragging. Preserve distinct values for restart verification.
            _page.MasterSlider.value = .74f;
            _page.MusicSlider.value = .42f;
            _page.SfxSlider.value = .63f;
            _page.UiSlider.value = .51f;
            yield return Submit(_page.TestMusicButton);
            yield return Until(() => _audio.IsTestMusicPlaying, "Music test starts through its UI button");
            yield return Until(() => _audio.ProcessedBlocks > 3 && _audio.LastPeak > .00001f,
                "Final listener receives nonzero mixed audio samples");
            int blocks = _audio.ProcessedBlocks;
            _audio.SetVolume(AudioChannel.Master, 0);
            yield return Seconds(.3f);
            Check(_audio.ProcessedBlocks > blocks && _audio.LastPeak < .0001f, "General zero attenuates the complete final mix");
            _audio.SetVolume(AudioChannel.Master, .74f);
            _page.SpeakerModeDropdown.value = 1;
            yield return Frames(3);
            Check(_audio.Preferences.Mono && _audio.Processor.Mono, "Mono UI updates final signal processing");
            Check(_audio.IsTestMusicPlaying, "Changing mono does not restart or stop music");
            yield return Submit(_page.TestSfxButton);
            yield return Submit(_page.TestUiButton);
            CheckMixerVolumes();

            var devices = _audio.GetOutputDevices();
            Check(devices != null && devices.Any(d => d.Id == ""), "Playback list includes the system default route");
            var endpoint = devices.FirstOrDefault(d => d.IsDefault && d.Id != "") ?? devices.FirstOrDefault(d => d.Id != "");
            if (endpoint != null)
            {
                Check(_audio.TrySetOutputDevice(endpoint.Id), "A real Windows playback endpoint accepts the selected route: " + _audio.LastError);
                yield return Until(() => _audio.Processor.Output.RenderedFrames > 0 && _audio.Processor.Output.NonSilentFrames > 0,
                    "WASAPI receives nonzero frames from the complete Unity mix");
                Trace("Endpoint transport frames=" + _audio.Processor.Output.RenderedFrames + "; software proof, not a hardware listening test.");
                Check(_audio.TrySetOutputDevice(""), "Switching back to system default releases the explicit route");
            }
            else Trace("SKIP Explicit Windows endpoint transport: this host exposes no active playback endpoint.");
            Check(!_audio.TrySetOutputDevice("joyjoey.invalid.test.endpoint"), "An unavailable output device is rejected");
            Check(_audio.Preferences.OutputDeviceId == "" && !_audio.Processor.Output.IsRouting,
                "An unavailable output falls back to the system default route");
            Check(_audio.TrySetOutputDevice(""), "System default remains selectable after an unavailable device");

            yield return Submit(_page.DefaultsButton);
            Check(SameVolumes(AudioPreferences.Defaults(), _audio.Preferences) && _audio.Preferences.Mono,
                "Restoring volumes preserves the selected mono mode");
            _page.MasterSlider.value = .74f;
            _page.MusicSlider.value = .42f;
            _page.SfxSlider.value = .63f;
            _page.UiSlider.value = .51f;

            var beforeContext = _audio.Preferences.Copy();
            _audio.SetContext(AudioContext.Cutscene);
            yield return Seconds(.2f);
            CheckMixerVolumes();
            Check(SameVolumes(beforeContext, _audio.Preferences), "Effect snapshots preserve all four user volumes");
            _audio.SetContext(AudioContext.Normal);
            _audio.StopTestMusic();
            Check(_audio.SavePreferences(), "Preferences save succeeds");
            File.WriteAllText(Path.Combine(_output, "audio-expected.json"), JsonUtility.ToJson(_audio.Preferences, true));
            Capture("audio-options-configured.png");

            yield return KeyTap(Key.Escape);
            yield return KeyTap(Key.Escape);
            yield return Until(() => _menu.CurrentPage == "Principal", "Back returns to main menu");
            yield return Submit(_menu.StartButton);
            LevelRunner level = null;
            yield return Until(() => (level = FindAnyObjectByType<LevelRunner>()) && level.Simulation != null &&
                !GameSession.Instance.IsLoading && !GameSession.Instance.BlocksGameplay, "Campaign starts with the same audio service");
            yield return Frames(3); // Allow the pause input guard of the newly activated scene to expire.
            yield return KeyTap(Key.Escape);
            yield return Until(() => GameSession.Instance.IsPaused, "Pause opens in the campaign");
            var pause = FindAnyObjectByType<PauseMenuController>();
            yield return Submit(pause.OptionsButton);
            yield return Submit(pause.AudioButton);
            yield return Until(() => pause.AudioPage && pause.AudioPage.IsOpen, "Audio page is also available from pause");
            Check(SameVolumes(beforeContext, _audio.Preferences), "Entering pause preserves user volumes");
            CheckMixerVolumes();
            Capture("audio-options-pause.png");
            yield return Submit(pause.AudioPage.TestUiButton);
            Check(GameSession.Instance.IsPaused, "UI test sound leaves gameplay paused");
            yield return KeyTap(Key.Escape);
            yield return KeyTap(Key.Escape);
            yield return Submit(pause.MainMenuButton);
            yield return Until(() => (_menu = FindAnyObjectByType<MainMenuController>()) && !GameSession.Instance.IsLoading,
                "Main menu reloads without losing audio settings");
            yield return KeyTap(Key.Enter);
            yield return Until(() => _menu.ReadyForInput, "Returned menu is ready");
            Check(FindObjectsByType<GameAudio>().Length == 1, "Only one persistent audio service exists");
            Check(FindObjectsByType<AudioListener>().Count(l => l.enabled) == 1, "Only one active listener exists");
            yield return QuitThroughUI(_menu.QuitButton);
        }

        IEnumerator RestartRun()
        {
            var expected = JsonUtility.FromJson<AudioPreferences>(File.ReadAllText(Path.Combine(_output, "audio-expected.json")));
            Check(SameVolumes(expected, _audio.Preferences), "All volume preferences survive application restart");
            Check(expected.Mono == _audio.Preferences.Mono && expected.OutputDeviceId == _audio.Preferences.OutputDeviceId,
                "Mono and device preferences survive application restart");
            Check(Mathf.Abs(_page.MasterSlider.value - expected.Master) < .0001f && _page.SpeakerModeDropdown.value == 1,
                "Restarted Audio UI displays the saved values");
            CheckMixerVolumes();
            Capture("audio-options-restored.png");
            yield return KeyTap(Key.Escape);
            yield return KeyTap(Key.Escape);
            yield return QuitThroughUI(_menu.QuitButton);
        }

        void CheckMixerVolumes()
        {
            var names = new[] { "MasterVolume", "MusicVolume", "SfxVolume", "UiVolume" };
            var channels = new[] { AudioChannel.Master, AudioChannel.Music, AudioChannel.Sfx, AudioChannel.Ui };
            for (int i = 0; i < names.Length; i++)
                Check(_audio.Mixer.GetFloat(names[i], out float actual) &&
                    Mathf.Abs(actual - GameAudio.LinearToDecibels(_audio.Preferences.GetVolume(channels[i]))) < .01f,
                    "Mixer applies the saved " + names[i] + " level");
        }
        static bool SameVolumes(AudioPreferences a, AudioPreferences b) =>
            Mathf.Abs(a.Master - b.Master) < .0001f && Mathf.Abs(a.Music - b.Music) < .0001f &&
            Mathf.Abs(a.Sfx - b.Sfx) < .0001f && Mathf.Abs(a.Ui - b.Ui) < .0001f;
        IEnumerator Submit(Selectable control) { Focus(control); yield return Frames(2); yield return KeyTap(Key.Enter); }
        void Focus(Selectable control)
        {
            Check(control && control.gameObject.activeInHierarchy && control.interactable && EventSystem.current, "UI control is available");
            if (EventSystem.current && control) EventSystem.current.SetSelectedGameObject(control.gameObject);
        }
        IEnumerator QuitThroughUI(Button button)
        {
            Focus(button); yield return Frames(2);
            Check(_audio.SavePreferences(), "Preferences flush before application exit");
            _quitting = true; _deadline = Time.realtimeSinceStartup + 5;
            WriteResult(); Keys(Key.Enter);
            while (true) yield return null;
        }
        IEnumerator KeyTap(Key key) { Keys(key); yield return Frames(2); Keys(); yield return Frames(3); }
        IEnumerator PadTap(GamepadButton button) { InputSystem.QueueStateEvent(_pad, new GamepadState().WithButton(button)); yield return Frames(2); InputSystem.QueueStateEvent(_pad, new GamepadState()); yield return Frames(3); }
        void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        static IEnumerator Frames(int count) { for (int i = 0; i < count; i++) yield return null; }
        static IEnumerator Seconds(float seconds) { float end = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < end) yield return null; }
        IEnumerator Until(Func<bool> predicate, string description) { float end = Time.realtimeSinceStartup + 15; while (!predicate() && Time.realtimeSinceStartup < end) yield return null; Check(predicate(), description); }
        void Check(bool success, string description) { Trace((success ? "PASS " : "FAIL ") + description); if (!success) Fail(); }
        void Trace(string text) => File.AppendAllText(TracePath, text + "\n");
        void Update() { if (!_stopped && !string.IsNullOrEmpty(_output) && Time.realtimeSinceStartup > _deadline) { Trace("FAIL Verification or real UI exit timed out"); Fail(); } }
        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            File.AppendAllText(Path.Combine(_output, "audio-phase" + _phase + "-errors.txt"), message + "\n" + stack + "\n"); Fail();
        }
        void Fail() { if (_stopped) return; _failed = _stopped = true; WriteResult(); StopAllCoroutines(); Application.Quit(2); }
        void WriteResult() => File.WriteAllText(Path.Combine(_output, "audio-phase" + _phase + "-result.txt"),
            (_failed ? "FAIL" : "PASS") + "\nStage 3 phase " + _phase + ": audio UI, mixer levels, final software samples, mono, routing and preferences across restart.\n");

        sealed class CanvasState { public Canvas Canvas; public RenderMode Mode; public Camera Camera; public float Distance; }
        void Capture(string filename)
        {
            var camera = Camera.main;
            if (!camera) camera = FindAnyObjectByType<SandboxRunner>()?.ArenaCamera;
            if (!camera) { Check(false, "Camera exists for capture"); return; }
            var states = new List<CanvasState>();
            var target = RenderTexture.GetTemporary(1440, 900, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                foreach (var canvas in FindObjectsByType<Canvas>())
                {
                    if (!canvas.isActiveAndEnabled || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    states.Add(new CanvasState { Canvas = canvas, Mode = canvas.renderMode, Camera = canvas.worldCamera, Distance = canvas.planeDistance });
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
                foreach (var state in states) if (state.Canvas) { state.Canvas.renderMode = state.Mode; state.Canvas.worldCamera = state.Camera; state.Canvas.planeDistance = state.Distance; }
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
