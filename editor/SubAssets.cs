using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace DigitoyEditor;

// Container asset erisimi (editor): sahibin "index" artifact'ini (SubAssetIndex) okur ve cache'ler.
// Panel/Inspector/DragDrop/Atlas dosya tipini bilmez — yalniz bu siniftan sorar. Cache, asset
// degisim sayaci (AssetWatcher.AssetChangeVersion) her arttiginda tumden duser (reimport ucuz, nadir).
public static class SubAssets
{
    static readonly Dictionary<string, SubAssetIndex> _cache = new(StringComparer.Ordinal);
    static readonly SubAssetIndex _empty = new();
    static int _seenVersion = -1;

    static void Sync()
    {
        if (_seenVersion == AssetWatcher.AssetChangeVersion)
            return;
        _seenVersion = AssetWatcher.AssetChangeVersion;
        _cache.Clear();
    }

    // Sahibin index'i (yoksa bos tablo; null donmez).
    public static SubAssetIndex IndexOf(string rel)
    {
        if (string.IsNullOrEmpty(rel))
            return _empty;
        Sync();
        if (_cache.TryGetValue(rel, out var idx))
            return idx;
        idx = _empty;
        if (ImportPipeline.ImporterFor(rel) != null)
        {
            var blob = ImportPipeline.PeekArtifact(rel, SubAssetIndex.ArtifactName);
            if (SubAssetIndex.IsIndex(blob) && App.Catalog != null
                && ObjectBinary.Read(blob, App.Catalog, App.Assets) is SubAssetIndex read)
                idx = read;
        }
        _cache[rel] = idx;
        return idx;
    }

    public static bool IsContainer(string rel) => IndexOf(rel).Entries.Count > 0;

    public static bool TryGetEntry(string rel, string id, out SubAssetIndex.Entry entry)
    {
        foreach (var e in IndexOf(rel).Entries)
        {
            if (e.Id == id)
            {
                entry = e;
                return true;
            }
        }
        entry = default;
        return false;
    }

    // AssetDatabase.SubAssetTypeResolver hedefi: alt ogenin runtime tipi (Entry.Type -> Type).
    public static Type TypeOf(string ownerRel, string id)
    {
        if (!TryGetEntry(ownerRel, id, out var e) || string.IsNullOrEmpty(e.Type))
            return null;
        return ResolveTypeName(e.Type);
    }

    static Type ResolveTypeName(string name)
    {
        // Cekirdek IAsset tipleri motor assembly'sinde; [Asset] veri siniflari katalogda.
        var t = typeof(Sprite).Assembly.GetType("DigitoyEngine." + name);
        if (t != null)
            return t;
        return App.Catalog?.Find(name)?.Type;
    }

    // "yol#id" -> (yol, id); '#' yoksa id null.
    public static string Split(string key, out string id)
    {
        int h = key?.IndexOf('#') ?? -1;
        if (h <= 0)
        {
            id = null;
            return key;
        }
        id = key.Substring(h + 1);
        return key.Substring(0, h);
    }
}
