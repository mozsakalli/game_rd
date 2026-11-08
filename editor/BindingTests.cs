#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using DigitoyEngine;

namespace DigitoyEditor;

// Pull binding testleri (Binding / BindingContext / BindingSystem): izole sahne, Scene.Update elle.
public static class BindingTests
{
    static int _pass, _fail;

    [Bindable]
    public sealed class LobbyModel
    {
        public int Total;
        public long Money;
        public string Title = "sample";
        public bool Open;
        public float Alpha = 1f;
        public Color Tint = new(255, 0, 0, 255);
        public Pos Position;
        [Bindable] public string Summary => Open ? "open" : "closed";
        [Bindable] public float Half => Alpha * 0.5f;
        public string NotBound => "x"; // isaretsiz property: tabloya girmez
    }

    [Serializable]
    public struct Pos
    {
        public Vec3 P;
    }

    [Bindable]
    public sealed class OtherModel
    {
        public int Total = 99;
    }

    [Bindable]
    public sealed class RoomModel
    {
        public List<PlayerModel> Players = new();
        [Bindable] public List<PlayerModel> Ready => Players.FindAll(p => p.IsReady);
    }

    [Bindable]
    public sealed class PlayerModel
    {
        public string Name;
        public int Score;
        public bool IsReady;
    }

    [AddComponentMenu("")]
    public sealed class View : Component
    {
        public string Text = "";
        public int Count;
        public long Big;
        public float Alpha;
        public bool Shown;
        public Color Tint;
        public Vec3 P;
        public int Validated;
        public string Label { get; set; } = "";
        protected internal override void OnValidate() => Validated++;
    }

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prev = Scene.Active;
        var s = Scene.Create("binding-test");
        s.Catalog = catalog;
        Scene.SetActive(s);

        SourceTable(s);
        ContextPropagation(s);
        DictionaryModel(s);
        NearestContextWins(s);
        Globals(s);
        ModelSwap(s);
        Pseudo(s);
        Lifetime(s);
        EditModeSample(catalog);
        ListViews(s);
        ZeroAlloc(s);

        Scene.SetActive(prev);
        Scene.Unload(s);
        Console.WriteLine($"[binding] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[binding] FAIL: {name}"); }
    }

    static void SourceTable(Scene s)
    {
        var sys = s.GetSystem<BindingSystem>();
        Check(sys.Resolve(typeof(LobbyModel), "Total")?.Type == BindType.Int, "alan Int");
        Check(sys.Resolve(typeof(LobbyModel), "Money")?.Type == BindType.Int, "long alan Int (kayipsiz)");
        Check(sys.Resolve(typeof(LobbyModel), "Title")?.Type == BindType.String, "alan String");
        Check(sys.Resolve(typeof(LobbyModel), "Position.P")?.Type == BindType.Vec3, "ic ice struct yolu");
        Check(sys.Resolve(typeof(LobbyModel), "Summary")?.Type == BindType.String, "[Bindable] property");
        Check(sys.Resolve(typeof(LobbyModel), "NotBound") == null, "isaretsiz property tabloda yok");
        var m = new LobbyModel { Open = true, Money = 9_007_199_254_740_993L };
        Check(sys.Resolve(typeof(LobbyModel), "Summary").Get(m).Ref as string == "open", "property getter okur");
        Check(sys.Resolve(typeof(LobbyModel), "Money").Get(m).I == 9_007_199_254_740_993L, "long 2^53+1 kayipsiz");
        // hedef tarafi: sema alanlari + public property + pseudo; anim tablosuyla iliskisiz
        Check(sys.ResolveTarget(s.Catalog, typeof(View), "Text")?.Type == BindType.String, "hedef String alan");
        Check(sys.ResolveTarget(s.Catalog, typeof(View), "Big")?.Type == BindType.Int, "hedef long alan");
        Check(sys.ResolveTarget(s.Catalog, typeof(View), "Label")?.Type == BindType.String, "hedef public property");
        Check(sys.ResolveTarget(s.Catalog, typeof(View), "Active")?.Pseudo == true, "pseudo Active");
        Check(sys.ResolveTarget(s.Catalog, typeof(View), "Validated") != null && sys.ResolveTarget(s.Catalog, typeof(View), "Yok") == null, "bilinmeyen yol null");
        Check(AnimRegistry.Find(s.Catalog, typeof(View), "Text") == null && AnimRegistry.Find(s.Catalog, typeof(View), "Count") != null,
            "anim tablosu degismedi (String keyframe'lenmez)");
        // sozluk modeli: anahtar zinciri, calisma zamani turu
        var d = new Dictionary<string, object> { ["ME"] = new Dictionary<string, object> { ["name"] = "oz", ["money"] = 12L, ["ok"] = true } };
        var src = sys.Resolve(d.GetType(), "ME.name");
        Check(src != null && src.Dynamic && src.Get(d).Type == BindType.String && (string)src.Get(d).Ref == "oz", "sozluk: ic ice string");
        Check(sys.Resolve(d.GetType(), "ME.money").Get(d).I == 12, "sozluk: long");
        Check(sys.Resolve(d.GetType(), "ME.yok").Get(d).Type == BindType.Null, "sozluk: eksik anahtar Null");
        Check(sys.Resolve(d.GetType(), "ME.ok.x").Get(d).Type == BindType.Null, "sozluk: ara dugum sozluk degil → Null");
        // donusum kurallari
        Check(BindValue.Int(7).TryConvert(BindType.String, out var cv) && (string)cv.Ref == "7", "Int→String");
        Check(BindValue.Float(2.5).TryConvert(BindType.String, out cv) && (string)cv.Ref == "2.5", "Float→String invariant");
        Check(BindValue.Int(3).TryConvert(BindType.Bool, out cv) && cv.I == 1, "Int→Bool");
        Check(!BindValue.Str("3").TryConvert(BindType.Int, out _), "String→Int yok");
        Check(!BindValue.Vec(new Vec3(1, 2, 3)).TryConvert(BindType.Vec2, out _), "Vec3→Vec2 yok");
    }

    static (GameObject root, View view, Binding b, LobbyModel m) Setup(Scene s, string name)
    {
        var root = new GameObject(name + "-root");
        var m = new LobbyModel();
        root.AddComponent<BindingContext>().SetModel(m);
        var child = new GameObject(name + "-child");
        child.transform.SetParent(root.transform, false);
        var view = child.AddComponent<View>();
        var b = child.AddComponent<Binding>();
        return (root, view, b, m);
    }

    static void ContextPropagation(Scene s)
    {
        var (root, view, b, m) = Setup(s, "ctx");
        b.Add(view, "Text", "Title");
        b.Add(view, "Count", "Total");
        b.Add(view, "Alpha", "Total");     // Int → Float
        b.Add(view, "Shown", "Open");
        b.Add(view, "Tint", "Tint");
        b.Add(view, "P", "Position.P");
        b.Add(view, "Text", "NotBound");   // cozulmez, sessiz atlanir
        m.Total = 5; m.Title = "hello"; m.Open = true; m.Position.P = new Vec3(1, 2, 3);
        s.Update(0f);
        Check(view.Text == "hello" && view.Count == 5 && view.Alpha == 5f && view.Shown && view.P.z == 3f && view.Tint.r == 255,
            "ilk frame: tum degerler yazildi");
        Check(view.Validated == 1, "frame sonunda TEK OnValidate (" + view.Validated + ")");
        Check(b.ResolvedCount == 7, "hedefi cozulen bind sayisi 7 (kaynak cozumu model-bagimli, tembel) (" + b.ResolvedCount + ")");
        s.Update(0f);
        Check(view.Validated == 1, "degisiklik yoksa yazim/OnValidate yok");
        m.Total = 7;
        view.Count = 0;
        s.Update(0f);
        Check(view.Count == 7 && view.Alpha == 7f && view.Validated == 2, "degisen alan ayni frame'de yazildi");
        m.Title = new string("hello".ToCharArray());
        s.Update(0f);
        Check(view.Validated == 2, "ayni icerikli yeni string degisiklik degil");
        // computed property
        b.Add(view, "Text", "Summary");
        s.Update(0f);
        Check(view.Text == "open", "computed property bagi");
        m.Open = false;
        s.Update(0f);
        Check(view.Text == "closed", "computed property degisimi izlenir");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    // Sozluk modeli uctan uca + donusumlu yazim (long → string hedefi, property hedefi).
    static void DictionaryModel(Scene s)
    {
        var root = new GameObject("dict-root");
        var me = new Dictionary<string, object> { ["name"] = "oz", ["money"] = 1_000_000_000_000L, ["faceId"] = 3 };
        var model = new Dictionary<string, object> { ["ME"] = me };
        root.AddComponent<BindingContext>().SetModel(model);
        var child = new GameObject("dict-child");
        child.transform.SetParent(root.transform, false);
        var view = child.AddComponent<View>();
        var b = child.AddComponent<Binding>();
        b.Add(view, "Label", "ME.name");
        b.Add(view, "Text", "ME.money");   // long → string
        b.Add(view, "Big", "ME.money");    // long → long
        b.Add(view, "Count", "ME.faceId"); // int → int
        b.Add(view, "Alpha", "ME.yok");    // eksik: yazilmaz
        view.Alpha = 0.25f;
        s.Update(0f);
        Check(view.Label == "oz" && view.Text == "1000000000000" && view.Big == 1_000_000_000_000L && view.Count == 3 && view.Alpha == 0.25f,
            "sozluk modeli: zincir okuma, long kayipsiz, long→string, eksik anahtar dokunmaz");
        Check(view.Validated == 1, "sozluk: frame sonunda tek OnValidate (" + view.Validated + ")");
        s.Update(0f);
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        s.Update(0f);
        long d = GC.GetAllocatedBytesForCurrentThread() - a0;
        Check(d == 0, "sozluk idle frame alloc 0 (" + d + " B)");
        me["money"] = 5L;
        me["name"] = 42; // tur degisti: int → string hedefi (donusum)
        s.Update(0f);
        Check(view.Text == "5" && view.Big == 5 && view.Label == "42", "sozluk degeri/turu degisince yazildi");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    static void NearestContextWins(Scene s)
    {
        var (root, view, b, m) = Setup(s, "near");
        var mid = new GameObject("near-mid");
        mid.transform.SetParent(root.transform, false);
        view.transform.SetParent(mid.transform, false);
        var m2 = new LobbyModel { Total = 42 };
        mid.AddComponent<BindingContext>().SetModel(m2);
        b.Invalidate();
        b.Add(view, "Count", "Total");
        m.Total = 1;
        s.Update(0f);
        Check(view.Count == 42, "en yakin context kazanir");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    static void Globals(Scene s)
    {
        var sys = s.GetSystem<BindingSystem>();
        var go = new GameObject("glob");
        var view = go.AddComponent<View>();
        var b = go.AddComponent<Binding>();
        b.Add(view, "Count", "$Lobby.Total");
        s.Update(0f);
        Check(view.Count == 0 && b.ResolvedCount == 1, "global yokken bind cozulu kalir, yazmaz");
        var g = new LobbyModel { Total = 3 };
        sys.SetGlobal("Lobby", g);
        s.Update(0f);
        Check(view.Count == 3, "SetGlobal sonrasi yazildi");
        sys.SetGlobal("Lobby", new OtherModel());
        s.Update(0f);
        Check(view.Count == 99, "farkli tipte global: kaynak yeniden cozuldu");
        sys.SetGlobal("Lobby", null);
        s.Update(0f);
        Check(view.Count == 99, "global kaldirilinca son deger kalir");
        GameObject.Destroy(go);
        s.Update(0f);
    }

    static void ModelSwap(Scene s)
    {
        var (root, view, b, m) = Setup(s, "swap");
        var ctx = root.GetComponent<BindingContext>();
        b.Add(view, "Count", "Total");
        m.Total = 1;
        s.Update(0f);
        var m2 = new LobbyModel { Total = 1 };
        ctx.SetModel(m2);
        view.Count = 0;
        s.Update(0f);
        Check(view.Count == 1, "model degisince ayni deger de yeniden yazilir");
        ctx.SetModel(null);
        m2.Total = 5;
        s.Update(0f);
        Check(view.Count == 1, "model null: son deger kalir, hata yok");
        ctx.SetModel(m2);
        s.Update(0f);
        Check(view.Count == 5, "model geri gelince guncel deger");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    static void Pseudo(Scene s)
    {
        var (root, view, b, m) = Setup(s, "pseudo");
        b.Add(view.transform, "Active", "Open");
        b.Add(view, "Enabled", "Open");
        m.Open = true;
        s.Update(0f);
        Check(view.gameObject.activeSelf && view.enabled, "Transform.Active / Enabled pseudo hedefler (true)");
        m.Open = false;
        s.Update(0f);
        Check(!view.gameObject.activeSelf, "Active=false: Binding'in GO'su kapandi (sweep icinde unregister)");
        Check(!view.enabled, "Enabled=false ayni frame");
        var sys = s.GetSystem<BindingSystem>();
        int before = sys.ActiveCount;
        m.Open = true;
        s.Update(0f);
        Check(!view.gameObject.activeSelf && sys.ActiveCount == before, "kapali GO'daki binding uyur (kendini acamaz)");
        GameObject.Destroy(root);
        s.Update(0f);
    }

    static void Lifetime(Scene s)
    {
        var sys = s.GetSystem<BindingSystem>();
        int baseCount = sys.ActiveCount;
        var (root, view, b, m) = Setup(s, "life");
        b.Add(view, "Count", "Total");
        s.Update(0f);
        Check(sys.ActiveCount == baseCount + 1, "enable: sisteme kayit");
        b.enabled = false;
        Check(sys.ActiveCount == baseCount, "disable: kayittan dusme");
        b.enabled = true;
        m.Total = 9;
        s.Update(0f);
        Check(view.Count == 9 && sys.ActiveCount == baseCount + 1, "yeniden enable: calisir");
        GameObject.Destroy(root);
        s.Update(0f);
        Check(sys.ActiveCount == baseCount, "destroy: kayit kalmadi");
        // context'i yok olan binding: hata yok, Invalidate ile yeni context'e baglanir
        var (root2, view2, b2, _) = Setup(s, "life2");
        b2.Add(view2, "Count", "Total");
        s.Update(0f);
        GameObject.Destroy(root2.GetComponent<BindingContext>());
        s.Update(0f);
        s.Update(0f);
        Check(b2.ResolvedCount == 0, "context yok edilince bind duser");
        var g = new GameObject("life2-newroot");
        g.AddComponent<BindingContext>().SetModel(new LobbyModel { Total = 12 });
        root2.transform.SetParent(g.transform, false);
        b2.Invalidate();
        s.Update(0f);
        Check(view2.Count == 12, "Invalidate: yeni context'e baglandi");
        GameObject.Destroy(g);
        s.Update(0f);
    }

    static void EditModeSample(TypeCatalog catalog)
    {
        var prev = Scene.Active;
        var es = Scene.Create("binding-edit");
        es.Catalog = catalog;
        es.EditMode = true;
        Scene.SetActive(es);
        var go = new GameObject("edit");
        var ctx = go.AddComponent<BindingContext>();
        ctx.ModelType = typeof(LobbyModel).FullName;
        var view = go.AddComponent<View>();
        var b = go.AddComponent<Binding>();
        b.Add(view, "Text", "Title");
        b.Add(view, "Alpha", "Half");
        es.Update(0f, simulate: false);
        Check(view.Text == "sample" && view.Alpha == 0.5f, "edit modu: ModelType'tan ornek instance ile onizleme");
        ctx.ModelType = "YokBoyleTip";
        es.Update(0f, simulate: false);
        Check(view.Text == "sample", "bilinmeyen tip: hata yok, son deger kalir");
        Scene.SetActive(prev);
        Scene.Unload(es);
    }

    static void ListViews(Scene s)
    {
        var sys = s.GetSystem<BindingSystem>();
        Check(sys.Resolve(typeof(RoomModel), "Players")?.IsList == true, "List<T> alani liste kaynagi");
        Check(sys.Resolve(typeof(RoomModel), "Ready")?.IsList == true, "[Bindable] liste property kaynagi");
        Check(sys.Resolve(typeof(PlayerModel), "Name")?.Type == BindType.String, "eleman modeli tablosu");

        var root = new GameObject("list-root");
        var room = new RoomModel();
        root.AddComponent<BindingContext>().SetModel(room);
        // sablon: pasif GO, icinde View + Binding (eleman context'i otomatik eklenir)
        var tpl = new GameObject("tpl");
        tpl.transform.SetParent(root.transform, false);
        tpl.SetActive(false);
        var tv = tpl.AddComponent<View>();
        var tb = tpl.AddComponent<Binding>();
        tb.Add(tv, "Text", "Name");
        tb.Add(tv, "Count", "Score");
        var holder = new GameObject("list-holder");
        holder.transform.SetParent(root.transform, false);
        var lb = holder.AddComponent<ListBinding>();
        lb.Source = "Players";
        lb.Template = tpl;

        s.Update(0f);
        Check(lb.ViewCount == 0 && holder.transform.childCount == 0, "bos liste: gorunum yok");

        room.Players.Add(new PlayerModel { Name = "a", Score = 1 });
        room.Players.Add(new PlayerModel { Name = "b", Score = 2 });
        s.Update(0f);
        Check(lb.ViewCount == 2 && holder.transform.childCount == 2, "2 eleman → 2 gorunum (" + lb.ViewCount + ")");
        var v0 = lb.ViewAt(0)?.GetComponent<View>();
        var v1 = lb.ViewAt(1)?.GetComponent<View>();
        Check(v0 != null && v0.Text == "a" && v0.Count == 1 && v1 != null && v1.Text == "b" && v1.Count == 2,
            "gorunumler AYNI frame'de elemana baglandi");
        Check(lb.ViewAt(0).activeSelf && lb.ViewAt(0).GetComponent<BindingContext>() != null, "kopya aktif + eleman context'i");

        room.Players[1].Score = 20;
        s.Update(0f);
        Check(v1.Count == 20, "eleman alani degisimi yayildi");

        room.Players.Reverse();
        s.Update(0f);
        Check(ReferenceEquals(lb.ViewAt(0).GetComponent<View>(), v0) && v0.Text == "b" && v1.Text == "a",
            "indeks sabit, eleman swap → ayni gorunum yeniden baglandi");

        room.Players.Add(new PlayerModel { Name = "c", Score = 3 });
        s.Update(0f);
        Check(lb.ViewCount == 3 && lb.ViewAt(2).GetComponent<View>().Text == "c", "buyume");

        room.Players.RemoveAt(0);
        s.Update(0f);
        Check(lb.ViewCount == 2 && holder.transform.childCount == 2 && v0.Text == "a", "kuculme: fazla gorunum yok edildi");

        GameObject.Destroy(lb.ViewAt(1));
        s.Update(0f);
        Check(lb.ViewCount == 2 && !lb.ViewAt(1).IsDestroyed && lb.ViewAt(1).GetComponent<View>().Text == "c",
            "disaridan yok edilen gorunum yeniden yaratildi");

        // computed liste: filtre
        var holder2 = new GameObject("ready-holder");
        holder2.transform.SetParent(root.transform, false);
        var lb2 = holder2.AddComponent<ListBinding>();
        lb2.Source = "Ready";
        lb2.Template = tpl;
        room.Players[0].IsReady = true;
        s.Update(0f);
        Check(lb2.ViewCount == 1 && lb2.ViewAt(0).GetComponent<View>().Text == "a", "computed liste (filtre)");

        // idle frame alloc: liste okuma + kiyas sifir alloc (computed liste haric: FindAll her turda yeni liste)
        GameObject.Destroy(holder2);
        s.Update(0f);
        s.Update(0f);
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        s.Update(0f);
        long d = GC.GetAllocatedBytesForCurrentThread() - a0;
        Check(d == 0, "liste idle frame alloc 0 (" + d + " B)");

        room.Players.Clear();
        s.Update(0f);
        Check(lb.ViewCount == 0 && holder.transform.childCount == 0, "temizle: tum gorunumler gitti");

        GameObject.Destroy(root);
        s.Update(0f);
        Check(sys.ListCount == 0, "yok edilen ListBinding kayittan dustu");
    }

    static void ZeroAlloc(Scene s)
    {
        const int N = 1000;
        var root = new GameObject("bench-root");
        var m = new LobbyModel { Total = 1, Title = "t" };
        root.AddComponent<BindingContext>().SetModel(m);
        var views = new View[N];
        for (int i = 0; i < N; i++)
        {
            var go = new GameObject("b" + i);
            go.transform.SetParent(root.transform, false);
            views[i] = go.AddComponent<View>();
            var b = go.AddComponent<Binding>();
            b.Add(views[i], "Count", "Total");
            b.Add(views[i], "Text", "Title");
            b.Add(views[i], "Alpha", "Half");
        }
        s.Update(0f);
        s.Update(0f);
        var sw = Stopwatch.StartNew();
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int f = 0; f < 10; f++)
            s.Update(0f);
        double idle = sw.Elapsed.TotalMilliseconds / 10;
        long idleAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        a0 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        for (int f = 0; f < 10; f++) { m.Total = f + 10; s.Update(0f); }
        double busy = sw.Elapsed.TotalMilliseconds / 10;
        long busyAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        Check(idleAlloc == 0, "sweep alloc 0 (degismeyen frame, " + idleAlloc + " B)");
        Check(busyAlloc == 0, "sweep alloc 0 (degisen frame, " + busyAlloc + " B)");
        Check(views[N - 1].Count == 19, "bench degerleri dogru");
        Console.WriteLine($"[binding] bench: {N * 3} bind, idle {idle:F3} ms/frame, 1 alan degisimi {busy:F3} ms/frame");
        GameObject.Destroy(root);
        s.Update(0f);
    }
}
#endif
