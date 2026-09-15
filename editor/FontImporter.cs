using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// TTF -> prebaked SDF font (DFNT): atlas + glyph metrikleri + kerning EDITORDE
// bir kez pisirilir (native stb_truetype). Pak'a ttf girmez, runtime bake yok.
// v2: atlas alt bandina UiPieces utility bolgesi (kutu/golge SDF parcalari) gomulur.
[AssetImporter(".ttf", ".otf", Version = 4)]
sealed class FontImporter : AssetImporter
{
    public override void Import(ImportContext ctx)
    {
        var dfnt = Font.Bake(File.ReadAllBytes(ctx.SourcePath));
        if (dfnt == null)
        {
            ctx.Fail("font bake basarisiz (gecersiz/desteklenmeyen ttf)");
            return;
        }
        ctx.AddArtifact("main", dfnt);
    }
}
