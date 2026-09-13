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

    // Ortho gorus dikdortgeni (dunya uzayi). Yalnizca Set*Ortho ile kurulunca
    // gecerli; ViewProj'u disaridan dogrudan yazan (custom matris) rect vermez.
    float _oL, _oR, _oT, _oB, _oNear, _oFar;
    bool _hasOrtho;

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
        if (_hasOrtho && left == _oL && right == _oR && top == _oT && bottom == _oB
            && near == _oNear && far == _oFar) return;
        _oL = left; _oR = right; _oT = top; _oB = bottom; _oNear = near; _oFar = far;
        _hasOrtho = true;
        Mat4.Ortho(left, right, bottom, top, near, far, out ViewProj);
        RectVersion++;
    }

    // Kameranin gorus dikdortgeni dunya uzayinda (x,y = sol-ust). Perspektif
    // gelince ayni API verilen duzlem mesafesindeki frustum kesitini verecek —
    // cagiran (layout/picking) projeksiyon tipini bilmez.
    public bool GetWorldRect(out float x, out float y, out float w, out float h)
    {
        if (!_hasOrtho) { x = y = w = h = 0; return false; }
        x = _oL; y = _oT; w = _oR - _oL; h = _oB - _oT;
        return true;
    }

    // Viewport noktasi (piksel, sol-ust orijin) -> dunya. viewW/H = bu kameranin
    // ciktigi yuzeyin boyutu (swapchain veya RT).
    public bool ScreenToWorld(float sx, float sy, float viewW, float viewH,
        out float wx, out float wy)
    {
        if (!_hasOrtho || viewW <= 0 || viewH <= 0) { wx = wy = 0; return false; }
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
