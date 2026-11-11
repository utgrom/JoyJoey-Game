using UnityEngine;

namespace JoyJoey.Game.Events
{
    /// <summary>Expone cambios de estado simples para las acciones de un UnityEvent.</summary>
    public sealed class WorldEventStateActions : MonoBehaviour
    {
        [SerializeField] private WorldEventScope _scope;

        public void SetFlag(string key)
        {
            if (_scope) _scope.SetFlag(key, true);
        }

        public void ClearFlag(string key)
        {
            if (_scope) _scope.SetFlag(key, false);
        }

        public void IncrementCounter(string key)
        {
            if (_scope) _scope.AddToCounter(key);
        }
    }
}
