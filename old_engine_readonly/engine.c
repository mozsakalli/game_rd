
// engine.c - hafif instanced batch renderer.
// 2D ve 3D TEK yoldan gecer: her cizim = Mesh(_engine_Vertex + index) + per-instance Mat4/uvRect/tint.
// Sprite = built-in unit quad mesh. Ayni shader, ayni pipeline cache, ayni batcher.
// Hedefler: Windows/Linux GLCORE, WebGL2 + Android GLES3, iOS/macOS Metal.
// NOT: Apple hedeflerinde bu dosya Objective-C olarak derlenmeli (clang -x objective-c).

#include <stdlib.h>
#include <string.h>
#include <math.h>
#include <stdint.h>
#include <stddef.h>
#include <stdbool.h>
#include <stdio.h>

#define SOKOL_GFX_IMPL
#if !defined(ENGINE_USE_GLFW)
#define SOKOL_APP_IMPL
#define SOKOL_GLUE_IMPL
#endif
#define SOKOL_LOG_IMPL
#define SOKOL_TIME_IMPL

#if !defined(SOKOL_METAL) && !defined(SOKOL_GLCORE) && !defined(SOKOL_GLES3)
#if defined(__APPLE__)
#define SOKOL_METAL
#elif defined(__EMSCRIPTEN__)
#define SOKOL_GLES3
#elif defined(__ANDROID__)
#define SOKOL_GLES3
#elif defined(_WIN32)
#define SOKOL_GLCORE
#else
#define SOKOL_GLCORE
#endif
#endif

// Android disinda giris noktasi bizde (generated.c main) -> sokol_app main/sokol_main uretmesin.
#if !defined(__ANDROID__)
#define SOKOL_NO_ENTRY
#endif

// Android'de giris noktasini sokol_app yonetir (ANativeActivity), digerlerinde sapp_run'i biz cagiririz.
#if !defined(__ANDROID__)
#define SOKOL_WIN32_FORCE_MAIN
#endif

#include "sokol_gfx.h"
#if !defined(ENGINE_USE_GLFW)
#include "sokol_app.h"
#include "sokol_glue.h"
#endif
#include "sokol_log.h"
#include "sokol_time.h"
#include "imgui.h"
#include "imgui_font.h"
#include "engine_shader.h"
#include "engine_render.h"
#include "engine_window.h"

// GLFW host koprusu: pencere/swapchain sahipligi engine_host_glfw.c'de.
#if defined(ENGINE_USE_GLFW)
int engine_host_width(void);
int engine_host_height(void);
sg_swapchain engine_host_swapchain(void);
#define _ENGINE_HOST_W() engine_host_width()
#define _ENGINE_HOST_H() engine_host_height()
#else
#define _ENGINE_HOST_W() sapp_width()
#define _ENGINE_HOST_H() sapp_height()
#endif

typedef struct _engine_Shader
{
    GCHeader gc;
    sg_shader handle;
    uint32_t id;
} _engine_Shader;

// Metal/D3D/WGPU clip-space derinligi 0..1, GL ailesi -1..1.
#if defined(SOKOL_METAL) || defined(SOKOL_D3D11) || defined(SOKOL_WGPU)
#define ENGINE_DEPTH_ZERO_TO_ONE 1
#else
#define ENGINE_DEPTH_ZERO_TO_ONE 0
#endif

struct _engine_BitmapData
{
    GCHeader gc;
    int width;
    int height;
    _engine_Color *pixels;
    int _texture;
    int _textureView;
    int _colorAttachmentView;
    int _depthTexture;
    int _depthAttachmentView;
    int _dirty;
    int _pointFilter;
};

static const char *_engine_basic_effect_body =
    "VEC4 fs_main(VEC2 uv, VEC4 color) {\n"
    "    return SAMPLE(tex, uv) * color;\n"
    "}\n";

typedef enum
{
    ENGINE_SORT_NONE = 0,
    ENGINE_SORT_OPAQUE,
    ENGINE_SORT_TRANSPARENT,
    ENGINE_SORT_UI,
    ENGINE_SORT_PAINTER
} engine_sort_mode;

// Pass sadece mantiksal bir birim: sort semantigi + kamera. Hedef (render target)
// pass'tan bagimsiz bir stack ile yonetilir, bkz. engine_push_target/engine_pop_target.
typedef struct
{
    const char *name; // profiler capture etiketi
    int depthEnabled;
    int depthWrite;
    engine_sort_mode sortMode;
    _engine_Mat4 viewProj;
} engine_pass_desc;

// her flush icin bir capture kaydi
typedef struct
{
    const char *pass;
    _engine_BitmapData *target; // NULL => swapchain
    int drawCalls;
    int batches;
    int instances;
    float cpuMs;
} engine_profile_sample;

typedef struct
{
    int x;
    int y;
    int width;
    int height;
} _engine_Scissor;

// GPU'ya giden per-instance veri (vertex buffer slot 1, PER_INSTANCE step)
typedef struct _engine_Instance
{
    float model[16];
    float uvRect[4]; // u0, v0, du, dv
    unsigned char tint[4];
} _engine_Instance;

struct _engine_Mesh
{
    sg_buffer vbuf;
    sg_buffer ibuf;
    int indexCount;
    int index32;
    int slot; // mesh registry indeksi, sort key'e girer
};

void engine_mat4_multiply(_engine_Mat4 *a0, _engine_Mat4 *a1, _engine_Mat4 *a2)
{
    float _a0 = a0->m[0];
    float _a1 = a0->m[1];
    float _a2 = a0->m[2];
    float a3 = a0->m[3];
    float a4 = a0->m[4];
    float a5 = a0->m[5];
    float a6 = a0->m[6];
    float a7 = a0->m[7];
    float a8 = a0->m[8];
    float a9 = a0->m[9];
    float a10 = a0->m[10];
    float a11 = a0->m[11];
    float a12 = a0->m[12];
    float a13 = a0->m[13];
    float a14 = a0->m[14];
    float a15 = a0->m[15];
    float b0 = a1->m[0];
    float b1 = a1->m[1];
    float b2 = a1->m[2];
    float b3 = a1->m[3];
    float b4 = a1->m[4];
    float b5 = a1->m[5];
    float b6 = a1->m[6];
    float b7 = a1->m[7];
    float b8 = a1->m[8];
    float b9 = a1->m[9];
    float b10 = a1->m[10];
    float b11 = a1->m[11];
    float b12 = a1->m[12];
    float b13 = a1->m[13];
    float b14 = a1->m[14];
    float b15 = a1->m[15];
    // elle acilmis 16 ifade: clang -O2 otomatik vektorlestirir (FMA'siz -> .NET ile bit-esit)
    a2->m[0] = _a0 * b0 + a4 * b1 + a8 * b2 + a12 * b3;
    a2->m[1] = _a1 * b0 + a5 * b1 + a9 * b2 + a13 * b3;
    a2->m[2] = _a2 * b0 + a6 * b1 + a10 * b2 + a14 * b3;
    a2->m[3] = a3 * b0 + a7 * b1 + a11 * b2 + a15 * b3;
    a2->m[4] = _a0 * b4 + a4 * b5 + a8 * b6 + a12 * b7;
    a2->m[5] = _a1 * b4 + a5 * b5 + a9 * b6 + a13 * b7;
    a2->m[6] = _a2 * b4 + a6 * b5 + a10 * b6 + a14 * b7;
    a2->m[7] = a3 * b4 + a7 * b5 + a11 * b6 + a15 * b7;
    a2->m[8] = _a0 * b8 + a4 * b9 + a8 * b10 + a12 * b11;
    a2->m[9] = _a1 * b8 + a5 * b9 + a9 * b10 + a13 * b11;
    a2->m[10] = _a2 * b8 + a6 * b9 + a10 * b10 + a14 * b11;
    a2->m[11] = a3 * b8 + a7 * b9 + a11 * b10 + a15 * b11;
    a2->m[12] = _a0 * b12 + a4 * b13 + a8 * b14 + a12 * b15;
    a2->m[13] = _a1 * b12 + a5 * b13 + a9 * b14 + a13 * b15;
    a2->m[14] = _a2 * b12 + a6 * b13 + a10 * b14 + a14 * b15;
    a2->m[15] = a3 * b12 + a7 * b13 + a11 * b14 + a15 * b15;
}

void engine_mat4_identity(_engine_Mat4 *r)
{
    memset(r, 0, sizeof(*r));
    r->m[0] = r->m[5] = r->m[10] = r->m[15] = 1.0f;
}

// olcek -> Z ekseni donusu -> oteleme (2D icin yeterli)
void engine_mat4_trs2d(_engine_Mat4 *r, float x, float y, float z, float sx, float sy, float rotation)
{
    float c = cosf(rotation), s = sinf(rotation);
    memset(r, 0, sizeof(*r));
    r->m[0] = c * sx;
    r->m[1] = s * sx;
    r->m[4] = -s * sy;
    r->m[5] = c * sy;
    r->m[10] = 1.0f;
    r->m[12] = x;
    r->m[13] = y;
    r->m[14] = z;
    r->m[15] = 1.0f;
}

// YXZ sirali euler + olcek + oteleme (3D)
void engine_mat4_trs(_engine_Mat4 *r, _engine_Vec3 pos, _engine_Vec3 rot, _engine_Vec3 scale)
{
    float cx = cosf(rot.x), sx = sinf(rot.x);
    float cy = cosf(rot.y), sy = sinf(rot.y);
    float cz = cosf(rot.z), sz = sinf(rot.z);
    float r00 = cy * cz + sy * sx * sz;
    float r01 = cx * sz;
    float r02 = -sy * cz + cy * sx * sz;
    float r10 = -cy * sz + sy * sx * cz;
    float r11 = cx * cz;
    float r12 = sy * sz + cy * sx * cz;
    float r20 = sy * cx;
    float r21 = -sx;
    float r22 = cy * cx;
    r->m[0] = r00 * scale.x;
    r->m[1] = r01 * scale.x;
    r->m[2] = r02 * scale.x;
    r->m[3] = 0.0f;
    r->m[4] = r10 * scale.y;
    r->m[5] = r11 * scale.y;
    r->m[6] = r12 * scale.y;
    r->m[7] = 0.0f;
    r->m[8] = r20 * scale.z;
    r->m[9] = r21 * scale.z;
    r->m[10] = r22 * scale.z;
    r->m[11] = 0.0f;
    r->m[12] = pos.x;
    r->m[13] = pos.y;
    r->m[14] = pos.z;
    r->m[15] = 1.0f;
}

void engine_mat4_ortho(_engine_Mat4 *r, float left, float right, float bottom, float top, float znear, float zfar)
{
    memset(r, 0, sizeof(*r));
    r->m[0] = 2.0f / (right - left);
    r->m[5] = 2.0f / (top - bottom);
    r->m[12] = -(right + left) / (right - left);
    r->m[13] = -(top + bottom) / (top - bottom);
#if ENGINE_DEPTH_ZERO_TO_ONE
    r->m[10] = 1.0f / (znear - zfar);
    r->m[14] = znear / (znear - zfar);
#else
    r->m[10] = -2.0f / (zfar - znear);
    r->m[14] = -(zfar + znear) / (zfar - znear);
#endif
    r->m[15] = 1.0f;
}

void engine_mat4_perspective(_engine_Mat4 *r, float fovYRadians, float aspect, float znear, float zfar)
{
    float f = 1.0f / tanf(fovYRadians * 0.5f);
    memset(r, 0, sizeof(*r));
    r->m[0] = f / aspect;
    r->m[5] = f;
    r->m[11] = -1.0f;
#if ENGINE_DEPTH_ZERO_TO_ONE
    r->m[10] = zfar / (znear - zfar);
    r->m[14] = (znear * zfar) / (znear - zfar);
#else
    r->m[10] = (zfar + znear) / (znear - zfar);
    r->m[14] = (2.0f * zfar * znear) / (znear - zfar);
#endif
}

static _engine_Vec3 _engine_v3sub(_engine_Vec3 a, _engine_Vec3 b)
{
    _engine_Vec3 r = {a.x - b.x, a.y - b.y, a.z - b.z};
    return r;
}

static _engine_Vec3 _engine_v3cross(_engine_Vec3 a, _engine_Vec3 b)
{
    _engine_Vec3 r = {a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x};
    return r;
}

static float _engine_v3dot(_engine_Vec3 a, _engine_Vec3 b)
{
    return a.x * b.x + a.y * b.y + a.z * b.z;
}

static _engine_Vec3 _engine_v3norm(_engine_Vec3 a)
{
    float l = sqrtf(_engine_v3dot(a, a));
    if (l > 1e-8f)
    {
        float inv = 1.0f / l;
        a.x *= inv;
        a.y *= inv;
        a.z *= inv;
    }
    return a;
}

void engine_mat4_lookat(_engine_Mat4 *r, _engine_Vec3 eye, _engine_Vec3 center, _engine_Vec3 up)
{
    _engine_Vec3 f = _engine_v3norm(_engine_v3sub(center, eye));
    _engine_Vec3 s = _engine_v3norm(_engine_v3cross(f, up));
    _engine_Vec3 u = _engine_v3cross(s, f);
    r->m[0] = s.x;
    r->m[1] = u.x;
    r->m[2] = -f.x;
    r->m[3] = 0.0f;
    r->m[4] = s.y;
    r->m[5] = u.y;
    r->m[6] = -f.y;
    r->m[7] = 0.0f;
    r->m[8] = s.z;
    r->m[9] = u.z;
    r->m[10] = -f.z;
    r->m[11] = 0.0f;
    r->m[12] = -_engine_v3dot(s, eye);
    r->m[13] = -_engine_v3dot(u, eye);
    r->m[14] = _engine_v3dot(f, eye);
    r->m[15] = 1.0f;
}

static inline int _engine_sokol_create_image(int width, int height, int format, const void *data, int dataSize, bool isRT)
{
    sg_image_desc desc = {0};
    desc.type = SG_IMAGETYPE_2D;
    desc.width = width;
    desc.height = height;
    desc.pixel_format = format;
    desc.num_mipmaps = 1;

    if (isRT)
    {
        // Render target: determine attachment type from pixel format
        sg_pixel_format pf = desc.pixel_format;
        if (pf == SG_PIXELFORMAT_DEPTH || pf == SG_PIXELFORMAT_DEPTH_STENCIL)
        {
            desc.usage.depth_stencil_attachment = true;
        }
        else
        {
            desc.usage.color_attachment = true;
        }
        desc.usage.immutable = false;
        desc.sample_count = 1;
    }
    else if (data && dataSize > 0)
    {
        // Regular texture with initial data
        desc.usage.immutable = true;
        desc.data.mip_levels[0].ptr = data;
        desc.data.mip_levels[0].size = (size_t)dataSize;
    }
    else
    {
        // Dynamic texture (no initial data)
        desc.usage.dynamic_update = true;
        desc.usage.immutable = false;
    }

    sg_image img = sg_make_image(&desc);
    return (int)img.id;
}

int engine_sokol_create_view(int imgId, int viewType)
{
    sg_image img = {(uint32_t)imgId};
    sg_view_desc desc = {0};
    switch (viewType)
    {
    case 1:
        desc.color_attachment.image = img;
        break;
    case 2:
        desc.depth_stencil_attachment.image = img;
        break;
    default:
        desc.texture.image = img;
        break;
    }
    sg_view view = sg_make_view(&desc);
    return (int)view.id;
}

void *_engine_bitmapData_empty(int w, int h, struct _engine_Color clearColor)
{
    _engine_BitmapData *bitmap = (_engine_BitmapData *)calloc(1, sizeof(_engine_BitmapData));
    bitmap->width = w;
    bitmap->height = h;
    bitmap->pixels = (_engine_Color *)malloc(sizeof(_engine_Color) * w * h);
    for (int i = 0; i < w * h; i++)
    {
        bitmap->pixels[i] = clearColor;
    }
    bitmap->_dirty = 1;
    bitmap->_texture = 0;
    return bitmap;
}

void _engine_bitmapData_sync(_engine_BitmapData *bitmap)
{
    if (!bitmap || !bitmap->_dirty || !bitmap->pixels)
        return;
    bitmap->_dirty = 0;
    if (!bitmap->_texture)
    {
        bitmap->_texture = _engine_sokol_create_image(bitmap->width, bitmap->height, SG_PIXELFORMAT_RGBA8, bitmap->pixels, sizeof(_engine_Color) * bitmap->width * bitmap->height, false);
        bitmap->_textureView = engine_sokol_create_view(bitmap->_texture, 0);
    }
    else
    {
        sg_image img = {(uint32_t)bitmap->_texture};
        int bpp = 4; // assume RGBA8
        size_t dataSize = (size_t)(bitmap->width * bitmap->height * bpp);
        sg_image_data imgData = {0};
        imgData.mip_levels[0].ptr = bitmap->pixels;
        imgData.mip_levels[0].size = dataSize;
        sg_update_image(img, &imgData);
    }
}

_engine_BitmapData *engine_bitmapData_create_render_target(int width, int height)
{
    if (width <= 0 || height <= 0)
        return NULL;

    _engine_BitmapData *bitmap = (_engine_BitmapData *)calloc(1, sizeof(_engine_BitmapData));
    bitmap->width = width;
    bitmap->height = height;
    bitmap->pixels = NULL; // icerigi GPU uretir, CPU tarafinda piksel tutulmaz

    bitmap->_texture = _engine_sokol_create_image(width, height, SG_PIXELFORMAT_RGBA8, NULL, 0, true);
    bitmap->_colorAttachmentView = engine_sokol_create_view(bitmap->_texture, 1);
    bitmap->_textureView = engine_sokol_create_view(bitmap->_texture, 0);
    bitmap->_depthTexture = _engine_sokol_create_image(width, height, SG_PIXELFORMAT_DEPTH_STENCIL, NULL, 0, true);
    bitmap->_depthAttachmentView = engine_sokol_create_view(bitmap->_depthTexture, 2);
    bitmap->_dirty = 0;
    return bitmap;
}

// ---------------------------------------------------------------------------
// shader kaynaklari: GLCORE 410 / GLES3 + WebGL2 (300 es) / Metal MSL
// ---------------------------------------------------------------------------

#if defined(SOKOL_GLCORE) || defined(SOKOL_GLES3)
#if defined(SOKOL_GLCORE)
#define _ENGINE_VS_HEADER "#version 410\n"
#else
#define _ENGINE_VS_HEADER "#version 300 es\nprecision highp float;\n"
#endif

static const char *_engine_vs_src =
    _ENGINE_VS_HEADER
    "uniform mat4 u_viewProj;\n"
    "in vec3 a_pos;\n"
    "in vec2 a_uv;\n"
    "in vec4 a_color;\n"
    "in vec4 a_m0;\n"
    "in vec4 a_m1;\n"
    "in vec4 a_m2;\n"
    "in vec4 a_m3;\n"
    "in vec4 a_uvrect;\n"
    "in vec4 a_tint;\n"
    "out vec2 v_uv;\n"
    "out vec4 v_color;\n"
    "void main() {\n"
    "  mat4 model = mat4(a_m0, a_m1, a_m2, a_m3);\n"
    "  gl_Position = u_viewProj * (model * vec4(a_pos, 1.0));\n"
    "  vec2 uv = (a_uvrect.z == 0.0 && a_uvrect.w == 0.0) ? vec2(0.5, 0.5) : (a_uvrect.xy + a_uv * a_uvrect.zw);\n"
    "  v_uv = uv;\n"
    "  v_color = a_color * a_tint;\n"
    "}\n";
#elif defined(SOKOL_METAL)
static const char *_engine_vs_src =
    "#include <metal_stdlib>\n"
    "using namespace metal;\n"
    "struct vs_params { float4x4 viewProj; };\n"
    "struct vs_in {\n"
    "  float3 pos [[attribute(0)]];\n"
    "  float2 uv [[attribute(1)]];\n"
    "  float4 color [[attribute(2)]];\n"
    "  float4 m0 [[attribute(3)]];\n"
    "  float4 m1 [[attribute(4)]];\n"
    "  float4 m2 [[attribute(5)]];\n"
    "  float4 m3 [[attribute(6)]];\n"
    "  float4 uvrect [[attribute(7)]];\n"
    "  float4 tint [[attribute(8)]];\n"
    "};\n"
    "struct vs_out { float4 pos [[position]]; float2 uv; float4 color; };\n"
    "vertex vs_out _main(vs_in in [[stage_in]], constant vs_params& params [[buffer(0)]]) {\n"
    "  float4x4 model = float4x4(in.m0, in.m1, in.m2, in.m3);\n"
    "  vs_out out;\n"
    "  out.pos = params.viewProj * (model * float4(in.pos, 1.0));\n"
    "  float2 uv = (in.uvrect.z == 0.0 && in.uvrect.w == 0.0) ? float2(0.5, 0.5) : (in.uvrect.xy + in.uv * in.uvrect.zw);\n"
    "  out.uv = uv;\n"
    "  out.color = in.color * in.tint;\n"
    "  return out;\n"
    "}\n";
#else
#error "engine.c: desteklenmeyen sokol backend"
#endif

// ---------------------------------------------------------------------------
// renderer durumu
// ---------------------------------------------------------------------------

#define ENGINE_MAX_MESHES 4096
#define ENGINE_MAX_PIPELINES 64
#define ENGINE_MAX_TARGET_STACK 16
#define ENGINE_MAX_PROFILE_SAMPLES 256

typedef struct
{
    _engine_BitmapData *target; // NULL => swapchain
    _engine_Color clear;
    bool doClear;
    int scissorTop;
} _engine_TargetEntry;

typedef struct
{
    uint32_t stateKey;
    sg_pipeline pip;
} _engine_PipEntry;

typedef struct
{
    uint64_t key; // (layer<<48) | (pip<<40) | (mesh<<24) | (texView & 0xFFFFFF)
    uint32_t instance;
    uint16_t mesh;
    uint16_t pip;
    uint32_t texView;
    unsigned char samplerType;
} _engine_DrawItem;

typedef struct
{
    float viewProj[16];
} _engine_vs_params;

static struct
{
    bool valid;
    _engine_Shader basicShader;
    uint32_t nextShaderId;
    sg_sampler smpLinear;
    sg_sampler smpNearest;
    bool pointFilter;

    _engine_PipEntry pips[ENGINE_MAX_PIPELINES];
    int pipCount;

    _engine_Mesh *meshes[ENGINE_MAX_MESHES];
    int meshCount;

    sg_buffer instBuf;
    int instBufCapacity;

    _engine_Instance *instances; // frame boyunca cizim sirasinda birikir
    _engine_Instance *upload;    // sort sonrasi GPU'ya gidecek sirali kopya
    _engine_DrawItem *items;
    int itemCount;
    int itemCapacity;

    _engine_Mesh *quadMesh;
    _engine_Mesh *cubeMesh;
    _engine_BitmapData *whiteTex;
    _engine_Material defaultMaterial;

    _engine_vs_params camera;
    bool inFrame;
    bool sokolPassActive;
    engine_pass_desc currentPass;
    bool hasPass;

    _engine_TargetEntry targetStack[ENGINE_MAX_TARGET_STACK];
    int targetTop; // -1 => stack bos
    _engine_Scissor scissors[64];
    int scissorTop;

    int frameInstanceUsed; // bu frame'de append edilen toplam instance
    int instBufNeeded;     // bir sonraki frame icin gereken kapasite

    engine_profile_sample profile[ENGINE_MAX_PROFILE_SAMPLES];
    int profileCount;

    int statDrawCalls;
    int statInstances;
    uint64_t lastTime;
    float dt;
} _engine;

// ---------------------------------------------------------------------------
// pipeline cache
// ---------------------------------------------------------------------------

static sg_blend_factor _engine_blend_factor(int f)
{
    switch (f)
    {
    case ENGINE_BLEND_ZERO:
        return SG_BLENDFACTOR_ZERO;
    case ENGINE_BLEND_ONE:
        return SG_BLENDFACTOR_ONE;
    case ENGINE_BLEND_SRC_ALPHA:
        return SG_BLENDFACTOR_SRC_ALPHA;
    case ENGINE_BLEND_ONE_MINUS_SRC_ALPHA:
        return SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA;
    case ENGINE_BLEND_DST_ALPHA:
        return SG_BLENDFACTOR_DST_ALPHA;
    case ENGINE_BLEND_ONE_MINUS_DST_ALPHA:
        return SG_BLENDFACTOR_ONE_MINUS_DST_ALPHA;
    case ENGINE_BLEND_SRC_COLOR:
        return SG_BLENDFACTOR_SRC_COLOR;
    case ENGINE_BLEND_ONE_MINUS_SRC_COLOR:
        return SG_BLENDFACTOR_ONE_MINUS_SRC_COLOR;
    case ENGINE_BLEND_DST_COLOR:
        return SG_BLENDFACTOR_DST_COLOR;
    case ENGINE_BLEND_ONE_MINUS_DST_COLOR:
        return SG_BLENDFACTOR_ONE_MINUS_DST_COLOR;
    default:
        return SG_BLENDFACTOR_ONE;
    }
}

static void _engine_setup_layout(sg_pipeline_desc *pd)
{
    pd->layout.buffers[0].stride = sizeof(_engine_Vertex);
    pd->layout.buffers[1].stride = sizeof(_engine_Instance);
    pd->layout.buffers[1].step_func = SG_VERTEXSTEP_PER_INSTANCE;

    pd->layout.attrs[0].buffer_index = 0;
    pd->layout.attrs[0].offset = (int)offsetof(_engine_Vertex, position);
    pd->layout.attrs[0].format = SG_VERTEXFORMAT_FLOAT3;
    pd->layout.attrs[1].buffer_index = 0;
    pd->layout.attrs[1].offset = (int)offsetof(_engine_Vertex, uv);
    pd->layout.attrs[1].format = SG_VERTEXFORMAT_FLOAT2;
    pd->layout.attrs[2].buffer_index = 0;
    pd->layout.attrs[2].offset = (int)offsetof(_engine_Vertex, color);
    pd->layout.attrs[2].format = SG_VERTEXFORMAT_UBYTE4N;

    for (int i = 0; i < 4; i++)
    {
        pd->layout.attrs[3 + i].buffer_index = 1;
        pd->layout.attrs[3 + i].offset = (int)(offsetof(_engine_Instance, model) + (size_t)i * 16);
        pd->layout.attrs[3 + i].format = SG_VERTEXFORMAT_FLOAT4;
    }
    pd->layout.attrs[7].buffer_index = 1;
    pd->layout.attrs[7].offset = (int)offsetof(_engine_Instance, uvRect);
    pd->layout.attrs[7].format = SG_VERTEXFORMAT_FLOAT4;
    pd->layout.attrs[8].buffer_index = 1;
    pd->layout.attrs[8].offset = (int)offsetof(_engine_Instance, tint);
    pd->layout.attrs[8].format = SG_VERTEXFORMAT_UBYTE4N;
}

static int _engine_get_pipeline(const _engine_Material *mat, int index32)
{
    uint32_t key = (uint32_t)(mat->srcBlend & 15) |
                   ((uint32_t)(mat->dstBlend & 15) << 4) |
                   ((uint32_t)(mat->cullMode & 3) << 8) |
                   ((mat->depthTest ? 1u : 0u) << 10) |
                   ((mat->depthWrite ? 1u : 0u) << 11) |
                   ((index32 ? 1u : 0u) << 12);
    key ^= mat->shader ? (mat->shader->id * 0x9E3779B9u) : 0u;
    for (int i = 0; i < _engine.pipCount; i++)
    {
        if (_engine.pips[i].stateKey == key)
            return i;
    }
    if (_engine.pipCount >= ENGINE_MAX_PIPELINES)
        return 0;

    sg_pipeline_desc pd = {0};
    pd.shader = mat->shader ? mat->shader->handle : _engine.basicShader.handle;
    pd.index_type = index32 ? SG_INDEXTYPE_UINT32 : SG_INDEXTYPE_UINT16;
    pd.primitive_type = SG_PRIMITIVETYPE_TRIANGLES;
    pd.face_winding = SG_FACEWINDING_CCW;
    pd.cull_mode = (mat->cullMode == ENGINE_CULL_FRONT)  ? SG_CULLMODE_FRONT
                   : (mat->cullMode == ENGINE_CULL_BACK) ? SG_CULLMODE_BACK
                                                         : SG_CULLMODE_NONE;
    pd.depth.compare = mat->depthTest ? SG_COMPAREFUNC_LESS_EQUAL : SG_COMPAREFUNC_ALWAYS;
    pd.depth.write_enabled = mat->depthWrite ? true : false;
    _engine_setup_layout(&pd);

    bool opaque = (mat->srcBlend == ENGINE_BLEND_ONE && mat->dstBlend == ENGINE_BLEND_ZERO);
    if (!opaque)
    {
        pd.colors[0].blend.enabled = true;
        pd.colors[0].blend.src_factor_rgb = _engine_blend_factor(mat->srcBlend);
        pd.colors[0].blend.dst_factor_rgb = _engine_blend_factor(mat->dstBlend);
        pd.colors[0].blend.op_rgb = SG_BLENDOP_ADD;
        pd.colors[0].blend.src_factor_alpha = SG_BLENDFACTOR_ONE;
        pd.colors[0].blend.dst_factor_alpha = SG_BLENDFACTOR_ONE_MINUS_SRC_ALPHA;
        pd.colors[0].blend.op_alpha = SG_BLENDOP_ADD;
    }
    pd.label = "engine-batch-pip";

    int slot = _engine.pipCount++;
    _engine.pips[slot].stateKey = key;
    _engine.pips[slot].pip = sg_make_pipeline(&pd);
    return slot;
}

// ---------------------------------------------------------------------------
// mesh (2D quad da 3D model de ayni yapi)
// ---------------------------------------------------------------------------

_engine_Mesh *engine_mesh_create(const _engine_Vertex *verts, int vertexCount, const void *indices, int indexCount, int index32)
{
    if (!verts || vertexCount <= 0 || !indices || indexCount <= 0)
        return NULL;
    if (_engine.meshCount >= ENGINE_MAX_MESHES)
        return NULL;

    _engine_Mesh *mesh = (_engine_Mesh *)calloc(1, sizeof(_engine_Mesh));
    sg_buffer_desc vd = {0};
    vd.usage.vertex_buffer = true;
    vd.usage.immutable = true;
    vd.data.ptr = verts;
    vd.data.size = (size_t)vertexCount * sizeof(_engine_Vertex);
    mesh->vbuf = sg_make_buffer(&vd);

    sg_buffer_desc id = {0};
    id.usage.index_buffer = true;
    id.usage.immutable = true;
    id.data.ptr = indices;
    id.data.size = (size_t)indexCount * (index32 ? 4u : 2u);
    mesh->ibuf = sg_make_buffer(&id);

    mesh->indexCount = indexCount;
    mesh->index32 = index32 ? 1 : 0;
    mesh->slot = _engine.meshCount;
    _engine.meshes[_engine.meshCount++] = mesh;
    return mesh;
}

void engine_mesh_destroy(_engine_Mesh *mesh)
{
    if (!mesh)
        return;
    sg_destroy_buffer(mesh->vbuf);
    sg_destroy_buffer(mesh->ibuf);
    if (mesh->slot >= 0 && mesh->slot < _engine.meshCount && _engine.meshes[mesh->slot] == mesh)
        _engine.meshes[mesh->slot] = NULL;
    free(mesh);
}

_engine_Mesh *engine_quad_mesh(void) { return _engine.quadMesh; }
_engine_Mesh *engine_cube_mesh(void) { return _engine.cubeMesh; }

static _engine_Mesh *_engine_make_quad(void)
{
    // birim kare, merkez (0,0): 2D sprite ve 3D billboard ayni mesh
    _engine_Vertex v[4] = {
        {{-0.5f, -0.5f, 0.0f}, {0.0f, 1.0f}, {255, 255, 255, 255}},
        {{0.5f, -0.5f, 0.0f}, {1.0f, 1.0f}, {255, 255, 255, 255}},
        {{0.5f, 0.5f, 0.0f}, {1.0f, 0.0f}, {255, 255, 255, 255}},
        {{-0.5f, 0.5f, 0.0f}, {0.0f, 0.0f}, {255, 255, 255, 255}},
    };
    uint16_t idx[6] = {0, 1, 2, 0, 2, 3};
    return engine_mesh_create(v, 4, idx, 6, 0);
}

static _engine_Mesh *_engine_make_cube(void)
{
    static const float n[6][3] = {{0, 0, 1}, {0, 0, -1}, {1, 0, 0}, {-1, 0, 0}, {0, 1, 0}, {0, -1, 0}};
    static const float corners[6][4][3] = {
        {{-1, -1, 1}, {1, -1, 1}, {1, 1, 1}, {-1, 1, 1}},
        {{1, -1, -1}, {-1, -1, -1}, {-1, 1, -1}, {1, 1, -1}},
        {{1, -1, 1}, {1, -1, -1}, {1, 1, -1}, {1, 1, 1}},
        {{-1, -1, -1}, {-1, -1, 1}, {-1, 1, 1}, {-1, 1, -1}},
        {{-1, 1, 1}, {1, 1, 1}, {1, 1, -1}, {-1, 1, -1}},
        {{-1, -1, -1}, {1, -1, -1}, {1, -1, 1}, {-1, -1, 1}},
    };
    static const float uvs[4][2] = {{0, 1}, {1, 1}, {1, 0}, {0, 0}};
    _engine_Vertex v[24];
    // isik yokken hacim gorunsun diye yuzler vertex renginde sabit tonlandi
    for (int f = 0; f < 6; f++)
    {
        float shade = 0.55f + 0.45f * fabsf(n[f][0] * 0.4f + n[f][1] * 1.0f + n[f][2] * 0.7f);
        if (shade > 1.0f)
            shade = 1.0f;
        unsigned char c = (unsigned char)(shade * 255.0f);
        for (int i = 0; i < 4; i++)
        {
            _engine_Vertex *vert = &v[f * 4 + i];
            vert->position.x = corners[f][i][0] * 0.5f;
            vert->position.y = corners[f][i][1] * 0.5f;
            vert->position.z = corners[f][i][2] * 0.5f;
            vert->uv.x = uvs[i][0];
            vert->uv.y = uvs[i][1];
            vert->color.r = c;
            vert->color.g = c;
            vert->color.b = c;
            vert->color.a = 255;
        }
    }
    uint16_t idx[36];
    for (int f = 0; f < 6; f++)
    {
        uint16_t b = (uint16_t)(f * 4);
        idx[f * 6 + 0] = b;
        idx[f * 6 + 1] = (uint16_t)(b + 1);
        idx[f * 6 + 2] = (uint16_t)(b + 2);
        idx[f * 6 + 3] = b;
        idx[f * 6 + 4] = (uint16_t)(b + 2);
        idx[f * 6 + 5] = (uint16_t)(b + 3);
    }
    return engine_mesh_create(v, 24, idx, 36, 0);
}

// ---------------------------------------------------------------------------
// init / shutdown
// ---------------------------------------------------------------------------

static void _engine_reserve(int need)
{
    if (need <= _engine.itemCapacity)
        return;
    int cap = _engine.itemCapacity ? _engine.itemCapacity : 4096;
    while (cap < need)
        cap *= 2;
    _engine.instances = (_engine_Instance *)realloc(_engine.instances, (size_t)cap * sizeof(_engine_Instance));
    _engine.upload = (_engine_Instance *)realloc(_engine.upload, (size_t)cap * sizeof(_engine_Instance));
    _engine.items = (_engine_DrawItem *)realloc(_engine.items, (size_t)cap * sizeof(_engine_DrawItem));
    _engine.itemCapacity = cap;
}

// Efekt govdesinden (engine_shader.h makro dili) tam sg_shader kurar.
// Ortak instanced vertex shader + kullanicinin fragment govdesi.
static sg_shader _engine_make_effect_shader(const char *effectBody)
{
    engine_shader_source source = {0};
    if (!engine_shader_build_effect(effectBody, &source))
        return (sg_shader){0};

    static const char *attrNames[9] = {"a_pos", "a_uv", "a_color", "a_m0", "a_m1", "a_m2", "a_m3", "a_uvrect", "a_tint"};
    sg_shader_desc sd = {0};
    sd.vertex_func.source = _engine_vs_src;
#if defined(SOKOL_METAL)
    sd.vertex_func.entry = "_main";
    sd.fragment_func.entry = source.metalEntry;
#endif
    for (int i = 0; i < 9; i++)
    {
        sd.attrs[i].glsl_name = attrNames[i];
        sd.attrs[i].base_type = SG_SHADERATTRBASETYPE_FLOAT;
    }
    sd.uniform_blocks[0].stage = SG_SHADERSTAGE_VERTEX;
    sd.uniform_blocks[0].size = sizeof(_engine_vs_params);
    sd.uniform_blocks[0].layout = SG_UNIFORMLAYOUT_STD140;
    sd.uniform_blocks[0].msl_buffer_n = 0;
    sd.uniform_blocks[0].hlsl_register_b_n = 0;
    sd.uniform_blocks[0].glsl_uniforms[0].type = SG_UNIFORMTYPE_MAT4;
    sd.uniform_blocks[0].glsl_uniforms[0].glsl_name = "u_viewProj";
    sd.views[0].texture.stage = SG_SHADERSTAGE_FRAGMENT;
    sd.views[0].texture.image_type = SG_IMAGETYPE_2D;
    sd.views[0].texture.sample_type = SG_IMAGESAMPLETYPE_FLOAT;
    sd.views[0].texture.msl_texture_n = 0;
    sd.views[0].texture.hlsl_register_t_n = 0;
    sd.samplers[0].stage = SG_SHADERSTAGE_FRAGMENT;
    sd.samplers[0].sampler_type = SG_SAMPLERTYPE_FILTERING;
    sd.samplers[0].msl_sampler_n = 0;
    sd.samplers[0].hlsl_register_s_n = 0;
    sd.texture_sampler_pairs[0].stage = SG_SHADERSTAGE_FRAGMENT;
    sd.texture_sampler_pairs[0].view_slot = 0;
    sd.texture_sampler_pairs[0].sampler_slot = 0;
    sd.texture_sampler_pairs[0].glsl_name = "tex";
    sd.label = "engine-effect-shader";
    sd.fragment_func.source = source.fragmentSource;
    sg_shader shader = sg_make_shader(&sd);
    engine_shader_free_source(&source);
    return shader;
}

// Modullere acik API: kendi efekt shader'ini yarat (ornegin font SDF'i).
_engine_Shader *engine_shader_create_effect(const char *effectBody)
{
    sg_shader handle = _engine_make_effect_shader(effectBody);
    if (handle.id == SG_INVALID_ID)
        return NULL;
    _engine_Shader *shader = (_engine_Shader *)calloc(1, sizeof(_engine_Shader));
    shader->handle = handle;
    shader->id = _engine.nextShaderId++;
    return shader;
}

void engine_shader_destroy(_engine_Shader *shader)
{
    if (!shader)
        return;
    sg_destroy_shader(shader->handle);
    free(shader);
}

void engine_renderer_init(int maxInstances)
{
    if (_engine.valid)
        return;
    if (maxInstances <= 0)
        maxInstances = 16384;

    _engine.nextShaderId = 100;
    _engine.basicShader.handle = _engine_make_effect_shader(_engine_basic_effect_body);
    if (_engine.basicShader.handle.id == SG_INVALID_ID)
        return;
    _engine.basicShader.id = 1;

    sg_sampler_desc smp = {0};
    smp.min_filter = SG_FILTER_LINEAR;
    smp.mag_filter = SG_FILTER_LINEAR;
    smp.wrap_u = SG_WRAP_CLAMP_TO_EDGE;
    smp.wrap_v = SG_WRAP_CLAMP_TO_EDGE;
    _engine.smpLinear = sg_make_sampler(&smp);
    smp.min_filter = SG_FILTER_NEAREST;
    smp.mag_filter = SG_FILTER_NEAREST;
    _engine.smpNearest = sg_make_sampler(&smp);

    sg_buffer_desc ib = {0};
    ib.usage.vertex_buffer = true;
    ib.usage.immutable = false;
    ib.usage.stream_update = true;
    ib.size = (size_t)maxInstances * sizeof(_engine_Instance);
    ib.label = "engine-instance-buffer";
    _engine.instBuf = sg_make_buffer(&ib);
    _engine.instBufCapacity = maxInstances;

    _engine_reserve(maxInstances);

    _engine.quadMesh = _engine_make_quad();
    _engine.cubeMesh = _engine_make_cube();

    _engine_Color white = {255, 255, 255, 255};
    _engine.whiteTex = (_engine_BitmapData *)_engine_bitmapData_empty(1, 1, white);
    _engine_bitmapData_sync(_engine.whiteTex);

    _engine.defaultMaterial.shader = &_engine.basicShader;
    _engine.defaultMaterial.srcBlend = ENGINE_BLEND_SRC_ALPHA;
    _engine.defaultMaterial.dstBlend = ENGINE_BLEND_ONE_MINUS_SRC_ALPHA;
    _engine.defaultMaterial.cullMode = ENGINE_CULL_NONE;

    engine_mat4_identity((_engine_Mat4 *)_engine.camera.viewProj);
    _engine.valid = true;
}

void engine_renderer_shutdown(void)
{
    if (!_engine.valid)
        return;
    for (int i = 0; i < _engine.meshCount; i++)
    {
        if (_engine.meshes[i])
        {
            sg_destroy_buffer(_engine.meshes[i]->vbuf);
            sg_destroy_buffer(_engine.meshes[i]->ibuf);
            free(_engine.meshes[i]);
            _engine.meshes[i] = NULL;
        }
    }
    for (int i = 0; i < _engine.pipCount; i++)
        sg_destroy_pipeline(_engine.pips[i].pip);
    sg_destroy_buffer(_engine.instBuf);
    sg_destroy_sampler(_engine.smpLinear);
    sg_destroy_sampler(_engine.smpNearest);
    sg_destroy_shader(_engine.basicShader.handle);
    free(_engine.instances);
    free(_engine.upload);
    free(_engine.items);
    memset(&_engine, 0, sizeof(_engine));
}

// ---------------------------------------------------------------------------
// kamera
// ---------------------------------------------------------------------------

static uint64_t _engine_make_sort_key(const engine_pass_desc *pass, const _engine_Material *mat, const _engine_Mesh *mesh, int layer)
{
    uint64_t base = (uint64_t)(uint16_t)layer << 32;
    uint64_t materialKey = (uint64_t)(uint16_t)(mat->cullMode | (mat->depthTest << 2) | (mat->depthWrite << 3));
    uint64_t meshKey = (uint64_t)(uint16_t)mesh->slot << 16;

    switch (pass ? pass->sortMode : ENGINE_SORT_OPAQUE)
    {
    case ENGINE_SORT_TRANSPARENT:
        return (base | materialKey | meshKey);
    case ENGINE_SORT_UI:
        return (base | (materialKey << 8) | meshKey);
    case ENGINE_SORT_PAINTER:
        return ((uint64_t)(uint16_t)layer << 48) | (materialKey << 16) | meshKey;
    case ENGINE_SORT_OPAQUE:
    case ENGINE_SORT_NONE:
    default:
        return (meshKey | (materialKey << 8) | base);
    }
}

void engine_flush(void);

void engine_set_view_projection(_engine_Mat4 *viewProj)
{
    memcpy(_engine.camera.viewProj, viewProj->m, sizeof(float) * 16);
}

// Pass, aktif render target'i degistirmez: sadece sort semantigini ve kamerayi kurar.
// Bu sayede ayni target icine birden fazla pass (opaque/transparent/ui) girebilir.
void engine_begin_pass(engine_pass_desc *desc)
{
    engine_flush(); // onceki pass'in siralamasi bir sonrakine karismasin
    if (desc)
    {
        _engine.currentPass = *desc;
        memcpy(_engine.camera.viewProj, desc->viewProj.m, sizeof(float) * 16);
    }
    else
    {
        memset(&_engine.currentPass, 0, sizeof(_engine.currentPass));
        _engine.currentPass.sortMode = ENGINE_SORT_OPAQUE;
    }
    _engine.hasPass = true;
}

void engine_end_pass(void)
{
    if (!_engine.hasPass)
        return;
    engine_flush();
    _engine.hasPass = false;
}

// sol-ust (0,0) baslangicli piksel uzayi
void engine_camera_2d(float width, float height)
{
    _engine_Mat4 m;
    engine_mat4_ortho(&m, 0.0f, width, height, 0.0f, -1024.0f, 1024.0f);
    engine_set_view_projection(&m);
}

void engine_camera_3d(_engine_Vec3 eye, _engine_Vec3 target, float fovYRadians, float aspect, float znear, float zfar)
{
    _engine_Mat4 proj, view, vp;
    _engine_Vec3 up = {0.0f, 1.0f, 0.0f};
    engine_mat4_perspective(&proj, fovYRadians, aspect, znear, zfar);
    engine_mat4_lookat(&view, eye, target, up);
    engine_mat4_multiply(&proj, &view, &vp);
    engine_set_view_projection(&vp);
}

void engine_set_point_filter(int enabled)
{
    _engine.pointFilter = enabled ? true : false;
}

_engine_Material *engine_default_material(void)
{
    return &_engine.defaultMaterial;
}

// ---------------------------------------------------------------------------
// cizim kuyrugu
// ---------------------------------------------------------------------------

void engine_draw_mesh(_engine_Mesh *mesh, _engine_Material *material, _engine_Mat4 *model,
                      _engine_Color tint, float u0, float v0, float u1, float v1, int layer)
{
    if (!_engine.valid || !mesh || !model)
        return;
    _engine_Material *mat = material ? material : &_engine.defaultMaterial;
    _engine_BitmapData *tex = mat->mainTexture ? mat->mainTexture : _engine.whiteTex;
    _engine_bitmapData_sync(tex);

    _engine_reserve(_engine.itemCount + 1);
    int idx = _engine.itemCount++;

    _engine_Instance *inst = &_engine.instances[idx];
    memcpy(inst->model, model->m, sizeof(float) * 16);
    inst->uvRect[0] = u0;
    inst->uvRect[1] = v0;
    inst->uvRect[2] = u1 - u0;
    inst->uvRect[3] = v1 - v0;
    inst->tint[0] = tint.r;
    inst->tint[1] = tint.g;
    inst->tint[2] = tint.b;
    inst->tint[3] = tint.a;

    int pip = _engine_get_pipeline(mat, mesh->index32);
    uint32_t texView = (uint32_t)tex->_textureView;

    _engine_DrawItem *item = &_engine.items[idx];
    item->instance = (uint32_t)idx;
    item->mesh = (uint16_t)mesh->slot;
    item->pip = (uint16_t)pip;
    item->texView = texView;
    item->samplerType = (unsigned char)(mat->samplerType == ENGINE_SAMPLER_NEAREST ? ENGINE_SAMPLER_NEAREST : ENGINE_SAMPLER_LINEAR);
    item->key = _engine_make_sort_key(_engine.hasPass ? &_engine.currentPass : NULL, mat, mesh, layer);
    item->key |= ((uint64_t)(uint8_t)pip << 40) | (uint64_t)(texView & 0xFFFFFFu);
}

// UV gerektirmeyen renkli (tekstursuz) mesh'ler icin ortak vertex yapisini korur:
// uvRect 0 olursa shader 0.5,0.5'e donusup white texture uzerinden renklendirme yapar.
void engine_draw_mesh_color(_engine_Mesh *mesh, _engine_Material *material, _engine_Mat4 *model,
                            _engine_Color tint, int layer)
{
    _engine_Material mat = material ? *material : _engine.defaultMaterial;
    mat.mainTexture = _engine.whiteTex;
    engine_draw_mesh(mesh, &mat, model, tint, 0.0f, 0.0f, 0.0f, 0.0f, layer);
}

void engine_draw_ui_rect(float x, float y, float width, float height,
                         unsigned char r, unsigned char g, unsigned char b, unsigned char a, int layer)
{
    _engine_Mat4 model;
    _engine_Color tint = {r, g, b, a};
    engine_mat4_trs2d(&model, x + width * 0.5f, y + height * 0.5f, 0.0f, width, height, 0.0f);
    engine_draw_mesh_color(_engine.quadMesh, &_engine.defaultMaterial, &model, tint, layer);
}

// GUIUtility.RotateAroundPivot karsiligi: merkez etrafinda dondurulmus ui rect (elmas vb.)
void engine_draw_ui_rect_rotated(float x, float y, float width, float height, float rotation,
                                 unsigned char r, unsigned char g, unsigned char b, unsigned char a, int layer)
{
    _engine_Mat4 model;
    _engine_Color tint = {r, g, b, a};
    engine_mat4_trs2d(&model, x + width * 0.5f, y + height * 0.5f, 0.0f, width, height, rotation);
    engine_draw_mesh_color(_engine.quadMesh, &_engine.defaultMaterial, &model, tint, layer);
}

// 2D sprite: built-in quad + Z rotasyonu; x/y sol-ust kose, w/h piksel
void engine_draw_sprite(_engine_BitmapData *texture, float x, float y, float w, float h,
                        float rotation, _engine_Color tint, int layer)
{
    _engine_Material mat = _engine.defaultMaterial;
    mat.mainTexture = texture ? texture : _engine.whiteTex;
    _engine_Mat4 model;
    engine_mat4_trs2d(&model, x + w * 0.5f, y + h * 0.5f, 0.0f, w, h, rotation);
    engine_draw_mesh(_engine.quadMesh, &mat, &model, tint, 0.0f, 0.0f, 1.0f, 1.0f, layer);
}

// ---------------------------------------------------------------------------
// flush: sort -> tek buffer upload -> batch basina tek draw call
// ---------------------------------------------------------------------------

static int _engine_item_cmp(const void *a, const void *b)
{
    uint64_t ka = ((const _engine_DrawItem *)a)->key;
    uint64_t kb = ((const _engine_DrawItem *)b)->key;
    return (ka < kb) ? -1 : (ka > kb) ? 1
                                      : 0;
}

// Frame basina birden fazla flush olabildigi icin sg_update_buffer yerine
// sg_append_buffer kullanilir; her flush kendi byte offset'inden cizer.
void engine_flush(void)
{
    int count = _engine.itemCount;
    _engine.itemCount = 0;
    if (!_engine.valid || count <= 0 || !_engine.sokolPassActive)
        return;

    uint64_t flushStart = stm_now();

    _engine.instBufNeeded = _engine.frameInstanceUsed + count;
    if (_engine.instBufNeeded > _engine.instBufCapacity)
        return; // buffer bir sonraki frame basinda buyutulur
    _engine.frameInstanceUsed += count;

    qsort(_engine.items, (size_t)count, sizeof(_engine_DrawItem), _engine_item_cmp);
    for (int i = 0; i < count; i++)
        _engine.upload[i] = _engine.instances[_engine.items[i].instance];

    sg_range instRange = {_engine.upload, (size_t)count * sizeof(_engine_Instance)};
    int baseOffset = sg_append_buffer(_engine.instBuf, &instRange);

    sg_range uniRange = {&_engine.camera, sizeof(_engine.camera)};
    sg_sampler smp = _engine.pointFilter ? _engine.smpNearest : _engine.smpLinear;

    int flushDrawCalls = 0;
    int flushInstances = 0;
    int i = 0;
    int curPip = -1;
    while (i < count)
    {
        const _engine_DrawItem *head = &_engine.items[i];
        int j = i + 1;
        while (j < count &&
               _engine.items[j].pip == head->pip &&
               _engine.items[j].mesh == head->mesh &&
               _engine.items[j].texView == head->texView &&
               _engine.items[j].samplerType == head->samplerType)
        {
            j++;
        }
        _engine_Mesh *mesh = _engine.meshes[head->mesh];
        if (mesh)
        {
            if (curPip != (int)head->pip)
            {
                sg_apply_pipeline(_engine.pips[head->pip].pip);
                sg_apply_uniforms(0, &uniRange);
                curPip = (int)head->pip;
            }
            sg_bindings bind = {0};
            bind.vertex_buffers[0] = mesh->vbuf;
            bind.vertex_buffers[1] = _engine.instBuf;
            bind.vertex_buffer_offsets[1] = baseOffset + i * (int)sizeof(_engine_Instance);
            bind.index_buffer = mesh->ibuf;
            bind.views[0].id = head->texView;
            bind.samplers[0] = head->samplerType == ENGINE_SAMPLER_NEAREST ? _engine.smpNearest : smp;
            sg_apply_bindings(&bind);
            sg_draw(0, mesh->indexCount, j - i);
            flushDrawCalls++;
            flushInstances += j - i;
        }
        i = j;
    }

    _engine.statDrawCalls += flushDrawCalls;
    _engine.statInstances += flushInstances;

    if (_engine.profileCount < ENGINE_MAX_PROFILE_SAMPLES)
    {
        engine_profile_sample *s = &_engine.profile[_engine.profileCount++];
        s->pass = _engine.hasPass && _engine.currentPass.name ? _engine.currentPass.name : "<pass>";
        s->target = _engine.targetTop >= 0 ? _engine.targetStack[_engine.targetTop].target : NULL;
        s->drawCalls = flushDrawCalls;
        s->batches = flushDrawCalls;
        s->instances = flushInstances;
        s->cpuMs = (float)stm_ms(stm_diff(stm_now(), flushStart));
    }
}

int engine_stat_draw_calls(void) { return _engine.statDrawCalls; }
int engine_stat_instances(void) { return _engine.statInstances; }
int engine_profile_count(void) { return _engine.profileCount; }
const engine_profile_sample *engine_profile_sample_at(int i)
{
    if (i < 0 || i >= _engine.profileCount)
        return NULL;
    return &_engine.profile[i];
}
float engine_delta_time(void) { return _engine.dt; }
float engine_width(void) { return (float)_ENGINE_HOST_W(); }
float engine_height(void) { return (float)_ENGINE_HOST_H(); }

// ---------------------------------------------------------------------------
// frame
// ---------------------------------------------------------------------------

// Stack'teki mevcut hedef icin sokol pass'i acar. clear=false ise onceki icerik korunur,
// bu sayede pop sonrasi parent target'a kaldigi yerden cizmeye devam edilebilir.
static void _engine_open_sokol_pass(const _engine_TargetEntry *entry, bool clear)
{
    sg_pass pass = {0};
    pass.action.colors[0].load_action = clear ? SG_LOADACTION_CLEAR : SG_LOADACTION_LOAD;
    pass.action.colors[0].clear_value.r = entry->clear.r / 255.0f;
    pass.action.colors[0].clear_value.g = entry->clear.g / 255.0f;
    pass.action.colors[0].clear_value.b = entry->clear.b / 255.0f;
    pass.action.colors[0].clear_value.a = entry->clear.a / 255.0f;
    pass.action.depth.load_action = clear ? SG_LOADACTION_CLEAR : SG_LOADACTION_LOAD;
    pass.action.depth.clear_value = 1.0f;

    if (entry->target)
    {
        sg_view color = {(uint32_t)entry->target->_colorAttachmentView};
        sg_view depth = {(uint32_t)entry->target->_depthAttachmentView};
        pass.attachments.colors[0] = color;
        pass.attachments.depth_stencil = depth;
    }
    else
    {
#if defined(ENGINE_USE_GLFW)
        pass.swapchain = engine_host_swapchain();
#else
        pass.swapchain = sglue_swapchain();
#endif
    }
    sg_begin_pass(&pass);
    _engine.sokolPassActive = true;
}

static void _engine_close_sokol_pass(void)
{
    if (!_engine.sokolPassActive)
        return;
    engine_flush();
    sg_end_pass();
    _engine.sokolPassActive = false;
}

void engine_begin_frame(_engine_Color clearColor)
{
    _engine.statDrawCalls = 0;
    _engine.statInstances = 0;
    _engine.profileCount = 0;
    _engine.itemCount = 0;
    _engine.frameInstanceUsed = 0;
    memset(&_engine.currentPass, 0, sizeof(_engine.currentPass));
    _engine.currentPass.sortMode = ENGINE_SORT_OPAQUE;
    _engine.hasPass = false;

    uint64_t now = stm_now();
    if (_engine.lastTime)
        _engine.dt = (float)stm_sec(stm_diff(now, _engine.lastTime));
    _engine.lastTime = now;

    if (_engine.instBufNeeded > _engine.instBufCapacity)
    {
        sg_destroy_buffer(_engine.instBuf);
        int cap = _engine.instBufCapacity ? _engine.instBufCapacity : 4096;
        while (cap < _engine.instBufNeeded)
            cap *= 2;
        sg_buffer_desc ib = {0};
        ib.usage.vertex_buffer = true;
        ib.usage.immutable = false;
        ib.usage.stream_update = true;
        ib.size = (size_t)cap * sizeof(_engine_Instance);
        ib.label = "engine-instance-buffer";
        _engine.instBuf = sg_make_buffer(&ib);
        _engine.instBufCapacity = cap;
        _engine_reserve(cap);
    }

    _engine.targetTop = 0;
    _engine.targetStack[0].target = NULL;
    _engine.targetStack[0].clear = clearColor;
    _engine.targetStack[0].doClear = true;
    _engine.targetStack[0].scissorTop = 0;
    _engine.scissorTop = 0;
    _engine_open_sokol_pass(&_engine.targetStack[0], true);
    _engine.inFrame = true;
}

// Hedef degistirmek aktif sokol pass'ini kapatir; bekleyen cizimler once flush edilir.
void engine_push_target(_engine_BitmapData *target, _engine_Color clearColor)
{
    if (!_engine.inFrame || !target)
        return;
    if (_engine.targetTop + 1 >= ENGINE_MAX_TARGET_STACK)
        return;

    _engine_close_sokol_pass();
    _engine_TargetEntry *entry = &_engine.targetStack[++_engine.targetTop];
    entry->target = target;
    entry->clear = clearColor;
    entry->doClear = true;
    entry->scissorTop = _engine.scissorTop;
    _engine.scissorTop = 0;
    _engine_open_sokol_pass(entry, true);
}

void engine_pop_target(void)
{
    if (!_engine.inFrame || _engine.targetTop <= 0)
        return;

    _engine_close_sokol_pass();
    _engine.targetTop--;
    _engine.scissorTop = _engine.targetStack[_engine.targetTop].scissorTop;
    _engine_open_sokol_pass(&_engine.targetStack[_engine.targetTop], false);
}

void engine_camera_2d(float width, float height);
void engine_draw_mesh(_engine_Mesh *mesh, _engine_Material *material, _engine_Mat4 *model,
                      _engine_Color tint, float u0, float v0, float u1, float v1, int layer);

void *engine_imgui_render_target_create(int width, int height)
{
    return engine_bitmapData_create_render_target(width, height);
}

void engine_imgui_push_render_target(void *target, unsigned char r, unsigned char g, unsigned char b, unsigned char a)
{
    _engine_BitmapData *bitmap = (_engine_BitmapData *)target;
    engine_push_target(bitmap, (_engine_Color){r, g, b, a});
    if (bitmap)
        engine_camera_2d((float)bitmap->width, (float)bitmap->height);
}

void engine_imgui_pop_render_target(void)
{
    engine_pop_target();
    engine_camera_2d((float)_ENGINE_HOST_W(), (float)_ENGINE_HOST_H());
}

void engine_imgui_draw_render_target(void *target, float x, float y, float width, float height)
{
    _engine_Material material = _engine.defaultMaterial;
    material.mainTexture = (_engine_BitmapData *)target;
    _engine_Mat4 model;
    engine_mat4_trs2d(&model, x + width * 0.5f, y + height * 0.5f, 0.0f, width, height, 0.0f);
    engine_draw_mesh(_engine.quadMesh, &material, &model, (_engine_Color){255, 255, 255, 255}, 0.0f, 1.0f, 1.0f, 0.0f, 900);
}

_engine_BitmapData *engine_bitmap_data_create_alpha(int width, int height, const unsigned char *alpha)
{
    if (!alpha || width <= 0 || height <= 0)
        return NULL;
    _engine_BitmapData *bitmap = (_engine_BitmapData *)_engine_bitmapData_empty(width, height, (_engine_Color){255, 255, 255, 0});
    if (!bitmap)
        return NULL;
    for (int i = 0; i < width * height; ++i)
    {
        bitmap->pixels[i].r = 255;
        bitmap->pixels[i].g = 255;
        bitmap->pixels[i].b = 255;
        bitmap->pixels[i].a = alpha[i];
    }
    bitmap->_pointFilter = 1;
    _engine_bitmapData_sync(bitmap);
    return bitmap;
}

static _engine_Scissor _engine_full_scissor(void)
{
    _engine_Scissor r = {0, 0, (int)_ENGINE_HOST_W(), (int)_ENGINE_HOST_H()};
    if (_engine.targetTop >= 0 && _engine.targetStack[_engine.targetTop].target)
    {
        r.width = _engine.targetStack[_engine.targetTop].target->width;
        r.height = _engine.targetStack[_engine.targetTop].target->height;
    }
    return r;
}

static _engine_Scissor _engine_intersect_scissor(_engine_Scissor a, _engine_Scissor b)
{
    int right = a.x + a.width;
    int bottom = a.y + a.height;
    int bRight = b.x + b.width;
    int bBottom = b.y + b.height;
    _engine_Scissor r = {a.x > b.x ? a.x : b.x, a.y > b.y ? a.y : b.y, 0, 0};
    int rRight = right < bRight ? right : bRight;
    int rBottom = bottom < bBottom ? bottom : bBottom;
    r.width = rRight > r.x ? rRight - r.x : 0;
    r.height = rBottom > r.y ? rBottom - r.y : 0;
    return r;
}

void engine_push_scissor(int x, int y, int width, int height)
{
    if (!_engine.inFrame || !_engine.sokolPassActive || _engine.scissorTop >= 64)
        return;
    engine_flush();
    _engine_Scissor requested = {x, y, width > 0 ? width : 0, height > 0 ? height : 0};
    _engine_Scissor current = _engine.scissorTop > 0 ? _engine.scissors[_engine.scissorTop - 1] : _engine_full_scissor();
    _engine.scissors[_engine.scissorTop++] = _engine_intersect_scissor(current, requested);
    _engine_Scissor clip = _engine.scissors[_engine.scissorTop - 1];
    sg_apply_scissor_rect(clip.x, clip.y, clip.width, clip.height, true);
}

void engine_pop_scissor(void)
{
    if (!_engine.inFrame || !_engine.sokolPassActive || _engine.scissorTop <= 0)
        return;
    engine_flush();
    _engine.scissorTop--;
    _engine_Scissor clip = _engine.scissorTop > 0 ? _engine.scissors[_engine.scissorTop - 1] : _engine_full_scissor();
    sg_apply_scissor_rect(clip.x, clip.y, clip.width, clip.height, true);
}

_engine_BitmapData *engine_current_target(void)
{
    if (_engine.targetTop < 0)
        return NULL;
    return _engine.targetStack[_engine.targetTop].target;
}

void engine_end_frame(void)
{
    if (!_engine.inFrame)
        return;
    while (_engine.targetTop > 0)
        engine_pop_target();
    _engine_close_sokol_pass();
    sg_commit();
    _engine.targetTop = -1;
    _engine.inFrame = false;
}

// ---------------------------------------------------------------------------
// uygulama dongusu
// ---------------------------------------------------------------------------

typedef struct engine_app_desc
{
    int width;
    int height;
    const char *title;
    int sampleCount;
    int highDpi;
    int maxInstances;
    void (*init)(void);
    void (*frame)(float dt);
    void (*cleanup)(void);
    void (*event)(const void *nativeEvent);
} engine_app_desc;

static engine_app_desc _engine_app;
static imgui_event _engine_imgui_pending_event;
static bool _engine_imgui_has_pending_event;
static float _engine_imgui_mouse_x;
static float _engine_imgui_mouse_y;
static imgui_font *_engine_demo_font;
static VmString *_engine_demo_title;
static engine_window *_engine_demo_native_console;

static VmString *_engine_demo_string(const char *text)
{
    int length = (int)strlen(text);
    VmString *result = vmstring_alloc(length);
    if (result)
        for (int i = 0; i < length; ++i)
            ((unsigned short *)result->data)[i] = (unsigned char)text[i];
    return result;
}

// ---------------------------------------------------------------------------
// Unity benzeri editor layout demo: 4 dock alani + surukle/detach
// ---------------------------------------------------------------------------

#if defined(ENGINE_USE_GLFW)
bool engine_host_open_panel_window(uint32_t panelId, const char *title, int x, int y, int width, int height);
void engine_host_main_window_pos(int *x, int *y);
#endif

static _engine_BitmapData *_engine_demo_sceneRT;

typedef struct
{
    uint32_t id;
    const char *name;
    VmString *title;
    imgui_panel_draw_fn draw;
    int area; // 0 sol, 1 orta, 2 sag, 3 alt, -1 detached
} _engine_editor_panel;

#define _ENGINE_EDITOR_PANEL_COUNT 6
#define _ENGINE_EDITOR_AREA_COUNT 4

static _engine_editor_panel _engine_editor_panels[_ENGINE_EDITOR_PANEL_COUNT];
static uint32_t _engine_editor_active[_ENGINE_EDITOR_AREA_COUNT];
static uint32_t _engine_editor_drag_panel;
static float _engine_editor_drag_start_x;
static float _engine_editor_drag_start_y;
static imgui_style _engine_editor_tab_idle;
static imgui_style _engine_editor_tab_active;

#define _ENGINE_EDITOR_TAB_H 26.0f
#define _ENGINE_EDITOR_TAB_W 104.0f

static imgui_rect _engine_editor_area_rect(int area, float width, float height)
{
    const float top = 30.0f, bottomH = 190.0f, leftW = 250.0f, rightW = 330.0f;
    float midH = height - top - bottomH;
    switch (area)
    {
    case 0:
        return (imgui_rect){0.0f, top, leftW, midH};
    case 1:
        return (imgui_rect){leftW, top, width - leftW - rightW, midH};
    case 2:
        return (imgui_rect){width - rightW, top, rightW, midH};
    default:
        return (imgui_rect){0.0f, top + midH, width, bottomH};
    }
}

static _engine_editor_panel *_engine_editor_find(uint32_t panelId)
{
    for (int i = 0; i < _ENGINE_EDITOR_PANEL_COUNT; ++i)
        if (_engine_editor_panels[i].id == panelId)
            return &_engine_editor_panels[i];
    return NULL;
}

// Detached pencere kapaninca panel alt dock alanina geri doner (redock ispati).
void engine_editor_panel_closed(uint32_t panelId)
{
    _engine_editor_panel *panel = _engine_editor_find(panelId);
    if (!panel)
        return;
    panel->area = 3;
    _engine_editor_active[3] = panelId;
}

static void _engine_draw_hierarchy_panel(imgui_rect r, void *userData)
{
    (void)userData;
    static float scroll = 0.0f;
    const int itemCount = 24;
    float contentHeight = 12.0f + (float)itemCount * 30.0f;
    imgui_box(9031, (imgui_rect){r.x, r.y, r.width, r.height}, &imgui_skin_current()->window);
    imgui_rect view = {r.x, r.y, r.width - imgui_scrollbar_size() - 4.0f, r.height};
    if (imgui_begin_scroll_view(9036, view, contentHeight, &scroll))
    {
        for (int i = 0; i < itemCount; ++i)
            imgui_button(9100 + (uint32_t)i,
                         (imgui_rect){r.x + 10.0f, r.y + 12.0f + (float)i * 30.0f - scroll, view.width - 20.0f, 24.0f},
                         &imgui_skin_current()->button);
        imgui_end_scroll_view();
    }
    imgui_vertical_scrollbar(9037, (imgui_rect){r.x + r.width - imgui_scrollbar_size() - 2.0f, r.y + 2.0f, imgui_scrollbar_size(), r.height - 4.0f},
                             &scroll, r.height, contentHeight, NULL, NULL);
}

static void _engine_draw_scene_panel(imgui_rect r, void *userData)
{
    (void)userData;
    imgui_box(9041, (imgui_rect){r.x, r.y, r.width, r.height}, &imgui_skin_current()->window);
    if (_engine_demo_sceneRT)
        engine_imgui_draw_render_target(_engine_demo_sceneRT, r.x + 4.0f, r.y + 4.0f,
                                        r.width - 8.0f, r.height - 8.0f);
}

static void _engine_draw_inspector_panel(imgui_rect r, void *userData)
{
    (void)userData;
    static bool value = true;
    static float slider = 0.35f;
    static unsigned short nameBuf[64] = {'P', 'l', 'a', 'y', 'e', 'r'};
    static int nameLen = 6;
    imgui_box(9011, (imgui_rect){r.x, r.y, r.width, r.height}, &imgui_skin_current()->window);
    imgui_button(9012, (imgui_rect){r.x + 20.0f, r.y + 16.0f, 180.0f, 30.0f}, &imgui_skin_current()->button);
    imgui_toggle(9013, (imgui_rect){r.x + 20.0f, r.y + 58.0f, 28.0f, 24.0f}, &value, &imgui_skin_current()->toggle);
    imgui_horizontal_slider(9014, (imgui_rect){r.x + 64.0f, r.y + 61.0f, 150.0f, 18.0f}, &slider,
                            0.0f, 1.0f, &imgui_skin_current()->horizontalSlider,
                            &imgui_skin_current()->horizontalSliderThumb);
    imgui_text_field(9015, (imgui_rect){r.x + 20.0f, r.y + 94.0f, 194.0f, 26.0f}, nameBuf, 64, &nameLen, NULL);
}

// Icerik-olculu widget demosu: font boyutu slider'i buyudukce GUILayout tarzi
// butonlar CalcSize uzerinden otomatik buyur (Unity GUILayout davranisi).
static void _engine_draw_console_panel(imgui_rect r, void *userData)
{
    (void)userData;
    static VmString *message, *addTrack, *snapLabel;
    static float uiFontSize = 18.0f;
    static bool snapValue = true;
    if (!message)
    {
        message = _engine_demo_string("Console ready");
        addTrack = _engine_demo_string("Add Track");
        snapLabel = _engine_demo_string("Snap");
    }
    imgui_box(9021, (imgui_rect){r.x, r.y, r.width, r.height}, &imgui_skin_current()->window);
    imgui_horizontal_slider(9026, (imgui_rect){r.x + 12.0f, r.y + 12.0f, 150.0f, 16.0f},
                            &uiFontSize, 12.0f, 34.0f, NULL, NULL);
    const imgui_font *font = imgui_font_current();
    imgui_set_font(font, uiFontSize);
    imgui_set_layout_rect((imgui_rect){r.x + 12.0f, r.y + 40.0f, r.width - 24.0f, r.height - 48.0f});
    imgui_begin_horizontal();
    imgui_layout_button(9027, addTrack, NULL, 0);
    imgui_layout_toggle(9028, snapLabel, &snapValue, NULL, 0);
    imgui_layout_label(message, NULL, 0);
    imgui_end_horizontal();
    imgui_set_font(font, 18.0f);
}

static void _engine_draw_project_panel(imgui_rect r, void *userData)
{
    (void)userData;
    static VmString *message = NULL;
    if (!message)
        message = _engine_demo_string("Assets / Scenes / Scripts");
    imgui_box(9051, (imgui_rect){r.x, r.y, r.width, r.height}, &imgui_skin_current()->window);
    imgui_label(message, (imgui_rect){r.x + 16.0f, r.y + 14.0f, r.width - 32.0f, 24.0f}, &imgui_skin_current()->label);
}

// ---------------------------------------------------------------------------
// Timeline demo: Unity MovieClipTimelineWindow desenlerinin C imgui karsiligi.
// Ruler+playhead drag, elmas keyframe, snap'li grup drag, double-click ekleme,
// shift-click coklu secim, rubber-band, header splitter, scrollbar, Delete.
// ---------------------------------------------------------------------------

#define _TL_TRACKS 5
#define _TL_MAX_KEYS 32
#define _TL_ROW_H 26.0f
#define _TL_RULER_H 18.0f
#define _TL_TOOLBAR_H 26.0f
#define _TL_KEY_HALF 5.0f
#define _TL_SNAP 0.25f

typedef struct
{
    float times[_TL_MAX_KEYS];
    bool selected[_TL_MAX_KEYS];
    int count;
} _tl_track_t;

static _tl_track_t _tl_tracks[_TL_TRACKS];
static bool _tl_ready;
static float _tl_playTime;
static float _tl_pps = 110.0f;
static float _tl_hScroll;
static bool _tl_snap = true;
static float _tl_headerW = 130.0f;
static bool _tl_dragPlayhead, _tl_dragSplitter, _tl_dragKeys, _tl_rubber;
static int _tl_dragTrack, _tl_dragKey;
static float _tl_dragGrabTime;                                       // yakalanan key'in drag baslangic zamani
static float _tl_dragOrig[_TL_TRACKS][_TL_MAX_KEYS];                 // grup drag icin orijinal zamanlar
static float _tl_rubberX0, _tl_rubberY0, _tl_rubberX1, _tl_rubberY1; // lane-lokal
static VmString *_tl_trackNames[_TL_TRACKS];
static VmString *_tl_snapLabel, *_tl_titleLabel;

static float _tl_snap_time(float t) { return _tl_snap ? floorf(t / _TL_SNAP + 0.5f) * _TL_SNAP : t; }

static float _tl_duration(void)
{
    float max = 4.0f;
    for (int i = 0; i < _TL_TRACKS; ++i)
        for (int k = 0; k < _tl_tracks[i].count; ++k)
            if (_tl_tracks[i].times[k] + 0.5f > max)
                max = _tl_tracks[i].times[k] + 0.5f;
    return max;
}

static void _tl_init(void)
{
    static const char *names[_TL_TRACKS] = {"POT Position", "POT Scale", "LIGHT1 Rot", "LIGHT1 Alpha", "JACKPOT Alpha"};
    for (int i = 0; i < _TL_TRACKS; ++i)
    {
        _tl_trackNames[i] = _engine_demo_string(names[i]);
        _tl_tracks[i].count = 3 + i % 3;
        for (int k = 0; k < _tl_tracks[i].count; ++k)
            _tl_tracks[i].times[k] = (float)k * (0.75f + 0.25f * (float)i);
    }
    _tl_snapLabel = _engine_demo_string("Snap");
    _tl_titleLabel = _engine_demo_string("Timeline");
    _tl_ready = true;
}

static void _tl_clear_selection(void)
{
    for (int i = 0; i < _TL_TRACKS; ++i)
        for (int k = 0; k < _TL_MAX_KEYS; ++k)
            _tl_tracks[i].selected[k] = false;
}

static int _tl_hit_key(int track, float localX, float localY)
{
    float cy = (float)track * _TL_ROW_H + _TL_ROW_H * 0.5f;
    for (int k = 0; k < _tl_tracks[track].count; ++k)
    {
        float cx = _tl_tracks[track].times[k] * _tl_pps - _tl_hScroll + 10.0f;
        if (localX >= cx - _TL_KEY_HALF - 2 && localX <= cx + _TL_KEY_HALF + 2 &&
            localY >= cy - _TL_KEY_HALF - 2 && localY <= cy + _TL_KEY_HALF + 2)
            return k;
    }
    return -1;
}

static void _engine_draw_timeline_panel(imgui_rect r, void *userData)
{
    (void)userData;
    if (!_tl_ready)
        _tl_init();
    const imgui_event *event = imgui_current_event();
    float duration = _tl_duration();

    imgui_box(9200, (imgui_rect){r.x, r.y, r.width, r.height}, &imgui_skin_current()->window);

    // ---- toolbar ----
    imgui_begin_group((imgui_rect){r.x, r.y, r.width, _TL_TOOLBAR_H}, false);
    imgui_toggle_text(9201, _tl_snapLabel, (imgui_rect){4, 2, 60, 22}, &_tl_snap, NULL);
    imgui_horizontal_slider(9202, (imgui_rect){72, 5, 110, 16}, &_tl_pps, 30.0f, 300.0f, NULL, NULL);
    imgui_label(_tl_titleLabel, (imgui_rect){192, 2, 120, 22}, NULL);
    imgui_end_group();

    float laneX = _tl_headerW + 5.0f;
    float laneW = r.width - laneX - 4.0f;
    float lanesTop = _TL_TOOLBAR_H + _TL_RULER_H;
    float lanesH = (float)_TL_TRACKS * _TL_ROW_H;
    float contentW = duration * _tl_pps + 20.0f;

    // ---- header kolonu (track adlari) ----
    imgui_begin_group((imgui_rect){r.x, r.y + lanesTop, _tl_headerW, lanesH}, true);
    for (int i = 0; i < _TL_TRACKS; ++i)
    {
        imgui_draw_rect((imgui_rect){0, (float)i * _TL_ROW_H, _tl_headerW, _TL_ROW_H},
                        (imgui_color){34, 37, 44, 255}, 1180);
        imgui_draw_rect((imgui_rect){0, (float)i * _TL_ROW_H + _TL_ROW_H - 1, _tl_headerW, 1},
                        (imgui_color){0, 0, 0, 100}, 1181);
        imgui_label(_tl_trackNames[i], (imgui_rect){6, (float)i * _TL_ROW_H + 3, _tl_headerW - 12, 20}, NULL);
    }
    imgui_end_group();

    // ---- header splitter ----
    {
        imgui_rect splitter = {r.x + _tl_headerW, r.y + lanesTop, 5.0f, lanesH};
        imgui_draw_rect((imgui_rect){splitter.x, splitter.y, splitter.width, splitter.height},
                        (imgui_color){18, 20, 24, 255}, 1181);
        if (event && event->type == IMGUI_EVENT_MOUSE_DOWN &&
            imgui_rect_contains(splitter, event->mouseX, event->mouseY))
            _tl_dragSplitter = true;
        if (_tl_dragSplitter && event && event->type == IMGUI_EVENT_MOUSE_DRAG)
        {
            _tl_headerW = event->mouseX - r.x - 2.5f;
            if (_tl_headerW < 80.0f)
                _tl_headerW = 80.0f;
            if (_tl_headerW > r.width - 160.0f)
                _tl_headerW = r.width - 160.0f;
        }
        if (event && event->type == IMGUI_EVENT_MOUSE_UP)
            _tl_dragSplitter = false;
    }

    // ---- ruler: saniye cizgileri + playhead surukleme ----
    imgui_begin_group((imgui_rect){r.x + laneX, r.y + _TL_TOOLBAR_H, laneW, _TL_RULER_H}, true);
    {
        imgui_draw_rect((imgui_rect){0, 0, laneW, _TL_RULER_H}, (imgui_color){28, 30, 35, 255}, 1180);
        for (int s = 0; s <= (int)duration + 1; ++s)
        {
            float x = (float)s * _tl_pps - _tl_hScroll + 10.0f;
            if (x < -2 || x > laneW + 2)
                continue;
            imgui_draw_rect((imgui_rect){x, 4, 1, _TL_RULER_H - 4}, (imgui_color){255, 255, 255, 60}, 1182);
        }
        float px = _tl_playTime * _tl_pps - _tl_hScroll + 10.0f;
        imgui_draw_rect((imgui_rect){px, 0, 1.5f, _TL_RULER_H}, (imgui_color){230, 64, 64, 255}, 1185);
        if (event && event->type == IMGUI_EVENT_MOUSE_DOWN &&
            imgui_local_mouse_x() >= 0 && imgui_local_mouse_x() <= laneW &&
            imgui_local_mouse_y() >= 0 && imgui_local_mouse_y() <= _TL_RULER_H)
            _tl_dragPlayhead = true;
        if (_tl_dragPlayhead && event &&
            (event->type == IMGUI_EVENT_MOUSE_DOWN || event->type == IMGUI_EVENT_MOUSE_DRAG))
        {
            _tl_playTime = (imgui_local_mouse_x() - 10.0f + _tl_hScroll) / _tl_pps;
            if (_tl_playTime < 0)
                _tl_playTime = 0;
        }
        if (event && event->type == IMGUI_EVENT_MOUSE_UP)
            _tl_dragPlayhead = false;
    }
    imgui_end_group();

    // ---- lanes ----
    imgui_begin_group((imgui_rect){r.x + laneX, r.y + lanesTop, laneW, lanesH}, true);
    {
        float mx = imgui_local_mouse_x(), my = imgui_local_mouse_y();
        bool inside = mx >= 0 && mx <= laneW && my >= 0 && my <= lanesH;

        for (int i = 0; i < _TL_TRACKS; ++i)
        {
            imgui_color bg = (i & 1) ? (imgui_color){52, 56, 63, 255} : (imgui_color){46, 50, 57, 255};
            imgui_draw_rect((imgui_rect){0, (float)i * _TL_ROW_H, laneW, _TL_ROW_H}, bg, 1180);
            imgui_draw_rect((imgui_rect){0, (float)i * _TL_ROW_H + _TL_ROW_H - 1, laneW, 1},
                            (imgui_color){0, 0, 0, 90}, 1181);
        }
        for (int s = 0; s <= (int)duration + 1; ++s)
        {
            float x = (float)s * _tl_pps - _tl_hScroll + 10.0f;
            if (x < 0 || x > laneW)
                continue;
            imgui_draw_rect((imgui_rect){x, 0, 1, lanesH}, (imgui_color){255, 255, 255, 26}, 1182);
        }

        // elmas keyframe'ler (45 derece dondurulmus kare)
        for (int i = 0; i < _TL_TRACKS; ++i)
        {
            float cy = (float)i * _TL_ROW_H + _TL_ROW_H * 0.5f;
            for (int k = 0; k < _tl_tracks[i].count; ++k)
            {
                float cx = _tl_tracks[i].times[k] * _tl_pps - _tl_hScroll + 10.0f;
                if (cx < -_TL_KEY_HALF || cx > laneW + _TL_KEY_HALF)
                    continue;
                imgui_color col = _tl_tracks[i].selected[k] ? (imgui_color){255, 255, 255, 255}
                                                            : (imgui_color){242, 191, 51, 255};
                imgui_draw_rect_rotated((imgui_rect){cx - _TL_KEY_HALF, cy - _TL_KEY_HALF,
                                                     _TL_KEY_HALF * 2, _TL_KEY_HALF * 2},
                                        0.785398f, col, 1184);
            }
        }

        // playhead
        float px = _tl_playTime * _tl_pps - _tl_hScroll + 10.0f;
        if (px >= 0 && px <= laneW)
            imgui_draw_rect((imgui_rect){px, 0, 1.5f, lanesH}, (imgui_color){230, 64, 64, 255}, 1185);

        // etkilesim
        if (event && event->type == IMGUI_EVENT_MOUSE_DOWN && inside)
        {
            int row = (int)(my / _TL_ROW_H);
            int hit = (row >= 0 && row < _TL_TRACKS) ? _tl_hit_key(row, mx, my) : -1;
            if (hit >= 0)
            {
                if (event->shift || event->control)
                    _tl_tracks[row].selected[hit] = !_tl_tracks[row].selected[hit]; // additive toggle
                else
                {
                    if (!_tl_tracks[row].selected[hit])
                    {
                        _tl_clear_selection();
                        _tl_tracks[row].selected[hit] = true;
                    }
                    _tl_dragKeys = true;
                    _tl_dragTrack = row;
                    _tl_dragKey = hit;
                    _tl_dragGrabTime = _tl_tracks[row].times[hit];
                    for (int i = 0; i < _TL_TRACKS; ++i)
                        for (int k = 0; k < _tl_tracks[i].count; ++k)
                            _tl_dragOrig[i][k] = _tl_tracks[i].times[k];
                }
            }
            else if (event->clickCount >= 2 && row >= 0 && row < _TL_TRACKS &&
                     _tl_tracks[row].count < _TL_MAX_KEYS)
            {
                // double-click: yeni keyframe
                float t = _tl_snap_time((mx - 10.0f + _tl_hScroll) / _tl_pps);
                if (t < 0)
                    t = 0;
                _tl_tracks[row].times[_tl_tracks[row].count] = t;
                _tl_clear_selection();
                _tl_tracks[row].selected[_tl_tracks[row].count] = true;
                _tl_tracks[row].count++;
                printf("timeline: key added track=%d t=%.2f\n", row, t);
            }
            else
            {
                // bos alan: rubber-band baslat
                if (!(event->shift || event->control))
                    _tl_clear_selection();
                _tl_rubber = true;
                _tl_rubberX0 = _tl_rubberX1 = mx;
                _tl_rubberY0 = _tl_rubberY1 = my;
            }
        }
        if (event && event->type == IMGUI_EVENT_MOUSE_DRAG)
        {
            if (_tl_dragKeys)
            {
                // grup drag: yakalanan key'in deltasi tum secime uygulanir
                float t = _tl_snap_time((mx - 10.0f + _tl_hScroll) / _tl_pps);
                float delta = t - _tl_dragGrabTime;
                for (int i = 0; i < _TL_TRACKS; ++i)
                    for (int k = 0; k < _tl_tracks[i].count; ++k)
                        if (_tl_tracks[i].selected[k])
                        {
                            float nt = _tl_dragOrig[i][k] + delta;
                            _tl_tracks[i].times[k] = nt < 0 ? 0 : nt;
                        }
            }
            else if (_tl_rubber)
            {
                _tl_rubberX1 = mx;
                _tl_rubberY1 = my;
            }
        }
        if (event && event->type == IMGUI_EVENT_MOUSE_UP)
        {
            if (_tl_rubber)
            {
                float x0 = _tl_rubberX0 < _tl_rubberX1 ? _tl_rubberX0 : _tl_rubberX1;
                float x1 = _tl_rubberX0 < _tl_rubberX1 ? _tl_rubberX1 : _tl_rubberX0;
                float y0 = _tl_rubberY0 < _tl_rubberY1 ? _tl_rubberY0 : _tl_rubberY1;
                float y1 = _tl_rubberY0 < _tl_rubberY1 ? _tl_rubberY1 : _tl_rubberY0;
                int selCount = 0;
                for (int i = 0; i < _TL_TRACKS; ++i)
                {
                    float cy = (float)i * _TL_ROW_H + _TL_ROW_H * 0.5f;
                    if (cy < y0 || cy > y1)
                        continue;
                    for (int k = 0; k < _tl_tracks[i].count; ++k)
                    {
                        float cx = _tl_tracks[i].times[k] * _tl_pps - _tl_hScroll + 10.0f;
                        if (cx >= x0 && cx <= x1)
                        {
                            _tl_tracks[i].selected[k] = true;
                            ++selCount;
                        }
                    }
                }
                if (selCount > 0)
                    printf("timeline: rubber-band selected %d keys\n", selCount);
                _tl_rubber = false;
            }
            _tl_dragKeys = false;
        }
        // rubber-band gorseli
        if (_tl_rubber)
        {
            float x0 = _tl_rubberX0 < _tl_rubberX1 ? _tl_rubberX0 : _tl_rubberX1;
            float x1 = _tl_rubberX0 < _tl_rubberX1 ? _tl_rubberX1 : _tl_rubberX0;
            float y0 = _tl_rubberY0 < _tl_rubberY1 ? _tl_rubberY0 : _tl_rubberY1;
            float y1 = _tl_rubberY0 < _tl_rubberY1 ? _tl_rubberY1 : _tl_rubberY0;
            imgui_draw_rect((imgui_rect){x0, y0, x1 - x0, y1 - y0}, (imgui_color){102, 153, 230, 38}, 1186);
            imgui_draw_rect((imgui_rect){x0, y0, x1 - x0, 1}, (imgui_color){255, 255, 255, 255}, 1187);
            imgui_draw_rect((imgui_rect){x0, y1 - 1, x1 - x0, 1}, (imgui_color){255, 255, 255, 255}, 1187);
            imgui_draw_rect((imgui_rect){x0, y0, 1, y1 - y0}, (imgui_color){255, 255, 255, 255}, 1187);
            imgui_draw_rect((imgui_rect){x1 - 1, y0, 1, y1 - y0}, (imgui_color){255, 255, 255, 255}, 1187);
        }
    }
    imgui_end_group();

    // Delete: secili keyframe'leri sil (yuksek index'ten geriye)
    if (event && event->type == IMGUI_EVENT_KEY_DOWN &&
        (event->key == 261 || event->key == 259) && imgui_keyboard_control() == 0)
    {
        int deleted = 0;
        for (int i = 0; i < _TL_TRACKS; ++i)
            for (int k = _tl_tracks[i].count - 1; k >= 0; --k)
                if (_tl_tracks[i].selected[k])
                {
                    for (int m = k; m < _tl_tracks[i].count - 1; ++m)
                    {
                        _tl_tracks[i].times[m] = _tl_tracks[i].times[m + 1];
                        _tl_tracks[i].selected[m] = _tl_tracks[i].selected[m + 1];
                    }
                    _tl_tracks[i].count--;
                    ++deleted;
                }
        if (deleted > 0)
            printf("timeline: deleted %d keys\n", deleted);
    }

    // ---- yatay scrollbar ----
    imgui_horizontal_scrollbar(9203,
                               (imgui_rect){r.x + laneX, r.y + lanesTop + lanesH + 2.0f, laneW, 12.0f},
                               &_tl_hScroll, laneW, contentW, NULL, NULL);
}

static void _engine_editor_init_panels(void)
{
    _engine_editor_panel init[_ENGINE_EDITOR_PANEL_COUNT] = {
        {9030, "Hierarchy", NULL, _engine_draw_hierarchy_panel, 0},
        {9040, "Scene", NULL, _engine_draw_scene_panel, 1},
        {9010, "Inspector", NULL, _engine_draw_inspector_panel, 2},
        {9020, "Console", NULL, _engine_draw_console_panel, 3},
        {9050, "Project", NULL, _engine_draw_project_panel, 3},
        {9210, "Timeline", NULL, _engine_draw_timeline_panel, 3},
    };
    for (int i = 0; i < _ENGINE_EDITOR_PANEL_COUNT; ++i)
    {
        init[i].title = _engine_demo_string(init[i].name);
        _engine_editor_panels[i] = init[i];
        imgui_register_panel(init[i].id, init[i].title);
        imgui_register_panel_content(init[i].id, init[i].draw, NULL);
    }
    _engine_editor_active[0] = 9030;
    _engine_editor_active[1] = 9040;
    _engine_editor_active[2] = 9010;
    _engine_editor_active[3] = 9210;

    // Tab stilleri: pasif koyu, aktif panel zeminiyle birlesir.
    _engine_editor_tab_idle = imgui_skin_current()->button;
    _engine_editor_tab_idle.normal.backgroundColor = (imgui_color){30, 33, 39, 255};
    _engine_editor_tab_idle.normal.textColor = (imgui_color){150, 156, 166, 255};
    _engine_editor_tab_idle.hover.backgroundColor = (imgui_color){44, 48, 57, 255};
    _engine_editor_tab_active = _engine_editor_tab_idle;
    _engine_editor_tab_active.normal.backgroundColor = (imgui_color){40, 44, 52, 255};
    _engine_editor_tab_active.normal.textColor = (imgui_color){236, 240, 246, 255};
    _engine_editor_tab_active.hover = _engine_editor_tab_active.normal;
}

static void _engine_bootstrap_common(void)
{
    stm_setup();
    engine_renderer_init(_engine_app.maxInstances);
    _engine_demo_title = _engine_demo_string("DigiPlay IMGUI / VmString text");
    _engine_demo_font = imgui_font_load_file(_engine_demo_string("test-project/font.ttf"), 18.0f);
    if (_engine_app.init)
        _engine_app.init();
}

#if !defined(ENGINE_USE_GLFW)
static void _engine_init_cb(void)
{
    sg_desc d = {0};
    d.environment = sglue_environment();
    d.logger.func = slog_func;
    sg_setup(&d);
    _engine_bootstrap_common();
}
#endif

static void _engine_frame_cb(void)
{
    engine_window_poll_events();
    if (_engine_app.frame)
        _engine_app.frame(_engine.dt);
}

static void _engine_cleanup_cb(void)
{
    if (_engine_app.cleanup)
        _engine_app.cleanup();
    engine_window_destroy(_engine_demo_native_console);
    _engine_demo_native_console = NULL;
    engine_renderer_shutdown();
    sg_shutdown();
}

#if defined(ENGINE_USE_GLFW)
void engine_host_bootstrap(void)
{
    _engine_bootstrap_common();
}

void engine_host_frame_tick(void)
{
    _engine_frame_cb();
}

void engine_host_shutdown_tick(void)
{
    _engine_cleanup_cb();
}

void engine_host_update_mouse(float x, float y)
{
    _engine_imgui_mouse_x = x;
    _engine_imgui_mouse_y = y;
}

void engine_host_push_imgui_event(const imgui_event *event)
{
    if (!event)
        return;
    // DRAG, ayni frame'de bekleyen down/up gibi kritik bir event'i ezmesin.
    if (event->type == IMGUI_EVENT_MOUSE_DRAG && _engine_imgui_has_pending_event &&
        _engine_imgui_pending_event.type != IMGUI_EVENT_MOUSE_DRAG)
        return;
    _engine_imgui_pending_event = *event;
    _engine_imgui_has_pending_event = true;
    _engine_imgui_mouse_x = event->mouseX;
    _engine_imgui_mouse_y = event->mouseY;
}

static void _engine_draw_console_panel(imgui_rect contentRect, void *userData);

// Detached panel pencereleri: host FBO swapchain'i aktifken cagirilir.
// event pencere-lokal koordinatlidir; once event pass'i sonra repaint kosulur.
void engine_host_render_panel(uint32_t panelId, int width, int height,
                              const imgui_event *event, float mouseX, float mouseY)
{
    float savedDt = _engine.dt;
    uint64_t savedTime = _engine.lastTime;
    imgui_rect contentRect = {0, 0, (float)width, (float)height};
    engine_begin_frame((_engine_Color){24, 27, 34, 255});
    engine_pass_desc pass = {0};
    pass.name = "panel-window";
    pass.sortMode = ENGINE_SORT_UI;
    engine_begin_pass(&pass);
    engine_camera_2d((float)width, (float)height);
    imgui_begin_frame(savedDt);
    imgui_set_font(_engine_demo_font, 18.0f);
    imgui_event layoutEvent = {0};
    layoutEvent.type = IMGUI_EVENT_LAYOUT;
    imgui_begin_event(&layoutEvent);
    imgui_panel_draw_direct(panelId, contentRect);
    imgui_end_event();
    if (event)
    {
        imgui_begin_event(event);
        imgui_panel_draw_direct(panelId, contentRect);
        imgui_end_event();
    }
    imgui_event repaint = {0};
    repaint.type = IMGUI_EVENT_REPAINT;
    repaint.mouseX = mouseX;
    repaint.mouseY = mouseY;
    imgui_begin_event(&repaint);
    imgui_panel_draw_direct(panelId, contentRect);
    imgui_end_event();
    imgui_end_frame();
    engine_end_pass();
    engine_end_frame();
    _engine.dt = savedDt;
    _engine.lastTime = savedTime;
}
#endif

#if !defined(ENGINE_USE_GLFW)
static void _engine_event_cb(const sapp_event *ev)
{
    if (_engine_app.event)
        _engine_app.event(ev);
    if (!ev)
        return;

    _engine_imgui_mouse_x = ev->mouse_x;
    _engine_imgui_mouse_y = ev->mouse_y;
    memset(&_engine_imgui_pending_event, 0, sizeof(_engine_imgui_pending_event));
    _engine_imgui_pending_event.mouseX = ev->mouse_x;
    _engine_imgui_pending_event.mouseY = ev->mouse_y;
    _engine_imgui_pending_event.scrollX = ev->scroll_x;
    _engine_imgui_pending_event.scrollY = ev->scroll_y;
    _engine_imgui_pending_event.button = (int)ev->mouse_button;
    _engine_imgui_pending_event.key = (int)ev->key_code;
    _engine_imgui_pending_event.character = ev->char_code;
    switch (ev->type)
    {
    case SAPP_EVENTTYPE_MOUSE_DOWN:
        _engine_imgui_pending_event.type = IMGUI_EVENT_MOUSE_DOWN;
        _engine_imgui_has_pending_event = true;
        break;
    case SAPP_EVENTTYPE_MOUSE_UP:
        _engine_imgui_pending_event.type = IMGUI_EVENT_MOUSE_UP;
        _engine_imgui_has_pending_event = true;
        break;
    case SAPP_EVENTTYPE_MOUSE_SCROLL:
        _engine_imgui_pending_event.type = IMGUI_EVENT_SCROLL_WHEEL;
        _engine_imgui_has_pending_event = true;
        break;
    case SAPP_EVENTTYPE_KEY_DOWN:
        _engine_imgui_pending_event.type = IMGUI_EVENT_KEY_DOWN;
        _engine_imgui_has_pending_event = true;
        break;
    case SAPP_EVENTTYPE_KEY_UP:
        _engine_imgui_pending_event.type = IMGUI_EVENT_KEY_UP;
        _engine_imgui_has_pending_event = true;
        break;
    default:
        break;
    }
}
#endif

static void _engine_demo_imgui(void)
{
    static bool editorReady = false;
    if (!editorReady)
    {
        _engine_editor_init_panels();
        editorReady = true;
    }
    imgui_set_font(_engine_demo_font, 16.0f);
    float width = engine_width();
    float height = engine_height();
    imgui_set_layout_rect((imgui_rect){0.0f, 0.0f, width, height});
    const imgui_event *event = imgui_current_event();

    // ust toolbar
    engine_draw_ui_rect(0.0f, 0.0f, width, 30.0f, 25, 27, 32, 255, 990);
    if (_engine_demo_font && _engine_demo_title)
        imgui_label(_engine_demo_title, (imgui_rect){12.0f, 4.0f, 400.0f, 22.0f}, &imgui_skin_current()->label);

    // dock alanlari: tab seridi + aktif panel icerigi
    for (int area = 0; area < _ENGINE_EDITOR_AREA_COUNT; ++area)
    {
        imgui_rect areaRect = _engine_editor_area_rect(area, width, height);
        engine_draw_ui_rect(areaRect.x, areaRect.y, areaRect.width, _ENGINE_EDITOR_TAB_H,
                            25, 27, 32, 255, 995);
        float tabX = areaRect.x + 4.0f;
        for (int i = 0; i < _ENGINE_EDITOR_PANEL_COUNT; ++i)
        {
            _engine_editor_panel *panel = &_engine_editor_panels[i];
            if (panel->area != area)
                continue;
            imgui_rect tabRect = {tabX, areaRect.y + 2.0f, _ENGINE_EDITOR_TAB_W, _ENGINE_EDITOR_TAB_H - 2.0f};
            bool activeTab = _engine_editor_active[area] == panel->id;
            imgui_button_text(panel->id + 500, panel->title, tabRect,
                              activeTab ? &_engine_editor_tab_active : &_engine_editor_tab_idle);
            if (event && event->type == IMGUI_EVENT_MOUSE_DOWN &&
                imgui_rect_contains(tabRect, event->mouseX, event->mouseY))
            {
                _engine_editor_active[area] = panel->id;
                _engine_editor_drag_panel = panel->id;
                _engine_editor_drag_start_x = event->mouseX;
                _engine_editor_drag_start_y = event->mouseY;
            }
            tabX += _ENGINE_EDITOR_TAB_W + 3.0f;
        }
        imgui_rect content = {areaRect.x, areaRect.y + _ENGINE_EDITOR_TAB_H,
                              areaRect.width, areaRect.height - _ENGINE_EDITOR_TAB_H};
        _engine_editor_panel *active = _engine_editor_find(_engine_editor_active[area]);
        if (active && active->area == area && active->draw)
            active->draw(content, NULL);
        else
            imgui_box(9060 + (uint32_t)area, content, &imgui_skin_current()->box);
    }

    // alan ayiricilari: 1px koyu cizgiler
    {
        imgui_rect leftArea = _engine_editor_area_rect(0, width, height);
        imgui_rect rightArea = _engine_editor_area_rect(2, width, height);
        imgui_rect bottomArea = _engine_editor_area_rect(3, width, height);
        engine_draw_ui_rect(leftArea.x + leftArea.width - 1.0f, leftArea.y, 2.0f, leftArea.height, 18, 20, 24, 255, 1300);
        engine_draw_ui_rect(rightArea.x - 1.0f, rightArea.y, 2.0f, rightArea.height, 18, 20, 24, 255, 1300);
        engine_draw_ui_rect(bottomArea.x, bottomArea.y - 1.0f, bottomArea.width, 2.0f, 18, 20, 24, 255, 1300);
        engine_draw_ui_rect(0.0f, 29.0f, width, 1.0f, 18, 20, 24, 255, 1300);
    }

    // surukleme cozumu: dock alanina birak -> re-dock; disari birak -> native pencere
    if (_engine_editor_drag_panel && event && event->type == IMGUI_EVENT_MOUSE_UP)
    {
        _engine_editor_panel *panel = _engine_editor_find(_engine_editor_drag_panel);
        float dx = event->mouseX - _engine_editor_drag_start_x;
        float dy = event->mouseY - _engine_editor_drag_start_y;
        bool moved = dx * dx + dy * dy > 24.0f * 24.0f;
        if (panel && moved)
        {
            int target = -1;
            for (int area = 0; area < _ENGINE_EDITOR_AREA_COUNT; ++area)
            {
                if (imgui_rect_contains(_engine_editor_area_rect(area, width, height),
                                        event->mouseX, event->mouseY))
                    target = area;
            }
            if (target >= 0)
            {
                panel->area = target;
                _engine_editor_active[target] = panel->id;
                printf("editor: panel %s -> dock area %d\n", panel->name, target);
            }
            else
            {
#if defined(ENGINE_USE_GLFW)
                int winX = 0, winY = 0;
                engine_host_main_window_pos(&winX, &winY);
                if (engine_host_open_panel_window(panel->id, panel->name,
                                                  winX + (int)event->mouseX, winY + (int)event->mouseY,
                                                  480, 360))
                {
                    panel->area = -1;
                    printf("editor: panel %s detached to native window\n", panel->name);
                }
#endif
            }
        }
        _engine_editor_drag_panel = 0;
    }

    // surukleme sirasinda ghost tab
    if (_engine_editor_drag_panel && event && event->type == IMGUI_EVENT_REPAINT)
    {
        _engine_editor_panel *panel = _engine_editor_find(_engine_editor_drag_panel);
        if (panel)
        {
            engine_draw_ui_rect(event->mouseX + 10.0f, event->mouseY + 10.0f, 110.0f, 24.0f,
                                46, 130, 190, 200, 1400);
            imgui_label(panel->title,
                        (imgui_rect){event->mouseX + 16.0f, event->mouseY + 12.0f, 100.0f, 22.0f},
                        &imgui_skin_current()->label);
        }
    }
}

static void _engine_text_only_frame(float dt)
{
    static float fpsAccum = 0.0f;
    static int fpsFrames = 0;
    static VmString *pangram;
    static VmString *sizes;
    static VmString *alphabet;
    (void)dt;

    if (!pangram)
    {
        pangram = _engine_demo_string("SDF text: The quick brown fox jumps over 0123456789");
        sizes = _engine_demo_string("small 18px / medium 32px / large 50px");
        alphabet = _engine_demo_string("ABCDEFGHIJKLMNOPQRSTUVWXYZ abcdefghijklmnopqrstuvwxyz");
    }

    engine_begin_frame((_engine_Color){0, 0, 0, 255});
    engine_pass_desc textPass = {0};
    textPass.name = "text-only";
    textPass.sortMode = ENGINE_SORT_PAINTER;
    engine_begin_pass(&textPass);
    engine_camera_2d((float)_ENGINE_HOST_W(), (float)_ENGINE_HOST_H());

    imgui_begin_frame(engine_delta_time());
    imgui_event repaint = {0};
    repaint.type = IMGUI_EVENT_REPAINT;
    imgui_begin_event(&repaint);
    if (_engine_demo_font)
    {
        imgui_debug_draw_text_guides(_engine_demo_font, _engine_demo_title, 18.0f, 32.0f, 24.0f, 8);
        imgui_debug_draw_text_guides(_engine_demo_font, pangram, 32.0f, 32.0f, 90.0f, 8);
        imgui_debug_draw_text_guides(_engine_demo_font, sizes, 18.0f, 32.0f, 150.0f, 8);
        imgui_debug_draw_text_guides(_engine_demo_font, alphabet, 24.0f, 32.0f, 190.0f, 8);
        imgui_draw_text_at(_engine_demo_font, _engine_demo_title, 18.0f, 32.0f, 24.0f,
                           (imgui_color){255, 255, 255, 255}, 10);
        imgui_draw_text_at(_engine_demo_font, pangram,
                           32.0f, 32.0f, 90.0f, (imgui_color){255, 255, 255, 255}, 10);
        imgui_draw_text_at(_engine_demo_font, sizes,
                           18.0f, 32.0f, 150.0f, (imgui_color){255, 255, 255, 255}, 10);
        imgui_draw_text_at(_engine_demo_font, alphabet,
                           24.0f, 32.0f, 190.0f, (imgui_color){255, 255, 255, 255}, 10);
    }
    imgui_end_event();
    imgui_end_frame();

    engine_end_pass();
    engine_end_frame();

    fpsAccum += engine_delta_time();
    ++fpsFrames;
    if (fpsAccum >= 0.5f)
    {
        printf("fps=%.2f drawcalls=%d instances=%d\n",
               (float)fpsFrames / fpsAccum, engine_stat_draw_calls(), engine_stat_instances());
        fpsAccum = 0.0f;
        fpsFrames = 0;
    }
}

static void _engine_default_demo_frame(float dt)
{
    const float TAU = 6.28318530717958647692f;
    const float RT_W = 1280.0f;
    const float RT_H = 720.0f;
    static float t = 0.0f;
    static float fpsAccum = 0.0f;
    static int fpsFrames = 0;
    static float fps = 0.0f;
    t += dt;

    if (!_engine_demo_sceneRT)
        _engine_demo_sceneRT = engine_bitmapData_create_render_target((int)RT_W, (int)RT_H);

    _engine_Color bg = {23, 25, 29, 255};
    _engine_Color rtClear = {6, 8, 16, 255};
    engine_begin_frame(bg);

    // oyun sahnesi offscreen target'a cizilir (editor "game view" senaryosu)
    engine_push_target(_engine_demo_sceneRT, rtClear);

    engine_pass_desc worldPass = {0};
    worldPass.name = "world-opaque";
    worldPass.sortMode = ENGINE_SORT_OPAQUE;
    engine_begin_pass(&worldPass);

    _engine_Vec3 eye = {0.0f, 1.5f, 8.5f};
    _engine_Vec3 target = {0.0f, 0.0f, 0.0f};
    _engine_Mat4 proj, view, vp;
    engine_mat4_perspective(&proj, 0.8f, RT_W / RT_H, 0.1f, 30.0f);
    engine_mat4_lookat(&view, eye, target, (_engine_Vec3){0.0f, 1.0f, 0.0f});
    engine_mat4_multiply(&proj, &view, &vp);
    engine_set_view_projection(&vp);
    for (int z = -2; z <= 2; ++z)
    {
        for (int x = -3; x <= 3; ++x)
        {
            int idx = (z + 2) * 7 + (x + 3);
            float px = (float)x * 1.5f;
            float py = sinf(t * 1.2f + (float)idx * 0.7f) * 0.8f;
            float pz = (float)z * 1.5f;
            _engine_Vec3 pos = {px, py, pz};
            _engine_Vec3 rot = {t * 0.9f + idx * 0.18f, t * 1.1f + idx * 0.22f, t * 0.7f + idx * 0.15f};
            _engine_Vec3 scale = {0.45f, 0.45f, 0.45f};
            _engine_Mat4 m;
            engine_mat4_trs(&m, pos, rot, scale);
            _engine_Color tint = {
                (unsigned char)(60 + idx * 8 % 180),
                (unsigned char)(120 + idx * 5 % 120),
                (unsigned char)(200 - idx * 6 % 80),
                255};
            engine_draw_mesh(_engine.cubeMesh, &_engine.defaultMaterial, &m, tint, 0.0f, 0.0f, 1.0f, 1.0f, idx % 16);
        }
    }
    engine_end_pass();

    engine_pass_desc fxPass = {0};
    fxPass.name = "world-transparent";
    fxPass.sortMode = ENGINE_SORT_TRANSPARENT;
    engine_begin_pass(&fxPass);
    engine_camera_2d(RT_W, RT_H);
    for (int i = 0; i < 128; ++i)
    {
        float a = ((float)i / 128.0f) * TAU + t * 0.8f;
        float r = 180.0f + 150.0f * sinf(t * 1.5f + i * 0.33f);
        float x = 640.0f + cosf(a) * r;
        float y = 360.0f + sinf(a * 1.7f) * 200.0f;
        float size = 12.0f + (i % 6) * 2.5f;
        _engine_Mat4 m;
        engine_mat4_trs2d(&m, x, y, 0.0f, size, size, a * 2.0f + t);
        _engine_Color tint = {
            (unsigned char)(120 + 110.0f * sinf(a + t)),
            (unsigned char)(140 + 90.0f * cosf(a * 2.1f - t)),
            (unsigned char)(200 + 55.0f * sinf(a * 1.7f + t * 1.4f)),
            160};
        engine_draw_mesh(_engine.quadMesh, &_engine.defaultMaterial, &m, tint, 0.0f, 0.0f, 1.0f, 1.0f, 64 + (i % 8));
    }
    engine_end_pass();

    engine_pop_target();

    // swapchain: editor layout tum pencereyi kaplar; sahne Scene panelinde gorunur
    engine_pass_desc uiPass = {0};
    uiPass.name = "ui";
    uiPass.sortMode = ENGINE_SORT_UI;
    engine_begin_pass(&uiPass);
    engine_camera_2d(engine_width(), engine_height());

    imgui_begin_frame(dt);
    imgui_event layout = {0};
    layout.type = IMGUI_EVENT_LAYOUT;
    imgui_begin_event(&layout);
    _engine_demo_imgui();
    imgui_end_event();

    if (_engine_imgui_has_pending_event)
    {
        imgui_begin_event(&_engine_imgui_pending_event);
        _engine_demo_imgui();
        imgui_end_event();
        _engine_imgui_has_pending_event = false;
    }

    imgui_event repaint = {0};
    repaint.type = IMGUI_EVENT_REPAINT;
    repaint.mouseX = _engine_imgui_mouse_x;
    repaint.mouseY = _engine_imgui_mouse_y;
    imgui_begin_event(&repaint);
    _engine_demo_imgui();
    imgui_end_event();
    imgui_end_frame();
    engine_end_pass();

    engine_end_frame();

    fpsAccum += dt;
    fpsFrames++;
    if (fpsAccum >= 0.5f)
    {
        fps = (float)fpsFrames / fpsAccum;
        printf("fps=%.2f drawcalls=%d instances=%d\n", fps, engine_stat_draw_calls(), engine_stat_instances());
        for (int i = 0; i < engine_profile_count(); ++i)
        {
            const engine_profile_sample *s = engine_profile_sample_at(i);
            printf("  [%d] %-18s target=%-3s draws=%d inst=%d cpu=%.3fms\n",
                   i, s->pass, s->target ? "rt" : "scr", s->drawCalls, s->instances, s->cpuMs);
        }
        fpsAccum = 0.0f;
        fpsFrames = 0;
    }
}

#if !defined(ENGINE_USE_GLFW)
static sapp_desc _engine_sapp_desc(void)
{
    sapp_desc d = {0};
    d.width = _engine_app.width > 0 ? _engine_app.width : 1280;
    d.height = _engine_app.height > 0 ? _engine_app.height : 720;
    d.window_title = _engine_app.title ? _engine_app.title : "engine";
    d.sample_count = _engine_app.sampleCount > 0 ? _engine_app.sampleCount : 1;
    d.high_dpi = _engine_app.highDpi ? true : false;
    d.init_cb = _engine_init_cb;
    d.frame_cb = _engine_frame_cb;
    d.cleanup_cb = _engine_cleanup_cb;
    d.event_cb = (void (*)(const sapp_event *))_engine_event_cb;
    d.logger.func = slog_func;
    return d;
}

// Android'de sokol once main()'i cagirir; orada engine_run sadece desc'i saklar.
void engine_run(engine_app_desc *desc)
{
    _engine_app = *desc;
    if (!_engine_app.frame)
        _engine_app.frame = _engine_default_demo_frame;
#if !defined(__ANDROID__)
    sapp_desc d = _engine_sapp_desc();
    sapp_run(&d);
#endif
}
#else
void engine_host_glfw_run(int width, int height, const char *title);

void engine_run(engine_app_desc *desc)
{
    _engine_app = *desc;
    if (!_engine_app.frame)
        _engine_app.frame = _engine_default_demo_frame;
    engine_host_glfw_run(_engine_app.width > 0 ? _engine_app.width : 1280,
                         _engine_app.height > 0 ? _engine_app.height : 720,
                         _engine_app.title ? _engine_app.title : "engine");
}
#endif

int main(int argc, char **argv)
{
    (void)argc;
    (void)argv;
    engine_app_desc desc = {0};
    desc.width = 1280;
    desc.height = 720;
    desc.title = "engine";
    desc.sampleCount = 1;
    desc.maxInstances = 4096;
    engine_run(&desc);
    return 0;
}

#if defined(__ANDROID__)
extern int main(int argc, char **argv);
sapp_desc sokol_main(int argc, char *argv[])
{
    main(argc, argv);
    return _engine_sapp_desc();
}
#endif
