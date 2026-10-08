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
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

#define STB_TRUETYPE_IMPLEMENTATION
#define STBTT_STATIC
#include "stb_truetype.h"


#if defined(DE_BUILD_DLL)
#if defined(_WIN32)
#define SOKOL_API __declspec(dllexport)
#endif
#else
#define SOKOL_API
#endif

// ---------------------------------------------------------------------------
// METAL HOST (macOS/iOS) — sokol_gfx swapchain'i icin MTLDevice + CAMetalLayer.
// ---------------------------------------------------------------------------
// GLCORE'da swapchain = GL varsayilan framebuffer (fbo 0). Metal'de host,
// pencerenin NSView'ine bir CAMetalLayer takar; her frame layer'dan bir
// CAMetalDrawable + eslesen bir depth-stencil texture uretilip sg_pass.swapchain
// .metal alanlarina verilir. sokol_gfx swapchain pass'in sonunda drawable'i
// otomatik present eder (sg_commit). Yalniz ANA pencere swapchain kullanir.
//
// Bu blok Objective-C + ARC ile derlenir (build_editor.sh; -x objective-c
// -fobjc-arc). Windows/GLCORE build'inde tamamen dislanir.
#if defined(SOKOL_METAL)
#define GLFW_INCLUDE_NONE
#include "GLFW/glfw3.h"
#define GLFW_EXPOSE_NATIVE_COCOA
#include "GLFW/glfw3native.h"
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>
#import <AppKit/AppKit.h>

// COK PENCERE: her native pencerenin kendi CAMetalLayer'i + bu frame'in
// drawable'i + eslesen depth-stencil texture'i vardir. Slot 0 = ANA pencere;
// 1..N = ikincil (tear-off / floating) pencereler. sokol_gfx her swapchain
// pass'in SONUNDA (end_pass) o pass'in drawable'ini present eder (commit'te
// degil) -> ayni frame'de birden cok pencereye cizip her birini bagimsiz
// present etmek dogal olarak calisir. Pencere "handle" = slot indeksi ve
// Camera.Framebuffer araciligiyla begin_pass'e tasinir (GL'de ayni alan FBO).
#define DE_MTL_MAX_WINDOWS 32
typedef struct
{
    CAMetalLayer *layer;
    id<CAMetalDrawable> drawable; // bu frame'in drawable'i (present sonrasi birakilir)
    id<MTLTexture> depth;         // depth-stencil (drawable boyutunda)
    int depth_w, depth_h;
    bool active;
} de_mtl_window_t;

static id<MTLDevice> _de_mtl_device;
static de_mtl_window_t _de_mtl_wins[DE_MTL_MAX_WINDOWS];

// Verilen GLFW penceresinin NSView'ine bir CAMetalLayer takar (slot'a kaydeder).
static void de_metal_attach_layer(int slot, void *glfwWindow)
{
    NSWindow *nswin = glfwGetCocoaWindow((GLFWwindow *)glfwWindow);
    CAMetalLayer *layer = [CAMetalLayer layer];
    layer.device = _de_mtl_device;
    layer.pixelFormat = MTLPixelFormatRGBA8Unorm; // offscreen RT'ler ve pipeline varsayilaniyla ayni (RGBA8)
    layer.framebufferOnly = YES;
    NSView *view = nswin.contentView;
    view.wantsLayer = YES;
    view.layer = layer;
    _de_mtl_wins[slot].layer = layer;
    _de_mtl_wins[slot].drawable = nil;
    _de_mtl_wins[slot].depth = nil;
    _de_mtl_wins[slot].depth_w = 0;
    _de_mtl_wins[slot].depth_h = 0;
    _de_mtl_wins[slot].active = true;
}

// C# ANA pencereyi (GLFW_NO_API) olusturduktan SONRA cagrilir: device'i kurar
// ve ana pencereye (slot 0) layer takar. de_sokol_setup'tan ONCE cagrilmali.
SOKOL_API void de_metal_init_window(void *glfwWindow)
{
    if (!_de_mtl_device)
        _de_mtl_device = MTLCreateSystemDefaultDevice();
    de_metal_attach_layer(0, glfwWindow);
}

// Ikincil pencere: bos bir slot bulup layer takar, handle (slot indeksi) doner.
// Basarisizsa -1. Yalniz ANA pencere + de_metal_init_window'dan SONRA cagrilir.
SOKOL_API int de_metal_create_window(void *glfwWindow)
{
    if (!_de_mtl_device)
        return -1;
    for (int i = 1; i < DE_MTL_MAX_WINDOWS; i++)
    {
        if (!_de_mtl_wins[i].active)
        {
            de_metal_attach_layer(i, glfwWindow);
            return i;
        }
    }
    return -1;
}

// Ikincil pencere kapatilirken cagrilir: slot'u serbest birakir (ARC nil'ler).
SOKOL_API void de_metal_destroy_window(int handle)
{
    if (handle <= 0 || handle >= DE_MTL_MAX_WINDOWS)
        return;
    _de_mtl_wins[handle].drawable = nil;
    _de_mtl_wins[handle].depth = nil;
    _de_mtl_wins[handle].layer = nil;
    _de_mtl_wins[handle].active = false;
}

// de_sokol_setup icin device pointer'i (unretained __bridge).
static const void *de_metal_device(void) { return (__bridge const void *)_de_mtl_device; }

// Swapchain pass'inden ONCE (begin_pass icinde lazy): handle'in drawable + depth'ini hazirla.
static void de_metal_acquire(int handle, int width, int height)
{
    if (handle < 0 || handle >= DE_MTL_MAX_WINDOWS)
        return;
    de_mtl_window_t *w = &_de_mtl_wins[handle];
    if (!w->active || w->drawable != nil)
        return; // pasif slot ya da bu frame zaten alindi
    if (width <= 0 || height <= 0)
        return;
    w->layer.drawableSize = CGSizeMake(width, height);
    w->drawable = [w->layer nextDrawable];
    if (w->depth == nil || width != w->depth_w || height != w->depth_h)
    {
        MTLTextureDescriptor *dd = [MTLTextureDescriptor
            texture2DDescriptorWithPixelFormat:MTLPixelFormatDepth32Float_Stencil8
                                         width:(NSUInteger)width
                                        height:(NSUInteger)height
                                     mipmapped:NO];
        dd.usage = MTLTextureUsageRenderTarget;
        dd.storageMode = MTLStorageModePrivate;
        w->depth = [_de_mtl_device newTextureWithDescriptor:dd];
        w->depth_w = width;
        w->depth_h = height;
    }
}

// sg_commit sonrasi: TUM pencerelerin drawable referanslarini birak (sokol
// kendi ref'ini tutar; her pass end_pass'te zaten present etti).
static void de_metal_frame_end(void)
{
    for (int i = 0; i < DE_MTL_MAX_WINDOWS; i++)
        _de_mtl_wins[i].drawable = nil;
}
#endif // SOKOL_METAL

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
#if defined(SOKOL_METAL)
    // Metal: framebuffer = pencere handle'i (0 = ana). Her frame o pencerenin
    // CAMetalLayer'indan drawable + depth alinir (host).
    de_metal_acquire((int)framebuffer, width, height);
    pass.swapchain.color_format = SG_PIXELFORMAT_RGBA8;
    pass.swapchain.depth_format = SG_PIXELFORMAT_DEPTH_STENCIL;
    if ((int)framebuffer >= 0 && (int)framebuffer < DE_MTL_MAX_WINDOWS)
    {
        de_mtl_window_t *w = &_de_mtl_wins[framebuffer];
        pass.swapchain.metal.current_drawable = (__bridge const void *)w->drawable;
        pass.swapchain.metal.depth_stencil_texture = (__bridge const void *)w->depth;
    }
#else
    pass.swapchain.gl.framebuffer = framebuffer;
#endif
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

void de_audio_tick(void); // audio_shim.c: idle gating + ertelenmis free (frame'de bir)

SOKOL_API void de_sokol_commit(void)
{
    sg_commit();
    de_audio_tick();
#if defined(SOKOL_METAL)
    de_metal_frame_end(); // present sonrasi drawable ref'ini birak
#endif
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
static char de_last_err[2048]; // son ERROR + onu izleyen INFO detayi (GL compile log)
static int de_last_err_pending;
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
    if (level <= 1)
    {
        snprintf(de_last_err, sizeof de_last_err, "%s", msg ? msg : "");
        de_last_err_pending = 1; // GL info log'u hemen ardindan INFO olarak gelir
    }
    else if (level == 3 && de_last_err_pending)
    {
        size_t len = strlen(de_last_err);
        snprintf(de_last_err + len, sizeof de_last_err - len, "\n%s", msg ? msg : "");
        de_last_err_pending = 0;
    }
    fputs(buf, stderr);
    fflush(stderr);
    FILE *f = fopen("crash_native.log", "a");
    if (f)
    {
        fputs(buf, f);
        fclose(f);
    }
}

SOKOL_API const char *de_sokol_last_error(void) { return de_last_err; }

SOKOL_API void de_sokol_setup(void)
{
    sg_desc d = {0};
    d.logger.func = de_sokol_log;
#if defined(SOKOL_METAL)
    // Metal ortami: device host'tan; swapchain ve offscreen RT'ler ayni RGBA8/
    // DEPTH_STENCIL formatinda tutulur -> pipeline'lar tek varsayilan formatla eslesir.
    d.environment.metal.device = de_metal_device();
    d.environment.defaults.color_format = SG_PIXELFORMAT_RGBA8;
    d.environment.defaults.depth_format = SG_PIXELFORMAT_DEPTH_STENCIL;
    d.environment.defaults.sample_count = 1;
#endif
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
// Enum degeri SIZDIRILMAZ (sokol surumlerinde kayar: UNSEALED eklendi) — yalniz
// "gecerli mi" boolean'i doner.
SOKOL_API int de_sokol_shader_valid(unsigned int id)
{
    sg_shader h = {id};
    return sg_query_shader_state(h) == SG_RESOURCESTATE_VALID ? 1 : 0;
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
    _de_sd.uniform_blocks[slot].msl_buffer_n = (uint8_t)slot; // Metal [[buffer(slot)]] (0..7)
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
    _de_sd.views[slot].texture.msl_texture_n = (uint8_t)slot; // Metal [[texture(slot)]]
}
SOKOL_API void de_sokol_shader_sampler(int slot, int stage, int samplerType)
{
    _de_sd.samplers[slot].stage = (sg_shader_stage)stage;
    _de_sd.samplers[slot].sampler_type = (sg_sampler_type)samplerType;
    _de_sd.samplers[slot].msl_sampler_n = (uint8_t)slot; // Metal [[sampler(slot)]]
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

// ---------------------------------------------------------------------------
// Glyph-bazli SDF baker (FontImporter): sabit hucre/atlas YOK — cagiran codepoint
// listesini ve SDF boyunu verir, glyph'leri tek tek alir ve kendi paketler.
// Handle = fontinfo + ttf kopyasi (importer omru boyunca acik kalir).
//   de_sdf_font_open      : ttf baytlarindan handle (NULL = gecersiz font)
//   de_sdf_font_metrics   : out5 = ascent, descent, lineHeight (SDF px), kernScale, sdfSize
//   de_sdf_font_glyph     : out5 = advance, xoff, yoff, w, h; buffer'a w*h SDF (satir 0 ustte)
//   de_sdf_font_kern_table: cps[n] icin n*n int16 (font-unit; kernScale ile carpilir)
// SDF parametreleri de_font_bake ile ayni (onedge 128, spread 21, pad 6) — shader
// esikleri ve TextSprite.SdfSpread sabitiyle birebir uyumlu kalir.
// ---------------------------------------------------------------------------

typedef struct de_sdf_font_t
{
    stbtt_fontinfo info;
    unsigned char *ttf;
} de_sdf_font_t;

SOKOL_API void *de_sdf_font_open(const unsigned char *ttf, int length)
{
    if (!ttf || length <= 0)
        return NULL;
    de_sdf_font_t *f = (de_sdf_font_t *)malloc(sizeof(de_sdf_font_t));
    if (!f)
        return NULL;
    f->ttf = (unsigned char *)malloc((size_t)length);
    if (!f->ttf)
    {
        free(f);
        return NULL;
    }
    memcpy(f->ttf, ttf, (size_t)length);
    if (!stbtt_InitFont(&f->info, f->ttf, 0))
    {
        free(f->ttf);
        free(f);
        return NULL;
    }
    return f;
}

SOKOL_API void de_sdf_font_close(void *handle)
{
    de_sdf_font_t *f = (de_sdf_font_t *)handle;
    if (!f)
        return;
    free(f->ttf);
    free(f);
}

SOKOL_API int de_sdf_font_metrics(void *handle, float sdfSize, float *out5)
{
    de_sdf_font_t *f = (de_sdf_font_t *)handle;
    if (!f || sdfSize <= 0)
        return 0;
    float scale = stbtt_ScaleForPixelHeight(&f->info, sdfSize);
    int ascent = 0, descent = 0, lineGap = 0;
    stbtt_GetFontVMetrics(&f->info, &ascent, &descent, &lineGap);
    out5[0] = scale * (float)ascent;
    out5[1] = scale * (float)descent;
    out5[2] = scale * (float)(ascent - descent + lineGap);
    out5[3] = scale;
    out5[4] = sdfSize;
    return 1;
}

// Donus: 1 ok (w*h buffer'a yazildi; w veya h 0 olabilir: bosluk), 0 hata/sigmadi.
SOKOL_API int de_sdf_font_glyph(void *handle, int codepoint, float sdfSize,
                                unsigned char *buffer, int bufferSize, float *out5)
{
    de_sdf_font_t *f = (de_sdf_font_t *)handle;
    if (!f || sdfSize <= 0)
        return 0;
    float scale = stbtt_ScaleForPixelHeight(&f->info, sdfSize);
    int glyphIndex = stbtt_FindGlyphIndex(&f->info, codepoint);
    int advance = 0, lsb = 0, x0 = 0, y0 = 0, w = 0, h = 0;
    stbtt_GetGlyphHMetrics(&f->info, glyphIndex, &advance, &lsb);
    unsigned char *sdf = stbtt_GetGlyphSDF(&f->info, scale, glyphIndex,
                                           DE_FONT_SDF_PAD, 128, DE_FONT_SDF_SPREAD, &w, &h, &x0, &y0);
    if (sdf && w > 0 && h > 0)
    {
        if (w * h > bufferSize)
        {
            stbtt_FreeSDF(sdf, NULL);
            return 0;
        }
        memcpy(buffer, sdf, (size_t)(w * h));
    }
    else
        w = h = 0;
    if (sdf)
        stbtt_FreeSDF(sdf, NULL);
    out5[0] = scale * (float)advance;
    out5[1] = (float)x0;
    out5[2] = (float)y0;
    out5[3] = (float)w;
    out5[4] = (float)h;
    return 1;
}

SOKOL_API int de_sdf_font_kern_table(void *handle, const int *codepoints, int count, short *out)
{
    de_sdf_font_t *f = (de_sdf_font_t *)handle;
    if (!f || count <= 0)
        return 0;
    int *gi = (int *)malloc(sizeof(int) * (size_t)count);
    if (!gi)
        return 0;
    for (int i = 0; i < count; ++i)
        gi[i] = stbtt_FindGlyphIndex(&f->info, codepoints[i]);
    for (int l = 0; l < count; ++l)
        for (int r = 0; r < count; ++r)
            out[l * count + r] = (short)stbtt_GetGlyphKernAdvance(&f->info, gi[l], gi[r]);
    free(gi);
    return 1;
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
        case 5: // SetUniforms — inline veri akistan uygulanir; hizasizsa hizali tampona kopyalanir
        {         // (WebGL HEAPF32 gorunumu 4-byte hizalama ister; akis 1-bayt opcode'lar yuzunden hizasiz).
            int slot = de_ri32(&p);
            int size = de_ri32(&p);
            static _Alignas(16) unsigned char ubuf[4096];
            const void *src = p;
            if (((uintptr_t)p & 15) != 0 && size <= (int)sizeof ubuf)
            {
                memcpy(ubuf, p, (size_t)size);
                src = ubuf;
            }
            sg_range r = {src, (size_t)size};
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
// ---------------------------------------------------------------------------
// Async asset IO: de_fs.c (platform-bagimsiz handle + job tablosu). Buradaki
// de_asset_* sembolleri yalniz geriye uyum sarmalayicisidir (editor loose yol).
// ---------------------------------------------------------------------------
#include "de_fs.h"

SOKOL_API int de_asset_load_range(const char *path, long long offset, long long length, long long rawLength)
{
    return de_fs_decode(-1, path, offset, length, rawLength);
}

SOKOL_API int de_asset_load(const char *path)
{
    return de_fs_decode(-1, path, 0, 0, 0);
}

SOKOL_API int de_asset_poll(int job, void **pixels, int *w, int *h)
{
    long long len;
    return de_job_poll(job, pixels, &len, w, h);
}

SOKOL_API void de_asset_free_job(int job)
{
    de_job_free(job);
}

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
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

// Klasor secici (Open Project): modal, SENKRON. Donus 1 = secildi (out UTF-8), 0 = iptal.
#include <shlobj.h>
SOKOL_API int de_dialog_pick_folder(void *glfwWindow, const char *title, char *out, int cap)
{
    HWND hwnd = glfwWindow ? (HWND)glfwGetWin32Window(glfwWindow) : NULL;
    CoInitializeEx(NULL, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
    wchar_t wtitle[256];
    de__menu_wide(title ? title : "Select folder", wtitle, 256);
    BROWSEINFOW bi;
    memset(&bi, 0, sizeof bi);
    bi.hwndOwner = hwnd;
    bi.lpszTitle = wtitle;
    bi.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_EDITBOX;
    LPITEMIDLIST pidl = SHBrowseForFolderW(&bi);
    if (!pidl)
        return 0;
    wchar_t wpath[MAX_PATH];
    int ok = SHGetPathFromIDListW(pidl, wpath) ? 1 : 0;
    CoTaskMemFree(pidl);
    if (!ok)
        return 0;
    WideCharToMultiByte(CP_UTF8, 0, wpath, -1, out, cap, NULL, NULL);
    out[cap - 1] = 0;
    return 1;
}

#elif defined(__APPLE__) // macOS: pthread IO + Cocoa menu/dialog (editor). Android/wasm/iOS: menu/dialog yok (DllImport'lar weak, cagrilmaz).
// ---------------------------------------------------------------------------
// Native menu (Cocoa): editor ana penceresine NSMenu bar takar; sag-tik
// context menusu SENKRON secim dondurur. Item id'leri NSMenuItem.tag'inde.
// Ana-bar tiklamalari ring buffer'a yazilir (de_menu_poll ile cekilir);
// context menusunde ise (_de_menu_in_context) secim dogrudan dondurulur.
// ---------------------------------------------------------------------------
#import <Cocoa/Cocoa.h>

static int de__menu_ring[64];
static volatile int de__menu_rd, de__menu_wr;
static int de__menu_in_context;
static int de__menu_ctx_result;

@interface DEMenuTarget : NSObject
- (void)fire:(id)sender;
@end
@implementation DEMenuTarget
- (void)fire:(id)sender
{
    int tag = (int)[(NSMenuItem *)sender tag];
    if (de__menu_in_context)
        de__menu_ctx_result = tag;
    else
    {
        de__menu_ring[de__menu_wr & 63] = tag;
        de__menu_wr++;
    }
}
@end

static DEMenuTarget *de__menu_shared_target(void)
{
    static DEMenuTarget *t;
    if (!t)
        t = [[DEMenuTarget alloc] init];
    return t;
}

SOKOL_API void *de_menu_create_bar(void)
{
    NSMenu *bar = [[NSMenu alloc] init];
    [bar setAutoenablesItems:NO];
    return (void *)CFBridgingRetain(bar); // uygulama omru boyunca yasar
}

SOKOL_API void *de_menu_add_popup(void *parent, const char *title)
{
    NSString *t = [NSString stringWithUTF8String:title];
    NSMenu *sub = [[NSMenu alloc] initWithTitle:t];
    [sub setAutoenablesItems:NO];
    NSMenuItem *item = [[NSMenuItem alloc] initWithTitle:t action:NULL keyEquivalent:@""];
    [item setSubmenu:sub];
    [(__bridge NSMenu *)parent addItem:item];
    return (__bridge void *)sub; // parent sahibi; ayrica retain gerekmez
}

SOKOL_API void de_menu_add_item(void *menu, const char *title, int id)
{
    NSMenuItem *it = [[NSMenuItem alloc] initWithTitle:[NSString stringWithUTF8String:title]
                                                action:@selector(fire:)
                                         keyEquivalent:@""];
    [it setTarget:de__menu_shared_target()];
    [it setTag:id];
    [it setEnabled:YES];
    [(__bridge NSMenu *)menu addItem:it];
}

SOKOL_API void de_menu_add_separator(void *menu)
{
    [(__bridge NSMenu *)menu addItem:[NSMenuItem separatorItem]];
}

SOKOL_API void de_menu_attach(void *glfwWindow, void *bar)
{
    (void)glfwWindow;
    [NSApp setMainMenu:(__bridge NSMenu *)bar];
}

// -1 = komut yok; >=0 = de_menu_add_item'a verilen id.
SOKOL_API int de_menu_poll(void)
{
    if (de__menu_rd == de__menu_wr)
        return -1;
    int id = de__menu_ring[de__menu_rd & 63];
    de__menu_rd++;
    return id;
}

SOKOL_API void *de_menu_create_popup(void)
{
    NSMenu *popup = [[NSMenu alloc] init];
    [popup setAutoenablesItems:NO];
    return (void *)CFBridgingRetain(popup); // de_menu_destroy serbest birakir
}

// Sag-tik context menusu: SENKRON — secim yapilmadan donmez.
SOKOL_API int de_menu_show_context(void *glfwWindow, void *popup)
{
    (void)glfwWindow;
    de__menu_ctx_result = -1;
    de__menu_in_context = 1;
    NSPoint loc = [NSEvent mouseLocation]; // ekran koordinati (inView:nil ile uyumlu)
    [(__bridge NSMenu *)popup popUpMenuPositioningItem:nil atLocation:loc inView:nil];
    de__menu_in_context = 0;
    return de__menu_ctx_result;
}

SOKOL_API void de_menu_destroy(void *menu)
{
    CFBridgingRelease(menu); // alt popup'lar item hiyerarsisiyle birlikte serbest kalir
}

// Klasor secici (Open Project): NSOpenPanel, modal/senkron. Donus 1 = secildi (out UTF-8), 0 = iptal.
SOKOL_API int de_dialog_pick_folder(void *glfwWindow, const char *title, char *out, int cap)
{
    (void)glfwWindow;
    NSOpenPanel *panel = [NSOpenPanel openPanel];
    [panel setCanChooseDirectories:YES];
    [panel setCanChooseFiles:NO];
    [panel setAllowsMultipleSelection:NO];
    if (title)
        [panel setMessage:[NSString stringWithUTF8String:title]];
    if ([panel runModal] != NSModalResponseOK)
        return 0;
    NSURL *url = [[panel URLs] firstObject];
    if (!url)
        return 0;
    const char *p = [[url path] UTF8String];
    strncpy(out, p, cap - 1);
    out[cap - 1] = 0;
    return 1;
}
#endif // _WIN32 / __APPLE__
