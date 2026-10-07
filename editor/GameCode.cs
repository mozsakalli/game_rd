using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using DigitoyEngine.Build;

namespace DigitoyEditor;

// Oyun kodu derleyici + yukleyici. Unity modeli: Assets/ altinda gorulen HER .cs
// derlenir; yolu /Editor/ iceren dosyalar oyun assembly'sine GIRMEZ (custom editor
// kodu — ayri assembly olarak derlenmesi sonraki faz). Derleme: IN-PROCESS Roslyn
// (RegistryCompiler ile ayni; .NET SDK GEREKMEZ — kurulu editor yalniz runtime ister;
// docs/editor-distribution.md Faz 2). Cikti Library/Build/bin/<Ad>.Game.dll + portable pdb
// (satir numarali trace). Yukleme: collectible AssemblyLoadContext (unload -> taze derleme
// -> TypeCatalog yeniden kurulur).
public sealed class GameCode
{
    AssemblyLoadContext _alc;

    public Assembly GameAssembly { get; private set; }

    // Son basarili derlemenin dll yolu (RegistryCompiler referans olarak okur).
    public string GameDllPath { get; private set; }

    // Son derlemede tespit edilen tip rename'leri (eski -> yeni). Katalog alias'i olur.
    public readonly List<KeyValuePair<string, string>> Renames = new();

    // Worker-safe derleme ciktisi: yalniz dosya IO + Roslyn, ALC yok.
    public sealed class CompileJob
    {
        public bool Ok;
        public string Output = "";
        public string Dll;              // null = script yok
        public List<string> Scripts = new();
        public string AsmName;
    }

    // SAF derleme (background thread'te kosabilir): script topla, ortak RoslynCompiler ile derle
    // (aotcompiler/source/RoslynCompiler.cs; Link ile bu projede), dll+pdb yaz. ALC'ye, sahneye, GPU'ya DOKUNMAZ.
    // Eski csproj ile birebir: framework TPA + engine DLL referansi, DEBUG;TRACE;DE_GAME, Debug, portable PDB.
    public static CompileJob CompileOnly(Project project)
    {
        var job = new CompileJob();
        job.Scripts.AddRange(RoslynCompiler.SourcesUnder(project.AssetsPath, "/Editor/")); // editor-yalniz kod oyun assembly'sine girmez
        if (job.Scripts.Count == 0)
        {
            job.Ok = true;
            return job;
        }

        string buildDir = Path.Combine(project.LibraryPath, "Build");
        string binDir = Path.Combine(buildDir, "bin");
        Directory.CreateDirectory(binDir);
        job.AsmName = project.Name + ".Game";
        string dll = Path.Combine(binDir, job.AsmName + ".dll");

        var build = new RoslynBuild
        {
            AssemblyName = job.AsmName,
            SourceFiles = job.Scripts,
            References = { typeof(DigitoyEngine.GameObject).Assembly.Location },
            Defines = new[] { "DEBUG", "TRACE", "DE_GAME" },
        };
        // Uretilen registry assembly'si internal oyun tiplerine erisebilsin.
        build.ExtraSources.Add((Path.Combine(buildDir, "RegistryVisibility.g.cs"),
            "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"" + CatalogWriter.AssemblyName + "\")]\n"));
        // Basarisizsa diskteki eski (calisan) dll'e dokunulmaz (CompileToFile once bellege derler).
        var r = RoslynCompiler.CompileToFile(build, dll, useCache: false);
        job.Output = r.Errors;
        job.Ok = r.Ok;
        if (r.Ok) job.Dll = dll;
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
        GameDllPath = job.Dll;
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

    // Uretilen registry PE'sini game ALC'sine yukler (reload'da birlikte olur).
    // Script yoksa registry yalniz engine tiplerini tasir — ALC yine de acilir.
    public Assembly LoadRegistry(byte[] pe)
    {
        _alc ??= new AssemblyLoadContext("game", isCollectible: true);
        using var ms = new MemoryStream(pe);
        return _alc.LoadFromStream(ms);
    }

    public void Unload()
    {
        _alc?.Unload();
        _alc = null;
        GameAssembly = null;
        GameDllPath = null;
    }
}
