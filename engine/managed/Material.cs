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

    // KANON: shader'lar duz renk doner, wrapper cikista premultiply eder ->
    // normal alpha blend'in faktorleri One/OneMinusSrcAlpha'dir (motor geneli tek sozlesme).
    public BlendFactor SrcBlend = BlendFactor.One;
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

    // BlendMode varyantlari (Normal haric 3 mod, lazy). Paylasilan materyal deseni
    // korunur: taban mutate edilebildigi icin MainTexture/Shader her secimde esitlenir.
    Material[] _blendVariants;

    public Material ForBlend(BlendMode mode)
    {
        if (mode == BlendMode.Normal)
            return this;
        _blendVariants ??= new Material[3];
        int i = (int)mode - 1;
        var v = _blendVariants[i];
        if (v == null)
        {
            v = new Material
            {
                SortMode = SortMode,
                CullMode = CullMode,
                DepthTest = DepthTest,
                DepthWrite = DepthWrite,
                SamplerType = SamplerType,
            };
            switch (mode)
            {
                case BlendMode.Additive: v.SrcBlend = BlendFactor.One; v.DstBlend = BlendFactor.One; break;
                case BlendMode.Multiply: v.SrcBlend = BlendFactor.DstColor; v.DstBlend = BlendFactor.OneMinusSrcAlpha; break;
                case BlendMode.Screen: v.SrcBlend = BlendFactor.One; v.DstBlend = BlendFactor.OneMinusSrcColor; break;
            }
            _blendVariants[i] = v;
        }
        v.MainTexture = MainTexture;
        v.Shader = Shader;
        return v;
    }

    // Pixel-effect varyantlari: composed shader'li klon (composed Shader referansi
    // anahtar — zincir cache'i FxCompose'ta icerik-anahtarli). .fx reload'unda yeni
    // composed dogar, eski girdi olu kalir (editor-only kucuk sizinti, kabul).
    System.Collections.Generic.List<(Shader s, Material v)> _fxVariants;

    public Material ForEffects(System.Collections.Generic.List<PixelEffect> fx)
    {
        if (fx == null || fx.Count == 0)
            return this;
        var core = Shader ?? Shader.Default;
        var composed = FxCompose.Get(core, fx);
        if (ReferenceEquals(composed, core))
            return this; // bos/derlenemeyen zincir: efektsiz core
        _fxVariants ??= new System.Collections.Generic.List<(Shader, Material)>(2);
        for (int i = 0; i < _fxVariants.Count; i++)
        {
            if (!ReferenceEquals(_fxVariants[i].s, composed))
                continue;
            var mv = _fxVariants[i].v;
            mv.MainTexture = MainTexture;
            return mv;
        }
        var nv = new Material
        {
            MainTexture = MainTexture,
            Shader = composed,
            SortMode = SortMode,
            SrcBlend = SrcBlend,
            DstBlend = DstBlend,
            CullMode = CullMode,
            DepthTest = DepthTest,
            DepthWrite = DepthWrite,
            SamplerType = SamplerType,
        };
        _fxVariants.Add((composed, nv));
        return nv;
    }
}
