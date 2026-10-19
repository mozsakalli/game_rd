using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Font import ayarlari (.meta "importer:" blogu; Inspector duzenler).
// charset: virgullu liste — "32-126" / "U+00A0-U+017F" araliklari, "U+20AC" tekil
// codepoint, ya da dogrudan karakterler ("ĞğİıŞş€"). ' ' ve '?' her zaman eklenir.
// Tek mod: MTSDF (RGBA sheet; rgb msdf keskin kose, a gercek sdf — golge/kontur/kalinlik oradan).
// Tek kanal SDF yolu KALDIRILDI (MSDF kadar net degil; tum sayfalar RGBA, tek shader).
// kerning: ozel ciftler (fontunkini EZER, yoksa ekler) — deger em‰ (1000 = 1 em; sdfSize'dan bagimsiz).
// customGlyphs: SVG'den pisirilen glyph'ler (ikon/ozel sembol/eksik harf); ayni codepoint TTF'de varsa ustune yazar.
[Serializable]
public sealed class FontImportSettings
{
    public string charset = "32-126,160-383"; // ASCII + Latin-1 + Latin Extended-A (Turkce dahil)
    public int sdfSize = 32;                   // SDF taban puntosu (px); buyuk = daha keskin buyuk metin, daha cok alan
    public bool useFontKerning = true;         // TTF kern/GPOS ciftleri (kapali = yalniz asagidaki liste)
    public List<KernPair> kerning = new();
    public List<SvgGlyph> customGlyphs = new();
    public List<GlyphOverride> glyphOverrides = new();
}

// Glyph metrik override'i (TTF ve SVG glyph'leri; bake sonrasi uygulanir). advance em (-1 = dokunma);
// offsetX/offsetY em DELTA (0 = dokunma; y yukari) — murekkep kutusunu kaydirir, Ink kenarlari birlikte kayar.
[Serializable]
public sealed class GlyphOverride
{
    public string codepoint = "";
    public float advance = -1f;
    public float offsetX;
    public float offsetY;
}

// Ozel kerning cifti: pair = iki karakter ("AV") ya da bosluklu codepoint'ler ("U+0041 U+0056").
[Serializable]
public sealed class KernPair
{
    public string pair = "";
    public float value;   // em‰: negatif yaklastirir (AV icin -60 gibi)
}

// SVG'den glyph: viewBox yuksekligi * scale = 1 em; baseline = viewBox alt kenari; offset'ler em (y yukari).
// advance < 0 = SVG genisliginden (scale'li). Yalniz FILL pisirilir (stroke/gradient yok; nonzero).
[Serializable]
public sealed class SvgGlyph
{
    public string codepoint = "";  // "U+E001" / "E001" / tek karakter
    public string svg = "";        // Assets'e goreli yol ("Icons/star.svg")
    public float scale = 1f;
    public float advance = -1f;    // em; -1 = otomatik
    public float offsetX;          // em
    public float offsetY;          // em (pozitif yukari)
}

// TTF -> prebaked MTSDF font: glyph'ler TEK TEK pisirilir (msdfgen; metrik/kerning stb_truetype),
// ortak paketleyiciyle bir "sheet"e dizilir. Ciktilar:
//   main    : Font (DOBJ: metrikler + glyph tablosu + kerning; glyph'ler Sprite referansi "x.ttf#U+0041")
//   sheet   : PNG glyph sayfasi RGBA8 (+ UiPieces bandi) — Standalone: gruba alinmamis font bunu kullanir
//   sprites : SpriteTable (DOBJ) — glyph bolgeleri sheet uzerinde; AtlasImporter buradan okuyup kendi
//             sayfasina tasir, runtime (gruba alinmamis font) buradan baglar. Standalone.
//   report  : Editor-only metin (eksik codepoint'ler, custom glyph'ler, uyarilar, doluluk) — Inspector onizlemesi okur.
// Pak'a ttf girmez, runtime bake yok.
[AssetImporter(".ttf", ".otf", Version = 19, Settings = typeof(FontImportSettings))]
public sealed unsafe partial class FontImporter : AssetImporter
{
    const int SheetMaxSize = 4096;
    const int GlyphPad = 1; // SDF bitmap'i kendi 6px pad'ini tasir; bilinear icin 1px yeter

    // Shim bake sabitleri (sokol_shim.c DE_FONT_SDF_PAD / DE_FONT_SDF_SPREAD): Msdf yolu
    // ayni kutu ve deger uzayini uretsin diye buradan verilir.
    const int BakePad = 6;
    const float BakePixelDistScale = 21f;
    const double MsdfAngleThreshold = 3.0; // msdfgen onerisi (radyan)

    // Custom glyph SVG'leri stamp'e girer: svg degisince font yeniden piser.
    public override void CollectDependencies(DependencyContext ctx)
    {
        if (ctx.Settings is not FontImportSettings s)
            return;
        foreach (var g in s.customGlyphs)
            if (!string.IsNullOrWhiteSpace(g?.svg))
                ctx.DependsOn(g.svg.Trim().Replace('\\', '/'));
    }

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
        IntPtr font, msdf;
        fixed (byte* p = ttf)
        {
            font = Sokol.SdfFontOpen(p, ttf.Length); // metrik + kerning (stb_truetype)
            msdf = font != IntPtr.Zero ? Sokol.MsdfFontOpen(p, ttf.Length) : IntPtr.Zero;
        }
        if (font == IntPtr.Zero || msdf == IntPtr.Zero)
        {
            if (font != IntPtr.Zero)
                Sokol.SdfFontClose(font);
            ctx.Fail("font acilamadi (gecersiz/desteklenmeyen ttf)");
            return;
        }
        try
        {
            Bake(ctx, settings, font, msdf, sdfSize, codepoints, ReadUnitsPerEm(ttf));
        }
        finally
        {
            Sokol.SdfFontClose(font);
            Sokol.MsdfFontClose(msdf);
        }
    }

    // 'head' tablosundan unitsPerEm (ofset 18, uint16 BE). stb'nin bake olcegi satir kutusuna (ascent-descent)
    // gore; em'i bilmek icin tek eksik bu. Bulunamazsa 0 (Font.EmSize=0 -> SdfSize'a dusulur).
    static int ReadUnitsPerEm(byte[] ttf)
    {
        static uint U32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        static ushort U16(byte[] b, int o) => (ushort)(b[o] << 8 | b[o + 1]);
        if (ttf.Length < 12)
            return 0;
        int off = 0;
        if (U32(ttf, 0) == 0x74746366) // 'ttcf': koleksiyonun ilk fontu (stbtt_InitFont offset 0 ile ayni)
        {
            off = (int)U32(ttf, 12);
            if (off < 0 || off + 12 > ttf.Length)
                return 0;
        }
        int numTables = U16(ttf, off + 4);
        for (int i = 0; i < numTables; i++)
        {
            int rec = off + 12 + i * 16;
            if (rec + 16 > ttf.Length)
                return 0;
            if (U32(ttf, rec) != 0x68656164) // 'head'
                continue;
            int tab = (int)U32(ttf, rec + 8);
            return tab >= 0 && tab + 20 <= ttf.Length ? U16(ttf, tab + 18) : 0;
        }
        return 0;
    }

    void Bake(ImportContext ctx, FontImportSettings settings, IntPtr font, IntPtr msdf, float sdfSize, List<int> codepoints, int unitsPerEm)
    {
        float* metrics = stackalloc float[5];
        if (Sokol.SdfFontMetrics(font, sdfSize, metrics) == 0)
        {
            ctx.Fail("font metrikleri okunamadi");
            return;
        }

        var report = new StringBuilder();
        var missing = new List<int>();
        var customCps = new List<int>();

        // Custom glyph'ler: codepoint listesine katilir (zaten varsa TTF'inkinin ustune yazilir).
        var custom = new Dictionary<int, SvgGlyph>();
        foreach (var g in settings.customGlyphs)
        {
            if (g == null || string.IsNullOrWhiteSpace(g.codepoint))
                continue;
            if (!TryGlyphCodepoint(g.codepoint, out int cp))
            {
                report.Append("warn=custom glyph codepoint parse edilemedi: ").Append(g.codepoint).Append('\n');
                continue;
            }
            if (string.IsNullOrWhiteSpace(g.svg))
            {
                report.Append("warn=custom glyph svg bos: U+").Append(cp.ToString("X4")).Append('\n');
                continue;
            }
            custom[cp] = g;
            if (!codepoints.Contains(cp))
                codepoints.Add(cp);
        }
        // Fontta olmayan codepoint'ler ATLANIR (.notdef pisirilmez): runtime GlyphIndex zaten '?' fallback'ine
        // duser; 38 ayni kutu icin atlas/kern yeri harcanmaz. ' ' muaf (advance'i lazim).
        codepoints.RemoveAll(cp =>
        {
            if (custom.ContainsKey(cp) || cp == ' ' || Sokol.MsdfFontHasGlyph(msdf, cp))
                return false;
            missing.Add(cp);
            return true;
        });
        codepoints.Sort();
        missing.Sort();
        if (codepoints.Count == 0)
        {
            ctx.Fail("charset'teki hicbir codepoint fontta yok");
            return;
        }

        const int channels = 4;
        int n = codepoints.Count;
        var glyphs = new Font.Glyph[n];
        var sources = new List<RegionSource>(n);
        // Ayni pikselleri ureten glyph'ler (Latin A / Yunan Α / Kiril А gibi) tek bolgeyi paylasir:
        // hash -> aday listesi, hash esitse byte-byte dogrulanir. Metrikler glyph basina kalir.
        var spriteName = new string[n];
        var dedup = new Dictionary<ulong, List<(byte[] px, int w, int h, string name)>>();
        var shared = new List<int>();
        var buf = new byte[(1 << 20) * channels];
        float* g5 = stackalloc float[5];
        fixed (byte* pBuf = buf)
        {
            for (int i = 0; i < n; i++)
            {
                int cp = codepoints[i];
                int ok;
                if (custom.TryGetValue(cp, out var sg))
                {
                    ok = BakeSvgGlyph(ctx, sg, cp, sdfSize, pBuf, buf.Length, g5, report);
                    if (ok == 0)
                        return; // ctx.Fail icerde
                    customCps.Add(cp);
                }
                else
                {
                    ok = Sokol.MsdfFontGlyph(msdf, cp, sdfSize, BakePad, BakePixelDistScale, MsdfAngleThreshold, pBuf, buf.Length, g5);
                    if (ok == 0)
                    {
                        ctx.Fail($"glyph pisirilemedi: U+{cp:X4} (sdfSize {sdfSize} cok buyuk olabilir)");
                        return;
                    }
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
                    int len = w * h * channels;
                    ulong hash = Fnv1a64(buf.AsSpan(0, len), w, h);
                    string own = ctx.AssetPath + "#" + SpriteTable.GlyphName(cp);
                    if (!dedup.TryGetValue(hash, out var cands))
                        dedup[hash] = cands = new List<(byte[], int, int, string)>(1);
                    string found = null;
                    foreach (var c in cands)
                        if (c.w == w && c.h == h && buf.AsSpan(0, len).SequenceEqual(c.px))
                        {
                            found = c.name;
                            break;
                        }
                    if (found != null)
                    {
                        spriteName[i] = found;
                        shared.Add(cp);
                        continue;
                    }
                    var px = new byte[len];
                    Buffer.BlockCopy(buf, 0, px, 0, px.Length);
                    cands.Add((px, w, h, own));
                    spriteName[i] = own;
                    var s = RegionSource.FromBuffer(own, RegionKind.Sdf, px, channels, w, 0, 0, w, h);
                    s.Pad = GlyphPad;
                    s.Unit = 0;
                    sources.Add(s);
                }
            }
        }

        // Glyph override'lari: advance / konum (em -> SDF px). Charset disi codepoint uyari.
        var overridden = new List<int>();
        var cpIndex = new Dictionary<int, int>(n);
        for (int i = 0; i < n; i++)
            cpIndex[codepoints[i]] = i;
        foreach (var ov in settings.glyphOverrides)
        {
            if (ov == null || string.IsNullOrWhiteSpace(ov.codepoint))
                continue;
            if (!TryGlyphCodepoint(ov.codepoint, out int cp))
            {
                report.Append("warn=glyph override codepoint parse edilemedi: ").Append(ov.codepoint).Append('\n');
                continue;
            }
            if (!cpIndex.TryGetValue(cp, out int gi))
            {
                report.Append("warn=glyph override charset disinda: U+").Append(cp.ToString("X4")).Append('\n');
                continue;
            }
            ref var g = ref glyphs[gi];
            if (ov.advance >= 0)
                g.Advance = ov.advance * sdfSize;
            float dx = ov.offsetX * sdfSize, dy = -ov.offsetY * sdfSize; // bitmap y asagi
            g.XOff += dx;
            g.YOff += dy;
            if (g.W > 0)
            {
                g.InkL += dx;
                g.InkR += dx;
            }
            overridden.Add(cp);
        }

        // Kerning: TTF tablosu (secimli) + ozel ciftler (em‰ -> font-unit; KernScale ile geri carpilir).
        var kern = new short[n * n];
        var cps = codepoints.ToArray();
        if (settings.useFontKerning)
        {
            fixed (int* pc = cps)
            fixed (short* pk = kern)
                Sokol.SdfFontKernTable(font, pc, n, pk);
        }
        float kernScale = metrics[3];
        var indexOf = cpIndex;
        int customPairs = 0;
        foreach (var kp in settings.kerning)
        {
            if (kp == null || string.IsNullOrWhiteSpace(kp.pair))
                continue;
            if (!TryKernPair(kp.pair, out int a, out int b))
            {
                report.Append("warn=kerning cifti parse edilemedi: ").Append(kp.pair).Append('\n');
                continue;
            }
            if (!indexOf.TryGetValue(a, out int ia) || !indexOf.TryGetValue(b, out int ib))
            {
                report.Append("warn=kerning cifti charset disinda: ").Append(kp.pair).Append('\n');
                continue;
            }
            float px = kp.value / 1000f * sdfSize;
            float units = kernScale > 0 ? px / kernScale : 0f;
            kern[ia * n + ib] = (short)Math.Clamp(MathF.Round(units), short.MinValue, short.MaxValue);
            customPairs++;
        }

        var pages = RegionPacker.Pack(sources, SheetMaxSize, channels, uiPieces: true, out string err);
        if (err != null || pages.Count != 1)
        {
            ctx.Fail(err ?? $"glyph seti tek sheet'e sigmadi ({pages.Count} sayfa; charset'i kucultun veya sdfSize'i dusurun)");
            return;
        }
        var page = pages[0];

        // Yerlesimi glyph tablosuna ve sprite tablosuna yaz (bolge adi -> sahibi glyph; paylasanlar sonra baglanir).
        var index = new Dictionary<string, int>(n);
        for (int i = 0; i < n; i++)
            if (spriteName[i] != null && !index.ContainsKey(spriteName[i]))
                index[spriteName[i]] = i;
        var table = new SpriteTable { SinglePage = true };
        table.Pages.Add(new SpriteTable.Page
        {
            Artifact = "sheet",
            Width = page.Width,
            Height = page.Height,
            PiecesX = page.PiecesX,
            PiecesY = page.PiecesY,
        });
        long inkArea = 0;
        foreach (var (src, x, y) in page.Placed)
        {
            int gi = index[src.Name];
            // Glyph sprite'i: serilesirken yalniz anahtari yazilir ("hero.ttf#U+0041"); runtime tablodan baglar.
            glyphs[gi].Sprite = Sprite.FromTexture(null, src.Name);
            inkArea += (long)src.W * src.H;
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
        // Paylasan glyph'ler: sahibinin bolgesine baglanir (ayni ad -> runtime/atlas ayni bolgeyi cozer).
        for (int i = 0; i < n; i++)
            if (spriteName[i] != null && glyphs[i].Sprite == null)
                glyphs[i].Sprite = Sprite.FromTexture(null, spriteName[i]);

        var kernBytes = new byte[kern.Length * 2];
        Buffer.BlockCopy(kern, 0, kernBytes, 0, kernBytes.Length);
        var fontAsset = new Font
        {
            Ascent = metrics[0],
            Descent = metrics[1],
            LineHeight = metrics[2],
            KernScale = metrics[3],
            SdfSize = metrics[4],
            EmSize = unitsPerEm * metrics[3], // metrics[3] = px/font-birimi olcegi
            Glyphs = glyphs,
            Kern = kernBytes,
        };
        ctx.AddArtifact("main", fontAsset);
        ctx.AddArtifact("sheet", page.EncodePng(), ArtifactScope.Standalone);
        ctx.AddArtifact("sprites", table, ArtifactScope.Standalone);
        // Container sozlesmesi: glyph'ler Project panelinde gezilir / Sprite alanina suruklenir (codepoint sirali).
        var subIndex = SubAssetIndex.FromSpriteTable(table, ctx.AssetPath); // AtlasMembers=false: glyph'ler butun olarak 'sources' ile gider
        subIndex.Entries.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        ctx.AddArtifact(SubAssetIndex.ArtifactName, subIndex, ArtifactScope.Editor);

        // Rapor (anahtar=deger satirlari; Inspector onizlemesi okur).
        report.Append("glyphs=").Append(n).Append('\n');
        report.Append("sheet=").Append(page.Width).Append('x').Append(page.Height).Append('\n');
        report.Append("fill=").Append((int)(100.0 * inkArea / ((double)page.Width * page.Height))).Append('\n');
        report.Append("kernPairs=").Append(customPairs).Append('\n');
        report.Append("missing=").Append(JoinCps(missing)).Append('\n');
        report.Append("shared=").Append(JoinCps(shared)).Append('\n');
        report.Append("custom=").Append(JoinCps(customCps)).Append('\n');
        report.Append("overridden=").Append(JoinCps(overridden)).Append('\n');
        ctx.AddArtifact("report", Encoding.UTF8.GetBytes(report.ToString()), ArtifactScope.Editor);
        if (missing.Count > 0)
            EditorLog.Warning($"[font] {ctx.AssetPath}: {missing.Count} codepoint fontta yok (atlandi, '?' fallback): {JoinCps(missing, 8)}");
    }

    // Piksel bufferi icin 64-bit FNV-1a (w/h karistirilir); esitlik byte-byte ayrica dogrulanir.
    static ulong Fnv1a64(ReadOnlySpan<byte> data, int w, int h)
    {
        ulong hash = 14695981039346656037UL;
        hash = (hash ^ (uint)w) * 1099511628211UL;
        hash = (hash ^ (uint)h) * 1099511628211UL;
        foreach (byte b in data)
            hash = (hash ^ b) * 1099511628211UL;
        return hash;
    }

    // SVG glyph: dosya okunur, shim'de msdfgen ile pisirilir. Donus shim kodu (1 ok, 2 bos, 0 hata -> ctx.Fail).
    int BakeSvgGlyph(ImportContext ctx, SvgGlyph sg, int cp, float sdfSize, byte* pBuf, int bufLen, float* g5, StringBuilder report)
    {
        string rel = sg.svg.Trim().Replace('\\', '/');
        string full = ctx.FullPath(rel);
        if (!File.Exists(full))
        {
            ctx.Fail($"custom glyph U+{cp:X4}: svg bulunamadi: {rel}");
            return 0;
        }
        byte[] svg = File.ReadAllBytes(full);
        int ok;
        fixed (byte* ps = svg)
            ok = Sokol.MsdfSvgGlyph(ps, svg.Length, sdfSize, sg.scale <= 0 ? 1f : sg.scale, sg.advance, sg.offsetX, sg.offsetY,
                BakePad, BakePixelDistScale, MsdfAngleThreshold, pBuf, bufLen, g5);
        if (ok == 0)
        {
            ctx.Fail($"custom glyph U+{cp:X4}: svg pisirilemedi ({rel}; parse hatasi ya da scale/sdfSize cok buyuk)");
            return 0;
        }
        if (ok == 2)
            report.Append("warn=custom glyph gorunur fill icermiyor (bosluk pisti): U+").Append(cp.ToString("X4")).Append(' ').Append(rel).Append('\n');
        return ok;
    }

    static string JoinCps(List<int> cps, int max = int.MaxValue)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < cps.Count && i < max; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append("U+").Append(cps[i].ToString("X4"));
        }
        if (cps.Count > max)
            sb.Append(",...");
        return sb.ToString();
    }

    // "U+E001" / "E001" / "0xE001" / tek karakter (surrogate cifti dahil).
    public static bool TryGlyphCodepoint(string s, out int cp)
    {
        s = (s ?? "").Trim();
        cp = 0;
        if (s.Length == 0)
            return false;
        if (TryCodepoint(s, out cp) && s.Length > 1)
            return true;
        if (s.Length <= 2)
        {
            cp = char.IsHighSurrogate(s[0]) && s.Length == 2 ? char.ConvertToUtf32(s[0], s[1]) : s[0];
            return s.Length == 1 || char.IsHighSurrogate(s[0]);
        }
        if (s.Length >= 3 && int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cp))
            return true;
        return false;
    }

    // "AV" (iki karakter) ya da "U+0041 U+0056" / "A U+0056" (bosluk/virgul ayracli iki token).
    public static bool TryKernPair(string s, out int a, out int b)
    {
        a = b = 0;
        s = (s ?? "").Trim();
        var parts = s.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
            return TryGlyphCodepoint(parts[0], out a) && TryGlyphCodepoint(parts[1], out b);
        if (parts.Length == 1)
        {
            string t = parts[0];
            int i = 0;
            a = Next(t, ref i);
            if (i >= t.Length) return false;
            b = Next(t, ref i);
            return i == t.Length;
        }
        return false;

        static int Next(string t, ref int i)
        {
            int c = char.IsHighSurrogate(t[i]) && i + 1 < t.Length ? char.ConvertToUtf32(t[i], t[i + 1]) : t[i];
            i += c > 0xFFFF ? 2 : 1;
            return c;
        }
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

