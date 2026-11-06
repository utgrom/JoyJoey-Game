using System;
using UnityEngine;

namespace JoyJoey.Sandbox
{
    /// <summary>Separate digital readings of one physical stick for movement and eight-way dashes.</summary>
    public static class StickDirections
    {
        const float SectorTolerance = .000001f;
        const float DashBoundaryToleranceDegrees = .0001f;
        const float Diagonal = .7071067811865475f;
        static readonly Vector2[] DashDirections =
        {
            Vector2.right, new Vector2(Diagonal, Diagonal), Vector2.up, new Vector2(-Diagonal, Diagonal),
            Vector2.left, new Vector2(-Diagonal, -Diagonal), Vector2.down, new Vector2(Diagonal, -Diagonal)
        };

        /// <summary>
        /// Radial strength before digitalizing a source. Zero means inactive, including a 100%
        /// deadzone. The remaining radius is remapped to 0..1 for arbitration against other inputs.
        /// </summary>
        public static float Strength(Vector2 raw, float deadzonePercent)
        {
            if (!IsFinite(raw)) return 0;
            float deadzone = StickInputSettings.SanitizeDeadzonePercent(deadzonePercent) * .01f;
            if (deadzone >= 1) return 0;
            // Double precision also avoids overflow for malformed but finite serialized values.
            double length = Math.Sqrt((double)raw.x * raw.x + (double)raw.y * raw.y);
            float magnitude = (float)Math.Min(1, length);
            if (magnitude <= deadzone) return 0;
            return Mathf.Clamp01((magnitude - deadzone) / (1 - deadzone));
        }

        /// <summary>
        /// Down spans 225..315 degrees, including both boundaries. The rest is left or right;
        /// exactly 90 degrees belongs to right so the vertical boundary is deterministic.
        /// Outside the deadzone every returned direction is digital and full strength.
        /// </summary>
        public static Vector2 ForMovement(Vector2 raw, float deadzonePercent)
        {
            if (Strength(raw, deadzonePercent) <= 0) return Vector2.zero;
            var direction = Unit(raw);
            if (direction.y < 0 && direction.y <= -Mathf.Abs(direction.x) + SectorTolerance)
                return Vector2.down;
            return direction.x < -SectorTolerance ? Vector2.left : Vector2.right;
        }

        public static Vector2 ForDash(Vector2 raw, float deadzonePercent)
            => Strength(raw, deadzonePercent) > 0 ? SnapDash(raw) : Vector2.zero;

        /// <summary>
        /// Nearest 45-degree direction, with equal cardinal/diagonal speed. An exact halfway
        /// boundary belongs to the counterclockwise sector; tolerance only absorbs float noise.
        /// </summary>
        public static Vector2 SnapDash(Vector2 direction)
        {
            if (!IsFinite(direction) || direction.x == 0 && direction.y == 0) return Vector2.zero;
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            if (angle < 0) angle += 360;
            int octant = Mathf.FloorToInt((angle + 22.5f + DashBoundaryToleranceDegrees) / 45) % 8;
            return DashDirections[octant];
        }

        static Vector2 Unit(Vector2 value)
        {
            double length = Math.Sqrt((double)value.x * value.x + (double)value.y * value.y);
            return length > 0 ? new Vector2((float)(value.x / length), (float)(value.y / length)) : Vector2.zero;
        }

        static bool IsFinite(Vector2 value)
            => !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}
