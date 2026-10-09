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
    static readonly string OutBin = Path.Combine(WorkDir, OperatingSystem.IsWindows() ? "selftest.exe" : "selftest"); // acik uzanti: uzantisiz eski dosya calismasin

    const string EngineProj = "engine/managed/DigitoyEngine.csproj";
    static readonly string EngineWorkDir = Path.Combine("obj", "engine-c");
    static readonly string EngineGeneratedC = Path.Combine(EngineWorkDir, "generated.c");
    static readonly string EngineOutBin = Path.Combine(EngineWorkDir, "DigitoyEngine.exe");

    public static int Main(string[] args)
    {
        try
        {
            // Tek meta (docs/modules.md): reflection descriptor'lari her build'de modul host verisidir; ayri export bayragi yok.
            // AOT_MODULES=1 yalniz selftest'e interp (vmint) adimini ekler (RunCilSelftest).

            // 'engine' modu: desktop engine exe'sini uret (kendi native host + sokol).
            if (args.Length > 0 && args[0] == "engine")
                return RunEngineBuild();

            // 'player <proje koku> [--target windows|android]' modu: oyunu derle. windows: tek native exe;
            // android: Android Studio projesi (<proje>/Build/android, kaynak + CMake; kullanici derler).
            if (args.Length > 0 && args[0] == "player")
            {
                string target = "windows";
                var app = new AppInfo();
                for (int i = 2; i + 1 < args.Length; i++)
                    switch (args[i])
                    {
                        case "--target": target = args[++i]; break;
                        case "--app-id": app.Id = args[++i]; break;
                        case "--app-name": app.Name = args[++i]; break;
                        case "--app-version": app.Version = args[++i]; break;
                        case "--orientation": app.Orientation = args[++i]; break;
                        case "--props": app.Props = ReadProps(args[++i]); break;
                    }
                return RunPlayerBuild(args.Length > 1 && !args[1].StartsWith("--") ? args[1] : Path.Combine(RepoRoot, "Projects", "Sandbox"), target, app);
            }

            // 'module <cikti.dmod> --bundled a.dll;b.dll [--provided c.dll;d.dll]' modu: dinamik modul IR'i (docs/modules.md Faz B).
            if (args.Length > 0 && args[0] == "module")
                return RunModuleBuild(args);

            // 'sdk-il <dir>' modu: publisher icin prebuilt IL (Digitoy.CoreLib.dll + DigitoyEngine.dll AOT).
            if (args.Length > 1 && args[0] == "sdk-il")
                return RunSdkIl(args[1]);

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

    // ---- Managed IL kaynaklari (docs/platform-hosts.md H2) ----
    // Uc assembly, hepsi in-process Roslyn (dotnet build / csproj YOK):
    //   Digitoy.CoreLib.dll   c_runtime/corelib/**.cs, NoStdLib, referanssiz          -> prebuilt (sdk/) ya da dev'de taze
    //   DigitoyEngine.dll     engine/managed/**.cs, NoStdLib, ref CoreLib, DE_AOT     -> prebuilt (sdk/) ya da dev'de taze
    //   <Ad>.Game.dll         <proje>/Assets/Scripts (Editor/ haric)                -> her build (kullanici makinesi; script yoksa yok)
    // KURULU MOD: aotcompiler.dll'in yaninda Digitoy.CoreLib.dll + DigitoyEngine.dll varsa (publisher sdk/) onlar kullanilir;
    // DEV MOD: repo kaynaklarindan derlenir, obj/aot-il/ altinda mtime cache.
    static readonly string AotIlDir = Path.Combine("obj", "aot-il");
    static string PrebuiltDir => AppContext.BaseDirectory;
    static bool Installed => File.Exists(Path.Combine(PrebuiltDir, "Digitoy.CoreLib.dll")) && !Directory.Exists(Path.Combine("c_runtime", "corelib"));
    // Hedef -> renderer define'i (Shader.cs GLSL profili). windows: GLCORE 410; mac: Metal; android/wasm: GLES3.
    static string RendererDefine(string target) => target switch
    {
        "android" or "wasm" => "DE_RENDERER_GLES3",
        "mac" or "ios" => "DE_RENDERER_METAL",
        _ => OperatingSystem.IsMacOS() ? "DE_RENDERER_METAL" : "DE_RENDERER_OPENGL",
    };
    // Hedefin platform define'lari: renderer + DE_DESKTOP (GLFW binding'leri yalniz masaustunde).
    static IEnumerable<string> TargetDefines(string target)
    {
        yield return RendererDefine(target);
        if (target == "windows" || target == "mac") yield return "DE_DESKTOP";
    }
    // Prebuilt engine IL hedefe gore: masaustu kokte DigitoyEngine.dll; digerleri DigitoyEngine.<target>.dll.
    static string EngineIlName(string target) => target == "windows" || target == "mac" ? "DigitoyEngine.dll" : $"DigitoyEngine.{target}.dll";

    static string CoreLibDll()
    {
        if (Installed) return Path.Combine(PrebuiltDir, "Digitoy.CoreLib.dll");
        string dll = Path.Combine(AotIlDir, "Digitoy.CoreLib.dll");
        var r = DigitoyEngine.Build.RoslynCompiler.CompileToFile(new DigitoyEngine.Build.RoslynBuild
        {
            AssemblyName = "Digitoy.CoreLib",
            SourceFiles = DigitoyEngine.Build.RoslynCompiler.SourcesUnder(Path.Combine("c_runtime", "corelib")),
            NoStdLib = true,
        }, dll, useCache: true);
        if (!r.Ok) throw new Exception("corelib derlemesi basarisiz (Roslyn):\n" + r.Errors);
        return dll;
    }

    static string EngineAotDll(string target)
    {
        if (Installed) return Path.Combine(PrebuiltDir, EngineIlName(target));
        string dll = Path.Combine(AotIlDir, EngineIlName(target));
        var defines = new List<string> { "DE_AOT" }; defines.AddRange(TargetDefines(target));
        var r = DigitoyEngine.Build.RoslynCompiler.CompileToFile(new DigitoyEngine.Build.RoslynBuild
        {
            AssemblyName = "DigitoyEngine",
            SourceFiles = DigitoyEngine.Build.RoslynCompiler.SourcesUnder(Path.Combine(RepoRoot, "engine", "managed")),
            References = { CoreLibDll() },
            Defines = defines.ToArray(),
            NoStdLib = true,
            Optimize = true,
        }, dll, useCache: true);
        if (!r.Ok) throw new Exception("engine AOT derlemesi basarisiz (Roslyn):\n" + r.Errors);
        return dll;
    }

    // Oyun: Assets/Scripts (Assets/**/Editor/ haric) -> <Ad>.Game.dll (DE_AOT, NoStdLib). Katalog runtime reflection (Registry yok).
    static string GameAotDll(string projectRoot, string projName, string outDir, string target)
    {
        // Registry.g.cs yok (docs/registry-removal.md): katalog runtime reflection'dan kurulur. Yalniz oyun scriptleri.
        var sources = DigitoyEngine.Build.RoslynCompiler.SourcesUnder(Path.Combine(projectRoot, "Assets", "Scripts"), "/Editor/");
        if (sources.Count == 0)
            return null; // script yok: engine tek basina
        var defines = new List<string> { "DE_AOT", "DE_GAME" }; defines.AddRange(TargetDefines(target));
        string dll = Path.Combine(outDir, projName + ".Game.dll");
        var r = DigitoyEngine.Build.RoslynCompiler.CompileToFile(new DigitoyEngine.Build.RoslynBuild
        {
            AssemblyName = projName + ".Game",
            SourceFiles = sources,
            References = { CoreLibDll(), EngineAotDll(target) },
            Defines = defines.ToArray(),
            NoStdLib = true,
            Optimize = true,
        }, dll, useCache: false);
        if (!r.Ok) throw new Exception("oyun kodu AOT derlemesi basarisiz:\n" + r.Errors);
        return dll;
    }

    // 'sdk-il <dir>': publisher icin prebuilt IL (CoreLib + Engine AOT, +pdb) uretir.
    static int RunSdkIl(string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var src in new[] { CoreLibDll(), EngineAotDll("windows"), EngineAotDll("android"), EngineAotDll("wasm") })
        {
            File.Copy(src, Path.Combine(outDir, Path.GetFileName(src)), true);
            string pdb = Path.ChangeExtension(src, ".pdb");
            if (File.Exists(pdb)) File.Copy(pdb, Path.Combine(outDir, Path.GetFileName(pdb)), true);
            Console.WriteLine("sdk-il: " + Path.GetFileName(src));
        }
        return 0;
    }

    static List<Code> LoadCoreLib(Context ctx)
    {
        string dll = CoreLibDll();
        var codes = CilFrontend.Compile(ctx, dll, out _, isCoreLib: true);
        if (CilFrontend.LastDiagnostics.Count > 0)
            Console.WriteLine($"[corelib] CIL tani: {CilFrontend.LastDiagnostics.Count} oge:\n  " + string.Join("\n  ", CilFrontend.LastDiagnostics));
        return codes;
    }

    // NIHAI URUN: engine (DE_AOT) + oyun (scriptler) -> CIL -> IR -> C -> clang; platform host
    // (c_runtime/host_desktop.c) + native (sokol + glfw + ses) STATIK linklenir. DigitoyPlayer assembly'si YOK.
    // Cikti: <proje>/Build/<ProjeAdi>.exe, game.pak'in yanina. Exe ".." = proje koku kabul eder.
    // Oyun ara urunleri PROJENIN icinde (Unity Library/ modeli; gitignore'lu, silinebilir): <proje>/Library/aot/<target>/
    // generated.c, <Ad>.Game.dll, diag.txt, nesne/arsiv cache'leri. aotcompiler/obj yalniz motora ait cache (aot-il) + selftest.
    // Teslim edilen urun yalniz <proje>/Build/ (exe, wasm/, android/).
    static string ProjectAotDir(string projectRoot, string target) => Path.Combine(projectRoot, "Library", "aot", target);

    static int RunPlayerBuild(string projectRoot, string target, AppInfo app)
    {
        projectRoot = Path.GetFullPath(projectRoot);
        string projName = Path.GetFileName(projectRoot.TrimEnd('\\', '/'));
        string workDir = ProjectAotDir(projectRoot, target);
        string generated = Path.Combine(workDir, "generated.c");
        string outExe = Path.Combine(projectRoot, "Build", projName + ".exe");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        void Lap(string what) { Console.WriteLine($"  [sure] {what}: {sw.Elapsed.TotalSeconds:F1} s"); sw.Restart(); }
        // 1) managed IL: corelib + engine (prebuilt ya da taze) + oyun (Roslyn, her seferinde)
        Directory.CreateDirectory(workDir);
        var engineDll = EngineAotDll(target);
        var playerDll = GameAotDll(projectRoot, projName, workDir, target);
        Console.WriteLine($"managed: {engineDll}\n         {playerDll ?? "(script yok)"}" + (Installed ? "  (kurulu mod: prebuilt sdk)" : ""));
        Lap("Roslyn (engine AOT + oyun)");

        // 2) CIL -> IR: corelib + engine + player (player engine tiplerini ctx'ten cozer)
        var ctx = new Context();
        var allCodes = new List<Code>();
        allCodes.AddRange(LoadCoreLib(ctx));
        Lap("corelib (Digitoy.CoreLib.dll, CIL)");
        var diag = new List<string>();
        allCodes.AddRange(CilFrontend.Compile(ctx, engineDll, out _));
        diag.AddRange(CilFrontend.LastDiagnostics.Select(d => "[engine] " + d));
        if (playerDll != null)
        {
            allCodes.AddRange(CilFrontend.Compile(ctx, playerDll, out _));
            diag.AddRange(CilFrontend.LastDiagnostics.Select(d => "[player] " + d));
        }
        Lap("CIL frontend (engine+player)");
        // Host sozlesmesi (docs/platform-hosts.md): Main KULLANILMAZ; platform host'u de_app.h uzerinden
        // GameHost.Init/Event/Frame/Pause/Resume/Shutdown'i surer (GC safepoint = de_app_frame icinde).
        Code HostCode(string name)
        {
            var hits = ctx.AllCodes.Where(c => c.Owner?.Name == "DigitoyEngine.GameHost" && !c.IsExternal
                && (c.Name == name || c.Name.StartsWith(name + "_", StringComparison.Ordinal))).ToList();
            if (hits.Count != 1) throw new Exception($"DigitoyEngine.GameHost.{name}: {hits.Count} aday (1 bekleniyor)");
            return hits[0];
        }
        var initCode = HostCode("Init");
        var eventCode = HostCode("Event");
        var frameCode = HostCode("Frame");
        var pauseCode = HostCode("Pause");
        var resumeCode = HostCode("Resume");
        var shutdownCode = HostCode("Shutdown");
        Directory.CreateDirectory(workDir);
        File.WriteAllLines(Path.Combine(workDir, "diag.txt"), diag);
        Console.WriteLine($"CIL tani: {diag.Count} oge atlandi/stub'landi -> {Path.Combine(workDir, "diag.txt")}");
        Resolver.ResolveAll(ctx, allCodes);
        Lap("resolve/monomorph");

        // STUB KAPISI: host girislerinden cagri grafiyla ERISILEBILEN stub =
        // runtime'da NotImplementedException demek -> build HATASI (exe cikmaz; liste basilir).
        // Erisilemeyen stub'lar (editor/import yolu, kullanilmayan API) diag.txt'de kalir.
        var hostEntries = new[] { initCode, eventCode, frameCode, pauseCode, resumeCode, shutdownCode };
        var reachableStubs = ReachableStubs(ctx, hostEntries);
        if (reachableStubs.Count > 0)
        {
            Console.WriteLine($"[HATA] runtime'dan erisilebilen {reachableStubs.Count} AOT stub (calisirken patlar):");
            foreach (var (code, via) in reachableStubs)
                Console.WriteLine($"  {code.Owner?.Name}.{code.DisplayName}\n      neden: {code.UntranslatableReason}\n      yol:   {via}");
            throw new Exception("AOT stub kapisi: eksik corelib/frontend destegi (yukaridaki liste); corelib'e ekleyin ya da motor kodunu yuzeye uydurun");
        }

        // 3) C transpile + de_app.h kopruleri: main/dongu/crash dizini artik platform host'unda
        //    (c_runtime/host_desktop.c + de_app.c). Unhandled exception yolu runtime'da (DIGITOYENGINE_dispatch).
        var cSource = CTranspiler.TranspileProgram(ctx);
        string Sym(Code c) => CTranspiler.CName(c.EncodeName());
        var entrySym = Sym(frameCode);
        cSource += "\n/* de_app.h kopruleri (platform host -> managed) */\n" +
            $"void de_managed_init(const char* root, int fbw, int fbh, float scale) {{ {Sym(initCode)}(digitoyengine_from_utf8(root), fbw, fbh, scale); }}\n" +
            $"int de_managed_frame(float dt, int fbw, int fbh, float scale) {{ return {Sym(frameCode)}(dt, fbw, fbh, scale) ? 1 : 0; }}\n" +
            $"void de_managed_event(int type, int id, float x, float y, int a, int b) {{ {Sym(eventCode)}(type, id, x, y, a, b); }}\n" +
            $"void de_managed_pause(void) {{ {Sym(pauseCode)}(); }}\n" +
            $"void de_managed_resume(void) {{ {Sym(resumeCode)}(); }}\n" +
            $"void de_managed_shutdown(void) {{ {Sym(shutdownCode)}(); }}\n";
        File.WriteAllText(generated, cSource);
        Console.WriteLine($"transpile -> {generated} ({cSource.Length} karakter, giris {entrySym})");
        Lap("C transpile + yaz");

        if (target == "android")
        {
            EmitAndroidProject(projectRoot, projName, generated, app);
            Lap("android projesi");
            return 0;
        }
        if (target == "wasm")
        {
            BuildWasm(projectRoot, projName, generated, app);
            Lap("emcc + link");
            return 0;
        }
        if (target != "windows")
            throw new Exception("bilinmeyen hedef: " + target + " (windows | android | wasm)");

        // 4) clang: uretilen C + runtime (vmrt/corelib/vmint) + platform host (de_app/host_desktop) + native
        //    (sokol+glfw+ses+de_fs) — hepsi KAYNAKTAN (prebuilt player native'i yok; docs/platform-hosts.md).
        //    Native kaynaklar obj/native-static/ altinda mtime cache'li arsive derlenir. DLL yok, P/Invoke yok.
        var staticLib = BuildNativeStaticLib(Path.Combine(workDir, "native-static"));
        var cFiles = new List<string> { generated };
        foreach (var n in new[] { "vmrt.c", "corelib.c", "vmint.c", "de_app.c", "host_desktop.c" })
            cFiles.Add(Path.Combine(CRuntimeDir, n));
        var defines = "-DSOKOL_GLCORE" + (Environment.GetEnvironmentVariable("AOT_GCPOISON") == "1" ? " -DDIGITOYENGINE_GC_POISON" : "");
        var libs = "-lopengl32 -lgdi32 -luser32 -lkernel32 -lshell32 -lole32 -loleaut32 -lmfplat -lmfuuid -luuid";
        Directory.CreateDirectory(Path.GetDirectoryName(outExe));
        // AOT_DEBUG: -O0 + sembol (lldb) + DIGITOYENGINE_DEBUG (STEP/local tablolari -> crash'te degisken dokumu)
        var dbg = Environment.GetEnvironmentVariable("AOT_DEBUG") == "1" ? "-O0 -g -gcodeview -DDIGITOYENGINE_DEBUG" : "-O1";
        var compileArgs = $"{dbg} -w {defines} {NativeIncludes} " +
            $"{string.Join(" ", cFiles.Select(Quote))} {Quote(staticLib)} {libs} -o {Quote(outExe)}";
        var (ccExit, ccOut) = RunProcess(ClangPath(), compileArgs);
        if (ccExit != 0) throw new Exception($"clang derleme/link hatasi (exit {ccExit}):\n{ccOut}");
        Console.WriteLine($"clang ok -> {outExe} ({new FileInfo(outExe).Length / 1024} KB)");
        Lap("clang + link");
        return 0;
    }

    // ---- Android (docs/platform-hosts.md H5): Android Studio projesi, kaynak + CMake; kullanici derler ----
    // <proje>/Build/android/
    //   generated/   HER build silinip yazilir: cpp/{generated.c, c_runtime/, native/, CMakeLists.txt}, java/com/digitoy/host/*.kt, assets/game.pak,
    //                app.properties (Player Settings -> Gradle), res/ (adaptive launcher icon)
    //   keystore.properties   imza varsa her build (parola icerir; kabuk .gitignore'unda), yoksa silinir
    //   app/, settings.gradle.kts, build.gradle.kts, gradle.properties, gradle/wrapper, shell.version   ILK uretimde sablondan, sonra DOKUNULMAZ
    //                (kabuk statik: degisken her sey generated/app.properties'ten okunur)
    static string PlatformsDir => Installed ? Path.GetFullPath(Path.Combine(PrebuiltDir, "..", "platforms")) : "platforms";

    // Yayin kimligi (editor PlayerSettings'ten --app-* ile gelir; CLI'dan verilmezse proje adindan turetilir).
    // Props: Android'e ozel ayarlar (editor AndroidBuildProps.Write -> --props; key=value). null = CLI/varsayilan.
    sealed class AppInfo { public string Id, Name, Version = "1.0", Orientation = "landscape"; public Dictionary<string, string> Props; }

    static Dictionary<string, string> ReadProps(string path)
    {
        if (!File.Exists(path)) throw new Exception("props dosyasi yok: " + path);
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            d[line[..eq].Trim()] = line[(eq + 1)..].Replace("\\n", "\n");
        }
        return d;
    }

    // Kabuk surumu: sablon degisince artar; eski kabuk tespit edilip kullanici uyarilir (dosyalarina dokunulmaz).
    const int AndroidShellVersion = 2;

    static void EmitAndroidProject(string projectRoot, string projName, string generatedC, AppInfo app)
    {
        string tpl = Path.Combine(PlatformsDir, "android");
        if (!Directory.Exists(tpl)) throw new Exception("android sablonu yok: " + tpl);
        string root = Path.Combine(projectRoot, "Build", "android");
        string gen = Path.Combine(root, "generated");

        // Ayarlari onceden dogrula: hata varsa generated/ silinmeden cik (eski proje calisir kalir).
        var props = app.Props ?? new Dictionary<string, string>();
        string P(string k, string def = "") => props.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : def;
        string appId = P("applicationId", app.Id);
        if (string.IsNullOrWhiteSpace(appId) || appId == "com.defaultcompany.game")
        {
            appId = "com.digitoy." + new string(projName.ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());
            if (appId.EndsWith(".")) appId += "game";
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(appId, @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$"))
            throw new Exception($"android: gecersiz applicationId '{appId}' (Player Settings > Bundle Identifier / android.packageName; ornek com.sirket.oyun)");
        string appName = P("appName", app.Name);
        if (string.IsNullOrWhiteSpace(appName) || appName == "Game") appName = projName;
        string versionName = P("versionName", app.Version);
        if (!int.TryParse(P("versionCode", "1"), out int versionCode) || versionCode <= 0)
            throw new Exception("android: versionCode pozitif tam sayi olmali (Player Settings > android.versionCode)");
        if (!int.TryParse(P("targetSdk", "35"), out int targetSdk) || targetSdk < 26)
            throw new Exception("android: targetSdk >= 26 olmali (minSdk 26: AAudio)");
        string abis = P("abis", "arm64-v8a,x86_64");
        string orientation = P("orientation", app.Orientation) switch { "portrait" => "sensorPortrait", "auto" => "fullSensor", _ => "sensorLandscape" };

        // Imza: hepsi ya da hicbiri. Parolalar: props (editor UserSettings) < ortam degiskeni (CI).
        string storeFile = P("signing.storeFile");
        string keyAlias = P("signing.keyAlias");
        string storePass = Environment.GetEnvironmentVariable("DE_ANDROID_KEYSTORE_PASS") ?? P("signing.storePassword");
        string keyPass = Environment.GetEnvironmentVariable("DE_ANDROID_KEY_PASS") ?? P("signing.keyPassword");
        if (string.IsNullOrEmpty(keyPass)) keyPass = storePass;
        bool signing = storeFile.Length > 0;
        if (signing)
        {
            if (!File.Exists(storeFile)) throw new Exception("android: keystore dosyasi yok: " + storeFile + " (Player Settings > android.keystorePath)");
            if (keyAlias.Length == 0) throw new Exception("android: keystore verildi ama keyAlias bos (Player Settings > android.keyAlias)");
            if (string.IsNullOrEmpty(storePass)) throw new Exception("android: keystore parolasi yok (Player Settings > Android Signing ya da DE_ANDROID_KEYSTORE_PASS)");
        }
        string iconFg = P("iconForeground");
        if (iconFg.Length > 0 && !File.Exists(iconFg)) throw new Exception("android: iconForeground dosyasi yok: " + iconFg);
        string iconBg = P("iconBackground", "#1E1E1E");
        bool bgIsColor = iconBg.StartsWith("#");
        if (bgIsColor && !System.Text.RegularExpressions.Regex.IsMatch(iconBg, "^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$"))
            throw new Exception("android: iconBackground rengi #RRGGBB olmali: " + iconBg);
        if (!bgIsColor && !File.Exists(iconBg)) throw new Exception("android: iconBackground dosyasi yok: " + iconBg);

        if (Directory.Exists(gen)) Directory.Delete(gen, true);

        // cpp: generated.c + runtime (host_desktop haric) + native shim'ler (glfw yok) + CMake
        string cpp = Path.Combine(gen, "cpp");
        Directory.CreateDirectory(Path.Combine(cpp, "c_runtime"));
        Directory.CreateDirectory(Path.Combine(cpp, "native"));
        File.Copy(generatedC, Path.Combine(cpp, "generated.c"), true);
        foreach (var f in Directory.GetFiles(CRuntimeDir))
        {
            string n = Path.GetFileName(f);
            if (n == "host_desktop.c" || !(n.EndsWith(".c") || n.EndsWith(".h"))) continue;
            File.Copy(f, Path.Combine(cpp, "c_runtime", n), true);
        }
        foreach (var f in Directory.GetFiles(EngineNativeDir))
        {
            string n = Path.GetFileName(f);
            if (!(n.EndsWith(".c") || n.EndsWith(".h"))) continue;
            File.Copy(f, Path.Combine(cpp, "native", n), true);
        }
        CopyTree(Path.Combine(EngineNativeDir, "sokol"), Path.Combine(cpp, "native", "sokol"));
        File.Copy(Path.Combine(tpl, "CMakeLists.txt"), Path.Combine(cpp, "CMakeLists.txt"), true);

        // java: host (her build taze — engine'e ait), assets: game.pak
        CopyTree(Path.Combine(tpl, "java"), Path.Combine(gen, "java"));
        // assets: game.pak -> assets/Build/game.pak (GameHost "<root>/Build/game.pak"; root = "asset:" -> AAssetManager fd+offset, kopya yok)
        string pak = Path.Combine(projectRoot, "Build", "game.pak");
        if (!File.Exists(pak)) throw new Exception("game.pak yok (once asset pack): " + pak);
        Directory.CreateDirectory(Path.Combine(gen, "assets", "Build"));
        File.Copy(pak, Path.Combine(gen, "assets", "Build", "game.pak"), true);

        // app.properties: kabuk Gradle her build okur (applicationId/versionCode/versionName/targetSdk/abis + manifest placeholder'lari).
        File.WriteAllText(Path.Combine(gen, "app.properties"),
            "# DigitoyEngine: her build yeniden yazilir (Player Settings). Elle duzenlemeyin; app/build.gradle.kts okur.\n" +
            $"applicationId={appId}\nappName={PropEscape(appName)}\nversionName={PropEscape(versionName)}\nversionCode={versionCode}\n" +
            $"targetSdk={targetSdk}\nabis={abis}\norientation={orientation}\n");

        // res: adaptive launcher icon (minSdk 26 -> yalniz anydpi-v26; PNG yeniden boyutlandirma yok).
        string res = Path.Combine(gen, "res");
        Directory.CreateDirectory(Path.Combine(res, "mipmap-anydpi-v26"));
        Directory.CreateDirectory(Path.Combine(res, "drawable"));
        Directory.CreateDirectory(Path.Combine(res, "values"));
        if (iconFg.Length > 0) File.Copy(iconFg, Path.Combine(res, "drawable", "ic_launcher_foreground.png"), true);
        else File.Copy(Path.Combine(tpl, "res", "ic_launcher_foreground.xml"), Path.Combine(res, "drawable", "ic_launcher_foreground.xml"), true);
        string bgRef;
        if (bgIsColor)
        {
            File.WriteAllText(Path.Combine(res, "values", "ic_launcher_background.xml"),
                $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<resources>\n    <color name=\"ic_launcher_background\">{iconBg}</color>\n</resources>\n");
            bgRef = "@color/ic_launcher_background";
        }
        else
        {
            File.Copy(iconBg, Path.Combine(res, "drawable", "ic_launcher_background.png"), true);
            bgRef = "@drawable/ic_launcher_background";
        }
        string iconXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<adaptive-icon xmlns:android=\"http://schemas.android.com/apk/res/android\">\n" +
            $"    <background android:drawable=\"{bgRef}\" />\n    <foreground android:drawable=\"@drawable/ic_launcher_foreground\" />\n</adaptive-icon>\n";
        File.WriteAllText(Path.Combine(res, "mipmap-anydpi-v26", "ic_launcher.xml"), iconXml);
        File.WriteAllText(Path.Combine(res, "mipmap-anydpi-v26", "ic_launcher_round.xml"), iconXml);

        // keystore.properties: kabuk kokunde (Gradle konvansiyonu; kabuk .gitignore'unda). Imza yoksa eski dosya kalmasin.
        string ksProps = Path.Combine(root, "keystore.properties");
        if (signing)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(ksProps,
                "# DigitoyEngine: her build yeniden yazilir; PAROLA ICERIR, git'e girmez (.gitignore).\n" +
                $"storeFile={PropEscape(storeFile)}\nstorePassword={PropEscape(storePass)}\nkeyAlias={PropEscape(keyAlias)}\nkeyPassword={PropEscape(keyPass)}\n");
        }
        else if (File.Exists(ksProps))
            File.Delete(ksProps);

        // kabuk: yalniz yoksa (kullaniciya ait)
        string shellVersionFile = Path.Combine(root, "shell.version");
        if (!File.Exists(Path.Combine(root, "settings.gradle.kts")))
        {
            foreach (var f in Directory.GetFiles(Path.Combine(tpl, "shell"), "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(Path.Combine(tpl, "shell"), f);
                string to = Path.Combine(root, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.WriteAllText(to, File.ReadAllText(f).Replace("{{APP_NAME}}", appName));
            }
            File.WriteAllText(shellVersionFile, AndroidShellVersion.ToString());
            Console.WriteLine($"android kabugu olusturuldu: {root}");
        }
        else
        {
            int have = File.Exists(shellVersionFile) && int.TryParse(File.ReadAllText(shellVersionFile).Trim(), out int v) ? v : 1;
            if (have < AndroidShellVersion)
                Console.WriteLine($"[uyari] android kabugu eski (v{have} < v{AndroidShellVersion}): generated/app.properties, keystore.properties ve ikon okunmaz. " +
                    $"Ozel degisikliginiz yoksa {root} altindaki kabuk dosyalarini (generated/ haric) silip yeniden build edin.");
            else
                Console.WriteLine("android kabugu mevcut, dokunulmadi: " + root);
        }
        Console.WriteLine($"android projesi hazir -> {root}  (applicationId {appId}, v{versionName} ({versionCode}), {abis}, " +
            (signing ? $"imza: {Path.GetFileName(storeFile)}/{keyAlias}" : "imza: debug") + ")  Android Studio: Open -> bu klasor -> Run");
    }

    // java.util.Properties: '\' kacis karakteri -> yollar '/' ile; satir sonu kacislanir.
    static string PropEscape(string v) => v.Replace("\\", "/").Replace("\r", "").Replace("\n", "\\n");

    static void CopyTree(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(f, to, true);
        }
    }

    // ---- wasm32 (docs/platform-hosts.md H4): emscripten ile editorde derlenir (toolchain indirilir, EmsdkToolchain) ----
    // <proje>/Build/wasm/
    //   game.js + game.wasm + game.data   HER build (emcc; game.data = --preload-file game.pak)
    //   index.html                        ILK uretimde sablondan (platforms/wasm/shell), sonra DOKUNULMAZ (kullaniciya ait)
    // Nesneler <proje>/Library/aot/wasm/obj/ altinda: generated.c her build, runtime/shim'ler mtime cache. Tarayici file:// ile wasm yuklemez:
    // Build/wasm/ bir HTTP sunucudan servis edilmeli (editor "Run in Browser" ya da `python -m http.server`).
    static void BuildWasm(string projectRoot, string projName, string generatedC, AppInfo app)
    {
        string pak = Path.Combine(projectRoot, "Build", "game.pak");
        if (!File.Exists(pak)) throw new Exception("game.pak yok (once asset pack): " + pak);
        string tpl = Path.Combine(PlatformsDir, "wasm");
        if (!Directory.Exists(tpl)) throw new Exception("wasm sablonu yok: " + tpl);
        string outDir = Path.Combine(projectRoot, "Build", "wasm");
        Directory.CreateDirectory(outDir);
        string objDir = Path.Combine(ProjectAotDir(projectRoot, "wasm"), "obj");
        Directory.CreateDirectory(objDir);

        DigitoyEngine.Build.EmsdkToolchain.Ensure(Console.WriteLine);
        bool debug = Environment.GetEnvironmentVariable("AOT_DEBUG") == "1";
        string common = $"-w -DSOKOL_GLES3 {NativeIncludes}" + (debug ? " -g -DDIGITOYENGINE_DEBUG" : "");
        string stamp = Path.Combine(objDir, ".toolchain");
        bool sameToolchain = File.Exists(stamp) && File.ReadAllText(stamp) == DigitoyEngine.Build.EmsdkToolchain.Key + common;

        // (kaynak, ek bayrak). generated.c -O1 (cok buyuk, derleme suresi); runtime/shim -O2. Shim'ler SOKOL_IMPL ile.
        var units = new List<(string src, string flags, bool cache)>();
        units.Add((generatedC, debug ? "-O0" : "-O1", false));
        foreach (var n in new[] { "vmrt.c", "corelib.c", "vmint.c", "de_app.c", "host_wasm.c" })
            units.Add((Path.Combine(CRuntimeDir, n), "-O2", true));
        foreach (var n in new[] { "sokol_shim.c", "audio_shim.c", "de_fs.c" })
            units.Add((Path.Combine(EngineNativeDir, n), "-O2 -DSOKOL_IMPL", true));
        var headers = Directory.GetFiles(EngineNativeDir, "*.h", SearchOption.AllDirectories).Concat(Directory.GetFiles(CRuntimeDir, "*.h")).ToList();
        DateTime newestHeader = headers.Count > 0 ? headers.Max(File.GetLastWriteTimeUtc) : DateTime.MinValue;

        var objs = new List<string>();
        int compiled = 0;
        foreach (var (src, flags, cache) in units)
        {
            string obj = Path.Combine(objDir, Path.GetFileNameWithoutExtension(src) + ".o");
            objs.Add(obj);
            if (cache && sameToolchain && File.Exists(obj) && File.GetLastWriteTimeUtc(obj) >= File.GetLastWriteTimeUtc(src) && File.GetLastWriteTimeUtc(obj) >= newestHeader)
                continue;
            var (e, o) = RunEmcc($"{common} {flags} -c {Quote(src)} -o {Quote(obj)}");
            if (e != 0) throw new Exception($"emcc derleme basarisiz: {src}\n{o}");
            compiled++;
        }
        File.WriteAllText(stamp, DigitoyEngine.Build.EmsdkToolchain.Key + common);
        Console.WriteLine($"emcc: {compiled}/{units.Count} birim derlendi");

        // Link: WebGL2, bellek buyur, setjmp/longjmp (exception = longjmp + shadow stack), pak MEMFS'e gomulu (game.data).
        string outJs = Path.Combine(outDir, "game.js");
        string link = (debug ? "-g -sASSERTIONS=1" : "-O2") +
            " -sUSE_WEBGL2=1 -sMIN_WEBGL_VERSION=2 -sMAX_WEBGL_VERSION=2 -sALLOW_MEMORY_GROWTH=1 -sINITIAL_MEMORY=64MB -sSTACK_SIZE=8MB" +
            " -sSUPPORT_LONGJMP=emscripten -sENVIRONMENT=web -sEXIT_RUNTIME=0 -sMODULARIZE=0 -sEXPORT_NAME=Module" +
            " -sEXPORTED_RUNTIME_METHODS=UTF8ToString" +
            $" --preload-file {Quote(pak + "@/data/Build/game.pak")}" +
            $" {string.Join(" ", objs.Select(Quote))} -o {Quote(outJs)}";
        var (le, lo) = RunEmcc(link);
        if (le != 0) throw new Exception($"emcc link hatasi (exit {le}):\n{lo}");
        Console.WriteLine($"emcc ok -> {Path.Combine(outDir, "game.wasm")} ({new FileInfo(Path.Combine(outDir, "game.wasm")).Length / 1024} KB wasm, {new FileInfo(Path.Combine(outDir, "game.data")).Length / 1024} KB data)");

        // kabuk: yalniz yoksa (kullaniciya ait)
        string index = Path.Combine(outDir, "index.html");
        if (!File.Exists(index))
        {
            string appName = string.IsNullOrWhiteSpace(app.Name) || app.Name == "Game" ? projName : app.Name;
            foreach (var f in Directory.GetFiles(Path.Combine(tpl, "shell"), "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(outDir, Path.GetRelativePath(Path.Combine(tpl, "shell"), f));
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.WriteAllText(to, File.ReadAllText(f).Replace("{{APP_NAME}}", appName).Replace("{{APP_VERSION}}", app.Version));
            }
            Console.WriteLine($"wasm kabugu olusturuldu: {index}");
        }
        else
            Console.WriteLine("wasm kabugu mevcut, dokunulmadi: " + index);
        Console.WriteLine($"wasm hazir -> {outDir}  (HTTP sunucudan acin; file:// calismaz)");
    }

    static (int exitCode, string output) RunEmcc(string args)
    {
        var psi = DigitoyEngine.Build.EmsdkToolchain.Emcc(args);
        using var p = Process.Start(psi);
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout + stderrTask.Result);
    }

    // ---- Native kaynak yerlesimi (docs/editor-distribution.md "Paket duzeni") ----
    // DEV: aotcompiler/c_runtime + <repo>/engine/native.  KURULU: <sdk>/native/c_runtime + <sdk>/native/engine
    // (EditorPublisher NativeSources adimi kopyalar). Player native'i her iki modda da KAYNAKTAN derlenir.
    static string SdkNativeDir => Path.GetFullPath(Path.Combine(PrebuiltDir, "..", "native"));
    static string CRuntimeDir => Installed ? Path.Combine(SdkNativeDir, "c_runtime") : "c_runtime";
    static string EngineNativeDir => Installed ? Path.Combine(SdkNativeDir, "engine") : Path.Combine(RepoRoot, "engine", "native");
    static string NativeIncludes => $"-I{Quote(CRuntimeDir)} -I{Quote(EngineNativeDir)} -I{Quote(Path.Combine(EngineNativeDir, "glfw-master", "include"))}";

    // engine/native/build_native_static.cmd'nin esdegeri, aotcompiler icinde (cmd/sh bagimliligi yok; kurulu modda da calisir):
    // sokol_shim + audio_shim + de_fs + GLFW (Win32) -> <outDir>/digitoyengine_native_static.a. Kaynak/header degistiyse yeniden.
    static readonly string[] GlfwWin32Sources = { "context", "init", "input", "monitor", "platform", "vulkan", "window", "win32_init", "win32_joystick",
        "win32_module", "win32_monitor", "win32_time", "win32_thread", "win32_window", "wgl_context", "egl_context", "osmesa_context",
        "null_init", "null_joystick", "null_monitor", "null_window" };

    static string BuildNativeStaticLib(string outDir)
    {
        string lib = Path.Combine(outDir, "digitoyengine_native_static.a");
        string clang = ClangPath();
        // Cache anahtari: kaynak/header mtime'lari + TOOLCHAIN (MSVC-hedef clang ile mingw nesneleri karismasin: __chkstk/_fltused).
        string stamp = Path.Combine(outDir, ".toolchain");
        var sources = new List<string>();
        foreach (var n in new[] { "sokol_shim.c", "audio_shim.c", "de_fs.c" }) sources.Add(Path.Combine(EngineNativeDir, n));
        foreach (var g in GlfwWin32Sources) sources.Add(Path.Combine(EngineNativeDir, "glfw-master", "src", g + ".c"));
        var inputs = sources.Concat(Directory.GetFiles(EngineNativeDir, "*.h", SearchOption.AllDirectories)).ToList();
        bool sameToolchain = File.Exists(stamp) && File.ReadAllText(stamp) == clang;
        if (sameToolchain && File.Exists(lib) && inputs.All(f => File.Exists(f) && File.GetLastWriteTimeUtc(f) <= File.GetLastWriteTimeUtc(lib)))
            return lib;
        Console.WriteLine($"native statik lib eski/yok -> {sources.Count} kaynak derleniyor ({EngineNativeDir})");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);
        string ar = Path.Combine(Path.GetDirectoryName(clang) ?? "", "llvm-ar" + Path.GetExtension(clang));
        if (!File.Exists(ar)) ar = "llvm-ar";
        var objs = new List<string>();
        foreach (var src in sources)
        {
            string obj = Path.Combine(outDir, Path.GetFileNameWithoutExtension(src) + ".o");
            var (e, o) = RunProcess(clang, $"-O2 -w -c -DSOKOL_GLCORE -D_GLFW_WIN32 -DSOKOL_IMPL {NativeIncludes} {Quote(src)} -o {Quote(obj)}");
            if (e != 0) throw new Exception($"native derleme basarisiz: {src}\n{o}");
            objs.Add(obj);
        }
        if (File.Exists(lib)) File.Delete(lib);
        var (ae, ao) = RunProcess(ar, $"rcs {Quote(lib)} {string.Join(" ", objs.Select(Quote))}");
        if (ae != 0) throw new Exception($"native arsiv olusturulamadi:\n{ao}");
        File.WriteAllText(stamp, clang);
        return lib;
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
        var info = "dmod v" + ModuleWriter.Version + "; " + string.Join(", ", provided.Select(p => Path.GetFileNameWithoutExtension(p) + " " + AssemblyVersionOf(p)));
        var bytes = ModuleWriter.Write(ctx, bundledNames, null, info, out var report);
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

    static string AssemblyVersionOf(string dll)
    {
        using var pe = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(dll));
        var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        return md.GetAssemblyDefinition().Version.ToString();
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
        bool interpTest = Environment.GetEnvironmentVariable("AOT_MODULES") == "1"; // selftest.dmod yaz + vmint ile yorumla
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
            if (interpTest)
            {
                Directory.CreateDirectory(WorkDir);
                var dmod = Path.Combine(WorkDir, "selftest.dmod");
                var bytes = ModuleWriter.Write(ctx, new[] { AssemblyNameOf(dll) }, entry, "selftest dmod v" + ModuleWriter.Version, out var report);
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
            if (!interpTest) return aotResult;

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
                "    char err[512]; VmModule *m = vmint_load(buf, (int)n, \"selftest\", err, sizeof err);\n" +
                "    if (!m) { fprintf(stderr, \"[interp] yukleme hatasi: %s\\n\", err); return 3; }\n" +
                "    fprintf(stderr, \"[interp] yuklendi; entry kosuyor\\n\");\n" +
                "    vmint_run_entry(m);\n" +
                "    /* fault izolasyonu: host'ta handler yokken stub metot (NotImplemented) -> crash degil FAULT */\n" +
                "    vmint_call(m, \"Demo47.App47$Run\", 0, 0);\n" +
                "    fprintf(stderr, \"[interp] fault testi: faulted=%d (1 bekleniyor)\\n\", vmint_faulted(m));\n" +
                "    vmint_unload(m); gc_major(); gc_major(); /* ilk dongu surmekte olabilir (yeni nesneler siyah dogar) */\n" +
                "    fprintf(stderr, \"[interp] unload: live=%d, collect=%d\\n\", vmint_live(m), vmint_collect());\n" +
                "    return 0;\n}\n";
            var hostC = Path.Combine(WorkDir, "interp_host.c");
            var hostBin = Path.Combine(WorkDir, OperatingSystem.IsWindows() ? "interp_host.exe" : "interp_host"); // acik uzanti: eski uzantisiz dosya calismasin
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
    // Windows: llvm-mingw (kullanici dizini; yoksa indirilir — docs/editor-distribution.md). AOT_CLANG=<yol> ile ezilebilir
    // (dev deneyleri, MSVC-hedef clang). Diger OS: PATH'teki clang (macOS: Xcode).
    static string ClangPath()
    {
        var env = Environment.GetEnvironmentVariable("AOT_CLANG");
        if (!string.IsNullOrEmpty(env)) return env;
        if (OperatingSystem.IsWindows())
        {
            DigitoyEngine.Build.MingwToolchain.Ensure(Console.WriteLine);
            return DigitoyEngine.Build.MingwToolchain.Clang;
        }
        return "clang";
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
