using System;
using ParityKit;

// 89: sayi/metin formatlama paritesi — ToString ozel/standart bicimler, hizalama, shortest-roundtrip double, enum/bool/char.
namespace Demo89
{
    enum Color { Red = 1, Green = 2, Blue = 4 }
    [Flags] enum Perm : byte { None = 0, R = 1, W = 2, X = 4, RW = 3 }
    enum Big : long { A = long.MinValue, B = -1, C = 0, D = long.MaxValue }
    enum Neg { M = -5, Z = 0, P = 5 }

    class App89
    {
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static int I(int v) { return v; }
        static long L(long v) { return v; }
        static double D(double v) { return v; }
        static float F(float v) { return v; }

        public static int Run() { return Par.Loop(89, 24, Step); }

        static string Step(int k)
        {
            switch (k)
            {
                case 1: return IntToString();
                case 2: return HexFormat();
                case 3: return DecimalPadding();
                case 4: return FixedFormat();
                case 5: return ExpFormat();
                case 6: return GeneralFormat();
                case 7: return NumberFormat();
                case 8: return PercentFormat();
                case 9: return Alignment();
                case 10: return DoubleShortest();
                case 11: return DoubleLarge();
                case 12: return FloatShortest();
                case 13: return SpecialValues();
                case 14: return RoundTripR();
                case 15: return FixedMidpoint();
                case 16: return CustomFormat();
                case 17: return CompositeFormat();
                case 18: return EnumToString();
                case 19: return FlagsToString();
                case 20: return BoolCharString();
                case 21: return Interpolation();
                case 22: return FormatProvider();
                case 23: return UnsignedFormat();
                case 24: return StringBuilderAppendFormat();
            }
            return "?";
        }

        static string IntToString()
        {
            return Par.J(Q(() => I(0)), Q(() => I(-0)), Q(() => I(42)), Q(() => I(-42)), Q(() => I(int.MinValue)), Q(() => I(int.MaxValue)), Q(() => L(long.MinValue)), Q(() => L(long.MaxValue)), Q(() => (short)I(-32768)), Q(() => (byte)I(255)), Q(() => (sbyte)I(-128)), Q(() => (ushort)I(65535)), Q(() => uint.MaxValue), Q(() => ulong.MaxValue), Q(() => I(1000000)), Q(() => I(-1000000)),
                Q(() => I(7).ToString()), Q(() => I(-7).ToString()), Q(() => I(int.MinValue).ToString()), Q(() => L(-1).ToString()), Q(() => ((object)I(5)).ToString()), Q(() => I(123).ToString("")), Q(() => I(123).ToString((string)null)), Q(() => I(123).ToString("G")), Q(() => I(-123).ToString("D")), Q(() => I(123).ToString("g")), Q(() => I(123).ToString("d")), Q(() => I(0).ToString("D")));
        }

        static string HexFormat()
        {
            return Par.J(Q(() => I(255).ToString("X")), Q(() => I(255).ToString("x")), Q(() => I(255).ToString("X2")), Q(() => I(255).ToString("X8")), Q(() => I(-1).ToString("X")), Q(() => I(-1).ToString("x8")), Q(() => I(int.MinValue).ToString("X")), Q(() => I(0).ToString("X")), Q(() => I(0).ToString("X4")), Q(() => I(10).ToString("x2")), Q(() => I(4095).ToString("X3")), Q(() => I(4096).ToString("X3")),
                Q(() => L(-1).ToString("X")), Q(() => L(255).ToString("X16")), Q(() => ((byte)I(10)).ToString("X2")), Q(() => ((byte)I(255)).ToString("x")), Q(() => ((sbyte)I(-1)).ToString("X")), Q(() => ((short)I(-1)).ToString("X")), Q(() => ((ushort)I(65535)).ToString("x4")), Q(() => ((uint)I(-1)).ToString("X")), Q(() => ulong.MaxValue.ToString("X")), Q(() => I(1).ToString("X0")), Q(() => I(0xABCDEF).ToString("x")), Q(() => I(0xABCDEF).ToString("X9")), Q(() => I(-255).ToString("x")), Q(() => I(255).ToString("X02")),
                Q(() => I(255).ToString("B")), Q(() => I(5).ToString("b8")), Q(() => I(-1).ToString("B")), Q(() => ((byte)I(5)).ToString("B8")), Q(() => I(0).ToString("b")));
        }

        static string DecimalPadding()
        {
            return Par.J(Q(() => I(5).ToString("D")), Q(() => I(5).ToString("D3")), Q(() => I(-5).ToString("D3")), Q(() => I(12345).ToString("D3")), Q(() => I(0).ToString("D5")), Q(() => I(int.MinValue).ToString("D12")), Q(() => L(42).ToString("D20")), Q(() => ((byte)I(7)).ToString("D2")), Q(() => I(5).ToString("d05")), Q(() => I(-5).ToString("D0")), Q(() => I(5).ToString("D00")), Q(() => I(123).ToString("D2")), Q(() => uint.MaxValue.ToString("D12")), Q(() => I(5).ToString("D99").Length));
        }

        static string FixedFormat()
        {
            return Par.J(Q(() => D(1.5).ToString("F")), Q(() => D(1.5).ToString("F0")), Q(() => D(1.5).ToString("F1")), Q(() => D(1.5).ToString("F3")), Q(() => D(0.125).ToString("F2")), Q(() => D(2.5).ToString("F0")), Q(() => D(3.5).ToString("F0")), Q(() => D(-2.5).ToString("F0")), Q(() => D(0.5).ToString("F0")), Q(() => D(1.005).ToString("F2")), Q(() => D(2.675).ToString("F2")), Q(() => D(123456789.987654321).ToString("F4")), Q(() => D(1e20).ToString("F2")), Q(() => D(1e-10).ToString("F5")), Q(() => D(-0.0001).ToString("F2")), Q(() => D(-0.004).ToString("F2")), Q(() => D(-0.005).ToString("F2")), Q(() => D(-0.006).ToString("F2")),
                Q(() => I(5).ToString("F2")), Q(() => I(-5).ToString("F0")), Q(() => L(long.MaxValue).ToString("F1")), Q(() => F(1.5f).ToString("F3")), Q(() => F(0.1f).ToString("F10")), Q(() => D(0.1).ToString("F20")), Q(() => D(1.0 / 3).ToString("F17")), Q(() => D(double.MaxValue).ToString("F0").Length), Q(() => D(0.95).ToString("F1")), Q(() => D(0.05).ToString("F1")), Q(() => D(0.15).ToString("F1")), Q(() => D(0.25).ToString("F1")), Q(() => D(0.35).ToString("F1")), Q(() => D(0.45).ToString("F1")), Q(() => D(1e15 + 0.5).ToString("F0")), Q(() => D(4503599627370497.0).ToString("F0")), Q(() => D(99.995).ToString("F2")), Q(() => D(9.995).ToString("F2")));
        }

        static string ExpFormat()
        {
            return Par.J(Q(() => D(12345.6789).ToString("E")), Q(() => D(12345.6789).ToString("E2")), Q(() => D(12345.6789).ToString("e3")), Q(() => D(0.000123).ToString("E1")), Q(() => D(0).ToString("E")), Q(() => D(-1).ToString("E0")), Q(() => D(1e100).ToString("E2")), Q(() => D(1e-100).ToString("E2")), Q(() => D(1.5).ToString("E0")), Q(() => D(2.5).ToString("E0")), Q(() => D(123456789).ToString("E3")), Q(() => I(12345).ToString("E2")), Q(() => F(1.5e10f).ToString("E2")), Q(() => D(9.9999).ToString("E2")), Q(() => D(9.995).ToString("E2")), Q(() => D(double.MaxValue).ToString("E")), Q(() => D(double.Epsilon).ToString("E")), Q(() => D(1e308).ToString("E16")), Q(() => D(123.456).ToString("E20")));
        }

        static string GeneralFormat()
        {
            return Par.J(Q(() => D(12345.6789).ToString("G")), Q(() => D(12345.6789).ToString("G2")), Q(() => D(12345.6789).ToString("G5")), Q(() => D(12345.6789).ToString("G10")), Q(() => D(0.000012345).ToString("G3")), Q(() => D(0.0001).ToString("G")), Q(() => D(0.00001).ToString("G")), Q(() => D(1e15).ToString("G")), Q(() => D(1e16).ToString("G")), Q(() => D(123456789012345680000.0).ToString("G")), Q(() => D(1e-5).ToString("G17")), Q(() => D(0.1).ToString("G17")), Q(() => D(0.1).ToString("G15")), Q(() => D(1.0 / 3).ToString("G")), Q(() => D(1.0 / 3).ToString("G3")), Q(() => D(100).ToString("G")), Q(() => D(100).ToString("G1")), Q(() => D(100).ToString("G2")), Q(() => D(150).ToString("G2")), Q(() => D(250).ToString("G2")), Q(() => D(1234.5).ToString("G4")), Q(() => D(1235.5).ToString("G4")),
                Q(() => I(12345).ToString("G3")), Q(() => I(12345).ToString("G10")), Q(() => L(123456789012).ToString("G5")), Q(() => F(1.5f).ToString("G")), Q(() => F(0.1f).ToString("G9")), Q(() => F(0.1f).ToString("G7")), Q(() => F(16777216f).ToString("G")), Q(() => F(1e10f).ToString("G")), Q(() => D(1e21).ToString("G")), Q(() => D(1e20).ToString("G")), Q(() => D(0.0000001).ToString("G")), Q(() => D(123456.7).ToString("G6")), Q(() => D(123456.7).ToString("G7")), Q(() => D(0.000001234).ToString("G2")));
        }

        static string NumberFormat()
        {
            return Par.J(Q(() => D(1234567.891).ToString("N")), Q(() => D(1234567.891).ToString("N0")), Q(() => D(1234567.891).ToString("N1")), Q(() => D(-1234567.891).ToString("N2")), Q(() => D(0.5).ToString("N0")), Q(() => D(1.5).ToString("N0")), Q(() => D(2.5).ToString("N0")), Q(() => D(999.999).ToString("N2")), Q(() => D(1e20).ToString("N0")), Q(() => I(1234567).ToString("N")), Q(() => I(1234567).ToString("N0")), Q(() => I(-1234567).ToString("N0")), Q(() => I(0).ToString("N")), Q(() => I(999).ToString("N0")), Q(() => I(1000).ToString("N0")), Q(() => L(long.MinValue).ToString("N0")), Q(() => ulong.MaxValue.ToString("N0")), Q(() => D(0.001).ToString("N2")), Q(() => D(-0.001).ToString("N2")), Q(() => D(-0.0).ToString("N2")), Q(() => F(1234.5f).ToString("N1")), Q(() => D(123).ToString("N5")));
        }

        static string PercentFormat()
        {
            return Par.J(Q(() => D(0.1234).ToString("P")), Q(() => D(0.1234).ToString("P0")), Q(() => D(0.1234).ToString("P1")), Q(() => D(1).ToString("P")), Q(() => D(-0.5).ToString("P0")), Q(() => D(0.005).ToString("P0")), Q(() => D(0.015).ToString("P0")), Q(() => D(0.025).ToString("P0")), Q(() => I(1).ToString("P")), Q(() => D(12.3456).ToString("P3")), Q(() => D(0).ToString("P")),
                Q(() => I(1234).ToString("C")), Q(() => D(1234.567).ToString("C")), Q(() => D(-1234.567).ToString("C0")), Q(() => D(0.5).ToString("C")), Q(() => D(1234.5).ToString("C3")));
        }

        static string Alignment()
        {
            return Par.J(Q(() => string.Format("[{0,5}]", I(42))), Q(() => string.Format("[{0,-5}]", I(42))), Q(() => string.Format("[{0,2}]", I(12345))), Q(() => string.Format("[{0,5:D3}]", I(7))), Q(() => string.Format("[{0,-8:F2}]", D(3.14159))), Q(() => string.Format("[{0,8:F2}]", D(-3.14159))), Q(() => string.Format("[{0,10:X8}]", I(255))), Q(() => string.Format("[{0,5}]", "ab")), Q(() => string.Format("[{0,-5}]", "ab")), Q(() => string.Format("[{0,5}]", (string)null)), Q(() => string.Format("[{0,0}]", I(1))), Q(() => string.Format("[{0,1}]", "")), Q(() => string.Format("[{0,5}]", 'c')), Q(() => string.Format("[{0,6}]", true)), Q(() => string.Format("[{0,-6}]", Color.Red)), Q(() => string.Format("[{0,20}]", D(1.0 / 3))), Q(() => string.Format("[{0,5:F1}]", F(2.25f))), Q(() => string.Format("[{0,5:F1}]", F(2.35f))), Q(() => string.Format("[{0,-3}][{1,3}]", I(1), I(2))), Par.T(() => string.Format("[{0,+5}]", I(42))));
        }

        static string DoubleShortest()
        {
            double[] xs = { 0.1, 0.2, 0.3, 0.1 + 0.2, 1.0 / 3, 2.0 / 3, 1.0, 100.0, 123.456, 0.000001, 0.0000001, 1e-7, 1.5e-7, 123456789.0, 1234567890123456.0, 12345678901234567.0, 0.1 * 3, 1.1 * 1.1, 4.35, 4.345, 1e-5, 1e-4, 1e-3, 9.999999999999999e-5, 0.30000000000000004, 1.0000000000000002, 0.9999999999999999, 5e-324, 1.7976931348623157e308, 2.2250738585072014e-308, 2.225073858507201e-308 };
            var sb = new System.Text.StringBuilder();
            foreach (var x0 in xs) { double x = D(x0); sb.Append(x.ToString()).Append(';'); }
            return sb.ToString();
        }

        static string DoubleLarge()
        {
            double[] xs = { 1e14, 1e15, 1e16, 1e17, 1e20, 1e21, 1e22, 1e23, 1e100, 1e300, 123456789012345680000.0, 9007199254740992.0, 9007199254740993.0, 1e15 + 0.3, 1e16 + 2, 4503599627370497.0, -1e15, -1e16, -1e21, 12345678.9, 123456789.1, 1234567890.12, 100000000000000000000.0, 999999999999999900000.0, 1e15 * 10, 0.5e16 };
            var sb = new System.Text.StringBuilder();
            foreach (var x0 in xs) { double x = D(x0); sb.Append(x.ToString()).Append(',').Append(x.ToString("R")).Append(';'); }
            return sb.ToString();
        }

        static string FloatShortest()
        {
            float[] xs = { 0.1f, 0.2f, 0.3f, 0.1f + 0.2f, 1f / 3f, 2f / 3f, 1f, 100f, 123.456f, 1e-7f, 16777216f, 16777217f, 3.14159274f, (float)Math.PI, 0.1f * 3f, 1.1f * 1.1f, 1e10f, 1e-10f, 3.4028235e38f, 1.4e-45f, 1e7f, 1e8f, 12345678f, 123456792f, 0.00001f, 0.000001f, 33554432f, 0.3f - 0.1f, 1.0000001f, 0.99999994f };
            var sb = new System.Text.StringBuilder();
            foreach (var x0 in xs) { float x = F(x0); sb.Append(x.ToString()).Append(',').Append(x.ToString("R")).Append(',').Append(((double)x).ToString()).Append(';'); }
            return sb.ToString();
        }

        static string SpecialValues()
        {
            double nan = D(double.NaN), pinf = D(double.PositiveInfinity), ninf = D(double.NegativeInfinity), nz = -D(0.0);
            float fnan = F(float.NaN), fpinf = F(float.PositiveInfinity);
            return Par.J(Q(() => nan), Q(() => pinf), Q(() => ninf), Q(() => nz), Q(() => nan.ToString("F2")), Q(() => pinf.ToString("E")), Q(() => ninf.ToString("N")), Q(() => nz.ToString("F0")), Q(() => nz.ToString("E0")), Q(() => nz.ToString("G")), Q(() => nz.ToString("N0")), Q(() => nz.ToString("P")), Q(() => nan.ToString("R")), Q(() => pinf.ToString("G17")), Q(() => fnan), Q(() => fpinf), Q(() => -fpinf), Q(() => (-F(0f)).ToString()), Q(() => (-nan).ToString()), Q(() => D(0.0 / 0.0).ToString()), Q(() => string.Format("{0}|{1}|{2}", nan, pinf, nz)), Q(() => string.Format("{0,6}|{1,-10}|", nan, pinf)), Q(() => D(double.MaxValue).ToString()), Q(() => D(double.MinValue).ToString()), Q(() => D(double.Epsilon).ToString()), Q(() => F(float.Epsilon).ToString()), Q(() => F(float.MaxValue).ToString()), Q(() => D(-double.Epsilon).ToString()));
        }

        static string RoundTripR()
        {
            double[] xs = { 0.1, 1.0 / 3, Math.PI, Math.E, 1e23, 5e-324, 0.30000000000000004, 1.0000000000000002, 9007199254740993.0, 123456789012345678.0 };
            var sb = new System.Text.StringBuilder();
            foreach (var x0 in xs)
            {
                double x = D(x0);
                string r = x.ToString("R"), g17 = x.ToString("G17"), def = x.ToString();
                bool rt = double.Parse(r) == x, rt17 = double.Parse(g17) == x, rtd = double.Parse(def) == x;
                sb.Append(r).Append(',').Append(g17).Append(',').Append(rt).Append(rt17).Append(rtd).Append(';');
            }
            float f = F(0.1f); sb.Append(f.ToString("R")).Append(',').Append(f.ToString("G9")).Append(',').Append(float.Parse(f.ToString()) == f).Append(',').Append(float.Parse(f.ToString("G9")) == f);
            return sb.ToString();
        }

        static string FixedMidpoint()
        {
            // F biciminde tam temsil edilen orta noktalarin yuvarlanmasi (0.125, 0.375 tam; 2.675 degil)
            double[] xs = { 0.125, 0.375, 0.625, 0.875, -0.125, 2.675, 2.665, 1.005, 1.015, 1.025, 0.5, 1.5, 2.5, -0.5, -1.5, 1234.5, 0.0005, 0.0015, 0.0025, 1e-7 };
            var sb = new System.Text.StringBuilder();
            foreach (var x0 in xs) { double x = D(x0); sb.Append(x.ToString("F2")).Append(',').Append(x.ToString("F0")).Append(',').Append(x.ToString("F3")).Append(',').Append(x.ToString("N2")).Append(';'); }
            float[] fs = { 0.125f, 2.675f, 1.005f, 0.5f, 2.5f, 1.15f, 1.25f, 1.35f };
            foreach (var f0 in fs) { float f = F(f0); sb.Append(f.ToString("F2")).Append(',').Append(f.ToString("F1")).Append(',').Append(f.ToString("F0")).Append(';'); }
            return sb.ToString();
        }

        static string CustomFormat()
        {
            double x = D(1234.5678), y = D(0.5), z = D(0), neg = D(-1234.5678), small = D(0.00123);
            return Par.J(Q(() => x.ToString("0.##")), Q(() => x.ToString("0.00")), Q(() => x.ToString("#,##0.00")), Q(() => x.ToString("#,##0")), Q(() => x.ToString("0000000.000")), Q(() => y.ToString("0.##")), Q(() => y.ToString("#.##")), Q(() => z.ToString("#.##")), Q(() => z.ToString("0.##")), Q(() => z.ToString("#")), Q(() => neg.ToString("#,##0.00;(#,##0.00)")), Q(() => x.ToString("#,##0.00;(#,##0.00);zero")), Q(() => z.ToString("#,##0.00;(#,##0.00);zero")), Q(() => x.ToString("0.0e+00")), Q(() => x.ToString("0.00E+0")), Q(() => small.ToString("0.00%")), Q(() => small.ToString("0.0‰")), Q(() => x.ToString("#,,")), Q(() => D(1234567).ToString("#,##0,")), Q(() => D(1234567).ToString("#,##0,,")), Q(() => x.ToString("'x='0.0")), Q(() => x.ToString("0.0 'TL'")), Q(() => x.ToString(@"\#0.0")), Q(() => I(5).ToString("000")), Q(() => I(5).ToString("#")), Q(() => I(0).ToString("#")), Q(() => I(-5).ToString("000")), Q(() => I(12345).ToString("0,0")), Q(() => I(42).ToString("00-00")), Q(() => I(1234567).ToString("(###) ###-####")), Q(() => x.ToString("0")), Q(() => D(0.5).ToString("0")), Q(() => D(1.5).ToString("0")), Q(() => D(2.5).ToString("0")), Q(() => D(0.125).ToString("0.00")), Q(() => D(2.675).ToString("0.00")), Q(() => x.ToString("##.###")), Q(() => D(123).ToString("0.0#")), Q(() => L(12345678901234).ToString("#,##0")));
        }

        static string CompositeFormat()
        {
            return Par.J(Q(() => string.Format("{0} {1} {0}", I(1), I(2))), Q(() => string.Format("{{{0}}}", I(5))), Q(() => string.Format("{{}}")), Q(() => string.Format("{0}{{{1}}}", "a", "b")), Q(() => string.Format("{0}", (object)null)), Q(() => string.Format("{0}|{1}", null, "x")), Q(() => string.Format("{0:D3}", (object)null)), Q(() => string.Format("{0,5}", (object)null)), Q(() => string.Format("[{0}]", "")), Q(() => string.Format("{0}", new object[] { null })), Q(() => string.Format("{0} {1} {2} {3}", 1, 2, 3, 4)), Q(() => string.Format("{3}{2}{1}{0}", "a", "b", "c", "d")), Q(() => string.Format("{0:x}{0:X}", I(255))), Q(() => string.Format("{0:0.0}|{0:F2}|{0,8:E1}", D(3.14159))), Q(() => string.Format("{0}", Color.Green)), Q(() => string.Format("{0:D}", Color.Green)), Q(() => string.Format("{0:X}", Color.Green)), Q(() => string.Format("{0:G}", Color.Green)), Q(() => string.Format("{0:F}", Color.Green)), Q(() => string.Format("{0}", (Color)7)), Q(() => string.Format("{0:D}", (Color)7)), Q(() => string.Format("{0}", 'c')), Q(() => string.Format("{0}", true)), Q(() => string.Format("{0}", (object)I(5))), Q(() => string.Format("{0}", new int[0])), Q(() => string.Format("{0}", (object)D(0.1))),
                Par.T(() => string.Format("{1}", I(0))), Par.T(() => string.Format("{0", I(0))), Par.T(() => string.Format("{", I(0))), Par.T(() => string.Format("}", I(0))), Par.T(() => string.Format(null, I(0))), Par.T(() => string.Format("{0}", (object[])null)), Par.T(() => string.Format("{-1}", I(0))), Par.T(() => string.Format("{0:}", I(0))), Par.T(() => string.Format("{ 0}", I(0))), Par.T(() => string.Format("{0 }", I(0))), Par.T(() => string.Format("{0,}", I(0))), Q(() => string.Format("{0,-0}", I(0))));
        }

        static string EnumToString()
        {
            return Par.J(Q(() => Color.Red), Q(() => Color.Blue), Q(() => (Color)I(0)), Q(() => (Color)I(3)), Q(() => (Color)I(7)), Q(() => (Color)I(-1)), Q(() => (Color)I(256)), Q(() => Color.Red.ToString("D")), Q(() => Color.Red.ToString("X")), Q(() => Color.Red.ToString("G")), Q(() => Color.Red.ToString("F")), Q(() => ((Color)I(3)).ToString("F")), Q(() => ((Color)I(3)).ToString("G")), Q(() => ((Color)I(5)).ToString("F")), Q(() => ((Color)I(8)).ToString("F")), Q(() => Big.A), Q(() => Big.B), Q(() => Big.D), Q(() => (Big)L(5)), Q(() => Big.A.ToString("D")), Q(() => Big.A.ToString("X")), Q(() => Neg.M), Q(() => (Neg)I(-5)), Q(() => (Neg)I(-6)), Q(() => Neg.M.ToString("D")), Q(() => Neg.M.ToString("X")), Q(() => ((Neg)I(5)).ToString()), Q(() => Perm.None.ToString("D")), Q(() => Perm.None.ToString("X")), Q(() => Perm.X.ToString("X")), Par.T(() => Color.Red.ToString("Q")), Q(() => Color.Red.ToString("")), Q(() => Color.Red.ToString((string)null)), Q(() => ((object)Color.Red).ToString()), Q(() => Color.Red.GetType().Name), Q(() => Color.Red.GetTypeCode()), Q(() => Big.A.GetTypeCode()), Q(() => Convert.ToInt32(Color.Blue)), Q(() => (int)(Color)I(99)));
        }

        static string FlagsToString()
        {
            return Par.J(Q(() => Perm.None), Q(() => Perm.R), Q(() => Perm.RW), Q(() => Perm.R | Perm.W), Q(() => Perm.R | Perm.X), Q(() => Perm.R | Perm.W | Perm.X), Q(() => (Perm)I(8)), Q(() => (Perm)I(9)), Q(() => (Perm)I(15)), Q(() => (Perm)I(255)), Q(() => Perm.RW | Perm.X), Q(() => (Perm)I(0)), Q(() => (Perm)(I(3))), Q(() => Perm.R.ToString("F")), Q(() => Perm.R.ToString("D")), Q(() => (Perm.R | Perm.X).ToString("D")), Q(() => (Perm.R | Perm.X).ToString("X")), Q(() => Perm.RW.HasFlag(Perm.R)), Q(() => Perm.R.HasFlag(Perm.None)), Q(() => Perm.None.HasFlag(Perm.None)), Q(() => Perm.R.HasFlag(Perm.RW)), Q(() => (Perm.R | Perm.X).HasFlag(Perm.R | Perm.X)), Q(() => Color.Red.HasFlag(Color.Red)), Q(() => ((Color)I(3)).HasFlag(Color.Green)), Q(() => (Color)I(3) | Color.Blue), Q(() => Perm.R & Perm.RW), Q(() => Perm.RW ^ Perm.R), Q(() => ~Perm.R), Q(() => (~Perm.R).ToString("D")), Q(() => ~Color.Red), Q(() => (~Color.Red).ToString("D")), Q(() => Perm.R == (Perm)I(1)), Q(() => Perm.RW.Equals(Perm.R | Perm.W)), Q(() => Perm.R.CompareTo(Perm.W)), Q(() => Perm.X.CompareTo(Perm.R)), Par.T(() => Perm.R.CompareTo(Color.Red).ToString()), Par.T(() => Perm.R.HasFlag(Color.Red).ToString()));
        }

        static string BoolCharString()
        {
            bool t = I(1) == 1, f = I(1) == 2;
            char c = (char)I(65), nul = (char)I(0), sur = (char)I(0xD83D), tab = (char)I(9);
            return Par.J(Q(() => t), Q(() => f), Q(() => t.ToString()), Q(() => f.ToString()), Q(() => ((object)t).ToString()), Q(() => t.ToString().Length), Q(() => string.Format("{0}", f)), Q(() => c), Q(() => c.ToString()), Q(() => nul.ToString().Length), Q(() => sur.ToString().Length), Q(() => tab.ToString() == "\t"), Q(() => c.ToString() + c), Q(() => c + c.ToString()), Q(() => c + c), Q(() => (object)c), Q(() => string.Concat(c, c)), Q(() => new string(c, 3)), Q(() => new string(c, 0).Length), Par.T(() => new string(c, -1)), Q(() => char.ToString(c)), Q(() => c.ToString(null)), Q(() => ((IConvertible)c).ToInt32(null)), Q(() => ((IConvertible)t).ToInt32(null)), Q(() => Convert.ToString(t)), Q(() => Convert.ToString(c)), Q(() => t.CompareTo(f)), Q(() => f.CompareTo(t)), Q(() => t.Equals(true)), Q(() => t.GetHashCode()), Q(() => bool.TrueString), Q(() => bool.FalseString), Q(() => t & f), Q(() => t | f), Q(() => t ^ f), Q(() => !t), Q(() => t ? 1 : 0), Q(() => c.CompareTo('B')), Q(() => 'B'.CompareTo(c)), Q(() => c.Equals('A')), Q(() => c.Equals((object)65)));
        }

        static string Interpolation()
        {
            int i = I(42); double d = D(3.14159); string s = "ab"; object o = null; Color c = Color.Blue; bool b = I(1) == 1; float f = F(2.5f); long l = L(-7); char ch = (char)I(88); int? n = null; int? n2 = I(5);
            return Par.J(Q(() => $"{i}"), Q(() => $"{i,5}"), Q(() => $"{i,-5}|"), Q(() => $"{i:D5}"), Q(() => $"{i,8:X4}"), Q(() => $"{d}"), Q(() => $"{d:F2}"), Q(() => $"{d,10:F3}"), Q(() => $"{d:0.0}"), Q(() => $"{d:E2}"), Q(() => $"{s}"), Q(() => $"{s,4}"), Q(() => $"{o}"), Q(() => $"{c}"), Q(() => $"{c:D}"), Q(() => $"{b}"), Q(() => $"{f}"), Q(() => $"{f:F1}"), Q(() => $"{l}"), Q(() => $"{ch}"), Q(() => $"{n}"), Q(() => $"{n2}"), Q(() => $"{n2:D3}"), Q(() => $"{{{i}}}"), Q(() => $"{i}{d}{s}"), Q(() => $"{(i > 40 ? "big" : "small")}"), Q(() => $"{i + 1}"), Q(() => $"{d * 2:F1}"), Q(() => $"{s.Length}"), Q(() => $"{s[0]}"), Q(() => $"{(object)i}"), Q(() => $"{i:C}"), Q(() => $"{i:P}"), Q(() => $"{i:N}"), Q(() => $"{i:E}"), Q(() => $"{d:G3}"), Q(() => $"{d:R}"), Q(() => $"{d:#.###}"), Q(() => $"{i,0}"), Q(() => $"{1.5:F0}"), Q(() => $"{2.5:F0}"), Q(() => $"{0.125:F2}"), Q(() => $"a{i}b{d:F1}c{s,3}d"), Q(() => $"{'x'}"), Q(() => $"{"lit"}"), Q(() => $"{(string)null ?? "nul"}"), Q(() => $"{i,-10}|{d,10:F2}|{s,-4}|{b,6}"), Q(() => $"{long.MinValue}"), Q(() => $"{ulong.MaxValue:X}"), Q(() => $"{double.NaN}"), Q(() => $"{-0.0}"), Q(() => $"{float.Epsilon}"));
        }

        static string FormatProvider()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var nfi = System.Globalization.NumberFormatInfo.InvariantInfo;
            return Par.J(Q(() => D(1234.5).ToString(inv)), Q(() => D(1234.5).ToString("N2", inv)), Q(() => D(1234.5).ToString("F2", nfi)), Q(() => I(1234).ToString(inv)), Q(() => I(1234).ToString("N0", inv)), Q(() => string.Format(inv, "{0:F2}", D(1.5))), Q(() => string.Format(inv, "{0}", D(0.1))), Q(() => D(1234.5).ToString("C", inv)), Q(() => D(0.5).ToString("P", inv)), Q(() => D(1234.5).ToString(null, inv)), Q(() => D(1234.5).ToString("", inv)), Q(() => Convert.ToString(D(1.5), inv)), Q(() => Convert.ToString(I(5), inv)), Q(() => D(1.5).ToString(System.Globalization.CultureInfo.CurrentCulture)), Q(() => System.Globalization.CultureInfo.CurrentCulture.Name == "" || System.Globalization.CultureInfo.CurrentCulture.Name.Length > 0), Q(() => nfi.NumberDecimalSeparator), Q(() => nfi.NumberGroupSeparator), Q(() => nfi.NaNSymbol), Q(() => nfi.PositiveInfinitySymbol), Q(() => nfi.NegativeInfinitySymbol), Q(() => nfi.NegativeSign), Q(() => nfi.CurrencySymbol), Q(() => nfi.PercentSymbol), Q(() => nfi.NumberDecimalDigits), Q(() => nfi.CurrencyDecimalDigits), Q(() => nfi.PercentDecimalDigits), Q(() => nfi.NumberNegativePattern), Q(() => nfi.CurrencyNegativePattern), Q(() => nfi.PercentPositivePattern));
        }

        static string UnsignedFormat()
        {
            uint u = (uint)I(-1); ulong ul = (ulong)L(-1); ushort us = (ushort)I(-1); byte b = (byte)I(-1);
            return Par.J(Q(() => u), Q(() => ul), Q(() => us), Q(() => b), Q(() => u.ToString("D")), Q(() => u.ToString("N0")), Q(() => ul.ToString("E3")), Q(() => us.ToString("X")), Q(() => b.ToString("D5")), Q(() => u.ToString("F1")), Q(() => ul.ToString("G5")), Q(() => u.ToString("P0")), Q(() => b.ToString("B")), Q(() => us.ToString("b")), Q(() => ((sbyte)I(-1)).ToString("D")), Q(() => ((sbyte)I(-1)).ToString("X")), Q(() => ((sbyte)I(-1)).ToString("B")), Q(() => ((short)I(-1)).ToString("X")), Q(() => ((short)I(-1)).ToString("B")), Q(() => I(-1).ToString("B")), Q(() => L(-1).ToString("B").Length), Q(() => L(-1).ToString("X")), Q(() => L(long.MinValue).ToString("X")), Q(() => L(long.MinValue).ToString("B").Length), Q(() => u.ToString("X0")), Q(() => ul.ToString("x1")), Q(() => string.Format("{0,12}|{1,-12}|", u, b)), Q(() => $"{u:X}|{ul:x}|{us:D8}|{b:X2}"));
        }

        static string StringBuilderAppendFormat()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendFormat("{0}-{1}", I(1), I(2)).Append('|').AppendFormat("{0:X2}", I(255)).Append('|').AppendFormat("{0,5}|{1,-5}|", "a", "b").AppendFormat("{{{0}}}", I(3)).Append('|').AppendFormat("{0}", (object)null).Append('|').AppendFormat("{0:F2}", D(1.005)).Append('|').AppendFormat("{0}{1}{2}{3}{4}", 1, 2, 3, 4, 5).Append('|').AppendFormat("{0:D3}", Color.Red).Append('|');
            sb.Append(D(0.1)).Append('|').Append(F(0.1f)).Append('|').Append(I(-5)).Append('|').Append(L(long.MinValue)).Append('|').Append(true).Append('|').Append((char)I(65)).Append('|').Append((object)null).Append('|').Append((string)null).Append('|').Append(Color.Blue).Append('|').Append(D(double.NaN)).Append('|').Append((byte)I(7)).Append('|').Append((short)I(-7)).Append('|').Append(uint.MaxValue).Append('|').Append(ulong.MaxValue).Append('|').Append((sbyte)I(-1)).Append('|').Append((ushort)I(1)).Append('|').Append(D(1e21)).Append('|').Append(F(1e-7f)).Append('|').Append(1.5m).Append('|').Append((object)I(9)).Append('|').Append(new char[] { 'x', 'y' }).Append('|').Append("abc", 1, 2).Append('|').Append('z', 3).Append('|').Append(new char[] { 'p', 'q', 'r' }, 1, 2);
            return sb.ToString();
        }
    }
}
