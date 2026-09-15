using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitoyEditor;

// CatalogWriter ciktisini in-memory derler (MSBuild subprocess YOK: ilk derleme
// ~yarim saniye Roslyn warmup, sonrakiler ~50-150ms). Cikti PE byte'lari — cagiran
// game ALC'sine LoadFromStream ile yukler (reload'da game ile birlikte olur).
public static class RegistryCompiler
{
    // Uretilen kodun ihtiyaci olan cekirdek runtime referanslari (TPA'dan secilir).
    static readonly HashSet<string> CoreRefNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Private.CoreLib", "System.Runtime", "netstandard",
        "System.Collections", "System.Reflection",
    };

    static List<MetadataReference> _coreRefs; // TPA sabittir, bir kez cozulur

    public static byte[] Compile(string source, string gameDllPath, out string errors)
    {
        if (_coreRefs == null)
        {
            _coreRefs = new List<MetadataReference>();
            string tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            foreach (var p in tpa.Split(Path.PathSeparator))
                if (CoreRefNames.Contains(Path.GetFileNameWithoutExtension(p)))
                    _coreRefs.Add(MetadataReference.CreateFromFile(p));
        }
        var refs = new List<MetadataReference>(_coreRefs)
        {
            MetadataReference.CreateFromFile(typeof(DigitoyEngine.GameObject).Assembly.Location),
        };
        if (gameDllPath != null)
            refs.Add(MetadataReference.CreateFromImage(File.ReadAllBytes(gameDllPath))); // dosya kilidi yok

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var comp = CSharpCompilation.Create(CatalogWriter.AssemblyName, new[] { tree }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));

        using var ms = new MemoryStream();
        var result = comp.Emit(ms);
        if (!result.Success)
        {
            var sb = new StringBuilder();
            foreach (var d in result.Diagnostics)
                if (d.Severity == DiagnosticSeverity.Error)
                    sb.Append(d.ToString()).Append('\n');
            errors = sb.ToString();
            return null;
        }
        errors = null;
        return ms.ToArray();
    }
}
