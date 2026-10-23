namespace DigitoyEngine;

// Kameranin PROJEKSIYON stratejisi: CameraComponent ile ayni GO'ya takilan kardes
// component (polimorfik gomulu nesne serilestirmesi yok; component zaten polimorfik
// ve oyun kodundan genisler). Tuketiciler (layout/picking/editor cercevesi) tipi
// bilmez, yalniz bu sozlesmeyi cagirir: "z=0 duzlemindeki gorus rect'i" + matris.
// Kayit: OnEnable/OnDisable kardes kameraya Attach/Detach (frame'de GetComponent yok).
[ExecuteInEditMode]
public abstract class CameraProjection : Component
{
    protected internal override void OnEnable() => GetComponent<CameraComponent>()?.Attach(this);
    protected internal override void OnDisable() => GetComponent<CameraComponent>()?.Detach(this);

    // Gorus dikdortgeni dunya uzayinda (x,y = sol-ust); perspektifte z=0 kesiti.
    public abstract void GetWorldRect(in Vec3 camPos, float viewW, float viewH,
        out float x, out float y, out float w, out float h);

    // Matrisi render kamerasina basar (Set* no-op dedup'li — statik kamera bedava).
    public abstract void Apply(Camera cam, in Vec3 camPos, float viewW, float viewH);

    // (wx,wy) = z=0 kesit noktasi (GetWorldRect'ten). Default: eksene paralel (ortho) isin.
    public virtual void ScreenPointToRay(in Vec3 camPos, float viewW, float viewH,
        float wx, float wy, out Ray ray)
    {
        ray.Origin = new Vec3(wx, wy, -16384f); // ortho near default'u
        ray.Dir = new Vec3(0, 0, 1);
    }
}

// 1 dunya birimi = 1 piksel, kamera konumu = gorusun SOL-UST kosesi.
// Projeksiyon component'i olmayan kameranin da fallback davranisi budur.
[AddComponentMenu("Camera/Pixel Perfect Projection")]
public sealed class PixelPerfectProjection : CameraProjection
{
    public override void GetWorldRect(in Vec3 camPos, float viewW, float viewH,
        out float x, out float y, out float w, out float h)
        => PixelRect(camPos, viewW, viewH, out x, out y, out w, out h);

    public override void Apply(Camera cam, in Vec3 camPos, float viewW, float viewH)
        => cam.SetOrtho(camPos.x, camPos.x + viewW, camPos.y, camPos.y + viewH);

    internal static void PixelRect(in Vec3 camPos, float viewW, float viewH,
        out float x, out float y, out float w, out float h)
    {
        x = camPos.x; y = camPos.y; w = viewW; h = viewH;
    }
}

// Unity ortho: orthoSize = gorusun dunya YARI yuksekligi, kamera konumu = merkez.
[AddComponentMenu("Camera/Orthographic Projection")]
public sealed class OrthographicProjection : CameraProjection
{
    public float orthoSize = 300;

    public override void GetWorldRect(in Vec3 camPos, float viewW, float viewH,
        out float x, out float y, out float w, out float h)
    {
        if (orthoSize <= 0) { PixelPerfectProjection.PixelRect(camPos, viewW, viewH, out x, out y, out w, out h); return; }
        h = orthoSize * 2f;
        w = h * (viewH > 0 ? viewW / viewH : 1f);
        x = camPos.x - w * 0.5f;
        y = camPos.y - h * 0.5f;
    }

    public override void Apply(Camera cam, in Vec3 camPos, float viewW, float viewH)
    {
        GetWorldRect(camPos, viewW, viewH, out float x, out float y, out float w, out float h);
        cam.SetOrtho(x, x + w, y, y + h);
    }
}

// fov hangi eksende sabit: Vertical (Unity varsayilani) ya da Horizontal (dar ekranda
// yatay genislik korunur — "aspect'e gore fov" polinomlarinin genel karsiligi).
public enum FovAxis
{
    Vertical,
    Horizontal,
}

// Unity persp: fov derece (fovAxis eksende), kamera konumu = goz (z<0, bakis +z; dunya birimi
// piksel oldugundan tipik -500..-1500). Turevler yalniz Distance'i degistirir.
[AddComponentMenu("Camera/Perspective Projection")]
public class PerspectiveProjection : CameraProjection
{
    public float fov = 60;
    public FovAxis fovAxis = FovAxis.Vertical;
    // Dunya birimi piksel oldugundan Unity'den genis varsayilanlar.
    public float nearClip = 1;
    public float farClip = 20000;

    // Dikey fov (derece): Horizontal eksende aspect'ten turetilir.
    protected float VerticalFov(float aspect)
    {
        if (fovAxis == FovAxis.Vertical || aspect <= 0)
            return fov;
        float halfH = System.MathF.Tan(fov * (System.MathF.PI / 360f)) / aspect;
        return System.MathF.Atan(halfH) * (360f / System.MathF.PI);
    }

    // Gozun z=0 duzlemine uzakligi (pozitif). Default: transform z'si.
    protected virtual float Distance(in Vec3 camPos, float aspect) => -camPos.z;

    float SafeDistance(in Vec3 camPos, float aspect)
    {
        float d = Distance(camPos, aspect);
        return d < 1e-3f ? 1e-3f : d; // dejenere: minik mesafeyle koru
    }

    public override void GetWorldRect(in Vec3 camPos, float viewW, float viewH,
        out float x, out float y, out float w, out float h)
    {
        float aspect = viewH > 0 ? viewW / viewH : 1f;
        float d = SafeDistance(camPos, aspect);
        h = 2f * d * System.MathF.Tan(VerticalFov(aspect) * (System.MathF.PI / 360f));
        w = h * aspect;
        x = camPos.x - w * 0.5f;
        y = camPos.y - h * 0.5f;
    }

    public override void Apply(Camera cam, in Vec3 camPos, float viewW, float viewH)
    {
        float aspect = viewH > 0 ? viewW / viewH : 1f;
        cam.SetPerspective(VerticalFov(aspect), aspect, nearClip, farClip,
            camPos.x, camPos.y, -SafeDistance(camPos, aspect));
    }

    public override void ScreenPointToRay(in Vec3 camPos, float viewW, float viewH,
        float wx, float wy, out Ray ray)
    {
        float d = SafeDistance(camPos, viewH > 0 ? viewW / viewH : 1f);
        ray.Origin = new Vec3(camPos.x, camPos.y, -d);
        ray.Dir = Vec3.Normalize(new Vec3(wx - camPos.x, wy - camPos.y, d));
    }
}

// Perspektif + referans cozunurluk ekrani KAPLAYACAK mesafe (CanvasScaler'in kamera
// karsiligi): en veya boydan biri tam oturur, digeri tasar. transform z yok sayilir.
[AddComponentMenu("Camera/Fit Perspective Projection")]
public sealed class FitPerspectiveProjection : PerspectiveProjection
{
    public Vec2 referenceResolution = new Vec2(1080, 1920);

    protected override float Distance(in Vec3 camPos, float aspect)
    {
        float needH = System.MathF.Max(referenceResolution.y, referenceResolution.x / aspect);
        return needH / (2f * System.MathF.Tan(VerticalFov(aspect) * System.MathF.PI / 360f));
    }
}

// Serbest 3D kamera: transform'un KONUMU ve ROTASYONU birlikte kullanilir (Unity Camera
// paritesi; lokal +z bakis yonu, dunya y-asagi). 3D sahne icerigi (MeshRenderer) icin;
// UI layout bu kamerada calismaz (GetWorldRect yaklasik: konum merkezli, |z| mesafeli kesit).
// Picking: ekran noktasi inverse(ViewProj) ile dunya isinina acilir.
[AddComponentMenu("Camera/Free Perspective Projection")]
public sealed class FreePerspectiveProjection : PerspectiveProjection
{
    protected override float Distance(in Vec3 camPos, float aspect) => camPos.z < 0 ? -camPos.z : camPos.z;

    public override void Apply(Camera cam, in Vec3 camPos, float viewW, float viewH)
    {
        float aspect = viewH > 0 ? viewW / viewH : 1f;
        ref var world = ref transform._getWorldMatrix();
        cam.SetPerspectiveView(VerticalFov(aspect), aspect, nearClip, farClip, ref world);
    }

    public override void ScreenPointToRay(in Vec3 camPos, float viewW, float viewH,
        float wx, float wy, out Ray ray)
    {
        ray = default;
        float aspect = viewH > 0 ? viewW / viewH : 1f;
        ref var world = ref transform._getWorldMatrix();
        if (!Mat4.ViewFromWorld(ref world, out var view))
            return;
        Mat4.Perspective(VerticalFov(aspect) * (System.MathF.PI / 180f), aspect, nearClip, farClip, out var proj);
        Mat4.Multiply(ref proj, ref view, out var vp);
        if (!Mat4.Inverse(ref vp, out var inv))
            return;
        // (wx,wy) = GetWorldRect kesitindeki nokta -> viewport orani -> NDC (ust = +1).
        GetWorldRect(camPos, viewW, viewH, out float rx, out float ry, out float rw, out float rh);
        float nx = rw > 0 ? (wx - rx) / rw * 2f - 1f : 0f;
        float ny = rh > 0 ? 1f - (wy - ry) / rh * 2f : 0f;
        if (!Mat4.TransformPointW(ref inv, new Vec3(nx, ny, Mat4.NdcNear), out var pNear)
            || !Mat4.TransformPointW(ref inv, new Vec3(nx, ny, Mat4.NdcFar), out var pFar))
            return;
        ray.Origin = pNear;
        ray.Dir = Vec3.Normalize(pFar - pNear);
    }
}
