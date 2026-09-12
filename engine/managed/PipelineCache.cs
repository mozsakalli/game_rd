using System.Collections.Generic;

namespace DigitoyEngine;

// C '_engine_get_pipeline' + '_engine.pips[]' karsiligi. Material'in render
// state'inden pipeline'i lazy turetir ve state key ile cache'ler. GetPipeline
// engine.c gibi kucuk yogun bir SLOT indeksi dondurur (sort key'e 8 bit sigar);
// gercek sokol pipeline id'si PipelineId(slot) ile alinir.
//
// Vertex layout (buffer 0, per-vertex, Vertex struct):
//   attr0 position FLOAT3  @0 ; attr1 uv FLOAT2 @12 ; attr2 color UBYTE4N @20
// Instance layout (buffer 1, per-instance, Instance struct):
//   attr3..6 model rows FLOAT4 @0/16/32/48 ; attr7 uvRect FLOAT4 @64 ;
//   attr8..11 tint0..3 UBYTE4N @80/84/88/92 ; attr12 user FLOAT4 @96
public static unsafe class PipelineCache
{
    struct Entry
    {
        public uint StateKey;
        public uint Pipeline;
    }

    static readonly List<Entry> _entries = new();

    // Materyal + index genisligi -> pipeline SLOT indeksi (cache'li).
    public static int GetPipeline(Material material, bool index32)
    {
        uint key = material.StateKey(index32);
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].StateKey == key)
                return i;

        _entries.Add(new Entry { StateKey = key, Pipeline = Build(material, index32) });
        return _entries.Count - 1;
    }

    // Slot -> gercek sokol pipeline id.
    public static uint PipelineId(int slot) => _entries[slot].Pipeline;

    public static void Clear() => _entries.Clear();

    static uint Build(Material material, bool index32)
    {
        Sokol.PipelineBegin();
        Sokol.PipelineShader(material.Shader != null ? material.Shader.Handle : Shader.Default.Handle);
        Sokol.PipelineIndexType(index32 ? SG.IndexTypeUint32 : SG.IndexTypeUint16);
        Sokol.PipelinePrimitiveType(SG.PrimitiveTypeTriangles);
        Sokol.PipelineFaceWinding(SG.FaceWindingCcw);
        Sokol.PipelineCullMode(CullToSg(material.CullMode));
        Sokol.PipelineDepth(
            material.DepthTest ? SG.CompareFuncLessEqual : SG.CompareFuncAlways,
            material.DepthWrite);

        // buffer 0: vertex (per-vertex), buffer 1: instance (per-instance)
        Sokol.PipelineBuffer(0, sizeof(Vertex), SG.VertexStepPerVertex);
        Sokol.PipelineBuffer(1, sizeof(Instance), SG.VertexStepPerInstance);

        Sokol.PipelineAttr(0, 0, 0, SG.VertexFormatFloat3);   // position
        Sokol.PipelineAttr(1, 0, 12, SG.VertexFormatFloat2);  // uv
        Sokol.PipelineAttr(2, 0, 20, SG.VertexFormatUbyte4N); // color
        for (int i = 0; i < 4; i++)
            Sokol.PipelineAttr(3 + i, 1, i * 16, SG.VertexFormatFloat4); // model rows
        Sokol.PipelineAttr(7, 1, 64, SG.VertexFormatFloat4);  // uvRect
        for (int i = 0; i < 4; i++)
            Sokol.PipelineAttr(8 + i, 1, 80 + i * 4, SG.VertexFormatUbyte4N); // tint0..3
        var shader = material.Shader ?? Shader.Default;
        if (shader.UsesUser)
            Sokol.PipelineAttr(12, 1, 96, SG.VertexFormatFloat4); // user (yalniz USER'li shader)

        if (!material.IsOpaque)
        {
            Sokol.PipelineColorBlend(
                0, true,
                BlendToSg(material.SrcBlend), BlendToSg(material.DstBlend), SG.BlendOpAdd,
                SG.BlendFactorOne, SG.BlendFactorOneMinusSrcAlpha, SG.BlendOpAdd);
        }

        return Sokol.PipelineEnd();
    }

    static int CullToSg(CullMode cull) => cull switch
    {
        CullMode.Front => SG.CullModeFront,
        CullMode.Back => SG.CullModeBack,
        _ => SG.CullModeNone,
    };

    // engine.c _engine_blend_factor ile ayni eslesme.
    static int BlendToSg(BlendFactor f) => f switch
    {
        BlendFactor.Zero => SG.BlendFactorZero,
        BlendFactor.One => SG.BlendFactorOne,
        BlendFactor.SrcAlpha => SG.BlendFactorSrcAlpha,
        BlendFactor.OneMinusSrcAlpha => SG.BlendFactorOneMinusSrcAlpha,
        BlendFactor.DstAlpha => SG.BlendFactorDstAlpha,
        BlendFactor.OneMinusDstAlpha => SG.BlendFactorOneMinusDstAlpha,
        BlendFactor.SrcColor => SG.BlendFactorSrcColor,
        BlendFactor.OneMinusSrcColor => SG.BlendFactorOneMinusSrcColor,
        BlendFactor.DstColor => SG.BlendFactorDstColor,
        BlendFactor.OneMinusDstColor => SG.BlendFactorOneMinusDstColor,
        _ => SG.BlendFactorOne,
    };
}
