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

    // Managed plugin'ler (Assets/**/*.dll, /Editor/ haric): oyun assembly'siyle AYNI ALC'de, ondan once yuklenir.
    public Assembly[] PluginAssemblies { get; private set; } = Array.Empty<Assembly>();

    // Katalog/tarama icin assembly kumesi: plugin'ler + oyun (null'lar yok).
    public Assembly[] CodeAssemblies
    {
        get
        {
            var list = new List<Assembly>(PluginAssemblies);
            if (GameAssembly != null) list.Add(GameAssembly);
            return list.ToArray();
        }
    }

    // Unity Plugins modeli: Assets altindaki her .dll runtime plugin'idir (yolu /Editor/ icerenler haric). Sirali, deterministik.
    public static List<string> PluginDlls(Project project)
    {
        var list = new List<string>();
        if (!Directory.Exists(project.AssetsPath)) return list;
        foreach (var f in Directory.GetFiles(project.AssetsPath, "*.dll", SearchOption.AllDirectories))
            if (!f.Replace('\\', '/').Contains("/Editor/"))
                list.Add(f);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

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
        public List<string> Plugins = new(); // Assets/**/*.dll (referans + ALC)
        public string AsmName;
    }

    // SAF derleme (background thread'te kosabilir): script topla, ortak RoslynCompiler ile derle
    // (aotcompiler/source/RoslynCompiler.cs; Link ile bu projede), dll+pdb yaz. ALC'ye, sahneye, GPU'ya DOKUNMAZ.
    // Eski csproj ile birebir: framework TPA + engine DLL referansi, DEBUG;TRACE;DE_GAME, Debug, portable PDB.
    public static CompileJob CompileOnly(Project project)
    {
        var job = new CompileJob();
        job.Scripts.AddRange(RoslynCompiler.SourcesUnder(project.AssetsPath, "/Editor/")); // editor-yalniz kod oyun assembly'sine girmez
        job.Plugins.AddRange(PluginDlls(project));
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
        build.References.AddRange(job.Plugins);
        // Basarisizsa diskteki eski (calisan) dll'e dokunulmaz (CompileToFile once bellege derler).
        var r = RoslynCompiler.CompileToFile(build, dll, useCache: false);
        job.Output = r.Errors;
        job.Ok = r.Ok;
        if (r.Ok) job.Dll = dll;
        return job;
    }
    // IDE projesi (Unity'nin Assembly-CSharp.csproj'u): proje kokune <Ad>.Game.csproj yazar — yalniz
    // IntelliSense/gezinme icin, editor derlemesi bunu KULLANMAZ (in-process Roslyn). Kaynak kumesi glob
    // (Assets/**/*.cs, Editor/ haric) oldugundan dosya ekle/sil'de yeniden uretim gerekmez; ayarlar
    // RoslynCompiler ile birebir (net10.0, unsafe, nullable kapali, DEBUG;TRACE;DE_GAME). bin/obj Library/'ye.
    public static void WriteIdeProject(Project project)
    {
        string engineDll = typeof(DigitoyEngine.GameObject).Assembly.Location;
        string asmName = project.Name + ".Game";
        var sb = new StringBuilder();
        sb.AppendLine("<!-- DigitoyEditor tarafindan uretilir; elle duzenleme bir sonraki acilista ezilir. -->");
        sb.AppendLine("<Project>");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <BaseIntermediateOutputPath>Library/IdeBuild/obj/</BaseIntermediateOutputPath>");
        sb.AppendLine("    <BaseOutputPath>Library/IdeBuild/bin/</BaseOutputPath>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <Import Project=\"Sdk.props\" Sdk=\"Microsoft.NET.Sdk\" />");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <OutputType>Library</OutputType>");
        sb.AppendLine($"    <AssemblyName>{asmName}</AssemblyName>");
        sb.AppendLine("    <RootNamespace></RootNamespace>");
        sb.AppendLine("    <LangVersion>latest</LangVersion>");
        sb.AppendLine("    <Nullable>disable</Nullable>");
        sb.AppendLine("    <ImplicitUsings>disable</ImplicitUsings>");
        sb.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
        sb.AppendLine("    <DefineConstants>DEBUG;TRACE;DE_GAME</DefineConstants>");
        sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        sb.AppendLine("    <EnableDefaultItems>false</EnableDefaultItems>");
        sb.AppendLine("    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>");
        sb.AppendLine("    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>");
        sb.AppendLine("  </PropertyGroup>");
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <Compile Include=\"Assets/**/*.cs\" Exclude=\"Assets/**/Editor/**/*.cs\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <Reference Include=\"DigitoyEngine\">");
        sb.AppendLine($"      <HintPath>{engineDll}</HintPath>");
        sb.AppendLine("      <Private>false</Private>");
        sb.AppendLine("    </Reference>");
        sb.AppendLine("    <Reference Include=\"Assets/**/*.dll\" Exclude=\"Assets/**/Editor/**/*.dll\" Private=\"false\" />");
        sb.AppendLine("  </ItemGroup>");
        sb.AppendLine("  <Import Project=\"Sdk.targets\" Sdk=\"Microsoft.NET.Sdk\" />");
        sb.AppendLine("</Project>");

        string path = Path.Combine(project.Root, asmName + ".csproj");
        string text = sb.ToString();
        // Icerik ayniysa dokunma (IDE'nin yeniden yukleme tetiklenmesin).
        if (File.Exists(path) && File.ReadAllText(path) == text)
            return;
        File.WriteAllText(path, text);
    }

    // ANA THREAD: eski ALC bosalir, taze dll stream'den yuklenir, typemap guncellenir.
    // Cagiran once tum canli instance'lari yikmis olmali (UnloadLive/Stop).
    public bool LoadCompiled(Project project, DigitoyEngine.AssetDatabase assets, CompileJob job)
    {
        if (!job.Ok)
            return false;
        Unload();
        if (job.Dll == null && job.Plugins.Count == 0)
        {
            // script de plugin de yok; Editor/ kodu yine olabilir (ayar tipi plugin'siz)
            _alc = new AssemblyLoadContext("game", isCollectible: true);
            LoadEditorCode(project);
            return true;
        }
        _alc = new AssemblyLoadContext("game", isCollectible: true);
        // Plugin'ler once (oyun dll'i onlara referans verir); hepsi stream'den: dosya kilitlenmez, uzerine yazilabilir.
        var plugins = new List<Assembly>();
        foreach (var p in job.Plugins)
        {
            try
            {
                using var ps = File.OpenRead(p);
                plugins.Add(_alc.LoadFromStream(ps));
            }
            catch (Exception e)
            {
                Console.WriteLine($"[gamecode] plugin yuklenemedi: {Path.GetFileName(p)}: {e.Message}");
            }
        }
        PluginAssemblies = plugins.ToArray();
        // Oyun dll'inin plugin referansi basit adla cozulur (ALC kendi yukledigini her zaman bulmaz).
        _alc.Resolving += (_, name) =>
        {
            foreach (var a in plugins)
                if (string.Equals(a.GetName().Name, name.Name, StringComparison.Ordinal)) return a;
            return null;
        };
        if (job.Dll != null)
        {
            using (var fs = File.OpenRead(job.Dll))
                GameAssembly = _alc.LoadFromStream(fs);
        }
        GameDllPath = job.Dll;
        Console.WriteLine($"[gamecode] yuklendi: {job.AsmName ?? "(script yok)"} ({job.Scripts.Count} script, {plugins.Count} plugin)");
        UpdateTypemap(project, assets, job.Scripts);
        LoadEditorCode(project);
        return true;
    }

    public bool CompileAndLoad(Project project, DigitoyEngine.AssetDatabase assets)
    {
        var job = CompileOnly(project);
        if (!job.Ok)
        {
            Console.WriteLine("[gamecode] DERLEME HATASI:\n" + job.Output);
            EditorLog.Error("[gamecode] derleme hatasi — oyun kodu yuklenmedi:\n" + job.Output);
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
        DigitoyEngine.Plugin.MockHost.Instance.Clear(); // eski ALC mock'lari tutulmasin (reload kacagi)
        DigitoyEngine.Plugin.ResetAll();
        _alc?.Unload();
        _alc = null;
        GameAssembly = null;
        EditorAssembly = null;
        PluginAssemblies = Array.Empty<Assembly>();
        GameDllPath = null;
    }

    // Assets/**/Editor/**/*.cs -> <Ad>.Editor.dll (docs/plugins.md): plugin build kancalari (IAndroidProjectHook...) + PluginMock<T>.
    // Oyun assembly'siyle ayni ALC'de, ondan sonra (Runtime ayar tiplerine referans verir). Hata = Console'a; oyun kodu etkilenmez.
    public Assembly EditorAssembly { get; private set; }

    public void LoadEditorCode(Project project)
    {
        EditorAssembly = null;
        var sources = new List<string>();
        foreach (var f in RoslynCompiler.SourcesUnder(project.AssetsPath))
            if (f.Replace('\\', '/').Contains("/Editor/")) sources.Add(f);
        if (sources.Count == 0 || _alc == null)
            return;
        string dll = Path.Combine(project.LibraryPath, "Build", "bin", project.Name + ".Editor.dll");
        var build = new RoslynBuild
        {
            AssemblyName = project.Name + ".Editor",
            SourceFiles = sources,
            References = { typeof(DigitoyEngine.GameObject).Assembly.Location, typeof(DigitoyEngine.Build.ProjectHooks).Assembly.Location },
            Defines = new[] { "DEBUG", "TRACE", "DE_EDITOR", "DE_GAME" },
        };
        if (GameDllPath != null) build.References.Add(GameDllPath);
        build.References.AddRange(PluginDlls(project));
        var r = RoslynCompiler.CompileToFile(build, dll, useCache: false);
        if (!r.Ok)
        {
            EditorLog.Error("[gamecode] Editor.dll derleme hatasi (Assets/**/Editor):\n" + r.Errors);
            return;
        }
        try
        {
            using (var fs = File.OpenRead(dll))
                EditorAssembly = _alc.LoadFromStream(fs);
        }
        catch (Exception e) { EditorLog.Error("[gamecode] Editor.dll yuklenemedi: " + e.Message); return; }
        int mocks = 0;
        foreach (var t in EditorAssembly.GetTypes())
        {
            if (t.IsAbstract || !typeof(DigitoyEngine.PluginMockBase).IsAssignableFrom(t)) continue;
            try { DigitoyEngine.Plugin.MockHost.Instance.Register((DigitoyEngine.PluginMockBase)Activator.CreateInstance(t)); mocks++; }
            catch (Exception e) { EditorLog.Error("[gamecode] mock kurulamadi " + t.Name + ": " + e.Message); }
        }
        Console.WriteLine($"[gamecode] editor kodu: {sources.Count} dosya, {mocks} plugin mock");
    }
}
