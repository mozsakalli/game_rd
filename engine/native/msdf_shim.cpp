// ===========================================================================
// msdf_shim.cpp - EDITOR-ONLY native: TTF glyph -> MTSDF (msdfgen) pisirici.
// ---------------------------------------------------------------------------
// Yalniz build_editor.cmd/.sh kaynak listesinde (digitoyengine_native editor
// DLL'i). Player derlemeleri (aotcompiler BuildNativeStaticLib, Android CMake)
// acik kaynak listesi kullanir; msdfgen oraya girmez. FontImporter 'Msdf'
// modunda glyph bitmap'ini buradan alir; metrik/kerning mevcut de_sdf_font_*
// yolundan okunur (ikisi de stbtt_ScaleForPixelHeight -> ayni olcek).
//
// SOZLESME de_sdf_font_glyph ile BIREBIR (ayni kutu, ayni deger uzayi):
//   - kutu: stbtt bitmap box (scale, subpixel 0) + her yone pad
//   - deger: stbtt gibi 128 + d_px * pixelDistScale (0..255 kirpilir). Shim'de
//     pixelDistScale = 21 -> piksel basina 21 birim, tam menzil 255/21 = ~12 px
//     (kenardan ±6 px; pad 6 bununla uyumlu).
//   - satir 0 USTTE (y-down bitmap), xoff/yoff sol-ust kose (kalem orijinine gore)
//   - RGB = msdf (median = kenar), A = gercek signed distance (golge/kontur/
//     kalinlik gibi kenardan uzak okumalar bu kanaldan)
// Cikti RGBA8, w*h*4 bayt.
// ===========================================================================
#define STB_TRUETYPE_IMPLEMENTATION
#define STBTT_STATIC
#include "stb_truetype.h"

// MSDFGEN_PUBLIC derleme satirindan bos tanimlanir (msdfgen-config.h yok).
#include "msdfgen/msdfgen.h"

#include <cstdlib>
#include <cstring>

#if defined(_WIN32)
#define DE_API extern "C" __declspec(dllexport)
#else
#define DE_API extern "C" __attribute__((visibility("default")))
#endif

struct de_msdf_font_t
{
    stbtt_fontinfo info;
    unsigned char *ttf;
};

DE_API void *de_msdf_font_open(const unsigned char *ttf, int length)
{
    if (!ttf || length <= 0)
        return nullptr;
    de_msdf_font_t *f = (de_msdf_font_t *)malloc(sizeof(de_msdf_font_t));
    if (!f)
        return nullptr;
    f->ttf = (unsigned char *)malloc((size_t)length);
    if (!f->ttf)
    {
        free(f);
        return nullptr;
    }
    memcpy(f->ttf, ttf, (size_t)length);
    if (!stbtt_InitFont(&f->info, f->ttf, 0))
    {
        free(f->ttf);
        free(f);
        return nullptr;
    }
    return f;
}

DE_API void de_msdf_font_close(void *handle)
{
    de_msdf_font_t *f = (de_msdf_font_t *)handle;
    if (!f)
        return;
    free(f->ttf);
    free(f);
}

// stbtt outline (font birimi, y-up) -> msdfgen Shape. Konturlar vmove ile baslar;
// vcurve quadratic, vcubic cubic (CFF/OTF). stbtt kapatma vertex'i uretmez:
// kontur sonu baslangica esit degilse kapatma cizgisi eklenir.
static bool de_build_shape(const stbtt_fontinfo *info, int glyphIndex, msdfgen::Shape &shape)
{
    stbtt_vertex *v = nullptr;
    int n = stbtt_GetGlyphShape(info, glyphIndex, &v);
    if (n <= 0 || !v)
    {
        if (v)
            stbtt_FreeShape(info, v);
        return false;
    }
    msdfgen::Contour *contour = nullptr;
    msdfgen::Point2 cur(0, 0), start(0, 0);
    for (int i = 0; i < n; i++)
    {
        const stbtt_vertex &p = v[i];
        msdfgen::Point2 to((double)p.x, (double)p.y);
        switch (p.type)
        {
        case STBTT_vmove:
            if (contour && !contour->edges.empty() && !(cur == start))
                contour->addEdge(msdfgen::EdgeHolder(cur, start));
            contour = &shape.addContour();
            cur = start = to;
            break;
        case STBTT_vline:
            if (contour && !(cur == to))
                contour->addEdge(msdfgen::EdgeHolder(cur, to));
            cur = to;
            break;
        case STBTT_vcurve:
            if (contour)
                contour->addEdge(msdfgen::EdgeHolder(cur, msdfgen::Point2((double)p.cx, (double)p.cy), to));
            cur = to;
            break;
        case STBTT_vcubic:
            if (contour)
                contour->addEdge(msdfgen::EdgeHolder(cur,
                    msdfgen::Point2((double)p.cx, (double)p.cy),
                    msdfgen::Point2((double)p.cx1, (double)p.cy1), to));
            cur = to;
            break;
        default:
            break;
        }
    }
    if (contour && !contour->edges.empty() && !(cur == start))
        contour->addEdge(msdfgen::EdgeHolder(cur, start));
    stbtt_FreeShape(info, v);

    // Bos konturlar (tek vmove) atilir.
    for (size_t i = 0; i < shape.contours.size();)
    {
        if (shape.contours[i].edges.empty())
            shape.contours.erase(shape.contours.begin() + (std::ptrdiff_t)i);
        else
            ++i;
    }
    return !shape.contours.empty();
}

// Donus: 1 ok (w*h*4 bayt buffer'a yazildi; w veya h 0 olabilir: bosluk), 0 hata/sigmadi.
// out5: advance, xoff, yoff, w, h (de_sdf_font_glyph ile ayni).
// pixelDistScale: stbtt_GetGlyphSDF'in ayni adli parametresi (deger birimi / px).
// angleThreshold: kose sayilan min aci (radyan; msdfgen onerisi 3.0).
DE_API int de_msdf_font_glyph(void *handle, int codepoint, float sdfSize, int pad, float pixelDistScale,
                              double angleThreshold, unsigned char *buffer, int bufferSize, float *out5)
{
    de_msdf_font_t *f = (de_msdf_font_t *)handle;
    if (!f || sdfSize <= 0 || pixelDistScale <= 0 || pad < 0)
        return 0;
    float scale = stbtt_ScaleForPixelHeight(&f->info, sdfSize);
    int glyphIndex = stbtt_FindGlyphIndex(&f->info, codepoint);
    int advance = 0, lsb = 0;
    stbtt_GetGlyphHMetrics(&f->info, glyphIndex, &advance, &lsb);
    out5[0] = scale * (float)advance;
    out5[1] = out5[2] = out5[3] = out5[4] = 0;

    if (stbtt_IsGlyphEmpty(&f->info, glyphIndex))
        return 1; // bosluk: w=h=0

    int ix0 = 0, iy0 = 0, ix1 = 0, iy1 = 0;
    stbtt_GetGlyphBitmapBoxSubpixel(&f->info, glyphIndex, scale, scale, 0.0f, 0.0f, &ix0, &iy0, &ix1, &iy1);
    ix0 -= pad; iy0 -= pad; ix1 += pad; iy1 += pad;
    int w = ix1 - ix0, h = iy1 - iy0;
    if (w <= 0 || h <= 0)
        return 1;
    if (w * h * 4 > bufferSize)
        return 0;

    msdfgen::Shape shape;
    if (!de_build_shape(&f->info, glyphIndex, shape))
        return 1; // outline yok (bitmap-only font vb.): bosluk gibi davran
    shape.normalize();
    shape.orientContours(); // ic pozitif: TrueType/CFF yon farki burada esitlenir
    msdfgen::edgeColoringSimple(shape, angleThreshold);

    // stbtt kutusu y-down: ust satir glyph y = -iy0/scale (y-up). msdfgen y-up
    // bitmap uretir; piksel (px, r) merkezi <-> glyph (gx, gy):
    //   px + .5 = gx*scale - ix0   ->  translate.x = -ix0/scale
    //   r  + .5 = gy*scale + iy1   ->  translate.y =  iy1/scale   (r=0 alt satir)
    // Projection: pixel = scale * (coord + translate). Sonra satirlar ters cevrilir.
    msdfgen::Projection projection(msdfgen::Vector2(scale, scale),
                                   msdfgen::Vector2(-ix0 / (double)scale, iy1 / (double)scale));
    // Deger uzayi: msdfgen 0.5 + d/range; stbtt 128/255 + d_px*pds/255 ->
    // range_px = 255/pds (shape birimine: /scale). 128 vs 127.5 farki (0.5 birim) ihmal.
    msdfgen::Range range((255.0 / (double)pixelDistScale) / (double)scale);
    msdfgen::SDFTransformation xf(projection, msdfgen::DistanceMapping(range));
    msdfgen::MSDFGeneratorConfig config(true);

    msdfgen::Bitmap<float, 4> bmp(w, h);
    msdfgen::generateMTSDF(bmp, shape, xf, config);

    for (int y = 0; y < h; y++)
    {
        const float *src = bmp(0, h - 1 - y); // y-up -> y-down
        unsigned char *dst = buffer + (size_t)y * (size_t)w * 4;
        for (int i = 0; i < w * 4; i++)
            dst[i] = msdfgen::pixelFloatToByte(src[i]);
    }
    out5[1] = (float)ix0;
    out5[2] = (float)iy0;
    out5[3] = (float)w;
    out5[4] = (float)h;
    return 1;
}
