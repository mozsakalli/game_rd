using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DigitoyEngine.Build;

namespace DigitoyPublisher;

// Editorun kendini publish etmesi — docs/editor-distribution.md.
// Parametre YOK. Host OS + kurulu toolchain + mevcut cikti durumundan ne yapilacagi turetilir.
// Her bilesen kapali birimdir: kendi manifest.json'u (girdi hash'i, toolchain, define seti, VERSION).
// "Yok ya da bayat" bilesen bu host uretebiliyorsa uretilir, uretemiyorsa kok manifest'e eksik yazilir.
// Idempotent: tekrar kosmak zararsiz; baska makinede uretilen klasorleri ustune kopyalamak = birlestirme.
static class Program
{
    static string RepoRoot;
    static string Version;
    static string Pkg;        // <kok>/DigitoyEditor-<VERSION>
    static readonly List<string> Missing = new();
    static readonly List<string> Done = new();
    static readonly List<string> Skipped = new();

    static int Main()
    {
        try
        {
            RepoRoot = FindRepoRoot();
            Version = File.ReadAllText(Path.Combine(RepoRoot, "VERSION")).Trim();
            string root = Environment.GetEnvironmentVariable("DIGITOY_PUBLISH_DIR");
            if (string.IsNullOrWhiteSpace(root))
                root = OperatingSystem.IsWindows()
                    ? Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "DigitoyPublish")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DigitoyPublish");
            Pkg = Path.Combine(root, Version, "DigitoyEditor-" + Version);
            Directory.CreateDirectory(Pkg);
            Log($"repo: {RepoRoot}\nsurum: {Version}\ncikti: {Pkg}\nhost: {Rid()}");

            // CEKIRDEK: managed + editorun acilmasi icin sart olan tek native DLL. Platform Support modulleri
            // (toolchain + prebuilt statik lib'ler) ayri paketler (docs/editor-distribution.md) — bu adimda yok.
            Step("templates", Templates);
            Step("managed", Managed);
            Step("native sources", NativeSources);
            Step("platforms", Platforms);
            if (OperatingSystem.IsWindows())
            {
                Step("toolchain (kullanici dizini)", Toolchain); // yalniz publisher'in kendi derlemesi icin; pakete GIRMEZ
                Step("editor native (win)", EditorNativeWin);
            }
            else
                Missing.Add("editor native digitoyengine_native.dll: Windows host");
            if (OperatingSystem.IsMacOS())
                Step("editor native (mac)", NativeMac);
            else
                Missing.Add("editor native libdigitoyengine_native.dylib: macOS host");

            RootManifest();
            Log("\n== ozet ==");
            foreach (var d in Done) Log("  uretildi : " + d);
            foreach (var s in Skipped) Log("  guncel   : " + s);
            foreach (var m in Missing) Log("  eksik    : " + m);
            Log($"\nOK -> {Pkg}");
            return 0;
        }
        catch (Exception e)
        {
            Log("[HATA] " + e.Message);
            return 1;
        }
    }

    // ---------------- bilesenler ----------------

    // Player/host native KAYNAKLARI (prebuilt YOK; kullanici makinesinde aotcompiler clang ile derler):
    //   sdk/native/c_runtime/  vmrt/corelib/vmint/de_app/host_desktop (.c/.h)
    //   sdk/native/engine/     sokol_shim/audio_shim/de_fs (+ stb/dr header'lari), sokol/, glfw-master/{include,src}
    // Yerlesim aotcompiler SdkNativeDir ile birebir (Program.cs CRuntimeDir/EngineNativeDir).
    static void NativeSources()
    {
        string dst = Path.Combine(Pkg, "sdk", "native");
        var map = new List<(string src, string rel)>();
        string cr = Path.Combine(RepoRoot, "aotcompiler", "c_runtime");
        foreach (var f in Directory.GetFiles(cr, "*.c").Concat(Directory.GetFiles(cr, "*.h")))
            map.Add((f, Path.Combine("c_runtime", Path.GetFileName(f))));
        string en = Path.Combine(RepoRoot, "engine", "native");
        foreach (var f in Directory.GetFiles(en, "*.c").Concat(Directory.GetFiles(en, "*.h")).Concat(Directory.GetFiles(en, "*.m")))
            map.Add((f, Path.Combine("engine", Path.GetFileName(f))));
        foreach (var sub in new[] { "sokol", Path.Combine("glfw-master", "include"), Path.Combine("glfw-master", "src") })
            foreach (var f in Directory.GetFiles(Path.Combine(en, sub), "*", SearchOption.AllDirectories))
                if (f.EndsWith(".c") || f.EndsWith(".h") || f.EndsWith(".m"))
                    map.Add((f, Path.Combine("engine", Path.GetRelativePath(en, f))));
        string h = HashFiles(map.Select(m => m.src));
        if (UpToDate(dst, h)) { Skipped.Add("sdk/native (kaynak)"); return; }
        Fresh(dst);
        foreach (var (src, rel) in map)
        {
            string to = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(src, to, true);
        }
        WriteManifest(dst, h, null, null);
        Done.Add($"sdk/native ({map.Count} kaynak dosya)");
    }

    // Platform proje sablonlari (android: Gradle kabugu + Kotlin host + CMake) -> sdk/platforms (aotcompiler PlatformsDir).
    static void Platforms()
    {
        string src = Path.Combine(RepoRoot, "aotcompiler", "platforms");
        string dst = Path.Combine(Pkg, "sdk", "platforms");
        var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories);
        string h = HashFiles(files);
        if (UpToDate(dst, h)) { Skipped.Add("sdk/platforms"); return; }
        Fresh(dst);
        foreach (var f in files)
        {
            string to = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(f, to, true);
        }
        WriteManifest(dst, h, null, null);
        Done.Add($"sdk/platforms ({files.Length} dosya)");
    }

    static void Templates()
    {
        string dst = Path.Combine(Pkg, "templates", "NewProject");
        string src = Path.Combine(RepoRoot, "tools", "EditorPublisher", "templates", "NewProject");
        var files = Directory.Exists(src) ? Directory.GetFiles(src, "*", SearchOption.AllDirectories) : Array.Empty<string>();
        string h = HashFiles(files);
        if (UpToDate(dst, h)) { Skipped.Add("templates"); return; }
        Fresh(dst);
        foreach (var f in files)
        {
            string rel = Path.GetRelativePath(src, f);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dst, rel)));
            File.Copy(f, Path.Combine(dst, rel), true);
        }
        WriteManifest(dst, h, null, null);
        Done.Add("templates");
    }

    // Managed IL (RID'den bagimsiz): editor publish (framework-dependent, Roslyn dahil) -> kok;
    // aotcompiler publish + prebuilt IL (Digitoy.CoreLib.dll, DigitoyEngine.dll AOT) -> sdk/aotcompiler. Apphost'lar: win-x64 + osx-arm64.
    static void Managed()
    {
        var inputs = new List<string>();
        inputs.AddRange(SourceFiles("editor", "*.cs", "*.csproj"));
        inputs.AddRange(SourceFiles("engine/managed", "*.cs", "*.csproj"));
        inputs.AddRange(SourceFiles("aotcompiler/source", "*.cs"));
        inputs.Add(Path.Combine(RepoRoot, "aotcompiler", "aotcompiler.csproj"));
        inputs.AddRange(SourceFiles("aotcompiler/c_runtime/corelib", "*.cs"));
        string h = HashFiles(inputs);
        string marker = Path.Combine(Pkg, "sdk");
        if (UpToDate(marker, h) && File.Exists(Path.Combine(Pkg, "DigitoyEditor.dll"))) { Skipped.Add("managed"); return; }

        string tmp = Path.Combine(Path.GetTempPath(), "DigitoyPublish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string common = "-c Release -v q --nologo -p:Deterministic=true -p:ContinuousIntegrationBuild=true -p:DebugType=portable";
            string edCommon = common + " -p:DigitoyEditorBuild=true"; // engine'de DE_EDITOR (editor altyapisi) Release'te de acilir
            string edOut = Path.Combine(tmp, "editor-win");
            Run("dotnet", $"publish \"{Path.Combine(RepoRoot, "editor", "DigitoyEditor.csproj")}\" {edCommon} -r win-x64 --self-contained false -o \"{edOut}\"");
            string edMac = Path.Combine(tmp, "editor-mac");
            Run("dotnet", $"publish \"{Path.Combine(RepoRoot, "editor", "DigitoyEditor.csproj")}\" {edCommon} -r osx-arm64 --self-contained false -o \"{edMac}\"");
            string aotOut = Path.Combine(tmp, "aot");
            Run("dotnet", $"publish \"{Path.Combine(RepoRoot, "aotcompiler", "aotcompiler.csproj")}\" {common} -o \"{aotOut}\"");
            // Prebuilt IL (docs/platform-hosts.md H2): aotcompiler kendi Roslyn yoluyla CoreLib + Engine(DE_AOT) uretir;
            // aotcompiler.dll'in YANINA konur -> kurulu modda aotcompiler bunlari kaynak yerine kullanir.
            Run("dotnet", $"\"{Path.Combine(aotOut, "aotcompiler.dll")}\" sdk-il \"{aotOut}\"", Path.Combine(RepoRoot, "aotcompiler"));

            // yerlestir: kokteki eski managed dosyalar (native artifact'ler haric) + sdk/ temizlenir
            foreach (var f in Directory.GetFiles(Pkg))
            {
                string n = Path.GetFileName(f);
                if (n == "manifest.json" || IsNativeArtifact(f)) continue;
                File.Delete(f);
            }
            Fresh(Path.Combine(Pkg, "sdk"));
            foreach (var f in Directory.GetFiles(edOut))
                if (!IsNativeArtifact(f)) File.Copy(f, Path.Combine(Pkg, Path.GetFileName(f)), true); // native dll'i kendi adimi koyar
            string macHost = Path.Combine(edMac, "DigitoyEditor");
            if (File.Exists(macHost)) File.Copy(macHost, Path.Combine(Pkg, "DigitoyEditor"), true);
            CopyDir(aotOut, Path.Combine(Pkg, "sdk", "aotcompiler"));
            WriteManifest(marker, h, "dotnet " + DotnetVersion(), null);
            Done.Add("managed (editor + apphost win/mac, sdk/aotcompiler + prebuilt CoreLib/Engine IL)");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    static bool IsNativeArtifact(string f)
    {
        string n = Path.GetFileName(f);
        return n.StartsWith("digitoyengine_native") || n.StartsWith("libdigitoyengine_native");
    }

    // llvm-mingw: aotcompiler ile ORTAK MingwToolchain (kullanici dizini, yoksa indirir+budar). Pakete girmez;
    // publisher'in native derlemesi (editor DLL'i) bunu kullanir. Kullanicida aotcompiler ayni yoldan ayni surumu kurar.
    static void Toolchain()
    {
        if (MingwToolchain.IsReady) { Skipped.Add("toolchain " + MingwToolchain.Key); return; }
        MingwToolchain.Ensure(Log);
        Done.Add("toolchain " + MingwToolchain.Key + " (" + MingwToolchain.Root + ")");
    }

    static string ClangExe => MingwToolchain.Clang;
    static string ArExe => MingwToolchain.Ar;
    static string LlvmMingwTag => MingwToolchain.Tag;

    static readonly string[] GlfwWin = { "context", "init", "input", "monitor", "platform", "vulkan", "window", "win32_init", "win32_joystick",
        "win32_module", "win32_monitor", "win32_time", "win32_thread", "win32_window", "wgl_context", "egl_context", "osmesa_context",
        "null_init", "null_joystick", "null_monitor", "null_window" };

    static IEnumerable<string> NativeInputs()
    {
        foreach (var f in SourceFiles("engine/native", "*.c", "*.h", "*.m"))
        {
            string n = f.Replace('\\', '/');
            if (n.Contains("/glfw-master/") && !n.Contains("/glfw-master/src/") && !n.Contains("/glfw-master/include/")) continue;
            yield return f;
        }
    }

    // Prebuilt statik lib: native (sokol+glfw+ses+de_fs) + runtime (vmrt+corelib+vmint), release/debug define setleri.
    // ABI kurali: aotcompiler generated.c'yi AYNI define setiyle ve AYNI toolchain'le derler.
    static void NativeWin(string rid)
    {
        string dst = Path.Combine(Pkg, "native", rid);
        var inputs = NativeInputs().ToList();
        inputs.AddRange(SourceFiles("aotcompiler/c_runtime", "*.c", "*.h"));
        string h = HashFiles(inputs) + "|" + LlvmMingwTag;
        if (UpToDate(dst, h)) { Skipped.Add("native/" + rid); return; }
        Fresh(dst);
        string native = Path.Combine(RepoRoot, "engine", "native");
        string crt = Path.Combine(RepoRoot, "aotcompiler", "c_runtime");
        foreach (var (variant, defines) in new[] { ("release", ""), ("debug", "-DDIGITOYENGINE_DEBUG") })
        {
            string obj = Path.Combine(dst, "obj-" + variant);
            Directory.CreateDirectory(obj);
            var objs = new List<string>();
            string Cc(string src, string extra)
            {
                string o = Path.Combine(obj, Path.GetFileNameWithoutExtension(src) + ".o");
                Run(ClangExe, $"-c -O1 -w -g -gcodeview {defines} {extra} -I\"{native}\" -I\"{crt}\" \"{src}\" -o \"{o}\"");
                return o;
            }
            foreach (var s in new[] { "sokol_shim", "audio_shim", "de_fs" }) objs.Add(Cc(Path.Combine(native, s + ".c"), "-DSOKOL_GLCORE -DSOKOL_IMPL -D_GLFW_WIN32"));
            foreach (var g in GlfwWin) objs.Add(Cc(Path.Combine(native, "glfw-master", "src", g + ".c"), "-D_GLFW_WIN32"));
            foreach (var r in new[] { "vmrt", "corelib", "vmint" }) objs.Add(Cc(Path.Combine(crt, r + ".c"), ""));
            string lib = Path.Combine(dst, $"digitoyengine_static_{variant}.a");
            Run(ArExe, $"rcs \"{lib}\" " + string.Join(" ", objs.Select(o => $"\"{o}\"")));
            Directory.Delete(obj, true);
        }
        WriteManifest(dst, h, "llvm-mingw " + LlvmMingwTag, new JsonObject
        {
            ["release"] = new JsonArray(),
            ["debug"] = new JsonArray("DIGITOYENGINE_DEBUG"),
            ["link"] = new JsonArray("opengl32", "gdi32", "user32", "kernel32", "shell32", "ole32", "oleaut32", "mfplat", "mfuuid", "uuid"),
        });
        Done.Add("native/" + rid + " (release+debug)");
    }

    // Editorun P/Invoke DLL'i (digitoyengine_native.dll) — ayni toolchain, -shared.
    static void EditorNativeWin()
    {
        string dst = Path.Combine(Pkg, "digitoyengine_native.dll");
        string marker = Path.Combine(Pkg, "sdk", "editor-native-win");
        string h = HashFiles(NativeInputs()) + "|" + LlvmMingwTag;
        if (UpToDate(marker, h) && File.Exists(dst)) { Skipped.Add("editor native (win)"); return; }
        Fresh(marker);
        string native = Path.Combine(RepoRoot, "engine", "native");
        var srcs = new List<string> { "sokol_shim.c", "de_fs.c", "audio_shim.c" }.Select(s => Path.Combine(native, s)).ToList();
        srcs.AddRange(GlfwWin.Select(g => Path.Combine(native, "glfw-master", "src", g + ".c")));
        Run(ClangExe, $"-O1 -w -shared -DSOKOL_GLCORE -D_GLFW_WIN32 -D_GLFW_BUILD_DLL -DDE_BUILD_DLL -DSOKOL_IMPL -I\"{native}\" " +
            string.Join(" ", srcs.Select(s => $"\"{s}\"")) + " -lopengl32 -lgdi32 -luser32 -lkernel32 -lshell32 -lole32 -loleaut32 -lmfplat -lmfuuid -luuid " +
            $"-o \"{dst}\"");
        WriteManifest(marker, h, "llvm-mingw " + LlvmMingwTag, null);
        Done.Add("editor native (digitoyengine_native.dll)");
    }

    // macOS editor dylib: build_editor.sh ile ayni kurulum (Metal backend; sokol_shim ObjC+ARC, GLFW Cocoa ARC'siz).
    // Derleyici: Xcode Command Line Tools clang'i (Apple SDK yalniz Mac'te). Cikti pakete kokune (apphost yani).
    static void NativeMac()
    {
        string dst = Path.Combine(Pkg, "libdigitoyengine_native.dylib");
        string marker = Path.Combine(Pkg, "sdk", "editor-native-mac");
        string h = HashFiles(NativeInputs()) + "|xcode";
        if (UpToDate(marker, h) && File.Exists(dst)) { Skipped.Add("editor native (mac)"); return; }
        Fresh(marker);
        string native = Path.Combine(RepoRoot, "engine", "native");
        string src = Path.Combine(native, "glfw-master", "src");
        string tmp = Path.Combine(Path.GetTempPath(), "DigitoyPublish-mac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string defs = "-DSOKOL_METAL -D_GLFW_COCOA -DSOKOL_IMPL";
            string incs = $"-I\"{native}\" -I\"{Path.Combine(native, "glfw-master", "include")}\"";
            string shim = Path.Combine(tmp, "sokol_shim.o"), audio = Path.Combine(tmp, "audio_shim.o"), fs = Path.Combine(tmp, "de_fs.o");
            Run("clang", $"-O1 -w -c -fobjc-arc -x objective-c \"{Path.Combine(native, "sokol_shim.c")}\" {defs} {incs} -o \"{shim}\"");
            Run("clang", $"-O1 -w -c \"{Path.Combine(native, "audio_shim.c")}\" {defs} {incs} -o \"{audio}\"");
            Run("clang", $"-O1 -w -c \"{Path.Combine(native, "de_fs.c")}\" {defs} {incs} -o \"{fs}\"");
            var glfw = new[] { "context.c", "init.c", "input.c", "monitor.c", "platform.c", "vulkan.c", "window.c", "egl_context.c", "osmesa_context.c",
                "null_init.c", "null_joystick.c", "null_monitor.c", "null_window.c",
                "cocoa_init.m", "cocoa_joystick.m", "cocoa_monitor.m", "cocoa_window.m", "nsgl_context.m", "macos_time.c", "posix_module.c", "posix_thread.c" };
            string frameworks = "-framework Cocoa -framework IOKit -framework CoreFoundation -framework CoreGraphics -framework CoreVideo -framework AppKit -framework QuartzCore -framework Metal -framework MetalKit -framework AudioToolbox";
            Run("clang", $"-O1 -w -dynamiclib -fvisibility=default -Wno-deprecated-declarations {defs} {incs} \"{shim}\" \"{audio}\" \"{fs}\" " +
                string.Join(" ", glfw.Select(g => $"\"{Path.Combine(src, g)}\"")) + $" {frameworks} -install_name \"@rpath/libdigitoyengine_native.dylib\" -o \"{dst}\"");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
        WriteManifest(marker, h, "xcode clang", null);
        Done.Add("editor native (libdigitoyengine_native.dylib)");
    }

    // ---------------- manifest / hash ----------------

    static void WriteManifest(string dir, string inputHash, string toolchain, JsonObject defines)
    {
        Directory.CreateDirectory(dir);
        var m = new JsonObject
        {
            ["version"] = Version,
            ["inputHash"] = inputHash,
            ["toolchain"] = toolchain,
            ["host"] = Rid(),
            ["builtAt"] = DateTime.UtcNow.ToString("o"),
        };
        if (defines != null) m["defines"] = defines;
        File.WriteAllText(Path.Combine(dir, "manifest.json"), m.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static bool UpToDate(string dir, string inputHash)
    {
        string p = Path.Combine(dir, "manifest.json");
        if (!File.Exists(p)) return false;
        try
        {
            var m = JsonNode.Parse(File.ReadAllText(p)).AsObject();
            return (string)m["inputHash"] == inputHash && (string)m["version"] == Version;
        }
        catch { return false; }
    }

    // Kok manifest: cekirdek paketi tanimlar; eksikler gercek duruma gore (baska makineden kopyalanan dosyalar dahil).
    static void RootManifest()
    {
        var missing = new JsonArray();
        foreach (var m in Missing)
        {
            string head = m.Split(':')[0].Trim();
            if (head.Contains("dylib") && File.Exists(Path.Combine(Pkg, "libdigitoyengine_native.dylib"))) continue;
            if (head.Contains("digitoyengine_native.dll") && File.Exists(Path.Combine(Pkg, "digitoyengine_native.dll"))) continue;
            missing.Add(m);
        }
        var root = new JsonObject
        {
            ["name"] = "DigitoyEditor",
            ["kind"] = "editor-core",
            ["version"] = Version,
            ["requires"] = new JsonArray(".NET 9 Runtime"),
            ["platformSupport"] = "ayri moduller (modules/<rid>-support); Build Player icin gerekli",
            ["missing"] = missing,
            ["complete"] = missing.Count == 0,
            ["updatedAt"] = DateTime.UtcNow.ToString("o"),
            ["updatedBy"] = Rid(),
        };
        File.WriteAllText(Path.Combine(Pkg, "manifest.json"), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static string HashFiles(IEnumerable<string> files)
    {
        using var sha = SHA256.Create();
        var sb = new StringBuilder();
        foreach (var f in files.Where(File.Exists).OrderBy(x => x.Replace('\\', '/'), StringComparer.Ordinal))
        {
            sb.Append(Path.GetRelativePath(RepoRoot, f).Replace('\\', '/')).Append('=');
            sb.Append(Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(f)))).Append('\n');
        }
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    static IEnumerable<string> SourceFiles(string rel, params string[] patterns)
    {
        string dir = Path.Combine(RepoRoot, rel);
        if (!Directory.Exists(dir)) yield break;
        foreach (var pat in patterns)
            foreach (var f in Directory.GetFiles(dir, pat, SearchOption.AllDirectories))
            {
                string n = f.Replace('\\', '/');
                if (n.Contains("/bin/") || n.Contains("/obj/") || n.Contains("/build/")) continue;
                yield return f;
            }
    }

    // ---------------- yardimcilar ----------------

    static void Step(string name, Action a)
    {
        Log($"\n== {name}");
        var sw = Stopwatch.StartNew();
        a();
        Log($"   ({sw.Elapsed.TotalSeconds:F1} s)");
    }

    static void Fresh(string dir) { if (Directory.Exists(dir)) Directory.Delete(dir, true); Directory.CreateDirectory(dir); }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(src, f);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dst, rel)));
            File.Copy(f, Path.Combine(dst, rel), true);
        }
    }

    static void Run(string exe, string args, string cwd = null)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = cwd ?? RepoRoot };
        using var p = Process.Start(psi);
        string o = p.StandardOutput.ReadToEnd(), e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new Exception($"{Path.GetFileName(exe)} {args.Substring(0, Math.Min(140, args.Length))}... (exit {p.ExitCode})\n{o}\n{e}");
    }

    static string DotnetVersion()
    {
        var psi = new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi);
        string v = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); return v;
    }


    static string Rid() => (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" +
        (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64");

    static string FindRepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "VERSION")) && Directory.Exists(Path.Combine(d.FullName, "aotcompiler")))
                    return d.FullName;
        throw new Exception("repo koku bulunamadi (VERSION + aotcompiler/)");
    }

    static void Log(string s) => Console.WriteLine(s);
}
