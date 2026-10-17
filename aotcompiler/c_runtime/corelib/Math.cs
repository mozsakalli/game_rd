namespace System
{
    public class Math
    {
        public static extern int Min(int x, int y);
        public static extern int Max(int x, int y);
        public static extern float Abs(float value);
        public static extern double Abs(double value);
        public static extern float Max(float x, float y);
        public static extern double Max(double x, double y);
        public static long Min(long x, long y) { return x < y ? x : y; }
        public static long Max(long x, long y) { return x > y ? x : y; }
        public static int Abs(int value) { if (value == int.MinValue) throw new OverflowException("Negating the minimum value of a twos complement number is invalid."); return value < 0 ? -value : value; }
        public static long Abs(long value) { if (value == long.MinValue) throw new OverflowException("Negating the minimum value of a twos complement number is invalid."); return value < 0 ? -value : value; }
        // .NET: Sign(NaN) -> ArithmeticException; Clamp(min > max) -> ArgumentException
        public static int Sign(double value) { if (value != value) throw new ArithmeticException("Function does not accept floating point Not-a-Number values."); return value > 0 ? 1 : value < 0 ? -1 : 0; }
        public static extern double Ceiling(double value);
        public static extern double Round(double value);
        // .NET: digits 0..15 disi ArgumentOutOfRange; MidpointRounding.ToEven (Round(double) ile ayni kural)
        public static double Round(double value, int digits)
        {
            if (digits < 0 || digits > 15) throw new ArgumentOutOfRangeException("digits", "Rounding digits must be between 0 and 15, inclusive.");
            if (value != value || digits == 0) return Round(value);
            double scale = 1;
            for (int i = 0; i < digits; i++) scale *= 10;
            double scaled = value * scale;
            if (scaled > 9007199254740992.0 || scaled < -9007199254740992.0) return value; // 2^53 ustu zaten tamsayi
            return Round(scaled) / scale;
        }
        public static float Clamp(float value, float min, float max) { if (min > max) throw new ArgumentException("'" + min + "' cannot be greater than " + max + "."); return value < min ? min : value > max ? max : value; }
        public static int Clamp(int value, int min, int max) { if (min > max) throw new ArgumentException("'" + min + "' cannot be greater than " + max + "."); return value < min ? min : value > max ? max : value; }
    }
}