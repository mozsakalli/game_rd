namespace Demo46
{
    class Defaults
    {
        public enum Mode { First, Second }
        public static int Pick(Mode mode = Mode.Second) { return mode == Mode.Second ? 1 : 0; }
    }
    class GenericProbe<T>
    {
        public static int Kind(T value) { return value is System.ValueType ? 8 : 16; }
        public static int IsNull(T value) { return !(value is System.ValueType) && null == (object)value ? 1 : 0; }
        public static string AsString(T value) { return value as string; }
    }
    class BaseIndex46
    {
        int stored;
        public int this[int key] { get { return stored + key; } set { stored = value; } }
    }
    class DerivedIndex46 : BaseIndex46
    {
        public int Test() { base[2] = 5; return base[2]; }
    }
    unsafe class App46
    {
        int delegateTotal;
        void First(int value) { delegateTotal = delegateTotal * 10 + value; }
        void Second(int value) { delegateTotal = delegateTotal * 10 + value + 1; }
        int TestDelegates()
        {
            System.Action<int> handlers = null;
            handlers += First;
            handlers += Second;
            handlers += First;
            handlers -= First;
            handlers(2);
            return delegateTotal;
        }
        static int AcceptUInt(uint value) { return value == 0u ? 1 : 0; }
        static int TestNullableCast()
        {
            float? value = 3.75f;
            int result = (int)value + (int)System.Math.Ceiling(3.25f);
            float? missing = null;
            try { float invalid = (float)missing; }
            catch (System.InvalidOperationException) { result += 10; }
            return result;
        }
        public static int Run()
        {
            int[] values = new int[] { 1, 2, 3 };
            fixed (int* pointer = values)
            {
                pointer[0] = 7;
                pointer[1] = pointer[0] + 5;
            }
            return values[0] + values[1] + AcceptUInt(0) + Defaults.Pick() + new App46().TestDelegates() + TestNullableCast()
                + GenericProbe<int>.Kind(3) + GenericProbe<string>.Kind("x") + GenericProbe<string>.IsNull(null)
                + (GenericProbe<string>.AsString("x") != null ? 2 : 0) + (GenericProbe<int>.AsString(3) == null ? 4 : 0)
                + new DerivedIndex46().Test() + (System.Math.Abs(-2.5f) == 2.5f ? 8 : 0)
                + (double.PositiveInfinity > 0.0 && double.NegativeInfinity < 0.0 && double.NaN != double.NaN ? 16 : 0)
                + (System.Math.Abs(-3.5) == 3.5 && System.Math.Max(2.0, 5.0) == 5.0
                    && System.Math.Sign(-9.0) == -1 && System.Math.Sign(0.0) == 0 ? 32 : 0)
                + ("Cubic:test".StartsWith("Cubic:", System.StringComparison.Ordinal)
                    && "^name".StartsWith('^') && "file.xml".EndsWith(".xml")
                    && "a:b".IndexOf(":", System.StringComparison.Ordinal) == 1 ? 64 : 0)
                + ($"{1.5f},{2.25}" == "1.5,2.25" && System.MathF.Cos(0) == 1 && System.MathF.Sin(0) == 0 && System.MathF.Sqrt(9) == 3 ? 128 : 0);
        }
    }
}
