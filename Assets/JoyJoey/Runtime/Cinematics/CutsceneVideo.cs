using UnityEngine;
using UnityEngine.Video;

namespace JoyJoey.Game.Cinematics
{
    /// <summary>Plays a video on a GameObject activated by the owning cutscene's Timeline.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(VideoPlayer))]
    public sealed class CutsceneVideo : MonoBehaviour
    {
        [SerializeField, Tooltip("Cinemática que controla este video. Si se omite, se busca en los padres.")]
        CutscenePlayer _cutscene;

        [SerializeField, Tooltip("Objeto hijo con la imagen del video. Se muestra cuando el video está listo.")]
        GameObject _surface;

        VideoPlayer _video;
        bool _requested;
        bool _ended;

        void Reset()
        {
            _cutscene = GetComponentInParent<CutscenePlayer>();
            GetComponent<VideoPlayer>().playOnAwake = false;
        }

        void Awake()
        {
            _video = GetComponent<VideoPlayer>();
            _video.playOnAwake = false;
            _video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            _video.waitForFirstFrame = true;
        }

        void OnEnable()
        {
            SetSurface(false);
            if (!_cutscene) _cutscene = GetComponentInParent<CutscenePlayer>();
            if (!_cutscene) return;
            CutscenePlayer.ActiveChanged += ActiveChanged;
            _cutscene.StateChanged += Synchronize;
            _video.prepareCompleted += Prepared;
            _video.loopPointReached += ReachedEnd;
            _video.errorReceived += Failed;
            Synchronize();
        }

        void OnDisable()
        {
            CutscenePlayer.ActiveChanged -= ActiveChanged;
            if (_cutscene) _cutscene.StateChanged -= Synchronize;
            if (_video)
            {
                _video.prepareCompleted -= Prepared;
                _video.loopPointReached -= ReachedEnd;
                _video.errorReceived -= Failed;
            }
            StopSegment();
        }

        void ActiveChanged(CutscenePlayer _) => Synchronize();
        void Prepared(VideoPlayer _) => Synchronize();

        void Synchronize()
        {
            if (!_cutscene || CutscenePlayer.Active != _cutscene)
            {
                StopSegment();
                return;
            }
            if (!_video || !_video.isActiveAndEnabled || _ended) return;
            if (!_requested)
            {
                bool hasSource = _video.source == VideoSource.VideoClip ? _video.clip : !string.IsNullOrWhiteSpace(_video.url);
                if (!hasSource) return;
                _requested = true;
                _video.Prepare();
            }
            if (!_video.isPrepared) return;
            if (_cutscene.IsPaused) _video.Pause();
            else if (_cutscene.IsPlaying && !_video.isPlaying)
            {
                _video.Play();
                SetSurface(true);
            }
        }

        void ReachedEnd(VideoPlayer video) => _ended = !video.isLooping;

        void Failed(VideoPlayer video, string error)
        {
            if (!_requested || _ended) return;
            _ended = true;
            video.Stop();
            SetSurface(false);
            Debug.LogWarning("No se pudo reproducir el video de la cinemática. " + error, this);
        }

        void StopSegment()
        {
            bool wasRequested = _requested;
            _requested = false;
            _ended = false;
            if (wasRequested && _video) _video.Stop();
            SetSurface(false);
        }

        void SetSurface(bool visible)
        {
            if (_surface && _surface != gameObject && _surface.transform.IsChildOf(transform))
                _surface.SetActive(visible);
        }
    }
}
