using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using DigitoyEngine;

namespace DigitoyEditor;

// RPC/MCP cikis filtresi (docs/plugins.md "Sensitive"): [Sensitive] alanlarin degerleri AI'a GIDEN HICBIR yanitta gorunmez.
// Tek nokta: RpcHost her yaniti (Ok/deferred) buradan gecirir. Kapsam alan ADI bazli (tip baglami bilinmedigi icin): editor assembly'si +
// oyun/plugin assembly'leri + ayar tipleri taranir, [Sensitive] alan adlari kumesi kurulur (reload'da tazelenir).
//   - DocNode agaci: hassas anahtarin degeri (skaler/koleksiyon) -> "***"
//   - Metin skalerleri (asset.read icerigi, log satirlari): `anahtar: deger` / `"anahtar": deger` / `anahtar=deger` satir ici ikame
//   - Girdi: "***" degerini hassas alana geri yazma denemesi reddedilir (RpcHost.GuardInput)
// Isim sezgisi YOK; yalniz attribute.
public static class RpcRedact
{
    public const string Mask = "***";
    static readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    static Regex _textRx;
    static Assembly[] _scannedFor;

    public static IReadOnlyCollection<string> Keys => _keys;

    // Editor + motor + oyun/plugin assembly'leri: [Sensitive] tasiyan tum alan adlari.
    public static void Rebuild(Assembly[] codeAssemblies)
    {
        _keys.Clear();
        var asms = new List<Assembly> { typeof(App).Assembly, typeof(GameObject).Assembly };
        if (codeAssemblies != null) asms.AddRange(codeAssemblies);
        foreach (var asm in asms)
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var t in types)
            {
                if (t == null) continue;
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (f.IsDefined(typeof(SensitiveAttribute), false)) _keys.Add(f.Name);
            }
        }
        _scannedFor = codeAssemblies;
        _textRx = _keys.Count == 0 ? null : new Regex(
            // YAML `key: value` | JSON `"key": value` | properties `key=value`; deger satir sonuna kadar (JSON'da virgule kadar)
            "(?<pre>(^|[\\s{,\"'.])(?<key>" + string.Join("|", _keys) + ")[\"']?\\s*[:=]\\s*)(?<val>[^\\r\\n,}]*)",
            RegexOptions.Multiline | RegexOptions.Compiled);
    }

    public static bool IsSensitiveKey(string key) => key != null && _keys.Contains(key);

    // Yanit agaci: hassas anahtar -> "***"; tum string skalerler metin filtresinden gecer. Yerinde degistirir (yanit zaten gecici).
    public static DocNode Apply(DocNode n)
    {
        if (n == null || _keys.Count == 0) return n;
        Walk(n);
        return n;
    }

    static void Walk(DocNode n)
    {
        if (n.Fields != null)
        {
            for (int i = 0; i < n.Fields.Count; i++)
            {
                var kv = n.Fields[i];
                if (_keys.Contains(kv.Key)) { n.Fields[i] = new KeyValuePair<string, DocNode>(kv.Key, DocNode.Scal(Mask)); continue; }
                Walk(kv.Value);
            }
        }
        else if (n.Items != null)
        {
            foreach (var it in n.Items) Walk(it);
        }
        else if (n.Scalar != null && n.Scalar.Length > 0 && _textRx != null)
        {
            n.Scalar = RedactText(n.Scalar);
        }
    }

    // Serbest metinde satir ici `key: value` ikamesi (asset.read YAML, log.tail, scene.dump metin bicimleri).
    public static string RedactText(string s)
    {
        if (s == null || _textRx == null) return s;
        return _textRx.Replace(s, m => m.Groups["pre"].Value + Mask);
    }

    // Girdi korumasi: hassas alana "***" (maske) yazma denemesi — yanlislikla ezme.
    public static void GuardInput(string key, string value)
    {
        if (_keys.Contains(key) && value != null && value.Trim() == Mask)
            throw new RpcError($"'{key}' is sensitive: the masked value '{Mask}' cannot be written back (set a real value or leave unchanged)");
    }

    // Serbest metin yaziminda (asset.write) maskeli satir varsa reddet: "token: ***" dosyaya girmesin.
    public static void GuardText(string content)
    {
        if (content == null || _textRx == null) return;
        var m = _textRx.Match(content);
        while (m.Success)
        {
            if (m.Groups["val"].Value.Trim().Trim('"', '\'') == Mask)
                throw new RpcError($"'{m.Groups["key"].Value}' is sensitive: masked value '{Mask}' cannot be written to a file (read the file in the editor instead)");
            m = m.NextMatch();
        }
    }
}
