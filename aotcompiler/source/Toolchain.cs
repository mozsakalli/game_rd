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
            if (!File.Exists(zip) || !ZipOk(zip))
            {
                string url = $"https://github.com/mstorsjo/llvm-mingw/releases/download/{Tag}/{Key}.zip";
                log($"[toolchain] indiriliyor: {url}");
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
                using (var s = http.GetStreamAsync(url).GetAwaiter().GetResult())
                using (var f = File.Create(zip + ".part"))
                    s.CopyTo(f);
                File.Move(zip + ".part", zip, true);
                if (!ZipOk(zip)) throw new Exception("toolchain zip bozuk: " + zip);
            }
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

        static void DeleteDir(string d) { if (Directory.Exists(d)) Directory.Delete(d, true); }

        static bool ZipOk(string zip)
        {
            try { using var z = ZipFile.OpenRead(zip); return z.Entries.Count > 0; } catch { return false; }
        }
    }
}
