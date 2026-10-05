// Native math intrinsic contract. The C target provides these through the host runtime
// when a compiled program uses them; declarations keep the Core API source-compatible.
namespace System
{
    class MathF
    {
        public const float PI = 3.14159274f;
        extern public static float Abs(float value);
        extern public static float Asin(float value);
        extern public static float Atan2(float y, float x);
        extern public static float Ceiling(float value);
        extern public static float Cos(float value);
        extern public static float Exp(float value);
        extern public static float Floor(float value);
        extern public static float Min(float x, float y);
        extern public static float Pow(float x, float y);
        extern public static float Round(float value);
        extern public static float Sin(float value);
        extern public static float Sqrt(float value);
        extern public static float Tan(float value);
    }
}