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

    [RpcCommand("ui.screenshot", "Editor penceresinin tamamini PNG yazar (GUI dahil). scale: mantiksal boyuta carpan (1 = tiklama koordinatiyla birebir). path bos = Library/Screenshots/ui_N.png")]
    static DocNode Screenshot(string path = "", float scale = 1f)
    {
        if (scale <= 0f || scale > 4f)
            throw new RpcError("scale 0..4 araliginda olmali");
        if (path.Length == 0)
            path = Path.Combine("Library", "Screenshots", $"ui_{++_shotSeq:D4}.png");
        string full = Path.IsPathRooted(path) ? path : Path.Combine(App.Project.Root, path);
        UiAutomation.RequestScreenshot(full, scale);
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

    [RpcCommand("ui.click", "Mantiksal (x,y)'ye tiklar (button 0 sol/1 sag, clicks 2 = cift tik, mods 'ctrl+shift'). Acik combo listesi (popup) varsa icine dusen tik ona gider")]
    static DocNode Click(float x, float y, int button = 0, int clicks = 1, string mods = "")
    {
        UiAutomation.Click(x, y, button, clicks, UiAutomation.Mods(mods));
        return null;
    }

    [RpcCommand("ui.drag", "Fareyle surukler: (x0,y0) -> (x1,y1), steps ara adim")]
    static DocNode Drag(float x0, float y0, float x1, float y1, int steps = 8, int button = 0)
    {
        UiAutomation.Drag(x0, y0, x1, y1, steps, button);
        return null;
    }

    [RpcCommand("ui.scroll", "Tekerlek: (x,y) uzerinde dy (pozitif = yukari)")]
    static DocNode Scroll(float x, float y, float dy)
    {
        UiAutomation.Scroll(x, y, dy);
        return null;
    }

    [RpcCommand("ui.type", "Odaktaki metin alanina yazar (once ui.click ile alana tikla); '\\n' = Enter")]
    static DocNode Type(string text)
    {
        UiAutomation.Type(text ?? "");
        return null;
    }

    [RpcCommand("ui.key", "Tus basar: key 'enter|esc|tab|backspace|delete|left|right|up|down|home|end|space|a..z|0..9|f1..f12', mods 'ctrl+shift+alt' (Ctrl+Z = undo, Ctrl+A = tumunu sec)")]
    static DocNode Key(string key, string mods = "")
    {
        int code = UiAutomation.KeyCode(key);
        if (code < 0)
            throw new RpcError("bilinmeyen tus: " + key);
        UiAutomation.Key(code, UiAutomation.Mods(mods));
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
