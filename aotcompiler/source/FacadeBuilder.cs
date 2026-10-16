using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace DigitoyEngine.Build
{
    // FACADE ASSEMBLY'LER (Roslyn icin, frontend'e GIRMEZ).
    // Plugin DLL'leri (Assets/**/*.dll, orn. Digitoy.Common.dll) gercek .NET'e karsi derlenir: metadata'larinda
    // `System.Runtime, 10.0.0.0, b03f...`, `System.Collections`, `System.Memory` gibi assembly referanslari vardir.
    // Oyun kodu NoStdLib + Digitoy.CoreLib ile derlenirken plugin'in bir uyesine dokununca Roslyn imzadaki tipi
    // (Enum, List<>, Nullable<>...) o ADLI assembly'de arar -> yok -> CS0012. CilFrontend adla remap yapar ama Roslyn
    // yapmaz. Cozum: referans edilen her System.* kimligi icin corelib'in TUM public tiplerini Digitoy.CoreLib'e
    // `[TypeForwardedTo]` ile yonlendiren kucuk bir facade dll uretilir; kimlik (ad + surum + public key) plugin'in
    // beklediğiyle BIREBIR (public key host TPA'daki ayni adli dll'den okunur, PublicSign = imza yok, yalniz identity).
    // Roslyn forwarder'i derlemede cozer: Game.dll dogrudan Digitoy.CoreLib'e referans verir, facade pak'a/IR'a girmez.
    public static class FacadeBuilder
    {
        // plugins'in referans ettigi System.* kimlikleri icin facade dll yollarini dondurur (cache: corelib/plugin mtime).
        public static List<string> Build(List<string> plugins, string coreLibDll, string outDir)
        {
            var result = new List<string>();
            if (plugins == null || plugins.Count == 0) return result;

            var wanted = new Dictionary<string, (Version version, byte[] token)>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in plugins)
                foreach (var (name, version, token) in AssemblyRefs(p))
                {
                    if (!IsFrameworkName(name)) continue;
                    if (!wanted.TryGetValue(name, out var cur) || cur.version < version)
                        wanted[name] = (version, token);
                }
            if (wanted.Count == 0) return result;

            Directory.CreateDirectory(outDir);
            var coreT = File.GetLastWriteTimeUtc(coreLibDll);
            var pluginT = plugins.Max(File.GetLastWriteTimeUtc);
            List<string> forwards = null;
            foreach (var kv in wanted.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string dll = Path.Combine(outDir, kv.Key + ".dll");
                result.Add(dll);
                if (File.Exists(dll))
                {
                    var t = File.GetLastWriteTimeUtc(dll);
                    if (t >= coreT && t >= pluginT) continue;
                }
                forwards ??= CoreLibPublicTypes(coreLibDll);
                byte[] key = FrameworkPublicKey(kv.Key, kv.Value.token);
                var sb = new StringBuilder();
                sb.Append("[assembly: System.Reflection.AssemblyVersion(\"").Append(kv.Value.version).Append("\")]\n");
                foreach (var f in forwards)
                    sb.Append("[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(").Append(f).Append("))]\n");
                var r = RoslynCompiler.CompileToFile(new RoslynBuild
                {
                    AssemblyName = kv.Key,
                    ExtraSources = { (kv.Key + ".facade.cs", sb.ToString()) },
                    References = { coreLibDll },
                    NoStdLib = true,
                    PublicKey = key,
                }, dll, useCache: false);
                if (!r.Ok) throw new Exception($"facade derlemesi basarisiz ({kv.Key}):\n" + r.Errors);
            }
            return result;
        }

        static bool IsFrameworkName(string n) =>
            n == "System" || n.StartsWith("System.", StringComparison.Ordinal) || n == "netstandard" || n == "mscorlib";

        static IEnumerable<(string name, Version version, byte[] token)> AssemblyRefs(string dll)
        {
            using var pe = new PEReader(File.OpenRead(dll));
            var md = pe.GetMetadataReader();
            foreach (var h in md.AssemblyReferences)
            {
                var a = md.GetAssemblyReference(h);
                byte[] tok = a.PublicKeyOrToken.IsNil ? null : md.GetBlobBytes(a.PublicKeyOrToken);
                yield return (md.GetString(a.Name), a.Version, tok);
            }
        }

        // Host TPA'daki ayni adli framework dll'inin TAM public key blob'u (ref'te yalniz 8 baytlik token var; PublicSign tam anahtar ister).
        static byte[] FrameworkPublicKey(string name, byte[] token)
        {
            if (token == null || token.Length == 0) return null;
            string tpa = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (tpa == null) return null;
            byte[] fallback = null;
            foreach (var p in tpa.Split(Path.PathSeparator))
            {
                string n = Path.GetFileNameWithoutExtension(p);
                bool sameName = string.Equals(n, name, StringComparison.OrdinalIgnoreCase);
                if (!sameName && fallback != null) continue;
                byte[] key;
                try
                {
                    using var pe = new PEReader(File.OpenRead(p));
                    var md = pe.GetMetadataReader();
                    if (!md.IsAssembly) continue;
                    var def = md.GetAssemblyDefinition();
                    if (def.PublicKey.IsNil) continue;
                    key = md.GetBlobBytes(def.PublicKey);
                }
                catch { continue; }
                if (!TokenMatches(key, token)) continue;
                if (sameName) return key;
                fallback = key; // ayni token'li baska bir framework dll'i (ad TPA'da yoksa)
            }
            return fallback;
        }

        // Public key token = SHA1(publicKey) son 8 baytinin tersi.
        static bool TokenMatches(byte[] publicKey, byte[] token)
        {
            if (token.Length == 8 && publicKey.Length > 8)
            {
                var sha = System.Security.Cryptography.SHA1.HashData(publicKey);
                for (int i = 0; i < 8; i++)
                    if (sha[sha.Length - 1 - i] != token[i]) return false;
                return true;
            }
            return publicKey.AsSpan().SequenceEqual(token); // ref tam anahtar tasiyorsa
        }

        // corelib'in top-level public tipleri C# typeof yazimiyla (`global::System.Collections.Generic.List<>`).
        static List<string> CoreLibPublicTypes(string coreLibDll)
        {
            var list = new List<string>();
            using var pe = new PEReader(File.OpenRead(coreLibDll));
            var md = pe.GetMetadataReader();
            foreach (var h in md.TypeDefinitions)
            {
                var t = md.GetTypeDefinition(h);
                if (!t.GetDeclaringType().IsNil) continue; // ic ice: ebeveyn uzerinden cozulur
                var vis = t.Attributes & System.Reflection.TypeAttributes.VisibilityMask;
                if (vis != System.Reflection.TypeAttributes.Public) continue;
                string name = md.GetString(t.Name);
                if (name.StartsWith("<", StringComparison.Ordinal) || name == "Void") continue; // typeof(System.Void) C#'ta yasak (CS0673)
                string ns = t.Namespace.IsNil ? "" : md.GetString(t.Namespace);
                int arity = 0;
                int tick = name.IndexOf('`');
                if (tick >= 0) { arity = int.Parse(name.Substring(tick + 1)); name = name.Substring(0, tick); }
                var sb = new StringBuilder("global::");
                if (ns.Length > 0) sb.Append(ns).Append('.');
                sb.Append('@').Append(name); // @: anahtar kelime adli tipe karsi (Object/String degil ama guvenli)
                if (arity > 0) sb.Append('<').Append(',', arity - 1).Append('>');
                list.Add(sb.ToString());
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }
}
