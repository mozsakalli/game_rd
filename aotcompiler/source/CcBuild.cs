using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DigitoyEngine.Build
{
    // Yerlesik paralel + artimli C derleme surucusu (dis arac YOK; docs/registry-removal.md C adimi).
    // Her kaynak icin obj/<ad>.o + <ad>.o.d (clang -MD: kaynak + include ettigi header'lar) + <ad>.o.flags.
    // Yeniden derleme kosulu: nesne yok | bayraklar degisti | .d'deki herhangi bir dosya nesneden yeni.
    // Uretilen .c/.h'ler icerik degismediyse yazilmadigi icin (CTranspiler.WriteFiles) mtime'lari sabit kalir.
    public static class CcBuild
    {
        public sealed class Item
        {
            public string Source;
            public string ExtraFlags = ""; // kaynak basina ek bayrak (orn. runtime -O2, uretilen -O1)
            public string ObjName;         // bos: kaynak dosya adi
        }

        static string Q(string s) => s.Contains(' ') ? "\"" + s + "\"" : s;
        static string Fwd(string s) => s.Replace('\\', '/');

        // .d dosyasi: "obj: dep dep \\\n dep ..." ; "\\ " kacisli bosluk; birden cok hedef satiri olabilir.
        static IEnumerable<string> DepFiles(string dPath)
        {
            var text = File.ReadAllText(dPath).Replace("\\\r\n", " ").Replace("\\\n", " ");
            int colon = text.IndexOf(':');
            if (colon < 0) yield break;
            // Windows surucu harfi "C:/..." ilk ':' olabilir: hedef " obj.o:" kalibini ara
            int sep = text.IndexOf(".o:", StringComparison.Ordinal);
            if (sep >= 0) colon = sep + 2;
            var rest = text.Substring(colon + 1);
            var sb = new StringBuilder();
            for (int i = 0; i < rest.Length; i++)
            {
                char ch = rest[i];
                if (ch == '\\' && i + 1 < rest.Length && rest[i + 1] == ' ') { sb.Append(' '); i++; continue; }
                if (char.IsWhiteSpace(ch))
                {
                    if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                    continue;
                }
                sb.Append(ch);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        static bool NeedsBuild(string src, string obj, string flags)
        {
            if (!File.Exists(obj)) return true;
            var flagsPath = obj + ".flags";
            if (!File.Exists(flagsPath) || File.ReadAllText(flagsPath) != flags) return true;
            var objTime = File.GetLastWriteTimeUtc(obj);
            if (File.GetLastWriteTimeUtc(src) > objTime) return true;
            var d = obj + ".d";
            if (!File.Exists(d)) return true;
            try
            {
                foreach (var dep in DepFiles(d))
                {
                    if (!File.Exists(dep)) return true;
                    if (File.GetLastWriteTimeUtc(dep) > objTime) return true;
                }
            }
            catch { return true; }
            return false;
        }

        public sealed class Result { public List<string> Objects = new List<string>(); public int Compiled, Skipped; public TimeSpan Elapsed; }

        public static Result Compile(string cc, string objDir, string baseFlags, IEnumerable<Item> items, Action<string> log = null, int parallelism = 0)
        {
            Directory.CreateDirectory(objDir);
            var sw = Stopwatch.StartNew();
            var r = new Result();
            var todo = new List<(Item item, string obj, string flags)>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in items)
            {
                var stem = string.IsNullOrEmpty(it.ObjName) ? Path.GetFileNameWithoutExtension(it.Source) : it.ObjName;
                var obj = Path.Combine(objDir, stem + ".o");
                if (!used.Add(obj)) obj = Path.Combine(objDir, stem + "_" + (uint)Fwd(Path.GetFullPath(it.Source)).GetHashCode() + ".o");
                r.Objects.Add(obj);
                var flags = (baseFlags + " " + it.ExtraFlags).Trim() + " :: " + cc;
                if (NeedsBuild(Path.GetFullPath(it.Source), obj, flags)) todo.Add((it, obj, flags));
                else r.Skipped++;
            }
            var errors = new ConcurrentBag<string>();
            var po = new ParallelOptions { MaxDegreeOfParallelism = parallelism > 0 ? parallelism : Math.Max(1, Environment.ProcessorCount) };
            Parallel.ForEach(todo, po, t =>
            {
                var flagsPath = t.obj + ".flags";
                try { File.Delete(flagsPath); } catch { }
                var args = $"{baseFlags} {t.item.ExtraFlags} -MD -MF {Q(Fwd(t.obj + ".d"))} -c {Q(Fwd(Path.GetFullPath(t.item.Source)))} -o {Q(Fwd(t.obj))}";
                var (exit, output) = Run(cc, args);
                if (exit != 0) { errors.Add($"{t.item.Source}:\n{output}"); try { File.Delete(t.obj); } catch { } return; }
                File.WriteAllText(flagsPath, t.flags);
            });
            r.Compiled = todo.Count;
            r.Elapsed = sw.Elapsed;
            if (!errors.IsEmpty) throw new Exception($"C derleme hatasi ({errors.Count} dosya):\n" + string.Join("\n", errors.Take(5)));
            log?.Invoke($"cc: {r.Compiled} derlendi, {r.Skipped} guncel ({r.Elapsed.TotalSeconds:F1} s, {po.MaxDegreeOfParallelism} paralel)");
            return r;
        }

        // Link: nesne listesi rsp dosyasindan (Windows komut satiri limiti); yollar ileri egik cizgi (rsp'de '\' kacis).
        public static void Link(string cc, IEnumerable<string> objects, string extraInputs, string ldflags, string output, Action<string> log = null)
        {
            var sw = Stopwatch.StartNew();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            var rsp = output + ".rsp";
            File.WriteAllText(rsp, string.Join("\n", objects.Select(o => Q(Fwd(Path.GetFullPath(o))))) + "\n");
            var args = $"{ldflags} @{Q(Fwd(rsp))} {extraInputs} -o {Q(Fwd(output))}";
            var (exit, outp) = Run(cc, args);
            if (exit != 0) throw new Exception($"link hatasi (exit {exit}):\n{outp}");
            log?.Invoke($"link: {output} ({new FileInfo(output).Length / 1024} KB, {sw.Elapsed.TotalSeconds:F1} s)");
        }

        static (int, string) Run(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var p = Process.Start(psi);
            var err = p.StandardError.ReadToEndAsync();
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, outp + err.Result);
        }
    }
}
