// ---------------------------------------------------------------------------
// de_image_write.c — EDITOR-ONLY piksel encoder (stb_image_write): importer/paketleyici ciktilari
// PNG/JPG olarak yazilir; release ikilisine girmez (build_editor.cmd disinda derlenmez).
// ---------------------------------------------------------------------------
#include <stdlib.h>
#include <string.h>

#define STB_IMAGE_WRITE_IMPLEMENTATION
#define STBI_WRITE_NO_STDIO
#define STB_IMAGE_WRITE_STATIC
#include "stb_image_write.h"

#if defined(_WIN32)
#define DE_IW_API __declspec(dllexport)
#else
#define DE_IW_API
#endif

typedef struct
{
    unsigned char *buf;
    int len, cap;
} de_iw_sink;

static void de_iw_write(void *ctx, void *data, int size)
{
    de_iw_sink *s = (de_iw_sink *)ctx;
    if (s->len + size > s->cap)
    {
        int cap = s->cap ? s->cap * 2 : 65536;
        while (cap < s->len + size)
            cap *= 2;
        s->buf = (unsigned char *)realloc(s->buf, (size_t)cap);
        s->cap = cap;
    }
    memcpy(s->buf + s->len, data, (size_t)size);
    s->len += size;
}

// pixels: comp kanalli, satir 0 bellekte ilk. kind 0 = PNG (kayipsiz), 1 = JPG (quality 1..100, comp 1/3/4; alfa atilir).
// flip != 0: dosyaya satirlar ters yazilir (stb_image yukleme flip'iyle bellek duzeni birebir geri gelsin diye).
// Donus malloc'lu tampon (de_image_free), *outLen bayt; hata = NULL.
DE_IW_API unsigned char *de_image_encode(const unsigned char *pixels, int w, int h, int comp, int kind, int quality, int flip, int *outLen)
{
    de_iw_sink s = {0};
    stbi_flip_vertically_on_write(flip);
    int ok = kind == 1
                 ? stbi_write_jpg_to_func(de_iw_write, &s, w, h, comp, pixels, quality < 1 ? 90 : quality > 100 ? 100
                                                                                                                : quality)
                 : stbi_write_png_to_func(de_iw_write, &s, w, h, comp, pixels, w * comp);
    stbi_flip_vertically_on_write(0);
    if (!ok)
    {
        free(s.buf);
        *outLen = 0;
        return NULL;
    }
    *outLen = s.len;
    return s.buf;
}

DE_IW_API void de_image_free(void *p) { free(p); }
