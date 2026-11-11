using UnityEngine;

namespace JoyJoey.Game.Events
{
    public sealed class WorldFlagCondition : WorldEventCondition
    {
        [SerializeField] private string _key;
        [SerializeField] private bool _expected = true;

        public override bool Evaluate(WorldEventContext context) =>
            context.Scope && !string.IsNullOrWhiteSpace(_key) &&
            context.Scope.GetFlag(_key) == _expected;
    }
}
