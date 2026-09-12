using System;

namespace DigitoyEngine;

// FBO/renderbuffer/blit icin minimal GL yuzeyi. sokol bunlari expose etmez;
// glfwGetProcAddress ile dogrudan yuklenir (native shim degisikligi gerekmez).
static unsafe class GLExt
{
    public const uint FRAMEBUFFER = 0x8D40;
    public const uint READ_FRAMEBUFFER = 0x8CA8;
    public const uint DRAW_FRAMEBUFFER = 0x8CA9;
    public const uint RENDERBUFFER = 0x8D41;
    public const uint COLOR_ATTACHMENT0 = 0x8CE0;
    public const uint DEPTH_STENCIL_ATTACHMENT = 0x821A;
    public const uint RGBA8 = 0x8058;
    public const uint DEPTH24_STENCIL8 = 0x88F0;
    public const uint FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const uint COLOR_BUFFER_BIT = 0x00004000;
    public const uint LINEAR = 0x2601;

    public static delegate* unmanaged<int, uint*, void> GenRenderbuffers;
    public static delegate* unmanaged<uint, uint, void> BindRenderbuffer;
    public static delegate* unmanaged<uint, uint, int, int, void> RenderbufferStorage;
    public static delegate* unmanaged<int, uint*, void> DeleteRenderbuffers;
    public static delegate* unmanaged<int, uint*, void> GenFramebuffers;
    public static delegate* unmanaged<uint, uint, void> BindFramebuffer;
    public static delegate* unmanaged<uint, uint, uint, uint, void> FramebufferRenderbuffer;
    public static delegate* unmanaged<uint, uint> CheckFramebufferStatus;
    public static delegate* unmanaged<int, uint*, void> DeleteFramebuffers;
    public static delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void> BlitFramebuffer;

    static bool _loaded;

    // GL context AKTIF iken bir kez cagrilir.
    public static bool Load()
    {
        if (_loaded)
            return true;
        GenRenderbuffers = (delegate* unmanaged<int, uint*, void>)(void*)GLFW.GetProcAddress("glGenRenderbuffers");
        BindRenderbuffer = (delegate* unmanaged<uint, uint, void>)(void*)GLFW.GetProcAddress("glBindRenderbuffer");
        RenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)(void*)GLFW.GetProcAddress("glRenderbufferStorage");
        DeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)(void*)GLFW.GetProcAddress("glDeleteRenderbuffers");
        GenFramebuffers = (delegate* unmanaged<int, uint*, void>)(void*)GLFW.GetProcAddress("glGenFramebuffers");
        BindFramebuffer = (delegate* unmanaged<uint, uint, void>)(void*)GLFW.GetProcAddress("glBindFramebuffer");
        FramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)(void*)GLFW.GetProcAddress("glFramebufferRenderbuffer");
        CheckFramebufferStatus = (delegate* unmanaged<uint, uint>)(void*)GLFW.GetProcAddress("glCheckFramebufferStatus");
        DeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)(void*)GLFW.GetProcAddress("glDeleteFramebuffers");
        BlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)(void*)GLFW.GetProcAddress("glBlitFramebuffer");
        _loaded = GenRenderbuffers != null && BindRenderbuffer != null && RenderbufferStorage != null
            && DeleteRenderbuffers != null && GenFramebuffers != null && BindFramebuffer != null
            && FramebufferRenderbuffer != null && CheckFramebufferStatus != null
            && DeleteFramebuffers != null && BlitFramebuffer != null;
        return _loaded;
    }
}

// Ikincil NATIVE pencere (eski engine_host_glfw.c deseni):
//   - sokol TEK context'te (ana pencere) kalir; tum sg_* cagrilari orada.
//   - Pencere icerigi ana context'te PAYLASILAN renderbuffer'li bir FBO'ya
//     (MainFb) cizilir — Camera.Framebuffer bu FBO'yu hedefler.
//   - Present: pencere kendi context'inde ayni renderbuffer'i saran ikinci
//     FBO'dan (winFb) default framebuffer'a blit eder, swap yapar, ana
//     context'e donulur ve sg_reset_state_cache cagrilir.
// GL objeleri (renderbuffer) shared context'ler arasi paylasilir; container
// objeleri (FBO) context-lokal oldugu icin iki ayri FBO gerekir.
public sealed unsafe class NativeWindow
{
    public IntPtr Handle { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public readonly Camera Camera = new Camera();
    public readonly GuiHost Gui = new GuiHost();

    uint _colorRb, _depthRb, _mainFb, _winFb;
    static IntPtr _main;

    public static IntPtr MainWindow => _main;

    NativeWindow() { }

    // Ana pencere kurulup context aktifken bir kez cagrilir.
    public static bool Initialize(IntPtr mainWindow)
    {
        _main = mainWindow;
        return GLExt.Load();
    }

    public static NativeWindow Open(string title, int x, int y, int width, int height)
    {
        var w = new NativeWindow();
        // share=_main: buffer/texture/program objeleri ana context'le paylasilir.
        w.Handle = GLFW.CreateWindow(width, height, title, IntPtr.Zero, _main);
        if (w.Handle == IntPtr.Zero)
            return null;
        GLFW.SetWindowPos(w.Handle, x, y);
        if (!w.CreateBuffers(width, height))
        {
            GLFW.DestroyWindow(w.Handle);
            return null;
        }
        w.Width = width;
        w.Height = height;
        w.Camera.Framebuffer = w._mainFb;
        GuiInput.Attach(w.Handle);
        return w;
    }

    public bool ShouldClose => GLFW.WindowShouldClose(Handle) != GLFWConst.FALSE;

    // Frame basinda cagrilir: boyut degistiyse renderbuffer'lari yeniden kurar.
    // false => minimize (bu frame render/present atlanir).
    public bool UpdateSize()
    {
        GLFW.GetFramebufferSize(Handle, out int w, out int h);
        if (w <= 0 || h <= 0)
            return false;
        if (w != Width || h != Height)
        {
            DestroyBuffers();
            if (!CreateBuffers(w, h))
                return false;
            Width = w;
            Height = h;
            Camera.Framebuffer = _mainFb;
            Sokol.ResetStateCache();
        }
        return true;
    }

    // Commit SONRASI cagrilir: pencere context'inde blit + swap, ana context'e don.
    public void Present()
    {
        GLFW.GetFramebufferSize(Handle, out int dw, out int dh);
        GLFW.MakeContextCurrent(Handle);
        GLExt.BindFramebuffer(GLExt.DRAW_FRAMEBUFFER, 0);
        GLExt.BindFramebuffer(GLExt.READ_FRAMEBUFFER, _winFb);
        GLExt.BlitFramebuffer(0, 0, Width, Height, 0, 0, dw, dh,
            GLExt.COLOR_BUFFER_BIT, GLExt.LINEAR);
        GLFW.SwapBuffers(Handle);
        GLFW.MakeContextCurrent(_main);
        Sokol.ResetStateCache(); // context degisti: sokol'un GL state cache'i bayat
    }

    public void Destroy()
    {
        if (Handle == IntPtr.Zero)
            return;
        GuiInput.Detach(Handle);
        DestroyBuffers();
        GLFW.DestroyWindow(Handle);
        Handle = IntPtr.Zero;
        GLFW.MakeContextCurrent(_main);
        Sokol.ResetStateCache();
    }

    bool CreateBuffers(int width, int height)
    {
        // sokol'un cizecegi renderbuffer'lar ANA context'te olusur.
        GLFW.MakeContextCurrent(_main);
        fixed (uint* p = &_colorRb) GLExt.GenRenderbuffers(1, p);
        GLExt.BindRenderbuffer(GLExt.RENDERBUFFER, _colorRb);
        GLExt.RenderbufferStorage(GLExt.RENDERBUFFER, GLExt.RGBA8, width, height);
        fixed (uint* p = &_depthRb) GLExt.GenRenderbuffers(1, p);
        GLExt.BindRenderbuffer(GLExt.RENDERBUFFER, _depthRb);
        GLExt.RenderbufferStorage(GLExt.RENDERBUFFER, GLExt.DEPTH24_STENCIL8, width, height);
        fixed (uint* p = &_mainFb) GLExt.GenFramebuffers(1, p);
        GLExt.BindFramebuffer(GLExt.FRAMEBUFFER, _mainFb);
        GLExt.FramebufferRenderbuffer(GLExt.FRAMEBUFFER, GLExt.COLOR_ATTACHMENT0, GLExt.RENDERBUFFER, _colorRb);
        GLExt.FramebufferRenderbuffer(GLExt.FRAMEBUFFER, GLExt.DEPTH_STENCIL_ATTACHMENT, GLExt.RENDERBUFFER, _depthRb);
        bool mainOk = GLExt.CheckFramebufferStatus(GLExt.FRAMEBUFFER) == GLExt.FRAMEBUFFER_COMPLETE;
        GLExt.BindFramebuffer(GLExt.FRAMEBUFFER, 0);

        // Pencere context'i ayni renderbuffer'i blit KAYNAGI olarak sarar.
        GLFW.MakeContextCurrent(Handle);
        GLFW.SwapInterval(0); // vsync yalniz ana pencerede (cift bekleme olmasin)
        fixed (uint* p = &_winFb) GLExt.GenFramebuffers(1, p);
        GLExt.BindFramebuffer(GLExt.FRAMEBUFFER, _winFb);
        GLExt.FramebufferRenderbuffer(GLExt.FRAMEBUFFER, GLExt.COLOR_ATTACHMENT0, GLExt.RENDERBUFFER, _colorRb);
        bool winOk = GLExt.CheckFramebufferStatus(GLExt.FRAMEBUFFER) == GLExt.FRAMEBUFFER_COMPLETE;
        GLExt.BindFramebuffer(GLExt.FRAMEBUFFER, 0);
        GLFW.MakeContextCurrent(_main);

        if (!mainOk || !winOk)
            Console.Error.WriteLine($"NativeWindow: FBO incomplete ({width}x{height})");
        return mainOk && winOk;
    }

    void DestroyBuffers()
    {
        if (_winFb != 0)
        {
            GLFW.MakeContextCurrent(Handle);
            fixed (uint* p = &_winFb) GLExt.DeleteFramebuffers(1, p);
            _winFb = 0;
        }
        GLFW.MakeContextCurrent(_main);
        if (_mainFb != 0) { fixed (uint* p = &_mainFb) GLExt.DeleteFramebuffers(1, p); _mainFb = 0; }
        if (_colorRb != 0) { fixed (uint* p = &_colorRb) GLExt.DeleteRenderbuffers(1, p); _colorRb = 0; }
        if (_depthRb != 0) { fixed (uint* p = &_depthRb) GLExt.DeleteRenderbuffers(1, p); _depthRb = 0; }
    }
}
