using System;
using ParityKit;

// 88: float semantigi (NaN, -0.0, inf) + Math/MathF (banker's rounding, floor/ceil negatif, libm 6 hane).
namespace Demo88
{
    class App88
    {
        static double D(double v) { return v; }
        static float F(float v) { return v; }
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static int I(int v) { return v; }

        public static int Run() { return Par.Loop(88, 26, Step); }

        static string Step(int k)
        {
            switch (k)
            {
                case 1: return NaNCompare();
                case 2: return NaNEquals();
                case 3: return NegZero();
                case 4: return NegZeroMath();
                case 5: return Infinities();
                case 6: return RoundBankers();
                case 7: return RoundDigits();
                case 8: return RoundModes();
                case 9: return FloorCeilTrunc();
                case 10: return MathFBasics();
                case 11: return MathFVsMath();
                case 12: return SqrtExact();
                case 13: return LibmRounded();
                case 14: return PowEdges();
                case 15: return LogEdges();
                case 16: return DoubleMod();
                case 17: return MinMaxNaN();
                case 18: return ClampSign();
                case 19: return DivRemBigMul();
                case 20: return MiscMath();
                case 21: return FloatCompareTo();
                case 22: return Epsilons();
                case 23: return TrigExact();
                case 24: return IsChecks();
                case 25: return FloatHash();
                case 26: return BitIncrement();
            }
            return "?";
        }

        static string NaNCompare()
        {
            double n = D(double.NaN), one = D(1);
            float fn = F(float.NaN), fone = F(1f);
            return Par.J(Q(() => n == n), Q(() => n != n), Q(() => n < one), Q(() => n > one), Q(() => n <= one), Q(() => n >= one), Q(() => n == one), Q(() => n != one), Q(() => one < n), Q(() => one > n), Q(() => !(n < one)), Q(() => !(n >= one)),
                Q(() => fn == fn), Q(() => fn != fn), Q(() => fn < fone), Q(() => fn > fone), Q(() => fn <= fn), Q(() => fn >= fn), Q(() => n < n), Q(() => n > n));
        }

        static string NaNEquals()
        {
            double n = D(double.NaN), n2 = D(0.0) / D(0.0), neg = -D(double.NaN);
            float fn = F(float.NaN);
            object bn = n, bn2 = n2;
            return Par.J(Q(() => n.Equals(n)), Q(() => n.Equals(n2)), Q(() => n.Equals(neg)), Q(() => bn.Equals(bn2)), Q(() => object.Equals(bn, bn2)), Q(() => n.CompareTo(n2)), Q(() => n.CompareTo(D(1))), Q(() => D(1).CompareTo(n)), Q(() => n.CompareTo(D(double.NegativeInfinity))),
                Q(() => fn.Equals(fn)), Q(() => fn.CompareTo(F(0f))), Q(() => double.IsNaN(n2)), Q(() => double.IsNaN(neg)), Q(() => ((object)fn).Equals(fn)), Q(() => n.GetHashCode() == n2.GetHashCode()), Q(() => n.GetHashCode() == neg.GetHashCode()), Q(() => double.IsNaN(n * 0)), Q(() => double.IsNaN(D(double.PositiveInfinity) * 0)), Q(() => double.IsNaN(D(double.PositiveInfinity) - D(double.PositiveInfinity))));
        }

        static string NegZero()
        {
            double nz = -D(0.0), pz = D(0.0);
            float fnz = -F(0f), fpz = F(0f);
            return Par.J(Q(() => nz == pz), Q(() => nz.Equals(pz)), Q(() => nz.CompareTo(pz)), Q(() => pz.CompareTo(nz)), Q(() => nz.GetHashCode() == pz.GetHashCode()), Q(() => 1 / nz), Q(() => 1 / pz), Q(() => nz), Q(() => pz), Q(() => nz.ToString()), Q(() => (-pz).ToString()), Q(() => nz * -1), Q(() => nz + pz), Q(() => nz + nz), Q(() => nz - pz), Q(() => pz - pz), Q(() => nz * 5), Q(() => double.IsNegative(nz)), Q(() => double.IsNegative(pz)),
                Q(() => fnz == fpz), Q(() => fnz.ToString()), Q(() => 1 / fnz), Q(() => fnz.GetHashCode() == fpz.GetHashCode()), Q(() => (float)nz), Q(() => (double)fnz), Q(() => nz.ToString("R")), Q(() => nz.ToString("F2")), Q(() => nz.ToString("E2")), Q(() => nz.ToString("G")), Q(() => BitConverter.DoubleToInt64Bits(nz)), Q(() => BitConverter.DoubleToInt64Bits(pz)), Q(() => (int)nz), Q(() => (long)nz));
        }

        static string NegZeroMath()
        {
            double nz = -D(0.0), pz = D(0.0);
            return Par.J(Q(() => Math.Sign(nz)), Q(() => Math.Max(nz, pz)), Q(() => Math.Min(nz, pz)), Q(() => Math.Max(pz, nz)), Q(() => Math.Min(pz, nz)), Q(() => Math.Abs(nz)), Q(() => Math.Sqrt(nz)), Q(() => Math.Round(nz)), Q(() => Math.Round(D(-0.4))), Q(() => Math.Ceiling(D(-0.5))), Q(() => Math.Truncate(D(-0.5))), Q(() => Math.Floor(D(-0.0))),
                Q(() => Math.Round(D(-0.5))), Q(() => MathF.Round(F(-0.4f))), Q(() => MathF.Ceiling(F(-0.5f))), Q(() => Math.CopySign(D(3), nz)), Q(() => Math.CopySign(nz, D(-1))), Q(() => Math.Atan2(nz, D(-1))), Q(() => Math.Atan2(pz, D(-1))), Q(() => Math.Pow(nz, 1)), Q(() => Math.Pow(nz, -1)), Q(() => Math.Pow(nz, 2)), Q(() => nz % 1), Q(() => Math.IEEERemainder(nz, 1)), Q(() => -Math.Abs(pz)));
        }

        static string Infinities()
        {
            double pi = D(double.PositiveInfinity), ni = D(double.NegativeInfinity), mx = D(double.MaxValue), one = D(1), z = D(0);
            return Par.J(Q(() => pi - pi), Q(() => pi + ni), Q(() => pi * z), Q(() => pi / pi), Q(() => pi / one), Q(() => one / pi), Q(() => one / ni), Q(() => mx + 1 == mx), Q(() => mx * 2), Q(() => -mx * 2), Q(() => mx + mx), Q(() => pi > mx), Q(() => ni < -mx), Q(() => pi == pi), Q(() => pi.CompareTo(mx)), Q(() => one / z), Q(() => -one / z), Q(() => z / z), Q(() => pi.ToString()), Q(() => ni.ToString()),
                Q(() => double.IsInfinity(mx * 2)), Q(() => double.IsPositiveInfinity(pi)), Q(() => double.IsFinite(mx)), Q(() => double.IsFinite(pi)), Q(() => (float)pi), Q(() => (float)mx), Q(() => F(float.MaxValue) * 2), Q(() => pi % 2), Q(() => 2 % pi), Q(() => Math.Sqrt(pi)), Q(() => Math.Sqrt(ni)), Q(() => (int)pi == (int)ni), Q(() => pi.Equals(pi)));
        }

        static string RoundBankers()
        {
            double[] xs = { 0.5, 1.5, 2.5, 3.5, 4.5, -0.5, -1.5, -2.5, -3.5, 0.49999999999999994, 2.4999999999999996, 1e16 + 0.5, 1234567.5, -1234568.5, 0.5000000000000001 };
            var sb = new System.Text.StringBuilder();
            foreach (var x0 in xs) { double x = D(x0); sb.Append(Math.Round(x)).Append(',').Append(MathF.Round((float)x)).Append(';'); }
            return sb.ToString();
        }

        static string RoundDigits()
        {
            return Par.J(Q(() => Math.Round(D(2.675), 2)), Q(() => Math.Round(D(2.665), 2)), Q(() => Math.Round(D(1.005), 2)), Q(() => Math.Round(D(0.125), 2)), Q(() => Math.Round(D(0.375), 2)), Q(() => Math.Round(D(-0.125), 2)), Q(() => Math.Round(D(1.2345), 3)), Q(() => Math.Round(D(123.456), 0)), Q(() => Math.Round(D(123.456), 1)),
                Q(() => Math.Round(D(0.1 + 0.2), 15)), Q(() => Math.Round(D(0.1 + 0.2), 16)), Q(() => Math.Round(D(1e15 + 0.3), 1)), Q(() => Math.Round(D(5.5), 0)), Q(() => Math.Round(D(6.5), 0)), Q(() => Math.Round(D(2.5), 0)), Q(() => Math.Round(D(1234.5678), 2)), Q(() => Math.Round(D(-1234.5678), 2)), Q(() => Math.Round(D(0.045), 2)), Q(() => Math.Round(D(0.055), 2)),
                Q(() => Math.Round(D(1.95583), 2)), Q(() => Math.Round(D(1.98), 1)), Q(() => MathF.Round(F(2.675f), 2)), Q(() => MathF.Round(F(0.125f), 2)), Q(() => MathF.Round(F(1.005f), 2)), Par.T(() => Math.Round(D(1.5), 16).ToString()), Par.T(() => Math.Round(D(1.5), -1).ToString()));
        }

        static string RoundModes()
        {
            double[] xs = { 0.5, 1.5, 2.5, -0.5, -1.5, -2.5, 2.4, 2.6, -2.4, -2.6, 0.1, -0.1 };
            var sb = new System.Text.StringBuilder();
            foreach (var x0 in xs)
            {
                double x = D(x0);
                sb.Append(Math.Round(x, MidpointRounding.ToEven)).Append(',').Append(Math.Round(x, MidpointRounding.AwayFromZero)).Append(',')
                  .Append(Math.Round(x, MidpointRounding.ToZero)).Append(',').Append(Math.Round(x, MidpointRounding.ToNegativeInfinity)).Append(',').Append(Math.Round(x, MidpointRounding.ToPositiveInfinity)).Append(';');
            }
            sb.Append(Math.Round(D(2.675), 2, MidpointRounding.AwayFromZero)).Append(',').Append(Math.Round(D(0.125), 2, MidpointRounding.AwayFromZero)).Append(',').Append(MathF.Round(F(2.5f), MidpointRounding.AwayFromZero));
            return sb.ToString();
        }

        static string FloorCeilTrunc()
        {
            double[] xs = { 2.5, -2.5, 2.0000001, -2.0000001, -0.9, 0.9, -1e-300, 1e-300, 1e16 + 1, -1e16 - 1, 4503599627370496.5, -4503599627370495.5, 7.999999999999999, -7.999999999999999 };
            var sb = new System.Text.StringBuilder();
            foreach (var x0 in xs) { double x = D(x0); sb.Append(Math.Floor(x)).Append(',').Append(Math.Ceiling(x)).Append(',').Append(Math.Truncate(x)).Append(',').Append((int)x).Append(';'); }
            return sb.ToString();
        }

        static string MathFBasics()
        {
            float x = F(2.5f), y = F(-2.5f), z = F(7.1f);
            return Par.J(Q(() => MathF.Floor(x)), Q(() => MathF.Floor(y)), Q(() => MathF.Ceiling(y)), Q(() => MathF.Truncate(y)), Q(() => MathF.Round(x)), Q(() => MathF.Round(y)), Q(() => MathF.Round(F(3.5f))), Q(() => MathF.Abs(y)), Q(() => MathF.Sqrt(F(2f))), Q(() => MathF.Max(x, y)), Q(() => MathF.Min(x, F(float.NaN))), Q(() => MathF.Sign(y)), Q(() => MathF.Pow(F(2f), F(10f))), Q(() => MathF.PI), Q(() => MathF.E),
                Q(() => MathF.Round(z, 1)), Q(() => MathF.Round(F(7.15f), 1)), Q(() => MathF.Round(F(7.25f), 1)), Q(() => MathF.Round(F(-7.25f), 1)), Q(() => MathF.FusedMultiplyAdd(F(0.1f), F(10f), F(-1f))), Q(() => MathF.Cbrt(F(27f))), Q(() => MathF.Log2(F(8f))), Q(() => MathF.Exp(F(0f))), Q(() => MathF.Sin(F(0f))), Q(() => MathF.Cos(F(0f))), Q(() => MathF.Atan2(F(1f), F(1f))), Q(() => MathF.ScaleB(F(1f), 10)), Q(() => MathF.IEEERemainder(F(5f), F(3f))));
        }

        static string MathFVsMath()
        {
            float f = F(2f); double d = D(2);
            float fs = MathF.Sqrt(f); double ds = Math.Sqrt(d);
            float fsd = (float)Math.Sqrt(f);
            return Par.J(Q(() => fs), Q(() => ds), Q(() => fsd), Q(() => fs == fsd), Q(() => (double)fs == ds), Q(() => (float)ds == fs), Q(() => MathF.Sqrt(F(3f))), Q(() => (float)Math.Sqrt(D(3))), Q(() => MathF.Sqrt(F(3f)) == (float)Math.Sqrt(D(3))), Q(() => MathF.Abs(F(-1.5f))), Q(() => MathF.Max(F(1f), F(2f))),
                Q(() => MathF.Sqrt(F(0.1f)).ToString("R")), Q(() => ((float)Math.Sqrt(D(0.1f))).ToString("R")), Par.R6(MathF.Sin(F(1f))), Par.R6(Math.Sin(D(1))), Par.R6((float)Math.Sin(F(1f))), Par.R6(MathF.Exp(F(1f))), Par.R6(MathF.Log(F(10f))), Par.R6(MathF.Pow(F(1.5f), F(2.5f))), Par.R6(Math.Pow(D(1.5), D(2.5))));
        }

        static string SqrtExact()
        {
            // IEEE sqrt dogru yuvarlanir: bit-bit esit olmali
            double[] xs = { 2, 3, 5, 7, 10, 0.1, 0.5, 1e10, 1e-10, 123456789, 2.2250738585072014E-308, 1.7976931348623157E308, 4, 1e300, 0.001 };
            var sb = new System.Text.StringBuilder();
            foreach (var x0 in xs) { double x = D(x0); sb.Append(Math.Sqrt(x).ToString("R")).Append(',').Append(MathF.Sqrt((float)x).ToString("R")).Append(';'); }
            sb.Append(Math.Sqrt(D(-1))).Append(',').Append(Math.Sqrt(D(-0.0))).Append(',').Append(Math.Sqrt(D(double.NaN))).Append(',').Append(Math.Cbrt(D(-8))).Append(',').Append(Math.Cbrt(D(27)).ToString("R"));
            return sb.ToString();
        }

        static string LibmRounded()
        {
            double x = D(1.2345);
            return Par.J(Par.R6(Math.Sin(x)), Par.R6(Math.Cos(x)), Par.R6(Math.Tan(x)), Par.R6(Math.Asin(D(0.5))), Par.R6(Math.Acos(D(0.5))), Par.R6(Math.Atan(x)), Par.R6(Math.Atan2(D(1), D(-1))), Par.R6(Math.Sinh(x)), Par.R6(Math.Cosh(x)), Par.R6(Math.Tanh(x)),
                Par.R6(Math.Exp(x)), Par.R6(Math.Log(x)), Par.R6(Math.Log10(x)), Par.R6(Math.Log2(x)), Par.R6(Math.Log(D(8), D(2))), Par.R6(Math.Pow(x, D(2.5))), Par.R6(Math.Pow(D(2), D(0.5))), Par.R6(Math.Exp(D(1))), Par.R6(Math.Log(D(Math.E))), Par.R6(Math.Asinh(x)), Par.R6(Math.Acosh(D(2))), Par.R6(Math.Atanh(D(0.5))),
                Par.R6(Math.Sin(D(Math.PI))), Par.R6(Math.Cos(D(Math.PI / 2))), Par.R6(Math.Sin(D(1e6))), Par.R6(Math.Tan(D(Math.PI / 4))), Par.R6(Math.Exp(D(-745))), Par.R6(Math.Exp(D(709))), Q(() => Math.Exp(D(710))), Q(() => Math.Exp(D(-746))));
        }

        static string PowEdges()
        {
            double nan = D(double.NaN), inf = D(double.PositiveInfinity), ninf = D(double.NegativeInfinity), z = D(0), nz = -D(0.0), one = D(1), m1 = D(-1);
            return Par.J(Q(() => Math.Pow(z, z)), Q(() => Math.Pow(nan, z)), Q(() => Math.Pow(one, nan)), Q(() => Math.Pow(one, inf)), Q(() => Math.Pow(m1, inf)), Q(() => Math.Pow(m1, ninf)), Q(() => Math.Pow(D(-8), D(1.0 / 3))), Q(() => Math.Pow(D(-8), D(3))), Q(() => Math.Pow(D(-8), D(2))), Q(() => Math.Pow(D(-2), D(0.5))), Q(() => Math.Pow(z, D(-1))), Q(() => Math.Pow(nz, D(-1))), Q(() => Math.Pow(nz, D(-2))), Q(() => Math.Pow(nz, D(3))),
                Q(() => Math.Pow(D(2), D(10))), Q(() => Math.Pow(D(2), D(-1))), Q(() => Math.Pow(D(10), D(308))), Q(() => Math.Pow(D(10), D(309))), Q(() => Math.Pow(D(10), D(-324))), Q(() => Math.Pow(inf, z)), Q(() => Math.Pow(inf, D(-1))), Q(() => Math.Pow(ninf, D(3))), Q(() => Math.Pow(ninf, D(2))), Q(() => Math.Pow(D(0.5), inf)), Q(() => Math.Pow(D(2), inf)), Q(() => Math.Pow(D(2), ninf)), Q(() => Math.Pow(nan, nan)), Q(() => Math.Pow(D(2), D(0.5)).ToString("R")), Q(() => Math.Pow(D(3), D(3))), Q(() => Math.Pow(D(1.1), D(2)).ToString("R")), Q(() => Math.Pow(D(10), D(2)).ToString("R")), Q(() => Math.Pow(D(10), D(-2)).ToString("R")));
        }

        static string LogEdges()
        {
            double z = D(0), nz = -D(0.0), m1 = D(-1), inf = D(double.PositiveInfinity), nan = D(double.NaN);
            return Par.J(Q(() => Math.Log(z)), Q(() => Math.Log(nz)), Q(() => Math.Log(m1)), Q(() => Math.Log(inf)), Q(() => Math.Log(nan)), Q(() => Math.Log(D(1))), Q(() => Math.Log10(D(1000))), Q(() => Math.Log10(D(0.001)).ToString("R")), Q(() => Math.Log2(D(1024))), Q(() => Math.Log2(D(0.5))), Q(() => Math.Log(D(8), D(2))), Q(() => Math.Log(D(8), D(1))), Q(() => Math.Log(D(8), D(0))), Q(() => Math.Log(D(8), D(-2))), Q(() => Math.Log(D(1), D(1))), Q(() => Math.Log(D(0), D(0))), Q(() => Math.Log(D(8), inf)), Q(() => Math.ILogB(D(1024))), Q(() => Math.ILogB(D(0.1))), Q(() => Math.ILogB(z)), Q(() => Math.Log10(D(1e-320)).ToString("R")), Q(() => Math.Exp(D(0))), Q(() => Math.Exp(inf)), Q(() => Math.Exp(-inf)), Q(() => Math.Exp(nan)));
        }

        static string DoubleMod()
        {
            return Par.J(Q(() => D(-5.5) % D(2)), Q(() => D(5.5) % D(-2)), Q(() => D(-5.5) % D(-2)), Q(() => D(5.5) % D(2)), Q(() => D(1e20) % D(3)), Q(() => D(0.3) % D(0.1)), Q(() => D(1) % D(0.1)), Q(() => D(-1) % D(0.1)), Q(() => D(7) % D(double.PositiveInfinity)), Q(() => D(double.PositiveInfinity) % D(7)), Q(() => D(7) % D(0)), Q(() => D(0) % D(7)), Q(() => -D(0.0) % D(7)), Q(() => D(5) % D(5)), Q(() => D(-5) % D(5)),
                Q(() => Math.IEEERemainder(D(-5.5), D(2))), Q(() => Math.IEEERemainder(D(5.5), D(2))), Q(() => Math.IEEERemainder(D(5), D(2))), Q(() => Math.IEEERemainder(D(7), D(2))), Q(() => Math.IEEERemainder(D(3), D(2))), Q(() => Math.IEEERemainder(D(1), D(0.1))), Q(() => Math.IEEERemainder(D(7), D(0))), Q(() => F(5.5f) % F(2f)), Q(() => F(-5.5f) % F(2f)), Q(() => F(1f) % F(0.1f)), Q(() => D(1e308) % D(1e-308)), Q(() => D(123456.789) % D(1000)));
        }

        static string MinMaxNaN()
        {
            double n = D(double.NaN), one = D(1), nz = -D(0.0), pz = D(0.0);
            float fn = F(float.NaN), fone = F(1f);
            return Par.J(Q(() => Math.Max(n, one)), Q(() => Math.Max(one, n)), Q(() => Math.Min(n, one)), Q(() => Math.Min(one, n)), Q(() => Math.Max(nz, pz)), Q(() => Math.Min(pz, nz)), Q(() => Math.Max(n, n)), Q(() => MathF.Max(fn, fone)), Q(() => MathF.Min(fone, fn)), Q(() => Math.MaxMagnitude(D(-3), D(2))), Q(() => Math.MinMagnitude(D(-3), D(2))), Q(() => Math.MaxMagnitude(D(-2), D(2))), Q(() => Math.MinMagnitude(D(-2), D(2))),
                Q(() => Math.Max(I(3), I(-3))), Q(() => Math.Min(I(int.MinValue), I(0))), Q(() => Math.Max(D(double.NegativeInfinity), D(double.MinValue))), Q(() => Math.Max(D(1e-320), D(0))), Q(() => Math.Max((byte)I(3), (byte)I(5))), Q(() => Math.Max(2u, 3u)), Q(() => Math.Min(-1L, 1L)), Q(() => Math.Min(F(-0f), F(0f))), Q(() => Math.Max(F(-0f), F(0f))));
        }

        static string ClampSign()
        {
            return Par.J(Q(() => Math.Clamp(I(5), I(1), I(3))), Q(() => Math.Clamp(I(-5), I(1), I(3))), Q(() => Math.Clamp(I(2), I(1), I(3))), Par.T(() => Math.Clamp(I(2), I(3), I(1)).ToString()), Q(() => Math.Clamp(D(double.NaN), D(0), D(1))), Q(() => Math.Clamp(D(-0.0), D(0), D(1))), Q(() => Math.Clamp(D(5.5), D(0), D(1))), Q(() => Math.Clamp(F(float.NegativeInfinity), F(0f), F(1f))), Q(() => Math.Clamp((byte)I(200), (byte)I(0), (byte)I(100))), Q(() => Math.Clamp(L(long.MinValue), L(-5), L(5))), Q(() => Math.Clamp(UL(10), UL(0), UL(5))),
                Q(() => Math.Sign(I(-5))), Q(() => Math.Sign(I(0))), Q(() => Math.Sign(L(long.MinValue))), Q(() => Math.Sign(D(-0.0))), Q(() => Math.Sign(D(-1e-300))), Q(() => Math.Sign(D(double.PositiveInfinity))), Par.T(() => Math.Sign(D(double.NaN)).ToString()), Q(() => Math.Sign((sbyte)I(-1))), Q(() => Math.Sign((short)I(5))), Q(() => Math.Sign(F(float.NegativeInfinity))), Q(() => Math.Sign(1.5m)), Q(() => Math.Sign(D(5))));
        }

        static long L(long v) { return v; }
        static ulong UL(ulong v) { return v; }

        static string DivRemBigMul()
        {
            int q, r; long lq, lr;
            q = Math.DivRem(I(-7), I(2), out r); lq = Math.DivRem(L(-7), L(2), out lr);
            var t = Math.DivRem(I(7), I(-2)); var tl = Math.DivRem(L(long.MinValue), L(3)); var tu = Math.DivRem(U(7), U(2)); var tb = Math.DivRem((byte)I(7), (byte)I(2)); var ts = Math.DivRem((sbyte)I(-7), (sbyte)I(2));
            return Par.J(Q(() => q), Q(() => r), Q(() => lq), Q(() => lr), Q(() => t.Quotient), Q(() => t.Remainder), Q(() => tl.Quotient), Q(() => tl.Remainder), Q(() => tu.Quotient), Q(() => tu.Remainder), Q(() => tb.Quotient), Q(() => tb.Remainder), Q(() => ts.Quotient), Q(() => ts.Remainder), Par.T(() => Math.DivRem(I(1), I(0), out r).ToString()), Par.K(() => Math.DivRem(I(int.MinValue), I(-1), out r).ToString()),
                Q(() => Math.BigMul(I(int.MaxValue), I(int.MaxValue))), Q(() => Math.BigMul(I(int.MinValue), I(int.MinValue))), Q(() => Math.BigMul(I(int.MinValue), I(-1))), Q(() => Math.BigMul(U(uint.MaxValue), U(uint.MaxValue))), Math.BigMul(L(long.MaxValue), L(long.MaxValue), out long low), Q(() => low), Math.BigMul(UL(ulong.MaxValue), UL(ulong.MaxValue), out ulong ulow), Q(() => ulow), Math.BigMul(L(-1), L(-1), out long low2), Q(() => low2), Math.BigMul(L(long.MinValue), L(-1), out long low3), Q(() => low3));
        }

        static uint U(uint v) { return v; }

        static string MiscMath()
        {
            return Par.J(Q(() => Math.Abs(D(-0.0))), Q(() => Math.Abs(D(double.NegativeInfinity))), Q(() => Math.Abs(F(-float.Epsilon))), Q(() => Math.Abs(D(double.MinValue))), Q(() => Math.CopySign(D(1), D(-2))), Q(() => Math.CopySign(D(-1), D(2))), Q(() => Math.CopySign(D(1), D(double.NaN))), Q(() => Math.CopySign(D(3), -D(0.0))), Q(() => Math.FusedMultiplyAdd(D(0.1), D(10), D(-1))), Q(() => D(0.1) * D(10) - D(1)), Q(() => Math.ScaleB(D(1), 1024)), Q(() => Math.ScaleB(D(1), -1074)), Q(() => Math.ScaleB(D(1), -1075)), Q(() => Math.ScaleB(D(3), 2)), Q(() => Math.ReciprocalEstimate(D(4)) == 0.25 || true),
                Q(() => Math.Round(D(1e300))), Q(() => Math.Round(D(4503599627370497.0))), Q(() => Math.Floor(D(4503599627370497.0))), Q(() => Math.Ceiling(D(-4503599627370497.0))), Q(() => Math.Truncate(D(1e300))), Q(() => Math.Round(D(double.NaN))), Q(() => Math.Floor(D(double.PositiveInfinity))), Q(() => Math.Ceiling(D(double.NegativeInfinity))), Q(() => Math.Round(D(0.49999999999999994))), Q(() => Math.Round(D(-0.49999999999999994))), Q(() => Math.Round(D(0.5000000000000001))), Q(() => Math.Round(D(1.4999999999999998))), Q(() => Math.Round(D(1.5000000000000002))), Q(() => Math.Round(D(2.5000000000000004))));
        }

        static string FloatCompareTo()
        {
            double a = D(1), b = D(2), n = D(double.NaN), nz = -D(0.0), pz = D(0.0), ni = D(double.NegativeInfinity);
            float fa = F(1f), fn = F(float.NaN);
            return Par.J(Q(() => a.CompareTo(b)), Q(() => b.CompareTo(a)), Q(() => a.CompareTo(a)), Q(() => n.CompareTo(n)), Q(() => n.CompareTo(a)), Q(() => a.CompareTo(n)), Q(() => n.CompareTo(ni)), Q(() => ni.CompareTo(n)), Q(() => nz.CompareTo(pz)), Q(() => pz.CompareTo(nz)), Q(() => a.CompareTo((object)b)), Par.T(() => a.CompareTo("x").ToString()), Q(() => a.CompareTo(null)), Q(() => fa.CompareTo(fn)), Q(() => fn.CompareTo(fa)), Q(() => fn.CompareTo(fn)),
                Q(() => a.Equals((object)1)), Q(() => a.Equals(1.0f)), Q(() => a.Equals((object)1.0f)), Q(() => fa.Equals(1.0)), Q(() => fa.Equals((object)1.0)), Q(() => ((object)a).Equals(1.0)), Q(() => a == 1), Q(() => fa == 1.0), Q(() => I(1) == D(1)), Q(() => I(1).Equals(D(1))), Q(() => D(1).Equals(I(1))), Q(() => ((IComparable)a).CompareTo(2.0)));
        }

        static string Epsilons()
        {
            return Par.J(Q(() => D(double.Epsilon)), Q(() => F(float.Epsilon)), Q(() => D(double.Epsilon) / 2), Q(() => D(double.Epsilon) * 0.5 == 0), Q(() => F(float.Epsilon) / 2), Q(() => D(double.MaxValue)), Q(() => D(double.MinValue)), Q(() => F(float.MaxValue)), Q(() => F(float.MinValue)), Q(() => D(double.Epsilon) > 0), Q(() => D(1) + D(double.Epsilon) == 1), Q(() => D(1) + D(1e-16) == 1), Q(() => D(1) + D(1.2e-16) == 1), Q(() => D(1) + D(2.3e-16) == 1),
                Q(() => F(1f) + F(1e-8f) == 1), Q(() => F(1f) + F(6e-8f) == 1), Q(() => D(2.2250738585072014E-308) / 2), Q(() => D(2.2250738585072014E-308) / 2 > 0), Q(() => D(4.9e-324)), Q(() => D(4.9e-324) == double.Epsilon), Q(() => D(2.5e-324)), Q(() => D(2.4e-324)), Q(() => D(1e-323)), Q(() => F(1.4e-45f) == float.Epsilon), Q(() => F(7e-46f)), Q(() => F(8e-46f)), Q(() => D(double.MaxValue) * D(1.0000000000000002)), Q(() => D(1.7976931348623158E308)), Q(() => double.Parse("1.797693134862316E308")));
        }

        static string TrigExact()
        {
            // bu degerler exact olmali (sifir/birim argumanlar)
            return Par.J(Q(() => Math.Sin(D(0))), Q(() => Math.Cos(D(0))), Q(() => Math.Tan(D(0))), Q(() => Math.Atan(D(0))), Q(() => Math.Asin(D(0))), Q(() => Math.Acos(D(1))), Q(() => Math.Atan2(D(0), D(1))), Q(() => Math.Atan2(D(0), D(-1))), Q(() => Math.Atan2(D(1), D(0))), Q(() => Math.Atan2(D(-1), D(0))), Q(() => Math.Atan2(D(0), D(0))), Q(() => Math.Atan2(-D(0.0), D(0))), Q(() => Math.Atan2(-D(0.0), D(-1))), Q(() => Math.Atan2(D(0), -D(0.0))), Q(() => Math.Exp(D(0))), Q(() => Math.Log(D(1))), Q(() => Math.Log10(D(10))), Q(() => Math.Log10(D(100))), Q(() => Math.Log2(D(8))), Q(() => Math.Pow(D(2), D(3))), Q(() => Math.Pow(D(4), D(0.5))), Q(() => Math.Pow(D(8), D(1.0 / 3))), Q(() => Math.Sinh(D(0))), Q(() => Math.Cosh(D(0))), Q(() => Math.Tanh(D(0))), Q(() => Math.Tanh(D(1000))), Q(() => Math.Tanh(D(-1000))), Q(() => Math.Sin(D(double.PositiveInfinity))), Q(() => Math.Cos(D(double.NaN))), Q(() => Math.Asin(D(2))), Q(() => Math.Acos(D(-2))), Q(() => Math.Atan(D(double.PositiveInfinity)).ToString("R")), Q(() => Math.Atan(D(double.NegativeInfinity)).ToString("R")));
        }

        static string IsChecks()
        {
            double n = D(double.NaN), inf = D(double.PositiveInfinity), nz = -D(0.0), sub = D(1e-310), norm = D(1);
            return Par.J(Q(() => double.IsNaN(n)), Q(() => double.IsInfinity(inf)), Q(() => double.IsNegativeInfinity(-inf)), Q(() => double.IsFinite(n)), Q(() => double.IsFinite(norm)), Q(() => double.IsNormal(sub)), Q(() => double.IsSubnormal(sub)), Q(() => double.IsNormal(norm)), Q(() => double.IsNormal(D(0))), Q(() => double.IsSubnormal(D(0))), Q(() => double.IsNegative(nz)), Q(() => double.IsNegative(n)), Q(() => double.IsNegative(-n)), Q(() => double.IsInteger(D(2.0))), Q(() => double.IsInteger(D(2.5))), Q(() => double.IsEvenInteger(D(4))), Q(() => double.IsOddInteger(D(3))), Q(() => double.IsPositive(nz)), Q(() => double.IsPositive(D(0))), Q(() => double.IsRealNumber(n)),
                Q(() => float.IsNaN(F(float.NaN))), Q(() => float.IsSubnormal(F(1e-40f))), Q(() => float.IsNormal(F(1e-38f))), Q(() => float.IsNormal(F(1e-39f))), Q(() => float.IsFinite(F(float.MaxValue))), Q(() => int.IsEvenInteger(I(4))), Q(() => int.IsNegative(I(-1))), Q(() => int.IsPow2(I(1024))), Q(() => int.IsPow2(I(0))), Q(() => int.IsPow2(I(-4))), Q(() => int.Log2(I(1024))), Q(() => int.Log2(I(0))), Q(() => int.PopCount(I(-1))), Q(() => int.LeadingZeroCount(I(1))), Q(() => int.TrailingZeroCount(I(8))), Q(() => int.RotateLeft(I(1), 33)), Q(() => uint.RotateRight(U(1), 1)), Q(() => int.Clamp(I(5), I(0), I(3))), Q(() => int.Abs(I(-5))), Q(() => int.Max(I(1), I(2))), Q(() => int.CopySign(I(5), I(-1))), Par.T(() => int.CopySign(I(int.MinValue), I(1)).ToString()));
        }

        static string FloatHash()
        {
            return Par.J(Q(() => D(1.0).GetHashCode()), Q(() => D(2.0).GetHashCode()), Q(() => D(0.5).GetHashCode()), Q(() => D(-1.0).GetHashCode()), Q(() => D(0.0).GetHashCode()), Q(() => (-D(0.0)).GetHashCode()), Q(() => D(double.NaN).GetHashCode()), Q(() => D(double.PositiveInfinity).GetHashCode()), Q(() => D(double.Epsilon).GetHashCode()), Q(() => D(1e300).GetHashCode()), Q(() => D(0.1).GetHashCode()),
                Q(() => F(1f).GetHashCode()), Q(() => F(2f).GetHashCode()), Q(() => F(0.5f).GetHashCode()), Q(() => F(-1f).GetHashCode()), Q(() => F(0f).GetHashCode()), Q(() => (-F(0f)).GetHashCode()), Q(() => F(float.NaN).GetHashCode()), Q(() => F(0.1f).GetHashCode()), Q(() => I(5).GetHashCode()), Q(() => I(-5).GetHashCode()), Q(() => L(5).GetHashCode()), Q(() => L(-5).GetHashCode()), Q(() => L(1L << 40).GetHashCode()), Q(() => L(long.MinValue).GetHashCode()), Q(() => U(5).GetHashCode()), Q(() => UL(1UL << 40).GetHashCode()), Q(() => ((short)I(-1)).GetHashCode()), Q(() => ((ushort)I(5)).GetHashCode()), Q(() => ((byte)I(200)).GetHashCode()), Q(() => ((sbyte)I(-1)).GetHashCode()), Q(() => 'A'.GetHashCode()), Q(() => true.GetHashCode()), Q(() => false.GetHashCode()));
        }

        static string BitIncrement()
        {
            double one = D(1), z = D(0), mx = D(double.MaxValue);
            return Par.J(Q(() => Math.BitIncrement(one).ToString("R")), Q(() => Math.BitDecrement(one).ToString("R")), Q(() => Math.BitIncrement(z).ToString("R")), Q(() => Math.BitDecrement(z).ToString("R")), Q(() => Math.BitIncrement(mx)), Q(() => Math.BitDecrement(D(double.PositiveInfinity)).ToString("R")), Q(() => Math.BitIncrement(-D(0.0)).ToString("R")), Q(() => Math.BitIncrement(D(double.NaN))), Q(() => MathF.BitIncrement(F(1f)).ToString("R")), Q(() => MathF.BitDecrement(F(0f)).ToString("R")),
                Q(() => BitConverter.DoubleToInt64Bits(D(1))), Q(() => BitConverter.DoubleToInt64Bits(D(2))), Q(() => BitConverter.DoubleToInt64Bits(D(0.1))), Q(() => BitConverter.DoubleToInt64Bits(D(-1))), Q(() => BitConverter.DoubleToInt64Bits(D(double.NaN))), Q(() => BitConverter.DoubleToInt64Bits(D(0.0) / D(0.0))), Q(() => BitConverter.DoubleToInt64Bits(D(double.PositiveInfinity))), Q(() => BitConverter.DoubleToInt64Bits(D(double.Epsilon))), Q(() => BitConverter.SingleToInt32Bits(F(1f))), Q(() => BitConverter.SingleToInt32Bits(F(-0f))), Q(() => BitConverter.SingleToInt32Bits(F(float.NaN))), Q(() => BitConverter.SingleToInt32Bits(F(0.1f))), Q(() => BitConverter.Int64BitsToDouble(L(0x3FF8000000000000L))), Q(() => BitConverter.Int32BitsToSingle(I(0x40490FDB)).ToString("R")), Q(() => BitConverter.Int64BitsToDouble(L(1))), Q(() => BitConverter.Int64BitsToDouble(L(-1))), Q(() => BitConverter.Int64BitsToDouble(L(0x7FF0000000000001L))));
        }
    }
}
