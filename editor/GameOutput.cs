using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Oyunun PAYLASILAN render hedefi: oyun sahnesi frame'de BIR kez buraya cizilir,
// Game View gosterir. Boyut iki modda: panel takibi (frame'in ilk isteyen Game
// View'u belirler) ya da CIHAZ PROFILI (fiziksel w/h + pixelRatio dayatilir,
// panel letterbox gosterir — AI/otomasyon icin deterministik koordinat uzayi).
static unsafe class GameOutput
{
    const string Lib = "digitoyengine_native";

    [DllImport(Lib, EntryPoint = "de_sokol_read_image")]
    static extern int ReadImage(uint img, int w, int h, void* rgba);

    public readonly struct Device
    {
        public readonly string Name;
        public readonly int W, H;          // fiziksel px (portre)
        public readonly float PixelRatio;
        public Device(string name, int w, int h, float pr) { Name = name; W = w; H = h; PixelRatio = pr; }
    }

    // Preset'ler fiziksel cozunurluk + pixelRatio; mantiksal = fiziksel / pixelRatio.
    public static readonly Device[] Devices =
    {
        new("iphone-se", 750, 1334, 2f),
        new("iphone-14", 1170, 2532, 3f),
        new("iphone-14-pro-max", 1290, 2796, 3f),
        new("pixel-7", 1080, 2400, 2.625f),
        new("galaxy-s23", 1080, 2340, 3f),
        new("ipad", 1620, 2160, 2f),
        new("ipad-pro-12", 2048, 2732, 2f),
        new("desktop-1080p", 1920, 1080, 1f),
    };

    public static Device? FindDevice(string name)
    {
        foreach (var d in Devices)
            if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    static Texture _rt;
    static Texture _retired; // frame ortasi destroy yasak: sonraki frame basinda olur
    static int _rtW, _rtH;
    static bool _sizedThisFrame;

    // Dayatilan profil (0 = panel takibi).
    static int _forcedW, _forcedH;
    static float _forcedRatio = 1f;
    public static bool Forced => _forcedW > 0;
    public static int ForcedW => _forcedW;
    public static int ForcedH => _forcedH;
    public static string ForcedName { get; private set; } = "";

    // Oyunun Screen.width/height'i (mantiksal).
    public static float ViewW { get; private set; } = 800f;
    public static float ViewH { get; private set; } = 600f;
    // Oyunun Screen.pixelRatio'su: profilde cihazin, degilse editor penceresinin.
    public static float PixelRatio => Forced ? _forcedRatio : (Gui.Scale > 0 ? Gui.Scale : 1f);
    public static int PhysicalW => _rtW;
    public static int PhysicalH => _rtH;

    // w/h fiziksel px; 0,0 = panel takibine don. Landscape icin w>h ver.
    public static void SetDevice(int w, int h, float pixelRatio, string name = "")
    {
        if (w <= 0 || h <= 0)
        {
            _forcedW = _forcedH = 0;
            _forcedRatio = 1f;
            ForcedName = "";
            return;
        }
        _forcedW = Math.Clamp(w, 8, 8192);
        _forcedH = Math.Clamp(h, 8, 8192);
        _forcedRatio = pixelRatio > 0 ? pixelRatio : 1f;
        ForcedName = name ?? "";
    }

    public static Texture EnsureTarget()
    {
        if (_rt == null)
            Resize(800, 600);
        return _rt;
    }

    public static void BeginFrame()
    {
        _retired?.Destroy();
        _retired = null;
        _sizedThisFrame = false;
        // Profil panelden bagimsiz uygulanir (Game sekmesi gizliyken de boyut dogru).
        if (Forced && !RenderDebug.Frozen)
        {
            _sizedThisFrame = true;
            ViewW = _forcedW / _forcedRatio;
            ViewH = _forcedH / _forcedRatio;
            if (_forcedW != _rtW || _forcedH != _rtH)
                Resize(_forcedW, _forcedH);
        }
    }

    // Frame'in ilk istegi kazanir (surucu viewport). w/h mantiksal panel boyutu.
    public static void RequestSize(float w, float h)
    {
        // Frozen capture eski RT view'larini replay eder: resize = destroy = olu handle.
        if (_sizedThisFrame || w < 8 || h < 8 || RenderDebug.Frozen)
            return;
        _sizedThisFrame = true;
        ViewW = w;
        ViewH = h;
        float s = Gui.Scale > 0 ? Gui.Scale : 1f;
        int pw = Math.Max(8, (int)(w * s));
        int ph = Math.Max(8, (int)(h * s));
        if (pw != _rtW || ph != _rtH)
            Resize(pw, ph);
    }

    static void Resize(int w, int h)
    {
        _retired ??= _rt;
        _rt = Texture.CreateRenderTarget(w, h);
        _rtW = w;
        _rtH = h;
    }

    // RT'yi okuyup PNG yazar (pass disinda cagrilmali — RPC pump frame basinda).
    // scale: mantiksal boyuta carpan (1 = Screen.width x height; PixelRatio = tam fiziksel).
    // Kutu filtreli kucultme; cikti RGB (alfa atilir). Donus: yazilan boyut.
    public static (int w, int h) Screenshot(string path, float scale)
    {
        var rt = EnsureTarget();
        int sw = _rtW, sh = _rtH;
        int dw = Math.Max(1, (int)MathF.Round(ViewW * scale));
        int dh = Math.Max(1, (int)MathF.Round(ViewH * scale));
        byte* src = (byte*)NativeMemory.Alloc((nuint)(sw * sh * 4));
        try
        {
            if (ReadImage(rt.ImageId, sw, sh, src) == 0)
                throw new InvalidOperationException("GPU okuma desteklenmiyor (yalniz GL backend)");
            var rgb = new byte[dw * dh * 3];
            float kx = (float)sw / dw, ky = (float)sh / dh;
            for (int y = 0; y < dh; y++)
            {
                int y0 = (int)(y * ky), y1 = Math.Max(y0 + 1, (int)((y + 1) * ky));
                for (int x = 0; x < dw; x++)
                {
                    int x0 = (int)(x * kx), x1 = Math.Max(x0 + 1, (int)((x + 1) * kx));
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int yy = y0; yy < y1 && yy < sh; yy++)
                    {
                        // GL satir 0 = alt: ust satirdan okumak icin cevir.
                        byte* row = src + ((sh - 1 - yy) * sw) * 4;
                        for (int xx = x0; xx < x1 && xx < sw; xx++)
                        {
                            byte* p = row + xx * 4;
                            r += p[0]; g += p[1]; b += p[2]; n++;
                        }
                    }
                    int o = (y * dw + x) * 3;
                    if (n > 0) { rgb[o] = (byte)(r / n); rgb[o + 1] = (byte)(g / n); rgb[o + 2] = (byte)(b / n); }
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using var fs = File.Create(path);
            PngWriter.WriteRgb(fs, dw, dh, rgb);
        }
        finally
        {
            NativeMemory.Free(src);
        }
        return (dw, dh);
    }
}
