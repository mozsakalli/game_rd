using System;
using ParityKit;

// 91: string kenarlari — null/bos, sinirlar, Split/Replace/Trim, buyuk-kucuk harf invariant, surrogate, karsilastirma, interning.
namespace Demo91
{
    class App91
    {
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static int I(int v) { return v; }
        static string S(string v) { return v; }

        public static int Run() { return Par.Loop(91, 28, Step); }

        static string Step(int k)
        {
            switch (k)
            {
                case 1: return NullEmpty();
                case 2: return ConcatNull();
                case 3: return SubstringBounds();
                case 4: return IndexOfEdges();
                case 5: return ReplaceEdges();
                case 6: return SplitChar();
                case 7: return SplitString();
                case 8: return SplitOptions();
                case 9: return TrimPad();
                case 10: return CaseInvariant();
                case 11: return TurkishI();
                case 12: return Surrogates();
                case 13: return CompareOrdinal();
                case 14: return CompareCulture();
                case 15: return EqualsVariants();
                case 16: return Interning();
                case 17: return EmbeddedNul();
                case 18: return JoinConcat();
                case 19: return IndexerBounds();
                case 20: return StartsEnds();
                case 21: return InsertRemove();
                case 22: return CharArrayCtor();
                case 23: return ContainsLastIndex();
                case 24: return HashConsistency();
                case 25: return StringMisc();
                case 26: return ToCharArrayCopy();
                case 27: return CompareToSort();
                case 28: return NormalizeAndUnicode();
            }
            return "?";
        }

        static string P(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static string V(Action a) { try { a(); return "ok"; } catch (Exception e) { return Par.Ex(e); } }

        static string NullEmpty()
        {
            string n = S(null), e = S(""), w = S("  \t\n"), nb = S("\u00A0"), z = S("\u200B");
            return Par.J(Q(() => string.IsNullOrEmpty(n)), Q(() => string.IsNullOrEmpty(e)), Q(() => string.IsNullOrEmpty(S("a"))), Q(() => string.IsNullOrWhiteSpace(n)), Q(() => string.IsNullOrWhiteSpace(e)), Q(() => string.IsNullOrWhiteSpace(w)), Q(() => string.IsNullOrWhiteSpace(nb)), Q(() => string.IsNullOrWhiteSpace(z)), Q(() => string.IsNullOrWhiteSpace(S(" a "))), Q(() => e.Length), Q(() => e == ""), Q(() => e == string.Empty), Q(() => ReferenceEquals(e, string.Empty)), Q(() => ReferenceEquals(S("") + S(""), string.Empty)), Q(() => n == null), Q(() => n == e), Q(() => string.Equals(n, e)), Q(() => string.Equals(n, n)), Q(() => object.Equals(n, null)), P(() => n.Length), P(() => n.ToString()), Q(() => e.ToString() == e), Q(() => ReferenceEquals(e.ToString(), e)), Q(() => string.Compare(n, e)), Q(() => string.Compare(e, n)), Q(() => string.Compare(n, n)), Q(() => string.CompareOrdinal(n, e)), Q(() => e.GetHashCode() == S("").GetHashCode()), Q(() => e.CompareTo(n)), P(() => e.CompareTo(I(5))), Q(() => string.Concat(n, n) == ""), Q(() => string.Concat(e, e).Length), Q(() => (n + n) == ""), Q(() => (n + e) == ""), Q(() => n + "x"), Q(() => "x" + n), Q(() => string.Empty.Length));
        }

        static string ConcatNull()
        {
            string n = S(null); object on = null; object oi = I(5); int i = I(7); char c = (char)I(65); bool b = true; double d = 1.5; object[] arr = null;
            return Par.J(Q(() => "a" + n), Q(() => n + "b"), Q(() => "a" + on), Q(() => "a" + oi), Q(() => "a" + i), Q(() => "a" + c), Q(() => "a" + b), Q(() => "a" + d), Q(() => "a" + (object)null + "b"), Q(() => string.Concat("a", n, "b")), Q(() => string.Concat(on, on)), Q(() => string.Concat(oi, on)), Q(() => string.Concat("a", "b", "c", "d", "e")), Q(() => string.Concat(new string[] { "a", null, "b" })), Q(() => string.Concat(new object[] { 1, null, "x", 2.5, 'c', true })), P(() => string.Concat((string[])null)), P(() => string.Concat(arr)), Q(() => string.Concat(new object[0])), Q(() => string.Concat((object)null)), Q(() => string.Concat(I(1), I(2))), Q(() => string.Concat(I(1), I(2), I(3))), Q(() => string.Concat(I(1), I(2), I(3), I(4))), Q(() => string.Concat(new System.Collections.Generic.List<string> { "x", null, "y" })), Q(() => string.Concat(new System.Collections.Generic.List<int> { 1, 2 })), Q(() => "" + c + c), Q(() => c + "" + c), Q(() => c + c + ""), Q(() => 'a' + 'b' + ""), Q(() => "" + 'a' + 'b'), Q(() => "x" + 1 + 2), Q(() => 1 + 2 + "x"), Q(() => "x" + (1 + 2)), Q(() => "x" + 1.0f), Q(() => "x" + 0.1), Q(() => "x" + (object)0.1f), Q(() => "x" + 1e21), Q(() => "x" + double.NaN), Q(() => "x" + (-0.0)), Q(() => "x" + long.MinValue), Q(() => "x" + (byte)200), Q(() => "x" + (sbyte)-1), Q(() => "x" + 'a' + 1), Q(() => "x" + ('a' + 1)), Q(() => "x" + (char)('a' + 1)), Q(() => "" + (string)null + (string)null), Q(() => string.Concat<int>(new int[] { 1, 2 })), Q(() => string.Concat<object>(new object[] { "a", null })));
        }

        static string SubstringBounds()
        {
            string s = S("hello"), e = S("");
            return Par.J(Q(() => s.Substring(0)), Q(() => s.Substring(5)), Q(() => s.Substring(0, 0)), Q(() => s.Substring(5, 0)), Q(() => s.Substring(2, 3)), Q(() => s.Substring(1, 2)), P(() => s.Substring(6)), P(() => s.Substring(-1)), P(() => s.Substring(0, 6)), P(() => s.Substring(3, 3)), P(() => s.Substring(0, -1)), P(() => s.Substring(6, 0)), Q(() => e.Substring(0)), Q(() => e.Substring(0, 0)), P(() => e.Substring(1)), Q(() => ReferenceEquals(s.Substring(0), s)), Q(() => ReferenceEquals(s.Substring(0, 5), s)), Q(() => ReferenceEquals(s.Substring(5), "")), Q(() => ReferenceEquals(s.Substring(2, 0), string.Empty)), Q(() => s[1..3]), Q(() => s[..2]), Q(() => s[3..]), Q(() => s[^2..]), Q(() => s[..^1]), Q(() => s[^0..]), P(() => s[3..2]), P(() => s[..6]), Q(() => s[new Range(1, 3)]), Q(() => s[new Index(1)]), Q(() => s[^1]), P(() => s[^0]), Q(() => s.AsSpan(1, 2).ToString()), Q(() => s.AsSpan(5).Length), P(() => s.AsSpan(6).Length), Q(() => s.AsSpan()[^1]), Q(() => new string(s.AsSpan(1, 3))));
        }

        static string IndexOfEdges()
        {
            string s = S("abcabc"), e = S("");
            return Par.J(Q(() => s.IndexOf("")), Q(() => s.IndexOf("", 3)), Q(() => s.IndexOf("", 6)), P(() => s.IndexOf("", 7)), Q(() => e.IndexOf("")), Q(() => e.IndexOf('x')), Q(() => e.IndexOf("x")), Q(() => s.IndexOf('c')), Q(() => s.IndexOf('c', 3)), Q(() => s.IndexOf('c', 6)), P(() => s.IndexOf('c', 7)), P(() => s.IndexOf('c', -1)), Q(() => s.IndexOf("bc")), Q(() => s.IndexOf("bc", 2)), Q(() => s.IndexOf("bc", 4)), Q(() => s.IndexOf("bc", 5)), Q(() => s.IndexOf("bca")), Q(() => s.IndexOf("abcabcd")), Q(() => s.IndexOf('a', 0, 0)), Q(() => s.IndexOf('a', 1, 3)), Q(() => s.IndexOf('c', 1, 2)), P(() => s.IndexOf('c', 5, 2)), P(() => s.IndexOf((string)null)), Q(() => s.IndexOf("B")), Q(() => s.IndexOf("B", StringComparison.OrdinalIgnoreCase)), Q(() => s.IndexOf("B", StringComparison.Ordinal)), Q(() => s.IndexOf("\0")), Q(() => s.IndexOf('\0')), Q(() => s.IndexOf("abc", 1, StringComparison.Ordinal)), Q(() => s.IndexOf("abc", 1, 4, StringComparison.Ordinal)), Q(() => s.IndexOf("abc", 1, 5, StringComparison.Ordinal)), Q(() => s.IndexOfAny(new[] { 'z', 'c' })), Q(() => s.IndexOfAny(new[] { 'z' })), Q(() => s.IndexOfAny(new char[0])), Q(() => s.IndexOfAny(new[] { 'a' }, 1)), Q(() => s.IndexOfAny(new[] { 'a' }, 1, 2)), Q(() => s.IndexOfAny(new[] { 'a' }, 1, 3)), Q(() => s.IndexOf("ABC", StringComparison.CurrentCultureIgnoreCase)), Q(() => s.IndexOf("ABC", StringComparison.InvariantCultureIgnoreCase)), Q(() => S("a\u0301").IndexOf("\u0301", StringComparison.Ordinal)), Q(() => S("a\u0301").IndexOf("\u0301")), Q(() => S("ab").IndexOf("", StringComparison.Ordinal)), Q(() => S("ab").IndexOf("\u0000")));
        }

        static string ReplaceEdges()
        {
            string s = S("aaa"), e = S("");
            return Par.J(Q(() => s.Replace("a", "b")), Q(() => s.Replace("aa", "b")), Q(() => s.Replace("aaa", "")), Q(() => s.Replace("a", "")), Q(() => s.Replace("a", "aa")), Q(() => s.Replace("x", "y")), Q(() => ReferenceEquals(s.Replace("x", "y"), s)), P(() => s.Replace("", "x")), P(() => s.Replace((string)null, "x")), Q(() => s.Replace("a", (string)null)), Q(() => s.Replace('a', 'b')), Q(() => s.Replace('x', 'y')), Q(() => ReferenceEquals(s.Replace('x', 'y'), s)), Q(() => e.Replace("a", "b")), P(() => e.Replace("", "b")), Q(() => s.Replace("A", "b")), Q(() => s.Replace("A", "b", StringComparison.OrdinalIgnoreCase)), Q(() => s.Replace("A", "b", true, null)), Q(() => S("abcabc").Replace("bc", "x")), Q(() => S("abcabc").Replace("abcabc", "y")), Q(() => S("abcabcab").Replace("ab", "ba")), Q(() => S("ababab").Replace("aba", "x")), Q(() => S("aaaa").Replace("aa", "a")), Q(() => S("a.b.c").Replace(".", "")), Q(() => S("\0a\0").Replace("\0", "n")), Q(() => S("ğüş").Replace("ü", "u")), Q(() => S("😀😀").Replace("😀", "x")), Q(() => S("😀😀").Replace("\uD83D", "x")), Q(() => S("abc").Replace("b", "😀")), Q(() => s.Replace("a", "b") == "bbb"), Q(() => S("hello").Replace('l', '\0').Length), Q(() => S("a").Replace("a", "a") == "a"), Q(() => ReferenceEquals(S("a").Replace("a", "a"), S("a"))));
        }

        static string SplitChar()
        {
            var sb = new System.Text.StringBuilder();
            string[] inputs = { "a,b,c", "a,,b", ",a,b,", ",", ",,", "", "abc", "a,b,c,", ",a" };
            foreach (var inp in inputs)
            {
                var parts = S(inp).Split(',');
                sb.Append(parts.Length).Append(':');
                foreach (var p in parts) sb.Append('[').Append(p).Append(']');
                sb.Append(';');
            }
            var m = S("a,b;c d").Split(',', ';', ' ');
            sb.Append(m.Length).Append(string.Join("/", m)).Append(';');
            var n = S("a b  c").Split((char[])null);
            sb.Append(n.Length).Append(string.Join("/", n)).Append(';');
            var n2 = S("a b\tc\nd").Split(new char[0]);
            sb.Append(n2.Length).Append(string.Join("/", n2)).Append(';');
            var n3 = S("a b").Split();
            sb.Append(n3.Length).Append(string.Join("/", n3)).Append(';');
            var c2 = S("a,b,c,d").Split(',', 2);
            sb.Append(c2.Length).Append(string.Join("/", c2)).Append(';');
            var c0 = S("a,b").Split(',', 1);
            sb.Append(c0.Length).Append(string.Join("/", c0)).Append(';');
            sb.Append(P(() => S("a,b").Split(',', 0).Length)).Append(';');
            sb.Append(P(() => S("a,b").Split(',', -1).Length)).Append(';');
            var c3 = S("a,b,c").Split(new[] { ',' }, 2);
            sb.Append(c3.Length).Append(string.Join("/", c3)).Append(';');
            var c4 = S("a,,b,").Split(',', 3);
            sb.Append(c4.Length).Append(string.Join("/", c4)).Append(';');
            var c5 = S("ab").Split('a');
            sb.Append(c5.Length).Append('[').Append(c5[0]).Append(']').Append('[').Append(c5[1]).Append(']').Append(';');
            var c6 = S("😀,😀").Split(',');
            sb.Append(c6.Length).Append(c6[0].Length).Append(';');
            return sb.ToString();
        }

        static string SplitString()
        {
            var sb = new System.Text.StringBuilder();
            void Dump(string[] parts) { sb.Append(parts.Length).Append(':'); foreach (var p in parts) sb.Append('[').Append(p).Append(']'); sb.Append(';'); }
            Dump(S("a::b").Split("::"));
            Dump(S("a::b").Split(":"));
            Dump(S("a:b:c").Split(":", 2));
            Dump(S("::").Split("::"));
            Dump(S("a").Split("::"));
            Dump(S("").Split("::"));
            Dump(S("abab").Split("ab"));
            Dump(S("aaa").Split("aa"));
            Dump(S("a,b;c").Split(new[] { ",", ";" }, StringSplitOptions.None));
            Dump(S("a,b;c").Split(new[] { ";,", "," }, StringSplitOptions.None));
            Dump(S("a,;b").Split(new[] { ",;", "," }, StringSplitOptions.None));
            Dump(S("a,;b").Split(new[] { ",", ",;" }, StringSplitOptions.None));
            Dump(S("a b").Split((string[])null, StringSplitOptions.None));
            Dump(S("a b").Split(new string[0], StringSplitOptions.None));
            Dump(S("a b").Split(new string[] { null, "" }, StringSplitOptions.None));
            Dump(S("a b").Split(new string[] { null, " " }, StringSplitOptions.None));
            Dump(S("a").Split((string)null));
            Dump(S("a b").Split(""));
            Dump(S("xaxbx").Split("x", 3));
            Dump(S("xaxbx").Split("x", 2));
            Dump(S("a😀b").Split("😀"));
            return sb.ToString();
        }

        static string SplitOptions()
        {
            var sb = new System.Text.StringBuilder();
            void Dump(string[] parts) { sb.Append(parts.Length).Append(':'); foreach (var p in parts) sb.Append('[').Append(p).Append(']'); sb.Append(';'); }
            Dump(S("a,,b,").Split(',', StringSplitOptions.RemoveEmptyEntries));
            Dump(S(",,,").Split(',', StringSplitOptions.RemoveEmptyEntries));
            Dump(S("").Split(',', StringSplitOptions.RemoveEmptyEntries));
            Dump(S(" a , b ,, ").Split(',', StringSplitOptions.TrimEntries));
            Dump(S(" a , b ,, ").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            Dump(S("  ").Split(',', StringSplitOptions.TrimEntries));
            Dump(S("  ").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            Dump(S("a,b,c,d").Split(',', 2, StringSplitOptions.RemoveEmptyEntries));
            Dump(S(",a,,b,c").Split(',', 2, StringSplitOptions.RemoveEmptyEntries));
            Dump(S(",a,,b,c").Split(',', 3, StringSplitOptions.RemoveEmptyEntries));
            Dump(S(" a , b , c ").Split(',', 2, StringSplitOptions.TrimEntries));
            Dump(S("a::b::::c").Split("::", StringSplitOptions.RemoveEmptyEntries));
            Dump(S("a::b::::c").Split("::", 2, StringSplitOptions.RemoveEmptyEntries));
            Dump(S("a,b;c").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
            Dump(S("a, b ;c").Split(new[] { ",", ";" }, StringSplitOptions.TrimEntries));
            Dump(S("a,b").Split(new[] { ',' }, 1, StringSplitOptions.RemoveEmptyEntries));
            Dump(S(",,a").Split(new[] { ',' }, 1, StringSplitOptions.RemoveEmptyEntries));
            Dump(S(",,a,b").Split(',', 1, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            sb.Append(P(() => S("a").Split(',', (StringSplitOptions)4).Length));
            return sb.ToString();
        }

        static string TrimPad()
        {
            string s = S("  ab  "), t = S("xxabxx"), e = S(""), w = S("\t\n\r a \u00A0\u2003"), z = S("\u200B a \uFEFF");
            return Par.J(Q(() => "[" + s.Trim() + "]"), Q(() => "[" + s.TrimStart() + "]"), Q(() => "[" + s.TrimEnd() + "]"), Q(() => t.Trim('x')), Q(() => t.TrimStart('x')), Q(() => t.TrimEnd('x')), Q(() => t.Trim('x', 'a')), Q(() => t.Trim(new char[0])), Q(() => t.Trim((char[])null)), Q(() => "[" + e.Trim() + "]"), Q(() => "[" + w.Trim() + "]"), Q(() => "[" + z.Trim() + "]"), Q(() => ReferenceEquals(S("ab").Trim(), S("ab")) ? "same" : "copy"), Q(() => S("ab").Trim().Length), Q(() => S("   ").Trim().Length), Q(() => ReferenceEquals(S("   ").Trim(), string.Empty)), Q(() => S("aXa").Trim('a')), Q(() => S("a").Trim('a').Length), Q(() => S("xx").Trim('x') == ""),
                Q(() => S("ab").PadLeft(5)), Q(() => S("ab").PadLeft(5, '*')), Q(() => S("ab").PadRight(5, '*')), Q(() => S("ab").PadLeft(1)), Q(() => S("ab").PadLeft(2)), Q(() => S("ab").PadLeft(0)), P(() => S("ab").PadLeft(-1)), Q(() => ReferenceEquals(S("ab").PadLeft(2), S("ab")) ? "same" : "copy"), Q(() => S("").PadLeft(3, '-')), Q(() => S("😀").PadLeft(3, '.')), Q(() => S("ab").PadRight(5).Length), Q(() => S("ab").PadLeft(5, '\0').Length), Q(() => S("ab").PadLeft(4, S("😀")[0]).Length));
        }

        static string CaseInvariant()
        {
            string s = S("Hello Wörld ÀÉÎÕÜ ğüşiöç ß ﬁ Ω σς µ");
            return Par.J(Q(() => s.ToUpper()), Q(() => s.ToLower()), Q(() => s.ToUpperInvariant()), Q(() => s.ToLowerInvariant()), Q(() => S("ß").ToUpper()), Q(() => S("ß").ToUpperInvariant()), Q(() => S("ﬁ").ToUpper()), Q(() => S("ς").ToUpper()), Q(() => S("Σ").ToLower()), Q(() => S("ΑΣ").ToLower()), Q(() => S("µ").ToUpper()), Q(() => S("ǅ").ToUpper()), Q(() => S("ǅ").ToLower()), Q(() => S("ǆ").ToUpper()), Q(() => S("Ǆ").ToLower()), Q(() => S("ſ").ToUpper()), Q(() => S("K").ToLower()), Q(() => S("ı").ToUpper()), Q(() => S("İ").ToLower()), Q(() => S("İ").ToLower().Length), Q(() => S("i").ToUpper()), Q(() => S("I").ToLower()), Q(() => S("ⅰ").ToUpper()), Q(() => S("ⓐ").ToUpper()), Q(() => S("𐐨").ToUpper()), Q(() => S("𐐨").ToUpper().Length), Q(() => S("a😀B").ToUpper()), Q(() => S("a😀B").ToLower()), Q(() => S("ABC").ToLower() == "abc"), Q(() => ReferenceEquals(S("abc").ToLower(), S("abc")) ? "same" : "copy"), Q(() => S("").ToUpper().Length), Q(() => S("123!@#").ToUpper()), Q(() => char.ToUpper('ß')), Q(() => char.ToUpperInvariant('ı')), Q(() => char.ToLowerInvariant('İ')), Q(() => char.ToUpper('ﬁ')), Q(() => char.ToLower('Σ')), Q(() => char.ToUpper('ς')), Q(() => char.ToUpper('µ')), Q(() => char.ToLower('K')), Q(() => char.ToUpper('ǅ')), Q(() => char.ToLower('ǅ')), Q(() => char.ToUpperInvariant('ſ')), Q(() => char.ToUpper('\uD83D') == '\uD83D'));
        }

        static string TurkishI()
        {
            // InvariantGlobalization=true: ozgul kultur olusturma CultureNotFoundException verebilir -> yalniz P ile gozlenir
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string trProbe = P(() => new System.Globalization.CultureInfo("tr-TR").Name);
            string trUpper = P(() => S("i").ToUpper(new System.Globalization.CultureInfo("tr-TR")));
            return Par.J(Q(() => trProbe), Q(() => trUpper), Q(() => S("i").ToUpper(inv)), Q(() => S("I").ToLower(inv)), Q(() => S("ı").ToUpper(inv)), Q(() => S("İ").ToLower(inv)), Q(() => S("istanbul").ToUpper(inv)), Q(() => S("ISTANBUL").ToLower(inv)), Q(() => S("file").ToUpper(inv)), Q(() => string.Compare("i", "I", true, inv)), Q(() => string.Compare("i", "I", StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("ı", "I", StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("İ", "i", StringComparison.OrdinalIgnoreCase)), Q(() => string.Equals("ı", "I", StringComparison.OrdinalIgnoreCase)), Q(() => string.Equals("İ", "i", StringComparison.InvariantCultureIgnoreCase)), Q(() => string.Equals("ı", "I", StringComparison.InvariantCultureIgnoreCase)), Q(() => string.Equals("i", "I", StringComparison.InvariantCultureIgnoreCase)), Q(() => S("Iİıi").ToUpperInvariant()), Q(() => S("Iİıi").ToLowerInvariant()), Q(() => char.ToUpper('i', inv)), Q(() => char.ToLower('I', inv)), Q(() => inv.Name.Length), Q(() => inv.TwoLetterISOLanguageName), Q(() => inv.TextInfo.ToUpper("i")), Q(() => inv.TextInfo.ToTitleCase("istanbul ılık")), Q(() => inv.TextInfo.ToTitleCase("hello wORLD ğüş")), Q(() => inv.TextInfo.ToTitleCase("a-b c'd e.f")), Q(() => inv.TextInfo.IsRightToLeft), Q(() => inv.CompareInfo.Compare("a", "b")), Q(() => inv.CompareInfo.Compare("ç", "d")), Q(() => string.Compare("ç", "d", inv, System.Globalization.CompareOptions.None)), Q(() => string.Compare("ç", "d", StringComparison.Ordinal)), Q(() => System.Globalization.CultureInfo.CurrentCulture.Name.Length), Q(() => System.Globalization.CultureInfo.GetCultureInfo("").Name.Length), P(() => System.Globalization.CultureInfo.GetCultureInfo("en-US").Name));
        }

        static string Surrogates()
        {
            string e = S("😀"), m = S("a😀b"), lone = S("\uD83D"), bad = S("\uDE00\uD83D");
            return Par.J(Q(() => e.Length), Q(() => m.Length), Q(() => e[0] == '\uD83D'), Q(() => e[1] == '\uDE00'), Q(() => char.IsSurrogate(e[0])), Q(() => char.IsHighSurrogate(e[0])), Q(() => char.IsLowSurrogate(e[1])), Q(() => char.IsSurrogatePair(e[0], e[1])), Q(() => char.IsSurrogatePair(e[1], e[0])), Q(() => char.ConvertToUtf32(e, 0)), P(() => char.ConvertToUtf32(e, 1)), P(() => char.ConvertToUtf32(lone, 0)), Q(() => char.ConvertFromUtf32(0x1F600) == e), P(() => char.ConvertFromUtf32(0xD800)), P(() => char.ConvertFromUtf32(0x110000)), Q(() => char.ConvertFromUtf32(0x41)), Q(() => e.Substring(1).Length), Q(() => e.Substring(1) == "\uDE00"), Q(() => m.IndexOf(S("😀")[0])), Q(() => m.IndexOf("😀")), Q(() => m.IndexOf("\uDE00")), Q(() => m.Contains("\uD83D")), Q(() => m.ToUpper() == m), Q(() => m.Reverse()), Q(() => new string(e.ToCharArray().Reverse2())), Q(() => e.Normalize() == e), Q(() => lone.Normalize() == lone), P(() => lone.Normalize().Length), Q(() => lone.IsNormalized()), Q(() => e.IsNormalized()), Q(() => bad.Length), Q(() => bad.IsNormalized()), Q(() => string.Compare(e, "z", StringComparison.Ordinal) > 0), Q(() => string.Compare(e, "z") > 0), Q(() => string.Compare(e, "\uFFFF", StringComparison.Ordinal) > 0), Q(() => string.Compare(e, "\uFFFF") > 0), Q(() => e.CompareTo("\uFFFF") > 0), Q(() => string.CompareOrdinal(e, "\uFFFF") > 0), Q(() => e.GetHashCode() == S("\uD83D\uDE00").GetHashCode()), Q(() => e.EnumerateRunes().Count2()), Q(() => m.EnumerateRunes().Count2()), Q(() => lone.EnumerateRunes().Count2()), Q(() => new System.Text.Rune(0x1F600).Utf16SequenceLength), Q(() => new System.Text.Rune(0x1F600).Utf8SequenceLength), Q(() => System.Text.Rune.IsValid(0xD800)), Q(() => System.Text.Rune.GetRuneAt(m, 1).Value), P(() => System.Text.Rune.GetRuneAt(m, 2).Value), System.Text.Rune.TryGetRuneAt(lone, 0, out _), Q(() => System.Text.Rune.ReplacementChar.Value), Q(() => char.GetUnicodeCategory(e[0])), Q(() => char.GetUnicodeCategory(S("😀")[1])), Q(() => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(m, 1)), Q(() => System.Globalization.StringInfo.GetNextTextElement(m, 1).Length), Q(() => new System.Globalization.StringInfo(m).LengthInTextElements), Q(() => new System.Globalization.StringInfo(S("e\u0301")).LengthInTextElements), Q(() => new System.Globalization.StringInfo(S("👨‍👩‍👧")).LengthInTextElements), Q(() => S("👨‍👩‍👧").Length));
        }

        static string CompareOrdinal()
        {
            return Par.J(Q(() => string.CompareOrdinal("a", "b")), Q(() => string.CompareOrdinal("b", "a")), Q(() => string.CompareOrdinal("a", "a")), Q(() => string.CompareOrdinal("a", "B")), Q(() => string.CompareOrdinal("A", "b")), Q(() => string.CompareOrdinal("abc", "ab")), Q(() => string.CompareOrdinal("ab", "abc")), Q(() => string.CompareOrdinal("", "a")), Q(() => string.CompareOrdinal("a", "")), Q(() => string.CompareOrdinal(null, "")), Q(() => string.CompareOrdinal("", null)), Q(() => string.CompareOrdinal(null, null)), Q(() => string.CompareOrdinal("a", "\u0101")), Q(() => string.CompareOrdinal("😀", "\uFFFF")), Q(() => string.CompareOrdinal("abcd", 1, "xbcy", 1, 2)), Q(() => string.CompareOrdinal("abcd", 1, "xbcy", 1, 3)), Q(() => string.CompareOrdinal("abc", 0, "abd", 0, 10)), P(() => string.CompareOrdinal("abc", 4, "abd", 0, 1)), Q(() => string.CompareOrdinal("abc", 3, "abd", 3, 1)), Q(() => string.CompareOrdinal("a", 0, "b", 0, 0)), Q(() => string.Compare("a", "B", StringComparison.Ordinal)), Q(() => string.Compare("a", "B", StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("abc", "ABD", StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("ǆ", "Ǆ", StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("ǆ", "ǅ", StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("ß", "SS", StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("ı", "I", StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("i", "İ", StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("K", "K", StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("a", null, StringComparison.Ordinal)), Q(() => string.Compare(null, "a", StringComparison.Ordinal)), Q(() => string.Compare(null, null, StringComparison.Ordinal)), Q(() => string.Compare("abc", 1, "xbc", 1, 2, StringComparison.Ordinal)), Q(() => string.Compare("abc", 1, "xBC", 1, 2, StringComparison.OrdinalIgnoreCase)), Q(() => string.Compare("abc", 1, "xBC", 1, 2, StringComparison.Ordinal)), Q(() => string.Compare("ab", "abc", StringComparison.Ordinal)), Q(() => string.Compare("abc", "ab", StringComparison.OrdinalIgnoreCase)), Q(() => Math.Sign(string.CompareOrdinal("Z", "a"))), Q(() => Math.Sign(string.CompareOrdinal("z", "A"))), Q(() => Math.Sign(string.CompareOrdinal("0", "a"))), Q(() => Math.Sign(string.CompareOrdinal("ä", "z"))));
        }

        static string CompareCulture()
        {
            // InvariantGlobalization=true: kultur karsilastirmasi ordinal'e duser (.NET davranisi); AOT ayni olmali
            return Par.J(Q(() => string.Compare("a", "B")), Q(() => string.Compare("a", "b")), Q(() => string.Compare("A", "a")), Q(() => string.Compare("a", "A")), Q(() => string.Compare("abc", "ABD")), Q(() => string.Compare("a", "B", true)), Q(() => string.Compare("a", "B", false)), Q(() => string.Compare("ä", "z")), Q(() => string.Compare("ä", "b")), Q(() => string.Compare("é", "f")), Q(() => string.Compare("ß", "ss")), Q(() => string.Compare("co-op", "coop")), Q(() => string.Compare("a-b", "ab")), Q(() => string.Compare("10", "9")), Q(() => string.Compare("ǅ", "ǆ")), Q(() => string.Compare("a", "B", StringComparison.CurrentCulture)), Q(() => string.Compare("a", "B", StringComparison.CurrentCultureIgnoreCase)), Q(() => string.Compare("a", "B", StringComparison.InvariantCulture)), Q(() => string.Compare("a", "B", StringComparison.InvariantCultureIgnoreCase)), Q(() => "a".CompareTo("B")), Q(() => "B".CompareTo("a")), Q(() => "a".CompareTo("b")), Q(() => "abc".CompareTo("abd")), Q(() => "abc".CompareTo("ab")), Q(() => "".CompareTo("a")), Q(() => "a".CompareTo((string)null)), Q(() => "a".CompareTo((object)"b")), Q(() => "😀".CompareTo("z")), Q(() => "ä".CompareTo("z")), Q(() => string.Compare("\u00C5", "\u0041\u030A")), Q(() => string.Compare("\u00C5", "\u0041\u030A", StringComparison.Ordinal)), Q(() => string.Equals("\u00C5", "\u0041\u030A", StringComparison.InvariantCulture)), Q(() => string.Compare("a\u0000", "a")), Q(() => string.Compare("a\u200B", "a")), Q(() => string.Compare("a\u200B", "a", StringComparison.Ordinal)), Q(() => System.Globalization.CultureInfo.CurrentCulture.CompareInfo.Compare("a", "B")), Q(() => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare("a", "B", System.Globalization.CompareOptions.IgnoreCase)), Q(() => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare("a", "B", System.Globalization.CompareOptions.Ordinal)), Q(() => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare("a-b", "ab", System.Globalization.CompareOptions.IgnoreSymbols)), Q(() => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare("ä", "a", System.Globalization.CompareOptions.IgnoreNonSpace)), Q(() => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare("a", "A", System.Globalization.CompareOptions.IgnoreCase)), Q(() => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare("10", "9", System.Globalization.CompareOptions.NumericOrdering)));
        }

        static string EqualsVariants()
        {
            string a = S("abc"), b = new string(new[] { 'a', 'b', 'c' }), c = S("ABC"), n = null;
            return Par.J(Q(() => a == b), Q(() => a.Equals(b)), Q(() => ReferenceEquals(a, b)), Q(() => a.Equals((object)b)), Q(() => a.Equals((object)null)), Q(() => a.Equals(n)), Q(() => a == n), Q(() => n == null), Q(() => string.Equals(a, n)), Q(() => string.Equals(n, n)), Q(() => string.Equals(a, c)), Q(() => string.Equals(a, c, StringComparison.OrdinalIgnoreCase)), Q(() => string.Equals(a, c, StringComparison.Ordinal)), Q(() => string.Equals(a, c, StringComparison.InvariantCultureIgnoreCase)), Q(() => string.Equals(a, c, StringComparison.CurrentCulture)), Q(() => a.Equals(c, StringComparison.OrdinalIgnoreCase)), Q(() => string.Equals(n, "", StringComparison.OrdinalIgnoreCase)), Q(() => string.Equals("ß", "SS", StringComparison.OrdinalIgnoreCase)), Q(() => string.Equals("ß", "ss", StringComparison.InvariantCultureIgnoreCase)), Q(() => string.Equals("ǆ", "Ǆ", StringComparison.OrdinalIgnoreCase)), Q(() => string.Equals("\u00C5", "\u0041\u030A")), Q(() => string.Equals("\u00C5", "\u0041\u030A", StringComparison.InvariantCulture)), Q(() => (object)a == (object)b), Q(() => (object)a == (object)a), Q(() => object.Equals(a, b)), Q(() => object.ReferenceEquals(a, b)), Q(() => a.Equals(I(1))), Q(() => a.Equals('a')), Q(() => ((object)a).Equals(b)), Q(() => a.AsSpan().SequenceEqual(b.AsSpan())), Q(() => a.AsSpan().Equals(c.AsSpan(), StringComparison.OrdinalIgnoreCase)), Q(() => a.Equals(S("abc\0"))), Q(() => S("abc\0").Equals(a)), Q(() => S("abc\0").Length), Q(() => a.CompareTo(S("abc\0"))), P(() => string.Equals(a, c, (StringComparison)99)), Q(() => S("a").Equals(S("a"), StringComparison.Ordinal)), Q(() => string.Equals("I", "ı", StringComparison.OrdinalIgnoreCase)), Q(() => string.Equals("İ", "i", StringComparison.OrdinalIgnoreCase)));
        }

        static string Interning()
        {
            string lit = "ab", cat = "a" + "b", dyn = S("a") + S("b"), dyn2 = string.Concat(S("a"), S("b")), built = new string(new[] { 'a', 'b' }), cs = new System.Text.StringBuilder().Append('a').Append('b').ToString();
            return Par.J(Q(() => ReferenceEquals(lit, cat)), Q(() => ReferenceEquals(lit, dyn)), Q(() => ReferenceEquals(lit, dyn2)), Q(() => ReferenceEquals(lit, built)), Q(() => ReferenceEquals(lit, cs)), Q(() => ReferenceEquals(string.Intern(dyn), lit)), Q(() => ReferenceEquals(string.Intern(built), lit)), Q(() => ReferenceEquals(string.Intern(dyn), string.Intern(built))), Q(() => string.IsInterned(dyn) != null), Q(() => string.IsInterned(S("xyzzy_notinterned_" + I(1))) == null), Q(() => string.IsInterned("ab") != null), Q(() => ReferenceEquals(string.Empty, "")), Q(() => ReferenceEquals(string.Empty, S(""))), Q(() => ReferenceEquals("", new string('x', 0))), Q(() => ReferenceEquals("", new string(new char[0]))), Q(() => ReferenceEquals("", S("ab").Substring(2))), Q(() => ReferenceEquals("", S("ab").Remove(0))), Q(() => ReferenceEquals("", string.Concat(S(""), S("")))), Q(() => ReferenceEquals("", S("") + S(""))), Q(() => ReferenceEquals("", new System.Text.StringBuilder().ToString())), Q(() => ReferenceEquals("", string.Join(",", new string[0]))), Q(() => ReferenceEquals("", S("   ").Trim())), Q(() => ReferenceEquals(lit, lit.ToString())), Q(() => ReferenceEquals(lit, lit.Trim())), Q(() => ReferenceEquals(lit, lit.Substring(0))), Q(() => ReferenceEquals(lit, lit.Replace('z', 'y'))), Q(() => ReferenceEquals(lit, lit.Replace("z", "y"))), Q(() => ReferenceEquals(lit, lit.ToLower()) ? "same" : "copy"), Q(() => ReferenceEquals(lit, lit.Insert(0, ""))), Q(() => ReferenceEquals(lit, lit.PadLeft(2))), Q(() => ReferenceEquals(lit, string.Concat(lit, ""))), Q(() => ReferenceEquals(lit, string.Concat("", lit))), Q(() => ReferenceEquals(lit, lit + "")), Q(() => ReferenceEquals(lit, lit + null)), Q(() => ReferenceEquals(lit, string.Format("{0}", lit))), Q(() => ReferenceEquals(lit, $"{lit}")), Q(() => ReferenceEquals(lit, (string)lit.Clone())), Q(() => ReferenceEquals(lit, string.Copy(lit))), Q(() => ReferenceEquals(lit, new string(lit.AsSpan()))), Q(() => lit == dyn), Q(() => (object)lit == (object)dyn), P(() => string.Intern(null)), Q(() => ReferenceEquals(typeof(string).Name, "String")), Q(() => ReferenceEquals(I(5).ToString(), I(5).ToString())), Q(() => ReferenceEquals(true.ToString(), true.ToString())), Q(() => ReferenceEquals(bool.TrueString, true.ToString())), Q(() => ReferenceEquals('a'.ToString(), 'a'.ToString())), Q(() => ReferenceEquals(I(0).ToString(), "0")), Q(() => ReferenceEquals(I(7).ToString(), "7")), Q(() => ReferenceEquals(I(300).ToString(), "300")), Q(() => ReferenceEquals(string.Empty.ToString(), string.Empty)), Q(() => ReferenceEquals(S(null) ?? "", "")));
        }

        static string EmbeddedNul()
        {
            string a = S("a\0b"), b = S("a\0c"), c = S("a"), d = S("a\0");
            return Par.J(Q(() => a.Length), Q(() => a == b), Q(() => a.Equals(b)), Q(() => a.CompareTo(b)), Q(() => string.CompareOrdinal(a, b)), Q(() => a.IndexOf('\0')), Q(() => a.IndexOf("\0")), Q(() => a.IndexOf("\0b")), Q(() => a.Contains('\0')), Q(() => a.Contains("\0")), Q(() => a.StartsWith("a\0")), Q(() => a.EndsWith("\0b")), Q(() => a.Split('\0').Length), Q(() => a.Split('\0')[1]), Q(() => a.Replace("\0", "-")), Q(() => a.Trim('\0')), Q(() => d.TrimEnd('\0')), Q(() => d == c), Q(() => d.CompareTo(c)), Q(() => c.CompareTo(d)), Q(() => string.Compare(d, c, StringComparison.Ordinal)), Q(() => string.Compare(d, c)), Q(() => d.Length), Q(() => d.GetHashCode() == c.GetHashCode()), Q(() => a.ToUpper()), Q(() => a.ToCharArray().Length), Q(() => (int)a[1]), Q(() => a.Substring(1, 1) == "\0"), Q(() => new string('\0', 3).Length), Q(() => new string('\0', 3) == "\0\0\0"), Q(() => new string('\0', 3).Trim().Length), Q(() => string.IsNullOrWhiteSpace("\0")), Q(() => char.IsWhiteSpace('\0')), Q(() => char.IsControl('\0')), Q(() => a.PadRight(5).Length), Q(() => string.Join("\0", "x", "y").Length), Q(() => string.Concat(a, a).Length), Q(() => a + "\0" == "a\0b\0"), Q(() => (a + "\0").TrimEnd('\0')), Q(() => a.LastIndexOf('\0')), Q(() => a.IndexOfAny(new[] { '\0' })), Q(() => a.AsSpan().IndexOf('\0')), Q(() => S("\0").Length), Q(() => S("\0") == "\0"), Q(() => S("\0").CompareTo("")), Q(() => S("").CompareTo("\0")));
        }

        static string JoinConcat()
        {
            return Par.J(Q(() => string.Join(",", new[] { "a", null, "", "b" })), Q(() => string.Join(",", new string[0])), Q(() => string.Join(",", new[] { "a" })), Q(() => string.Join(",", (string[])new[] { (string)null })), P(() => string.Join(",", (string[])null)), Q(() => string.Join(null, new[] { "a", "b" })), Q(() => string.Join("", new[] { "a", "b" })), Q(() => string.Join(",", new object[] { 1, null, "x", 2.5, 'c' })), Q(() => string.Join(",", new object[] { null, 1 })), Q(() => string.Join(",", new object[] { null })), P(() => string.Join(",", (object[])null)), Q(() => string.Join(",", new int[] { 1, 2, 3 })), Q(() => string.Join(",", new System.Collections.Generic.List<int> { 1, 2 })), Q(() => string.Join(",", new System.Collections.Generic.List<string> { "a", null, "b" })), Q(() => string.Join(",", new[] { 1.5, 2.5 })), Q(() => string.Join("", new[] { 'a', 'b' })), Q(() => string.Join(',', new[] { "a", "b" })), Q(() => string.Join(',', 1, 2, 3)), Q(() => string.Join(",", new[] { "a", "b", "c", "d" }, 1, 2)), Q(() => string.Join(",", new[] { "a", "b" }, 2, 0)), P(() => string.Join(",", new[] { "a", "b" }, 1, 2)), P(() => string.Join(",", new[] { "a", "b" }, -1, 1)), Q(() => string.Join(", ", new[] { "x", "y" })), Q(() => string.Join("😀", new[] { "a", "b" })), Q(() => string.Join(",", (System.Collections.Generic.IEnumerable<string>)new[] { "p", "q" })), Q(() => string.Join(",", new object[] { Demo91Helper.Nested })), Q(() => string.Join(",", new object[] { new object() })), Q(() => string.Join(",", new object[] { 0.1, 1e21, -0.0, double.NaN })), Q(() => string.Join(",", "ab")), Q(() => string.Join(",", (object)"ab")), Q(() => string.Join(",", new[] { "ab" })), Q(() => string.Join<char>(",", "ab")));
        }

        static string IndexerBounds()
        {
            string s = S("abc");
            return Par.J(Q(() => s[0]), Q(() => s[2]), P(() => s[3]), P(() => s[-1]), P(() => s[int.MaxValue]), P(() => s[int.MinValue]), P(() => S("")[0]), Q(() => s[I(1)]), Q(() => (int)s[I(0)]), Q(() => s.Length), Q(() => S("").Length), Q(() => s.ToCharArray()[1]), P(() => s.ToCharArray()[3]), Q(() => s.ToCharArray(1, 2).Length), P(() => s.ToCharArray(2, 2)), Q(() => s.ToCharArray(3, 0).Length), P(() => s.ToCharArray(4, 0)), Q(() => s.ToCharArray(0, 0).Length), Q(() => s.GetEnumerator().MoveNext()), Q(() => s.ElementAt2(1)), P(() => s.ElementAt2(5)), Q(() => s.First2()), Q(() => S("").FirstOrDefault2()), Q(() => s.Last2()), Q(() => s.Skip2(1)), Q(() => s.Take2(2)), Q(() => s.Count2()), Q(() => s.Reverse()), Q(() => s.Chars(0)), Q(() => s.AsSpan()[0]), P(() => s.AsSpan()[3]), Q(() => s.AsSpan(1)[1]), Q(() => s.AsMemory(1).Span[0]), Q(() => s.AsMemory(1, 1).Length), P(() => s.AsMemory(4).Length), Q(() => s[^1]), Q(() => s[^3]), P(() => s[^4]), Q(() => s[1..]), Q(() => s[..1]), Q(() => s[1..1].Length), Q(() => s[3..3].Length), P(() => s[4..4]), P(() => s[2..1]), Q(() => s[..]), Q(() => s[Index.Start]), Q(() => s[Index.FromEnd(1)]), Q(() => s.AsSpan()[^1]), Q(() => s.AsSpan()[..2].ToString()), Q(() => s.AsSpan()[1..].ToString()));
        }

        static string StartsEnds()
        {
            string s = S("Hello");
            return Par.J(Q(() => s.StartsWith("He")), Q(() => s.StartsWith("he")), Q(() => s.StartsWith("he", StringComparison.OrdinalIgnoreCase)), Q(() => s.StartsWith("")), Q(() => s.StartsWith("Hello")), Q(() => s.StartsWith("Hello!")), Q(() => s.StartsWith('H')), Q(() => s.StartsWith('h')), Q(() => S("").StartsWith("")), Q(() => S("").StartsWith('a')), Q(() => S("").EndsWith("")), P(() => s.StartsWith((string)null)), Q(() => s.EndsWith("lo")), Q(() => s.EndsWith("LO")), Q(() => s.EndsWith("LO", StringComparison.OrdinalIgnoreCase)), Q(() => s.EndsWith("")), Q(() => s.EndsWith('o')), Q(() => s.EndsWith("Hello")), Q(() => s.EndsWith("xHello")), Q(() => s.StartsWith("He", true, null)), Q(() => s.StartsWith("he", true, null)), Q(() => s.StartsWith("he", false, null)), Q(() => s.EndsWith("LO", true, System.Globalization.CultureInfo.InvariantCulture)), Q(() => s.StartsWith("He", StringComparison.CurrentCulture)), Q(() => s.StartsWith("He\u200B")), Q(() => s.StartsWith("He\u200B", StringComparison.Ordinal)), Q(() => s.StartsWith("He\u0000")), Q(() => s.EndsWith("o\u0000")), Q(() => s.StartsWith("\u00C5".Normalize(System.Text.NormalizationForm.FormD).Substring(0, 1))), Q(() => S("\u00C5").StartsWith("A")), Q(() => S("\u00C5").StartsWith("A", StringComparison.Ordinal)), Q(() => S("A\u030A").StartsWith("A")), Q(() => S("A\u030A").StartsWith("A", StringComparison.Ordinal)), Q(() => S("ß").StartsWith("ss", StringComparison.OrdinalIgnoreCase)), Q(() => S("😀x").StartsWith("\uD83D")), Q(() => S("😀x").StartsWith("\uD83D", StringComparison.Ordinal)), Q(() => S("x😀").EndsWith("\uDE00")), Q(() => S("x😀").EndsWith("\uDE00", StringComparison.Ordinal)), Q(() => s.AsSpan().StartsWith("He")), Q(() => s.AsSpan().EndsWith("LO", StringComparison.OrdinalIgnoreCase)));
        }

        static string InsertRemove()
        {
            string s = S("abc");
            return Par.J(Q(() => s.Insert(0, "x")), Q(() => s.Insert(3, "x")), Q(() => s.Insert(1, "")), P(() => s.Insert(4, "x")), P(() => s.Insert(-1, "x")), P(() => s.Insert(0, null)), Q(() => s.Insert(0, "😀")), Q(() => S("").Insert(0, "x")), Q(() => s.Remove(0)), Q(() => s.Remove(3)), Q(() => s.Remove(1)), Q(() => s.Remove(1, 1)), Q(() => s.Remove(0, 3)), Q(() => s.Remove(0, 0)), Q(() => s.Remove(3, 0)), P(() => s.Remove(4)), P(() => s.Remove(-1)), P(() => s.Remove(1, 3)), P(() => s.Remove(0, -1)), P(() => s.Remove(4, 0)), Q(() => ReferenceEquals(s.Remove(3), s)), Q(() => ReferenceEquals(s.Remove(0, 0), s)), Q(() => ReferenceEquals(s.Insert(1, ""), s)), Q(() => ReferenceEquals(s.Remove(3, 0), s)), Q(() => ReferenceEquals(s.Remove(0), "")), Q(() => S("ab").Insert(1, "ab")), Q(() => S("aaa").Remove(1, 1)), Q(() => S("hello world").Remove(5)), Q(() => S("hello world").Remove(0, 6)), Q(() => S("hello world").Insert(5, ",")));
        }

        static string CharArrayCtor()
        {
            char[] arr = { 'a', 'b', 'c' };
            return Par.J(Q(() => new string(arr)), Q(() => new string(arr, 1, 2)), Q(() => new string(arr, 0, 0).Length), Q(() => new string(arr, 3, 0).Length), P(() => new string(arr, 2, 2)), P(() => new string(arr, -1, 1)), P(() => new string(arr, 0, -1)), P(() => new string(arr, 4, 0)), Q(() => new string((char[])null)), Q(() => new string((char[])null).Length), P(() => new string((char[])null, 0, 0)), Q(() => new string('x', 3)), Q(() => new string('x', 0).Length), P(() => new string('x', -1)), Q(() => new string('\0', 2).Length), Q(() => new string(S("😀")[0], 2).Length), Q(() => new string(arr.AsSpan(1))), Q(() => new string(ReadOnlySpan<char>.Empty).Length), Q(() => ReferenceEquals(new string(ReadOnlySpan<char>.Empty), "")), Q(() => ReferenceEquals(new string('x', 0), "")), Q(() => ReferenceEquals(new string(arr, 0, 0), "")), Q(() => ReferenceEquals(new string((char[])null), "")), Q(() => string.Create(3, 'z', StrExt.Fill3)), Q(() => string.Create(0, 0, StrExt.FillNone).Length), P(() => string.Create(-1, 0, StrExt.FillNone)), Q(() => new string(new char[] { '\uD83D', '\uDE00' })), Q(() => new string(new char[] { '\uD83D', '\uDE00' }).Length), Q(() => new string(S("ab").ToCharArray()) == "ab"), Q(() => ReferenceEquals(new string(S("ab").ToCharArray()), S("ab"))), Q(() => string.Concat(arr)), Q(() => new string(arr).ToCharArray().Length), Q(() => (new string(arr) == string.Join("", arr))));
        }

        static string ContainsLastIndex()
        {
            string s = S("abcabc");
            return Par.J(Q(() => s.Contains("bc")), Q(() => s.Contains("")), Q(() => s.Contains("x")), Q(() => s.Contains('c')), Q(() => s.Contains('x')), P(() => s.Contains((string)null)), Q(() => s.Contains("BC")), Q(() => s.Contains("BC", StringComparison.OrdinalIgnoreCase)), Q(() => s.Contains('C', StringComparison.OrdinalIgnoreCase)), Q(() => S("").Contains("")), Q(() => S("").Contains("a")), Q(() => s.LastIndexOf('c')), Q(() => s.LastIndexOf('c', 4)), Q(() => s.LastIndexOf('c', 1)), Q(() => s.LastIndexOf('a')), Q(() => s.LastIndexOf('a', 0)), Q(() => s.LastIndexOf('x')), Q(() => S("").LastIndexOf('x')), Q(() => s.LastIndexOf("bc")), Q(() => s.LastIndexOf("bc", 4)), Q(() => s.LastIndexOf("bc", 3)), Q(() => s.LastIndexOf("")), Q(() => S("").LastIndexOf("")), Q(() => s.LastIndexOf("", 0)), Q(() => s.LastIndexOf("", 5)), Q(() => s.LastIndexOf("", 6)), P(() => s.LastIndexOf("", 7)), Q(() => s.LastIndexOf("abc")), Q(() => s.LastIndexOf("abcabc")), Q(() => s.LastIndexOf("abcabcd")), P(() => s.LastIndexOf('c', 6)), P(() => s.LastIndexOf('c', -1)), Q(() => s.LastIndexOf('c', 5, 1)), Q(() => s.LastIndexOf('c', 5, 6)), P(() => s.LastIndexOf('c', 5, 7)), Q(() => s.LastIndexOf('c', 5, 0)), Q(() => s.LastIndexOf("c", 5, 3)), Q(() => s.LastIndexOf("c", 5, 3, StringComparison.Ordinal)), Q(() => s.LastIndexOf("C", StringComparison.OrdinalIgnoreCase)), Q(() => s.LastIndexOfAny(new[] { 'a', 'b' })), Q(() => s.LastIndexOfAny(new[] { 'x' })), Q(() => s.LastIndexOfAny(new[] { 'a' }, 2)), Q(() => s.LastIndexOfAny(new[] { 'a' }, 2, 2)), Q(() => s.LastIndexOfAny(new[] { 'a' }, 2, 3)), Q(() => S("").LastIndexOf("", 0)), P(() => S("").LastIndexOf("", 1)), Q(() => S("a").LastIndexOf("", 1)), P(() => S("a").LastIndexOf("", 2)), Q(() => S("a").LastIndexOf('a', 0, 1)), Q(() => S("😀😀").LastIndexOf("😀")), Q(() => S("😀😀").LastIndexOf('\uDE00')));
        }

        static string HashConsistency()
        {
            // string.GetHashCode .NET'te surec-rasgele: mutlak deger yazilmaz; yalniz tutarlilik ve kurallar
            string a = S("abc"), b = new string(new[] { 'a', 'b', 'c' }), c = S("ABC"), e = S(""), n1 = S("a\0"), n2 = S("a");
            return Par.J(Q(() => a.GetHashCode() == b.GetHashCode()), Q(() => a.GetHashCode() == c.GetHashCode()), Q(() => e.GetHashCode() == string.Empty.GetHashCode()), Q(() => n1.GetHashCode() == n2.GetHashCode()), Q(() => a.GetHashCode() == a.GetHashCode()), Q(() => a.GetHashCode(StringComparison.OrdinalIgnoreCase) == c.GetHashCode(StringComparison.OrdinalIgnoreCase)), Q(() => a.GetHashCode(StringComparison.Ordinal) == a.GetHashCode()), Q(() => string.GetHashCode(a.AsSpan()) == a.GetHashCode()), Q(() => string.GetHashCode(a.AsSpan(), StringComparison.OrdinalIgnoreCase) == c.GetHashCode(StringComparison.OrdinalIgnoreCase)), Q(() => StringComparer.Ordinal.GetHashCode(a) == a.GetHashCode()), Q(() => StringComparer.OrdinalIgnoreCase.GetHashCode(a) == StringComparer.OrdinalIgnoreCase.GetHashCode(c)), Q(() => StringComparer.InvariantCultureIgnoreCase.GetHashCode(a) == StringComparer.InvariantCultureIgnoreCase.GetHashCode(c)), Q(() => StringComparer.InvariantCulture.GetHashCode(a) == StringComparer.InvariantCulture.GetHashCode(b)), Q(() => S("ß").GetHashCode(StringComparison.OrdinalIgnoreCase) == S("SS").GetHashCode(StringComparison.OrdinalIgnoreCase)), Q(() => S("ı").GetHashCode(StringComparison.OrdinalIgnoreCase) == S("I").GetHashCode(StringComparison.OrdinalIgnoreCase)), Q(() => S("i").GetHashCode(StringComparison.OrdinalIgnoreCase) == S("I").GetHashCode(StringComparison.OrdinalIgnoreCase)), P(() => StringComparer.Ordinal.GetHashCode((string)null)), P(() => ((System.Collections.IEqualityComparer)StringComparer.Ordinal).GetHashCode(null)), Q(() => System.Collections.Generic.EqualityComparer<string>.Default.GetHashCode(a) == a.GetHashCode()), Q(() => System.Collections.Generic.EqualityComparer<string>.Default.GetHashCode(null)), Q(() => System.Collections.Generic.EqualityComparer<string>.Default.Equals(a, b)), Q(() => System.Collections.Generic.EqualityComparer<string>.Default.Equals(null, null)), Q(() => System.Collections.Generic.EqualityComparer<string>.Default.Equals(a, null)), Q(() => ((object)a).GetHashCode() == a.GetHashCode()), Q(() => a.GetHashCode() != 0 || true), Q(() => S("Aa").GetHashCode() == S("BB").GetHashCode()));
        }

        static string StringMisc()
        {
            string s = S("Hello, World");
            return Par.J(Q(() => s.ToCharArray().Length), Q(() => s.Length), Q(() => s.Clone() == (object)s), Q(() => ReferenceEquals(s.Clone(), s)), Q(() => s.GetType().Name), Q(() => s.GetTypeCode()), Q(() => ((IConvertible)s).ToInt32(null) == 0 ? "" : ""), P(() => ((IConvertible)S("12")).ToInt32(null)), P(() => ((IConvertible)s).ToInt32(null)), Q(() => ((IConvertible)S("true")).ToBoolean(null)), Q(() => ((IConvertible)S("1.5")).ToDouble(System.Globalization.CultureInfo.InvariantCulture)), Q(() => ((IConvertible)S("x")).ToChar(null)), P(() => ((IConvertible)S("xy")).ToChar(null)), Q(() => s.IsNormalized()), Q(() => s.Normalize() == s), Q(() => s.ToString() == s), Q(() => ((object)s).ToString() == s), Q(() => s.Equals(s.ToString())), Q(() => string.Concat(s.AsSpan(0, 5), s.AsSpan(7))), Q(() => s.AsSpan().Trim().Length), Q(() => S(" x ").AsSpan().Trim().ToString()), Q(() => s.AsSpan().IndexOf("World")), Q(() => s.AsSpan().Slice(7).ToString()), Q(() => s.AsSpan().Contains("lo,", StringComparison.Ordinal)), Q(() => s.AsSpan().Contains('W')), Q(() => s.AsSpan().ToArray().Length), Q(() => s.AsSpan().ToString() == s), Q(() => ReferenceEquals(s.AsSpan().ToString(), s)), Q(() => string.Join("", s.AsSpan().ToArray())), Q(() => s.AsMemory().Length), Q(() => s.AsMemory(7).ToString()), Q(() => s.AsMemory().Slice(0, 5).ToString()), Q(() => s.ToUpper().ToLower() == s.ToLower()), Q(() => s.Replace(" ", "").Length), Q(() => s.Split(", ")[1]), Q(() => s.ToLowerInvariant().GetHashCode() == s.ToLower().GetHashCode()), Q(() => S("a").CompareTo("a\u0000")), Q(() => S("abc").CompareTo("abd")), Q(() => S("b").CompareTo("a")), Q(() => string.Compare("x", "x")), Q(() => string.Compare("abc", 0, "abd", 0, 2)), Q(() => string.Compare("abc", 0, "abd", 0, 3)), Q(() => string.Compare("abc", 1, "bcd", 0, 2)), Q(() => string.Compare("abc", 0, "ABC", 0, 3, true)), Q(() => string.Compare("abc", 0, "ABC", 0, 3, false)), Q(() => string.Compare("abc", 0, "abd", 0, 10)), P(() => string.Compare("abc", 0, "abd", 0, -1)), P(() => string.Compare("abc", 4, "abd", 0, 1)), Q(() => string.Compare("", 0, "", 0, 1)));
        }

        static string ToCharArrayCopy()
        {
            string s = S("abcde");
            char[] dst = new char[10];
            s.CopyTo(1, dst, 2, 3);
            string d1 = new string(dst, 0, 6).Replace('\0', '.');
            s.AsSpan().CopyTo(dst);
            string d2 = new string(dst, 0, 6).Replace('\0', '.');
            bool ok = s.AsSpan().TryCopyTo(new Span<char>(dst, 0, 3));
            char[] arr = s.ToCharArray(); arr[0] = 'z';
            Span<char> sp = stackalloc char[5]; s.CopyTo(sp); sp[0] = 'y';
            return Par.J(Q(() => d1), Q(() => d2), Q(() => ok), Q(() => s), Q(() => new string(arr)), new string(sp), V(() => s.CopyTo(0, dst, 0, 6)), V(() => s.CopyTo(0, dst, 8, 3)), V(() => s.CopyTo(5, dst, 0, 1)), V(() => s.CopyTo(0, (char[])null, 0, 1)), V(() => s.CopyTo(-1, dst, 0, 1)), V(() => s.CopyTo(0, dst, -1, 1)), V(() => s.CopyTo(0, dst, 0, -1)), P(() => { s.CopyTo(stackalloc char[3]); return "ok"; }), Q(() => s.ToCharArray(0, 5).Length), Q(() => s.ToCharArray(5, 0).Length), Q(() => s.ToCharArray(2, 2)[0]), Q(() => s.ToCharArray() == s.ToCharArray()), Q(() => ReferenceEquals(S("").ToCharArray(), S("").ToCharArray())), Q(() => S("").ToCharArray().Length), Q(() => Array.Empty<char>().Length), Q(() => ReferenceEquals(S("").ToCharArray(), Array.Empty<char>())), Q(() => s.EnumerateRunes().Count2()), Q(() => string.Join(",", s.ToCharArray())), Q(() => string.Join(",", s.AsSpan().ToArray())), Q(() => ((System.Collections.Generic.IEnumerable<char>)s).Count2()), Q(() => ((System.Collections.IEnumerable)s).GetEnumerator().MoveNext()), Q(() => s.GetEnumerator().Current == '\0' ? "init0" : "initX"));
        }

        static string CompareToSort()
        {
            var arr = new[] { S("b"), S("B"), S("a"), S("A"), S("ä"), S("Z"), S("z"), S("10"), S("9"), S("_"), S(""), S(" "), S("😀"), S("é"), S("e"), S("Ab"), S("aB"), S("ab"), S("AB") };
            var ord = (string[])arr.Clone(); Array.Sort(ord, StringComparer.Ordinal);
            var oic = (string[])arr.Clone(); Array.Sort(oic, StringComparer.OrdinalIgnoreCase);
            var def = (string[])arr.Clone(); Array.Sort(def);
            var cmp = (string[])arr.Clone(); Array.Sort(cmp, string.CompareOrdinal);
            var inv = (string[])arr.Clone(); Array.Sort(inv, StringComparer.InvariantCulture);
            var invic = (string[])arr.Clone(); Array.Sort(invic, StringComparer.InvariantCultureIgnoreCase);
            var cur = (string[])arr.Clone(); Array.Sort(cur, StringComparer.CurrentCulture);
            var lst = new System.Collections.Generic.List<string>(arr); lst.Sort();
            var lst2 = new System.Collections.Generic.List<string>(arr); lst2.Sort((x, y) => string.Compare(x, y, StringComparison.OrdinalIgnoreCase));
            return Par.J(Q(() => string.Join("|", ord)), Q(() => string.Join("|", oic)), Q(() => string.Join("|", def)), Q(() => string.Join("|", cmp)), Q(() => string.Join("|", inv)), Q(() => string.Join("|", invic)), Q(() => string.Join("|", cur)), Q(() => string.Join("|", lst)), Q(() => string.Join("|", lst2)));
        }

        static string NormalizeAndUnicode()
        {
            string c = S("\u00C5"), d = S("A\u030A"), h = S("한"), hd = S("\u1112\u1161\u11AB"), ff = S("ﬁ"), sup = S("²"), s = S("ﬀ");
            return Par.J(Q(() => c.Length), Q(() => d.Length), Q(() => c == d), Q(() => c.Normalize() == d.Normalize()), Q(() => c.Normalize(System.Text.NormalizationForm.FormD) == d), Q(() => d.Normalize(System.Text.NormalizationForm.FormC) == c), Q(() => c.Normalize(System.Text.NormalizationForm.FormD).Length), Q(() => c.IsNormalized()), Q(() => d.IsNormalized()), Q(() => d.IsNormalized(System.Text.NormalizationForm.FormD)), Q(() => h.Normalize(System.Text.NormalizationForm.FormD) == hd), Q(() => hd.Normalize() == h), Q(() => hd.Length), Q(() => ff.Normalize(System.Text.NormalizationForm.FormKC)), Q(() => ff.Normalize(System.Text.NormalizationForm.FormKD)), Q(() => ff.Normalize()), Q(() => sup.Normalize(System.Text.NormalizationForm.FormKC)), Q(() => sup.Normalize(System.Text.NormalizationForm.FormKD)), Q(() => S("①").Normalize(System.Text.NormalizationForm.FormKC)), Q(() => S("Ａ").Normalize(System.Text.NormalizationForm.FormKC)), Q(() => S("ｱ").Normalize(System.Text.NormalizationForm.FormKC)), Q(() => S("\u00A0").Normalize(System.Text.NormalizationForm.FormKC) == " "), Q(() => S("x").Normalize() == "x"), Q(() => ReferenceEquals(S("x").Normalize(), S("x")) ? "same" : "copy"), Q(() => S("").Normalize().Length), Q(() => char.GetUnicodeCategory('A')), Q(() => char.GetUnicodeCategory('a')), Q(() => char.GetUnicodeCategory('1')), Q(() => char.GetUnicodeCategory(' ')), Q(() => char.GetUnicodeCategory('\u00A0')), Q(() => char.GetUnicodeCategory('_')), Q(() => char.GetUnicodeCategory('-')), Q(() => char.GetUnicodeCategory('$')), Q(() => char.GetUnicodeCategory('ǅ')), Q(() => char.GetUnicodeCategory('\u0301')), Q(() => char.GetUnicodeCategory('\u200B')), Q(() => char.GetUnicodeCategory('\uFEFF')), Q(() => char.GetUnicodeCategory('\0')), Q(() => char.GetUnicodeCategory('\uE000')), Q(() => char.GetUnicodeCategory('\uFFFF')), Q(() => char.GetUnicodeCategory('²')), Q(() => char.GetUnicodeCategory('½')), Q(() => char.GetUnicodeCategory('٣')), Q(() => char.GetUnicodeCategory('Ⅳ')), Q(() => char.GetUnicodeCategory('€')), Q(() => char.GetUnicodeCategory('©')), Q(() => char.GetUnicodeCategory('«')), Q(() => char.GetUnicodeCategory('»')), Q(() => char.GetUnicodeCategory('(')), Q(() => char.GetUnicodeCategory('+')), Q(() => char.GetUnicodeCategory('^')), Q(() => char.GetUnicodeCategory('\n')), Q(() => char.GetUnicodeCategory('\u2028')), Q(() => char.GetUnicodeCategory('\u2029')), Q(() => char.GetUnicodeCategory('\u00AD')));
        }
    }

    static class Demo91Helper
    {
        public static object Nested = new NestedObj();
        class NestedObj { public override string ToString() { return "nested"; } }
    }

    static class StrExt
    {
        public static string Reverse(this string s) { var a = s.ToCharArray(); Array.Reverse(a); return new string(a); }
        public static char[] Reverse2(this char[] a) { var b = (char[])a.Clone(); Array.Reverse(b); return b; }
        public static int Count2<T>(this System.Collections.Generic.IEnumerable<T> e) { int n = 0; foreach (var _ in e) n++; return n; }
        public static int Count2(this System.Text.StringRuneEnumerator e) { int n = 0; foreach (var _ in e) n++; return n; }
        public static char ElementAt2(this string s, int i) { int n = 0; foreach (var c in s) { if (n++ == i) return c; } throw new ArgumentOutOfRangeException("i"); }
        public static char First2(this string s) { foreach (var c in s) return c; throw new InvalidOperationException(); }
        public static char FirstOrDefault2(this string s) { foreach (var c in s) return c; return '\0'; }
        public static char Last2(this string s) { char r = '\0'; bool any = false; foreach (var c in s) { r = c; any = true; } if (!any) throw new InvalidOperationException(); return r; }
        public static string Skip2(this string s, int n) { var sb = new System.Text.StringBuilder(); int i = 0; foreach (var c in s) { if (i++ >= n) sb.Append(c); } return sb.ToString(); }
        public static string Take2(this string s, int n) { var sb = new System.Text.StringBuilder(); int i = 0; foreach (var c in s) { if (i++ < n) sb.Append(c); } return sb.ToString(); }
        public static char Chars(this string s, int i) { return s[i]; }
        public static void Fill3(Span<char> span, char c) { span[0] = c; span[1] = c; span[2] = 'q'; }
        public static void FillNone(Span<char> span, int st) { }
    }
}
