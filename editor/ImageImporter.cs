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

// Gorsel import ayarlari (.meta "importer:" blogu; Inspector / Sprite Editor duzenler).
[Serializable]
public sealed class ImageImportSettings
{
    public ImageFormat format = ImageFormat.Auto;
    public int jpgQuality = 90;
    // 9-slice kenarlari (kaynak piksel; left, top, right, bottom). Resmin OZELLIGI: Sprite.Border'a tasinir
    // (standalone "border" artifact'i / atlas Region), LayoutBox Stretch modunda otomatik 3x3 cizer.
    [Labels("L", "T", "R", "B")] public Vec4 border;

    public bool HasBorder => border.x > 0 || border.y > 0 || border.z > 0 || border.w > 0;
}

// png/jpg -> "main": pak'a KODLANMIS gorsel girer (ham RGBA degil; 57 KB jpg -> 7.8 MB ham -> zlib 500 KB
// idi). Runtime stb_image ile native worker'da async cozer (diger gorsellerle ayni yol). Auto = kaynak
// baytlari aynen; Png/Jpg = decode + yeniden kodla (flip'li yazim: yukleme flip'iyle bellek duzeni ayni).
// Standalone: atlas grubu yutmussa pak'a girmez (pikseller sayfada). "border" (4 x int32 LE) yalniz
// border > 0 ise yazilir; atlas uyesinde deger Region'a gecer, artifact yutulur.
[AssetImporter(".png", ".jpg", ".jpeg", Version = 2, Settings = typeof(ImageImportSettings))]
public sealed class ImageImporter : AssetImporter
{
    const float PvButtonH = 24f, PvImageMax = 220f;
    Texture _pvTex;
    string _pvRel;

    // Onizleme: [Sprite Editor...] + resim (sigdir) + kayitli 9-slice kilavuzlari.
    public override float PreviewHeight(string assetPath)
    {
        var t = PreviewTexture(assetPath);
        float imgH = t != null && t.Width > 0 && !t.Loading ? Math.Min(PvImageMax, t.Height) : 0f;
        return PvButtonH + (imgH > 0 ? imgH + 6 : 0);
    }

    public override void DrawPreview(string assetPath, in Rect rect)
    {
        if (Gui.Button(new Rect(rect.x, rect.y, 110, PvButtonH - 2), "Sprite Editor..."))
            SpriteEditorPopup.Show(assetPath);
        var t = PreviewTexture(assetPath);
        if (t == null || t.Width <= 0 || t.Height <= 0 || t.Loading || Event.Current.Type != EventType.Repaint)
            return;
        float y = rect.y + PvButtonH + 4;
        float scale = MathF.Min(1f, MathF.Min(rect.width / t.Width, PvImageMax / t.Height));
        var img = new Rect(rect.x, y, t.Width * scale, t.Height * scale);
        GuiRenderer.DrawRect(img, new Color(64, 66, 74, 255), 0);
        GuiRenderer.DrawTextureBlended(img, t, 1);
        if (ImportPipeline.LoadSettings(assetPath) is ImageImportSettings s && s.HasBorder)
        {
            var g = new Color(80, 230, 110, 255);
            GuiRenderer.DrawRect(new Rect(img.x + s.border.x * scale, img.y, 1, img.height), g, 2);
            GuiRenderer.DrawRect(new Rect(img.xMax - s.border.z * scale - 1, img.y, 1, img.height), g, 2);
            GuiRenderer.DrawRect(new Rect(img.x, img.y + s.border.y * scale, img.width, 1), g, 2);
            GuiRenderer.DrawRect(new Rect(img.x, img.yMax - s.border.w * scale - 1, img.width, 1), g, 2);
        }
    }

    Texture PreviewTexture(string rel)
    {
        if (App.Assets == null) return null;
        if (_pvRel != rel) { _pvRel = rel; _pvTex = App.Assets.LoadTexture(rel); }
        return _pvTex;
    }

    public override void Import(ImportContext ctx)
    {
        var s = ctx.Settings as ImageImportSettings ?? new ImageImportSettings();
        if (s.HasBorder)
        {
            var b = new byte[16];
            BitConverter.TryWriteBytes(b.AsSpan(0, 4), (int)MathF.Round(Math.Max(0, s.border.x)));
            BitConverter.TryWriteBytes(b.AsSpan(4, 4), (int)MathF.Round(Math.Max(0, s.border.y)));
            BitConverter.TryWriteBytes(b.AsSpan(8, 4), (int)MathF.Round(Math.Max(0, s.border.z)));
            BitConverter.TryWriteBytes(b.AsSpan(12, 4), (int)MathF.Round(Math.Max(0, s.border.w)));
            ctx.AddArtifact(AssetDatabase.BorderArtifact, b, ArtifactScope.Standalone);
        }
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
