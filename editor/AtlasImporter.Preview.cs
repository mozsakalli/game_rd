using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Atlas grubu Inspector onizlemesi (ayarlarin altinda): bilgi satiri (bolge/sayfa/boyut/doluluk), sayfa gezinme,
// uretilen sayfa gorseli (importer artifact'i; AssetDatabase'in yuklu tablosundan — reimport yerinde tazeler) ve
// bolge cerceveleri (Color = yesil, Sdf = camgobegi; trim'li bolgede mantiksal kutu kesikli degil, soluk ikinci cerceve).
// Fare bolgenin ustundeyse adi + boyut/trim bilgisi alt satirda. IMGUI sozlesmesi: kontrol sayisi sabit (3 buton).
public sealed partial class AtlasImporter
{
    const float PvRowH = 20f;

    string _pvRel;
    int _pvPage;
    bool _pvRegions = true;
    float _pvWidth = 320f; // son cizim genisligi (PreviewHeight genislik bilmez; bir frame gecikme kabul)
    int _pvObjVersion = -1;
    string _pvInfo = "";
    Material _pvMaterial;

    static readonly Color PvBg = new(24, 26, 32, 255);
    static readonly Color PvColorRegion = new(90, 200, 110, 200);
    static readonly Color PvSdfRegion = new(80, 190, 220, 170);
    static readonly Color PvLogical = new(200, 200, 90, 110);
    static readonly Color PvHover = new(255, 255, 255, 255);

    bool PreviewTable(string rel, out IReadOnlyList<Texture> pages, out IReadOnlyList<SpriteTable.Region> regions)
    {
        pages = null; regions = null;
        return App.Assets != null && App.Assets.TryGetSpriteTable(rel, out pages, out regions) && pages.Count > 0;
    }

    void PreviewBindAtlas(string rel)
    {
        if (_pvRel != rel)
        {
            _pvRel = rel;
            _pvPage = 0;
            _pvObjVersion = -1;
        }
        if (_pvObjVersion == AssetDatabase.ObjectVersion)
            return;
        _pvObjVersion = AssetDatabase.ObjectVersion;
        if (!PreviewTable(rel, out var pages, out var regions))
        {
            _pvInfo = "(not imported)";
            return;
        }
        _pvPage = Math.Clamp(_pvPage, 0, pages.Count - 1);
        long pageArea = 0, used = 0, trimmed = 0;
        int color = 0, sdf = 0;
        foreach (var p in pages) pageArea += (long)p.Width * p.Height;
        foreach (var r in regions)
        {
            used += (long)r.W * r.H;
            trimmed += (long)r.OrigW * r.OrigH - (long)r.W * r.H;
            if (r.Kind == RegionKind.Sdf) sdf++; else color++;
        }
        int fill = pageArea > 0 ? (int)(used * 100 / pageArea) : 0;
        string size = pages.Count == 1 ? $"{pages[0].Width}x{pages[0].Height}" : $"{pages.Count} pages";
        _pvInfo = $"{regions.Count} regions ({color} color, {sdf} sdf)  ·  {size}  ·  {fill}% filled  ·  trim saved {trimmed / 1024} Kpx";
    }

    public override void HidePreview()
    {
        _pvRel = null;
    }

    public override float PreviewHeight(string assetPath)
    {
        float h = PvRowH * 2; // bilgi + butonlar
        if (PreviewTable(assetPath, out var pages, out _))
        {
            var page = pages[Math.Clamp(_pvPage, 0, pages.Count - 1)];
            h += page.Width > 0 ? _pvWidth * page.Height / page.Width : PvRowH;
            h += PvRowH; // hover satiri
        }
        else h += PvRowH;
        return h + 4;
    }

    public override void DrawPreview(string assetPath, in Rect rect)
    {
        PreviewBindAtlas(assetPath);
        _pvWidth = Math.Max(64f, rect.width);
        bool repaint = Event.Current.Type == EventType.Repaint;
        var dim = new Color(160, 168, 182, 255);
        float y = rect.y;
        bool has = PreviewTable(assetPath, out var pages, out var regions);

        if (repaint)
            GuiRenderer.DrawTextIn(new Rect(rect.x, y, rect.width, PvRowH), _pvInfo, Gui.FontSize - 1, dim);
        y += PvRowH;

        // Kontroller: sayfa gezinme (tek sayfada etkisiz) + bolge cerceveleri. Sayi sabit.
        int pageCount = has ? pages.Count : 0;
        if (Gui.Button(new Rect(rect.x, y + 1, 28, PvRowH - 2), "<") && _pvPage > 0) _pvPage--;
        if (Gui.Button(new Rect(rect.x + 32, y + 1, 28, PvRowH - 2), ">") && _pvPage < pageCount - 1) _pvPage++;
        if (Gui.Button(new Rect(rect.x + 68, y + 1, 90, PvRowH - 2), _pvRegions ? "Hide regions" : "Show regions"))
            _pvRegions = !_pvRegions;
        if (repaint && has)
            GuiRenderer.DrawTextIn(new Rect(rect.x + 164, y, rect.width - 164, PvRowH),
                $"page {_pvPage + 1}/{pageCount}  {pages[_pvPage].Width}x{pages[_pvPage].Height}", Gui.FontSize - 1, dim);
        y += PvRowH;

        if (!has)
        {
            if (repaint)
                GuiRenderer.DrawTextIn(new Rect(rect.x, y, rect.width, PvRowH), "(not imported / not loaded)", Gui.FontSize - 1, dim);
            return;
        }

        _pvPage = Math.Clamp(_pvPage, 0, pageCount - 1);
        var page = pages[_pvPage];
        float imgH = page.Width > 0 ? rect.width * page.Height / page.Width : PvRowH;
        var img = new Rect(rect.x, y, rect.width, imgH);
        float sx = page.Width > 0 ? img.width / page.Width : 0f, sy = page.Height > 0 ? img.height / page.Height : 0f;
        y += imgH;

        // Fare altindaki bolge.
        var mouse = Event.Current.MousePosition;
        int hover = -1;
        if (img.Contains(mouse))
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (r.Page != _pvPage) continue;
                var rr = RegionRect(in img, page, in r, sx, sy, false);
                if (rr.Contains(mouse)) { hover = i; break; }
            }

        if (repaint)
        {
            GuiRenderer.DrawRect(img, PvBg, 0);
            _pvMaterial ??= new Material { SortMode = SortMode.Ui };
            _pvMaterial.MainTexture = page;
            GuiRenderer.DrawRegion(img, _pvMaterial, Color.White, 0f, 1f, 1f, 0f, 1); // v: ust = 1 (satir 0 altta)
            if (_pvRegions)
                for (int i = 0; i < regions.Count; i++)
                {
                    var r = regions[i];
                    if (r.Page != _pvPage) continue;
                    bool trimmed = r.W != r.OrigW || r.H != r.OrigH;
                    if (trimmed && (i == hover || sx >= 0.5f))
                        Outline(RegionRect(in img, page, in r, sx, sy, true), PvLogical);
                    Outline(RegionRect(in img, page, in r, sx, sy, false), i == hover ? PvHover : r.Kind == RegionKind.Sdf ? PvSdfRegion : PvColorRegion);
                }
            string line = "hover a region for details";
            if (hover >= 0)
            {
                var r = regions[hover];
                line = $"{r.Name}  ·  {r.W}x{r.H} @ {r.X},{r.Y}" + (r.W != r.OrigW || r.H != r.OrigH ? $"  ·  logical {r.OrigW}x{r.OrigH} off {r.OffX},{r.OffY}" : "") + (r.Kind == RegionKind.Sdf ? "  ·  sdf" : "");
            }
            GuiRenderer.DrawTextIn(new Rect(rect.x, y, rect.width, PvRowH), line, Gui.FontSize - 1, dim);
        }
    }

    // Bolgenin (ya da trim'li ise mantiksal kutusunun) onizleme rect'i. Sayfa gorselinde satir indeksi yukaridan asagi
    // buyur (DrawRegion v0=1,v1=0 ile cizim; GUI quad ust kenari <-> v1); OffY mantiksal ust boslugu.
    static Rect RegionRect(in Rect img, Texture page, in SpriteTable.Region r, float sx, float sy, bool logical)
    {
        int x = r.X, w = r.W, h = r.H;
        int top = r.Y;
        if (logical)
        {
            x -= r.OffX; top -= r.OffY; w = r.OrigW; h = r.OrigH;
        }
        return new Rect(img.x + x * sx, img.y + top * sy, w * sx, h * sy);
    }

    static void Outline(in Rect r, Color c)
    {
        const float t = 1f;
        GuiRenderer.DrawRect(new Rect(r.x, r.y, r.width, t), c, 2);
        GuiRenderer.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c, 2);
        GuiRenderer.DrawRect(new Rect(r.x, r.y, t, r.height), c, 2);
        GuiRenderer.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c, 2);
    }
}
