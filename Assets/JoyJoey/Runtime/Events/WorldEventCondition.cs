using UnityEngine;

namespace JoyJoey.Game.Events
{
    /// <summary>Base class for inspector-configured checks against an event and its world.</summary>
    public abstract class WorldEventCondition : MonoBehaviour
    {
        public abstract bool Evaluate(WorldEventContext context);
    }
}
