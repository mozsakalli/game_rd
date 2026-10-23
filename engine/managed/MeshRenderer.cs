namespace DigitoyEngine;

// Unity MeshRenderer+MeshFilter karsiligi (tek component): Mesh asset (DMSH) + Sprite.
// Isiksiz: doku x renk (varsayilan sprite shader'i) — motor vertex duzeni pos/uv/color,
// normal yok; golge/isik dokuya pisirilir. Doku kavrami motor geneliyle AYNI: Sprite.
// Atlas uyesi sprite'ta mesh UV'leri (0..1, orijinal gorsel) instance uvRect ile bolgeye
// haritalanir (shader zaten yapar: uv = uvRect.xy + a_uv * uvRect.zw) — ek maliyet yok;
// trim'li bolgede kirpilan kisim bolge disina tasar (3D dokular opak/kenarsiz olmali).
// Materyal state'i alanlardan her Encode'da derlenir (kendi Material instance'i).
// SortMode varsayilani Transparent: diger renderer'larla ayni katman/SortingOrder sirasina
// girer (Opaque kovasi her seyden ONCE cizilir, BG sprite'i ustunu orterdi); depth test/write
// mesh'in kendi icinde yine calisir. Opaque = ayri 3D kamerada cok mesh (batch dostu) icin.
[AddComponentMenu("Rendering/Mesh Renderer")]
public sealed class MeshRenderer : Renderer
{
    public Mesh Mesh;
    public Sprite Sprite;
    public Color Color = new(255, 255, 255, 255);
    public CullMode CullMode = CullMode.Back;
    public bool DepthTest = true;
    public bool DepthWrite = true;
    // Opaque: One/Zero blend, state gruplu sira. Transparent: BlendMode uygulanir, layer sirali.
    public SortMode SortMode = SortMode.Transparent;
    public SamplerType Sampler = SamplerType.Linear;

    Material _mat;

    internal override void Encode(RenderQueue queue)
    {
        var mesh = Mesh;
        if (mesh == null || mesh.IndexCount == 0)
            return;
        _mat ??= new Material();
        var spr = Sprite;
        var page = spr?.Page ?? SpriteRenderer.White;
        float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;
        bool region = spr != null && spr.IsRegion;
        if (region)
        {
            // Mesh u (orijinal gorsel 0..1) -> bolge: piksel u*OrigW, bolgeye goreli (.. - OffX)/W.
            float su = (spr.U1 - spr.U0) / spr.W, sv = (spr.V1 - spr.V0) / spr.H;
            u0 = spr.U0 - spr.OffX * su; u1 = u0 + spr.OrigW * su;
            v0 = spr.V0 - spr.OffY * sv; v1 = v0 + spr.OrigH * sv;
        }
        else if (Sampler == SamplerType.Trilinear && spr?.Page != null)
            spr.Page.RequestMipmaps(); // yalniz tam-sayfa doku: atlas sayfasinda mip = bolgeler arasi bleed (ucuz no-op)
        _mat.MainTexture = page;
        Material mat;
        if (SortMode == SortMode.Opaque)
        {
            _mat.SrcBlend = BlendFactor.One;
            _mat.DstBlend = BlendFactor.Zero;
            mat = _mat.ForEffects(Effects);
        }
        else
        {
            _mat.SrcBlend = BlendFactor.One;
            _mat.DstBlend = BlendFactor.OneMinusSrcAlpha;
            mat = _mat.ForBlend(BlendMode).ForEffects(Effects);
        }
        // Varyantlar (blend/fx) state'i olusturulurken kopyalar; alanlar sonradan degisebilir
        // (inspector) -> nihai materyale her Encode'da basilir (varyant bu renderer'a ozel).
        mat.CullMode = CullMode;
        mat.DepthTest = DepthTest;
        mat.DepthWrite = DepthWrite;
        mat.SortMode = SortMode;
        // Atlas uyesinde Trilinear -> Linear (sayfaya mip zinciri istenmez).
        mat.SamplerType = region && Sampler == SamplerType.Trilinear ? SamplerType.Linear : Sampler;
        ref var model = ref transform._getWorldMatrix();
        queue.DrawMesh(mesh, mat, in model, Color, Color, Color, Color, default, in FxParams, u0, v0, u1, v1, _effectiveOrder);
    }

    // Editor cerceve/odak: AABB'nin xy izdusumu.
    public override bool GetLocalSelectionBounds(out Vec2 center, out Vec2 halfSize)
    {
        center = default;
        halfSize = default;
        var m = Mesh;
        if (m == null || !m.HasBounds)
            return false;
        center = new Vec2((m.BoundsMin.x + m.BoundsMax.x) * 0.5f, (m.BoundsMin.y + m.BoundsMax.y) * 0.5f);
        halfSize = new Vec2((m.BoundsMax.x - m.BoundsMin.x) * 0.5f, (m.BoundsMax.y - m.BoundsMin.y) * 0.5f);
        return true;
    }

    public override bool GetLocalBounds3D(out Vec3 min, out Vec3 max)
    {
        var m = Mesh;
        if (m == null || !m.HasBounds)
        {
            min = max = default;
            return false;
        }
        min = m.BoundsMin;
        max = m.BoundsMax;
        return true;
    }

    // Picking: lokal AABB ile isin kesisimi (z=0 duzlemi varsayimi 3D'de gecersiz).
    public override bool HitTest(in Ray ray, out float distance, out Vec3 point)
    {
        distance = 0;
        point = default;
        var m = Mesh;
        if (m == null || !m.HasBounds)
            return false;
        ref var world = ref transform._getWorldMatrix();
        if (!Mat4.Inverse(ref world, out var inv))
            return false;
        var o = Mat4.TransformPoint(ref inv, ray.Origin);
        var d = Mat4.TransformVector(ref inv, ray.Dir);
        if (!m.RayIntersectsBounds(in o, in d, out float t))
            return false;
        var lp = o + d * t;
        point = Mat4.TransformPoint(ref world, lp);
        distance = Vec3.Distance(ray.Origin, point);
        return true;
    }
}
