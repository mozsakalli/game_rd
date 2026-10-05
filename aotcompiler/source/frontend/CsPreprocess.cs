using System.Collections.Generic;
using System.Text;

namespace DigitoyEngine.Frontend
{
    // C# on-islemcisi: #if/#elif/#else/#endif + #define/#undef (old_dotnet_compiler/Preprocess portu).
    // Metin -> metin: inaktif bolgeler ve direktif satirlari BOSLUKLA doldurulur (newline korunur)
    // -> satir numaralari degismez, CsError konumlari orijinal kaynakla birebir dogru kalir.
    // C# kurali: #define/#undef dosyanin ilk token'indan ONCE olmali (yorum/bos satir kod sayilmaz).
    // "GAMOS" sembolu her zaman tanimli (kaynak, bu derleyiciyi #if ile ayirt edebilsin).
    public static class CsPreprocess
    {
        public static List<string> Defines = new List<string>(); // dis tanimlar (arac/CLI)

        class Frame
        {
            public bool Taken;    // zincirde bir dal alindi mi (#elif/#else karari)
            public bool Active;   // su anki dal aktif mi
            public bool SeenElse;
            public Frame(bool taken, bool active) { Taken = taken; Active = active; }
        }

        public static string Run(string src, string file)
        {
            var defs = new List<string> { "GAMOS" };
            defs.AddRange(Defines);
            var st = new List<Frame>();
            var o = new StringBuilder(src.Length);
            bool codeSeen = false;
            int n = src.Length, pos = 0, lineNo = 1;
            while (pos < n)
            {
                int ls = pos, le = pos;
                while (le < n && src[le] != '\n') le++;
                if (le < n) le++; // '\n' dahil
                pos = le;
                int k = ls;
                while (k < le && (src[k] == ' ' || src[k] == '\t' || src[k] == '\r')) k++;
                bool act = true;
                foreach (var fr in st)
                    if (!fr.Active) act = false;
                if (k < le && src[k] == '#') // direktif = satirin ilk bosluk-olmayan karakteri (C# kurali)
                {
                    var line = src.Substring(k + 1, le - k - 1);
                    var word = FirstWord(line);
                    var rest = AfterWord(line);
                    switch (word)
                    {
                        case "if":
                            st.Add(act && EvalExpr(rest, defs, file, lineNo << 10) ? new Frame(true, true) : new Frame(false, false));
                            break;
                        case "elif":
                            {
                                if (st.Count == 0) throw new CsError(file, lineNo << 10, "#elif eslesmeyen");
                                var fr = st[st.Count - 1];
                                if (fr.SeenElse) throw new CsError(file, lineNo << 10, "#elif #else'ten sonra");
                                bool outer = true;
                                for (int fi = 0; fi < st.Count - 1; fi++)
                                    if (!st[fi].Active) outer = false;
                                if (fr.Taken) fr.Active = false;
                                else
                                {
                                    fr.Active = outer && EvalExpr(rest, defs, file, lineNo << 10);
                                    if (fr.Active) fr.Taken = true;
                                }
                                break;
                            }
                        case "else":
                            {
                                if (st.Count == 0) throw new CsError(file, lineNo << 10, "#else eslesmeyen");
                                var fr = st[st.Count - 1];
                                if (fr.SeenElse) throw new CsError(file, lineNo << 10, "cift #else");
                                fr.SeenElse = true;
                                bool outer = true;
                                for (int fi = 0; fi < st.Count - 1; fi++)
                                    if (!st[fi].Active) outer = false;
                                fr.Active = outer && !fr.Taken;
                                if (fr.Active) fr.Taken = true;
                                break;
                            }
                        case "endif":
                            if (st.Count == 0) throw new CsError(file, lineNo << 10, "#endif eslesmeyen");
                            st.RemoveAt(st.Count - 1);
                            break;
                        case "define":
                            if (codeSeen) throw new CsError(file, lineNo << 10, "#define ilk token'dan once olmali (C#)");
                            if (act)
                            {
                                var sym = FirstWord(rest);
                                if (!defs.Contains(sym)) defs.Add(sym);
                            }
                            break;
                        case "undef":
                            if (codeSeen) throw new CsError(file, lineNo << 10, "#undef ilk token'dan once olmali (C#)");
                            if (act) defs.Remove(FirstWord(rest));
                            break;
                        case "pragma": // #pragma warning vb: derleyiciye anlamsiz, yutulur (C# uyumu)
                        case "region":
                        case "endregion":
                        case "nullable":
                            break;
                        default:
                            throw new CsError(file, lineNo << 10, $"bilinmeyen direktif: #{word}");
                    }
                    Blank(o, src, ls, le);
                }
                else if (act)
                {
                    o.Append(src, ls, le - ls);
                    if (!codeSeen && HasCode(src, k, le)) codeSeen = true;
                }
                else Blank(o, src, ls, le);
                lineNo++;
            }
            if (st.Count > 0) throw new CsError(file, lineNo << 10, "#if kapanmadi (#endif eksik)");
            return o.ToString();
        }

        // newline'lari koruyarak bosluk doldur (satir numarasi sabitleme numarasi)
        static void Blank(StringBuilder o, string src, int ls, int le)
        {
            for (int p = ls; p < le; p++)
                o.Append(src[p] == '\n' || src[p] == '\r' ? src[p] : ' ');
        }

        static bool HasCode(string src, int k, int le)
        {
            if (k >= le) return false;
            char c = src[k];
            if (c == '\n' || c == '\r') return false;
            if (c == '/' && k + 1 < le && src[k + 1] == '/') return false; // yorum kod sayilmaz
            return true;
        }

        static bool IsIdentCh(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';

        static string FirstWord(string s)
        {
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            int start = i;
            while (i < s.Length && IsIdentCh(s[i])) i++;
            return s.Substring(start, i - start);
        }

        static string AfterWord(string s)
        {
            int i = 0;
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t')) i++;
            while (i < s.Length && IsIdentCh(s[i])) i++;
            return s.Substring(i);
        }

        // mini ifade dili: sembol | true | false | ! | && | || | (...)
        class ExprEval
        {
            readonly string s; readonly List<string> defs; readonly string file; readonly int line;
            int p;
            public ExprEval(string s, List<string> defs, string file, int line) { this.s = s; this.defs = defs; this.file = file; this.line = line; }

            public bool Eval()
            {
                bool v = Or();
                SkipWs();
                if (p < s.Length && s[p] != '\r' && s[p] != '\n' && s[p] != '/')
                    throw new CsError(file, line, $"#if ifadesi cozumlenemedi: {s.Trim()}");
                return v;
            }

            void SkipWs() { while (p < s.Length && (s[p] == ' ' || s[p] == '\t')) p++; }

            bool Or()
            {
                bool v = And();
                SkipWs();
                while (p + 1 < s.Length && s[p] == '|' && s[p + 1] == '|')
                {
                    p += 2;
                    v |= And(); // kisa devre GEREKMEZ: yan etkisiz sembol testi
                    SkipWs();
                }
                return v;
            }

            bool And()
            {
                bool v = Un();
                SkipWs();
                while (p + 1 < s.Length && s[p] == '&' && s[p + 1] == '&')
                {
                    p += 2;
                    v &= Un();
                    SkipWs();
                }
                return v;
            }

            bool Un()
            {
                SkipWs();
                if (p < s.Length && s[p] == '!') { p++; return !Un(); }
                if (p < s.Length && s[p] == '(')
                {
                    p++;
                    bool v = Or();
                    SkipWs();
                    if (p >= s.Length || s[p] != ')') throw new CsError(file, line, "#if ')' bekleniyordu");
                    p++;
                    return v;
                }
                int start = p;
                while (p < s.Length && IsIdentCh(s[p])) p++;
                if (p == start) throw new CsError(file, line, "#if sembol bekleniyordu");
                var w = s.Substring(start, p - start);
                if (w == "true") return true;
                if (w == "false") return false;
                return defs.Contains(w);
            }
        }

        static bool EvalExpr(string s, List<string> defs, string file, int line) =>
            new ExprEval(s, defs, file, line).Eval();
    }
}
