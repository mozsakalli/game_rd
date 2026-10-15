using System;
using ParityKit;

// 86: tamsayi aritmetigi paritesi — wrap, isaret, bolme kenarlari, unsigned karsilastirma, ~, shift sayilari.
namespace Demo86
{
    class App86
    {
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static int I(int v) { return v; }          // sabit katlamayi kirmak icin
        static long L(long v) { return v; }
        static uint U(uint v) { return v; }
        static ulong UL(ulong v) { return v; }

        public static int Run() { return Par.Loop(86, 30, Step); }

        static string Step(int k)
        {
            switch (k)
            {
                case 1: return WrapInt();
                case 2: return WrapLong();
                case 3: return WrapSmall();
                case 4: return CompoundNarrow();
                case 5: return IncDecWrap();
                case 6: return MinDiv();
                case 7: return MinRem();
                case 8: return DivZero();
                case 9: return NegDivRem();
                case 10: return NegMin();
                case 11: return AbsMin();
                case 12: return UnsignedDivRem();
                case 13: return UnsignedCmp();
                case 14: return UlongCmp();
                case 15: return Promotion();
                case 16: return CharArith();
                case 17: return BitwiseNeg();
                case 18: return BitNot();
                case 19: return ShlInt();
                case 20: return ShrInt();
                case 21: return ShlLong();
                case 22: return ShrLong();
                case 23: return ShrUnsigned();
                case 24: return ShiftSmall();
                case 25: return MulHigh();
                case 26: return UnsignedToSignedMix();
                case 27: return LongDivZero();
                case 28: return ULongMinus();
                case 29: return SbyteByteMix();
                case 30: return ShiftNegativeCount();
            }
            return "?";
        }

        static string WrapInt()
        {
            int a = I(int.MaxValue), b = I(int.MinValue);
            return Par.J(Q(() => a + 1), Q(() => b - 1), Q(() => a * 2), Q(() => a + a), Q(() => b * -1), Q(() => a * a), Q(() => I(65536) * I(65536)), Q(() => I(-1) * b));
        }

        static string WrapLong()
        {
            long a = L(long.MaxValue), b = L(long.MinValue);
            return Par.J(Q(() => a + 1), Q(() => b - 1), Q(() => a * 2), Q(() => b * -1), Q(() => a * a), Q(() => L(1L << 32) * L(1L << 31)), Q(() => unchecked((long)UL(ulong.MaxValue))));
        }

        static string WrapSmall()
        {
            byte by = (byte)I(255); sbyte sb = (sbyte)I(127); short sh = (short)I(32767); ushort us = (ushort)I(65535);
            // C#: kucuk tipler int'e terfi eder; sonuc int (wrap YOK), cast'te kesilir
            return Par.J(Q(() => by + 1), Q(() => (byte)(by + 1)), Q(() => sb + 1), Q(() => (sbyte)(sb + 1)), Q(() => sh + 1), Q(() => (short)(sh + 1)), Q(() => us + 1), Q(() => (ushort)(us + 1)),
                Q(() => (byte)I(-1)), Q(() => (sbyte)I(255)), Q(() => (short)I(65535)), Q(() => (ushort)I(-1)), Q(() => (byte)I(300)), Q(() => (sbyte)I(-129)));
        }

        static string CompoundNarrow()
        {
            byte b = (byte)I(250); b += 10; byte c = (byte)I(5); c -= 10; short s = (short)I(32760); s += 100;
            sbyte sb = (sbyte)I(120); sb += 10; ushort us = (ushort)I(10); us -= 20; byte m = (byte)I(100); m *= 3;
            byte d = (byte)I(7); d /= 2; byte x = (byte)I(0xF0); x ^= 0xFF; byte sh = (byte)I(1); sh <<= 9;
            return Par.J(Q(() => b), Q(() => c), Q(() => s), Q(() => sb), Q(() => us), Q(() => m), Q(() => d), Q(() => x), Q(() => sh));
        }

        static string IncDecWrap()
        {
            byte b = (byte)I(255); b++; sbyte sb = (sbyte)I(-128); sb--; int i = I(int.MaxValue); i++; long l = L(long.MinValue); l--;
            ushort us = (ushort)I(0); us--; char ch = (char)I(65535); ch++; uint u = U(0); u--; ulong ul = UL(0); ul--;
            return Par.J(Q(() => b), Q(() => sb), Q(() => i), Q(() => l), Q(() => us), Q(() => (int)ch), Q(() => u), Q(() => ul));
        }

        static string MinDiv()
        {
            int a = I(int.MinValue), b = I(-1);
            string r1 = Par.K(() => (a / b).ToString());
            long la = L(long.MinValue), lb = L(-1);
            string r2 = Par.K(() => (la / lb).ToString());
            return Par.J(Q(() => r1), Q(() => r2), Q(() => I(int.MinValue) / I(1)), Q(() => I(int.MinValue) / I(2)), Q(() => I(int.MinValue) / I(int.MinValue)), Q(() => I(7) / I(int.MinValue)));
        }

        static string MinRem()
        {
            int a = I(int.MinValue), b = I(-1);
            string r1 = Par.K(() => (a % b).ToString());
            long la = L(long.MinValue), lb = L(-1);
            string r2 = Par.K(() => (la % lb).ToString());
            return Par.J(Q(() => r1), Q(() => r2), Q(() => I(int.MinValue) % I(2)), Q(() => I(int.MinValue) % I(3)), Q(() => I(int.MinValue) % I(int.MaxValue)), Q(() => I(int.MaxValue) % I(int.MinValue)));
        }

        static string DivZero()
        {
            int z = I(0); long lz = L(0); uint uz = U(0); byte bz = (byte)I(0);
            return Par.J(Par.T(() => (I(5) / z).ToString()), Par.T(() => (I(5) % z).ToString()), Par.T(() => (L(5) / lz).ToString()),
                Par.T(() => (U(5) / uz).ToString()), Par.T(() => (I(5) / bz).ToString()), Par.T(() => (z / z).ToString()),
                Par.T(() => (I(0) % z).ToString()));
        }

        static string NegDivRem()
        {
            return Par.J(Q(() => I(-7) / I(2)), Q(() => I(-7) % I(2)), Q(() => I(7) / I(-2)), Q(() => I(7) % I(-2)), Q(() => I(-7) / I(-2)), Q(() => I(-7) % I(-2)),
                Q(() => L(-7) / L(2)), Q(() => L(-7) % L(2)), Q(() => L(7) % L(-2)), Q(() => (short)I(-7) / (short)I(2)), Q(() => (sbyte)I(-7) % (sbyte)I(2)),
                Q(() => I(-1) / I(2)), Q(() => I(-1) % I(2)), Q(() => I(1) / I(-2)), Q(() => I(-9) / I(3)), Q(() => I(int.MinValue) % I(-2)));
        }

        static string NegMin()
        {
            int a = I(int.MinValue); long b = L(long.MinValue); short s = (short)I(short.MinValue); sbyte sb = (sbyte)I(sbyte.MinValue);
            uint u = U(5); ulong ul = UL(5);
            return Par.J(Q(() => -a), Q(() => -b), Q(() => -s), Q(() => -sb), Q(() => -(long)a), Q(() => -u), Q(() => -(long)ul), Q(() => -I(0)), Q(() => -L(0)));
        }

        static string AbsMin()
        {
            int a = I(int.MinValue); long b = L(long.MinValue); short s = (short)I(short.MinValue); sbyte sb = (sbyte)I(sbyte.MinValue);
            return Par.J(Par.T(() => Math.Abs(a).ToString()), Par.T(() => Math.Abs(b).ToString()), Par.T(() => Math.Abs(s).ToString()),
                Par.T(() => Math.Abs(sb).ToString()), Q(() => Math.Abs(I(-5))), Q(() => Math.Abs(I(int.MaxValue))), Q(() => Math.Abs(L(-1L << 40))), Q(() => Math.Abs(I(0))));
        }

        static string UnsignedDivRem()
        {
            uint a = U(0xFFFFFFFF), b = U(2), c = U(0x80000000);
            ulong la = UL(ulong.MaxValue), lb = UL(3), lc = UL(1UL << 63);
            return Par.J(Q(() => a / b), Q(() => a % b), Q(() => c / b), Q(() => c % U(3)), Q(() => a / c), Q(() => la / lb), Q(() => la % lb), Q(() => lc / lb), Q(() => lc % UL(7)), Q(() => U(7) / a), Q(() => U(7) % a),
                Q(() => (uint)I(-8) / U(2)), Q(() => (ushort)I(65535) / (ushort)I(2)), Q(() => (byte)I(200) / (byte)I(3)));
        }

        static string UnsignedCmp()
        {
            uint a = U(0x80000000), b = U(1), c = U(0xFFFFFFFF);
            int sa = unchecked((int)a);
            return Par.J(Q(() => a > b), Q(() => a < b), Q(() => c > a), Q(() => a >= c), Q(() => c == U(uint.MaxValue)), Q(() => sa < 0), Q(() => sa > (int)b), Q(() => a > (uint)I(5)), Q(() => a.CompareTo(b)), Q(() => c.CompareTo(a)),
                Q(() => b.CompareTo(a)), Q(() => Math.Max(a, b)), Q(() => Math.Min(a, c)), Q(() => U(0) > U(0)), Q(() => U(3) <= U(3)), Q(() => (uint)I(-1) > U(0)));
        }

        static string UlongCmp()
        {
            ulong a = UL(1UL << 63), b = UL(1), c = UL(ulong.MaxValue);
            long sa = unchecked((long)a);
            return Par.J(Q(() => a > b), Q(() => c > a), Q(() => a < c), Q(() => sa < 0), Q(() => a.CompareTo(c)), Q(() => c.CompareTo(b)), Q(() => Math.Max(a, c)), Q(() => Math.Min(a, b)), Q(() => a == UL(0x8000000000000000)),
                Q(() => (ulong)L(-1) == c), Q(() => a / b), Q(() => c - a));
        }

        static string Promotion()
        {
            int i = I(1 << 30); long l = L(4); uint u = U(3); short s = (short)I(-3); ushort us = (ushort)I(3); byte b = (byte)I(200); sbyte sb = (sbyte)I(-100);
            var r1 = i * l;      // long
            var r2 = i * 4;      // int wrap
            var r3 = u + i;      // long (uint+int -> long)
            var r4 = s + us;     // int
            var r5 = b + sb;     // int
            var r6 = u * u;      // uint
            var r7 = -u;         // long
            var r8 = s * s;      // int
            var r9 = b * b;      // int
            var r10 = (byte)(b * b);
            var r11 = u - U(5);  // uint wrap
            var r12 = l * I(int.MaxValue) * I(2);
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => r9), Q(() => r10), Q(() => r11), Q(() => r12), Q(() => r1.GetType().Name), Q(() => r3.GetType().Name), Q(() => r4.GetType().Name), Q(() => r6.GetType().Name), Q(() => r7.GetType().Name));
        }

        static string CharArith()
        {
            char a = (char)I('a'), z = (char)I('z');
            var sum = a + z;           // int
            var diff = z - a;          // int
            char next = (char)(a + 1);
            char big = (char)(a + I(0x10000)); // wrap
            int cmp = a.CompareTo(z);
            bool lt = a < z;
            char up = (char)(a & ~0x20);
            return Par.J(Q(() => sum), Q(() => diff), Q(() => next), Q(() => (int)big), Q(() => cmp), Q(() => lt), Q(() => up), Q(() => sum.GetType().Name), Q(() => a + 'b' == I(195)), Q(() => (char)I(0) == '\0'), Q(() => ((int)'A').ToString()), Q(() => 'A' + 1), Q(() => (char)('A' + 1)));
        }

        static string BitwiseNeg()
        {
            int a = I(-8), b = I(5);
            long la = L(-8), lb = L(5);
            return Par.J(Q(() => a & b), Q(() => a | b), Q(() => a ^ b), Q(() => a & 0xFF), Q(() => a | 0x100), Q(() => a ^ -1), Q(() => la & lb), Q(() => la | lb), Q(() => la ^ lb), Q(() => la & 0xFFFFFFFFL), Q(() => I(-1) & I(int.MaxValue)),
                Q(() => I(int.MinValue) | I(1)), Q(() => I(int.MinValue) ^ I(int.MinValue)), Q(() => (byte)I(0xF0) & (byte)I(0x3C)), Q(() => (short)I(-1) & (ushort)I(0xFFFF)), Q(() => I(-1) >> 31), Q(() => I(int.MinValue) >> 31));
        }

        static string BitNot()
        {
            int a = I(5); uint u = U(5); long l = L(5); ulong ul = UL(5); byte b = (byte)I(5); short s = (short)I(5); sbyte sb = (sbyte)I(5); ushort us = (ushort)I(5);
            int zero = I(0), m1 = I(-1), mn = I(int.MinValue), mx = I(int.MaxValue);
            return Par.J(Q(() => ~a), Q(() => ~u), Q(() => ~l), Q(() => ~ul), Q(() => ~b), Q(() => ~s), Q(() => ~sb), Q(() => ~us), Q(() => ~zero), Q(() => ~m1), Q(() => ~mn), Q(() => ~mx), Q(() => (~a).GetType().Name), Q(() => (~b).GetType().Name), Q(() => (~u).GetType().Name), Q(() => (byte)~b), Q(() => (ushort)~us),
                Q(() => ~a & 0xFF), Q(() => ~(a | 2)), Q(() => ~~a), Q(() => ~a == -a - 1), Q(() => ~U(0)), Q(() => ~UL(0)), Q(() => ~L(long.MinValue)));
        }

        static string ShlInt()
        {
            int one = I(1), neg = I(-1), v = I(0x12345678);
            int[] counts = { 0, 1, 31, 32, 33, 63, 64, 65 };
            var sb = new System.Text.StringBuilder();
            foreach (var c in counts) { int cc = I(c); sb.Append(one << cc).Append(',').Append(neg << cc).Append(',').Append(v << cc).Append(';'); }
            return sb.ToString();
        }

        static string ShrInt()
        {
            int mn = I(int.MinValue), neg = I(-256), v = I(0x12345678), m1 = I(-1);
            int[] counts = { 0, 1, 31, 32, 33, 63, 64, 65 };
            var sb = new System.Text.StringBuilder();
            foreach (var c in counts) { int cc = I(c); sb.Append(mn >> cc).Append(',').Append(neg >> cc).Append(',').Append(v >> cc).Append(',').Append(m1 >> cc).Append(';'); }
            return sb.ToString();
        }

        static string ShlLong()
        {
            long one = L(1), neg = L(-1), v = L(0x123456789ABCDEF0L);
            int[] counts = { 0, 1, 31, 32, 33, 63, 64, 65, 127, 128 };
            var sb = new System.Text.StringBuilder();
            foreach (var c in counts) { int cc = I(c); sb.Append(one << cc).Append(',').Append(neg << cc).Append(',').Append(v << cc).Append(';'); }
            return sb.ToString();
        }

        static string ShrLong()
        {
            long mn = L(long.MinValue), neg = L(-256), v = L(0x123456789ABCDEF0L);
            int[] counts = { 0, 1, 31, 32, 33, 63, 64, 65, 127, 128 };
            var sb = new System.Text.StringBuilder();
            foreach (var c in counts) { int cc = I(c); sb.Append(mn >> cc).Append(',').Append(neg >> cc).Append(',').Append(v >> cc).Append(';'); }
            return sb.ToString();
        }

        static string ShrUnsigned()
        {
            uint u = U(0x80000000), um = U(0xFFFFFFFF); ulong ul = UL(1UL << 63), ulm = UL(ulong.MaxValue);
            int[] counts = { 0, 1, 31, 32, 33, 63, 64, 65 };
            var sb = new System.Text.StringBuilder();
            foreach (var c in counts) { int cc = I(c); sb.Append(u >> cc).Append(',').Append(um >> cc).Append(',').Append(ul >> cc).Append(',').Append(ulm >> cc).Append(',').Append(u << cc).Append(',').Append(ul << cc).Append(';'); }
            // C# 11 >>> (isaretsiz sag kaydirma)
            int neg = I(-256); long lneg = L(-256);
            sb.Append(neg >>> I(4)).Append(',').Append(lneg >>> I(4)).Append(',').Append(neg >>> I(32)).Append(',').Append(I(int.MinValue) >>> I(31));
            return sb.ToString();
        }

        static string ShiftSmall()
        {
            byte b = (byte)I(0x81); sbyte sb = (sbyte)I(-127); short s = (short)I(-32767); ushort us = (ushort)I(0x8001); char ch = (char)I(0x8001);
            int c8 = I(8), c7 = I(7), c16 = I(16), c31 = I(31), c32 = I(32);
            return Par.J(Q(() => b << c8), Q(() => b >> c7), Q(() => sb >> c7), Q(() => sb << c8), Q(() => s >> c16), Q(() => s << c16), Q(() => us << c16), Q(() => ch >> c7), Q(() => b << c31), Q(() => b << c32), Q(() => (b << c8).GetType().Name),
                Q(() => (byte)(b << c8)), Q(() => (sbyte)(sb << I(1))), Q(() => (short)(s << I(1))), Q(() => (ushort)(us << I(1))), Q(() => sb >> c31), Q(() => (sbyte)(sb >> I(1))));
        }

        static string MulHigh()
        {
            int a = I(0x7FFFFFFF), b = I(0x7FFFFFFF);
            long full = (long)a * b;
            int lo = unchecked(a * b);
            uint ua = U(0xFFFFFFFF);
            ulong ufull = (ulong)ua * ua;
            uint ulo = unchecked(ua * ua);
            long hi = Math.BigMul(a, b);
            return Par.J(Q(() => full), Q(() => lo), Q(() => ufull), Q(() => ulo), Q(() => hi), Q(() => I(-3) * I(int.MaxValue)), Q(() => unchecked(L(long.MaxValue) * L(3))), Q(() => (int)(full >> 32)), Q(() => (uint)(ufull >> 32)));
        }

        static string UnsignedToSignedMix()
        {
            uint u = U(0xFFFFFFFF); int i = I(-1); long l = L(-1); ulong ul = UL(ulong.MaxValue);
            return Par.J(Q(() => u == i ? 1 : 0 /* long karsilastirma: 4294967295 == -1 false */), Q(() => (int)u == i), Q(() => (long)u), Q(() => (ulong)i), Q(() => (uint)l), Q(() => (long)ul == l), Q(() => ul == (ulong)l),
                Q(() => u + i /* long */), Q(() => (u + i).GetType().Name), Q(() => (uint)I(int.MinValue)), Q(() => (int)U(2147483648u)), Q(() => (ulong)I(-1)), Q(() => (ushort)I(-2)), Q(() => (sbyte)U(200)), Q(() => (byte)L(-1)), Q(() => (char)I(-1) == (char)65535));
        }

        static string LongDivZero()
        {
            long z = L(0); ulong uz = UL(0);
            return Par.J(Par.T(() => (L(1) / z).ToString()), Par.T(() => (L(1) % z).ToString()), Par.T(() => (UL(1) / uz).ToString()), Par.T(() => (UL(1) % uz).ToString()),
                Par.K(() => (L(long.MinValue) / L(-1)).ToString()), Par.K(() => (L(long.MinValue) % L(-1)).ToString()));
        }

        static string ULongMinus()
        {
            ulong a = UL(3), b = UL(5); uint ua = U(3), ub = U(5);
            return Par.J(Q(() => a - b), Q(() => ua - ub), Q(() => (ua - ub).GetType().Name), Q(() => (long)(a - b)), Q(() => (int)(ua - ub)), Q(() => UL(0) - UL(1)), Q(() => U(0) - U(1)), Q(() => a * UL(ulong.MaxValue)), Q(() => ua * U(uint.MaxValue)));
        }

        static string SbyteByteMix()
        {
            sbyte sb = (sbyte)I(-1); byte b = (byte)I(255); short s = (short)I(-1); ushort us = (ushort)I(65535);
            return Par.J(Q(() => sb == b), Q(() => sb + b), Q(() => s == us), Q(() => s + us), Q(() => (byte)sb == b), Q(() => sb == (sbyte)b), Q(() => (int)sb), Q(() => (int)b), Q(() => (uint)sb), Q(() => (ulong)s), Q(() => (long)us), Q(() => sb < b), Q(() => s < us), Q(() => (byte)sb), Q(() => (sbyte)b), Q(() => (ushort)s), Q(() => (short)us));
        }

        static string ShiftNegativeCount()
        {
            int v = I(1), c = I(-1), c2 = I(-31), c3 = I(-32), c4 = I(-33);
            long lv = L(1);
            uint uv = U(0x80000000);
            return Par.J(Q(() => v << c), Q(() => v << c2), Q(() => v << c3), Q(() => v << c4), Q(() => lv << c), Q(() => lv << c2), Q(() => uv >> c), Q(() => uv >> c3), Q(() => I(-1) >> c), Q(() => I(int.MinValue) >> c), Q(() => L(long.MinValue) >> c));
        }
    }
}
