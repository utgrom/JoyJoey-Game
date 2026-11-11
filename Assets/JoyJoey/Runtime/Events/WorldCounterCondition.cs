using UnityEngine;

namespace JoyJoey.Game.Events
{
    public sealed class WorldCounterCondition : WorldEventCondition
    {
        [SerializeField] private string _key;
        [SerializeField] private int _minimum = 1;

        public override bool Evaluate(WorldEventContext context) =>
            context.Scope && !string.IsNullOrWhiteSpace(_key) &&
            context.Scope.GetCounter(_key) >= _minimum;
    }
}
