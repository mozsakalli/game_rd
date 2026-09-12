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

// Unity SpriteRenderer karsiligi (quad + texture). Boyut dunya biriminde
// (Width/Height); transform scale ile carpilir. Material null ise paylasilan
// varsayilan materyal kullanilir (texture DrawMesh aninda yakalanir, guvenli).
public sealed unsafe class SpriteRenderer : Renderer
{
    public Texture Texture;
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

    // Texture piksel boyutunu dunya boyutu yap (Unity sprite native size).
    public void SetNativeSize()
    {
        if (Texture == null)
            return;
        Width = Texture.Width;
        Height = Texture.Height;
    }

    public override bool GetLocalSelectionBounds(out Vec2 center, out Vec2 halfSize)
    {
        center = default;
        halfSize = new Vec2(Width * 0.5f, Height * 0.5f);
        return true;
    }

    internal override void Encode(RenderQueue queue)
    {
        // Atlas uyeligi: kaynak png bir atlasa girdiyse HER modda atlastan cizilir
        // (davranis paritesi — drawcall/bleeding editorde de gercek). Ozel Material
        // kullanan renderer atlasa sokulmaz (doku secimi kullanicinin).
        var rec = Material == null && Texture != null ? SpriteTable.Resolve(Texture.Name) : null;

        var mat = Material ?? Shared;
        if (Material == null)
            mat.MainTexture = rec?.Atlas ?? Texture ?? White;

        // Width/Height dünya matrisinin basis kolonlarina bake edilir (ek mat mul yok).
        Mat4 model = transform._getWorldMatrix();
        if (rec?.Atlas != null)
        {
            // Mantiksal quad OrigW x OrigH'yi temsil eder; kirpilmis parca once
            // yerel uzayda otelenip (trim ofseti) sonra parca boyutuna olceklenir.
            float sx = Width * (rec.DrawW / (float)rec.OrigW);
            float sy = Height * (rec.DrawH / (float)rec.OrigH);
            float cx = Width * ((rec.OffX + rec.DrawW * 0.5f) / rec.OrigW - 0.5f);
            float cy = Height * (0.5f - (rec.OffY + rec.DrawH * 0.5f) / rec.OrigH);
            model.m[12] += model.m[0] * cx + model.m[4] * cy;
            model.m[13] += model.m[1] * cx + model.m[5] * cy;
            model.m[14] += model.m[2] * cx + model.m[6] * cy;
            model.m[0] *= sx; model.m[1] *= sx; model.m[2] *= sx;
            model.m[4] *= sy; model.m[5] *= sy; model.m[6] *= sy;
            float aw = rec.Atlas.Width, ah = rec.Atlas.Height;
            queue.DrawMesh(Mesh.Quad(), mat, in model, Color,
                rec.FrameX / aw, rec.FrameY / ah,
                (rec.FrameX + rec.DrawW) / aw, (rec.FrameY + rec.DrawH) / ah, SortingOrder);
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
