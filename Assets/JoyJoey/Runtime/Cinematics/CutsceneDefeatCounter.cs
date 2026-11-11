using System.Collections.Generic;
using UnityEngine;

namespace JoyJoey.Game.Cinematics
{
    /// <summary>Connect only the defeat events that belong to this encounter.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(CutsceneTrigger))]
    public sealed class CutsceneDefeatCounter : MonoBehaviour
    {
        [SerializeField, Min(1)] int _requiredDefeats = 1;
        readonly HashSet<GameObject> _reportedEnemies = new HashSet<GameObject>();
        CutsceneTrigger _trigger;
        int _defeats;
        bool _played;

        void Awake() => _trigger = GetComponent<CutsceneTrigger>();

        public void RegisterDefeat() => _defeats = Mathf.Min(_defeats + 1, Mathf.Max(1, _requiredDefeats));

        public void RegisterDefeatedEnemy(GameObject enemy)
        {
            if (enemy && _reportedEnemies.Add(enemy)) RegisterDefeat();
        }

        void LateUpdate()
        {
            if (!_played && _defeats >= Mathf.Max(1, _requiredDefeats)) _played = _trigger.TryActivate();
        }
    }
}
