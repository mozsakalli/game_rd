namespace DigitoyEngine;

using System.Runtime.InteropServices;

// C '_engine_Shader' + engine_shader.c efekt DSL'i karsiligi.
// Ortak instanced vertex shader sabit; kullanicinin fragment govdesi
// ("VEC4 fs_main(VEC2 uv, VEC4 color) { ... }") prefix/suffix ile sarilip
// tam shader kaynagina cevrilir ve sokol shader builder ile kurulur.
//
// BACKEND: Windows=GLCORE (GLSL 410), macOS/iOS=Metal (MSL). Ayni fragment DSL
// govdesi her iki dilde de derlenir; makrolar (SAMPLE/VEC4/DFDX/USER...) backend'e
// gore farkli tanimlanir. MSL'de doku/sampler global olamaz -> fs_main tanimi bir
// fonksiyon-benzeri makroyla (tex, smp) parametreleri eklenerek yeniden yazilir.
public sealed unsafe class Shader
{
    // Backend DERLEME zamani secilir (DE_RENDERER_METAL / DE_RENDERER_OPENGL,
    // csproj'daki Renderer ozelligi). Metal'de MSL sablonu secilir.
#if DE_RENDERER_METAL
    static readonly bool _metal = true;
#else
    static readonly bool _metal = false;
#endif

    static uint _nextId = 1;
    static Shader _default;

    // Pipeline state key'inde kullanilan kararli kimlik (engine.c: shader->id).
    public uint Id { get; } = _nextId++;

    // sokol shader nesne id'si (0 = henuz olusturulmadi).
    internal uint Handle;

    // Fragment USER okuyor mu: a_user attribute'u yalniz o zaman shader'a girer
    // (GL derleyicisi kullanilmayan attribute'u budar -> sokol WARN spam'i olmasin).
    internal bool UsesUser;

    // CreateEffect'e verilen DSL govdesi (compose icin saklanir; FromHandle/compose
    // edilmis shader'larda null = tekrar compose edilemez).
    internal string Body;

    // Zaten olusturulmus bir sokol shader handle'ini sarar.
    public static Shader FromHandle(uint handle) => new Shader { Handle = handle };

    // Yerlesik sprite shader'i (doku * renk). GL setup sonrasi ilk erisimde kurulur.
    public static Shader Default => _default ??= CreateEffect(_defaultFragment);

    // Ortak instanced vertex shader (engine.c _engine_vs_src, GLCORE 410).
    // Kose tint'leri uv uzayinda bilinear secilir: 4 renk ayniysa eski tek-tint
    // davranisiyla birebir; farkliysa quad ici gradient (DrawMultiColorQuad modeli).
    // {USER_*} yer tutuculari fragment USER kullaniyorsa doldurulur.
    const string _vertexTemplate =
        "#version 410\n" +
        "uniform mat4 u_viewProj;\n" +
        "in vec3 a_pos;\n" +
        "in vec2 a_uv;\n" +
        "in vec4 a_color;\n" +
        "in vec4 a_m0;\n" +
        "in vec4 a_m1;\n" +
        "in vec4 a_m2;\n" +
        "in vec4 a_m3;\n" +
        "in vec4 a_uvrect;\n" +
        "in vec4 a_tint0;\n" +
        "in vec4 a_tint1;\n" +
        "in vec4 a_tint2;\n" +
        "in vec4 a_tint3;\n" +
        "{USER_IN}" +
        "out vec2 v_uv;\n" +
        "out vec4 v_color;\n" +
        "{USER_OUT}" +
        "{LOCAL_OUT}" +
        "void main() {\n" +
        "  mat4 model = mat4(a_m0, a_m1, a_m2, a_m3);\n" +
        "  gl_Position = u_viewProj * (model * vec4(a_pos, 1.0));\n" +
        "  vec2 uv = (a_uvrect.z == 0.0 && a_uvrect.w == 0.0) ? vec2(0.5, 0.5) : (a_uvrect.xy + a_uv * a_uvrect.zw);\n" +
        "  v_uv = uv;\n" +
        "  vec4 tint = mix(mix(a_tint0, a_tint1, a_uv.x), mix(a_tint3, a_tint2, a_uv.x), a_uv.y);\n" +
        "  v_color = a_color * tint;\n" +
        "{USER_ASSIGN}" +
        "{LOCAL_ASSIGN}" +
        "}\n";

    // Fragment DSL sarmalayicilari (engine_shader.c GLCORE yolu).
    const string _fragPrefix =
        "#version 410\n" +
        "uniform sampler2D tex;\n" +
        "#define SAMPLE(t, uv) texture(t, uv)\n" +
        "#define SATURATE(x) clamp(x, 0.0, 1.0)\n" +
        "#define DFDX(x) dFdx(x)\n" +
        "#define DFDY(x) dFdy(x)\n" +
        "#define FWIDTH(x) fwidth(x)\n" +
        "in vec2 v_uv;\n" +
        "in vec4 v_color;\n" +
        "out vec4 frag_color;\n" +
        "#define VEC2 vec2\n#define VEC3 vec3\n#define VEC4 vec4\n#define FLOAT float\n";

    const string _fragUserPrefix =
        "in vec4 v_user;\n" +
        "#define USER v_user\n";

    const string _fragLocalPrefix =
        "in vec2 v_uvLocal;\n" +
        "#define LOCALUV v_uvLocal\n";

    // KANON: fs_main DUZ (straight) renk doner; premultiply TEK burada yapilir.
    // Tum alpha materyaller One/OneMinusSrcAlpha ile cizer (premultiplied kompozisyon).
    const string _fragSuffix =
        "\nvoid main() { vec4 c = fs_main(v_uv, v_color); frag_color = vec4(c.rgb * c.a, c.a); }\n";

    const string _defaultFragment =
        "VEC4 fs_main(VEC2 uv, VEC4 color) { return SAMPLE(tex, uv) * color; }";

    // -----------------------------------------------------------------------
    // METAL (MSL) sablonlari — GLSL ile ayni DSL, farkli makro/yapilar.
    // -----------------------------------------------------------------------
    // Vertex: attribute'lar [[stage_in]]; uniform block [[buffer(0)]] (sokol
    // msl_buffer_n=0). vertex buffer'lari sokol [[buffer(23+slot)]]'e baglar,
    // cakisma yok. Stage'ler arasi baglanti [[user(locnN)]] ile sabitlenir.
    const string _vertexTemplateMsl =
        "#include <metal_stdlib>\n" +
        "using namespace metal;\n" +
        "struct vs_in {\n" +
        "  float3 a_pos    [[attribute(0)]];\n" +
        "  float2 a_uv     [[attribute(1)]];\n" +
        "  float4 a_color  [[attribute(2)]];\n" +
        "  float4 a_m0     [[attribute(3)]];\n" +
        "  float4 a_m1     [[attribute(4)]];\n" +
        "  float4 a_m2     [[attribute(5)]];\n" +
        "  float4 a_m3     [[attribute(6)]];\n" +
        "  float4 a_uvrect [[attribute(7)]];\n" +
        "  float4 a_tint0  [[attribute(8)]];\n" +
        "  float4 a_tint1  [[attribute(9)]];\n" +
        "  float4 a_tint2  [[attribute(10)]];\n" +
        "  float4 a_tint3  [[attribute(11)]];\n" +
        "{USER_IN}" +
        "};\n" +
        "struct vs_out {\n" +
        "  float4 pos     [[position]];\n" +
        "  float2 v_uv    [[user(locn0)]];\n" +
        "  float4 v_color [[user(locn1)]];\n" +
        "{USER_OUT}" +
        "{LOCAL_OUT}" +
        "};\n" +
        "struct vs_uniforms { float4x4 u_viewProj; };\n" +
        "vertex vs_out vs_main(vs_in in [[stage_in]], constant vs_uniforms& ub [[buffer(0)]]) {\n" +
        "  vs_out out;\n" +
        "  float4x4 model = float4x4(in.a_m0, in.a_m1, in.a_m2, in.a_m3);\n" +
        "  out.pos = ub.u_viewProj * (model * float4(in.a_pos, 1.0));\n" +
        "  float2 uv = (in.a_uvrect.z == 0.0 && in.a_uvrect.w == 0.0) ? float2(0.5, 0.5) : (in.a_uvrect.xy + in.a_uv * in.a_uvrect.zw);\n" +
        "  out.v_uv = uv;\n" +
        "  float4 tint = mix(mix(in.a_tint0, in.a_tint1, in.a_uv.x), mix(in.a_tint3, in.a_tint2, in.a_uv.x), in.a_uv.y);\n" +
        "  out.v_color = in.a_color * tint;\n" +
        "{USER_ASSIGN}" +
        "{LOCAL_ASSIGN}" +
        "  return out;\n" +
        "}\n";

    // Fragment DSL makrolari (MSL). SAMPLE 'smp' isimli sampler'i kullanir; bu
    // isim fs_main_impl'e makroyla eklenen parametredir (asagi bak).
    const string _fragPrefixMsl =
        "#include <metal_stdlib>\n" +
        "using namespace metal;\n" +
        "#define VEC2 float2\n#define VEC3 float3\n#define VEC4 float4\n#define FLOAT float\n" +
        "#define SAMPLE(t, uv) t.sample(smp, uv)\n" +
        "#define SATURATE(x) saturate(x)\n" +
        "#define DFDX(x) dfdx(x)\n" +
        "#define DFDY(x) dfdy(x)\n" +
        "#define FWIDTH(x) fwidth(x)\n";

    // fs_main tanimini yeniden yazan makro: MSL'de doku/sampler global olamaz,
    // bu yuzden 'VEC4 fs_main(VEC2 uv, VEC4 color)' -> 'float4 fs_main_impl(...,
    // texture2d<float> tex, sampler smp[, float4 USER][, float2 LOCALUV])'. Cagri
    // suffix'te dogrudan fs_main_impl'e yapilir (makro cagri tarafinda tetiklenmez).
    static string FragMacroMsl(bool user, bool local)
        => "#define fs_main(a, b) fs_main_impl(a, b, texture2d<float> tex, sampler smp"
           + (user ? ", float4 USER" : "") + (local ? ", float2 LOCALUV" : "") + ")\n";

    static string FragSuffixMsl(bool user, bool local)
        => "struct fs_in {\n" +
           "  float2 v_uv    [[user(locn0)]];\n" +
           "  float4 v_color [[user(locn1)]];\n" +
           (user ? "  float4 v_user  [[user(locn2)]];\n" : "") +
           (local ? "  float2 v_uvLocal [[user(locn3)]];\n" : "") +
           "};\n" +
           "fragment float4 fs_main_entry(fs_in in [[stage_in]],\n" +
           "    texture2d<float> tex [[texture(0)]], sampler smp [[sampler(0)]]) {\n" +
           "  float4 c = fs_main_impl(in.v_uv, in.v_color, tex, smp"
           + (user ? ", in.v_user" : "") + (local ? ", in.v_uvLocal" : "") + ");\n" +
           "  return float4(c.rgb * c.a, c.a);\n" + // kanon: cikis premultiply
           "}\n";

    static readonly byte[] _vsEntryMsl = Ascii("vs_main");
    static readonly byte[] _fsEntryMsl = Ascii("fs_main_entry");

    // Fragment govdesinden tam shader kurar (ortak vertex + sarilmis fragment).
    // Govde USER iceriyorsa a_user attribute'lu vertex varyanti secilir.
    public static Shader CreateEffect(string fragmentBody) => CreateEffect(fragmentBody, localUv: false);

    // localUv: v_uvLocal varying'i (atlas-remap'siz ham kose uv'si) eklenir —
    // yalniz pixel-effect kompozisyonu ister; normal shader'lar interpolator odemez.
    internal static Shader CreateEffect(string fragmentBody, bool localUv)
    {
        bool usesUser = fragmentBody.Contains("USER");
        string vertexSource, fragmentSource;
        if (_metal)
        {
            vertexSource = _vertexTemplateMsl
                .Replace("{USER_IN}", usesUser ? "  float4 a_user [[attribute(12)]];\n" : "")
                .Replace("{USER_OUT}", usesUser ? "  float4 v_user [[user(locn2)]];\n" : "")
                .Replace("{LOCAL_OUT}", localUv ? "  float2 v_uvLocal [[user(locn3)]];\n" : "")
                .Replace("{USER_ASSIGN}", usesUser ? "  out.v_user = in.a_user;\n" : "")
                .Replace("{LOCAL_ASSIGN}", localUv ? "  out.v_uvLocal = in.a_uv;\n" : "");
            fragmentSource = _fragPrefixMsl
                + FragMacroMsl(usesUser, localUv)
                + fragmentBody + "\n"
                + FragSuffixMsl(usesUser, localUv);
        }
        else
        {
            vertexSource = _vertexTemplate
                .Replace("{USER_IN}", usesUser ? "in vec4 a_user;\n" : "")
                .Replace("{USER_OUT}", usesUser ? "out vec4 v_user;\n" : "")
                .Replace("{LOCAL_OUT}", localUv ? "out vec2 v_uvLocal;\n" : "")
                .Replace("{USER_ASSIGN}", usesUser ? "  v_user = a_user;\n" : "")
                .Replace("{LOCAL_ASSIGN}", localUv ? "  v_uvLocal = a_uv;\n" : "");
            fragmentSource = _fragPrefix
                + (usesUser ? _fragUserPrefix : "")
                + (localUv ? _fragLocalPrefix : "")
                + fragmentBody + _fragSuffix;
        }

        byte[] vs = Ascii(vertexSource);
        byte[] fs = Ascii(fragmentSource);

        Sokol.ShaderBegin();
        fixed (byte* p = vs) Sokol.ShaderVertexSource(p);
        fixed (byte* p = fs) Sokol.ShaderFragmentSource(p);
        // Metal entry fonksiyon adlari (GLSL'de "main" varsayilir, ad verilmez).
        if (_metal)
        {
            fixed (byte* p = _vsEntryMsl) Sokol.ShaderVertexEntry(p);
            fixed (byte* p = _fsEntryMsl) Sokol.ShaderFragmentEntry(p);
        }

        int attrCount = usesUser ? 13 : 12;
        for (int i = 0; i < attrCount; i++)
            fixed (byte* np = _attrNames[i])
                Sokol.ShaderAttr(i, np, SG.ShaderAttrBaseTypeFloat);

        // vertex uniform block 0: mat4 u_viewProj (std140).
        Sokol.ShaderUniformBlock(0, SG.ShaderStageVertex, 64, SG.UniformLayoutStd140);
        fixed (byte* np = _uViewProj)
            Sokol.ShaderUniform(0, 0, SG.UniformTypeMat4, np);

        // fragment doku view + sampler + eslesme ("tex").
        Sokol.ShaderTextureView(0, SG.ShaderStageFragment, SG.ImageType2D, SG.ImageSampleTypeFloat);
        Sokol.ShaderSampler(0, SG.ShaderStageFragment, SG.SamplerTypeFiltering);
        fixed (byte* np = _texName)
            Sokol.ShaderTextureSamplerPair(0, SG.ShaderStageFragment, 0, 0, np);

        return new Shader { Handle = Sokol.ShaderEnd(), UsesUser = usesUser, Body = fragmentBody };
    }

    // Dogrulamali kurulum: derleme basarisizsa handle yok edilir, null + hata doner.
    // (sg_make_shader abort ETMEZ, FAILED kaynak doner; pipeline'a bulasmadan yakala.)
    internal static Shader TryCreateEffect(string fragmentBody, bool localUv, out string error)
    {
        var sh = CreateEffect(fragmentBody, localUv);
        if (Sokol.ShaderValid(sh.Handle) != 0)
        {
            error = null;
            return sh;
        }
        Sokol.DestroyShader(sh.Handle);
        error = Marshal.PtrToStringAnsi(Sokol.LastError());
        if (string.IsNullOrEmpty(error))
            error = "shader derlenemedi (detay: crash_native.log)";
        return null;
    }

    // Pixel-effect zinciri kompozisyonu: core fs_main -> fs_core, her efekt govdesi
    // fx -> fx<i>, uretilen fs_main zinciri sirayla uygular. fx imzasi:
    // 'VEC4 fx(VEC4 c, VEC2 uv)' — c DUZ renk, uv LOKAL 0..1 (atlas-remap'siz).
    // Basarisizsa null doner (cagiran efektsiz core'a duser).
    internal static Shader ComposeEffects(string coreBody, string[] fxBodies, out string error)
    {
        if (string.IsNullOrEmpty(coreBody))
        {
            error = "core shader govdesi yok (compose edilemez)";
            return null;
        }
        var sb = new System.Text.StringBuilder(coreBody.Length + 256);
        string core = coreBody.Replace("fs_main", "fs_core");
        bool coreUser = core.Contains("USER");
        if (_metal)
        {
            sb.Append("#define fs_core(a, b) fs_core_impl(a, b, texture2d<float> tex, sampler smp")
              .Append(coreUser ? ", float4 USER" : "").Append(")\n");
            for (int i = 0; i < fxBodies.Length; i++)
                sb.Append("#define fx").Append(i).Append("(a, b) fx").Append(i)
                  .Append("_impl(a, b, texture2d<float> tex, sampler smp)\n");
            sb.Append(core).Append('\n');
            for (int i = 0; i < fxBodies.Length; i++)
                sb.Append(fxBodies[i].Replace("fx(", "fx" + i + "(")).Append('\n');
            sb.Append("VEC4 fs_main(VEC2 uv, VEC4 color) {\n")
              .Append("  VEC4 c = fs_core_impl(uv, color, tex, smp").Append(coreUser ? ", USER" : "").Append(");\n");
            for (int i = 0; i < fxBodies.Length; i++)
                sb.Append("  c = fx").Append(i).Append("_impl(c, LOCALUV, tex, smp);\n");
            sb.Append("  return c;\n}\n");
        }
        else
        {
            sb.Append(core).Append('\n');
            for (int i = 0; i < fxBodies.Length; i++)
                sb.Append(fxBodies[i].Replace("fx(", "fx" + i + "(")).Append('\n');
            sb.Append("VEC4 fs_main(VEC2 uv, VEC4 color) {\n  VEC4 c = fs_core(uv, color);\n");
            for (int i = 0; i < fxBodies.Length; i++)
                sb.Append("  c = fx").Append(i).Append("(c, LOCALUV);\n");
            sb.Append("  return c;\n}\n");
        }
        var sh = TryCreateEffect(sb.ToString(), localUv: true, out error);
        if (sh != null)
            sh.Body = null; // compose edilmis shader tekrar compose edilmez
        return sh;
    }

    static readonly byte[][] _attrNames =
    {
        Ascii("a_pos"), Ascii("a_uv"), Ascii("a_color"),
        Ascii("a_m0"), Ascii("a_m1"), Ascii("a_m2"), Ascii("a_m3"),
        Ascii("a_uvrect"),
        Ascii("a_tint0"), Ascii("a_tint1"), Ascii("a_tint2"), Ascii("a_tint3"),
        Ascii("a_user"),
    };
    static readonly byte[] _uViewProj = Ascii("u_viewProj");
    static readonly byte[] _texName = Ascii("tex");

    // GLSL kaynagi ASCII; null-sonlu byte dizisine cevir (Encoding bagimliligi yok).
    static byte[] Ascii(string s)
    {
        var bytes = new byte[s.Length + 1];
        for (int i = 0; i < s.Length; i++)
            bytes[i] = (byte)s[i];
        return bytes;
    }
}
