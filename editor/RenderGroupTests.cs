#if DE_EDITOR
using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// RenderGroup (hiyerarsik alfa) smoke: ic ice carpim, IgnoreParentAlpha, alfa 0 =
// encode yok + pointer gormez, disabled grup etkisiz, Tween "Alpha" yolu, zero alloc.
public static class RenderGroupTests
{
    static int _pass, _fail;

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prev = Scene.Active;
        var s = Scene.Create("rendergroup-test");
        s.Catalog = catalog;
        Scene.SetActive(s);
        s.ScreenWidth = 800;
        s.ScreenHeight = 600;

        var root = new GameObject("root");
        var g = root.AddComponent<RenderGroup>();
        var mid = new GameObject("mid");
        mid.transform.SetParent(root.transform, false);
        var gm = mid.AddComponent<RenderGroup>();
        var leaf = new GameObject("leaf");
        leaf.transform.SetParent(mid.transform, false);
        leaf.transform.localPosition = new Vec3(100, 100, 0);
        var sr = leaf.AddComponent<SpriteRenderer>();
        sr.Width = sr.Height = 50;
        sr.Color = new Color(255, 255, 255, 200);
        var sib = new GameObject("sib"); // grup disi: etkilenmez
        var sr2 = sib.AddComponent<SpriteRenderer>();
        s.Update(0f);

        var q = new RenderQueue();
        q.Begin(); s.Render(q);
        Check(q.Count == 2 && q.TintAlphaAt(0) == 200 && q.TintAlphaAt(1) == 255, "alfa 1: dokunulmaz");

        g.Alpha = 0.5f;
        q.Begin(); s.Render(q);
        Check(q.TintAlphaAt(0) == 100 && q.TintAlphaAt(1) == 255, "grup alfasi cocuga iner, kardes etkilenmez");

        gm.Alpha = 0.5f;
        q.Begin(); s.Render(q);
        Check(q.TintAlphaAt(0) == 50, "ic ice gruplar carpilir (0.5*0.5*200)");

        gm.IgnoreParentAlpha = true;
        q.Begin(); s.Render(q);
        Check(q.TintAlphaAt(0) == 100, "IgnoreParentAlpha ataları yok sayar");
        gm.IgnoreParentAlpha = false;

        gm.enabled = false;
        q.Begin(); s.Render(q);
        Check(q.TintAlphaAt(0) == 100, "disabled grup etkisiz");
        gm.enabled = true;

        g.Alpha = 0f;
        q.Begin(); s.Render(q);
        Check(q.Count == 1, "alfa 0: alt agac encode edilmez");
        Check(q.GroupAlpha == 1f, "Render sonunda GroupAlpha sifirlanir");
        var h = leaf.AddComponent<Handler>();
        s.Pointer.Down(100, 100);
        s.Pointer.Up(100, 100);
        Check(h.Downs == 0, "alfa 0: pointer gormez");
        g.Alpha = 1f; gm.Alpha = 1f;
        s.Pointer.Down(100, 100);
        s.Pointer.Up(100, 100);
        Check(h.Downs == 1, "alfa 1: pointer gorur");

        // Tween generic yol: katalogdan "Alpha".
        g.Tween("Alpha", 0f, 1f);
        s.Update(0.5f);
        Check(Math.Abs(g.Alpha - 0.5f) < 0.01f, "Tween(\"Alpha\") calisir");
        q.Begin(); s.Render(q);
        Check(q.TintAlphaAt(0) == 100, "tween'li alfa cizime yansir");
        s.Update(1f);

        // Zero alloc: isinma sonrasi N frame render.
        g.Alpha = 0.7f; gm.Alpha = 0.9f;
        for (int i = 0; i < 3; i++) { q.Begin(); s.Render(q); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { q.Begin(); s.Render(q); }
        long alloc = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(alloc == 0, $"render walk zero alloc ({alloc} B)");

        GameObject.Destroy(root);
        GameObject.Destroy(sib);
        s.Update(0f);
        Scene.SetActive(prev);
        Scene.Unload(s);
        Console.WriteLine($"[rendergroup] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[rendergroup] FAIL: {name}"); }
    }

    sealed class Handler : Component
    {
        public int Downs;
        protected internal override void OnPointerDown(PointerEvent e) => Downs++;
    }
}
#endif
