using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace DigitoyEditor;

// Oyun kodu derleyici + yukleyici. Unity modeli: Assets/ altinda gorulen HER .cs
// derlenir; yolu /Editor/ iceren dosyalar oyun assembly'sine GIRMEZ (custom editor
// kodu — ayri assembly olarak derlenmesi sonraki faz). Derleme: Library/Build'e
// uretilen csproj + dotnet build (bagimlilik yok); yukleme: collectible
// AssemblyLoadContext (unload → taze derleme → TypeCatalog yeniden kurulur).
public sealed class GameCode
{
    AssemblyLoadContext _alc;

    public Assembly GameAssembly { get; private set; }

    // Son derlemede tespit edilen tip rename'leri (eski -> yeni). Katalog alias'i olur.
    public readonly List<KeyValuePair<string, string>> Renames = new();

    // Worker-safe derleme ciktisi: yalniz dosya IO + subprocess urunu, ALC yok.
    public sealed class CompileJob
    {
        public bool Ok;
        public string Output = "";
        public string Dll;              // null = script yok
        public List<string> Scripts = new();
        public string AsmName;
    }

    // SAF derleme (background thread'te kosabilir): script topla, csproj uret,
    // dotnet build. ALC'ye, sahneye, GPU'ya DOKUNMAZ.
    public static CompileJob CompileOnly(Project project)
    {
        var job = new CompileJob();
        foreach (var f in Directory.GetFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories))
        {
            if (f.Replace('\\', '/').Contains("/Editor/"))
                continue; // editor-yalniz kod: oyun assembly'sine girmez
            job.Scripts.Add(f);
        }
        if (job.Scripts.Count == 0)
        {
            job.Ok = true;
            return job;
        }

        string buildDir = Path.Combine(project.LibraryPath, "Build");
        Directory.CreateDirectory(buildDir);
        job.AsmName = project.Name + ".Game";
        string enginePath = typeof(DigitoyEngine.GameObject).Assembly.Location;

        var sb = new System.Text.StringBuilder(1024);
        sb.Append("<Project Sdk=\"Microsoft.NET.Sdk\">\n");
        sb.Append("  <PropertyGroup>\n");
        sb.Append("    <TargetFramework>net9.0</TargetFramework>\n");
        sb.Append("    <AssemblyName>").Append(job.AsmName).Append("</AssemblyName>\n");
        sb.Append("    <Nullable>disable</Nullable>\n");
        sb.Append("    <ImplicitUsings>disable</ImplicitUsings>\n");
        sb.Append("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>\n");
        sb.Append("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>\n");
        sb.Append("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>\n");
        sb.Append("    <OutputPath>bin</OutputPath>\n");
        sb.Append("    <ProduceReferenceAssembly>false</ProduceReferenceAssembly>\n");
        sb.Append("  </PropertyGroup>\n  <ItemGroup>\n");
        sb.Append("    <Reference Include=\"DigitoyEngine\"><HintPath>").Append(enginePath).Append("</HintPath></Reference>\n");
        foreach (var s in job.Scripts)
            sb.Append("    <Compile Include=\"").Append(s).Append("\" />\n");
        sb.Append("  </ItemGroup>\n</Project>\n");

        string csproj = Path.Combine(buildDir, "Game.csproj");
        File.WriteAllText(csproj, sb.ToString());

        var psi = new ProcessStartInfo("dotnet", $"build \"{csproj}\" -v q --nologo")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var proc = Process.Start(psi);
        job.Output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        job.Ok = proc.ExitCode == 0;
        if (job.Ok)
            job.Dll = Path.Combine(buildDir, "bin", job.AsmName + ".dll");
        return job;
    }

    // ANA THREAD: eski ALC bosalir, taze dll stream'den yuklenir, typemap guncellenir.
    // Cagiran once tum canli instance'lari yikmis olmali (UnloadLive/Stop).
    public bool LoadCompiled(Project project, DigitoyEngine.AssetDatabase assets, CompileJob job)
    {
        if (!job.Ok)
            return false;
        Unload();
        if (job.Dll == null)
            return true; // script yok
        _alc = new AssemblyLoadContext("game", isCollectible: true);
        // Stream'den yukle: dosya kilitlenmez, sonraki derleme uzerine yazabilir.
        using (var fs = File.OpenRead(job.Dll))
            GameAssembly = _alc.LoadFromStream(fs);
        Console.WriteLine($"[gamecode] yuklendi: {job.AsmName} ({job.Scripts.Count} script)");
        UpdateTypemap(project, assets, job.Scripts);
        return true;
    }

    public bool CompileAndLoad(Project project, DigitoyEngine.AssetDatabase assets)
    {
        var job = CompileOnly(project);
        if (!job.Ok)
        {
            Console.WriteLine("[gamecode] DERLEME HATASI:\n" + job.Output);
            return false;
        }
        return LoadCompiled(project, assets, job);
    }

    // Script kimligi: typemap.yaml = script GUID'i -> o dosyada bildirilen Component
    // class'lari. Bir guid'de tek class kaybolup tek class belirdiyse = RENAME:
    // migrasyon kaydedilir, sahnelerdeki eski ad katalog alias'iyla cozulur.
    // (Unity bunu YAPMAZ: class rename = missing script. Biz dosya kimliginden yakaliyoruz.)
    void UpdateTypemap(Project project, DigitoyEngine.AssetDatabase assets, List<string> scripts)
    {
        Renames.Clear();
        if (GameAssembly == null || assets == null)
            return;

        var compTypes = new HashSet<string>();
        foreach (var t in GameAssembly.GetTypes())
            if (!t.IsAbstract && typeof(DigitoyEngine.Component).IsAssignableFrom(t))
                compTypes.Add(t.Name);

        string mapPath = Path.Combine(project.LibraryPath, "typemap.yaml");
        var oldFiles = new Dictionary<string, string>();
        var renames = new Dictionary<string, string>();
        if (File.Exists(mapPath))
        {
            var old = DigitoyEngine.Yaml.Parse(File.ReadAllText(mapPath));
            var files = old.Get("files");
            if (files?.Fields != null)
                foreach (var kv in files.Fields)
                    oldFiles[kv.Key] = kv.Value.Scalar ?? "";
            var ren = old.Get("renames");
            if (ren?.Fields != null)
                foreach (var kv in ren.Fields)
                    renames[kv.Key] = kv.Value.Scalar ?? "";
        }

        var newFiles = new Dictionary<string, string>();
        var classRegex = new System.Text.RegularExpressions.Regex(@"\bclass\s+([A-Za-z_]\w*)");
        foreach (var script in scripts)
        {
            string rel = Path.GetRelativePath(project.AssetsPath, script).Replace('\\', '/');
            string guid = assets.PathToGuid(rel);
            if (guid == null)
                continue;
            var found = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in classRegex.Matches(File.ReadAllText(script)))
                if (compTypes.Contains(m.Groups[1].Value))
                    found.Add(m.Groups[1].Value);
            newFiles[guid] = string.Join(' ', found);

            // Rename tespiti: ayni dosyada (guid) tek class kayip + tek class yeni.
            if (oldFiles.TryGetValue(guid, out var oldList) && oldList != newFiles[guid])
            {
                var oldSet = new List<string>(oldList.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                var newSet = new List<string>(newFiles[guid].Split(' ', StringSplitOptions.RemoveEmptyEntries));
                var lost = oldSet.FindAll(n => !newSet.Contains(n));
                var gained = newSet.FindAll(n => !oldSet.Contains(n));
                if (lost.Count == 1 && gained.Count == 1)
                {
                    renames[lost[0]] = gained[0];
                    // Zincir sikistirma: A->eski, eski->yeni ise A->yeni.
                    foreach (var key in new List<string>(renames.Keys))
                        if (renames[key] == lost[0])
                            renames[key] = gained[0];
                    Console.WriteLine($"[gamecode] rename yakalandi: {lost[0]} -> {gained[0]}");
                }
            }
        }

        var root = DigitoyEngine.DocNode.Map();
        var filesNode = DigitoyEngine.DocNode.Map();
        foreach (var kv in newFiles)
            filesNode.Add(kv.Key, DigitoyEngine.DocNode.Scal(kv.Value));
        root.Add("files", filesNode);
        var renamesNode = DigitoyEngine.DocNode.Map();
        foreach (var kv in renames)
        {
            renamesNode.Add(kv.Key, DigitoyEngine.DocNode.Scal(kv.Value));
            Renames.Add(new(kv.Key, kv.Value));
        }
        root.Add("renames", renamesNode);
        File.WriteAllText(mapPath, DigitoyEngine.Yaml.Write(root));
    }

    public void Unload()
    {
        _alc?.Unload();
        _alc = null;
        GameAssembly = null;
    }
}
