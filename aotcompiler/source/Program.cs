using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DigitoyEngine.Language;
using DigitoyEngine.Cil;

// Selftest surucusu: dotnet-selftest.dll'i CIL frontend ile IR'a yukler, corelib ile C'ye
// transpile eder, clang ile derler, calistirir ve ciktiyi .NET baseline'iyla (N<TAB>sonuc)
// karsilastirir. VM/interpreter yoktur - tek dogruluk kaynagi canli .NET calismasidir.
public static class Program
{
    const string SelftestProj = "tests/dotnet-selftest/DotnetSelfTest.csproj";
    static readonly string WorkDir = Path.Combine("obj", "selftest-c");
    static readonly string GeneratedC = Path.Combine(WorkDir, "generated.c");
    static readonly string OutBin = Path.Combine(WorkDir, "selftest");

    const string EngineProj = "engine/managed/DigitoyEngine.csproj";
    static readonly string EngineWorkDir = Path.Combine("obj", "engine-c");
    static readonly string EngineGeneratedC = Path.Combine(EngineWorkDir, "generated.c");
    static readonly string EngineOutBin = Path.Combine(EngineWorkDir, "DigitoyEngine.exe");

    public static int Main(string[] args)
    {
        try
        {
            // Modul host export tablolari (docs/modules.md): player her zaman host'tur; diger modlar
            // AOT_MODULES=1 ile acar (selftest --interp yolu corelib host'u ister).
            CTranspiler.EmitModuleExports = (args.Length > 0 && args[0] == "player") || Environment.GetEnvironmentVariable("AOT_MODULES") == "1";

            // 'engine' modu: desktop engine exe'sini uret (kendi native host + sokol).
            if (args.Length > 0 && args[0] == "engine")
                return RunEngineBuild();

            // 'player <proje koku>' modu: oyunu tek native exe'ye derle (release urunu).
            if (args.Length > 0 && args[0] == "player")
                return RunPlayerBuild(args.Length > 1 ? args[1] : Path.Combine(RepoRoot, "Projects", "Sandbox"));

            // 'module <cikti.dmod> --bundled a.dll;b.dll [--provided c.dll;d.dll]' modu: dinamik modul IR'i (docs/modules.md Faz B).
            if (args.Length > 0 && args[0] == "module")
                return RunModuleBuild(args);

            // Varsayilan: CIL frontend selftest'i (derlenmis DLL -> IR). Tek frontend CIL'dir; corelib de
            // Roslyn ile derlenip ayni yoldan yuklenir (MiniCs kaynak frontend'i silindi).
            return RunCilSelftest();
        }
        catch (Exception e)
        {
            Console.WriteLine($"[HATA] {e.Message}");
            if (Environment.GetEnvironmentVariable("AOT_TRACE") == "1")
                Console.WriteLine(e.ToString());
            return 1;
        }
    }

    // Calisma dizini aotcompiler/ (c_runtime goreli yollari); repo koku bir ust.
    static string RepoRoot => Path.GetFullPath("..");

    // corelib: c_runtime/corelib/*.cs Roslyn ile (NoStdLib) Digitoy.CoreLib.dll'e derlenir, CIL frontend
    // engine/player ile AYNI yoldan yukler (isCoreLib: well-known binding + extern kurali). Tek frontend.
    const string CoreLibProj = "corelib/Digitoy.CoreLib.csproj";
    const string CoreLibDll = "corelib/bin/Digitoy.CoreLib.dll";

    static List<Code> LoadCoreLib(Context ctx)
    {
        var (exit, output) = RunProcess("dotnet", $"build {Quote(CoreLibProj)} -v q --nologo");
        if (exit != 0) throw new Exception($"corelib derlemesi basarisiz (Roslyn):\n{output}");
        if (!File.Exists(CoreLibDll)) throw new Exception("corelib dll bulunamadi: " + CoreLibDll);
        var codes = CilFrontend.Compile(ctx, CoreLibDll, out _, isCoreLib: true);
        if (CilFrontend.LastDiagnostics.Count > 0)
            Console.WriteLine($"[corelib] CIL tani: {CilFrontend.LastDiagnostics.Count} oge:\n  " + string.Join("\n  ", CilFrontend.LastDiagnostics));
        return codes;
    }

    // NIHAI URUN: DigitoyPlayer (engine + uretilmis registry + oyun scriptleri tek DLL,
    // DE_AOT) -> CIL -> IR -> C -> clang; native (sokol + glfw + ses) STATIK linklenir.
    // Cikti: <proje>/Build/<ProjeAdi>.exe, game.pak'in yanina. Exe ".." = proje koku kabul eder.
    static int RunPlayerBuild(string projectRoot)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        string projName = Path.GetFileName(projectRoot.TrimEnd('\\', '/'));
        string workDir = Path.Combine("obj", "player-c");
        string generated = Path.Combine(workDir, "generated.c");
        string outExe = Path.Combine(projectRoot, "Build", projName + ".exe");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Lap(string what) { Console.WriteLine($"  [sure] {what}: {sw.Elapsed.TotalSeconds:F1} s"); sw.Restart(); }
        // 1) managed derleme (Release: DE_EDITOR yok; GameProject: registry + scriptler gomulu)
        var playerProj = Path.Combine(RepoRoot, "player", "DigitoyPlayer.csproj");
        var (bexit, bout) = RunProcess("dotnet", $"build {Quote(playerProj)} -c Release -v q --nologo -p:GameProject={Quote(projectRoot)}");
        if (bexit != 0) throw new Exception($"player derlemesi basarisiz:\n{bout}");
        var binDir = Path.Combine(RepoRoot, "player", "bin", "Release", "net9.0");
        var engineDll = Path.Combine(binDir, "DigitoyEngine.dll");
        var playerDll = Path.Combine(binDir, "DigitoyPlayer.dll");
        foreach (var d in new[] { engineDll, playerDll })
            if (!File.Exists(d)) throw new Exception("dll bulunamadi: " + d);
        Console.WriteLine($"managed: {engineDll}\n         {playerDll}");
        Lap("dotnet build (engine+player Release)");

        // 2) CIL -> IR: corelib + engine + player (player engine tiplerini ctx'ten cozer)
        var ctx = new Context();
        var allCodes = new List<Code>();
        allCodes.AddRange(LoadCoreLib(ctx));
        Lap("corelib (Digitoy.CoreLib.dll, CIL)");
        var diag = new List<string>();
        allCodes.AddRange(CilFrontend.Compile(ctx, engineDll, out _));
        diag.AddRange(CilFrontend.LastDiagnostics.Select(d => "[engine] " + d));
        allCodes.AddRange(CilFrontend.Compile(ctx, playerDll, out _));
        diag.AddRange(CilFrontend.LastDiagnostics.Select(d => "[player] " + d));
        Lap("CIL frontend (engine+player)");
        // Host sozlesmesi: Main KULLANILMAZ; C dongusu Init/Frame/Shutdown cagirir (GC safepoint = Frame sonrasi).
        Code HostCode(string name) => ctx.TryGetCode("DigitoyPlayer.PlayerApp$" + name, out var c) ? c
            : throw new Exception($"DigitoyPlayer.PlayerApp.{name} bulunamadi");
        var initCode = HostCode("Init_System_String");
        var frameCode = HostCode("Frame");
        var shutdownCode = HostCode("Shutdown");
        Directory.CreateDirectory(workDir);
        File.WriteAllLines(Path.Combine(workDir, "diag.txt"), diag);
        Console.WriteLine($"CIL tani: {diag.Count} oge atlandi/stub'landi -> {Path.Combine(workDir, "diag.txt")}");
        Resolver.ResolveAll(ctx, allCodes);
        Lap("resolve/monomorph");

        // STUB KAPISI: host dongusunden (Init/Frame/Shutdown) cagri grafiyla ERISILEBILEN stub =
        // runtime'da NotImplementedException demek -> build HATASI (exe cikmaz; liste basilir).
        // Erisilemeyen stub'lar (editor/import yolu, kullanilmayan API) diag.txt'de kalir.
        var reachableStubs = ReachableStubs(ctx, new[] { initCode, frameCode, shutdownCode });
        if (reachableStubs.Count > 0)
        {
            Console.WriteLine($"[HATA] runtime'dan erisilebilen {reachableStubs.Count} AOT stub (calisirken patlar):");
            foreach (var (code, via) in reachableStubs)
                Console.WriteLine($"  {code.Owner?.Name}.{code.DisplayName}\n      neden: {code.UntranslatableReason}\n      yol:   {via}");
            throw new Exception("AOT stub kapisi: eksik corelib/frontend destegi (yukaridaki liste); corelib'e ekleyin ya da motor kodunu yuzeye uydurun");
        }

        // 3) C transpile + host main: crash dump dizini (exe yaninda "crash/"), cikista rapor
        //    (sessiz cikis YOK: managed frame icindeyken exit = crash dump), runtime init, Main.
        //    Unhandled exception yolu runtime'da (DIGITOYENGINE_dispatch -> rapor + dump + exit 134).
        var cSource = CTranspiler.TranspileProgram(ctx);
        string Sym(Code c) => CTranspiler.CName(c.EncodeName());
        var entrySym = Sym(frameCode);
        // Host dongusu (langtest modeli): her frame managed Frame(), ardindan managed frame YOKKEN
        // GC safepoint: gc_minor (genc nesil, ucuz) + gc_maybe_major(butce) (artimli, fps'i oldurmez).
        cSource += "\n#include <direct.h>\nstatic void digitoyengine_host_atexit(void) {\n" +
            "    fprintf(stderr, \"[host] exit (shadow stack depth %d)\\n\", DIGITOYENGINE_sp);\n" +
            "    if (DIGITOYENGINE_sp > 0) DIGITOYENGINE_crash_dump(\"exit inside managed frames\", DIGITOYENGINE_stack, DIGITOYENGINE_sp);\n" +
            "}\n" +
            "int main(void) {\n" +
            "    _mkdir(\"crash\");\n    digitoyengine_crash_init(\"crash\");\n" +
            "    atexit(digitoyengine_host_atexit);\n" +
            "    fprintf(stderr, \"[host] start\\n\");\n" +
            "    digitoyengine_init();\n" +
            $"    {Sym(initCode)}(0);\n" +
            $"    while ({Sym(frameCode)}()) {{\n" +
            "        if (!getenv(\"AOT_NOGC\")) gc_maybe_major(1 << 14); // artimli GC dilimi: managed frame YOKKEN (safepoint)\n" +
            "    }\n" +
            $"    {Sym(shutdownCode)}();\n" +
            "    gc_major();\n" +
            "    fprintf(stderr, \"[host] main returned\\n\");\n" +
            "    return 0;\n}\n";
        File.WriteAllText(generated, cSource);
        Console.WriteLine($"transpile -> {generated} ({cSource.Length} karakter, giris {entrySym})");
        Lap("C transpile + yaz");

        // 4) clang: uretilen C + runtime (vmrt/corelib) + STATIK native kutuphane
        //    (engine/native/build_native_static.cmd -> digitoyengine_native_static.lib: sokol+glfw+ses+de_fs).
        //    DLL yok, P/Invoke yok: [DllImport] sembolleri linker'da statik cozulur.
        var native = Path.Combine(RepoRoot, "engine", "native");
        var staticLib = Path.Combine(native, "build", "digitoyengine_native_static.lib");
        if (!File.Exists(staticLib) || Directory.GetFiles(native, "*.c").Any(f => File.GetLastWriteTimeUtc(f) > File.GetLastWriteTimeUtc(staticLib)))
        {
            Console.WriteLine("native statik lib eski/yok -> build_native_static.cmd");
            var (nexit, nout) = RunProcess("cmd.exe", $"/c \"{Path.Combine(native, "build_native_static.cmd")}\"");
            if (nexit != 0) throw new Exception($"native statik lib derlemesi basarisiz:\n{nout}");
        }
        var cFiles = new List<string> { generated, "c_runtime/vmrt.c", "c_runtime/corelib.c", "c_runtime/vmint.c" };
        var defines = "-DSOKOL_GLCORE" + (Environment.GetEnvironmentVariable("AOT_GCPOISON") == "1" ? " -DDIGITOYENGINE_GC_POISON" : "");
        var libs = "-lopengl32 -lgdi32 -luser32 -lkernel32 -lshell32 -lole32 -loleaut32 -lmfplat -lmfuuid";
        Directory.CreateDirectory(Path.GetDirectoryName(outExe));
        // AOT_DEBUG: -O0 + sembol (lldb) + DIGITOYENGINE_DEBUG (STEP/local tablolari -> crash'te degisken dokumu)
        var dbg = Environment.GetEnvironmentVariable("AOT_DEBUG") == "1" ? "-O0 -g -gcodeview -DDIGITOYENGINE_DEBUG" : "-O1";
        var compileArgs = $"{dbg} -w {defines} -Ic_runtime -I{Quote(native)} " +
            $"{string.Join(" ", cFiles.Select(Quote))} {Quote(staticLib)} {libs} -o {Quote(outExe)}";
        var (ccExit, ccOut) = RunProcess(ClangPath(), compileArgs);
        if (ccExit != 0) throw new Exception($"clang derleme/link hatasi (exit {ccExit}):\n{ccOut}");
        Console.WriteLine($"clang ok -> {outExe} ({new FileInfo(outExe).Length / 1024} KB)");
        Lap("clang + link");
        return 0;
    }

    // Engine managed DLL'ini derler, CIL -> IR -> C transpile eder ve kendi native
    // host'umuz (host_win32.c) + sokol (sokol_impl.c, sokol_shim.c) ile linkler.
    // Selftest'ten farkli: baseline/diff yoktur (GUI uygulama); yalnizca uretim + link.
    static int RunEngineBuild()
    {
        var dll = BuildEngineDll();
        Console.WriteLine($"engine dll: {dll}");

        // 1) CIL frontend -> IR (corelib + engine dll)
        var ctx = new Context();
        var corelib = LoadCoreLib(ctx);
        var cil = CilFrontend.Compile(ctx, dll, out var entry);
        if (entry == null) throw new Exception("engine dll giris noktasi (Main) bulunamadi");
        if (CilFrontend.LastDiagnostics.Count > 0)
        {
            Console.WriteLine($"CIL tani: {CilFrontend.LastDiagnostics.Count} oge atlandi/stub'landi");
            Directory.CreateDirectory(EngineWorkDir);
            File.WriteAllLines(Path.Combine(EngineWorkDir, "diag.txt"), CilFrontend.LastDiagnostics);
        }
        var allCodes = new List<Code>();
        allCodes.AddRange(corelib);
        allCodes.AddRange(cil);
        Resolver.ResolveAll(ctx, allCodes);

        // 2) C transpile + main sarmalayici
        var cSource = CTranspiler.TranspileProgram(ctx);
        var entrySym = CTranspiler.CName(entry.EncodeName());
        cSource += $"\nint main(void) {{\n    digitoyengine_init();\n    {entrySym}();\n    return 0;\n}}\n";
        Directory.CreateDirectory(EngineWorkDir);
        File.WriteAllText(EngineGeneratedC, cSource);
        Console.WriteLine($"transpile -> {EngineGeneratedC} ({cSource.Length} karakter, giris {entrySym})");

        // 3) clang derle + link (native host + sokol + Windows GL/GDI/User)
        var cFiles = new[]
        {
            EngineGeneratedC,
            "c_runtime/vmrt.c",
            "c_runtime/corelib.c",
            "c_runtime/vmint.c",
            "engine/native/sokol_impl.c",
            "engine/native/sokol_shim.c",
            "engine/native/host_win32.c",
        };
        var libs = "-lopengl32 -lgdi32 -luser32 -lkernel32 -lshell32";
        var compileArgs =
            $"-O1 -w -DSOKOL_GLCORE -Ic_runtime -Iengine/native " +
            $"{string.Join(" ", cFiles.Select(Quote))} {libs} -o {Quote(EngineOutBin)}";
        var (ccExit, ccOut) = RunProcess(ClangPath(), compileArgs);
        if (ccExit != 0) throw new Exception($"clang derleme/link hatasi (exit {ccExit}):\n{ccOut}");
        Console.WriteLine($"clang derleme ok -> {Path.GetFullPath(EngineOutBin)}");
        Console.WriteLine("Calistirmak icin: " + Quote(Path.GetFullPath(EngineOutBin)));
        return 0;
    }

    // Dinamik modul derlemesi: corelib + provided + bundled assembly'ler tek Context'te cozulur/monomorfize edilir,
    // ModuleWriter bundled olanlari (ve provided generic somutlamalarini gomulu olarak) .dmod'a yazar.
    // Komut: module <cikti.dmod> --bundled a.dll;b.dll [--provided c.dll;d.dll]
    static int RunModuleBuild(string[] args)
    {
        if (args.Length < 2) throw new Exception("kullanim: module <cikti.dmod> --bundled a.dll;b.dll [--provided c.dll;d.dll]");
        string output = args[1];
        var bundled = new List<string>();
        var provided = new List<string>();
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--bundled" && i + 1 < args.Length) bundled.AddRange(args[++i].Split(';', StringSplitOptions.RemoveEmptyEntries));
            else if (args[i] == "--provided" && i + 1 < args.Length) provided.AddRange(args[++i].Split(';', StringSplitOptions.RemoveEmptyEntries));
            else throw new Exception("bilinmeyen arguman: " + args[i]);
        }
        if (bundled.Count == 0) throw new Exception("en az bir --bundled assembly gerekli");
        // Engine her zaman host'tadir: provided listesinde DigitoyEngine yoksa AOT engine DLL'i (player Release) eklenir.
        if (!provided.Any(p => Path.GetFileNameWithoutExtension(p).Equals("DigitoyEngine", StringComparison.OrdinalIgnoreCase)))
        {
            var engine = Path.Combine(RepoRoot, "player", "bin", "Release", "net9.0", "DigitoyEngine.dll");
            if (!File.Exists(engine)) throw new Exception("AOT engine DLL yok (once player build alin): " + engine);
            provided.Insert(0, engine);
        }
        var ctx = new Context();
        var allCodes = new List<Code>(LoadCoreLib(ctx));
        var diag = new List<string>();
        var bundledNames = new List<string>();
        foreach (var dll in provided.Concat(bundled))
        {
            if (!File.Exists(dll)) throw new Exception("assembly bulunamadi: " + dll);
            allCodes.AddRange(CilFrontend.Compile(ctx, dll, out _));
            diag.AddRange(CilFrontend.LastDiagnostics.Select(d => $"[{Path.GetFileName(dll)}] " + d));
            if (bundled.Contains(dll)) bundledNames.Add(AssemblyNameOf(dll));
        }
        Resolver.ResolveAll(ctx, allCodes);
        var bytes = ModuleWriter.Write(ctx, bundledNames, null, out var report);
        if (report.ReachableStubs.Count > 0)
        {
            Console.WriteLine($"[HATA] modulden erisilebilen {report.ReachableStubs.Count} AOT stub:");
            foreach (var (code, via) in report.ReachableStubs)
                Console.WriteLine($"  {code.Owner?.Name}.{code.DisplayName}\n      neden: {code.UntranslatableReason}\n      yol:   {via}");
            throw new Exception("modul stub kapisi: eksik corelib/frontend destegi");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllBytes(output, bytes);
        File.WriteAllLines(Path.ChangeExtension(output, ".report.txt"), ModuleReportLines(report, diag));
        Console.WriteLine($"module -> {output}: {report}");
        return 0;
    }

    static string AssemblyNameOf(string dll)
    {
        using var pe = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(dll));
        var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        return md.GetString(md.GetAssemblyDefinition().Name);
    }

    static IEnumerable<string> ModuleReportLines(ModuleWriter.Report r, List<string> diag)
    {
        yield return r.ToString();
        yield return "";
        yield return "[yerel tipler]"; foreach (var s in r.LocalTypes) yield return "  " + s;
        yield return "[gomulu tipler (preferHost)]"; foreach (var s in r.EmbeddedTypes) yield return "  " + s;
        yield return "[dis tipler]"; foreach (var s in r.ExternTypes) yield return "  " + s;
        yield return "[yerel metotlar]"; foreach (var s in r.LocalMethods) yield return "  " + s;
        yield return "[gomulu metotlar (preferHost)]"; foreach (var s in r.EmbeddedMethods) yield return "  " + s;
        yield return "[dis metotlar]"; foreach (var s in r.ExternMethods) yield return "  " + s;
        yield return "[CIL tani]"; foreach (var s in diag) yield return "  " + s;
    }

    // Engine managed projesini derler ve uretilen DLL yolunu doner.
    static string BuildEngineDll()
    {
        var (exit, output) = RunProcess("dotnet", $"build {Quote(EngineProj)} -c Debug -v q");
        if (exit != 0) throw new Exception($"engine dll derlemesi basarisiz:\n{output}");
        var dll = Path.Combine("engine", "managed", "bin", "Debug", "net9.0", "DigitoyEngine.dll");
        if (!File.Exists(dll)) throw new Exception($"engine dll bulunamadi: {dll}");
        return dll;
    }

    static int RunCilSelftest()
    {
        {
            var dll = BuildSelftestDll();
            Console.WriteLine($"selftest dll: {dll}");

            // 1) .NET baseline: dll'i .NET uzerinde calistir, N<TAB>... satirlarini al
            var baseline = RunProcess("dotnet", Quote(dll)).output;
            var baseMap = ParseResults(baseline);
            Console.WriteLine($"baseline: {baseMap.Count} case");

            // 2) CIL frontend -> IR (corelib + dll)
            var ctx = new Context();
            var corelib = LoadCoreLib(ctx);
            var cil = CilFrontend.Compile(ctx, dll, out var entry);
            if (entry == null) throw new Exception("dll giris noktasi (Main) bulunamadi");
            if (CilFrontend.LastDiagnostics.Count > 0)
            {
                Console.WriteLine($"CIL tani: {CilFrontend.LastDiagnostics.Count} oge atlandi/stub'landi");
                Directory.CreateDirectory(WorkDir);
                File.WriteAllLines(Path.Combine(WorkDir, "diag.txt"), CilFrontend.LastDiagnostics);
            }
            var allCodes = new List<Code>();
            allCodes.AddRange(corelib);
            allCodes.AddRange(cil);
            Resolver.ResolveAll(ctx, allCodes);

            // AOT_MODULES=1: selftest'i modul olarak da yaz (ModuleWriter dogrulamasi; Faz C'de --interp bunu yorumlar)
            if (CTranspiler.EmitModuleExports)
            {
                Directory.CreateDirectory(WorkDir);
                var dmod = Path.Combine(WorkDir, "selftest.dmod");
                var bytes = ModuleWriter.Write(ctx, new[] { AssemblyNameOf(dll) }, entry, out var report);
                File.WriteAllBytes(dmod, bytes);
                File.WriteAllLines(Path.ChangeExtension(dmod, ".report.txt"), ModuleReportLines(report, CilFrontend.LastDiagnostics));
                Console.WriteLine($"module -> {dmod}: {report}");
            }

            // 3) C transpile + main sarmalayici
            var cSource = CTranspiler.TranspileProgram(ctx);
            var entrySym = CTranspiler.CName(entry.EncodeName());
            cSource += $"\nint main(void) {{\n    digitoyengine_init();\n    {entrySym}();\n    return 0;\n}}\n";
            Directory.CreateDirectory(WorkDir);
            File.WriteAllText(GeneratedC, cSource);
            Console.WriteLine($"transpile -> {GeneratedC} ({cSource.Length} karakter, giris {entrySym})");

            // 4) clang derle
            var cFiles = new[] { GeneratedC, "c_runtime/vmrt.c", "c_runtime/corelib.c", "c_runtime/vmint.c" };
            var poison = Environment.GetEnvironmentVariable("AOT_GCPOISON") == "1" ? " -DDIGITOYENGINE_GC_POISON" : "";
            var compileArgs = $"-O1 -w{poison} -Ic_runtime {string.Join(" ", cFiles.Select(Quote))} -o {Quote(OutBin)}";
            var (ccExit, ccOut) = RunProcess(ClangPath(), compileArgs);
            if (ccExit != 0) throw new Exception($"clang derleme hatasi (exit {ccExit}):\n{ccOut}");
            Console.WriteLine("clang derleme ok");

            // 5) calistir + karsilastir
            var actual = RunProcess(Path.GetFullPath(OutBin), "").output;
            var actualMap = ParseResults(actual);
            int aotResult = Diff(baseMap, actualMap);
            if (!CTranspiler.EmitModuleExports) return aotResult;

            // 6) INTERP yolu (docs/modules.md Faz C): yalniz corelib'den AOT host + vmint.c; selftest.dmod yorumlanir, ayni baseline.
            Console.WriteLine("\n=== interp: corelib host + vmint (selftest.dmod) ===");
            var hctx = new Context();
            var hcodes = LoadCoreLib(hctx);
            Resolver.ResolveAll(hctx, hcodes);
            var hostSrc = CTranspiler.TranspileProgram(hctx);
            hostSrc += "\n#include \"vmint.h\"\n" +
                "int main(int argc, char **argv) {\n" +
                "    if (argc < 2) { fprintf(stderr, \"kullanim: host <modul.dmod>\\n\"); return 2; }\n" +
                "    digitoyengine_init();\n" +
                "    FILE *f = fopen(argv[1], \"rb\"); if (!f) { fprintf(stderr, \"[interp] dosya acilamadi\\n\"); return 2; }\n" +
                "    fseek(f, 0, SEEK_END); long n = ftell(f); fseek(f, 0, SEEK_SET);\n" +
                "    unsigned char *buf = (unsigned char *)malloc(n); fread(buf, 1, n, f); fclose(f);\n" +
                "    char err[512]; VmModule *m = vmint_load(buf, (int)n, err, sizeof err);\n" +
                "    if (!m) { fprintf(stderr, \"[interp] yukleme hatasi: %s\\n\", err); return 3; }\n" +
                "    fprintf(stderr, \"[interp] yuklendi; entry kosuyor\\n\");\n" +
                "    vmint_run_entry(m);\n" +
                "    vmint_unload(m); gc_major(); gc_major(); /* ilk dongu surmekte olabilir (yeni nesneler siyah dogar) */\n" +
                "    fprintf(stderr, \"[interp] unload: live=%d, collect=%d\\n\", vmint_live(m), vmint_collect());\n" +
                "    return 0;\n}\n";
            var hostC = Path.Combine(WorkDir, "interp_host.c");
            var hostBin = Path.Combine(WorkDir, "interp_host");
            File.WriteAllText(hostC, hostSrc);
            var hFiles = new[] { hostC, "c_runtime/vmrt.c", "c_runtime/corelib.c", "c_runtime/vmint.c" };
            var hArgs = $"-O1 -w{poison} -Ic_runtime {string.Join(" ", hFiles.Select(Quote))} -o {Quote(hostBin)}";
            var (hExit, hOut) = RunProcess(ClangPath(), hArgs);
            if (hExit != 0) throw new Exception($"interp host clang hatasi (exit {hExit}):\n{hOut}");
            Console.WriteLine("interp host clang ok");
            var interp = RunProcess(Path.GetFullPath(hostBin), Quote(Path.GetFullPath(Path.Combine(WorkDir, "selftest.dmod"))));
            var tail = interp.output.Split('\n').Where(l => l.StartsWith("[interp]") || l.StartsWith("[vmint]")).ToList();
            foreach (var l in tail) Console.WriteLine(l.Trim());
            int interpResult = Diff(baseMap, ParseResults(interp.output));
            return aotResult != 0 ? aotResult : interpResult;
        }
    }

    static string FirstLine(string s)
    {
        var i = s.IndexOf('\n');
        var line = i < 0 ? s : s.Substring(0, i);
        return line.Trim();
    }

    // Selftest projesini derler ve uretilen dll yolunu doner.
    static string BuildSelftestDll()
    {
        var (exit, output) = RunProcess("dotnet", $"build {Quote(SelftestProj)} -c Debug -v q");
        if (exit != 0) throw new Exception($"selftest dll derlemesi basarisiz:\n{output}");
        var dll = Path.Combine("tests", "dotnet-selftest", "bin", "Debug", "net9.0", "DotnetSelfTest.dll");
        if (!File.Exists(dll)) throw new Exception($"dll bulunamadi: {dll}");
        return dll;
    }

    // "N<TAB>..." satirlarini (N -> tam satir kuyrugu) haritalar; digerlerini yok sayar.
    static Dictionary<int, string> ParseResults(string text)
    {
        var map = new Dictionary<int, string>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            if (!int.TryParse(line.Substring(0, tab), out var n)) continue;
            map[n] = line.Substring(tab + 1);
        }
        return map;
    }

    static int Diff(Dictionary<int, string> baseMap, Dictionary<int, string> actualMap)
    {
        // Demo15 haric: `return acc + new object().GetHashCode()` -> .NET kimlik hash'i rasgele
        // (her kosumda farkli), deterministik eslesemez. Bilincli olarak kapsam disi.
        var excluded = new HashSet<int> { 15 };
        int pass = 0, fail = 0, total = 0;
        foreach (var n in baseMap.Keys.OrderBy(x => x))
        {
            if (excluded.Contains(n)) continue;
            total++;
            var expected = baseMap[n];
            if (!actualMap.TryGetValue(n, out var got))
            {
                Console.WriteLine($"FAIL {n}: C ciktisi yok (beklenen: {expected})");
                fail++;
            }
            else if (got == expected)
            {
                pass++;
            }
            else
            {
                Console.WriteLine($"FAIL {n}: beklenen [{expected}] geldi [{got}]");
                fail++;
            }
        }
        Console.WriteLine($"--- SONUC: {pass}/{total} PASS, {fail} FAIL (Demo15 haric: rasgele kimlik hash) ---");
        return fail == 0 ? 0 : 1;
    }

    static string Quote(string s) => "\"" + s + "\"";

    // Cagri grafi erisilebilirligi (koklerden BFS). Sanal cagrida hedefin tum alt-tip
    // override'lari da (ayni mangled ad, Owner alt tipi) erisilebilir sayilir (konservatif).
    // Donus: erisilen stub'lar + ilk bulunan cagri yolu (tani icin).
    static List<(Code Code, string Via)> ReachableStubs(Context ctx, IEnumerable<Code> roots)
    {
        var all = ctx.AllCodes.ToList();
        var byName = new Dictionary<string, List<Code>>();
        foreach (var c in all)
        {
            if (!byName.TryGetValue(c.Name, out var l)) byName[c.Name] = l = new List<Code>();
            l.Add(c);
        }
        static bool IsSubtype(Primitive t, Primitive of)
        {
            if (t == null || of == null) return false;
            var ofT = of.GenericTemplate ?? of;
            for (var p = t; p != null; p = p.Parent)
            {
                if ((p.GenericTemplate ?? p) == ofT) return true;
                foreach (var i in p.Interfaces)
                    if ((i.GenericTemplate ?? i) == ofT) return true;
            }
            return false;
        }
        var parent = new Dictionary<Code, Code>();
        var queue = new Queue<Code>();
        foreach (var r in roots) { if (r != null && parent.TryAdd(r, null)) queue.Enqueue(r); }
        void Visit(Code target, Code from)
        {
            if (target == null || !parent.TryAdd(target, from)) return;
            queue.Enqueue(target);
        }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var op in c.Operations)
            {
                if (op.Code == null) continue;
                Visit(op.Code, c);
                if (op.Type == OpType.CallVirtual && byName.TryGetValue(op.Code.Name, out var cands))
                    foreach (var cand in cands)
                        if (cand != op.Code && IsSubtype(cand.Owner, op.Code.Owner)) Visit(cand, c);
            }
        }
        var result = new List<(Code, string)>();
        foreach (var kv in parent)
        {
            if (kv.Key.UntranslatableReason == null) continue;
            var path = new List<string>();
            for (var p = kv.Value; p != null && path.Count < 6; p = parent[p]) path.Add($"{p.Owner?.Name}.{p.DisplayName}");
            result.Add((kv.Key, string.Join(" <- ", path)));
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Item1.Owner?.Name + a.Item1.Name, b.Item1.Owner?.Name + b.Item1.Name));
        return result;
    }

    // clang PATH'te olmayabilir (Windows); once PATH, sonra bilinen LLVM kurulum yollari.
    static string ClangPath()
    {
        var candidates = new[]
        {
            @"C:\Program Files\LLVM\bin\clang.exe",
            @"C:\Program Files (x86)\LLVM\bin\clang.exe",
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return "clang"; // PATH'e birak (Linux/macOS ya da PATH'te kuruluysa)
    }

    static (int exitCode, string output) RunProcess(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi);
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        var stderr = stderrTask.Result;
        return (p.ExitCode, stdout + stderr);
    }
}
