namespace DigitoyEngine;

// Sahne vatandasi kamera TANIMI. Render-katmani Camera nesnesinin (pass sahibi:
// Target/Queue/Encode) sahibi DEGILDIR — onu editor/player kurar; bu component
// yalnizca projeksiyonu ve arka plani TARIF eder, sahibi her frame ApplyTo ile
// basar (SetOrtho ayni degerlerde no-op => statik kamera maliyeti bir kiyas).
// Boylece editor RT dolaylamasi (GameOutput) ve standalone swapchain ayni tarifi
// paylasir. Tuketiciler (layout/picking) Camera.GetWorldRect/ScreenToWorld
// uzerinden calistigi icin projeksiyon tipinden habersizdir.
// Projeksiyon tipleri (Unity: orthographic bool; burada piksel-ortho da birinci
// sinif mod oldugundan enum).
public enum CameraProjection
{
    PixelPerfect,  // 1 dunya birimi = 1 piksel, position = gorusun SOL-UST kosesi
    Orthographic,  // Unity ortho: orthoSize = YARI yukseklik, position = merkez
    Perspective,   // Unity persp: fov dikey derece, position = kamera konumu (z<0)
}

// Kamera stacking'de temizleme davranisi (Unity clear flags alt kumesi).
public enum CameraClearFlags
{
    SolidColor, // renk + depth temizle (taban kamera)
    DepthOnly,  // yalniz depth (ust katman: onceki goruntu kalir)
    DontClear,  // hicbir sey
}

public sealed class CameraComponent : Component
{
    // Birden fazla kamera varsa en dusuk depth "main" secilir (Unity Camera.depth).
    public int depth;
    public Color background = new Color(0, 0, 0, 255);
    // Bit i = layer i'yi cizer (8 layer; -1 = hepsi).
    public int cullingMask = -1;
    public CameraClearFlags clearFlags = CameraClearFlags.SolidColor;

    public CameraProjection projection = CameraProjection.PixelPerfect;
    // Unity semantigi: gorusun dunya YARI yuksekligi (tam yukseklik = 2*orthoSize).
    [ShowIf(nameof(projection), CameraProjection.Orthographic)]
    public float orthoSize = 300;
    [ShowIf(nameof(projection), CameraProjection.Perspective)]
    public float fov = 60;
    // Dunya birimi piksel oldugundan Unity'den genis varsayilanlar.
    [ShowIf(nameof(projection), CameraProjection.Perspective)]
    public float nearClip = 1;
    [ShowIf(nameof(projection), CameraProjection.Perspective)]
    public float farClip = 20000;

    protected internal override void OnEnable() => gameObject.scene?.RegisterCamera(this);
    protected internal override void OnDisable() => gameObject.scene?.UnregisterCamera(this);

    // Gorus dikdortgeni dunya uzayinda (x,y = sol-ust); perspektifte z=0
    // duzlemindeki frustum kesiti. Layout (fitScreen) ve ApplyTo ayni matematigi
    // paylasir — rect tanimi tek noktada.
    public void GetWorldRect(float viewW, float viewH,
        out float x, out float y, out float w, out float h)
    {
        var p = transform.position;
        float aspect = viewH > 0 ? viewW / viewH : 1f;
        switch (projection)
        {
            case CameraProjection.Orthographic when orthoSize > 0:
                h = orthoSize * 2f;
                w = h * aspect;
                break;
            case CameraProjection.Perspective:
                float d = -p.z; if (d < 1e-3f) d = 1e-3f;
                h = 2f * d * System.MathF.Tan(fov * (System.MathF.PI / 360f));
                w = h * aspect;
                break;
            default: // PixelPerfect (ve gecersiz orthoSize fallback'i)
                x = p.x; y = p.y; w = viewW; h = viewH;
                return;
        }
        x = p.x - w * 0.5f;
        y = p.y - h * 0.5f;
    }

    // Tarifi render kamerasina basar. viewW/H = cikti yuzeyinin mantiksal boyutu.
    public void ApplyTo(Camera cam, float viewW, float viewH)
    {
        if (projection == CameraProjection.Perspective)
        {
            var p = transform.position;
            cam.SetPerspective(fov, viewH > 0 ? viewW / viewH : 1f,
                nearClip, farClip, p.x, p.y, p.z);
        }
        else
        {
            GetWorldRect(viewW, viewH, out float x, out float y, out float w, out float h);
            cam.SetOrtho(x, x + w, y, y + h);
        }
        cam.BackgroundColor = background;
        cam.ClearColor = clearFlags == CameraClearFlags.SolidColor;
        cam.ClearDepth = clearFlags != CameraClearFlags.DontClear;
    }

    // Viewport noktasindan dunya isini. Tuketici (picking) projeksiyon tipini
    // bilmez: ortho'da eksene paralel isin, perspektifte kameradan gecen isin.
    public bool ScreenPointToRay(float sx, float sy, float viewW, float viewH, out Ray ray)
    {
        ray = default;
        if (viewW <= 0 || viewH <= 0)
            return false;
        GetWorldRect(viewW, viewH, out float x, out float y, out float w, out float h);
        float wx = x + sx / viewW * w;
        float wy = y + sy / viewH * h;
        if (projection == CameraProjection.Perspective)
        {
            var p = transform.position;
            // (wx,wy) z=0 kesit noktasi; isin kameradan o noktaya.
            ray.Origin = p;
            ray.Dir = Vec3.Normalize(new Vec3(wx - p.x, wy - p.y, -p.z));
        }
        else
        {
            ray.Origin = new Vec3(wx, wy, -16384f); // ortho near default'u
            ray.Dir = new Vec3(0, 0, 1);
        }
        return true;
    }
}
