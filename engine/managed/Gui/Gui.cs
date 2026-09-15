using System;

namespace DigitoyEngine;

// Unity GUI karsiligi: rect tabanli temel widget'lar + layout sarmalayicilar.
// Tum widget'lar klasik hot-control state machine'i kullanir:
//   MouseDown(rect icinde) -> hot al + Use
//   MouseDrag(hot bende)   -> Use
//   MouseUp(hot bende)     -> hot birak + Use; rect icindeyse tiklama
//   Repaint                -> stil ciz
public static partial class Gui
{
    public static GuiSkin Skin = GuiSkin.Default;

    // Aktif font (App yukler); null ise text cizilmez, olculer sabit kalir.
    public static GuiFont Font;
    public static float FontSize = 14f;
    // Ana pencerenin DPI olcegi (mantiksal->fiziksel; detach konumu icin).
    public static float Scale = 1f;
    static readonly Color _textColor = new Color(228, 230, 238, 255);

    static readonly int _boxHash = "Gui.Box".GetHashCode();
    static readonly int _buttonHash = "Gui.Button".GetHashCode();
    static readonly int _toggleHash = "Gui.Toggle".GetHashCode();
    static readonly int _sliderHash = "Gui.Slider".GetHashCode();
    static readonly int _labelHash = "Gui.Label".GetHashCode();

    // --- Rect tabanli ---

    public static void Box(in Rect rect, GuiStyle style = null)
    {
        style ??= Skin.Box;
        int id = GuiUtility.GetControlID(_boxHash, FocusType.Passive);
        style.Draw(rect, id);
    }

    public static void Label(in Rect rect, ReadOnlySpan<char> text)
    {
        GuiUtility.GetControlID(_labelHash, FocusType.Passive);
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(rect, text, FontSize, _textColor);
    }

    public static bool Button(in Rect rect, GuiStyle style = null)
        => ButtonCore(rect, default, style ?? Skin.Button);

    public static bool Button(in Rect rect, ReadOnlySpan<char> text, GuiStyle style = null)
        => ButtonCore(rect, text, style ?? Skin.Button);

    static bool ButtonCore(in Rect rect, ReadOnlySpan<char> text, GuiStyle style)
    {
        int id = GuiUtility.GetControlID(_buttonHash, FocusType.Passive);
        Event ev = Event.Current;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                    ev.Use();
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                    return rect.Contains(ev.MousePosition);
                }
                break;
            case EventType.Repaint:
                style.Draw(rect, id);
                if (!text.IsEmpty)
                    GuiRenderer.DrawTextIn(rect, text, FontSize, _textColor, centerX: true);
                break;
        }
        return false;
    }

    public static bool Toggle(in Rect rect, bool value, GuiStyle style = null)
    {
        style ??= Skin.Toggle;
        int id = GuiUtility.GetControlID(_toggleHash, FocusType.Passive);
        Event ev = Event.Current;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                    ev.Use();
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                    if (rect.Contains(ev.MousePosition))
                        return !value;
                }
                break;
            case EventType.Repaint:
            {
                // Checkbox: cerceve (off-state rengi) + koyu ic zemin + isaretliyse
                // aralikli mavi kare (on-state rengi).
                bool hovered = rect.Contains(ev.MousePosition);
                Color frame = style.StateFor(id, false, hovered).Background;
                GuiRenderer.DrawRect(rect, frame);
                Rect inner = new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
                GuiRenderer.DrawRect(inner, new Color(28, 30, 38), layerOffset: 1);
                if (value)
                {
                    float pad = MathF.Max(3f, MathF.Round(rect.width * 0.22f));
                    Rect mark = new Rect(rect.x + pad, rect.y + pad,
                        rect.width - pad * 2, rect.height - pad * 2);
                    GuiRenderer.DrawRect(mark, style.StateFor(id, true, hovered).Background, layerOffset: 2);
                }
                break;
            }
        }
        return value;
    }

    public static float HorizontalSlider(in Rect rect, float value, float min, float max,
        GuiStyle track = null, GuiStyle thumb = null)
    {
        track ??= Skin.HorizontalSlider;
        thumb ??= Skin.SliderThumb;
        int id = GuiUtility.GetControlID(_sliderHash, FocusType.Passive);
        Event ev = Event.Current;

        float thumbW = thumb.FixedWidth > 0 ? thumb.FixedWidth : 10f;

        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    ev.Use();
                    return ValueFromMouse(rect, ev.MousePosition.x, min, max, thumbW);
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    ev.Use();
                    return ValueFromMouse(rect, ev.MousePosition.x, min, max, thumbW);
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                {
                    track.Draw(rect, id);
                    float t = max > min ? (value - min) / (max - min) : 0f;
                    if (t < 0) t = 0;
                    if (t > 1) t = 1;
                    float tx = rect.x + t * (rect.width - thumbW);
                    var thumbRect = new Rect(tx, rect.y, thumbW, rect.height);
                    bool hovered = rect.Contains(ev.MousePosition);
                    ref GuiStyleState s = ref thumb.StateFor(id, false, hovered);
                    GuiRenderer.DrawRect(thumbRect, s.Background, 1);
                    break;
                }
        }
        return value;
    }

    static float ValueFromMouse(in Rect rect, float mouseX, float min, float max, float thumbW)
    {
        float t = (mouseX - rect.x - thumbW * 0.5f) / (rect.width - thumbW);
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        return min + t * (max - min);
    }

    // --- Scrollbar / ScrollView ---

    static readonly int _vscrollHash = "Gui.VScroll".GetHashCode();
    static readonly int _windowHash = "Gui.Window".GetHashCode();

    struct GrabState { public float Offset; }
    struct WindowState { public Vec2 Grab; }

    // Dikey scrollbar: value = gorunur alanin basi, size = gorunur miktar,
    // [min..max] icerik araligi. Thumb boyu orantili; grab-offset'li surukleme.
    public static float VerticalScrollbar(in Rect rect, float value, float size, float min, float max,
        GuiStyle track = null, GuiStyle thumbStyle = null)
    {
        track ??= Skin.HorizontalSlider;
        thumbStyle ??= Skin.SliderThumb;
        int id = GuiUtility.GetControlID(_vscrollHash, FocusType.Passive);
        Event ev = Event.Current;

        float range = max - min;
        if (range <= size || rect.height <= 0)
        {
            if (ev.GetTypeForControl(id) == EventType.Repaint)
                track.Draw(rect, id);
            return min;
        }

        float thumbH = rect.height * (size / range);
        if (thumbH < 20) thumbH = 20;
        float maxScroll = range - size;
        float t = (value - min) / maxScroll;
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        float thumbY = rect.y + t * (rect.height - thumbH);
        var thumb = new Rect(rect.x + 2, thumbY, rect.width - 4, thumbH);

        ref GrabState grab = ref GuiUtility.GetState<GrabState>(id);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (thumb.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    grab.Offset = ev.MousePosition.y - thumbY;
                    ev.Use();
                }
                else if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    grab.Offset = thumbH * 0.5f; // track'e tikla: thumb ortasindan yakala
                    ev.Use();
                    return ScrollFromMouse(rect, ev.MousePosition.y, grab.Offset, thumbH, min, maxScroll);
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    ev.Use();
                    return ScrollFromMouse(rect, ev.MousePosition.y, grab.Offset, thumbH, min, maxScroll);
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                {
                    track.Draw(rect, id);
                    bool hovered = rect.Contains(ev.MousePosition);
                    ref GuiStyleState s = ref thumbStyle.StateFor(id, false, hovered);
                    GuiRenderer.DrawRect(thumb, s.Background, 1);
                    break;
                }
        }
        return value;
    }

    static float ScrollFromMouse(in Rect rect, float mouseY, float grabOffset, float thumbH, float min, float maxScroll)
    {
        float t = (mouseY - grabOffset - rect.y) / (rect.height - thumbH);
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        return min + t * maxScroll;
    }

    static readonly int _hscrollHash = "Gui.HScroll".GetHashCode();

    // Yatay scrollbar (dikeyin aynadaki esi).
    public static float HorizontalScrollbar(in Rect rect, float value, float size, float min, float max,
        GuiStyle track = null, GuiStyle thumbStyle = null)
    {
        track ??= Skin.HorizontalSlider;
        thumbStyle ??= Skin.SliderThumb;
        int id = GuiUtility.GetControlID(_hscrollHash, FocusType.Passive);
        Event ev = Event.Current;

        float range = max - min;
        if (range <= size || rect.width <= 0)
        {
            if (ev.GetTypeForControl(id) == EventType.Repaint)
                track.Draw(rect, id);
            return min;
        }

        float thumbW = rect.width * (size / range);
        if (thumbW < 20) thumbW = 20;
        float maxScroll = range - size;
        float t = (value - min) / maxScroll;
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        float thumbX = rect.x + t * (rect.width - thumbW);
        var thumb = new Rect(thumbX, rect.y + 2, thumbW, rect.height - 4);

        ref GrabState grab = ref GuiUtility.GetState<GrabState>(id);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (thumb.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    grab.Offset = ev.MousePosition.x - thumbX;
                    ev.Use();
                }
                else if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    grab.Offset = thumbW * 0.5f;
                    ev.Use();
                    return HScrollFromMouse(rect, ev.MousePosition.x, grab.Offset, thumbW, min, maxScroll);
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    ev.Use();
                    return HScrollFromMouse(rect, ev.MousePosition.x, grab.Offset, thumbW, min, maxScroll);
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                {
                    track.Draw(rect, id);
                    bool hovered = rect.Contains(ev.MousePosition);
                    ref GuiStyleState s = ref thumbStyle.StateFor(id, false, hovered);
                    GuiRenderer.DrawRect(thumb, s.Background, 1);
                    break;
                }
        }
        return value;
    }

    static float HScrollFromMouse(in Rect rect, float mouseX, float grabOffset, float thumbW, float min, float maxScroll)
    {
        float t = (mouseX - grabOffset - rect.x) / (rect.width - thumbW);
        if (t < 0) t = 0;
        if (t > 1) t = 1;
        return min + t * maxScroll;
    }

    static readonly int _dragFloatHash = "Gui.DragFloat".GetHashCode();

    struct DragFloatState { public double Start; public float StartX; public bool Dragged; }

    // Tikla-yaz edit oturumu: ayni anda tek sayi alani (id) duzenlenir; ekstra
    // kontrol YARATILMAZ (id kaymasi olmaz) — klavye odagi drag kontrolunun kendisi.
    static int _numEditId;
    static char[] _numBuf = new char[32];
    static int _numLen;

    // Inspector hedefi degistiginde sirali control id ayni kalabilir; eski
    // tamponun yeni nesneye commit edilmesini engelle.
    public static void CancelNumericEdit()
    {
        if (_numEditId == 0)
            return;
        if (GuiUtility.KeyboardControl == _numEditId)
            GuiUtility.KeyboardControl = 0;
        if (GuiUtility.HotControl == _numEditId)
            GuiUtility.HotControl = 0;
        _numEditId = 0;
        _numLen = 0;
    }

    // Gorunmez drag tutamaci (Unity label-drag): rect uzerinde yatay surukleme
    // degeri degistirir, cizim yapmaz — cagiran etiketi kendisi cizer.
    static readonly int _dragZoneHash = "Gui.DragZone".GetHashCode();

    public static float DragZone(in Rect rect, float value, float speed = 0.05f,
        float min = float.MinValue, float max = float.MaxValue)
    {
        int id = GuiUtility.GetControlID(_dragZoneHash, FocusType.Passive);
        Event ev = Event.Current;
        ref DragFloatState st = ref GuiUtility.GetState<DragFloatState>(id);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    st.Start = value;
                    st.StartX = ev.MousePosition.x;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    ev.Use();
                    double v = st.Start + (ev.MousePosition.x - st.StartX) * speed;
                    return (float)(v < min ? min : (v > max ? max : v));
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                if (rect.Contains(ev.MousePosition) || GuiUtility.HotControl == id)
                    GuiCursorManager.Request(GuiCursor.ResizeH);
                break;
        }
        return value;
    }

    // Tam sayi alani: DragFloat mekanigi, "0" formatiyla cizim (ondalik gosterilmez).
    public static int DragInt(in Rect rect, int value, float speed = 0.05f, int min = int.MinValue, int max = int.MaxValue)
        => (int)System.Math.Round(DragCore(rect, value, speed, min, max, "0", integer: true));

    // Yatay surukle = deger degistir; suruklenmeden tik = yazarak gir (Enter/odak
    // kaybi commit, Escape iptal).
    public static float DragFloat(in Rect rect, float value, float speed = 0.01f, float min = float.MinValue, float max = float.MaxValue)
        => (float)DragCore(rect, value, speed, min, max, "G9", integer: false);

    static double DragCore(in Rect rect, double value, double speed, double min, double max,
        string format, bool integer)
    {
        int id = GuiUtility.GetControlID(_dragFloatHash, FocusType.Keyboard);
        Event ev = Event.Current;
        ref DragFloatState st = ref GuiUtility.GetState<DragFloatState>(id);
        bool editing = _numEditId == id;
        // Tab ile odak geldi: dogrudan yazma moduna gir, deger tumu secili.
        if (!editing && GuiUtility.ConsumeTabFocus(id))
        {
            _numEditId = id;
            value.TryFormat(_numBuf, out _numLen, format, System.Globalization.CultureInfo.InvariantCulture);
            ref TextEditState ts = ref GuiUtility.GetState<TextEditState>(id);
            ts.Anchor = 0;
            ts.Caret = _numLen;
            ts.ScrollX = 0;
            editing = true;
        }
        // Odak baska yere gectiyse (baska alan tiklandi vs.) oturum commit ile biter.
        if (editing && GuiUtility.KeyboardControl != id)
            return CommitNum(value, min, max, integer);
        if (editing)
            return NumEditEvents(rect, id, value, min, max, integer);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(ev.MousePosition))
                {
                    // Yeni numeric kontrol eski editorden once ciziliyor olabilir.
                    // Tamponu MouseUp'a kadar elleme; eski kontrol bu event'te
                    // odak kaybini gorup kendi degerini guvenle commit etsin.
                    if (_numEditId != 0)
                        GuiUtility.KeyboardControl = 0;
                    GuiUtility.HotControl = id;
                    st.Start = value;
                    st.StartX = ev.MousePosition.x;
                    st.Dragged = false;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    ev.Use();
                    if (!st.Dragged && System.MathF.Abs(ev.MousePosition.x - st.StartX) < 3f)
                        break; // esik altinda: hala "tik" olabilir
                    st.Dragged = true;
                    double v = st.Start + (ev.MousePosition.x - st.StartX) * speed;
                    return v < min ? min : (v > max ? max : v);
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                    if (!st.Dragged && rect.Contains(ev.MousePosition))
                    {
                        // Suruklenmemis tik: yazma moduna gir (mevcut deger tumu secili).
                        _numEditId = id;
                        GuiUtility.KeyboardControl = id;
                        value.TryFormat(_numBuf, out _numLen, format, System.Globalization.CultureInfo.InvariantCulture);
                        ref TextEditState ts = ref GuiUtility.GetState<TextEditState>(id);
                        ts.Anchor = 0;
                        ts.Caret = _numLen;
                        ts.ScrollX = 0;
                    }
                }
                break;
            case EventType.Repaint:
                {
                    if (rect.Contains(ev.MousePosition) || GuiUtility.HotControl == id)
                        GuiCursorManager.Request(GuiCursor.ResizeH);
                    Skin.TextField.Draw(rect, id);
                    Span<char> tmp = stackalloc char[24];
                    value.TryFormat(tmp, out int n, format, System.Globalization.CultureInfo.InvariantCulture);
                    GuiRenderer.DrawTextIn(rect, tmp.Slice(0, n), FontSize, _textColor);
                    break;
                }
        }
        return value;
    }

    // Edit oturumu = tam TextField davranisi (caret/secim/pano) sayi filtresiyle;
    // GuiTextField cekirdegi (HandleKey/DrawTextField/IndexFromX) aynen kullanilir.
    static double NumEditEvents(in Rect rect, int id, double value, double min, double max, bool integer)
    {
        Event ev = Event.Current;
        ref TextEditState st = ref GuiUtility.GetState<TextEditState>(id);
        GuiStyle style = Skin.TextField;
        float pad = style.Padding.Left;
        var inner = new Rect(rect.x + pad, rect.y, rect.width - pad * 2, rect.height);
        var text = new System.ReadOnlySpan<char>(_numBuf, 0, _numLen);
        if (st.Caret > _numLen) st.Caret = _numLen;
        if (st.Anchor > _numLen) st.Anchor = _numLen;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (rect.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    int idx = IndexFromX(Font, text, ev.MousePosition.x - inner.x + st.ScrollX);
                    if (ev.ClickCount >= 2) { st.Anchor = 0; st.Caret = _numLen; }
                    else
                    {
                        st.Caret = idx;
                        if ((ev.Modifiers & EventModifiers.Shift) == 0)
                            st.Anchor = idx;
                    }
                    ev.Use();
                }
                else
                {
                    return CommitNum(value, min, max, integer); // disari tik = commit
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    st.Caret = IndexFromX(Font, text, ev.MousePosition.x - inner.x + st.ScrollX);
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.KeyDown:
                if (ev.KeyCode is GLFWConst.KEY_ENTER or GLFWConst.KEY_TAB)
                {
                    ev.Use();
                    if (ev.KeyCode == GLFWConst.KEY_TAB)
                        GuiUtility.MoveFocus(id, (ev.Modifiers & EventModifiers.Shift) != 0);
                    return CommitNum(value, min, max, integer);
                }
                if (ev.KeyCode == GLFWConst.KEY_ESCAPE)
                {
                    ev.Use();
                    _numEditId = 0;
                    if (GuiUtility.KeyboardControl == id)
                        GuiUtility.KeyboardControl = 0;
                    return value;
                }
                if (HandleKey(ev, ref _numBuf, ref _numLen, ref st, grow: false))
                    ev.Use();
                break;
            case EventType.TextInput:
                {
                    char c = ev.Character == ',' ? '.' : ev.Character;
                    bool ok = c is >= '0' and <= '9' or '-' || (!integer && c is '.' or 'e' or 'E' or '+');
                    if (ok)
                    {
                        DeleteSelection(_numBuf, ref _numLen, ref st);
                        Insert(_numBuf, ref _numLen, ref st, c);
                        ev.Use();
                    }
                }
                break;
            case EventType.Repaint:
                if (rect.Contains(ev.MousePosition) || GuiUtility.HotControl == id)
                    GuiCursorManager.Request(GuiCursor.IBeam);
                DrawTextField(rect, inner, style, id, _numBuf, _numLen, ref st, Font);
                break;
        }
        return value;
    }

    static double CommitNum(double fallback, double min, double max, bool integer)
    {
        if (GuiUtility.KeyboardControl == _numEditId)
            GuiUtility.KeyboardControl = 0;
        _numEditId = 0;
        var text = new System.ReadOnlySpan<char>(_numBuf, 0, _numLen);
        double v;
        if (integer)
        {
            if (!int.TryParse(text, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int intValue))
                return fallback;
            v = intValue;
        }
        else
        {
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float floatValue))
                return fallback;
            v = floatValue;
        }
        return v < min ? min : (v > max ? max : v);
    }

    // Rect tabanli scroll view: position gorunur pencere, viewRect icerik olcusu.
    // Icerik BeginScrollView/EndScrollView arasinda viewRect'in LOKAL uzayinda
    // cizilir (scroll tamamen GuiClip offset'iyle). Donen deger yeni scroll.
    public static Vec2 BeginScrollView(in Rect position, Vec2 scroll, in Rect viewRect)
    {
        const float barW = 14f;
        bool needV = viewRect.height > position.height;

        // Tekerlek: imlec view uzerindeyken dikey scroll (GLFW: +y = yukari).
        Event ev = Event.Current;
        if (needV && ev.Type == EventType.ScrollWheel && position.Contains(ev.MousePosition))
        {
            scroll.y -= ev.Delta.y * 40f;
            float maxScroll = viewRect.height - position.height;
            if (scroll.y < 0) scroll.y = 0;
            if (scroll.y > maxScroll) scroll.y = maxScroll;
            ev.Use();
        }

        var content = new Rect(position.x, position.y, position.width - (needV ? barW : 0), position.height);
        if (needV)
        {
            var bar = new Rect(position.xMax - barW, position.y, barW, position.height);
            scroll.y = VerticalScrollbar(bar, scroll.y, position.height, 0, viewRect.height);
        }
        else
        {
            // Kontrol sirasi korunumu: bar gorunmese de ID tuketilmeli.
            GuiUtility.GetControlID(_vscrollHash, FocusType.Passive);
            scroll.y = 0;
        }
        GuiClip.Push(content, scroll);
        return scroll;
    }

    public static void EndScrollView() => GuiClip.Pop();

    // --- Window (basligindan suruklenebilir panel; icerik auto-layout area'si) ---

    public delegate void WindowFunc(int windowId);

    public const float WindowTitleHeight = 24f;

    // Icerik func'i pencere iceriginin LOKAL uzayinda calisir (padding sonrasi).
    // Donen rect suruklemeyle guncellenmis konumdur; cagiran saklar (Unity gibi).
    public static Rect Window(int windowId, Rect rect, WindowFunc func, GuiStyle style = null)
    {
        style ??= Skin.Window;
        int id = GuiUtility.GetControlID(_windowHash, FocusType.Passive);
        Event ev = Event.Current;
        var title = new Rect(rect.x, rect.y, rect.width, WindowTitleHeight);

        ref WindowState st = ref GuiUtility.GetState<WindowState>(id);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (title.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    st.Grab = new Vec2(ev.MousePosition.x - rect.x, ev.MousePosition.y - rect.y);
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    rect.x = ev.MousePosition.x - st.Grab.x;
                    rect.y = ev.MousePosition.y - st.Grab.y;
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                style.Draw(rect, id);
                GuiRenderer.DrawRect(title, new Color(58, 74, 120, 255), 1);
                break;
        }

        Rect contentRect = style.Padding.Remove(rect);
        GuiLayoutUtility.BeginArea(contentRect);
        func(windowId);
        GuiLayoutUtility.EndArea();
        return rect;
    }


    // --- Layout sarmalayicilar (stil olculeri + margin layout'a girer) ---

    static Rect LayoutRect(GuiStyle style, float defW, float defH, ReadOnlySpan<LayoutOption> options)
    {
        float w = style.FixedWidth > 0 ? style.FixedWidth : defW;
        float h = style.FixedHeight > 0 ? style.FixedHeight : defH;
        bool stretchW = style.FixedWidth <= 0;
        Rect r = GuiLayoutUtility.GetRect(w, w, h, h, stretchW, false, options);
        return r;
    }

    public static void LayoutBox(float height, ReadOnlySpan<LayoutOption> options = default)
    {
        GuiStyle style = Skin.Box;
        Box(GuiLayoutUtility.GetRect(20, 20, height, height, true, false, options), style);
    }

    public static bool LayoutButton(ReadOnlySpan<LayoutOption> options = default)
        => Button(LayoutRect(Skin.Button, 80, 24, options), Skin.Button);

    public static bool LayoutButton(ReadOnlySpan<char> text, ReadOnlySpan<LayoutOption> options = default)
    {
        GuiStyle style = Skin.Button;
        float w = 80;
        if (Font != null && !text.IsEmpty)
            w = Font.TextSize(text, FontSize).x + style.Padding.Horizontal + 8;
        float h = style.FixedHeight > 0 ? style.FixedHeight : 24;
        Rect r = GuiLayoutUtility.GetRect(w, w, h, h, true, false, options);
        return Button(r, text, style);
    }

    public static void LayoutLabel(ReadOnlySpan<char> text, ReadOnlySpan<LayoutOption> options = default)
    {
        Vec2 size = Font != null ? Font.TextSize(text, FontSize) : new Vec2(60, 18);
        Rect r = GuiLayoutUtility.GetRect(size.x + 8, size.x + 8, size.y + 4, size.y + 4, false, false, options);
        Label(r, text);
    }

    public static bool LayoutToggle(bool value, ReadOnlySpan<LayoutOption> options = default)
        => Toggle(LayoutRect(Skin.Toggle, 18, 18, options), value, Skin.Toggle);

    public static float LayoutHorizontalSlider(float value, float min, float max,
        ReadOnlySpan<LayoutOption> options = default)
        => HorizontalSlider(LayoutRect(Skin.HorizontalSlider, 120, 18, options), value, min, max);
}
