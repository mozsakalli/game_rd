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

    // Game View tercihleri (EditorSettings'e kalici): cihaz yon zorlamasi (Auto =
    // PlayerSettings.orientation) ve panelde 1:1 gosterim (false = sigdir).
    public static ScreenOrientation Orientation = ScreenOrientation.Auto;
    public static bool OneToOne;

    // Preset'i yon tercihine gore uygular (RPC view.device + Game View combo ayni
    // bogaz). Donus: landscape mi.
    public static bool ApplyDevice(in Device dv)
    {
        var o = Orientation == ScreenOrientation.Auto
            ? App.Project?.Player.orientation ?? ScreenOrientation.Auto
            : Orientation;
        bool portraitPreset = dv.H >= dv.W;
        bool landscape = o switch
        {
            ScreenOrientation.Landscape => true,
            ScreenOrientation.Portrait => false,
            _ => !portraitPreset,
        };
        bool swap = landscape == portraitPreset; // preset yonu istenen yonle uyusmuyorsa cevir
        SetDevice(swap ? dv.H : dv.W, swap ? dv.W : dv.H, dv.PixelRatio, dv.Name);
        return landscape;
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

    // Kaba renk izgarasi (render.describe pixels=N): cols sutun x orana gore satir, hucre = kutu ortalamasi
    // "rrggbb". Piksel okumadan "ekran bos mu / parlak bolge nerede" sorusuna metin cevap.
    public static string[] PixelGrid(int cols)
    {
        var rt = EnsureTarget();
        int sw = _rtW, sh = _rtH;
        cols = Math.Clamp(cols, 1, 128);
        int rows = Math.Clamp((int)MathF.Round(cols * sh / (float)sw), 1, 128);
        byte* src = (byte*)NativeMemory.Alloc((nuint)(sw * sh * 4));
        try
        {
            if (ReadImage(rt.ImageId, sw, sh, src) == 0)
                throw new InvalidOperationException("GPU okuma desteklenmiyor (yalniz GL backend)");
            var lines = new string[rows];
            var sb = new System.Text.StringBuilder(cols * 7);
            float kx = (float)sw / cols, ky = (float)sh / rows;
            for (int y = 0; y < rows; y++)
            {
                sb.Clear();
                int y0 = (int)(y * ky), y1 = Math.Max(y0 + 1, (int)((y + 1) * ky));
                for (int x = 0; x < cols; x++)
                {
                    int x0 = (int)(x * kx), x1 = Math.Max(x0 + 1, (int)((x + 1) * kx));
                    long r = 0, g = 0, b = 0; int n = 0;
                    for (int yy = y0; yy < y1 && yy < sh; yy++)
                    {
                        byte* row = src + ((sh - 1 - yy) * sw) * 4; // GL satir 0 = alt
                        for (int xx = x0; xx < x1 && xx < sw; xx++)
                        {
                            byte* p = row + xx * 4;
                            r += p[0]; g += p[1]; b += p[2]; n++;
                        }
                    }
                    if (x > 0) sb.Append(' ');
                    if (n > 0) sb.Append((r / n).ToString("x2")).Append((g / n).ToString("x2")).Append((b / n).ToString("x2"));
                    else sb.Append("000000");
                }
                lines[y] = sb.ToString();
            }
            return lines;
        }
        finally
        {
            NativeMemory.Free(src);
        }
    }

    // Screenshot istegi/sonucu. Region MANTIKSAL koordinat (sol-ust, y asagi); null = tum ekran.
    // MaxSize: uzun kenar siniri (0 = sinirsiz) — AI'in gorsel token maliyeti piksel sayisiyla olceklenir,
    // o yuzden varsayilan kucultulmus jpg. Cikti olcegi = Scale, MaxSize asilirsa dusurulur.
    public struct ShotRequest
    {
        public float Scale;
        public int MaxSize;
        public bool Jpg;
        public int Quality;
        public float RegionX, RegionY, RegionW, RegionH; // RegionW<=0 → tum ekran
    }

    public struct ShotResult
    {
        public int W, H;          // yazilan goruntu
        public int Bytes;
        public float RegionX, RegionY, RegionW, RegionH; // kirpilmis (mantiksal)
        public float ToScreen;    // tiklama = RegionX + px * ToScreen
    }

    // RT'yi okuyup PNG/JPG yazar (pass disinda cagrilmali — RPC pump frame basinda).
    // Kutu filtreli kucultme; cikti RGB (alfa atilir). Jpg = native stb encoder; Png = PngWriter (daha kucuk).
    public static ShotResult Screenshot(string path, in ShotRequest req)
    {
        var rt = EnsureTarget();
        int sw = _rtW, sh = _rtH;
        float vw = ViewW, vh = ViewH;
        // Bolge: mantiksal → kirp.
        float rx = 0, ry = 0, rw = vw, rh = vh;
        if (req.RegionW > 0 && req.RegionH > 0)
        {
            rx = Math.Clamp(req.RegionX, 0, Math.Max(0, vw - 1));
            ry = Math.Clamp(req.RegionY, 0, Math.Max(0, vh - 1));
            rw = Math.Clamp(req.RegionW, 1, vw - rx);
            rh = Math.Clamp(req.RegionH, 1, vh - ry);
        }
        float eff = req.Scale;
        if (req.MaxSize > 0)
        {
            float longEdge = Math.Max(rw, rh) * eff;
            if (longEdge > req.MaxSize)
                eff *= req.MaxSize / longEdge;
        }
        int dw = Math.Max(1, (int)MathF.Round(rw * eff));
        int dh = Math.Max(1, (int)MathF.Round(rh * eff));
        // Mantiksal → RT pikseli (RT fiziksel boyutta).
        float px = sw / vw, py = sh / vh;
        float sx0 = rx * px, sy0 = ry * py;
        float kx = rw * px / dw, ky = rh * py / dh;
        byte* src = (byte*)NativeMemory.Alloc((nuint)(sw * sh * 4));
        try
        {
            if (ReadImage(rt.ImageId, sw, sh, src) == 0)
                throw new InvalidOperationException("GPU okuma desteklenmiyor (yalniz GL backend)");
            var rgb = new byte[dw * dh * 3];
            for (int y = 0; y < dh; y++)
            {
                int y0 = (int)(sy0 + y * ky), y1 = Math.Max(y0 + 1, (int)(sy0 + (y + 1) * ky));
                for (int x = 0; x < dw; x++)
                {
                    int x0 = (int)(sx0 + x * kx), x1 = Math.Max(x0 + 1, (int)(sx0 + (x + 1) * kx));
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
            int bytes;
            if (req.Jpg)
            {
                var enc = Sokol.ImageEncode(rgb, dw, dh, 3, jpg: true, req.Quality, flip: false)
                    ?? throw new InvalidOperationException("jpg kodlama basarisiz");
                File.WriteAllBytes(path, enc);
                bytes = enc.Length;
            }
            else
            {
                using var fs = File.Create(path);
                PngWriter.WriteRgb(fs, dw, dh, rgb);
                bytes = (int)fs.Length;
            }
            return new ShotResult
            {
                W = dw, H = dh, Bytes = bytes,
                RegionX = rx, RegionY = ry, RegionW = rw, RegionH = rh,
                ToScreen = rw / dw,
            };
        }
        finally
        {
            NativeMemory.Free(src);
        }
    }
}
