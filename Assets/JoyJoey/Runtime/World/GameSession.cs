using System;
using System.Collections;
using System.IO;
using System.Linq;
using JoyJoey.Game.UI;
using JoyJoey.Sandbox;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace JoyJoey.Game
{
    /// <summary>Scene flow, campaign persistence and pause ownership shared by every entry scene.</summary>
    [DefaultExecutionOrder(-250), DisallowMultipleComponent]
    public sealed class GameSession : MonoBehaviour
    {
        public const string MainMenuScene = "MenuPrincipal", CampaignScene = "Nivel_Prototipo", SandboxScene = "CircoOnirico_Sandbox";
        public static GameSession Instance { get; private set; }
        public bool HasSavedGame => _saved != null;
        public bool IsPaused { get; private set; }
        public bool IsLoading { get; private set; }
        public bool BlocksGameplay => IsPaused || IsLoading || _waitForRelease;
        public string LastError { get; private set; } = "";
        public event Action StateChanged;
        public SaveStore Store { get; private set; }
        SavedGameData _saved, _campaign, _pendingResume;
        LevelRunner _level;
        bool _campaignRequested, _campaignActive, _waitForRelease, _dirty;
        float _nextSaveTime, _unpausedTimeScale = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearStatics() => Instance = null;

        public static GameSession EnsureExists()
        {
            if (Instance) return Instance;
            var existing = FindAnyObjectByType<GameSession>();
            return existing ? existing : new GameObject("Joy Joey · Sesión").AddComponent<GameSession>();
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            string directory = Application.persistentDataPath;
            string[] args = Environment.GetCommandLineArgs();
            int option = Array.IndexOf(args, "-joySaveDirectory");
            if (option >= 0 && option + 1 < args.Length) directory = args[option + 1];
            Store = new SaveStore(directory);
            Store.TryLoad(out _saved, out string message);
            LastError = message;
            SceneManager.sceneLoaded += SceneLoaded;
            JoyJoey.Game.Audio.GameAudio.EnsureExists();
            JoyJoey.Game.Display.GameDisplay.EnsureExists();
            var prefab = Resources.Load<GameObject>("Game/PauseUI");
            if (prefab) Instantiate(prefab, transform);
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _level = null;
            _campaignActive = false;
            IsPaused = false;
            Time.timeScale = 1;
            Notify();
        }

        public void RegisterLevel(LevelRunner level)
        {
            _level = level;
            _campaignActive = _campaignRequested && level.gameObject.scene.name == CampaignScene;
            if (!_campaignActive) return; // Direct editor play is a practice session, without campaign writes.
            if (_pendingResume != null && level.IsSafeCheckpoint(_pendingResume.Position))
            {
                level.Simulation.Player.Position = _pendingResume.Position;
                level.Simulation.Player.Facing = _pendingResume.Facing;
                level.Player.Render(level.Simulation.Player);
                FindAnyObjectByType<LevelCamera>()?.SnapToTarget();
            }
            _campaign = _pendingResume?.Copy() ?? NewCampaign(level);
            _campaign.Position = level.Simulation.Player.Position;
            _campaign.Facing = level.Simulation.Player.Facing;
            _pendingResume = null;
            _dirty = true;
            _nextSaveTime = Time.unscaledTime + 3;
            SaveCampaign();
        }

        static SavedGameData NewCampaign(LevelRunner level) => new SavedGameData
        {
            Version = SavedGameData.CurrentVersion, SceneName = level.gameObject.scene.name,
            Position = level.Simulation.Player.Position, Facing = level.Simulation.Player.Facing,
            HasCheckpoint = true, ProgressFlags = Array.Empty<string>()
        };

        void Update()
        {
            if (_waitForRelease && !InputHeld()) _waitForRelease = false;
            if (!_campaignActive || !_level || IsPaused || IsLoading) return;
            CaptureSafePosition();
            if (_dirty && Time.unscaledTime >= _nextSaveTime)
            {
                SaveCampaign();
                _nextSaveTime = Time.unscaledTime + 3;
            }
        }

        void CaptureSafePosition()
        {
            if (!_campaignActive || !_level || _campaign == null || !_level.TryGetCheckpoint(out Vector2 point)) return;
            int facing = _level.Simulation.Player.Facing;
            if (Vector2.Distance(_campaign.Position, point) > .05f || _campaign.Facing != facing)
            { _campaign.Position = point; _campaign.Facing = facing; _dirty = true; }
        }

        public bool SaveCampaign()
        {
            if (!_campaignActive || _campaign == null) return true;
            CaptureSafePosition();
            if (!_dirty) return true;
            var copy = _campaign.Copy();
            copy.SavedUtc = DateTime.UtcNow.ToString("O");
            if (!Store.TrySave(copy, out string error)) { LastError = error; Notify(); return false; }
            _saved = copy;
            _dirty = false;
            LastError = "";
            Notify();
            return true;
        }

        public void SetProgressFlag(string flag)
        {
            if (!_campaignActive || string.IsNullOrWhiteSpace(flag) || flag.Length > 256 ||
                _campaign.ProgressFlags.Contains(flag)) return;
            _campaign.ProgressFlags = _campaign.ProgressFlags.Concat(new[] { flag }).ToArray();
            _dirty = true;
            SaveCampaign();
        }

        public void StartOrContinueGame()
        {
            if (IsLoading) return;
            _pendingResume = _saved?.Copy();
            _campaignRequested = true;
            StartCoroutine(Load(CampaignScene));
        }
        public void EnterSandbox()
        {
            if (IsLoading || !SaveCampaign()) return;
            _campaignRequested = false;
            _pendingResume = null;
            StartCoroutine(Load(SandboxScene));
        }
        public void ReturnToMainMenu()
        {
            if (IsLoading || !SaveCampaign()) return;
            _campaignRequested = false;
            _pendingResume = null;
            StartCoroutine(Load(MainMenuScene));
        }
        IEnumerator Load(string sceneName)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            { LastError = "No se pudo abrir esta zona del juego."; Notify(); yield break; }
            IsLoading = true;
            IsPaused = false;
            Time.timeScale = 1;
            _waitForRelease = true;
            LastError = "";
            Notify();
            yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            IsLoading = false;
            Notify();
        }

        public void SetPaused(bool paused)
        {
            if (IsLoading || IsPaused == paused) return;
            if (paused)
            {
                SaveCampaign();
                _unpausedTimeScale = Time.timeScale > 0 ? Time.timeScale : 1;
            }
            IsPaused = paused;
            Time.timeScale = paused ? 0 : _unpausedTimeScale;
            // Scene entry has its own release gate. Resuming a pause never blocks movement:
            // each input owner cancels action intent and rearms held buttons independently.
            _waitForRelease = false;
            foreach (var level in FindObjectsByType<LevelRunner>()) level.SetGameplayPaused(paused);
            foreach (var sandbox in FindObjectsByType<SandboxRunner>()) sandbox.SetGameplayPaused(paused);
            Notify();
        }

        public void QuitGame()
        {
            if (IsLoading || !SaveCampaign()) return;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static bool InputHeld() => StickMenuInput.AnyInputHeld();

        void Notify() => StateChanged?.Invoke();
        void OnApplicationFocus(bool focus)
        {
            if (!focus && !IsLoading && (SceneManager.GetActiveScene().name == CampaignScene || SceneManager.GetActiveScene().name == SandboxScene))
            {
                // Native verification explicitly opts into background input; normal play pauses on focus loss.
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-uiSmoke") < 0 &&
                    Array.IndexOf(Environment.GetCommandLineArgs(), "-displaySmoke") < 0 &&
                    Array.IndexOf(Environment.GetCommandLineArgs(), "-audioSmoke") < 0 &&
                    Array.IndexOf(Environment.GetCommandLineArgs(), "-frontEndSmoke") < 0 &&
                    Array.IndexOf(Environment.GetCommandLineArgs(), "-worldSmoke") < 0 &&
                    Array.IndexOf(Environment.GetCommandLineArgs(), "-sandboxSmoke") < 0) SetPaused(true);
            }
        }
        void OnApplicationQuit() => SaveCampaign();
        void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (Instance != this) return;
            Time.timeScale = 1;
            Instance = null;
        }
    }
}
