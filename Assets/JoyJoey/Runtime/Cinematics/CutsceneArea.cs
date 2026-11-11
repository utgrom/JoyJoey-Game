using UnityEngine;

namespace JoyJoey.Game.Cinematics
{
    [DisallowMultipleComponent, RequireComponent(typeof(BoxCollider2D), typeof(CutsceneTrigger))]
    public sealed class CutsceneArea : JoeyArea
    {
        CutsceneTrigger _trigger;

        protected override void Awake()
        {
            base.Awake();
            _trigger = GetComponent<CutsceneTrigger>();
        }

        protected override void Entered(GameObject joey)
        {
            _trigger.TryActivate();
            base.Entered(joey);
        }
    }
}
