using UnityEngine;

namespace JoyJoey.Game.Cinematics
{
    /// <summary>Scene-local entry point for UnityEvents, zones and encounter logic.</summary>
    [DisallowMultipleComponent]
    public sealed class CutsceneTrigger : MonoBehaviour
    {
        [SerializeField] CutscenePlayer _player;
        [SerializeField] bool _oncePerSceneInstance = true;
        bool _triggered;

        public bool CanTrigger => isActiveAndEnabled && _player && (!_oncePerSceneInstance || !_triggered);

        public void Activate() => TryActivate();

        public bool TryActivate()
        {
            if (!CanTrigger || CutscenePlayer.Active || GameSession.Instance && GameSession.Instance.BlocksGameplay) return false;
            _triggered = true;
            if (_player.TryPlay()) return true;
            _triggered = false;
            return false;
        }
    }
}
