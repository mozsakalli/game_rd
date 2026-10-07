// corelib: primitive struct'larin static yuzeyi (well-known binding: yeni tip yaratmaz,
// uyeler Primitive.Int/Double... uzerine baglanir -> `double.TryParse(...)` cozulur).
// Bilincli kismi yuzey: yalniz (string, out T) TryParse'lar (NumberStyles/IFormatProvider yok).
// Semantik dotnet birebir: bas/son whitespace kabul, invariant, tasma -> false (float/double: Infinity+true).
namespace System
{
    public struct Int32
    {
        public const int MinValue = -2147483647 - 1;
        public const int MaxValue = 2147483647;
        public extern override string ToString();
        public extern string ToString(string format); // yalniz "X"/"Xn"/"D"/"Dn" (invariant)
        public extern int CompareTo(int value);
        public extern static bool TryParse(string s, out int result);
        public extern static bool TryParse(string s, Globalization.NumberStyles style, object provider, out int result);
    }
    public struct UInt32
    {
        public const uint MaxValue = 4294967295u;
        public extern override string ToString();
        public extern static bool TryParse(string s, out uint result);
    }
    public struct Int64
    {
        public extern override string ToString();
        public extern static bool TryParse(string s, out long result);
    }
    public struct Int16
    {
        public const short MaxValue = 32767;
        public const short MinValue = -32768;
    }
    public struct Single
    {
        public const float MaxValue = 3.40282347E+38f;
        public const float MinValue = -3.40282347E+38f;
        public const float NaN = 0.0f / 0.0f;
        public const float Epsilon = 1.401298E-45f;
        public const float PositiveInfinity = 1.0f / 0.0f;
        public const float NegativeInfinity = -1.0f / 0.0f;
        public extern override string ToString();
        public extern static bool IsPositiveInfinity(float value);
        public extern static bool TryParse(string s, out float result);
    }
    public struct Double
    {
        public const double MaxValue = 1.7976931348623157E+308;
        public const double MinValue = -1.7976931348623157E+308;
        public const double PositiveInfinity = 1.0 / 0.0;
        public const double NegativeInfinity = -1.0 / 0.0;
        public const double NaN = 0.0 / 0.0;
        public extern override string ToString();
        public extern static bool TryParse(string s, out double result);
    }
}
