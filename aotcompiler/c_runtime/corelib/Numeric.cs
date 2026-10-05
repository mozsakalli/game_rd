// corelib: primitive struct'larin static yuzeyi (well-known binding: yeni tip yaratmaz,
// uyeler Primitive.Int/Double... uzerine baglanir -> `double.TryParse(...)` cozulur).
// Bilincli kismi yuzey: yalniz (string, out T) TryParse'lar (NumberStyles/IFormatProvider yok).
// Semantik dotnet birebir: bas/son whitespace kabul, invariant, tasma -> false (float/double: Infinity+true).
namespace System
{
    struct Int32
    {
        public const int MinValue = -2147483647 - 1;
        public const int MaxValue = 2147483647;
        extern string ToString();
        extern string ToString(string format); // yalniz "X"/"Xn"/"D"/"Dn" (invariant)
        extern int CompareTo(int value);
        extern static bool TryParse(string s, out int result);
        extern static bool TryParse(string s, Globalization.NumberStyles style, object provider, out int result);
    }
    struct UInt32
    {
        public const uint MaxValue = 4294967295u;
        extern string ToString();
        extern static bool TryParse(string s, out uint result);
    }
    struct Int64
    {
        extern string ToString();
        extern static bool TryParse(string s, out long result);
    }
    struct Int16
    {
        public const short MaxValue = 32767;
    }
    struct Single
    {
        public const float MaxValue = 3.40282347E+38f;
        public const float PositiveInfinity = 1.0f / 0.0f;
        public const float NegativeInfinity = -1.0f / 0.0f;
        extern string ToString();
        extern static bool IsPositiveInfinity(float value);
        extern static bool TryParse(string s, out float result);
    }
    struct Double
    {
        public const double MaxValue = 1.7976931348623157E+308;
        public const double MinValue = -1.7976931348623157E+308;
        public const double PositiveInfinity = 1.0 / 0.0;
        public const double NegativeInfinity = -1.0 / 0.0;
        public const double NaN = 0.0 / 0.0;
        extern string ToString();
        extern static bool TryParse(string s, out double result);
    }
}
