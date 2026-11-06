using UnityEngine;

namespace JoyJoey.Sandbox
{
    [CreateAssetMenu(menuName = "Joy Joey/Motor Tuning")]
    public sealed class MotorTuning : ScriptableObject
    {
        public float RunSpeed = 8.5f, GroundAcceleration = 75, AirAcceleration = 26;
        public float GroundFriction = 30, AirFriction = 1.2f;
        public float Gravity = 38, JumpSpeed = 15, FallSpeed = 23;
        public float DashSpeed = 18;
        public int DashDuration = 11, DashCooldown = 19, InputBuffer = 10, CoyoteTime = 6;
        public float Width = .8f, Height = 1.65f;
    }
}
