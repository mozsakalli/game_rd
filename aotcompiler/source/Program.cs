using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DigitoyEngine.Language;
using DigitoyEngine.Cil;
using DigitoyEngine.Frontend;

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
            // 'engine' modu: desktop engine exe'sini uret (kendi native host + sokol).
            if (args.Length > 0 && args[0] == "engine")
                return RunEngineBuild();

            // 'player <proje koku>' modu: oyunu tek native exe'ye derle (release urunu).
            if (args.Length > 0 && args[0] == "player")
                return RunPlayerBuild(args.Length > 1 ? args[1] : Path.Combine(RepoRoot, "Projects", "Sandbox"));

            // Varsayilan: CIL frontend selftest'i (derlenmis DLL -> IR). MiniCs source
            // frontend'i deprecate edildi (kod source/frontend/ altinda duruyor ama yol KALDIRILDI).
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

        // 2) CIL -> IR: corelib + engine + player (player engine tiplerini ctx'ten cozer)
        var ctx = new Context();
        var allCodes = new List<Code>();
        allCodes.AddRange(Prelude.Compile(ctx));
        var diag = new List<string>();
        allCodes.AddRange(CilFrontend.Compile(ctx, engineDll, out _));
        diag.AddRange(CilFrontend.LastDiagnostics.Select(d => "[engine] " + d));
        allCodes.AddRange(CilFrontend.Compile(ctx, playerDll, out var entry));
        diag.AddRange(CilFrontend.LastDiagnostics.Select(d => "[player] " + d));
        if (entry == null) throw new Exception("DigitoyPlayer giris noktasi (Main) bulunamadi");
        Directory.CreateDirectory(workDir);
        File.WriteAllLines(Path.Combine(workDir, "diag.txt"), diag);
        Console.WriteLine($"CIL tani: {diag.Count} oge atlandi/stub'landi -> {Path.Combine(workDir, "diag.txt")}");
        Resolver.ResolveAll(ctx, allCodes);

        // 3) C transpile + main
        var cSource = CTranspiler.TranspileProgram(ctx);
        var entrySym = CTranspiler.CName(entry.EncodeName());
        cSource += $"\nint main(void) {{\n    digitoyengine_init();\n    {entrySym}();\n    return 0;\n}}\n";
        File.WriteAllText(generated, cSource);
        Console.WriteLine($"transpile -> {generated} ({cSource.Length} karakter, giris {entrySym})");

        // 4) clang: uretilen C + runtime + native (build_editor.cmd ile ayni kaynak listesi, statik)
        var native = Path.Combine(RepoRoot, "engine", "native");
        var glfw = Path.Combine(native, "glfw-master", "src");
        var cFiles = new List<string> { generated, "c_runtime/vmrt.c", "c_runtime/corelib.c",
            Path.Combine(native, "sokol_shim.c"), Path.Combine(native, "audio_shim.c") };
        foreach (var g in new[] { "context", "init", "input", "monitor", "platform", "vulkan", "window",
            "win32_init", "win32_joystick", "win32_module", "win32_monitor", "win32_time", "win32_thread",
            "win32_window", "wgl_context", "egl_context", "osmesa_context",
            "null_init", "null_joystick", "null_monitor", "null_window" })
            cFiles.Add(Path.Combine(glfw, g + ".c"));
        var defines = "-DSOKOL_GLCORE -D_GLFW_WIN32 -DSOKOL_IMPL";
        var libs = "-lopengl32 -lgdi32 -luser32 -lkernel32 -lshell32 -lole32 -loleaut32 -lmfplat -lmfuuid";
        Directory.CreateDirectory(Path.GetDirectoryName(outExe));
        var compileArgs = $"-O1 -w {defines} -Ic_runtime -I{Quote(native)} " +
            $"{string.Join(" ", cFiles.Select(Quote))} {libs} -o {Quote(outExe)}";
        var (ccExit, ccOut) = RunProcess(ClangPath(), compileArgs);
        if (ccExit != 0) throw new Exception($"clang derleme/link hatasi (exit {ccExit}):\n{ccOut}");
        Console.WriteLine($"clang ok -> {outExe} ({new FileInfo(outExe).Length / 1024} KB)");
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
        var corelib = Prelude.Compile(ctx);
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
            var corelib = Prelude.Compile(ctx);
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

            // 3) C transpile + main sarmalayici
            var cSource = CTranspiler.TranspileProgram(ctx);
            var entrySym = CTranspiler.CName(entry.EncodeName());
            cSource += $"\nint main(void) {{\n    digitoyengine_init();\n    {entrySym}();\n    return 0;\n}}\n";
            Directory.CreateDirectory(WorkDir);
            File.WriteAllText(GeneratedC, cSource);
            Console.WriteLine($"transpile -> {GeneratedC} ({cSource.Length} karakter, giris {entrySym})");

            // 4) clang derle
            var cFiles = new[] { GeneratedC, "c_runtime/vmrt.c", "c_runtime/corelib.c" };
            var compileArgs = $"-O1 -w -Ic_runtime {string.Join(" ", cFiles.Select(Quote))} -o {Quote(OutBin)}";
            var (ccExit, ccOut) = RunProcess(ClangPath(), compileArgs);
            if (ccExit != 0) throw new Exception($"clang derleme hatasi (exit {ccExit}):\n{ccOut}");
            Console.WriteLine("clang derleme ok");

            // 5) calistir + karsilastir
            var actual = RunProcess(Path.GetFullPath(OutBin), "").output;
            var actualMap = ParseResults(actual);
            return Diff(baseMap, actualMap);
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