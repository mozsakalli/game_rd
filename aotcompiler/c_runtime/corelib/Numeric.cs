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
        public const long MinValue = -9223372036854775807L - 1L;
        public const long MaxValue = 9223372036854775807L;
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
        public static bool IsNaN(float f) { return f != f; }
        public static bool IsInfinity(float f) { return f == PositiveInfinity || f == NegativeInfinity; }
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
        public static bool IsNaN(double d) { return d != d; }
        public static bool IsInfinity(double d) { return d == PositiveInfinity || d == NegativeInfinity; }
    }
    // Char statikleri (well-known binding: Primitive.Char). Kultursuz: ASCII + Latin-1/Latin Ext-A harfler
    // (motor fontlarinin charset'i; Turkce i/I ozel: 'ı'->'I', 'i'->'İ' YAPILMAZ, .NET invariant ile ayni).
    public struct Char
    {
        public static bool IsDigit(char c) { return c >= '0' && c <= '9'; }
        public static bool IsWhiteSpace(char c) { return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v' || c == '\u00A0'; }
        public static bool IsUpper(char c) { return (c >= 'A' && c <= 'Z') || (c >= '\u00C0' && c <= '\u00DE' && c != '\u00D7') || (c >= '\u0100' && c <= '\u017F' && (c & 1) == 0 && c != '\u0130' && c != '\u0131'); }
        public static bool IsLower(char c) { return (c >= 'a' && c <= 'z') || (c >= '\u00DF' && c <= '\u00FF' && c != '\u00F7') || (c >= '\u0100' && c <= '\u017F' && (c & 1) == 1); }
        public static bool IsLetter(char c) { return IsUpper(c) || IsLower(c); }
        public static bool IsLetterOrDigit(char c) { return IsLetter(c) || IsDigit(c); }
        public static char ToUpperInvariant(char c)
        {
            if (c >= 'a' && c <= 'z') return (char)(c - 32);
            if (c >= '\u00E0' && c <= '\u00FE' && c != '\u00F7') return (char)(c - 32);
            if (c >= '\u0100' && c <= '\u017F' && (c & 1) == 1 && c != '\u0131') return (char)(c - 1);
            if (c == '\u0131') return 'I';
            return c;
        }
        public static char ToLowerInvariant(char c)
        {
            if (c >= 'A' && c <= 'Z') return (char)(c + 32);
            if (c >= '\u00C0' && c <= '\u00DE' && c != '\u00D7') return (char)(c + 32);
            if (c >= '\u0100' && c <= '\u017F' && (c & 1) == 0 && c != '\u0130') return (char)(c + 1);
            if (c == '\u0130') return 'i';
            return c;
        }
        public static char ToUpper(char c) { return ToUpperInvariant(c); }
        public static char ToLower(char c) { return ToLowerInvariant(c); }
        // UTF-16 vekil ciftleri (.NET ile ayni; ConvertToUtf32 gecersiz ciftte ArgumentException).
        public static bool IsHighSurrogate(char c) { return c >= '\uD800' && c <= '\uDBFF'; }
        public static bool IsLowSurrogate(char c) { return c >= '\uDC00' && c <= '\uDFFF'; }
        public static bool IsSurrogate(char c) { return c >= '\uD800' && c <= '\uDFFF'; }
        public static bool IsSurrogatePair(char highSurrogate, char lowSurrogate) { return IsHighSurrogate(highSurrogate) && IsLowSurrogate(lowSurrogate); }
        public static int ConvertToUtf32(char highSurrogate, char lowSurrogate)
        {
            if (!IsHighSurrogate(highSurrogate)) throw new ArgumentOutOfRangeException("highSurrogate", "A valid high surrogate character is between 0xd800 and 0xdbff, inclusive.");
            if (!IsLowSurrogate(lowSurrogate)) throw new ArgumentOutOfRangeException("lowSurrogate", "A valid low surrogate character is between 0xdc00 and 0xdfff, inclusive.");
            return ((highSurrogate - 0xD800) << 10) + (lowSurrogate - 0xDC00) + 0x10000;
        }
    }
}
