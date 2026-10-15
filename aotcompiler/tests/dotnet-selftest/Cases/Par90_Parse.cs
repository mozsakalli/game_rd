using System;
using System.Globalization;
using ParityKit;

// 90: Parse/TryParse/Convert kenarlari + Enum API'si (Parse/GetNames/GetValues sirasi/IsDefined/ToObject).
namespace Demo90
{
    enum Color { Red = 1, Green = 2, Blue = 4 }
    [Flags] enum Perm : byte { None = 0, R = 1, W = 2, X = 4 }
    enum Mixed : short { Neg = -3, Zero = 0, Pos = 7, Big = short.MaxValue, Dup = 7 }
    enum Unordered : uint { C = 3, A = 1, B = 2, Max = uint.MaxValue }
    enum SignedOrder { M2 = -2, P1 = 1, M1 = -1, Z = 0 }

    class App90
    {
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static int I(int v) { return v; }
        static string S(string v) { return v; }

        public static int Run() { return Par.Loop(90, 26, Step); }

        static string Step(int k)
        {
            switch (k)
            {
                case 1: return IntParseOk();
                case 2: return IntParseFail();
                case 3: return IntTryParse();
                case 4: return LongUnsignedParse();
                case 5: return SmallParse();
                case 6: return DoubleParseOk();
                case 7: return DoubleParseFail();
                case 8: return DoubleParseSpecial();
                case 9: return FloatParse();
                case 10: return NumStyles();
                case 11: return BoolCharParse();
                case 12: return ConvertToInt();
                case 13: return ConvertRounding();
                case 14: return ConvertBase();
                case 15: return ConvertMisc();
                case 16: return ConvertOverflow();
                case 17: return EnumParse();
                case 18: return EnumTryParse();
                case 19: return EnumNamesValues();
                case 20: return EnumIsDefined();
                case 21: return EnumToObject();
                case 22: return EnumCompare();
                case 23: return EnumUnderlying();
                case 24: return HexParse();
                case 25: return ParseSpan();
                case 26: return ChangeType();
            }
            return "?";
        }

        static string P(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }

        static string IntParseOk()
        {
            return Par.J(Q(() => int.Parse(S("42"))), Q(() => int.Parse(S("-42"))), Q(() => int.Parse(S("+42"))), Q(() => int.Parse(S("  42  "))), Q(() => int.Parse(S("\t42\n"))), Q(() => int.Parse(S("-0"))), Q(() => int.Parse(S("007"))), Q(() => int.Parse(S("2147483647"))), Q(() => int.Parse(S("-2147483648"))), Q(() => int.Parse(S("0"))), Q(() => int.Parse(S("00000000000000000000001"))), Q(() => int.Parse(S(" -1 "))), Q(() => int.Parse(S("1,000"), NumberStyles.AllowThousands)), Q(() => int.Parse(S("(5)"), NumberStyles.AllowParentheses)), Q(() => int.Parse(S("5 "), NumberStyles.AllowTrailingWhite)), Q(() => int.Parse(S("ff"), NumberStyles.HexNumber)), Q(() => int.Parse(S("FFFFFFFF"), NumberStyles.HexNumber)), Q(() => int.Parse(S("7FFFFFFF"), NumberStyles.HexNumber)), Q(() => int.Parse(S("80000000"), NumberStyles.HexNumber)), Q(() => int.Parse(S("1e2"), NumberStyles.AllowExponent)), Q(() => int.Parse(S("1.0"), NumberStyles.AllowDecimalPoint)), Q(() => int.Parse(S("1.5"), NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)));
        }

        static string IntParseFail()
        {
            return Par.J(P(() => int.Parse(S("2147483648"))), P(() => int.Parse(S("-2147483649"))), P(() => int.Parse(S(""))), P(() => int.Parse(S(" "))), P(() => int.Parse((string)null)), P(() => int.Parse(S("abc"))), P(() => int.Parse(S("0x10"))), P(() => int.Parse(S("1.0"))), P(() => int.Parse(S("1,000"))), P(() => int.Parse(S("1 000"))), P(() => int.Parse(S("--1"))), P(() => int.Parse(S("+-1"))), P(() => int.Parse(S("1-"))), P(() => int.Parse(S("- 1"))), P(() => int.Parse(S("1e2"))), P(() => int.Parse(S("٤٢"))), P(() => int.Parse(S("4 2"))), P(() => int.Parse(S("\u00A042"))), P(() => int.Parse(S("42\0"))), P(() => int.Parse(S("99999999999999999999"))), P(() => int.Parse(S("1_000"))), P(() => int.Parse(S("١"))), P(() => int.Parse(S("Infinity"))), P(() => int.Parse(S("(5)"))), P(() => int.Parse(S("5.0"), NumberStyles.Integer)), P(() => int.Parse(S("1.5"), NumberStyles.AllowDecimalPoint)));
        }

        static string IntTryParse()
        {
            int a, b, c, d, e, f, g;
            bool ra = int.TryParse(S("42"), out a), rb = int.TryParse(S("x"), out b), rc = int.TryParse(S("2147483648"), out c), rd = int.TryParse((string)null, out d), re = int.TryParse(S(""), out e), rf = int.TryParse(S(" -7 "), out f), rg = int.TryParse(S("ff"), NumberStyles.HexNumber, null, out g);
            long la; bool rla = long.TryParse(S("9223372036854775808"), out la);
            uint ua; bool rua = uint.TryParse(S("-1"), out ua);
            byte ba; bool rba = byte.TryParse(S("256"), out ba);
            double da; bool rda = double.TryParse(S("1e400"), out da);
            double db; bool rdb = double.TryParse(S("abc"), out db);
            bool rh = int.TryParse(S("12"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h);
            return Par.J(Q(() => ra), Q(() => a), Q(() => rb), Q(() => b), Q(() => rc), Q(() => c), Q(() => rd), Q(() => d), Q(() => re), Q(() => e), Q(() => rf), Q(() => f), Q(() => rg), Q(() => g), Q(() => rla), Q(() => la), Q(() => rua), Q(() => ua), Q(() => rba), Q(() => ba), Q(() => rda), Q(() => da), Q(() => rdb), Q(() => db), Q(() => rh), Q(() => h));
        }

        static string LongUnsignedParse()
        {
            return Par.J(Q(() => long.Parse(S("-9223372036854775808"))), Q(() => long.Parse(S("9223372036854775807"))), P(() => long.Parse(S("9223372036854775808"))), P(() => long.Parse(S("-9223372036854775809"))), Q(() => ulong.Parse(S("18446744073709551615"))), P(() => ulong.Parse(S("18446744073709551616"))), P(() => ulong.Parse(S("-1"))), P(() => ulong.Parse(S("-0"))), Q(() => uint.Parse(S("4294967295"))), P(() => uint.Parse(S("4294967296"))), P(() => uint.Parse(S("-1"))), Q(() => uint.Parse(S("+0"))), Q(() => ulong.Parse(S("FFFFFFFFFFFFFFFF"), NumberStyles.HexNumber)), Q(() => long.Parse(S("FFFFFFFFFFFFFFFF"), NumberStyles.HexNumber)), Q(() => long.Parse(S("8000000000000000"), NumberStyles.HexNumber)), P(() => long.Parse(S("10000000000000000"), NumberStyles.HexNumber)), P(() => uint.Parse(S("-5"), NumberStyles.AllowLeadingSign)));
        }

        static string SmallParse()
        {
            return Par.J(Q(() => byte.Parse(S("255"))), P(() => byte.Parse(S("256"))), P(() => byte.Parse(S("-1"))), P(() => byte.Parse(S("-0"))), Q(() => sbyte.Parse(S("-128"))), P(() => sbyte.Parse(S("128"))), P(() => sbyte.Parse(S("-129"))), Q(() => short.Parse(S("-32768"))), P(() => short.Parse(S("32768"))), Q(() => ushort.Parse(S("65535"))), P(() => ushort.Parse(S("65536"))), P(() => ushort.Parse(S("-1"))), Q(() => byte.Parse(S("FF"), NumberStyles.HexNumber)), Q(() => sbyte.Parse(S("FF"), NumberStyles.HexNumber)), Q(() => sbyte.Parse(S("80"), NumberStyles.HexNumber)), P(() => sbyte.Parse(S("100"), NumberStyles.HexNumber)), Q(() => short.Parse(S("FFFF"), NumberStyles.HexNumber)), P(() => byte.Parse(S("1FF"), NumberStyles.HexNumber)), Q(() => byte.Parse(S(" 1 "))), P(() => byte.Parse(S("1.0"))));
        }

        static string DoubleParseOk()
        {
            string[] xs = { "1", "1.0", "1.5", ".5", "5.", "-.5", "-5.", "+1.5", "1e3", "1E3", "1e+3", "1e-3", "1.5e-3", "-1.5E+3", "0.1", "0.30000000000000004", "1,000", "1,000.5", "1,0,0,0", " 1.5 ", "\t1.5\n", "1e308", "1.7976931348623157e308", "4.9e-324", "5e-324", "2.5e-324", "2.4e-324", "1e-400", "0", "-0", "0.0", "-0.0", "00001.5000", "123456789012345678901234567890", "0.000000000000000000000000000001", "1e22", "1e23", "9007199254740993", "1.00000000000000011102230246251565404236316680908203125", "1.0000000000000001", "3.14159265358979323846264338327950288", "2.2250738585072011e-308", "0x10" };
            var sb = new System.Text.StringBuilder();
            foreach (var s0 in xs) sb.Append(P(() => double.Parse(S(s0), CultureInfo.InvariantCulture).ToString("R"))).Append(';');
            return sb.ToString();
        }

        static string DoubleParseFail()
        {
            string[] xs = { "", " ", "abc", "1.5.5", "1e", "e5", "1e5.5", "--1", "1-", ".", "-", "+", "1 5", "1_000", "1,5", "٫5", "1.5f", "1.5d", "1.5m", "∞", "nan", "inf", "+Infinity", "INFINITY", "NAN", "-NaN", "0x1p3", "1e+", "1e-", ".e5", "5e400x", "\u00A01.5" };
            var sb = new System.Text.StringBuilder();
            foreach (var s0 in xs) sb.Append(P(() => double.Parse(S(s0), CultureInfo.InvariantCulture).ToString("R"))).Append(';');
            sb.Append(P(() => double.Parse((string)null)));
            return sb.ToString();
        }

        static string DoubleParseSpecial()
        {
            return Par.J(Q(() => double.Parse(S("NaN"), CultureInfo.InvariantCulture)), Q(() => double.Parse(S("Infinity"), CultureInfo.InvariantCulture)), Q(() => double.Parse(S("-Infinity"), CultureInfo.InvariantCulture)), P(() => double.Parse(S("+Infinity"), CultureInfo.InvariantCulture)), P(() => double.Parse(S("∞"), CultureInfo.InvariantCulture)), P(() => double.Parse(S("-∞"), CultureInfo.InvariantCulture)), P(() => double.Parse(S("nan"), CultureInfo.InvariantCulture)), P(() => double.Parse(S("-NaN"), CultureInfo.InvariantCulture)), P(() => double.Parse(S(" NaN "), CultureInfo.InvariantCulture)), Q(() => double.Parse(S("1e309"), CultureInfo.InvariantCulture)), Q(() => double.Parse(S("-1e309"), CultureInfo.InvariantCulture)), Q(() => double.Parse(S("1e-324"), CultureInfo.InvariantCulture)), Q(() => double.Parse(S("-1e-400"), CultureInfo.InvariantCulture).ToString()), Q(() => 1 / double.Parse(S("-0"), CultureInfo.InvariantCulture)), Q(() => 1 / double.Parse(S("-0.0e5"), CultureInfo.InvariantCulture)), Q(() => double.Parse(S("1.7976931348623158e308"), CultureInfo.InvariantCulture)), Q(() => double.Parse(S("1.7976931348623159e308"), CultureInfo.InvariantCulture)), Q(() => double.Parse(S("179769313486231580000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"), CultureInfo.InvariantCulture)), Q(() => double.Parse(S("179769313486231581000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"), CultureInfo.InvariantCulture)));
        }

        static string FloatParse()
        {
            return Par.J(Q(() => float.Parse(S("0.1"), CultureInfo.InvariantCulture).ToString("R")), Q(() => float.Parse(S("3.4028235e38"), CultureInfo.InvariantCulture)), Q(() => float.Parse(S("3.4028236e38"), CultureInfo.InvariantCulture)), Q(() => float.Parse(S("3.5e38"), CultureInfo.InvariantCulture)), Q(() => float.Parse(S("1e39"), CultureInfo.InvariantCulture)), Q(() => float.Parse(S("1e-46"), CultureInfo.InvariantCulture)), Q(() => float.Parse(S("1.4e-45"), CultureInfo.InvariantCulture).ToString("R")), Q(() => float.Parse(S("7e-46"), CultureInfo.InvariantCulture).ToString("R")), Q(() => float.Parse(S("16777217"), CultureInfo.InvariantCulture).ToString("R")), Q(() => float.Parse(S("0.30000000000000004"), CultureInfo.InvariantCulture).ToString("R")), Q(() => float.Parse(S("1.00000006"), CultureInfo.InvariantCulture).ToString("R")), Q(() => float.Parse(S("1.00000005"), CultureInfo.InvariantCulture).ToString("R")), Q(() => float.Parse(S("NaN"), CultureInfo.InvariantCulture)), Q(() => float.Parse(S("-Infinity"), CultureInfo.InvariantCulture)), P(() => float.Parse(S(""))), Q(() => (double)float.Parse(S("0.1"), CultureInfo.InvariantCulture)), Q(() => float.Parse(S("1.1754943508222875e-38"), CultureInfo.InvariantCulture).ToString("R")), Q(() => float.Parse(S("1.17549435e-38"), CultureInfo.InvariantCulture) == float.Epsilon * 8388608f), Q(() => float.Parse(S("2.3509886e-38"), CultureInfo.InvariantCulture).ToString("R")));
        }

        static string NumStyles()
        {
            var inv = CultureInfo.InvariantCulture;
            return Par.J(P(() => double.Parse(S("1,000.5"), System.Globalization.NumberStyles.Float, inv)), Q(() => double.Parse(S("1,000.5"), System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, inv)), P(() => double.Parse(S("1e3"), System.Globalization.NumberStyles.Number, inv)), P(() => double.Parse(S(" 1"), System.Globalization.NumberStyles.None, inv)), P(() => double.Parse(S("-1"), System.Globalization.NumberStyles.None, inv)), Q(() => double.Parse(S("1"), System.Globalization.NumberStyles.None, inv)), P(() => double.Parse(S("1.5"), System.Globalization.NumberStyles.None, inv)), Q(() => double.Parse(S("(1.5)"), System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowParentheses, inv)), P(() => double.Parse(S("$1.5"), System.Globalization.NumberStyles.Float, inv)), Q(() => double.Parse(S("$1.5"), System.Globalization.NumberStyles.Currency, inv)), P(() => double.Parse(S("1.5$"), System.Globalization.NumberStyles.Currency, inv)), P(() => double.Parse(S("ff"), System.Globalization.NumberStyles.HexNumber, inv)), P(() => int.Parse(S("-ff"), System.Globalization.NumberStyles.HexNumber, inv)), P(() => int.Parse(S("0xff"), System.Globalization.NumberStyles.HexNumber, inv)), Q(() => int.Parse(S(" ff "), System.Globalization.NumberStyles.HexNumber, inv)), P(() => int.Parse(S("ff"), System.Globalization.NumberStyles.AllowHexSpecifier | System.Globalization.NumberStyles.AllowLeadingSign, inv)), Q(() => int.Parse(S("1 "), System.Globalization.NumberStyles.AllowTrailingWhite, inv)), P(() => int.Parse(S(" 1"), System.Globalization.NumberStyles.AllowTrailingWhite, inv)), Q(() => int.Parse(S("-1"), System.Globalization.NumberStyles.AllowLeadingSign, inv)), P(() => int.Parse(S("1-"), System.Globalization.NumberStyles.AllowLeadingSign, inv)), Q(() => int.Parse(S("1-"), System.Globalization.NumberStyles.AllowTrailingSign, inv)), P(() => int.Parse(S("(1)"), System.Globalization.NumberStyles.AllowParentheses | System.Globalization.NumberStyles.AllowLeadingSign, inv)), P(() => int.Parse(S("-(1)"), System.Globalization.NumberStyles.AllowParentheses | System.Globalization.NumberStyles.AllowLeadingSign, inv)));
        }

        static string BoolCharParse()
        {
            bool b1, b2, b3;
            bool r1 = bool.TryParse(S("TRUE"), out b1), r2 = bool.TryParse(S("yes"), out b2), r3 = bool.TryParse(S(" false "), out b3);
            return Par.J(Q(() => bool.Parse(S("true"))), Q(() => bool.Parse(S("True"))), Q(() => bool.Parse(S("TRUE"))), Q(() => bool.Parse(S("tRuE"))), Q(() => bool.Parse(S(" true "))), Q(() => bool.Parse(S("\tfalse\n"))), P(() => bool.Parse(S("yes"))), P(() => bool.Parse(S("1"))), P(() => bool.Parse(S("0"))), P(() => bool.Parse(S(""))), P(() => bool.Parse((string)null)), P(() => bool.Parse(S("true "))), P(() => bool.Parse(S("t"))), P(() => bool.Parse(S("True\0"))), Q(() => r1), Q(() => b1), Q(() => r2), Q(() => b2), Q(() => r3), Q(() => b3),
                Q(() => char.Parse(S("a"))), P(() => char.Parse(S("ab"))), P(() => char.Parse(S(""))), P(() => char.Parse((string)null)), Q(() => char.Parse(S(" "))), Q(() => char.Parse(S("\uD83D")).ToString().Length), char.TryParse(S("x"), out char c1), Q(() => c1), char.TryParse(S("xy"), out char c2), Q(() => (int)c2));
        }

        static string ConvertToInt()
        {
            return Par.J(Q(() => Convert.ToInt32(S("12"))), Q(() => Convert.ToInt32(S(" -12 "))), P(() => Convert.ToInt32(S("12.5"))), P(() => Convert.ToInt32(S(""))), Q(() => Convert.ToInt32((string)null)), Q(() => Convert.ToInt32((object)null)), P(() => Convert.ToInt32(S("x"))), Q(() => Convert.ToInt32(true)), Q(() => Convert.ToInt32(false)), Q(() => Convert.ToInt32('A')), Q(() => Convert.ToInt32((byte)I(200))), Q(() => Convert.ToInt32((sbyte)I(-1))), Q(() => Convert.ToInt32(1.5f)), Q(() => Convert.ToInt32(2.5f)), Q(() => Convert.ToInt32(3.5)), Q(() => Convert.ToInt32(-2.5)), Q(() => Convert.ToInt32(-3.5)), Q(() => Convert.ToInt32(2.5m)), Q(() => Convert.ToInt32(3.5m)), Q(() => Convert.ToInt32(0.5)), Q(() => Convert.ToInt32(1.5)), Q(() => Convert.ToInt32(-0.5)), Q(() => Convert.ToInt32(-1.5)), Q(() => Convert.ToInt32(0.49999999999999994)), Q(() => Convert.ToInt32(2147483647.4)), P(() => Convert.ToInt32(2147483647.5)), P(() => Convert.ToInt32(2147483648.0)), Q(() => Convert.ToInt32(-2147483648.5)), P(() => Convert.ToInt32(-2147483648.51)), P(() => Convert.ToInt32(double.NaN)), P(() => Convert.ToInt32(double.PositiveInfinity)), Q(() => Convert.ToInt32(Color.Blue)), Q(() => Convert.ToInt32((object)I(7))), Q(() => Convert.ToInt32((object)S("8"))), Q(() => Convert.ToInt32((object)true)), Q(() => Convert.ToInt32((object)2.5)), Q(() => Convert.ToInt32(L(5))), P(() => Convert.ToInt32(L(1L << 40))), Q(() => Convert.ToInt32(uint.MaxValue - uint.MaxValue)), P(() => Convert.ToInt32(uint.MaxValue)), P(() => Convert.ToInt32(DateTime.MinValue)), P(() => Convert.ToInt32(S("1e2"), CultureInfo.InvariantCulture)));
        }

        static string ConvertRounding()
        {
            double[] xs = { 0.5, 1.5, 2.5, 3.5, -0.5, -1.5, -2.5, 4.5000001, 4.4999999, 1e15 + 0.5, 0.49999999999999994, 2.5000000000000004, 1234.5, 1235.5 };
            var sb = new System.Text.StringBuilder();
            foreach (var x0 in xs) { double x = x0 + I(0); sb.Append(Convert.ToInt32(x)).Append(',').Append(Convert.ToInt64(x)).Append(',').Append(Convert.ToInt16(x)).Append(',').Append(Convert.ToByte(Math.Abs(x) % 200)).Append(',').Append(Convert.ToInt32((float)x)).Append(';'); }
            sb.Append(Convert.ToByte(0.5)).Append(',').Append(Convert.ToByte(1.5)).Append(',').Append(Convert.ToSByte(-0.5)).Append(',').Append(Convert.ToSByte(-1.5)).Append(',').Append(Convert.ToUInt32(0.5)).Append(',').Append(Convert.ToUInt32(1.5)).Append(',').Append(Convert.ToUInt64(2.5)).Append(',').Append(Convert.ToUInt16(3.5)).Append(',').Append(P(() => Convert.ToUInt32(-0.5))).Append(',').Append(P(() => Convert.ToUInt32(-0.6))).Append(',').Append(P(() => Convert.ToByte(255.5))).Append(',').Append(Convert.ToByte(255.4)).Append(',').Append(P(() => Convert.ToByte(-0.5))).Append(',').Append(P(() => Convert.ToByte(-0.6)));
            return sb.ToString();
        }

        static string ConvertBase()
        {
            return Par.J(Q(() => Convert.ToInt32(S("ff"), 16)), Q(() => Convert.ToInt32(S("FF"), 16)), Q(() => Convert.ToInt32(S("0xff"), 16)), Q(() => Convert.ToInt32(S("0XFF"), 16)), Q(() => Convert.ToInt32(S("1010"), 2)), Q(() => Convert.ToInt32(S("777"), 8)), Q(() => Convert.ToInt32(S("-10"), 10)), P(() => Convert.ToInt32(S("-10"), 16)), P(() => Convert.ToInt32(S("ff"), 10)), P(() => Convert.ToInt32(S("12"), 3)), P(() => Convert.ToInt32(S("ff"), 0)), Q(() => Convert.ToInt32(S("FFFFFFFF"), 16)), P(() => Convert.ToInt32(S("1FFFFFFFF"), 16)), Q(() => Convert.ToInt32(S("80000000"), 16)), Q(() => Convert.ToInt32((string)null, 16)), P(() => Convert.ToInt32(S(""), 16)), P(() => Convert.ToInt32(S(" ff"), 16)), P(() => Convert.ToInt32(S("ff "), 16)), Q(() => Convert.ToByte(S("ff"), 16)), P(() => Convert.ToByte(S("1ff"), 16)), Q(() => Convert.ToSByte(S("ff"), 16)), Q(() => Convert.ToInt64(S("FFFFFFFFFFFFFFFF"), 16)), Q(() => Convert.ToUInt32(S("FFFFFFFF"), 16)), Q(() => Convert.ToInt16(S("FFFF"), 16)), Q(() => Convert.ToInt32(S("11111111111111111111111111111111"), 2)), Q(() => Convert.ToInt32(S("37777777777"), 8)),
                Q(() => Convert.ToString(I(255), 2)), Q(() => Convert.ToString(I(255), 8)), Q(() => Convert.ToString(I(255), 16)), Q(() => Convert.ToString(I(-1), 2)), Q(() => Convert.ToString(I(-1), 16)), Q(() => Convert.ToString(I(-255), 16)), Q(() => Convert.ToString(I(0), 2)), Q(() => Convert.ToString(L(-1), 16)), Q(() => Convert.ToString(L(255), 2)), Q(() => Convert.ToString((byte)I(255), 2)), Q(() => Convert.ToString((sbyte)I(-1), 2)), Q(() => Convert.ToString((short)I(-1), 16)), Q(() => Convert.ToString(I(255), 10)), P(() => Convert.ToString(I(255), 3)), Q(() => Convert.ToString(I(int.MinValue), 2)), Q(() => Convert.ToString(I(int.MinValue), 16)));
        }

        static long L(long v) { return v; }

        static string ConvertMisc()
        {
            return Par.J(Q(() => Convert.ToDouble(S("1.5"), CultureInfo.InvariantCulture)), Q(() => Convert.ToDouble(I(5))), Q(() => Convert.ToDouble(true)), Q(() => Convert.ToDouble((object)null)), Q(() => Convert.ToDouble((string)null)), P(() => Convert.ToDouble(S(""))), P(() => Convert.ToDouble('A')), Q(() => Convert.ToDouble(1.5f)), Q(() => Convert.ToDouble(0.1f)), Q(() => Convert.ToSingle(0.1)), Q(() => Convert.ToSingle(S("0.1"), CultureInfo.InvariantCulture)), Q(() => Convert.ToSingle(1e40)), Q(() => Convert.ToBoolean(I(1))), Q(() => Convert.ToBoolean(I(0))), Q(() => Convert.ToBoolean(I(-5))), Q(() => Convert.ToBoolean(0.0)), Q(() => Convert.ToBoolean(double.NaN)), Q(() => Convert.ToBoolean(S("True"))), P(() => Convert.ToBoolean(S("1"))), P(() => Convert.ToBoolean('t')), Q(() => Convert.ToBoolean((object)null)), Q(() => Convert.ToChar(I(65))), P(() => Convert.ToChar(I(-1))), P(() => Convert.ToChar(I(65536))), Q(() => Convert.ToChar(S("a"))), P(() => Convert.ToChar(S("ab"))), P(() => Convert.ToChar(1.5)), P(() => Convert.ToChar(true)), Q(() => Convert.ToChar((byte)I(66))), Q(() => Convert.ToString(I(5))), Q(() => Convert.ToString((object)null)), Q(() => Convert.ToString((string)null)), Q(() => Convert.ToString(1.5, CultureInfo.InvariantCulture)), Q(() => Convert.ToString(true)), Q(() => Convert.ToString('x')), Q(() => Convert.ToString(Color.Red)), Q(() => Convert.ToString(0.1f, CultureInfo.InvariantCulture)), Q(() => Convert.ToString(new object())), Q(() => Convert.ToByte('A')), P(() => Convert.ToByte('Ā')), Q(() => Convert.ToInt64(S("-9223372036854775808"))), Q(() => Convert.ToUInt64(S("18446744073709551615"))), Q(() => Convert.ToInt16(S("-32768"))), Q(() => Convert.ToUInt16(S("65535"))), Q(() => Convert.ToDecimal(S("1.5"), CultureInfo.InvariantCulture)), Q(() => Convert.ToDecimal(1.1)), Q(() => Convert.ToDecimal(0.1f)), Q(() => Convert.GetTypeCode(I(1))), Q(() => Convert.GetTypeCode(S(""))), Q(() => Convert.GetTypeCode(null)), Q(() => Convert.GetTypeCode(Color.Red)), Q(() => Convert.GetTypeCode(1.5m)), Q(() => Convert.GetTypeCode(new object())), Q(() => Convert.IsDBNull(null)), Q(() => Convert.IsDBNull(DBNull.Value)));
        }

        static string ConvertOverflow()
        {
            return Par.J(P(() => Convert.ToByte(I(256))), P(() => Convert.ToByte(I(-1))), Q(() => Convert.ToByte(I(255))), P(() => Convert.ToSByte(I(128))), P(() => Convert.ToInt16(I(32768))), P(() => Convert.ToUInt16(I(-1))), P(() => Convert.ToUInt32(I(-1))), P(() => Convert.ToUInt64(L(-1))), P(() => Convert.ToInt32(L(2147483648L))), P(() => Convert.ToInt64(double.MaxValue)), P(() => Convert.ToInt64(9.3e18)), Q(() => Convert.ToInt64(9.2e18)), P(() => Convert.ToUInt64(-0.6)), Q(() => Convert.ToUInt64(-0.5)), P(() => Convert.ToUInt64(1.9e19)), P(() => Convert.ToByte(S("256"))), P(() => Convert.ToInt32(S("2147483648"))), P(() => Convert.ToByte(255.5)), P(() => Convert.ToInt16(32767.5)), P(() => Convert.ToInt32(1e10f)), P(() => Convert.ToInt32(ulong.MaxValue)), P(() => Convert.ToByte(300m)), P(() => Convert.ToInt32(float.NaN)), P(() => Convert.ToInt64(float.NegativeInfinity)), P(() => Convert.ToSByte(S("-129"))), Q(() => Convert.ToSByte(S("-128"))), P(() => Convert.ToUInt16(70000.0)), P(() => Convert.ToChar(L(70000))), P(() => Convert.ToByte(S("-1"))), P(() => Convert.ToUInt32(S("-1"))));
        }

        static string EnumParse()
        {
            return Par.J(Q(() => Enum.Parse(typeof(Color), S("Red"))), Q(() => Enum.Parse(typeof(Color), S("red"), true)), P(() => Enum.Parse(typeof(Color), S("red"))), P(() => Enum.Parse(typeof(Color), S("Purple"))), Q(() => Enum.Parse(typeof(Color), S("2"))), Q(() => Enum.Parse(typeof(Color), S("7"))), Q(() => Enum.Parse(typeof(Color), S("-1"))), Q(() => Enum.Parse(typeof(Color), S(" Red "))), Q(() => Enum.Parse(typeof(Color), S("Red, Green"))), Q(() => Enum.Parse(typeof(Color), S("Red,Green,Blue"))), Q(() => Enum.Parse(typeof(Color), S("Red , Blue"))), P(() => Enum.Parse(typeof(Color), S(""))), P(() => Enum.Parse(typeof(Color), S(" "))), P(() => Enum.Parse(typeof(Color), (string)null)), P(() => Enum.Parse(typeof(Color), S("Red,"))), P(() => Enum.Parse(typeof(Color), S(",Red"))), P(() => Enum.Parse(typeof(Color), S("Red Green"))), P(() => Enum.Parse(typeof(int), S("5"))), P(() => Enum.Parse(typeof(Color), S("4294967295"))), P(() => Enum.Parse(typeof(Color), S("2147483648"))), Q(() => Enum.Parse<Color>(S("Blue"))), Q(() => Enum.Parse<Perm>(S("R, W"))), Q(() => Enum.Parse<Perm>(S("3"))), Q(() => Enum.Parse<Perm>(S("r, x"), true)), P(() => Enum.Parse<Perm>(S("256"))), P(() => Enum.Parse<Perm>(S("-1"))), Q(() => Enum.Parse<Mixed>(S("-3"))), Q(() => Enum.Parse<Mixed>(S("Dup"))), Q(() => Enum.Parse<Mixed>(S("7"))), Q(() => Enum.Parse<Unordered>(S("4294967295"))), Q(() => Enum.Parse<SignedOrder>(S("-1"))), Q(() => Enum.Parse<Color>(S("+1"))), P(() => Enum.Parse<Color>(S("1.0"))), P(() => Enum.Parse<Color>(S("0x1"))), Q(() => Enum.Parse(typeof(Color), S("Red")).GetType().Name));
        }

        static string EnumTryParse()
        {
            Color c1, c2, c3, c4; Perm p1; Mixed m1; object o1;
            return Par.J(Enum.TryParse(S("Green"), out c1), Q(() => c1), Enum.TryParse(S("green"), out c2), Q(() => c2), Enum.TryParse(S("green"), true, out c3), Q(() => c3), Enum.TryParse(S("99"), out c4), Q(() => c4), Enum.TryParse(S("R, W, X"), out p1), Q(() => p1), Enum.TryParse(S("Big"), out m1), Q(() => m1), Enum.TryParse(typeof(Color), S("Blue"), out o1), Q(() => o1), Enum.TryParse(S(""), out Color c5), Q(() => c5), Enum.TryParse((string)null, out Color c6), Q(() => c6), Enum.TryParse(S("Red,Blue"), out Color c7), Q(() => c7), Q(() => (int)c7), Enum.TryParse(S("-2"), out SignedOrder so), Q(() => so), Enum.TryParse(S("65536"), out Mixed m2), Q(() => m2), Enum.TryParse<Perm>(S("256"), out Perm p2), Q(() => p2), Enum.TryParse<Unordered>(S("-1"), out Unordered u1), Q(() => u1), Enum.TryParse(S("  Red  "), out Color c8), Q(() => c8), Enum.TryParse(S("Red\0"), out Color c9), Q(() => c9));
        }

        static string EnumNamesValues()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var n in Enum.GetNames(typeof(Color))) sb.Append(n).Append(',');
            sb.Append('|');
            foreach (var v in Enum.GetValues(typeof(Color))) sb.Append((int)v).Append(',');
            sb.Append('|');
            foreach (var n in Enum.GetNames(typeof(Mixed))) sb.Append(n).Append(','); // Dup=7 ile Pos=7: hangi ad?
            sb.Append('|');
            foreach (var v in Enum.GetValues(typeof(Mixed))) sb.Append((short)v).Append(',');
            sb.Append('|');
            foreach (var n in Enum.GetNames(typeof(Unordered))) sb.Append(n).Append(','); // deger sirasi: A,B,C,Max
            sb.Append('|');
            foreach (var n in Enum.GetNames(typeof(SignedOrder))) sb.Append(n).Append(','); // isaretli sira: .NET unsigned siralar -> Z,P1,M2,M1
            sb.Append('|');
            foreach (var v in Enum.GetValues(typeof(SignedOrder))) sb.Append((int)v).Append(',');
            sb.Append('|');
            foreach (var n in Enum.GetNames<Perm>()) sb.Append(n).Append(',');
            sb.Append('|');
            foreach (var v in Enum.GetValues<Perm>()) sb.Append((byte)v).Append(',');
            sb.Append('|').Append(Enum.GetValues(typeof(Color)).GetType().Name).Append('|').Append(Enum.GetValues(typeof(Color)).Length).Append('|').Append(Enum.GetName(typeof(Color), I(2))).Append('|').Append(Enum.GetName(typeof(Color), I(3)) == null).Append('|').Append(Enum.GetName(typeof(Mixed), (short)I(7))).Append('|').Append(Enum.GetName<Color>((Color)I(4))).Append('|').Append(P(() => Enum.GetName(typeof(Color), L(2)))).Append('|').Append(P(() => Enum.GetName(typeof(Color), (byte)I(2)))).Append('|').Append(P(() => Enum.GetNames(typeof(int)).Length)).Append('|').Append(Enum.GetUnderlyingType(typeof(Perm)).Name).Append('|').Append(Enum.GetUnderlyingType(typeof(Big2)).Name).Append('|').Append(((Mixed)I(7)).ToString()).Append('|').Append(Mixed.Pos == Mixed.Dup).Append('|').Append(Mixed.Dup.ToString()).Append('|').Append(((Color)I(6)).ToString()).Append('|').Append(((Perm)I(6)).ToString());
            return sb.ToString();
        }

        enum Big2 : ulong { X = ulong.MaxValue }

        static string EnumIsDefined()
        {
            return Par.J(Q(() => Enum.IsDefined(typeof(Color), I(1))), Q(() => Enum.IsDefined(typeof(Color), I(3))), Q(() => Enum.IsDefined(typeof(Color), S("Red"))), Q(() => Enum.IsDefined(typeof(Color), S("red"))), Q(() => Enum.IsDefined(typeof(Color), S("Red, Green"))), Q(() => Enum.IsDefined(typeof(Color), Color.Blue)), Q(() => Enum.IsDefined(typeof(Color), (Color)I(7))), P(() => Enum.IsDefined(typeof(Color), L(1))), P(() => Enum.IsDefined(typeof(Color), (byte)I(1))), P(() => Enum.IsDefined(typeof(Color), 1.0)), P(() => Enum.IsDefined(typeof(Color), Perm.R)), P(() => Enum.IsDefined(typeof(Color), null)), Q(() => Enum.IsDefined(typeof(Perm), (byte)I(3))), P(() => Enum.IsDefined(typeof(Perm), I(3))), Q(() => Enum.IsDefined(typeof(Perm), Perm.R | Perm.W)), Q(() => Enum.IsDefined<Color>((Color)I(2))), Q(() => Enum.IsDefined<Color>((Color)I(0))), Q(() => Enum.IsDefined(typeof(Mixed), (short)I(7))), Q(() => Enum.IsDefined(typeof(Mixed), S("Dup"))), Q(() => Enum.IsDefined(typeof(SignedOrder), I(-2))), Q(() => Enum.IsDefined(typeof(Unordered), uint.MaxValue)), P(() => Enum.IsDefined(typeof(int), I(1))), P(() => Enum.IsDefined(typeof(Color), S(""))));
        }

        static string EnumToObject()
        {
            return Par.J(Q(() => Enum.ToObject(typeof(Color), I(2))), Q(() => Enum.ToObject(typeof(Color), I(9))), Q(() => Enum.ToObject(typeof(Color), L(2))), Q(() => Enum.ToObject(typeof(Color), (byte)I(4))), Q(() => Enum.ToObject(typeof(Color), I(-1))), Q(() => ((Color)Enum.ToObject(typeof(Color), I(-1))).ToString("D")), Q(() => Enum.ToObject(typeof(Perm), I(3))), Q(() => Enum.ToObject(typeof(Perm), I(256) + 0)), Q(() => ((Perm)Enum.ToObject(typeof(Perm), I(257) + 0)).ToString("D")), Q(() => Enum.ToObject(typeof(Perm), I(-1))), Q(() => ((Perm)Enum.ToObject(typeof(Perm), I(-1))).ToString("D")), Q(() => Enum.ToObject(typeof(Mixed), I(65543))), Q(() => ((Mixed)Enum.ToObject(typeof(Mixed), I(65543))).ToString("D")), Q(() => Enum.ToObject(typeof(Big2), L(-1))), Q(() => Enum.ToObject(typeof(Color), 'A')), P(() => Enum.ToObject(typeof(Color), 1.5)), P(() => Enum.ToObject(typeof(Color), S("1"))), P(() => Enum.ToObject(typeof(Color), null)), P(() => Enum.ToObject(typeof(int), I(1))), Q(() => Enum.ToObject(typeof(Color), true)), Q(() => Enum.ToObject(typeof(Color), Perm.W)), Q(() => Enum.ToObject(typeof(Color), I(2)).GetType().Name), Q(() => Enum.ToObject(typeof(SignedOrder), uint.MaxValue)), Q(() => Enum.ToObject(typeof(Unordered), I(-1))));
        }

        static string EnumCompare()
        {
            return Par.J(Q(() => Color.Red.CompareTo(Color.Blue)), Q(() => Color.Blue.CompareTo(Color.Red)), Q(() => Color.Red.CompareTo(Color.Red)), Q(() => Color.Red.CompareTo(null)), P(() => Color.Red.CompareTo(Perm.R)), P(() => Color.Red.CompareTo(I(1))), Q(() => SignedOrder.M2.CompareTo(SignedOrder.P1)), Q(() => SignedOrder.M1.CompareTo(SignedOrder.M2)), Q(() => Unordered.Max.CompareTo(Unordered.A)), Q(() => Big2.X.CompareTo((Big2)I(0))), Q(() => Color.Red.Equals(Color.Red)), Q(() => Color.Red.Equals((Color)I(1))), Q(() => Color.Red.Equals(I(1))), Q(() => Color.Red.Equals(Perm.R)), Q(() => Color.Red.Equals(null)), Q(() => Color.Red == (Color)I(1)), Q(() => Color.Red.GetHashCode()), Q(() => ((Color)I(-1)).GetHashCode()), Q(() => Perm.R.GetHashCode()), Q(() => Mixed.Neg.GetHashCode()), Q(() => Big2.X.GetHashCode()), Q(() => SignedOrder.M1.GetHashCode()), Q(() => Color.Red < Color.Green), Q(() => Color.Blue > Color.Green), Q(() => SignedOrder.M2 < SignedOrder.Z), Q(() => Unordered.Max > Unordered.C), Q(() => (int)Color.Red + (int)Color.Green), Q(() => Color.Red + 1), Q(() => Color.Blue - 1), Q(() => Color.Blue - Color.Red), Q(() => (Color.Blue - Color.Red).GetType().Name), Q(() => (Color.Red + 1).GetType().Name), Q(() => Perm.X + 1), Q(() => (Perm)(Perm.X + (byte)I(252))), Q(() => (int)(Perm.X + (byte)I(252))), Q(() => Mixed.Big + (short)I(1)), Q(() => (short)(Mixed.Big + (short)I(1))));
        }

        static string EnumUnderlying()
        {
            Perm p = (Perm)I(200); Mixed m = (Mixed)I(70000); Big2 b = (Big2)L(-1); SignedOrder s = (SignedOrder)I(int.MinValue); Unordered u = (Unordered)I(-2);
            return Par.J(Q(() => (byte)p), Q(() => (int)p), Q(() => (sbyte)p), Q(() => (short)m), Q(() => (int)m), Q(() => (ulong)b), Q(() => (long)b), Q(() => (int)s), Q(() => (long)s), Q(() => (uint)u), Q(() => (int)u), Q(() => (long)u), Q(() => p.ToString("D")), Q(() => m.ToString("D")), Q(() => b.ToString("D")), Q(() => s.ToString("D")), Q(() => u.ToString("D")), Q(() => p.ToString("X")), Q(() => m.ToString("X")), Q(() => b.ToString("X")), Q(() => s.ToString("X")), Q(() => u.ToString("X")), Q(() => Convert.ToInt32(p)), Q(() => Convert.ToInt64(b)), P(() => Convert.ToInt32(b)), Q(() => Convert.ToUInt64(b)), Q(() => Convert.ToInt16(m)), P(() => Convert.ToByte(m)), Q(() => ((IConvertible)p).ToInt32(null)), Q(() => ((IConvertible)m).ToDouble(null)), Q(() => ((IConvertible)b).ToString(null)), Q(() => p.GetTypeCode()), Q(() => m.GetTypeCode()), Q(() => b.GetTypeCode()), Q(() => u.GetTypeCode()), Q(() => (Perm)(byte)I(1) | (Perm)(byte)I(2)), Q(() => ((Perm)(byte)I(1) | (Perm)(byte)I(2)).GetType().Name), Q(() => (object)p is byte), Q(() => (object)p is Perm), Q(() => (object)p is Enum), Q(() => (object)p is ValueType), Q(() => (object)p is IConvertible), Q(() => (object)p is IComparable), Q(() => (object)p is IFormattable));
        }

        static string HexParse()
        {
            return Par.J(Q(() => int.Parse(S("ff"), System.Globalization.NumberStyles.HexNumber)), Q(() => int.Parse(S("FFFFFFFF"), System.Globalization.NumberStyles.HexNumber)), P(() => int.Parse(S("FFFFFFFFF"), System.Globalization.NumberStyles.HexNumber)), Q(() => uint.Parse(S("FFFFFFFF"), System.Globalization.NumberStyles.HexNumber)), Q(() => byte.Parse(S("ff"), System.Globalization.NumberStyles.HexNumber)), Q(() => sbyte.Parse(S("ff"), System.Globalization.NumberStyles.HexNumber)), P(() => sbyte.Parse(S("-1"), System.Globalization.NumberStyles.HexNumber)), Q(() => long.Parse(S("ffffffffffffffff"), System.Globalization.NumberStyles.HexNumber)), P(() => int.Parse(S("0x1"), System.Globalization.NumberStyles.HexNumber)), Q(() => int.Parse(S("0001"), System.Globalization.NumberStyles.HexNumber)), Q(() => int.Parse(S("00000000000000000001"), System.Globalization.NumberStyles.HexNumber)), P(() => int.Parse(S("g"), System.Globalization.NumberStyles.HexNumber)), Q(() => int.Parse(S("ABCDEF"), System.Globalization.NumberStyles.HexNumber)), Q(() => int.Parse(S("abcdef"), System.Globalization.NumberStyles.HexNumber)), P(() => int.Parse(S("80000000"), System.Globalization.NumberStyles.HexNumber) < 0 ? "neg" : "pos"), Q(() => int.Parse(S("1010"), System.Globalization.NumberStyles.BinaryNumber)), P(() => int.Parse(S("12"), System.Globalization.NumberStyles.BinaryNumber)), Q(() => int.Parse(S("11111111111111111111111111111111"), System.Globalization.NumberStyles.BinaryNumber)), P(() => int.Parse(S("111111111111111111111111111111111"), System.Globalization.NumberStyles.BinaryNumber)), Q(() => byte.Parse(S("11111111"), System.Globalization.NumberStyles.BinaryNumber)));
        }

        static string ParseSpan()
        {
            ReadOnlySpan<char> sp = S("x42y").AsSpan(1, 2);
            ReadOnlySpan<char> dsp = S("3.25").AsSpan();
            int a = int.Parse(sp); double d = double.Parse(dsp, CultureInfo.InvariantCulture);
            bool ok = int.TryParse(S("12x").AsSpan(0, 2), out int b);
            bool ok2 = int.TryParse(S("12x").AsSpan(), out int c);
            bool ok3 = long.TryParse(S("-9").AsSpan(), System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out long l);
            Span<char> buf = stackalloc char[16];
            bool f1 = I(12345).TryFormat(buf, out int w1); string s1 = new string(buf.Slice(0, w1));
            bool f2 = D(3.14159).TryFormat(buf, out int w2, "F2", CultureInfo.InvariantCulture); string s2 = new string(buf.Slice(0, w2));
            Span<char> small = stackalloc char[3];
            bool f3 = I(12345).TryFormat(small, out int w3);
            bool f4 = I(-5).TryFormat(buf, out int w4, "D4"); string s4 = new string(buf.Slice(0, w4));
            bool f5 = L(long.MinValue).TryFormat(buf, out int w5); string s5 = new string(buf.Slice(0, w5));
            bool f6 = I(255).TryFormat(buf, out int w6, "X"); string s6 = new string(buf.Slice(0, w6));
            bool f7 = true.TryFormat(buf, out int w7); string s7 = new string(buf.Slice(0, w7));
            bool f8 = F(0.1f).TryFormat(buf, out int w8); string s8 = new string(buf.Slice(0, w8));
            return Par.J(Q(() => a), Q(() => d), Q(() => ok), Q(() => b), Q(() => ok2), Q(() => c), Q(() => ok3), Q(() => l), Q(() => f1), Q(() => s1), Q(() => f2), Q(() => s2), Q(() => f3), Q(() => w3), Q(() => f4), Q(() => s4), Q(() => f5), Q(() => s5), Q(() => f6), Q(() => s6), Q(() => f7), Q(() => s7), Q(() => f8), Q(() => s8), Enum.TryParse<Color>(S("Blue").AsSpan(), out Color col), Q(() => col), bool.TryParse(S("true").AsSpan(), out bool bb), Q(() => bb), byte.TryParse(S("256").AsSpan(), out byte by), Q(() => by));
        }

        static double D(double v) { return v; }
        static float F(float v) { return v; }

        static string ChangeType()
        {
            var inv = CultureInfo.InvariantCulture;
            return Par.J(Q(() => Convert.ChangeType(S("12"), typeof(int), inv)), Q(() => Convert.ChangeType(I(12), typeof(string), inv)), Q(() => Convert.ChangeType(I(12), typeof(double), inv)), Q(() => Convert.ChangeType(2.5, typeof(int), inv)), Q(() => Convert.ChangeType(3.5, typeof(int), inv)), Q(() => Convert.ChangeType(S("1.5"), typeof(double), inv)), Q(() => Convert.ChangeType(true, typeof(int), inv)), Q(() => Convert.ChangeType(I(1), typeof(bool), inv)), Q(() => Convert.ChangeType(S("true"), typeof(bool), inv)), P(() => Convert.ChangeType(S("x"), typeof(int), inv)), P(() => Convert.ChangeType(I(300), typeof(byte), inv)), P(() => Convert.ChangeType(I(1), typeof(Color), inv)), P(() => Convert.ChangeType(Color.Red, typeof(int), inv)), P(() => Convert.ChangeType(new object(), typeof(int), inv)), Q(() => Convert.ChangeType(null, typeof(int), inv) == null), Q(() => Convert.ChangeType(null, typeof(string), inv) == null), P(() => Convert.ChangeType(null, typeof(int?), inv) == null), Q(() => Convert.ChangeType(I(5), typeof(int?), inv)), P(() => Convert.ChangeType(S("5"), typeof(int?), inv)), Q(() => Convert.ChangeType(S("5"), TypeCode.Int32, inv)), Q(() => Convert.ChangeType(1.5, TypeCode.String, inv)), Q(() => Convert.ChangeType(I(65), TypeCode.Char, inv)), P(() => Convert.ChangeType(1.5, TypeCode.Char, inv)), Q(() => Convert.ChangeType(I(5), TypeCode.Object, inv).GetType().Name), P(() => Convert.ChangeType(I(5), TypeCode.DBNull, inv)), P(() => Convert.ChangeType(I(5), TypeCode.Empty, inv)), Q(() => Convert.ChangeType(I(5), typeof(object), inv).GetType().Name), Q(() => Convert.ChangeType(S("5"), typeof(IConvertible), inv).GetType().Name), Q(() => Convert.ChangeType(S("x"), typeof(IComparable), inv).GetType().Name), P(() => Convert.ChangeType(S("x"), typeof(IDisposable), inv)), Q(() => Convert.ChangeType(S("12"), typeof(int))));
        }
    }
}
