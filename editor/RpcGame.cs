using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// RPC game.*: proje = ortak Assets + N oyun (docs/plugins.md "Games"). Calisilan oyun (toolbar combosu) = Play/Build/pak
// baglami; ayarlar ProjectSettings/Games/<ad>/PlayerSettings.asset (+ Plugins/). Oyun degisimi = calisma alani degisimi
// (son sahne/tema/cihaz geri gelir, oyun kodu taze ALC ile yeniden yuklenir).
static class RpcGame
{
    static DocNode Strs(IEnumerable<string> items)
    {
        var s = DocNode.Seq();
        foreach (var i in items) s.Items.Add(DocNode.Scal(i));
        return s;
    }

    [RpcCommand("game.list", "Projedeki oyunlar (ProjectSettings/Games/*), calisilan oyun ve ozeti (productName, startScene, scenes, themes)")]
    static DocNode List(RpcContext ctx)
    {
        var p = App.Project;
        var r = DocNode.Map();
        r.Add("working", DocNode.Scal(p.Game));
        var games = DocNode.Seq();
        foreach (var g in p.GameNames())
        {
            var ps = p.PlayerFor(g);
            var m = DocNode.Map();
            m.Add("name", DocNode.Scal(g));
            m.Add("productName", DocNode.Scal(ps.productName ?? ""));
            m.Add("bundleIdentifier", DocNode.Scal(ps.bundleIdentifier ?? ""));
            m.Add("startScene", DocNode.Scal(ps.startScene ?? ""));
            m.Add("scenes", Strs(ps.BuildScenes()));
            m.Add("themes", Strs(ps.themes));
            m.Add("activeTheme", DocNode.Scal(ps.activeTheme ?? ""));
            games.Items.Add(m);
        }
        r.Add("games", games);
        return r;
    }

    [RpcCommand("game.set", "Calisilan oyunu degistirir (toolbar combosu): o oyunun son sahnesi/temasi/cihazi geri gelir, oyun kodu yeniden yuklenir (statikler sifir). Acik sahnede kaydedilmemis degisiklik varsa sahne degismez (uyari)")]
    static DocNode Set(RpcContext ctx, string name)
    {
        try { App.SetWorkingGame(name); }
        catch (ArgumentException e) { throw new RpcError(e.Message); }
        return List(ctx);
    }

    [RpcCommand("game.create", "Yeni oyun: ProjectSettings/Games/<name>/ (copyFrom verilirse o oyunun PlayerSettings + Plugins kopyalanir — kimlik/Display/Build/plugin ayarlari sablon olur; sahne kumesi BOS gelir, Assets kopyalanmaz). Sahneleri sonra Player Settings > Scenes ile ver")]
    static DocNode Create(RpcContext ctx, string name, string copyFrom = "")
    {
        try { App.Project.CreateGame(name, string.IsNullOrEmpty(copyFrom) ? null : copyFrom); }
        catch (ArgumentException e) { throw new RpcError(e.Message); }
        PluginSettingsEditor.Invalidate();
        return List(ctx);
    }

    [RpcCommand("game.remove", "Oyunu klasoruyle siler (son oyun silinemez; calisilan silinirse ilk kalan oyuna gecilir)")]
    static DocNode Remove(RpcContext ctx, string name)
    {
        var p = App.Project;
        bool wasWorking = string.Equals(p.Game, name, StringComparison.OrdinalIgnoreCase);
        try
        {
            if (wasWorking)
            {
                // Calisilan oyun: once baska oyuna gec (editor durumu/ALC), sonra sil.
                foreach (var g in p.GameNames())
                    if (!string.Equals(g, name, StringComparison.OrdinalIgnoreCase)) { App.SetWorkingGame(g); break; }
            }
            p.RemoveGame(name);
        }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { throw new RpcError(e.Message); }
        PluginSettingsEditor.Invalidate();
        return List(ctx);
    }
}
