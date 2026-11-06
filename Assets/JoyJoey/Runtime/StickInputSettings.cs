using UnityEngine;

namespace JoyJoey.Sandbox
{
    /// <summary>Stick deadzone shared by gameplay, menus and input release gates.</summary>
    [CreateAssetMenu(fileName = "JoyJoeyStickSettings", menuName = "Joy Joey/Input/Stick Settings")]
    public sealed class StickInputSettings : ScriptableObject
    {
        public const string ResourceName = "JoyJoeyStickSettings";
        public const float DefaultDeadzonePercent = 22;

        [Range(0, 100)]
        [Tooltip("Zona muerta radial del stick izquierdo, en porcentaje. 100 desactiva sus direcciones en juego y menús; teclado y cruceta siguen disponibles.")]
        public float LeftStickDeadzonePercent = DefaultDeadzonePercent;

        public static float SanitizeDeadzonePercent(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? DefaultDeadzonePercent : Mathf.Clamp(value, 0, 100);

        void OnValidate() => LeftStickDeadzonePercent = SanitizeDeadzonePercent(LeftStickDeadzonePercent);
    }
}
