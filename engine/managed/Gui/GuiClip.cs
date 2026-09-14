namespace DigitoyEngine;

// Unity GUIClip karsiligi: clip-rect stack'i. Push edilen her rect hem cizimi
// kirpar (fiziksel scissor) hem KOORDINAT UZAYINI degistirir: clip icinde (0,0)
// = rect'in sol ustu. Scroll tamamen scrollOffset ile cozulur (icerik clip
// icinde kaydirilir). Event.Current.MousePosition da push/pop'ta lokal uzaya
// cevrilir — widget kodu hep kendi lokal koordinatinda calisir.
public static class GuiClip
{
    struct Entry
    {
        public Vec2 Offset;    // lokal -> global donusum ofseti
        public Rect Physical;  // global uzayda kesisim scissor rect'i
    }

    const int MaxDepth = 64;
    static readonly Entry[] _stack = new Entry[MaxDepth];
    static int _depth;

    // Pass basi: stack ekran rect'iyle kurulur (GuiUtility.BeginPass cagirir).
    internal static void Reset(Rect screen, Event ev)
    {
        _depth = 0;
        _stack[0] = new Entry { Offset = new Vec2(0, 0), Physical = screen };
        if (ev != null)
            ev.MousePosition = ev.GlobalMousePosition;
    }

    // rect PARENT'IN lokal uzayindadir; scrollOffset icerigi yukari/sola kaydirir.
    public static void Push(in Rect rect, Vec2 scrollOffset = default)
    {
        ref Entry top = ref _stack[_depth];
        var globalRect = new Rect(rect.x + top.Offset.x, rect.y + top.Offset.y, rect.width, rect.height);

        _depth++;
        _stack[_depth] = new Entry
        {
            Offset = new Vec2(globalRect.x - scrollOffset.x, globalRect.y - scrollOffset.y),
            Physical = Rect.Intersect(top.Physical, globalRect),
        };
        SyncMouse();
    }

    public static void Pop()
    {
        if (_depth > 0)
            _depth--;
        SyncMouse();
    }

    // Overlay cizimi icin: ekran kokune doner (popup aktif clip disina tasabilir).
    public static void PushScreen()
    {
        if (_depth + 1 >= MaxDepth)
            return;
        _depth++;
        _stack[_depth] = _stack[0];
        SyncMouse();
    }

    // Pass'in ekran rect'i (global uzay) — popup konum clamp'i icin.
    public static Rect ScreenRect => _stack[0].Physical;

    // Aktif fiziksel scissor rect'i (global piksel; cizim koprusu kullanir).
    public static Rect Physical => _stack[_depth].Physical;

    // Lokal -> global donusum (cizim koprusu quad konumlandirirken kullanir).
    public static Vec2 Unclip(Vec2 local)
    {
        ref Entry top = ref _stack[_depth];
        return new Vec2(local.x + top.Offset.x, local.y + top.Offset.y);
    }

    public static Rect Unclip(in Rect local)
    {
        ref Entry top = ref _stack[_depth];
        return new Rect(local.x + top.Offset.x, local.y + top.Offset.y, local.width, local.height);
    }

    // Aktif clip'in gorunur alani lokal uzayda (scroll cull'u icin).
    public static Rect VisibleRect
    {
        get
        {
            ref Entry top = ref _stack[_depth];
            var p = top.Physical;
            return new Rect(p.x - top.Offset.x, p.y - top.Offset.y, p.width, p.height);
        }
    }

    static void SyncMouse()
    {
        var ev = Event.Current;
        if (ev == null)
            return;
        ref Entry top = ref _stack[_depth];
        ev.MousePosition = new Vec2(
            ev.GlobalMousePosition.x - top.Offset.x,
            ev.GlobalMousePosition.y - top.Offset.y);
    }
}
