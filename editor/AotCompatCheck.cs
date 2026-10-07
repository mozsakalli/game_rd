using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitoyEditor;

// AOT uyumluluk denetimi (editor icinde, erken): oyun scriptleri Digitoy.CoreLib.dll (AOT BCL'si) ve
// AOT engine DLL'ine karsi in-memory Roslyn ile IKINCI kez derlenir. Corelib'de olmayan API burada
// Console'a "AOT uyumsuz" olarak duser — player build'ini (ya da runtime'i) beklemeden.
// Editor play'ini BLOKLAMAZ (editor gercek .NET'te kosar); yalniz bilgi. Referanslar yoksa (henuz
// hic player build alinmamis) sessizce atlanir.
public static class AotCompatCheck
{
    public static string CoreLibDll => Path.Combine(App.RepoRoot, "aotcompiler", "corelib", "bin", "Digitoy.CoreLib.dll");
    public static string AotEngineDll => Path.Combine(App.RepoRoot, "player", "bin", "Release", "net9.0", "DigitoyEngine.dll");

    public static bool Available => File.Exists(CoreLibDll) && File.Exists(AotEngineDll);

    // Donus: hata sayisi (0 = uyumlu ya da denetim atlandi). Hatalar EditorLog'a yazilir.
    public static int Check(IReadOnlyList<string> scriptFiles, string registrySource)
    {
        if (!Available || scriptFiles.Count == 0)
            return 0;
        try
        {
            var refs = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(CoreLibDll),
                MetadataReference.CreateFromFile(AotEngineDll),
            };
            var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { "DE_AOT" });
            var trees = new List<SyntaxTree>();
            foreach (var f in scriptFiles)
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, path: f));
            if (registrySource != null)
                trees.Add(CSharpSyntaxTree.ParseText(registrySource, parse, path: "Registry.g.cs"));
            var comp = CSharpCompilation.Create("AotCompat", trees, refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                    optimizationLevel: OptimizationLevel.Release));
            int n = 0;
            foreach (var d in comp.GetDiagnostics())
            {
                if (d.Severity != DiagnosticSeverity.Error)
                    continue;
                if (n < 30)
                    EditorLog.Warning("[aot-compat] " + d.ToString());
                n++;
            }
            if (n > 0)
                EditorLog.Warning($"[aot-compat] {n} hata: bu kod AOT player'da derlenmez (corelib disi API) — editorde calisir, Build Player basarisiz olur");
            return n;
        }
        catch (Exception e)
        {
            EditorLog.Warning("[aot-compat] denetim calismadi: " + e.Message);
            return 0;
        }
    }
}
