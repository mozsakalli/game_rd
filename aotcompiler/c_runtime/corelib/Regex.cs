using System.Collections.Generic;
using System.Text;

// Minimal .NET-uyumlu regex (Faz 1, docs/engine-notes.md REGEX): runtime backtracking, UTF-16 uzerinde dogrudan, tek eslesme
// cekirdegi (Regex.Scan -> MatchSeq). Desteklenen alt kume ctor'da dogrulanir; disi ArgumentException (cihazda degil, cctor'da).
// Alt kume: literal, kacislar (\{ \} \( \) \. \\ \n \t \r \uXXXX), '.', siniflar [..] [^..] a-z, \d\D\w\W\s\S \p{L} \p{N},
// gruplar ( ) (?: ), '|', * + ? {m} {m,} {m,n} (+ tembel '?'), ^ $. Options: None/Compiled(yok sayilir)/IgnoreCase/Singleline.
// Faz 2: lookaround, isimli grup, backreference, \b, Multiline, Split, Matches. Char siniflari corelib Char'in (Latin) sinirinda.
namespace System.Text.RegularExpressions
{
    [Flags]
    public enum RegexOptions
    {
        None = 0, IgnoreCase = 1, Multiline = 2, ExplicitCapture = 4, Compiled = 8, Singleline = 16,
        IgnorePatternWhitespace = 32, RightToLeft = 64, ECMAScript = 256, CultureInvariant = 512,
    }

    public delegate string MatchEvaluator(Match match);

    // .NET hiyerarsisi korunur: gercek .NET'e karsi derlenmis IL Index/Length/Value'yu Capture uzerinden cagirir.
    public class Capture
    {
        internal string input;
        internal int index, length;
        internal bool success;
        internal Capture() { }
        public int Index { get { return index; } }
        public int Length { get { return length; } }
        public string Value { get { return success ? input.Substring(index, length) : ""; } }
        public override string ToString() { return Value; }
    }

    public class Group : Capture
    {
        internal Group() { }
        internal Group(string input, int index, int length, bool success) { this.input = input; this.index = index; this.length = length; this.success = success; }
        public bool Success { get { return success; } }
    }

    public class GroupCollection
    {
        readonly Group[] groups;
        internal GroupCollection(Group[] groups) { this.groups = groups; }
        public int Count { get { return groups.Length; } }
        public Group this[int index] { get { return index >= 0 && index < groups.Length ? groups[index] : new Group("", 0, 0, false); } }
    }

    public class Match : Group
    {
        readonly Regex owner;
        readonly GroupCollection groups;
        static readonly Match empty = new Match(null, "", null);
        internal Match(Regex owner, string input, int[] caps)
        {
            this.owner = owner; this.input = input;
            if (caps == null) { groups = new GroupCollection(new Group[0]); return; }
            int n = caps.Length / 2;
            var g = new Group[n];
            for (int i = 0; i < n; i++)
            {
                int s = caps[2 * i], e = caps[2 * i + 1];
                bool ok = s >= 0 && e >= 0;
                g[i] = i == 0 ? (Group)this : new Group(input, ok ? s : 0, ok ? e - s : 0, ok);
            }
            index = caps[0]; length = caps[1] - caps[0]; success = true;
            groups = new GroupCollection(g);
        }
        public static Match Empty { get { return empty; } }
        public GroupCollection Groups { get { return groups; } }
        public Match NextMatch()
        {
            if (!success || owner == null) return empty;
            return owner.Scan(input, length == 0 ? index + 1 : index + length);
        }
    }

    public class Regex
    {
        // ---- derlenmis program ----
        const int KChar = 1, KAny = 2, KSet = 3, KBol = 4, KEol = 5, KGroup = 6, KRepeat = 7; // kind < 0: grup kapanisi (-(g+1))
        sealed class Node
        {
            public int kind;
            public char ch;
            public CharSet set;
            public Node[][] alts;  // KGroup: alternatifler
            public int group = -1; // KGroup: yakalama numarasi (-1 = (?:...))
            public Node[] child;   // KRepeat: tekrarlanan (tek elemanli dizi)
            public int min, max;   // KRepeat (max -1 = sinirsiz)
            public bool lazy;
        }
        sealed class CharSet
        {
            public bool negate;
            public List<char> lo = new List<char>(), hi = new List<char>();
            public bool digit, notDigit, word, notWord, space, notSpace, letter, number;
            public bool Test(char c, bool ignoreCase)
            {
                bool r = Raw(c);
                if (!r && ignoreCase) r = Raw(Char.ToLowerInvariant(c)) || Raw(Char.ToUpperInvariant(c));
                return r != negate;
            }
            bool Raw(char c)
            {
                for (int i = 0; i < lo.Count; i++) if (c >= lo[i] && c <= hi[i]) return true;
                if (digit && Char.IsDigit(c)) return true;
                if (notDigit && !Char.IsDigit(c)) return true;
                if (word && IsWord(c)) return true;
                if (notWord && !IsWord(c)) return true;
                if (space && Char.IsWhiteSpace(c)) return true;
                if (notSpace && !Char.IsWhiteSpace(c)) return true;
                if (letter && Char.IsLetter(c)) return true;
                if (number && Char.IsDigit(c)) return true;
                return false;
            }
        }
        static bool IsWord(char c) { return Char.IsLetterOrDigit(c) || c == '_'; }

        // Devam cercevesi: dizi-icinde-konum ya da tekrar adimi (delegate'siz backtracking).
        sealed class Cont
        {
            public Node[] seq; public int idx; public Cont next;
            public Node rep; public int count, iterStart; // rep != null: tekrar devam cercevesi
        }

        readonly string pattern;
        readonly RegexOptions options;
        readonly Node[] program;
        readonly int groupCount; // yakalayan grup sayisi (0 dahil)
        readonly bool ignoreCase, singleline;
        int steps;
        const int MaxSteps = 1000000;
        string input;
        int[] caps;
        Node[][] closeSeqs;

        public Regex(string pattern) : this(pattern, RegexOptions.None) { }
        public Regex(string pattern, RegexOptions options)
        {
            if (pattern == null) throw new ArgumentNullException("pattern");
            var bad = options & ~(RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled | RegexOptions.CultureInvariant);
            if (bad != 0) throw new ArgumentException("regex: desteklenmeyen secenek: " + ((int)bad).ToString());
            this.pattern = pattern; this.options = options;
            ignoreCase = (options & RegexOptions.IgnoreCase) != 0;
            singleline = (options & RegexOptions.Singleline) != 0;
            var p = new Parser(pattern);
            program = p.ParseTop();
            groupCount = p.groupCount + 1;
        }
        public RegexOptions Options { get { return options; } }
        public override string ToString() { return pattern; }

        // ---- yuzey: hepsi Scan uzerinden ----
        public bool IsMatch(string input) { return Scan(input, 0).Success; }
        public Match Match(string input) { return Scan(input, 0); }
        public Match Match(string input, int startat) { return Scan(input, startat); }
        public string Replace(string input, string replacement) { return ReplaceCore(input, replacement, null); }
        public string Replace(string input, MatchEvaluator evaluator) { return ReplaceCore(input, null, evaluator); }

        public static bool IsMatch(string input, string pattern) { return Cached(pattern, RegexOptions.None).IsMatch(input); }
        public static bool IsMatch(string input, string pattern, RegexOptions options) { return Cached(pattern, options).IsMatch(input); }
        public static Match Match(string input, string pattern) { return Cached(pattern, RegexOptions.None).Match(input); }
        public static Match Match(string input, string pattern, RegexOptions options) { return Cached(pattern, options).Match(input); }
        public static string Replace(string input, string pattern, string replacement) { return Cached(pattern, RegexOptions.None).Replace(input, replacement); }
        public static string Replace(string input, string pattern, MatchEvaluator evaluator) { return Cached(pattern, RegexOptions.None).Replace(input, evaluator); }
        public static string Replace(string input, string pattern, string replacement, RegexOptions options) { return Cached(pattern, options).Replace(input, replacement); }

        public static string Escape(string str)
        {
            if (str == null) throw new ArgumentNullException("str");
            var sb = new StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (c == '\n') { sb.Append("\\n"); continue; }
                if (c == '\t') { sb.Append("\\t"); continue; }
                if (c == '\r') { sb.Append("\\r"); continue; }
                if ("\\*+?|{[()^$.# ".IndexOf(c) >= 0) sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // Statik yuzey icin kucuk sinirli onbellek (her cagrida parse yok).
        static readonly Dictionary<string, Regex> cache = new Dictionary<string, Regex>();
        static Regex Cached(string pattern, RegexOptions options)
        {
            string key = ((int)options).ToString() + "\u0001" + pattern;
            Regex r;
            if (cache.TryGetValue(key, out r)) return r;
            if (cache.Count >= 16) cache.Clear();
            r = new Regex(pattern, options);
            cache[key] = r;
            return r;
        }

        string ReplaceCore(string text, string replacement, MatchEvaluator evaluator)
        {
            if (text == null) throw new ArgumentNullException("input");
            var sb = new StringBuilder();
            int start = 0;
            var m = Scan(text, 0);
            while (m.Success)
            {
                if (m.Index > start) sb.Append(text.Substring(start, m.Index - start));
                if (evaluator != null) sb.Append(evaluator(m));
                else AppendReplacement(sb, m, replacement);
                start = m.Index + m.Length;
                m = m.NextMatch();
            }
            if (start < text.Length) sb.Append(text.Substring(start));
            return sb.ToString();
        }
        // $0 $1.. $$ ; diger $ literal
        static void AppendReplacement(StringBuilder sb, Match m, string rep)
        {
            for (int i = 0; i < rep.Length; i++)
            {
                char c = rep[i];
                if (c == '$' && i + 1 < rep.Length)
                {
                    char d = rep[i + 1];
                    if (d == '$') { sb.Append('$'); i++; continue; }
                    if (d >= '0' && d <= '9')
                    {
                        int n = 0, j = i + 1;
                        while (j < rep.Length && rep[j] >= '0' && rep[j] <= '9') { n = n * 10 + (rep[j] - '0'); j++; }
                        sb.Append(m.Groups[n].Value); i = j - 1; continue;
                    }
                }
                sb.Append(c);
            }
        }

        // ---- eslesme cekirdegi ----
        internal Match Scan(string text, int startat)
        {
            if (text == null) throw new ArgumentNullException("input");
            if (startat < 0 || startat > text.Length) throw new ArgumentException("startat");
            input = text;
            caps = new int[groupCount * 2];
            steps = 0;
            for (int pos = startat; pos <= text.Length; pos++)
            {
                for (int i = 0; i < caps.Length; i++) caps[i] = -1;
                caps[0] = pos;
                if (MatchSeq(program, 0, pos, null))
                    return new Match(this, text, caps);
            }
            return new Match(this, text, null);
        }

        bool MatchSeq(Node[] seq, int i, int pos, Cont k)
        {
            if (++steps > MaxSteps) throw new InvalidOperationException("regex: eslesme adim siniri asildi (" + pattern + ")");
            if (i == seq.Length)
            {
                if (k == null) { caps[1] = pos; return true; }
                if (k.rep != null) return Repeat(k.rep, k.count, pos, k.iterStart, k.seq, k.idx, k.next);
                return MatchSeq(k.seq, k.idx, pos, k.next);
            }
            var n = seq[i];
            if (n.kind < 0) // grup kapanisi
            {
                int g = -n.kind - 1;
                int old = caps[2 * g + 1];
                caps[2 * g + 1] = pos;
                if (MatchSeq(seq, i + 1, pos, k)) return true;
                caps[2 * g + 1] = old;
                return false;
            }
            switch (n.kind)
            {
                case KChar:
                    if (pos >= input.Length) return false;
                    if (ignoreCase ? Char.ToLowerInvariant(input[pos]) != Char.ToLowerInvariant(n.ch) : input[pos] != n.ch) return false;
                    return MatchSeq(seq, i + 1, pos + 1, k);
                case KAny:
                    if (pos >= input.Length || (!singleline && input[pos] == '\n')) return false;
                    return MatchSeq(seq, i + 1, pos + 1, k);
                case KSet:
                    if (pos >= input.Length || !n.set.Test(input[pos], ignoreCase)) return false;
                    return MatchSeq(seq, i + 1, pos + 1, k);
                case KBol:
                    return pos == 0 && MatchSeq(seq, i + 1, pos, k);
                case KEol: // .NET: sonda ya da son '\n' oncesinde
                    if (!(pos == input.Length || (pos == input.Length - 1 && input[pos] == '\n'))) return false;
                    return MatchSeq(seq, i + 1, pos, k);
                case KGroup:
                    {
                        int g = n.group;
                        int oldS = 0;
                        if (g >= 0) oldS = caps[2 * g];
                        var after = new Cont { seq = seq, idx = i + 1, next = k };
                        Cont close = after;
                        if (g >= 0) close = new Cont { seq = GroupCloseSeq(g), idx = 0, next = after };
                        for (int a = 0; a < n.alts.Length; a++)
                        {
                            if (g >= 0) caps[2 * g] = pos;
                            if (MatchSeq(n.alts[a], 0, pos, close)) return true;
                        }
                        if (g >= 0) caps[2 * g] = oldS;
                        return false;
                    }
                case KRepeat:
                    return Repeat(n, 0, pos, -1, seq, i, k);
            }
            return false;
        }

        Node[] GroupCloseSeq(int g)
        {
            if (closeSeqs == null) closeSeqs = new Node[groupCount][];
            if (closeSeqs[g] == null) closeSeqs[g] = new Node[] { new Node { kind = -(g + 1) } };
            return closeSeqs[g];
        }

        bool Repeat(Node n, int count, int pos, int iterStart, Node[] seq, int i, Cont k)
        {
            if (count > 0 && pos == iterStart && count > n.min) return false; // bos tekrar: ilerlemiyorsa dur
            bool canMore = n.max < 0 || count < n.max;
            if (n.lazy)
            {
                if (count >= n.min && MatchSeq(seq, i + 1, pos, k)) return true;
                if (!canMore) return false;
                return MatchSeq(n.child, 0, pos, new Cont { rep = n, count = count + 1, iterStart = pos, seq = seq, idx = i, next = k });
            }
            if (canMore && MatchSeq(n.child, 0, pos, new Cont { rep = n, count = count + 1, iterStart = pos, seq = seq, idx = i, next = k })) return true;
            return count >= n.min && MatchSeq(seq, i + 1, pos, k);
        }

        // ---- parser ----
        sealed class Parser
        {
            readonly string p;
            int at;
            public int groupCount;
            public Parser(string pattern) { p = pattern; }
            Exception Err(string what) { return new ArgumentException("regex: desteklenmeyen yapi '" + what + "' @" + at.ToString() + " (" + p + ")"); }

            public Node[] ParseTop()
            {
                var alts = ParseAlts();
                if (at < p.Length) throw Err(")");
                if (alts.Length == 1) return alts[0];
                return new Node[] { new Node { kind = KGroup, alts = alts, group = -1 } };
            }
            Node[][] ParseAlts()
            {
                var list = new List<Node[]>();
                list.Add(ParseSeq());
                while (at < p.Length && p[at] == '|') { at++; list.Add(ParseSeq()); }
                return list.ToArray();
            }
            Node[] ParseSeq()
            {
                var seq = new List<Node>();
                while (at < p.Length && p[at] != '|' && p[at] != ')')
                    seq.Add(ParseQuant(ParseAtom()));
                return seq.ToArray();
            }
            Node ParseAtom()
            {
                char c = p[at++];
                switch (c)
                {
                    case '(':
                        {
                            int g = -1;
                            if (at < p.Length && p[at] == '?')
                            {
                                if (at + 1 < p.Length && p[at + 1] == ':') at += 2;
                                else throw Err("(?" + (at + 1 < p.Length ? p[at + 1].ToString() : ""));
                            }
                            else g = ++groupCount;
                            var alts = ParseAlts();
                            if (at >= p.Length || p[at] != ')') throw Err("kapanmamis (");
                            at++;
                            return new Node { kind = KGroup, alts = alts, group = g };
                        }
                    case '[': return new Node { kind = KSet, set = ParseClass() };
                    case '.': return new Node { kind = KAny };
                    case '^': return new Node { kind = KBol };
                    case '$': return new Node { kind = KEol };
                    case '\\': return ParseEscape();
                    case '*': case '+': case '?': throw Err(c.ToString());
                    default: return new Node { kind = KChar, ch = c };
                }
            }
            Node ParseEscape()
            {
                if (at >= p.Length) throw Err("\\");
                char e = p[at++];
                var set = new CharSet();
                switch (e)
                {
                    case 'd': set.digit = true; return new Node { kind = KSet, set = set };
                    case 'D': set.notDigit = true; return new Node { kind = KSet, set = set };
                    case 'w': set.word = true; return new Node { kind = KSet, set = set };
                    case 'W': set.notWord = true; return new Node { kind = KSet, set = set };
                    case 's': set.space = true; return new Node { kind = KSet, set = set };
                    case 'S': set.notSpace = true; return new Node { kind = KSet, set = set };
                    case 'p': return new Node { kind = KSet, set = ParseCategory() };
                    case 'n': return new Node { kind = KChar, ch = '\n' };
                    case 't': return new Node { kind = KChar, ch = '\t' };
                    case 'r': return new Node { kind = KChar, ch = '\r' };
                    case 'f': return new Node { kind = KChar, ch = '\f' };
                    case 'v': return new Node { kind = KChar, ch = '\v' };
                    case 'u':
                        {
                            if (at + 4 > p.Length) throw Err("\\u");
                            int v = 0;
                            for (int i = 0; i < 4; i++) v = v * 16 + Hex(p[at++]);
                            return new Node { kind = KChar, ch = (char)v };
                        }
                    default:
                        if (Char.IsLetterOrDigit(e)) throw Err("\\" + e.ToString()); // \b \B \A \z \1 ... faz 2
                        return new Node { kind = KChar, ch = e };
                }
            }
            int Hex(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                throw Err("\\u" + c.ToString());
            }
            CharSet ParseCategory()
            {
                if (at >= p.Length || p[at] != '{') throw Err("\\p");
                int end = p.IndexOf('}', at);
                if (end < 0) throw Err("\\p{");
                string name = p.Substring(at + 1, end - at - 1);
                at = end + 1;
                var set = new CharSet();
                if (name == "L") set.letter = true;
                else if (name == "N" || name == "Nd") set.number = true;
                else throw Err("\\p{" + name + "}");
                return set;
            }
            CharSet ParseClass()
            {
                var set = new CharSet();
                if (at < p.Length && p[at] == '^') { set.negate = true; at++; }
                bool first = true;
                while (true)
                {
                    if (at >= p.Length) throw Err("kapanmamis [");
                    char c = p[at++];
                    if (c == ']' && !first) break;
                    first = false;
                    if (c == '\\')
                    {
                        var esc = ParseEscape();
                        if (esc.kind == KSet) { Merge(set, esc.set); continue; }
                        c = esc.ch;
                    }
                    char hi = c;
                    if (at + 1 < p.Length && p[at] == '-' && p[at + 1] != ']')
                    {
                        at++;
                        char h = p[at++];
                        if (h == '\\') { var esc = ParseEscape(); if (esc.kind != KChar) throw Err("aralikta sinif"); h = esc.ch; }
                        if (h < c) throw Err("ters aralik");
                        hi = h;
                    }
                    set.lo.Add(c); set.hi.Add(hi);
                }
                return set;
            }
            static void Merge(CharSet into, CharSet from)
            {
                for (int i = 0; i < from.lo.Count; i++) { into.lo.Add(from.lo[i]); into.hi.Add(from.hi[i]); }
                into.digit |= from.digit; into.notDigit |= from.notDigit; into.word |= from.word; into.notWord |= from.notWord;
                into.space |= from.space; into.notSpace |= from.notSpace; into.letter |= from.letter; into.number |= from.number;
            }
            Node ParseQuant(Node atom)
            {
                if (at >= p.Length) return atom;
                int min, max;
                char c = p[at];
                if (c == '*') { min = 0; max = -1; at++; }
                else if (c == '+') { min = 1; max = -1; at++; }
                else if (c == '?') { min = 0; max = 1; at++; }
                else if (c == '{' && TryBraces(out min, out max)) { }
                else return atom;
                bool lazy = false;
                if (at < p.Length && p[at] == '?') { lazy = true; at++; }
                if (at < p.Length && (p[at] == '*' || p[at] == '+' || p[at] == '?')) throw Err("ic ice nicelik");
                if (atom.kind == KBol || atom.kind == KEol) throw Err("capada nicelik");
                return new Node { kind = KRepeat, child = new Node[] { atom }, min = min, max = max, lazy = lazy };
            }
            // {m} {m,} {m,n}; gecerli degilse literal '{' (.NET davranisi), at ilerlemez.
            bool TryBraces(out int min, out int max)
            {
                min = 0; max = -1;
                int i = at + 1, m = 0, digits = 0;
                while (i < p.Length && p[i] >= '0' && p[i] <= '9') { m = m * 10 + (p[i] - '0'); i++; digits++; }
                if (digits == 0) return false;
                min = m; max = m;
                if (i < p.Length && p[i] == ',')
                {
                    i++; int n = 0; digits = 0;
                    while (i < p.Length && p[i] >= '0' && p[i] <= '9') { n = n * 10 + (p[i] - '0'); i++; digits++; }
                    max = digits == 0 ? -1 : n;
                    if (max >= 0 && max < min) throw Err("{m,n} n<m");
                }
                if (i >= p.Length || p[i] != '}') return false;
                at = i + 1;
                return true;
            }
        }
    }
}
