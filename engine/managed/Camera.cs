using System.Collections.Generic;

namespace DigitoyEngine;

// Unity Camera karsiligi: pass'in SAHIBI. Nereye cizilecegi (Target: null =
// swapchain, Texture = render target), nasil temizlenecegi (ClearFlags/renk)
// ve hangi matris ile cizilecegi (ViewProj) kameranin ozelligidir. Icerik
// secimi kamera-basina kuyruk ile: cizen kod hangi kameraya cizdigini bilir
// (Unity cullingMask yerine explicit kuyruk).
//
// Frame: kameralar Order'a gore encode edilir (Unity Camera.depth). Ayni RT'yi
// once yazip sonra baska kamerada doku olarak okumak serbest (pass'ler sirali).
public sealed class Camera
{
    public int Order;                 // Unity Camera.depth: kucuk once cizilir
    public Texture Target;            // null = swapchain, degilse render target
    public uint Framebuffer;          // Target=null iken ham GL fbo (0=ekran; NativeWindow FBO'su)
    public bool ClearColor = true;
    public Color BackgroundColor = new Color(0, 0, 0, 255);
    public bool ClearDepth = true;
    public Mat4 ViewProj;
    public readonly RenderQueue Queue = new();

    // Projeksiyon degisim damgasi: gorus rect'i degisince ++. Layout/picking
    // cache'leri her frame yalnizca bu int'i kiyaslar (matris kiyasi yok).
    public int RectVersion { get; private set; } = 1;

    // Gorus dikdortgeni (dunya uzayi): ortho'da dogrudan, perspektifte z=0
    // duzlemindeki frustum kesiti. Yalnizca Set* ile kurulunca gecerli;
    // ViewProj'u disaridan dogrudan yazan (custom matris) rect vermez.
    float _oL, _oR, _oT, _oB, _oNear, _oFar;
    bool _hasRect;
    bool _persp;                          // son kurulum perspektif miydi
    float _pFov, _pAspect, _pX, _pY, _pZ; // perspektif no-op kiyasi

    // Piksel/point koordinatli 2D/UI kamerasi (orijin sol-ust, y asagi).
    public void SetPixelOrtho(float width, float height)
        => SetOrtho(0, width, 0, height);

    // y-asagi dunya uzayinda ortho: top < bottom. Ayni parametrelerle tekrar
    // cagri bedava no-op (statik kamera frame maliyeti = bu kiyas).
    // near/far genis: dunya birimi = piksel oldugundan X/Y rotasyonlu quad'lar
    // z'de yuzlerce birim uzar; dar [-1,1] araligi onlari kirpiyordu.
    public void SetOrtho(float left, float right, float top, float bottom,
        float near = -16384, float far = 16384)
    {
        if (_hasRect && !_persp && left == _oL && right == _oR && top == _oT && bottom == _oB
            && near == _oNear && far == _oFar) return;
        _oL = left; _oR = right; _oT = top; _oB = bottom; _oNear = near; _oFar = far;
        _hasRect = true; _persp = false;
        Mat4.Ortho(left, right, bottom, top, near, far, out ViewProj);
        RectVersion++;
    }

    // Perspektif kamera: (px,py,pz) = kamera konumu, bakis +z yonune (y-asagi
    // dunya, sahne icerigi z=0 duzleminde). Gorunmesi icin pz < 0 olmali
    // (Unity'deki z=-10 gibi; dunya birimi piksel oldugundan tipik -500..-1500).
    // Rect = z=0 duzlemindeki frustum kesiti: GetWorldRect/ScreenToWorld ayni
    // kesitle calisir, tuketici (layout/picking) projeksiyon tipini bilmez.
    public void SetPerspective(float fovYDeg, float aspect, float near, float far,
        float px, float py, float pz)
    {
        if (_hasRect && _persp && fovYDeg == _pFov && aspect == _pAspect
            && near == _oNear && far == _oFar && px == _pX && py == _pY && pz == _pZ) return;
        _pFov = fovYDeg; _pAspect = aspect; _oNear = near; _oFar = far;
        _pX = px; _pY = py; _pZ = pz;
        _hasRect = true; _persp = true;

        float d = -pz; if (d < 1e-3f) d = 1e-3f; // pz>=0 dejenere: minik mesafeyle koru
        float fovRad = fovYDeg * (System.MathF.PI / 180f);
        float halfH = d * System.MathF.Tan(fovRad * 0.5f);
        float halfW = halfH * aspect;
        _oL = px - halfW; _oR = px + halfW; _oT = py - halfH; _oB = py + halfH;

        Mat4.Perspective(fovRad, aspect, near, far, out var proj);
        Mat4.ViewYDown(px, py, pz, out var view);
        Mat4.Multiply(ref proj, ref view, out ViewProj);
        RectVersion++;
    }

    // Kameranin gorus dikdortgeni dunya uzayinda (x,y = sol-ust). Perspektifte
    // z=0 duzlemindeki frustum kesiti — cagiran (layout/picking) projeksiyon
    // tipini bilmez.
    public bool GetWorldRect(out float x, out float y, out float w, out float h)
    {
        if (!_hasRect) { x = y = w = h = 0; return false; }
        x = _oL; y = _oT; w = _oR - _oL; h = _oB - _oT;
        return true;
    }

    // Viewport noktasi (piksel, sol-ust orijin) -> dunya. viewW/H = bu kameranin
    // ciktigi yuzeyin boyutu (swapchain veya RT). Perspektifte sonuc z=0
    // duzleminde birebir dogrudur (kesit o duzlemde alinir).
    public bool ScreenToWorld(float sx, float sy, float viewW, float viewH,
        out float wx, out float wy)
    {
        if (!_hasRect || viewW <= 0 || viewH <= 0) { wx = wy = 0; return false; }
        wx = _oL + sx / viewW * (_oR - _oL);
        wy = _oT + sy / viewH * (_oB - _oT);
        return true;
    }

    // Bu kameranin pass'ini + kuyrugunu akisa yazar. swapchain boyutlari
    // yalnizca Target=null iken kullanilir (RT boyutu attachment'tan gelir).
    public void Encode(CommandBuffer cb, int swapchainWidth, int swapchainHeight)
    {
        float r = BackgroundColor.r / 255f, g = BackgroundColor.g / 255f;
        float b = BackgroundColor.b / 255f, a = BackgroundColor.a / 255f;

        if (Target != null)
            cb.BeginPassOffscreen(ClearColor, r, g, b, a, ClearDepth, 1f,
                Target.ColorAttachmentView, Target.DepthAttachmentView);
        else
            cb.BeginPass(ClearColor, r, g, b, a, ClearDepth, 1f,
                swapchainWidth, swapchainHeight, 1, Framebuffer);

        Queue.Flush(cb, ref ViewProj);
        cb.EndPass();
    }

    // Kameralari Order sirasina gore encode eder (liste yerinde siralanir).
    public static void EncodeAll(List<Camera> cameras, CommandBuffer cb, int swapchainWidth, int swapchainHeight)
    {
        cameras.Sort((x, y) => x.Order.CompareTo(y.Order));
        for (int i = 0; i < cameras.Count; i++)
            cameras[i].Encode(cb, swapchainWidth, swapchainHeight);
    }
}
