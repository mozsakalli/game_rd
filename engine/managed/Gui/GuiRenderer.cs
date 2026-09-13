using System;

namespace DigitoyEngine;

// GUI cizim koprusu: widget'lar lokal rect verir, burasi GuiClip ile global'e
// cevirip fiziksel clip'e KIRPAR (duz renkli quad'larda scissor'a gerek yok —
// rect kesisimi ayni sonucu verir) ve aktif kamera kuyruguna instanced quad
// olarak yazar. Draw list yok; mevcut RenderQueue/sort-key altyapisi kullanilir.
public static unsafe class GuiRenderer
{
    // Frame basinda host atar: GUI'nin cizilecegi kamera kuyrugu.
    public static RenderQueue Queue;

    // GUI quad'lari bu layer'dan baslar (oyun iceriginin ustunde, SortMode.Ui).
    public static int BaseLayer = 1000;

    static Material _material;
    static Mesh _quad;

    static void EnsureResources()
    {
        if (_material != null)
            return;
        _quad = Mesh.Quad();
        _material = new Material
        {
            MainTexture = Texture.FromColor(1, 1, new Color(255, 255, 255, 255)),
            SortMode = SortMode.Ui,
        };
    }

    // Lokal uzayda duz renkli rect. layerOffset ayni widget icinde katmanlama
    // icindir (arka plan 0, thumb +1 gibi).
    public static void DrawRect(in Rect localRect, Color color, int layerOffset = 0)
    {
        if (Queue == null || color.a == 0)
            return;
        EnsureResources();
        DrawClipped(localRect, _material, color, 0, 0, 1, 1, layerOffset);
    }

    // Klip'e kirpilmis dokulu quad: rect kismen tasarsa UV de orantili kirpilir
    // (metin glyph'leri panel kenarinda dogru kesilsin).
    static void DrawClipped(in Rect localRect, Material material, Color color,
        float u0, float v0, float u1, float v1, int layerOffset)
    {
        Rect g = GuiClip.Unclip(localRect);
        Rect c = Rect.Intersect(g, GuiClip.Physical);
        if (c.width <= 0 || c.height <= 0)
            return;
        if (c.x != g.x || c.y != g.y || c.width != g.width || c.height != g.height)
        {
            float du = (u1 - u0) / g.width, dv = (v1 - v0) / g.height;
            u0 += (c.x - g.x) * du;
            v0 += (c.y - g.y) * dv;
            u1 = u0 + c.width * du;
            v1 = v0 + c.height * dv;
        }

        Mat4 model = default;
        model.m[0] = c.width;
        model.m[5] = c.height;
        model.m[10] = 1f;
        model.m[12] = c.x + c.width * 0.5f;
        model.m[13] = c.y + c.height * 0.5f;
        model.m[15] = 1f;
        Queue.DrawMesh(_quad, material, in model, color, u0, v0, u1, v1, BaseLayer + layerOffset);
    }

    // Metal/D3D framebuffer orijini sol-UST, GL sol-ALT. Offscreen RT'ye cizip
    // sonra ornekleyince Metal'de goruntu dikey TERS gelir; RT dokularinda V'yi
    // cevir. Normal (CPU-yuklu) dokular stbi flip ile zaten dogru. Backend
    // derleme zamani secilir (DE_RENDERER_METAL / DE_RENDERER_OPENGL).
#if DE_RENDERER_METAL
    static readonly bool _flipRenderTargets = true;
#else
    static readonly bool _flipRenderTargets = false;
#endif

    // Dokulu rect (RT onizleme vb.). Tek materyal mutate edilir — TexView
    // DrawMesh aninda yakalandigi icin frame icinde coklu doku guvenlidir.
    static Material _texMaterial;

    public static void DrawTexture(in Rect localRect, Texture texture, int layerOffset = 1)
    {
        if (Queue == null || texture == null)
            return;
        EnsureResources();
        _texMaterial ??= new Material
        {
            SrcBlend = BlendFactor.One,
            DstBlend = BlendFactor.Zero,
            SortMode = SortMode.Ui,
        };
        _texMaterial.MainTexture = texture;
        bool flip = _flipRenderTargets && texture.IsRenderTarget;
        DrawClipped(localRect, _texMaterial, new Color(255, 255, 255, 255),
            0, flip ? 1 : 0, 1, flip ? 0 : 1, layerOffset);
    }

    // Alpha-blend'li dokulu rect (saydam RT ortuleri: Duzenle modu kompoziti).
    static Material _texBlendMaterial;

    public static void DrawTextureBlended(in Rect localRect, Texture texture, int layerOffset = 1)
    {
        if (Queue == null || texture == null)
            return;
        EnsureResources();
        _texBlendMaterial ??= new Material
        {
            SrcBlend = BlendFactor.SrcAlpha,
            DstBlend = BlendFactor.OneMinusSrcAlpha,
            SortMode = SortMode.Ui,
        };
        _texBlendMaterial.MainTexture = texture;
        bool flip = _flipRenderTargets && texture.IsRenderTarget;
        DrawClipped(localRect, _texBlendMaterial, new Color(255, 255, 255, 255),
            0, flip ? 1 : 0, 1, flip ? 0 : 1, layerOffset);
    }

    // --- SDF text ---

    // Eski imgui_font.c SDF efekti; R8 atlas oldugu icin .r kanalindan okur.
    const string _sdfFragment =
        "VEC4 fs_main(VEC2 uv, VEC4 color) {\n" +
        "  FLOAT distance = SAMPLE(tex, uv).r;\n" +
        "  FLOAT smoothing = FWIDTH(distance);\n" +
        "  FLOAT alpha = smoothstep(0.5 - smoothing, 0.5 + smoothing, distance);\n" +
        "  FLOAT outAlpha = alpha * color.a;\n" +
        "  return VEC4(color.rgb * outAlpha, outAlpha);\n}";

    static Shader _sdfShader;

    static Material FontMaterial(GuiFont font)
    {
        if (font.Material != null)
            return font.Material;
        _sdfShader ??= Shader.CreateEffect(_sdfFragment);
        font.Material = new Material
        {
            MainTexture = font.Texture,
            Shader = _sdfShader,
            SrcBlend = BlendFactor.One, // premultiplied (shader rgb*alpha yazar)
            DstBlend = BlendFactor.OneMinusSrcAlpha,
            SortMode = SortMode.Ui,
        };
        return font.Material;
    }

    // --- UI text: RASTER yol (fontstash modeli) ---
    // SDF kucuk puntoda minify + smoothstep yuzunden camurlasir. UI metni gercek
    // fiziksel piksel boyutunda rasterize edilir ve TAM SAYI fiziksel piksele
    // oturtulur -> cam gibi. SDF shader'i dunya/olcekli metin icin duruyor.

    const string _rasterFragment =
        "VEC4 fs_main(VEC2 uv, VEC4 color) {\n" +
        "  FLOAT a = SAMPLE(tex, uv).r * color.a;\n" +
        "  return VEC4(color.rgb * a, a);\n}";

    static Shader _rasterShader;

    static Material RasterFontMaterial(GuiFont font)
    {
        if (font.RasterMaterial != null)
            return font.RasterMaterial;
        _rasterShader ??= Shader.CreateEffect(_rasterFragment);
        font.RasterMaterial = new Material
        {
            MainTexture = font.RasterTexture,
            Shader = _rasterShader,
            SrcBlend = BlendFactor.One, // premultiplied
            DstBlend = BlendFactor.OneMinusSrcAlpha,
            SortMode = SortMode.Ui,
        };
        return font.RasterMaterial;
    }

    // Tek/cok satirli metin; pos sol-ust kose (lokal uzay). Yalniz Repaint'te cagrilmali.
    public static void DrawText(Vec2 pos, ReadOnlySpan<char> text, float pixelHeight, Color color, int layerOffset = 2)
    {
        GuiFont font = Gui.Font;
        if (font == null || Queue == null)
            return;

        float s = Gui.Scale > 0 ? Gui.Scale : 1f;
        float invS = 1f / s;
        int px = GuiFont.PhysPx(pixelHeight);
        Vec2 vm = font.RasterVMetrics(px); // x=ascent, y=lineH (fiziksel, tamsayi)
        Material material = RasterFontMaterial(font);
        const float inv = 1f / GuiFont.RasterAtlasSize;

        // Snap GLOBAL fiziksel uzayda yapilir (klip ofseti kesirli olabilir),
        // sonra lokale geri cevrilir; DrawClipped ofseti yeniden ekler.
        Vec2 off = GuiClip.Unclip(default(Vec2));
        float startX = MathF.Round((pos.x + off.x) * s);
        float baseY = MathF.Round((pos.y + off.y) * s) + vm.x;
        float penX = startX;
        int line = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\n')
            {
                penX = startX;
                line++;
                continue;
            }
            GuiFont.RGlyph g = font.GetRasterGlyph(px, ch);
            if (g.W > 0 && g.H > 0)
            {
                float gx = penX + g.X0;                    // tamsayi fiziksel px
                float gy = baseY + line * vm.y + g.Y0;
                var rect = new Rect(
                    gx * invS - off.x, gy * invS - off.y,
                    g.W * invS, g.H * invS);
                // V takasi: CPU atlasinda satir 0 ustte (SDF yoluyla ayni duzen).
                DrawClipped(rect, material, color,
                    g.AtlasX * inv, (g.AtlasY + g.H) * inv,
                    (g.AtlasX + g.W) * inv, g.AtlasY * inv,
                    layerOffset);
            }
            penX += MathF.Round(g.Advance); // tamsayi ilerleme (TextSize ile birebir)
        }
    }

    // Rect icinde hizali tek satir metin (dikey ortali).
    public static void DrawTextIn(in Rect rect, ReadOnlySpan<char> text, float pixelHeight,
        Color color, bool centerX = false, int layerOffset = 2)
    {
        GuiFont font = Gui.Font;
        if (font == null)
            return;
        Vec2 size = font.TextSize(text, pixelHeight);
        float x = centerX ? rect.x + (rect.width - size.x) * 0.5f : rect.x + 4f;
        float y = rect.y + (rect.height - size.y) * 0.5f;
        DrawText(new Vec2(x, y), text, pixelHeight, color, layerOffset);
    }

    // 45 derece dondurulmus kare = elmas (timeline keyframe'i). Dondurulmus quad
    // rect-kirpmaya girmez; kaba cull: merkez klip disindaysa cizilmez.
    public static void DrawDiamond(Vec2 center, float half, Color color, int layerOffset = 2)
    {
        if (Queue == null || color.a == 0)
            return;
        EnsureResources();
        Vec2 g = GuiClip.Unclip(center);
        Rect phys = GuiClip.Physical;
        if (g.x < phys.x - half || g.x > phys.xMax + half || g.y < phys.y - half || g.y > phys.yMax + half)
            return;
        float s = half * 1.41421356f; // kare kenari: kosegen = 2*half
        const float c45 = 0.70710678f;
        Mat4 model = default;
        model.m[0] = c45 * s;
        model.m[1] = c45 * s;
        model.m[4] = -c45 * s;
        model.m[5] = c45 * s;
        model.m[10] = 1f;
        model.m[12] = g.x;
        model.m[13] = g.y;
        model.m[15] = 1f;
        Queue.DrawMesh(_quad, _material, in model, color, 0, 0, 1, 1, BaseLayer + layerOffset);
    }
}
