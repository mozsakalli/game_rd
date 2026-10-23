using System;
using System.Collections.Generic;
using System.Globalization;
using DigitoyEngine;

namespace DigitoyEditor;

// render.describe: ekranin METIN ozeti (DOM/accessibility snapshot karsiligi). Piksele bakmadan
// "ne var, nerede, tiklanabilir mi, ne yaziyor" sorusuna cevap — screenshot'tan ucuz ve kesin.
// EDITOR-ONLY: motor koduna dokunmaz; projeksiyon kameranin public ScreenPointToRay'inden turetilir
// (isin orijini = goz; ortho'da yon (0,0,1)). Rect'ler picking ile ayni tanimlardan geldigi icin
// rect merkezi input.tap'e birebir duser.
static class RpcDescribe
{
    // sim.run/anim.goto icin varsayilan ayarlarla play ozeti.
    internal static DocNode DescribePlay(RpcContext ctx) => Describe(ctx);

    internal sealed class Cam
    {
        public CameraComponent C;
    }

    // Tek bir gorunur renderer'in ekran ozeti. render.describe, annotate screenshot (legend) ve render.expect
    // AYNI listeyi kullanir: etiket numarasi ile describe rect'i asla farkli olamaz.
    internal sealed class Item
    {
        public Renderer R;
        public string Path, Type;
        public float X, Y, W, H;      // ekrana kirpilmis mantiksal rect
        public bool Partial, Click;
        public int Order;
        public float Alpha;
        public string Text, Sprite, Fill, Tint;
        public float Cx => X + W / 2;
        public float Cy => Y + H / 2;

        public DocNode ToNode()
        {
            var m = DocNode.Map();
            m.Add("path", DocNode.Scal(Path));
            m.Add("type", DocNode.Scal(Type));
            m.Add("rect", DocNode.Scal(Rect(X, Y, W, H)));
            if (Partial)
                m.Add("partial", DocNode.Scal("true")); // ekran kenarindan tasiyor (rect kirpilmis)
            m.Add("order", DocNode.Scal(Order.ToString(CultureInfo.InvariantCulture)));
            if (Alpha < 1f)
                m.Add("alpha", DocNode.Scal(RpcCommands.F(MathF.Round(Alpha, 3))));
            if (Text != null) m.Add("text", DocNode.Scal(Text));
            if (Sprite != null) m.Add("sprite", DocNode.Scal(Sprite));
            if (Fill != null) m.Add("fill", DocNode.Scal(Fill));
            if (Tint != null) m.Add("tint", DocNode.Scal(Tint));
            if (Click)
                m.Add("click", DocNode.Scal("true"));
            return m;
        }
    }

    internal sealed class Snapshot
    {
        public Scene Scene;
        public float Vw, Vh;
        public List<Cam> Cams;
        public List<Item> Items = new();
        public int Offscreen, Tiny;
    }

    internal static Scene ResolveScene(RpcContext ctx, string which)
    {
        var scene = which switch
        {
            "play" => PlayMode.PlayScene,
            "edit" => ctx.Scene.LiveScene,
            _ => throw new RpcError("which play|edit olmali"),
        };
        if (scene == null)
            throw new RpcError($"canli sahne yok: {which}" + (which == "play" ? " (sim.run/play.start ile baslat)" : ""));
        return scene;
    }

    [RpcCommand("render.describe", "Ekranin METIN ozeti (screenshot yerine ONCE BUNU dene): cizim sirasinda gorunur renderer'lar, mantiksal ekran rect'i 'x y w h' (merkez = input.tap koordinati), order, alpha(<1), text, sprite, fill, click (tiklanabilir). ILK cagri TAM liste; AYNI parametrelerle sonraki cagri yalniz DEGISENI doner (added/changed/removed | unchanged:true) — full=true tam listeyi yeniden ister. Varsayilan KOMPAKT: ayni yoldaki tekrar eden ogeler (kart ×11) tek satir 'count'+birlesik rect, metinsiz/tiklanmaz dekor alt parcalari atilir; verbose=true ham liste. which: play|edit; root: yalniz bu GO'nun alt agaci ('A/B' yolu ya da ad); types: virgullu tip filtresi (LayoutBox,TextSprite); minSize: kucuk rect'leri at (mantiksal px); clickable=true → yalniz tiklanabilirler (buton envanteri); pixels=N → N sutunluk ortalama renk izgarasi ('rrggbb', ekran bos mu/parlak nerede). Dogrulama icin render.expect, gorsel ton/suphe icin render.screenshot(annotate=true)")]
    static DocNode Describe(RpcContext ctx, string which = "play", string root = "", string types = "", float minSize = 2f, bool clickable = false, int pixels = 0, bool full = false, bool verbose = false)
    {
        var scene = ResolveScene(ctx, which);
        if (pixels < 0 || pixels > 128)
            throw new RpcError("pixels 0..128 olmali");
        var snap = Collect(scene, root, types, minSize, clickable);

        var keyed = new List<KeyValuePair<string, DocNode>>();
        if (verbose)
            foreach (var it in snap.Items)
                keyed.Add(new(it.Path, it.ToNode()));
        else
            Compact(snap.Items, keyed);

        string key = $"describe|{which}|{root}|{types}|{minSize}|{clickable}|{verbose}";
        var res = RpcDelta.Apply(key, keyed, full, "items");
        res.Fields.Insert(0, new KeyValuePair<string, DocNode>("screen", DocNode.Scal(string.Create(CultureInfo.InvariantCulture, $"{snap.Vw:0.##} {snap.Vh:0.##}"))));
        if (res.Get("full") != null)
        {
            var camSeq = DocNode.Seq();
            foreach (var c in snap.Cams)
            {
                var cm = DocNode.Map();
                cm.Add("go", DocNode.Scal(RpcCommands.LivePath(c.C._gameObject)));
                cm.Add("depth", DocNode.Scal(c.C.depth.ToString(CultureInfo.InvariantCulture)));
                cm.Add("projection", DocNode.Scal(c.C.Projection?.GetType().Name ?? "PixelPerfect"));
                camSeq.Items.Add(cm);
            }
            res.Fields.Insert(1, new KeyValuePair<string, DocNode>("cameras", camSeq));
        }
        if (snap.Offscreen > 0) res.Add("offscreen", DocNode.Scal(snap.Offscreen.ToString(CultureInfo.InvariantCulture)));
        if (snap.Tiny > 0) res.Add("tiny", DocNode.Scal(snap.Tiny.ToString(CultureInfo.InvariantCulture)));
        if (pixels > 0)
        {
            var grid = DocNode.Seq();
            foreach (var line in GameOutput.PixelGrid(pixels))
                grid.Items.Add(DocNode.Scal(line));
            res.Add("pixels", grid);
            res.Add("pixelsNote", DocNode.Scal("satir = ust→alt, hucre = ortalama rrggbb; hucre (col,row) ≈ ekran (col*screenW/cols, row*screenH/rows)"));
        }
        if (which == "play" && PlayMode.PlaySceneStale)
            res.Add("stale", DocNode.Scal("true"));
        return res;
    }

    // KOMPAKT liste: (1) ayni yoldaki tekrar eden ogeler (prefab instance'lari: CARD ×11) tek satira iner —
    // 'count', birlesik rect, metin yalniz hepsinde ayniysa; (2) metni/tiklamasi/sprite'i olmayan ve ebeveyni de listede
    // olan dekor alt parcalari (SHADOW/HIGHLIGHT) atilir. Sira ilk gorulme sirasi (cizim sirasi).
    static void Compact(List<Item> items, List<KeyValuePair<string, DocNode>> outList)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var it in items) paths.Add(it.Path);
        var groups = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var it in items)
        {
            bool decor = it.Text == null && !it.Click && it.Sprite == null && it.Fill == null;
            if (decor)
            {
                int slash = it.Path.LastIndexOf('/');
                if (slash > 0 && paths.Contains(it.Path[..slash]))
                    continue; // ebeveyn zaten anlatiyor
            }
            if (!groups.TryGetValue(it.Path, out var g)) { groups[it.Path] = g = new List<Item>(); order.Add(it.Path); }
            g.Add(it);
        }
        foreach (var p in order)
        {
            var g = groups[p];
            if (g.Count == 1) { outList.Add(new(p, g[0].ToNode())); continue; }
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            bool click = false, sameText = true, sameSprite = true;
            foreach (var it in g)
            {
                x0 = MathF.Min(x0, it.X); y0 = MathF.Min(y0, it.Y); x1 = MathF.Max(x1, it.X + it.W); y1 = MathF.Max(y1, it.Y + it.H);
                click |= it.Click;
                sameText &= it.Text == g[0].Text;
                sameSprite &= it.Sprite == g[0].Sprite;
            }
            var m = DocNode.Map();
            m.Add("path", DocNode.Scal(p));
            m.Add("type", DocNode.Scal(g[0].Type));
            m.Add("count", DocNode.Scal(g.Count.ToString(CultureInfo.InvariantCulture)));
            m.Add("rect", DocNode.Scal(Rect(x0, y0, x1 - x0, y1 - y0)));
            m.Add("order", DocNode.Scal(g[0].Order.ToString(CultureInfo.InvariantCulture)));
            if (sameText && g[0].Text != null) m.Add("text", DocNode.Scal(g[0].Text));
            if (sameSprite && g[0].Sprite != null) m.Add("sprite", DocNode.Scal(g[0].Sprite));
            else if (!sameSprite) m.Add("sprite", DocNode.Scal("(farkli)"));
            if (click) m.Add("click", DocNode.Scal("true"));
            m.Add("note", DocNode.Scal("tekrarli oge; tek tek: verbose=true ya da root=" + p));
            outList.Add(new(p, m));
        }
    }

    // Gorunur renderer'lari cizim sirasinda toplar (filtreler describe ile ayni).
    internal static Snapshot Collect(Scene scene, string root = "", string types = "", float minSize = 2f, bool clickable = false)
    {
        float vw = scene.ScreenWidth > 0 ? scene.ScreenWidth : GameOutput.ViewW;
        float vh = scene.ScreenHeight > 0 ? scene.ScreenHeight : GameOutput.ViewH;

        GameObject rootGo = null;
        if (root.Length > 0)
            rootGo = RpcCommands.FindLiveByPath(scene, root)
                ?? throw new RpcError($"canli sahnede GO yok: '{root}' (scene.dumpLive ile adlari gor)");
        HashSet<string> typeSet = null;
        if (types.Length > 0)
        {
            typeSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                typeSet.Add(t);
        }

        scene.ResolveSortOrder(); // bu frame eklenen/tasinan renderer'lar da cizimle ayni sirayi gorsun
        var cams = CollectCams(scene, vw, vh);

        // Cizim sirasi: kamera depth artan (ustteki sonra), icinde effectiveOrder, paintOrder.
        var list = new List<(Renderer r, int camIdx)>();
        for (int i = 0; i < scene.RendererCount; i++)
        {
            var r = scene.RendererAt(i);
            if (r == null || r._destroyed || !r._enabled || !r._gameObject.activeInHierarchy)
                continue;
            if (r._effectiveAlpha <= 0f)
                continue;
            if (rootGo != null && !IsUnder(r._gameObject, rootGo))
                continue;
            if (typeSet != null && !typeSet.Contains(r.GetType().Name))
                continue;
            int ci = PickCam(cams, r._gameObject.layer);
            if (ci < 0)
                continue; // hicbir kamera bu katmani cizmiyor
            list.Add((r, ci));
        }
        list.Sort((a, b) =>
        {
            int c = a.camIdx.CompareTo(b.camIdx);
            if (c != 0) return c;
            c = a.r._effectiveOrder.CompareTo(b.r._effectiveOrder);
            return c != 0 ? c : a.r._paintOrder.CompareTo(b.r._paintOrder);
        });

        var snap = new Snapshot { Scene = scene, Vw = vw, Vh = vh, Cams = cams };
        foreach (var (r, ci) in list)
        {
            if (!ScreenRect(r, cams.Count > 0 ? cams[ci] : null, vw, vh, out float x0, out float y0, out float x1, out float y1))
                continue;
            if (x1 <= 0 || y1 <= 0 || x0 >= vw || y0 >= vh) { snap.Offscreen++; continue; }
            float cx0 = MathF.Max(0, x0), cy0 = MathF.Max(0, y0), cx1 = MathF.Min(vw, x1), cy1 = MathF.Min(vh, y1);
            if (cx1 - cx0 < minSize || cy1 - cy0 < minSize) { snap.Tiny++; continue; }
            bool click = r.BlocksRaycast || HasHandlerInChain(r._gameObject);
            if (clickable && !click)
                continue;
            var it = new Item
            {
                R = r,
                Path = RpcCommands.LivePath(r._gameObject),
                Type = r.GetType().Name,
                X = cx0, Y = cy0, W = cx1 - cx0, H = cy1 - cy0,
                Partial = cx0 != x0 || cy0 != y0 || cx1 != x1 || cy1 != y1,
                Order = r._effectiveOrder,
                Alpha = r._effectiveAlpha,
                Click = click,
            };
            Detail(r, it);
            snap.Items.Add(it);
        }
        return snap;
    }

    // Kameralar depth ARTAN sirada (cizim sirasi); pick ise ters-depth ilk eslesen (PickCam).
    static List<Cam> CollectCams(Scene scene, float vw, float vh)
    {
        var cams = new List<Cam>();
        for (int i = 0; i < scene.CameraCount; i++)
        {
            var c = scene.CameraAt(i);
            if (!c.isActiveAndEnabled)
                continue;
            var cam = new Cam { C = c };
            cams.Add(cam);
        }
        cams.Sort((a, b) => a.C.depth.CompareTo(b.C.depth));
        return cams;
    }

    // Pointer.Pick ile ayni: en buyuk depth'ten baslayip katmani iceren ilk kamera. -1 = kamera listesi bos
    // degil ama hicbiri cizmiyor; kamera yoksa 0 (piksel-ortho fallback).
    static int PickCam(List<Cam> cams, int layer)
    {
        if (cams.Count == 0)
            return 0;
        for (int i = cams.Count - 1; i >= 0; i--)
            if ((cams[i].C.cullingMask & (1 << layer)) != 0)
                return i;
        return -1;
    }

    // Lokal 3D AABB'nin 8 kosesi kameranin GERCEK matrisiyle ekrana izdusurulur (2D renderer'da z=0 →
    // 4 farkli kose); projeksiyon tipi (ortho/persp/rotasyonlu 3D) burada bilinmez.
    static bool ScreenRect(Renderer r, Cam cam, float vw, float vh, out float x0, out float y0, out float x1, out float y1)
    {
        x0 = y0 = x1 = y1 = 0;
        if (!r.GetLocalBounds3D(out var mn, out var mx))
            return false;
        ref var wm = ref r.transform._getWorldMatrix();
        bool any = false;
        for (int k = 0; k < 8; k++)
        {
            var lp = new Vec3((k & 1) == 0 ? mn.x : mx.x, (k & 2) == 0 ? mn.y : mx.y, (k & 4) == 0 ? mn.z : mx.z);
            var wp = Mat4.TransformPoint(ref wm, lp);
            if (!Project(cam, vw, vh, wp, out float sx, out float sy))
                return false; // kose gozun arkasinda: rect tanimsiz
            if (!any) { x0 = x1 = sx; y0 = y1 = sy; any = true; }
            else
            {
                if (sx < x0) x0 = sx; if (sx > x1) x1 = sx;
                if (sy < y0) y0 = sy; if (sy > y1) y1 = sy;
            }
        }
        return any;
    }

    // Dunya noktasi → mantiksal ekran: kameranin kendi izdusumu (CameraComponent.WorldToScreenPoint);
    // kamera yoksa piksel-ortho (dunya xy = ekran).
    static bool Project(Cam cam, float vw, float vh, Vec3 p, out float sx, out float sy)
    {
        if (cam == null) { sx = p.x; sy = p.y; return true; }
        return cam.C.WorldToScreenPoint(p, vw, vh, out sx, out sy);
    }

    static void Detail(Renderer r, Item it)
    {
        switch (r)
        {
            case LayoutBox lb:
                if (lb.Font != null && lb.Text.Length > 0)
                    it.Text = lb.Text;
                if (lb.Sprite != null)
                    it.Sprite = lb.Sprite.Name ?? "";
                if (lb.Fill.Visible)
                    it.Fill = GradientStr(lb.Fill);
                break;
            case TextSprite ts:
                if (ts.Text.Length > 0)
                    it.Text = ts.Text;
                break;
            case SpriteRenderer sr:
                if (sr.Sprite != null)
                    it.Sprite = sr.Sprite.Name ?? "";
                if (sr.Color.r != 255 || sr.Color.g != 255 || sr.Color.b != 255 || sr.Color.a != 255)
                    it.Tint = Hex(sr.Color);
                break;
        }
    }

    static string GradientStr(in Gradient g)
        => g.type == GradientType.Solid ? Hex(g.color) : $"{g.type}:{Hex(g.color)}→{Hex(g.color2)}";

    static string Hex(Color c) => $"#{c.r:x2}{c.g:x2}{c.b:x2}{c.a:x2}";

    static string Rect(float x, float y, float w, float h)
        => string.Create(CultureInfo.InvariantCulture, $"{MathF.Round(x)} {MathF.Round(y)} {MathF.Round(w)} {MathF.Round(h)}");

    static bool IsUnder(GameObject go, GameObject root)
    {
        for (var g = go; g != null; g = g.transform.parent?._gameObject)
            if (g == root)
                return true;
        return false;
    }

    // PointerInput.HasHandlerInChain'in editor kopyasi (motor API'si private; tik aninda kisa zincir).
    static bool HasHandlerInChain(GameObject go)
    {
        for (var g = go; g != null; g = g.transform.parent?._gameObject)
        {
            int n = g.ComponentCount;
            for (int i = 0; i < n; i++)
            {
                var c = g.ComponentAt(i);
                if (!c._destroyed && c._enabled && (c._flags & LifecycleFlags.AnyPointer) != 0)
                    return true;
            }
        }
        return false;
    }
}
