#if DE_EDITOR
using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

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
        FlexMarginMinMax(s);
        FlexShrink(s);
        FlexAlignSelf(s);
        FlexWrapMargin(s);
        ScrollSmoke(s);
        ScrollViewGesture(s);
        StyleApplyAndTransition(s);
        ArrangeBench(s);
        AutosizeAndDirtyPull(s);
        IgnoreLayoutStretch(s);
        ScreenResizeViaSweep(s);
        StyleMeshSmoke(s);
        Scale9Smoke(s);
        GradientStopsSmoke(s);
        SpriteFitSmoke(s);
        EncodeBench(s);
        OverflowSmoke(s);
        WrapSmoke(s);
        JustifySmoke(s);
        DeepInvalidation(s);
        ClipChainReparent(s);

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

    // Margin ana eksen toplamina/konuma girer; min/max olcum + arrange + stretch anchor'da clamp.
    static void FlexMarginMinMax(Scene s)
    {
        var p = NewBox("mm");
        p.Layout = LayoutMode.Horizontal;
        p.Height = 50;
        var a = NewBox("a", p.transform); a.Width = 40; a.Height = 20; a.Margin = new Vec4(10, 0, 10, 0);
        var b = NewBox("b", p.transform); b.Width = 60; b.Height = 30;
        Check(Near(p.RectWidth, 120), "autosize margin dahil (10+40+10+60)");
        // pivot 0.5: parent sol kenari -60; a merkezi = -60 + 10 + 20 = -30; b merkezi = -60+60+30 = 30
        Check(Near(a.transform.localPosition.x, -30) && Near(b.transform.localPosition.x, 30), "margin konumu kaydirdi");

        b.MaxWidth = 50; // olcumde clamp -> parent 110, b 50
        Check(Near(b.RectWidth, 50) && Near(p.RectWidth, 110), "maxWidth olcum+arrange clamp");
        b.MaxWidth = 0; b.MinWidth = 80;
        Check(Near(b.RectWidth, 80) && Near(p.RectWidth, 140), "minWidth clamp");

        // Grow'lu cocuk max ile sinirlanir (kalan alan baska cocuga gitmez — tek tur, bilincli).
        p.Width = 400; p.OverflowX = OverflowMode.Visible;
        var g = NewBox("g", p.transform); g.Grow = 1; g.Height = 10; g.MaxWidth = 50;
        Check(Near(g.RectWidth, 50), "grow + maxWidth clamp");

        // Stretch anchor + margin: kenarlardan dusulur.
        var st = NewBox("st", p.transform);
        st.IgnoreLayout = true;
        st.AnchorMin = new Vec2(0, 0); st.AnchorMax = new Vec2(1, 1);
        st.Margin = new Vec4(20, 5, 20, 5);
        Check(Near(st.RectWidth, 360) && Near(st.RectHeight, 40), "stretch anchor margin dusuldu");
        Check(Near(st.transform.localPosition.x, 0), "stretch anchor simetrik margin ortada kalir");
        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }

    // Shrink: tasma varsa shrink*pref agirligiyla kucul; shrink=0 kucultmez; min clamp.
    static void FlexShrink(Scene s)
    {
        var p = NewBox("sh");
        p.Layout = LayoutMode.Horizontal;
        p.Width = 200; p.Height = 40; p.OverflowX = OverflowMode.Visible;
        var a = NewBox("a", p.transform); a.Width = 100; a.Height = 10; a.Shrink = 1;
        var b = NewBox("b", p.transform); b.Width = 100; b.Height = 10; b.Shrink = 1;
        var c = NewBox("c", p.transform); c.Width = 100; c.Height = 10; // shrink yok
        // fixed 300, avail 200 -> acik 100, agirlik a=100,b=100 -> her biri 50 kuculur
        Check(Near(a.RectWidth, 50) && Near(b.RectWidth, 50) && Near(c.RectWidth, 100), "shrink orantili, shrink=0 sabit");
        Check(Near(c.transform.localPosition.x, 50), "shrink sonrasi siralama bitisik");
        a.MinWidth = 80; // a 80'e takilir; artik (30) serbest b'ye yeniden dagitilir -> b 20
        Check(Near(a.RectWidth, 80) && Near(b.RectWidth, 20), "shrink min clamp + artik yeniden dagitim");
        Check(Near(a.RectWidth + b.RectWidth + c.RectWidth, 200), "shrink sonrasi tam sigar");
        p.Width = 400; // tasma yok -> shrink etkisiz
        Check(Near(a.RectWidth, 100) && Near(b.RectWidth, 100), "tasma yokken shrink etkisiz");
        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }

    // alignSelf: Auto parent'inki, Center/End kendi, Stretch capraz alani doldurur (margin dusulur, max clamp).
    static void FlexAlignSelf(Scene s)
    {
        var p = NewBox("as");
        p.Layout = LayoutMode.Horizontal;
        p.Width = 300; p.Height = 100; p.AlignChildren = LayoutAlign.Start;
        var a = NewBox("a", p.transform); a.Width = 50; a.Height = 20;
        var b = NewBox("b", p.transform); b.Width = 50; b.Height = 20; b.AlignSelf = LayoutAlignSelf.End;
        var c = NewBox("c", p.transform); c.Width = 50; c.Height = 20; c.AlignSelf = LayoutAlignSelf.Stretch; c.Margin = new Vec4(0, 10, 0, 10);
        var d = NewBox("d", p.transform); d.Width = 50; d.Height = 20; d.AlignSelf = LayoutAlignSelf.Stretch; d.MaxHeight = 30;
        // parent ust kenari -50
        p.ResolveIfDirty(); // transform okumak pull degil
        Check(Near(a.transform.localPosition.y, -40), "Auto -> parent Start");
        Check(Near(b.transform.localPosition.y, 40), "End alt kenara");
        Check(Near(c.RectHeight, 80) && Near(c.transform.localPosition.y, 0), "Stretch margin dusulmus capraz alan");
        Check(Near(d.RectHeight, 30) && Near(d.transform.localPosition.y, -35), "Stretch maxHeight clamp (bas hizali)");
        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }

    // Wrap + margin: kirilim dis boyutla; satir caprazi dis boyut; alignSelf satir icinde.
    static void FlexWrapMargin(Scene s)
    {
        var p = NewBox("wm");
        p.Layout = LayoutMode.Horizontal;
        p.OverflowX = OverflowMode.Wrap;
        p.Width = 100; p.Height = 0;
        for (int i = 0; i < 3; i++)
        {
            var c = NewBox("c" + i, p.transform);
            c.Width = 40; c.Height = 10; c.Margin = new Vec4(5, 2, 5, 2); // dis 50x14
        }
        // 2 sigar (100), 3. satira -> yukseklik 14+14 = 28
        Check(Near(p.RectHeight, 28), "wrap: margin'li dis boyutla kirilim + satir caprazi");
        var c2 = p.transform.LastChild._gameObject.GetComponent<LayoutBox>();
        // 2. satir ust = -14 + 14 = 0; c2 merkezi = 0 + 2 + 5 = 7
        Check(Near(c2.transform.localPosition.y, 7), "wrap: 2. satir konumu margin dahil");
        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }

    // Scroll: ofset cocuklari kaydirir (boyut sabit), ScrollMax = icerik - gorunur, ofset degisimi
    // parent'i kirletmez, pencere disi cocuk quad uretmez (kutu duzeyi culling) ve tiklanmaz.
    static void ScrollSmoke(Scene s)
    {
        var root = NewBox("sroot");
        root.Width = 300; root.Height = 300; root.OverflowX = root.OverflowY = OverflowMode.Visible;
        var p = NewBox("scroll", root.transform);
        p.Layout = LayoutMode.Vertical;
        p.Width = 100; p.Height = 100; p.OverflowY = OverflowMode.Scroll; p.Spacing = 10;
        LayoutBox[] kids = new LayoutBox[5];
        for (int i = 0; i < 5; i++)
        {
            var c = NewBox("k" + i, p.transform);
            c.Width = 100; c.Height = 40; c.Fill = new Gradient(Color.Red);
            kids[i] = c;
        }
        // icerik = 5*40 + 4*10 = 240 -> max = 140
        Check(Near(p.RectHeight, 100) && Near(p.ScrollMax.y, 140), "Scroll: authored boyut + ScrollMax");
        float y0 = kids[0].transform.localPosition.y;
        root.ResolveIfDirty();
        p.ScrollOffset = new Vec2(0, 50);
        Check(Near(kids[0].transform.localPosition.y, y0 - 50), "ofset cocuklari kaydirdi");
        Check(!root._dirty && !p._dirty, "ofset degisimi kirletmedi");

        // culling: ofset 50'de k1 (-50..-10) pencere icinde cizilir; k0 (-100..-60) tamamen disarida.
        var q = new RenderQueue();
        q.Begin(); kids[1].Encode(q);
        Check(q.Count > 0, "pencere icindeki cocuk cizildi");
        q.Begin(); kids[0].Encode(q);
        Check(q.Count == 0, "pencere disindaki cocuk hic quad uretmedi (early-out)");

        // hit-test: klip disinda kalan cocuk vurulmaz, icindeki vurulur (piksel-ortho isin, kamera yok).
        p.ScrollOffset = new Vec2(0, 0);
        var ray = new Ray { Origin = new Vec3(kids[4].WorldCenter.x, kids[4].WorldCenter.y, -100), Dir = new Vec3(0, 0, 1) };
        Check(!kids[4].HitTest(in ray, out _, out _), "klip disindaki cocuk tiklanmaz");
        var ray0 = new Ray { Origin = new Vec3(kids[0].WorldCenter.x, kids[0].WorldCenter.y, -100), Dir = new Vec3(0, 0, 1) };
        Check(kids[0].HitTest(in ray0, out _, out _), "klip icindeki cocuk tiklanir");
        GameObject.Destroy(root.gameObject);
        s.Update(0f);
    }

    // ScrollView: pointer jesti ofseti surer, birakinca atalet sonumlenir, sinir disi yayla doner.
    static void ScrollViewGesture(Scene s)
    {
        var p = NewBox("sv");
        p.Layout = LayoutMode.Vertical;
        p.Width = 100; p.Height = 100; p.OverflowY = OverflowMode.Scroll;
        for (int i = 0; i < 10; i++) { var c = NewBox("k" + i, p.transform); c.Width = 100; c.Height = 40; c.Fill = new Gradient(Color.Blue); }
        var sv = p.gameObject.AddComponent<ScrollView>();
        p.ResolveIfDirty();
        s.Update(0f);
        var input = s.Pointer;
        var wc = p.WorldCenter;
        input.Down(wc.x, wc.y);
        s.Update(1f / 60);
        input.Move(wc.x, wc.y - 30); // esik asilir, 30px yukari surukle
        s.Update(1f / 60);
        float off1 = p.ScrollOffset.y;
        Check(off1 > 20 && off1 <= 30.01f, $"drag ofseti surdu ({off1:F1})");
        input.Move(wc.x, wc.y - 60);
        s.Update(1f / 60);
        float off2 = p.ScrollOffset.y;
        Check(Near(off2, 60), $"kumulatif drag ({off2:F1})");
        input.Up(wc.x, wc.y - 60);
        s.Update(1f / 60);
        float off3 = p.ScrollOffset.y;
        Check(off3 > off2, "birakinca atalet devam etti");
        for (int i = 0; i < 240; i++) s.Update(1f / 60);
        float off4 = p.ScrollOffset.y;
        Check(off4 >= off3 && off4 <= p.ScrollMax.y + 0.01f && sv.Velocity.y == 0f, "atalet sonumlendi, sinir icinde");

        // overscroll: ustten disari cek -> birakinca 0'a yay
        input.Down(wc.x, wc.y);
        s.Update(1f / 60);
        input.Move(wc.x, wc.y + 2000);
        s.Update(1f / 60);
        Check(p.ScrollOffset.y < 0, "overscroll negatif (direncli)");
        input.Up(wc.x, wc.y + 2000);
        for (int i = 0; i < 180; i++) s.Update(1f / 60);
        Check(Near(p.ScrollOffset.y, 0), "yay 0'a dondu");
        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }

    // BoxStyle: kopyalama, asset versiyon bump -> yeniden kopya, SetState transition (ara deger,
    // bitis, listeden dusme), yarida kesme mevcut degerden devam, etkin olmayan varyant -> Normal, 0 alloc.
    static void StyleApplyAndTransition(Scene s)
    {
        var st = new BoxStyle { transition = 0.5f, transitionEase = Ease.Linear };
        st.normal.look.fill = new Gradient(new Color(0, 0, 0, 255));
        st.normal.look.radius = new Vec4(4, 4, 4, 4);
        var pressed = new BoxStyleState { key = BoxStyle.PressedKey, overrides = BoxLookFields.Fill | BoxLookFields.Radius };
        pressed.look.fill = new Gradient(new Color(200, 0, 0, 255));
        pressed.look.radius = new Vec4(12, 12, 12, 12);
        st.variants.Add(pressed);
        st.variants.Add(new BoxStyleState { key = BoxStyle.DisabledKey, overrides = BoxLookFields.None }); // -> Normal

        var b = NewBox("styled");
        b.Width = 100; b.Height = 40;
        b.Fill = new Gradient(Color.White);
        b.Style = st;
        Check(b.Fill.color.r == 0 && Near(b.Radius.x, 4), "stil Normal kopyalandi (kendi alanlar suruldu)");

        st.normal.look.radius = new Vec4(6, 6, 6, 6);
        var q = new RenderQueue();
        q.Begin(); b.Encode(q);
        Check(Near(b.Radius.x, 4), "versiyon degismeden yeniden kopya yok");
        AssetDatabase.NoteObjectChanged();
        q.Begin(); b.Encode(q);
        Check(Near(b.Radius.x, 6), "ObjectVersion bump -> stil yeniden kopyalandi");

        b.SetState(BoxStyle.Pressed);
        Check(b.InTransition && b.Fill.color.r == 0, "SetState: gecis basladi, henuz from degerinde");
        s.Update(0.25f); // lineer, 0.5 sn -> t=0.5
        Check(b.InTransition && b.Fill.color.r > 90 && b.Fill.color.r < 110 && Near(b.Radius.x, 9), $"t=0.5 ara deger (r={b.Fill.color.r}, radius={b.Radius.x})");
        // yarida kes: Normal'e don -> from = mevcut (r~100), hedef 0
        b.SetState(0);
        s.Update(0.25f); // t=0.5 -> r ~ 50
        Check(b.Fill.color.r > 40 && b.Fill.color.r < 60, $"yarida kesme mevcut degerden devam (r={b.Fill.color.r})");
        s.Update(0.3f);
        Check(!b.InTransition && b.Fill.color.r == 0 && Near(b.Radius.x, 6), "gecis bitti: tam hedef, listeden dustu");
        b.SetState(BoxStyle.Disabled);
        s.Update(1f);
        Check(b.Fill.color.r == 0 && Near(b.Radius.x, 6), "etkin olmayan varyant -> Normal");
        // stil kaldirilinca alanlar son degerde kalir, durum degisimi gorunumu degistirmez
        b.Style = null;
        b.SetState(BoxStyle.Pressed);
        s.Update(1f);
        Check(b.Fill.color.r == 0, "stilsiz SetState gorunumu degistirmez");

        // alloc: gecis tick'i + Encode sync
        b.Style = st;
        b.SetState(0);
        b.SetState(BoxStyle.Pressed);
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++) { s.Update(0.01f); q.Begin(); b.Encode(q); }
        Check(GC.GetAllocatedBytesForCurrentThread() - a0 == 0, "transition tick + style sync sifir alloc");
        GameObject.Destroy(b.gameObject);
        s.Update(0f);
        StyleMaskAndTransform(s);
    }

    // Override maskesi: varyant yalniz maskeli gruplari ezer (gerisi Normal'den miras); Transform
    // grubu scale/rotation'i Transform'a yazar (taban x oran), stil kalkinca taban geri; notr stil
    // Transform'a dokunmaz.
    static void StyleMaskAndTransform(Scene s)
    {
        var st = new BoxStyle { transition = 0f };
        st.normal.look.fill = new Gradient(new Color(10, 20, 30, 255));
        st.normal.look.border = new Vec4(3, 3, 3, 3);
        st.normal.look.shadowColor = new Color(0, 0, 0, 128);
        var pressed = new BoxStyleState { key = BoxStyle.PressedKey, overrides = BoxLookFields.Fill | BoxLookFields.Transform };
        pressed.look.fill = new Gradient(new Color(200, 0, 0, 255));
        pressed.look.scale = new Vec2(0.9f, 0.9f);
        pressed.look.rotation = 5f;
        pressed.look.border = new Vec4(9, 9, 9, 9); // maskede yok -> Normal'in 3'u kalir
        st.variants.Add(pressed);

        var b = NewBox("masked");
        b.Width = 100; b.Height = 40;
        b.transform.localScale = new Vec3(2, 2, 1);
        b.Style = st;
        var q = new RenderQueue(); q.Begin(); b.Encode(q);
        Check(Near(b.transform.localScale.x, 2) && !b.InTransition, "Normal notr: Transform'a dokunulmadi");
        b.SetState(BoxStyle.Pressed);
        Check(b.Fill.color.r == 200 && Near(b.Border.x, 3) && b.ShadowColor.a == 128, "maske: Fill ezildi, Border/Shadow Normal'den miras");
        Check(Near(b.transform.localScale.x, 1.8f) && Near(b.transform.localEulerAngles.z, 5f), "Transform grubu: taban x 0.9, +5 derece");
        b.SetState(0);
        Check(Near(b.transform.localScale.x, 2) && Near(b.transform.localEulerAngles.z, 0f), "Normal'e donus: taban geri (notr oran)");
        b.SetState(BoxStyle.Pressed);
        b.Style = null;
        Check(Near(b.transform.localScale.x, 2) && Near(b.transform.localEulerAngles.z, 0f), "stil kaldirilinca Transform tabani geri");
        GameObject.Destroy(b.gameObject);
        s.Update(0f);
        StyleNamedStates(s);
    }

    // v3 isimli durumlar: string/hash API, coklu durum liste sirasiyla bindirme (sonraki ezer), Remove ile geri,
    // bilinmeyen key Normal, StateSet dolu/sikistirma, Resolve/AddState 0 alloc, YAML round-trip (variants listesi).
    static void StyleNamedStates(Scene s)
    {
        var st = new BoxStyle { transition = 0f };
        st.normal.look.fill = new Gradient(new Color(0, 0, 0, 255));
        st.normal.look.radius = new Vec4(1, 1, 1, 1);
        var sel = new BoxStyleState { key = "selected", overrides = BoxLookFields.Fill | BoxLookFields.Radius };
        sel.look.fill = new Gradient(new Color(0, 0, 200, 255));
        sel.look.radius = new Vec4(5, 5, 5, 5);
        var pressed = new BoxStyleState { key = "pressed", overrides = BoxLookFields.Fill };
        pressed.look.fill = new Gradient(new Color(200, 0, 0, 255));
        var vip = new BoxStyleState { key = "vip", overrides = BoxLookFields.Border };
        vip.look.border = new Vec4(7, 7, 7, 7);
        st.variants.Add(sel); st.variants.Add(pressed); st.variants.Add(vip);
        AssetDatabase.NoteObjectChanged();

        var b = NewBox("named");
        b.Width = 50; b.Height = 20;
        b.Style = st;
        b.SetState("selected");
        Check(b.Fill.color.b == 200 && Near(b.Radius.x, 5) && b.HasState("selected") && b.StateCount == 1, "string SetState");
        b.AddState("pressed"); // listede selected'tan SONRA -> Fill'i ezer, Radius selected'tan kalir
        Check(b.Fill.color.r == 200 && b.Fill.color.b == 0 && Near(b.Radius.x, 5) && b.StateCount == 2, "coklu durum: liste sirasiyla bindirme");
        b.AddState(BoxStyle.Hash("vip"));
        Check(Near(b.Border.x, 7) && b.StateCount == 3, "ucuncu durum (hash API)");
        b.RemoveState("pressed");
        Check(b.Fill.color.b == 200 && Near(b.Border.x, 7) && !b.HasState("pressed"), "RemoveState: kalanlar yeniden cozuldu");
        b.SetState("nosuch");
        Check(b.Fill.color.r == 0 && b.Fill.color.b == 0 && Near(b.Radius.x, 1) && Near(b.Border.x, 0), "bilinmeyen key -> Normal");
        b.ClearStates();
        Check(b.StateCount == 0, "ClearStates");

        var set = new StateSet();
        Check(set.Add(1) && set.Add(2) && set.Add(3) && set.Add(4) && !set.Add(5) && !set.Add(2), "StateSet: 4 kapasite, tekrar yok");
        Check(set.Remove(2) && set.Count == 3 && set.Get(0) == 1 && set.Get(1) == 3 && set.Get(2) == 4 && set.Get(3) == 0, "StateSet: sikistirma");

        b.SetState(BoxStyle.Hash("selected"));
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++) { b.AddState(BoxStyle.Pressed); b.RemoveState(BoxStyle.Pressed); }
        Check(GC.GetAllocatedBytesForCurrentThread() - a0 == 0, "Add/Remove/Resolve sifir alloc");

        var node = ObjectSerializer.ToNode(st, null);
        var st2 = new BoxStyle();
        ObjectSerializer.FromNode(st2, node, null);
        Check(st2.variants.Count == 3 && st2.variants[1].key == "pressed" && st2.variants[2].look.border.x == 7 && st2.Has("vip"),
            "BoxStyle YAML round-trip (variants listesi + key)");
        GameObject.Destroy(b.gameObject);
        s.Update(0f);
    }

    // Arrange mikro-bench: 300 cocuk (grow/shrink/margin/alignSelf karisik), dirty -> cozum; 0 B alloc.
    static void ArrangeBench(Scene s)
    {
        const int N = 300;
        var p = NewBox("ab");
        p.Layout = LayoutMode.Horizontal;
        p.Width = 2000; p.Height = 100; p.OverflowX = OverflowMode.Visible;
        for (int i = 0; i < N; i++)
        {
            var c = NewBox("c" + i, p.transform);
            c.Width = 20; c.Height = 20;
            if (i % 3 == 0) c.Shrink = 1;
            if (i % 7 == 0) c.Grow = 1;
            if (i % 5 == 0) c.Margin = new Vec4(2, 2, 2, 2);
            if (i % 4 == 0) c.AlignSelf = LayoutAlignSelf.Stretch;
            if (i % 6 == 0) c.MaxWidth = 15;
        }
        p.ResolveIfDirty(); // isinma
        var sw = new System.Diagnostics.Stopwatch();
        long alloc0 = GC.GetAllocatedBytesForCurrentThread();
        sw.Start();
        for (int rep = 0; rep < 20; rep++)
        {
            p.Width = 2000 + (rep & 1); // kirlet (tum cocuklar yeniden yerlesir)
            p.ResolveIfDirty();
        }
        sw.Stop();
        long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
        Console.WriteLine($"[layout] bench arrange: {N} cocuk, {sw.Elapsed.TotalMilliseconds / 20:F3} ms/cozum, alloc {alloc} B");
        Check(alloc == 0, "bench arrange: sifir alloc");
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
        int plain = q.Count;
        b.LightColor = new Color(255, 255, 255, 200); // gloss = EmitRadial, tam 4 quad
        q.Begin();
        b.Encode(q);
        Check(q.Count == plain + 4, "light +4 quad");
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

    // Cok durakli / capraz gradient: At parcali-lineer; eksenel ara duraklar dolgu
    // quad'larini durak cizgisinde boler (quad sayisi artar), capraz 2-durak kose
    // renkleriyle cizilir (quad sayisi degismez).
    static void GradientStopsSmoke(Scene s)
    {
        var g = new Gradient(Color.Black, Color.White) { stops = 3, color3 = Color.Red, stop3 = 0.5f };
        Check(g.At(0f).r == 0 && g.At(0.5f).r == 255 && g.At(0.5f).g == 0, "3 durak: ortada tam color3");
        Check(g.At(0.75f).g > 120 && g.At(0.75f).g < 136, "3 durak: 0.75 color3->color2 yarisi");
        g.stops = 4; g.color4 = Color.Blue; g.stop4 = 0.75f;
        Check(g.At(0.75f).b == 255 && g.At(0.75f).r == 0, "4 durak: 0.75'te tam color4");
        var two = new Gradient(Color.Black, Color.White);
        Check(two.At(0.5f).r > 120 && two.At(0.5f).r < 136 && two.MidStops == 0, "2 durak: duz lerp");
        var old = new Gradient(Color.Black, Color.White) { stops = 0 };
        Check(old.MidStops == 0 && old.At(1f).r == 255, "stops=0 (eski yaml) 2 durak sayilir");

        var b = NewBox("grad");
        b.Width = 200; b.Height = 100;
        b.Fill = two;
        var q = new RenderQueue();
        q.Begin();
        b.Encode(q);
        int plain = q.Count;
        b.Fill = new Gradient(Color.Black, Color.White, GradientDirection.Diagonal);
        q.Begin();
        b.Encode(q);
        Check(q.Count == plain, "capraz 2 durak: ek quad yok");
        b.Fill = new Gradient(Color.Black, Color.White, GradientDirection.Diagonal) { stops = 3, color3 = Color.Red, stop3 = 0.5f };
        q.Begin();
        b.Encode(q);
        Check(q.Count == plain && b.Fill.MidStops == 0, "capraz: ara duraklar yok sayilir (dikis yok)");
        b.Fill = new Gradient(Color.Black, Color.White) { stops = 3, color3 = Color.Red, stop3 = 0.5f };
        q.Begin();
        b.Encode(q);
        Check(q.Count > plain && q.Count <= plain * 2, "dikey 3 durak: quad'lar durakta bolundu (sinirli artis)");
        GameObject.Destroy(b.gameObject);
        s.Update(0f);
    }

    // SpriteFit: Contain/Cover tek quad, Tile grid (kismi karo dahil), TileCap asiminda karo buyur.
    static void SpriteFitSmoke(Scene s)
    {
        var tex = Texture.FromColor(32, 16, Color.White);
        var b = NewBox("fit");
        b.Width = 100; b.Height = 40;
        b.Sprite = Sprite.FromTexture(tex);
        b.Slice9 = 8; // Stretch disinda yok sayilmali
        var q = new RenderQueue();
        b.SpriteFit = SpriteFit.Contain;
        q.Begin(); b.Encode(q);
        Check(q.Count == 1, "Contain tek quad (slice9 yok sayilir)");
        b.SpriteFit = SpriteFit.Cover;
        q.Begin(); b.Encode(q);
        Check(q.Count == 1, "Cover tek quad");
        b.SpriteFit = SpriteFit.Tile;
        q.Begin(); b.Encode(q);
        Check(q.Count == 4 * 3, "Tile: ceil(100/32) x ceil(40/16) = 12 karo");
        b.Width = 2000; b.Height = 2000; // 63x125 > cap -> buyutulmus karo
        q.Begin(); b.Encode(q);
        Check(q.Count > 0 && q.Count <= 256 + 64, "Tile cap: karo sayisi sinirli");
        GameObject.Destroy(b.gameObject);
        tex.Destroy();
        s.Update(0f);
    }

    // Encode mikro-bench: 300 stilli kutu (radius+border+gradient) 2 durak vs 4 durak vs
    // capraz; sure ve yonetilen tahsis raporlanir. Sicak yol SIFIR alloc olmali (PASS/FAIL).
    static void EncodeBench(Scene s)
    {
        const int N = 300;
        var boxes = new LayoutBox[N];
        for (int i = 0; i < N; i++)
        {
            var b = NewBox("bench" + i);
            b.Width = 120; b.Height = 60; b.CornerRadius = 10; b.BorderWidth = 2;
            b.BorderFill = Color.White;
            b.AnchoredPos = new Vec2(i % 20 * 10, i / 20 * 10);
            boxes[i] = b;
        }
        var q = new RenderQueue();
        void Run(string label, Gradient g, bool check)
        {
            for (int i = 0; i < N; i++) boxes[i].Fill = g;
            q.Begin();
            for (int i = 0; i < N; i++) boxes[i].Encode(q); // isinma + JIT
            var sw = new System.Diagnostics.Stopwatch();
            long alloc0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Start();
            int quads = 0;
            for (int rep = 0; rep < 10; rep++)
            {
                q.Begin();
                for (int i = 0; i < N; i++) boxes[i].Encode(q);
                quads = q.Count;
            }
            sw.Stop();
            long alloc = GC.GetAllocatedBytesForCurrentThread() - alloc0;
            Console.WriteLine($"[layout] bench {label}: {N} kutu, {quads} quad, {sw.Elapsed.TotalMilliseconds / 10:F2} ms/encode, alloc {alloc} B");
            if (check) Check(alloc == 0, $"bench {label}: sifir alloc");
        }
        Run("2-stop", new Gradient(new Color(40, 90, 200, 255), new Color(10, 20, 60, 255)), true);
        Run("4-stop", new Gradient(Color.Red, Color.Blue) { stops = 4, color3 = Color.Green, stop3 = 0.3f, color4 = Color.White, stop4 = 0.7f }, true);
        Run("diagonal", new Gradient(Color.Red, Color.Blue, GradientDirection.Diagonal), true);
        for (int i = 0; i < N; i++) GameObject.Destroy(boxes[i].gameObject);
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

    // Wrap: yatay dizide sigmayan cocuk sonraki satira gecer; yukseklik (Grow)
    // satir toplamina buyur, olcum/arrange ayni kirilimi uretir (lazy pull).
    static void WrapSmoke(Scene s)
    {
        var p = NewBox("wrap");
        p.Layout = LayoutMode.Horizontal;
        p.Width = 100;
        p.OverflowX = OverflowMode.Wrap;
        LayoutBox Kid(string n, float w, float h)
        {
            var b = NewBox(n, p.transform);
            b.Width = w; b.Height = h;
            return b;
        }
        var c1 = Kid("c1", 40, 20);
        var c2 = Kid("c2", 40, 20);
        var c3 = Kid("c3", 40, 30); // 120 > 100 -> 2. satir

        Check(Near(p.RectWidth, 100), "Wrap: ana eksen authored kaldi");
        Check(Near(p.RectHeight, 50), "Wrap: yukseklik satir toplamina buyudu (20+30)");
        var l1 = c1.transform.localPosition;
        var l2 = c2.transform.localPosition;
        var l3 = c3.transform.localPosition;
        Check(Near(l1.x, -30) && Near(l1.y, -15), "Wrap: c1 ilk satirda");
        Check(Near(l2.x, 10) && Near(l2.y, -15), "Wrap: c2 ilk satirda yaninda");
        Check(Near(l3.x, -30) && Near(l3.y, 10), "Wrap: c3 ikinci satira kirildi");

        // spacing hem satir ici hem satir arasi bosluk (gap analogu).
        p.Spacing = 30; // 40+30+40=110 > 100 -> c2 de kirilir: 3 satir
        Check(Near(p.RectHeight, 20 + 30 + 20 + 30 + 30), "Wrap: spacing satirlari da acar");

        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }

    // JustifyChildren: ana eksende grup dagilimi (grow yokken); grow varsa etkisiz.
    static void JustifySmoke(Scene s)
    {
        var p = NewBox("justify");
        p.Layout = LayoutMode.Horizontal;
        p.Width = 300; p.Height = 50;
        p.OverflowX = OverflowMode.Visible; // authored 300 kalsin (icerik 100)
        var a = NewBox("a", p.transform); a.Width = 40; a.Height = 50;
        var b = NewBox("b", p.transform); b.Width = 60; b.Height = 50;
        // transform.localPosition pull YAPMAZ; RectWidth okumasi cozumu tetikler.
        float Ax() { _ = p.RectWidth; return a.transform.localPosition.x; }
        float Bx() { _ = p.RectWidth; return b.transform.localPosition.x; }

        Check(Near(Ax(), -130), "Justify Start: sol kenar");
        p.JustifyChildren = LayoutJustify.End;
        Check(Near(Bx(), 120), "Justify End: sag kenar");
        p.JustifyChildren = LayoutJustify.Center;
        Check(Near(Ax(), -30) && Near(Bx(), 20), "Justify Center");
        p.JustifyChildren = LayoutJustify.SpaceBetween;
        Check(Near(Ax(), -130) && Near(Bx(), 120), "Justify SpaceBetween");
        p.JustifyChildren = LayoutJustify.SpaceEvenly; // 200/3 bosluk
        Check(Near(Ax(), -150 + 200f / 3 + 20), "Justify SpaceEvenly");
        b.Grow = 1; // grow kalan alani alir -> justify etkisiz, a solda
        Check(Near(Ax(), -130) && Near(b.RectWidth, 260), "Justify grow ile etkisiz");
        GameObject.Destroy(p.gameObject);
        s.Update(0f);
    }

    // Derin agacta yaprak degisimi: olcum cache'i dusup koke kadar yeniden
    // olculmeli; artimli arrange boyutu degismeyen kardes dali atlarken degisen
    // dal dogru guncellenmeli (lazy pull ile).
    static void DeepInvalidation(Scene s)
    {
        var root = NewBox("droot");
        root.Layout = LayoutMode.Vertical;
        var row = NewBox("row", root.transform);
        row.Layout = LayoutMode.Horizontal;
        var leaf = NewBox("leaf", row.transform); leaf.Width = 30; leaf.Height = 10;
        var side = NewBox("side", row.transform); side.Width = 20; side.Height = 10;
        var sideKid = NewBox("sidekid", side.transform);
        sideKid.AnchorMin = new Vec2(0, 0); sideKid.AnchorMax = new Vec2(1, 1); // stretch

        Check(Near(root.RectWidth, 50) && Near(sideKid.RectWidth, 20), "derin: ilk cozum");
        leaf.Width = 70; // yaprak -> row -> root olcumu degismeli
        Check(Near(row.RectWidth, 90) && Near(root.RectWidth, 90), "derin: yaprak degisimi koke yansidi");
        Check(Near(sideKid.RectWidth, 20), "derin: boyutu degismeyen kardes dal bozulmadi");
        side.Width = 40; // kardes degisince stretch torun da buyumeli
        // Torun TEMIZ (kirli olan atalar); pull yine de kirli atayi cozmeli.
        Check(Near(sideKid.RectWidth, 40) && Near(root.RectWidth, 110), "derin: degisen dal torunu guncelledi (temiz torundan pull)");
        // Yalniz pozisyon degisimi (leaf kuculur, side kayar): side boyutu ayni,
        // alt agaci atlanir ama pozisyon dogru.
        leaf.Width = 10;
        _ = root.RectWidth;
        Check(Near(side.transform.localPosition.x, -25 + 10 + 20), "derin: kaydirilan kardes pozisyonu");
        Check(Near(sideKid.RectWidth, 40), "derin: atlanan alt agac gecerli kaldi");
        GameObject.Destroy(root.gameObject);
        s.Update(0f);
    }

    // Klip zinciri cozumden gelir (GetComponent yok): reparent sonrasi Hidden
    // ata degisince kirpma yeni ataya gore olmali.
    static void ClipChainReparent(Scene s)
    {
        var hidden = NewBox("hidden");
        hidden.Width = 100; hidden.Height = 40;
        hidden.Overflow = OverflowMode.Hidden;
        var open = NewBox("open");
        open.Width = 100; open.Height = 40;
        var c = NewBox("c", hidden.transform);
        c.Width = 100; c.Height = 40;
        c.AnchoredPos = new Vec2(0, 500); // tamamen disarida
        c.Fill = new Gradient(Color.Red);

        var q = new RenderQueue();
        hidden.ResolveIfDirty();
        q.Begin(); c.Encode(q);
        Check(q.Count == 0, "klip: Hidden ata altinda disaridaki cocuk cizilmedi");
        c.transform.SetParent(open.transform, false);
        open.ResolveIfDirty();
        q.Begin(); c.Encode(q);
        Check(q.Count > 0, "klip: Visible ataya tasininca cizildi");
        GameObject.Destroy(hidden.gameObject);
        GameObject.Destroy(open.gameObject);
        s.Update(0f);
    }
}
#endif
