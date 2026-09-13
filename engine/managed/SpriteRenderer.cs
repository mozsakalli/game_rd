namespace DigitoyEngine;

// Cizen componentlerin tabani: enable/disable Scene renderer listesine kayit demek.
public abstract class Renderer : Component
{
    internal int _rendererSlot = -1;
    public int SortingOrder;

    internal abstract void Encode(RenderQueue queue);

    // Editor secim/odak icin TRANSFORM-LOKAL bbox: merkez + yari boyut.
    // false = bu renderer tiklanamaz/olcusuz (editor onu atlar).
    public virtual bool GetLocalSelectionBounds(out Vec2 center, out Vec2 halfSize)
    {
        center = default;
        halfSize = default;
        return false;
    }
}

// Unity SpriteRenderer karsiligi (quad + sprite). Boyut dunya biriminde
// (Width/Height); transform scale ile carpilir. Material null ise paylasilan
// varsayilan materyal kullanilir (texture DrawMesh aninda yakalanir, guvenli).
public sealed unsafe class SpriteRenderer : Renderer
{
    public Sprite Sprite;
    public Material Material;
    public Color Color = new(255, 255, 255, 255);
    public float Width = 1f, Height = 1f;
    public float U0, V0, U1 = 1f, V1 = 1f;

    static Material _shared;
    static Material Shared => _shared ??= new Material();

    static Texture _white;
    // Texture atanmadiysa beyaz quad (deserialize sonrasi asset yokken de gorunur).
    static Texture White => _white ??= MakeWhite();

    static Texture MakeWhite()
    {
        var t = Texture.FromColor(1, 1, Color.White);
        t.Persistent = true;
        return t;
    }

    // Sprite'in mantiksal piksel boyutunu dunya boyutu yap (Unity native size).
    public void SetNativeSize()
    {
        if (Sprite == null || Sprite.LogicalWidth <= 0)
            return;
        Width = Sprite.LogicalWidth;
        Height = Sprite.LogicalHeight;
    }

    public override bool GetLocalSelectionBounds(out Vec2 center, out Vec2 halfSize)
    {
        center = default;
        halfSize = new Vec2(Width * 0.5f, Height * 0.5f);
        return true;
    }

    internal override void Encode(RenderQueue queue)
    {
        // Sprite = tek bolge primitifi: atlas uyeligi builder'in Sprite'i yerinde
        // baglamasiyla gelir — burada lookup yok, alan okunur. Ozel Material'de
        // doku secimi kullanicinin (sprite bolgesi uygulanmaz, U0..V1 gecerli).
        var spr = Sprite;
        var mat = Material ?? Shared;
        if (Material == null)
            mat.MainTexture = spr?.Page ?? White;

        // Width/Height dünya matrisinin basis kolonlarina bake edilir (ek mat mul yok).
        Mat4 model = transform._getWorldMatrix();
        if (Material == null && spr != null && spr.IsRegion)
        {
            // Mantiksal quad OrigW x OrigH'yi temsil eder; kirpilmis parca once
            // yerel uzayda otelenip (trim ofseti) sonra parca boyutuna olceklenir.
            float sx = Width * (spr.W / (float)spr.OrigW);
            float sy = Height * (spr.H / (float)spr.OrigH);
            float cx = Width * ((spr.OffX + spr.W * 0.5f) / spr.OrigW - 0.5f);
            float cy = Height * (0.5f - (spr.OffY + spr.H * 0.5f) / spr.OrigH);
            model.m[12] += model.m[0] * cx + model.m[4] * cy;
            model.m[13] += model.m[1] * cx + model.m[5] * cy;
            model.m[14] += model.m[2] * cx + model.m[6] * cy;
            model.m[0] *= sx; model.m[1] *= sx; model.m[2] *= sx;
            model.m[4] *= sy; model.m[5] *= sy; model.m[6] *= sy;
            float aw = spr.Page.Width, ah = spr.Page.Height;
            queue.DrawMesh(Mesh.Quad(), mat, in model, Color,
                spr.X / aw, spr.Y / ah,
                (spr.X + spr.W) / aw, (spr.Y + spr.H) / ah, SortingOrder);
            return;
        }
        if (Width != 1f)
        {
            model.m[0] *= Width; model.m[1] *= Width; model.m[2] *= Width;
        }
        if (Height != 1f)
        {
            model.m[4] *= Height; model.m[5] *= Height; model.m[6] *= Height;
        }
        queue.DrawMesh(Mesh.Quad(), mat, in model, Color, U0, V0, U1, V1, SortingOrder);
    }
}
