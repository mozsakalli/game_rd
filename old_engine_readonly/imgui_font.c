#define STB_TRUETYPE_IMPLEMENTATION
#include "stb_truetype.h"
#include "imgui_font.h"
#include "engine_render.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

#define IMGUI_FONT_ATLAS_SIZE 2048
#define IMGUI_FONT_FIRST_CHAR 32
#define IMGUI_FONT_CHAR_COUNT 224
#define IMGUI_FONT_SDF_SIZE 32.0f
#define IMGUI_FONT_SDF_SPREAD 21.0f // 128/padding: alan tam padding'de doyar (TMP genis gradyan)
#define IMGUI_FONT_SDF_PADDING 6

typedef _engine_BitmapData *imgui_font_texture;
extern void engine_draw_ui_rect(float x, float y, float width, float height,
                                unsigned char r, unsigned char g, unsigned char b, unsigned char a, int layer);

typedef struct
{
    int glyphIndex;
    int atlasX;
    int atlasY;
    int atlasPixelWidth;
    int atlasPixelHeight;
    int atlasWidth;
    int atlasHeight;
    int width;
    int height;
    float xoff;
    float yoff;
    float xadvance;
} imgui_font_glyph;

struct imgui_font
{
    imgui_font_texture texture;
    imgui_font_glyph glyphs[IMGUI_FONT_CHAR_COUNT];
    float pixelHeight;
    float lineHeight;
    float ascender;
    float descender;
    float sdfSize;
    float kernScale; // font-unit -> SDF piksel donusumu
    int16_t kerning[IMGUI_FONT_CHAR_COUNT][IMGUI_FONT_CHAR_COUNT];
    int atlasWidth;
    int atlasHeight;
};

// SDF metin efekti: engine sadece generic efekt API'si saglar, SDF font modulunundur.
static const char *_imgui_font_sdf_effect_body =
    "FLOAT contour(FLOAT distance, FLOAT edge, FLOAT width) {\n"
    "    return SATURATE(smoothstep(edge - width, edge + width, distance));\n"
    "}\n"
    "FLOAT getSample(VEC2 uv, FLOAT edge, FLOAT width) {\n"
    "    return contour(SAMPLE(tex, uv).a, edge, width);\n"
    "}\n"
    "VEC4 fs_main(VEC2 uv, VEC4 color) {\n"
    "    FLOAT distance = SAMPLE(tex, uv).a;\n"
    "    FLOAT smoothing = FWIDTH(distance) ;\n"
    "    FLOAT alpha = smoothstep(0.5 - smoothing, 0.5 + smoothing, distance);\n"
    "    FLOAT outAlpha = alpha * color.a;\n"
    "    return VEC4(color.rgb * outAlpha, outAlpha);\n"
    /*"    FLOAT distance = SAMPLE(tex, uv).a;\n"
    "    FLOAT width = FWIDTH(distance);\n"
    "    FLOAT alpha = contour(distance, 0.5, width);\n"
    "    FLOAT dscale = 0.0354;\n"
    "    VEC2 duv = dscale * (DFDX(uv) + DFDY(uv));\n"
    "    VEC4 box = VEC4(uv - duv, uv + duv);\n"
    "    FLOAT asum = getSample(box.xy, 0.5, width) + getSample(box.zw, 0.5, width) +\n"
    "                 getSample(box.xw, 0.5, width) + getSample(box.zy, 0.5, width);\n"
    "    alpha = (alpha + 0.5 * asum) / 4.0;\n"
    "    FLOAT outAlpha = alpha * color.a;\n"
    "    return VEC4(color.rgb * outAlpha, outAlpha);\n"*/
    "}\n";

static _engine_Shader *_imgui_font_shader;

static _engine_Shader *_imgui_font_get_shader(void)
{
    if (!_imgui_font_shader)
        _imgui_font_shader = engine_shader_create_effect(_imgui_font_sdf_effect_body);
    return _imgui_font_shader;
}

static void _imgui_draw_font_glyph(const imgui_font *font, float x, float y, float width, float height,
                                   float u0, float v0, float u1, float v1, imgui_color color,
                                   int layer)
{
    _engine_Material material = *engine_default_material();
    material.mainTexture = font->texture;
    material.shader = _imgui_font_get_shader();
    material.srcBlend = ENGINE_BLEND_ONE;
    material.dstBlend = ENGINE_BLEND_ONE_MINUS_SRC_ALPHA;
    material.samplerType = ENGINE_SAMPLER_LINEAR;
    _engine_Mat4 model;
    engine_mat4_trs2d(&model, x + width * 0.5f, y + height * 0.5f, 0.0f, width, -height, 0.0f);
    engine_draw_mesh(engine_quad_mesh(), &material, &model,
                     (_engine_Color){color.r, color.g, color.b, color.a},
                     u0, v0, u1, v1, layer);
}

static void _imgui_draw_font_atlas(const imgui_font *font, float x, float y, float width, float height, int layer)
{
    _engine_Material material = *engine_default_material();
    material.mainTexture = font->texture;
    material.samplerType = ENGINE_SAMPLER_NEAREST;
    _engine_Mat4 model;
    engine_mat4_trs2d(&model, x + width * 0.5f, y + height * 0.5f, 0.0f, width, -height, 0.0f);
    engine_draw_mesh(engine_quad_mesh(), &material, &model,
                     (_engine_Color){255, 255, 255, 255}, 0.0f, 0.0f, 1.0f, 1.0f, layer);
}

static int _imgui_utf16_next(const VmString *text, int *index)
{
    if (!text || !index || *index >= text->length)
        return -1;
    unsigned int first = text->data[(*index)++];
    if (first >= 0xD800 && first <= 0xDBFF && *index < text->length)
    {
        unsigned int second = text->data[*index];
        if (second >= 0xDC00 && second <= 0xDFFF)
        {
            ++*index;
            return (int)(0x10000 + ((first - 0xD800) << 10) + (second - 0xDC00));
        }
    }
    return (int)first;
}

static char *_imgui_path_ascii(VmString *path)
{
    if (!path)
        return NULL;
    char *result = (char *)malloc((size_t)path->length + 1);
    if (!result)
        return NULL;
    int count = 0;
    int index = 0;
    int codepoint;
    while ((codepoint = _imgui_utf16_next(path, &index)) >= 0)
        result[count++] = codepoint < 128 ? (char)codepoint : '?';
    result[count] = 0;
    return result;
}

imgui_font *imgui_font_load_memory(const unsigned char *data, int size, float pixelHeight)
{
    if (!data || size <= 0 || pixelHeight <= 0.0f)
        return NULL;

    imgui_font *font = (imgui_font *)calloc(1, sizeof(*font));
    unsigned char *alpha = (unsigned char *)calloc(IMGUI_FONT_ATLAS_SIZE * IMGUI_FONT_ATLAS_SIZE, 1);
    if (!font || !alpha)
    {
        free(font);
        free(alpha);
        return NULL;
    }

    stbtt_fontinfo info;
    if (!stbtt_InitFont(&info, data, 0))
    {
        free(font);
        free(alpha);
        return NULL;
    }

    float scale = stbtt_ScaleForPixelHeight(&info, IMGUI_FONT_SDF_SIZE);
    int padding = IMGUI_FONT_SDF_PADDING;
    int cellSize = 128;
    int columns = IMGUI_FONT_ATLAS_SIZE / cellSize;
    if (columns < 1)
        columns = 1;
    for (int i = 0; i < IMGUI_FONT_CHAR_COUNT; ++i)
    {
        int codepoint = IMGUI_FONT_FIRST_CHAR + i;
        int glyphIndex = stbtt_FindGlyphIndex(&info, codepoint);
        int advance = 0;
        int lsb = 0;
        int x0 = 0;
        int y0 = 0;
        int x1 = 0;
        int y1 = 0;
        stbtt_GetGlyphHMetrics(&info, glyphIndex, &advance, &lsb);
        int sdfWidth = 0;
        int sdfHeight = 0;
        unsigned char *sdf = stbtt_GetCodepointSDF(&info, scale, codepoint, padding, 128,
                                                   IMGUI_FONT_SDF_SPREAD,
                                                   &sdfWidth, &sdfHeight, &x0, &y0);
        x1 = x0 + sdfWidth;
        y1 = y0 + sdfHeight;
        int width = x1 - x0;
        int height = y1 - y0;
        int column = i % columns;
        int row = i / columns;
        int atlasX = column * cellSize;
        int atlasY = row * cellSize;
        if (atlasY + cellSize > IMGUI_FONT_ATLAS_SIZE)
            break;
        font->glyphs[i].atlasX = atlasX;
        font->glyphs[i].glyphIndex = glyphIndex;
        font->glyphs[i].atlasY = atlasY;
        font->glyphs[i].atlasPixelWidth = width;
        font->glyphs[i].atlasPixelHeight = height;
        font->glyphs[i].atlasWidth = width + padding * 2;
        font->glyphs[i].atlasHeight = height + padding * 2;
        font->glyphs[i].width = width;
        font->glyphs[i].height = height;
        font->glyphs[i].xoff = (float)x0;
        font->glyphs[i].yoff = (float)y0;
        font->glyphs[i].xadvance = scale * (float)advance;
        if (sdf && width > 0 && height > 0)
        {
            for (int rowIndex = 0; rowIndex < height; ++rowIndex)
            {
                memcpy(alpha + atlasX + (atlasY + rowIndex) * IMGUI_FONT_ATLAS_SIZE,
                       sdf + rowIndex * width, (size_t)width);
            }
        }
        stbtt_FreeSDF(sdf, NULL);
    }

    for (int left = 0; left < IMGUI_FONT_CHAR_COUNT; ++left)
    {
        for (int right = 0; right < IMGUI_FONT_CHAR_COUNT; ++right)
        {
            int amount = stbtt_GetGlyphKernAdvance(&info,
                                                   font->glyphs[left].glyphIndex,
                                                   font->glyphs[right].glyphIndex);
            font->kerning[left][right] = (int16_t)amount;
        }
    }

    font->texture = engine_bitmap_data_create_alpha(IMGUI_FONT_ATLAS_SIZE, IMGUI_FONT_ATLAS_SIZE, alpha);
    font->pixelHeight = pixelHeight;
    font->sdfSize = IMGUI_FONT_SDF_SIZE;
    font->kernScale = scale;
    int ascent = 0;
    int descent = 0;
    int lineGap = 0;
    stbtt_GetFontVMetrics(&info, &ascent, &descent, &lineGap);
    float displayScale = pixelHeight / IMGUI_FONT_SDF_SIZE;
    font->ascender = scale * (float)ascent * displayScale;
    font->descender = scale * (float)descent * displayScale;
    font->lineHeight = scale * (float)(ascent - descent + lineGap) * displayScale;
    font->atlasWidth = IMGUI_FONT_ATLAS_SIZE;
    font->atlasHeight = IMGUI_FONT_ATLAS_SIZE;
    free(alpha);
    if (!font->texture)
    {
        free(font);
        return NULL;
    }
    return font;
}

imgui_font *imgui_font_load_file(VmString *path, float pixelHeight)
{
    char *filename = _imgui_path_ascii(path);
    if (!filename)
        return NULL;
    FILE *file = NULL;
#if defined(_WIN32)
    fopen_s(&file, filename, "rb");
#else
    file = fopen(filename, "rb");
#endif
    free(filename);
    if (!file)
        return NULL;
    fseek(file, 0, SEEK_END);
    long length = ftell(file);
    fseek(file, 0, SEEK_SET);
    if (length <= 0 || length > 64 * 1024 * 1024)
    {
        fclose(file);
        return NULL;
    }
    unsigned char *data = (unsigned char *)malloc((size_t)length);
    if (!data)
    {
        fclose(file);
        return NULL;
    }
    size_t readCount = fread(data, 1, (size_t)length, file);
    fclose(file);
    imgui_font *font = readCount == (size_t)length ? imgui_font_load_memory(data, (int)length, pixelHeight) : NULL;
    free(data);
    return font;
}

void imgui_font_destroy(imgui_font *font)
{
    free(font);
}

float imgui_font_line_height(const imgui_font *font)
{
    return font ? font->lineHeight : 0.0f;
}

void imgui_text_size(const imgui_font *font, VmString *text, float *width, float *height)
{
    imgui_text_size_at(font, text, font ? font->pixelHeight : 0.0f, width, height);
}

void imgui_text_size_at(const imgui_font *font, VmString *text, float pixelHeight, float *width, float *height)
{
    float maxWidth = 0.0f;
    float lineWidth = 0.0f;
    int lines = 1;
    if (font && text)
    {
        int previousIndex = -1;
        int index = 0;
        int codepoint;
        while ((codepoint = _imgui_utf16_next(text, &index)) >= 0)
        {
            if (codepoint == '\n')
            {
                if (lineWidth > maxWidth)
                    maxWidth = lineWidth;
                lineWidth = 0.0f;
                ++lines;
                continue;
            }
            if (codepoint < IMGUI_FONT_FIRST_CHAR || codepoint >= IMGUI_FONT_FIRST_CHAR + IMGUI_FONT_CHAR_COUNT)
                codepoint = '?';
            int glyphIndex = codepoint - IMGUI_FONT_FIRST_CHAR;
            float scale = pixelHeight / font->sdfSize;
            if (previousIndex >= 0)
                lineWidth += (float)font->kerning[previousIndex][glyphIndex] * font->kernScale * scale;
            lineWidth += font->glyphs[glyphIndex].xadvance * scale;
            previousIndex = glyphIndex;
        }
        if (lineWidth > maxWidth)
            maxWidth = lineWidth;
    }
    if (width)
        *width = maxWidth;
    if (height)
        *height = font ? lines * (font->lineHeight * pixelHeight / font->pixelHeight) : 0.0f;
}

void imgui_draw_text(const imgui_font *font, VmString *text, float x, float y, imgui_color color, int layer)
{
    imgui_draw_text_at(font, text, font ? font->pixelHeight : 0.0f, x, y, color, layer);
}

void imgui_draw_text_at(const imgui_font *font, VmString *text, float pixelHeight,
                        float x, float y, imgui_color color, int layer)
{
    if (!font || !text)
        return;
    float originX = x;
    float relativeScale = pixelHeight / font->sdfSize;
    float ascender = font->ascender / font->pixelHeight * pixelHeight;
    float lineY = y;
    int previousIndex = -1;
    int index = 0;
    int codepoint;
    while ((codepoint = _imgui_utf16_next(text, &index)) >= 0)
    {
        if (codepoint == '\n')
        {
            x = originX;
            lineY += font->lineHeight * relativeScale;
            previousIndex = -1;
            continue;
        }
        if (codepoint < IMGUI_FONT_FIRST_CHAR || codepoint >= IMGUI_FONT_FIRST_CHAR + IMGUI_FONT_CHAR_COUNT)
            codepoint = '?';
        const imgui_font_glyph *glyph = &font->glyphs[codepoint - IMGUI_FONT_FIRST_CHAR];
        int glyphIndex = codepoint - IMGUI_FONT_FIRST_CHAR;
        if (previousIndex >= 0)
            x += (float)font->kerning[previousIndex][glyphIndex] * font->kernScale * relativeScale;
        float width = (float)glyph->width * relativeScale;
        float height = (float)glyph->height * relativeScale;
        if (width > 0.0f && height > 0.0f)
        {
            _imgui_draw_font_glyph(font,
                                   floorf(x + glyph->xoff * relativeScale + 0.5f),
                                   floorf(lineY + ascender + glyph->yoff * relativeScale + 0.5f),
                                   width, height,
                                   ((float)glyph->atlasX + 0.5f) / font->atlasWidth,
                                   ((float)glyph->atlasY + 0.5f) / font->atlasHeight,
                                   ((float)(glyph->atlasX + glyph->atlasPixelWidth) - 0.5f) / font->atlasWidth,
                                   ((float)(glyph->atlasY + glyph->atlasPixelHeight) - 0.5f) / font->atlasHeight,
                                   color, layer);
        }
        x += glyph->xadvance * relativeScale;
        previousIndex = glyphIndex;
    }
}

void imgui_debug_draw_text_guides(const imgui_font *font, VmString *text, float pixelHeight,
                                  float x, float y, int layer)
{
    if (!font || !text || pixelHeight <= 0.0f)
        return;

    float width = 0.0f;
    float height = 0.0f;
    imgui_text_size_at(font, text, pixelHeight, &width, &height);
    float ascender = font->ascender / font->pixelHeight * pixelHeight;
    float descent = font->descender / font->pixelHeight * pixelHeight;
    float baseline = y + ascender;
    float lineBottom = baseline - descent;

    engine_draw_ui_rect(x, y, width, 1.0f, 80, 160, 255, 180, layer);
    engine_draw_ui_rect(x, y + height, width, 1.0f, 255, 120, 80, 180, layer);
    engine_draw_ui_rect(x, baseline, width, 1.0f, 80, 255, 120, 220, layer);
    engine_draw_ui_rect(x, lineBottom, width, 1.0f, 255, 220, 80, 180, layer);
    engine_draw_ui_rect(x, y, 1.0f, height, 120, 120, 255, 180, layer);
    engine_draw_ui_rect(x + width, y, 1.0f, height, 255, 120, 120, 180, layer);
}

void imgui_debug_draw_atlas(const imgui_font *font, float x, float y, float width, float height, int layer)
{
    if (!font || !font->texture || width <= 0.0f || height <= 0.0f)
        return;
    _imgui_draw_font_atlas(font, x, y, width, height, layer);
}
