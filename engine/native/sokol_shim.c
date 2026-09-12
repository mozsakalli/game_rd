// ---------------------------------------------------------------------------
// sokol_shim.c — DigitoyEngine.Sokol (engine/managed/Sokol.cs) icin C govdeleri
// ---------------------------------------------------------------------------
//
// Bu dosya, langtest C# derleyicisinin uretecegi `extern` cagrilarin C tarafini
// saglar. Ince, mantiksiz bir katmandir: her fonksiyon duz skaler/pointer
// argumanlari alir, sokol_gfx desc'ini kurar ve TEK bir sokol cagrisi yapar.
//
// API SURUMU: engine/native/sokol/sokol_gfx.h (YENI sokol). Cagrilar
// test-project/engine/engine.c icindeki KANITLANMIS kullanimdan birebir alindi:
//   - sg_apply_uniforms(int ub_slot, const sg_range*)
//   - sg_apply_bindings(&bind) : instance = 2. VERTEX buffer, doku = views[0]
//   - sg_append_buffer(sg_buffer, const sg_range*)
//   - sg_begin_pass(const sg_pass*) : swapchain host'tan gelir
//   - sg_buffer_desc.usage.{vertex_buffer|index_buffer|stream_update|immutable}
//
// SEMBOL ADLARI: Sokol.cs'teki [DllImport(..., EntryPoint="de_sokol_*")] adlariyla
// birebir ayni. langtest AOT bu EntryPoint'i dogrudan emit eder (mangling yok);
// gercek .NET host ise platform native lib'inden bu adla yukler.
//
// SWAPCHAIN: shim host BILMEZ. Pass'in tum alanlari (hedef framebuffer, boyut,
// sample count, clear) C#'tan duz argumanlarla gelir; shim yalnizca sg_pass'e
// kopyalar. Nereye/nasil cizilecegine C# karar verir.
// ---------------------------------------------------------------------------

#include "sokol/sokol_gfx.h"
#include <stddef.h>
#include <string.h>

#define STB_TRUETYPE_IMPLEMENTATION
#define STBTT_STATIC
#include "stb_truetype.h"

#define STB_IMAGE_IMPLEMENTATION
#define STBI_STATIC
#include "stb_image.h"

#if defined(DE_BUILD_DLL)
#if defined(_WIN32)
#define SOKOL_API __declspec(dllexport)
#endif
#else
#define SOKOL_API
#endif

// ---------------------------------------------------------------------------
// Pipeline / bindings / uniforms
// ---------------------------------------------------------------------------

SOKOL_API void de_sokol_apply_pipeline(unsigned int pip)
{
    sg_pipeline p = {pip};
    sg_apply_pipeline(p);
}

SOKOL_API void de_sokol_apply_bindings(
    unsigned int vertexBuffer,
    unsigned int instanceBuffer, int instanceOffset,
    unsigned int indexBuffer,
    unsigned int textureView, unsigned int sampler)
{
    sg_bindings b = {0};
    b.vertex_buffers[0].id = vertexBuffer;
    b.vertex_buffers[1].id = instanceBuffer; // instance = 2. vertex buffer (PER_INSTANCE)
    b.vertex_buffer_offsets[1] = instanceOffset;
    b.index_buffer.id = indexBuffer;
    b.views[0].id = textureView; // yeni sokol: doku bir VIEW
    b.samplers[0].id = sampler;
    sg_apply_bindings(&b);
}

// data 'byteSize' uzunlugunda ham uniform verisi.
SOKOL_API void de_sokol_apply_uniforms(int slot, void *data, int byteSize)
{
    sg_range r = {data, (size_t)byteSize};
    sg_apply_uniforms(slot, &r);
}

SOKOL_API void de_sokol_draw(int baseElement, int numElements, int numInstances)
{
    sg_draw(baseElement, numElements, numInstances);
}

SOKOL_API int de_sokol_append_buffer(unsigned int buffer, void *data, int byteSize)
{
    sg_buffer buf = {buffer};
    sg_range r = {data, (size_t)byteSize};
    return sg_append_buffer(buf, &r);
}

// ---------------------------------------------------------------------------
// Viewport / scissor  (bool -> C int)
// ---------------------------------------------------------------------------

SOKOL_API void de_sokol_apply_viewport(
    int x, int y, int width, int height, int originTopLeft)
{
    sg_apply_viewport(x, y, width, height, originTopLeft != 0);
}

SOKOL_API void de_sokol_apply_scissor_rect(
    int x, int y, int width, int height, int originTopLeft)
{
    sg_apply_scissor_rect(x, y, width, height, originTopLeft != 0);
}

// ---------------------------------------------------------------------------
// Pass yasam dongusu  (swapchain pass'i, tum alanlar C#'tan)
// ---------------------------------------------------------------------------

// Tum pass'i C# kurar: clear bayraklari + degerleri ve swapchain (hedef fbo,
// boyut, sample count). framebuffer=0 => GL varsayilan ekran; !=0 => offscreen fbo.
SOKOL_API void de_sokol_begin_pass(
    int clearColor, float clearR, float clearG, float clearB, float clearA,
    int clearDepth, float depthValue,
    int width, int height, int sampleCount, unsigned int framebuffer)
{
    sg_pass pass = {0};
    pass.action.colors[0].load_action = clearColor ? SG_LOADACTION_CLEAR : SG_LOADACTION_LOAD;
    pass.action.colors[0].clear_value.r = clearR;
    pass.action.colors[0].clear_value.g = clearG;
    pass.action.colors[0].clear_value.b = clearB;
    pass.action.colors[0].clear_value.a = clearA;
    pass.action.depth.load_action = clearDepth ? SG_LOADACTION_CLEAR : SG_LOADACTION_LOAD;
    pass.action.depth.clear_value = depthValue;
    pass.swapchain.width = width;
    pass.swapchain.height = height;
    pass.swapchain.sample_count = sampleCount;
    pass.swapchain.gl.framebuffer = framebuffer;
    sg_begin_pass(&pass);
}

SOKOL_API void de_sokol_end_pass(void)
{
    sg_end_pass();
}

// Offscreen pass: hedef attachment VIEW'lari (de_sokol_make_view color/depth).
// Boyut/format attachment'lardan turetilir. depthView=0 => derinliksiz RT.
SOKOL_API void de_sokol_begin_pass_offscreen(
    int clearColor, float clearR, float clearG, float clearB, float clearA,
    int clearDepth, float depthValue,
    unsigned int colorView, unsigned int depthView)
{
    sg_pass pass = {0};
    pass.action.colors[0].load_action = clearColor ? SG_LOADACTION_CLEAR : SG_LOADACTION_LOAD;
    pass.action.colors[0].clear_value.r = clearR;
    pass.action.colors[0].clear_value.g = clearG;
    pass.action.colors[0].clear_value.b = clearB;
    pass.action.colors[0].clear_value.a = clearA;
    pass.action.depth.load_action = clearDepth ? SG_LOADACTION_CLEAR : SG_LOADACTION_LOAD;
    pass.action.depth.clear_value = depthValue;
    pass.attachments.colors[0].id = colorView;
    pass.attachments.depth_stencil.id = depthView;
    sg_begin_pass(&pass);
}

SOKOL_API void de_sokol_commit(void)
{
    sg_commit();
}

// ---------------------------------------------------------------------------
// Stream buffer olustur / yok et
// ---------------------------------------------------------------------------

SOKOL_API unsigned int de_sokol_make_stream_buffer(int byteSize, int isIndex)
{
    sg_buffer_desc d = {0};
    d.usage.immutable = false;
    d.usage.stream_update = true;
    if (isIndex != 0)
        d.usage.index_buffer = true;
    else
        d.usage.vertex_buffer = true;
    d.size = (size_t)byteSize;
    return sg_make_buffer(&d).id;
}

SOKOL_API void de_sokol_destroy_buffer(unsigned int buffer)
{
    sg_buffer buf = {buffer};
    sg_destroy_buffer(buf);
}

// ---------------------------------------------------------------------------
// Kurulum / durum / frame (ek)
// ---------------------------------------------------------------------------
// GL context AKTIF olduktan sonra cagrilir; sokol GL fonksiyonlarini kendi yukler.
// Sifirli desc => GL backend varsayilanlari (RGBA8 / DEPTH_STENCIL / sample 1).

// Sokol logger: logger baglanmazsa validation hatalari SESSIZCE abort eder.
// Her mesaj stderr'e (flush'li) + crash_native.log'a yazilir; panic'te bile iz kalir.
#include <stdio.h>
static void de_sokol_log(const char *tag, uint32_t level, uint32_t item_id,
                         const char *msg, uint32_t line, const char *file, void *user)
{
    (void)user;
    const char *lvl = level == 0 ? "PANIC" : level == 1 ? "ERROR"
                                         : level == 2   ? "WARN"
                                                        : "INFO";
    char buf[1024];
    snprintf(buf, sizeof buf, "[sokol %s] %s (%u) %s:%u %s\n",
             lvl, tag ? tag : "?", item_id, file ? file : "?", line, msg ? msg : "");
    fputs(buf, stderr);
    fflush(stderr);
    FILE *f = fopen("crash_native.log", "a");
    if (f)
    {
        fputs(buf, f);
        fclose(f);
    }
}

SOKOL_API void de_sokol_setup(void)
{
    sg_desc d = {0};
    d.logger.func = de_sokol_log;
    sg_setup(&d);
}

static void de_render_reset_state(void); // decoder statiklerini sifirla (asagida)

SOKOL_API void de_sokol_shutdown(void)
{
    sg_shutdown();
    de_render_reset_state();
}
SOKOL_API int de_sokol_is_valid(void) { return sg_isvalid() ? 1 : 0; }
SOKOL_API void de_sokol_reset_state_cache(void) { sg_reset_state_cache(); }
SOKOL_API int de_sokol_query_backend(void) { return (int)sg_query_backend(); }
SOKOL_API void de_sokol_push_debug_group(const char *name) { sg_push_debug_group(name); }
SOKOL_API void de_sokol_pop_debug_group(void) { sg_pop_debug_group(); }

SOKOL_API void de_sokol_apply_viewportf(
    float x, float y, float width, float height, int originTopLeft)
{
    sg_apply_viewportf(x, y, width, height, originTopLeft != 0);
}

SOKOL_API void de_sokol_apply_scissor_rectf(
    float x, float y, float width, float height, int originTopLeft)
{
    sg_apply_scissor_rectf(x, y, width, height, originTopLeft != 0);
}

SOKOL_API void de_sokol_draw_ex(
    int baseElement, int numElements, int numInstances, int baseVertex, int baseInstance)
{
    sg_draw_ex(baseElement, numElements, numInstances, baseVertex, baseInstance);
}

SOKOL_API void de_sokol_dispatch(int numGroupsX, int numGroupsY, int numGroupsZ)
{
    sg_dispatch(numGroupsX, numGroupsY, numGroupsZ);
}

// ---------------------------------------------------------------------------
// Buffer / image / sampler / view olustur (genel)
// ---------------------------------------------------------------------------

// bufferType: 0=vertex, 1=index. dynamism: 0=immutable(data), 1=dynamic, 2=stream.
SOKOL_API unsigned int de_sokol_make_buffer(
    void *data, int size, int bufferType, int dynamism)
{
    sg_buffer_desc d = {0};
    if (bufferType == 1)
        d.usage.index_buffer = true;
    else
        d.usage.vertex_buffer = true;
    if (dynamism == 0)
    {
        d.usage.immutable = true;
        d.data.ptr = data;
        d.data.size = (size_t)size;
    }
    else
    {
        d.usage.immutable = false;
        if (dynamism == 2)
            d.usage.stream_update = true;
        else
            d.usage.dynamic_update = true;
        d.size = (size_t)size;
    }
    return sg_make_buffer(&d).id;
}

SOKOL_API void de_sokol_update_buffer(unsigned int buffer, void *data, int size)
{
    sg_buffer b = {buffer};
    sg_range r = {data, (size_t)size};
    sg_update_buffer(b, &r);
}

SOKOL_API int de_sokol_query_buffer_overflow(unsigned int buffer)
{
    sg_buffer b = {buffer};
    return sg_query_buffer_overflow(b) ? 1 : 0;
}

// usage: 0=immutable(data), 1=dynamic, 2=color_attachment, 3=depth_stencil_attachment.
SOKOL_API unsigned int de_sokol_make_image(
    int width, int height, int pixelFormat, int numMipmaps, int sampleCount,
    int usage, void *data, int dataSize)
{
    sg_image_desc d = {0};
    d.type = SG_IMAGETYPE_2D;
    d.width = width;
    d.height = height;
    d.pixel_format = (sg_pixel_format)pixelFormat;
    d.num_mipmaps = numMipmaps > 0 ? numMipmaps : 1;
    d.sample_count = sampleCount > 0 ? sampleCount : 1;
    switch (usage)
    {
    case 2:
        d.usage.color_attachment = true;
        d.usage.immutable = false;
        break;
    case 3:
        d.usage.depth_stencil_attachment = true;
        d.usage.immutable = false;
        break;
    case 1:
        d.usage.dynamic_update = true;
        d.usage.immutable = false;
        break;
    default:
        d.usage.immutable = true;
        if (data && dataSize > 0)
        {
            d.data.mip_levels[0].ptr = data;
            d.data.mip_levels[0].size = (size_t)dataSize;
        }
        break;
    }
    return sg_make_image(&d).id;
}

// Mip 0 tek yuzey guncelle (dynamic image).
SOKOL_API void de_sokol_update_image(unsigned int image, void *data, int size)
{
    sg_image im = {image};
    sg_image_data d = {0};
    d.mip_levels[0].ptr = data;
    d.mip_levels[0].size = (size_t)size;
    sg_update_image(im, &d);
}

SOKOL_API unsigned int de_sokol_make_sampler(
    int minFilter, int magFilter, int mipmapFilter,
    int wrapU, int wrapV, int wrapW, int compare)
{
    sg_sampler_desc d = {0};
    d.min_filter = (sg_filter)minFilter;
    d.mag_filter = (sg_filter)magFilter;
    d.mipmap_filter = (sg_filter)mipmapFilter;
    d.wrap_u = (sg_wrap)wrapU;
    d.wrap_v = (sg_wrap)wrapV;
    d.wrap_w = (sg_wrap)wrapW;
    d.compare = (sg_compare_func)compare;
    return sg_make_sampler(&d).id;
}

// viewType: 0=texture, 1=color_attachment, 2=depth_stencil_attachment.
SOKOL_API unsigned int de_sokol_make_view(unsigned int imageId, int viewType)
{
    sg_image img = {imageId};
    sg_view_desc d = {0};
    switch (viewType)
    {
    case 1:
        d.color_attachment.image = img;
        break;
    case 2:
        d.depth_stencil_attachment.image = img;
        break;
    default:
        d.texture.image = img;
        break;
    }
    return sg_make_view(&d).id;
}

SOKOL_API void de_sokol_destroy_image(unsigned int id)
{
    sg_image h = {id};
    sg_destroy_image(h);
}
SOKOL_API void de_sokol_destroy_sampler(unsigned int id)
{
    sg_sampler h = {id};
    sg_destroy_sampler(h);
}
SOKOL_API void de_sokol_destroy_shader(unsigned int id)
{
    sg_shader h = {id};
    sg_destroy_shader(h);
}
SOKOL_API void de_sokol_destroy_pipeline(unsigned int id)
{
    sg_pipeline h = {id};
    sg_destroy_pipeline(h);
}
SOKOL_API void de_sokol_destroy_view(unsigned int id)
{
    sg_view h = {id};
    sg_destroy_view(h);
}

// ---------------------------------------------------------------------------
// Shader builder — sg_shader_desc coklu-alanli oldugundan artimli setter'larla
// bir scratch desc doldurulup end()'de sg_make_shader cagrilir. GL alanlari
// (glsl_name / base_type / uniform layout) doldurulur; msl_*/hlsl_* GL'de yok sayilir.
// ---------------------------------------------------------------------------
static sg_shader_desc _de_sd;

SOKOL_API void de_sokol_shader_begin(void)
{
    sg_shader_desc z = {0};
    _de_sd = z;
}
SOKOL_API void de_sokol_shader_vertex_source(const char *src) { _de_sd.vertex_func.source = src; }
SOKOL_API void de_sokol_shader_fragment_source(const char *src) { _de_sd.fragment_func.source = src; }
SOKOL_API void de_sokol_shader_vertex_entry(const char *e) { _de_sd.vertex_func.entry = e; }
SOKOL_API void de_sokol_shader_fragment_entry(const char *e) { _de_sd.fragment_func.entry = e; }
SOKOL_API void de_sokol_shader_attr(int index, const char *glslName, int baseType)
{
    _de_sd.attrs[index].glsl_name = glslName;
    _de_sd.attrs[index].base_type = (sg_shader_attr_base_type)baseType;
}
SOKOL_API void de_sokol_shader_uniform_block(int slot, int stage, int size, int layout)
{
    _de_sd.uniform_blocks[slot].stage = (sg_shader_stage)stage;
    _de_sd.uniform_blocks[slot].size = (size_t)size;
    _de_sd.uniform_blocks[slot].layout = (sg_uniform_layout)layout;
}
SOKOL_API void de_sokol_shader_uniform(int blockSlot, int index, int type, const char *glslName)
{
    _de_sd.uniform_blocks[blockSlot].glsl_uniforms[index].type = (sg_uniform_type)type;
    _de_sd.uniform_blocks[blockSlot].glsl_uniforms[index].glsl_name = glslName;
}
SOKOL_API void de_sokol_shader_texture_view(int slot, int stage, int imageType, int sampleType)
{
    _de_sd.views[slot].texture.stage = (sg_shader_stage)stage;
    _de_sd.views[slot].texture.image_type = (sg_image_type)imageType;
    _de_sd.views[slot].texture.sample_type = (sg_image_sample_type)sampleType;
}
SOKOL_API void de_sokol_shader_sampler(int slot, int stage, int samplerType)
{
    _de_sd.samplers[slot].stage = (sg_shader_stage)stage;
    _de_sd.samplers[slot].sampler_type = (sg_sampler_type)samplerType;
}
SOKOL_API void de_sokol_shader_texture_sampler_pair(
    int slot, int stage, int viewSlot, int samplerSlot, const char *glslName)
{
    _de_sd.texture_sampler_pairs[slot].stage = (sg_shader_stage)stage;
    _de_sd.texture_sampler_pairs[slot].view_slot = viewSlot;
    _de_sd.texture_sampler_pairs[slot].sampler_slot = samplerSlot;
    _de_sd.texture_sampler_pairs[slot].glsl_name = glslName;
}
SOKOL_API unsigned int de_sokol_shader_end(void) { return sg_make_shader(&_de_sd).id; }

// ---------------------------------------------------------------------------
// Pipeline builder — ayni artimli desen (scratch sg_pipeline_desc).
// ---------------------------------------------------------------------------
static sg_pipeline_desc _de_pd;

SOKOL_API void de_sokol_pipeline_begin(void)
{
    sg_pipeline_desc z = {0};
    _de_pd = z;
}

// ---------------------------------------------------------------------------
// SDF font baker — eski imgui_font.c ile birebir parametreler (kanitlanmis):
// atlas 2048x2048 alpha, hucre 128, SDF taban boyutu 32px, spread 21, pad 6,
// codepoint 32..255 (224 glyph) + tam glyph-cifti kerning tablosu.
// TEK cagri: cagiran tum tamponlari ayirir, baker doldurur.
//   atlas:    atlasSize*atlasSize bayt (tek kanal SDF)
//   glyphOut: glyph basina 9 float: advance, xoff, yoff, w, h, atlasX, atlasY (7 kullanilan + 2 rezerv)
//   kernOut:  224*224 int16 (font-unit; kernScale ile carpilir)
//   metrics:  [0]=ascent [1]=descent [2]=lineHeight (SDF px) [3]=kernScale [4]=sdfSize
// Donus: yazilan glyph sayisi (hata: 0).
// ---------------------------------------------------------------------------

#define DE_FONT_FIRST 32
#define DE_FONT_COUNT 224
#define DE_FONT_SDF_SIZE 32.0f
#define DE_FONT_SDF_SPREAD 21.0f
#define DE_FONT_SDF_PAD 6
#define DE_FONT_CELL 128

SOKOL_API int de_font_bake(
    const unsigned char *ttf,
    unsigned char *atlas, int atlasSize,
    float *glyphOut, short *kernOut, float *metricsOut)
{
    stbtt_fontinfo info;
    if (!stbtt_InitFont(&info, ttf, 0))
        return 0;

    memset(atlas, 0, (size_t)atlasSize * (size_t)atlasSize);
    float scale = stbtt_ScaleForPixelHeight(&info, DE_FONT_SDF_SIZE);
    int columns = atlasSize / DE_FONT_CELL;
    int glyphIndices[DE_FONT_COUNT];
    int count = 0;

    for (int i = 0; i < DE_FONT_COUNT; ++i)
    {
        int codepoint = DE_FONT_FIRST + i;
        int glyphIndex = stbtt_FindGlyphIndex(&info, codepoint);
        glyphIndices[i] = glyphIndex;
        int advance = 0, lsb = 0, x0 = 0, y0 = 0;
        stbtt_GetGlyphHMetrics(&info, glyphIndex, &advance, &lsb);
        int sdfW = 0, sdfH = 0;
        unsigned char *sdf = stbtt_GetCodepointSDF(&info, scale, codepoint,
                                                   DE_FONT_SDF_PAD, 128, DE_FONT_SDF_SPREAD, &sdfW, &sdfH, &x0, &y0);
        int column = i % columns;
        int row = i / columns;
        int atlasX = column * DE_FONT_CELL;
        int atlasY = row * DE_FONT_CELL;
        if (atlasY + DE_FONT_CELL > atlasSize)
        {
            stbtt_FreeSDF(sdf, NULL);
            break;
        }
        float *g = glyphOut + i * 9;
        g[0] = scale * (float)advance; // xadvance (SDF px)
        g[1] = (float)x0;              // xoff
        g[2] = (float)y0;              // yoff
        g[3] = (float)sdfW;            // piksel genislik
        g[4] = (float)sdfH;            // piksel yukseklik
        g[5] = (float)atlasX;
        g[6] = (float)atlasY;
        g[7] = 0.0f;
        g[8] = 0.0f;
        if (sdf && sdfW > 0 && sdfH > 0)
            for (int r = 0; r < sdfH; ++r)
                memcpy(atlas + atlasX + (size_t)(atlasY + r) * atlasSize, sdf + (size_t)r * sdfW, (size_t)sdfW);
        stbtt_FreeSDF(sdf, NULL);
        count++;
    }

    for (int left = 0; left < DE_FONT_COUNT; ++left)
        for (int right = 0; right < DE_FONT_COUNT; ++right)
            kernOut[left * DE_FONT_COUNT + right] =
                (short)stbtt_GetGlyphKernAdvance(&info, glyphIndices[left], glyphIndices[right]);

    int ascent = 0, descent = 0, lineGap = 0;
    stbtt_GetFontVMetrics(&info, &ascent, &descent, &lineGap);
    metricsOut[0] = scale * (float)ascent;
    metricsOut[1] = scale * (float)descent;
    metricsOut[2] = scale * (float)(ascent - descent + lineGap);
    metricsOut[3] = scale;
    metricsOut[4] = DE_FONT_SDF_SIZE;
    return count;
}
SOKOL_API void de_sokol_pipeline_shader(unsigned int shader)
{
    sg_shader h = {shader};
    _de_pd.shader = h;
}
SOKOL_API void de_sokol_pipeline_index_type(int t) { _de_pd.index_type = (sg_index_type)t; }
SOKOL_API void de_sokol_pipeline_primitive_type(int t) { _de_pd.primitive_type = (sg_primitive_type)t; }
SOKOL_API void de_sokol_pipeline_cull_mode(int m) { _de_pd.cull_mode = (sg_cull_mode)m; }
SOKOL_API void de_sokol_pipeline_face_winding(int w) { _de_pd.face_winding = (sg_face_winding)w; }
SOKOL_API void de_sokol_pipeline_depth(int compare, int writeEnabled)
{
    _de_pd.depth.compare = (sg_compare_func)compare;
    _de_pd.depth.write_enabled = writeEnabled != 0;
}
SOKOL_API void de_sokol_pipeline_color_count(int n) { _de_pd.color_count = n; }
SOKOL_API void de_sokol_pipeline_buffer(int bufferIndex, int stride, int stepFunc)
{
    _de_pd.layout.buffers[bufferIndex].stride = stride;
    if (stepFunc != 0)
        _de_pd.layout.buffers[bufferIndex].step_func = (sg_vertex_step)stepFunc;
}
SOKOL_API void de_sokol_pipeline_attr(int attrIndex, int bufferIndex, int offset, int format)
{
    _de_pd.layout.attrs[attrIndex].buffer_index = bufferIndex;
    _de_pd.layout.attrs[attrIndex].offset = offset;
    _de_pd.layout.attrs[attrIndex].format = (sg_vertex_format)format;
}
SOKOL_API void de_sokol_pipeline_color_blend(
    int colorIndex, int enabled,
    int srcRgb, int dstRgb, int opRgb, int srcAlpha, int dstAlpha, int opAlpha)
{
    _de_pd.colors[colorIndex].blend.enabled = enabled != 0;
    _de_pd.colors[colorIndex].blend.src_factor_rgb = (sg_blend_factor)srcRgb;
    _de_pd.colors[colorIndex].blend.dst_factor_rgb = (sg_blend_factor)dstRgb;
    _de_pd.colors[colorIndex].blend.op_rgb = (sg_blend_op)opRgb;
    _de_pd.colors[colorIndex].blend.src_factor_alpha = (sg_blend_factor)srcAlpha;
    _de_pd.colors[colorIndex].blend.dst_factor_alpha = (sg_blend_factor)dstAlpha;
    _de_pd.colors[colorIndex].blend.op_alpha = (sg_blend_op)opAlpha;
}
SOKOL_API unsigned int de_sokol_pipeline_end(void) { return sg_make_pipeline(&_de_pd).id; }

// ---------------------------------------------------------------------------
// RenderExecute — C# CommandBuffer akisinin decoder'i.
//
// AKIS FORMATI engine/managed/RenderCommands.cs (RenderCmd enum) ile BIREBIR:
// her komut = 1 bayt opcode + sabit payload; SetUniforms/UploadInstances
// payload sonunda 'byteSize' kadar inline veri tasir. Little-endian, hizasiz
// (memcpy ile okunur, ARM guvenli). Akis pointer'i submit boyunca gecerli
// oldugundan inline uniform verisi kopyasiz sg_apply_uniforms'a verilir.
//
// FRAME INSTANCE BUFFER: decoder'in sahip oldugu tek stream vertex buffer.
// UploadInstances blogu buraya sg_append_buffer ile eklenir; SetBindings'te
// instanceBuffer=0 sentineli bu buffer'i secer ve instanceOffset son blogun
// tabanina eklenir. Kapasite yetmezse buffer buyutulerek yeniden yaratilir
// (o frame'in onceki bloklari kaybolur — nadir, tek frame'lik).
// ---------------------------------------------------------------------------

static sg_buffer _de_inst_buf; // frame instance buffer (0 = henuz yok)
static int _de_inst_cap;       // buffer kapasitesi (bayt)
static int _de_inst_used;      // bu frame append edilen toplam bayt
static int _de_inst_block;     // son UploadInstances blogunun buffer ofseti

static void de_render_reset_state(void)
{
    _de_inst_buf.id = 0; // sg_shutdown zaten yok etti, sadece unut
    _de_inst_cap = 0;
    _de_inst_used = 0;
    _de_inst_block = 0;
}

static void de_inst_ensure(int neededTotal)
{
    if (_de_inst_buf.id != 0 && neededTotal <= _de_inst_cap)
        return;
    int cap = _de_inst_cap > 0 ? _de_inst_cap : 256 * 1024;
    while (cap < neededTotal)
        cap *= 2;
    if (_de_inst_buf.id != 0)
        sg_destroy_buffer(_de_inst_buf);
    sg_buffer_desc d = {0};
    d.usage.vertex_buffer = true;
    d.usage.stream_update = true;
    d.size = (size_t)cap;
    _de_inst_buf = sg_make_buffer(&d);
    _de_inst_cap = cap;
    _de_inst_used = 0;
}

// Hizasiz little-endian okuyucular (akis pointer'ini ilerletir).
static unsigned char de_r8(const unsigned char **p) { return *(*p)++; }
static int de_ri32(const unsigned char **p)
{
    int v;
    memcpy(&v, *p, 4);
    *p += 4;
    return v;
}
static unsigned int de_ru32(const unsigned char **p)
{
    unsigned int v;
    memcpy(&v, *p, 4);
    *p += 4;
    return v;
}
static float de_rf32(const unsigned char **p)
{
    float v;
    memcpy(&v, *p, 4);
    *p += 4;
    return v;
}

static void de_render_execute_core(
    void *stream, int length, int maxDraws,
    unsigned int overrideColorView, unsigned int overrideDepthView);

SOKOL_API void de_sokol_render_execute(void *stream, int length)
{
    de_render_execute_core(stream, length, -1, 0, 0);
}

// Frame debugger yolu: maxDraws>=0 ise N'inci draw'dan sonrasi ATLANIR (pass/state
// komutlari yine islenir — hedefler dogru kalir). overrideColorView!=0 ise
// SWAPCHAIN pass'leri (fbo==0) o attachment'lara yonlendirilir (replay ekrana
// degil RT'ye cizilir). Normal yolun maliyeti: draw basina tek karsilastirma.
SOKOL_API void de_sokol_render_execute_dbg(
    void *stream, int length, int maxDraws,
    unsigned int overrideColorView, unsigned int overrideDepthView)
{
    de_render_execute_core(stream, length, maxDraws, overrideColorView, overrideDepthView);
}

static void de_render_execute_core(
    void *stream, int length, int maxDraws,
    unsigned int overrideColorView, unsigned int overrideDepthView)
{
    const unsigned char *p = (const unsigned char *)stream;
    const unsigned char *end = p + length;
    int drawCount = 0;
    _de_inst_used = 0; // frame'de bir kez cagrilir; append ofsetleri sifirdan baslar
    _de_inst_block = 0;

    while (p < end)
    {
        switch (de_r8(&p))
        {
        case 1: // BeginPass
        {
            int clearColor = de_r8(&p);
            float r = de_rf32(&p), g = de_rf32(&p), b = de_rf32(&p), a = de_rf32(&p);
            int clearDepth = de_r8(&p);
            float depthValue = de_rf32(&p);
            int w = de_ri32(&p), h = de_ri32(&p), sc = de_ri32(&p);
            unsigned int fbo = de_ru32(&p);
            if (fbo == 0 && overrideColorView != 0)
                de_sokol_begin_pass_offscreen(clearColor, r, g, b, a, clearDepth, depthValue,
                                              overrideColorView, overrideDepthView);
            else
                de_sokol_begin_pass(clearColor, r, g, b, a, clearDepth, depthValue, w, h, sc, fbo);
            break;
        }
        case 2: // EndPass
            sg_end_pass();
            break;
        case 3: // SetPipeline
        {
            sg_pipeline pip = {de_ru32(&p)};
            sg_apply_pipeline(pip);
            break;
        }
        case 4: // SetBindings
        {
            unsigned int vbo = de_ru32(&p);
            unsigned int inst = de_ru32(&p);
            int instOff = de_ri32(&p);
            unsigned int ibo = de_ru32(&p);
            unsigned int view = de_ru32(&p);
            unsigned int smp = de_ru32(&p);
            if (inst == 0) // sentinel: decoder'in frame instance buffer'i
            {
                inst = _de_inst_buf.id;
                instOff += _de_inst_block;
            }
            de_sokol_apply_bindings(vbo, inst, instOff, ibo, view, smp);
            break;
        }
        case 5: // SetUniforms — inline veri akistan kopyasiz uygulanir
        {
            int slot = de_ri32(&p);
            int size = de_ri32(&p);
            sg_range r = {p, (size_t)size};
            sg_apply_uniforms(slot, &r);
            p += size;
            break;
        }
        case 6: // SetViewport
        {
            int x = de_ri32(&p), y = de_ri32(&p), w = de_ri32(&p), h = de_ri32(&p);
            int topLeft = de_r8(&p);
            sg_apply_viewport(x, y, w, h, topLeft != 0);
            break;
        }
        case 7: // SetScissor
        {
            int x = de_ri32(&p), y = de_ri32(&p), w = de_ri32(&p), h = de_ri32(&p);
            int topLeft = de_r8(&p);
            sg_apply_scissor_rect(x, y, w, h, topLeft != 0);
            break;
        }
        case 8: // Draw
        {
            int base = de_ri32(&p), num = de_ri32(&p), ninst = de_ri32(&p);
            if (maxDraws < 0 || drawCount < maxDraws)
                sg_draw(base, num, ninst);
            drawCount++;
            break;
        }
        case 9: // DrawEx
        {
            int base = de_ri32(&p), num = de_ri32(&p), ninst = de_ri32(&p);
            int baseVtx = de_ri32(&p), baseInst = de_ri32(&p);
            if (maxDraws < 0 || drawCount < maxDraws)
                sg_draw_ex(base, num, ninst, baseVtx, baseInst);
            drawCount++;
            break;
        }
        case 10: // UploadInstances
        {
            int size = de_ri32(&p);
            if (size > 0)
            {
                de_inst_ensure(_de_inst_used + size);
                sg_range r = {p, (size_t)size};
                _de_inst_block = sg_append_buffer(_de_inst_buf, &r);
                _de_inst_used += size;
            }
            p += size;
            break;
        }
        case 11: // BeginPassOffscreen
        {
            int clearColor = de_r8(&p);
            float r = de_rf32(&p), g = de_rf32(&p), b = de_rf32(&p), a = de_rf32(&p);
            int clearDepth = de_r8(&p);
            float depthValue = de_rf32(&p);
            unsigned int colorView = de_ru32(&p);
            unsigned int depthView = de_ru32(&p);
            de_sokol_begin_pass_offscreen(clearColor, r, g, b, a, clearDepth, depthValue, colorView, depthView);
            break;
        }
        default: // bilinmeyen opcode: akis bozuk, devam etmek anlamsiz
            return;
        }
    }
}

// ---------------------------------------------------------------------------
// Raster glyph (UI text): SDF kucuk puntoda camur — UI icin glyph GERCEK piksel
// boyutunda rasterize edilir (fontstash/Dear ImGui modeli). Codepoint bazli:
// dinamik atlas managed tarafta, Turkce dahil her karakter ihtiyac aninda.
// ---------------------------------------------------------------------------

// metrics[5] = advance, bearingX, bearingY(ustun baseline'a ofseti, negatif), w, h.
// buffer'a w*h gri piksel (satir 0 ustte). Donus: 1 ok, 0 hata/sigmadi.
SOKOL_API int de_font_glyph(
    const unsigned char *ttf, float pixelHeight, int codepoint,
    unsigned char *buffer, int bufferSize, float *metrics)
{
    stbtt_fontinfo info;
    if (!stbtt_InitFont(&info, ttf, 0))
        return 0;
    float scale = stbtt_ScaleForPixelHeight(&info, pixelHeight);
    int adv, lsb;
    stbtt_GetCodepointHMetrics(&info, codepoint, &adv, &lsb);
    int x0, y0, x1, y1;
    stbtt_GetCodepointBitmapBox(&info, codepoint, scale, scale, &x0, &y0, &x1, &y1);
    int w = x1 - x0, h = y1 - y0;
    if (w < 0 || h < 0 || w * h > bufferSize)
        return 0;
    if (w > 0 && h > 0)
        stbtt_MakeCodepointBitmap(&info, buffer, w, h, w, scale, scale, codepoint);
    metrics[0] = adv * scale;
    metrics[1] = (float)x0;
    metrics[2] = (float)y0;
    metrics[3] = (float)w;
    metrics[4] = (float)h;
    return 1;
}

// out3 = ascent, descent(negatif), lineAdvance — verilen piksel boyuta olcekli.
SOKOL_API int de_font_vmetrics(const unsigned char *ttf, float pixelHeight, float *out3)
{
    stbtt_fontinfo info;
    if (!stbtt_InitFont(&info, ttf, 0))
        return 0;
    float scale = stbtt_ScaleForPixelHeight(&info, pixelHeight);
    int a, d, g;
    stbtt_GetFontVMetrics(&info, &a, &d, &g);
    out3[0] = a * scale;
    out3[1] = d * scale;
    out3[2] = (a - d + g) * scale;
    return 1;
}

// ---------------------------------------------------------------------------
// Async asset IO: dosya okuma + stb_image decode WORKER thread'de. Managed kod
// TEK thread kalir (AOT sozlesmesi) — API poll tabanli, bloklayan cagri yok.
// WASM backend'i ayni sozlesmeyi emscripten_fetch/JS ile dolduracak (thread'siz).
// ---------------------------------------------------------------------------
#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define DE_IO_MAX_JOBS 64
#define DE_IO_PATH_MAX 512

typedef struct
{
    volatile LONG state; // 0 bos, 1 bekliyor, 2 calisiyor, 3 tamam, 4 hata
    char path[DE_IO_PATH_MAX];
    long long offset, length; // length>0: pak icinden aralik oku (release)
    long long rawLength;      // != length: aralik zlib'li, once inflate
    unsigned char *pixels;    // RGBA8, satir 0 altta (GL RT yonelimi)
    int w, h;
} de_io_job_t;

static de_io_job_t de_io_jobs[DE_IO_MAX_JOBS];
static CRITICAL_SECTION de_io_lock;
static HANDLE de_io_sem;
static int de_io_started;

// DTEX (build'de onceden cozulmus texture): [magic][format][w][h][veri].
// format 0 = RGBA8 duz (memcpy); 1 = RGBA8 PNG-tarzi satir filtreli (satir basi
// 1 filtre bayti, defilter lineer). Satir 0 altta. Eslesirse stbi decode atlanir.
// Donen buffer malloc'lu — stbi_image_free (default: free) ile uyumlu.
static int de_paeth(int a, int b, int c)
{
    int p = a + b - c;
    int pa = abs(p - a), pb = abs(p - b), pc = abs(p - c);
    return pa <= pb && pa <= pc ? a : (pb <= pc ? b : c);
}

static unsigned char *de_try_dtex(const unsigned char *data, long long len, int *w, int *h)
{
    if (len < 16)
        return NULL;
    int magic, fmt, tw, th;
    memcpy(&magic, data, 4);
    if (magic != 0x58455444)
        return NULL;
    memcpy(&fmt, data + 4, 4);
    memcpy(&tw, data + 8, 4);
    memcpy(&th, data + 12, 4);
    if (tw <= 0 || th <= 0)
        return NULL;
    long long stride = (long long)tw * 4;
    if (fmt == 0)
    {
        if (stride * th != len - 16)
            return NULL;
        unsigned char *px = (unsigned char *)malloc((size_t)(stride * th));
        if (!px)
            return NULL;
        memcpy(px, data + 16, (size_t)(stride * th));
        *w = tw;
        *h = th;
        return px;
    }
    if (fmt == 1)
    {
        if ((stride + 1) * th != len - 16)
            return NULL;
        unsigned char *px = (unsigned char *)malloc((size_t)(stride * th));
        if (!px)
            return NULL;
        const unsigned char *src = data + 16;
        for (int y = 0; y < th; y++)
        {
            int f = *src++;
            unsigned char *cur = px + (size_t)y * stride;
            const unsigned char *prev = y > 0 ? cur - stride : NULL;
            for (long long x = 0; x < stride; x++)
            {
                int left = x >= 4 ? cur[x - 4] : 0;
                int up = prev ? prev[x] : 0;
                int ul = (prev && x >= 4) ? prev[x - 4] : 0;
                int pred = f == 1 ? left : f == 2 ? up
                                       : f == 3   ? ((left + up) >> 1)
                                       : f == 4   ? de_paeth(left, up, ul)
                                                  : 0;
                cur[x] = (unsigned char)(src[x] + pred);
            }
            src += stride;
        }
        *w = tw;
        *h = th;
        return px;
    }
    return NULL;
}

// Once DTEX hizli yolu, degilse stbi (png/jpg/bmp...).
static unsigned char *de_decode_pixels(const unsigned char *data, long long len, int *w, int *h, int *comp)
{
    unsigned char *px = de_try_dtex(data, len, w, h);
    return px ? px : stbi_load_from_memory(data, (int)len, w, h, comp, 4);
}

static DWORD WINAPI de_io_worker(LPVOID arg)
{
    (void)arg;
    for (;;)
    {
        WaitForSingleObject(de_io_sem, INFINITE);
        de_io_job_t *job = NULL;
        EnterCriticalSection(&de_io_lock);
        for (int i = 0; i < DE_IO_MAX_JOBS; i++)
        {
            if (de_io_jobs[i].state == 1)
            {
                de_io_jobs[i].state = 2;
                job = &de_io_jobs[i];
                break;
            }
        }
        LeaveCriticalSection(&de_io_lock);
        if (!job)
            continue;
        int w = 0, h = 0, comp = 0;
        unsigned char *px = NULL;
        if (job->length > 0)
        {
            FILE *f = fopen(job->path, "rb");
            if (f)
            {
                unsigned char *buf = (unsigned char *)malloc((size_t)job->length);
                if (buf && _fseeki64(f, job->offset, SEEK_SET) == 0 &&
                    fread(buf, 1, (size_t)job->length, f) == (size_t)job->length)
                {
                    unsigned char *data = buf;
                    long long dataLen = job->length;
                    unsigned char *raw = NULL;
                    if (job->rawLength != job->length)
                    {
                        // giris zlib'li: stb_image'in gomulu inflate'i ile ac
                        raw = (unsigned char *)malloc((size_t)job->rawLength);
                        if (raw && stbi_zlib_decode_buffer((char *)raw, (int)job->rawLength,
                                                           (const char *)buf, (int)job->length) == (int)job->rawLength)
                        {
                            data = raw;
                            dataLen = job->rawLength;
                        }
                        else
                            data = NULL;
                    }
                    if (data)
                        px = de_decode_pixels(data, dataLen, &w, &h, &comp);
                    free(raw);
                }
                free(buf);
                fclose(f);
            }
        }
        else
            px = stbi_load(job->path, &w, &h, &comp, 4);
        job->pixels = px;
        job->w = w;
        job->h = h;
        InterlockedExchange(&job->state, px ? 3 : 4); // full fence: pixels once yazildi
    }
}

static void de_io_ensure(void)
{
    if (de_io_started)
        return;
    de_io_started = 1;
    stbi_set_flip_vertically_on_load(1); // satir 0 altta: quad UV'leriyle uyumlu
    InitializeCriticalSection(&de_io_lock);
    de_io_sem = CreateSemaphoreA(NULL, 0, DE_IO_MAX_JOBS, NULL);
    CreateThread(NULL, 0, de_io_worker, NULL, 0, NULL);
}

// Kuyruga ekler; donus: job id (kuyruk dolu: -1). Yalniz main thread cagirir.
// length>0: dosyanin [offset, offset+length) araligi okunur; rawLength!=length
// ise aralik zlib'lidir, worker once inflate eder sonra decode.
SOKOL_API int de_asset_load_range(const char *path, long long offset, long long length, long long rawLength)
{
    de_io_ensure();
    EnterCriticalSection(&de_io_lock);
    int id = -1;
    for (int i = 0; i < DE_IO_MAX_JOBS; i++)
    {
        if (de_io_jobs[i].state == 0)
        {
            de_io_jobs[i].pixels = NULL;
            de_io_jobs[i].w = de_io_jobs[i].h = 0;
            de_io_jobs[i].offset = offset;
            de_io_jobs[i].length = length;
            de_io_jobs[i].rawLength = rawLength;
            strncpy(de_io_jobs[i].path, path, DE_IO_PATH_MAX - 1);
            de_io_jobs[i].path[DE_IO_PATH_MAX - 1] = 0;
            de_io_jobs[i].state = 1;
            id = i;
            break;
        }
    }
    LeaveCriticalSection(&de_io_lock);
    if (id >= 0)
        ReleaseSemaphore(de_io_sem, 1, NULL);
    return id;
}

SOKOL_API int de_asset_load(const char *path)
{
    return de_asset_load_range(path, 0, 0, 0);
}

// 0=bekliyor, 1=tamam (pixels/w/h dolu), -1=hata. Yalniz main thread poll eder.
SOKOL_API int de_asset_poll(int job, void **pixels, int *w, int *h)
{
    if (job < 0 || job >= DE_IO_MAX_JOBS)
        return -1;
    LONG s = de_io_jobs[job].state;
    if (s == 3)
    {
        *pixels = de_io_jobs[job].pixels;
        *w = de_io_jobs[job].w;
        *h = de_io_jobs[job].h;
        return 1;
    }
    return s == 4 ? -1 : 0;
}

// Pikseller kopyalandiktan sonra slot serbest birakilir.
SOKOL_API void de_asset_free_job(int job)
{
    if (job < 0 || job >= DE_IO_MAX_JOBS)
        return;
    if (de_io_jobs[job].pixels)
    {
        stbi_image_free(de_io_jobs[job].pixels);
        de_io_jobs[job].pixels = NULL;
    }
    InterlockedExchange(&de_io_jobs[job].state, 0);
}

// ---------------------------------------------------------------------------
// Native menu bar (Win32): editor ana penceresine gercek Windows menusu takar.
// GLFW wndproc'u subclass'lanir; WM_COMMAND ring buffer'a yazilir, managed
// taraf frame basina de_menu_poll ile ceker (tek thread sozlesmesi korunur).
// ---------------------------------------------------------------------------

extern void *glfwGetWin32Window(void *window); // glfw ayni DLL'e statik linkli

static WNDPROC de__menu_oldproc;
static int de__menu_ring[64];
static volatile LONG de__menu_rd, de__menu_wr;

static LRESULT CALLBACK de__menu_wndproc(HWND h, UINT m, WPARAM wp, LPARAM lp)
{
    if (m == WM_COMMAND && HIWORD(wp) == 0) // 0 = menu kaynakli
    {
        de__menu_ring[de__menu_wr & 63] = (int)LOWORD(wp);
        de__menu_wr++;
        return 0;
    }
    return CallWindowProcW(de__menu_oldproc, h, m, wp, lp);
}

static void de__menu_wide(const char *utf8, wchar_t *out, int cap)
{
    MultiByteToWideChar(CP_UTF8, 0, utf8, -1, out, cap);
    out[cap - 1] = 0;
}

SOKOL_API void *de_menu_create_bar(void)
{
    return (void *)CreateMenu();
}

SOKOL_API void *de_menu_add_popup(void *parent, const char *title)
{
    wchar_t w[128];
    de__menu_wide(title, w, 128);
    HMENU pop = CreatePopupMenu();
    AppendMenuW((HMENU)parent, MF_POPUP, (UINT_PTR)pop, w);
    return (void *)pop;
}

// id 1000 ofsetlenir: 0..999 arasi sistem/aksesuar id'leriyle cakismasin.
SOKOL_API void de_menu_add_item(void *menu, const char *title, int id)
{
    wchar_t w[128];
    de__menu_wide(title, w, 128);
    AppendMenuW((HMENU)menu, MF_STRING, (UINT_PTR)(id + 1000), w);
}

SOKOL_API void de_menu_add_separator(void *menu)
{
    AppendMenuW((HMENU)menu, MF_SEPARATOR, 0, NULL);
}

SOKOL_API void de_menu_attach(void *glfwWindow, void *bar)
{
    HWND hwnd = (HWND)glfwGetWin32Window(glfwWindow);
    SetMenu(hwnd, (HMENU)bar);
    if (!de__menu_oldproc)
        de__menu_oldproc = (WNDPROC)SetWindowLongPtrW(hwnd, GWLP_WNDPROC, (LONG_PTR)de__menu_wndproc);
    DrawMenuBar(hwnd);
}

// -1 = komut yok; >=0 = de_menu_add_item'a verilen id.
SOKOL_API int de_menu_poll(void)
{
    if (de__menu_rd == de__menu_wr)
        return -1;
    int id = de__menu_ring[de__menu_rd & 63];
    de__menu_rd++;
    return id - 1000;
}

// Sag-tik context menusu: TrackPopupMenu TPM_RETURNCMD ile SENKRON secim
// dondurur (WM_COMMAND'a dusmez). Menu cagri basina yaratilip yok edilir.
SOKOL_API void *de_menu_create_popup(void)
{
    return (void *)CreatePopupMenu();
}

SOKOL_API int de_menu_show_context(void *glfwWindow, void *popup)
{
    HWND hwnd = (HWND)glfwGetWin32Window(glfwWindow);
    POINT pt;
    GetCursorPos(&pt);
    SetForegroundWindow(hwnd); // menu disina tiklaninca kapanabilsin
    int cmd = (int)TrackPopupMenu((HMENU)popup, TPM_RETURNCMD | TPM_RIGHTBUTTON,
                                  pt.x, pt.y, 0, hwnd, NULL);
    return cmd >= 1000 ? cmd - 1000 : -1;
}

SOKOL_API void de_menu_destroy(void *menu)
{
    DestroyMenu((HMENU)menu); // alt popup'lari da yok eder
}
#endif // _WIN32
