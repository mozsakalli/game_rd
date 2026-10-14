using System;
using System.Text.RegularExpressions;
namespace Demo85 // corelib Regex Faz 1: Common NickValidator + core.upm LocalizationFormatter kaliplari, greedy/lazy, {m,n}, IgnoreCase, Replace, NextMatch
{
    class App85
    {
        static string Eval(Match m) { return "<" + m.Groups[1].Value + (m.Groups[2].Success ? ":" + m.Groups[2].Value : "") + ">"; }
        public static int Run()
        {
            int acc = 0;
            var nick = new Regex(@"^[\p{L}\d _-]+$", RegexOptions.Compiled);
            if (nick.IsMatch("Ali_Veli 42")) acc += 1;
            if (!nick.IsMatch("Ali@Veli")) acc += 2;
            if (!nick.IsMatch("")) acc += 4;
            if (nick.IsMatch("çelik-üçgen")) acc += 8;

            var ph = new Regex(@"\{(\d+)(?::([^}]+))?\}");
            string r = ph.Replace("a{0}b{12:N2}c{x}", new MatchEvaluator(Eval));
            if (r == "a<0>b<12:N2>c{x}") acc += 16;
            var m = ph.Match("--{7:cap}--{3}");
            if (m.Success && m.Index == 2 && m.Length == 7 && m.Groups.Count == 3 && m.Groups[2].Value == "cap") acc += 32;
            m = m.NextMatch();
            if (m.Success && m.Value == "{3}" && !m.Groups[2].Success && m.Groups[2].Value == "") acc += 64;
            if (!m.NextMatch().Success) acc += 128;

            var loc = Regex.Match("x {locale:tr-TR} y", @"\{locale:([^\}]+)\}");
            if (loc.Success && loc.Groups[1].Value == "tr-TR") acc += 256;
            if (Regex.Match("Texture_Pixel(Main)", @"Texture_Pixel\(([^\)]+)\)").Groups[1].Value == "Main") acc += 512;

            if (Regex.Match("<a><b>", "<.+>").Value == "<a><b>") acc += 1024;      // greedy
            if (Regex.Match("<a><b>", "<.+?>").Value == "<a>") acc += 2048;        // lazy
            if (Regex.Match("aaaa", "a{2,3}").Value == "aaa" && !Regex.IsMatch("a", "a{2}") && Regex.Match("x{2", "x{2").Success) acc += 4096;
            if (Regex.IsMatch("HeLLo", "hello", RegexOptions.IgnoreCase) && !Regex.IsMatch("HeLLo", "hello")) acc += 8192;
            if (Regex.Replace("2024-01-05", @"(\d+)-(\d+)-(\d+)", "$3/$2/$1") == "05/01/2024") acc += 16384;
            if (Regex.Match("cat dog", @"dog|cat").Value == "cat" && Regex.Match("a1 b2", @"\w\d\s\w\d").Success) acc += 32768;
            if (Regex.Match("ab\n", "b$").Success && !Regex.Match("a.b", @"a\.c").Success && Regex.Match("a.b", "a.b").Success) acc += 65536;
            if (Regex.Match("xxay", "x*a").Value == "xxa" && Regex.Match("ay", "x*a").Index == 0) acc += 131072;
            if (Regex.Escape("a.b*c") == @"a\.b\*c") acc += 262144;
            if (Regex.Replace("a1b22c", @"\d+", "#") == "a#b#c") acc += 524288;
            if (Regex.Match("ab", "a(x)?b").Groups[1].Success == false && Regex.Match("axb", "a(x)?b").Groups[1].Index == 1) acc += 1048576;
            return acc;
        }
    }
}
