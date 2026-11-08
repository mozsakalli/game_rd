using System;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Unity benzeri Sprite Editor: gorselin 9-slice kenarlarini (ImageImportSettings.border, kaynak piksel)
// resim uzerinde surukleyerek / sayi alanindan duzenler. Deger RESMIN ozelligidir (.meta), sahneye degil:
// Apply = meta'ya yaz -> reimport -> canli Sprite.Border tazelenir -> atlas grubu yeniden paketlenir.
// Kontrol sayisi sabit: 4 kilavuz tutamaci + 4 sayi alani + 3 buton her frame kaydedilir.
public sealed class SpriteEditorPopup : EditorPopup
{
    const float W = 640f, H = 560f;
    const float ToolbarH = 56f, Pad = 10f;
    const float Grab = 5f; // kilavuz yakalama yari genisligi (mantiksal px)

    static readonly Color GuideColor = new(80, 230, 110, 255);
    static readonly Color GuideHot = new(255, 240, 120, 255);
    static readonly Color CheckerA = new(64, 66, 74, 255);
    static readonly Color CheckerB = new(84, 86, 96, 255);
    static readonly Color TextColor = new(220, 224, 232, 255);
    static readonly Color DimText = new(150, 156, 170, 255);
    static readonly int _guideHash = "SpriteEditor.Guide".GetHashCode();

    static SpriteEditorPopup _instance;

    // rel: Assets'e goreli png/jpg yolu.
    public static void Show(string rel)
    {
        if (string.IsNullOrEmpty(rel) || App.Assets == null)
            return;
        _instance ??= new SpriteEditorPopup();
        var p = _instance;
        p._rel = rel;
        p._settings = ImportPipeline.LoadSettings(rel) as ImageImportSettings ?? new ImageImportSettings();
        p._l = Round(p._settings.border.x); p._t = Round(p._settings.border.y);
        p._r = Round(p._settings.border.z); p._b = Round(p._settings.border.w);
        p._saved = (p._l, p._t, p._r, p._b);
        p._tex = App.Assets.LoadTexture(rel);
        p._dragStart = 0;
        p.Open(W, H);
    }

    static int Round(float v) => (int)MathF.Round(Math.Max(0, v));

    string _rel;
    ImageImportSettings _settings;
    Texture _tex;
    int _l, _t, _r, _b;
    (int l, int t, int r, int b) _saved;
    int _dragStart; // surukleme baslangic degeri (hot kilavuz)
    float _dragMouse;

    bool Dirty => (_l, _t, _r, _b) != _saved;

    protected override void OnGui(Event ev)
    {
        bool repaint = ev.Type == EventType.Repaint;
        bool ready = _tex != null && _tex.Width > 0 && _tex.Height > 0 && !_tex.Loading;
        int iw = ready ? _tex.Width : 0, ih = ready ? _tex.Height : 0;

        // --- Ust cubuk: baslik + L/T/R/B alanlari + butonlar ---
        if (repaint)
        {
            string title = System.IO.Path.GetFileName(_rel) + (ready ? $"  {iw}x{ih}" : "  (loading)") + (Dirty ? "  *" : "");
            GuiRenderer.DrawTextIn(new Rect(Pad, 6, W - 2 * Pad, 18), title, Gui.FontSize - 1, TextColor, false, 2);
        }
        float fy = 28, fx = Pad;
        _l = Field(ref fx, fy, "L", _l, Math.Max(0, iw - _r));
        _t = Field(ref fx, fy, "T", _t, Math.Max(0, ih - _b));
        _r = Field(ref fx, fy, "R", _r, Math.Max(0, iw - _l));
        _b = Field(ref fx, fy, "B", _b, Math.Max(0, ih - _t));
        bool revert = Gui.Button(new Rect(W - 3 * 66 - Pad, fy, 60, 22), "Revert");
        bool apply = Gui.Button(new Rect(W - 2 * 66 - Pad, fy, 60, 22), Dirty ? "Apply" : "Applied");
        if (Gui.Button(new Rect(W - 66 - Pad, fy, 60, 22), "Close"))
        {
            Close();
            return;
        }
        if (revert && Dirty)
            (_l, _t, _r, _b) = _saved;
        else if (apply && Dirty)
            Apply();

        // --- Resim alani: sigdir (buyutme yok: 1:1 veya kucult), damali zemin ---
        var area = new Rect(Pad, ToolbarH, W - 2 * Pad, H - ToolbarH - Pad);
        if (repaint)
            DrawChecker(area);
        if (!ready)
        {
            if (repaint)
                GuiRenderer.DrawTextIn(area, "(image not loaded)", Gui.FontSize - 1, DimText, true, 2);
            // kontrol sayisi sabit kalsin: tutamaclar yine kaydedilir
            for (int i = 0; i < 4; i++) GuiUtility.GetControlID(_guideHash, FocusType.Passive);
            return;
        }
        float scale = MathF.Min(1f, MathF.Min(area.width / iw, area.height / ih));
        float dw = iw * scale, dh = ih * scale;
        var img = new Rect(area.x + (area.width - dw) * 0.5f, area.y + (area.height - dh) * 0.5f, dw, dh);
        if (repaint)
        {
            GuiRenderer.DrawTextureBlended(img, _tex, 1); // flip'li yuklenen dokuda DrawTexture V yonu dogru
        }

        // Kilavuzlar: kaynak piksel -> ekran. Sira sabit (L,T,R,B) -> id'ler sabit.
        float gl = img.x + _l * scale, gr = img.xMax - _r * scale;
        float gt = img.y + _t * scale, gb = img.yMax - _b * scale;
        _l = Guide(ev, vertical: true, pos: gl, origin: img.x, dir: +1, scale, _l, Math.Max(0, iw - _r), in img, out bool hotL);
        _t = Guide(ev, vertical: false, pos: gt, origin: img.y, dir: +1, scale, _t, Math.Max(0, ih - _b), in img, out bool hotT);
        _r = Guide(ev, vertical: true, pos: gr, origin: img.xMax, dir: -1, scale, _r, Math.Max(0, iw - _l), in img, out bool hotR);
        _b = Guide(ev, vertical: false, pos: gb, origin: img.yMax, dir: -1, scale, _b, Math.Max(0, ih - _t), in img, out bool hotB);

        if (repaint)
        {
            gl = img.x + _l * scale; gr = img.xMax - _r * scale;
            gt = img.y + _t * scale; gb = img.yMax - _b * scale;
            GuiRenderer.DrawRect(new Rect(gl, img.y, 1, img.height), hotL ? GuideHot : GuideColor, 2);
            GuiRenderer.DrawRect(new Rect(gr - 1, img.y, 1, img.height), hotR ? GuideHot : GuideColor, 2);
            GuiRenderer.DrawRect(new Rect(img.x, gt, img.width, 1), hotT ? GuideHot : GuideColor, 2);
            GuiRenderer.DrawRect(new Rect(img.x, gb - 1, img.width, 1), hotB ? GuideHot : GuideColor, 2);
            string info = $"scale {scale:0.##}x   center {Math.Max(0, iw - _l - _r)}x{Math.Max(0, ih - _t - _b)} px";
            GuiRenderer.DrawTextIn(new Rect(Pad, H - Pad - 16, W - 2 * Pad, 16), info, Gui.FontSize - 2, DimText, false, 2);
        }
    }

    // Etiketli tam sayi alani; ilerleyen x.
    static int Field(ref float x, float y, string label, int value, int max)
    {
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(x, y, 14, 22), label, Gui.FontSize - 1, DimText, false, 2);
        int v = Gui.DragInt(new Rect(x + 14, y, 54, 22), value, 0.2f, 0, Math.Max(0, max));
        x += 14 + 54 + 10;
        return v;
    }

    // Tek kilavuz tutamaci: pos ekrandaki cizgi; origin kenar; dir +1 (sol/ust'ten iceri) / -1 (sag/alt'tan iceri).
    int Guide(Event ev, bool vertical, float pos, float origin, int dir, float scale, int value, int max,
        in Rect img, out bool hot)
    {
        int id = GuiUtility.GetControlID(_guideHash, FocusType.Passive);
        hot = GuiUtility.HotControl == id;
        var m = ev.MousePosition;
        float along = vertical ? m.x : m.y;
        bool near = vertical
            ? MathF.Abs(m.x - pos) <= Grab && m.y >= img.y && m.y <= img.yMax
            : MathF.Abs(m.y - pos) <= Grab && m.x >= img.x && m.x <= img.xMax;
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (ev.Button == 0 && near && GuiUtility.HotControl == 0)
                {
                    GuiUtility.HotControl = id;
                    _dragStart = value;
                    _dragMouse = along;
                    hot = true;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (hot)
                {
                    ev.Use();
                    int v = _dragStart + (int)MathF.Round((along - _dragMouse) * dir / scale);
                    return Math.Clamp(v, 0, Math.Max(0, max));
                }
                break;
            case EventType.MouseUp:
                if (hot)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                if (near || hot)
                    GuiCursorManager.Request(vertical ? GuiCursor.ResizeH : GuiCursor.ResizeV);
                break;
        }
        return value;
    }

    static void DrawChecker(in Rect r)
    {
        const float cell = 12f;
        GuiRenderer.DrawRect(r, CheckerA, 0);
        int nx = (int)MathF.Ceiling(r.width / cell), ny = (int)MathF.Ceiling(r.height / cell);
        for (int y = 0; y < ny; y++)
            for (int x = (y & 1); x < nx; x += 2)
            {
                float cx = r.x + x * cell, cy = r.y + y * cell;
                GuiRenderer.DrawRect(new Rect(cx, cy, MathF.Min(cell, r.xMax - cx), MathF.Min(cell, r.yMax - cy)), CheckerB, 0);
            }
    }

    void Apply()
    {
        _settings.border = new Vec4(_l, _t, _r, _b);
        ImportPipeline.SaveSettings(_rel, _settings);
        ImportPipeline.EnsureImported(_rel);
        App.Assets.InvalidateImported(_rel);
        AtlasSystem.RefreshAll();
        InspectorPanel.ReloadAssetSettings(_rel);
        _saved = (_l, _t, _r, _b);
    }

    protected override void OnClosing()
    {
        GuiUtility.HotControl = 0;
        GuiUtility.KeyboardControl = 0;
    }
}
