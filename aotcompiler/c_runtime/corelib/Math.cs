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
        // .NET: Sign(NaN) -> ArithmeticException; Clamp(min > max) -> ArgumentException
        public static int Sign(double value) { if (value != value) throw new ArithmeticException("Function does not accept floating point Not-a-Number values."); return value > 0 ? 1 : value < 0 ? -1 : 0; }
        public static extern double Ceiling(double value);
        public static extern double Round(double value);
        public static float Clamp(float value, float min, float max) { if (min > max) throw new ArgumentException("'" + min + "' cannot be greater than " + max + "."); return value < min ? min : value > max ? max : value; }
        public static int Clamp(int value, int min, int max) { if (min > max) throw new ArgumentException("'" + min + "' cannot be greater than " + max + "."); return value < min ? min : value > max ? max : value; }
    }
}