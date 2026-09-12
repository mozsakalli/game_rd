namespace DigitoyEngine;

// C '_engine_Material' karsiligi (Unity Material). Yalnizca RENDER STATE tutar;
// pipeline bu state'ten PipelineCache tarafindan lazy turetilir. Materyal degisince
// yeni bir state key olusur, cache yeni pipeline'i bir kez kurup saklar.
public sealed class Material
{
    public Texture MainTexture;
    public Shader Shader;

    // Unity Material.renderQueue karsiligi. Varsayilan blend alpha oldugu icin Transparent.
    public SortMode SortMode = SortMode.Transparent;

    public BlendFactor SrcBlend = BlendFactor.SrcAlpha;
    public BlendFactor DstBlend = BlendFactor.OneMinusSrcAlpha;
    public CullMode CullMode = CullMode.None;
    public bool DepthTest;
    public bool DepthWrite;
    public SamplerType SamplerType = SamplerType.Linear;

    // engine.c _engine_get_pipeline ile BIREBIR ayni state key.
    //   blend(4b) | blend(4b) | cull(2b) | depthTest(1b) | depthWrite(1b) | index32(1b)
    //   ^ shader.Id * 0x9E3779B9  (Fibonacci hashing)
    public uint StateKey(bool index32)
    {
        uint key = ((uint)(int)SrcBlend & 15)
                 | (((uint)(int)DstBlend & 15) << 4)
                 | (((uint)(int)CullMode & 3) << 8)
                 | ((DepthTest ? 1u : 0u) << 10)
                 | ((DepthWrite ? 1u : 0u) << 11)
                 | ((index32 ? 1u : 0u) << 12);
        key ^= Shader != null ? (Shader.Id * 0x9E3779B9u) : 0u;
        return key;
    }

    public bool IsOpaque => SrcBlend == BlendFactor.One && DstBlend == BlendFactor.Zero;
}
