namespace DigitoyEngine;

// C '_engine_Shader' + engine_shader.c efekt DSL'i karsiligi (GLCORE 410).
// Ortak instanced vertex shader sabit; kullanicinin fragment govdesi
// ("VEC4 fs_main(VEC2 uv, VEC4 color) { ... }") prefix/suffix ile sarilip
// tam GLSL fragment kaynagina cevrilir ve sokol shader builder ile kurulur.
public sealed unsafe class Shader
{
    static uint _nextId = 1;
    static Shader _default;

    // Pipeline state key'inde kullanilan kararli kimlik (engine.c: shader->id).
    public uint Id { get; } = _nextId++;

    // sokol shader nesne id'si (0 = henuz olusturulmadi).
    internal uint Handle;

    // Fragment USER okuyor mu: a_user attribute'u yalniz o zaman shader'a girer
    // (GL derleyicisi kullanilmayan attribute'u budar -> sokol WARN spam'i olmasin).
    internal bool UsesUser;

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
        "void main() {\n" +
        "  mat4 model = mat4(a_m0, a_m1, a_m2, a_m3);\n" +
        "  gl_Position = u_viewProj * (model * vec4(a_pos, 1.0));\n" +
        "  vec2 uv = (a_uvrect.z == 0.0 && a_uvrect.w == 0.0) ? vec2(0.5, 0.5) : (a_uvrect.xy + a_uv * a_uvrect.zw);\n" +
        "  v_uv = uv;\n" +
        "  vec4 tint = mix(mix(a_tint0, a_tint1, a_uv.x), mix(a_tint3, a_tint2, a_uv.x), a_uv.y);\n" +
        "  v_color = a_color * tint;\n" +
        "{USER_ASSIGN}" +
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

    const string _fragSuffix =
        "\nvoid main() { frag_color = fs_main(v_uv, v_color); }\n";

    const string _defaultFragment =
        "VEC4 fs_main(VEC2 uv, VEC4 color) { return SAMPLE(tex, uv) * color; }";

    // Fragment govdesinden tam GLSL shader kurar (ortak vertex + sarilmis fragment).
    // Govde USER iceriyorsa a_user attribute'lu vertex varyanti secilir.
    public static Shader CreateEffect(string fragmentBody)
    {
        bool usesUser = fragmentBody.Contains("USER");
        string vertexSource = _vertexTemplate
            .Replace("{USER_IN}", usesUser ? "in vec4 a_user;\n" : "")
            .Replace("{USER_OUT}", usesUser ? "out vec4 v_user;\n" : "")
            .Replace("{USER_ASSIGN}", usesUser ? "  v_user = a_user;\n" : "");
        string fragmentSource = _fragPrefix + (usesUser ? _fragUserPrefix : "") + fragmentBody + _fragSuffix;

        byte[] vs = Ascii(vertexSource);
        byte[] fs = Ascii(fragmentSource);

        Sokol.ShaderBegin();
        fixed (byte* p = vs) Sokol.ShaderVertexSource(p);
        fixed (byte* p = fs) Sokol.ShaderFragmentSource(p);

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

        return new Shader { Handle = Sokol.ShaderEnd(), UsesUser = usesUser };
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
