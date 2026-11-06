using UnityEngine;

namespace JoyJoey.Game.Audio
{
    /// <summary>Attach to the active AudioListener so music, SFX and UI share the same final output.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(AudioListener))]
    public sealed class AudioMixOutput : MonoBehaviour
    {
        public AudioOutputProcessor Processor;
        void OnAudioFilterRead(float[] data, int channels) => Processor?.Process(data, channels);
        void OnDestroy() => Processor = null;
    }
}
