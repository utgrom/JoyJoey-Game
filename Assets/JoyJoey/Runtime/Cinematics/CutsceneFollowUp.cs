using UnityEngine;

namespace JoyJoey.Game.Cinematics
{
    /// <summary>Optional common follow-up actions for CutscenePlayer.OnFinished.</summary>
    [DisallowMultipleComponent]
    public sealed class CutsceneFollowUp : MonoBehaviour
    {
        [Tooltip("Marcador de pies sobre un piso válido del nivel actual.")]
        [SerializeField] Transform _destination;
        [Tooltip("Nombre de una escena incluida en el Build Profile.")]
        [SerializeField] string _nextScene;

        public void MoveJoeyToMarker()
        {
            var level = GameSession.Instance?.ActiveLevel;
            if (!level || !_destination || !level.TryMoveJoeyTo(_destination.position))
                Debug.LogWarning("Joy Joey: la cinemática necesita un marcador válido en el nivel activo.", this);
        }

        public void LoadNextScene()
        {
            if (string.IsNullOrWhiteSpace(_nextScene))
            {
                Debug.LogWarning("Joy Joey: asigná una escena de destino a la cinemática.", this);
                return;
            }
            GameSession.EnsureExists().TravelToScene(_nextScene);
        }
    }
}
