using System;
using System.Collections.Generic;
using System.IO;
using DigitoyEngine;

namespace DigitoyEditor;

// Editorun font kayit defteri kapisi (LocTableStore paritesi). Dosyalar KONVANSIYONLA yasar
// (Fonts.TableKeyFor): host Assets/Fonts.asset, modul Assets/Modules/<ad>/Fonts.asset; kullanici dosya
// secmez — ilk kayitta (Font asset'i bir [FontName] alanina birakilinca) burada yaratilir. Yazimlar
// AssetDatabase'in CANLI instance'ina gider (projeksiyon/Play ayni nesneyi gorur), hemen YAML'a yazilir.
public static class FontTableStore
{
    public static string HostScope => Fonts.TableFile;

    // Acik sahnenin kapsami (loc kapsamindan turetilir).
    public static string CurrentScope => Fonts.TableKeyFor(App.EditScene?.LocTableKey);

    public static FontTable Get(string scope, bool create = false)
    {
        var assets = App.Assets;
        if (string.IsNullOrEmpty(scope) || assets == null)
            return null;
        var t = Fonts.Table(scope, assets);
        if (t != null || !create)
            return t;
        t = new FontTable();
        string abs = Path.Combine(assets.Root, scope);
        Directory.CreateDirectory(Path.GetDirectoryName(abs));
        AssetWatcher.NoteSelfWrite(abs);
        ObjectSerializer.Save(t, abs, assets);
        assets.ScanMetas(createMissing: true);
        t = Fonts.Table(scope, assets) ?? t; // AssetDatabase'in canli instance'i
        EditorLog.Info("[fonts] tablo olusturuldu: " + scope);
        return t;
    }

    // Host + acik kapsam tablolarini yukler (combo adlari icin); tanimli adlar (modul ezmesi dahil).
    public static void Names(List<string> into)
    {
        Get(HostScope);
        var cur = CurrentScope;
        if (cur != HostScope)
            Get(cur);
        Fonts.Names(into);
    }

    // Font asset'ini (Assets'e goreli yol) HOST tablosuna kaydeder; ad = dosya adi (uzantisiz), cakisirsa _2.
    // Zaten kayitliysa mevcut adi doner. Ilk giris varsayilan olur.
    public static string Register(string fontPath)
    {
        var assets = App.Assets;
        if (assets == null || string.IsNullOrEmpty(fontPath))
            return null;
        if (assets.LoadAsset(fontPath, typeof(Font)) is not Font font)
            return null;
        var t = Get(HostScope, create: true);
        if (t == null)
            return null;
        foreach (var e in t.Entries)
            if (e != null && ReferenceEquals(e.Font, font))
                return e.Name;
        string baseName = Path.GetFileNameWithoutExtension(fontPath);
        string name = baseName;
        for (int i = 2; t.Contains(name); i++)
            name = baseName + "_" + i;
        t.Entries.Add(new FontEntry { Name = name, Font = font });
        if (string.IsNullOrEmpty(t.DefaultFont))
            t.DefaultFont = name;
        Save(HostScope, t);
        EditorLog.Info($"[fonts] kaydedildi: '{name}' = {fontPath}");
        return name;
    }

    public static void Save(string scope, FontTable t)
    {
        var assets = App.Assets;
        if (assets == null || t == null)
            return;
        string abs = Path.Combine(assets.Root, scope);
        AssetWatcher.NoteSelfWrite(abs);
        ObjectSerializer.Save(t, abs, assets);
        Fonts.Touch();
        AssetDatabase.NoteObjectChanged();
    }

    // Kapsamdan gorunen tum (ad, font, yol) ciftleri: modul ezmeleri once (LocAudit glyph denetimi).
    public static void Visible(string locScope, List<(string Name, Font Font)> into)
    {
        into.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(FontTable t)
        {
            if (t?.Entries == null) return;
            foreach (var e in t.Entries)
                if (e != null && e.Font != null && !string.IsNullOrEmpty(e.Name) && seen.Add(e.Name))
                    into.Add((e.Name, e.Font));
        }
        string k = Fonts.TableKeyFor(locScope);
        if (k != HostScope)
            Add(Get(k));
        Add(Get(HostScope));
    }
}
