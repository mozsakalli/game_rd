using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DigitoyEngine;

// GLFW callback'lerinden pencere-basina EVENT KUYRUGU (eski engine_host_glfw.c
// dispatch deseninin kuyruklu hali). Polling diff'inin kacirdigi her seyi yakalar:
// scroll wheel, ayni frame'de down+up, klavye, double-click. Move/Drag ardisiksa
// COALESCE edilir (frame'de yuzlerce move eventi pass patlatmasin); down/up/
// scroll/key asla ezilmez — eski koddaki "DRAG down'i ezmesin" dersinin genellemesi.
public static unsafe class GuiInput
{
    public struct Queued
    {
        public EventType Type;
        public Vec2 Mouse;
        public Vec2 Delta;
        public int Button;
        public int Key;
        public char Character;
        public int ClickCount;
        public EventModifiers Mods;
    }

    const int MaxWindows = 9, MaxEvents = 32;

    struct Slot
    {
        public IntPtr Window;
        public bool Used;
        public Vec2 Mouse; // son bilinen imlec konumu (pencere-lokal)
        public int Count;
    }

    static readonly Slot[] _slots = new Slot[MaxWindows];
    static readonly Queued[] _events = new Queued[MaxWindows * MaxEvents];

    // Double-click takibi (pencereler arasi tek sayac; eski C ile ayni esikler).
    static double _lastClickTime;
    static Vec2 _lastClickPos;
    static IntPtr _lastClickWindow;
    static int _clickCount = 1;

    public static void Attach(IntPtr window)
    {
        for (int i = 0; i < MaxWindows; i++)
        {
            if (_slots[i].Used)
                continue;
            _slots[i] = new Slot { Window = window, Used = true, Mouse = new Vec2(-1000, -1000) };
            GLFW.SetMouseButtonCallback(window, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, int, int, void>)&MouseButtonCb);
            GLFW.SetCursorPosCallback(window, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, double, double, void>)&CursorCb);
            GLFW.SetScrollCallback(window, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, double, double, void>)&ScrollCb);
            GLFW.SetKeyCallback(window, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, int, int, int, int, void>)&KeyCb);
            GLFW.SetCharCallback(window, (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, uint, void>)&CharCb);
            return;
        }
        throw new InvalidOperationException("GuiInput: pencere slotu doldu");
    }

    public static void Detach(IntPtr window)
    {
        int s = SlotOf(window);
        if (s >= 0)
            _slots[s].Used = false;
    }

    public static Vec2 MousePos(IntPtr window)
    {
        int s = SlotOf(window);
        return s >= 0 ? _slots[s].Mouse : default;
    }

    public static ReadOnlySpan<Queued> Events(IntPtr window)
    {
        int s = SlotOf(window);
        return s >= 0 ? _events.AsSpan(s * MaxEvents, _slots[s].Count) : default;
    }

    public static void Clear(IntPtr window)
    {
        int s = SlotOf(window);
        if (s >= 0)
            _slots[s].Count = 0;
    }

    static int SlotOf(IntPtr window)
    {
        for (int i = 0; i < MaxWindows; i++)
            if (_slots[i].Used && _slots[i].Window == window)
                return i;
        return -1;
    }

    static void Enqueue(int slot, in Queued q, bool coalesce)
    {
        ref Slot s = ref _slots[slot];
        int baseIdx = slot * MaxEvents;
        if (coalesce && s.Count > 0 && _events[baseIdx + s.Count - 1].Type == q.Type)
        {
            _events[baseIdx + s.Count - 1] = q; // ardisik move/drag: sonuncusu yeter
            return;
        }
        if (s.Count == MaxEvents)
            return; // tasma: en eskiyi korumak daha guvenli, yenisini dusur
        _events[baseIdx + s.Count++] = q;
    }

    static EventModifiers Mods(IntPtr w)
    {
        EventModifiers m = EventModifiers.None;
        if (GLFW.GetKey(w, GLFWConst.KEY_LEFT_SHIFT) == GLFWConst.PRESS || GLFW.GetKey(w, GLFWConst.KEY_RIGHT_SHIFT) == GLFWConst.PRESS)
            m |= EventModifiers.Shift;
        if (GLFW.GetKey(w, GLFWConst.KEY_LEFT_CONTROL) == GLFWConst.PRESS || GLFW.GetKey(w, GLFWConst.KEY_RIGHT_CONTROL) == GLFWConst.PRESS)
            m |= EventModifiers.Control;
        if (GLFW.GetKey(w, GLFWConst.KEY_LEFT_ALT) == GLFWConst.PRESS || GLFW.GetKey(w, GLFWConst.KEY_RIGHT_ALT) == GLFWConst.PRESS)
            m |= EventModifiers.Alt;
        return m;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void MouseButtonCb(IntPtr win, int button, int action, int mods)
    {
        int s = SlotOf(win);
        if (s < 0)
            return;
        Vec2 mouse = _slots[s].Mouse;
        bool down = action == GLFWConst.PRESS;
        int clicks = 1;
        if (down)
        {
            double now = GLFW.GetTime();
            float dx = mouse.x - _lastClickPos.x, dy = mouse.y - _lastClickPos.y;
            if (win == _lastClickWindow && now - _lastClickTime < 0.35 && dx * dx + dy * dy < 25f)
                _clickCount++;
            else
                _clickCount = 1;
            _lastClickTime = now;
            _lastClickPos = mouse;
            _lastClickWindow = win;
            clicks = _clickCount;
        }
        Enqueue(s, new Queued
        {
            Type = down ? EventType.MouseDown : EventType.MouseUp,
            Mouse = mouse,
            Button = button,
            ClickCount = clicks,
            Mods = Mods(win),
        }, coalesce: false);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void CursorCb(IntPtr win, double x, double y)
    {
        int s = SlotOf(win);
        if (s < 0)
            return;
        var mouse = new Vec2((float)x, (float)y);
        var delta = new Vec2(mouse.x - _slots[s].Mouse.x, mouse.y - _slots[s].Mouse.y);
        _slots[s].Mouse = mouse;
        bool anyDown = GLFW.GetMouseButton(win, 0) == GLFWConst.PRESS
            || GLFW.GetMouseButton(win, 1) == GLFWConst.PRESS
            || GLFW.GetMouseButton(win, 2) == GLFWConst.PRESS;
        Enqueue(s, new Queued
        {
            Type = anyDown ? EventType.MouseDrag : EventType.MouseMove,
            Mouse = mouse,
            Delta = delta,
            Mods = Mods(win),
        }, coalesce: true);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void ScrollCb(IntPtr win, double sx, double sy)
    {
        int s = SlotOf(win);
        if (s < 0)
            return;
        Enqueue(s, new Queued
        {
            Type = EventType.ScrollWheel,
            Mouse = _slots[s].Mouse,
            Delta = new Vec2((float)sx, (float)sy),
            Mods = Mods(win),
        }, coalesce: false);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void KeyCb(IntPtr win, int key, int scancode, int action, int mods)
    {
        int s = SlotOf(win);
        if (s < 0)
            return;
        if (action == GLFWConst.REPEAT)
            action = GLFWConst.PRESS; // basili tutma tekrar uretsin (backspace vb.)
        Enqueue(s, new Queued
        {
            Type = action == GLFWConst.PRESS ? EventType.KeyDown : EventType.KeyUp,
            Mouse = _slots[s].Mouse,
            Key = key,
            Mods = Mods(win),
        }, coalesce: false);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    static void CharCb(IntPtr win, uint codepoint)
    {
        int s = SlotOf(win);
        if (s < 0)
            return;
        Enqueue(s, new Queued
        {
            Type = EventType.TextInput,
            Mouse = _slots[s].Mouse,
            Character = codepoint <= char.MaxValue ? (char)codepoint : '\uFFFD',
            Mods = Mods(win),
        }, coalesce: false);
    }
}
