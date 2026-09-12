using System;

namespace DigitoyEngine;

// Unity EventType karsiligi. UI kodu her event icin bastan kosulur (cok-pass):
// once Layout, sonra input event'leri, en son Repaint. Kontrol sayisi/sirasi
// TUM event'lerde ayni olmalidir (layout cache replay invariant'i).
public enum EventType : byte
{
    Ignore = 0,
    Layout,
    Repaint,
    MouseDown,
    MouseUp,
    MouseMove,
    MouseDrag,
    ScrollWheel,
    KeyDown,
    KeyUp,
    TextInput,
    Used,
}

[Flags]
public enum EventModifiers : byte
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
    Super = 8,
}

// Unity Event karsiligi. TEK instance mutate edilir (alloc yok); aktif pass'in
// eventi Event.Current. MousePosition GuiClip'in LOKAL uzayindadir — clip
// push/pop ettikce guncellenir; ham global deger GlobalMousePosition'da.
public sealed class Event
{
    public static Event Current { get; internal set; }

    EventType _type;
    EventType _rawType;

    public Vec2 MousePosition;              // aktif clip uzayinda
    internal Vec2 GlobalMousePosition;      // pencere/ekran uzayinda
    public Vec2 Delta;                      // mouse delta veya scroll miktari
    public int Button;                      // 0 sol, 1 sag, 2 orta
    public int ClickCount;                  // double-click tespiti
    public int KeyCode;                     // GLFW tus kodu (GLFWConst.KEY_*)
    public char Character;                  // TextInput karakteri
    public EventModifiers Modifiers;

    // Use() sonrasi Used doner; RawType orijinal tipi korur (Unity semantigi).
    public EventType Type => _type;
    public EventType RawType => _rawType;

    public bool IsMouse => _rawType is EventType.MouseDown or EventType.MouseUp
        or EventType.MouseMove or EventType.MouseDrag;
    public bool IsKey => _rawType is EventType.KeyDown or EventType.KeyUp or EventType.TextInput;

    public void Use() => _type = EventType.Used;

    // Hot/keyboard filtresi (Unity Event.GetTypeForControl): mouse event'leri
    // hot control baskasindayken, key event'leri keyboard focus baskasindayken
    // bu kontrole Ignore olarak gorunur.
    public EventType GetTypeForControl(int controlId)
    {
        if (_type == EventType.Used)
            return EventType.Used;
        switch (_type)
        {
            case EventType.MouseDown:
            case EventType.MouseUp:
            case EventType.MouseDrag:
            case EventType.MouseMove:
                int hot = GuiUtility.HotControl;
                return (hot == 0 || hot == controlId) ? _type : EventType.Ignore;
            case EventType.KeyDown:
            case EventType.KeyUp:
            case EventType.TextInput:
                return GuiUtility.KeyboardControl == controlId ? _type : EventType.Ignore;
            default:
                return _type;
        }
    }

    // Pass baslatilirken host tarafindan doldurulur (instance yeniden kullanilir).
    internal void Set(
        EventType type, Vec2 globalMouse, Vec2 delta, int button, int clickCount,
        int keyCode, char character, EventModifiers modifiers)
    {
        _type = type;
        _rawType = type;
        GlobalMousePosition = globalMouse;
        MousePosition = globalMouse;
        Delta = delta;
        Button = button;
        ClickCount = clickCount;
        KeyCode = keyCode;
        Character = character;
        Modifiers = modifiers;
    }
}
