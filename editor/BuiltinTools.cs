using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Builtin donusum araclari (Move/Rotate/Scale) — SceneView'a hardcoded degil,
// diger araclarla ayni [EditorTool] yolundan gelirler.

// Ortak cizim yardimcilari (eksen kollari, uc isaretleri, durum renkleri).
static class GizmoDraw
{
    public const float AxisLen = 44f;
    public static readonly Color Shadow = new Color(0, 0, 0, 110);
    public static readonly Color HotYellow = new Color(255, 222, 60, 255);
    public static readonly Color XRed = new Color(230, 65, 60, 255);
    public static readonly Color YGreen = new Color(120, 205, 70, 255);

    // Kol rengi: aktif surukleme = sari (Unity), hover = acik ton.
    public static Color AxisColor(bool hot, bool hover, Color normal)
    {
        if (hot)
            return HotYellow;
        if (hover)
            return new Color((byte)Math.Min(255, normal.r + 50), (byte)Math.Min(255, normal.g + 50),
                (byte)Math.Min(255, normal.b + 50), 255);
        return normal;
    }

    // X/Y kollari: golge + cizgi (Move ve Scale ayni govde).
    public static void DrawArms(Vec2 c, Color xCol, Color yCol)
    {
        GuiRenderer.DrawRect(new Rect(c.x + 8, c.y - 2, AxisLen - 4, 4), Shadow, 3);
        GuiRenderer.DrawRect(new Rect(c.x + 8, c.y - 1, AxisLen - 4, 2), xCol, 4);
        GuiRenderer.DrawRect(new Rect(c.x - 2, c.y + 8, 4, AxisLen - 4), Shadow, 3);
        GuiRenderer.DrawRect(new Rect(c.x - 1, c.y + 8, 2, AxisLen - 4), yCol, 4);
    }

    // Merkez serbest kolu: koyu cerceve + yari saydam dolgu.
    public static void DrawCenter(Vec2 c, bool hot, bool hover)
    {
        Color fill = hot ? new Color(255, 222, 60, 210)
            : hover ? new Color(255, 255, 255, 120)
            : new Color(255, 255, 255, 60);
        GuiRenderer.DrawRect(new Rect(c.x - 7, c.y - 7, 14, 14), new Color(20, 22, 28, 200), 3);
        GuiRenderer.DrawRect(new Rect(c.x - 6, c.y - 6, 12, 12), fill, 4);
    }
}

// Move/Scale ortak omurgasi: merkez + X + Y kollari, isabet rect'leri, hover.
public abstract class AxisTool : EditorTool
{
    protected int HotAxis = -1; // suruklenen kol (-1 yok, 0 merkez, 1 X, 2 Y)

    public sealed override void OnToolGui(SceneViewPanel view, Event ev)
    {
        // Kontrol sayisi pass'ler arasi sabit: id'ler kosulsuz alinir.
        int hash = GetType().Name.GetHashCode();
        int idFree = GuiUtility.GetControlID(hash, FocusType.Passive);
        int idX = GuiUtility.GetControlID(hash, FocusType.Passive);
        int idY = GuiUtility.GetControlID(hash, FocusType.Passive);

        var es = App.EditScene;
        var g = es?.FindGo(Selection.DocId);
        var live = g != null ? es.Live(g.Id) : null;
        if (live == null)
            return;

        Vec3 wp = live.transform.position;
        Vec2 c = view.WorldToPanel(new Vec2(wp.x, wp.y));
        // Isabet alanlari kol cizgisi + uc isaretini kapsar (gorselden genis).
        var freeRect = new Rect(c.x - 7, c.y - 7, 14, 14);
        var xRect = new Rect(c.x + 7, c.y - 6, GizmoDraw.AxisLen + 12, 12);
        var yRect = new Rect(c.x - 6, c.y + 7, 12, GizmoDraw.AxisLen + 12);

        // Merkez ONCE kaydedilir: esit uzaklikta oncelik kayit sirasindan gelir.
        DoAxis(idFree, 0, HandleUtility.DistanceToRect(freeRect, ev.MousePosition), view, es, g, live, ev);
        DoAxis(idX, 1, HandleUtility.DistanceToRect(xRect, ev.MousePosition), view, es, g, live, ev);
        DoAxis(idY, 2, HandleUtility.DistanceToRect(yRect, ev.MousePosition), view, es, g, live, ev);

        if (ev.Type == EventType.Repaint)
        {
            int hover = HotAxis >= 0 ? HotAxis
                : Handles.IsHover(idFree) ? 0
                : Handles.IsHover(idX) ? 1
                : Handles.IsHover(idY) ? 2 : -1;
            if (hover >= 0)
                GuiCursorManager.Request(hover switch
                {
                    1 => GuiCursor.ResizeH,
                    2 => GuiCursor.ResizeV,
                    _ => GuiCursor.ResizeAll,
                });
            Draw(c, hover);
        }
    }

    void DoAxis(int id, int axis, float dist, SceneViewPanel view, EditorScene es,
        SceneDoc.GoDoc g, GameObject live, Event ev)
    {
        switch (Handles.Drag(id, dist, ev))
        {
            case Handles.Act.Pressed:
                HotAxis = axis;
                OnPress(es, g);
                break;
            case Handles.Act.Dragged:
                OnDrag(axis, view, es, g, live, ev);
                break;
            case Handles.Act.Released:
                HotAxis = -1;
                break;
        }
    }

    protected abstract void OnPress(EditorScene es, SceneDoc.GoDoc g);
    protected abstract void OnDrag(int axis, SceneViewPanel view, EditorScene es,
        SceneDoc.GoDoc g, GameObject live, Event ev);
    protected abstract void Draw(Vec2 c, int hover);
}

[EditorTool("Move", Order = 0, Key = 'W')]
public sealed class MoveTool : AxisTool
{
    // Tasima TUM secime uygulanir: basista tum secilmislerin start durumlari.
    // Driven pozisyonlu nesne (ITransformDriver): proxy alani varsa (anchoredPos)
    // surukleme ORAYA yazilir (Unity move tool anchoredPosition yazar), yoksa kilitli.
    struct Entry
    {
        public SceneDoc.GoDoc G;
        public Vec3 StartPos;
        public SceneDoc.CompDoc ProxyComp;               // null = normal pozisyon tasima
        public SerializedType.FieldSchema ProxyField;
        public Vec2 StartVal;
    }

    readonly List<Entry> _multiStart = new();

    protected override void OnPress(EditorScene es, SceneDoc.GoDoc g)
    {
        _multiStart.Clear();
        foreach (int sid in Selection.Ids)
        {
            var sg = es.FindGo(sid);
            if (sg == null)
                continue;
            var slive = es.Live(sg.Id);
            ITransformDriver driver = null;
            var driven = slive != null
                ? TransformDriver.Driven(slive, out driver)
                : DrivenTransformProperties.None;
            if (slive == null || (driven & DrivenTransformProperties.Position) == 0)
            {
                _multiStart.Add(new Entry { G = sg, StartPos = sg.Pos });
                continue;
            }
            string fieldName = driver.PositionEditField;
            if (fieldName == null)
                continue; // tamamen kilitli: secimde dursa da tasinmaz
            var comp = (Component)driver;
            string typeName = comp.GetType().Name;
            SceneDoc.CompDoc cd = null;
            foreach (var c in sg.Components)
                if (c.Type == typeName) { cd = c; break; }
            var entry = App.Catalog?.Find(typeName);
            SerializedType.FieldSchema f = null;
            if (entry != null)
                foreach (var fs in entry.Schema)
                    if (fs.Name == fieldName) { f = fs; break; }
            if (cd == null || f == null)
                continue;
            _multiStart.Add(new Entry
            {
                G = sg,
                ProxyComp = cd,
                ProxyField = f,
                StartVal = f.Get(comp) is Vec2 v ? v : default,
            });
        }
    }

    protected override void OnDrag(int axis, SceneViewPanel view, EditorScene es,
        SceneDoc.GoDoc g, GameObject live, Event ev)
    {
        var d = new Vec2((ev.MousePosition.x - Handles.StartMouse.x) / view.Zoom,
                         (ev.MousePosition.y - Handles.StartMouse.y) / view.Zoom);
        if (axis == 1)
            d.y = 0;
        else if (axis == 2)
            d.x = 0;
        for (int i = 0; i < _multiStart.Count; i++)
        {
            var e = _multiStart[i];
            var slive = es.Live(e.G.Id);
            if (slive == null)
                continue;
            Vec2 ld = HandleUtility.WorldDeltaToParentLocal(slive, d);
            if (e.ProxyComp != null)
            {
                var nv = new Vec2(e.StartVal.x + ld.x, e.StartVal.y + ld.y);
                es.SetProp(e.G, e.ProxyComp, e.ProxyField, V2(e.StartVal), V2(nv));
            }
            else
            {
                es.SetTransform(e.G,
                    new Vec3(e.StartPos.x + ld.x, e.StartPos.y + ld.y, e.StartPos.z),
                    e.G.Rot, e.G.Scale);
            }
        }
    }

    static string V2(Vec2 v)
        => v.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " "
         + v.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    protected override void Draw(Vec2 c, int hover)
    {
        Color xCol = GizmoDraw.AxisColor(HotAxis == 1, hover == 1, GizmoDraw.XRed);
        Color yCol = GizmoDraw.AxisColor(HotAxis == 2, hover == 2, GizmoDraw.YGreen);
        GizmoDraw.DrawArms(c, xCol, yCol);
        // Ok baslari (elmas).
        float tipX = c.x + 8 + GizmoDraw.AxisLen, tipY = c.y + 8 + GizmoDraw.AxisLen;
        GuiRenderer.DrawDiamond(new Vec2(tipX, c.y), 7f, GizmoDraw.Shadow, 3);
        GuiRenderer.DrawDiamond(new Vec2(tipX, c.y), 5.5f, xCol, 4);
        GuiRenderer.DrawDiamond(new Vec2(c.x, tipY), 7f, GizmoDraw.Shadow, 3);
        GuiRenderer.DrawDiamond(new Vec2(c.x, tipY), 5.5f, yCol, 4);
        GizmoDraw.DrawCenter(c, HotAxis == 0, hover == 0);
    }
}

[EditorTool("Scale", Order = 2, Key = 'R')]
public sealed class ScaleTool : AxisTool
{
    Vec3 _startScale;

    protected override void OnPress(EditorScene es, SceneDoc.GoDoc g) => _startScale = g.Scale;

    protected override void OnDrag(int axis, SceneViewPanel view, EditorScene es,
        SceneDoc.GoDoc g, GameObject live, Event ev)
    {
        float dx = (ev.MousePosition.x - Handles.StartMouse.x) / 100f;
        float dy = (ev.MousePosition.y - Handles.StartMouse.y) / 100f;
        Vec3 sc = _startScale;
        if (axis == 0)
        {
            float f = MathF.Max(0.01f, 1f + dx - dy); // sag/yukari = buyut
            sc = new Vec3(_startScale.x * f, _startScale.y * f, _startScale.z);
        }
        else if (axis == 1)
        {
            sc.x = _startScale.x * MathF.Max(0.01f, 1f + dx);
        }
        else
        {
            sc.y = _startScale.y * MathF.Max(0.01f, 1f + dy);
        }
        es.SetTransform(g, g.Pos, g.Rot, sc);
    }

    protected override void Draw(Vec2 c, int hover)
    {
        Color xCol = GizmoDraw.AxisColor(HotAxis == 1, hover == 1, GizmoDraw.XRed);
        Color yCol = GizmoDraw.AxisColor(HotAxis == 2, hover == 2, GizmoDraw.YGreen);
        GizmoDraw.DrawArms(c, xCol, yCol);
        // Kare uclar (Unity scale tutamaci).
        float tipX = c.x + 8 + GizmoDraw.AxisLen, tipY = c.y + 8 + GizmoDraw.AxisLen;
        GuiRenderer.DrawRect(new Rect(tipX - 5.5f, c.y - 5.5f, 11, 11), GizmoDraw.Shadow, 3);
        GuiRenderer.DrawRect(new Rect(tipX - 4.5f, c.y - 4.5f, 9, 9), xCol, 4);
        GuiRenderer.DrawRect(new Rect(c.x - 5.5f, tipY - 5.5f, 11, 11), GizmoDraw.Shadow, 3);
        GuiRenderer.DrawRect(new Rect(c.x - 4.5f, tipY - 4.5f, 9, 9), yCol, 4);
        GizmoDraw.DrawCenter(c, HotAxis == 0, hover == 0);
    }
}

[EditorTool("Rotate", Order = 1, Key = 'E')]
public sealed class RotateTool : EditorTool
{
    const float RingR = 48f;   // dondurme halkasi yaricapi
    const float RingBand = 4f; // halka isabet bandi (+PickDistance = eski ±9)

    Vec3 _startRot;
    Vec2 _center; // drag basindaki merkez (panel uzayi)
    bool _hot;

    public override void OnToolGui(SceneViewPanel view, Event ev)
    {
        int id = GuiUtility.GetControlID("RotateTool".GetHashCode(), FocusType.Passive);

        var es = App.EditScene;
        var g = es?.FindGo(Selection.DocId);
        var live = g != null ? es.Live(g.Id) : null;
        if (live == null)
            return;

        Vec3 wp = live.transform.position;
        Vec2 c = view.WorldToPanel(new Vec2(wp.x, wp.y));
        float dist = MathF.Min(
            HandleUtility.DistanceToRect(new Rect(c.x - 7, c.y - 7, 14, 14), ev.MousePosition),
            HandleUtility.DistanceToCircleEdge(c, RingR, RingBand, ev.MousePosition));

        switch (Handles.Drag(id, dist, ev))
        {
            case Handles.Act.Pressed:
                _hot = true;
                _startRot = g.Rot;
                _center = c;
                break;
            case Handles.Act.Dragged:
                // Merkez etrafinda aci farki; 2D'de tum donusler z ekseninde —
                // parent'li objede de lokal delta = ekran delta'si.
                float a0 = MathF.Atan2(Handles.StartMouse.y - _center.y, Handles.StartMouse.x - _center.x);
                float a1 = MathF.Atan2(ev.MousePosition.y - _center.y, ev.MousePosition.x - _center.x);
                float deg = (a1 - a0) * (180f / MathF.PI);
                if ((ev.Modifiers & EventModifiers.Shift) != 0)
                    deg = MathF.Round(deg / 15f) * 15f; // Shift: 15 derece snap
                es.SetTransform(g, g.Pos,
                    new Vec3(_startRot.x, _startRot.y, _startRot.z + deg), g.Scale);
                break;
            case Handles.Act.Released:
                _hot = false;
                break;
        }

        if (ev.Type == EventType.Repaint)
        {
            bool hover = _hot || Handles.IsHover(id);
            if (hover)
                GuiCursorManager.Request(GuiCursor.Hand);
            // Halka: nokta dizisi (z-donusu mavi, Unity gibi) + aci isaretcisi.
            Color ring = GizmoDraw.AxisColor(_hot, hover, new Color(90, 160, 245, 255));
            const int segs = 36;
            for (int i = 0; i < segs; i++)
            {
                float a = i * (MathF.PI * 2f / segs);
                float px = c.x + MathF.Cos(a) * RingR, py = c.y + MathF.Sin(a) * RingR;
                GuiRenderer.DrawRect(new Rect(px - 1.5f, py - 1.5f, 3f, 3f), ring, 4);
            }
            float ang = live.transform.eulerAngles.z * (MathF.PI / 180f);
            float mx = c.x + MathF.Cos(ang) * RingR, my = c.y + MathF.Sin(ang) * RingR;
            GuiRenderer.DrawDiamond(new Vec2(mx, my), 6.5f, GizmoDraw.Shadow, 3);
            GuiRenderer.DrawDiamond(new Vec2(mx, my), 5f, GizmoDraw.HotYellow, 4);
            GuiRenderer.DrawRect(new Rect(c.x - 2, c.y - 2, 4, 4), ring, 4);
        }
    }
}
