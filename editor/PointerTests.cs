#if DE_EDITOR
using System;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Pointer boru hatti smoke testleri (acilista, izole sahne): pick + click/drag
// ayrimi + bubble + layer mask + perspektif isin. Gorsel dogrulama gerektirmez.
public static class PointerTests
{
    static int _pass, _fail;

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prev = Scene.Active;
        var s = Scene.Create("pointer-test");
        s.Catalog = catalog;
        Scene.SetActive(s);
        s.ScreenWidth = 800;
        s.ScreenHeight = 600;

        var camGo = new GameObject("cam");
        var cam = camGo.AddComponent<CameraComponent>();

        ClickVsDrag(s);
        DragTotals(s);
        BubbleToParent(s);
        LayerMaskBlocks(s, cam);
        TopMostWins(s);
        PerspectivePick(s, cam);

        Scene.SetActive(prev);
        Scene.Unload(s);
        Console.WriteLine($"[pointer] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[pointer] FAIL: {name}"); }
    }

    // D=down U=up C=click S=dragStart G=drag; son event bilgileri alanlarda.
    sealed class Handler : Component
    {
        public readonly StringBuilder Log = new();
        public GameObject LastTarget;
        public bool LastDragged;
        public Vec3 LastWorldTotal;
        public Vec3 LastParentTotal;
        protected internal override void OnPointerDown(PointerEvent e)
        { Log.Append('D'); LastTarget = e.target; }
        protected internal override void OnPointerUp(PointerEvent e)
        { Log.Append('U'); LastDragged = e.dragged; }
        protected internal override void OnPointerClick(PointerEvent e) => Log.Append('C');
        protected internal override void OnDragStart(PointerEvent e) => Log.Append('S');
        protected internal override void OnDrag(PointerEvent e)
        {
            Log.Append('G');
            LastWorldTotal = e.worldTotal;
            LastParentTotal = e.TotalInParentOf(transform);
        }
    }

    static GameObject Sprite(string name, float x, float y, float size = 100)
    {
        var go = new GameObject(name);
        go.transform.localPosition = new Vec3(x, y, 0);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.Width = size;
        sr.Height = size;
        return go;
    }

    static void ClickVsDrag(Scene s)
    {
        var go = Sprite("click", 100, 100);
        var h = go.AddComponent<Handler>();
        s.Pointer.Down(100, 100);
        s.Pointer.Move(102, 101); // esik alti: drag baslamaz
        s.Pointer.Up(102, 101);
        Check(h.Log.ToString() == "DUC", "click: DUC sirasi (" + h.Log + ")");
        Check(!h.LastDragged, "click: up.dragged=false");
        Check(h.LastTarget == go, "click: e.target dogru");
        GameObject.Destroy(go);
        s.Update(0f);
    }

    static void DragTotals(Scene s)
    {
        var go = Sprite("drag", 200, 200);
        var h = go.AddComponent<Handler>();
        s.Pointer.Down(200, 200);
        s.Pointer.Move(230, 200); // esik ustu
        s.Pointer.Up(230, 200);
        Check(h.Log.ToString() == "DSGU", "drag: DSGU sirasi (" + h.Log + ")");
        Check(h.LastDragged, "drag: up.dragged=true");
        Check(MathF.Abs(h.LastWorldTotal.x - 30f) < 0.01f && MathF.Abs(h.LastWorldTotal.y) < 0.01f,
            "drag: worldTotal (30,0)");
        Check(MathF.Abs(h.LastParentTotal.x - 30f) < 0.01f, "drag: TotalInParentOf (kok GO)");
        GameObject.Destroy(go);
        s.Update(0f);
    }

    static void BubbleToParent(Scene s)
    {
        var parent = new GameObject("masa");
        var h = parent.AddComponent<Handler>();
        var child = Sprite("tas", 300, 300);
        child.transform.SetParent(parent.transform, false);
        s.Pointer.Down(300, 300);
        s.Pointer.Up(300, 300);
        Check(h.Log.ToString() == "DUC", "bubble: parent handler aldi (" + h.Log + ")");
        Check(h.LastTarget == child, "bubble: e.target = cocuk (hangi tas)");
        GameObject.Destroy(parent);
        s.Update(0f);
    }

    static void LayerMaskBlocks(Scene s, CameraComponent cam)
    {
        var go = Sprite("masked", 400, 400);
        go.layer = 3;
        var h = go.AddComponent<Handler>();
        cam.cullingMask = ~(1 << 3); // layer 3 kameradan cikarildi
        s.Pointer.Down(400, 400);
        s.Pointer.Up(400, 400);
        Check(h.Log.Length == 0, "mask: maskelenen layer hit almaz");
        cam.cullingMask = -1;
        s.Pointer.Down(400, 400);
        s.Pointer.Up(400, 400);
        Check(h.Log.ToString() == "DUC", "mask: maske acilinca hit (" + h.Log + ")");
        GameObject.Destroy(go);
        s.Update(0f);
    }

    static void TopMostWins(Scene s)
    {
        var below = Sprite("alt", 500, 200);
        var hb = below.AddComponent<Handler>();
        var above = Sprite("ust", 500, 200);
        above.GetComponent<SpriteRenderer>().SortingOrder = 5;
        var ha = above.AddComponent<Handler>();
        s.Pointer.Down(500, 200);
        s.Pointer.Up(500, 200);
        Check(ha.Log.ToString() == "DUC" && hb.Log.Length == 0,
            "topmost: buyuk SortingOrder kazanir");
        GameObject.Destroy(below);
        GameObject.Destroy(above);
        s.Update(0f);
    }

    static void PerspectivePick(Scene s, CameraComponent cam)
    {
        // fov 60, z=-519.615: z=0 kesiti tam 800x600 (piksel-ortho ile ayni rect) —
        // ayni ekran noktasi perspektif isinle de ayni objeyi vurmali.
        cam.projection = CameraProjection.Perspective;
        cam.fov = 60;
        cam.transform.localPosition = new Vec3(400, 300, -519.6152f);
        var go = Sprite("persp", 600, 450);
        var h = go.AddComponent<Handler>();
        s.Pointer.Down(600, 450);
        s.Pointer.Move(630, 450);
        s.Pointer.Up(630, 450);
        Check(h.Log.ToString() == "DSGU", "persp: perspektif isinle pick+drag (" + h.Log + ")");
        Check(MathF.Abs(h.LastWorldTotal.x - 30f) < 0.1f && MathF.Abs(h.LastWorldTotal.y) < 0.1f,
            "persp: worldTotal z=0 duzleminde (30,0)");
        GameObject.Destroy(go);
        s.Update(0f);
        cam.projection = CameraProjection.PixelPerfect;
        cam.transform.localPosition = default;
    }
}
#endif
