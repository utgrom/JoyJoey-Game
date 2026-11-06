using System;

namespace JoyJoey.Game.Display
{
    public enum DisplayWindowMode { Borderless, Windowed }

    [Serializable]
    public sealed class DisplayPreferences
    {
        public int Version = 1;
        public DisplayWindowMode WindowMode = DisplayWindowMode.Borderless;
        public int WindowWidth = 1440, WindowHeight = 900;
        public float RenderScale = 1;
        public int FpsCap = 120;
        public bool VSync;
        public float Brightness, Contrast = 1, Gamma = 1;

        public static DisplayPreferences Defaults() => new DisplayPreferences();
        public DisplayPreferences Copy() => (DisplayPreferences)MemberwiseClone();
        public static bool IsValid(DisplayPreferences data)
        {
            if (data == null || data.Version != 1 ||
                data.WindowMode != DisplayWindowMode.Borderless && data.WindowMode != DisplayWindowMode.Windowed ||
                data.WindowWidth < 640 || data.WindowWidth > 16384 || data.WindowHeight < 360 || data.WindowHeight > 16384 ||
                !InRange(data.RenderScale, .5f, 1) || !InRange(data.Brightness, -.5f, .5f) ||
                !InRange(data.Contrast, .5f, 1.5f) || !InRange(data.Gamma, .6f, 1.6f)) return false;
            return data.FpsCap == 0 || data.FpsCap == 30 || data.FpsCap == 60 || data.FpsCap == 90 ||
                data.FpsCap == 120 || data.FpsCap == 144 || data.FpsCap == 165 || data.FpsCap == 240;
        }
        static bool InRange(float value, float min, float max) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }

    public readonly struct DisplayResolution : IEquatable<DisplayResolution>
    {
        public int Width { get; }
        public int Height { get; }
        public DisplayResolution(int width, int height) { Width = width; Height = height; }
        public bool Equals(DisplayResolution other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object other) => other is DisplayResolution resolution && Equals(resolution);
        public override int GetHashCode() => Width * 397 ^ Height;
        public override string ToString() => Width + " × " + Height;
    }
}
