#if DE_EDITOR
using System;

namespace DigitoyEngine.Editor;

// Native popup penceresi: combo listesi gibi gecici acilir icerik ana pencerenin
// clip'i/kenari ile SINIRLI KALMAZ — dekorasyonsuz, floating, odakli bir GLFW
// penceresi acilir (dock'un yuzen pencereleri ve surukleme hayaletiyle ayni altyapi:
// NativeWindow + kendi GuiHost'u). Tek instance yeniden kullanilir.
//
// Protokol: sahip kontrol OpenList(id, ...) ile acar; her frame GuiDock.UpdateWindows
// icinden Update() kosar (event + cizim); kullanici oge secince sonuc saklanir ve
// sahip bir sonraki GUI turunda TryTakeResult(id) ile alir. Dis tik (ana pencerede
// MouseDown → sahip kapatir), odak kaybi, Esc ya da pencere kapanisi = iptal.
public static class GuiPopup
{
    const float ItemH = 20f, MaxH = 302f, MinW = 120f;
    const int Layer = 1;

    static NativeWindow _win;
    static int _ownerId;
    static string[] _items = Array.Empty<string>();
    static int _selected;
    static float _scroll, _maxScroll;
    static int _frames;
    static Rect _rect;      // pencere lokal (mantiksal)
    static int _resultId, _resultIndex = -1;
    static readonly GuiHost.GuiFunc _draw = Draw;

    public static bool IsOpen(int ownerId) => _win != null && _ownerId == ownerId;
    public static bool Active => _win != null;

    // buttonLogical: sahip pencerenin MANTIKSAL (GuiClip.Unclip) uzayinda buton rect'i.
    public static void OpenList(int ownerId, string[] items, int selected, IntPtr hostWindow, in Rect buttonLogical)
    {
        Close();
        if (items == null || items.Length == 0 || hostWindow == IntPtr.Zero)
            return;
        float k = NativeWindow.ScreenScale(hostWindow); // ekran-birimi / mantiksal
        GLFW.GetWindowPos(hostWindow, out int wx, out int wy);
        float contentH = items.Length * ItemH;
        float h = Math.Min(contentH, MaxH);
        // Genislik: buton genisligi ile en uzun ogenin metni (olculebiliyorsa) arasindaki buyugu.
        float w = Math.Max(buttonLogical.width, MinW);
        float ts = Math.Min(Gui.FontSize, ItemH - 4);
        if (Gui.Font != null)
            for (int i = 0; i < items.Length; i++)
                w = Math.Max(w, Gui.Font.TextSize(items[i], ts).x + 16);
        int px = wx + (int)(buttonLogical.x * k);
        int py = wy + (int)(buttonLogical.yMax * k);
        int pw = Math.Max(1, (int)(w * k)), ph = Math.Max(1, (int)(h * k));
        // Calisma alanina sigdir: asagi sigmazsa yukari ac, yatayda iceri it.
        var mon = GLFW.GetPrimaryMonitor();
        if (mon != IntPtr.Zero)
        {
            GLFW.GetMonitorWorkarea(mon, out int mx, out int my, out int mw, out int mh);
            if (py + ph > my + mh && wy + (int)(buttonLogical.y * k) - ph >= my)
                py = wy + (int)(buttonLogical.y * k) - ph;
            px = Math.Clamp(px, mx, Math.Max(mx, mx + mw - pw));
        }

        // SCALE_TO_MONITOR ana pencere icin acik: boyut zaten k ile olcekli, kapatilmazsa
        // Windows'ta DPI ile ikinci kez buyur (ColorPickerWindow ile ayni desen).
        GLFW.WindowHint(GLFWConst.DECORATED, GLFWConst.FALSE);
        GLFW.WindowHint(GLFWConst.FLOATING, GLFWConst.TRUE);
        GLFW.WindowHint(GLFWConst.RESIZABLE, GLFWConst.FALSE);
        GLFW.WindowHint(GLFWConst.SCALE_TO_MONITOR, GLFWConst.FALSE);
        _win = NativeWindow.Open("popup", px, py, pw, ph);
        GLFW.WindowHint(GLFWConst.DECORATED, GLFWConst.TRUE);
        GLFW.WindowHint(GLFWConst.FLOATING, GLFWConst.FALSE);
        GLFW.WindowHint(GLFWConst.RESIZABLE, GLFWConst.TRUE);
        GLFW.WindowHint(GLFWConst.SCALE_TO_MONITOR, GLFWConst.TRUE);
        if (_win == null)
            return;
        _win.Camera.BackgroundColor = new Color(18, 19, 23, 255);
        GLFW.FocusWindow(_win.Handle);
        _ownerId = ownerId;
        _items = items;
        _selected = selected;
        _maxScroll = Math.Max(0, contentH - h);
        _scroll = Math.Clamp(selected * ItemH - (h - ItemH) * 0.5f, 0, _maxScroll);
        _frames = 0;
        _resultId = 0;
    }

    public static bool TryTakeResult(int ownerId, out int index)
    {
        if (_resultId == ownerId && ownerId != 0)
        {
            index = _resultIndex;
            _resultId = 0;
            _resultIndex = -1;
            return true;
        }
        index = -1;
        return false;
    }

    public static void Close()
    {
        if (_win == null)
            return;
        if (GuiUtility.HotControl == _ownerId)
            GuiUtility.HotControl = 0; // sahip modal hot'u birakir (dis kapanista da)
        _win.Destroy();
        _win = null;
        _ownerId = 0;
        _items = Array.Empty<string>();
    }

    // Her frame (GuiDock.UpdateWindows): kapanis kosullari + GUI turu.
    public static void Update()
    {
        if (_win == null)
            return;
        _frames++;
        if (_win.ShouldClose || (_frames > 3 && GLFW.GetWindowAttrib(_win.Handle, GLFWConst.FOCUSED) == 0))
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
        _win.Gui.Frame(_win.Handle, _rect, _draw, NativeWindow.ScreenScale(_win.Handle));
    }

    static void Draw()
    {
        var ev = Event.Current;
        switch (ev.Type)
        {
            case EventType.MouseDown:
                {
                    int idx = (int)((ev.MousePosition.y + _scroll) / ItemH);
                    if (_rect.Contains(ev.MousePosition) && idx >= 0 && idx < _items.Length)
                    {
                        _resultId = _ownerId;
                        _resultIndex = idx;
                    }
                    ev.Use();
                    Close(); // sonuc saklandi; pencere kapanir, sahip sonraki turda alir
                    return;
                }
            case EventType.ScrollWheel:
                _scroll = Math.Clamp(_scroll - ev.Delta.y * ItemH * 2, 0, _maxScroll);
                ev.Use();
                break;
            case EventType.KeyDown:
                if (ev.KeyCode == GLFWConst.KEY_ESCAPE) { ev.Use(); Close(); return; }
                break;
            case EventType.Repaint:
                {
                    GuiRenderer.DrawRect(_rect, new Color(38, 40, 48, 255), Layer);
                    float ts = Math.Min(Gui.FontSize, ItemH - 4);
                    int first = Math.Max(0, (int)(_scroll / ItemH));
                    int last = Math.Min(_items.Length - 1, (int)((_scroll + _rect.height) / ItemH));
                    for (int i = first; i <= last; i++)
                    {
                        var row = new Rect(0, i * ItemH - _scroll, _rect.width, ItemH);
                        bool hover = row.Contains(ev.MousePosition);
                        if (hover)
                            GuiRenderer.DrawRect(row, new Color(50, 110, 200, 255), Layer + 1);
                        else if (i == _selected)
                            GuiRenderer.DrawRect(row, new Color(62, 66, 80, 255), Layer + 1);
                        GuiRenderer.DrawTextIn(new Rect(row.x + 6, row.y, row.width - 12, row.height),
                            _items[i], ts, new Color(215, 218, 228, 255), false, Layer + 2);
                    }
                    break;
                }
        }
    }

    public static void Encode(CommandBuffer cb)
    {
        if (_win != null)
            _win.Camera.Encode(cb, _win.Width, _win.Height);
    }

    public static void Present() => _win?.Present();
}
#endif
