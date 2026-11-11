using UnityEngine;

namespace JoyJoey.Game.Events
{
    /// <summary>Publica un canal desde un UnityEvent, un Timeline o cualquier otro componente.</summary>
    public sealed class WorldEventEmitter : MonoBehaviour
    {
        [SerializeField] private WorldEventScope _scope;
        [SerializeField] private WorldEvent _event;
        [Tooltip("Origen opcional. Se usa este objeto cuando el campo queda vacío.")]
        [SerializeField] private GameObject _source;

        public void Raise() => RaiseFrom(null);

        public void RaiseFrom(GameObject source)
        {
            if (!_scope) return;
            if (!source) source = _source;
            _scope.Publish(_event, source ? source : gameObject);
        }
    }
}
