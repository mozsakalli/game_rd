using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitoyEditor;

// Dinamik modul publish'i (docs/modules.md Faz D): acik proje bir .pak modulu olarak paketlenir.
// Zincir (hepsi ana thread; aotcompiler 'module' kisa surer — corelib+engine frontend + IR yazimi):
//   1) Modul assembly'si: Assets/**/*.cs + module Registry.g.cs (yalniz oyun assembly'sinin tipleri, benzersiz
//      ad alani) Roslyn ile AOT corelib + AOT engine (+ varsa host oyun DLL'i) referanslariyla derlenir.
//      AotCompatCheck ile ayni referans kumesi: editorde derlenen ama corelib'de olmayan API burada hata verir.
//   2) aotcompiler module <code.dmod> --bundled <modul.dll> [--provided <hostDll>]
//   3) Pak: AssetPackBuilder (sahneler + asset'ler + .project) + __module/code.dmod + __module/entry
// Cikti: <proje>/Build/<ProjeAdi>.module.pak -> host oyun kodunda Module.LoadAsync(yol).
public static class ModuleBuilder
{
    [MenuItem("Project/Build Module", 3)]
    static void BuildMenu() => Build();

    public static bool Build()
    {
        var project = App.Project;
        if (project == null)
            return Fail("acik proje yok");
        if (AssetWatcher.Status.Length > 0)
            return Fail("oyun kodu derlemesi suruyor; bitince tekrar deneyin");
        if (AssetWatcher.Failed)
            return Fail("oyun kodu derlenemiyor; once hatalari giderin (Console)");
        if (!AotCompatCheck.Available)
            return Fail("AOT referanslari yok (corelib/engine): once bir kez Build Player alin: " + AotCompatCheck.AotEngineDll);
        var gameAsm = App.GameAssembly;
        if (gameAsm == null)
            return Fail("modulde script yok (Assets altinda .cs bulunamadi)");

        string buildDir = Path.Combine(project.LibraryPath, "Build", "module");
        Directory.CreateDirectory(buildDir);
        string safeName = Sanitize(project.Name);
        string asmName = safeName + ".Module";
        string ns = safeName + ".Generated";
        string entryName = ns + ".Registry$RegisterAll_DigitoyEngine_TypeCatalog"; // Code.EncodeName sozlesmesi

        // 1) kaynaklar: scriptler (+ editor-yalniz haric) + modul registry'si
        var scripts = new List<string>();
        foreach (var f in Directory.GetFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories))
            if (!f.Replace('\\', '/').Contains("/Editor/"))
                scripts.Add(f);
        string registrySrc = CatalogWriter.Write(App.Catalog, t => t.Assembly == gameAsm, ns);
        File.WriteAllText(Path.Combine(buildDir, "Registry.g.cs"), registrySrc);

        string hostDll = project.Player.moduleHostDll;
        if (!string.IsNullOrWhiteSpace(hostDll) && !Path.IsPathRooted(hostDll))
            hostDll = Path.GetFullPath(Path.Combine(project.Root, hostDll));
        if (!string.IsNullOrWhiteSpace(hostDll) && !File.Exists(hostDll))
            return Fail("moduleHostDll bulunamadi: " + hostDll);

        string dll = Path.Combine(buildDir, asmName + ".dll");
        if (!CompileModuleDll(scripts, registrySrc, asmName, hostDll, dll))
            return false;
        EditorLog.Info($"[module] 1/3 assembly: {dll} ({scripts.Count} script)");

        // 2) IR
        string dmod = Path.Combine(buildDir, "code.dmod");
        if (!RunAotModule(dll, hostDll, dmod))
            return false;
        EditorLog.Info($"[module] 2/3 code.dmod: {new FileInfo(dmod).Length / 1024} KB (rapor: {Path.ChangeExtension(dmod, ".report.txt")})");

        // 3) pak
        string outPath = Path.Combine(project.Root, "Build", safeName + ".module.pak");
        var extra = new List<(string, byte[])>
        {
            (Module.CodeKey, File.ReadAllBytes(dmod)),
            (Module.EntryKey, Encoding.UTF8.GetBytes(entryName)),
        };
        if (!AssetPackBuilder.Build(outPath, extra))
            return Fail("modul pak'i uretilemedi");
        EditorLog.Info($"[module] 3/3 OK -> {outPath} ({new FileInfo(outPath).Length / 1024} KB); host: Module.LoadAsync(\"{outPath}\")");
        return true;
    }

    static bool CompileModuleDll(List<string> scripts, string registrySrc, string asmName, string hostDll, string outDll)
    {
        try
        {
            var refs = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(AotCompatCheck.CoreLibDll),
                MetadataReference.CreateFromFile(AotCompatCheck.AotEngineDll),
            };
            if (!string.IsNullOrWhiteSpace(hostDll))
                refs.Add(MetadataReference.CreateFromFile(hostDll));
            var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { "DE_AOT", "DE_MODULE" });
            var trees = new List<SyntaxTree>();
            foreach (var f in scripts)
                trees.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(f), parse, path: f));
            trees.Add(CSharpSyntaxTree.ParseText(registrySrc, parse, path: "Registry.g.cs"));
            var comp = CSharpCompilation.Create(asmName, trees, refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
                    optimizationLevel: OptimizationLevel.Release));
            using var pe = File.Create(outDll);
            using var pdb = File.Create(Path.ChangeExtension(outDll, ".pdb"));
            var result = comp.Emit(pe, pdb, options: new Microsoft.CodeAnalysis.Emit.EmitOptions(debugInformationFormat: Microsoft.CodeAnalysis.Emit.DebugInformationFormat.PortablePdb));
            int n = 0;
            foreach (var d in result.Diagnostics)
                if (d.Severity == DiagnosticSeverity.Error && n++ < 30)
                    EditorLog.Error("[module] " + d);
            if (!result.Success)
                return Fail($"modul assembly'si derlenemedi ({n} hata) — bu kod AOT corelib yuzeyinin disinda API kullaniyor olabilir");
            return true;
        }
        catch (Exception e)
        {
            return Fail("Roslyn: " + e.Message);
        }
    }

    static bool RunAotModule(string dll, string hostDll, string dmod)
    {
        string aotProj = Path.Combine(App.RepoRoot, "aotcompiler", "aotcompiler.csproj");
        if (!File.Exists(aotProj))
            return Fail("aotcompiler bulunamadi: " + aotProj);
        string args = $"run --project \"{aotProj}\" --no-launch-profile -v q -- module \"{dmod}\" --bundled \"{dll}\"";
        if (!string.IsNullOrWhiteSpace(hostDll))
            args += $" --provided \"{hostDll}\"";
        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory = Path.GetDirectoryName(aotProj),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var sw = Stopwatch.StartNew();
        using var proc = Process.Start(psi);
        string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        foreach (var line in output.Split('\n'))
            if (line.Contains("[HATA]") || line.Contains("error") || line.Contains("basarisiz"))
                EditorLog.Error("[aot] " + line.Trim());
        if (proc.ExitCode != 0 || !File.Exists(dmod))
            return Fail($"aotcompiler module basarisiz (exit {proc.ExitCode}, {sw.Elapsed.TotalSeconds:F0} s)");
        return true;
    }

    static string Sanitize(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        if (sb.Length == 0 || char.IsDigit(sb[0])) sb.Insert(0, 'M');
        return sb.ToString();
    }

    static bool Fail(string why)
    {
        EditorLog.Error("[module] " + why);
        return false;
    }
}
