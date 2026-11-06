using System;
using System.Collections.Generic;
using JoyJoey.Game.Audio;
using System.Globalization;
using JoyJoey.Game.Display;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    /// <summary>Edits a draft and asks the display service to preview before committing it.</summary>
    public sealed class DisplayOptionsController : MonoBehaviour
    {
        public RectTransform Content;
        public GameObject BasicPanel, AdvancedPanel, ConfirmPanel;
        public Dropdown ModeDropdown, ResolutionDropdown, FpsDropdown, VSyncDropdown, RenderScaleDropdown;
        public Slider BrightnessSlider, ContrastSlider, GammaSlider;
        public Text BrightnessValue, ContrastValue, GammaValue, ResolutionInfo, RenderInfo, VSyncInfo, StatusText, ConfirmationText;
        public Button BasicButton, AdvancedButton, ApplyButton, RevertButton, BackButton, KeepButton, CancelPreviewButton, ResetImageButton;
        public RawImage CalibrationPattern;
        public bool IsOpen => gameObject.activeInHierarchy;
        public bool HasOpenDropdown
        {
            get { foreach (var dropdown in Dropdowns()) if (Expanded(dropdown)) return true; return false; }
        }
        public bool IsAdvanced { get; private set; }
        public DisplayPreferences Draft => _draft;
        GameDisplay _display;
        DisplayPreferences _draft;
        DisplayResolution[] _resolutions = Array.Empty<DisplayResolution>();
        static readonly int[] FpsCaps = { 0, 30, 60, 90, 120, 144, 165, 240 };
        static readonly string[] FpsLabels = { "Sin límite", "30", "60", "90", "120", "144", "165", "240" };
        static readonly float[] Scales = { 1, .75f, .67f, .5f };
        Action _onBack;
        bool _synchronizing, _wasPreviewing;
        Texture2D _pattern;
        float _nextMonitorRefresh;

        void Awake()
        {
            FitContent();
            BasicButton.onClick.AddListener(() => ShowAdvanced(false));
            AdvancedButton.onClick.AddListener(() => ShowAdvanced(true));
            ApplyButton.onClick.AddListener(Apply);
            RevertButton.onClick.AddListener(Discard);
            BackButton.onClick.AddListener(Close);
            KeepButton.onClick.AddListener(Keep);
            CancelPreviewButton.onClick.AddListener(RevertPreview);
            ResetImageButton.onClick.AddListener(ResetImage);
            ModeDropdown.onValueChanged.AddListener(value => Edit(() => _draft.WindowMode = value == 0 ? DisplayWindowMode.Windowed : DisplayWindowMode.Borderless));
            ResolutionDropdown.onValueChanged.AddListener(value => Edit(() =>
            {
                if (value < 0 || value >= _resolutions.Length) return;
                _draft.WindowWidth = _resolutions[value].Width;
                _draft.WindowHeight = _resolutions[value].Height;
            }));
            FpsDropdown.onValueChanged.AddListener(value =>
            {
                if (_draft == null || _draft.VSync) return;
                Edit(() => _draft.FpsCap = FpsCaps[Mathf.Clamp(value, 0, FpsCaps.Length - 1)]);
            });
            VSyncDropdown.onValueChanged.AddListener(value => Edit(() => _draft.VSync = value == 1));
            RenderScaleDropdown.onValueChanged.AddListener(value => Edit(() => _draft.RenderScale = Scales[Mathf.Clamp(value, 0, Scales.Length - 1)]));
            BrightnessSlider.onValueChanged.AddListener(value => Edit(() => _draft.Brightness = value));
            ContrastSlider.onValueChanged.AddListener(value => Edit(() => _draft.Contrast = value));
            GammaSlider.onValueChanged.AddListener(value => Edit(() => _draft.Gamma = value));
        }

        void OnEnable()
        {
            FitContent();
            _display = GameDisplay.EnsureExists();
            if (!_display) return;
            _display.StateChanged += DisplayChanged;
        }
        void OnDisable()
        {
            if (_display) _display.StateChanged -= DisplayChanged;
        }
        void OnRectTransformDimensionsChange() => FitContent();
        void FitContent()
        {
            if (!Application.isPlaying || !Content || !(transform is RectTransform rect)) return;
            Content.localScale = Vector3.one * CalculateContentScale(rect.rect.width, rect.rect.height);
        }
        public static float CalculateContentScale(float width, float height)
            => Mathf.Clamp01(Mathf.Min(width / 1440f, height / 900f));
        public void Open(Action onBack)
        {
            _onBack = onBack;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            UiAudioFeedback.EnsureForCanvas(gameObject);
            MenuInputCoordinator.Ensure(EventSystem.current)?.SetFocusScope(transform, ModeDropdown);
            if (!_display) return;
            _draft = (_display.IsPreviewing ? _display.Current : _display.Preferences).Copy();
            BuildChoices();
            ShowAdvanced(false);
            _wasPreviewing = false;
            DisplayChanged();
            if (!_display.IsPreviewing) Select(ModeDropdown);
        }
        public void Close()
        {
            if (!gameObject.activeSelf) return;
            HideDropdowns();
            if (_display && _display.IsPreviewing) _display.RevertPreview();
            gameObject.SetActive(false);
            var callback = _onBack;
            _onBack = null;
            callback?.Invoke();
        }
        public void HandleBack()
        {
            UiAudioFeedback.PlayConfirm();
            foreach (var dropdown in Dropdowns())
                if (Expanded(dropdown)) { dropdown.Hide(); return; }
            if (_display && _display.IsPreviewing) { RevertPreview(); return; }
            Close();
        }
        static bool Expanded(Dropdown dropdown)
        {
            var list = dropdown ? dropdown.transform.Find("Dropdown List") : null;
            return list && list.gameObject.activeInHierarchy;
        }
        IEnumerable<Dropdown> Dropdowns()
        {
            yield return ModeDropdown; yield return ResolutionDropdown; yield return FpsDropdown;
            yield return VSyncDropdown; yield return RenderScaleDropdown;
        }
        void HideDropdowns() { foreach (var dropdown in Dropdowns()) if (dropdown) dropdown.Hide(); }
        void BuildChoices()
        {
            _resolutions = _display.GetWindowResolutions();
            var resolutions = new List<string>();
            foreach (var value in _resolutions) resolutions.Add(value.Width + " × " + value.Height);
            ResolutionDropdown.ClearOptions(); ResolutionDropdown.AddOptions(resolutions);
        }
        void Edit(Action edit)
        {
            if (_synchronizing || _draft == null || !_display || _display.IsPreviewing) return;
            edit();
            Synchronize();
        }
        void Synchronize()
        {
            if (_draft == null || !_display) return;
            _synchronizing = true;
            ModeDropdown.SetValueWithoutNotify(_draft.WindowMode == DisplayWindowMode.Windowed ? 0 : 1);
            int resolutionIndex = 0;
            for (int i = 0; i < _resolutions.Length; i++)
                if (_resolutions[i].Width == _draft.WindowWidth && _resolutions[i].Height == _draft.WindowHeight) { resolutionIndex = i; break; }
            ResolutionDropdown.SetValueWithoutNotify(resolutionIndex);
            double monitorRate = _display.MonitorRefreshRate;
            ConfigureFpsPresentation(FpsDropdown, _draft, monitorRate);
            _nextMonitorRefresh = Time.unscaledTime + .5f;
            VSyncDropdown.SetValueWithoutNotify(_draft.VSync ? 1 : 0);
            int scale = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < Scales.Length; i++)
                if (Mathf.Abs(Scales[i] - _draft.RenderScale) < nearest) { nearest = Mathf.Abs(Scales[i] - _draft.RenderScale); scale = i; }
            RenderScaleDropdown.SetValueWithoutNotify(scale);
            BrightnessSlider.SetValueWithoutNotify(_draft.Brightness);
            ContrastSlider.SetValueWithoutNotify(_draft.Contrast);
            GammaSlider.SetValueWithoutNotify(_draft.Gamma);
            BrightnessValue.text = _draft.Brightness.ToString("+0.00;-0.00;0.00");
            ContrastValue.text = _draft.Contrast.ToString("0.00");
            GammaValue.text = _draft.Gamma.ToString("0.00");
            bool borderless = _draft.WindowMode == DisplayWindowMode.Borderless;
            bool editable = !_display.IsPreviewing;
            ModeDropdown.interactable = VSyncDropdown.interactable = RenderScaleDropdown.interactable = editable;
            BrightnessSlider.interactable = ContrastSlider.interactable = GammaSlider.interactable = editable;
            BasicButton.interactable = AdvancedButton.interactable = ApplyButton.interactable = RevertButton.interactable = BackButton.interactable = ResetImageButton.interactable = editable;
            ResolutionDropdown.interactable = !borderless && editable;
            FpsDropdown.interactable = !_draft.VSync && editable;
            ResolutionInfo.text = borderless ? "Sin bordes: " + _display.DesktopWidth + " × " + _display.DesktopHeight + " del escritorio." : "Tamaño de la ventana del juego.";
            VSyncInfo.text = _draft.VSync ? VSyncExplanation(monitorRate) : "El límite de FPS afecta al dibujo; la simulación conserva su ritmo.";
            int width = borderless ? _display.DesktopWidth : _draft.WindowWidth;
            int height = borderless ? _display.DesktopHeight : _draft.WindowHeight;
            RenderInfo.text = "Mundo: " + Mathf.Max(1, (int)(width * _draft.RenderScale)) + " × " + Mathf.Max(1, (int)(height * _draft.RenderScale)) +
                "    ·    Ventana e interfaz: " + width + " × " + height;
            foreach (var dropdown in Dropdowns()) dropdown.RefreshShownValue();
            StatusText.text = _display.LastError;
            _synchronizing = false;
            ConfigureNavigation();
            UpdatePattern();
        }
        void DisplayChanged()
        {
            if (!_display) return;
            bool previewing = _display.IsPreviewing;
            if (_wasPreviewing && !previewing) _draft = _display.Preferences.Copy();
            if (ConfirmPanel) ConfirmPanel.SetActive(previewing);
            bool changed = previewing != _wasPreviewing;
            if (previewing && changed) HideDropdowns();
            _wasPreviewing = previewing;
            Synchronize();
            if (changed) Select(previewing ? (Selectable)KeepButton : IsAdvanced ? RenderScaleDropdown : ModeDropdown);
        }
        void Update()
        {
            if (!_display) return;
            if (_draft != null && _draft.VSync && Time.unscaledTime >= _nextMonitorRefresh)
            {
                double monitorRate = _display.MonitorRefreshRate;
                ConfigureFpsPresentation(FpsDropdown, _draft, monitorRate);
                FpsDropdown.interactable = false;
                VSyncInfo.text = VSyncExplanation(monitorRate);
                _nextMonitorRefresh = Time.unscaledTime + .5f;
            }
            if (_display.IsPreviewing)
                ConfirmationText.text = "¿Conservar estos ajustes?\nSe restaurarán los anteriores en " + Mathf.CeilToInt(_display.RemainingSeconds) + " s." +
                    (string.IsNullOrEmpty(_display.LastError) ? "" : "\n" + _display.LastError);
            var system = EventSystem.current;
            if (!MenuInputCoordinator.PointerMode && system && _display.IsPreviewing && system.currentSelectedGameObject &&
                !system.currentSelectedGameObject.transform.IsChildOf(ConfirmPanel.transform))
                Select(KeepButton);
            if (!MenuInputCoordinator.PointerMode && system && (!system.currentSelectedGameObject || !system.currentSelectedGameObject.activeInHierarchy ||
                !system.currentSelectedGameObject.transform.IsChildOf(transform) ||
                system.currentSelectedGameObject.GetComponent<Selectable>() is Selectable selectable && !selectable.interactable))
                Select(_display.IsPreviewing ? (Selectable)KeepButton : IsAdvanced ? RenderScaleDropdown : ModeDropdown);
        }
        public void ShowAdvanced(bool advanced)
        {
            if (_display && _display.IsPreviewing) return;
            IsAdvanced = advanced;
            BasicPanel.SetActive(!advanced);
            AdvancedPanel.SetActive(advanced);
            ConfigureNavigation();
            Select(advanced ? RenderScaleDropdown : ModeDropdown);
        }
        void ConfigureNavigation()
        {
            var controls = new List<Selectable>();
            if (IsAdvanced)
                controls.AddRange(new Selectable[] { RenderScaleDropdown, BrightnessSlider, ContrastSlider, GammaSlider, ResetImageButton });
            else
            {
                controls.Add(ModeDropdown);
                if (ResolutionDropdown.interactable) controls.Add(ResolutionDropdown);
                if (FpsDropdown.interactable) controls.Add(FpsDropdown);
                controls.Add(VSyncDropdown);
            }
            controls.AddRange(new Selectable[] { RevertButton, BasicButton, AdvancedButton });
            for (int i = 0; i < controls.Count; i++)
            {
                var nav = controls[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = controls[(i + controls.Count - 1) % controls.Count];
                nav.selectOnDown = controls[(i + 1) % controls.Count];
                if (nav.selectOnLeft == ApplyButton || nav.selectOnLeft == BackButton) nav.selectOnLeft = null;
                if (nav.selectOnRight == ApplyButton || nav.selectOnRight == BackButton) nav.selectOnRight = null;
                controls[i].navigation = nav;
            }
            var basic = BasicButton.navigation;
            basic.selectOnLeft = basic.selectOnRight = AdvancedButton;
            BasicButton.navigation = basic;
            var advanced = AdvancedButton.navigation;
            advanced.selectOnLeft = advanced.selectOnRight = BasicButton;
            AdvancedButton.navigation = advanced;
        }

        public static string FormatVSyncRate(double refreshRate, IFormatProvider format = null)
        {
            if (double.IsNaN(refreshRate) || double.IsInfinity(refreshRate) || refreshRate <= 0)
                return "Controlado por VSync";
            return refreshRate.ToString("0.##", format ?? CultureInfo.CurrentCulture) + " FPS (VSync)";
        }
        static string VSyncExplanation(double refreshRate)
        {
            if (double.IsNaN(refreshRate) || double.IsInfinity(refreshRate) || refreshRate <= 0)
                return "VSync sigue la frecuencia del monitor; no se pudo consultar su valor.";
            return "Monitor: " + refreshRate.ToString("0.##", CultureInfo.CurrentCulture) +
                " Hz. VSync usa esa frecuencia como referencia; los FPS reales pueden ser menores.";
        }
        /// <summary>The VSync caption is presentation only; the user's manual cap remains untouched.</summary>
        public static void ConfigureFpsPresentation(Dropdown dropdown, DisplayPreferences preferences, double monitorRate)
        {
            if (!dropdown || preferences == null) return;
            if (preferences.VSync)
            {
                string label = FormatVSyncRate(monitorRate);
                if (dropdown.options.Count != 1 || dropdown.options[0].text != label)
                {
                    dropdown.ClearOptions();
                    dropdown.AddOptions(new List<string> { label });
                }
                dropdown.SetValueWithoutNotify(0);
                dropdown.interactable = false;
            }
            else
            {
                bool matches = dropdown.options.Count == FpsLabels.Length;
                for (int i = 0; matches && i < FpsLabels.Length; i++) matches = dropdown.options[i].text == FpsLabels[i];
                if (!matches)
                {
                    dropdown.ClearOptions();
                    dropdown.AddOptions(new List<string>(FpsLabels));
                }
                dropdown.SetValueWithoutNotify(Mathf.Max(0, Array.IndexOf(FpsCaps, preferences.FpsCap)));
                dropdown.interactable = true;
            }
            dropdown.RefreshShownValue();
        }
        public void Apply()
        {
            if (_display && _draft != null) { UiAudioFeedback.PlayConfirm(); _display.TryPreview(_draft.Copy()); DisplayChanged(); }
        }
        public void Keep() { if (_display) { _display.ConfirmPreview(); DisplayChanged(); } }
        public void RevertPreview() { if (_display) { _display.RevertPreview(); DisplayChanged(); } }
        public void Discard()
        {
            if (!_display) return;
            if (_display.IsPreviewing) _display.RevertPreview();
            _draft = _display.Preferences.Copy();
            Synchronize();
        }
        public void ResetImage()
        {
            if (_draft == null || _display && _display.IsPreviewing) return;
            _draft.Brightness = 0; _draft.Contrast = 1; _draft.Gamma = 1;
            Synchronize();
        }
        void UpdatePattern()
        {
            if (!CalibrationPattern || _draft == null) return;
            if (!_pattern)
            {
                _pattern = new Texture2D(256, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "Calibración de pantalla" };
                CalibrationPattern.texture = _pattern;
            }
            var pixels = new Color[256 * 32];
            for (int x = 0; x < 256; x++)
            {
                float input = Mathf.Floor(x / 16f) / 15f;
                float value = CalibrationMath.ApplyDisplay(input, _draft.Brightness, _draft.Contrast, _draft.Gamma);
                var color = new Color(value, value, value, 1);
                for (int y = 0; y < 32; y++) pixels[y * 256 + x] = color;
            }
            _pattern.SetPixels(pixels); _pattern.Apply(false);
        }
        static void Select(Selectable selectable)
        {
            if (!MenuInputCoordinator.PointerMode && EventSystem.current && selectable && selectable.gameObject.activeInHierarchy && selectable.interactable)
                EventSystem.current.SetSelectedGameObject(selectable.gameObject);
        }
        void OnDestroy() { if (_pattern) Destroy(_pattern); }
    }
}
