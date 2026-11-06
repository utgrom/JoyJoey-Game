using System;
using System.Collections;
using System.IO;
using JoyJoey.Sandbox;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace JoyJoey.Game
{
    /// <summary>Opt-in native player verification; never adds devices in ordinary play.</summary>
    public sealed class WorldSmoke : MonoBehaviour
    {
        LevelRunner _runner;
        Keyboard _keyboard;
        Gamepad _gamepad;
        string _output;
        bool _failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-worldSmoke") < 0) return;
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var instance = new GameObject("Stage 1 smoke verification");
            DontDestroyOnLoad(instance);
            instance.AddComponent<WorldSmoke>();
        }

        IEnumerator Start()
        {
            _output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Validation/Stage1"));
            string[] args = Environment.GetCommandLineArgs();
            int arg = Array.IndexOf(args, "-worldSmokeOutput");
            if (arg >= 0 && arg + 1 < args.Length) _output = args[arg + 1];
            Directory.CreateDirectory(_output);
            File.WriteAllText(Path.Combine(_output, "trace.txt"), "");
            File.WriteAllText(Path.Combine(_output, "errors.txt"), "");
            Application.logMessageReceived += OnLog;
            yield return Until(() => (_runner = FindAnyObjectByType<LevelRunner>()) && _runner.Simulation != null, "Bootstrap loaded the level");
            if (!_runner || _runner.Simulation == null) { Finish(); yield break; }
            Check(GameBootstrap.Instance != null, "Persistent bootstrap exists");
            _keyboard = InputSystem.AddDevice<Keyboard>("WorldSmokeKeyboard");
            _gamepad = InputSystem.AddDevice<Gamepad>("WorldSmokeGamepad");
            yield return Ticks(4);
            Capture("level-idle.png");

            Vector2 spawn = _runner.Simulation.Player.Position;
            Keys(Key.D);
            yield return Until(() => _runner.Simulation.Player.Position.x > spawn.x + 3, "Keyboard moves Joey");
            Keys(Key.D, Key.Space);
            yield return Until(() => _runner.Simulation.Player.Position.y > spawn.y + .8f, "Keyboard jumps");
            Capture("level-jump.png");
            Keys();
            yield return Until(() => _runner.Simulation.Player.Grounded, "Joey lands on serialized floor");
            float dashStart = _runner.Simulation.Player.Position.x;
            Keys(Key.D, Key.LeftShift);
            yield return Until(() => _runner.Simulation.Player.DashTicks > 0, "Keyboard dash starts");
            yield return Until(() => _runner.Simulation.Player.Position.x > dashStart + 1, "Dash moves Joey");
            Keys();
            yield return Ticks(25);

            float padStart = _runner.Simulation.Player.Position.x;
            Pad(new GamepadState { leftStick = Vector2.left });
            yield return Until(() => _runner.Simulation.Player.Position.x < padStart - 3, "Gamepad stick moves Joey");
            Pad(new GamepadState().WithButton(GamepadButton.South));
            yield return Until(() => _runner.Simulation.Player.Position.y > spawn.y + .8f, "Gamepad jump works");
            Pad(new GamepadState());
            yield return Until(() => _runner.Simulation.Player.Grounded, "Gamepad jump lands");
            Keys(Key.D);
            yield return Until(() => _runner.Simulation.Player.Position.x > 22, "Level extends beyond sandbox x=20 bounds");
            Keys();
            yield return Ticks(12);
            Capture("level-right.png");
            Check(_runner.Simulation.Player.Position.x > 21, "No legacy sandbox respawn in the level");
            Check(_runner.Simulation.Dummy.Damage == 0 && _runner.Simulation.ComboHits == 0, "No hidden target in the level");
            _runner.Simulation.Player.Position = new Vector2(23, _runner.RespawnBelowY - 5);
            yield return Until(() => Vector2.Distance(_runner.Simulation.Player.Position, (Vector2)_runner.SpawnPoint.position) < .5f, "Fall returns to the scene spawn point");

            yield return SceneManager.LoadSceneAsync("MenuPrincipal");
            yield return null;
            Capture("menu-base.png");
            Check(FindObjectsByType<Camera>().Length == 1, "Menu base has one camera");
            yield return SceneManager.LoadSceneAsync("CircoOnirico_Sandbox");
            yield return Until(() => FindAnyObjectByType<SandboxRunner>()?.Simulation != null, "Sandbox can still load");
            Capture("sandbox-preserved.png", FindAnyObjectByType<SandboxRunner>().ArenaCamera);
            Check(FindObjectsByType<AudioListener>().Length == 1, "Sandbox has one audio listener");
            yield return SceneManager.LoadSceneAsync("Nivel_Prototipo");
            yield return Until(() => (_runner = FindAnyObjectByType<LevelRunner>()) && _runner.Simulation != null, "Level can reload after sandbox");
            yield return Ticks(3);
            Check(FindObjectsByType<LevelRunner>().Length == 1, "One level runner after scene switches");
            Check(FindObjectsByType<GameBootstrap>().Length == 1, "One bootstrap after scene switches");
            Check(FindObjectsByType<AudioListener>().Length == 1, "One audio listener after scene switches");
            float reloadStart = _runner.Simulation.Player.Position.x;
            Keys(Key.A);
            yield return Until(() => _runner.Simulation.Player.Position.x < reloadStart - 1, "Input works after scene switches");
            Keys();
            Finish();
        }

        void Keys(params Key[] keys) => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        void Pad(GamepadState state) => InputSystem.QueueStateEvent(_gamepad, state);
        IEnumerator Ticks(int count)
        {
            int end = _runner.Simulation.Tick + count;
            yield return Until(() => _runner.Simulation.Tick >= end, "Simulation advanced " + count + " ticks");
        }
        IEnumerator Until(Func<bool> predicate, string description)
        {
            float deadline = Time.realtimeSinceStartup + 15;
            while (!predicate() && Time.realtimeSinceStartup < deadline) yield return null;
            Check(predicate(), description);
        }
        void Check(bool passed, string description)
        {
            if (!passed) _failed = true;
            File.AppendAllText(Path.Combine(_output, "trace.txt"), (passed ? "PASS " : "FAIL ") + description + "\n");
        }
        void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            _failed = true;
            File.AppendAllText(Path.Combine(_output, "errors.txt"), message + "\n" + stack + "\n");
        }
        void Capture(string fileName, Camera camera = null)
        {
            if (!camera) camera = Camera.main;
            if (!camera) { Check(false, "No camera for " + fileName); return; }
            var target = RenderTexture.GetTemporary(1440, 900, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(_output, fileName), pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                if (pixels) Destroy(pixels);
            }
        }
        void Finish()
        {
            Application.logMessageReceived -= OnLog;
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            if (_gamepad != null) InputSystem.RemoveDevice(_gamepad);
            File.WriteAllText(Path.Combine(_output, "result.txt"), (_failed ? "FAIL" : "PASS") + "\nStage 1: bootstrap, serialized level, keyboard/gamepad, jump/dash, wide bounds, spawn and scene changes.\n");
            Application.Quit(_failed ? 1 : 0);
        }
    }
}
