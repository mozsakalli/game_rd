using System.Collections.Generic;
using System.Text;

namespace DigitoyEngine;

// Genel YAML degeri: skaler | dizi | harita. Serilestirme agaci (koleksiyon,
// ic ice [Serializable] nesne) bu modelle tasinir; SceneDoc butun dosyayi
// bununla yazar/okur. Bagimlilik yok, dar altkume: 2'ser bosluk girinti,
// "- " dizi ogesi, tek satir skaler, '#'/':'/tirnak icerenler tek tirnakli.
public sealed class DocNode
{
    public string Scalar;
    public List<DocNode> Items;                          // dizi
    public List<KeyValuePair<string, DocNode>> Fields;   // harita (sira korunur)

    public bool IsMap => Fields != null;
    public bool IsSeq => Items != null;

    public static DocNode Scal(string s) => new() { Scalar = s ?? "" };
    public static DocNode Seq() => new() { Items = new List<DocNode>() };
    public static DocNode Map() => new() { Fields = new List<KeyValuePair<string, DocNode>>() };

    public void Add(string key, DocNode value) => Fields.Add(new(key, value));

    public DocNode Get(string key)
    {
        if (Fields == null)
            return null;
        foreach (var kv in Fields)
            if (kv.Key == key)
                return kv.Value;
        return null;
    }

    public string GetScalar(string key, string def = "")
        => Get(key) is { Scalar: not null } n ? n.Scalar : def;

    // Derin kopya (prefab expand: prefab doc'u instance'a klonlanir).
    public DocNode Clone()
    {
        var n = new DocNode { Scalar = Scalar };
        if (Items != null)
        {
            n.Items = new List<DocNode>(Items.Count);
            foreach (var it in Items)
                n.Items.Add(it.Clone());
        }
        if (Fields != null)
        {
            n.Fields = new List<KeyValuePair<string, DocNode>>(Fields.Count);
            foreach (var kv in Fields)
                n.Fields.Add(new(kv.Key, kv.Value.Clone()));
        }
        return n;
    }

    // Yapisal esitlik (prefab diff: override uretilecek mi).
    public static bool Equal(DocNode a, DocNode b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null)
            return false;
        if (a.Scalar != b.Scalar)
            return false;
        if ((a.Items != null) != (b.Items != null) || (a.Fields != null) != (b.Fields != null))
            return false;
        if (a.Items != null)
        {
            if (a.Items.Count != b.Items.Count)
                return false;
            for (int i = 0; i < a.Items.Count; i++)
                if (!Equal(a.Items[i], b.Items[i]))
                    return false;
        }
        if (a.Fields != null)
        {
            if (a.Fields.Count != b.Fields.Count)
                return false;
            for (int i = 0; i < a.Fields.Count; i++)
                if (a.Fields[i].Key != b.Fields[i].Key || !Equal(a.Fields[i].Value, b.Fields[i].Value))
                    return false;
        }
        return true;
    }
}

public static class Yaml
{
    // --- Yazici ---

    public static string Write(DocNode rootMap)
    {
        var sb = new StringBuilder(4096);
        WriteMapBody(sb, rootMap, 0);
        return sb.ToString();
    }

    static void WriteMapBody(StringBuilder sb, DocNode map, int indent)
    {
        foreach (var kv in map.Fields)
        {
            Indent(sb, indent);
            sb.Append(kv.Key).Append(':');
            WriteValueAfterKey(sb, kv.Value, indent);
        }
    }

    static void WriteValueAfterKey(StringBuilder sb, DocNode v, int indent)
    {
        if (v.IsMap)
        {
            sb.Append('\n');
            WriteMapBody(sb, v, indent + 2);
        }
        else if (v.IsSeq)
        {
            sb.Append('\n');
            WriteSeqBody(sb, v, indent + 2);
        }
        else
        {
            sb.Append(' ').Append(Quote(v.Scalar)).Append('\n');
        }
    }

    static void WriteSeqBody(StringBuilder sb, DocNode seq, int indent)
    {
        foreach (var item in seq.Items)
        {
            Indent(sb, indent);
            sb.Append("- ");
            if (item.IsMap)
            {
                // Ilk alan "- " ile ayni satirda, kalanlar +2 sutunda (okunur biçim).
                bool firstField = true;
                foreach (var kv in item.Fields)
                {
                    if (!firstField)
                    {
                        Indent(sb, indent + 2);
                    }
                    firstField = false;
                    sb.Append(kv.Key).Append(':');
                    WriteValueAfterKey(sb, kv.Value, indent + 2);
                }
                if (item.Fields.Count == 0)
                    sb.Append("{}\n");
            }
            else if (item.IsSeq)
            {
                sb.Append('\n');
                WriteSeqBody(sb, item, indent + 2); // dizi-icinde-dizi (kullanilmiyor ama tutarli)
            }
            else
            {
                sb.Append(Quote(item.Scalar)).Append('\n');
            }
        }
    }

    static void Indent(StringBuilder sb, int n) => sb.Append(' ', n);

    static string Quote(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "''";
        return (s[0] == '#' || s[0] == '\'' || s.Contains(':'))
            ? "'" + s.Replace("'", "''") + "'"
            : s;
    }

    // --- Okuyucu ---

    struct Line
    {
        public int Indent;
        public bool Item;    // "- " ile basliyor
        public bool HasKey;  // "key:" icerir
        public string Key;
        public string Val;
    }

    public static DocNode Parse(string text)
    {
        var lines = Tokenize(text);
        int i = 0;
        return ParseMap(lines, ref i, 0);
    }

    static List<Line> Tokenize(string text)
    {
        var list = new List<Line>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r', ' ');
            if (line.Length == 0)
                continue;
            int indent = 0;
            while (indent < line.Length && line[indent] == ' ')
                indent++;
            var body = line.Substring(indent);
            if (body.StartsWith("#"))
                continue;
            var ln = new Line { Indent = indent };
            if (body.StartsWith("- "))
            {
                ln.Item = true;
                body = body.Substring(2);
            }
            else if (body == "-")
            {
                ln.Item = true;
                body = "";
            }
            // "key: val" / "key:" ayrimi — tirnakli skaler ':' icerebilir,
            // o yuzden tirnakla baslayan govde anahtar sayilmaz.
            int colon = body.StartsWith("'") ? -1 : body.IndexOf(": ", System.StringComparison.Ordinal);
            if (colon < 0 && body.EndsWith(":") && !body.StartsWith("'"))
                colon = body.Length - 1;
            if (colon >= 0)
            {
                ln.HasKey = true;
                ln.Key = body.Substring(0, colon).Trim();
                ln.Val = Unquote(colon + 1 < body.Length ? body.Substring(colon + 1).Trim() : "");
            }
            else
            {
                ln.Val = Unquote(body.Trim());
            }
            list.Add(ln);
        }
        return list;
    }

    static DocNode ParseBlock(List<Line> l, ref int i, int minIndent)
    {
        if (i >= l.Count || l[i].Indent < minIndent)
            return DocNode.Scal("");
        int indent = l[i].Indent;
        return l[i].Item ? ParseSeq(l, ref i, indent) : ParseMap(l, ref i, indent);
    }

    static DocNode ParseMap(List<Line> l, ref int i, int indent)
    {
        var map = DocNode.Map();
        while (i < l.Count && l[i].Indent == indent && !l[i].Item && l[i].HasKey)
        {
            var ln = l[i];
            i++;
            if (ln.Val.Length > 0)
                map.Add(ln.Key, DocNode.Scal(ln.Val));
            else if (i < l.Count && l[i].Indent > indent)
                map.Add(ln.Key, ParseBlock(l, ref i, indent + 1));
            else
                map.Add(ln.Key, DocNode.Scal(""));
        }
        return map;
    }

    static DocNode ParseSeq(List<Line> l, ref int i, int indent)
    {
        var seq = DocNode.Seq();
        while (i < l.Count && l[i].Indent == indent && l[i].Item)
        {
            var ln = l[i];
            if (!ln.HasKey)
            {
                seq.Items.Add(DocNode.Scal(ln.Val));
                i++;
                continue;
            }
            // Harita ogesi: "- key: val" ilk alani, devami +2 sutunda —
            // satiri normal harita satirina cevirip ParseMap'e devret.
            l[i] = new Line { Indent = indent + 2, Item = false, HasKey = true, Key = ln.Key, Val = ln.Val };
            seq.Items.Add(ParseMap(l, ref i, indent + 2));
        }
        return seq;
    }

    static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
            return s.Substring(1, s.Length - 2).Replace("''", "'");
        return s;
    }
}
