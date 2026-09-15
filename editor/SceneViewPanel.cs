using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Scene View: DURAGAN doc projeksiyonunu editor kamerasiyla gosterir (hic simule
// olmaz). Pan (drag) + imlec merkezli zoom + tikla-sec + tasima gizmosu.
[MenuItem("Window/Scene", 2)]
public sealed unsafe class SceneViewPanel : EditorWindow
{
    static readonly int _panHash = "SceneView.Pan".GetHashCode();
    static readonly int _selectHash = "SceneView.Select".GetHashCode();

    Vec2 _center = new Vec2(400f, 300f); // dunya uzayinda bakilan nokta
    float _zoom = 1f;                    // world -> panel px carpani

    internal readonly Camera EditorCam = new Camera { Order = 1, BackgroundColor = new Color(34, 36, 44, 255) };
    Texture _rt;
    Texture _retired; // eski RT frame sonuna kadar yasar (frame ortasi destroy yasak)
    int _rtW, _rtH;
    bool _seen;
    float _viewW = 800f, _viewH = 600f;

    public bool VisibleLastFrame { get; private set; }

    public SceneViewPanel() => Title = "Scene";

    public void BeginFrame()
    {
        _retired?.Destroy();
        _retired = null;
        VisibleLastFrame = _seen;
        _seen = false;
    }

    public Texture EnsureTarget()
    {
        if (_rt == null)
            Resize(800, 600);
        return _rt;
    }

    void Resize(int w, int h)
    {
        _retired ??= _rt;
        _rt = Texture.CreateRenderTarget(w, h);
        _rtW = w;
        _rtH = h;
    }

    // Editor kamerasinin ortho'su: merkez + zoom -> dunya penceresi (y asagi).
    internal void ApplyEditorCam()
    {
        float halfW = _viewW * 0.5f / _zoom, halfH = _viewH * 0.5f / _zoom;
        EditorCam.SetOrtho(_center.x - halfW, _center.x + halfW,
                           _center.y - halfH, _center.y + halfH);
    }

    // Araclarin (EditorTool) kullandigi kamera uzayi API'si.
    public float Zoom => _zoom;

    public Vec2 PanelToWorld(Vec2 p)
        => new Vec2(_center.x + (p.x - _viewW * 0.5f) / _zoom,
                    _center.y + (p.y - _viewH * 0.5f) / _zoom);

    public Vec2 WorldToPanel(Vec2 w)
        => new Vec2(_viewW * 0.5f + (w.x - _center.x) * _zoom,
                    _viewH * 0.5f + (w.y - _center.y) * _zoom);

    // Unity F / cift tik: kamerayi objeye ortala, boyutuna gore zoom'u sigdir
    // (cocuklarin sprite'lari dahil kaba dunya-bbox; boyutsuz objede zoom korunur).
    public void FocusOn(int docId)
    {
        var es = App.EditScene;
        var live = es?.Live(docId);
        if (live == null)
            return;
        Vec3 wp = live.transform.position;
        _center = new Vec2(wp.x, wp.y);
        float ext = BoundsExtent(es, docId);
        if (ext > 0.5f)
            _zoom = Math.Clamp(MathF.Min(_viewW, _viewH) * 0.4f / ext, 0.05f, 40f);
    }

    // Objenin + alt agacinin merkezden en uzak sprite kenari (yaklasik yaricap).
    static float BoundsExtent(EditorScene es, int rootId)
    {
        var root = es.Live(rootId);
        if (root == null)
            return 0f;
        Vec3 c = root.transform.position;
        float ext = 0f;
        foreach (var g in es.Doc.Objects)
        {
            if (g.Id != rootId && !IsUnder(es.Doc, g, rootId))
                continue;
            var live = es.Live(g.Id);
            var r = live?.GetComponent<Renderer>();
            if (r == null || !r.GetLocalSelectionBounds(out var bc, out var bh))
                continue;
            ref var m = ref live.transform._getWorldMatrix();
            // dunya-uzayi yari boyut: matris kolon uzunluklariyla olcek dahil
            float sx = MathF.Sqrt(m.m[0] * m.m[0] + m.m[1] * m.m[1]) * bh.x;
            float sy = MathF.Sqrt(m.m[4] * m.m[4] + m.m[5] * m.m[5]) * bh.y;
            float wcx = m.m[0] * bc.x + m.m[4] * bc.y + m.m[12];
            float wcy = m.m[1] * bc.x + m.m[5] * bc.y + m.m[13];
            float d = MathF.Sqrt((wcx - c.x) * (wcx - c.x) + (wcy - c.y) * (wcy - c.y));
            ext = MathF.Max(ext, d + MathF.Max(sx, sy));
        }
        return ext;
    }

    static bool IsUnder(SceneDoc doc, SceneDoc.GoDoc g, int rootId)
    {
        int cur = g.Parent, guard = 0;
        while (cur != 0 && guard++ < 256)
        {
            if (cur == rootId)
                return true;
            SceneDoc.GoDoc p = null;
            foreach (var o in doc.Objects)
                if (o.Id == cur) { p = o; break; }
            cur = p?.Parent ?? 0;
        }
        return false;
    }

    protected override void OnGui()
    {
        var vis = GuiClip.VisibleRect;
        var ev = Event.Current;

        if (Gui.Button(new Rect(4, 4, 36, 20), "1:1"))
            _zoom = 1f;
        if (Gui.Button(new Rect(44, 4, 36, 20), "Fit"))
            FitGameFrame();

        // Arac butonlari registry'den (builtin + [EditorTool] tasiyan her sinif).
        float tx = 92f;
        var tools = EditorTools.All;
        for (int i = 0; i < tools.Count; i++)
        {
            var t = tools[i];
            bool act = EditorTools.Active == t;
            string label = act ? t.Name + "*" : t.Name;
            float w = 14f + label.Length * 8f;
            if (Gui.Button(new Rect(tx, 4, w, 20), label))
                EditorTools.Active = t;
            tx += w + 4f;
        }

        // Kisayollar (Unity W/E/R): textfield odagi ve aktif drag yokken —
        // drag ortasinda arac degisirse kontrol id dizilimi kayar.
        if (ev.Type == EventType.KeyDown && GuiUtility.KeyboardControl == 0
            && GuiUtility.HotControl == 0)
        {
            for (int i = 0; i < tools.Count; i++)
            {
                if (tools[i].Key != 0 && ev.KeyCode == tools[i].Key)
                {
                    EditorTools.Active = tools[i];
                    ev.Use();
                    break;
                }
            }
        }

        if (ev.Type == EventType.Repaint && vis.width >= 8 && vis.height >= 8)
        {
            _seen = true;
            _viewW = vis.width;
            _viewH = vis.height;
            float s = Gui.Scale > 0 ? Gui.Scale : 1f;
            int w = Math.Max(8, (int)(vis.width * s));
            int h = Math.Max(8, (int)(vis.height * s));
            if ((w != _rtW || h != _rtH) && !RenderDebug.Frozen) // frozen: capture eski RT'yi replay eder
                Resize(w, h); // eski RT BeginFrame'de olur; hedef encode oncesi baglanir
            GuiRenderer.DrawTexture(new Rect(0, 0, vis.width, vis.height), _rt);
        }

        DoGizmoAndSelect(vis, ev); // pan'den ONCE: tiklama onceligi

        int id = GuiUtility.GetControlID(_panHash, FocusType.Passive);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (vis.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    _center.x -= ev.Delta.x / _zoom;
                    _center.y -= ev.Delta.y / _zoom;
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
            case EventType.ScrollWheel:
                if (vis.Contains(ev.MousePosition))
                {
                    // Imlec merkezli zoom: imlecin altindaki dunya noktasi sabit kalir.
                    Vec2 before = PanelToWorld(ev.MousePosition);
                    _zoom = Math.Clamp(_zoom * MathF.Pow(1.1f, ev.Delta.y), 0.05f, 40f);
                    Vec2 after = PanelToWorld(ev.MousePosition);
                    _center.x += before.x - after.x;
                    _center.y += before.y - after.y;
                    ev.Use();
                }
                break;
        }

        if (ev.Type == EventType.Repaint)
        {
            DrawGameFrame();
            // Proje panelinden PREFAB birakma: imlecin dunya konumuna instance
            // (kabul karari importer tipinden — dosya tipi hardcode edilmez).
            if (DragDrop.Kind == DragDrop.Payload.Asset
                && AssetDatabase.ImportTypeOf(DragDrop.AssetPath) == typeof(Prefab)
                && vis.Contains(ev.MousePosition))
            {
                var wp = PanelToWorld(ev.MousePosition);
                string guid = DragDrop.AssetGuid;
                GuiRenderer.DrawRect(new Rect(ev.MousePosition.x - 4, ev.MousePosition.y - 4, 8, 8),
                    new Color(90, 170, 90, 220), 5);
                DragDrop.RegisterTarget(() =>
                {
                    var r = App.EditScene.InstantiatePrefab(guid, 0, new Vec3(wp.x, wp.y, 0f));
                    if (r != null)
                        Selection.DocId = r.Id;
                });
            }
        }
    }

    void FitGameFrame()
    {
        float gw = GameOutput.ViewW, gh = GameOutput.ViewH;
        _center = new Vec2(gw * 0.5f, gh * 0.5f);
        _zoom = MathF.Min(_viewW / MathF.Max(1f, gw), _viewH / MathF.Max(1f, gh)) * 0.9f;
    }

    // Oyun ekrani konturu: (0,0)-(gameW,gameH) dunya dikdortgeni panel uzayinda.
    void DrawGameFrame()
    {
        Vec2 a = WorldToPanel(default);
        Vec2 b = WorldToPanel(new Vec2(GameOutput.ViewW, GameOutput.ViewH));
        var c = new Color(120, 160, 255, 170);
        // layer 2: RT dokusu layer 1'de — kontur ustune cizilmeli.
        GuiRenderer.DrawRect(new Rect(a.x, a.y, b.x - a.x, 1), c, 2);
        GuiRenderer.DrawRect(new Rect(a.x, b.y, b.x - a.x, 1), c, 2);
        GuiRenderer.DrawRect(new Rect(a.x, a.y, 1, b.y - a.y), c, 2);
        GuiRenderer.DrawRect(new Rect(b.x, a.y, 1, b.y - a.y + 1), c, 2);
    }

    // --- Secim + arac (gizmo) dispatch'i ---

    void DoGizmoAndSelect(in Rect vis, Event ev)
    {
        // Handle isabetleri Layout'ta mesafe kaydiyla yarisir (Unity modeli);
        // imlec panel disindaysa kayit kapali (komsu panelin tiki calinmaz).
        if (ev.Type == EventType.Layout)
            HandleUtility.BeginLayout(vis.Contains(ev.MousePosition));

        EditorTools.Active?.OnToolGui(this, ev);

        int idSel = GuiUtility.GetControlID(_selectHash, FocusType.Passive);

        // Secim vurgusu aracdan bagimsiz: braketler TUM secim icin.
        if (ev.Type == EventType.Repaint)
        {
            var esb = App.EditScene;
            if (esb != null)
            {
                var ids = Selection.Ids;
                for (int i = 0; i < ids.Count; i++)
                {
                    var slive = esb.Live(ids[i]);
                    if (slive == null)
                        continue;
                    DrawSelectionBrackets(slive, ids[i] == Selection.DocId
                        ? new Color(255, 150, 30, 235)
                        : new Color(200, 130, 50, 140));
                }
            }
        }

        // Tikla-sec (sol tus): gizmo almadiysa. Ctrl = coklu toggle. Bos tik secimi
        // birakir, event kullanilmaz — pan kontrolu devralir.
        if (ev.GetTypeForControl(idSel) == EventType.MouseDown && ev.Button == 0
            && vis.Contains(ev.MousePosition))
        {
            int hit = HitTest(PanelToWorld(ev.MousePosition));
            bool ctrl = (ev.Modifiers & EventModifiers.Control) != 0;
            if (hit != 0)
            {
                if (ctrl)
                    Selection.Toggle(hit);
                else
                    Selection.DocId = hit;
                ev.Use();
            }
            else if (!ctrl)
            {
                Selection.Clear();
            }
        }
    }

    // Dunya noktasindaki en ustteki (doc sirasinda sonuncu) renderer'in docId'si;
    // 0 = yok. Isabet siniri renderer'in kendi bildirdigi lokal bbox'tan.
    static int HitTest(Vec2 world)
    {
        var es = App.EditScene;
        if (es == null)
            return 0;
        int best = 0;
        var objs = es.Doc.Objects;
        for (int i = 0; i < objs.Count; i++)
        {
            var g = objs[i];
            var live = es.Live(g.Id);
            if (live == null || !live.activeInHierarchy)
                continue;
            var r = live.GetComponent<Renderer>();
            if (r == null || !r.GetLocalSelectionBounds(out var bc, out var bh))
                continue;
            ref var m = ref live.transform._getWorldMatrix();
            float a = m.m[0], b = m.m[1], c = m.m[4], e = m.m[5];
            float det = a * e - c * b;
            if (MathF.Abs(det) < 1e-8f)
                continue;
            float dx = world.x - m.m[12], dy = world.y - m.m[13];
            float lx = (e * dx - c * dy) / det;
            float ly = (-b * dx + a * dy) / det;
            if (MathF.Abs(lx - bc.x) <= bh.x && MathF.Abs(ly - bc.y) <= bh.y)
                best = g.Id; // doc sirasi ~ cizim sirasi: sonuncu ustte
        }
        return best;
    }

    void DrawSelectionBrackets(GameObject live, Color bcol)
    {
        // Secim vurgusu: renderer bbox koselerinde L-braketler (Unity bounds stili).
        var r = live.GetComponent<Renderer>();
        if (r == null || !r.GetLocalSelectionBounds(out var bc, out var bh))
            return;
        ref var m = ref live.transform._getWorldMatrix();
        Span<Vec2> corners = stackalloc Vec2[4]
        {
            new Vec2(bc.x - bh.x, bc.y - bh.y), new Vec2(bc.x + bh.x, bc.y - bh.y),
            new Vec2(bc.x + bh.x, bc.y + bh.y), new Vec2(bc.x - bh.x, bc.y + bh.y),
        };
        // Braket kollari kutu MERKEZINE dogru bakar (LayoutBox'ta merkez pivot'a
        // gore kayik olabilir — transform pos degil bbox merkezi baz alinir).
        Vec2 cw = new Vec2(m.m[0] * bc.x + m.m[4] * bc.y + m.m[12],
                           m.m[1] * bc.x + m.m[5] * bc.y + m.m[13]);
        Vec2 c = WorldToPanel(cw);
        const float arm = 9f, thick = 2f;
        for (int i = 0; i < 4; i++)
        {
            float wx = m.m[0] * corners[i].x + m.m[4] * corners[i].y + m.m[12];
            float wy = m.m[1] * corners[i].x + m.m[5] * corners[i].y + m.m[13];
            Vec2 pp = WorldToPanel(new Vec2(wx, wy));
            float sx = pp.x < c.x ? 1f : -1f;
            float sy = pp.y < c.y ? 1f : -1f;
            GuiRenderer.DrawRect(new Rect(sx > 0 ? pp.x : pp.x - arm, pp.y - thick * 0.5f, arm, thick), bcol, 3);
            GuiRenderer.DrawRect(new Rect(pp.x - thick * 0.5f, sy > 0 ? pp.y : pp.y - arm, thick, arm), bcol, 3);
        }
    }
}
