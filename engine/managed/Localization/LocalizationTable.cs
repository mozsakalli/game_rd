using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Ceviri tablosu: [Asset] saf veri, kapsam basina TEK dosya (konvansiyon, elle secilmez):
//   host  -> Assets/Localization.asset
//   modul -> Assets/Modules/<ad>/Localization.asset
// Kapsam = sahne/prefab dosyasinin bulundugu yer (Localize.TableKeyFor). Modul ve host tablo
// PAYLASMAZ; modul klasoru kopyalaninca metinleri de gider. Diller tablo basina sutun listesi;
// Languages[0] = KAYNAK/FALLBACK dil (eksik ceviri buna duser). Satir = key + dil sirasinda degerler.
// Lookup indeksi tembel kurulur ve Entries referansi/sayisi degisince yeniden (ReloadObject yerinde
// alanlari degistirir; editor pencere satir ekler). Sicak yolda cagrilmaz: yalnizca sahne yukleme,
// dil degisimi ve Format(args) aninda.
[Serializable, Asset]
public sealed class LocalizationTable
{
    public List<string> Languages = new() { "en" };
    public List<LocEntry> Entries = new();
    // [Localize] enum kayitlari (editor LocEnumSync yazar): tip kisa adi -> key prefix'i. "<Prefix>.*" satirlari o
    // enum'a AITTIR (elle acilan satir bir sonraki senkronda silinir). Runtime prefix'i buradan okur (attribute okumaz).
    public List<LocEnum> Enums = new();

    [NonSerialized] Dictionary<string, int> _index;
    [NonSerialized] List<LocEntry> _indexedList;
    [NonSerialized] int _indexedCount;

    public string SourceLanguage => Languages != null && Languages.Count > 0 ? Languages[0] : "en";

    // Enum tipinin (kisa ad) prefix'i; kayit yoksa null.
    public string EnumPrefix(string typeName)
    {
        if (Enums == null || string.IsNullOrEmpty(typeName))
            return null;
        for (int i = 0; i < Enums.Count; i++)
            if (Enums[i] != null && string.Equals(Enums[i].Type, typeName, StringComparison.Ordinal))
                return Enums[i].Prefix;
        return null;
    }

    public int LanguageIndex(string code)
    {
        if (string.IsNullOrEmpty(code) || Languages == null)
            return -1;
        for (int i = 0; i < Languages.Count; i++)
            if (string.Equals(Languages[i], code, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // Satir indeksi, yoksa -1.
    public int Find(string key)
    {
        if (string.IsNullOrEmpty(key) || Entries == null)
            return -1;
        EnsureIndex();
        return _index.TryGetValue(key, out int i) ? i : -1;
    }

    public LocEntry Entry(string key)
    {
        int i = Find(key);
        return i < 0 ? null : Entries[i];
    }

    // Dildeki deger; bos/eksikse null (fallback kararini cagiran verir).
    public string Raw(string key, int langIndex)
    {
        int i = Find(key);
        if (i < 0 || langIndex < 0)
            return null;
        var vals = Entries[i].Values;
        if (vals == null || langIndex >= vals.Count)
            return null;
        var s = vals[langIndex];
        return string.IsNullOrEmpty(s) ? null : s;
    }

    // Istenen dil, yoksa kaynak dil, o da yoksa null.
    public string Get(string key, string language)
    {
        int li = LanguageIndex(language);
        var s = li >= 0 ? Raw(key, li) : null;
        return s ?? (li != 0 ? Raw(key, 0) : null);
    }

    // Editor: satiri bul ya da ac (Values dil sayisina tamamlanir).
    public LocEntry GetOrAdd(string key)
    {
        var e = Entry(key);
        if (e != null)
            return e;
        e = new LocEntry { Key = key };
        Entries.Add(e);
        PadValues(e);
        Invalidate();
        return e;
    }

    public void Set(string key, string language, string value)
    {
        int li = LanguageIndex(language);
        if (li < 0)
            return;
        var e = GetOrAdd(key);
        PadValues(e);
        e.Values[li] = value ?? "";
    }

    public bool Remove(string key)
    {
        int i = Find(key);
        if (i < 0)
            return false;
        Entries.RemoveAt(i);
        Invalidate();
        return true;
    }

    public bool Rename(string oldKey, string newKey)
    {
        if (string.IsNullOrEmpty(newKey) || Find(newKey) >= 0)
            return false;
        var e = Entry(oldKey);
        if (e == null)
            return false;
        e.Key = newKey;
        Invalidate();
        return true;
    }

    public void Invalidate() => _index = null;

    void PadValues(LocEntry e)
    {
        e.Values ??= new List<string>();
        while (e.Values.Count < Languages.Count)
            e.Values.Add("");
    }

    void EnsureIndex()
    {
        if (_index != null && ReferenceEquals(_indexedList, Entries) && _indexedCount == Entries.Count)
            return;
        _index = new Dictionary<string, int>(Entries.Count, StringComparer.Ordinal);
        for (int i = 0; i < Entries.Count; i++)
        {
            var k = Entries[i]?.Key;
            if (!string.IsNullOrEmpty(k))
                _index[k] = i; // ayni key iki kez: sonuncu kazanir (editor dogrulamasi uyarir)
        }
        _indexedList = Entries;
        _indexedCount = Entries.Count;
    }
}

[Serializable]
public sealed class LocEntry
{
    public string Key = "";
    public List<string> Values = new(); // LocalizationTable.Languages sirasinda
}

[Serializable]
public sealed class LocEnum
{
    public string Type = "";   // enum kisa adi (namespace'siz; Type.Name ile eslesir)
    public string Prefix = ""; // key prefix'i ("error" -> "error.room_full")
}
