using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace DigitoyEngine.Build
{
    // In-process Roslyn: csproj/dotnet build YOK (son kullanici makinesinde yalniz .NET Runtime).
    // Editor (oyun kodu hot-reload) ve aotcompiler (corelib / engine AOT / Game.dll) AYNI dosyayi kullanir
    // (editor csproj Link ile alir). Tanilar csc bicimindedir: path(line,col): error CSxxxx: mesaj.
    public sealed class RoslynBuild
    {
        public string AssemblyName;
        public List<string> SourceFiles = new List<string>();
        public List<(string path, string text)> ExtraSources = new List<(string, string)>(); // uretilen kaynaklar
        public List<string> References = new List<string>();      // dll yollari
        public string[] Defines = Array.Empty<string>();
        public bool NoStdLib;                                      // corelib / AOT: framework referansi yok
        public bool Optimize;
        public string PdbPath;                                     // portable PDB'ye gomulecek yol (null = AssemblyName.pdb)
    }

    public sealed class RoslynResult
    {
        public bool Ok;
        public byte[] Dll, Pdb;
        public string Errors = "";                                 // yalniz error'lar, satir satir
    }

    public static class RoslynCompiler
    {
        static List<MetadataReference> _frameworkRefs;
        static readonly object _lock = new object();

        // Paylasimli framework'un TUM assembly'leri (dotnet build'in Microsoft.NETCore.App referansiyla esdeger; TPA'dan).
        public static List<MetadataReference> FrameworkReferences()
        {
            lock (_lock)
            {
                if (_frameworkRefs != null)
                    return _frameworkRefs;
                string fxDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
                var list = new List<MetadataReference>();
                string tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
                foreach (var p in tpa.Split(Path.PathSeparator))
                {
                    if (!string.Equals(Path.GetDirectoryName(p), fxDir, StringComparison.OrdinalIgnoreCase))
                        continue; // yalniz framework dizini (uygulama/Roslyn assembly'leri girmez)
                    string n = Path.GetFileNameWithoutExtension(p);
                    if (n.StartsWith("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase))
                        continue;
                    try { list.Add(MetadataReference.CreateFromFile(p)); } catch { }
                }
                _frameworkRefs = list;
                return list;
            }
        }

        public static RoslynResult Compile(RoslynBuild b)
        {
            var res = new RoslynResult();
            try
            {
                var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: b.Defines);
                var trees = new List<SyntaxTree>(b.SourceFiles.Count + b.ExtraSources.Count);
                foreach (var s in b.SourceFiles)
                    trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(s), Encoding.UTF8), parse, path: s));
                foreach (var (path, text) in b.ExtraSources)
                    trees.Add(CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), parse, path: path));

                var refs = new List<MetadataReference>();
                if (!b.NoStdLib) refs.AddRange(FrameworkReferences());
                foreach (var r in b.References) refs.Add(MetadataReference.CreateFromFile(r));

                // Eski csproj'larla birebir: Nullable/ImplicitUsings kapali, unsafe acik, deterministik.
                // CS0626 (govdesiz extern = C govdesi runtime'da) ve alan uyarilari corelib/engine icin bastirilir.
                var noWarn = new Dictionary<string, ReportDiagnostic>
                {
                    ["CS0626"] = ReportDiagnostic.Suppress, ["CS0649"] = ReportDiagnostic.Suppress, ["CS0169"] = ReportDiagnostic.Suppress,
                    ["CS0414"] = ReportDiagnostic.Suppress, ["CS0108"] = ReportDiagnostic.Suppress, ["CS0660"] = ReportDiagnostic.Suppress,
                    ["CS0661"] = ReportDiagnostic.Suppress,
                };
                var comp = CSharpCompilation.Create(b.AssemblyName, trees, refs,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                        allowUnsafe: true,
                        nullableContextOptions: NullableContextOptions.Disable,
                        optimizationLevel: b.Optimize ? OptimizationLevel.Release : OptimizationLevel.Debug,
                        deterministic: true,
                        concurrentBuild: true,
                        specificDiagnosticOptions: noWarn));

                using var peMs = new MemoryStream();
                using var pdbMs = new MemoryStream();
                var emit = new EmitOptions(
                    debugInformationFormat: DebugInformationFormat.PortablePdb,
                    pdbFilePath: b.PdbPath ?? b.AssemblyName + ".pdb",
                    runtimeMetadataVersion: b.NoStdLib ? "v4.0.30319" : null); // NoStdLib: mscorlib yok, surumu biz veririz
                var result = comp.Emit(peMs, pdbMs, options: emit);
                var sb = new StringBuilder();
                foreach (var d in result.Diagnostics)
                    if (d.Severity == DiagnosticSeverity.Error)
                        sb.Append(Format(d)).Append('\n');
                res.Errors = sb.ToString();
                res.Ok = result.Success;
                if (res.Ok)
                {
                    res.Dll = peMs.ToArray();
                    res.Pdb = pdbMs.ToArray();
                }
            }
            catch (Exception e)
            {
                res.Ok = false;
                res.Errors += $"{b.AssemblyName}: error RSLN: Roslyn derlemesi calismadi: {e.Message}\n";
            }
            return res;
        }

        // Derle + diske yaz (basarisizsa eski dll'e dokunulmaz). Girdi dosyalari ciktidan eskiyse atlar (cache).
        public static RoslynResult CompileToFile(RoslynBuild b, string dllPath, bool useCache)
        {
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            if (useCache && File.Exists(dllPath))
            {
                var outT = File.GetLastWriteTimeUtc(dllPath);
                bool stale = b.SourceFiles.Concat(b.References).Any(f => !File.Exists(f) || File.GetLastWriteTimeUtc(f) > outT)
                             || b.ExtraSources.Count > 0;
                if (!stale)
                    return new RoslynResult { Ok = true, Dll = File.ReadAllBytes(dllPath) };
            }
            b.PdbPath ??= pdbPath;
            var r = Compile(b);
            if (r.Ok)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dllPath)));
                File.WriteAllBytes(dllPath, r.Dll);
                File.WriteAllBytes(pdbPath, r.Pdb);
            }
            return r;
        }

        // csc/dotnet build ile ayni satir bicimi (editor AssetWatcher ": error " arar)
        public static string Format(Diagnostic d)
        {
            var span = d.Location.GetLineSpan();
            string where = span.IsValid ? $"{span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1})" : "?";
            return $"{where}: error {d.Id}: {d.GetMessage()}";
        }

        // Dizin altindaki *.cs (bin/obj haric; istege bagli ek dislama alt yolu, orn. "/Editor/").
        public static List<string> SourcesUnder(string dir, string excludeSegment = null)
        {
            var list = new List<string>();
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                string n = f.Replace('\\', '/');
                if (n.Contains("/bin/") || n.Contains("/obj/")) continue;
                if (excludeSegment != null && n.Contains(excludeSegment)) continue;
                list.Add(f);
            }
            list.Sort(StringComparer.Ordinal); // deterministik
            return list;
        }
    }
}
