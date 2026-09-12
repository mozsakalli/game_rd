using System;

namespace DigitoyEngine;

public unsafe class Texture : GpuResource
{
    Color* _pixels;
    bool _dirty;

    //sokol handles
    int _texture;
    int _textureView;
    int _colorAttachmentView;
    int _depthTexture;
    int _depthAttachmentView;

    public int Width { get; private set; }
    public int Height { get; private set; }

    // Bindings icin sokol texture-view id'si (0 = henuz senkronlanmadi).
    internal uint TextureView => (uint)_textureView;

    // Offscreen pass hedefi icin attachment view'lar (yalniz render target'ta dolu).
    internal uint ColorAttachmentView => (uint)_colorAttachmentView;
    internal uint DepthAttachmentView => (uint)_depthAttachmentView;
    public bool IsRenderTarget => _colorAttachmentView != 0;

    private Texture()
    {
    }

    // Yalniz CPU tarifi olan (piksel kopyali) dokular geri yuklenebilir; RT'ler olamaz.
    internal override bool CanEvict => _pixels != null && _colorAttachmentView == 0;

    internal override void ReleaseGpu()
    {
        if (_texture != 0)
        {
            Sokol.DestroyImage((uint)_texture);
            _texture = 0;
        }
        if (_textureView != 0)
        {
            Sokol.DestroyView((uint)_textureView);
            _textureView = 0;
        }
        if (_colorAttachmentView != 0)
        {
            Sokol.DestroyView((uint)_colorAttachmentView);
            _colorAttachmentView = 0;
        }
        if (_depthTexture != 0)
        {
            Sokol.DestroyImage((uint)_depthTexture);
            _depthTexture = 0;
        }
        if (_depthAttachmentView != 0)
        {
            Sokol.DestroyView((uint)_depthAttachmentView);
            _depthAttachmentView = 0;
        }
        if (_pixels != null)
            _dirty = true; // sonraki _Sync GPU'ya geri yukler
    }

    public override void Destroy()
    {
        base.Destroy();
        if (_pixels != null)
        {
            System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)_pixels);
            _pixels = null;
        }
    }

    // engine.c _engine_bitmapData_sync ile birebir: ilk sync'te immutable image +
    // texture-view olusturur; sonraki dirty'lerde piksel verisini gunceller.
    internal void _Sync()
    {
        Stamp(); // kullanim damgasi: Collect yakin zamanda kullanilana dokunmaz
        if (_pixels == null || !_dirty)
            return;
        _dirty = false;

        int byteSize = sizeof(Color) * Width * Height;
        if (_texture == 0)
        {
            _texture = (int)Sokol.MakeImage(
                Width, Height, SG.PixelFormatRgba8, 1, 1, 0 /*immutable(data)*/, _pixels, byteSize);
            _textureView = (int)Sokol.MakeView((uint)_texture, SG.ViewTexture);
        }
        else
        {
            Sokol.UpdateImage((uint)_texture, _pixels, byteSize);
        }
    }

    // Unity RenderTexture karsiligi: kamera hedefi olarak cizilir (Camera.Target),
    // sonra herhangi bir materyalde MainTexture olarak okunur. CPU pikseli yok,
    // _Sync no-op; view'lar burada eager kurulur.
    public static Texture CreateRenderTarget(int width, int height, bool depth = true)
    {
        var t = new Texture { Persistent = true }; // RT geri yuklenemez, toplama disi
        t.Width = (int)MathF.Max(1, width);
        t.Height = (int)MathF.Max(1, height);
        t._texture = (int)Sokol.MakeImage(
            t.Width, t.Height, SG.PixelFormatRgba8, 1, 1, SG.ImageColorAttachment, null, 0);
        t._textureView = (int)Sokol.MakeView((uint)t._texture, SG.ViewTexture);
        t._colorAttachmentView = (int)Sokol.MakeView((uint)t._texture, SG.ViewColorAttachment);
        if (depth)
        {
            t._depthTexture = (int)Sokol.MakeImage(
                t.Width, t.Height, SG.PixelFormatDepthStencil, 1, 1, SG.ImageDepthStencilAttachment, null, 0);
            t._depthAttachmentView = (int)Sokol.MakeView((uint)t._depthTexture, SG.ViewDepthStencilAttachment);
        }
        return t;
    }

    // Tek kanalli (R8) doku — SDF font atlasi gibi alpha/mesafe verisi icin.
    // Shader .r kanalindan okur (GL R8'de .a her zaman 1'dir).
    public static Texture FromAlpha(int width, int height, byte* pixels)
    {
        var t = new Texture();
        t.Width = width;
        t.Height = height;
        t._texture = (int)Sokol.MakeImage(
            width, height, SG.PixelFormatR8, 1, 1, 0 /*immutable(data)*/, pixels, width * height);
        t._textureView = (int)Sokol.MakeView((uint)t._texture, SG.ViewTexture);
        return t;
    }

    // Dinamik R8 doku (raster font atlasi): UpdateAlpha frame'de EN FAZLA BIR KEZ.
    public static Texture CreateDynamicAlpha(int width, int height)
    {
        var t = new Texture { Persistent = true };
        t.Width = width;
        t.Height = height;
        t._texture = (int)Sokol.MakeImage(
            width, height, SG.PixelFormatR8, 1, 1, 1 /*dynamic*/, null, 0);
        t._textureView = (int)Sokol.MakeView((uint)t._texture, SG.ViewTexture);
        return t;
    }

    public void UpdateAlpha(byte* pixels)
        => Sokol.UpdateImage((uint)_texture, pixels, Width * Height);

    public static Texture FromColor(int width, int height, Color color)
    {
        var texture = new Texture();
        width = (int)MathF.Max(1, width);
        height = (int)MathF.Max(1, height);
        texture._pixels = (Color*)System.Runtime.InteropServices.Marshal.AllocHGlobal(sizeof(Color) * width * height);
        texture.Width = width;
        texture.Height = height;
        texture._dirty = true;
        // Initialize the texture with the given color here.
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                texture._pixels[y * width + x] = color;
            }
        }
        return texture;
    }

    internal bool Loading;

    // CPU piksel blit'i (atlas kompozisyonu): iki doku da _pixels tasimali.
    // GPU'ya yukleme _dirty ile bir sonraki _Sync'te olur.
    internal void BlitFrom(Texture src, int dx, int dy)
    {
        if (_pixels == null || src?._pixels == null)
            return;
        int w = Math.Min(src.Width, Width - dx);
        int h = Math.Min(src.Height, Height - dy);
        for (int y = 0; y < h; y++)
        {
            if (dy + y < 0)
                continue;
            for (int x = 0; x < w; x++)
            {
                if (dx + x < 0)
                    continue;
                _pixels[(dy + y) * Width + (dx + x)] = src._pixels[y * src.Width + x];
            }
        }
        _dirty = true;
    }

    // Async yukleme handle'i: pikseller gelene kadar 1x1 beyaz placeholder cizer.
    internal static Texture CreatePending()
    {
        var t = FromColor(1, 1, Color.White);
        t.Loading = true;
        return t;
    }

    // Worker'dan gelen decode edilmis RGBA8 pikselleri baglar (main thread).
    internal void _AttachPixels(IntPtr rgba, int w, int h)
    {
        ReleaseGpu(); // 1x1 placeholder GPU nesneleri (varsa) gider; boyut degisiyor
        if (_pixels != null)
            System.Runtime.InteropServices.Marshal.FreeHGlobal((IntPtr)_pixels);
        long bytes = (long)w * h * 4;
        _pixels = (Color*)System.Runtime.InteropServices.Marshal.AllocHGlobal((IntPtr)bytes);
        Buffer.MemoryCopy((void*)rgba, _pixels, bytes, bytes);
        Width = w;
        Height = h;
        _dirty = true;
        Loading = false;
    }
}