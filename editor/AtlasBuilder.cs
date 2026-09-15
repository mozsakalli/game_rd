using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Atlas grubu TANIMI (asset): hangi klasorler girer + pack ayarlari. Saf
// authoring tipi — runtime yalniz Sprite baglamalarini gorur, o yuzden EDITOR
// assembly'sinde yasar (motora sizmasin).
[Serializable]
[CreateAssetMenu(MenuName = "Atlas Group", FileName = "AtlasGroup")]
public class AtlasGroup
{
    public List<string> folders = new();  // Assets'e goreli klasor onekleri ("Textures/UI")
    public int padding = 2;
    public int maxSize = 2048;
}

// Atlas uretimi (editor): AtlasGroup asset'lerini bulur, uye png'leri tek dokuya
// paketler (shelf pack, v1 trim/rotate yok) ve uye Sprite'lari YERINDE atlasa
// baglar — andan itibaren SpriteRenderer HER modda atlastan cizer. Uye png
// degisince watcher grubu yeniden paketletir (yukler inince, RepackPending uzerinden).
public static class AtlasBuilder
{
    sealed class Built
    {
        public string AssetPath;       // grubun .asset yolu (Assets'e goreli)
        public AtlasGroup Group;
        public Texture Atlas;
        public readonly List<Sprite> Members = new();
    }

    static readonly List<Built> _built = new();
    static readonly HashSet<string> _repack = new(StringComparer.OrdinalIgnoreCase);

    // Proje acilisinda: tum AtlasGroup asset'leri paketlenir.
    public static void BuildAll()
    {
        foreach (var b in _built)
            Teardown(b);
        _built.Clear();
        var assets = App.Assets;
        if (assets == null)
            return;
        foreach (var kv in assets.AllAssets)
        {
            if (!kv.Key.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                continue;
            string full = System.IO.Path.Combine(assets.Root, kv.Key);
            if (ObjectSerializer.TypeNameOf(full) != nameof(AtlasGroup))
                continue;
            var g = ObjectSerializer.Load<AtlasGroup>(full, assets);
            Pack(kv.Key, g);
        }
    }

    // png/grup degisti: bir sonraki uygun frame'de yeniden paketle (async yukler
    // inince — RepackPending Tick'ten cagrilir).
    public static void MarkDirtyByMember(string relPng)
    {
        foreach (var b in _built)
            if (IsMember(b.Group, relPng))
                _repack.Add(b.AssetPath);
    }

    public static void MarkDirtyGroup(string relAssetPath) => _repack.Add(relAssetPath);

    public static bool IsAtlasGroupAsset(string fullPath)
        => fullPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
        && ObjectSerializer.TypeNameOf(fullPath) == nameof(AtlasGroup);

    public static void Tick()
    {
        if (_repack.Count == 0 || App.Assets == null || App.Assets.PendingCount > 0)
            return; // tum async yukler insin ki taze pikseller paketlensin
        var list = new List<string>(_repack);
        _repack.Clear();
        foreach (var rel in list)
        {
            string full = System.IO.Path.Combine(App.Assets.Root, rel);
            if (!System.IO.File.Exists(full))
            {
                Remove(rel);
                continue;
            }
            var g = ObjectSerializer.Load<AtlasGroup>(full, App.Assets);
            Pack(rel, g);
        }
    }

    static bool IsMember(AtlasGroup g, string rel)
    {
        string ext = System.IO.Path.GetExtension(rel).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg"))
            return false;
        foreach (var f in g.folders)
        {
            if (string.IsNullOrEmpty(f))
                continue;
            string prefix = f.Replace('\\', '/').TrimEnd('/') + "/";
            if (rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static void Pack(string assetPath, AtlasGroup g)
    {
        var assets = App.Assets;
        Remove(assetPath); // eski kayitlar dusur (varsa)

        var members = new List<string>();
        foreach (var kv in assets.AllAssets)
            if (IsMember(g, kv.Key))
                members.Add(kv.Key);
        members.Sort(StringComparer.OrdinalIgnoreCase); // deterministik yerlesim
        if (members.Count == 0)
            return;

        // Kaynaklari yukle ve pikseller inene kadar pompala (editor: kabul edilebilir).
        var texs = new List<Texture>();
        foreach (var m in members)
            texs.Add(assets.LoadTexture(m));
        for (int guard = 0; guard < 2000 && assets.PendingCount > 0; guard++)
        {
            assets.Tick(64);
            System.Threading.Thread.Sleep(1);
        }

        // Shelf pack: yukseklige gore sirali raflar. Alan yetene kadar boyut ikilenir.
        int pad = Math.Max(0, g.padding);
        var order = new List<int>();
        for (int i = 0; i < texs.Count; i++)
            order.Add(i);
        order.Sort((a, b) => texs[b].Height.CompareTo(texs[a].Height));

        int size = 256;
        int[] px = null, py = null;
        while (true)
        {
            px = new int[texs.Count];
            py = new int[texs.Count];
            if (TryPlace(texs, order, size, pad, px, py))
                break;
            size *= 2;
            if (size > Math.Max(256, g.maxSize))
            {
                EditorLog.Error($"atlas '{assetPath}' exceeds maxSize {g.maxSize} ({members.Count} images)");
                return;
            }
        }

        var atlas = Texture.FromColor(size, size, new Color(0, 0, 0, 0));
        atlas.Persistent = true;
        atlas.Name = "atlas:" + assetPath;
        var built = new Built { AssetPath = assetPath, Group = g, Atlas = atlas };
        for (int i = 0; i < texs.Count; i++)
        {
            atlas.BlitFrom(texs[i], px[i], py[i]);
            var spr = assets.LoadSprite(members[i]);
            spr.BindRegion(atlas, px[i], py[i], texs[i].Width, texs[i].Height,
                0, 0, texs[i].Width, texs[i].Height);
            built.Members.Add(spr);
        }
        _built.Add(built);
        EditorLog.Info($"atlas packed: {assetPath} ({members.Count} sprites, {size}x{size})");
    }

    static bool TryPlace(List<Texture> texs, List<int> order, int size, int pad, int[] px, int[] py)
    {
        int x = pad, y = pad, shelfH = 0;
        foreach (int i in order)
        {
            int w = texs[i].Width, h = texs[i].Height;
            if (w + pad * 2 > size || h + pad * 2 > size)
                return false;
            if (x + w + pad > size)
            {
                x = pad;
                y += shelfH + pad;
                shelfH = 0;
            }
            if (y + h + pad > size)
                return false;
            px[i] = x;
            py[i] = y;
            x += w + pad;
            shelfH = Math.Max(shelfH, h);
        }
        return true;
    }

    static void Remove(string assetPath)
    {
        for (int i = _built.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(_built[i].AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                continue;
            Teardown(_built[i]);
            _built.RemoveAt(i);
        }
    }

    static void Teardown(Built b)
    {
        // Uyeler kaynak dokularina geri baglanir (tum-sayfa modu) — canli
        // referanslar atlas'siz da cizilmeye devam eder.
        foreach (var s in b.Members)
            s.BindFull(App.Assets?.LoadTexture(s.Name));
        b.Atlas?.Destroy();
    }
}
