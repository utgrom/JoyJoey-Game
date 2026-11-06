using System.Collections.Generic;
using UnityEngine;

namespace JoyJoey.Sandbox
{
    [CreateAssetMenu(menuName = "Joy Joey/Combat Catalog")]
    public sealed class CombatCatalog : ScriptableObject
    {
        public List<MoveDefinition> Moves = new List<MoveDefinition>();
        public int SchemaVersion;
        public List<MoveBinding> Bindings = new List<MoveBinding>();
        public MoveDefinition Find(string id) => Moves.Find(m => m.Id == id);
    }
}
