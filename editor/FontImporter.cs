using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Font import ayarlari (.meta "importer:" blogu; Inspector duzenler).
// charset: virgullu liste — "32-126" / "U+00A0-U+017F" araliklari, "U+20AC" tekil
// codepoint, ya da dogrudan karakterler ("ĞğİıŞş€"). ' ' ve '?' her zaman eklenir.
// mode: Sdf = tek kanal (R8 sheet, stb_truetype), Msdf = MTSDF (RGBA sheet; rgb msdf
// keskin kose, a gercek sdf — golge/kontur/kalinlik oradan). Kutu/metrik/deger uzayi
// iki modda da ayni; yalniz sheet formati degisir (shader secimi sayfadan).
public enum FontSdfMode { Sdf, Msdf }

[Serializable]
public sealed class FontImportSettings
{
    public string charset = "32-126,160-383"; // ASCII + Latin-1 + Latin Extended-A (Turkce dahil)
    public int sdfSize = 32;                   // SDF taban puntosu (px); buyuk = daha keskin buyuk metin, daha cok alan
    public FontSdfMode mode = FontSdfMode.Sdf;
}

// TTF -> prebaked SDF font: glyph'ler TEK TEK pisirilir (native stb_truetype ya da
// msdfgen), ortak paketleyiciyle bir "sheet"e dizilir. Ciktilar:
//   main    : Font (DOBJ: metrikler + glyph tablosu + kerning; glyph'ler Sprite referansi "x.ttf#U+0041")
//   sheet   : DPIX glyph sayfasi R8 (Sdf) / RGBA8 (Msdf) (+ UiPieces bandi) — Standalone:
//             gruba alinmamis font bunu kullanir
//   sprites : SpriteTable (DOBJ) — glyph bolgeleri sheet uzerinde; AtlasImporter buradan okuyup kendi
//             sayfasina tasir, runtime (gruba alinmamis font) buradan baglar. Standalone.
// Pak'a ttf girmez, runtime bake yok.
[AssetImporter(".ttf", ".otf", Version = 10, Settings = typeof(FontImportSettings))]
public sealed unsafe class FontImporter : AssetImporter
{
    const int SheetMaxSize = 4096;
    const int GlyphPad = 1; // SDF bitmap'i kendi 6px pad'ini tasir; bilinear icin 1px yeter

    // Shim bake sabitleri (sokol_shim.c DE_FONT_SDF_PAD / DE_FONT_SDF_SPREAD): Msdf yolu
    // ayni kutu ve deger uzayini uretsin diye buradan verilir.
    const int BakePad = 6;
    const float BakePixelDistScale = 21f;
    const double MsdfAngleThreshold = 3.0; // msdfgen onerisi (radyan)

    public override void Import(ImportContext ctx)
    {
        var settings = ctx.Settings as FontImportSettings ?? new FontImportSettings();
        float sdfSize = Math.Clamp(settings.sdfSize, 8, 128);
        var codepoints = ParseCharset(settings.charset);
        if (codepoints.Count == 0)
        {
            ctx.Fail("charset bos");
            return;
        }

        byte[] ttf = File.ReadAllBytes(ctx.SourcePath);
        IntPtr font, msdf = IntPtr.Zero;
        fixed (byte* p = ttf)
        {
            font = Sokol.SdfFontOpen(p, ttf.Length);
            if (font != IntPtr.Zero && settings.mode == FontSdfMode.Msdf)
                msdf = Sokol.MsdfFontOpen(p, ttf.Length);
        }
        if (font == IntPtr.Zero || (settings.mode == FontSdfMode.Msdf && msdf == IntPtr.Zero))
        {
            if (font != IntPtr.Zero)
                Sokol.SdfFontClose(font);
            ctx.Fail("font acilamadi (gecersiz/desteklenmeyen ttf)");
            return;
        }
        try
        {
            Bake(ctx, font, msdf, sdfSize, codepoints);
        }
        finally
        {
            Sokol.SdfFontClose(font);
            if (msdf != IntPtr.Zero)
                Sokol.MsdfFontClose(msdf);
        }
    }

    void Bake(ImportContext ctx, IntPtr font, IntPtr msdf, float sdfSize, List<int> codepoints)
    {
        float* metrics = stackalloc float[5];
        if (Sokol.SdfFontMetrics(font, sdfSize, metrics) == 0)
        {
            ctx.Fail("font metrikleri okunamadi");
            return;
        }

        int channels = msdf != IntPtr.Zero ? 4 : 1;
        int n = codepoints.Count;
        var glyphs = new Font.Glyph[n];
        var sources = new List<RegionSource>(n);
        var buf = new byte[(1 << 20) * channels];
        float* g5 = stackalloc float[5];
        fixed (byte* pBuf = buf)
        {
            for (int i = 0; i < n; i++)
            {
                int cp = codepoints[i];
                int ok = channels == 4
                    ? Sokol.MsdfFontGlyph(msdf, cp, sdfSize, BakePad, BakePixelDistScale, MsdfAngleThreshold, pBuf, buf.Length, g5)
                    : Sokol.SdfFontGlyph(font, cp, sdfSize, pBuf, buf.Length, g5);
                if (ok == 0)
                {
                    ctx.Fail($"glyph pisirilemedi: U+{cp:X4} (sdfSize {sdfSize} cok buyuk olabilir)");
                    return;
                }
                int w = (int)g5[3], h = (int)g5[4];
                InkExtents(buf, w, h, channels, out float inkL, out float inkR);
                glyphs[i] = new Font.Glyph
                {
                    Codepoint = cp,
                    Advance = g5[0],
                    XOff = g5[1],
                    YOff = g5[2],
                    W = w,
                    H = h,
                    InkL = w > 0 ? g5[1] + inkL : 0f,
                    InkR = w > 0 ? g5[1] + inkR : 0f,
                };
                if (w > 0 && h > 0)
                {
                    var px = new byte[w * h * channels];
                    Buffer.BlockCopy(buf, 0, px, 0, px.Length);
                    var s = RegionSource.FromBuffer(ctx.AssetPath + "#" + SpriteTable.GlyphName(cp),
                        RegionKind.Sdf, px, channels, w, 0, 0, w, h);
                    s.Pad = GlyphPad;
                    s.Unit = 0;
                    sources.Add(s);
                }
            }
        }

        var kern = new short[n * n];
        var cps = codepoints.ToArray();
        fixed (int* pc = cps)
        fixed (short* pk = kern)
            Sokol.SdfFontKernTable(font, pc, n, pk);

        var pages = RegionPacker.Pack(sources, SheetMaxSize, channels, uiPieces: true, out string err);
        if (err != null || pages.Count != 1)
        {
            ctx.Fail(err ?? $"glyph seti tek sheet'e sigmadi ({pages.Count} sayfa; charset'i kucultun veya sdfSize'i dusurun)");
            return;
        }
        var page = pages[0];

        // Yerlesimi glyph tablosuna ve sprite tablosuna yaz (ad -> indeks).
        var index = new Dictionary<string, int>(n);
        for (int i = 0; i < n; i++)
            index[ctx.AssetPath + "#" + SpriteTable.GlyphName(glyphs[i].Codepoint)] = i;
        var table = new SpriteTable { Channels = channels, SinglePage = true };
        table.Pages.Add(new SpriteTable.Page
        {
            Artifact = "sheet",
            Width = page.Width,
            Height = page.Height,
            PiecesX = page.PiecesX,
            PiecesY = page.PiecesY,
        });
        foreach (var (src, x, y) in page.Placed)
        {
            int gi = index[src.Name];
            // Glyph sprite'i: serilesirken yalniz anahtari yazilir ("hero.ttf#U+0041"); runtime tablodan baglar.
            glyphs[gi].Sprite = Sprite.FromTexture(null, src.Name);
            table.Regions.Add(new SpriteTable.Region
            {
                Name = src.Name,
                Page = 0,
                X = x,
                Y = y,
                W = src.W,
                H = src.H,
                OrigW = src.W,
                OrigH = src.H,
                Kind = RegionKind.Sdf,
                Pad = GlyphPad,
            });
        }

        var kernBytes = new byte[kern.Length * 2];
        Buffer.BlockCopy(kern, 0, kernBytes, 0, kernBytes.Length);
        var fontAsset = new Font
        {
            Ascent = metrics[0],
            Descent = metrics[1],
            LineHeight = metrics[2],
            KernScale = metrics[3],
            SdfSize = metrics[4],
            Glyphs = glyphs,
            Kern = kernBytes,
        };
        ctx.AddArtifact("main", fontAsset);
        ctx.AddArtifact("sheet", PixelBlob.Build(channels, page.Width, page.Height, page.Pixels), ArtifactScope.Standalone);
        ctx.AddArtifact("sprites", table, ArtifactScope.Standalone);
    }

    // SDF bitmap'inden gorunen murekkebin sol/sag kenari (bitmap px, alt-piksel):
    // kolon maksimumlari uzerinde esik 128'in lineer kesisimi (SDF degeri mesafede
    // lineer; bbox floor/ceil yuvarlamasi ve pad bu sayede elenir). channels=4
    // (MTSDF) ise gercek mesafe A kanalindan okunur.
    static void InkExtents(byte[] sdf, int w, int h, int channels, out float left, out float right)
    {
        left = 0f;
        right = w;
        if (w <= 0 || h <= 0)
            return;
        int off = channels - 1; // R8: 0, RGBA: alpha
        Span<int> colMax = w <= 512 ? stackalloc int[w] : new int[w];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int v = sdf[(y * w + x) * channels + off];
                if (v > colMax[x]) colMax[x] = v;
            }
        int c0 = 0;
        while (c0 < w && colMax[c0] < 128) c0++;
        if (c0 >= w)
        {
            left = right = w * 0.5f; // esigi gecen piksel yok (cok ince glyph)
            return;
        }
        int c1 = w - 1;
        while (c1 > c0 && colMax[c1] < 128) c1--;
        left = c0 > 0
            ? c0 - 0.5f + (128f - colMax[c0 - 1]) / (colMax[c0] - colMax[c0 - 1])
            : 0f;
        right = c1 < w - 1
            ? c1 + 0.5f + (colMax[c1] - 128f) / (colMax[c1] - colMax[c1 + 1])
            : w;
    }

    // Charset metni -> sirali, tekil codepoint listesi (' ' ve '?' garanti).
    public static List<int> ParseCharset(string text)
    {
        var set = new SortedSet<int> { ' ', '?' };
        foreach (var raw in (text ?? "").Split(','))
        {
            string tok = raw.Trim();
            if (tok.Length == 0)
                continue;
            int dash = tok.IndexOf('-', 1);
            if (dash > 0 && TryCodepoint(tok[..dash], out int a) && TryCodepoint(tok[(dash + 1)..], out int b))
            {
                if (b < a) (a, b) = (b, a);
                for (int c = a; c <= b && c - a < 65536; c++)
                    set.Add(c);
                continue;
            }
            if (TryCodepoint(tok, out int single))
            {
                set.Add(single);
                continue;
            }
            for (int i = 0; i < tok.Length; i++)
            {
                int c = char.IsHighSurrogate(tok[i]) && i + 1 < tok.Length
                    ? char.ConvertToUtf32(tok[i], tok[++i]) : tok[i];
                set.Add(c);
            }
        }
        set.RemoveWhere(c => c < 0 || c > 0x10FFFF);
        return new List<int>(set);
    }

    static bool TryCodepoint(string s, out int cp)
    {
        s = s.Trim();
        if (s.StartsWith("U+", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cp);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out cp);
    }
}
