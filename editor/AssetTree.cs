using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// ORTAK GEZINME TABANI: OS klasoru ve container asset (index yayan importer) ayni agacta. Yol sozlesmesi:
//   klasor    "a/b"            cocuklari: alt klasorler + dosyalar
//   container "a/b/x.psb"      cocuklari: index girisleri, anahtar "a/b/x.psb#id"
// Project paneli (listeleme) ve AtlasGroup (onek uyeligi) YALNIZ buradan sorar; dosya tipi bilgisi yok.
// Dosya islemleri (create/rename/delete, meta/guid) klasor-ozeldir ve burada DEGILDIR — container salt okunur turevdir.
public static class AssetTree
{
    public enum Kind { Folder, File, Container, Sub }

    public readonly struct Node
    {
        public readonly string Name;   // gorunen ad
        public readonly string Key;    // yol, "yol#id" ya da klasor yolu
        public readonly string RefKey; // guid / "guid#id" (klasor: null)
        public readonly Kind Kind;
        public Node(string name, string key, string refKey, Kind kind) { Name = name; Key = key; RefKey = refKey; Kind = kind; }
        public bool Enterable => Kind is Kind.Folder or Kind.Container;
    }

    public static bool IsContainerPath(AssetDatabase assets, string path)
        => path.Length > 0 && assets.AllAssets.ContainsKey(path) && SubAssets.IsContainer(path);

    public static string Parent(string path)
    {
        string owner = SubAssets.Split(path, out string id);
        if (id != null)
            return owner;
        int cut = owner.LastIndexOf('/');
        return cut > 0 ? owner.Substring(0, cut) : "";
    }

    // Bir yolun dogrudan cocuklari (siralama: klasorler once, sonra ad; container icinde importer sirasi).
    public static void List(AssetDatabase assets, string path, List<Node> into)
    {
        into.Clear();
        if (IsContainerPath(assets, path))
        {
            string ownerGuid = assets.PathToGuid(path);
            foreach (var e in SubAssets.IndexOf(path).Entries)
                into.Add(new Node(e.Name, path + "#" + e.Id, ownerGuid + "#" + e.Id, Kind.Sub));
            return;
        }
        string prefix = path.Length > 0 ? path + "/" : "";
        var dirs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kv in assets.AllAssets)
        {
            string rel = kv.Key;
            if (!rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            string rest = rel.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            if (slash >= 0)
            {
                string sub = rest.Substring(0, slash);
                if (dirs.Add(sub))
                    into.Add(new Node(sub, prefix + sub, null, Kind.Folder));
            }
            else
                into.Add(new Node(rest, rel, kv.Value, SubAssets.IsContainer(rel) ? Kind.Container : Kind.File));
        }
        into.Sort((a, b) => (a.Kind == Kind.Folder) != (b.Kind == Kind.Folder) ? (a.Kind == Kind.Folder ? -1 : 1)
            : string.CompareOrdinal(a.Name, b.Name));
    }

    // ONEK UYELIGI (AtlasGroup.folders): onek altindaki, atlas'a tek tek girebilen Sprite anahtarlari.
    //  - klasor onegi: altindaki goruntu dosyalari (png/jpg/svg...) + altindaki container'larin AtlasMembers girisleri
    //  - container onegi: o container'in AtlasMembers girisleri
    // Font gibi 'AtlasMembers=false' ureticilerin glyph'leri girmez (butun olarak 'sources' ile gider).
    public static void SpriteMembers(AssetDatabase assets, IEnumerable<string> prefixes, List<string> into)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in prefixes)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string p = raw.Replace('\\', '/').Trim().TrimEnd('/');
            if (IsContainerPath(assets, p))
            {
                AddContainerMembers(assets, p, seen, into);
                continue;
            }
            string prefix = p + "/";
            foreach (var kv in assets.AllAssets)
            {
                string rel = kv.Key;
                if (!rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (AssetDatabase.ImportTypeOf(rel) == typeof(Sprite))
                {
                    if (seen.Add(rel)) into.Add(rel);
                }
                else if (SubAssets.IsContainer(rel))
                    AddContainerMembers(assets, rel, seen, into);
            }
        }
        into.Sort(StringComparer.Ordinal); // deterministik yerlesim
    }

    static void AddContainerMembers(AssetDatabase assets, string owner, HashSet<string> seen, List<string> into)
    {
        var idx = SubAssets.IndexOf(owner);
        if (!idx.AtlasMembers)
            return;
        foreach (var e in idx.Entries)
        {
            if (e.Type != nameof(Sprite) || string.IsNullOrEmpty(e.Pixels))
                continue;
            string key = owner + "#" + e.Id;
            if (seen.Add(key)) into.Add(key);
        }
    }

    // Container'in atlas'a girebilen TUM girisleri bu kume icinde mi (sahibin kendi sayfasi gereksiz -> Absorbs)?
    public static bool CoversAllMembers(string owner, HashSet<string> members)
    {
        var idx = SubAssets.IndexOf(owner);
        if (!idx.AtlasMembers || idx.Entries.Count == 0)
            return false;
        foreach (var e in idx.Entries)
            if (e.Type == nameof(Sprite) && !string.IsNullOrEmpty(e.Pixels) && !members.Contains(owner + "#" + e.Id))
                return false;
        return true;
    }
}
