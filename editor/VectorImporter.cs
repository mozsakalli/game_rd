using System;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Vektor import ayarlari (.meta "importer:" blogu). Cozunurluk IMPORT'TA sabitlenir: hedef = dogal px * scale.
[Serializable]
public sealed class VectorImportSettings
{
    public float scale = 1f;
    public int maxSize = 4096; // hedef kenar ust siniri (koordinat i16/16 icin 2048'e kadar sabit nokta)
}

// svg -> "main": DVEC komut blob'u (de_svg_compile.c). Pak'a PNG yerine bu girer (ikon icin 10-100x kucuk);
// runtime de_fs decode job'u magic'i gorunce rasterize eder (de_vector.h) — LoadTexture/pak/player PNG ile
// ayni yol. Desteklenmeyen SVG ozellikleri (text/filter/mask/clipPath) sessizce atlanir (nanosvg).
// Standalone: atlas grubu yutmussa pak'a girmez.
[AssetImporter(".svg", Version = 1, Settings = typeof(VectorImportSettings))]
public sealed class VectorImporter : AssetImporter
{
    public override void Import(ImportContext ctx)
    {
        var s = ctx.Settings as VectorImportSettings ?? new VectorImportSettings();
        var text = File.ReadAllBytes(ctx.SourcePath);
        var blob = Sokol.SvgCompile(text, s.scale > 0 ? s.scale : 1f, s.maxSize, out int w, out int h);
        if (blob == null)
        {
            ctx.Fail("svg derlenemedi (bos/gecersiz ya da maxSize asimi)");
            return;
        }
        ctx.AddArtifact("main", blob, ArtifactScope.Standalone);
        EditorLog.Info($"[vector] {ctx.AssetPath}: {w}x{h} ({blob.Length} B, kaynak {text.Length} B)");
    }

    // --- Inspector onizleme: tek satir bilgi (derlenmis DVEC boyutu / kaynak svg boyutu). Kontrol yok. ---
    string _pvRel;
    int _pvSeenChange = -1;
    int _pvW, _pvH;
    long _pvEncoded, _pvSource;
    const float PreviewRowH = 22f;

    public override float PreviewHeight(string assetPath) => PreviewRowH;

    public override void HidePreview()
    {
        _pvRel = null;
    }

    void PreviewBind(string rel)
    {
        // Secim degisince ya da diskte bir sey degisince (Apply -> reimport) yeniden oku.
        if (_pvRel == rel && _pvSeenChange == AssetWatcher.AssetChangeVersion)
            return;
        _pvRel = rel;
        _pvSeenChange = AssetWatcher.AssetChangeVersion;
        _pvW = _pvH = 0;
        _pvEncoded = _pvSource = 0;
        try { _pvSource = new FileInfo(ImportPipeline.FullPath(rel)).Length; }
        catch { }
        string art = ImportPipeline.ArtifactPath(rel, "main");
        if (art == null)
            return;
        try { _pvEncoded = new FileInfo(art).Length; }
        catch { }
        ImageHeader.TryReadSize(art, out _pvW, out _pvH);
    }

    public override void DrawPreview(string assetPath, in Rect rect)
    {
        PreviewBind(assetPath);
        if (Event.Current.Type != EventType.Repaint)
            return;
        string info;
        if (_pvEncoded <= 0)
            info = "(not imported)";
        else
        {
            string ratio = _pvSource > 0 ? $"  ({_pvEncoded * 100 / _pvSource}%)" : "";
            info = $"{_pvW}×{_pvH}  ·  encoded {FormatKb(_pvEncoded)}  ·  source {FormatKb(_pvSource)}{ratio}";
        }
        GuiRenderer.DrawTextIn(new Rect(rect.x, rect.y, rect.width, PreviewRowH), info, Gui.FontSize - 1,
            new Color(160, 168, 182, 255));
    }

    static string FormatKb(long bytes) => bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024f:0.0} KB";
}
