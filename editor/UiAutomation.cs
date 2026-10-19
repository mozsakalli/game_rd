using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Editor GUI otomasyonu (RPC ui.*; docs/engine-notes.md LOCALIZATION F3): sentetik girdi GLFW callback'iyle
// AYNI kuyruga (GuiInput.Inject) girer, IMGUI farki bilmez. Adimlar FRAME'E yayilir (down ve up ayri
// pass'lerde — gercek fare gibi); Busy iken RPC pompasi bekler, yani ui.click'ten sonraki komut jest
// bitince kosar. Koordinat: ana pencere MANTIKSAL px (ui.screenshot pikseli == koordinat). Acik GuiPopup
// (combo listesi) varsa icine dusen tiklamalar popup penceresine yonlenir. Insan girdisi gorulurse
// (tus/tik) kuyruk atilir ("Engaged" biter) — iki el ayni direksiyonda olmasin.
static unsafe class UiAutomation
{
    const string Lib = "digitoyengine_native";

    [DllImport(Lib, EntryPoint = "de_sokol_read_fbo")]
    static extern int ReadFbo(uint fbo, int w, int h, void* rgba);

    struct Step
    {
        public GuiInput.Queued Ev;
        public bool Popup;     // popup penceresine (Ev.Mouse popup px)
        public IntPtr Target;  // hedef native pencere (Zero = ana pencere; utility/detached: ui.* window parametresi)
        public int WaitAfter;  // bu adimdan sonra kac frame beklensin
        public Action Run;     // olay yerine kod adimi (semantik jestler: surukle-birak baslat/bitir)
    }

    static readonly Queue<Step> _steps = new();
    static int _wait;
    static IntPtr _window;
    static float _mouseScale = 1f;
    static Vec2 _lastLogical;
    static bool _synthDown, _holding;

    // Sonraki jestlerin hedef penceresi (RPC ui.* window parametresi): Zero = ana pencere. Koordinat o pencerenin
    // mantiksal px'i; fare bolen'i o pencerenin ScreenScale'i.
    static IntPtr _target;
    static float _targetScale = 1f;

    public static void SetTarget(IntPtr window)
    {
        _target = window;
        _targetScale = window == IntPtr.Zero ? _mouseScale : NativeWindow.ScreenScale(window);
    }

    static float Scale => _target == IntPtr.Zero ? _mouseScale : _targetScale;

    // DragDrop.EndFrame icin "fare birakik mi": sentetik jest surerken fiziksel fare degil, sentetik durum gecerli
    // (fiziksel fare otomasyonda hep birakiktir; eski kod yuku ilk frame'de temizliyordu).
    public static bool MouseReleased(bool physicalReleased)
        => Busy || Engaged ? !_synthDown && !_holding : physicalReleased;

    // Sentetik girdi akisi suruyor mu / son girdi sentetik mi (RPC pompasi klavye odagi varken de kossun).
    public static bool Busy => _steps.Count > 0 || _wait > 0 || _shotPath != null;
    public static bool Engaged { get; private set; }

    // EditorApp: her frame guiHost.Frame'den ONCE. mouseScale = pencere px / mantiksal.
    public static void Tick(IntPtr window, float mouseScale)
    {
        _window = window;
        _mouseScale = mouseScale;
        if (GuiInput.HumanInputSeen)
        {
            GuiInput.HumanInputSeen = false;
            if (Engaged)
            {
                _steps.Clear();
                _wait = 0;
                Engaged = false;
            }
        }
        if (_wait > 0) { _wait--; return; }
        if (_steps.Count == 0)
            return;
        var st = _steps.Dequeue();
        Engaged = true;
        if (st.Run != null)
        {
            st.Run();
            _wait = st.WaitAfter;
            return;
        }
        if (st.Ev.Button == 0 && st.Ev.Type == EventType.MouseDown) _synthDown = true;
        if (st.Ev.Button == 0 && st.Ev.Type == EventType.MouseUp) _synthDown = false;
        if (st.Popup)
            GuiInput.Inject(GuiPopup.Handle, st.Ev);
        else
            GuiInput.Inject(st.Target != IntPtr.Zero ? st.Target : window, st.Ev);
        _wait = st.WaitAfter;
    }

    // --- adim uretimi ---

    static void Mouse(EventType type, Vec2 logical, int button, int wait, EventModifiers mods = EventModifiers.None, int clicks = 1)
    {
        var ev = new GuiInput.Queued { Type = type, Button = button, ClickCount = clicks, Mods = mods };
        Vec2 ppx = default;
        bool popup = _target == IntPtr.Zero && GuiPopup.Active && GuiPopup.TryMapFromHost(_window, logical, out ppx);
        float k = Scale;
        if (popup)
            ev.Mouse = ppx;
        else
            ev.Mouse = new Vec2(logical.x * k, logical.y * k);
        if (type is EventType.MouseMove or EventType.MouseDrag)
            ev.Delta = new Vec2((logical.x - _lastLogical.x) * k, (logical.y - _lastLogical.y) * k);
        _lastLogical = logical;
        _steps.Enqueue(new Step { Ev = ev, Popup = popup, Target = _target, WaitAfter = wait });
    }

    public static void Click(float x, float y, int button = 0, int clicks = 1, EventModifiers mods = EventModifiers.None)
    {
        var p = new Vec2(x, y);
        Mouse(EventType.MouseMove, p, 0, 1);
        for (int i = 0; i < Math.Max(1, clicks); i++)
        {
            Mouse(EventType.MouseDown, p, button, 1, mods, i + 1);
            Mouse(EventType.MouseUp, p, button, 1, mods, i + 1);
        }
        _wait = Math.Max(_wait, 0);
        _steps.Enqueue(new Step { Ev = new GuiInput.Queued { Type = EventType.MouseMove, Mouse = new Vec2(x * Scale, y * Scale) }, Target = _target, WaitAfter = 2 });
    }

    public static void Drag(float x0, float y0, float x1, float y1, int steps = 8, int button = 0)
    {
        Mouse(EventType.MouseMove, new Vec2(x0, y0), 0, 1);
        Mouse(EventType.MouseDown, new Vec2(x0, y0), button, 1);
        steps = Math.Clamp(steps, 1, 60);
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Mouse(EventType.MouseDrag, new Vec2(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t), button, 1);
        }
        Mouse(EventType.MouseUp, new Vec2(x1, y1), button, 2);
    }

    public static bool LastDropApplied { get; private set; }

    // SEMANTIK surukle-birak: koordinat hesabi yok. Yuk dogrudan DragDrop'a konur, imlec hedef dikdortgenin
    // merkezine tasinir (hedef alan kendini repaint'te kaydeder), bir frame sonra birakma uygulanir.
    public static void DropAsset(string guidKey, string pathKey, Rect targetGlobal)
    {
        var c = new Vec2(targetGlobal.x + targetGlobal.width * 0.5f, targetGlobal.y + targetGlobal.height * 0.5f);
        _steps.Enqueue(new Step { Run = () => { _holding = true; LastDropApplied = false; DragDrop.BeginAsset(guidKey, pathKey); }, WaitAfter = 0 });
        Mouse(EventType.MouseMove, c, 0, 2);
        Mouse(EventType.MouseMove, c, 0, 2);
        _steps.Enqueue(new Step { Run = () => { _holding = false; LastDropApplied = DragDrop.ApplyNow(); if (!LastDropApplied) DragDrop.Clear(); }, WaitAfter = 2 });
    }

    public static void Scroll(float x, float y, float dy)
    {
        Mouse(EventType.MouseMove, new Vec2(x, y), 0, 1);
        var ev = new GuiInput.Queued { Type = EventType.ScrollWheel, Mouse = new Vec2(x * Scale, y * Scale), Delta = new Vec2(0, dy) };
        _steps.Enqueue(new Step { Ev = ev, Target = _target, WaitAfter = 2 });
    }

    // Metin: her karakter TextInput (odaktaki TextField alir); '\n' = Enter tusu.
    public static void Type(string text)
    {
        foreach (var ch in text)
        {
            if (ch == '\n') { Key(GLFWConst.KEY_ENTER, EventModifiers.None); continue; }
            var ev = new GuiInput.Queued { Type = EventType.TextInput, Character = ch, Mouse = CurrentMousePx() };
            _steps.Enqueue(new Step { Ev = ev, Target = _target, WaitAfter = 0 });
        }
        _steps.Enqueue(new Step { Ev = new GuiInput.Queued { Type = EventType.MouseMove, Mouse = CurrentMousePx() }, Target = _target, WaitAfter = 1 });
    }

    public static void Key(int key, EventModifiers mods)
    {
        _steps.Enqueue(new Step { Ev = new GuiInput.Queued { Type = EventType.KeyDown, Key = key, Mods = mods, Mouse = CurrentMousePx() }, Target = _target, WaitAfter = 1 });
        _steps.Enqueue(new Step { Ev = new GuiInput.Queued { Type = EventType.KeyUp, Key = key, Mods = mods, Mouse = CurrentMousePx() }, Target = _target, WaitAfter = 1 });
    }

    static Vec2 CurrentMousePx() => new(_lastLogical.x * Scale, _lastLogical.y * Scale);

    // Tus adi -> GLFW kodu ("enter", "esc", "tab", "backspace", "delete", "left", "a", "f5", "space"...).
    public static int KeyCode(string name)
    {
        name = name.Trim().ToLowerInvariant();
        switch (name)
        {
            case "enter" or "return": return GLFWConst.KEY_ENTER;
            case "esc" or "escape": return GLFWConst.KEY_ESCAPE;
            case "tab": return GLFWConst.KEY_TAB;
            case "backspace": return GLFWConst.KEY_BACKSPACE;
            case "delete" or "del": return GLFWConst.KEY_DELETE;
            case "left": return GLFWConst.KEY_LEFT;
            case "right": return GLFWConst.KEY_RIGHT;
            case "up": return GLFWConst.KEY_UP;
            case "down": return GLFWConst.KEY_DOWN;
            case "home": return GLFWConst.KEY_HOME;
            case "end": return GLFWConst.KEY_END;
            case "space": return GLFWConst.KEY_SPACE;
        }
        if (name.Length == 1 && name[0] >= 'a' && name[0] <= 'z')
            return GLFWConst.KEY_A + (name[0] - 'a');
        if (name.Length == 1 && name[0] >= '0' && name[0] <= '9')
            return '0' + (name[0] - '0');
        if (name.Length >= 2 && name[0] == 'f' && int.TryParse(name.AsSpan(1), out int fn) && fn >= 1 && fn <= 12)
            return GLFWConst.KEY_F1 + fn - 1;
        return -1;
    }

    public static EventModifiers Mods(string mods)
    {
        var m = EventModifiers.None;
        if (string.IsNullOrEmpty(mods)) return m;
        foreach (var p in mods.ToLowerInvariant().Split(new[] { '+', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            m |= p switch { "ctrl" or "control" or "cmd" => EventModifiers.Control, "shift" => EventModifiers.Shift, "alt" => EventModifiers.Alt, _ => EventModifiers.None };
        return m;
    }

    // --- pencere screenshot'i: frame sonunda (sg_commit sonrasi, swap oncesi) okunur ---

    static string _shotPath;
    static float _shotScale = 1f;
    static NativeWindow _shotWindow; // null = ana pencere; detached/utility: paylasilan FBO'dan okunur
    static (int w, int h)? _shotDone;
    static string _shotError;

    public static void RequestScreenshot(string path, float scale, NativeWindow window = null)
    {
        _shotPath = path;
        _shotScale = scale;
        _shotWindow = window;
        _shotDone = null;
        _shotError = null;
    }

    public static bool ShotPending => _shotPath != null;

    // Deferred RPC poll: null = bekle.
    public static (int w, int h)? TakeShotResult(out string error)
    {
        error = _shotError;
        var r = _shotDone;
        if (r != null || error != null) { _shotDone = null; _shotError = null; }
        return r;
    }

    // EditorApp: Sokol.Commit sonrasi, SwapBuffers oncesi. fbw/fbh fiziksel; lw/lh mantiksal (ana pencere).
    // Hedef detached pencereyse boyutlar ondan alinir (FBO = o pencerenin Camera.Framebuffer'i, ana context'te okunur).
    public static void CaptureIfRequested(int fbw, int fbh, float lw, float lh)
    {
        if (_shotPath == null)
            return;
        string path = _shotPath;
        _shotPath = null;
        uint fbo = 0;
        var win = _shotWindow;
        _shotWindow = null;
        if (win != null)
        {
            if (win.Handle == IntPtr.Zero) { _shotError = "pencere kapanmis"; return; }
            fbo = win.Camera.Framebuffer;
            fbw = win.Width; fbh = win.Height;
            float k = NativeWindow.ScreenScale(win.Handle);
            GLFW.GetWindowSize(win.Handle, out int ww, out int wh);
            lw = ww / k; lh = wh / k;
        }
        int dw = Math.Max(1, (int)MathF.Round(lw * _shotScale)), dh = Math.Max(1, (int)MathF.Round(lh * _shotScale));
        byte* src = (byte*)NativeMemory.Alloc((nuint)(fbw * fbh * 4));
        try
        {
            if (ReadFbo(fbo, fbw, fbh, src) == 0)
            {
                _shotError = "framebuffer okunamadi (yalniz GL backend)";
                return;
            }
            var rgb = new byte[dw * dh * 3];
            float kx = (float)fbw / dw, ky = (float)fbh / dh;
            for (int y = 0; y < dh; y++)
            {
                int y0 = (int)(y * ky), y1 = Math.Max(y0 + 1, (int)((y + 1) * ky));
                for (int x = 0; x < dw; x++)
                {
                    int x0 = (int)(x * kx), x1 = Math.Max(x0 + 1, (int)((x + 1) * kx));
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int yy = y0; yy < y1 && yy < fbh; yy++)
                    {
                        byte* row = src + ((fbh - 1 - yy) * fbw) * 4; // GL satir 0 = alt
                        for (int xx = x0; xx < x1 && xx < fbw; xx++)
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
            _shotDone = (dw, dh);
        }
        catch (Exception e)
        {
            _shotError = e.Message;
        }
        finally
        {
            NativeMemory.Free(src);
        }
    }
}
