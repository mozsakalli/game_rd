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
[Serializable]
public sealed class FontImportSettings
{
    public string charset = "32-126,160-383"; // ASCII + Latin-1 + Latin Extended-A (Turkce dahil)
    public int sdfSize = 32;                   // SDF taban puntosu (px); buyuk = daha keskin buyuk metin, daha cok alan
}

// TTF -> prebaked SDF font: glyph'ler TEK TEK pisirilir (native stb_truetype), ortak
// paketleyiciyle bir "sheet"e dizilir. Ciktilar:
//   main    : DFNT v3 (metrikler + glyph tablosu + kerning; doku YOK)
//   sheet   : DPIX R8 glyph sayfasi (+ UiPieces bandi) — Standalone: gruba alinmamis font bunu kullanir
//   regions : bolge manifestosu — Editor: AtlasImporter glyph'leri kendi sayfasina tasir
// Pak'a ttf girmez, runtime bake yok.
[AssetImporter(".ttf", ".otf", Version = 6, Settings = typeof(FontImportSettings))]
public sealed unsafe class FontImporter : AssetImporter
{
    const int SheetMaxSize = 4096;
    const int GlyphPad = 1; // SDF bitmap'i kendi 6px pad'ini tasir; bilinear icin 1px yeter

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
        IntPtr font;
        fixed (byte* p = ttf)
            font = Sokol.SdfFontOpen(p, ttf.Length);
        if (font == IntPtr.Zero)
        {
            ctx.Fail("font acilamadi (gecersiz/desteklenmeyen ttf)");
            return;
        }
        try
        {
            Bake(ctx, font, sdfSize, codepoints);
        }
        finally
        {
            Sokol.SdfFontClose(font);
        }
    }

    void Bake(ImportContext ctx, IntPtr font, float sdfSize, List<int> codepoints)
    {
        float* metrics = stackalloc float[5];
        if (Sokol.SdfFontMetrics(font, sdfSize, metrics) == 0)
        {
            ctx.Fail("font metrikleri okunamadi");
            return;
        }

        int n = codepoints.Count;
        var glyphs = new Font.Glyph[n];
        var sources = new List<RegionSource>(n);
        var buf = new byte[1 << 20];
        float* g5 = stackalloc float[5];
        fixed (byte* pBuf = buf)
        {
            for (int i = 0; i < n; i++)
            {
                int cp = codepoints[i];
                if (Sokol.SdfFontGlyph(font, cp, sdfSize, pBuf, buf.Length, g5) == 0)
                {
                    ctx.Fail($"glyph pisirilemedi: U+{cp:X4} (sdfSize {sdfSize} cok buyuk olabilir)");
                    return;
                }
                int w = (int)g5[3], h = (int)g5[4];
                glyphs[i] = new Font.Glyph
                {
                    Codepoint = cp, Advance = g5[0], XOff = g5[1], YOff = g5[2], W = w, H = h,
                };
                if (w > 0 && h > 0)
                {
                    var px = new byte[w * h];
                    Buffer.BlockCopy(buf, 0, px, 0, px.Length);
                    var s = RegionSource.FromBuffer(ctx.AssetPath + "#" + AtlasData.GlyphName(cp),
                        RegionKind.Sdf, px, 1, w, 0, 0, w, h);
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

        var pages = RegionPacker.Pack(sources, SheetMaxSize, 1, uiPieces: true, out string err);
        if (err != null || pages.Count != 1)
        {
            ctx.Fail(err ?? $"glyph seti tek sheet'e sigmadi ({pages.Count} sayfa; charset'i kucultun veya sdfSize'i dusurun)");
            return;
        }
        var page = pages[0];

        // Yerlesimi glyph tablosuna ve manifestoya yaz (ad -> indeks).
        var index = new Dictionary<string, int>(n);
        for (int i = 0; i < n; i++)
            index[ctx.AssetPath + "#" + AtlasData.GlyphName(glyphs[i].Codepoint)] = i;
        var manifest = new RegionManifest { SinglePage = true };
        foreach (var (src, x, y) in page.Placed)
        {
            int gi = index[src.Name];
            glyphs[gi].AtlasX = x;
            glyphs[gi].AtlasY = y;
            manifest.Entries.Add(new RegionManifest.Entry
            {
                Sub = AtlasData.GlyphName(glyphs[gi].Codepoint),
                Pixels = "sheet",
                X = x, Y = y, W = src.W, H = src.H,
                OrigW = src.W, OrigH = src.H,
                Kind = RegionKind.Sdf,
                Pad = GlyphPad,
            });
        }

        ctx.AddArtifact("main", Font.WriteArtifact(metrics[0], metrics[1], metrics[2], metrics[3], metrics[4],
            page.Width, page.Height, page.PiecesX, page.PiecesY, glyphs, kern));
        ctx.AddArtifact("sheet", PixelBlob.Build(1, page.Width, page.Height, page.Pixels), ArtifactScope.Standalone);
        ctx.AddArtifact("regions", manifest.Write(), ArtifactScope.Editor);
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
