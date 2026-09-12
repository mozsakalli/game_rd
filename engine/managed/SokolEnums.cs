// ---------------------------------------------------------------------------
// SokolEnums.cs — sokol_gfx enum'larinin duz int sabit yansimasi.
// ---------------------------------------------------------------------------
//
// sokol enum'lari ince katmanda struct'a MIRROR edilmez; C# duz int gonderir,
// shim (sokol_shim.c) int'i ilgili sg_* enum'una cast eder. Bu sinifin degerleri
// engine/native/sokol/sokol_gfx.h icindeki ordinal siralamayla BIREBIR aynidir.
// Deger degistiginde header ile senkron tutulmalidir.
// ---------------------------------------------------------------------------

namespace DigitoyEngine;

public static class SG
{
    // sg_index_type
    public const int IndexTypeDefault = 0;
    public const int IndexTypeNone = 1;
    public const int IndexTypeUint16 = 2;
    public const int IndexTypeUint32 = 3;

    // sg_image_type
    public const int ImageTypeDefault = 0;
    public const int ImageType2D = 1;
    public const int ImageTypeCube = 2;
    public const int ImageType3D = 3;
    public const int ImageTypeArray = 4;

    // sg_image_sample_type
    public const int ImageSampleTypeDefault = 0;
    public const int ImageSampleTypeFloat = 1;
    public const int ImageSampleTypeDepth = 2;
    public const int ImageSampleTypeSint = 3;
    public const int ImageSampleTypeUint = 4;
    public const int ImageSampleTypeUnfilterableFloat = 5;

    // sg_sampler_type
    public const int SamplerTypeDefault = 0;
    public const int SamplerTypeFiltering = 1;
    public const int SamplerTypeNonfiltering = 2;
    public const int SamplerTypeComparison = 3;

    // sg_primitive_type
    public const int PrimitiveTypeDefault = 0;
    public const int PrimitiveTypePoints = 1;
    public const int PrimitiveTypeLines = 2;
    public const int PrimitiveTypeLineStrip = 3;
    public const int PrimitiveTypeTriangles = 4;
    public const int PrimitiveTypeTriangleStrip = 5;

    // sg_filter
    public const int FilterDefault = 0;
    public const int FilterNearest = 1;
    public const int FilterLinear = 2;

    // sg_wrap
    public const int WrapDefault = 0;
    public const int WrapRepeat = 1;
    public const int WrapClampToEdge = 2;
    public const int WrapClampToBorder = 3;
    public const int WrapMirroredRepeat = 4;

    // sg_vertex_format
    public const int VertexFormatInvalid = 0;
    public const int VertexFormatFloat = 1;
    public const int VertexFormatFloat2 = 2;
    public const int VertexFormatFloat3 = 3;
    public const int VertexFormatFloat4 = 4;
    public const int VertexFormatInt = 5;
    public const int VertexFormatInt2 = 6;
    public const int VertexFormatInt3 = 7;
    public const int VertexFormatInt4 = 8;
    public const int VertexFormatUint = 9;
    public const int VertexFormatUint2 = 10;
    public const int VertexFormatUint3 = 11;
    public const int VertexFormatUint4 = 12;
    public const int VertexFormatByte4 = 13;
    public const int VertexFormatByte4N = 14;
    public const int VertexFormatUbyte4 = 15;
    public const int VertexFormatUbyte4N = 16;
    public const int VertexFormatShort2 = 17;
    public const int VertexFormatShort2N = 18;
    public const int VertexFormatUshort2 = 19;
    public const int VertexFormatUshort2N = 20;
    public const int VertexFormatShort4 = 21;
    public const int VertexFormatShort4N = 22;
    public const int VertexFormatUshort4 = 23;
    public const int VertexFormatUshort4N = 24;
    public const int VertexFormatInt10N2 = 25;
    public const int VertexFormatUint10N2 = 26;
    public const int VertexFormatHalf2 = 27;
    public const int VertexFormatHalf4 = 28;

    // sg_vertex_step
    public const int VertexStepDefault = 0;
    public const int VertexStepPerVertex = 1;
    public const int VertexStepPerInstance = 2;

    // sg_uniform_type
    public const int UniformTypeInvalid = 0;
    public const int UniformTypeFloat = 1;
    public const int UniformTypeFloat2 = 2;
    public const int UniformTypeFloat3 = 3;
    public const int UniformTypeFloat4 = 4;
    public const int UniformTypeInt = 5;
    public const int UniformTypeInt2 = 6;
    public const int UniformTypeInt3 = 7;
    public const int UniformTypeInt4 = 8;
    public const int UniformTypeMat4 = 9;

    // sg_uniform_layout
    public const int UniformLayoutDefault = 0;
    public const int UniformLayoutNative = 1;
    public const int UniformLayoutStd140 = 2;

    // sg_cull_mode
    public const int CullModeDefault = 0;
    public const int CullModeNone = 1;
    public const int CullModeFront = 2;
    public const int CullModeBack = 3;

    // sg_face_winding
    public const int FaceWindingDefault = 0;
    public const int FaceWindingCcw = 1;
    public const int FaceWindingCw = 2;

    // sg_compare_func
    public const int CompareFuncDefault = 0;
    public const int CompareFuncNever = 1;
    public const int CompareFuncLess = 2;
    public const int CompareFuncEqual = 3;
    public const int CompareFuncLessEqual = 4;
    public const int CompareFuncGreater = 5;
    public const int CompareFuncNotEqual = 6;
    public const int CompareFuncGreaterEqual = 7;
    public const int CompareFuncAlways = 8;

    // sg_blend_factor
    public const int BlendFactorDefault = 0;
    public const int BlendFactorZero = 1;
    public const int BlendFactorOne = 2;
    public const int BlendFactorSrcColor = 3;
    public const int BlendFactorOneMinusSrcColor = 4;
    public const int BlendFactorSrcAlpha = 5;
    public const int BlendFactorOneMinusSrcAlpha = 6;
    public const int BlendFactorDstColor = 7;
    public const int BlendFactorOneMinusDstColor = 8;
    public const int BlendFactorDstAlpha = 9;
    public const int BlendFactorOneMinusDstAlpha = 10;
    public const int BlendFactorSrcAlphaSaturated = 11;
    public const int BlendFactorBlendColor = 12;
    public const int BlendFactorOneMinusBlendColor = 13;
    public const int BlendFactorBlendAlpha = 14;
    public const int BlendFactorOneMinusBlendAlpha = 15;
    public const int BlendFactorSrc1Color = 16;
    public const int BlendFactorOneMinusSrc1Color = 17;
    public const int BlendFactorSrc1Alpha = 18;
    public const int BlendFactorOneMinusSrc1Alpha = 19;

    // sg_blend_op
    public const int BlendOpDefault = 0;
    public const int BlendOpAdd = 1;
    public const int BlendOpSubtract = 2;
    public const int BlendOpReverseSubtract = 3;
    public const int BlendOpMin = 4;
    public const int BlendOpMax = 5;

    // sg_shader_stage
    public const int ShaderStageNone = 0;
    public const int ShaderStageVertex = 1;
    public const int ShaderStageFragment = 2;
    public const int ShaderStageCompute = 3;

    // sg_shader_attr_base_type
    public const int ShaderAttrBaseTypeUndefined = 0;
    public const int ShaderAttrBaseTypeFloat = 1;
    public const int ShaderAttrBaseTypeSint = 2;
    public const int ShaderAttrBaseTypeUint = 3;

    // sg_view_type
    public const int ViewTypeInvalid = 0;
    public const int ViewTypeStorageBuffer = 1;
    public const int ViewTypeStorageImage = 2;
    public const int ViewTypeTexture = 3;
    public const int ViewTypeColorAttachment = 4;
    public const int ViewTypeResolveAttachment = 5;
    public const int ViewTypeDepthStencilAttachment = 6;

    // sg_pixel_format (yaygin alt kume)
    public const int PixelFormatNone = 1;
    public const int PixelFormatR8 = 2;
    public const int PixelFormatRg8 = 11;
    public const int PixelFormatR32F = 17;
    public const int PixelFormatRgba8 = 23;
    public const int PixelFormatSrgb8A8 = 24;
    public const int PixelFormatBgra8 = 28;
    public const int PixelFormatRgba16F = 40;
    public const int PixelFormatRgba32F = 43;
    public const int PixelFormatDepth = 44;
    public const int PixelFormatDepthStencil = 45;

    // MakeView viewType (shim'e ozel: 0=texture, 1=color, 2=depth)
    public const int ViewTexture = 0;
    public const int ViewColorAttachment = 1;
    public const int ViewDepthStencilAttachment = 2;

    // MakeBuffer bufferType / dynamism
    public const int BufferVertex = 0;
    public const int BufferIndex = 1;
    public const int UsageImmutable = 0;
    public const int UsageDynamic = 1;
    public const int UsageStream = 2;

    // MakeImage usage
    public const int ImageImmutable = 0;
    public const int ImageDynamic = 1;
    public const int ImageColorAttachment = 2;
    public const int ImageDepthStencilAttachment = 3;
}
