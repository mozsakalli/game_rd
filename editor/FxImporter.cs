using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// .fx -> PixelEffect ([Asset] saf veri: govde metni) DOBJ artifact'i. Icerik dogrulamasi import'ta
// YAPILMAZ — derleme GL context ister; runtime compose dogrular, hatali zincir efektsiz core'a duser + Console'a log.
[AssetImporter(".fx", Version = 2)]
sealed class FxImporter : AssetImporter
{
    public override void Import(ImportContext ctx)
        => ctx.AddArtifact("main", new PixelEffect { Body = File.ReadAllText(ctx.SourcePath) });
}
