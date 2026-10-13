using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Editor popup tabani: imlecin yaninda acilan dekorasyonsuz, floating native pencere
// (ColorPickerWindow/GuiPopup ile ayni GLFW deseni). Focus kaybi, Esc veya pencere
// kapanisi = Close. Tek aktif popup (yeni Open oncekini kapatir). EditorApp kancalari:
// UpdateActive (GUI turu sonrasi) / EncodeActive / PresentActive.
public abstract class EditorPopup
{
    static EditorPopup _active;

    NativeWindow _win;
    Rect _rect;
    bool _wasFocused;
    int _openFrames;
    readonly GuiHost.GuiFunc _drawFunc;

    protected EditorPopup() => _drawFunc = DrawFrame;

    public bool IsOpen => _win != null;
    protected Rect WindowRect => _rect;

    // Mantiksal boyut; konum = ana penceredeki imlecin +12,+12'si, workarea'ya clamp.
    protected void Open(float logicalW, float logicalH)
    {
        if (_active != null && _active != this)
            _active.Close();
        _active = this;
        _wasFocused = false;
        _openFrames = 0;

        IntPtr main = NativeWindow.MainWindow;
        GLFW.GetWindowPos(main, out int wx, out int wy);
        GLFW.GetCursorPos(main, out double cx, out double cy);
        float k = NativeWindow.ScreenScale(main);
        int px = wx + (int)cx + 12, py = wy + (int)cy + 12;
        int pw = Math.Max(1, (int)(logicalW * k)), ph = Math.Max(1, (int)(logicalH * k));
        var mon = GLFW.GetPrimaryMonitor();
        if (mon != IntPtr.Zero)
        {
            GLFW.GetMonitorWorkarea(mon, out int mx, out int my, out int mw, out int mh);
            px = Math.Clamp(px, mx, Math.Max(mx, mx + mw - pw));
            py = Math.Clamp(py, my, Math.Max(my, my + mh - ph));
        }

        if (_win != null)
        {
            GLFW.SetWindowPos(_win.Handle, px, py);
            GLFW.FocusWindow(_win.Handle);
            OnOpened();
            return;
        }
        // DefaultWindowHints CAGIRMA (GL context hint'lerini sifirlar); hint'ler elle geri alinir.
        GLFW.WindowHint(GLFWConst.DECORATED, GLFWConst.FALSE);
        GLFW.WindowHint(GLFWConst.FLOATING, GLFWConst.TRUE);
        GLFW.WindowHint(GLFWConst.RESIZABLE, GLFWConst.FALSE);
        GLFW.WindowHint(GLFWConst.SCALE_TO_MONITOR, GLFWConst.FALSE);
        _win = NativeWindow.Open(GetType().Name, px, py, pw, ph);
        GLFW.WindowHint(GLFWConst.DECORATED, GLFWConst.TRUE);
        GLFW.WindowHint(GLFWConst.FLOATING, GLFWConst.FALSE);
        GLFW.WindowHint(GLFWConst.RESIZABLE, GLFWConst.TRUE);
        GLFW.WindowHint(GLFWConst.SCALE_TO_MONITOR, GLFWConst.TRUE);
        if (_win != null)
        {
            _win.Camera.BackgroundColor = new Color(32, 34, 40, 255);
            GLFW.FocusWindow(_win.Handle);
        }
        OnOpened();
    }

    public void Close()
    {
        if (_active == this)
            _active = null;
        OnClosing();
        if (_win == null)
            return;
        _win.Destroy();
        _win = null;
    }

    protected virtual void OnOpened() { }
    // Kapanmadan once: popup'a ait HotControl/KeyboardControl birakilmali (global state).
    protected virtual void OnClosing() { }
    protected abstract void OnGui(Event ev);

    void DrawFrame()
    {
        var ev = Event.Current;
        if (ev.Type == EventType.KeyDown && ev.KeyCode == GLFWConst.KEY_ESCAPE)
        {
            ev.Use();
            Close();
            return;
        }
        if (ev.Type == EventType.Repaint) // dekorasyonsuz pencereye ince cerceve
        {
            GuiRenderer.DrawRect(_rect, new Color(70, 73, 84, 255), 0);
            GuiRenderer.DrawRect(new Rect(1, 1, _rect.width - 2, _rect.height - 2),
                new Color(32, 34, 40, 255), 0);
        }
        OnGui(ev);
    }

    void Update()
    {
        if (_win == null)
            return;
        _openFrames++;
        bool focused = GLFW.GetWindowAttrib(_win.Handle, GLFWConst.FOCUSED) != 0;
        if (focused)
            _wasFocused = true;
        if (_win.ShouldClose || (!focused && (_wasFocused || _openFrames > 15)))
        {
            Close();
            return;
        }
        if (!_win.UpdateSize())
            return;
        GLFW.GetWindowContentScale(_win.Handle, out float ps, out _);
        if (ps <= 0) ps = 1f;
        float lw = _win.Width / ps, lh = _win.Height / ps;
        _win.Camera.SetPixelOrtho((int)lw, (int)lh);
        GuiRenderer.Queue = _win.Camera.Queue;
        _rect = new Rect(0, 0, lw, lh);
        _win.Gui.Frame(_win.Handle, _rect, _drawFunc, NativeWindow.ScreenScale(_win.Handle));
    }

    public static void UpdateActive() => _active?.Update();

    public static void EncodeActive(CommandBuffer cb)
    {
        var w = _active?._win;
        if (w != null)
            w.Camera.Encode(cb, w.Width, w.Height);
    }

    public static void PresentActive() => _active?._win?.Present();
}
