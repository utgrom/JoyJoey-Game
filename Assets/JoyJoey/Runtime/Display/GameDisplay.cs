using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace JoyJoey.Game.Display
{
    /// <summary>Display presentation is independent of the fixed gameplay simulation and campaign data.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-230)]
    public sealed class GameDisplay : MonoBehaviour
    {
        [Min(.1f)] public float ConfirmationSeconds = 15;
        public static GameDisplay Instance { get; private set; }
        public DisplayPreferences Preferences { get; private set; }
        public DisplayPreferences Current { get; private set; }
        public DisplayPreferencesStore Store { get; private set; }
        public bool IsPreviewing { get; private set; }
        public float RemainingSeconds => IsPreviewing ? Mathf.Max(0, _previewUntil - Time.realtimeSinceStartup) : 0;
        public string LastError { get; private set; } = "";
        public event Action StateChanged;
        public int DesktopWidth => DesktopResolution().Width;
        public int DesktopHeight => DesktopResolution().Height;
        public double MonitorRefreshRate
        {
            get
            {
                // Use the monitor containing this game's window, including after moving it.
                if (!Application.isEditor)
                {
                    var rate = Screen.mainWindowDisplayInfo.refreshRate;
                    if (rate.denominator > 0 && rate.numerator > 0) return rate.value;
                }
                var fallback = Screen.currentResolution.refreshRateRatio;
                return fallback.denominator > 0 && fallback.numerator > 0 ? fallback.value : 0;
            }
        }
        // Match URP's camera target sizing, which truncates fractional scaled pixels.
        public int RenderWidth => Mathf.Max(1, (int)(Screen.width * (Current?.RenderScale ?? 1)));
        public int RenderHeight => Mathf.Max(1, (int)(Screen.height * (Current?.RenderScale ?? 1)));
        public bool SupportsWindowChanges => !Application.isEditor;
        public UniversalRenderPipelineAsset RuntimePipeline => _runtimePipeline;
        float _previewUntil;
        int _originalVSync, _originalFrameRate;
        UniversalRenderPipelineAsset _runtimePipeline;
        RenderPipelineAsset _originalQuality, _originalDefault;
        bool _usesQualityOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearStaticState() => Instance = null;

        public static GameDisplay EnsureExists()
        {
            if (Instance) return Instance;
            if (!Application.isPlaying) return null;
            var existing = FindAnyObjectByType<GameDisplay>();
            if (existing) return existing;
            var prefab = Resources.Load<GameObject>("Game/DisplayRig");
            return prefab ? Instantiate(prefab).GetComponent<GameDisplay>() :
                new GameObject("Joy Joey · Pantalla").AddComponent<GameDisplay>();
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _originalVSync = QualitySettings.vSyncCount;
            _originalFrameRate = Application.targetFrameRate;
            string directory = Application.persistentDataPath;
            string[] args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "-joySettingsDirectory");
            if (option >= 0 && option + 1 < args.Length) directory = args[option + 1];
            Store = new DisplayPreferencesStore(directory);
            Store.TryLoad(out var loaded, out string error);
            if (Store.RecoverPending(out var recovered, out string recovery)) { loaded = recovered; error = recovery; }
            Preferences = NormalizeForDisplay(loaded, DesktopWidth, DesktopHeight);
            Current = Preferences.Copy();
            LastError = error;
        }

        void Start() => ApplyCurrent();
        void Update()
        {
            if (IsPreviewing && RemainingSeconds <= 0)
                Revert("Se recuperó la configuración anterior al vencer el tiempo.");
        }

        public bool TryPreview(DisplayPreferences candidate)
        {
            if (!DisplayPreferences.IsValid(candidate)) { LastError = "La configuración de pantalla no es válida."; Notify(); return false; }
            if (IsPreviewing) RevertPreview();
            if (!Store.BeginPreview(Preferences, out string error)) { LastError = error; Notify(); return false; }
            Current = NormalizeForDisplay(candidate, DesktopWidth, DesktopHeight);
            LastError = "";
            IsPreviewing = true;
            _previewUntil = Time.realtimeSinceStartup + Mathf.Max(.1f, ConfirmationSeconds);
            ApplyCurrent();
            Notify();
            return true;
        }
        public bool ConfirmPreview()
        {
            if (!IsPreviewing) return false;
            if (!Store.TrySave(Current, out string error)) { Revert(error); return false; }
            Preferences = Current.Copy();
            IsPreviewing = false;
            LastError = Store.ClearPreview(out string cleanup) ? "" : cleanup;
            Notify();
            return true;
        }
        public void RevertPreview() => Revert("");
        void Revert(string reason)
        {
            if (!IsPreviewing) return;
            Current = Preferences.Copy();
            IsPreviewing = false;
            LastError = reason;
            if (!Store.ClearPreview(out string error)) LastError = error;
            ApplyCurrent();
            Notify();
        }
        public bool RestoreDefaults() => TryPreview(DisplayPreferences.Defaults());

        void ApplyCurrent()
        {
            EnsurePipelineClone();
            if (_runtimePipeline) _runtimePipeline.renderScale = Current.RenderScale;
            QualitySettings.vSyncCount = Current.VSync ? 1 : 0;
            Application.targetFrameRate = Current.VSync || Current.FpsCap == 0 ? -1 : Current.FpsCap;
            CalibrationSettings.Set(Current.Brightness, Current.Contrast, Current.Gamma);
            if (!Application.isEditor)
            {
                bool borderless = Current.WindowMode == DisplayWindowMode.Borderless;
                int width = borderless ? DesktopWidth : Current.WindowWidth;
                int height = borderless ? DesktopHeight : Current.WindowHeight;
                var mode = borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
                if (Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode)
                    Screen.SetResolution(width, height, mode);
            }
        }

        void EnsurePipelineClone()
        {
            RenderPipelineAsset active = QualitySettings.renderPipeline ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            if (_runtimePipeline && active == _runtimePipeline) return;
            RestorePipeline();
            _originalQuality = QualitySettings.renderPipeline;
            _originalDefault = GraphicsSettings.defaultRenderPipeline;
            var source = _originalQuality ? _originalQuality : _originalDefault;
            if (!(source is UniversalRenderPipelineAsset universal))
            { LastError = "La resolución interna necesita el renderizador URP del proyecto."; return; }
            _runtimePipeline = Instantiate(universal);
            _runtimePipeline.name = universal.name + " · Runtime display";
            _runtimePipeline.hideFlags = HideFlags.DontSave;
            _usesQualityOverride = _originalQuality;
            if (_usesQualityOverride) QualitySettings.renderPipeline = _runtimePipeline;
            else GraphicsSettings.defaultRenderPipeline = _runtimePipeline;
        }
        void RestorePipeline()
        {
            if (!_runtimePipeline) return;
            if (_usesQualityOverride && QualitySettings.renderPipeline == _runtimePipeline)
                QualitySettings.renderPipeline = _originalQuality;
            else if (!_usesQualityOverride && GraphicsSettings.defaultRenderPipeline == _runtimePipeline)
                GraphicsSettings.defaultRenderPipeline = _originalDefault;
            if (Application.isPlaying) Destroy(_runtimePipeline); else DestroyImmediate(_runtimePipeline);
            _runtimePipeline = null;
        }

        static DisplayResolution DesktopResolution()
        {
            if (!Application.isEditor)
            {
                var display = Screen.mainWindowDisplayInfo;
                if (display.width > 0 && display.height > 0) return new DisplayResolution(display.width, display.height);
            }
            var resolution = Screen.currentResolution;
            return new DisplayResolution(Mathf.Max(640, resolution.width), Mathf.Max(360, resolution.height));
        }

        public static DisplayPreferences NormalizeForDisplay(DisplayPreferences source, int desktopWidth, int desktopHeight)
        {
            var result = DisplayPreferences.IsValid(source) ? source.Copy() : DisplayPreferences.Defaults();
            desktopWidth = Mathf.Max(640, desktopWidth); desktopHeight = Mathf.Max(360, desktopHeight);
            result.WindowWidth = Mathf.Clamp(result.WindowWidth, Mathf.Min(960, desktopWidth), desktopWidth);
            result.WindowHeight = Mathf.Clamp(result.WindowHeight, Mathf.Min(540, desktopHeight), desktopHeight);
            return result;
        }
        public DisplayResolution[] GetWindowResolutions()
        {
            int desktopWidth = DesktopWidth, desktopHeight = DesktopHeight;
            var choices = new List<DisplayResolution>();
            void Add(int width, int height)
            {
                var resolution = new DisplayResolution(width, height);
                if (width > desktopWidth || height > desktopHeight || width < Mathf.Min(960, desktopWidth) ||
                    height < Mathf.Min(540, desktopHeight) || choices.Contains(resolution)) return;
                choices.Add(resolution);
            }
            foreach (var resolution in new[] {
                new DisplayResolution(960, 540), new DisplayResolution(1024, 768), new DisplayResolution(1280, 720),
                new DisplayResolution(1280, 800), new DisplayResolution(1366, 768), new DisplayResolution(1440, 900),
                new DisplayResolution(1600, 900), new DisplayResolution(1920, 1080), new DisplayResolution(1920, 1200),
                new DisplayResolution(2560, 1440), new DisplayResolution(2560, 1600), new DisplayResolution(3440, 1440),
                new DisplayResolution(3840, 2160) }) Add(resolution.Width, resolution.Height);
            foreach (var resolution in Screen.resolutions) Add(resolution.width, resolution.height);
            Add(desktopWidth, desktopHeight);
            Add(Screen.width, Screen.height);
            if (Current != null) Add(Current.WindowWidth, Current.WindowHeight);
            choices.Sort((a, b) => a.Width != b.Width ? a.Width.CompareTo(b.Width) : a.Height.CompareTo(b.Height));
            return choices.ToArray();
        }
        void Notify() => StateChanged?.Invoke();
        void OnDestroy()
        {
            if (Instance != this) return;
            // Pending previews deliberately remain journaled if the process exits before confirmation.
            RestorePipeline();
            QualitySettings.vSyncCount = _originalVSync;
            Application.targetFrameRate = _originalFrameRate;
            CalibrationSettings.Reset();
            Instance = null;
        }
    }
}
