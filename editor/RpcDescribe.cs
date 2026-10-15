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

    sealed class Cam
    {
        public CameraComponent C;
        public float Rx, Ry, Rw, Rh;   // z=0 dunya gorus rect'i
        public bool Persp;
        public Vec3 Eye;
    }

    [RpcCommand("render.describe", "Ekranin METIN ozeti (screenshot yerine ONCE BUNU dene): cizim sirasinda gorunur renderer'lar, mantiksal ekran rect'i 'x y w h' (merkez = input.tap koordinati), order, alpha(<1), text, sprite, fill, click (tiklanabilir). which: play|edit; root: yalniz bu GO'nun alt agaci ('A/B' yolu ya da ad); types: virgullu tip filtresi (LayoutBox,TextSprite); minSize: kucuk rect'leri at (mantiksal px); clickable=true → yalniz tiklanabilirler (buton envanteri); pixels=N → N sutunluk ortalama renk izgarasi ('rrggbb', ekran bos mu/parlak nerede). Gorsel ton/suphe icin render.screenshot")]
    static DocNode Describe(RpcContext ctx, string which = "play", string root = "", string types = "", float minSize = 2f, bool clickable = false, int pixels = 0)
    {
        var scene = which switch
        {
            "play" => PlayMode.PlayScene,
            "edit" => ctx.Scene.LiveScene,
            _ => throw new RpcError("which play|edit olmali"),
        };
        if (scene == null)
            throw new RpcError($"canli sahne yok: {which}" + (which == "play" ? " (sim.run/play.start ile baslat)" : ""));
        if (pixels < 0 || pixels > 128)
            throw new RpcError("pixels 0..128 olmali");
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

        var items = DocNode.Seq();
        int offscreen = 0, tiny = 0;
        foreach (var (r, ci) in list)
        {
            if (!ScreenRect(r, cams.Count > 0 ? cams[ci] : null, vw, vh, out float x0, out float y0, out float x1, out float y1))
                continue;
            if (x1 <= 0 || y1 <= 0 || x0 >= vw || y0 >= vh) { offscreen++; continue; }
            float cx0 = MathF.Max(0, x0), cy0 = MathF.Max(0, y0), cx1 = MathF.Min(vw, x1), cy1 = MathF.Min(vh, y1);
            if (cx1 - cx0 < minSize || cy1 - cy0 < minSize) { tiny++; continue; }
            bool click = r.BlocksRaycast || HasHandlerInChain(r._gameObject);
            if (clickable && !click)
                continue;
            var m = DocNode.Map();
            m.Add("path", DocNode.Scal(RpcCommands.LivePath(r._gameObject)));
            m.Add("type", DocNode.Scal(r.GetType().Name));
            m.Add("rect", DocNode.Scal(Rect(cx0, cy0, cx1 - cx0, cy1 - cy0)));
            if (cx0 != x0 || cy0 != y0 || cx1 != x1 || cy1 != y1)
                m.Add("partial", DocNode.Scal("true")); // ekran kenarindan tasiyor (rect kirpilmis)
            m.Add("order", DocNode.Scal(r._effectiveOrder.ToString(CultureInfo.InvariantCulture)));
            if (r._effectiveAlpha < 1f)
                m.Add("alpha", DocNode.Scal(RpcCommands.F(MathF.Round(r._effectiveAlpha, 3))));
            Detail(r, m);
            if (click)
                m.Add("click", DocNode.Scal("true"));
            items.Items.Add(m);
        }

        var res = DocNode.Map();
        res.Add("screen", DocNode.Scal(string.Create(CultureInfo.InvariantCulture, $"{vw:0.##} {vh:0.##}")));
        var camSeq = DocNode.Seq();
        foreach (var c in cams)
        {
            var cm = DocNode.Map();
            cm.Add("go", DocNode.Scal(RpcCommands.LivePath(c.C._gameObject)));
            cm.Add("depth", DocNode.Scal(c.C.depth.ToString(CultureInfo.InvariantCulture)));
            cm.Add("projection", DocNode.Scal(c.C.Projection?.GetType().Name ?? "PixelPerfect"));
            camSeq.Items.Add(cm);
        }
        res.Add("cameras", camSeq);
        res.Add("items", items);
        res.Add("count", DocNode.Scal(items.Items.Count.ToString(CultureInfo.InvariantCulture)));
        if (offscreen > 0) res.Add("offscreen", DocNode.Scal(offscreen.ToString(CultureInfo.InvariantCulture)));
        if (tiny > 0) res.Add("tiny", DocNode.Scal(tiny.ToString(CultureInfo.InvariantCulture)));
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
            c.GetWorldRect(vw, vh, out cam.Rx, out cam.Ry, out cam.Rw, out cam.Rh);
            // Projeksiyon tipi public isin API'sinden: ortho isini eksene paralel, perspektifte goz orijin.
            if (c.ScreenPointToRay(0, 0, vw, vh, out var ray) && (MathF.Abs(ray.Dir.x) > 1e-6f || MathF.Abs(ray.Dir.y) > 1e-6f || ray.Dir.z < 0.9999f))
            {
                cam.Persp = true;
                cam.Eye = ray.Origin;
            }
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

    static bool ScreenRect(Renderer r, Cam cam, float vw, float vh, out float x0, out float y0, out float x1, out float y1)
    {
        x0 = y0 = x1 = y1 = 0;
        if (!r.GetLocalSelectionBounds(out var c, out var h))
            return false;
        ref var wm = ref r.transform._getWorldMatrix();
        bool any = false;
        for (int k = 0; k < 4; k++)
        {
            var lp = new Vec3(c.x + ((k & 1) == 0 ? -h.x : h.x), c.y + ((k & 2) == 0 ? -h.y : h.y), 0);
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

    // Dunya noktasi → mantiksal ekran. Perspektifte goz→p dogrusunun z=0 kesiti (ScreenPointToRay'in tersi),
    // sonra z=0 gorus rect'i ile dogrusal haritalama; kamera yoksa piksel-ortho (dunya xy = ekran).
    static bool Project(Cam cam, float vw, float vh, Vec3 p, out float sx, out float sy)
    {
        if (cam == null) { sx = p.x; sy = p.y; return true; }
        float wx = p.x, wy = p.y;
        if (cam.Persp)
        {
            float dz = p.z - cam.Eye.z;
            if (dz <= 1e-4f) { sx = sy = 0; return false; }
            float t = -cam.Eye.z / dz;
            wx = cam.Eye.x + (p.x - cam.Eye.x) * t;
            wy = cam.Eye.y + (p.y - cam.Eye.y) * t;
        }
        sx = cam.Rw > 0 ? (wx - cam.Rx) / cam.Rw * vw : 0;
        sy = cam.Rh > 0 ? (wy - cam.Ry) / cam.Rh * vh : 0;
        return true;
    }

    static void Detail(Renderer r, DocNode m)
    {
        switch (r)
        {
            case LayoutBox lb:
                if (lb.Font != null && lb.Text.Length > 0)
                    m.Add("text", DocNode.Scal(lb.Text));
                if (lb.Sprite != null)
                    m.Add("sprite", DocNode.Scal(lb.Sprite.Name ?? ""));
                if (lb.Fill.Visible)
                    m.Add("fill", DocNode.Scal(GradientStr(lb.Fill)));
                break;
            case TextSprite ts:
                if (ts.Text.Length > 0)
                    m.Add("text", DocNode.Scal(ts.Text));
                break;
            case SpriteRenderer sr:
                if (sr.Sprite != null)
                    m.Add("sprite", DocNode.Scal(sr.Sprite.Name ?? ""));
                if (sr.Color.r != 255 || sr.Color.g != 255 || sr.Color.b != 255 || sr.Color.a != 255)
                    m.Add("tint", DocNode.Scal(Hex(sr.Color)));
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
