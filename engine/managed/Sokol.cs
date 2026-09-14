using System;
using System.Runtime.InteropServices;

namespace DigitoyEngine;

// sokol_gfx uzerine INCE, DUZ-ARGUMANLI binding yuzeyi.
//
// Tasarim ilkesi: sokol'un buyuk '*_desc' struct'larini C# tarafinda birebir
// AYNALAMIYORUZ (hataya cok acik). Bunun yerine her metod duz skaler/pointer
// argumanlar alir; C shim (sokol_shim.c) bunlari sokol desc'ine cevirip TEK bir
// sokol cagrisi yapar. Batch/sort/state mantigi C# renderer'da; shim'de mantik yok.
//
// Handle'lar: sokol handle'lari { uint32_t id } yapisindadir. FFI'da struct-return
// ABI inceliklerinden kacinmak icin burada ham 'uint' id olarak tasinir; shim
// uint <-> sg_* donusumunu yapar. 0 = gecersiz/hicbir kaynak.
//
// BINDING: standart [DllImport] + EntryPoint kullaniyoruz.
//   - Gercek .NET host (editor/tool): platform native lib'inden (Lib) EntryPoint'i
//     dinamik yukler. Her platform icin bu lib (sokol_shim + sokol_gfx) derilir.
//   - langtest AOT: kutuphane adi YOK SAYILIR; sadece EntryPoint C sembol adi olarak
//     emit edilip sokol_shim.c ile statik linklenir. Mangling devreye girmez.
public static unsafe class Sokol
{
    // Gercek .NET host'ta yuklenecek platform native kutuphanesi (AOT'ta yok sayilir).
    const string Lib = "digitoyengine_native";

    // --- Per-frame cizim (SICAK YOL) ---

    // Verilen pipeline'i aktif eder (sg_apply_pipeline).
    [DllImport(Lib, EntryPoint = "de_sokol_apply_pipeline")]
    extern public static void ApplyPipeline(uint pip);

    // Cizim kaynaklarini baglar (sg_apply_bindings). sokol'da instance verisi 2. VERTEX
    // buffer slotudur (PER_INSTANCE), ayri bir "instance buffer" dizisi yoktur. Doku bir
    // VIEW olarak baglanir (sokol'un yeni view modeli). instanceOffset = AppendBuffer'in
    // dondurdugu bayt ofseti + item ofseti.
    [DllImport(Lib, EntryPoint = "de_sokol_apply_bindings")]
    extern public static void ApplyBindings(
        uint vertexBuffer,
        uint instanceBuffer, int instanceOffset,
        uint indexBuffer,
        uint textureView, uint sampler);

    // Uniform blogunu yukler (sg_apply_uniforms). data 'byteSize' uzunlugunda ham veri.
    [DllImport(Lib, EntryPoint = "de_sokol_apply_uniforms")]
    extern public static void ApplyUniforms(int slot, void* data, int byteSize);

    // Cizim cagrisi (sg_draw): index tamponundaki 'baseElement'ten 'numElements' index,
    // 'numInstances' ornek.
    [DllImport(Lib, EntryPoint = "de_sokol_draw")]
    extern public static void Draw(int baseElement, int numElements, int numInstances);

    // Stream tampona veri ekler (sg_append_buffer); dondurdugu bayt ofseti
    // ApplyBindings ofseti olarak kullanilir. Frame basi otomatik sifirlanir.
    [DllImport(Lib, EntryPoint = "de_sokol_append_buffer")]
    extern public static int AppendBuffer(uint buffer, void* data, int byteSize);

    // CommandBuffer akisini native decoder'da yurutur (frame'de bir kez, pass'ler
    // dahil tum komutlar akista). Akis formati RenderCmd enum ile birebir.
    [DllImport(Lib, EntryPoint = "de_sokol_render_execute")]
    extern public static void RenderExecute(byte* stream, int length);

    // Frame debugger replay'i: ilk maxDraws draw calisir (-1=hepsi); swapchain
    // pass'leri overrideColorView!=0 ise o RT attachment'larina yonlendirilir.
    [DllImport(Lib, EntryPoint = "de_sokol_render_execute_dbg")]
    extern public static void RenderExecuteDbg(byte* stream, int length, int maxDraws,
        uint overrideColorView, uint overrideDepthView);

    // --- Async asset IO (worker thread native'de; managed tek thread kalir) ---

    [DllImport(Lib, EntryPoint = "de_asset_load")]
    extern public static int AssetLoad([MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    // Pak icinden aralik: [offset, offset+length) worker'da okunur; rawLength!=length
    // ise aralik zlib'li, once inflate sonra decode.
    [DllImport(Lib, EntryPoint = "de_asset_load_range")]
    extern public static int AssetLoadRange([MarshalAs(UnmanagedType.LPUTF8Str)] string path, long offset, long length, long rawLength);

    [DllImport(Lib, EntryPoint = "de_asset_poll")]
    extern public static int AssetPoll(int job, out IntPtr pixels, out int w, out int h);

    [DllImport(Lib, EntryPoint = "de_asset_free_job")]
    extern public static void AssetFreeJob(int job);

    // --- Raster glyph (UI text; fontstash modeli) ---

    [DllImport(Lib, EntryPoint = "de_font_glyph")]
    extern public static int FontGlyph(byte* ttf, float pixelHeight, int codepoint,
        byte* buffer, int bufferSize, float* metrics);

    [DllImport(Lib, EntryPoint = "de_font_vmetrics")]
    extern public static int FontVMetrics(byte* ttf, float pixelHeight, float* out3);

    // --- Viewport / scissor ---

    [DllImport(Lib, EntryPoint = "de_sokol_apply_viewport")]
    extern public static void ApplyViewport(int x, int y, int width, int height, bool originTopLeft);

    [DllImport(Lib, EntryPoint = "de_sokol_apply_scissor_rect")]
    extern public static void ApplyScissorRect(int x, int y, int width, int height, bool originTopLeft);

    // --- Frame / pass ---

    // Swapchain pass'i baslatir; tum alanlar C#'tan. clear* bayraklari 0/1, renk/derinlik
    // degerleri 0..1. framebuffer=0 => GL varsayilan ekran, !=0 => offscreen fbo.
    [DllImport(Lib, EntryPoint = "de_sokol_begin_pass")]
    extern public static void BeginPass(
        bool clearColor, float clearR, float clearG, float clearB, float clearA,
        bool clearDepth, float depthValue,
        int width, int height, int sampleCount, uint framebuffer);

    [DllImport(Lib, EntryPoint = "de_sokol_end_pass")]
    extern public static void EndPass();

    // Offscreen pass: hedef attachment view'lar (MakeView color/depth). depthView=0 => derinliksiz.
    [DllImport(Lib, EntryPoint = "de_sokol_begin_pass_offscreen")]
    extern public static void BeginPassOffscreen(
        bool clearColor, float clearR, float clearG, float clearB, float clearA,
        bool clearDepth, float depthValue,
        uint colorView, uint depthView);

    // SDF font atlasi baker'i (stb_truetype; parametreler shim'de sabit).
    // glyphOut: 224*9 float, kernOut: 224*224 short, metricsOut: 5 float.
    [DllImport(Lib, EntryPoint = "de_font_bake")]
    extern public static int FontBake(
        byte* ttf, byte* atlas, int atlasSize,
        float* glyphOut, short* kernOut, float* metricsOut);

    [DllImport(Lib, EntryPoint = "de_sokol_commit")]
    extern public static void Commit();

    // --- Stream tampon olusturma (per-frame degil; init'te bir kez) ---

    // Bos, dinamik (STREAM) tampon; her frame append edilir. isIndex=true index tamponu.
    [DllImport(Lib, EntryPoint = "de_sokol_make_stream_buffer")]
    extern public static uint MakeStreamBuffer(int byteSize, bool isIndex);

    [DllImport(Lib, EntryPoint = "de_sokol_destroy_buffer")]
    extern public static void DestroyBuffer(uint buffer);

    // --- Kurulum / durum ---

    // GL context AKTIF olduktan sonra cagrilir; sokol GL fonksiyonlarini kendi yukler.
    [DllImport(Lib, EntryPoint = "de_sokol_setup")]
    extern public static void Setup();

    // macOS/iOS (Metal): ana pencereye CAMetalLayer takar + MTLDevice kurar.
    // Setup'tan ONCE, GLFW penceresi (GLFW_NO_API) olusturulduktan SONRA cagrilir.
    // Yalniz Metal native lib'inde export edilir; sadece macOS/iOS'ta cagirin.
    [DllImport(Lib, EntryPoint = "de_metal_init_window")]
    extern public static void MetalInitWindow(IntPtr glfwWindow);

    // Ikincil (tear-off/floating) pencereye CAMetalLayer takar; handle (>0) doner,
    // basarisizsa -1. Yalniz Metal native lib'inde export edilir.
    [DllImport(Lib, EntryPoint = "de_metal_create_window")]
    extern public static int MetalCreateWindow(IntPtr glfwWindow);

    // Ikincil pencere kapatilirken slot'u serbest birakir.
    [DllImport(Lib, EntryPoint = "de_metal_destroy_window")]
    extern public static void MetalDestroyWindow(int handle);

    [DllImport(Lib, EntryPoint = "de_sokol_shutdown")]
    extern public static void Shutdown();

    [DllImport(Lib, EntryPoint = "de_sokol_is_valid")]
    extern public static bool IsValid();

    [DllImport(Lib, EntryPoint = "de_sokol_reset_state_cache")]
    extern public static void ResetStateCache();

    // sg_backend enum degeri.
    [DllImport(Lib, EntryPoint = "de_sokol_query_backend")]
    extern public static int QueryBackend();

    // Hata ayiklama grubu (name null-sonlu UTF8).
    [DllImport(Lib, EntryPoint = "de_sokol_push_debug_group")]
    extern public static void PushDebugGroup(byte* name);

    [DllImport(Lib, EntryPoint = "de_sokol_pop_debug_group")]
    extern public static void PopDebugGroup();

    // --- Viewport / scissor (float) + ek cizim ---

    [DllImport(Lib, EntryPoint = "de_sokol_apply_viewportf")]
    extern public static void ApplyViewportf(float x, float y, float width, float height, bool originTopLeft);

    [DllImport(Lib, EntryPoint = "de_sokol_apply_scissor_rectf")]
    extern public static void ApplyScissorRectf(float x, float y, float width, float height, bool originTopLeft);

    [DllImport(Lib, EntryPoint = "de_sokol_draw_ex")]
    extern public static void DrawEx(int baseElement, int numElements, int numInstances, int baseVertex, int baseInstance);

    [DllImport(Lib, EntryPoint = "de_sokol_dispatch")]
    extern public static void Dispatch(int numGroupsX, int numGroupsY, int numGroupsZ);

    // --- Kaynak olustur / guncelle / yok et ---

    // bufferType: 0=vertex,1=index. dynamism: 0=immutable(data), 1=dynamic, 2=stream.
    [DllImport(Lib, EntryPoint = "de_sokol_make_buffer")]
    extern public static uint MakeBuffer(void* data, int size, int bufferType, int dynamism);

    [DllImport(Lib, EntryPoint = "de_sokol_update_buffer")]
    extern public static void UpdateBuffer(uint buffer, void* data, int size);

    [DllImport(Lib, EntryPoint = "de_sokol_query_buffer_overflow")]
    extern public static bool QueryBufferOverflow(uint buffer);

    // usage: 0=immutable(data), 1=dynamic, 2=color_attachment, 3=depth_stencil_attachment.
    [DllImport(Lib, EntryPoint = "de_sokol_make_image")]
    extern public static uint MakeImage(
        int width, int height, int pixelFormat, int numMipmaps, int sampleCount,
        int usage, void* data, int dataSize);

    [DllImport(Lib, EntryPoint = "de_sokol_update_image")]
    extern public static void UpdateImage(uint image, void* data, int size);

    [DllImport(Lib, EntryPoint = "de_sokol_make_sampler")]
    extern public static uint MakeSampler(
        int minFilter, int magFilter, int mipmapFilter,
        int wrapU, int wrapV, int wrapW, int compare);

    // viewType: 0=texture, 1=color_attachment, 2=depth_stencil_attachment.
    [DllImport(Lib, EntryPoint = "de_sokol_make_view")]
    extern public static uint MakeView(uint imageId, int viewType);

    [DllImport(Lib, EntryPoint = "de_sokol_destroy_image")]
    extern public static void DestroyImage(uint id);
    [DllImport(Lib, EntryPoint = "de_sokol_destroy_sampler")]
    extern public static void DestroySampler(uint id);
    [DllImport(Lib, EntryPoint = "de_sokol_destroy_shader")]
    extern public static void DestroyShader(uint id);

    // 1 = VALID; enum degeri bilerek sizdirilmiyor (sokol surumleri arasinda kayar).
    [DllImport(Lib, EntryPoint = "de_sokol_shader_valid")]
    extern public static int ShaderValid(uint id);

    [DllImport(Lib, EntryPoint = "de_sokol_last_error")]
    extern public static IntPtr LastError();
    [DllImport(Lib, EntryPoint = "de_sokol_destroy_pipeline")]
    extern public static void DestroyPipeline(uint id);
    [DllImport(Lib, EntryPoint = "de_sokol_destroy_view")]
    extern public static void DestroyView(uint id);

    // --- Shader builder (artimli; string'ler null-sonlu UTF8 byte*) ---

    [DllImport(Lib, EntryPoint = "de_sokol_shader_begin")]
    extern public static void ShaderBegin();
    [DllImport(Lib, EntryPoint = "de_sokol_shader_vertex_source")]
    extern public static void ShaderVertexSource(byte* src);
    [DllImport(Lib, EntryPoint = "de_sokol_shader_fragment_source")]
    extern public static void ShaderFragmentSource(byte* src);
    [DllImport(Lib, EntryPoint = "de_sokol_shader_vertex_entry")]
    extern public static void ShaderVertexEntry(byte* entry);
    [DllImport(Lib, EntryPoint = "de_sokol_shader_fragment_entry")]
    extern public static void ShaderFragmentEntry(byte* entry);
    [DllImport(Lib, EntryPoint = "de_sokol_shader_attr")]
    extern public static void ShaderAttr(int index, byte* glslName, int baseType);
    [DllImport(Lib, EntryPoint = "de_sokol_shader_uniform_block")]
    extern public static void ShaderUniformBlock(int slot, int stage, int size, int layout);
    [DllImport(Lib, EntryPoint = "de_sokol_shader_uniform")]
    extern public static void ShaderUniform(int blockSlot, int index, int type, byte* glslName);
    [DllImport(Lib, EntryPoint = "de_sokol_shader_texture_view")]
    extern public static void ShaderTextureView(int slot, int stage, int imageType, int sampleType);
    [DllImport(Lib, EntryPoint = "de_sokol_shader_sampler")]
    extern public static void ShaderSampler(int slot, int stage, int samplerType);
    [DllImport(Lib, EntryPoint = "de_sokol_shader_texture_sampler_pair")]
    extern public static void ShaderTextureSamplerPair(int slot, int stage, int viewSlot, int samplerSlot, byte* glslName);
    [DllImport(Lib, EntryPoint = "de_sokol_shader_end")]
    extern public static uint ShaderEnd();

    // --- Pipeline builder (artimli) ---

    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_begin")]
    extern public static void PipelineBegin();
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_shader")]
    extern public static void PipelineShader(uint shader);
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_index_type")]
    extern public static void PipelineIndexType(int indexType);
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_primitive_type")]
    extern public static void PipelinePrimitiveType(int primitiveType);
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_cull_mode")]
    extern public static void PipelineCullMode(int cullMode);
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_face_winding")]
    extern public static void PipelineFaceWinding(int faceWinding);
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_depth")]
    extern public static void PipelineDepth(int compare, bool writeEnabled);
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_color_count")]
    extern public static void PipelineColorCount(int count);
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_buffer")]
    extern public static void PipelineBuffer(int bufferIndex, int stride, int stepFunc);
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_attr")]
    extern public static void PipelineAttr(int attrIndex, int bufferIndex, int offset, int format);
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_color_blend")]
    extern public static void PipelineColorBlend(
        int colorIndex, bool enabled,
        int srcRgb, int dstRgb, int opRgb, int srcAlpha, int dstAlpha, int opAlpha);
    [DllImport(Lib, EntryPoint = "de_sokol_pipeline_end")]
    extern public static uint PipelineEnd();
}
