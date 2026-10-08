using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;

namespace DigitoyEngine.Build
{
    // Windows player derleyicisi icin llvm-mingw (clang + lld + UCRT mingw-w64 CRT/import lib'leri; yeniden dagitilabilir).
    // Platform Support paketi YOK: yoksa kullanici dizinine indirilir, budanir, kullanilir — o kadar.
    //   %LOCALAPPDATA%\DigitoyEngine\toolchains\llvm-mingw-<tag>-ucrt-x86_64\   (DIGITOY_TOOLCHAINS ile degistirilebilir)
    // aotcompiler (player build) ve EditorPublisher (editor native DLL) AYNI dosyayi kullanir (Link).
    // Neden mingw: Windows SDK dagitilamaz; MSVC-hedef clang kurulum ister. Spike: runtime/selftest/interp MSVC ile birebir.
    public static class MingwToolchain
    {
        public const string Tag = "20261006";
        public static string Key => "llvm-mingw-" + Tag + "-ucrt-x86_64";

        public static string Root
        {
            get
            {
                string baseDir = Environment.GetEnvironmentVariable("DIGITOY_TOOLCHAINS");
                if (string.IsNullOrWhiteSpace(baseDir))
                    baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitoyEngine", "toolchains");
                return Path.Combine(baseDir, Key);
            }
        }
        public static string Clang => Path.Combine(Root, "bin", "clang.exe");
        public static string Ar => Path.Combine(Root, "bin", "llvm-ar.exe");
        static string Marker => Path.Combine(Root, ".digitoy-ok");

        public static bool IsReady => File.Exists(Marker) && File.Exists(Clang);

        // Hazir degilse indir + ac + buda. Donus: kok dizin. log null olabilir.
        public static string Ensure(Action<string> log = null)
        {
            if (IsReady) return Root;
            log ??= _ => { };
            string parent = Path.GetDirectoryName(Root);
            Directory.CreateDirectory(parent);
            string zip = Path.Combine(parent, Key + ".zip");
            Download($"https://github.com/mstorsjo/llvm-mingw/releases/download/{Tag}/{Key}.zip", zip, log);
            log($"[toolchain] aciliyor: {Key}");
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            ZipFile.ExtractToDirectory(zip, parent); // zip koku = Key/
            if (!File.Exists(Clang)) throw new Exception("toolchain acildi ama clang yok: " + Clang);
            Prune(Root);
            File.WriteAllText(Marker, Key + "\n");
            try { File.Delete(zip); } catch { }
            log($"[toolchain] hazir: {Root}");
            return Root;
        }

        // Muhafazakar budama (~700 MB -> ~330 MB): diger mimarilerin triplet'leri/sarmalayicilari, lldb/clangd/clang-tidy/
        // python, diger mimari runtime lib'leri. x86_64 zinciri (wrapper, lld, CRT, libunwind, compiler-rt) DOKUNULMAZ.
        static void Prune(string tc)
        {
            foreach (var d in new[] { "i686-w64-mingw32", "armv7-w64-mingw32", "aarch64-w64-mingw32", "python" })
                DeleteDir(Path.Combine(tc, d));
            var junk = new[] { "lldb", "clangd", "clang-tidy", "clang-format", "scan-deps", "scan-build", "analyze-build", "intercept-build", "python",
                "aarch64-", "armv7-", "arm64ec-", "i686-", "libomp", "asan", "clang-repl", "llvm-pdbutil", "llvm-cov", "llvm-profdata" };
            foreach (var f in Directory.GetFiles(Path.Combine(tc, "bin")))
            {
                string n = Path.GetFileName(f).ToLowerInvariant();
                if (junk.Any(j => n.Contains(j))) File.Delete(f);
            }
            string clangLib = Path.Combine(tc, "lib", "clang");
            if (Directory.Exists(clangLib))
                foreach (var ver in Directory.GetDirectories(clangLib))
                {
                    string rtLib = Path.Combine(ver, "lib");
                    if (!Directory.Exists(rtLib)) continue;
                    foreach (var d in Directory.GetDirectories(rtLib))
                        if (Path.GetFileName(d) != "windows") DeleteDir(d);
                    string win = Path.Combine(rtLib, "windows");
                    if (Directory.Exists(win))
                        foreach (var f in Directory.GetFiles(win))
                        {
                            string n = Path.GetFileName(f).ToLowerInvariant();
                            if (n.Contains("aarch64") || n.Contains("armv7") || n.Contains("arm64ec") || n.Contains("i386") || n.Contains("i686") || n.Contains("fuzzer") || n.Contains("asan") || n.Contains("profile") || n.Contains("orc_rt"))
                                File.Delete(f);
                        }
                }
            foreach (var d in Directory.GetDirectories(Path.Combine(tc, "lib")))
                if (Path.GetFileName(d) != "clang") DeleteDir(d); // python site-packages vb.
            DeleteDir(Path.Combine(tc, "share"));
        }

        internal static void DeleteDir(string d) { if (Directory.Exists(d)) Directory.Delete(d, true); }

        internal static bool ZipOk(string zip)
        {
            try { using var z = ZipFile.OpenRead(zip); return z.Entries.Count > 0; } catch { return false; }
        }

        // Indir (yoksa / bozuksa) -> zip yolu. .part uzerinden atomik; bozuk zip yeniden indirilir.
        internal static string Download(string url, string zip, Action<string> log)
        {
            if (File.Exists(zip) && ZipOk(zip)) return zip;
            log($"[toolchain] indiriliyor: {url}");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using (var s = http.GetStreamAsync(url).GetAwaiter().GetResult())
            using (var f = File.Create(zip + ".part"))
                s.CopyTo(f);
            File.Move(zip + ".part", zip, true);
            if (!ZipOk(zip)) throw new Exception("toolchain zip bozuk: " + zip);
            return zip;
        }
    }

    // wasm32 player derleyicisi: emscripten. `emsdk` araci KULLANILMAZ (python script'i + sistem kurulumu ister); emsdk'nin
    // kendisinin indirdigi uc zip dogrudan alinir ve toolchain klasorune acilir (sisteme hicbir sey kurulmaz, PATH/registry yok):
    //   wasm-binaries.zip  clang/wasm-ld/binaryen + emscripten/ (emcc.py) + prebuilt sysroot cache   -> <Root>/upstream/
    //   python-*.zip       emcc.py'nin yorumlayicisi (gomulu, ozel kopya)                             -> <Root>/python/
    //   node-*.zip         emcc'nin JS optimizasyon/acorn adimlari                                    -> <Root>/node/
    // Ayni desen .NET wasm-tools workload'inda da var (Microsoft.NET.Runtime.Emscripten.{Sdk,Python,Node}). Sürüm pinli:
    // emscripten-releases-tags.json'daki hash. %LOCALAPPDATA%\DigitoyEngine\toolchains\emsdk-<ver>\ (DIGITOY_TOOLCHAINS ile degisir).
    public static class EmsdkToolchain
    {
        public const string Version = "6.0.11";
        const string ReleaseHash = "f6264d4a4dd9ba24a9f0a5702835a44d1463de13";
        const string PythonZip = "python-3.13.3-0-win-amd64.zip";
        const string NodeDir = "node-v22.16.0-win-x64";
        const string Storage = "https://storage.googleapis.com/webassembly/emscripten-releases-builds";
        public static string Key => "emsdk-" + Version;

        public static string Root
        {
            get
            {
                string baseDir = Environment.GetEnvironmentVariable("DIGITOY_TOOLCHAINS");
                if (string.IsNullOrWhiteSpace(baseDir))
                    baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitoyEngine", "toolchains");
                return Path.Combine(baseDir, Key);
            }
        }
        public static string Upstream => Path.Combine(Root, "upstream");
        public static string Emscripten => Path.Combine(Upstream, "emscripten");
        public static string Python => Path.Combine(Root, "python", "python.exe");
        public static string Node => Path.Combine(Root, "node", "bin", "node.exe");
        public static string Config => Path.Combine(Root, ".emscripten");
        public static string Cache => Path.Combine(Root, "cache");
        static string Marker => Path.Combine(Root, ".digitoy-ok");

        public static bool IsReady => File.Exists(Marker) && File.Exists(Python) && File.Exists(Path.Combine(Emscripten, "emcc.py"));

        public static string Ensure(Action<string> log = null)
        {
            if (IsReady) return Root;
            log ??= _ => { };
            string parent = Path.GetDirectoryName(Root);
            Directory.CreateDirectory(parent);
            string zBin = MingwToolchain.Download($"{Storage}/win/{ReleaseHash}/wasm-binaries.zip", Path.Combine(parent, Key + "-wasm-binaries.zip"), log);
            string zPy = MingwToolchain.Download($"{Storage}/deps/{PythonZip}", Path.Combine(parent, Key + "-python.zip"), log);
            string zNode = MingwToolchain.Download($"{Storage}/deps/{NodeDir}.zip", Path.Combine(parent, Key + "-node.zip"), log);

            log($"[toolchain] aciliyor: {Key}");
            MingwToolchain.DeleteDir(Root);
            Directory.CreateDirectory(Root);
            ZipFile.ExtractToDirectory(zBin, Root);                                 // kok: install/
            Directory.Move(Path.Combine(Root, "install"), Upstream);
            ZipFile.ExtractToDirectory(zPy, Path.Combine(Root, "python"));          // kok duz (python.exe + Lib/ + DLLs/)
            ZipFile.ExtractToDirectory(zNode, Root);                                // kok: node-vX-win-x64/
            Directory.Move(Path.Combine(Root, NodeDir), Path.Combine(Root, "node"));
            if (!File.Exists(Python)) throw new Exception("toolchain acildi ama python yok: " + Python);
            if (!File.Exists(Node)) throw new Exception("toolchain acildi ama node yok: " + Node);
            if (!File.Exists(Path.Combine(Upstream, "bin", "clang.exe"))) throw new Exception("toolchain acildi ama clang yok: " + Upstream);

            // emsdk'nin activate adiminin yazdigi .emscripten (yollar ileri egik cizgi; python dizgisi).
            string P(string p) => p.Replace('\\', '/');
            File.WriteAllText(Config,
                $"LLVM_ROOT = '{P(Path.Combine(Upstream, "bin"))}'\n" +
                $"BINARYEN_ROOT = '{P(Upstream)}'\n" +
                $"EMSCRIPTEN_ROOT = '{P(Emscripten)}'\n" +
                $"NODE_JS = '{P(Node)}'\n" +
                $"CACHE = '{P(Cache)}'\n");
            // Prebuilt sysroot cache paketle gelir (emscripten/cache); kendi CACHE dizinimize tasinir ki paket salt-okunur kalabilsin.
            string shipped = Path.Combine(Emscripten, "cache");
            if (Directory.Exists(shipped) && !Directory.Exists(Cache)) Directory.Move(shipped, Cache);
            Prune(Upstream);
            File.WriteAllText(Marker, Key + "\n");
            foreach (var z in new[] { zBin, zPy, zNode }) try { File.Delete(z); } catch { }
            log($"[toolchain] hazir: {Root}");
            return Root;
        }

        // Muhafazakar budama: emcc'nin cagirmadigi agir araclar. clang/wasm-ld/llvm-ar/nm/objcopy/dwarfdump + binaryen DOKUNULMAZ.
        static void Prune(string up)
        {
            var junk = new[] { "clangd", "clang-tidy", "clang-format", "clang-repl", "clang-scan-deps", "lld-link", "ld64.lld", "ld.lld", "llvm-pdbutil", "llvm-cov", "llvm-profdata", "lldb" };
            foreach (var f in Directory.GetFiles(Path.Combine(up, "bin")))
            {
                string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (junk.Any(j => n == j || n.StartsWith(j + "-"))) File.Delete(f);
            }
        }

        // emcc = python emcc.py. Ortam: EM_CONFIG (bizim .emscripten), EM_CACHE; PYTHONHOME/PYTHONPATH temiz (emsdk.bat ile ayni).
        public static System.Diagnostics.ProcessStartInfo Emcc(string args)
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Python, $"\"{Path.Combine(Emscripten, "emcc.py")}\" {args}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.Environment["EM_CONFIG"] = Config;
            psi.Environment["EM_CACHE"] = Cache;
            psi.Environment.Remove("PYTHONHOME");
            psi.Environment.Remove("PYTHONPATH");
            psi.Environment.Remove("EMSDK");
            psi.Environment.Remove("EMSDK_PYTHON");
            return psi;
        }
    }
}
