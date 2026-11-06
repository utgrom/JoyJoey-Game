using System;

namespace JoyJoey.Game.Audio
{
    public enum AudioChannel { Master, Music, Sfx, Ui }
    public enum AudioContext { Normal, Pause, Cutscene }

    [Serializable]
    public sealed class AudioPreferences
    {
        public int Version = 1;
        public float Master = 1, Music = .65f, Sfx = .8f, Ui = .8f;
        public bool Mono;
        public string OutputDeviceId = "";
        public static AudioPreferences Defaults() => new AudioPreferences();
        public AudioPreferences Copy() => (AudioPreferences)MemberwiseClone();
        public float GetVolume(AudioChannel channel)
            => channel == AudioChannel.Master ? Master : channel == AudioChannel.Music ? Music : channel == AudioChannel.Sfx ? Sfx : Ui;
        public void SetVolume(AudioChannel channel, float value)
        {
            value = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, Math.Min(1, value));
            switch (channel)
            {
                case AudioChannel.Master: Master = value; break;
                case AudioChannel.Music: Music = value; break;
                case AudioChannel.Sfx: Sfx = value; break;
                case AudioChannel.Ui: Ui = value; break;
            }
        }
    }
}
