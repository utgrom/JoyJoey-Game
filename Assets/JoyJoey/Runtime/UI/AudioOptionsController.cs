using System;
using System.Collections.Generic;
using JoyJoey.Game.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace JoyJoey.Game.UI
{
    /// <summary>Reusable audio page shared by the main menu and pause, with live saved values.</summary>
    public sealed class AudioOptionsController : MonoBehaviour
    {
        public Slider MasterSlider, MusicSlider, SfxSlider, UiSlider;
        public Text MasterValue, MusicValue, SfxValue, UiValue, StatusText;
        public Dropdown SpeakerModeDropdown, OutputDeviceDropdown;
        public Button TestMusicButton, TestSfxButton, TestUiButton, RefreshDevicesButton, DefaultsButton, BackButton;
        public bool IsOpen => gameObject.activeInHierarchy;
        public bool HasOpenDropdown => Expanded(SpeakerModeDropdown) || Expanded(OutputDeviceDropdown);
        GameAudio _audio;
        Action _onBack;
        AudioOutputDevice[] _devices = Array.Empty<AudioOutputDevice>();
        Text _musicLabel;

        void Awake()
        {
            _musicLabel = TestMusicButton.GetComponentInChildren<Text>(true);
            MasterSlider.onValueChanged.AddListener(value => _audio.SetVolume(AudioChannel.Master, value));
            MusicSlider.onValueChanged.AddListener(value => _audio.SetVolume(AudioChannel.Music, value));
            SfxSlider.onValueChanged.AddListener(value => _audio.SetVolume(AudioChannel.Sfx, value));
            UiSlider.onValueChanged.AddListener(value => _audio.SetVolume(AudioChannel.Ui, value));
            SpeakerModeDropdown.onValueChanged.AddListener(value => _audio.SetMono(value == 1));
            OutputDeviceDropdown.onValueChanged.AddListener(ChooseOutput);
            TestMusicButton.onClick.AddListener(() => _audio.ToggleTestMusic());
            TestSfxButton.onClick.AddListener(() => _audio.TestSfx());
            TestUiButton.onClick.AddListener(() => _audio.TestUi());
            RefreshDevicesButton.onClick.AddListener(RefreshDevices);
            DefaultsButton.onClick.AddListener(() => _audio.ResetVolumes());
            BackButton.onClick.AddListener(Close);
            ConfigureNavigation();
        }

        void OnEnable()
        {
            _audio = GameAudio.EnsureExists();
            _audio.StateChanged += Synchronize;
            RefreshDevices();
        }
        void OnDisable()
        {
            if (_audio) _audio.StateChanged -= Synchronize;
        }
        void Update()
        {
            if (_audio && _musicLabel)
            {
                string text = _audio.IsTestMusicPlaying ? "Detener música" : "Probar música";
                if (_musicLabel.text != text) _musicLabel.text = text;
            }
            var system = EventSystem.current;
            if (!MenuInputCoordinator.PointerMode && system && (!system.currentSelectedGameObject ||
                !system.currentSelectedGameObject.activeInHierarchy || !system.currentSelectedGameObject.transform.IsChildOf(transform)))
                system.SetSelectedGameObject(MasterSlider.gameObject);
        }

        public void Open(Action onBack)
        {
            _onBack = onBack;
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            UiAudioFeedback.EnsureForCanvas(gameObject);
            MenuInputCoordinator.Ensure(EventSystem.current)?.SetFocusScope(transform, MasterSlider);
            Synchronize();
            if (!MenuInputCoordinator.PointerMode && EventSystem.current) EventSystem.current.SetSelectedGameObject(MasterSlider.gameObject);
        }
        public void Close()
        {
            if (!gameObject.activeSelf) return;
            SpeakerModeDropdown.Hide();
            OutputDeviceDropdown.Hide();
            gameObject.SetActive(false);
            var callback = _onBack;
            _onBack = null;
            callback?.Invoke();
        }
        public void HandleBack()
        {
            UiAudioFeedback.PlayConfirm();
            if (Expanded(SpeakerModeDropdown)) { SpeakerModeDropdown.Hide(); return; }
            if (Expanded(OutputDeviceDropdown)) { OutputDeviceDropdown.Hide(); return; }
            Close();
        }
        static bool Expanded(Dropdown dropdown)
        {
            // UGUI creates this child in Show(); it has no public expanded-state property.
            var list = dropdown.transform.Find("Dropdown List");
            return list && list.gameObject.activeInHierarchy;
        }

        public void RefreshDevices()
        {
            if (!_audio) return;
            _audio.RefreshOutputDevices();
            Synchronize();
        }

        void Synchronize()
        {
            if (!_audio) return;
            var preferences = _audio.Preferences;
            MasterSlider.SetValueWithoutNotify(preferences.Master);
            MusicSlider.SetValueWithoutNotify(preferences.Music);
            SfxSlider.SetValueWithoutNotify(preferences.Sfx);
            UiSlider.SetValueWithoutNotify(preferences.Ui);
            MasterValue.text = Percent(preferences.Master);
            MusicValue.text = Percent(preferences.Music);
            SfxValue.text = Percent(preferences.Sfx);
            UiValue.text = Percent(preferences.Ui);
            SpeakerModeDropdown.SetValueWithoutNotify(preferences.Mono ? 1 : 0);
            SpeakerModeDropdown.RefreshShownValue();
            var devices = _audio.GetOutputDevices();
            if (!SameDevices(devices, _devices))
            {
                if (Expanded(OutputDeviceDropdown)) OutputDeviceDropdown.Hide();
                _devices = devices ?? Array.Empty<AudioOutputDevice>();
                var names = new List<string>();
                foreach (var device in _devices) names.Add(device.Name);
                if (names.Count == 0) names.Add("Predeterminado del sistema");
                OutputDeviceDropdown.ClearOptions();
                OutputDeviceDropdown.AddOptions(names);
            }
            int index = 0;
            for (int i = 0; i < _devices.Length; i++) if (_devices[i].Id == preferences.OutputDeviceId) { index = i; break; }
            OutputDeviceDropdown.SetValueWithoutNotify(index);
            OutputDeviceDropdown.RefreshShownValue();
            StatusText.text = _audio.LastError;
        }
        static bool SameDevices(AudioOutputDevice[] a, AudioOutputDevice[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i].Id != b[i].Id || a[i].Name != b[i].Name) return false;
            return true;
        }
        void ChooseOutput(int index)
        {
            if (_audio && index >= 0 && index < _devices.Length)
            {
                _audio.TrySetOutputDevice(_devices[index].Id);
                Synchronize();
            }
        }
        static string Percent(float value) => Mathf.RoundToInt(value * 100) + "%";
        void ConfigureNavigation()
        {
            var controls = new Selectable[] { MasterSlider, MusicSlider, SfxSlider, UiSlider, SpeakerModeDropdown,
                OutputDeviceDropdown, RefreshDevicesButton, TestMusicButton, TestSfxButton, TestUiButton, DefaultsButton };
            for (int i = 0; i < controls.Length; i++)
            {
                var nav = controls[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = controls[(i + controls.Length - 1) % controls.Length];
                nav.selectOnDown = controls[(i + 1) % controls.Length];
                if (nav.selectOnLeft == BackButton) nav.selectOnLeft = DefaultsButton;
                if (nav.selectOnRight == BackButton) nav.selectOnRight = DefaultsButton;
                controls[i].navigation = nav;
            }
        }
    }
}
