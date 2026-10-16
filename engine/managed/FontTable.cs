using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Font kayit defteri: [Asset] saf veri, kapsam basina TEK dosya (konvansiyon, elle secilmez):
//   host  -> Assets/Fonts.asset
//   modul -> Assets/Modules/<ad>/Fonts.asset   (opsiyonel; host'u MIRAS alir, ayni ad varsa EZER)
// Component'ler Font asset'ine DOGRUDAN referans vermez; yalniz burada tanimli bir AD tutar
// ([FontName] string alan, "" = varsayilan). Bagimlilik takibi bu tablodan yurur: tabloda listelenen
// fontlar pak'a girer, listelenmeyen kirpilir. Proje boyunca sabit bir font seti varsayimi —
// yeni yuz gerekince buraya bir satir eklenir. Cozumleme Fonts.Resolve ile; component sonucu
// (ad, surum) ile cache'ler, sicak yolda tarama yok.
[Serializable, Asset]
public sealed class FontTable
{
    public List<FontEntry> Entries = new();
    public string DefaultFont = ""; // Entries icinden ad; bos = ilk giris

    public Font Find(string name)
    {
        if (string.IsNullOrEmpty(name) || Entries == null)
            return null;
        for (int i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            if (e != null && string.Equals(e.Name, name, StringComparison.Ordinal))
                return e.Font;
        }
        return null;
    }

    public bool Contains(string name) => IndexOf(name) >= 0;

    public int IndexOf(string name)
    {
        if (string.IsNullOrEmpty(name) || Entries == null)
            return -1;
        for (int i = 0; i < Entries.Count; i++)
            if (Entries[i] != null && string.Equals(Entries[i].Name, name, StringComparison.Ordinal))
                return i;
        return -1;
    }

    // Varsayilan font: DefaultFont adi, yoksa ilk dolu giris.
    public Font Default
    {
        get
        {
            var f = Find(DefaultFont);
            if (f != null)
                return f;
            if (Entries != null)
                for (int i = 0; i < Entries.Count; i++)
                    if (Entries[i]?.Font != null)
                        return Entries[i].Font;
            return null;
        }
    }
}

[Serializable]
public sealed class FontEntry
{
    public string Name = "";
    public Font Font;
}

// Ad -> Font cozumleyici. Tablolar sahne spawn'inda yuklenir (Load): host + sahnenin modul kapsami.
// Cozum sirasi: en son yuklenen modul tablosu once (ezme), en sonda host. Hicbir tablo yoksa
// UiPieces.DefaultFont (ilk yuklenen font) son care — tablo kurulmamis prova projeleri bos kalmasin.
public static class Fonts
{
    public const string TableFile = "Fonts.asset";

    // Localization kapsam anahtarindan ("Localization.asset" | "Modules/<ad>/Localization.asset") font
    // tablosu anahtari; null -> host.
    public static string TableKeyFor(string locScope)
    {
        if (string.IsNullOrEmpty(locScope))
            return TableFile;
        int slash = locScope.LastIndexOf('/');
        return slash < 0 ? TableFile : locScope.Substring(0, slash + 1) + TableFile;
    }

    // Her tablo yukleme/kayit/degisiminde artar; tuketiciler (LayoutBox/TextSprite) bununla cache tazeler.
    public static int Version { get; private set; }

    static readonly Dictionary<string, FontTable> _registered = new(StringComparer.Ordinal);
    static FontTable _host;
    static readonly List<FontTable> _modules = new();

    public static FontTable Host => _host;
    public static IReadOnlyList<FontTable> Modules => _modules;

    // Acik kayit: AssetDatabase olmadan tablo (testler). table null = kaydi sil.
    public static void RegisterTable(string key, FontTable table)
    {
        if (string.IsNullOrEmpty(key))
            return;
        if (table == null)
        {
            if (_registered.TryGetValue(key, out var old))
                Forget(old);
            _registered.Remove(key);
        }
        else
        {
            _registered[key] = table;
            Attach(key, table);
        }
        Version++;
    }

    static void Forget(FontTable t)
    {
        if (ReferenceEquals(_host, t))
            _host = null;
        _modules.Remove(t);
    }

    static void Attach(string key, FontTable t)
    {
        if (key == TableFile)
        {
            if (!ReferenceEquals(_host, t)) { _host = t; Version++; }
            return;
        }
        if (!_modules.Contains(t))
        {
            _modules.Add(t);
            Version++;
        }
    }

    // Kapsam tablosu: kayitli degilse assets'ten [Asset] nesne olarak (pak'ta DOBJ, editorde YAML). Yoksa null.
    public static FontTable Table(string key, AssetDatabase assets)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        if (!_registered.TryGetValue(key, out var t))
        {
            if (assets == null || !assets.Exists(key))
                return null;
            t = assets.LoadObject(key, typeof(FontTable)) as FontTable;
        }
        if (t != null)
            Attach(key, t);
        return t;
    }

    // Sahne spawn'i: host tablosu + (varsa) sahnenin modul tablosu. locScope = SceneDoc.LocTable.
    public static void Load(string locScope, AssetDatabase assets)
    {
        Table(TableFile, assets);
        string k = TableKeyFor(locScope);
        if (k != TableFile)
            Table(k, assets);
    }

    // Editor: tablo icerigi degisti (satir eklendi/silindi/varsayilan degisti).
    public static void Touch() => Version++;

    // Ad -> Font. "" = varsayilan. Modul (son yuklenen once) -> host -> ilk yuklenen font.
    // Tanimsiz ad varsayilana duser (metin kaybolmaz; yanlis yuz gozle gorulur).
    public static Font Resolve(string name)
    {
        bool def = string.IsNullOrEmpty(name);
        for (int i = _modules.Count - 1; i >= 0; i--)
        {
            var f = def ? _modules[i].Default : _modules[i].Find(name);
            if (f != null)
                return f;
        }
        if (_host != null)
        {
            var f = def ? _host.Default : _host.Find(name);
            if (f != null)
                return f;
        }
        return def ? UiPieces.DefaultFont : Resolve(null);
    }

    public static Font Default => Resolve(null);

    // Tanimli adlar (modul ezmeleri dahil, tekil; editor combolari / rpc.guide icin).
    public static void Names(List<string> into)
    {
        into.Clear();
        void Add(FontTable t)
        {
            if (t?.Entries == null) return;
            foreach (var e in t.Entries)
                if (e != null && !string.IsNullOrEmpty(e.Name) && !into.Contains(e.Name))
                    into.Add(e.Name);
        }
        for (int i = _modules.Count - 1; i >= 0; i--)
            Add(_modules[i]);
        Add(_host);
    }
}
