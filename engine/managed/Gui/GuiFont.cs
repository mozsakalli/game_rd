#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.IO;

namespace DigitoyEngine.Editor;

// SDF font (eski imgui_font.c'nin C# hali). Bake native tarafta (de_font_bake,
// stb_truetype): 2048x2048 tek kanal atlas, 32px SDF taban boyu, 224 glyph
// (codepoint 32..255) + tam kerning tablosu. SDF sayesinde tek atlas her punto
// boyunda keskin cizilir; olcum ve konumlama kerning dahil burada yapilir.
public sealed unsafe class GuiFont
{
    public const int FirstChar = 32;
    public const int CharCount = 224;
    public const int AtlasSize = 2048;

    public struct Glyph
    {
        public float Advance, XOff, YOff, W, H, AtlasX, AtlasY;
    }

    readonly Glyph[] _glyphs = new Glyph[CharCount];
    readonly short[] _kern = new short[CharCount * CharCount];

    public Texture Texture { get; private set; }
    public float Ascent { get; private set; }     // SDF piksel uzayinda
    public float Descent { get; private set; }
    public float LineHeight { get; private set; } // SDF piksel uzayinda
    public float KernScale { get; private set; }
    public float SdfSize { get; private set; }

    internal Material Material; // GuiRenderer lazy kurar (SDF shader'li)

    GuiFont() { }

    public static GuiFont Load(string path)
        => File.Exists(path) ? FromMemory(File.ReadAllBytes(path)) : null;

    public static GuiFont FromMemory(byte[] ttf)
    {
        var font = new GuiFont();
        font._ttf = ttf; // raster yolu icin tutulur (glyph'ler ihtiyac aninda)
        byte[] atlas = new byte[AtlasSize * AtlasSize]; // bake sonrasi GC'ye birakilir
        float[] glyphData = new float[CharCount * 9];
        float[] metrics = new float[5];

        int baked;
        fixed (byte* pTtf = ttf)
        fixed (byte* pAtlas = atlas)
        fixed (float* pGlyphs = glyphData)
        fixed (short* pKern = font._kern)
        fixed (float* pMetrics = metrics)
        {
            baked = Sokol.FontBake(pTtf, pAtlas, AtlasSize, pGlyphs, pKern, pMetrics);
            if (baked <= 0)
                return null;
            font.Texture = Texture.FromAlpha(AtlasSize, AtlasSize, pAtlas);
        }

        for (int i = 0; i < CharCount; i++)
        {
            int o = i * 9;
            font._glyphs[i] = new Glyph
            {
                Advance = glyphData[o + 0],
                XOff = glyphData[o + 1],
                YOff = glyphData[o + 2],
                W = glyphData[o + 3],
                H = glyphData[o + 4],
                AtlasX = glyphData[o + 5],
                AtlasY = glyphData[o + 6],
            };
        }
        font.Ascent = metrics[0];
        font.Descent = metrics[1];
        font.LineHeight = metrics[2];
        font.KernScale = metrics[3];
        font.SdfSize = metrics[4];
        return font;
    }

    // Codepoint -> glyph indeksi (kapsam disi -> '?').
    public static int GlyphIndex(char c)
    {
        int cp = c;
        if (cp < FirstChar || cp >= FirstChar + CharCount)
            cp = '?';
        return cp - FirstChar;
    }

    public ref readonly Glyph GlyphAt(int index) => ref _glyphs[index];

    public float Kerning(int left, int right) => _kern[left * CharCount + right] * KernScale;

    public float LineHeightAt(float pixelHeight) => RasterVMetrics(PhysPx(pixelHeight)).y / Scale();

    // Cok satirli metin olcusu — RASTER advencelarla (cizimle birebir: tamsayi ilerleme).
    public Vec2 TextSize(ReadOnlySpan<char> text, float pixelHeight)
    {
        int px = PhysPx(pixelHeight);
        float inv = 1f / Scale();
        var vm = RasterVMetrics(px);
        float maxW = 0, w = 0;
        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                if (w > maxW) maxW = w;
                w = 0;
                lines++;
                continue;
            }
            w += MathF.Round(GetRasterGlyph(px, c).Advance);
        }
        if (w > maxW) maxW = w;
        return new Vec2(maxW * inv, lines * vm.y * inv);
    }

    // --- RASTER (UI) YOLU: fontstash modeli. SDF kucuk puntoda camur; UI metni
    // GERCEK fiziksel piksel boyutunda rasterize edilir, dinamik atlasa ihtiyac
    // aninda eklenir (codepoint bazli — Turkce dahil, sabit aralik yok). ---

    public const int RasterAtlasSize = 1024;

    public struct RGlyph
    {
        public float Advance;
        public float X0, Y0; // bearing (fiziksel px; Y0 baseline'a gore, negatif)
        public int W, H, AtlasX, AtlasY;
    }

    byte[] _ttf;
    readonly Dictionary<long, RGlyph> _rGlyphs = new();
    readonly Dictionary<int, Vec2> _rMetrics = new(); // physPx -> (ascent, lineH)
    byte[] _rAtlas;
    byte[] _rScratch;
    Texture _rTexture;
    int _shelfX, _shelfY, _shelfH;
    bool _rDirty;
    int _lastUpload = -1;

    internal Material RasterMaterial; // GuiRenderer lazy kurar

    public Texture RasterTexture
    {
        get
        {
            EnsureRasterAtlas();
            return _rTexture;
        }
    }

    static float Scale() => Gui.Scale > 0 ? Gui.Scale : 1f;

    public static int PhysPx(float logicalPx)
        => Math.Max(5, (int)MathF.Round(logicalPx * Scale()));

    // x = ascent, y = satir ilerlemesi (fiziksel px, boyut basina bir kez).
    public Vec2 RasterVMetrics(int physPx)
    {
        if (_rMetrics.TryGetValue(physPx, out var m))
            return m;
        float* o3 = stackalloc float[3];
        fixed (byte* p = _ttf)
            Sokol.FontVMetrics(p, physPx, o3);
        m = new Vec2(MathF.Round(o3[0]), MathF.Round(o3[2]));
        _rMetrics[physPx] = m;
        return m;
    }

    public RGlyph GetRasterGlyph(int physPx, char c)
    {
        long key = ((long)physPx << 32) | c;
        if (_rGlyphs.TryGetValue(key, out var g))
            return g;
        EnsureRasterAtlas();

        const int Cap = 160 * 160;
        _rScratch ??= new byte[Cap];
        float* met = stackalloc float[5];
        if (!BakeTriangle(physPx, c, met))
        {
            int ok;
            fixed (byte* pTtf = _ttf)
            fixed (byte* pBuf = _rScratch)
                ok = Sokol.FontGlyph(pTtf, physPx, c, pBuf, Cap, met);

            if (ok == 0 || (met[0] <= 0f && met[3] <= 0f))
            {
                // font glyph'i icermiyor: '?' fallback (kendisi '?' degilse).
                if (c != '?')
                {
                    g = GetRasterGlyph(physPx, '?');
                    _rGlyphs[key] = g;
                    return g;
                }
                g = default;
                _rGlyphs[key] = g;
                return g;
            }
        }

        int w = (int)met[3], h = (int)met[4];
        int ax = 0, ay = 0;
        if (w > 0 && h > 0)
        {
            if (_shelfX + w + 1 > RasterAtlasSize)
            {
                _shelfY += _shelfH + 1;
                _shelfX = 0;
                _shelfH = 0;
            }
            if (_shelfY + h + 1 > RasterAtlasSize)
            {
                // Atlas dolu (cok nadir): bastan basla, cache sifirla — glyph'ler yeniden dolar.
                _shelfX = 0; _shelfY = 0; _shelfH = 0;
                _rGlyphs.Clear();
                Array.Clear(_rAtlas);
            }
            ax = _shelfX;
            ay = _shelfY;
            for (int row = 0; row < h; row++)
                Array.Copy(_rScratch, row * w, _rAtlas, (ay + row) * RasterAtlasSize + ax, w);
            _shelfX += w + 1;
            if (h > _shelfH) _shelfH = h;
            _rDirty = true;
        }
        g = new RGlyph
        {
            Advance = met[0],
            X0 = met[1],
            Y0 = met[2],
            W = w,
            H = h,
            AtlasX = ax,
            AtlasY = ay,
        };
        _rGlyphs[key] = g;
        return g;
    }

    // Ucgen ok glyph'leri (U+25B2..U+25C4 alt kumesi) PROSEDUREL pisirilir —
    // UI fontlarinin cogunda yok, .notdef karesi cikiyordu. Ayni raster atlasa
    // girer: draw call artmaz, DrawText her yerde ayni yoldan cizer.
    bool BakeTriangle(int physPx, char c, float* met)
    {
        int dir = c switch
        {
            '\u25B2' or '\u25B4' => 0, // yukari
            '\u25BC' or '\u25BE' => 1, // asagi
            '\u25B6' or '\u25B8' or '\u25BA' => 2, // sag
            '\u25C0' or '\u25C2' or '\u25C4' => 3, // sol
            _ => -1,
        };
        if (dir < 0)
            return false;

        int b = Math.Max(6, (int)MathF.Round(physPx * 0.5f));  // taban
        int t = Math.Max(4, (int)MathF.Round(b * 0.6f));       // sivri eksen
        int w = dir >= 2 ? t : b;
        int h = dir >= 2 ? b : t;

        // 4x4 supersample kapsama (bir kez, cache'lenir).
        for (int py = 0; py < h; py++)
            for (int pxl = 0; pxl < w; pxl++)
            {
                int hit = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                    {
                        float x = (pxl + (sx + 0.5f) * 0.25f) / w;
                        float yn = (py + (sy + 0.5f) * 0.25f) / h;
                        // u = taban ekseni (0..1), v = tabandan sivri uca (0..1).
                        float u = dir >= 2 ? yn : x;
                        float v = dir == 0 ? 1f - yn : dir == 1 ? yn : dir == 2 ? x : 1f - x;
                        if (MathF.Abs(u - 0.5f) * 2f <= 1f - v)
                            hit++;
                    }
                _rScratch[py * w + pxl] = (byte)(hit * 255 / 16);
            }

        float ascent = RasterVMetrics(physPx).x;
        met[0] = w + 2;                                      // advance
        met[1] = 1;                                          // x0
        met[2] = -MathF.Round((ascent + h) * 0.5f);          // y0: cap ortasina hizali
        met[3] = w;
        met[4] = h;
        return true;
    }

    void EnsureRasterAtlas()
    {
        if (_rAtlas != null)
            return;
        _rAtlas = new byte[RasterAtlasSize * RasterAtlasSize];
        _rTexture = Texture.CreateDynamicAlpha(RasterAtlasSize, RasterAtlasSize);
    }

    // Frame'de BIR KEZ: kirliyse GPU'ya yukle (host tum GUI pass'lerinden sonra cagirir;
    // sokol dinamik image'i frame'de bir kez guncellemeye izin verir).
    public void FlushRasterAtlas()
    {
        if (!_rDirty || _rTexture == null || _lastUpload == Time.frameCount)
            return;
        fixed (byte* p = _rAtlas)
            _rTexture.UpdateAlpha(p);
        _rDirty = false;
        _lastUpload = Time.frameCount;
    }
}
#endif
