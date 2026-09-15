#if DE_EDITOR
using System;

namespace DigitoyEngine.Editor;

// GUI surucusu: GuiInput kuyrugundaki event'leri Unity'nin cok-pass modeliyle
// kosar: her frame Layout -> (kuyruktaki input event'leri sirayla) -> Repaint.
// UI kodu (GuiFunc) her pass'te bastan cagrilir; kontrol sirasi sabit kalmalidir.
// Pencere GuiInput.Attach ile kaydedilmis olmalidir.
public sealed class GuiHost
{
    public delegate void GuiFunc();

    readonly Event _ev = new Event();

    // screen MANTIKSAL (point) uzaydadir; scale = pencerenin DPI olcegi.
    // UNITY MODELI: HER event pass'inden once ayri bir Layout pass'i kosulur —
    // bir input pass'inde degisen state (secim vb.) sonraki pass'in layout
    // cache'ini gecersiz kilamaz.
    public void Frame(IntPtr window, Rect screen, GuiFunc gui, float scale = 1f)
    {
        float inv = scale > 0 ? 1f / scale : 1f;
        Vec2 mouse = Mul(GuiInput.MousePos(window), inv);
        GuiCursorManager.BeginFrame();

        ReadOnlySpan<GuiInput.Queued> events = GuiInput.Events(window);
        for (int i = 0; i < events.Length; i++)
        {
            ref readonly GuiInput.Queued q = ref events[i];
            // Scroll delta'si tik sayisidir, DPI'dan bagimsiz — olceklenmez.
            Vec2 delta = q.Type == EventType.ScrollWheel ? q.Delta : Mul(q.Delta, inv);
            Vec2 m = Mul(q.Mouse, inv);
            RunPass(EventType.Layout, m, default, 0, 1, 0, '\0', EventModifiers.None, screen, gui);
            RunPass(q.Type, m, delta, q.Button, q.ClickCount, q.Key, q.Character, q.Mods, screen, gui);
            mouse = m;
        }
        GuiInput.Clear(window);

        RunPass(EventType.Layout, mouse, default, 0, 1, 0, '\0', EventModifiers.None, screen, gui);
        RunPass(EventType.Repaint, mouse, default, 0, 1, 0, '\0', EventModifiers.None, screen, gui);
        GuiCursorManager.Apply(window);
    }

    static Vec2 Mul(Vec2 v, float s) => new Vec2(v.x * s, v.y * s);

    void RunPass(EventType type, Vec2 mouse, Vec2 delta, int button, int clickCount,
        int keyCode, char character, EventModifiers mods, Rect screen, GuiFunc gui)
    {
        _ev.Set(type, mouse, delta, button, clickCount, keyCode, character, mods);
        GuiUtility.BeginPass(_ev, screen);
        GuiLayoutUtility.BeginPass(type, screen);
        gui();
        GuiLayoutUtility.EndPass(type);
    }
}
#endif
