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
}
