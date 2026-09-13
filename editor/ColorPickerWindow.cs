using System;
using DigitoyEngine;

namespace DigitoyEditor;

// Unity Color Picker modeli: Inspector'daki renk swatch'ina tiklaninca imlecin
// yaninda FLOATING native popup acilir (dock'a girmez); focus kaybedince kapanir.
// Baglanti IMGUI usulu: alan sahibi her frame TryGet(key) ile guncel rengi ceker,
// commit/undo mevcut SetProp/asset-save boğazlarından akar.
public static unsafe class ColorPickerWindow
{
    // Aktif baglam: hangi alan (key) duzenleniyor. HSV tutulur ki s/v=0'da hue kaybolmasin.
    static string _owner;
    static float _h;      // 0..360
    static float _s, _v;  // 0..1
    static byte _a = 255;

    const float LogicalW = 240f;
    const float LogicalH = 246f;

    static NativeWindow _win;
    static Rect _winRect;
    static bool _wasFocused;
    static int _openFrames;
    static readonly GuiHost.GuiFunc _drawFunc = Draw;

    static readonly int _svHash = "ColorPicker.SV".GetHashCode();
    static readonly int _hueHash = "ColorPicker.Hue".GetHashCode();
    static readonly int _alphaHash = "ColorPicker.Alpha".GetHashCode();

    static char[] _hexBuf = new char[16];
    static int _hexLen;

    // SV karesi tek quad'la cizilmez: bilinear SV yuzeyi GPU'nun ucgen-bazli
    // interpolasyonunda kosegen dikis yapar. Unity gibi hue degisince CPU'da
    // yeniden pisirilen dinamik doku kullanilir.
    const int SvTexSize = 128;
    static Texture _svTex;
    static float _bakedHue = float.NaN;
    static readonly Color[] _svPixels = new Color[SvTexSize * SvTexSize];

    static void EnsureSvTexture()
    {
        _svTex ??= Texture.CreateDynamicRgba(SvTexSize, SvTexSize);
        if (_bakedHue == _h)
            return;
        _bakedHue = _h;
        // GL doku: satir 0 = v texcoord 0 = ekranin ALTI -> satir y'nin value'su y/(h-1).
        for (int y = 0; y < SvTexSize; y++)
        {
            float v = y / (float)(SvTexSize - 1);
            for (int x = 0; x < SvTexSize; x++)
                _svPixels[y * SvTexSize + x] =
                    HsvToColor(_h, x / (float)(SvTexSize - 1), v, 255);
        }
        fixed (Color* px = _svPixels)
            _svTex.UpdateRgba(px);
    }

    public static void Open(string key, Color initial)
    {
        _owner = key;
        ColorToHsv(initial, ref _h, ref _s, ref _v);
        _a = initial.a;
        _wasFocused = false;
        _openFrames = 0;

        // Imlec ekran konumu: pencere pos + pencere-lokal imlec (ikisi de fiziksel px).
        IntPtr main = NativeWindow.MainWindow;
        GLFW.GetWindowPos(main, out int wx, out int wy);
        GLFW.GetCursorPos(main, out double cx, out double cy);
        GLFW.GetWindowContentScale(main, out float cs, out _);
        if (cs <= 0) cs = 1f;
        int px = wx + (int)cx + 12, py = wy + (int)cy + 12;
        int pw = (int)(LogicalW * cs), ph = (int)(LogicalH * cs);

        if (_win != null)
        {
            GLFW.SetWindowPos(_win.Handle, px, py);
            GLFW.FocusWindow(_win.Handle);
            return;
        }
        // Yalniz bu cagrinin hint'leri elle geri alinir: DefaultWindowHints
        // GL context hint'lerini de sifirlar (sonraki detach pencereleri bozulur).
        GLFW.WindowHint(GLFWConst.DECORATED, GLFWConst.FALSE);
        GLFW.WindowHint(GLFWConst.FLOATING, GLFWConst.TRUE);
        GLFW.WindowHint(GLFWConst.RESIZABLE, GLFWConst.FALSE);
        _win = NativeWindow.Open("Color", px, py, pw, ph);
        GLFW.WindowHint(GLFWConst.DECORATED, GLFWConst.TRUE);
        GLFW.WindowHint(GLFWConst.FLOATING, GLFWConst.FALSE);
        GLFW.WindowHint(GLFWConst.RESIZABLE, GLFWConst.TRUE);
        if (_win != null)
        {
            _win.Camera.BackgroundColor = new Color(32, 34, 40, 255);
            GLFW.FocusWindow(_win.Handle);
        }
    }

    public static void Close()
    {
        _owner = null;
        if (_win == null)
            return;
        _win.Destroy();
        _win = null;
    }

    // Alan sahibi frame'de guncel degeri ceker; picker bu alani duzenlemiyorsa false.
    public static bool TryGet(string key, out Color color)
    {
        if (_owner != key)
        {
            color = default;
            return false;
        }
        color = HsvToColor(_h, _s, _v, _a);
        return true;
    }

    // EditorApp loop: ana GUI turundan sonra (GuiDock.UpdateWindows yaninda).
    public static void UpdateWindow()
    {
        if (_win == null)
            return;
        _openFrames++;
        bool focused = GLFW.GetWindowAttrib(_win.Handle, GLFWConst.FOCUSED) != 0;
        if (focused)
            _wasFocused = true;
        // Focus kaybi = kapan (popup davranisi). Acilis frame'lerinde focus henuz
        // gelmemis olabilir — kisa tolerans.
        if (_win.ShouldClose || (!focused && (_wasFocused || _openFrames > 15)))
        {
            Close();
            return;
        }
        if (!_win.UpdateSize())
            return; // minimize
        EnsureSvTexture(); // GUI pass'lerinden once, frame'de bir kez (dinamik image kurali)
        GLFW.GetWindowContentScale(_win.Handle, out float ps, out _);
        if (ps <= 0) ps = 1f;
        float lw = _win.Width / ps, lh = _win.Height / ps;
        _win.Camera.SetPixelOrtho((int)lw, (int)lh);
        GuiRenderer.Queue = _win.Camera.Queue;
        _winRect = new Rect(0, 0, lw, lh);
        _win.Gui.Frame(_win.Handle, _winRect, _drawFunc, ps);
    }

    public static void Encode(CommandBuffer cb)
    {
        if (_win != null)
            _win.Camera.Encode(cb, _win.Width, _win.Height);
    }

    public static void Present() => _win?.Present();

    static void Draw()
    {
        Event ev = Event.Current;
        Color rgb = HsvToColor(_h, _s, _v, 255);

        if (ev.Type == EventType.Repaint) // dekorasyonsuz pencereye ince cerceve
        {
            GuiRenderer.DrawRect(_winRect, new Color(70, 73, 84, 255), 0);
            GuiRenderer.DrawRect(new Rect(1, 1, _winRect.width - 2, _winRect.height - 2),
                new Color(32, 34, 40, 255), 0);
        }

        var svRect = new Rect(10, 10, 168, 168);
        var hueRect = new Rect(186, 10, 18, 168);
        var alphaRect = new Rect(212, 10, 18, 168);

        // --- SV karesi (TL=beyaz TR=hue BR/BL=siyah: bilinear mix tam SV duzlemi) ---
        int svId = GuiUtility.GetControlID(_svHash, FocusType.Passive);
        switch (ev.GetTypeForControl(svId))
        {
            case EventType.MouseDown:
                if (svRect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = svId;
                    ApplySv(svRect, ev.MousePosition);
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == svId) { ApplySv(svRect, ev.MousePosition); ev.Use(); }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == svId) { GuiUtility.HotControl = 0; ev.Use(); }
                break;
            case EventType.Repaint:
                GuiRenderer.DrawTexture(svRect, _svTex, 1);
                var cursor = new Vec2(svRect.x + _s * svRect.width,
                    svRect.y + (1f - _v) * svRect.height);
                GuiRenderer.DrawDiamond(cursor, 4.5f, _v > 0.6f && _s < 0.5f
                    ? Color.Black : Color.White, 2);
                GuiRenderer.DrawDiamond(cursor, 2.5f, rgb, 3);
                break;
        }

        // --- Hue seridi (dikey, 6 gradyan segmenti) ---
        int hueId = GuiUtility.GetControlID(_hueHash, FocusType.Passive);
        switch (ev.GetTypeForControl(hueId))
        {
            case EventType.MouseDown:
                if (hueRect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = hueId;
                    ApplyBar(hueRect, ev.MousePosition.y, t => _h = t * 360f);
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == hueId)
                {
                    ApplyBar(hueRect, ev.MousePosition.y, t => _h = t * 360f);
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == hueId) { GuiUtility.HotControl = 0; ev.Use(); }
                break;
            case EventType.Repaint:
                float segH = hueRect.height / 6f;
                for (int i = 0; i < 6; i++)
                {
                    Color top = HsvToColor(i * 60f, 1f, 1f, 255);
                    Color bottom = HsvToColor((i + 1) * 60f, 1f, 1f, 255);
                    var seg = new Rect(hueRect.x, hueRect.y + i * segH, hueRect.width, segH);
                    GuiRenderer.DrawRectGradient(seg, top, top, bottom, bottom, 1);
                }
                DrawBarMarker(hueRect, _h / 360f);
                break;
        }

        // --- Alfa seridi (dikey: ustte opak renk, altta seffaf) ---
        int alphaId = GuiUtility.GetControlID(_alphaHash, FocusType.Passive);
        switch (ev.GetTypeForControl(alphaId))
        {
            case EventType.MouseDown:
                if (alphaRect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = alphaId;
                    ApplyBar(alphaRect, ev.MousePosition.y, t => _a = (byte)((1f - t) * 255f + 0.5f));
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == alphaId)
                {
                    ApplyBar(alphaRect, ev.MousePosition.y, t => _a = (byte)((1f - t) * 255f + 0.5f));
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == alphaId) { GuiUtility.HotControl = 0; ev.Use(); }
                break;
            case EventType.Repaint:
                GuiRenderer.DrawRect(alphaRect, new Color(35, 37, 44, 255), 1);
                GuiRenderer.DrawRectGradient(alphaRect,
                    new Color(rgb.r, rgb.g, rgb.b, 255), new Color(rgb.r, rgb.g, rgb.b, 255),
                    new Color(rgb.r, rgb.g, rgb.b, 0), new Color(rgb.r, rgb.g, rgb.b, 0), 2);
                DrawBarMarker(alphaRect, 1f - _a / 255f);
                break;
        }

        // --- RGBA drag alanlari ---
        float y = 188;
        Color current = HsvToColor(_h, _s, _v, _a);
        byte r = ChannelDrag(10, y, "R", current.r);
        byte g = ChannelDrag(66, y, "G", current.g);
        byte b = ChannelDrag(122, y, "B", current.b);
        byte a2 = ChannelDrag(178, y, "A", current.a);
        if (r != current.r || g != current.g || b != current.b)
            ColorToHsv(new Color(r, g, b, 255), ref _h, ref _s, ref _v);
        _a = a2;

        // --- Hex (rrggbbaa) ---
        y += 26;
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(10, y, 30, 18), "Hex", Gui.FontSize - 3f,
                new Color(165, 168, 178, 255), false, 2);
        Color now = HsvToColor(_h, _s, _v, _a);
        if (GuiUtility.KeyboardControl == 0)
            WriteHex(now); // odak yokken buffer daima guncel renk
        Gui.TextField(new Rect(44, y, 116, 18), ref _hexBuf, ref _hexLen);
        if (TryParseHex(_hexBuf, _hexLen, now, out Color hexColor)
            && !Same(hexColor, now))
        {
            ColorToHsv(hexColor, ref _h, ref _s, ref _v);
            _a = hexColor.a;
        }

        // --- Onizleme ---
        var preview = new Rect(170, y, 60, 18);
        if (ev.Type == EventType.Repaint && _owner != null)
        {
            GuiRenderer.DrawRect(preview, new Color(20, 21, 26, 255), 1);
            GuiRenderer.DrawRect(new Rect(preview.x + 1, preview.y + 1,
                preview.width - 2, preview.height - 7), new Color(now.r, now.g, now.b, 255), 2);
            var abg = new Rect(preview.x + 1, preview.yMax - 5, preview.width - 2, 4);
            GuiRenderer.DrawRect(abg, Color.Black, 2);
            GuiRenderer.DrawRect(new Rect(abg.x, abg.y, abg.width * now.a / 255f, 4),
                Color.White, 3);
        }
    }

    static void ApplySv(in Rect rect, Vec2 mouse)
    {
        _s = Clamp01((mouse.x - rect.x) / rect.width);
        _v = 1f - Clamp01((mouse.y - rect.y) / rect.height);
    }

    static void ApplyBar(in Rect rect, float mouseY, Action<float> set)
        => set(Clamp01((mouseY - rect.y) / rect.height));

    static byte ChannelDrag(float x, float y, string label, byte value)
    {
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(x, y, 12, 18), label, Gui.FontSize - 3f,
                new Color(165, 168, 178, 255), false, 2);
        return (byte)Gui.DragInt(new Rect(x + 14, y, 38, 18), value, 1f, 0, 255);
    }

    static void DrawBarMarker(in Rect bar, float t)
    {
        float my = bar.y + Clamp01(t) * bar.height;
        GuiRenderer.DrawRect(new Rect(bar.x - 1, my - 2, bar.width + 2, 4), Color.Black, 3);
        GuiRenderer.DrawRect(new Rect(bar.x - 1, my - 1, bar.width + 2, 2), Color.White, 4);
    }

    static void WriteHex(Color c)
    {
        Span<char> hex = stackalloc char[8];
        WriteByteHex(hex, 0, c.r);
        WriteByteHex(hex, 2, c.g);
        WriteByteHex(hex, 4, c.b);
        WriteByteHex(hex, 6, c.a);
        hex.CopyTo(_hexBuf);
        _hexLen = 8;
    }

    static void WriteByteHex(Span<char> dst, int at, byte v)
    {
        const string digits = "0123456789abcdef";
        dst[at] = digits[v >> 4];
        dst[at + 1] = digits[v & 15];
    }

    // 6 (rgb, alfa korunur) veya 8 (rgba) hex hane; opsiyonel '#'.
    static bool TryParseHex(char[] buf, int len, Color fallback, out Color color)
    {
        color = default;
        int start = len > 0 && buf[0] == '#' ? 1 : 0;
        int n = len - start;
        if (n != 6 && n != 8)
            return false;
        Span<byte> parts = stackalloc byte[4];
        parts[3] = fallback.a;
        for (int i = 0; i < n / 2; i++)
        {
            int hi = HexDigit(buf[start + i * 2]);
            int lo = HexDigit(buf[start + i * 2 + 1]);
            if (hi < 0 || lo < 0)
                return false;
            parts[i] = (byte)((hi << 4) | lo);
        }
        color = new Color(parts[0], parts[1], parts[2], parts[3]);
        return true;
    }

    static int HexDigit(char c)
        => c >= '0' && c <= '9' ? c - '0'
        : c >= 'a' && c <= 'f' ? c - 'a' + 10
        : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

    static bool Same(Color a, Color b)
        => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    public static Color HsvToColor(float h, float s, float v, byte a)
    {
        h = (h % 360f + 360f) % 360f;
        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;
        (float rf, float gf, float bf) = (int)(h / 60f) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return new Color((byte)((rf + m) * 255f + 0.5f), (byte)((gf + m) * 255f + 0.5f),
            (byte)((bf + m) * 255f + 0.5f), a);
    }

    // Dejenere durumlarda (gri/siyah) mevcut hue/sat korunur (Unity davranisi).
    public static void ColorToHsv(Color c, ref float h, ref float s, ref float v)
    {
        float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;
        v = max;
        if (max > 0f)
            s = delta / max;
        if (delta > 0f)
        {
            if (max == r) h = 60f * ((g - b) / delta % 6f);
            else if (max == g) h = 60f * ((b - r) / delta + 2f);
            else h = 60f * ((r - g) / delta + 4f);
            if (h < 0f) h += 360f;
        }
    }
}
