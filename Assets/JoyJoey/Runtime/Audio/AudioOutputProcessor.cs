using System;
using System.Threading;

namespace JoyJoey.Game.Audio
{
    /// <summary>Final listener processing. No Unity calls or per-buffer allocations on the DSP thread.</summary>
    public sealed class AudioOutputProcessor
    {
        public volatile bool Mono;
        public WindowsAudioOutput Output;
        public float LastPeak;
        public int BlocksProcessed;
        public void Process(float[] data, int channels)
        {
            if (data == null || channels <= 0) return;
            if (Mono) DownmixToMono(data, channels);
            float peak = 0;
            foreach (float value in data) peak = Math.Max(peak, Math.Abs(value));
            LastPeak = peak;
            Interlocked.Increment(ref BlocksProcessed);
            var output = Output;
            if (output != null && output.Submit(data, channels)) Array.Clear(data, 0, data.Length);
        }
        public static void DownmixToMono(float[] data, int channels)
        {
            if (data == null || channels < 2) return;
            for (int frame = 0; frame + channels <= data.Length; frame += channels)
            {
                float sum = 0;
                for (int channel = 0; channel < channels; channel++) sum += data[frame + channel];
                float mono = sum / channels;
                for (int channel = 0; channel < channels; channel++) data[frame + channel] = mono;
            }
        }
    }
}
