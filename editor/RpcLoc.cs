using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// RPC loc.*: ceviri tablosu + baglar (docs/engine-notes.md LOCALIZATION F3). Hepsi LocTableStore/EditorScene
// bogazindan (undo'lu, canli patch). AI ceviri dongusu: loc.missing(lang) -> cevir -> loc.set(entries) (tek undo).
static class RpcLoc
{
    static string Scope(RpcContext ctx, string scope)
        => !string.IsNullOrEmpty(scope) ? scope : ctx.Scene?.Doc?.LocTable ?? LocTableStore.HostScope;

    static LocalizationTable Require(RpcContext ctx, string scope, bool create = false)
        => LocTableStore.Get(Scope(ctx, scope), create) ?? throw new RpcError("kapsam tablosu yok: " + Scope(ctx, scope) + " (ilk bag ya da loc.addLanguage olusturur)");

    static DocNode Strs(IEnumerable<string> items)
    {
        var s = DocNode.Seq();
        foreach (var i in items) s.Items.Add(DocNode.Scal(i));
        return s;
    }

    [RpcCommand("loc.languages", "Kapsamin dilleri ([0] = kaynak/fallback), calisilan dil, sabit dil katalogu. scope bos = acik sahnenin kapsami (host: Localization.asset, modul: Modules/<ad>/Localization.asset)")]
    static DocNode Languages(RpcContext ctx, string scope = "")
    {
        string sc = Scope(ctx, scope);
        var t = LocTableStore.Get(sc);
        var r = DocNode.Map();
        r.Add("scope", DocNode.Scal(sc));
        r.Add("exists", DocNode.Scal(t != null ? "true" : "false"));
        r.Add("languages", Strs(t?.Languages ?? new List<string>()));
        r.Add("source", DocNode.Scal(t?.SourceLanguage ?? ""));
        r.Add("working", DocNode.Scal(LocTableStore.WorkingLanguage));
        var cat = DocNode.Seq();
        foreach (var (code, name) in LocTableStore.LanguageCatalog)
        {
            var m = DocNode.Map();
            m.Add("code", DocNode.Scal(code));
            m.Add("name", DocNode.Scal(name));
            cat.Items.Add(m);
        }
        r.Add("catalog", cat);
        r.Add("scopes", Strs(LocTableStore.Scopes()));
        return r;
    }

    [RpcCommand("loc.setLanguage", "Calisilan dili degistirir (toolbar combosu): edit projeksiyonu + Play bu dilde; inspector bu dilin hucresini yazar. Bos = kaynak dil")]
    static DocNode SetLanguage(RpcContext ctx, string lang = "")
    {
        if (lang.Length > 0 && !LocTableStore.IsKnownLanguage(lang))
            throw new RpcError("katalog disi dil: " + lang);
        LocTableStore.WorkingLanguage = lang.ToLowerInvariant();
        return null;
    }

    [RpcCommand("loc.addLanguage", "Kapsam tablosuna dil sutunu ekler (yalniz katalogdaki kodlar); tablo yoksa olusturur")]
    static DocNode AddLanguage(RpcContext ctx, string code, string scope = "")
    {
        if (!LocTableStore.IsKnownLanguage(code))
            throw new RpcError("katalog disi dil: " + code + " (loc.languages catalog)");
        LocTableStore.Get(Scope(ctx, scope), create: true);
        LocTableStore.AddLanguage(Scope(ctx, scope), code);
        return Languages(ctx, scope);
    }

    [RpcCommand("loc.removeLanguage", "Dil sutununu ve o dildeki tum hucreleri siler (kaynak dil silinemez)")]
    static DocNode RemoveLanguage(RpcContext ctx, string code, string scope = "")
    {
        var t = Require(ctx, scope);
        if (t.LanguageIndex(code) <= 0)
            throw new RpcError("dil yok ya da kaynak dil: " + code);
        LocTableStore.RemoveLanguage(Scope(ctx, scope), code);
        return Languages(ctx, scope);
    }

    [RpcCommand("loc.list", "Tablo satirlari: [{key, values:{lang: metin}, uses}] (uses = acik doc'taki bag sayisi). filter: key/metin icinde gecen; missing: yalniz o dilde bos olanlar")]
    static DocNode List(RpcContext ctx, string scope = "", string filter = "", string missing = "")
    {
        var t = Require(ctx, scope);
        int mi = missing.Length > 0 ? t.LanguageIndex(missing) : -1;
        if (missing.Length > 0 && mi < 0)
            throw new RpcError("tabloda olmayan dil: " + missing);
        var rows = DocNode.Seq();
        foreach (var e in t.Entries)
        {
            if (filter.Length > 0 && !Matches(e, filter)) continue;
            if (mi >= 0 && e.Values != null && mi < e.Values.Count && !string.IsNullOrEmpty(e.Values[mi])) continue;
            rows.Items.Add(Row(ctx, t, e));
        }
        var r = DocNode.Map();
        r.Add("scope", DocNode.Scal(Scope(ctx, scope)));
        r.Add("languages", Strs(t.Languages));
        r.Add("count", DocNode.Scal(rows.Items.Count.ToString()));
        r.Add("rows", rows);
        return r;
    }

    static DocNode Row(RpcContext ctx, LocalizationTable t, LocEntry e)
    {
        var m = DocNode.Map();
        m.Add("key", DocNode.Scal(e.Key));
        var vals = DocNode.Map();
        for (int i = 0; i < t.Languages.Count; i++)
            vals.Add(t.Languages[i], DocNode.Scal(e.Values != null && i < e.Values.Count ? e.Values[i] ?? "" : ""));
        m.Add("values", vals);
        m.Add("uses", DocNode.Scal((ctx.Scene?.CountLocUsages(e.Key) ?? 0).ToString()));
        return m;
    }

    static bool Matches(LocEntry e, string filter)
    {
        if (e.Key != null && e.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (e.Values != null)
            foreach (var v in e.Values)
                if (v != null && v.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    [RpcCommand("loc.set", "Hucreleri TOPLU yazar (tek undo): entries: [{key, lang, value}] ya da [{key, values:{lang: metin}}]. Olmayan key satir acar; dil tabloda yoksa hata. AI ceviri: loc.missing(lang) -> cevir -> loc.set")]
    static DocNode Set(RpcContext ctx, DocNode entries, string scope = "")
    {
        if (entries?.Items == null)
            throw new RpcError("entries liste olmali");
        string sc = Scope(ctx, scope);
        var t = LocTableStore.Get(sc, create: true);
        var es = ctx.Scene;
        int n = 0;
        bool own = !es.History.InGroup;
        if (own) es.History.BeginGroup();
        try
        {
            foreach (var it in entries.Items)
            {
                string key = it.GetScalar("key", null);
                if (string.IsNullOrEmpty(key))
                    throw new RpcError("entry.key bos");
                if (t.Find(key) < 0)
                {
                    var vals = new List<string>();
                    for (int i = 0; i < t.Languages.Count; i++) vals.Add("");
                    es.History.Push(new LocRowOp { Scope = sc, Key = key, Values = vals, Removed = false });
                    LocTableStore.AddRow(sc, key, vals);
                }
                var values = it.Get("values");
                if (values?.Fields != null)
                {
                    foreach (var kv in values.Fields)
                        n += SetCell(es, sc, t, key, kv.Key, kv.Value?.Scalar ?? "");
                }
                else
                    n += SetCell(es, sc, t, key, it.GetScalar("lang", ""), it.GetScalar("value", ""));
            }
        }
        catch
        {
            if (own) es.History.RollbackGroup(es);
            throw;
        }
        if (own) es.History.EndGroup();
        var r = DocNode.Map();
        r.Add("written", DocNode.Scal(n.ToString()));
        return r;
    }

    static int SetCell(EditorScene es, string scope, LocalizationTable t, string key, string lang, string value)
    {
        if (t.LanguageIndex(lang) < 0)
            throw new RpcError($"tabloda olmayan dil: {lang} (loc.addLanguage)");
        string old = LocTableStore.GetValue(scope, key, lang) ?? "";
        if (old == value)
            return 0;
        es.History.Push(new LocCellOp { Scope = scope, Key = key, Lang = lang, OldValue = old, NewValue = value });
        LocTableStore.SetValue(scope, key, lang, value);
        return 1;
    }

    [RpcCommand("loc.remove", "Tablo satirini siler (baglar kalir -> authored metin gorunur; loc.usages ile kontrol et)")]
    static DocNode Remove(RpcContext ctx, string key, string scope = "")
    {
        var t = Require(ctx, scope);
        var e = t.Entry(key) ?? throw new RpcError("key yok: " + key);
        ctx.Scene.History.Push(new LocRowOp { Scope = Scope(ctx, scope), Key = key, Values = new List<string>(e.Values ?? new List<string>()), Removed = true });
        LocTableStore.RemoveRow(Scope(ctx, scope), key);
        return null;
    }

    [RpcCommand("loc.rename", "Key'i yeniden adlandirir: tablo + acik doc (undo'lu) + kapsamdaki diger sahne/prefab dosyalari (metin duzeyi)")]
    static DocNode Rename(RpcContext ctx, string key, string newKey, string scope = "")
    {
        var t = Require(ctx, scope);
        if (t.Find(key) < 0) throw new RpcError("key yok: " + key);
        if (string.IsNullOrEmpty(newKey) || t.Find(newKey) >= 0) throw new RpcError("yeni key bos ya da zaten var: " + newKey);
        string sc = Scope(ctx, scope);
        ctx.Scene.History.Push(new LocRenameOp { Scope = sc, OldKey = key, NewKey = newKey });
        LocTableStore.RenameRow(sc, key, newKey);
        ctx.Scene.RenameLocKey(key, newKey);
        int other = LocTableStore.RenameInOtherScenes(sc, key, newKey, ctx.Scene.Path);
        var r = DocNode.Map();
        r.Add("otherFiles", DocNode.Scal(other.ToString()));
        return r;
    }

    [RpcCommand("loc.key", "Alanin bagi: {key|null, bound, values:{lang: metin}}. comp = component index")]
    static DocNode Key(RpcContext ctx, int go, int comp, string field)
    {
        var g = ctx.Scene.FindGo(go) ?? throw new RpcError("GameObject yok: " + go);
        if (comp < 0 || comp >= g.Components.Count) throw new RpcError("component index gecersiz: " + comp);
        var cd = g.Components[comp];
        string key = EditorScene.LocKeyOf(cd, field);
        var r = DocNode.Map();
        r.Add("key", key == null ? DocNode.Null() : DocNode.Scal(key));
        r.Add("bound", DocNode.Scal(!string.IsNullOrEmpty(key) ? "true" : "false"));
        var t = LocTableStore.Get(Scope(ctx, ""));
        var e = !string.IsNullOrEmpty(key) ? t?.Entry(key) : null;
        if (e != null) r.Add("values", Row(ctx, t, e).Get("values"));
        return r;
    }

    [RpcCommand("loc.bind", "Alani cevirir: key bos = otomatik (ayni metin varsa o satir, yoksa baglamdan key + satir), key verilirse o satira baglar (yoksa acilir). Alan string olmali, metin bos olmamali")]
    static DocNode Bind(RpcContext ctx, int go, int comp, string field, string key = "")
    {
        var g = ctx.Scene.FindGo(go) ?? throw new RpcError("GameObject yok: " + go);
        if (comp < 0 || comp >= g.Components.Count) throw new RpcError("component index gecersiz: " + comp);
        var cd = g.Components[comp];
        var entry = ctx.Catalog.Find(cd.Type) ?? throw new RpcError("tip yok: " + cd.Type);
        var f = SerializedType.Find(entry.Schema, field) ?? throw new RpcError("alan yok: " + field);
        if (f.Kind != SerializedType.Kind.String) throw new RpcError("alan string degil: " + field);
        if (ctx.Scene.Doc.LocTable == null) throw new RpcError("sahnenin kapsami yok (kaydedilmemis/yolsuz sahne)");
        string text = EditorScene.FindProp(cd, f)?.Scalar ?? "";
        if (key.Length == 0)
        {
            if (text.Length == 0) throw new RpcError("metin bos: once prop.set ile metni yaz ya da key ver");
            ctx.Scene.BindLocNew(g, cd, f, text);
        }
        else
        {
            string sc = ctx.Scene.Doc.LocTable;
            var t = LocTableStore.Get(sc, create: true);
            bool own = !ctx.Scene.History.InGroup;
            if (own) ctx.Scene.History.BeginGroup();
            if (t.Find(key) < 0)
            {
                string lang = LocTableStore.EffectiveLanguage(sc);
                var vals = new List<string>();
                for (int i = 0; i < t.Languages.Count; i++)
                    vals.Add(string.Equals(t.Languages[i], lang, StringComparison.OrdinalIgnoreCase) ? text : "");
                LocTableStore.AddRow(sc, key, vals);
                ctx.Scene.SetLocKey(g, cd, field, key, createdKey: key, createdValues: vals);
            }
            else
                ctx.Scene.SetLocKey(g, cd, field, key);
            if (own) ctx.Scene.History.EndGroup();
        }
        return Key(ctx, go, comp, field);
    }

    [RpcCommand("loc.unbind", "Alanin cevirisini BILEREK kapatir (nick gibi): prop '' olur, otomatik bag bir daha acilmaz; metin authored haliyle kalir")]
    static DocNode Unbind(RpcContext ctx, int go, int comp, string field)
    {
        var g = ctx.Scene.FindGo(go) ?? throw new RpcError("GameObject yok: " + go);
        if (comp < 0 || comp >= g.Components.Count) throw new RpcError("component index gecersiz: " + comp);
        ctx.Scene.SetLocKey(g, g.Components[comp], field, "");
        return null;
    }

    [RpcCommand("loc.usages", "Key'in kullanim yerleri (kapsamdaki tum sahne/prefab dosyalari + C# Localize.Get/Format sabitleri; dosya duzeyi)")]
    static DocNode Usages(RpcContext ctx, string key, string scope = "")
    {
        var all = LocAudit.AllUsages(Scope(ctx, scope));
        var s = DocNode.Seq();
        if (all.TryGetValue(key, out var list))
            foreach (var u in list)
            {
                var m = DocNode.Map();
                m.Add("file", DocNode.Scal(u.File));
                m.Add("where", DocNode.Scal(u.Where));
                s.Items.Add(m);
            }
        var r = DocNode.Map();
        r.Add("count", DocNode.Scal(s.Items.Count.ToString()));
        r.Add("usages", s);
        return r;
    }

    [RpcCommand("loc.unused", "Denetim: unused = tabloda olup hicbir dosyada kullanilmayan key'ler; dangling = kullanilip tabloda olmayanlar")]
    static DocNode Unused(RpcContext ctx, string scope = "")
    {
        string sc = Scope(ctx, scope);
        var usages = LocAudit.AllUsages(sc);
        var r = DocNode.Map();
        r.Add("unused", Strs(LocAudit.Unused(sc)));
        r.Add("dangling", Strs(LocAudit.Dangling(sc, usages)));
        return r;
    }

    [RpcCommand("loc.missing", "Dilde bos hucresi olan key'ler + kaynak metinleri (ceviri girdisi): [{key, source}]")]
    static DocNode Missing(RpcContext ctx, string lang, string scope = "")
    {
        var t = Require(ctx, scope);
        if (t.LanguageIndex(lang) < 0) throw new RpcError("tabloda olmayan dil: " + lang);
        var s = DocNode.Seq();
        foreach (var key in LocAudit.Missing(Scope(ctx, scope), lang))
        {
            var m = DocNode.Map();
            m.Add("key", DocNode.Scal(key));
            m.Add("source", DocNode.Scal(t.Raw(key, 0) ?? ""));
            s.Items.Add(m);
        }
        var r = DocNode.Map();
        r.Add("lang", DocNode.Scal(lang));
        r.Add("source", DocNode.Scal(t.SourceLanguage));
        r.Add("count", DocNode.Scal(s.Items.Count.ToString()));
        r.Add("missing", s);
        return r;
    }

    [RpcCommand("loc.glyphCheck", "Tablo metinlerindeki karakterler projedeki fontlarin glyph setinde var mi: font basina eksik codepoint'ler + etkilenen key'ler (charset'i FontImporter ayarindan buyut)")]
    static DocNode GlyphCheck(RpcContext ctx, string scope = "")
    {
        var s = DocNode.Seq();
        foreach (var gap in LocAudit.GlyphCheck(Scope(ctx, scope)))
        {
            var m = DocNode.Map();
            m.Add("font", DocNode.Scal(gap.Font));
            m.Add("missing", DocNode.Scal(LocAudit.CodepointsText(gap.Codepoints, 200)));
            m.Add("count", DocNode.Scal(gap.Codepoints.Count.ToString()));
            m.Add("keys", Strs(gap.Keys));
            s.Items.Add(m);
        }
        var r = DocNode.Map();
        r.Add("ok", DocNode.Scal(s.Items.Count == 0 ? "true" : "false"));
        r.Add("fonts", s);
        return r;
    }
}
