// Native math intrinsic contract. The C target provides these through the host runtime
// when a compiled program uses them; declarations keep the Core API source-compatible.
namespace System
{
    public class MathF
    {
        public const float PI = 3.14159274f;
        public extern static float Abs(float value);
        public extern static float Asin(float value);
        public extern static float Atan2(float y, float x);
        public extern static float Ceiling(float value);
        public extern static float Cos(float value);
        public extern static float Exp(float value);
        public extern static float Floor(float value);
        public extern static float Min(float x, float y);
        public extern static float Max(float x, float y);
        public extern static float Pow(float x, float y);
        public extern static float Round(float value);
        public extern static float Sin(float value);
        public extern static float Sqrt(float value);
        public extern static float Tan(float value);
    }
}