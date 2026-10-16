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
    public List<string> folders = new();  // Assets'e goreli klasor onekleri ("Textures/UI")
    // Sprite ureten asset'ler (Font, ileride Spine): importer'inin "sprites" tablosu sayfaya tasinir.
    // Tip-ozel degil — ISpriteSource tasiyan her [Asset] sinifi suruklenebilir.
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
[AssetImporter(".asset", AssetType = nameof(AtlasGroup), Version = 7)]
public sealed partial class AtlasImporter : AssetImporter
{
    public const string TypeName = nameof(AtlasGroup);

    static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg" };

    // Grup yaml'ini runtime tipleri yuklemeden okur (kaynak referanslari guid/yol).
    static AtlasGroup ReadGroup(string sourcePath, out List<string> sourcePaths)
    {
        var g = new AtlasGroup();
        sourcePaths = new List<string>();
        var root = Yaml.Parse(File.ReadAllText(sourcePath));
        var fields = root.Get("fields") ?? root;
        var folders = fields.Get("folders");
        if (folders?.Items != null)
            foreach (var n in folders.Items)
                if (!string.IsNullOrWhiteSpace(n.Scalar))
                    g.folders.Add(n.Scalar.Trim());
        var sources = fields.Get("sources") ?? fields.Get("fonts"); // "fonts" = eski ad (FormerlySerializedAs)
        if (sources?.Items != null)
        {
            foreach (var n in sources.Items)
            {
                if (string.IsNullOrWhiteSpace(n.Scalar))
                    continue;
                string p = App.Assets?.ResolvePath(n.Scalar) ?? n.Scalar;
                if (!sourcePaths.Contains(p))
                    sourcePaths.Add(p);
            }
        }
        if (int.TryParse(fields.GetScalar("padding", null), out int pad)) g.padding = pad;
        if (int.TryParse(fields.GetScalar("maxSize", null), out int ms)) g.maxSize = ms;
        string up = fields.GetScalar("uiPieces", null);
        if (up != null) g.uiPieces = up == "true";
        string tr = fields.GetScalar("trim", null);
        if (tr != null) g.trim = tr == "true";
        return g;
    }

    public static bool IsImageMember(AtlasGroup g, string rel)
    {
        string ext = Path.GetExtension(rel).ToLowerInvariant();
        if (Array.IndexOf(ImageExts, ext) < 0)
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

    static List<string> ImageMembers(AtlasGroup g, IEnumerable<string> allAssets)
    {
        var list = new List<string>();
        foreach (var rel in allAssets)
            if (IsImageMember(g, rel))
                list.Add(rel);
        list.Sort(StringComparer.OrdinalIgnoreCase); // deterministik yerlesim
        return list;
    }

    public override void CollectDependencies(DependencyContext ctx)
    {
        var g = ReadGroup(ctx.SourcePath, out var sourcePaths);
        foreach (var m in ImageMembers(g, ctx.AllAssets))
            ctx.Absorbs(m); // uyenin pikselleri sayfada yasar; tekil kopyasi pak'a girmez
        foreach (var f in sourcePaths)
            ctx.Absorbs(f); // bolgeler sayfada; kaynagin kendi sheet'i (Standalone) gereksiz
    }

    public override void Import(ImportContext ctx)
    {
        var g = ReadGroup(ctx.SourcePath, out var sourcePaths);
        var sources = new List<RegionSource>();
        var members = new List<string>();
        var errors = new StringBuilder();

        // png/jpg uyeleri: tek bolge, pikseller dogrudan decode (importer'siz kaynak).
        // Build filtresi (IsLive): sahne grafinin erismedigi uye sayfaya girmez.
        foreach (var rel in ImageMembers(g, App.Assets?.AllAssets.Keys ?? Array.Empty<string>()))
        {
            if (!ctx.IsLive(rel))
                continue;
            var rgba = EditorPixels.DecodeFile(ctx.FullPath(rel), out int w, out int h);
            if (rgba == null)
            {
                errors.Append(rel).Append(" decode edilemedi; ");
                continue;
            }
            var s = RegionSource.FromBuffer(rel, RegionKind.Color, rgba, 4, w, 0, 0, w, h);
            s.Pad = Math.Max(0, g.padding);
            sources.Add(s);
            members.Add(rel);
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
            foreach (var e in man.Regions)
            {
                if (e.Page < 0 || e.Page >= man.Pages.Count)
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
