using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// RPC ui.*: editor GUI otomasyonu (UiAutomation). Koordinat = ana pencere MANTIKSAL px, sol-ust orijin;
// ui.screenshot(scale=1) pikseli == tiklama koordinati. Jest adimlari frame'e yayilir; ui.click'ten sonraki
// komut jest bitince kosar (pompa Busy'de bekler) — ayri "bekle" gerekmez.
static class RpcUi
{
    static int _shotSeq;

    // window parametresi: "" = ana pencere; aksi halde acik utility/detached pencere basligi (ui.windows). Koordinatlar o
    // pencerenin mantiksal px'i. Hedef cozulemiyorsa hata (sessizce ana pencereye dusmez).
    static NativeWindow Target(string window)
    {
        if (string.IsNullOrEmpty(window)) { UiAutomation.SetTarget(IntPtr.Zero); return null; }
        if (!GuiDock.TryFindWindow(window, out var w))
            throw new RpcError("acik pencere yok: '" + window + "' (ui.windows ile listele; ui.menu ile ac)");
        UiAutomation.SetTarget(w.Window.Handle);
        return w.Window;
    }

    [RpcCommand("ui.menu", "Menu ogesini yoluyla calistirir ('Project/Player Settings'); path bos = tum [MenuItem] yollarini listeler. Utility pencereler boylece acilir; sonra ui.windows + window parametresi")]
    static DocNode Menu(string path = "")
    {
        if (path.Length == 0)
        {
            var s = DocNode.Seq();
            foreach (var p in EditorMenu.Paths) s.Items.Add(DocNode.Scal(p));
            var r = DocNode.Map();
            r.Add("items", s);
            return r;
        }
        if (!EditorMenu.Invoke(path))
            throw new RpcError("menu yolu yok: '" + path + "' (ui.menu() ile listele)");
        // Menu Poll'de (sonraki frame basi) kosar; bir frame bekle ki pencere acilmis olsun.
        int until = RpcHost.FrameCount + 2;
        RpcHost.Defer(() => RpcHost.FrameCount >= until ? DocNode.Map() : null);
        return null;
    }

    [RpcCommand("ui.windows", "Acik utility/detached pencereler: {title, w, h} (mantiksal). ui.screenshot/click/type/key/drag/scroll window=title ile hedeflenir")]
    static DocNode Windows()
    {
        var list = new List<GuiDock.WindowInfo>();
        GuiDock.ListWindows(list);
        var s = DocNode.Seq();
        foreach (var w in list)
        {
            var m = DocNode.Map();
            m.Add("title", DocNode.Scal(w.Title));
            m.Add("w", DocNode.Scal(F(w.W)));
            m.Add("h", DocNode.Scal(F(w.H)));
            s.Items.Add(m);
        }
        var r = DocNode.Map();
        r.Add("windows", s);
        return r;
    }

    [RpcCommand("ui.panels", "Ana penceredeki dock'lu paneller: {title, tab:{x,y,w,h}, content:{x,y,w,h}, active} + pencere boyutu (mantiksal). Tiklanacak yeri bulmak icin")]
    static DocNode Panels()
    {
        var list = new List<GuiDock.PanelInfo>();
        GuiDock.ListPanels(list);
        var s = DocNode.Seq();
        foreach (var p in list)
        {
            var m = DocNode.Map();
            m.Add("title", DocNode.Scal(p.Title));
            m.Add("tab", RectNode(p.Tab));
            m.Add("content", RectNode(p.Content));
            m.Add("active", DocNode.Scal(p.Active ? "true" : "false"));
            s.Items.Add(m);
        }
        var r = DocNode.Map();
        r.Add("w", DocNode.Scal(App.ScreenW.ToString(CultureInfo.InvariantCulture)));
        r.Add("h", DocNode.Scal(App.ScreenH.ToString(CultureInfo.InvariantCulture)));
        r.Add("toolbarH", DocNode.Scal(App.ToolbarHeight.ToString(CultureInfo.InvariantCulture)));
        r.Add("panels", s);
        r.Add("popupOpen", DocNode.Scal(GuiPopup.Active ? "true" : "false"));
        return r;
    }

    static DocNode RectNode(in Rect r)
    {
        var m = DocNode.Map();
        m.Add("x", DocNode.Scal(F(r.x)));
        m.Add("y", DocNode.Scal(F(r.y)));
        m.Add("w", DocNode.Scal(F(r.width)));
        m.Add("h", DocNode.Scal(F(r.height)));
        return m;
    }

    static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    [RpcCommand("ui.screenshot", "Editor penceresinin tamamini PNG yazar (GUI dahil). scale: mantiksal boyuta carpan (1 = tiklama koordinatiyla birebir). path bos = Library/Screenshots/ui_N.png. window: utility/detached pencere basligi (ui.windows)")]
    static DocNode Screenshot(string path = "", float scale = 1f, string window = "")
    {
        if (scale <= 0f || scale > 4f)
            throw new RpcError("scale 0..4 araliginda olmali");
        var win = Target(window);
        if (path.Length == 0)
            path = Path.Combine("Library", "Screenshots", $"ui_{++_shotSeq:D4}.png");
        string full = Path.IsPathRooted(path) ? path : Path.Combine(App.Project.Root, path);
        UiAutomation.RequestScreenshot(full, scale, win);
        RpcHost.Defer(() =>
        {
            var res = UiAutomation.TakeShotResult(out string err);
            if (err != null)
                throw new RpcError(err);
            if (res == null)
                return null;
            var r = DocNode.Map();
            r.Add("path", DocNode.Scal(Path.GetFullPath(full)));
            r.Add("w", DocNode.Scal(res.Value.w.ToString(CultureInfo.InvariantCulture)));
            r.Add("h", DocNode.Scal(res.Value.h.ToString(CultureInfo.InvariantCulture)));
            r.Add("scale", DocNode.Scal(F(scale)));
            return r;
        });
        return null;
    }

    [RpcCommand("ui.click", "Mantiksal (x,y)'ye tiklar (button 0 sol/1 sag, clicks 2 = cift tik, mods 'ctrl+shift'). Acik combo listesi (popup) varsa icine dusen tik ona gider. window: hedef pencere basligi")]
    static DocNode Click(float x, float y, int button = 0, int clicks = 1, string mods = "", string window = "")
    {
        Target(window);
        UiAutomation.Click(x, y, button, clicks, UiAutomation.Mods(mods));
        return null;
    }

    [RpcCommand("ui.drag", "Fareyle surukler: (x0,y0) -> (x1,y1), steps ara adim. window: hedef pencere basligi")]
    static DocNode Drag(float x0, float y0, float x1, float y1, int steps = 8, int button = 0, string window = "")
    {
        Target(window);
        UiAutomation.Drag(x0, y0, x1, y1, steps, button);
        return null;
    }

    [RpcCommand("ui.scroll", "Tekerlek: (x,y) uzerinde dy (pozitif = yukari). window: hedef pencere basligi")]
    static DocNode Scroll(float x, float y, float dy, string window = "")
    {
        Target(window);
        UiAutomation.Scroll(x, y, dy);
        return null;
    }

    [RpcCommand("ui.type", "Odaktaki metin alanina yazar (once ui.click ile alana tikla); '\\n' = Enter. window: hedef pencere basligi")]
    static DocNode Type(string text, string window = "")
    {
        Target(window);
        UiAutomation.Type(text ?? "");
        return null;
    }

    [RpcCommand("ui.key", "Tus basar: key 'enter|esc|tab|backspace|delete|left|right|up|down|home|end|space|a..z|0..9|f1..f12', mods 'ctrl+shift+alt' (Ctrl+Z = undo, Ctrl+A = tumunu sec). window: hedef pencere basligi")]
    static DocNode Key(string key, string mods = "", string window = "")
    {
        int code = UiAutomation.KeyCode(key);
        if (code < 0)
            throw new RpcError("bilinmeyen tus: " + key);
        Target(window);
        UiAutomation.Key(code, UiAutomation.Mods(mods));
        return null;
    }

    [RpcCommand("ui.fields", "Inspector'da su an cizili alanlar: {path, kind asset|list, type, rect 'x y w h' (mantiksal)}. ui.dropAsset hedefi icin yol buradan")]
    static DocNode Fields()
    {
        var seq = DocNode.Seq();
        foreach (var e in FieldRegistry.Current)
        {
            var m = DocNode.Map();
            m.Add("path", DocNode.Scal(e.Path));
            m.Add("kind", DocNode.Scal(e.Kind));
            m.Add("type", DocNode.Scal(e.Type));
            m.Add("rect", DocNode.Scal(FormattableString.Invariant($"{e.Rect.x:0.#} {e.Rect.y:0.#} {e.Rect.width:0.#} {e.Rect.height:0.#}")));
            seq.Items.Add(m);
        }
        var r = DocNode.Map();
        r.Add("count", DocNode.Scal(seq.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        r.Add("fields", seq);
        return r;
    }

    [RpcCommand("ui.dropAsset", "SEMANTIK surukle-birak (koordinat yok): asset (yol/guid, alt oge 'x.psb#id' olabilir) Inspector alanina birakilir. field: ui.fields yolu — liste basligi ('a:sources') sona ekler, eleman ('a:sources[0]') atar. Sonuc: {dropped}", Mutates = true)]
    static DocNode DropAsset(string asset, string field)
    {
        var assets = App.Assets ?? throw new RpcError("proje yok");
        string rel = assets.ResolvePath(asset.Replace('\\', '/'));
        string owner = SubAssets.Split(rel, out string id);
        string guid = assets.PathToGuid(owner) ?? throw new RpcError("asset yok: " + asset);
        if (id != null && !SubAssets.TryGetEntry(owner, id, out _))
            throw new RpcError($"alt oge yok: {owner}#{id}");
        if (!FieldRegistry.TryFind(field, out var target))
            throw new RpcError($"alan cizili degil: '{field}' (ui.fields ile listele; Inspector'da dogru nesne secili mi?)");
        string guidKey = id == null ? guid : guid + "#" + id;
        UiAutomation.DropAsset(guidKey, rel, target.Rect);
        // Jest bitince sonuc: hedef kabul ettiyse DragDrop temizlenmis ve yuk uygulanmis olur.
        RpcHost.Defer(() =>
        {
            if (UiAutomation.Busy) return null;
            var m = DocNode.Map();
            m.Add("dropped", DocNode.Scal(UiAutomation.LastDropApplied ? "true" : "false"));
            m.Add("field", DocNode.Scal(target.Path));
            return m;
        });
        return null;
    }

    [RpcCommand("ui.wait", "N frame bekler (animasyon/yeniden cizim icin)")]
    static DocNode Wait(int frames = 2)
    {
        int until = RpcHost.FrameCount + Math.Clamp(frames, 1, 600);
        RpcHost.Defer(() => RpcHost.FrameCount >= until ? DocNode.Map() : null);
        return null;
    }
}
