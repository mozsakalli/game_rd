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

    // Piksel/point koordinatli 2D/UI kamerasi (orijin sol-ust, y asagi).
    public void SetPixelOrtho(float width, float height)
        => Mat4.Ortho(0, width, height, 0, -1, 1, out ViewProj);

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
