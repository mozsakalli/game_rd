using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Atlas grubu TANIMI (asset): hangi klasorler/sprite kaynaklari girer + pack ayarlari. Saf
// authoring tipi — runtime yalniz bolge tablosunu/sayfalari gorur, o yuzden EDITOR
// assembly'sinde yasar. Tasarimci birden fazla fontu + ikonlari tek sayfaya merge
// edebilir (tek draw); gruba girmeyen kaynak/png kendi tekil dokusunda kalir.
// Sayfalar hep RGBA8 PNG (MTSDF glyph + duz sprite + UiPieces ayni formatta; tek shader).
[Serializable]
[CreateAssetMenu(MenuName = "Atlas Group", FileName = "AtlasGroup")]
public class AtlasGroup
{
    // KURAL uyeligi: Assets'e goreli klasor ("Textures/UI") YA DA container yolu ("Shared/Common.psb") — altindaki
    // goruntu dosyalari + container'larin atlas'a girebilen girisleri (AssetTree.SpriteMembers). Asset referansi degil.
    public List<string> folders = new();
    // UYE uyeligi (tek liste): sprite ureten her sey — png dosyasi, "x.psb#id" katmani (Sprite = kendini ureten
    // kaynak), font / PSB butunu (uretici: tum girisleri). Importer cozdugu tipe gore davranir; tip-ozel kod yok.
    [FormerlySerializedAs("fonts")] public List<ISpriteSource> sources = new();
    public int padding = 2;
    public int maxSize = 2048;            // asilinca otomatik yeni sayfa
    public bool uiPieces = true;          // her sayfaya kutu/golge SDF parcalari (kutu + icerik tek draw)
    public bool trim = true;              // Color bolgelerin seffaf kenarlari sayfaya girmez (mantiksal boyut korunur)
}

// AtlasGroup .asset -> atlas artifact'i: uyeler (png, font glyph'leri, UiPieces)
// RegionPacker ile sayfalara dizilir. Ciktilar:
//   main    : SpriteTable (DOBJ: sayfa tanimlari + bolge tablosu)
//   pageN   : PNG sayfa (RGBA8, flip'li yazim)
// Uyeler CollectDependencies'te Absorbs ile bildirilir: stamp'e girer (biri degisince
// grup otomatik yeniden paketlenir) ve pak builder uyelerin tekil kopyasini atlayip
// uye->grup kenari yazar. Pak build'inde ctx.IsLive filtresi uygulanir: sahne grafinin
// erismedigi uye sayfaya girmez. Bolge ureticisi importer'lar (font, ileride spine)
// "sprites" tablosu + PNG sayfalariyla katilir — bu importer baska format bilmez.
[AssetImporter(".asset", AssetType = nameof(AtlasGroup), Version = 9)]
public sealed partial class AtlasImporter : AssetImporter
{
    public const string TypeName = nameof(AtlasGroup);

    // Grup yaml'ini runtime tipleri yuklemeden okur (referanslar guid/yol; alt oge guid#id -> yol#id). Tek 'sources'
    // listesi cozulen tipe gore ikiye ayrilir: producerPaths (font/PSB butunu: "sprites" tablosu) ve spriteKeys
    // (png ya da "sahip#id": tek bolge). Eski 'fonts'/'sprites' alanlari da okunur (geriye uyum).
    static AtlasGroup ReadGroup(string sourcePath, out List<string> producerPaths, out List<string> spriteKeys)
    {
        var g = new AtlasGroup();
        producerPaths = new List<string>();
        spriteKeys = new List<string>();
        var root = Yaml.Parse(File.ReadAllText(sourcePath));
        var fields = root.Get("fields") ?? root;
        var folders = fields.Get("folders");
        if (folders?.Items != null)
            foreach (var n in folders.Items)
                if (!string.IsNullOrWhiteSpace(n.Scalar))
                    g.folders.Add(n.Scalar.Trim());
        var refs = new List<string>();
        ReadRefs(fields.Get("sources") ?? fields.Get("fonts"), refs);
        ReadRefs(fields.Get("sprites"), refs);
        foreach (var k in refs)
        {
            string owner = SubAssets.Split(k, out string id);
            if (id != null || AssetDatabase.ImportTypeOf(owner) == typeof(Sprite))
                spriteKeys.Add(k);
            else
                producerPaths.Add(k);
        }
        if (int.TryParse(fields.GetScalar("padding", null), out int pad)) g.padding = pad;
        if (int.TryParse(fields.GetScalar("maxSize", null), out int ms)) g.maxSize = ms;
        string up = fields.GetScalar("uiPieces", null);
        if (up != null) g.uiPieces = up == "true";
        string tr = fields.GetScalar("trim", null);
        if (tr != null) g.trim = tr == "true";
        return g;
    }

    static void ReadRefs(DocNode list, List<string> into)
    {
        if (list?.Items == null)
            return;
        foreach (var n in list.Items)
        {
            if (string.IsNullOrWhiteSpace(n.Scalar))
                continue;
            string p = App.Assets?.ResolvePath(n.Scalar) ?? n.Scalar;
            if (!into.Contains(p))
                into.Add(p);
        }
    }

    // Tekil sprite uyeleri: onek uyeligi (klasor / container) + acik 'sprites' listesi. Anahtar = png yolu ya da "sahip#id".
    static List<string> SpriteMembers(AtlasGroup g, List<string> spriteKeys)
    {
        var list = new List<string>();
        if (App.Assets != null)
            AssetTree.SpriteMembers(App.Assets, g.folders, list);
        foreach (var k in spriteKeys)
        {
            string owner = SubAssets.Split(k, out string id);
            bool ok = id == null
                ? AssetDatabase.ImportTypeOf(owner) == typeof(Sprite)
                : SubAssets.TryGetEntry(owner, id, out var e) && e.Type == nameof(Sprite) && !string.IsNullOrEmpty(e.Pixels);
            if (ok && !list.Contains(k))
                list.Add(k);
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    // Pak builder: bu grubun tekil uye anahtarlari (atlas kapsami hesabi icin; sources'taki AtlasMembers container'lar dahil).
    public static List<string> MemberKeys(string rel)
    {
        var g = ReadGroup(ImportPipeline.FullPath(rel), out var sourcePaths, out var spriteKeys);
        var list = SpriteMembers(g, spriteKeys);
        foreach (var fp in sourcePaths)
        {
            var idx = SubAssets.IndexOf(fp);
            if (!idx.AtlasMembers) continue;
            foreach (var e in idx.Entries)
                if (!list.Contains(fp + "#" + e.Id)) list.Add(fp + "#" + e.Id);
        }
        return list;
    }

    public override void CollectDependencies(DependencyContext ctx)
    {
        var g = ReadGroup(ctx.SourcePath, out var sourcePaths, out var spriteKeys);
        var members = SpriteMembers(g, spriteKeys);
        var memberSet = new HashSet<string>(members, StringComparer.Ordinal);
        var owners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in members)
        {
            string owner = SubAssets.Split(m, out string id);
            if (id == null)
                ctx.Absorbs(m); // uyenin pikselleri sayfada yasar; tekil kopyasi pak'a girmez
            else
            {
                owners.Add(owner);
                ctx.Member(m); // alt oge canliligi: biri dussse pak import'u filtreyle kosar
            }
        }
        // Container: TUM girisleri bu grupta ise sahibin kendi sayfasi gereksiz (Absorbs); kismi uyelikte sahip
        // kendi sayfasiyla gelir (DependsOn) — grupta olmayan katmanlar oradan cizilir.
        foreach (var o in owners)
        {
            if (AssetTree.CoversAllMembers(o, memberSet))
                ctx.Absorbs(o);
            else
                ctx.DependsOn(o);
        }
        foreach (var f in sourcePaths)
            ctx.Absorbs(f); // bolgeler sayfada; kaynagin kendi sheet'i (Standalone) gereksiz
    }

    public override void Import(ImportContext ctx)
    {
        var g = ReadGroup(ctx.SourcePath, out var sourcePaths, out var spriteKeys);
        var sources = new List<RegionSource>();
        var members = new List<string>();
        var errors = new StringBuilder();

        // Tekil uyeler: png/jpg (tek bolge, dogrudan decode) ya da container alt ogesi (index'teki piksel artifact'i +
        // dikdortgen; dosya formati bilinmez). Build filtresi (IsLive): sahne grafinin erismedigi uye sayfaya girmez.
        var pixelCache = new Dictionary<string, (byte[] Px, int W, int H)>(StringComparer.Ordinal);
        var memberKeys = SpriteMembers(g, spriteKeys);
        foreach (var key in memberKeys)
        {
            string owner = SubAssets.Split(key, out string id);
            // Canlilik ALT OGE duzeyinde: pak build'inde yalniz referanslanan katman/parca sayfaya girer.
            if (!ctx.IsLive(id == null ? owner : key))
                continue;
            RegionSource s;
            if (id == null)
            {
                var rgba = EditorPixels.DecodeFile(ctx.FullPath(key), out int w, out int h);
                if (rgba == null)
                {
                    errors.Append(key).Append(" decode edilemedi; ");
                    continue;
                }
                s = RegionSource.FromBuffer(key, RegionKind.Color, rgba, 4, w, 0, 0, w, h);
            }
            else
            {
                if (!SubAssets.TryGetEntry(owner, id, out var e))
                {
                    errors.Append(key).Append(" index'te yok; ");
                    continue;
                }
                string pk = owner + "#" + e.Pixels;
                if (!pixelCache.TryGetValue(pk, out var pix))
                {
                    string path = ctx.ArtifactPath(owner, e.Pixels);
                    int pw = 0, ph = 0;
                    var px = path != null ? EditorPixels.DecodeFile(path, out pw, out ph) : null;
                    if (px == null)
                    {
                        errors.Append(key).Append(" piksel artifact'i yok: ").Append(e.Pixels).Append("; ");
                        continue;
                    }
                    pixelCache[pk] = pix = (px, pw, ph);
                }
                int rx = e.W > 0 ? e.X : 0, ry = e.W > 0 ? e.Y : 0, rw = e.W > 0 ? e.W : pix.W, rh = e.H > 0 ? e.H : pix.H;
                s = RegionSource.FromBuffer(key, e.Sdf ? RegionKind.Sdf : RegionKind.Color, pix.Px, 4, pix.W, rx, ry, rw, rh);
            }
            s.Pad = Math.Max(0, g.padding);
            sources.Add(s);
            members.Add(key);
        }

        // Bolge ureticileri (font, spine...): "sprites" tablosu + sayfa piksel artifact'leri.
        int unit = 0;
        foreach (var fp in sourcePaths)
        {
            if (!ctx.IsLive(fp))
                continue;
            var mb = ctx.ReadArtifact(fp, "sprites");
            var man = mb != null && SpriteTable.IsTable(mb) ? ObjectBinary.Read(mb, App.Catalog, App.Assets) as SpriteTable : null;
            if (man == null)
            {
                errors.Append(fp).Append(" sprite tablosu yok (import basarisiz?); ");
                continue;
            }
            var pixelBlobs = new Dictionary<string, (byte[] Px, int W, int H)>();
            bool ok = true;
            bool perEntry = SubAssets.IndexOf(fp).AtlasMembers; // katman/parca ureticisi: bolge bazli canlilik (font: butun)
            foreach (var e in man.Regions)
            {
                if (e.Page < 0 || e.Page >= man.Pages.Count)
                    continue;
                if (perEntry && !ctx.IsLive(e.Name))
                    continue;
                string pixels = man.Pages[e.Page].Artifact;
                if (!pixelBlobs.ContainsKey(pixels))
                {
                    // Sayfa PNG'si flip'li yazildi: decode flip'i geri alir -> bellek = depolama duzeni.
                    string path = ctx.ArtifactPath(fp, pixels);
                    int pw = 0, ph = 0;
                    var px = path != null ? EditorPixels.DecodeFile(path, out pw, out ph) : null;
                    if (px == null)
                    {
                        errors.Append(fp).Append(" sayfa artifact'i bozuk: ").Append(pixels).Append("; ");
                        ok = false;
                        break;
                    }
                    pixelBlobs[pixels] = (px, pw, ph);
                }
                var (pix, pstride, _) = pixelBlobs[pixels];
                var s = RegionSource.FromBuffer(e.Name, e.Kind, pix, 4, pstride, e.X, e.Y, e.W, e.H);
                s.OffX = e.OffX; s.OffY = e.OffY; s.OrigW = e.OrigW; s.OrigH = e.OrigH;
                s.Pad = Math.Max(e.Pad, g.padding);
                s.Unit = man.SinglePage ? unit : -1;
                sources.Add(s);
            }
            if (ok)
                members.Add(fp);
            unit++;
        }

        const int channels = 4;
        long trimmed = g.trim ? RegionPacker.TrimTransparent(sources) : 0; // tum kaynaklar (png, font sayfasi, svg...) ayni gecis
        List<PackedPage> pages;
        try
        {
            pages = RegionPacker.Pack(sources, g.maxSize, channels, g.uiPieces, out string perr);
            if (perr != null)
                errors.Append(perr).Append("; ");
        }
        catch (Exception ex)
        {
            ctx.Fail(ex.Message);
            return;
        }

        var data = new SpriteTable();
        for (int p = 0; p < pages.Count; p++)
        {
            var page = pages[p];
            data.Pages.Add(new SpriteTable.Page
            {
                Artifact = SpriteTable.PageArtifact(p),
                Width = page.Width,
                Height = page.Height,
                PiecesX = page.PiecesX,
                PiecesY = page.PiecesY,
            });
            foreach (var (src, x, y) in page.Placed)
            {
                data.Regions.Add(new SpriteTable.Region
                {
                    Name = src.Name,
                    Page = p,
                    X = x,
                    Y = y,
                    W = src.W,
                    H = src.H,
                    OffX = src.OffX,
                    OffY = src.OffY,
                    OrigW = src.OrigW,
                    OrigH = src.OrigH,
                    Kind = src.Kind,
                    Pad = src.Pad,
                });
            }
            ctx.AddArtifact(SpriteTable.PageArtifact(p), page.EncodePng());
        }
        ctx.AddArtifact("main", data);

        if (errors.Length > 0)
            EditorLog.Warning($"[atlas] {ctx.AssetPath}: {errors.ToString().TrimEnd(' ', ';')}");
        EditorLog.Info($"[atlas] {ctx.AssetPath}: {data.Regions.Count} bolge, {pages.Count} sayfa ({members.Count} uye)" +
            (trimmed > 0 ? $", trim {trimmed / 1024} Kpx atildi" : ""));
    }
}

// Editor tarafinda atlas yasam dongusu: gruplari import eder (stamp kapisi) ve
// AssetDatabase'e yukler/yeniler. Acilista ve asset degisikliklerinde (watcher,
// debounce sonrasi) cagrilir; import kendi piksellerini senkron cozer, frame
// butcesine bagli degil.
public static class AtlasSystem
{
    public static bool IsAtlasGroupAsset(string fullPath)
        => fullPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
        && ObjectSerializer.TypeNameOf(fullPath) == AtlasImporter.TypeName;

    public static List<string> GroupAssets()
    {
        var list = new List<string>();
        var assets = App.Assets;
        if (assets == null)
            return list;
        foreach (var kv in assets.AllAssets)
        {
            if (!kv.Key.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsAtlasGroupAsset(Path.Combine(assets.Root, kv.Key)))
                list.Add(kv.Key);
        }
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    // Tum gruplari gozden gecir: degisen (stamp) yeniden paketlenir ve yeniden
    // baglanir; silinen grup dusurulur (uyeler kaynaklarina doner).
    public static void RefreshAll()
    {
        var assets = App.Assets;
        if (assets == null)
            return;
        var groups = GroupAssets();
        var loaded = new List<string>(assets.LoadedAtlases);
        // Yalniz ATLAS GRUBU tablolari: '#'li anahtar ("x.ttf#sprites") ureticinin OZ tablosudur, grup degil —
        // ona dokunulmaz (bosaltilirsa glyph sprite'lari sayfasiz kalir, metin cizilmez).
        foreach (var key in loaded)
            if (key.IndexOf('#') < 0 && !groups.Contains(key))
                assets.UnloadAtlas(key);
        foreach (var rel in groups)
        {
            string dir = ImportPipeline.Import(rel, force: false, out bool ran);
            bool isLoaded = loaded.Contains(rel);
            if (dir == null)
            {
                if (isLoaded)
                    assets.UnloadAtlas(rel);
                continue;
            }
            if (ran || !isLoaded)
            {
                if (!assets.LoadAtlas(rel))
                    EditorLog.Error($"[atlas] yuklenemedi: {rel}");
            }
        }
    }
}
