using UnityEngine;

namespace JoyJoey.Game.Events
{
    [CreateAssetMenu(fileName = "WorldEvent", menuName = "Joy Joey/Eventos/Evento del mundo")]
    public sealed class WorldEvent : ScriptableObject { }

    public readonly struct WorldEventContext
    {
        public WorldEvent Channel { get; }
        public WorldEventScope Scope { get; }
        public GameObject Source { get; }

        internal WorldEventContext(WorldEvent channel, WorldEventScope scope, GameObject source)
        {
            Channel = channel;
            Scope = scope;
            Source = source;
        }
    }
}
