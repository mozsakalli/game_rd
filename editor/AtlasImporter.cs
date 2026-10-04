using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

public enum AtlasFormat
{
    Auto,  // tum uyeler SDF ise R8, degilse RGBA8
    RGBA8,
    R8,    // yalniz SDF uyeler (renkli uye varsa hata)
}

// Atlas grubu TANIMI (asset): hangi klasorler/fontlar girer + pack ayarlari. Saf
// authoring tipi — runtime yalniz bolge tablosunu/sayfalari gorur, o yuzden EDITOR
// assembly'sinde yasar. Tasarimci birden fazla fontu + ikonlari tek sayfaya merge
// edebilir (tek draw); gruba girmeyen font/png kendi tekil dokusunda kalir.
[Serializable]
[CreateAssetMenu(MenuName = "Atlas Group", FileName = "AtlasGroup")]
public class AtlasGroup
{
    public List<string> folders = new();  // Assets'e goreli klasor onekleri ("Textures/UI")
    public List<Font> fonts = new();      // gruba alinan fontlar (glyph'ler sayfaya tasinir)
    public int padding = 2;
    public int maxSize = 2048;            // asilinca otomatik yeni sayfa
    public AtlasFormat format = AtlasFormat.Auto;
    public bool uiPieces = true;          // her sayfaya kutu/golge SDF parcalari (kutu + icerik tek draw)
}

// AtlasGroup .asset -> atlas artifact'i: uyeler (png, font glyph'leri, UiPieces)
// RegionPacker ile sayfalara dizilir. Ciktilar:
//   main    : DATL (sayfa tanimlari + bolge tablosu)
//   pageN   : DPIX sayfa pikselleri
//   members : gruba alinan asset anahtarlari (pak builder bunlarin tekil kopyasini atlar)
// Bagimliliklar (uye png'ler, fontlarin artifact'leri) stamp'e girer: biri degisince
// grup otomatik yeniden paketlenir. Bolge ureticisi importer'lar (font, ileride spine)
// "regions" manifestosu + piksel artifact'iyle katilir — bu importer baska format bilmez.
[AssetImporter(".asset", AssetType = nameof(AtlasGroup), Version = 1)]
public sealed class AtlasImporter : AssetImporter
{
    public const string TypeName = nameof(AtlasGroup);

    static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg" };

    // Grup yaml'ini runtime tipleri yuklemeden okur (font referanslari guid/yol).
    static AtlasGroup ReadGroup(string sourcePath, out List<string> fontPaths)
    {
        var g = new AtlasGroup();
        fontPaths = new List<string>();
        var root = Yaml.Parse(File.ReadAllText(sourcePath));
        var fields = root.Get("fields") ?? root;
        var folders = fields.Get("folders");
        if (folders?.Items != null)
            foreach (var n in folders.Items)
                if (!string.IsNullOrWhiteSpace(n.Scalar))
                    g.folders.Add(n.Scalar.Trim());
        var fonts = fields.Get("fonts");
        if (fonts?.Items != null)
        {
            foreach (var n in fonts.Items)
            {
                if (string.IsNullOrWhiteSpace(n.Scalar))
                    continue;
                string p = App.Assets?.ResolvePath(n.Scalar) ?? n.Scalar;
                if (!fontPaths.Contains(p))
                    fontPaths.Add(p);
            }
        }
        if (int.TryParse(fields.GetScalar("padding", null), out int pad)) g.padding = pad;
        if (int.TryParse(fields.GetScalar("maxSize", null), out int ms)) g.maxSize = ms;
        if (Enum.TryParse(fields.GetScalar("format", null), out AtlasFormat fmt)) g.format = fmt;
        string up = fields.GetScalar("uiPieces", null);
        if (up != null) g.uiPieces = up == "true";
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
        var g = ReadGroup(ctx.SourcePath, out var fontPaths);
        foreach (var m in ImageMembers(g, ctx.AllAssets))
            ctx.DependsOn(m);
        foreach (var f in fontPaths)
            ctx.DependsOn(f);
    }

    public override void Import(ImportContext ctx)
    {
        var g = ReadGroup(ctx.SourcePath, out var fontPaths);
        var sources = new List<RegionSource>();
        var members = new List<string>();
        var errors = new StringBuilder();

        // png/jpg uyeleri: tek bolge, pikseller dogrudan decode (importer'siz kaynak).
        foreach (var rel in ImageMembers(g, App.Assets?.AllAssets.Keys ?? Array.Empty<string>()))
        {
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

        // Bolge ureticileri (font): manifesto + piksel artifact'leri.
        int unit = 0;
        foreach (var fp in fontPaths)
        {
            var mb = ctx.ReadArtifact(fp, "regions");
            var man = mb != null ? RegionManifest.Parse(mb) : null;
            if (man == null)
            {
                errors.Append(fp).Append(" bolge manifestosu yok (import basarisiz?); ");
                continue;
            }
            var pixelBlobs = new Dictionary<string, (byte[] Px, int Ch, int W, int H)>();
            bool ok = true;
            foreach (var e in man.Entries)
            {
                if (!pixelBlobs.ContainsKey(e.Pixels))
                {
                    var blob = ctx.ReadArtifact(fp, e.Pixels);
                    if (!PixelBlob.TryParse(blob, out int ch, out int pw, out int ph))
                    {
                        errors.Append(fp).Append(" piksel artifact'i bozuk: ").Append(e.Pixels).Append("; ");
                        ok = false;
                        break;
                    }
                    var px = new byte[pw * ph * ch];
                    Buffer.BlockCopy(blob, PixelBlob.HeaderSize, px, 0, px.Length);
                    pixelBlobs[e.Pixels] = (px, ch, pw, ph);
                }
                var (pix, pch, _, _) = pixelBlobs[e.Pixels];
                var s = RegionSource.FromBuffer(fp + "#" + e.Sub, e.Kind, pix, pch, pixelBlobs[e.Pixels].W, e.X, e.Y, e.W, e.H);
                s.OffX = e.OffX; s.OffY = e.OffY; s.OrigW = e.OrigW; s.OrigH = e.OrigH;
                s.Pad = Math.Max(e.Pad, g.padding);
                s.Unit = man.SinglePage ? unit : -1;
                sources.Add(s);
            }
            if (ok)
                members.Add(fp);
            unit++;
        }

        int channels = g.format switch
        {
            AtlasFormat.R8 => 1,
            AtlasFormat.RGBA8 => 4,
            _ => RegionPacker.AllSdf(sources) ? 1 : 4,
        };
        if (channels == 1 && !RegionPacker.AllSdf(sources))
        {
            ctx.Fail("format R8 ama renkli uye var (png); Auto veya RGBA8 secin");
            return;
        }

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

        var data = new AtlasData { Channels = channels };
        for (int p = 0; p < pages.Count; p++)
        {
            var page = pages[p];
            data.Pages.Add(new AtlasData.PageInfo
            {
                Width = page.Width, Height = page.Height, PiecesX = page.PiecesX, PiecesY = page.PiecesY,
            });
            foreach (var (src, x, y) in page.Placed)
            {
                data.Regions.Add(new AtlasRegion
                {
                    Name = src.Name, Page = p, X = x, Y = y, W = src.W, H = src.H,
                    OffX = src.OffX, OffY = src.OffY, OrigW = src.OrigW, OrigH = src.OrigH, Kind = src.Kind,
                });
            }
            ctx.AddArtifact(AtlasData.PageArtifact(p), PixelBlob.Build(channels, page.Width, page.Height, page.Pixels));
        }
        ctx.AddArtifact("main", data.Write());
        ctx.AddArtifact("members", Encoding.UTF8.GetBytes(string.Join("\n", members)));

        if (errors.Length > 0)
            EditorLog.Warning($"[atlas] {ctx.AssetPath}: {errors.ToString().TrimEnd(' ', ';')}");
        EditorLog.Info($"[atlas] {ctx.AssetPath}: {data.Regions.Count} bolge, {pages.Count} sayfa ({(channels == 1 ? "R8" : "RGBA8")}, {members.Count} uye)");
    }

    // Pak builder: gruplara alinmis asset'ler (tekil kopyalari paketlenmez).
    public static HashSet<string> ClaimedMembers(IEnumerable<string> atlasAssets)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in atlasAssets)
        {
            var mb = ImportPipeline.GetArtifact(rel, "members");
            if (mb == null)
                continue;
            foreach (var line in Encoding.UTF8.GetString(mb).Split('\n'))
                if (line.Length > 0)
                    set.Add(line.Trim());
        }
        return set;
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
        foreach (var key in loaded)
            if (!groups.Contains(key))
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
