// ---------------------------------------------------------------------------
// de_vector.h — DVEC vektor kodegi: komut akisi -> RGBA raster (nanosvgrast)
// ---------------------------------------------------------------------------
// Vektor asset (svg) import'ta DVEC blob'una derlenir (de_svg_compile.c, editor-only);
// runtime bu blob'u PNG gibi "decode" eder: de_fs decode job'u magic'i gorunce stbi
// yerine buraya gelir. Cozunurluk import'ta sabittir (header w/h), DPI olcegi yok.
//
// Tek baslik (stb tarzi): bir .c dosyasinda DE_VECTOR_IMPLEMENTATION tanimla
// (de_fs.c yapar; player/Android/iOS kaynak listeleri degismez).
//
// BICIM (little-endian):
//   "DVEC" u8 ver=1  u8 flags(bit0: koordinatlar f32, yoksa i16 1/16 px)  u16 shapeCount  u16 w  u16 h
//   shape: u8 sflags (bit0 fill, bit1 stroke, bit2 evenodd, bit3-4 join, bit5-6 cap, bit7 stroke-once-cizilir)
//          u8 opacity  [paint fill]  [paint stroke]
//          stroke varsa: f32 width  f32 miterLimit  u8 dashCount  (dashCount>0: f32 offset, f32 dash[n])
//          u16 pathCount  path*: u8 closed  u16 cmdCount  cmd*
//   paint: u8 type (1 renk: u32 rgba) | (2 linear / 3 radial: u8 spread  f32 fx fy  f32 xform[6]  u8 nstops  (u32 rgba, f32 off)*)
//   cmd:   u8 op (0 M, 1 L, 2 C)  koordinatlar (M/L: 2, C: 6) — koordinat uzayi = hedef piksel (olcek import'ta uygulanmis).
// ---------------------------------------------------------------------------
#ifndef DE_VECTOR_H
#define DE_VECTOR_H

#ifdef __cplusplus
extern "C"
{
#endif

    // Blob DVEC mi (magic + minimum uzunluk).
    int de_vector_is(const unsigned char *blob, long long len);

    // Header boyutlari; 1 = ok.
    int de_vector_info(const unsigned char *blob, long long len, int *w, int *h);

    // Tam raster: malloc'lu RGBA8 (w*h*4), satir 0 ALTTA (stbi flip sozlesmesi). Hata = NULL.
    unsigned char *de_vector_rasterize(const unsigned char *blob, long long len, int *w, int *h);

    // Buyuk bir RGBA tamponun (x,y) dikdortgenine cizer (atlas sayfasi birlestirme). Tampon satir 0
    // ALTTA kabul edilir (y = alttan). Dikdortgen onceden sifirlanir (nanosvg davranisi). 1 = ok.
    int de_vector_draw(const unsigned char *blob, long long len, unsigned char *dst, int dstW, int dstH, int dstStride, int x, int y);

#ifdef __cplusplus
}
#endif
#endif // DE_VECTOR_H

#ifdef DE_VECTOR_IMPLEMENTATION

#include <stdlib.h>
#include <string.h>
#include "nanosvg.h" // yalniz tipler (NANOSVG_IMPLEMENTATION yok: XML parser runtime'a girmez)
#define NANOSVGRAST_IMPLEMENTATION
#include "nanosvgrast.h"

typedef struct
{
    const unsigned char *p, *end;
    int fail, f32;
} de_vec_rd;

static unsigned de_vr_u8(de_vec_rd *r)
{
    if (r->p + 1 > r->end) { r->fail = 1; return 0; }
    return *r->p++;
}
static unsigned de_vr_u16(de_vec_rd *r)
{
    if (r->p + 2 > r->end) { r->fail = 1; return 0; }
    unsigned v = r->p[0] | (r->p[1] << 8);
    r->p += 2;
    return v;
}
static unsigned de_vr_u32(de_vec_rd *r)
{
    if (r->p + 4 > r->end) { r->fail = 1; return 0; }
    unsigned v = r->p[0] | (r->p[1] << 8) | (r->p[2] << 16) | ((unsigned)r->p[3] << 24);
    r->p += 4;
    return v;
}
static float de_vr_f32(de_vec_rd *r)
{
    unsigned u = de_vr_u32(r);
    float f;
    memcpy(&f, &u, 4);
    return f;
}
static float de_vr_coord(de_vec_rd *r)
{
    if (r->f32)
        return de_vr_f32(r);
    short s = (short)de_vr_u16(r);
    return s / 16.0f;
}

static int de_vr_paint(de_vec_rd *r, NSVGpaint *paint)
{
    paint->type = (signed char)de_vr_u8(r);
    if (paint->type == NSVG_PAINT_COLOR)
        paint->color = de_vr_u32(r);
    else if (paint->type == NSVG_PAINT_LINEAR_GRADIENT || paint->type == NSVG_PAINT_RADIAL_GRADIENT)
    {
        char spread = (char)de_vr_u8(r);
        float fx = de_vr_f32(r), fy = de_vr_f32(r), xf[6];
        for (int i = 0; i < 6; i++)
            xf[i] = de_vr_f32(r);
        int n = (int)de_vr_u8(r);
        if (r->fail)
            return 0;
        NSVGgradient *g = (NSVGgradient *)calloc(1, sizeof(NSVGgradient) + sizeof(NSVGgradientStop) * (n > 0 ? n - 1 : 0));
        if (!g)
            return 0;
        g->spread = spread;
        g->fx = fx;
        g->fy = fy;
        memcpy(g->xform, xf, sizeof xf);
        g->nstops = n;
        for (int i = 0; i < n; i++)
        {
            g->stops[i].color = de_vr_u32(r);
            g->stops[i].offset = de_vr_f32(r);
        }
        paint->gradient = g;
    }
    else if (paint->type != NSVG_PAINT_NONE)
        return 0;
    return !r->fail;
}

static void de_vec_free_image(NSVGimage *img)
{
    if (!img)
        return;
    NSVGshape *s = img->shapes;
    while (s)
    {
        NSVGshape *sn = s->next;
        NSVGpath *p = s->paths;
        while (p)
        {
            NSVGpath *pn = p->next;
            free(p->pts);
            free(p);
            p = pn;
        }
        if (s->fill.type == NSVG_PAINT_LINEAR_GRADIENT || s->fill.type == NSVG_PAINT_RADIAL_GRADIENT)
            free(s->fill.gradient);
        if (s->stroke.type == NSVG_PAINT_LINEAR_GRADIENT || s->stroke.type == NSVG_PAINT_RADIAL_GRADIENT)
            free(s->stroke.gradient);
        free(s);
        s = sn;
    }
    free(img);
}

// Yol komutlarini nanosvg'nin cubic listesine acar (L -> ucte-bir kontrol noktali cubic).
static NSVGpath *de_vr_path(de_vec_rd *r)
{
    char closed = (char)de_vr_u8(r);
    int n = (int)de_vr_u16(r);
    if (r->fail || n < 1)
        return NULL;
    int cap = 1 + n * 3; // en kotu: hepsi C
    float *pts = (float *)malloc(sizeof(float) * 2 * cap);
    if (!pts)
        return NULL;
    int np = 0;
    for (int i = 0; i < n && !r->fail; i++)
    {
        unsigned op = de_vr_u8(r);
        if (op == 0) // M: yalniz ilk komut (sonraki M'ler import'ta ayri path'e bolunur)
        {
            if (np != 0) { r->fail = 1; break; }
            pts[0] = de_vr_coord(r);
            pts[1] = de_vr_coord(r);
            np = 1;
        }
        else if (op == 1) // L
        {
            if (np == 0) { r->fail = 1; break; }
            float x0 = pts[(np - 1) * 2], y0 = pts[(np - 1) * 2 + 1];
            float x = de_vr_coord(r), y = de_vr_coord(r);
            float dx = x - x0, dy = y - y0;
            float *q = &pts[np * 2];
            q[0] = x0 + dx / 3.0f; q[1] = y0 + dy / 3.0f;
            q[2] = x - dx / 3.0f;  q[3] = y - dy / 3.0f;
            q[4] = x;              q[5] = y;
            np += 3;
        }
        else if (op == 2) // C
        {
            if (np == 0) { r->fail = 1; break; }
            float *q = &pts[np * 2];
            for (int k = 0; k < 6; k++)
                q[k] = de_vr_coord(r);
            np += 3;
        }
        else
        {
            r->fail = 1;
            break;
        }
    }
    if (r->fail || np < 1)
    {
        free(pts);
        return NULL;
    }
    NSVGpath *p = (NSVGpath *)calloc(1, sizeof(NSVGpath));
    if (!p)
    {
        free(pts);
        return NULL;
    }
    p->pts = pts;
    p->npts = np;
    p->closed = closed;
    p->bounds[0] = p->bounds[2] = pts[0];
    p->bounds[1] = p->bounds[3] = pts[1];
    for (int i = 1; i < np; i++)
    {
        float x = pts[i * 2], y = pts[i * 2 + 1];
        if (x < p->bounds[0]) p->bounds[0] = x;
        if (y < p->bounds[1]) p->bounds[1] = y;
        if (x > p->bounds[2]) p->bounds[2] = x;
        if (y > p->bounds[3]) p->bounds[3] = y;
    }
    return p;
}

static NSVGimage *de_vec_parse(const unsigned char *blob, long long len)
{
    de_vec_rd r = {blob, blob + len, 0, 0};
    if (len < 12 || memcmp(blob, "DVEC", 4) != 0)
        return NULL;
    r.p += 4;
    unsigned ver = de_vr_u8(&r), flags = de_vr_u8(&r);
    if (ver != 1)
        return NULL;
    r.f32 = (flags & 1) != 0;
    int nshapes = (int)de_vr_u16(&r);
    int w = (int)de_vr_u16(&r), h = (int)de_vr_u16(&r);
    NSVGimage *img = (NSVGimage *)calloc(1, sizeof(NSVGimage));
    if (!img)
        return NULL;
    img->width = (float)w;
    img->height = (float)h;
    NSVGshape *tail = NULL;
    for (int si = 0; si < nshapes && !r.fail; si++)
    {
        NSVGshape *s = (NSVGshape *)calloc(1, sizeof(NSVGshape));
        if (!s)
            break;
        unsigned sf = de_vr_u8(&r);
        s->opacity = de_vr_u8(&r) / 255.0f;
        s->fillRule = (sf & 4) ? NSVG_FILLRULE_EVENODD : NSVG_FILLRULE_NONZERO;
        s->strokeLineJoin = (char)((sf >> 3) & 3);
        s->strokeLineCap = (char)((sf >> 5) & 3);
        s->flags = NSVG_FLAGS_VISIBLE;
        // Boya sirasi: varsayilan fill -> stroke; bit7 = stroke once.
        s->paintOrder = (sf & 0x80) ? (unsigned char)(NSVG_PAINT_STROKE | (NSVG_PAINT_FILL << 2) | (NSVG_PAINT_MARKERS << 4))
                                    : (unsigned char)(NSVG_PAINT_FILL | (NSVG_PAINT_STROKE << 2) | (NSVG_PAINT_MARKERS << 4));
        s->fill.type = NSVG_PAINT_NONE;
        s->stroke.type = NSVG_PAINT_NONE;
        s->miterLimit = 4.0f;
        if ((sf & 1) && !de_vr_paint(&r, &s->fill))
            r.fail = 1;
        if (!r.fail && (sf & 2))
        {
            if (!de_vr_paint(&r, &s->stroke))
                r.fail = 1;
            else
            {
                s->strokeWidth = de_vr_f32(&r);
                s->miterLimit = de_vr_f32(&r);
                int dc = (int)de_vr_u8(&r);
                if (dc > 8) r.fail = 1;
                else if (dc > 0)
                {
                    s->strokeDashCount = (char)dc;
                    s->strokeDashOffset = de_vr_f32(&r);
                    for (int i = 0; i < dc; i++)
                        s->strokeDashArray[i] = de_vr_f32(&r);
                }
            }
        }
        int npaths = r.fail ? 0 : (int)de_vr_u16(&r);
        NSVGpath *ptail = NULL;
        int first = 1;
        for (int pi = 0; pi < npaths && !r.fail; pi++)
        {
            NSVGpath *p = de_vr_path(&r);
            if (!p) { r.fail = 1; break; }
            if (ptail) ptail->next = p; else s->paths = p;
            ptail = p;
            if (first) { memcpy(s->bounds, p->bounds, sizeof p->bounds); first = 0; }
            else
            {
                if (p->bounds[0] < s->bounds[0]) s->bounds[0] = p->bounds[0];
                if (p->bounds[1] < s->bounds[1]) s->bounds[1] = p->bounds[1];
                if (p->bounds[2] > s->bounds[2]) s->bounds[2] = p->bounds[2];
                if (p->bounds[3] > s->bounds[3]) s->bounds[3] = p->bounds[3];
            }
        }
        if (tail) tail->next = s; else img->shapes = s;
        tail = s;
    }
    if (r.fail)
    {
        de_vec_free_image(img);
        return NULL;
    }
    return img;
}

int de_vector_is(const unsigned char *blob, long long len)
{
    return blob && len >= 12 && memcmp(blob, "DVEC", 4) == 0;
}

int de_vector_info(const unsigned char *blob, long long len, int *w, int *h)
{
    if (!de_vector_is(blob, len))
        return 0;
    *w = blob[8] | (blob[9] << 8);
    *h = blob[10] | (blob[11] << 8);
    return *w > 0 && *h > 0;
}

// Satir 0 ustte raster -> satirlari ters cevirerek dst'ye (satir 0 altta) kopyalar.
static void de_vec_copy_flipped(const unsigned char *src, int w, int h, unsigned char *dst, int dstStride)
{
    for (int y = 0; y < h; y++)
        memcpy(dst + (size_t)(h - 1 - y) * dstStride, src + (size_t)y * w * 4, (size_t)w * 4);
}

unsigned char *de_vector_rasterize(const unsigned char *blob, long long len, int *w, int *h)
{
    if (!de_vector_info(blob, len, w, h))
        return NULL;
    NSVGimage *img = de_vec_parse(blob, len);
    if (!img)
        return NULL;
    size_t sz = (size_t)*w * *h * 4;
    unsigned char *top = (unsigned char *)malloc(sz), *out = (unsigned char *)malloc(sz);
    NSVGrasterizer *rast = nsvgCreateRasterizer();
    if (top && out && rast)
    {
        nsvgRasterize(rast, img, 0, 0, 1.0f, top, *w, *h, *w * 4);
        de_vec_copy_flipped(top, *w, *h, out, *w * 4);
    }
    else
    {
        free(out);
        out = NULL;
    }
    if (rast) nsvgDeleteRasterizer(rast);
    free(top);
    de_vec_free_image(img);
    return out;
}

int de_vector_draw(const unsigned char *blob, long long len, unsigned char *dst, int dstW, int dstH, int dstStride, int x, int y)
{
    int w, h;
    if (!de_vector_info(blob, len, &w, &h) || x < 0 || y < 0 || x + w > dstW || y + h > dstH)
        return 0;
    NSVGimage *img = de_vec_parse(blob, len);
    if (!img)
        return 0;
    unsigned char *top = (unsigned char *)malloc((size_t)w * h * 4);
    NSVGrasterizer *rast = nsvgCreateRasterizer();
    int ok = top && rast;
    if (ok)
    {
        nsvgRasterize(rast, img, 0, 0, 1.0f, top, w, h, w * 4);
        de_vec_copy_flipped(top, w, h, dst + (size_t)y * dstStride + (size_t)x * 4, dstStride);
    }
    if (rast) nsvgDeleteRasterizer(rast);
    free(top);
    de_vec_free_image(img);
    return ok;
}

#endif // DE_VECTOR_IMPLEMENTATION
