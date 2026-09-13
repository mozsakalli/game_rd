#if DE_EDITOR
using System;
using DigitoyEngine;

namespace DigitoyEditor;

// LayoutBox smoke testleri (acilista, izole sahnede). Lazy pull dogrulamasi:
// hicbir Update kosmadan RectWidth/pozisyon okumak GUNCEL deger vermeli
// ("bir frame bekle" sinifi bug'larin regresyon bekcisi).
public static class LayoutTests
{
    static int _pass, _fail;

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prev = Scene.Active;
        var s = Scene.Create("layout-test");
        s.Catalog = catalog;
        s.ScreenWidth = 800;
        s.ScreenHeight = 600;
        Scene.SetActive(s);

        ScreenRootAndAnchor(s);
        HorizontalFlex(s);
        AutosizeAndDirtyPull(s);
        IgnoreLayoutStretch(s);
        ScreenResizeViaSweep(s);
        StyleMeshSmoke(s);
        Scale9Smoke(s);
        OverflowSmoke(s);

        Scene.SetActive(prev);
        Scene.Unload(s);
        Console.WriteLine($"[layout] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[layout] FAIL: {name}"); }
    }

    static bool Near(float a, float b) => MathF.Abs(a - b) < 0.01f;

    static LayoutBox NewBox(string name, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent != null)
            go.transform.SetParent(parent, false);
        return go.AddComponent<LayoutBox>();
    }

    // Ekran koku + alt-orta anchor (istaka senaryosu): Update KOSMADAN dogru okunmali.
    static void ScreenRootAndAnchor(Scene s)
    {
        var root = NewBox("root");
        root.FitScreen = true;
        var rack = NewBox("rack", root.transform);
        rack.AnchorMin = rack.AnchorMax = new Vec2(0.5f, 1f); // alt-orta
        rack.Pivot = new Vec2(0.5f, 1f);
        rack.Width = 200;
        rack.Height = 50;

        Check(Near(rack.RectWidth, 200) && Near(rack.RectHeight, 50), "anchor rect boyutu");
        var lp = rack.transform.localPosition;
        Check(Near(lp.x, 0) && Near(lp.y, 300), "alt-orta anchor pozisyonu (pull, Update'siz)");
        var wc = rack.WorldCenter;
        Check(Near(wc.x, 400) && Near(wc.y, 575), "dunya merkezi (ekran 800x600 altinda ortada)");
        GameObject.Destroy(root.gameObject);
        s.Update(0f);
    }

    // Horizontal + grow agirlikli pay dagitimi.
    static void HorizontalFlex(Scene s)
    {
        var p = NewBox("hgroup");
        p.Layout = LayoutMode.Horizontal;
        p.Width = 300;
        p.Height = 50;
        var c1 = NewBox("c1", p.transform); c1.Width = 100; c1.Height = 50;
        var c2 = NewBox("c2", p.transform); c2.Grow = 1; c2.Height = 50;
        var c3 = NewBox("c3", p.transform); c3.Grow = 2; c3.Height = 50;

        Check(Near(c2.RectWidth, 200f / 3) && Near(c3.RectWidth, 400f / 3), "grow agirlikli pay");
        Check(Near(c1.transform.localPosition.x, -100) &&
              Near(c2.transform.localPosition.x, -150 + 100 + 200f / 6) &&
              Near(c3.transform.localPosition.x, -150 + 100 + 200f / 3 + 200f / 3), "sirali yerlesim");
        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }

    // Autosize (icerikten olcum) + property degisiminin pull ile aninda gorunmesi.
    static void AutosizeAndDirtyPull(Scene s)
    {
        var p = NewBox("auto");
        p.Layout = LayoutMode.Horizontal;
        p.Spacing = 10;
        var a = NewBox("a", p.transform); a.Width = 40; a.Height = 20;
        var b = NewBox("b", p.transform); b.Width = 60; b.Height = 30;

        Check(Near(p.RectWidth, 110) && Near(p.RectHeight, 30), "autosize toplami");
        a.Width = 100; // kirletir; okuma cozmeli
        Check(Near(p.RectWidth, 170), "degisiklik sonrasi pull guncel");
        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }

    // IgnoreLayout + stretch anchor = grup icinde arkaplan deseni.
    static void IgnoreLayoutStretch(Scene s)
    {
        var p = NewBox("panel");
        p.Layout = LayoutMode.Horizontal;
        p.Width = 300;
        p.Height = 80;
        var bg = NewBox("bg", p.transform);
        bg.IgnoreLayout = true;
        bg.AnchorMin = new Vec2(0, 0);
        bg.AnchorMax = new Vec2(1, 1);
        var c = NewBox("c", p.transform); c.Width = 50; c.Height = 50;

        Check(Near(bg.RectWidth, 300) && Near(bg.RectHeight, 80), "arkaplan parent'i kaplar");
        Check(Near(c.transform.localPosition.x, -125), "grup arkaplani dizmedi");
        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }

    // Ekran degisimi pull'suz da sweep'te yakalanmali (fitScreen kuyrukta kalir).
    static void ScreenResizeViaSweep(Scene s)
    {
        var root = NewBox("root2");
        root.FitScreen = true;
        var child = NewBox("child", root.transform);
        child.AnchorMin = child.AnchorMax = new Vec2(1f, 0f); // sag-ust
        child.Pivot = new Vec2(1f, 0f);
        child.Width = 10;
        child.Height = 10;
        s.Update(0f); // ilk sweep cozer
        float x0 = child.transform.localPosition.x;
        s.ScreenWidth = 1000;
        s.Update(0f); // kimse okumadi; sweep resize'i yakalamali
        Check(Near(child.transform.localPosition.x, x0 + 100), "ekran resize sweep'te yakalandi");
        GameObject.Destroy(root.gameObject);
        s.Update(0f);
    }

    // Stil quad emisyonu: dolgu + border ring katmanlari draw uretir (acilista kosar).
    static void StyleMeshSmoke(Scene s)
    {
        var b = NewBox("styled");
        b.Width = 120; b.Height = 60;
        b.CornerRadius = 12;
        b.Fill = new Gradient(new Color(40, 90, 200, 255), new Color(10, 20, 60, 255));
        b.BorderWidth = 3;
        b.BorderFill = Color.White;
        var q = new RenderQueue();
        q.Begin();
        b.Encode(q);
        int withBorder = q.Count;
        // dolgu (4 kose + 4 kenar + >=1 ic) + ring (4 kose + 4 kenar)
        Check(withBorder >= 17, "dolgu + border ring quad'lari uretildi");
        b.BorderWidth = 0; // border dusunce ring katmani gitmeli
        q.Begin();
        b.Encode(q);
        Check(q.Count > 0 && q.Count < withBorder, "border'siz daha az quad");
        GameObject.Destroy(b.gameObject);
        s.Update(0f);
    }

    // Doku + 9-slice: 3x3 grid quad'lari; slice sifirlaninca tek stretch quad.
    static void Scale9Smoke(Scene s)
    {
        var tex = Texture.FromColor(32, 32, Color.White);
        var b = NewBox("tex9");
        b.Width = 120; b.Height = 60;
        b.Sprite = Sprite.FromTexture(tex);
        b.Slice9 = 8;
        var q = new RenderQueue();
        q.Begin();
        b.Encode(q);
        Check(q.Count == 9, "scale9 3x3 grid quad'lari");
        b.Slice9 = 0; // stretch moda dusmeli (tek quad)
        q.Begin();
        b.Encode(q);
        Check(q.Count == 1, "slice sifirlaninca tek stretch quad");
        GameObject.Destroy(b.gameObject);
        tex.Destroy();
        s.Update(0f);
    }

    // Overflow: Grow icerikle buyur; Visible/Hidden authored boyutta kalir;
    // Hidden ata, tamamen disarida kalan cocugun quad'larini dusurur, kismen
    // tasani kirpar (draw sayisi uzerinden dogrulanir).
    static void OverflowSmoke(Scene s)
    {
        var p = NewBox("ovf");
        p.Layout = LayoutMode.Vertical;
        p.Width = 100; p.Height = 40;
        var c = NewBox("child", p.transform);
        c.Width = 100; c.Height = 120;
        c.Fill = new Gradient(Color.Red);

        Check(Near(p.RectHeight, 120), "Grow: icerik kutuyu buyuttu");
        p.OverflowY = OverflowMode.Visible;
        Check(Near(p.RectHeight, 40), "Visible: authored boyut korunur");

        // Visible: cocuk tam cizilir; Hidden: kismi kirpma quad sayisini DEGISTIRMEZ
        // ama tamamen disaridaki cocuk hic quad uretmez.
        var q = new RenderQueue();
        q.Begin();
        c.Encode(q);
        int full = q.Count;
        Check(full > 0, "Visible: tasan cocuk cizildi");

        p.OverflowY = OverflowMode.Hidden;
        p.ResolveIfDirty();
        q.Begin();
        c.Encode(q);
        Check(q.Count > 0 && q.Count <= full, "Hidden: kismen tasan cocuk kirpildi");

        c.IgnoreLayout = true; // anchor yerlesimine gec, kutuyu tamamen disari tasi
        c.AnchorMin = c.AnchorMax = new Vec2(0.5f, 0.5f);
        c.AnchoredPos = new Vec2(0, 500);
        p.ResolveIfDirty();
        q.Begin();
        c.Encode(q);
        Check(q.Count == 0, "Hidden: tamamen disaridaki cocuk cizilmedi");
        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }
}
#endif
