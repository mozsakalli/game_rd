using System;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

public enum ImageFormat
{
    Auto, // kaynak dosyanin kendisi (bayt-esit kopya; uzanti ne ise o)
    Png,  // yeniden kodla: kayipsiz
    Jpg,  // yeniden kodla: kayipli (alfa atilir), jpgQuality
}

// Gorsel import ayarlari (.meta "importer:" blogu; Inspector duzenler).
[Serializable]
public sealed class ImageImportSettings
{
    public ImageFormat format = ImageFormat.Auto;
    public int jpgQuality = 90;
}

// png/jpg -> "main": pak'a KODLANMIS gorsel girer (ham RGBA degil; 57 KB jpg -> 7.8 MB ham -> zlib 500 KB
// idi). Runtime stb_image ile native worker'da async cozer (diger gorsellerle ayni yol). Auto = kaynak
// baytlari aynen; Png/Jpg = decode + yeniden kodla (flip'li yazim: yukleme flip'iyle bellek duzeni ayni).
// Standalone: atlas grubu yutmussa pak'a girmez (pikseller sayfada).
[AssetImporter(".png", ".jpg", ".jpeg", Version = 1, Settings = typeof(ImageImportSettings))]
public sealed class ImageImporter : AssetImporter
{
    public override void Import(ImportContext ctx)
    {
        var s = ctx.Settings as ImageImportSettings ?? new ImageImportSettings();
        if (s.format == ImageFormat.Auto)
        {
            ctx.AddArtifact("main", File.ReadAllBytes(ctx.SourcePath), ArtifactScope.Standalone);
            return;
        }
        var rgba = EditorPixels.DecodeFile(ctx.SourcePath, out int w, out int h);
        if (rgba == null)
        {
            ctx.Fail("gorsel cozulemedi");
            return;
        }
        bool jpg = s.format == ImageFormat.Jpg;
        var bytes = jpg
            ? Sokol.ImageEncode(rgba, w, h, 4, jpg: true, Math.Clamp(s.jpgQuality, 1, 100), flip: true)
            : PngWriter.Encode(rgba, w, h, flip: true);
        if (bytes == null)
        {
            ctx.Fail("gorsel kodlanamadi");
            return;
        }
        ctx.AddArtifact("main", bytes, ArtifactScope.Standalone);
        EditorLog.Info($"[image] {ctx.AssetPath}: {(jpg ? "jpg q" + s.jpgQuality : "png")} {w}x{h} ({bytes.Length / 1024} KB)");
    }
}
