using System;

namespace DigitoyEngine;

public enum TextAlign { Left, Center, Right }

// Dunya vatandasi SDF metin (layout'suz, serbest transform): glyph'ler kutu
// parcalari gibi Mesh.Quad INSTANCED draw olarak emit edilir (glyph mesh'i YOK)
// — ayni font atlasi/shader'i kullanan kutu + metin draw'lari RenderQueue
// merge'uyle tek batch olur. Blok transform.position'da ORTALANIR (sprite
// paritesi). Size = bir satirin dunya birimi yuksekligi. Yerlesim yalniz
// metin/font/boyut/hiza degisince kurulur; stil (renk/kontur/golge) her frame
// instance verisinden okunur (rebuild yok).
public sealed unsafe class TextSprite : Renderer
{
    public Font Font;
    public string Text = "";
    public float Size = 32f;
    public TextAlign Align = TextAlign.Center;

    // Dolgu ve kontur dolgusu (Gradient: Solid tek renk / Linear dikey ust->alt).
    public Gradient Fill = new(Color.White);

    // Kontur (dunya birimi; SDF pad siniri ~6 sdf px * scale).
    public float OutlineWidth;
    public Gradient Outline = new(Color.Black);

    // Golge: a>0 acik; SDF smoothing ile yumusak kenar (RT/blur yok).
    public Color ShadowColor;
    public Vec2 ShadowOffset = new(0, 4);
    public float ShadowBlur = 8f;

    const float SdfSpread = 21f; // shim font bake sabiti (deger bandinin sdf-px genisligi)

    struct GlyphQuad
    {
        public float X0, Y0, X1, Y1; // lokal uzay (y-down, blok merkezi orijin)
        public float U0, V0, U1, V1; // uvRect parametreleri (V0 = alt kenar, V takasli)
    }

    GlyphQuad[] _quads;
    int _quadCount;
    Font _builtFont;
    string _builtText;
    float _builtSize;
    TextAlign _builtAlign;
    Vec2 _bounds; // kurulan blogun dunya boyutu (secim/braket)

    public override bool GetLocalSelectionBounds(out Vec2 center, out Vec2 halfSize)
    {
        center = default;
        halfSize = default;
        if (Font == null || string.IsNullOrEmpty(Text))
            return false;
        EnsureLayout();
        halfSize = new Vec2(_bounds.x * 0.5f, _bounds.y * 0.5f);
        return true;
    }

    internal override void Encode(RenderQueue queue)
    {
        if (Font == null || string.IsNullOrEmpty(Text))
            return;
        EnsureLayout();
        if (_quadCount == 0)
            return;
        Mat4 wm = transform._getWorldMatrix();
        var mat = Font.Material;
        int layer = SortingOrder;
        float scale = Font.SdfSize > 0 ? Size / Font.SdfSize : 1f;
        float halfH = _bounds.y * 0.5f;

        // Kontur kenar merkezi 0.5'ten disari kayar (SDF deger uzayinda).
        float cOut = 0f;
        if (OutlineWidth > 0f && scale > 0f)
            cOut = MathF.Max(0.5f - OutlineWidth / scale / SdfSpread, 0.05f);

        // Painter sirasi ayni layer'da submit sirasi: golge -> kontur -> dolgu.
        if (ShadowColor.a > 0)
        {
            float s = ShadowBlur > 0f && scale > 0f
                ? ShadowBlur * 0.5f / scale / SdfSpread : 0f;
            var user = new Vec4(s, cOut, 0f, 0f); // kontur varsa golge silueti de kontur kenarindan
            for (int i = 0; i < _quadCount; i++)
                Emit(queue, mat, in wm, in _quads[i], ShadowOffset.x, ShadowOffset.y,
                    ShadowColor, ShadowColor, in user, layer);
        }
        if (cOut > 0f)
        {
            var user = new Vec4(0f, cOut, 0f, 0f);
            for (int i = 0; i < _quadCount; i++)
            {
                ref readonly var g = ref _quads[i];
                Color cT = GradAt(in Outline, g.Y0, halfH);
                Color cB = GradAt(in Outline, g.Y1, halfH);
                Emit(queue, mat, in wm, in g, 0f, 0f, cT, cB, in user, layer);
            }
        }
        for (int i = 0; i < _quadCount; i++)
        {
            ref readonly var g = ref _quads[i];
            Color cT = GradAt(in Fill, g.Y0, halfH);
            Color cB = GradAt(in Fill, g.Y1, halfH);
            Emit(queue, mat, in wm, in g, 0f, 0f, cT, cB, default, layer);
        }
    }

    // Lokal glyph dikdortgeni world matrisiyle tek instanced quad'a cevrilir
    // (LayoutBox.EmitQuad deseni). Tint koseleri uv uzayinda: ust = uv.y=1.
    static void Emit(RenderQueue q, Material mat, in Mat4 world, in GlyphQuad g,
        float ox, float oy, Color cTop, Color cBottom, in Vec4 user, int layer)
    {
        Mat4 m = world;
        float cx = (g.X0 + g.X1) * 0.5f + ox, cy = (g.Y0 + g.Y1) * 0.5f + oy;
        float sx = g.X1 - g.X0, sy = g.Y1 - g.Y0;
        m.m[12] += m.m[0] * cx + m.m[4] * cy;
        m.m[13] += m.m[1] * cx + m.m[5] * cy;
        m.m[14] += m.m[2] * cx + m.m[6] * cy;
        m.m[0] *= sx; m.m[1] *= sx; m.m[2] *= sx;
        m.m[4] *= sy; m.m[5] *= sy; m.m[6] *= sy;
        q.DrawMesh(Mesh.Quad(), mat, in m, cBottom, cBottom, cTop, cTop, in user,
            g.U0, g.V0, g.U1, g.V1, layer);
    }

    // Dikey lineer gradient: blok ustu t=0, altta t=1 (lokal y-down uzay).
    static Color GradAt(in Gradient g, float y, float halfH)
        => g.At(halfH > 0f ? (y + halfH) / (halfH * 2f) : 0f);

    void EnsureLayout()
    {
        if (_quads != null && ReferenceEquals(_builtFont, Font) && _builtText == Text
            && _builtSize == Size && _builtAlign == Align)
            return;
        BuildQuads();
        _builtFont = Font;
        _builtText = Text;
        _builtSize = Size;
        _builtAlign = Align;
    }

    void BuildQuads()
    {
        var font = Font;
        string text = Text;
        float scale = font.SdfSize > 0 ? Size / font.SdfSize : 1f;

        // Satir olcumleri (blok genisligi = en genis satir).
        int lineCount = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n')
                lineCount++;
        Span<float> lineW = lineCount <= 64 ? stackalloc float[lineCount] : new float[lineCount];
        float maxW = 0;
        {
            int line = 0, start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i != text.Length && text[i] != '\n')
                    continue;
                float w = font.MeasureLine(text.AsSpan(start, i - start)) * scale;
                lineW[line++] = w;
                if (w > maxW)
                    maxW = w;
                start = i + 1;
            }
        }
        float blockH = lineCount * font.LineHeight * scale;
        _bounds = new Vec2(maxW, blockH);

        int glyphCap = text.Length;
        if (_quads == null || _quads.Length < glyphCap)
            _quads = new GlyphQuad[glyphCap];
        int q = 0;

        // y-down dunya (SpriteRenderer paritesi): blok merkezi orijinde.
        float top = -blockH * 0.5f;
        float invAtlas = 1f / font.AtlasSize;
        int curLine = 0, prev = -1;
        float penX = LineStartX(lineW[0], maxW);
        float baseY = top + font.Ascent * scale;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                curLine++;
                penX = LineStartX(lineW[curLine], maxW);
                baseY += font.LineHeight * scale;
                prev = -1;
                continue;
            }
            int gi = font.GlyphIndex(c);
            if (prev >= 0)
                penX += font.Kerning(prev, gi) * scale;
            ref readonly var g = ref font.GlyphAt(gi);
            if (g.W > 0 && g.H > 0)
            {
                // V takasi: CPU atlasinda satir 0 ustte (GuiRenderer SDF yoluyla ayni).
                _quads[q++] = new GlyphQuad
                {
                    X0 = penX + g.XOff * scale,
                    Y0 = baseY + g.YOff * scale,
                    X1 = penX + (g.XOff + g.W) * scale,
                    Y1 = baseY + (g.YOff + g.H) * scale,
                    U0 = g.AtlasX * invAtlas,
                    V0 = (g.AtlasY + g.H) * invAtlas,
                    U1 = (g.AtlasX + g.W) * invAtlas,
                    V1 = g.AtlasY * invAtlas,
                };
            }
            penX += g.Advance * scale;
            prev = gi;
        }
        _quadCount = q;
    }

    float LineStartX(float w, float maxW) => Align switch
    {
        TextAlign.Left => -maxW * 0.5f,
        TextAlign.Right => maxW * 0.5f - w,
        _ => -w * 0.5f,
    };
}
