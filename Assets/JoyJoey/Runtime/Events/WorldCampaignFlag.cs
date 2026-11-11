using UnityEngine;

namespace JoyJoey.Game.Events
{
    [AddComponentMenu("Joy Joey/Events/Campaign Flag")]
    public sealed class WorldCampaignFlag : WorldEventCondition
    {
        [Tooltip("Identificador de la marca en el guardado de la campaña.")]
        [SerializeField] string _flag;
        [SerializeField] bool _expected = true;

        public override bool Evaluate(WorldEventContext context)
        {
            var session = GameSession.Instance;
            return session && session.TryGetProgressFlag(_flag, out bool isSet) && isSet == _expected;
        }

        public void SetFlag()
        {
            if (GameSession.Instance) GameSession.Instance.SetProgressFlag(_flag);
        }
    }
}
