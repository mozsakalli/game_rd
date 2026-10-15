// ---------------------------------------------------------------------------
// de_svg_compile.c — EDITOR-ONLY: SVG metni -> DVEC komut blob'u (de_vector.h bicimi)
// ---------------------------------------------------------------------------
// nanosvg parser'i (XML/CSS/transform/<use> cozumu) yalniz burada derlenir; runtime
// yalniz rasterizer tasir. Olcek import'ta koordinatlara ISLENIR (hedef piksel uzayi);
// gradient ters donusumu ve stroke genisligi de buna gore olceklenir. Koordinatlar
// i16 1/16 px'e sigarsa sabit nokta, sigmazsa f32 (header flags bit0).
// ---------------------------------------------------------------------------
#include <stdlib.h>
#include <string.h>
#include <math.h>

#define NANOSVG_IMPLEMENTATION
#define NANOSVG_ALL_COLOR_KEYWORDS
#include "nanosvg.h"

#if defined(_WIN32)
#define DE_SVG_API __declspec(dllexport)
#else
#define DE_SVG_API
#endif

typedef struct
{
    unsigned char *buf;
    int len, cap;
    int f32;
} de_sw;

static void de_sw_bytes(de_sw *w, const void *p, int n)
{
    if (w->len + n > w->cap)
    {
        int cap = w->cap ? w->cap * 2 : 4096;
        while (cap < w->len + n)
            cap *= 2;
        w->buf = (unsigned char *)realloc(w->buf, (size_t)cap);
        w->cap = cap;
    }
    memcpy(w->buf + w->len, p, (size_t)n);
    w->len += n;
}
static void de_sw_u8(de_sw *w, unsigned v) { unsigned char b = (unsigned char)v; de_sw_bytes(w, &b, 1); }
static void de_sw_u16(de_sw *w, unsigned v) { unsigned char b[2] = {(unsigned char)v, (unsigned char)(v >> 8)}; de_sw_bytes(w, b, 2); }
static void de_sw_u32(de_sw *w, unsigned v)
{
    unsigned char b[4] = {(unsigned char)v, (unsigned char)(v >> 8), (unsigned char)(v >> 16), (unsigned char)(v >> 24)};
    de_sw_bytes(w, b, 4);
}
static void de_sw_f32(de_sw *w, float f) { unsigned u; memcpy(&u, &f, 4); de_sw_u32(w, u); }
static void de_sw_coord(de_sw *w, float v)
{
    if (w->f32)
        de_sw_f32(w, v);
    else
        de_sw_u16(w, (unsigned)(short)lrintf(v * 16.0f));
}

static void de_sw_paint(de_sw *w, const NSVGpaint *p, float s)
{
    de_sw_u8(w, (unsigned)p->type);
    if (p->type == NSVG_PAINT_COLOR)
        de_sw_u32(w, p->color);
    else if (p->type == NSVG_PAINT_LINEAR_GRADIENT || p->type == NSVG_PAINT_RADIAL_GRADIENT)
    {
        const NSVGgradient *g = p->gradient;
        de_sw_u8(w, (unsigned)g->spread);
        de_sw_f32(w, g->fx);
        de_sw_f32(w, g->fy);
        // xform = kullanici -> gradient uzayi (ters). Noktalar s ile carpildi: ters donusum 1/s ile oncelenir.
        for (int i = 0; i < 4; i++)
            de_sw_f32(w, g->xform[i] / s);
        de_sw_f32(w, g->xform[4]);
        de_sw_f32(w, g->xform[5]);
        int n = g->nstops > 255 ? 255 : g->nstops;
        de_sw_u8(w, (unsigned)n);
        for (int i = 0; i < n; i++)
        {
            de_sw_u32(w, g->stops[i].color);
            de_sw_f32(w, g->stops[i].offset);
        }
    }
}

// Cubic'in kontrol noktalari ucte-bir noktalarsa (nanosvg lineTo ciktisi) dogru parcasidir.
static int de_is_line(const float *p, float tol)
{
    float dx = p[6] - p[0], dy = p[7] - p[1];
    float c1x = p[0] + dx / 3.0f, c1y = p[1] + dy / 3.0f, c2x = p[6] - dx / 3.0f, c2y = p[7] - dy / 3.0f;
    return fabsf(p[2] - c1x) < tol && fabsf(p[3] - c1y) < tol && fabsf(p[4] - c2x) < tol && fabsf(p[5] - c2y) < tol;
}

static int de_shape_count(const NSVGimage *img)
{
    int n = 0;
    for (const NSVGshape *s = img->shapes; s; s = s->next)
        if ((s->flags & NSVG_FLAGS_VISIBLE) && (s->fill.type != NSVG_PAINT_NONE || s->stroke.type != NSVG_PAINT_NONE))
            n++;
    return n;
}

static int de_path_count(const NSVGshape *s)
{
    int n = 0;
    for (const NSVGpath *p = s->paths; p; p = p->next)
        if (p->npts >= 1)
            n++;
    return n;
}

// Koordinatlar i16/16'ya sigiyor mu (hedef uzayda).
static int de_fits_fixed(const NSVGimage *img, float s)
{
    const float lim = 32767.0f / 16.0f;
    for (const NSVGshape *sh = img->shapes; sh; sh = sh->next)
        for (const NSVGpath *p = sh->paths; p; p = p->next)
            for (int i = 0; i < p->npts * 2; i++)
                if (fabsf(p->pts[i] * s) > lim)
                    return 0;
    return 1;
}

// svg: UTF-8 metin (NUL sonlu kopyasi alinir; nanosvg girdiyi bozar). scale: dogal px'e carpan.
// maxSize: hedef kenar ust siniri (0 = 4096). outW/outH hedef piksel boyutu. Donus malloc'lu blob
// (de_svg_free), hata = NULL (*outLen = 0).
DE_SVG_API unsigned char *de_svg_compile(const char *svg, int svgLen, float scale, int maxSize, int *outLen, int *outW, int *outH)
{
    *outLen = 0;
    *outW = *outH = 0;
    if (!svg || svgLen <= 0 || !(scale > 0.0f))
        return NULL;
    if (maxSize <= 0)
        maxSize = 4096;
    char *copy = (char *)malloc((size_t)svgLen + 1);
    if (!copy)
        return NULL;
    memcpy(copy, svg, (size_t)svgLen);
    copy[svgLen] = 0;
    NSVGimage *img = nsvgParse(copy, "px", 96.0f);
    free(copy);
    if (!img)
        return NULL;
    int w = (int)ceilf(img->width * scale - 0.001f), h = (int)ceilf(img->height * scale - 0.001f);
    if (w < 1 || h < 1 || w > maxSize || h > maxSize || w > 65535 || h > 65535)
    {
        nsvgDelete(img);
        return NULL;
    }
    de_sw wr = {0};
    wr.f32 = !de_fits_fixed(img, scale);
    de_sw_bytes(&wr, "DVEC", 4);
    de_sw_u8(&wr, 1);
    de_sw_u8(&wr, wr.f32 ? 1u : 0u);
    de_sw_u16(&wr, (unsigned)de_shape_count(img));
    de_sw_u16(&wr, (unsigned)w);
    de_sw_u16(&wr, (unsigned)h);
    const float tol = 0.02f / scale; // dogru tespiti kaynak uzayda, hedefte 1/50 px
    for (const NSVGshape *s = img->shapes; s; s = s->next)
    {
        if (!(s->flags & NSVG_FLAGS_VISIBLE) || (s->fill.type == NSVG_PAINT_NONE && s->stroke.type == NSVG_PAINT_NONE))
            continue;
        unsigned sf = 0;
        if (s->fill.type != NSVG_PAINT_NONE) sf |= 1;
        if (s->stroke.type != NSVG_PAINT_NONE) sf |= 2;
        if (s->fillRule == NSVG_FILLRULE_EVENODD) sf |= 4;
        sf |= ((unsigned)s->strokeLineJoin & 3) << 3;
        sf |= ((unsigned)s->strokeLineCap & 3) << 5;
        if ((s->paintOrder & 3) == NSVG_PAINT_STROKE) sf |= 0x80;
        de_sw_u8(&wr, sf);
        int op = (int)lrintf(s->opacity * 255.0f);
        de_sw_u8(&wr, (unsigned)(op < 0 ? 0 : op > 255 ? 255 : op));
        if (sf & 1) de_sw_paint(&wr, &s->fill, scale);
        if (sf & 2)
        {
            de_sw_paint(&wr, &s->stroke, scale);
            de_sw_f32(&wr, s->strokeWidth * scale);
            de_sw_f32(&wr, s->miterLimit);
            int dc = s->strokeDashCount;
            if (dc < 0 || dc > 8) dc = 0;
            de_sw_u8(&wr, (unsigned)dc);
            if (dc > 0)
            {
                de_sw_f32(&wr, s->strokeDashOffset * scale);
                for (int i = 0; i < dc; i++)
                    de_sw_f32(&wr, s->strokeDashArray[i] * scale);
            }
        }
        de_sw_u16(&wr, (unsigned)de_path_count(s));
        for (const NSVGpath *p = s->paths; p; p = p->next)
        {
            if (p->npts < 1)
                continue;
            int nseg = (p->npts - 1) / 3;
            de_sw_u8(&wr, (unsigned)(p->closed ? 1 : 0));
            de_sw_u16(&wr, (unsigned)(1 + nseg));
            de_sw_u8(&wr, 0); // M
            de_sw_coord(&wr, p->pts[0] * scale);
            de_sw_coord(&wr, p->pts[1] * scale);
            for (int i = 0; i < nseg; i++)
            {
                const float *q = &p->pts[i * 6];
                if (de_is_line(q, tol))
                {
                    de_sw_u8(&wr, 1); // L
                    de_sw_coord(&wr, q[6] * scale);
                    de_sw_coord(&wr, q[7] * scale);
                }
                else
                {
                    de_sw_u8(&wr, 2); // C
                    for (int k = 2; k < 8; k++)
                        de_sw_coord(&wr, q[k] * scale);
                }
            }
        }
    }
    nsvgDelete(img);
    *outLen = wr.len;
    *outW = w;
    *outH = h;
    return wr.buf;
}

DE_SVG_API void de_svg_free(void *p) { free(p); }
