using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// RPC fonts.*: font kayit defteri (Assets/Fonts.asset; modul Modules/<ad>/Fonts.asset). Component'ler fonta
// AD ile referans verir ([FontName]); tablo = pak bagimliligi. Yazimlar FontTableStore uzerinden (canli + YAML).
static class RpcFonts
{
    [RpcCommand("fonts.list", "Tanimli font adlari (host + acik sahnenin modul tablosu; modul ezmesi dahil), varsayilan ad, tablo yollari. LayoutBox.font / TextSprite.FontName bu adlardan birini alir ('' = varsayilan)")]
    static DocNode List(RpcContext ctx)
    {
        var r = DocNode.Map();
        string cur = FontTableStore.CurrentScope;
        var host = FontTableStore.Get(FontTableStore.HostScope);
        var mod = cur != FontTableStore.HostScope ? FontTableStore.Get(cur) : null;
        r.Add("host", DocNode.Scal(FontTableStore.HostScope));
        r.Add("hostExists", DocNode.Scal(host != null ? "true" : "false"));
        if (cur != FontTableStore.HostScope)
        {
            r.Add("module", DocNode.Scal(cur));
            r.Add("moduleExists", DocNode.Scal(mod != null ? "true" : "false"));
        }
        r.Add("default", DocNode.Scal(mod?.DefaultFont is { Length: > 0 } md ? md : host?.DefaultFont ?? ""));
        var seq = DocNode.Seq();
        void Add(FontTable t, string scope)
        {
            if (t?.Entries == null) return;
            foreach (var e in t.Entries)
            {
                if (e == null) continue;
                var m = DocNode.Map();
                m.Add("name", DocNode.Scal(e.Name ?? ""));
                m.Add("font", DocNode.Scal(AssetDatabase.KeyOf(e.Font, ctx.Assets)));
                m.Add("scope", DocNode.Scal(scope));
                seq.Items.Add(m);
            }
        }
        Add(mod, cur);
        Add(host, FontTableStore.HostScope);
        r.Add("fonts", seq);
        return r;
    }

    [RpcCommand("fonts.register", "Font asset'ini (yol ya da guid; .ttf/.otf) HOST tablosuna ekler (tablo yoksa olusturur). name bos = dosya adi; makeDefault=true varsayilan yapar. Donus: ad")]
    static DocNode Register(RpcContext ctx, string path, string name = "", bool makeDefault = false)
    {
        var assets = ctx.Assets ?? throw new RpcError("proje acik degil");
        string rel = assets.ResolvePath(path) ?? throw new RpcError("asset yok: " + path);
        string got = FontTableStore.Register(rel) ?? throw new RpcError("font yuklenemedi: " + rel);
        var t = FontTableStore.Get(FontTableStore.HostScope);
        if (!string.IsNullOrEmpty(name) && name != got)
        {
            if (t.Contains(name))
                throw new RpcError("ad zaten tanimli: " + name);
            t.Entries[t.IndexOf(got)].Name = name;
            if (t.DefaultFont == got)
                t.DefaultFont = name;
            got = name;
        }
        if (makeDefault)
            t.DefaultFont = got;
        FontTableStore.Save(FontTableStore.HostScope, t);
        var r = DocNode.Map();
        r.Add("name", DocNode.Scal(got));
        r.Add("default", DocNode.Scal(t.DefaultFont ?? ""));
        return r;
    }
}
