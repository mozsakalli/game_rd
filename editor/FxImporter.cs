using System.IO;
using DigitoyEngine;

namespace DigitoyEditor;

// .fx -> pixel effect govdesi: metin oldugu gibi artifact olur (pak yolu bedava).
// Icerik dogrulamasi import'ta YAPILMAZ — derleme GL context ister; runtime compose
// TryCreateEffect ile dogrular, hatali zincir efektsiz core'a duser + Console'a log.
[AssetImporter(".fx", Version = 1)]
sealed class FxImporter : AssetImporter
{
    public override void Import(ImportContext ctx)
        => ctx.AddArtifact("main", File.ReadAllBytes(ctx.SourcePath));
}
