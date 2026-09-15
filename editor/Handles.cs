using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Unity HandleUtility modeli: her handle Layout pass'inde imlece uzakligini
// bildirir (AddControl); MouseDown'da yalniz EN YAKIN kontrol isabet alir.
// Oncelik hardcode edilmez — esitlikte ILK kayit kazanir (kayit sirasi = oncelik).
public static class HandleUtility
{
    public const float PickDistance = 5f;

    static int _nearest;
    static float _nearestDist;
    static bool _enabled;

    public static int NearestControl => _nearest;

    // Her Layout pass'inin basinda cagrilir (SceneViewPanel). mouseInside=false
    // ise AddControl no-op — imlec baska paneldeyken kenar kontrolleri tik calmaz.
    // (Kontroller yine de id ALMALI: pass'ler arasi kontrol sayisi invariant'i.)
    public static void BeginLayout(bool mouseInside)
    {
        _nearest = 0;
        _nearestDist = PickDistance;
        _enabled = mouseInside;
    }

    public static void AddControl(int id, float distToMouse)
    {
        if (_enabled && distToMouse < _nearestDist)
        {
            _nearestDist = distToMouse;
            _nearest = id;
        }
    }

    // --- mesafe yardimcilari (panel uzayi; 0 = isabet icinde) ---

    public static float DistanceToRect(in Rect r, Vec2 p)
    {
        if (r.width <= 0f || r.height <= 0f)
            return float.MaxValue;
        float dx = MathF.Max(MathF.Max(r.x - p.x, 0f), p.x - (r.x + r.width));
        float dy = MathF.Max(MathF.Max(r.y - p.y, 0f), p.y - (r.y + r.height));
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // Cember CIZGISINE uzaklik (halka isabeti); band = cizginin yari kalinligi.
    public static float DistanceToCircleEdge(Vec2 center, float radius, float band, Vec2 p)
    {
        float dx = p.x - center.x, dy = p.y - center.y;
        float d = MathF.Abs(MathF.Sqrt(dx * dx + dy * dy) - radius);
        return MathF.Max(0f, d - band);
    }

    // Dunya deltasi -> parent-lokal delta (GoDoc.Pos lokaldir).
    public static unsafe Vec2 WorldDeltaToParentLocal(GameObject live, Vec2 d)
    {
        var p = live.transform.parent;
        if (p == null)
            return d;
        ref var m = ref p._getWorldMatrix();
        float a = m.m[0], b = m.m[1], c = m.m[4], e = m.m[5];
        float det = a * e - c * b;
        if (MathF.Abs(det) < 1e-8f)
            return d;
        return new Vec2((e * d.x - c * d.y) / det, (-b * d.x + a * d.y) / det);
    }
}

// Etkilesim primitifi: GetControlID + AddControl + HotControl kalibini tek
// cagriya indirir (Unity Handles.Slider'in cekirdegi). Cizim cagiranin isidir.
public static class Handles
{
    public enum Act : byte { None, Pressed, Dragged, Released }

    static Vec2 _startMouse; // hot kontrolun basildigi panel konumu

    // Basistan kumulatif surukleme referansi (ara frame kaybi birikmez).
    public static Vec2 StartMouse => _startMouse;

    // distToMouse: bu pass'te kontrolun imlece uzakligi (Layout'ta kaydedilir).
    public static Act Drag(int id, float distToMouse, Event ev)
    {
        switch (ev.GetTypeForControl(id))
        {
            case EventType.Layout:
                HandleUtility.AddControl(id, distToMouse);
                break;
            case EventType.MouseDown:
                if (ev.Button == 0 && HandleUtility.NearestControl == id)
                {
                    GuiUtility.HotControl = id;
                    _startMouse = ev.MousePosition;
                    ev.Use();
                    return Act.Pressed;
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    ev.Use();
                    return Act.Dragged;
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                    return Act.Released;
                }
                break;
        }
        return Act.None;
    }

    public static bool IsHot(int id) => GuiUtility.HotControl == id;
    public static bool IsHover(int id) => GuiUtility.HotControl == 0 && HandleUtility.NearestControl == id;
}
