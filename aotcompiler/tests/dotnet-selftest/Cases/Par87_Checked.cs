using System;
using ParityKit;

// 87: checked tasma (OverflowException beklenir), conv.ovf, float->int donusum kenarlari, float32 ara hassasiyet.
namespace Demo87
{
    class App87
    {
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static int I(int v) { return v; }
        static long L(long v) { return v; }
        static uint U(uint v) { return v; }
        static ulong UL(ulong v) { return v; }
        static double D(double v) { return v; }
        static float F(float v) { return v; }

        public static int Run() { return Par.Loop(87, 24, Step); }

        static string Step(int k)
        {
            switch (k)
            {
                case 1: return CheckedAddInt();
                case 2: return CheckedSubMul();
                case 3: return CheckedLong();
                case 4: return CheckedUnsigned();
                case 5: return CheckedSmall();
                case 6: return CheckedNeg();
                case 7: return CheckedConvInt();
                case 8: return CheckedConvFloat();
                case 9: return CheckedBlockMixed();
                case 10: return FloatToIntNaN();
                case 11: return FloatToIntRange();
                case 12: return FloatToSmall();
                case 13: return FloatTrunc();
                case 14: return FloatToUnsigned();
                case 15: return Float32Precision();
                case 16: return FloatDoubleRoundTrip();
                case 17: return LongToFloat();
                case 18: return IntToFloatPrecision();
                case 19: return UlongDouble();
                case 20: return CharConv();
                case 21: return DecimalBasic();
                case 22: return CheckedIncrement();
                case 23: return CheckedCompound();
                case 24: return Float32Accumulate();
            }
            return "?";
        }

        static string OV(Func<string> f) { try { return f(); } catch (OverflowException) { return "OVF"; } }

        static string CheckedAddInt()
        {
            int a = I(int.MaxValue), b = I(int.MinValue), one = I(1);
            return Par.J(OV(() => checked(a + one).ToString()), OV(() => checked(b - one).ToString()), OV(() => checked(a + 0).ToString()),
                OV(() => checked(b + a).ToString()), OV(() => checked(I(1 << 30) + I(1 << 30)).ToString()), OV(() => checked(I(-1 << 30) + I(-1 << 30)).ToString()),
                OV(() => checked(I(-1 << 30) + I(-1 << 30) - one).ToString()));
        }

        static string CheckedSubMul()
        {
            int a = I(int.MaxValue), b = I(int.MinValue), m = I(65536), two = I(2), m1 = I(-1);
            return Par.J(OV(() => checked(m * m).ToString()), OV(() => checked(a * two).ToString()), OV(() => checked(b * m1).ToString()), OV(() => checked(b * two).ToString()),
                OV(() => checked(I(46341) * I(46341)).ToString()), OV(() => checked(I(46340) * I(46340)).ToString()), OV(() => checked(I(0) - b).ToString()),
                OV(() => checked(a - b).ToString()), OV(() => checked(b - a).ToString()), OV(() => checked(I(-3) * I(715827883)).ToString()), OV(() => checked(I(-3) * I(715827882)).ToString()));
        }

        static string CheckedLong()
        {
            long a = L(long.MaxValue), b = L(long.MinValue), one = L(1), two = L(2), m1 = L(-1);
            return Par.J(OV(() => checked(a + one).ToString()), OV(() => checked(b - one).ToString()), OV(() => checked(a * two).ToString()), OV(() => checked(b * m1).ToString()),
                OV(() => checked(L(1L << 32) * L(1L << 31)).ToString()), OV(() => checked(L(1L << 32) * L((1L << 31) - 1)).ToString()), OV(() => checked(L(3037000500L) * L(3037000500L)).ToString()),
                OV(() => checked(L(3037000499L) * L(3037000499L)).ToString()), Par.K(() => OV(() => checked(b / m1).ToString())), OV(() => checked(a - b).ToString()));
        }

        static string CheckedUnsigned()
        {
            uint a = U(uint.MaxValue), z = U(0), one = U(1), two = U(2), h = U(0x80000000);
            ulong la = UL(ulong.MaxValue), lz = UL(0), lone = UL(1), lh = UL(1UL << 63);
            return Par.J(OV(() => checked(a + one).ToString()), OV(() => checked(z - one).ToString()), OV(() => checked(h * two).ToString()), OV(() => checked(h + h).ToString()),
                OV(() => checked(la + lone).ToString()), OV(() => checked(lz - lone).ToString()), OV(() => checked(lh * UL(2)).ToString()), OV(() => checked(a * a).ToString()),
                OV(() => checked(U(65536) * U(65535)).ToString()), OV(() => checked(U(65536) * U(65536)).ToString()));
        }

        static string CheckedSmall()
        {
            byte b = (byte)I(255); sbyte sb = (sbyte)I(127); short s = (short)I(32767); ushort us = (ushort)I(65535); int one = I(1);
            // kucuk tipler int'te hesaplanir; tasma yalniz CAST'te (conv.ovf)
            return Par.J(OV(() => checked((byte)(b + one)).ToString()), OV(() => checked(b + one).ToString()), OV(() => checked((sbyte)(sb + one)).ToString()),
                OV(() => checked((short)(s + one)).ToString()), OV(() => checked((ushort)(us + one)).ToString()), OV(() => checked((byte)I(-1)).ToString()),
                OV(() => checked((sbyte)I(128)).ToString()), OV(() => checked((sbyte)I(-128)).ToString()), OV(() => checked((ushort)I(-1)).ToString()), OV(() => checked((char)I(-1)).ToString()),
                OV(() => ((int)checked((char)I(65535))).ToString()), OV(() => checked((byte)I(256)).ToString()), OV(() => checked((byte)I(255)).ToString()));
        }

        static string CheckedNeg()
        {
            int a = I(int.MinValue); long b = L(long.MinValue); uint u = U(1); int z = I(0);
            return Par.J(OV(() => checked(-a).ToString()), OV(() => checked(-b).ToString()), OV(() => checked(-u).ToString()), OV(() => checked(-z).ToString()),
                OV(() => checked(-I(int.MaxValue)).ToString()), OV(() => checked(0 - a).ToString()), OV(() => checked(-(long)a).ToString()));
        }

        static string CheckedConvInt()
        {
            long big = L(1L << 40), neg = L(-1), fit = L(int.MaxValue), nfit = L(int.MinValue), over = L((long)int.MaxValue + 1), under = L((long)int.MinValue - 1);
            ulong ub = UL(ulong.MaxValue), uh = UL(1UL << 63), ufit = UL(long.MaxValue);
            int m1 = I(-1);
            return Par.J(OV(() => checked((int)big).ToString()), OV(() => checked((int)fit).ToString()), OV(() => checked((int)nfit).ToString()), OV(() => checked((int)over).ToString()),
                OV(() => checked((int)under).ToString()), OV(() => checked((uint)neg).ToString()), OV(() => checked((uint)m1).ToString()), OV(() => checked((ulong)neg).ToString()),
                OV(() => checked((long)ub).ToString()), OV(() => checked((long)uh).ToString()), OV(() => checked((long)ufit).ToString()), OV(() => checked((uint)big).ToString()),
                OV(() => checked((uint)L(uint.MaxValue)).ToString()), OV(() => checked((int)UL(uint.MaxValue)).ToString()), OV(() => checked((ulong)L(long.MaxValue)).ToString()),
                OV(() => checked((short)L(-32769)).ToString()), OV(() => checked((byte)UL(255)).ToString()), OV(() => checked((byte)UL(256)).ToString()));
        }

        static string CheckedConvFloat()
        {
            double nan = D(double.NaN), pinf = D(double.PositiveInfinity), ninf = D(double.NegativeInfinity), big = D(1e30), mx = D(int.MaxValue), mx1 = D(2147483648.0), mn = D(int.MinValue), mn1 = D(-2147483649.0);
            float fbig = F(1e20f), fnan = F(float.NaN);
            return Par.J(OV(() => checked((int)nan).ToString()), OV(() => checked((int)pinf).ToString()), OV(() => checked((int)ninf).ToString()), OV(() => checked((int)big).ToString()),
                OV(() => checked((int)mx).ToString()), OV(() => checked((int)mx1).ToString()), OV(() => checked((int)mn).ToString()), OV(() => checked((int)mn1).ToString()),
                OV(() => checked((long)big).ToString()), OV(() => checked((long)D(9.2233720368547758E18)).ToString()), OV(() => checked((long)D(-9.2233720368547758E18)).ToString()),
                OV(() => checked((uint)D(-1.0)).ToString()), OV(() => checked((uint)D(-0.5)).ToString()), OV(() => checked((byte)D(255.9)).ToString()), OV(() => checked((byte)D(256.0)).ToString()),
                OV(() => checked((ulong)D(1.8446744073709552E19)).ToString()), OV(() => checked((ulong)D(-0.9)).ToString()), OV(() => checked((int)fbig).ToString()), OV(() => checked((int)fnan).ToString()),
                OV(() => checked((short)D(32767.99)).ToString()), OV(() => checked((sbyte)D(-128.5)).ToString()), OV(() => checked((char)D(65536.0)).ToString()));
        }

        static string CheckedBlockMixed()
        {
            int a = I(int.MaxValue), one = I(1);
            string r;
            try { checked { int x = a; x += one; r = x.ToString(); } } catch (OverflowException) { r = "OVF"; }
            string r2;
            try { checked { int x = a; unchecked { x += one; } r2 = x.ToString(); } } catch (OverflowException) { r2 = "OVF"; }
            string r3;
            try { r3 = checked((byte)(I(200) + I(100))).ToString(); } catch (OverflowException) { r3 = "OVF"; }
            string r4 = unchecked((byte)(I(200) + I(100))).ToString();
            string r5;
            try { checked { long l = L(long.MaxValue); l++; r5 = l.ToString(); } } catch (OverflowException) { r5 = "OVF"; }
            return Par.J(Q(() => r), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5));
        }

        static string FloatToIntNaN()
        {
            double nan = D(double.NaN), pinf = D(double.PositiveInfinity), ninf = D(double.NegativeInfinity);
            float fnan = F(float.NaN), fpinf = F(float.PositiveInfinity), fninf = F(float.NegativeInfinity);
            return Par.J(Q(() => unchecked((int)nan)), Q(() => unchecked((int)pinf)), Q(() => unchecked((int)ninf)), Q(() => unchecked((long)nan)), Q(() => unchecked((long)pinf)), Q(() => unchecked((long)ninf)),
                Q(() => unchecked((uint)nan)), Q(() => unchecked((uint)pinf)), Q(() => unchecked((ulong)nan)), Q(() => unchecked((ulong)pinf)), Q(() => unchecked((ulong)ninf)),
                Q(() => unchecked((int)fnan)), Q(() => unchecked((int)fpinf)), Q(() => unchecked((int)fninf)), Q(() => unchecked((long)fnan)), Q(() => unchecked((short)nan)), Q(() => unchecked((byte)nan)), Q(() => unchecked((sbyte)pinf)));
        }

        static string FloatToIntRange()
        {
            // .NET Core 3.0+ / .NET 9: doyurma (saturating) donusum; C'de UB
            double[] ds = { 1e10, -1e10, 2147483647.0, 2147483648.0, -2147483648.0, -2147483649.0, 1e19, -1e19, 1e30, -1e30, 4294967295.0, 4294967296.0, 9.2233720368547758E18, -9.2233720368547758E18, 1.8446744073709552E19 };
            var sb = new System.Text.StringBuilder();
            foreach (var d0 in ds)
            {
                double d = D(d0);
                sb.Append(unchecked((int)d)).Append(',').Append(unchecked((long)d)).Append(',').Append(unchecked((uint)d)).Append(',').Append(unchecked((ulong)d)).Append(';');
            }
            return sb.ToString();
        }

        static string FloatToSmall()
        {
            double[] ds = { 300.7, -1.5, 255.9, 256.0, 65535.5, 65536.0, 127.9, 128.0, -128.9, -129.0, 32767.9, 32768.0, -32768.1, 1e10, -1e10 };
            var sb = new System.Text.StringBuilder();
            foreach (var d0 in ds)
            {
                double d = D(d0);
                sb.Append(unchecked((byte)d)).Append(',').Append(unchecked((sbyte)d)).Append(',').Append(unchecked((short)d)).Append(',').Append(unchecked((ushort)d)).Append(',').Append((int)unchecked((char)d)).Append(';');
            }
            return sb.ToString();
        }

        static string FloatTrunc()
        {
            return Par.J(Q(() => (int)D(2.9)), Q(() => (int)D(-2.9)), Q(() => (int)D(0.9999999)), Q(() => (int)D(-0.9999999)), Q(() => (long)D(2.5)), Q(() => (long)D(-2.5)), Q(() => (int)D(1e-300)), Q(() => (int)D(-1e-300)), Q(() => (int)F(2.9f)), Q(() => (int)F(-2.9f)),
                Q(() => (int)D(2147483647.9)), Q(() => (long)D(-0.0)), Q(() => (int)D(3.5)), Q(() => (int)D(-3.5)), Q(() => (int)D(1.9999999999999998)), Q(() => (int)D(-1.9999999999999998)), Q(() => (long)D(123456789012.75)), Q(() => (long)D(-123456789012.75)));
        }

        static string FloatToUnsigned()
        {
            return Par.J(Q(() => unchecked((uint)D(-1.5))), Q(() => unchecked((uint)D(-0.5))), Q(() => unchecked((uint)D(3000000000.0))), Q(() => unchecked((uint)D(4294967295.9))), Q(() => unchecked((ulong)D(-1.0))), Q(() => unchecked((ulong)D(1e19))),
                Q(() => unchecked((ulong)D(9223372036854775808.0))), Q(() => unchecked((ulong)D(18446744073709549568.0))), Q(() => unchecked((ushort)D(-1.0))), Q(() => unchecked((byte)D(-0.9))), Q(() => unchecked((uint)F(-1.5f))), Q(() => unchecked((ulong)F(1e19f))));
        }

        static string Float32Precision()
        {
            float a = F(16777216f), one = F(1f), b = F(0.1f), c = F(0.2f), big = F(1e8f);
            float s1 = a + one;
            float s2 = b + c;
            float s3 = big + one;
            float x = F(0.1f); float y = x * F(3f);
            float z = F(1f) / F(3f);
            float w = z * F(3f);
            double dz = (double)z;
            double dsum = (double)b + (double)c;
            float fsum = (float)dsum;
            bool eq = (F(0.1f) + F(0.2f)) == F(0.3f);
            bool deq = (D(0.1) + D(0.2)) == D(0.3);
            float sq = F(2f); float r = (float)Math.Sqrt(sq) * (float)Math.Sqrt(sq);
            return Par.J(Q(() => s1), Q(() => s2), Q(() => s3), Q(() => y), Q(() => z), Q(() => w), Q(() => dz), Q(() => dsum), Q(() => fsum), Q(() => eq), Q(() => deq), Q(() => r), Q(() => s1 == a), Q(() => s2 == c + b), Q(() => F(1e-45f)), Q(() => F(1e-46f) == 0f), Q(() => F(3.4e38f) * F(10f)), Q(() => F(float.MaxValue) + F(1f) == F(float.MaxValue)));
        }

        static string FloatDoubleRoundTrip()
        {
            double d = D(0.1); float f = (float)d; double back = f;
            float f2 = F(0.1f); double d2 = f2;
            return Par.J(Q(() => f), Q(() => back), Q(() => back == d), Q(() => d2), Q(() => (float)d2 == f2), Q(() => d2 == D(0.1)), Q(() => (float)D(1.0 / 3) == F(1f / 3f)), Q(() => (double)F(1f / 3f)), Q(() => (float)D(16777217.0)), Q(() => (float)D(16777219.0)), Q(() => (float)D(1e40)), Q(() => (float)D(-1e40)), Q(() => (float)D(1e-50)), Q(() => (float)D(double.NaN)), Q(() => (double)F(float.Epsilon)), Q(() => (float)D(3.4028235677973366E38)), Q(() => (float)D(3.4028235677973362E38)));
        }

        static string LongToFloat()
        {
            long a = L(long.MaxValue), b = L(1L << 53), c = L((1L << 53) + 1), d = L(9007199254740993L), e = L(-9007199254740993L), f = L(123456789123456789L);
            ulong ua = UL(ulong.MaxValue), ub = UL(1UL << 63) + UL(1025);
            return Par.J(Q(() => (double)a), Q(() => (float)a), Q(() => (double)b), Q(() => (double)c), Q(() => (double)d), Q(() => (double)e), Q(() => (float)d), Q(() => (double)f), Q(() => (float)f), Q(() => (double)ua), Q(() => (float)ua), Q(() => (double)ub), Q(() => (double)b == (double)c), Q(() => (long)(double)a), Q(() => (long)(float)f), Q(() => (double)L(long.MinValue)), Q(() => (float)L(long.MinValue)));
        }

        static string IntToFloatPrecision()
        {
            int a = I(16777217), b = I(16777219), c = I(int.MaxValue), d = I(int.MinValue), e = I(33554433);
            uint u = U(uint.MaxValue), u2 = U(4294967295u - 128);
            return Par.J(Q(() => (float)a), Q(() => (float)b), Q(() => (float)c), Q(() => (float)d), Q(() => (float)e), Q(() => (double)a), Q(() => (float)u), Q(() => (float)u2), Q(() => (double)u), Q(() => (int)(float)a), Q(() => (int)(float)c), Q(() => (long)(float)c), Q(() => (uint)(float)u), Q(() => (float)I(-16777217)), Q(() => (float)I(0x7FFFFF80)), Q(() => (float)I(0x7FFFFF7F)));
        }

        static string UlongDouble()
        {
            ulong a = UL(1UL << 63), b = UL(ulong.MaxValue), c = UL(1UL << 53) + UL(1);
            double da = a, db = b, dc = c;
            return Par.J(Q(() => da), Q(() => db), Q(() => dc), Q(() => (ulong)da), Q(() => unchecked((ulong)db)), Q(() => (ulong)dc), Q(() => (long)da), Q(() => (float)a), Q(() => (ulong)F((float)a)), Q(() => (ulong)D(9223372036854775807.0)), Q(() => (ulong)D(9223372036854777856.0)), Q(() => (double)UL(ulong.MaxValue - 1023)), Q(() => (double)UL(ulong.MaxValue - 1024)));
        }

        static string CharConv()
        {
            return Par.J(Q(() => (char)I(65)), Q(() => (int)(char)I(65)), Q(() => (char)D(65.9)), Q(() => (char)F(66.9f)), Q(() => (int)(char)I(65536 + 66)), Q(() => (int)(char)L(-1)), Q(() => (double)(char)I(1000)), Q(() => (char)(byte)I(200)), Q(() => (int)(char)(sbyte)I(-1)),
                Q(() => (char)(I(65) + I(1))), Q(() => 'a' - 'A'), Q(() => (char)('a' - 'A' + 'b')), Q(() => (int)'\uFFFF'), Q(() => (char)I(0xD800) == '\uD800'), Q(() => char.MaxValue == (char)I(65535)), Q(() => (ushort)'A'), Q(() => (short)(char)I(0xFFFF)), Q(() => (long)'Z'));
        }

        static string DecimalBasic()
        {
            decimal a = 1.1m, b = 2.2m;
            decimal c = a + b;
            decimal d = 10m / 3m;
            return Par.J(Q(() => c), Q(() => c == 3.3m), Q(() => d), Q(() => Math.Round(2.5m)), Q(() => Math.Round(3.5m)), Q(() => decimal.MaxValue), Q(() => (int)D(2.5)), Q(() => (decimal)D(0.1)), Q(() => 1m / 3m * 3m), Q(() => decimal.Parse("1.50")), Q(() => (double)d));
        }

        static string CheckedIncrement()
        {
            int a = I(int.MaxValue); byte b = (byte)I(255); long l = L(long.MinValue); uint u = U(0); short s = (short)I(short.MaxValue);
            return Par.J(OV(() => { checked { a++; } return a.ToString(); }), OV(() => { checked { b++; } return b.ToString(); }), OV(() => { checked { l--; } return l.ToString(); }),
                OV(() => { checked { u--; } return u.ToString(); }), OV(() => { checked { s++; } return s.ToString(); }), OV(() => { checked { ++a; } return a.ToString(); }), Q(() => a), Q(() => b), Q(() => l), Q(() => u), Q(() => s));
        }

        static string CheckedCompound()
        {
            int a = I(int.MaxValue); byte b = (byte)I(250); long l = L(long.MaxValue); short s = (short)I(-32760);
            return Par.J(OV(() => { checked { a += I(1); } return a.ToString(); }), OV(() => { checked { b += (byte)I(10); } return b.ToString(); }), OV(() => { checked { l *= L(2); } return l.ToString(); }),
                OV(() => { checked { s -= (short)I(100); } return s.ToString(); }), OV(() => { checked { b <<= I(1); } return b.ToString(); }), OV(() => { checked { a -= I(-1); } return a.ToString(); }));
        }

        static string Float32Accumulate()
        {
            float acc = F(0f); double dacc = D(0);
            for (int i = 0; i < 1000; i++) { acc += F(0.1f); dacc += D(0.1); }
            float prod = F(1f); for (int i = 0; i < 30; i++) prod *= F(1.1f);
            float h = F(1e-8f); float big = F(1e8f); float cancel = (big + h) - big;
            float fma = F(0.1f) * F(10f) - F(1f);
            double dfma = D(0.1) * D(10) - D(1);
            float third = F(1f) / F(3f); float mul = third * F(3f) - F(1f);
            return Par.J(Q(() => acc), Q(() => dacc), Q(() => prod), Q(() => cancel), Q(() => fma), Q(() => dfma), Q(() => mul), Q(() => acc == F(100f)), Q(() => (float)dacc), Q(() => acc.ToString("R")), Q(() => prod.ToString("R")), Q(() => F(1f) / F(7f)), Q(() => (F(1f) / F(7f)) * F(7f)));
        }
    }
}
