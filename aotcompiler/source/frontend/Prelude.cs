using System.Collections.Generic;
using DigitoyEngine.Language;

namespace DigitoyEngine.Frontend
{
    // Corlib yukleyicisi: c_runtime/corelib/*.cs (MiniCs kaynagi) her pipeline'in basinda derlenir,
    // extern'lerin VM govdeleri Intrinsics.Bind ile baglanir (C govdeleri c_runtime/corelib.c'de).
    public static class Prelude
    {
        public const string CorelibDir = "c_runtime/corelib";

        public static List<Code> Compile(Context ctx)
        {
            var files = System.IO.Directory.GetFiles(CorelibDir, "*.cs");
            // KAYIT-ONCELIGI YERINE: once tum dosya AST'leri toplanir, sonra tipler kaydedilir,
            // sonra methodlar ve govdeler emisyon edilir. Bu, kaynak dosya okuma sirasina bagimli
            // calismanin onune gecen ana duzeltmedir; her dosya birbirine bagimli olabilir.
            var units = new List<(string source, string fileName)>();
            foreach (var f in files)
                units.Add((System.IO.File.ReadAllText(f), System.IO.Path.GetFileName(f)));
            var codes = CsCompiler.CompileAll(ctx, units).RequireSuccess();
            return codes;
        }
    }
}
