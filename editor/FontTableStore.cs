using System;
using System.Collections.Generic;
using System.IO;
using DigitoyEngine;

namespace DigitoyEditor;

// Editorun font kayit defteri kapisi (LocTableStore paritesi). Dosyalar KONVANSIYONLA yasar
// (Fonts.TableKeyFor): host Assets/Fonts.asset, modul Assets/Modules/<ad>/Fonts.asset; kullanici dosya
// secmez — editor acilisinda (ve yeni modul klasoru belirince) EnsureAll her kapsamin tablosunu yoksa bos yaratir;
// Register de gerekirse ayni yoldan yaratir. Yazimlar
// AssetDatabase'in CANLI instance'ina gider (projeksiyon/Play ayni nesneyi gorur), hemen YAML'a yazilir.
public static class FontTableStore
{
    public static string HostScope => Fonts.TableFile;

    // Acik sahnenin kapsami (loc kapsamindan turetilir).
    public static string CurrentScope => Fonts.TableKeyFor(App.EditScene?.LocTableKey);

    // Projedeki kapsamlar: host + her Assets/Modules/<ad>/ klasoru (LocTableStore.Scopes paritesi).
    public static List<string> Scopes()
    {
        var list = new List<string> { HostScope };
        var root = App.Assets?.Root;
        if (root != null)
        {
            string mods = Path.Combine(root, "Modules");
            if (Directory.Exists(mods))
                foreach (var dir in Directory.GetDirectories(mods))
                    list.Add("Modules/" + Path.GetFileName(dir) + "/" + Fonts.TableFile);
        }
        return list;
    }

    // Editor acilisinda ve yeni modul klasoru belirince: her kapsamin tablosu diskte yoksa BOS yaratilir
    // (modul tablosu host'u cozumde miras alir; kopya yok). Kullanici dosya acmak zorunda kalmaz.
    public static void EnsureAll()
    {
        if (App.Assets == null)
            return;
        foreach (var scope in Scopes())
            Get(scope, create: true);
    }

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
        AssetDatabase.NoteObjectChanged(t); // Fonts.Changed -> kutular yeniden cozer
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
