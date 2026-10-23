namespace DigitoyEngine;

// Sahne vatandasi kamera TANIMI. Render-katmani Camera nesnesinin (pass sahibi:
// Target/Queue/Encode) sahibi DEGILDIR — onu editor/player kurar; bu component
// yalnizca arka plani/maskeyi TARIF eder, projeksiyonu ayni GO'daki kardes
// CameraProjection component'ine delege eder (yoksa piksel-ortho fallback);
// sahibi her frame ApplyTo ile basar (Set* ayni degerlerde no-op => statik kamera
// maliyeti bir kiyas). Boylece editor RT dolaylamasi (GameOutput) ve standalone
// swapchain ayni tarifi paylasir. Tuketiciler (layout/picking) GetWorldRect/
// ScreenPointToRay uzerinden calistigi icin projeksiyon tipinden habersizdir.

// Kamera stacking'de temizleme davranisi (Unity clear flags alt kumesi).
public enum CameraClearFlags
{
    SolidColor, // renk + depth temizle (taban kamera)
    DepthOnly,  // yalniz depth (ust katman: onceki goruntu kalir)
    DontClear,  // hicbir sey
}

[ExecuteInEditMode]
[AddComponentMenu("Camera/Camera")]
public sealed class CameraComponent : Component
{
    // Birden fazla kamera varsa en dusuk depth "main" secilir (Unity Camera.depth).
    public int depth;
    public Color background = new Color(0, 0, 0, 255);
    // Bit i = layer i'yi cizer (8 layer; -1 = hepsi).
    public int cullingMask = -1;
    public CameraClearFlags clearFlags = CameraClearFlags.SolidColor;

    // Kardes projeksiyon (kayit CameraProjection.OnEnable/OnDisable; frame'de
    // GetComponent yok). null = PixelPerfect davranisi.
    CameraProjection _projection;
    public CameraProjection Projection => _projection;

    protected internal override void OnEnable()
    {
        gameObject.scene?.RegisterCamera(this);
        _projection ??= GetComponent<CameraProjection>(); // projeksiyon once eklenmisse
    }

    protected internal override void OnDisable() => gameObject.scene?.UnregisterCamera(this);

    internal void Attach(CameraProjection p)
    {
        if (_projection != null && _projection != p)
        {
            System.Console.WriteLine("[camera] '" + gameObject.name + "' birden fazla CameraProjection: "
                + p.GetType().Name + " yok sayildi (" + _projection.GetType().Name + " aktif)");
            return;
        }
        _projection = p;
    }

    internal void Detach(CameraProjection p)
    {
        if (_projection == p)
            _projection = null;
    }

    // Gorus dikdortgeni dunya uzayinda (x,y = sol-ust); perspektifte z=0
    // duzlemindeki frustum kesiti. Layout (fitScreen), picking ve editor cercevesi
    // ayni matematigi paylasir — rect tanimi projeksiyonda tek noktada.
    public void GetWorldRect(float viewW, float viewH,
        out float x, out float y, out float w, out float h)
    {
        var p = transform.position;
        if (_projection != null)
            _projection.GetWorldRect(p, viewW, viewH, out x, out y, out w, out h);
        else
            PixelPerfectProjection.PixelRect(p, viewW, viewH, out x, out y, out w, out h);
    }

    // Tarifi render kamerasina basar. viewW/H = cikti yuzeyinin mantiksal boyutu.
    public void ApplyTo(Camera cam, float viewW, float viewH)
    {
        var p = transform.position;
        if (_projection != null)
            _projection.Apply(cam, p, viewW, viewH);
        else
            cam.SetOrtho(p.x, p.x + viewW, p.y, p.y + viewH);
        cam.BackgroundColor = background;
        cam.ClearColor = clearFlags == CameraClearFlags.SolidColor;
        cam.ClearDepth = clearFlags != CameraClearFlags.DontClear;
    }

    // Dunya noktasi -> mantiksal viewport (sol-ust orijin). Projeksiyon tipinden bagimsiz: tarif
    // bir karalama render kamerasina basilir (Set* dedup'lu, ucuz) ve GERCEK ViewProj ile izdusum alinir
    // (rotasyonlu 3D kamera dahil). false = nokta gozun arkasinda (perspektif) ya da dejenere.
    static Camera _scratch;

    public bool WorldToScreenPoint(Vec3 world, float viewW, float viewH, out float sx, out float sy)
    {
        sx = sy = 0;
        if (viewW <= 0 || viewH <= 0)
            return false;
        _scratch ??= new Camera();
        ApplyTo(_scratch, viewW, viewH);
        if (!Mat4.TransformPointW(ref _scratch.ViewProj, world, out var ndc, out float w) || w <= 0)
            return false;
        sx = (ndc.x + 1f) * 0.5f * viewW;
        sy = (1f - ndc.y) * 0.5f * viewH;
        return true;
    }

    // Viewport noktasindan dunya isini. Tuketici (picking) projeksiyon tipini
    // bilmez: ortho'da eksene paralel isin, perspektifte gozden gecen isin.
    public bool ScreenPointToRay(float sx, float sy, float viewW, float viewH, out Ray ray)
    {
        ray = default;
        if (viewW <= 0 || viewH <= 0)
            return false;
        GetWorldRect(viewW, viewH, out float x, out float y, out float w, out float h);
        float wx = x + sx / viewW * w;
        float wy = y + sy / viewH * h;
        if (_projection != null)
            _projection.ScreenPointToRay(transform.position, viewW, viewH, wx, wy, out ray);
        else
        {
            ray.Origin = new Vec3(wx, wy, -16384f); // ortho near default'u
            ray.Dir = new Vec3(0, 0, 1);
        }
        return true;
    }
}
