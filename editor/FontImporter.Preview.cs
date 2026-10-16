using System;
using System.Collections.Generic;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Font importer Inspector onizlemesi (ayarlarin altinda): bilgi satiri, ornek metin (kerning dahil, canli font),
// secili glyph metrikleri, glyph izgarasi (tiklanir; eksik = kirmizi, custom = mavi) ya da ham sheet.
// IMGUI sozlesmesi: kontrol sayisi sabit (text field + 1 buton); izgara tiklamasi Event ile.
// Font App.Assets'ten okunur (reimport yerinde tazeler); rapor "report" artifact'inden (Editor scope).
public sealed partial class FontImporter
{
    const float PvRowH = 20f;
    const float PvSampleH = 56f;
    const float PvCellW = 40f, PvCellH = 46f;
    const float PvGlyphBox = 30f; // hucre icinde glyph'in sigdirildigi kare

    string _pvRel;
    float _pvWidth = 320f;      // son cizim genisligi (PreviewHeight genislik bilmez; bir frame gecikme kabul)
    int _pvSelected = -1;
    bool _pvSheet;
    int _pvObjVersion = -1;
    readonly char[] _pvText = new char[128];
    int _pvTextLen;
    readonly HashSet<int> _pvMissing = new();
    readonly HashSet<int> _pvShared = new();
    readonly HashSet<int> _pvCustom = new();
    readonly HashSet<int> _pvOverridden = new();
    string _pvInfo = "";
    readonly List<string> _pvWarnings = new();

    Font PreviewFont(string rel) => App.Assets?.LoadFont(rel);

    void PreviewBindFont(string rel)
    {
        if (_pvRel != rel)
        {
            _pvRel = rel;
            _pvSelected = -1;
            _pvObjVersion = -1;
            const string sample = "AVAWATo LTy 0123 ÇğİşÖü";
            _pvTextLen = Math.Min(sample.Length, _pvText.Length);
            sample.AsSpan(0, _pvTextLen).CopyTo(_pvText);
        }
        if (_pvObjVersion != AssetDatabase.ObjectVersion)
        {
            _pvObjVersion = AssetDatabase.ObjectVersion;
            ReadReport(rel);
        }
    }

    void ReadReport(string rel)
    {
        _pvMissing.Clear();
        _pvShared.Clear();
        _pvCustom.Clear();
        _pvOverridden.Clear();
        _pvWarnings.Clear();
        _pvInfo = "(not imported)";
        byte[] bytes = ImportPipeline.GetArtifact(rel, "report");
        if (bytes == null)
            return;
        string glyphs = "?", sheet = "?", fill = "?", kernPairs = "0";
        foreach (var raw in Encoding.UTF8.GetString(bytes).Split('\n'))
        {
            int eq = raw.IndexOf('=');
            if (eq <= 0)
                continue;
            string k = raw[..eq], v = raw[(eq + 1)..].Trim();
            switch (k)
            {
                case "glyphs": glyphs = v; break;
                case "sheet": sheet = v; break;
                case "fill": fill = v; break;
                case "kernPairs": kernPairs = v; break;
                case "missing": ParseCps(v, _pvMissing); break;
                case "shared": ParseCps(v, _pvShared); break;
                case "custom": ParseCps(v, _pvCustom); break;
                case "overridden": ParseCps(v, _pvOverridden); break;
                case "warn": _pvWarnings.Add(v); break;
            }
        }
        _pvInfo = $"{glyphs} glyphs  ·  sheet {sheet}  ·  {fill}% filled  ·  {kernPairs} custom kern  ·  {_pvMissing.Count} skipped  ·  {_pvShared.Count} shared  ·  {_pvCustom.Count} svg  ·  {_pvOverridden.Count} overridden";
    }

    static void ParseCps(string list, HashSet<int> into)
    {
        foreach (var t in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (TryGlyphCodepoint(t, out int cp))
                into.Add(cp);
    }

    public override void HidePreview()
    {
        _pvRel = null;
        _pvSelected = -1;
    }

    int GridCols => Math.Max(1, (int)(_pvWidth / PvCellW));

    public override float PreviewHeight(string assetPath)
    {
        var font = PreviewFont(assetPath);
        float h = PvRowH                   // bilgi
                + PvRowH                   // ornek metin alani
                + PvSampleH                // ornek metin cizimi
                + PvRowH                   // secili glyph metrikleri
                + PvRowH;                  // izgara/sheet butonu
        if (font == null)
            return h + PvRowH;
        if (_pvSheet)
        {
            var page = font.Page;
            h += page != null && page.Width > 0 ? _pvWidth * page.Height / page.Width : PvRowH;
        }
        else
        {
            int rows = (font.GlyphCount + GridCols - 1) / GridCols;
            h += rows * PvCellH;
        }
        if (_pvWarnings.Count > 0)
            h += PvRowH * Math.Min(_pvWarnings.Count, 4);
        return h + 4;
    }

    public override void DrawPreview(string assetPath, in Rect rect)
    {
        PreviewBindFont(assetPath);
        _pvWidth = Math.Max(PvCellW, rect.width);
        bool repaint = Event.Current.Type == EventType.Repaint;
        var dim = new Color(160, 168, 182, 255);
        var fg = new Color(225, 228, 235, 255);
        float y = rect.y;
        var font = PreviewFont(assetPath);

        if (repaint)
            GuiRenderer.DrawTextIn(new Rect(rect.x, y, rect.width, PvRowH), _pvInfo, Gui.FontSize - 1, dim);
        y += PvRowH;

        // Ornek metin: alan + canli fontla cizim.
        Gui.TextField(new Rect(rect.x, y + 1, rect.width - 4, PvRowH - 2), _pvText, ref _pvTextLen);
        y += PvRowH;
        var sampleRect = new Rect(rect.x, y, rect.width, PvSampleH);
        if (repaint)
        {
            GuiRenderer.DrawRect(sampleRect, new Color(24, 26, 32, 255), 0);
            if (font != null && font.IsBound)
                DrawSample(font, sampleRect, fg);
            else
                GuiRenderer.DrawTextIn(sampleRect, font == null ? "(font not loaded)" : "(sheet not bound yet)", Gui.FontSize - 1, dim);
        }
        y += PvSampleH;

        // Secili glyph metrikleri.
        if (repaint)
        {
            string line = "click a glyph for metrics";
            if (font != null && _pvSelected >= 0 && _pvSelected < font.GlyphCount)
            {
                ref readonly var g = ref font.GlyphAt(_pvSelected);
                string ch = g.Codepoint >= 0x20 && !(g.Codepoint >= 0xD800 && g.Codepoint <= 0xDFFF) ? char.ConvertFromUtf32(g.Codepoint) : "";
                string tag = (_pvCustom.Contains(g.Codepoint) ? " [svg]" : _pvMissing.Contains(g.Codepoint) ? " [MISSING in ttf]" : "")
                    + (_pvShared.Contains(g.Codepoint) ? " [shared region]" : "")
                    + (_pvOverridden.Contains(g.Codepoint) ? " [override]" : "");
                float em = font.SdfSize > 0 ? 1f / font.SdfSize : 0f;
                line = $"U+{g.Codepoint:X4} '{ch}'{tag}  adv {g.Advance:0.#}px = {g.Advance * em:0.###}em  off {g.XOff:0.#},{g.YOff:0.#}px  {g.W}x{g.H}  ink {g.InkL:0.#}..{g.InkR:0.#}px ({g.InkL * em:0.##}..{g.InkR * em:0.##}em)";
            }
            GuiRenderer.DrawTextIn(new Rect(rect.x, y, rect.width, PvRowH), line, Gui.FontSize - 1, dim);
        }
        y += PvRowH;

        if (Gui.Button(new Rect(rect.x, y + 1, 90, PvRowH - 2), _pvSheet ? "Show grid" : "Show sheet"))
            _pvSheet = !_pvSheet;
        if (repaint && font != null)
            GuiRenderer.DrawTextIn(new Rect(rect.x + 96, y, rect.width - 96, PvRowH),
                $"ascent {font.Ascent:0.#}  descent {font.Descent:0.#}  line {font.LineHeight:0.#}", Gui.FontSize - 1, dim);
        y += PvRowH;

        if (font == null)
        {
            if (repaint)
                GuiRenderer.DrawTextIn(new Rect(rect.x, y, rect.width, PvRowH), "(not imported)", Gui.FontSize - 1, dim);
            return;
        }

        if (_pvSheet)
        {
            var page = font.Page;
            if (page != null && page.Width > 0)
            {
                float h = rect.width * page.Height / page.Width;
                var r = new Rect(rect.x, y, rect.width, h);
                if (repaint)
                {
                    GuiRenderer.DrawRect(r, new Color(24, 26, 32, 255), 0);
                    // CPU'dan yuklenen sayfa: satir 0 ustte; GUI quad ust kenari <-> v1 -> v0=1, v1=0 (DrawTextureBlended RT icindir).
                    _pvSheetMaterial ??= new Material { SortMode = SortMode.Ui };
                    _pvSheetMaterial.MainTexture = page;
                    GuiRenderer.DrawRegion(r, _pvSheetMaterial, Color.White, 0f, 1f, 1f, 0f, 1);
                }
                y += h;
            }
        }
        else
            DrawGrid(font, rect.x, ref y, rect.width);

        if (repaint)
            for (int i = 0; i < _pvWarnings.Count && i < 4; i++, y += PvRowH)
                GuiRenderer.DrawTextIn(new Rect(rect.x, y, rect.width, PvRowH), "! " + _pvWarnings[i], Gui.FontSize - 1, new Color(235, 170, 90, 255));
    }

    // Glyph quad'i: SDF px -> ekran, V takasi (sayfada satir 0 ustte; TextSprite ile ayni).
    // Kendi materyali (SortMode.Ui; Font.Material Transparent sirali — GUI katmanina uymaz), shader ortak UiShader.
    Material _pvMaterial;
    Material _pvSheetMaterial; // ham sayfa (duz RGBA, varsayilan shader)

    void DrawGlyph(Font font, in Font.Glyph g, float x, float baseY, float s, Color color)
    {
        var sp = g.Sprite;
        if (g.W <= 0 || g.H <= 0 || sp == null || sp.Page == null)
            return;
        _pvMaterial ??= new Material { SortMode = SortMode.Ui, Shader = UiPieces.UiShader };
        _pvMaterial.MainTexture = sp.Page;
        var r = new Rect(x + g.XOff * s, baseY + g.YOff * s, g.W * s, g.H * s);
        GuiRenderer.DrawRegion(r, _pvMaterial, color, sp.U0, sp.V1, sp.U1, sp.V0, 2);
    }

    void DrawSample(Font font, in Rect area, Color color)
    {
        float s = (area.height - 12f) / font.SdfSize;
        // Genis metin alana sigmiyorsa olcek dusurulur (tasima yerine tum ornek gorunsun).
        float lineW = font.MeasureLine(_pvText.AsSpan(0, _pvTextLen));
        if (lineW * s > area.width - 16f && lineW > 0)
            s = (area.width - 16f) / lineW;
        float baseY = area.y + area.height * 0.5f + (font.Ascent + font.Descent) * 0.5f * s;
        float pen = area.x + 8f;
        int prev = -1;
        for (int i = 0; i < _pvTextLen; i++)
        {
            int cp = _pvText[i];
            if (char.IsHighSurrogate(_pvText[i]) && i + 1 < _pvTextLen)
                cp = char.ConvertToUtf32(_pvText[i], _pvText[++i]);
            int gi = font.GlyphIndex(cp);
            if (prev >= 0)
                pen += font.Kerning(prev, gi) * s;
            ref readonly var g = ref font.GlyphAt(gi);
            DrawGlyph(font, in g, pen, baseY, s, color);
            pen += g.Advance * s;
            prev = gi;
            if (pen > area.x + area.width)
                break;
        }
    }

    void DrawGrid(Font font, float x0, ref float y, float width)
    {
        int cols = GridCols;
        int n = font.GlyphCount;
        int rows = (n + cols - 1) / cols;
        var gridRect = new Rect(x0, y, cols * PvCellW, rows * PvCellH);
        var ev = Event.Current;
        if (ev.Type == EventType.MouseDown && ev.Button == 0 && gridRect.Contains(ev.MousePosition))
        {
            int c = (int)((ev.MousePosition.x - x0) / PvCellW), r = (int)((ev.MousePosition.y - y) / PvCellH);
            int idx = r * cols + c;
            _pvSelected = idx >= 0 && idx < n ? idx : -1;
            ev.Use();
        }
        if (ev.Type != EventType.Repaint)
        {
            y += rows * PvCellH;
            return;
        }
        var cellBg = new Color(30, 33, 40, 255);
        var cellBgAlt = new Color(36, 39, 47, 255);
        var missingC = new Color(200, 70, 70, 255);
        var customC = new Color(80, 140, 230, 255);
        var overC = new Color(90, 190, 110, 255);
        var selC = new Color(240, 200, 80, 255);
        var ink = new Color(235, 238, 243, 255);
        var label = new Color(130, 138, 150, 255);
        float gs = PvGlyphBox / font.SdfSize * 0.9f; // em-orantili; sigmayan kucultulur
        for (int i = 0; i < n; i++)
        {
            int c = i % cols, r = i / cols;
            var cell = new Rect(x0 + c * PvCellW, y + r * PvCellH, PvCellW - 1, PvCellH - 1);
            GuiRenderer.DrawRect(cell, ((c + r) & 1) == 0 ? cellBg : cellBgAlt, 0);
            ref readonly var g = ref font.GlyphAt(i);
            bool miss = _pvMissing.Contains(g.Codepoint), cust = _pvCustom.Contains(g.Codepoint), over = _pvOverridden.Contains(g.Codepoint);
            if (i == _pvSelected || miss || cust || over)
            {
                var bc = i == _pvSelected ? selC : miss ? missingC : cust ? customC : overC;
                GuiRenderer.DrawRect(new Rect(cell.x, cell.y, cell.width, 2), bc, 1);
                GuiRenderer.DrawRect(new Rect(cell.x, cell.y + cell.height - 2, cell.width, 2), bc, 1);
                GuiRenderer.DrawRect(new Rect(cell.x, cell.y, 2, cell.height), bc, 1);
                GuiRenderer.DrawRect(new Rect(cell.x + cell.width - 2, cell.y, 2, cell.height), bc, 1);
            }
            if (g.W > 0 && g.H > 0)
            {
                float s = gs;
                float m = Math.Max(g.W, g.H) * s;
                if (m > PvGlyphBox)
                    s *= PvGlyphBox / m;
                // Murekkep kutusunu hucrenin glyph alaninda ortala (baseline degil: ikonlar da ortali gorunsun).
                float bx = cell.x + (PvCellW - g.W * s) * 0.5f - g.XOff * s;
                float by = cell.y + 3f + (PvGlyphBox - g.H * s) * 0.5f - g.YOff * s;
                DrawGlyph(font, in g, bx, by, s, ink);
            }
            GuiRenderer.DrawTextIn(new Rect(cell.x, cell.y + PvGlyphBox + 2, cell.width, 12),
                g.Codepoint <= 0xFFFF ? g.Codepoint.ToString("X4") : g.Codepoint.ToString("X5"), 8f, label, true, 2);
        }
        y += rows * PvCellH;
    }
}
