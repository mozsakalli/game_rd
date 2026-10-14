using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DigitoyEngine;

namespace DigitoyEditor;

// Ceviri denetimleri (RPC loc.* + pak build uyarilari): key kullanimlari (sahne/prefab "@loc:" + C# Localize.Get/Format
// string sabitleri), kullanilmayan satirlar, dilde eksikler, glyph kapsami (hucre karakterleri ⊆ font glyph seti).
// Dosya duzeyi tarama (acik doc degil): proje genisi, kaydedilmemis degisiklikler gorunmez.
public static class LocAudit
{
    static readonly Regex CodeKey = new(@"Localize\s*\.\s*(?:Get|Format)\s*\(\s*(?:[A-Za-z_]\w*\s*,\s*)?""([^""]+)""", RegexOptions.Compiled);

    public sealed class Usage
    {
        public string File;   // Assets'e goreli
        public string Where;  // "go 'Ad' LayoutBox.text" | "line 12"
    }

    // Kapsamdaki sahne/prefab/C# dosyalari (modul kapsami = modul klasoru; host = Modules/ disi).
    static IEnumerable<string> ScopeFiles(string scope, string ext)
    {
        var root = App.Assets?.Root;
        if (root == null) yield break;
        foreach (var f in Directory.EnumerateFiles(root, "*" + ext, SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, f).Replace('\\', '/');
            if (Localize.TableKeyFor(rel) == scope)
                yield return rel;
        }
    }

    // key -> kullanimlar (tum dosyalar bir gecis; cok key icin tekrar tarama olmasin).
    public static Dictionary<string, List<Usage>> AllUsages(string scope)
    {
        var map = new Dictionary<string, List<Usage>>(StringComparer.Ordinal);
        var root = App.Assets.Root;
        void Add(string key, string file, string where)
        {
            if (!map.TryGetValue(key, out var l)) map[key] = l = new List<Usage>();
            l.Add(new Usage { File = file, Where = where });
        }
        foreach (var ext in new[] { ".scene", ".prefab" })
            foreach (var rel in ScopeFiles(scope, ext))
            {
                string goName = "?", compType = "?";
                foreach (var raw in File.ReadLines(Path.Combine(root, rel)))
                {
                    string t = raw.Trim();
                    if (t.StartsWith("- "))
                        t = t.Substring(2);
                    if (t.StartsWith("name: ")) goName = Unquote(t.Substring(6));
                    else if (t.StartsWith("type: ")) compType = t.Substring(6).Trim();
                    int c = t.IndexOf(Localize.PropSuffix + ": ", StringComparison.Ordinal);
                    if (c <= 0) continue;
                    string field = t.Substring(0, c);
                    string key = Unquote(t.Substring(c + Localize.PropSuffix.Length + 2));
                    if (key.Length > 0)
                        Add(key, rel, $"go '{goName}' {compType}.{field}");
                }
            }
        foreach (var rel in ScopeFiles(scope, ".cs"))
        {
            int line = 0;
            foreach (var raw in File.ReadLines(Path.Combine(root, rel)))
            {
                line++;
                foreach (Match m in CodeKey.Matches(raw))
                    Add(m.Groups[1].Value, rel, "line " + line);
            }
        }
        return map;
    }

    public static List<string> Unused(string scope)
    {
        var table = LocTableStore.Get(scope);
        var res = new List<string>();
        if (table == null) return res;
        var used = AllUsages(scope);
        foreach (var e in table.Entries)
            if (!string.IsNullOrEmpty(e.Key) && !used.ContainsKey(e.Key) && !e.Key.StartsWith("format.", StringComparison.Ordinal))
                res.Add(e.Key);
        return res;
    }

    // Tabloda olmayan ama kullanilan key'ler.
    public static List<string> Dangling(string scope, Dictionary<string, List<Usage>> usages = null)
    {
        var table = LocTableStore.Get(scope);
        var res = new List<string>();
        usages ??= AllUsages(scope);
        foreach (var k in usages.Keys)
            if (table == null || table.Find(k) < 0)
                res.Add(k);
        return res;
    }

    public static List<string> Missing(string scope, string lang)
    {
        var table = LocTableStore.Get(scope);
        var res = new List<string>();
        int li = table?.LanguageIndex(lang) ?? -1;
        if (li < 0) return res;
        foreach (var e in table.Entries)
            if (e.Values == null || li >= e.Values.Count || string.IsNullOrEmpty(e.Values[li]))
                res.Add(e.Key);
        return res;
    }

    public sealed class GlyphGap
    {
        public string Font;                       // asset yolu
        public readonly SortedSet<int> Codepoints = new();
        public readonly HashSet<string> Keys = new(StringComparer.Ordinal);
    }

    // Projedeki her font icin tablo hucrelerinde gecen ama fontta olmayan codepoint'ler (kontrol karakterleri haric).
    public static List<GlyphGap> GlyphCheck(string scope)
    {
        var res = new List<GlyphGap>();
        var table = LocTableStore.Get(scope);
        var assets = App.Assets;
        if (table == null || assets == null) return res;
        var fonts = new List<(string Key, HashSet<int> Set)>();
        foreach (var kv in assets.AllAssets)
        {
            string k = kv.Key;
            if (!(k.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (assets.LoadAsset(k, typeof(Font)) is not Font f || f.Glyphs == null || f.Glyphs.Length == 0)
                continue;
            var set = new HashSet<int>();
            foreach (var g in f.Glyphs) set.Add(g.Codepoint);
            fonts.Add((k, set));
        }
        if (fonts.Count == 0) return res;
        foreach (var (fk, set) in fonts)
        {
            var gap = new GlyphGap { Font = fk };
            foreach (var e in table.Entries)
            {
                if (e.Values == null) continue;
                foreach (var v in e.Values)
                {
                    if (string.IsNullOrEmpty(v)) continue;
                    for (int i = 0; i < v.Length; i++)
                    {
                        int cp = char.IsHighSurrogate(v[i]) && i + 1 < v.Length ? char.ConvertToUtf32(v[i], v[++i]) : v[i];
                        if (cp < 32 || cp == '{' || cp == '}') continue; // bicim isaretleri/kontrol
                        if (!set.Contains(cp)) { gap.Codepoints.Add(cp); gap.Keys.Add(e.Key); }
                    }
                }
            }
            if (gap.Codepoints.Count > 0)
                res.Add(gap);
        }
        return res;
    }

    public static string CodepointsText(IEnumerable<int> cps, int max = 40)
    {
        var sb = new System.Text.StringBuilder();
        int n = 0;
        foreach (var cp in cps)
        {
            if (n++ >= max) { sb.Append('…'); break; }
            sb.Append(char.ConvertFromUtf32(cp)).Append("(U+").Append(cp.ToString("X4")).Append(") ");
        }
        return sb.ToString().TrimEnd();
    }

    static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
            s = s.Substring(1, s.Length - 2).Replace("''", "'");
        return s;
    }
}
