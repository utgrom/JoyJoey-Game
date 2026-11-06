using UnityEngine;

namespace JoyJoey.Sandbox
{
    [CreateAssetMenu(menuName="Joy Joey/Enemy Profile")]
    public sealed class EnemyProfile : ScriptableObject
    {
        public string DisplayName = "Ligero";
        public bool CanActInAir;
        [Min(0)] public float GroundArmor;
        [Min(0)] public float ArmorRegenPerSecond = 12;
        [Min(1)] public int ArmorRegenDelay = 45;
        [Min(0)] public float AirJuggleThreshold;
        [Min(1)] public int JuggleWindow = 18;
        [Min(.05f)] public float CorrectionWeight = 1;
        [Range(.1f,2)] public float LaunchVelocityScale = 1;
    }
}
