using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace DigitoyEngine.Build
{
    // ninja: cok dosyali uretilen C'yi paralel + artimli derler (docs/registry-removal.md B/C adimlari).
    // Bulma sirasi: AOT_NINJA, PATH, Android SDK cmake/*/bin, yoksa kullanici toolchains dizinine indirilir (kucuk zip).
    public static class NinjaTool
    {
        public const string Version = "1.12.1";
        static string Root
        {
            get
            {
                string baseDir = Environment.GetEnvironmentVariable("DIGITOY_TOOLCHAINS");
                if (string.IsNullOrWhiteSpace(baseDir))
                    baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DigitoyEngine", "toolchains");
                return Path.Combine(baseDir, "ninja-" + Version);
            }
        }
        static string Exe => OperatingSystem.IsWindows() ? "ninja.exe" : "ninja";

        public static string Ensure(Action<string> log = null)
        {
            log ??= _ => { };
            var env = Environment.GetEnvironmentVariable("AOT_NINJA");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var p = Path.Combine(dir.Trim(), Exe);
                if (File.Exists(p)) return p;
            }
            var local = Path.Combine(Root, Exe);
            if (File.Exists(local)) return local;
            if (OperatingSystem.IsWindows())
            {
                var sdk = Environment.GetEnvironmentVariable("ANDROID_HOME") ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
                          ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk");
                var cmake = Path.Combine(sdk, "cmake");
                if (Directory.Exists(cmake))
                    foreach (var v in Directory.GetDirectories(cmake).OrderByDescending(x => x, StringComparer.Ordinal))
                    {
                        var p = Path.Combine(v, "bin", "ninja.exe");
                        if (File.Exists(p)) return p;
                    }
                Directory.CreateDirectory(Root);
                var zip = Path.Combine(Root, "ninja-win.zip");
                MingwToolchain.Download($"https://github.com/ninja-build/ninja/releases/download/v{Version}/ninja-win.zip", zip, log);
                ZipFile.ExtractToDirectory(zip, Root, true);
                try { File.Delete(zip); } catch { }
                if (!File.Exists(local)) throw new Exception("ninja acildi ama exe yok: " + local);
                return local;
            }
            throw new Exception("ninja bulunamadi (PATH'e ekleyin ya da AOT_NINJA=<yol>)");
        }
    }

    // build.ninja uretimi + kosum. Nesneler obj/ altinda; depfile ile header bagimliligi (uretilen .h degisince yalniz
    // onu include eden .c'ler derlenir). Kaynak listesi/bayrak degisince dosya yeniden yazilir; ninja gerisini halleder.
    public static class NinjaBuild
    {
        public sealed class Target
        {
            public string Output;                  // link ciktisi (exe/so) ya da null (yalniz nesneler)
            public List<string> Sources = new List<string>();
            public Dictionary<string, string> FlagsOf = new Dictionary<string, string>(); // kaynak -> ek bayraklar (orn. runtime -O2)
            public string Cc, CFlags = "", LdFlags = "", LdLibs = "";
            public string ObjDir;
        }

        static string Esc(string s) => s.Replace('\\', '/').Replace("$", "$$").Replace(" ", "$ ").Replace(":", "$:"); // ileri egik cizgi: rsp dosyasinda ters bolu kacis sayilir
        static string Q(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;

        public static string Write(string buildDir, Target t)
        {
            Directory.CreateDirectory(buildDir);
            Directory.CreateDirectory(t.ObjDir);
            var sb = new StringBuilder();
            sb.Append("ninja_required_version = 1.3\n");
            sb.Append($"cc = {Q(t.Cc)}\n");
            sb.Append($"cflags = {t.CFlags}\n");
            sb.Append($"ldflags = {t.LdFlags}\n");
            sb.Append($"ldlibs = {t.LdLibs}\n");
            sb.Append("rule cc\n  command = $cc $cflags $xflags -MD -MF $out.d -c $in -o $out\n  depfile = $out.d\n  deps = gcc\n  description = CC $out\n");
            sb.Append("rule link\n  command = $cc $ldflags @$out.rsp -o $out $ldlibs\n  rspfile = $out.rsp\n  rspfile_content = $in\n  description = LINK $out\n");
            var objs = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in t.Sources)
            {
                var stem = Path.GetFileNameWithoutExtension(src);
                var obj = Path.Combine(t.ObjDir, stem + ".o");
                if (!used.Add(obj)) obj = Path.Combine(t.ObjDir, stem + "_" + (uint)src.GetHashCode() + ".o"); // ayni adli kaynaklar (runtime/native)
                objs.Add(obj);
                sb.Append($"build {Esc(obj)}: cc {Esc(Path.GetFullPath(src))}\n");
                if (t.FlagsOf.TryGetValue(src, out var xf) && !string.IsNullOrEmpty(xf)) sb.Append($"  xflags = {xf}\n");
            }
            if (t.Output != null)
            {
                sb.Append($"build {Esc(Path.GetFullPath(t.Output))}: link {string.Join(" ", objs.Select(Esc))}\n");
                sb.Append($"default {Esc(Path.GetFullPath(t.Output))}\n");
            }
            var path = Path.Combine(buildDir, "build.ninja");
            var text = sb.ToString();
            if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
            return path;
        }

        public static (int exitCode, string output) Run(string buildDir, Action<string> log = null)
        {
            var ninja = NinjaTool.Ensure(log);
            var psi = new ProcessStartInfo(ninja, "-C " + Q(Path.GetFullPath(buildDir))) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            var err = p.StandardError.ReadToEndAsync();
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, outp + err.Result);
        }
    }
}
