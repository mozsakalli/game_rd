using System;

namespace DigitoyEngine;

public enum ProgressShape : byte { Bar, Radial }

// Bar dolum yonu. CenterOut: ortadan iki yana buyur.
public enum ProgressDirection : byte
{
    LeftToRight, RightToLeft, BottomToTop, TopToBottom, CenterOutHorizontal, CenterOutVertical,
}

// Prosedurel SDF progress bar / dairesel-eliptik sayac. Mesh/arc uretimi YOK: her
// katman tek Mesh.Quad instance'i, sekil ve dolum fragment'ta hesaplanir (FWIDTH
// AA, her olcekte keskin). Katmanlar (border -> track -> ghost -> fill -> edge)
// AYNI shader + beyaz dokuyla pes pese emit edilir -> RenderQueue tek instanced
// draw'a merge eder. Gorsel zenginlik: Gradient dolgular, segmentler, ghost
// (gecikmeli dusen ikinci bar), uc parlamasi ve Renderer.Effects (.fx) zinciri.
//
// Fragment sozlesmesi (her katman per-instance USER ile parametrelenir):
//   USER.x = aralik baslangici, USER.y = aralik uzunlugu (koordinat: bar'da eksen
//            boyunca 0..1, radial'da tepe/baslangic acidan tur (0..1, sarmali)).
//            USER.y >= 2: RING modu — aralik yok, sekil (USER.y-2)*kisaYariKenar
//            kadar ice ofsetli kopyasi cikarilarak cerceve bandi cizilir.
//   USER.z = +-(1 + oran): Bar: oran = kose yaricapi / (kisa kenar/2), isaret =
//            eksen (+ yatay, - dikey). Radial: oran = halka kalinligi / kisa yari
//            eksen (0 veya >=1 = tam dilim), isaret = yon (+ saat yonu, - tersi).
//   USER.w = +-(segment adedi + bosluk orani); 0 = segment yok. Isaret -: segment
//            sifiri aralik baslangicidir (radial), +: koordinat sifiri (bar).
// Quad en-boy orani FWIDTH turevlerinden cikarilir (CPU parametresi yok; parent
// non-uniform scale'i de dogru goturur).
public sealed unsafe class ProgressRenderer : Renderer
{
    public ProgressShape Shape;
    public float Value = 0.75f;
    public float Width = 200f, Height = 24f;

    [ShowIf(nameof(Shape), ProgressShape.Bar)] public ProgressDirection Direction;
    // Dunya birimi; 0 = keskin, kisa kenarin yarisi = kapsul.
    [ShowIf(nameof(Shape), ProgressShape.Bar)] public float CornerRadius = 12f;

    // Dunya birimi halka kalinligi; 0 = tam dilim (pie).
    [ShowIf(nameof(Shape), ProgressShape.Radial)] public float Thickness = 12f;
    // Derece, 0 = tepe (saat 12), saat yonunde artar.
    [ShowIf(nameof(Shape), ProgressShape.Radial)] public float StartAngle;
    [ShowIf(nameof(Shape), ProgressShape.Radial)] public bool Clockwise = true;

    // Dolgu ve zemin (Gradient Linear: tum bar boyunca; Radial tipi duz renge duser).
    public Gradient Fill = new(new Color(80, 200, 120, 255));
    public Gradient Track = new(new Color(0, 0, 0, 110));
    // Dolgunun zemine gore ic bosluğu (dunya birimi).
    public float Padding;

    // Cerceve: zemin seklinin disinda BorderWidth kalinliginda bant (0 = yok).
    public float BorderWidth;
    public Color BorderColor = new(255, 255, 255, 160);

    // Segmentler: 0 = surekli. SegmentGap = bir segment uzunlugunun bosluk orani.
    public int Segments;
    [ShowIf(nameof(Segments))] public float SegmentGap = 0.15f;

    // Ghost: deger dusunce eski seviyeden GhostSpeed (birim/sn) hiziyla asagi
    // kayan ikinci bar (hasar izi); 0 = kapali. Artista aninda yetisir.
    public float GhostSpeed;
    [ShowIf(nameof(GhostSpeed))] public Color GhostColor = new(255, 80, 60, 200);

    // Dolum ucunda EdgeSize (toplam uzunlugun orani) genisliginde parlama bandi; 0 = yok.
    public float EdgeSize;
    [ShowIf(nameof(EdgeSize))] public Color EdgeColor = new(255, 255, 255, 220);

    float _ghost = -1f;

    // Ghost'un anlik seviyesi (yalniz okunur; SnapGhost ile sifirlanir).
    public float GhostValue => _ghost < 0f ? Value : _ghost;

    public void SnapGhost() => _ghost = Value;

    public override bool GetLocalSelectionBounds(out Vec2 center, out Vec2 halfSize)
    {
        center = default;
        halfSize = new Vec2(Width * 0.5f, Height * 0.5f);
        return true;
    }

    protected internal override void OnValidate() => Value = Math.Clamp(Value, 0f, 1f);

    protected internal override void Update()
    {
        float v = Math.Clamp(Value, 0f, 1f);
        if (GhostSpeed <= 0f || _ghost < 0f || _ghost < v)
            _ghost = v;
        else if (_ghost > v)
            _ghost = MathF.Max(v, _ghost - GhostSpeed * Time.deltaTime);
    }

    // --- shader'lar / materyal ---

    const string _common =
        "FLOAT pr_range(FLOAT a, FLOAT s, FLOAT l, FLOAT fa) {\n" +
        "  return smoothstep(s - fa, s + fa, a) * (1.0 - smoothstep(s + l - fa, s + l + fa, a));\n}\n" +
        "FLOAT pr_segments(FLOAT a, FLOAT fa, FLOAT w) {\n" +
        "  FLOAT aw = abs(w);\n" +
        "  FLOAT n = floor(aw);\n" +
        "  if (n < 1.0) return 1.0;\n" +
        "  FLOAT g = (aw - n) * 0.5;\n" +
        "  FLOAT f = fract(a * n);\n" +
        "  FLOAT fn = fa * n;\n" +
        "  return smoothstep(g - fn, g + fn, f) * (1.0 - smoothstep(1.0 - g - fn, 1.0 - g + fn, f));\n}\n";

    // Tum FWIDTH'ler dallanmadan ONCE (turev tanimsizligi olmasin).
    public const string BarFragment = _common +
        "FLOAT pr_box(VEC2 p, VEC2 b, FLOAT r) {\n" +
        "  VEC2 q = abs(p) - b + VEC2(r, r);\n" +
        "  return length(max(q, VEC2(0.0, 0.0))) + min(max(q.x, q.y), 0.0) - r;\n}\n" +
        "VEC4 fs_main(VEC2 uv, VEC4 color) {\n" +
        "  FLOAT aspect = FWIDTH(uv.y) / max(FWIDTH(uv.x), 1e-6);\n" +
        "  VEC2 hs = VEC2(aspect * 0.5, 0.5);\n" +
        "  VEC2 p = (uv - VEC2(0.5, 0.5)) * VEC2(aspect, 1.0);\n" +
        "  FLOAT r = (abs(USER.z) - 1.0) * min(hs.x, hs.y);\n" +
        "  FLOAT d = pr_box(p, hs, r);\n" +
        "  FLOAT aa = FWIDTH(d);\n" +
        "  FLOAT a = USER.z > 0.0 ? uv.x : uv.y;\n" +
        "  FLOAT fa = FWIDTH(a);\n" +
        "  FLOAT m = 1.0 - smoothstep(-aa, aa, d);\n" +
        "  if (USER.y >= 2.0) {\n" +
        "    FLOAT b = (USER.y - 2.0) * min(hs.x, hs.y);\n" +
        "    m *= smoothstep(-aa, aa, d + b);\n" +
        "  } else {\n" +
        "    m *= pr_range(a, USER.x, USER.y, fa);\n" +
        "    m *= pr_segments(USER.w < 0.0 ? a - USER.x : a, fa, USER.w);\n" +
        "  }\n" +
        "  return VEC4(color.rgb, color.a * m);\n}";

    // Elips: gradient-duzeltmeli yaklasik mesafe (f/|grad f|). Ic sinir disin
    // SABIT ofsetlisi (es kalinlikta halka), benzer elips degil.
    public const string RadialFragment = _common +
        "FLOAT pr_ellipse(VEC2 p, VEC2 hs) {\n" +
        "  VEC2 q = p / hs;\n" +
        "  FLOAT lq = length(q);\n" +
        "  return (lq - 1.0) * lq / max(length(q / hs), 1e-6);\n}\n" +
        "VEC4 fs_main(VEC2 uv, VEC4 color) {\n" +
        "  FLOAT aspect = FWIDTH(uv.y) / max(FWIDTH(uv.x), 1e-6);\n" +
        "  FLOAT px = FWIDTH(uv.y);\n" +
        "  VEC2 hs = VEC2(aspect * 0.5, 0.5);\n" +
        "  VEC2 p = (uv - VEC2(0.5, 0.5)) * VEC2(aspect, 1.0);\n" +
        "  FLOAT mn = min(hs.x, hs.y);\n" +
        "  FLOAT t = abs(USER.z) - 1.0;\n" +
        "  FLOAT dOut = pr_ellipse(p, hs);\n" +
        "  FLOAT aaOut = FWIDTH(dOut);\n" +
        "  VEC2 hi = max(hs - VEC2(t * mn, t * mn), VEC2(1e-4, 1e-4));\n" +
        "  FLOAT dIn = pr_ellipse(p, hi);\n" +
        "  FLOAT aaIn = FWIDTH(dIn);\n" +
        "  bool ring = t > 0.0 && t < 1.0;\n" +
        "  FLOAT m = 1.0 - smoothstep(-aaOut, aaOut, dOut);\n" +
        "  if (ring) m *= smoothstep(-aaIn, aaIn, dIn);\n" +
        "  FLOAT turns = ATAN2(p.x, p.y) * 0.15915494;\n" +
        "  if (USER.z < 0.0) turns = -turns;\n" +
        "  FLOAT ar = fract(turns - USER.x);\n" +
        "  FLOAT fa = px / (6.2831853 * max(length(p), 1e-4));\n" +
        "  if (USER.y >= 2.0) {\n" +
        "    FLOAT b = (USER.y - 2.0) * mn;\n" +
        "    FLOAT mi = 1.0 - smoothstep(-aaOut, aaOut, dOut + b);\n" +
        "    if (ring) mi *= smoothstep(-aaIn, aaIn, dIn - b);\n" +
        "    m *= 1.0 - mi;\n" +
        "  } else {\n" +
        "    if (USER.y < 1.0) {\n" +
        "      FLOAT c = (USER.y + 1.0) * 0.5;\n" +
        "      FLOAT as = ar > c ? ar - 1.0 : ar;\n" +
        "      m *= pr_range(as, 0.0, USER.y, fa);\n" +
        "    }\n" +
        "    m *= pr_segments(ar, fa, USER.w);\n" +
        "  }\n" +
        "  return VEC4(color.rgb, color.a * m);\n}";

    static Shader _barShader, _radialShader;
    static Material _barMat, _radialMat;

    public static Shader BarShader => _barShader ??= Shader.CreateEffect(BarFragment);
    public static Shader RadialShader => _radialShader ??= Shader.CreateEffect(RadialFragment);

    static Material BarMaterial => _barMat ??= new Material
    {
        MainTexture = SpriteRenderer.White, Shader = BarShader, SortMode = SortMode.Transparent,
    };

    static Material RadialMaterial => _radialMat ??= new Material
    {
        MainTexture = SpriteRenderer.White, Shader = RadialShader, SortMode = SortMode.Transparent,
    };

    // --- encode ---

    RenderQueue _q;
    Material _mat;
    Mat4 _world;

    internal override void Encode(RenderQueue queue)
    {
        if (Width <= 0f || Height <= 0f)
            return;
        bool radial = Shape == ProgressShape.Radial;
        _q = queue;
        _mat = (radial ? RadialMaterial : BarMaterial).ForBlend(BlendMode).ForEffects(Effects);
        _world = transform._getWorldMatrix();

        float v = Math.Clamp(Value, 0f, 1f);
        float border = MathF.Max(0f, BorderWidth);
        float inner = border + MathF.Max(0f, Padding);

        if (border > 0f && BorderColor.a > 0)
            Layer(0f, 0f, 1f, BorderColor, BorderColor, BorderColor, BorderColor, LayerKind.Ring, border);
        if (Track.Visible)
            LayerGradient(border, 0f, 1f, in Track, LayerKind.Full);
        float ghost = GhostValue;
        if (GhostSpeed > 0f && GhostColor.a > 0 && ghost > v + 1e-4f)
            Layer(inner, 0f, ghost, GhostColor, GhostColor, GhostColor, GhostColor, LayerKind.Range);
        if (v > 0f && Fill.Visible)
            LayerGradient(inner, 0f, v, in Fill, LayerKind.Range);
        if (v > 0f && EdgeSize > 0f && EdgeColor.a > 0)
            Layer(inner, MathF.Max(0f, v - EdgeSize), v, EdgeColor, EdgeColor, EdgeColor, EdgeColor, LayerKind.Range);

        _q = null;
        _mat = null;
    }

    void LayerGradient(float inset, float u0, float u1, in Gradient g, LayerKind kind)
    {
        if (g.type != GradientType.Linear)
        {
            Layer(inset, u0, u1, g.color, g.color, g.color, g.color, kind);
            return;
        }
        // kose sirasi uv uzayinda 0=BL 1=BR 2=TR 3=TL (ust kenar = uv.y=1)
        if (g.direction == GradientDirection.Horizontal)
            Layer(inset, u0, u1, g.color, g.color2, g.color2, g.color, kind);
        else
            Layer(inset, u0, u1, g.color2, g.color2, g.color, g.color, kind);
    }

    // Range: ilerleme uzayindaki [u0,u1] (0 = bos uc, 1 = dolu uc). Full: tum sekil.
    // Ring: tum sekil eksi ringWidth (dunya birimi) ice ofsetli kopyasi (cerceve).
    enum LayerKind : byte { Range, Full, Ring }

    // Ilerleme araligini yon/sekle gore koordinat araligina cevirip emit eder.
    void Layer(float inset, float u0, float u1, Color c0, Color c1, Color c2, Color c3, LayerKind kind, float ringWidth = 0f)
    {
        float w = Width - inset * 2f, h = Height - inset * 2f;
        if (w <= 0f || h <= 0f)
            return;
        float minHalf = MathF.Min(w, h) * 0.5f;
        float segs = SegmentW();
        // USER.y >= 2 shader'da ring modu; oran kisa yari kenara gore.
        float ringLen = 2f + Math.Clamp(ringWidth / minHalf, 0f, 1f);

        if (Shape == ProgressShape.Radial)
        {
            float th = Thickness > 0f ? Thickness - inset * 2f : 0f;
            if (Thickness > 0f && th <= 0f)
                return; // halka bu katmanda kapandi
            float frac = th <= 0f || th >= minHalf ? 0f : th / minHalf;
            float z = (1f + frac) * (Clockwise ? 1f : -1f);
            float start = StartAngle / 360f * (Clockwise ? 1f : -1f);
            switch (kind)
            {
                case LayerKind.Ring: Emit(w, h, start, ringLen, z, 0f, c0, c1, c2, c3); break;
                case LayerKind.Full: Emit(w, h, start, 1f, z, -segs, c0, c1, c2, c3); break;
                default: Emit(w, h, start + u0, u1 - u0, z, -segs, c0, c1, c2, c3); break;
            }
            return;
        }

        float rfrac = Math.Clamp(MathF.Max(0f, CornerRadius - inset) / minHalf, 0f, 1f);
        bool vertical = Direction == ProgressDirection.BottomToTop
                     || Direction == ProgressDirection.TopToBottom
                     || Direction == ProgressDirection.CenterOutVertical;
        float zb = (1f + rfrac) * (vertical ? -1f : 1f);
        if (kind == LayerKind.Ring)
        {
            Emit(w, h, 0f, ringLen, zb, 0f, c0, c1, c2, c3);
            return;
        }
        if (kind == LayerKind.Full)
        {
            Emit(w, h, -0.5f, FullLen, zb, segs, c0, c1, c2, c3);
            return;
        }
        switch (Direction)
        {
            case ProgressDirection.RightToLeft:
            case ProgressDirection.TopToBottom:
                EmitBar(w, h, 1f - u1, 1f - u0, zb, segs, c0, c1, c2, c3);
                break;
            case ProgressDirection.CenterOutHorizontal:
            case ProgressDirection.CenterOutVertical:
                if (u0 <= 0f)
                    EmitBar(w, h, 0.5f - u1 * 0.5f, 0.5f + u1 * 0.5f, zb, segs, c0, c1, c2, c3);
                else
                {
                    EmitBar(w, h, 0.5f - u1 * 0.5f, 0.5f - u0 * 0.5f, zb, segs, c0, c1, c2, c3);
                    EmitBar(w, h, 0.5f + u0 * 0.5f, 0.5f + u1 * 0.5f, zb, segs, c0, c1, c2, c3);
                }
                break;
            default:
                EmitBar(w, h, u0, u1, zb, segs, c0, c1, c2, c3);
                break;
        }
    }

    // Sekil kenarina dayanan aralik uclari disari tasirilir: aralik AA'si sekil
    // AA'siyla ust uste binip kenar pikselini soldurmasin. Uzunluk 2'nin altinda
    // kalmali (>= 2 shader'da ring modu).
    const float FullLen = 1.9f;

    void EmitBar(float w, float h, float s, float e, float z, float segs,
        Color c0, Color c1, Color c2, Color c3)
    {
        if (e - s <= 0f)
            return;
        if (s <= 1e-4f) s = -0.5f;
        if (e >= 1f - 1e-4f) e = 1.4f;
        Emit(w, h, s, e - s, z, segs, c0, c1, c2, c3);
    }

    void Emit(float w, float h, float start, float len, float z, float segs,
        Color c0, Color c1, Color c2, Color c3)
    {
        Mat4 m = _world;
        m.m[0] *= w; m.m[1] *= w; m.m[2] *= w;
        m.m[4] *= h; m.m[5] *= h; m.m[6] *= h;
        var user = new Vec4(start, len, z, segs);
        _q.DrawMesh(Mesh.Quad(), _mat, in m, c0, c1, c2, c3, in user, in FxParams, 0f, 0f, 1f, 1f, _effectiveOrder);
    }

    // Bosluksuz segment gorunmez (sadece AA dikisi birakir) -> kapali say.
    float SegmentW()
    {
        if (Segments <= 0 || SegmentGap <= 0f)
            return 0f;
        return Segments + Math.Clamp(SegmentGap, 0.01f, 0.95f);
    }
}
