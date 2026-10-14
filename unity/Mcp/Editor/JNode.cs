using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Digitoy.Mcp
{
    // Hafif JSON agaci (Newtonsoft bagimliligi yok). Skaler = string olarak tutulur; Kind yazimda
    // dogru JSON tipini (sayi/bool/null/metin) secer. Harita sirali (hierarsi dokumunde okunabilirlik).
    public sealed class JNode
    {
        public enum K { Null, Bool, Number, String, Object, Array }

        public K Kind;
        public string Str;                                   // Bool/Number/String icin metin hali
        public List<KeyValuePair<string, JNode>> Fields;     // Object
        public List<JNode> Items;                            // Array

        public bool IsNull => Kind == K.Null;
        public bool IsMap => Kind == K.Object;
        public bool IsSeq => Kind == K.Array;
        public bool IsScalar => Kind == K.Bool || Kind == K.Number || Kind == K.String;
        public string Scalar => IsScalar ? Str : null;

        public static JNode Null() => new JNode { Kind = K.Null };
        public static JNode Map() => new JNode { Kind = K.Object, Fields = new List<KeyValuePair<string, JNode>>() };
        public static JNode Seq() => new JNode { Kind = K.Array, Items = new List<JNode>() };
        public static JNode Str_(string s) => s == null ? Null() : new JNode { Kind = K.String, Str = s };
        public static JNode Bool(bool b) => new JNode { Kind = K.Bool, Str = b ? "true" : "false" };
        public static JNode Num(int v) => new JNode { Kind = K.Number, Str = v.ToString(CultureInfo.InvariantCulture) };
        public static JNode Num(long v) => new JNode { Kind = K.Number, Str = v.ToString(CultureInfo.InvariantCulture) };
        public static JNode Num(float v) => new JNode { Kind = K.Number, Str = F(v) };
        public static JNode Num(double v) => new JNode { Kind = K.Number, Str = v.ToString("R", CultureInfo.InvariantCulture) };

        public static string F(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return "0";
            string s = Math.Round(v, 4).ToString("0.####", CultureInfo.InvariantCulture);
            return s == "-0" ? "0" : s;
        }

        public JNode Add(string key, JNode v)
        {
            Fields.Add(new KeyValuePair<string, JNode>(key, v ?? Null()));
            return this;
        }
        public JNode Add(string key, string v) => Add(key, Str_(v));
        public JNode Add(string key, int v) => Add(key, Num(v));
        public JNode Add(string key, float v) => Add(key, Num(v));
        public JNode Add(string key, bool v) => Add(key, Bool(v));
        public JNode Push(JNode v) { Items.Add(v ?? Null()); return this; }

        public JNode Get(string key)
        {
            if (Fields == null) return null;
            for (int i = 0; i < Fields.Count; i++)
                if (Fields[i].Key == key) return Fields[i].Value;
            return null;
        }

        public void Set(string key, JNode v)
        {
            for (int i = 0; i < Fields.Count; i++)
                if (Fields[i].Key == key) { Fields[i] = new KeyValuePair<string, JNode>(key, v); return; }
            Add(key, v);
        }

        public void Remove(string key)
        {
            for (int i = Fields.Count - 1; i >= 0; i--)
                if (Fields[i].Key == key) Fields.RemoveAt(i);
        }

        public string GetScalar(string key, string def)
        {
            var n = Get(key);
            return n != null && n.IsScalar ? n.Str : def;
        }

        public JNode Clone()
        {
            var c = new JNode { Kind = Kind, Str = Str };
            if (Fields != null)
            {
                c.Fields = new List<KeyValuePair<string, JNode>>(Fields.Count);
                foreach (var kv in Fields) c.Fields.Add(new KeyValuePair<string, JNode>(kv.Key, kv.Value.Clone()));
            }
            if (Items != null)
            {
                c.Items = new List<JNode>(Items.Count);
                foreach (var it in Items) c.Items.Add(it.Clone());
            }
            return c;
        }

        public static bool Equal(JNode a, JNode b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case K.Null: return true;
                case K.Object:
                    if (a.Fields.Count != b.Fields.Count) return false;
                    for (int i = 0; i < a.Fields.Count; i++)
                        if (a.Fields[i].Key != b.Fields[i].Key || !Equal(a.Fields[i].Value, b.Fields[i].Value)) return false;
                    return true;
                case K.Array:
                    if (a.Items.Count != b.Items.Count) return false;
                    for (int i = 0; i < a.Items.Count; i++)
                        if (!Equal(a.Items[i], b.Items[i])) return false;
                    return true;
                default: return a.Str == b.Str;
            }
        }

        // --- yazim ---

        public override string ToString()
        {
            var sb = new StringBuilder();
            Write(sb);
            return sb.ToString();
        }

        public void Write(StringBuilder sb)
        {
            switch (Kind)
            {
                case K.Null: sb.Append("null"); break;
                case K.Bool: sb.Append(Str == "true" ? "true" : "false"); break;
                case K.Number: sb.Append(Str); break;
                case K.String: WriteString(sb, Str); break;
                case K.Object:
                    sb.Append('{');
                    for (int i = 0; i < Fields.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        WriteString(sb, Fields[i].Key);
                        sb.Append(':');
                        Fields[i].Value.Write(sb);
                    }
                    sb.Append('}');
                    break;
                case K.Array:
                    sb.Append('[');
                    for (int i = 0; i < Items.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Items[i].Write(sb);
                    }
                    sb.Append(']');
                    break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // --- okuma ---

        public static JNode Parse(string text)
        {
            int i = 0;
            var n = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length)
                throw new FormatException($"JSON: beklenmeyen karakter @{i}");
            return n;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static JNode ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON: erken bitti");
            char c = s[i];
            if (c == '{')
            {
                i++;
                var m = Map();
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return m; }
                while (true)
                {
                    SkipWs(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new FormatException($"JSON: anahtar bekleniyor @{i}");
                    string key = ParseString(s, ref i);
                    SkipWs(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException($"JSON: ':' bekleniyor @{i}");
                    i++;
                    m.Add(key, ParseValue(s, ref i));
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return m; }
                    throw new FormatException($"JSON: ',' ya da '}}' bekleniyor @{i}");
                }
            }
            if (c == '[')
            {
                i++;
                var a = Seq();
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Items.Add(ParseValue(s, ref i));
                    SkipWs(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new FormatException($"JSON: ',' ya da ']' bekleniyor @{i}");
                }
            }
            if (c == '"')
                return Str_(ParseString(s, ref i));
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return Bool(true); }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return Bool(false); }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return Null(); }
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            if (i == start) throw new FormatException($"JSON: deger bekleniyor @{i}");
            string num = s.Substring(start, i - start);
            if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                throw new FormatException($"JSON: gecersiz sayi '{num}'");
            return new JNode { Kind = K.Number, Str = num };
        }

        static string ParseString(string s, ref int i)
        {
            i++; // acilis "
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("JSON: kisa \\u");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException($"JSON: gecersiz kacis '\\{e}'");
                }
            }
            throw new FormatException("JSON: metin kapanmadi");
        }
    }
}
