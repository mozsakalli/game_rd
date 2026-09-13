namespace DigitoyEngine;

// Sahne vatandasi kamera TANIMI. Render-katmani Camera nesnesinin (pass sahibi:
// Target/Queue/Encode) sahibi DEGILDIR — onu editor/player kurar; bu component
// yalnizca projeksiyonu ve arka plani TARIF eder, sahibi her frame ApplyTo ile
// basar (SetOrtho ayni degerlerde no-op => statik kamera maliyeti bir kiyas).
// Boylece editor RT dolaylamasi (GameOutput) ve standalone swapchain ayni tarifi
// paylasir. Perspektif ileride buraya alan olarak gelir; tuketiciler (layout/
// picking) Camera.GetWorldRect/ScreenToWorld uzerinden calistigi icin etkilenmez.
public sealed class CameraComponent : Component
{
    // Birden fazla kamera varsa en dusuk depth "main" secilir (Unity Camera.depth).
    public int depth;
    public Color background = new Color(0, 0, 0, 255);

    // true: 1 dunya birimi = 1 piksel, transform.position = gorusun SOL-UST kosesi
    // (pos 0,0 = eski sabit piksel-ortho davranisi birebir).
    // false: orthoSize = gorusun dunya YUKSEKLIGI, transform.position = MERKEZ,
    // genislik aspect'ten turer.
    public bool pixelPerfect = true;
    [ShowIf(nameof(pixelPerfect), false)]
    public float orthoSize = 600;

    protected internal override void OnEnable() => gameObject.scene?.RegisterCamera(this);
    protected internal override void OnDisable() => gameObject.scene?.UnregisterCamera(this);

    // Gorus dikdortgeni dunya uzayinda (x,y = sol-ust). Layout (fitScreen) ve
    // ApplyTo ayni matematigi paylasir — rect tanimi tek noktada.
    public void GetWorldRect(float viewW, float viewH,
        out float x, out float y, out float w, out float h)
    {
        var p = transform.position;
        if (pixelPerfect || orthoSize <= 0)
        {
            x = p.x; y = p.y; w = viewW; h = viewH;
        }
        else
        {
            h = orthoSize;
            w = viewH > 0 ? h * (viewW / viewH) : h;
            x = p.x - w * 0.5f;
            y = p.y - h * 0.5f;
        }
    }

    // Tarifi render kamerasina basar. viewW/H = cikti yuzeyinin mantiksal boyutu.
    public void ApplyTo(Camera cam, float viewW, float viewH)
    {
        GetWorldRect(viewW, viewH, out float x, out float y, out float w, out float h);
        cam.SetOrtho(x, x + w, y, y + h);
        cam.BackgroundColor = background;
    }
}
