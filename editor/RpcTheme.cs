using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// RPC theme.*: tema katmanlari (docs/engine-notes.md TEMA). Tema = sahne/prefab dosyasinda isimli override katmani;
// calisilan tema (toolbar combosu) dil gibi: edit projeksiyonu + Play; duzenlemeler o katmana yazilir. Build tek temayla
// (PlayerSettings.activeTheme ya da asset.buildPak/player.build `theme` argumani).
static class RpcTheme
{
    static DocNode Strs(IEnumerable<string> items)
    {
        var s = DocNode.Seq();
        foreach (var i in items) s.Items.Add(DocNode.Scal(i));
        return s;
    }

    [RpcCommand("theme.list", "Calisilan oyunun temalari (PlayerSettings.themes; taban 'default' = bos ad), calisilan tema, build temasi, acik dosyanin katmanlari")]
    static DocNode List(RpcContext ctx)
    {
        var r = DocNode.Map();
        var names = App.ThemeNames();
        names.RemoveAt(0);
        r.Add("themes", Strs(names));
        r.Add("working", DocNode.Scal(SceneDoc.ActiveTheme ?? ""));
        r.Add("build", DocNode.Scal(App.Project.Player.activeTheme ?? ""));
        var doc = ctx.Scene?.ThemeMode == true ? ctx.Scene.Base : ctx.Scene?.Doc;
        if (doc != null)
        {
            var layers = DocNode.Seq();
            foreach (var kv in doc.Themes)
            {
                var m = DocNode.Map();
                m.Add("name", DocNode.Scal(kv.Key));
                m.Add("overrides", DocNode.Scal((kv.Value.Get("overrides")?.Items?.Count ?? 0).ToString()));
                m.Add("removed", DocNode.Scal(kv.Value.GetScalar("removed", "")));
                m.Add("added", DocNode.Scal((kv.Value.Get("added")?.Items?.Count ?? 0).ToString()));
                layers.Items.Add(m);
            }
            r.Add("layers", layers);
        }
        return r;
    }

    [RpcCommand("theme.set", "Calisilan temayi degistirir (toolbar combosu) ve bu baglanti icin BEYAN eder: sonraki mutasyonlar o katmana yazilabilir (tema kapisi, rpc.guide themes). Acik sahne deltaya sikisir, tema uygulanir, prefab cache yenilenir. Bos = default. Undo yigini temizlenir, kaydedilmemis degisiklik korunur")]
    static DocNode Set(RpcContext ctx, string name = "")
    {
        try { App.SetWorkingTheme(name); }
        catch (ArgumentException e) { throw new RpcError(e.Message); }
        RpcHost.DeclareTheme(SceneDoc.ActiveTheme ?? "");
        return List(ctx);
    }

    [RpcCommand("theme.add", "PlayerSettings.themes'e tema adi ekler (serbest metin: xmas, kurban...) ve kaydeder")]
    static DocNode Add(RpcContext ctx, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || string.Equals(name, "default", StringComparison.OrdinalIgnoreCase))
            throw new RpcError("invalid theme name");
        if (!App.Project.Player.themes.Contains(name))
        {
            App.Project.Player.themes.Add(name);
            App.Project.SavePlayerSettings();
        }
        return List(ctx);
    }

    [RpcCommand("theme.remove", "Temayi listeden siler (calisilansa default'a doner). Dosyalardaki katmanlar DURUR (zararsiz); purge=true acik dosyadan katmani da siler (undo yok, kaydetmek gerekir)")]
    static DocNode Remove(RpcContext ctx, string name, bool purge = false)
    {
        if (string.Equals(name, SceneDoc.ActiveTheme, StringComparison.OrdinalIgnoreCase))
            App.SetWorkingTheme("");
        if (App.Project.Player.themes.Remove(name))
            App.Project.SavePlayerSettings();
        if (string.Equals(App.Project.Player.activeTheme, name, StringComparison.OrdinalIgnoreCase))
        {
            App.Project.Player.activeTheme = "";
            App.Project.SavePlayerSettings();
        }
        if (purge && ctx.Scene?.Doc != null)
        {
            ctx.Scene.Doc.Themes.Remove(name);
            ctx.Scene.Base?.Themes.Remove(name);
        }
        return List(ctx);
    }

    [RpcCommand("theme.setBuild", "Build temasini (PlayerSettings.activeTheme) yazar: pak/player bu temayla piser. Bos = default")]
    static DocNode SetBuild(RpcContext ctx, string name = "")
    {
        name = (name ?? "").Trim();
        if (name.Length > 0 && !App.Project.Player.themes.Contains(name))
            throw new RpcError("unknown theme: " + name);
        App.Project.Player.activeTheme = name;
        App.Project.SavePlayerSettings();
        return List(ctx);
    }

    [RpcCommand("theme.check", "Acik dosyanin tema katmanlarinda yetim hedefler (tabanda olmayan node'a override/removed): name bos = tum katmanlar. undeclared: dosyadaki katman adi calisilan oyunun tema listesinde yok (yazim hatasi ya da baska oyunun temasi — zararsiz, bu oyunda atil)")]
    static DocNode Check(RpcContext ctx, string name = "")
    {
        var es = ctx.Scene ?? throw new RpcError("no open scene");
        var doc = es.ThemeMode ? es.Base : es.Doc;
        var r = DocNode.Map();
        var list = DocNode.Seq();
        var undeclared = DocNode.Seq();
        var declared = App.ThemeNames();
        foreach (var kv in doc.Themes)
        {
            if (name.Length > 0 && !string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var o in doc.ThemeOrphans(kv.Key))
                list.Items.Add(DocNode.Scal(kv.Key + ": " + o));
            if (!declared.Contains(kv.Key))
                undeclared.Items.Add(DocNode.Scal(kv.Key));
        }
        r.Add("orphans", list);
        r.Add("undeclared", undeclared);
        r.Add("ok", DocNode.Scal(list.Items.Count == 0 ? "true" : "false"));
        return r;
    }
}
