using System;
using System.Text;
using ParityKit;

// 92: StringBuilder, Encoding (UTF8/ASCII/Unicode), Base64, char siniflandirma.
namespace Demo92
{
    class App92
    {
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static int I(int v) { return v; }
        static string S(string v) { return v; }
        static string P(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static string V(Action a) { try { a(); return "ok"; } catch (Exception e) { return Par.Ex(e); } }
        static string Hex(byte[] b) { var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("X2")); return sb.ToString(); }

        public static int Run() { return Par.Loop(92, 20, Step); }

        static string Step(int k)
        {
            switch (k)
            {
                case 1: return SbAppend();
                case 2: return SbInsertRemove();
                case 3: return SbReplace();
                case 4: return SbLengthCapacity();
                case 5: return SbIndexer();
                case 6: return SbMisc();
                case 7: return Utf8Bytes();
                case 8: return Utf8Decode();
                case 9: return Utf8Invalid();
                case 10: return OtherEncodings();
                case 11: return Base64();
                case 12: return Base64Invalid();
                case 13: return CharClass();
                case 14: return CharClassUnicode();
                case 15: return CharNumeric();
                case 16: return CharCase();
                case 17: return HexConvert();
                case 18: return Utf8Span();
                case 19: return EncodingProps();
                case 20: return SbAppendJoin();
            }
            return "?";
        }

        static string SbAppend()
        {
            var sb = new StringBuilder();
            sb.Append((string)null).Append('|').Append((object)null).Append('|').Append((char[])null).Append('|').Append("abc", 1, 1).Append('|').Append("abc", 3, 0).Append('|').Append('x', 0).Append('|').Append('x', 3).Append('|').Append(new char[] { 'a', 'b' }, 1, 1).Append('|');
            sb.Append(I(-5)).Append(uint.MaxValue).Append(long.MinValue).Append(ulong.MaxValue).Append((byte)I(1)).Append((sbyte)I(-1)).Append((short)I(-2)).Append((ushort)I(3)).Append('|').Append(0.1).Append(0.1f).Append(1.5m).Append(true).Append(false).Append('|').Append(StringComparison.Ordinal).Append((object)I(7)).Append(new object()).Append('|');
            sb.Append(S("ab").AsSpan()).Append(new ReadOnlySpan<char>(new[] { 'c' })).Append(S("x").AsMemory()).Append('|');
            string e1 = V(() => sb.Append("abc", 2, 2)), e2 = V(() => sb.Append("abc", -1, 1)), e3 = V(() => sb.Append((string)null, 0, 1)), e4 = V(() => sb.Append((string)null, 0, 0)), e5 = V(() => sb.Append('x', -1)), e6 = V(() => sb.Append(new char[] { 'a' }, 0, 2)), e7 = V(() => sb.Append((char[])null, 0, 1)), e8 = V(() => sb.Append((char[])null, 0, 0));
            return Par.J(Q(() => sb.ToString()), Q(() => sb.Length), Q(() => e1), Q(() => e2), Q(() => e3), Q(() => e4), Q(() => e5), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => new StringBuilder("init").Append(1).ToString()), Q(() => new StringBuilder((string)null).Length), Q(() => new StringBuilder("hello", 1, 3, 16).ToString()), P(() => new StringBuilder("hello", 4, 3, 16).ToString()), Q(() => new StringBuilder(0).Append("x").ToString()), P(() => new StringBuilder(-1).ToString()), Q(() => new StringBuilder(5, 5).Append("abcde").ToString()), P(() => new StringBuilder(5, 5).Append("abcdef").ToString()), Q(() => new StringBuilder().Append(1).Append('a').Append("b").Append(2.5).Append(true).ToString()));
        }

        static string SbInsertRemove()
        {
            var sb = new StringBuilder("abc");
            sb.Insert(0, "x").Insert(4, "y").Insert(2, 'z').Insert(0, 7).Insert(1, 1.5).Insert(0, (string)null).Insert(0, true).Insert(2, new char[] { 'p', 'q' }).Insert(0, "ab", 2);
            string s1 = sb.ToString();
            string e1 = V(() => sb.Insert(sb.Length + 1, "x")), e2 = V(() => sb.Insert(-1, "x")), e3 = V(() => sb.Insert(0, "x", -1)), e4 = V(() => sb.Insert(sb.Length, "end"));
            var r = new StringBuilder("abcdef");
            r.Remove(0, 1); string r1 = r.ToString(); r.Remove(r.Length - 1, 1); string r2 = r.ToString(); r.Remove(1, 2); string r3 = r.ToString(); r.Remove(0, 0); r.Remove(r.Length, 0); string r4 = r.ToString();
            string e5 = V(() => r.Remove(0, 10)), e6 = V(() => r.Remove(-1, 1)), e7 = V(() => r.Remove(0, -1)), e8 = V(() => r.Remove(r.Length + 1, 0));
            r.Remove(0, r.Length); string r5 = "[" + r + "]" + r.Length;
            var big = new StringBuilder(); for (int i = 0; i < 100; i++) big.Append("0123456789"); big.Remove(5, 990); big.Insert(3, "XYZ");
            return Par.J(Q(() => s1), Q(() => e1), Q(() => e2), Q(() => e3), Q(() => e4), Q(() => sb.ToString()), Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => e5), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => r5), Q(() => big.ToString()), Q(() => big.Length), Q(() => new StringBuilder("ab").Insert(1, "😀").ToString()), Q(() => new StringBuilder("a😀b").Remove(1, 1).ToString().Length), Q(() => new StringBuilder("abc").Insert(1, "x", 3).ToString()), P(() => new StringBuilder("abc").Insert(1, "x", -1).ToString()), Q(() => new StringBuilder("abc").Insert(1, "", 5).ToString()), Q(() => new StringBuilder("abc").Insert(1, "xy", 0).ToString()));
        }

        static string SbReplace()
        {
            var sb = new StringBuilder("aaa bbb aaa");
            string r1 = new StringBuilder(sb.ToString()).Replace("aaa", "x").ToString(), r2 = new StringBuilder(sb.ToString()).Replace("a", "").ToString(), r3 = new StringBuilder(sb.ToString()).Replace('a', 'z').ToString(), r4 = new StringBuilder(sb.ToString()).Replace("a", "aa").ToString(), r5 = new StringBuilder(sb.ToString()).Replace("aaa", "x", 4, 7).ToString(), r6 = new StringBuilder(sb.ToString()).Replace('a', 'z', 0, 3).ToString(), r7 = new StringBuilder(sb.ToString()).Replace("zzz", "q").ToString(), r8 = new StringBuilder(sb.ToString()).Replace("a", (string)null).ToString();
            string e1 = P(() => new StringBuilder("abc").Replace("", "x").ToString()), e2 = P(() => new StringBuilder("abc").Replace((string)null, "x").ToString()), e3 = P(() => new StringBuilder("abc").Replace("a", "x", 2, 5).ToString()), e4 = P(() => new StringBuilder("abc").Replace("a", "x", -1, 1).ToString()), e5 = P(() => new StringBuilder("abc").Replace("a", "x", 3, 0).ToString()), e6 = P(() => new StringBuilder("abc").Replace("a", "x", 4, 0).ToString());
            var chunky = new StringBuilder(); for (int i = 0; i < 50; i++) chunky.Append("abcabc"); chunky.Replace("ca", "XY"); chunky.Replace("abXY", "-");
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => e1), Q(() => e2), Q(() => e3), Q(() => e4), Q(() => e5), Q(() => e6), Q(() => chunky.ToString().Length), Q(() => chunky.ToString().Substring(0, 30)), Q(() => new StringBuilder("ababab").Replace("aba", "x").ToString()), Q(() => new StringBuilder("aaaa").Replace("aa", "a").ToString()), Q(() => new StringBuilder("a😀b").Replace("😀", "x").ToString()), Q(() => new StringBuilder("a😀b").Replace('\uD83D', 'x').ToString().Length), Q(() => new StringBuilder("abc").Replace("abc", "abc").ToString()), Q(() => new StringBuilder("abc").Replace("b", "bb", 1, 1).ToString()));
        }

        static string SbLengthCapacity()
        {
            var sb = new StringBuilder("hello world");
            sb.Length = 5; string s1 = sb.ToString(); sb.Length = 8; string s2 = sb.ToString().Replace('\0', '.'); sb.Length = 0; string s3 = "[" + sb + "]";
            string e1 = V(() => sb.Length = -1);
            var c = new StringBuilder(); int c0 = c.Capacity; c.Append("x"); int c1 = c.Capacity; c.Append(new string('y', 20)); int c2 = c.Capacity; c.Append(new string('z', 100)); int c3 = c.Capacity; c.Clear(); int c4 = c.Capacity; int c4l = c.Length;
            var cap = new StringBuilder(10); int cap0 = cap.Capacity; cap.Capacity = 20; int cap1 = cap.Capacity; cap.Append("abc"); string e2 = V(() => cap.Capacity = 2); int cap2 = cap.Capacity; cap.EnsureCapacity(100); int cap3 = cap.Capacity >= 100 ? 1 : 0; string e3 = V(() => cap.EnsureCapacity(-1));
            var mc = new StringBuilder(5, 10); string e4 = V(() => mc.Append("0123456789a")); mc.Append("0123456789"); string e5 = V(() => mc.Append('x')); string e6 = V(() => mc.Length = 11); string e7 = V(() => mc.Capacity = 11); string e8 = V(() => mc.Insert(0, "x"));
            return Par.J(Q(() => s1), Q(() => s2), Q(() => s3), Q(() => e1), Q(() => c0), Q(() => c1 > 0), Q(() => c2 >= 21), Q(() => c3 >= 121), Q(() => c4l), Q(() => c4 >= 0), Q(() => cap0), Q(() => cap1), Q(() => e2), Q(() => cap2), Q(() => cap3), Q(() => e3), Q(() => e4), Q(() => e5), Q(() => e6), Q(() => e7), Q(() => e8), Q(() => mc.ToString()), Q(() => mc.MaxCapacity), Q(() => new StringBuilder().MaxCapacity), Q(() => new StringBuilder(0).Capacity), Q(() => new StringBuilder("ab").Capacity >= 2), Q(() => new StringBuilder("ab", 100).Capacity), P(() => new StringBuilder(5, 2).ToString()), P(() => new StringBuilder(-1, 5).ToString()), P(() => new StringBuilder(0, 0).ToString()));
        }

        static string SbIndexer()
        {
            var sb = new StringBuilder("abc");
            char c0 = sb[0], c2 = sb[2];
            sb[1] = 'X'; string s1 = sb.ToString();
            string e1 = P(() => sb[3]), e2 = P(() => sb[-1]), e3 = V(() => sb[3] = 'x'), e4 = V(() => sb[-1] = 'x');
            var big = new StringBuilder(); for (int i = 0; i < 100; i++) big.Append((char)('a' + i % 26)); big[50] = '!'; big[99] = '?'; big.Insert(0, "012345678901234567890123456789");
            char b0 = big[0], b30 = big[30], b80 = big[80], b129 = big[129]; string e5 = P(() => big[130]);
            var chunks = new StringBuilder(4); chunks.Append("aaaa").Append("bbbb").Append("cccc"); chunks[4] = 'B'; chunks[11] = 'C';
            return Par.J(Q(() => c0), Q(() => c2), Q(() => s1), Q(() => e1), Q(() => e2), Q(() => e3), Q(() => e4), Q(() => b0), Q(() => b30), Q(() => b80), Q(() => b129), Q(() => e5), Q(() => big.Length), Q(() => chunks.ToString()), Q(() => chunks.Length), Q(() => new StringBuilder("").Length), P(() => new StringBuilder("")[0]), Q(() => sb.ToString(1, 2)), P(() => sb.ToString(2, 2)), P(() => sb.ToString(-1, 1)), Q(() => sb.ToString(3, 0)), P(() => sb.ToString(4, 0)), Q(() => sb.ToString(0, 0).Length), Q(() => ReferenceEquals(sb.ToString(0, 0), "")), Q(() => ReferenceEquals(new StringBuilder().ToString(), "")), Q(() => ReferenceEquals(new StringBuilder("ab").ToString(), new StringBuilder("ab").ToString())), Q(() => new StringBuilder("ab").ToString() == "ab"), Q(() => new StringBuilder("a😀").ToString(1, 2)), Q(() => new StringBuilder("abc").Equals(new StringBuilder("abc"))), Q(() => new StringBuilder("abc").Equals((object)new StringBuilder("abc"))), Q(() => new StringBuilder("abc").Equals("abc".AsSpan())), Q(() => new StringBuilder("abc").Equals((StringBuilder)null)), Q(() => new StringBuilder("abc").Equals(new StringBuilder("abcd"))), Q(() => new StringBuilder("abc", 100).Equals(new StringBuilder("abc", 10))));
        }

        static string SbMisc()
        {
            var sb = new StringBuilder("abc");
            var sb2 = sb.Clear(); bool same = ReferenceEquals(sb, sb2);
            var a = new StringBuilder("x").Append(1).AppendLine().Append(2).AppendLine("y").AppendLine(null).Append(3);
            string lines = a.ToString().Replace("\r", "\\r").Replace("\n", "\\n");
            var cp = new StringBuilder("hello"); char[] dst = new char[10]; cp.CopyTo(1, dst, 2, 3); string cpr = new string(dst).Replace('\0', '.'); string ec = V(() => cp.CopyTo(0, dst, 0, 11)); string ec2 = V(() => cp.CopyTo(5, dst, 0, 1)); string ec3 = V(() => cp.CopyTo(0, (char[])null, 0, 1)); Span<char> sp = stackalloc char[3]; cp.CopyTo(2, sp, 3); string cps = new string(sp); string ec4 = V(() => { Span<char> sp2 = stackalloc char[3]; cp.CopyTo(3, sp2, 3); });
            var chunks = new StringBuilder(2); chunks.Append("ab").Append("cd").Append("ef"); int nChunks = 0; int total = 0; foreach (var ch in chunks.GetChunks()) { nChunks++; total += ch.Length; }
            var j = new StringBuilder().AppendJoin(",", 1, 2, 3).Append('|').AppendJoin("", new[] { "a", null, "b" }).Append('|').AppendJoin(',', new System.Collections.Generic.List<int> { 4, 5 }).Append('|').AppendJoin(",", new object[0]).Append('|').AppendJoin(",", new object[] { null }).Append('|').AppendJoin((string)null, 1, 2);
            var inter = new StringBuilder().Append($"{I(5)}-{S("x")}").Append(System.Globalization.CultureInfo.InvariantCulture, $"{1.5}|{I(3),4}|{S("q"),-3}|");
            return Par.J(Q(() => same), Q(() => sb.Length), Q(() => lines), Q(() => cpr), Q(() => ec), Q(() => ec2), Q(() => ec3), Q(() => cps), Q(() => ec4), Q(() => nChunks), Q(() => total), Q(() => j.ToString()), Q(() => inter.ToString()), Q(() => new StringBuilder("ab").ToString().GetType().Name), Q(() => sb.GetType().Name), Q(() => new StringBuilder("ab").Append(new StringBuilder("cd")).ToString()), Q(() => new StringBuilder("ab").Append(new StringBuilder("cdef"), 1, 2).ToString()), Q(() => new StringBuilder("ab").Append((StringBuilder)null).ToString()), P(() => new StringBuilder("ab").Append((StringBuilder)null, 0, 1).ToString()), Q(() => new StringBuilder("ab").Append((StringBuilder)null, 0, 0).ToString()), Q(() => new StringBuilder().Append('a').Append(S(null)).Append('b').Length), Q(() => new StringBuilder().Append(new object[] { 1, 2 }).ToString()), Q(() => new StringBuilder().Append((object)new int[] { 1 }).ToString()), Q(() => new StringBuilder().Append(new char[0]).Length), Q(() => new StringBuilder().Append("").Length), Q(() => new StringBuilder().Append(string.Empty).Capacity >= 0), Q(() => new StringBuilder("x").Append('\0').Length), Q(() => new StringBuilder("x").Append('\0').ToString().Length), Q(() => new StringBuilder("x").Append('\0').ToString() == "x\0"));
        }

        static string Utf8Bytes()
        {
            var u = Encoding.UTF8;
            return Par.J(Q(() => Hex(u.GetBytes(S("a")))), Q(() => Hex(u.GetBytes(S("ğ")))), Q(() => Hex(u.GetBytes(S("€")))), Q(() => Hex(u.GetBytes(S("😀")))), Q(() => Hex(u.GetBytes(S("ağ€😀")))), Q(() => u.GetBytes(S("ağ€😀")).Length), Q(() => u.GetByteCount(S("ağ€😀"))), Q(() => u.GetByteCount(S(""))), Q(() => u.GetBytes(S("")).Length), Q(() => Hex(u.GetBytes(S("\0")))), Q(() => Hex(u.GetBytes(S("\u007F")))), Q(() => Hex(u.GetBytes(S("\u0080")))), Q(() => Hex(u.GetBytes(S("\u07FF")))), Q(() => Hex(u.GetBytes(S("\u0800")))), Q(() => Hex(u.GetBytes(S("\uFFFF")))), Q(() => Hex(u.GetBytes(S("\uFEFF")))), Q(() => Hex(u.GetBytes(S("\uD83D\uDE00")))), Q(() => Hex(u.GetBytes(S("\uDBFF\uDFFF")))), Q(() => Hex(u.GetBytes(S("\uD800\uDC00")))), Q(() => Hex(u.GetBytes(new[] { 'a', 'b', 'c' }, 1, 1))), P(() => Hex(u.GetBytes((string)null))), P(() => Hex(u.GetBytes((char[])null))), P(() => u.GetBytes(S("abc"), 2, 2).Length), Q(() => u.GetByteCount(new[] { 'a', 'ğ' })), Q(() => u.GetByteCount(S("a😀"), 0, 2)), Q(() => u.GetByteCount(S("a😀"), 1, 2)), Q(() => u.GetMaxByteCount(1)), Q(() => u.GetMaxByteCount(10)), Q(() => u.GetMaxCharCount(10)), Q(() => u.GetMaxByteCount(0)), P(() => u.GetMaxByteCount(-1)), Q(() => Hex(u.GetPreamble())), Q(() => Hex(new UTF8Encoding(true).GetPreamble())), Q(() => Hex(new UTF8Encoding(false).GetPreamble())), Q(() => Hex(Encoding.UTF8.GetBytes(S("ab"), 0, 2))), Q(() => Hex(Encoding.UTF8.GetBytes(S("ab").ToCharArray(), 0, 0))), Q(() => Hex(u.GetBytes(S("Ağüşiöç ĞÜŞİÖÇ")))));
        }

        static string Utf8Decode()
        {
            var u = Encoding.UTF8;
            byte[] b = u.GetBytes(S("ağ€😀"));
            return Par.J(Q(() => u.GetString(b)), Q(() => u.GetString(b).Length), Q(() => u.GetCharCount(b)), Q(() => u.GetString(b, 0, 1)), Q(() => u.GetString(b, 1, 2)), Q(() => u.GetString(b, 3, 3)), Q(() => u.GetString(b, 6, 4)), Q(() => u.GetString(b, 6, 4).Length), Q(() => u.GetString(new byte[0]).Length), Q(() => u.GetString(new byte[0]) == ""), Q(() => ReferenceEquals(u.GetString(new byte[0]), "")), P(() => u.GetString((byte[])null)), P(() => u.GetString(b, 9, 2)), P(() => u.GetString(b, -1, 1)), P(() => u.GetString(b, 0, -1)), Q(() => u.GetString(new byte[] { 0xEF, 0xBB, 0xBF, 0x61 })), Q(() => u.GetString(new byte[] { 0xEF, 0xBB, 0xBF, 0x61 }).Length), Q(() => (int)u.GetString(new byte[] { 0xEF, 0xBB, 0xBF })[0]), Q(() => u.GetChars(b).Length), Q(() => u.GetChars(b, 0, 3).Length), Q(() => new string(u.GetChars(b, 6, 4))), Q(() => u.GetCharCount(b, 6, 4)), Q(() => u.GetString(new ReadOnlySpan<byte>(b, 1, 2))), Q(() => u.GetCharCount(new ReadOnlySpan<byte>(b))), Q(() => u.GetString(new byte[] { 0x00 }).Length), Q(() => (int)u.GetString(new byte[] { 0x00 })[0]), Q(() => u.GetString(new byte[] { 0x7F })), Q(() => u.GetString(new byte[] { 0xC2, 0x80 }) == "\u0080"), Q(() => u.GetString(new byte[] { 0xF4, 0x8F, 0xBF, 0xBF }).Length), Q(() => u.GetString(new byte[] { 0xF4, 0x8F, 0xBF, 0xBF }) == "\uDBFF\uDFFF"), Q(() => u.GetString(u.GetBytes(S("Ağüşiöç ĞÜŞİÖÇ")))), Q(() => u.GetString(u.GetBytes(S("\0a\0"))).Length), Q(() => u.GetString(new byte[] { 0x61, 0x62 }, 1, 1)));
        }

        static string Utf8Invalid()
        {
            var u = Encoding.UTF8;
            byte[][] bad = { new byte[] { 0xFF }, new byte[] { 0xC0, 0x80 }, new byte[] { 0xC1, 0xBF }, new byte[] { 0x80 }, new byte[] { 0xE0, 0x80, 0x80 }, new byte[] { 0xED, 0xA0, 0x80 }, new byte[] { 0xF5, 0x80, 0x80, 0x80 }, new byte[] { 0xF4, 0x90, 0x80, 0x80 }, new byte[] { 0xE2, 0x82 }, new byte[] { 0x61, 0xE2, 0x82, 0x62 }, new byte[] { 0xF0, 0x9F, 0x98 }, new byte[] { 0xC2 }, new byte[] { 0x61, 0xC2 }, new byte[] { 0xE2, 0x28, 0xA1 }, new byte[] { 0xF0, 0x28, 0x8C, 0xBC }, new byte[] { 0xF8, 0x88, 0x80, 0x80, 0x80 }, new byte[] { 0xFC, 0x84, 0x80, 0x80, 0x80, 0x80 }, new byte[] { 0xFE }, new byte[] { 0xC0 }, new byte[] { 0xEF, 0xBF, 0xBE }, new byte[] { 0xEF, 0xBF, 0xBF } };
            var sb = new StringBuilder();
            foreach (var b in bad) { string s = u.GetString(b); sb.Append(s.Length).Append(':'); foreach (var c in s) sb.Append(((int)c).ToString("X4")).Append(' '); sb.Append(';'); }
            // lone surrogate kodlama: U+FFFD (EF BF BD)
            sb.Append(Hex(u.GetBytes(S("\uD83D")))).Append(';').Append(Hex(u.GetBytes(S("\uDE00")))).Append(';').Append(Hex(u.GetBytes(S("a\uD83Db")))).Append(';').Append(Hex(u.GetBytes(S("\uDE00\uD83D")))).Append(';').Append(u.GetByteCount(S("\uD83D"))).Append(';');
            var strict = new UTF8Encoding(false, true);
            sb.Append(P(() => strict.GetString(new byte[] { 0xFF }))).Append(';').Append(P(() => Hex(strict.GetBytes(S("\uD83D"))))).Append(';').Append(P(() => strict.GetString(new byte[] { 0xC0, 0x80 }))).Append(';').Append(P(() => strict.GetString(new byte[] { 0xED, 0xA0, 0x80 }))).Append(';').Append(P(() => strict.GetString(new byte[] { 0xE2, 0x82 }))).Append(';').Append(strict.GetString(new byte[] { 0x61 })).Append(';');
            sb.Append(u.GetCharCount(new byte[] { 0xFF, 0xFF })).Append(';').Append(u.GetCharCount(new byte[] { 0xE2, 0x82 })).Append(';').Append(u.GetCharCount(new byte[] { 0xF0, 0x9F, 0x98 })).Append(';').Append(u.GetCharCount(new byte[] { 0xC0, 0x80 }));
            return sb.ToString();
        }

        static string OtherEncodings()
        {
            var a = Encoding.ASCII; var u16 = Encoding.Unicode; var be = Encoding.BigEndianUnicode; var u32 = Encoding.UTF32; var l1 = Encoding.Latin1;
            return Par.J(Q(() => Hex(a.GetBytes(S("aZ09")))), Q(() => Hex(a.GetBytes(S("ğ")))), Q(() => Hex(a.GetBytes(S("€😀")))), Q(() => a.GetString(new byte[] { 0x41, 0x80, 0xFF })), Q(() => a.GetString(new byte[] { 0x41, 0x80, 0xFF }).Length), Q(() => a.GetByteCount(S("ğ😀"))), Q(() => a.GetMaxByteCount(5)), Q(() => Hex(u16.GetBytes(S("ağ😀")))), Q(() => u16.GetString(u16.GetBytes(S("ağ😀")))), Q(() => u16.GetByteCount(S("ağ😀"))), Q(() => Hex(u16.GetPreamble())), Q(() => Hex(u16.GetBytes(S("\uD83D")))), Q(() => u16.GetString(new byte[] { 0x3D, 0xD8 }).Length), Q(() => (int)u16.GetString(new byte[] { 0x3D, 0xD8 })[0]), Q(() => u16.GetString(new byte[] { 0x61 }).Length), Q(() => (int)u16.GetString(new byte[] { 0x61 })[0]), Q(() => Hex(be.GetBytes(S("ağ😀")))), Q(() => be.GetString(be.GetBytes(S("ağ")))), Q(() => Hex(be.GetPreamble())), Q(() => Hex(u32.GetBytes(S("a😀")))), Q(() => u32.GetString(u32.GetBytes(S("a😀")))), Q(() => u32.GetByteCount(S("a😀"))), Q(() => Hex(u32.GetBytes(S("\uD83D")))), Q(() => Hex(u32.GetPreamble())), Q(() => Hex(l1.GetBytes(S("aÿğ")))), Q(() => l1.GetString(new byte[] { 0x41, 0x80, 0xFF })), Q(() => l1.GetString(new byte[] { 0x41, 0x80, 0xFF }).Length), Q(() => (int)l1.GetString(new byte[] { 0x80 })[0]), Q(() => l1.GetByteCount(S("😀"))), Q(() => Hex(l1.GetBytes(S("😀")))), Q(() => Encoding.Default.WebName), Q(() => Encoding.Default.CodePage), Q(() => a.CodePage), Q(() => u16.CodePage), Q(() => be.CodePage), Q(() => u32.CodePage), Q(() => l1.CodePage), Q(() => Encoding.UTF8.CodePage), Q(() => Encoding.UTF8.WebName), Q(() => a.WebName), Q(() => u16.WebName), Q(() => be.WebName), Q(() => u32.WebName), Q(() => l1.WebName), Q(() => Encoding.UTF8.EncodingName), Q(() => Encoding.GetEncoding(65001).WebName), Q(() => Encoding.GetEncoding("utf-8").CodePage), Q(() => Encoding.GetEncoding("UTF-16").CodePage), Q(() => Encoding.GetEncoding("us-ascii").CodePage), P(() => Encoding.GetEncoding(1254).WebName), P(() => Encoding.GetEncoding("windows-1254").CodePage), P(() => Encoding.GetEncoding("nope").CodePage), P(() => Encoding.GetEncoding(-1).CodePage), Q(() => Hex(Encoding.Convert(Encoding.UTF8, Encoding.Unicode, Encoding.UTF8.GetBytes(S("ağ"))))), Q(() => Encoding.UTF8.Equals(new UTF8Encoding())), Q(() => Encoding.UTF8.Equals(new UTF8Encoding(true))), Q(() => Encoding.UTF8.Equals(new UTF8Encoding(true, false))), Q(() => Encoding.UTF8.Equals(Encoding.ASCII)), Q(() => ReferenceEquals(Encoding.UTF8, Encoding.UTF8)), Q(() => Encoding.UTF8.IsSingleByte), Q(() => a.IsSingleByte), Q(() => l1.IsSingleByte), Q(() => u16.IsSingleByte));
        }

        static string Base64()
        {
            byte[] b0 = new byte[0], b1 = { 0 }, b2 = { 0, 0 }, b3 = { 0, 0, 0 }, b4 = { 0xFF, 0xFF, 0xFF, 0xFF }, abc = Encoding.UTF8.GetBytes(S("abc")), hello = Encoding.UTF8.GetBytes(S("Hello, World! ğ😀")), bin = { 0xFB, 0xFF, 0xBF, 0x00, 0x3E, 0x3F };
            string s1 = Convert.ToBase64String(hello);
            return Par.J(Q(() => Convert.ToBase64String(b0)), Q(() => Convert.ToBase64String(b1)), Q(() => Convert.ToBase64String(b2)), Q(() => Convert.ToBase64String(b3)), Q(() => Convert.ToBase64String(b4)), Q(() => Convert.ToBase64String(abc)), Q(() => s1), Q(() => Convert.ToBase64String(bin)), Q(() => Convert.ToBase64String(abc, 1, 1)), Q(() => Convert.ToBase64String(abc, 3, 0)), P(() => Convert.ToBase64String(abc, 2, 2)), P(() => Convert.ToBase64String((byte[])null)), Q(() => Hex(Convert.FromBase64String(s1))), Q(() => Encoding.UTF8.GetString(Convert.FromBase64String(s1))), Q(() => Hex(Convert.FromBase64String(""))), Q(() => Hex(Convert.FromBase64String("AA=="))), Q(() => Hex(Convert.FromBase64String("AAA="))), Q(() => Hex(Convert.FromBase64String("AAAA"))), Q(() => Hex(Convert.FromBase64String("+/8="))), Q(() => Hex(Convert.FromBase64String(" YW Jj "))), Q(() => Hex(Convert.FromBase64String("YWJj\r\n"))), Q(() => Hex(Convert.FromBase64String("\tYWJj"))), Q(() => Convert.ToBase64String(new byte[57]).Length), Q(() => Convert.ToBase64String(new byte[57], Base64FormattingOptions.InsertLineBreaks).Length), Q(() => Convert.ToBase64String(new byte[57], Base64FormattingOptions.InsertLineBreaks).Contains("\r\n")), Q(() => Convert.ToBase64String(new byte[58], Base64FormattingOptions.InsertLineBreaks).Split("\r\n").Length), Q(() => Convert.ToBase64String(new byte[200]).Length), Q(() => Hex(Convert.FromBase64String(Convert.ToBase64String(new byte[200], Base64FormattingOptions.InsertLineBreaks))).Length), Q(() => Convert.ToBase64String(new ReadOnlySpan<byte>(abc))), Convert.TryToBase64Chars(abc, new char[4], out int w1), Q(() => w1), Convert.TryToBase64Chars(abc, new char[3], out int w2), Q(() => w2), Convert.TryFromBase64String("YWJj", new byte[3], out int r1), Q(() => r1), Convert.TryFromBase64String("YWJj", new byte[2], out int r2), Q(() => r2), Convert.TryFromBase64String("YWJ", new byte[3], out int r3), Q(() => r3), Convert.TryFromBase64Chars("YW==".AsSpan(), new byte[3], out int r4), Q(() => r4), Q(() => System.Buffers.Text.Base64.GetMaxEncodedToUtf8Length(3)), Q(() => System.Buffers.Text.Base64.GetMaxDecodedFromUtf8Length(4)), Q(() => System.Buffers.Text.Base64.IsValid("YWJj")), Q(() => System.Buffers.Text.Base64.IsValid("YWJ")), Q(() => System.Buffers.Text.Base64.IsValid("YW Jj")), Q(() => System.Buffers.Text.Base64.IsValid("YW!j")));
        }

        static string Base64Invalid()
        {
            string[] bad = { "A", "AB", "ABC", "A===", "AB=A", "=", "==", "====", "AAAA=", "AAAAA", "AA=A", "A=AA", "AB==x", "A B", "AB!=", "AB-=", "AB_=", "YWJj=", "YWJj==", "=YWJj", "YW=j", "YWJj\0", "YWJjYQ", "YWJjYQ=", "Y\u0100WJj", "0", "00", "000", "0000", "00==", "000=", "QQ==", "QQ=", "QQ", "Q===" };
            var sb = new StringBuilder();
            foreach (var s in bad) sb.Append(P(() => Hex(Convert.FromBase64String(s)))).Append(';');
            sb.Append(P(() => Convert.FromBase64String(null)));
            return sb.ToString();
        }

        static string CharClass()
        {
            char[] cs = { 'a', 'Z', '0', '9', ' ', '\t', '\n', '\r', '\0', '_', '-', '.', ',', '!', '?', '(', ')', '[', '@', '#', '$', '%', '^', '&', '*', '+', '=', '/', '\\', '|', '~', '`', '\'', '"', '<', '>', ';', ':', '{', '}', '\u007F', '\u0080', '\u00A0', '\u00AD', '\u00B2', '\u00BD', '\u00E9', '\u0130', '\u0131', '\u0301', '\u2000', '\u200B', '\u2028', '\u2029', '\u3000', '\uFEFF', '\uFFFF', '\uD83D', '\uDE00', '\uE000', '\u0660', '\u2160', '\u20AC', '\u00A9', '\u00AB', '\u00BB', '\u00B5', '\u01C5', '\u1E9E', '\uFB01', '\u0F33', '\u2070', '\u0E50' };
            var sb = new StringBuilder();
            foreach (var c in cs)
            {
                sb.Append(((int)c).ToString("X4")).Append(':').Append(char.IsLetter(c) ? 'L' : '.').Append(char.IsDigit(c) ? 'D' : '.').Append(char.IsNumber(c) ? 'N' : '.').Append(char.IsLetterOrDigit(c) ? 'A' : '.').Append(char.IsWhiteSpace(c) ? 'W' : '.').Append(char.IsUpper(c) ? 'U' : '.').Append(char.IsLower(c) ? 'l' : '.').Append(char.IsPunctuation(c) ? 'P' : '.').Append(char.IsSymbol(c) ? 'S' : '.').Append(char.IsControl(c) ? 'C' : '.').Append(char.IsSeparator(c) ? 's' : '.').Append(char.IsSurrogate(c) ? 'G' : '.').Append(char.IsAscii(c) ? 'a' : '.').Append(char.IsAsciiLetter(c) ? 'b' : '.').Append(char.IsAsciiDigit(c) ? 'c' : '.').Append(char.IsAsciiHexDigit(c) ? 'h' : '.').Append(char.IsAsciiLetterOrDigit(c) ? 'd' : '.').Append(char.IsAsciiLetterUpper(c) ? 'e' : '.').Append(char.IsAsciiLetterLower(c) ? 'f' : '.').Append(';');
            }
            return sb.ToString();
        }

        static string CharClassUnicode()
        {
            string s = S("aZ0 _\u00A0\u0130\u0131\u0301\u200B\uD83D\uDE00\uE000\u0660\u2160\u20AC\u01C5\uFB01\u00BD");
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
                sb.Append(char.GetUnicodeCategory(s, i)).Append(',').Append(char.IsLetter(s, i) ? 1 : 0).Append(char.IsDigit(s, i) ? 1 : 0).Append(char.IsUpper(s, i) ? 1 : 0).Append(char.IsLower(s, i) ? 1 : 0).Append(char.IsWhiteSpace(s, i) ? 1 : 0).Append(char.IsSurrogatePair(s, i) ? 1 : 0).Append(char.IsHighSurrogate(s, i) ? 1 : 0).Append(';');
            sb.Append(P(() => char.IsLetter(s, s.Length))).Append(P(() => char.IsLetter((string)null, 0))).Append(P(() => char.IsLetter(s, -1))).Append(P(() => char.GetUnicodeCategory(s, s.Length)));
            sb.Append(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(0x1F600)).Append(',').Append(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(0x10000)).Append(',').Append(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(0xE0001)).Append(',').Append(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(0x10FFFF)).Append(',').Append(System.Globalization.CharUnicodeInfo.GetUnicodeCategory('a')).Append(',').Append(System.Globalization.CharUnicodeInfo.GetNumericValue('٣')).Append(',').Append(System.Globalization.CharUnicodeInfo.GetDigitValue('٣')).Append(',').Append(System.Globalization.CharUnicodeInfo.GetDecimalDigitValue('²')).Append(',').Append(System.Globalization.CharUnicodeInfo.GetDigitValue('²')).Append(',').Append(System.Globalization.CharUnicodeInfo.GetNumericValue('½')).Append(',').Append(System.Globalization.CharUnicodeInfo.GetNumericValue('Ⅳ')).Append(',').Append(System.Globalization.CharUnicodeInfo.GetNumericValue('a')).Append(',').Append(System.Globalization.CharUnicodeInfo.GetNumericValue(s, 0)).Append(',').Append(System.Globalization.CharUnicodeInfo.GetNumericValue(S("𝟘"), 0));
            return sb.ToString();
        }

        static string CharNumeric()
        {
            char[] cs = { '0', '5', '9', 'a', ' ', '٣', '²', '½', 'Ⅳ', '〇', '๕', '０', '৾', '⑩', '\u2070', '\u00BC', '\uD835' };
            var sb = new StringBuilder();
            foreach (var c in cs) sb.Append(((int)c).ToString("X4")).Append(':').Append(char.GetNumericValue(c)).Append(',').Append(char.IsDigit(c)).Append(',').Append(char.IsNumber(c)).Append(',').Append(char.IsAsciiDigit(c)).Append(';');
            sb.Append(char.GetNumericValue(S("𝟘"), 0)).Append(',').Append(char.GetNumericValue(S("𝟘"), 1)).Append(',').Append(char.GetNumericValue(S("a1"), 1)).Append(',').Append(P(() => char.GetNumericValue(S("a1"), 2))).Append(',').Append((int)char.GetNumericValue('7')).Append(',').Append('7' - '0').Append(',').Append((char)('0' + I(7))).Append(',').Append(char.IsBetween('m', 'a', 'z')).Append(char.IsBetween('m', 'n', 'z')).Append(char.IsBetween('a', 'a', 'a')).Append(',').Append(char.MinValue == '\0').Append(',').Append((int)char.MaxValue).Append(',').Append(char.MaxValue.ToString().Length).Append(',').Append('a'.CompareTo('b')).Append(',').Append('b'.CompareTo('a')).Append(',').Append('a'.CompareTo('a')).Append(',').Append('\uFFFF'.CompareTo('\0')).Append(',').Append('a'.Equals('a')).Append(',').Append('a'.Equals((object)'a')).Append(',').Append('a'.Equals((object)97)).Append(',').Append('a'.GetHashCode()).Append(',').Append('\uFFFF'.GetHashCode()).Append(',').Append('\0'.GetHashCode()).Append(',').Append('a'.GetTypeCode()).Append(',').Append(((IConvertible)'a').ToInt32(null)).Append(',').Append(P(() => ((IConvertible)'a').ToBoolean(null))).Append(',').Append(((IConvertible)'a').ToString(null)).Append(',').Append(((IConvertible)'a').ToDouble(null) == 97.0 ? "97" : P(() => ((IConvertible)'a').ToDouble(null)));
            return sb.ToString();
        }

        static string CharCase()
        {
            char[] cs = { 'a', 'A', 'z', 'Z', '0', ' ', 'ß', 'ẞ', 'ı', 'İ', 'i', 'I', 'ǅ', 'Ǆ', 'ǆ', 'ſ', 'K', 'Σ', 'σ', 'ς', 'µ', 'Μ', 'ÿ', 'Ÿ', 'ﬁ', 'ⅰ', 'Ⅰ', 'ⓐ', 'Ⓐ', 'ǰ', 'ΐ', 'ῼ', 'ᾈ', 'ꭰ', 'Ꭰ', '\uD801', '\u0149', '\u1E9B', '\u0587', '\uA7AE', '\u026A', '\u1D79', '\uA77D', '\u0250', '\u2C6F', '\u01C4', '\u1E9E', '\u00DF', '\u0130' };
            var sb = new StringBuilder();
            foreach (var c in cs) sb.Append(((int)c).ToString("X4")).Append(':').Append(((int)char.ToUpper(c)).ToString("X4")).Append('/').Append(((int)char.ToLower(c)).ToString("X4")).Append('/').Append(((int)char.ToUpperInvariant(c)).ToString("X4")).Append('/').Append(((int)char.ToLowerInvariant(c)).ToString("X4")).Append('/').Append(char.IsUpper(c) ? 'U' : '.').Append(char.IsLower(c) ? 'l' : '.').Append(';');
            string s = new string(cs);
            sb.Append(s.ToUpperInvariant().Length).Append(',').Append(s.ToLowerInvariant().Length).Append(',').Append(s.ToUpper().Length).Append(',').Append(s.ToUpperInvariant() == s.ToUpper()).Append(',').Append(s.ToLowerInvariant() == s.ToLower()).Append(',').Append(S("ǅ").ToUpperInvariant()).Append(S("ǅ").ToLowerInvariant()).Append(',').Append(S("ΌΣΟΣ").ToLowerInvariant()).Append(',').Append(S("ǰ").ToUpperInvariant().Length).Append(',').Append(S("ΐ").ToUpperInvariant().Length).Append(',').Append(S("ß").ToUpperInvariant().Length).Append(',').Append(S("ŉ").ToUpperInvariant().Length).Append(',').Append(S("ﬃ").ToUpperInvariant()).Append(',').Append(S("ΐ").ToUpper()).Append(',').Append(S("i̇").ToUpperInvariant().Length).Append(',').Append(S("i̇").Length).Append(',').Append(S("İ").ToLowerInvariant().Length).Append(',').Append(S("\u0130").ToLower() == "i").Append(',').Append(S("\u0130").ToLowerInvariant() == "i").Append(',').Append(S("\u0130").ToLowerInvariant() == "i\u0307").Append(',').Append(S("\u0131").ToUpperInvariant() == "I").Append(',').Append(S("\u1E9E").ToLowerInvariant() == "ß").Append(',').Append(S("ẞ").ToLower()).Append(',').Append(S("ß").ToUpper());
            return sb.ToString();
        }

        static string HexConvert()
        {
            byte[] b = { 0x00, 0x7F, 0x80, 0xFF, 0xAB };
            return Par.J(Q(() => Convert.ToHexString(b)), Q(() => Convert.ToHexStringLower(b)), Q(() => Convert.ToHexString(new byte[0])), Q(() => Convert.ToHexString(b, 1, 2)), P(() => Convert.ToHexString(b, 4, 2)), P(() => Convert.ToHexString((byte[])null)), Q(() => Hex(Convert.FromHexString("007F80FFAB"))), Q(() => Hex(Convert.FromHexString("007f80ffab"))), Q(() => Hex(Convert.FromHexString(""))), P(() => Hex(Convert.FromHexString("0"))), P(() => Hex(Convert.FromHexString("0G"))), P(() => Hex(Convert.FromHexString(" 00"))), P(() => Hex(Convert.FromHexString("0x00"))), P(() => Hex(Convert.FromHexString((string)null))), Q(() => Hex(Convert.FromHexString("aBcD"))), Q(() => Convert.ToHexString(Encoding.UTF8.GetBytes(S("ğ")))), Q(() => BitConverter.ToString(b)), Q(() => BitConverter.ToString(new byte[0])), Q(() => BitConverter.ToString(b, 1)), Q(() => BitConverter.ToString(b, 1, 2)), P(() => BitConverter.ToString(b, 5, 1)), Q(() => BitConverter.ToString(b, 5, 0)), P(() => BitConverter.ToString(b, 6, 0)), P(() => BitConverter.ToString((byte[])null)), Q(() => BitConverter.ToString(new byte[] { 0x0A })), Q(() => Uri.HexEscape('a')), Q(() => Uri.HexEscape(' ')), Q(() => Uri.HexEscape('\u00FF')), P(() => Uri.HexEscape('\u0100')), Q(() => Uri.IsHexDigit('a')), Q(() => Uri.IsHexDigit('g')), Q(() => Uri.FromHex('a')), Q(() => Uri.FromHex('F')), P(() => Uri.FromHex('g')), Q(() => Uri.IsHexEncoding("%41", 0)), Q(() => Uri.IsHexEncoding("%4", 0)), Q(() => Uri.IsHexEncoding("41", 0)));
        }

        static string Utf8Span()
        {
            var u = Encoding.UTF8;
            Span<byte> buf = stackalloc byte[16];
            int n1 = u.GetBytes(S("ağ😀").AsSpan(), buf);
            Span<char> cbuf = stackalloc char[16];
            int n2 = u.GetChars(buf.Slice(0, n1), cbuf);
            string back = new string(cbuf.Slice(0, n2));
            string e1 = P(() => { Span<byte> small = stackalloc byte[2]; return u.GetBytes(S("ağ😀").AsSpan(), small); });
            string e2 = P(() => { Span<char> small = stackalloc char[1]; return u.GetChars(u.GetBytes(S("ab")), small); });
            bool t1 = u.TryGetBytes(S("ağ").AsSpan(), buf, out int w1);
            bool t2 = u.TryGetBytes(S("ağ😀").AsSpan(), buf.Slice(0, 3), out int w2);
            bool t3 = u.TryGetChars(u.GetBytes(S("ağ")), cbuf, out int w3);
            bool t4 = u.TryGetChars(u.GetBytes(S("ağ")), cbuf.Slice(0, 1), out int w4);
            var ops = System.Text.Unicode.Utf8.FromUtf16(S("ağ😀").AsSpan(), buf, out int rd, out int wr);
            var ops2 = System.Text.Unicode.Utf8.ToUtf16(buf.Slice(0, wr), cbuf, out int rd2, out int wr2);
            var ops3 = System.Text.Unicode.Utf8.FromUtf16(S("ağ😀").AsSpan(), buf.Slice(0, 4), out int rd3, out int wr3);
            var ops4 = System.Text.Unicode.Utf8.ToUtf16(new byte[] { 0xFF, 0x61 }, cbuf, out int rd4, out int wr4);
            var ops5 = System.Text.Unicode.Utf8.ToUtf16(new byte[] { 0xFF, 0x61 }, cbuf, out int rd5, out int wr5, replaceInvalidSequences: false);
            var ops6 = System.Text.Unicode.Utf8.ToUtf16(new byte[] { 0x61, 0xE2, 0x82 }, cbuf, out int rd6, out int wr6, isFinalBlock: false);
            var ops7 = System.Text.Unicode.Utf8.ToUtf16(new byte[] { 0x61, 0xE2, 0x82 }, cbuf, out int rd7, out int wr7, isFinalBlock: true);
            bool valid1 = System.Text.Unicode.Utf8.IsValid(new byte[] { 0x61, 0xC4, 0x9F }), valid2 = System.Text.Unicode.Utf8.IsValid(new byte[] { 0xFF }), valid3 = System.Text.Unicode.Utf8.IsValid(new byte[] { 0xED, 0xA0, 0x80 }), valid4 = System.Text.Unicode.Utf8.IsValid(new byte[0]);
            return Par.J(Q(() => n1), Q(() => n2), Q(() => back), Q(() => e1), Q(() => e2), Q(() => t1), Q(() => w1), Q(() => t2), Q(() => w2), Q(() => t3), Q(() => w3), Q(() => t4), Q(() => w4), Q(() => ops), Q(() => rd), Q(() => wr), Q(() => ops2), Q(() => rd2), Q(() => wr2), Q(() => ops3), Q(() => rd3), Q(() => wr3), Q(() => ops4), Q(() => rd4), Q(() => wr4), new string(cbuf.Slice(0, wr4)).Length, ((int)cbuf[0]).ToString("X4"), Q(() => ops5), Q(() => rd5), Q(() => wr5), Q(() => ops6), Q(() => rd6), Q(() => wr6), Q(() => ops7), Q(() => rd7), Q(() => wr7), Q(() => valid1), Q(() => valid2), Q(() => valid3), Q(() => valid4), Q(() => "ağ😀"u8.Length), Q(() => Hex("ağ😀"u8.ToArray())), Q(() => "x"u8[0]), Q(() => ""u8.Length), Q(() => Encoding.UTF8.GetString("ağ"u8)), Q(() => System.Text.Unicode.Utf8.IsValid("ağ"u8)));
        }

        static string EncodingProps()
        {
            var u = Encoding.UTF8;
            var enc = u.GetEncoder(); var dec = u.GetDecoder();
            byte[] part1 = { 0x61, 0xE2 }, part2 = { 0x82, 0xAC, 0x62 };
            char[] cb = new char[10];
            int c1 = dec.GetChars(part1, 0, part1.Length, cb, 0, false);
            int c2 = dec.GetChars(part2, 0, part2.Length, cb, c1, false);
            string stream = new string(cb, 0, c1 + c2);
            var dec2 = u.GetDecoder(); char[] cb2 = new char[10];
            int d1 = dec2.GetChars(new byte[] { 0xE2, 0x82 }, 0, 2, cb2, 0, true);
            string stream2 = new string(cb2, 0, d1); string s2hex = ""; foreach (var ch in stream2) s2hex += ((int)ch).ToString("X4");
            var dec3 = u.GetDecoder(); char[] cb3 = new char[10];
            int d3 = dec3.GetChars(new byte[] { 0xE2, 0x82 }, 0, 2, cb3, 0, false); dec3.Reset(); int d4 = dec3.GetChars(new byte[] { 0x61 }, 0, 1, cb3, d3, true);
            byte[] bb = new byte[10];
            int e1 = enc.GetBytes(new char[] { 'a', '\uD83D' }, 0, 2, bb, 0, false);
            int e2 = enc.GetBytes(new char[] { '\uDE00', 'b' }, 0, 2, bb, e1, false);
            string ehex = Hex(new ReadOnlySpan<byte>(bb, 0, e1 + e2).ToArray());
            var enc2 = u.GetEncoder(); byte[] bb2 = new byte[10];
            int e3 = enc2.GetBytes(new char[] { '\uD83D' }, 0, 1, bb2, 0, true);
            string ehex2 = Hex(new ReadOnlySpan<byte>(bb2, 0, e3).ToArray());
            var enc3 = u.GetEncoder(); byte[] bb3 = new byte[10];
            int e4 = enc3.GetBytes(new char[] { '\uD83D' }, 0, 1, bb3, 0, false); int e5 = enc3.GetBytes(new char[] { 'x' }, 0, 1, bb3, e4, true);
            string ehex3 = Hex(new ReadOnlySpan<byte>(bb3, 0, e4 + e5).ToArray());
            return Par.J(Q(() => c1), Q(() => c2), Q(() => stream), Q(() => d1), Q(() => s2hex), Q(() => d3), Q(() => d4), Q(() => new string(cb3, 0, d3 + d4).Length), Q(() => e1), Q(() => e2), Q(() => ehex), Q(() => e3), Q(() => ehex2), Q(() => e4), Q(() => e5), Q(() => ehex3), Q(() => dec.GetCharCount(new byte[] { 0xE2 }, 0, 1, false)), Q(() => dec.GetCharCount(new byte[] { 0xE2 }, 0, 1, true)), Q(() => enc.GetByteCount(new char[] { '\uD83D' }, 0, 1, false)), Q(() => enc.GetByteCount(new char[] { '\uD83D' }, 0, 1, true)), Q(() => u.DecoderFallback.GetType().Name), Q(() => u.EncoderFallback.GetType().Name), Q(() => new UTF8Encoding(false, true).DecoderFallback.GetType().Name), Q(() => Encoding.ASCII.EncoderFallback.GetType().Name), Q(() => u.DecoderFallback.MaxCharCount), Q(() => Encoding.ASCII.EncoderFallback.MaxCharCount), Q(() => ((DecoderReplacementFallback)u.DecoderFallback).DefaultString), Q(() => ((EncoderReplacementFallback)Encoding.ASCII.EncoderFallback).DefaultString), Q(() => u.IsReadOnly), Q(() => u.Clone().GetType().Name), Q(() => ((Encoding)u.Clone()).IsReadOnly), Q(() => u.GetHashCode() == Encoding.UTF8.GetHashCode()), Q(() => u.BodyName), Q(() => u.HeaderName), Q(() => u.IsBrowserDisplay), Q(() => u.IsMailNewsDisplay), Q(() => u.WindowsCodePage), Q(() => Encoding.Unicode.WindowsCodePage), Q(() => Encoding.ASCII.IsAlwaysNormalized()));
        }

        static string SbAppendJoin()
        {
            // buyuk SB: chunk sinirlari uzerinden ToString(start,len), indexer, Replace, Insert, Remove, Equals
            var sb = new StringBuilder(8);
            for (int i = 0; i < 40; i++) sb.Append("chunk").Append(i).Append(';');
            string all = sb.ToString();
            string mid = sb.ToString(100, 50), tail = sb.ToString(sb.Length - 20, 20), head = sb.ToString(0, 7);
            char c150 = sb[150]; sb[150] = '#';
            sb.Replace("chunk1", "C1"); sb.Insert(200, "<INS>"); sb.Remove(10, 30);
            var sb2 = new StringBuilder(all); sb2[150] = '#'; sb2.Replace("chunk1", "C1"); sb2.Insert(200, "<INS>"); sb2.Remove(10, 30);
            bool eq = sb.Equals(sb2), eqs = sb.ToString() == sb2.ToString();
            int sum = 0; for (int i = 0; i < sb.Length; i++) sum += sb[i];
            var sb3 = new StringBuilder(); for (int i = 0; i < 1000; i++) sb3.Append((char)('a' + i % 26)); sb3.Insert(500, new string('Z', 300)); sb3.Remove(100, 700);
            var sb4 = new StringBuilder(); for (int i = 0; i < 100; i++) sb4.Insert(0, (i % 10).ToString());
            var sb5 = new StringBuilder("x"); for (int i = 0; i < 10; i++) sb5.Append(sb5.ToString());
            return Par.J(Q(() => all.Length), Q(() => mid), Q(() => tail), Q(() => head), Q(() => c150), Q(() => eq), Q(() => eqs), Q(() => sb.Length), Q(() => sum), Q(() => sb.ToString(195, 20)), Q(() => sb3.Length), Q(() => sb3.ToString(95, 15)), Q(() => sb4.ToString()), Q(() => sb5.Length), Q(() => sb5.ToString(0, 5)), Q(() => sb.ToString() == all ? "unchanged" : "changed"), Q(() => all.Substring(0, 12)), Q(() => sb.ToString().IndexOf("<INS>")), Q(() => sb.ToString().IndexOf("C1")), Q(() => new StringBuilder(all).Replace("chunk", "").Length), Q(() => new StringBuilder(all).Replace(";", "").Length), Q(() => new StringBuilder(all).Replace("chunk", "", 0, 50).ToString().Substring(0, 20)));
        }
    }
}
