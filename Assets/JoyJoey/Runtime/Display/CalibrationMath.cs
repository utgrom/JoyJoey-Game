using UnityEngine;

namespace JoyJoey.Game.Display
{
    /// <summary>Display-referred correction shared by the rendered world and the UI calibration swatches.</summary>
    public static class CalibrationMath
    {
        public static float ApplyDisplay(float value, float brightness, float contrast, float gamma)
        {
            float adjusted = Mathf.Clamp01((value - .5f) * contrast + .5f + brightness);
            return Mathf.Pow(adjusted, 1 / Mathf.Max(.01f, gamma));
        }

        public static Color ApplyLinear(Color value, float brightness, float contrast, float gamma)
        {
            value.r = Mathf.GammaToLinearSpace(ApplyDisplay(Mathf.LinearToGammaSpace(value.r), brightness, contrast, gamma));
            value.g = Mathf.GammaToLinearSpace(ApplyDisplay(Mathf.LinearToGammaSpace(value.g), brightness, contrast, gamma));
            value.b = Mathf.GammaToLinearSpace(ApplyDisplay(Mathf.LinearToGammaSpace(value.b), brightness, contrast, gamma));
            return value;
        }
    }

    /// <summary>Only the display service writes this state. Rendering does not mutate scene color grading.</summary>
    public static class CalibrationSettings
    {
        public static Vector4 Values { get; private set; } = new Vector4(0, 1, 1, 0);
        public static bool IsNeutral => Mathf.Abs(Values.x) < .00001f && Mathf.Abs(Values.y - 1) < .00001f && Mathf.Abs(Values.z - 1) < .00001f;
        public static int LastRenderWidth { get; private set; }
        public static int LastRenderHeight { get; private set; }
        /// <summary>Number of calibrated camera render graphs recorded; pixel captures verify their visual result.</summary>
        public static int RenderedFrameCount { get; private set; }

        public static void Set(float brightness, float contrast, float gamma)
        {
            Values = new Vector4(SafeClamp(brightness, -.5f, .5f, 0),
                SafeClamp(contrast, .5f, 1.5f, 1), SafeClamp(gamma, .6f, 1.6f, 1), 0);
        }
        static float SafeClamp(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Values = new Vector4(0, 1, 1, 0);
            LastRenderWidth = LastRenderHeight = RenderedFrameCount = 0;
        }

        internal static void RecordDimensions(int width, int height)
        {
            LastRenderWidth = width;
            LastRenderHeight = height;
            RenderedFrameCount++;
        }
    }
}
