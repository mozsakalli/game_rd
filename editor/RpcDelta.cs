using System;
using System.Collections.Generic;
using System.Globalization;
using DigitoyEngine;

namespace DigitoyEditor;

// SNAPSHOT → DELTA: okuma komutlari (render.describe, scene.dumpLive, live.get) ilk cagrida TAM gorunum doner ve
// sonucu anahtar (komut + parametre imzasi) basina saklar; AYNI sorgu tekrar gelince yalniz DEGISENI doner
// (added/changed/removed; hic degisiklik yoksa {unchanged:true}). AI'in bir sey ogrenmesi gerekmez: buyuk cevap bir
// kez, sonrakiler kendiliginden kucuk. full=true tam gorunumu yeniden ister. Play yeniden dogunca (restart/hot reload)
// tum baseline'lar duser (ilk cagri yine tam). Ogeler YOL ile eslesir; ayni yol tekrar ediyorsa '#k' sira eki.
public static class RpcDelta
{
    static readonly Dictionary<string, Dictionary<string, DocNode>> _base = new(StringComparer.Ordinal);
    static int _generation; // play restart'ta ++ (teshis)

    public static void InvalidateAll()
    {
        _base.Clear();
        _generation++;
    }

    // items: sirali (anahtar, deger) listesi. full=true ya da baseline yok → tam liste (+ "full":true); degilse delta.
    // Donus haritasina cagiran ust alanlari (screen/cameras vb.) ekler. itemsKey: tam listenin anahtari ("items"/"objects").
    public static DocNode Apply(string key, List<KeyValuePair<string, DocNode>> items, bool full, string itemsKey)
    {
        var now = new Dictionary<string, DocNode>(StringComparer.Ordinal);
        var order = new List<string>(items.Count);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var kv in items)
        {
            string k = kv.Key;
            if (seen.TryGetValue(k, out int n)) { seen[k] = n + 1; k = k + "#" + (n + 1).ToString(CultureInfo.InvariantCulture); }
            else seen[k] = 0;
            now[k] = kv.Value;
            order.Add(k);
        }
        var r = DocNode.Map();
        bool hasBase = _base.TryGetValue(key, out var was);
        if (full || !hasBase)
        {
            var seq = DocNode.Seq();
            foreach (var k in order) seq.Items.Add(now[k]);
            r.Add(itemsKey, seq);
            r.Add("count", DocNode.Scal(order.Count.ToString(CultureInfo.InvariantCulture)));
            r.Add("full", DocNode.Scal("true"));
            r.Add("deltaNote", DocNode.Scal("Ayni sorgunun SONRAKI cagrisi yalniz degiseni doner (added/changed/removed | unchanged). Tam gorunum: full=true"));
        }
        else
        {
            var added = DocNode.Seq(); var changed = DocNode.Seq(); var removed = DocNode.Seq();
            foreach (var k in order)
            {
                if (!was.TryGetValue(k, out var old)) added.Items.Add(Tagged(now[k], k));
                else if (!DocNode.Equal(now[k], old)) changed.Items.Add(Tagged(now[k], k));
            }
            foreach (var k in was.Keys)
                if (!now.ContainsKey(k)) removed.Items.Add(DocNode.Scal(k));
            if (added.Items.Count == 0 && changed.Items.Count == 0 && removed.Items.Count == 0)
                r.Add("unchanged", DocNode.Scal("true"));
            else
            {
                if (added.Items.Count > 0) r.Add("added", added);
                if (changed.Items.Count > 0) r.Add("changed", changed);
                if (removed.Items.Count > 0) r.Add("removed", removed);
            }
            r.Add("delta", DocNode.Scal("true"));
            r.Add("count", DocNode.Scal(order.Count.ToString(CultureInfo.InvariantCulture)));
        }
        _base[key] = now;
        return r;
    }

    // Harita ogelerine anahtari koy (yoksa): delta okuyan hangi oge oldugunu bilsin. Skalerde {key, value}.
    static DocNode Tagged(DocNode v, string key)
    {
        if (v.IsMap)
        {
            if (v.Get("path") == null && v.Get("key") == null)
            {
                var m = DocNode.Map();
                m.Add("key", DocNode.Scal(key));
                foreach (var kv in v.Fields) m.Add(kv.Key, kv.Value);
                return m;
            }
            return v;
        }
        var w = DocNode.Map();
        w.Add("key", DocNode.Scal(key));
        w.Add("value", v);
        return w;
    }
}
