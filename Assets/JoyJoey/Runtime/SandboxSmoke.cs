using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

namespace JoyJoey.Sandbox
{
    /// <summary>Opt-in player smoke test. Never creates devices during ordinary play.</summary>
    public sealed class SandboxSmoke : MonoBehaviour
    {
        SandboxRunner runner;
        Keyboard keyboard;
        string output;
        bool failed;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-sandboxSmoke") >= 0)
            {
                Application.runInBackground = true;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                new GameObject("Sandbox smoke validation").AddComponent<SandboxSmoke>();
            }
        }
        IEnumerator Start()
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Validation"));
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-sandboxSmokeOutput");
            if (index >= 0 && index + 1 < args.Length) output = args[index + 1];
            Directory.CreateDirectory(output);
            Application.logMessageReceived += Log;
            yield return null;
            runner = FindAnyObjectByType<SandboxRunner>();
            if (runner == null || runner.Simulation == null) { Finish(false, "Runner did not initialize"); yield break; }
            File.WriteAllText(Path.Combine(output, "smoke-trace.txt"), "Started player validation\n");
            keyboard = InputSystem.AddDevice<Keyboard>("SandboxSmokeKeyboard");
            yield return WaitTicks(3);
            CaptureStage("stage-idle.png");
            yield return WaitTicks(3);
            Set(Key.D);
            yield return Until(() => runner.Simulation.Player.Position.x >= -1.25f);
            Set(); yield return WaitTicks(2);
            Set(Key.J); yield return Until(() => runner.Simulation.Hitstop > 0);
            Set(); yield return WaitTicks(1);
            Set(Key.J); yield return Until(() => runner.Simulation.ComboHits >= 2);
            Set(); yield return WaitTicks(1);
            Set(Key.S, Key.J); yield return Until(() => runner.Simulation.Player.MoveId == "launcher" && runner.Simulation.Player.MoveHit);
            yield return WaitTicks(8);
            Set(); yield return WaitTicks(1);
            Set(Key.J); yield return Until(() => runner.Simulation.ComboHits >= 4);
            Set(Key.F2); yield return WaitTicks(1); Set();
            CaptureStage("stage-combo.png");
            yield return WaitTicks(4);
            int combo = runner.Simulation.BestCombo;
            float damageBeforeShot=runner.Simulation.Dummy.Damage;
            Set(Key.W,Key.K);yield return Until(()=>runner.Simulation.Player.MoveId=="airShot");
            Set();yield return Until(()=>runner.Simulation.Dummy.Damage>damageBeforeShot);
            bool shot=runner.Simulation.Dummy.Velocity.y>0;
            combo=Mathf.Max(combo,runner.Simulation.BestCombo);
            CaptureStage("stage-shot.png");
            Set(Key.R);yield return WaitTicks(1);Set();yield return WaitTicks(1);
            Set(Key.S,Key.J);yield return Until(()=>runner.Simulation.Player.Position.y>.1f);
            bool whiffFollow=runner.Simulation.Dummy.Damage==0;
            Set();yield return WaitTicks(1);
            Set(Key.F6);yield return Until(()=>runner.HeavyTarget);Set();yield return WaitTicks(1);
            Set(Key.R);yield return WaitTicks(1);Set();yield return WaitTicks(1);
            Set(Key.D);yield return Until(()=>runner.Simulation.Player.Position.x>=-1.25f);
            Set();yield return WaitTicks(2);Set(Key.J);yield return Until(()=>runner.Simulation.Dummy.Damage>0);
            bool armor=runner.Simulation.Dummy.Armor>0 && runner.Simulation.Dummy.Grounded;
            Set();CaptureStage("stage-heavy.png");yield return WaitTicks(3);
            Finish(!failed && combo>=4 && whiffFollow && shot && armor,
                "Keyboard gameplay: jab > jab > launcher hold > air attack. Best continuous combo="+combo+
                "\nLauncher follows on whiff="+whiffFollow+"; aerial shot lifts="+shot+"; F6 heavy armor resists weak hit="+armor);
        }
        void Set(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        IEnumerator WaitTicks(int ticks)
        {
            int end = runner.Simulation.Tick + ticks;
            yield return Until(() => runner.Simulation.Tick >= end);
        }
        IEnumerator Until(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + 4;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            if (!condition()) { failed = true; Debug.LogWarning("Smoke condition timed out at: " + runner.Simulation.LastAction); }
            var sim = runner.Simulation;
            File.AppendAllText(Path.Combine(output, "smoke-trace.txt"), "tick=" + sim.Tick + " action=" + sim.Player.MoveId + " p=" + sim.Player.Position + " v=" + sim.Player.Velocity + " d=" + sim.Dummy.Position + " combo=" + sim.ComboHits + " held=" + sim.IsCharging + "\n");
        }
        void Log(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failed = true; }
        void CaptureStage(string name)
        {
            // A hidden/minimized Windows swapchain may be black even while simulation runs.
            // Render the same camera offscreen to verify scene graphics independently of the desktop.
            var target = RenderTexture.GetTemporary(1440, 900, 24, RenderTextureFormat.ARGB32);
            var request = new RenderPipeline.StandardRequest { destination = target };
            var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                RenderPipeline.SubmitRenderRequest(runner.ArenaCamera, request);
                RenderTexture.active = target;
                pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);pixels.Apply();
                File.WriteAllBytes(Path.Combine(output,name),pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
                if (pixels) Destroy(pixels);
            }
        }
        void Finish(bool success, string detail)
        {
            File.WriteAllText(Path.Combine(output, "player-smoke-result.txt"), (success ? "PASS" : "FAIL") + "\n" + detail);
            Application.logMessageReceived -= Log;
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            Application.Quit(success ? 0 : 1);
        }
    }
}
