using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace JoyJoey.Game.Audio
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-220)]
    public sealed class GameAudio : MonoBehaviour
    {
        public AudioMixer Mixer;
        public AudioMixerGroup MasterGroup, MusicGroup, SfxGroup, UiGroup;
        public AudioMixerSnapshot NormalSnapshot, PauseSnapshot, CutsceneSnapshot;
        public AudioClip TestMusicClip, TestSfxClip, TestUiClip, UiMoveClip, UiConfirmClip;
        public static GameAudio Instance { get; private set; }
        public AudioPreferences Preferences { get; private set; }
        public AudioPreferencesStore Store { get; private set; }
        public AudioOutputProcessor Processor { get; } = new AudioOutputProcessor();
        public float LastPeak => Processor.LastPeak;
        public int ProcessedBlocks => Processor.BlocksProcessed;
        public bool IsTestMusicPlaying => _music && _music.isPlaying && _music.clip == TestMusicClip;
        public string LastError { get; private set; } = "";
        public event Action StateChanged;
        WindowsAudioOutput _output;
        AudioOutputDevice[] _devices = Array.Empty<AudioOutputDevice>();
        AudioSource _music, _sfx, _ui;
        AudioMixOutput _tap;
        AudioContext _context, _appliedContext;
        bool _started, _snapshotApplied, _dirty;
        bool _musicRequested;
        int _lastMusicSample;
        float _saveAt, _nextUiMove;
        string _lastBackendError;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearStaticState() => Instance = null;
        public static GameAudio EnsureExists()
        {
            if (Instance) return Instance;
            if (!Application.isPlaying) return null;
            var existing = FindAnyObjectByType<GameAudio>();
            if (existing) return existing;
            var prefab = Resources.Load<GameObject>("Game/AudioRig");
            if (!prefab) { Debug.LogError("Joy Joey: falta el prefab AudioRig. Prepará el audio desde el menú de Unity."); return null; }
            return Instantiate(prefab).GetComponent<GameAudio>();
        }
        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            string directory = Application.persistentDataPath;
            var args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "-joySettingsDirectory");
            if (option >= 0 && option + 1 < args.Length) directory = args[option + 1];
            Store = new AudioPreferencesStore(directory);
            Store.TryLoad(out var data, out string error);
            Preferences = data;
            LastError = error;
            _output = new WindowsAudioOutput();
            Processor.Output = _output;
            Processor.Mono = data.Mono;
            _music = Source("Música", MusicGroup);
            _sfx = Source("Efectos", SfxGroup);
            _ui = Source("Interfaz", UiGroup);
            if (Mixer) Mixer.updateMode = AudioMixerUpdateMode.UnscaledTime;
            AudioSettings.OnAudioConfigurationChanged += ConfigurationChanged;
            SceneManager.sceneLoaded += SceneLoaded;
        }
        AudioSource Source(string label, AudioMixerGroup group)
        {
            var source = new GameObject(label).AddComponent<AudioSource>();
            source.transform.SetParent(transform, false);
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.ignoreListenerPause = true;
            source.outputAudioMixerGroup = group;
            return source;
        }
        void Start()
        {
            _started = true;
            ApplyVolumes();
            RefreshOutputDevices();
            if (!string.IsNullOrEmpty(Preferences.OutputDeviceId)) TrySetOutputDevice(Preferences.OutputDeviceId);
            BindListener();
        }
        void SceneLoaded(Scene scene, LoadSceneMode mode) => BindListener();
        void BindListener()
        {
            var listener = FindAnyObjectByType<AudioListener>();
            if (!listener || !listener.enabled) return;
            if (_tap && _tap.gameObject != listener.gameObject) _tap.Processor = null;
            _tap = listener.GetComponent<AudioMixOutput>();
            if (!_tap) _tap = listener.gameObject.AddComponent<AudioMixOutput>();
            _tap.Processor = Processor;
        }
        void Update()
        {
            if (!_tap) BindListener();
            if (_music && _music.isPlaying) _lastMusicSample = _music.timeSamples;
            else if (_musicRequested && _music && !_music.loop) _musicRequested = false;
            _output.Tick();
            if (!string.IsNullOrEmpty(_output.LastError) && _output.LastError != _lastBackendError)
            {
                _lastBackendError = _output.LastError;
                LastError = "La salida seleccionada dejó de estar disponible. Se utiliza la salida predeterminada.";
                Preferences.OutputDeviceId = "";
                Changed();
            }
            var context = GameSession.Instance && GameSession.Instance.IsPaused ? AudioContext.Pause : _context;
            if (!_snapshotApplied || context != _appliedContext)
            {
                _appliedContext = context;
                _snapshotApplied = true;
                var snapshot = context == AudioContext.Pause ? PauseSnapshot : context == AudioContext.Cutscene ? CutsceneSnapshot : NormalSnapshot;
                if (snapshot) snapshot.TransitionTo(.15f);
            }
            if (_dirty && Time.unscaledTime >= _saveAt) SavePreferences();
        }
        public static float LinearToDecibels(float value) => value <= .0001f ? -80 : Mathf.Clamp(20 * Mathf.Log10(Mathf.Clamp01(value)), -80, 0);
        public void SetVolume(AudioChannel channel, float volume)
        {
            Preferences.SetVolume(channel, volume);
            ApplyVolumes();
            LastError = "";
            Changed();
        }
        void ApplyVolumes()
        {
            if (!_started || !Mixer) return;
            Mixer.SetFloat("MasterVolume", LinearToDecibels(Preferences.Master));
            Mixer.SetFloat("MusicVolume", LinearToDecibels(Preferences.Music));
            Mixer.SetFloat("SfxVolume", LinearToDecibels(Preferences.Sfx));
            Mixer.SetFloat("UiVolume", LinearToDecibels(Preferences.Ui));
        }
        public void SetMono(bool mono) { Preferences.Mono = mono; Processor.Mono = mono; Changed(); }
        public AudioOutputDevice[] GetOutputDevices() => _devices;
        public void RefreshOutputDevices()
        {
            _devices = WindowsAudioOutput.GetDevices(out string error);
            if (!string.IsNullOrEmpty(error)) LastError = error;
            StateChanged?.Invoke();
        }
        public bool TrySetOutputDevice(string id)
        {
            if (!_output.TrySelect(id ?? "", AudioSettings.outputSampleRate, out string error))
            {
                LastError = error;
                Preferences.OutputDeviceId = "";
                Changed();
                return false;
            }
            Preferences.OutputDeviceId = id ?? "";
            LastError = "";
            _lastBackendError = _output.LastError;
            Changed();
            return true;
        }
        public void ResetVolumes()
        {
            var defaults = AudioPreferences.Defaults();
            Preferences.Master = defaults.Master; Preferences.Music = defaults.Music;
            Preferences.Sfx = defaults.Sfx; Preferences.Ui = defaults.Ui;
            ApplyVolumes(); Changed();
        }
        void Changed() { _dirty = true; _saveAt = Time.unscaledTime + .35f; StateChanged?.Invoke(); }
        public bool SavePreferences()
        {
            if (Store == null || !_dirty) return true;
            if (!Store.TrySave(Preferences, out string error)) { LastError = error; StateChanged?.Invoke(); _saveAt = Time.unscaledTime + 3; return false; }
            _dirty = false;
            return true;
        }
        public void PlayMusic(AudioClip clip, bool loop = true)
        {
            if (!_music) return;
            _musicRequested = clip;
            _lastMusicSample = 0;
            _music.clip = clip; _music.loop = loop;
            if (clip) _music.Play(); else _music.Stop();
        }
        public void ToggleTestMusic() { if (IsTestMusicPlaying) StopTestMusic(); else PlayMusic(TestMusicClip); }
        public void StopTestMusic() { _musicRequested = false; if (_music) _music.Stop(); }
        public void PlaySfx(AudioClip clip, float volume = 1) { if (_sfx && clip) _sfx.PlayOneShot(clip, Mathf.Clamp01(volume)); }
        public void PlayUi(AudioClip clip, float volume = 1) { if (_ui && clip) _ui.PlayOneShot(clip, Mathf.Clamp01(volume)); }
        public void TestSfx() => PlaySfx(TestSfxClip);
        public void TestUi() => PlayUi(TestUiClip);
        public void PlayUiMove()
        {
            if (Time.unscaledTime < _nextUiMove) return;
            _nextUiMove = Time.unscaledTime + .06f;
            PlayUi(UiMoveClip, .45f);
        }
        public void PlayUiConfirm() => PlayUi(UiConfirmClip, .6f);
        public void SetContext(AudioContext context) { _context = context; _snapshotApplied = false; }
        void ConfigurationChanged(bool deviceChanged)
        {
            ApplyVolumes();
            if (!string.IsNullOrEmpty(Preferences.OutputDeviceId)) TrySetOutputDevice(Preferences.OutputDeviceId);
            if (_musicRequested && _music && _music.clip && !_music.isPlaying)
            {
                _music.timeSamples = Mathf.Clamp(_lastMusicSample, 0, _music.clip.samples - 1);
                _music.Play();
            }
        }
        void OnApplicationFocus(bool focused) { if (!focused) SavePreferences(); }
        void OnApplicationQuit() => SavePreferences();
        void OnDestroy()
        {
            if (Instance != this) return;
            SavePreferences();
            AudioSettings.OnAudioConfigurationChanged -= ConfigurationChanged;
            SceneManager.sceneLoaded -= SceneLoaded;
            if (_tap) _tap.Processor = null;
            Processor.Output = null;
            _output?.Dispose();
            Instance = null;
        }
    }
}
