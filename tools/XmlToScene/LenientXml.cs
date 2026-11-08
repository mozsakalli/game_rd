namespace XmlToScene;

// Eski Digiplay XML'i standart XML degil: "T:ui:avatar" gibi iki noktali tag adlari,
// bildirimsiz "bind:"/"action:" onekleri, kacissiz '&' degerleri. Bu yuzden XDocument
// yerine kucuk, hosgorulu bir parser: element + attribute + yorum; metin icerigi tutulur
// (Html gibi tag'ler icin), kalan her sey yok sayilir.
sealed class XNode
{
    public string Name = "";
    public readonly List<KeyValuePair<string, string>> Attrs = new();
    public readonly List<XNode> Children = new();
    public string Text = "";
    public int Line;
    public XNode? Parent;

    public string? this[string key]
    {
        get { foreach (var a in Attrs) if (a.Key == key) return a.Value; return null; }
    }
    public bool Has(string key) => this[key] != null;
    public void Set(string key, string value)
    {
        for (int i = 0; i < Attrs.Count; i++)
            if (Attrs[i].Key == key) { Attrs[i] = new(key, value); return; }
        Attrs.Add(new(key, value));
    }
    public void Remove(string key) => Attrs.RemoveAll(a => a.Key == key);
}

static class LenientXml
{
    public static XNode Parse(string text, string file)
    {
        int i = 0, line = 1;
        var root = new XNode { Name = "#doc" };
        var stack = new Stack<XNode>();
        stack.Push(root);
        var sb = new System.Text.StringBuilder();

        void Count(int from, int to) { for (int k = from; k < to; k++) if (text[k] == '\n') line++; }

        while (i < text.Length)
        {
            int lt = text.IndexOf('<', i);
            if (lt < 0) { Count(i, text.Length); break; }
            if (lt > i)
            {
                string t = text.Substring(i, lt - i);
                if (t.Trim().Length > 0) stack.Peek().Text += Unescape(t);
                Count(i, lt);
            }
            i = lt;
            if (text.AsSpan(i).StartsWith("<!--"))
            {
                int e = text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (e < 0) throw new Exception($"{file}:{line}: yorum kapanmadi");
                Count(i, e + 3); i = e + 3; continue;
            }
            if (text.AsSpan(i).StartsWith("<?") || text.AsSpan(i).StartsWith("<!"))
            {
                int e = text.IndexOf('>', i);
                if (e < 0) throw new Exception($"{file}:{line}: bildirim kapanmadi");
                Count(i, e + 1); i = e + 1; continue;
            }
            if (text.AsSpan(i).StartsWith("</"))
            {
                int e = text.IndexOf('>', i);
                string name = text.Substring(i + 2, e - i - 2).Trim();
                if (stack.Count <= 1 || stack.Peek().Name != name)
                    throw new Exception($"{file}:{line}: beklenmeyen kapanis </{name}> (acik: {stack.Peek().Name})");
                stack.Pop();
                Count(i, e + 1); i = e + 1; continue;
            }
            // acilis tag'i
            int p = i + 1;
            int ns = p;
            while (p < text.Length && !char.IsWhiteSpace(text[p]) && text[p] != '>' && text[p] != '/') p++;
            var node = new XNode { Name = text.Substring(ns, p - ns), Line = line, Parent = stack.Peek() };
            bool selfClose = false;
            while (true)
            {
                while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
                if (p >= text.Length) throw new Exception($"{file}:{line}: tag kapanmadi <{node.Name}");
                if (text[p] == '/') { selfClose = true; p++; continue; }
                if (text[p] == '>') { p++; break; }
                int ks = p;
                while (p < text.Length && !char.IsWhiteSpace(text[p]) && text[p] != '=' && text[p] != '>' && text[p] != '/') p++;
                string key = text.Substring(ks, p - ks);
                while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
                string val = "";
                if (p < text.Length && text[p] == '=')
                {
                    p++;
                    while (p < text.Length && char.IsWhiteSpace(text[p])) p++;
                    char q = text[p];
                    if (q == '"' || q == '\'')
                    {
                        int e = text.IndexOf(q, p + 1);
                        if (e < 0) throw new Exception($"{file}:{line}: tirnak kapanmadi ({node.Name} {key})");
                        val = Unescape(text.Substring(p + 1, e - p - 1));
                        p = e + 1;
                    }
                    else
                    {
                        int vs = p;
                        while (p < text.Length && !char.IsWhiteSpace(text[p]) && text[p] != '>' && text[p] != '/') p++;
                        val = text.Substring(vs, p - vs);
                    }
                }
                node.Attrs.Add(new(key, val));
            }
            stack.Peek().Children.Add(node);
            if (!selfClose) stack.Push(node);
            Count(i, p); i = p;
        }
        if (stack.Count != 1) throw new Exception($"{file}: kapanmamis tag <{stack.Peek().Name}> (satir {stack.Peek().Line})");
        if (root.Children.Count == 0) throw new Exception($"{file}: kok element yok");
        return root.Children[0];
    }

    static string Unescape(string s)
    {
        if (s.IndexOf('&') < 0) return s;
        return s.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&amp;", "&");
    }
}
