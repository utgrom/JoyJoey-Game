using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;

namespace JoyJoey.Game.Cinematics
{
    public enum CutsceneEndReason { Completed, Skipped, Canceled }

    /// <summary>Owns the active Timeline and releases it before advancing the game.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(PlayableDirector))]
    public sealed class CutscenePlayer : MonoBehaviour
    {
        [SerializeField, Tooltip("Activa la cinemática al habilitar este objeto.")]
        bool _playOnEnable;

        [SerializeField, Tooltip("Acciones que se ejecutan al terminar o saltar la cinemática.")]
        UnityEvent _onFinished = new UnityEvent();

        static CutscenePlayer _active;
        static bool _quitting;
        PlayableDirector _director;
        bool _started;
        bool _paused;
        bool _finishing;

        public static CutscenePlayer Active => _active;
        public static event Action<CutscenePlayer> ActiveChanged;

        public PlayableDirector Director => _director ? _director : _director = GetComponent<PlayableDirector>();
        public UnityEvent OnFinished => _onFinished;
        public bool IsPlaying => _active == this && !_paused;
        public bool IsPaused => _active == this && _paused;
        public event Action StateChanged;
        public event Action<CutsceneEndReason> Ended;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _active = null;
            _quitting = false;
            ActiveChanged = null;
        }

        void Reset() => Director.playOnAwake = false;
        void Awake() => Director.playOnAwake = false;

        void Start()
        {
            _started = true;
            if (_playOnEnable) Play();
        }

        void OnEnable()
        {
            if (_started && _playOnEnable) Play();
        }

        public void Play() => TryPlay();

        public bool TryPlay()
        {
            if (_quitting || _active || _finishing || !isActiveAndEnabled) return false;
            var director = Director;
            if (!director || !director.isActiveAndEnabled || !director.playableAsset) return false;
            double duration = director.duration;
            if (duration <= 0 || double.IsNaN(duration) || double.IsInfinity(duration)) return false;

            director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            // Hold makes natural completion observable before Stop destroys the graph and resets time.
            director.extrapolationMode = DirectorWrapMode.Hold;
            director.time = director.initialTime;
            director.stopped += HandleDirectorStopped;
            _active = this;
            try
            {
                ActiveChanged?.Invoke(this);
                if (_active != this) return false;
                director.Play();
                if (_active != this) return false;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                Cancel();
                return false;
            }
            StateChanged?.Invoke();
            return true;
        }

        public void Pause()
        {
            if (!IsPlaying || _finishing) return;
            _paused = true;
            Director.Pause();
            StateChanged?.Invoke();
        }

        public void Resume()
        {
            if (!IsPaused || _finishing) return;
            _paused = false;
            Director.Resume();
            StateChanged?.Invoke();
        }

        public void Skip() => Finish(CutsceneEndReason.Skipped);
        public void Cancel() => Finish(CutsceneEndReason.Canceled);

        void LateUpdate()
        {
            if (_active != this) return;
            var director = Director;
            if (!director || !director.isActiveAndEnabled)
            {
                Cancel();
                return;
            }
            if (IsPlaying && director.time >= director.duration)
                Finish(CutsceneEndReason.Completed);
        }

        void HandleDirectorStopped(PlayableDirector _) => Finish(CutsceneEndReason.Canceled, stopDirector: false);

        void Finish(CutsceneEndReason reason, bool stopDirector = true)
        {
            if (_active != this || _finishing) return;
            _finishing = true;
            var director = Director;
            try
            {
                if (director)
                {
                    director.stopped -= HandleDirectorStopped;
                    if (stopDirector) director.Stop();
                }
            }
            catch (Exception exception) { Debug.LogException(exception, this); }

            _active = null;
            _paused = false;
            try
            {
                ActiveChanged?.Invoke(null);
                StateChanged?.Invoke();
                Ended?.Invoke(reason);
            }
            finally
            {
                _finishing = false;
                if (reason != CutsceneEndReason.Canceled) _onFinished.Invoke();
            }
        }

        void OnDisable() => Cancel();

        void OnApplicationQuit()
        {
            _quitting = true;
            Cancel();
        }
    }
}
