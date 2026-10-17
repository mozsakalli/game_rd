using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DigitoyEngine.Build;
using DigitoyEngine.Language;

// Plugin build adimi (docs/plugins.md): canli plugin'ler (TrimAnalysis) -> Editor.dll kancalari -> platform uretim verisi
// -> generated/ dosyalari. Program.Emit* bu sinifi cagirir; kabuk dosyalarina dokunulmaz.
public static class PluginBuild
{
    public sealed class Result
    {
        public List<PluginFolder> Folders = new();
        public HashSet<string> Live = new(StringComparer.Ordinal);   // canli plugin adlari
        public Assembly EditorAsm;                                     // null = kanca yok (Editor kodu yok ya da derlenemedi)
        public Assembly EngineNet;                                     // DigitoyEngine .NET (DE_EDITOR) — ayar YAML okuma
        public PluginSettingsStore Store;
        public List<string> Log = new();
        public IEnumerable<PluginFolder> LiveFolders => Folders.Where(f => Live.Contains(f.Name));
    }

    // Canli Plugin turevleri: DigitoyEngine.Plugin'den tureyen, trim sonucunda canli, somut tipler -> kisa ad.
    public static HashSet<string> LivePlugins(Context ctx, DigitoyEngine.Language.Trim.TrimResult trim)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (!ctx.TryGetPrimitive("DigitoyEngine.Plugin", out var baseP)) return set;
        foreach (var p in ctx.AllPrimitives)
        {
            if (p == baseP || p.IsGeneric || p.IsGenericInstance || p.IsInterface) continue;
            bool derives = false;
            for (var q = p.Parent; q != null; q = q.Parent) if (q.Definition == baseP) { derives = true; break; }
            if (!derives || !trim.IsTypeLive(p)) continue;
            if (p.Definition != p) continue;
            set.Add(ShortName(p.Name));
        }
        return set;
    }

    static string ShortName(string full)
    {
        int i = Math.Max(full.LastIndexOf('.'), Math.Max(full.LastIndexOf('/'), full.LastIndexOf('+')));
        string s = i >= 0 ? full.Substring(i + 1) : full;
        int g = s.IndexOf('`'); return g >= 0 ? s.Substring(0, g) : s;
    }

    // Assets/**/*.cs (Editor dahil) normal .NET'e karsi: <Ad>.Editor.dll. Referans: DigitoyEngine .NET (DE_EDITOR) + aotcompiler
    // (ProjectHooks). engineNetDll null -> derlenmez (kancasiz; uyari). Editor GameCode ile ayni kaynak kapsami.
    public static Result Prepare(string projectRoot, string projName, string workDir, HashSet<string> live, string engineNetDll, Action<string> log)
    {
        var r = new Result { Live = live, Folders = PluginFolder.Scan(projectRoot) };
        foreach (var f in r.Folders)
            log($"plugin: {f.Name} [{(live.Contains(f.Name) ? "canli" : "kullanilmiyor")}]" + (f.HasAndroid ? " android" : "") + (f.HasIos ? " ios" : "") + (f.HasWasm ? " wasm" : ""));
        if (engineNetDll == null || !File.Exists(engineNetDll))
        {
            log("[uyari] DigitoyEngine .NET dll yok (--editor-engine): plugin kancalari ve ayar dogrulamasi atlandi");
            r.Store = new PluginSettingsStore(projectRoot, null);
            return r;
        }
        var sources = RoslynCompiler.SourcesUnder(Path.Combine(projectRoot, "Assets"));
        try { r.EngineNet = Assembly.LoadFrom(engineNetDll); }
        catch (Exception e) { log("[uyari] DigitoyEngine .NET dll yuklenemedi: " + e.Message); }
        r.Store = new PluginSettingsStore(projectRoot, r.EngineNet);
        if (sources.Count == 0 || r.EngineNet == null) return r;
        string dll = Path.Combine(workDir, projName + ".Editor.dll");
        var build = new RoslynBuild
        {
            AssemblyName = projName + ".Editor",
            SourceFiles = sources,
            References = { engineNetDll, typeof(ProjectHooks).Assembly.Location },
            Defines = new[] { "DE_EDITOR", "DE_GAME" },
        };
        var res = RoslynCompiler.CompileToFile(build, dll, useCache: true);
        if (!res.Ok) throw new Exception("Editor.dll derlemesi basarisiz (Assets/**/Editor kancalari):\n" + res.Errors);
        try { r.EditorAsm = Assembly.LoadFrom(dll); }
        catch (Exception e) { log("[uyari] Editor.dll yuklenemedi: " + e.Message); }
        return r;
    }

    static void Fail(PluginSettingsStore store)
    {
        if (store.Errors.Count == 0) return;
        throw new Exception("plugin ayarlari eksik:\n  " + string.Join("\n  ", store.Errors));
    }

    // ================= Android =================
    public static AndroidProject RunAndroid(Result r, string projectRoot, Action<string> log)
    {
        var p = new AndroidProject { ProjectRoot = projectRoot, Store = r.Store };
        ProjectHooks.Run<AndroidProject, IAndroidProjectHook>(r.EditorAsm, p, () => new AndroidProject(), (sub, h) => h.OnGenerate(sub), r.Folders, r.Live, log);
        Fail(r.Store);
        foreach (var s in p.Log) log("  " + s);
        return p;
    }

    // generated/: plugins/<Ad>/ (modul kopyasi), plugins.settings.gradle.kts, plugins.deps.gradle.kts, gradle-plugins.properties,
    // plugins.repos.txt, res/values/plugin_strings.xml, java/.../Plugins.kt, app/<appFiles>. root = Build/android.
    public static void EmitAndroid(Result r, AndroidProject p, string root, string gen, Action<string, string> copyTree, Action<string, string> writeIfChanged, Action<string> log)
    {
        string pdir = Path.Combine(gen, "plugins");
        if (Directory.Exists(pdir)) Directory.Delete(pdir, true);
        var settings = new StringBuilder("// DigitoyEngine: her build yeniden yazilir — plugin Gradle library modulleri (docs/plugins.md)\n");
        var deps = new StringBuilder("// DigitoyEngine: her build yeniden yazilir — plugin modulleri, app-seviyesi gradle plugin'leri, surum sabitleri\n");
        var kt = new StringBuilder("package com.digitoy.host\n\n// URETILDI (aotcompiler): canli plugin'lerin DEPlugin turevleri. Her build yeniden yazilir.\n");
        var ktImports = new List<string>(); var ktNews = new List<string>();
        foreach (var f in r.LiveFolders.Where(f => f.HasAndroid))
        {
            string dst = Path.Combine(pdir, f.Name);
            copyTree(f.Android, dst);
            settings.Append($"include(\":plugins:{f.Name}\")\nproject(\":plugins:{f.Name}\").projectDir = file(\"generated/plugins/{f.Name}\")\n");
            deps.Append($"dependencies {{ add(\"implementation\", project(\":plugins:{f.Name}\")) }}\n");
            string cls = FindDEPluginClass(dst);
            if (cls == null) { log($"[uyari] {f.Name}/Android: DEPlugin turevi bulunamadi (onMessage yok -> Available=false)"); continue; }
            ktImports.Add(cls); ktNews.Add($"{cls.Substring(cls.LastIndexOf('.') + 1)}()");
        }
        foreach (var d in p.Dependencies) deps.Append($"dependencies {{ add(\"implementation\", \"{d}\") }}\n");
        foreach (var kv in p.GradlePlugins) deps.Append($"apply(plugin = \"{kv.Key}\")\n");
        if (p.ForcedVersions.Count > 0)
        {
            deps.Append("dependencies { constraints {\n");
            foreach (var kv in p.ForcedVersions) deps.Append($"    add(\"implementation\", \"{kv.Key}\") {{ version {{ strictly(\"{kv.Value}\") }} }}\n");
            deps.Append("} }\n");
        }
        writeIfChanged(Path.Combine(gen, "plugins.settings.gradle.kts"), settings.ToString());
        writeIfChanged(Path.Combine(gen, "plugins.deps.gradle.kts"), deps.ToString());
        writeIfChanged(Path.Combine(gen, "gradle-plugins.properties"), "# DigitoyEngine: app-seviyesi gradle plugin surumleri (id=version)\n" + string.Concat(p.GradlePlugins.Select(kv => $"{kv.Key}={kv.Value}\n")));
        writeIfChanged(Path.Combine(gen, "plugins.repos.txt"), "# DigitoyEngine: ek maven depolari\n" + string.Concat(p.MavenRepos.Select(u => u + "\n")));
        // res/values/plugin_strings.xml
        if (p.ResStrings.Count > 0)
        {
            Directory.CreateDirectory(Path.Combine(gen, "res", "values"));
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<resources>\n");
            foreach (var kv in p.ResStrings) sb.Append($"    <string name=\"{kv.Key}\" translatable=\"false\">{XmlEsc(kv.Value)}</string>\n");
            sb.Append("</resources>\n");
            File.WriteAllText(Path.Combine(gen, "res", "values", "plugin_strings.xml"), sb.ToString());
        }
        // Plugins.kt
        foreach (var i in ktImports.Distinct()) kt.Append($"import {i}\n");
        kt.Append("\nobject Plugins {\n    @JvmStatic val all: Array<DEPlugin> = arrayOf(" + string.Join(", ", ktNews) + ")\n}\n");
        Directory.CreateDirectory(Path.Combine(gen, "java", "com", "digitoy", "host"));
        File.WriteAllText(Path.Combine(gen, "java", "com", "digitoy", "host", "Plugins.kt"), kt.ToString());
        // app files (google-services.json)
        foreach (var (src, rel) in p.AppFiles)
        {
            string dst = Path.Combine(root, "app", rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(src, dst, true);
            log($"app dosyasi: app/{rel}");
        }
    }

    // Gradle modulunde `class X : DEPlugin(` / `: DEPlugin("` arayan basit tarama -> tam sinif adi (package + sinif).
    static string FindDEPluginClass(string moduleDir)
    {
        foreach (var f in Directory.GetFiles(moduleDir, "*.kt", SearchOption.AllDirectories).Concat(Directory.GetFiles(moduleDir, "*.java", SearchOption.AllDirectories)))
        {
            string text = File.ReadAllText(f);
            var m = System.Text.RegularExpressions.Regex.Match(text, @"class\s+(\w+)\s*(\([^)]*\))?\s*(:|extends)\s*(com\.digitoy\.host\.)?DEPlugin\b");
            if (!m.Success) continue;
            var pk = System.Text.RegularExpressions.Regex.Match(text, @"^\s*package\s+([\w.]+)", System.Text.RegularExpressions.RegexOptions.Multiline);
            return (pk.Success ? pk.Groups[1].Value + "." : "") + m.Groups[1].Value;
        }
        return null;
    }

    // ================= iOS =================
    public static IosProject RunIos(Result r, string projectRoot, Action<string> log)
    {
        var p = new IosProject { ProjectRoot = projectRoot, Store = r.Store };
        ProjectHooks.Run<IosProject, IIosProjectHook>(r.EditorAsm, p, () => new IosProject(), (sub, h) => h.OnGenerate(sub), r.Folders, r.Live, log);
        Fail(r.Store);
        foreach (var s in p.Log) log("  " + s);
        return p;
    }

    public sealed class IosXc { public string LdExtra = ""; public string Lines = ""; }

    // generated/DEPlugins/Package.swift (+Sources placeholder, binaryTarget'lar), generated/Info.plist (kabuk Game/Info.plist + merge),
    // generated/Game.entitlements, cpp/plugins/ (plugin kaynaklari). Donus: xcconfig ek satirlari + OTHER_LDFLAGS ekleri.
    public static IosXc EmitIos(Result r, IosProject p, string basePlist, string gen, string cpp, string minIos, Action<string> log)
    {
        // kaynaklar: cpp/plugins/<Ad>/ (synchronized folder her seyi derler; .m/.mm/.swift/.c/.h)
        string pdir = Path.Combine(cpp, "plugins");
        if (Directory.Exists(pdir)) Directory.Delete(pdir, true);
        foreach (var f in r.LiveFolders.Where(f => f.HasIos))
        {
            string dst = Path.Combine(pdir, f.Name);
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(f.Ios, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(f.Ios, file);
                if (rel.Replace('\\', '/').Contains(".xcframework/")) continue; // paket binaryTarget olarak gider
                string to = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(file, to, true);
            }
        }
        // Package.swift: tek urun DEPlugins; bagimliliklar + binaryTarget'lar
        string pkg = Path.Combine(gen, "DEPlugins");
        Directory.CreateDirectory(Path.Combine(pkg, "Sources", "DEPlugins"));
        var deps = new StringBuilder(); var prods = new StringBuilder(); var bins = new StringBuilder(); var binNames = new List<string>();
        foreach (var kv in p.SwiftPackages)
        {
            string id = PackageId(kv.Key);
            deps.Append($"        .package(url: \"{kv.Key}\", from: \"{kv.Value.From}\"),\n");
            foreach (var pr in kv.Value.Products) prods.Append($"            .product(name: \"{pr}\", package: \"{id}\"),\n");
        }
        foreach (var xf in p.XcFrameworks)
        {
            string name = Path.GetFileNameWithoutExtension(xf);
            string dst = Path.Combine(pkg, Path.GetFileName(xf));
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
            CopyDir(xf, dst);
            bins.Append($"        .binaryTarget(name: \"{name}\", path: \"{Path.GetFileName(xf)}\"),\n");
            binNames.Add(name);
        }
        foreach (var b in binNames) prods.Append($"            .target(name: \"{b}\"),\n");
        string hash = Hash(deps.ToString() + prods + bins);
        string swift = "// swift-tools-version:5.9\n// DigitoyEngine: HER BUILD yeniden yazilir (docs/plugins.md). Icerik hash: " + hash + "\n" +
            "// Xcode eski icerigi gosteriyorsa: File > Packages > Reset Package Caches.\n" +
            "import PackageDescription\n\nlet package = Package(\n    name: \"DEPlugins\",\n" +
            $"    platforms: [.iOS(\"{minIos}\")],\n" +
            "    products: [.library(name: \"DEPlugins\", targets: [\"DEPlugins\"])],\n" +
            "    dependencies: [\n" + deps + "    ],\n" +
            "    targets: [\n        .target(name: \"DEPlugins\", dependencies: [\n" + prods + "        ], path: \"Sources/DEPlugins\"),\n" + bins + "    ]\n)\n";
        File.WriteAllText(Path.Combine(pkg, "Package.swift"), swift);
        File.WriteAllText(Path.Combine(pkg, "Sources", "DEPlugins", "DEPlugins.swift"), "// DigitoyEngine: SPM hedefi bos olamaz; plugin kodu generated/cpp/plugins altinda (app target). Uretilir.\npublic enum DEPluginsMarker {}\n");
        // Info.plist: kabuk Game/Info.plist tabani (yoksa sablon) + plugin anahtarlari + URL scheme + background modes
        var plist = File.Exists(basePlist) ? Plist.Parse(File.ReadAllText(basePlist)) : new Dictionary<string, object>();
        foreach (var kv in p.Plist) plist[kv.Key] = kv.Value;
        if (p.UrlSchemes.Count > 0)
        {
            var types = plist.TryGetValue("CFBundleURLTypes", out var t) && t is List<object> l ? l : new List<object>();
            types.Add(new Dictionary<string, object> { ["CFBundleURLSchemes"] = p.UrlSchemes.Cast<object>().ToList() });
            plist["CFBundleURLTypes"] = types;
        }
        if (p.BackgroundModes.Count > 0)
        {
            var modes = plist.TryGetValue("UIBackgroundModes", out var m) && m is List<object> ml ? ml : new List<object>();
            foreach (var bm in p.BackgroundModes) if (!modes.Contains(bm)) modes.Add(bm);
            plist["UIBackgroundModes"] = modes;
        }
        File.WriteAllText(Path.Combine(gen, "Info.plist"), Plist.Write(plist));
        File.WriteAllText(Path.Combine(gen, "Game.entitlements"), Plist.Write(p.Entitlements.ToDictionary(k => k.Key, v => v.Value)));
        // xcconfig ekleri
        var x = new IosXc();
        x.Lines = "INFOPLIST_FILE = generated/Info.plist\nCODE_SIGN_ENTITLEMENTS = generated/Game.entitlements\nSWIFT_OPTIMIZATION_LEVEL = -Osize\nSWIFT_VERSION = 5.0\n";
        x.LdExtra = string.Join(" ", p.Frameworks.Select(f => "-framework " + f).Concat(p.LdFlags));
        return x;
    }

    static string PackageId(string url)
    {
        string s = url.TrimEnd('/');
        s = s.Substring(s.LastIndexOf('/') + 1);
        if (s.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
        return s;
    }

    // ================= WASM =================
    public static WasmProject RunWasm(Result r, string projectRoot, Action<string> log)
    {
        var p = new WasmProject { ProjectRoot = projectRoot, Store = r.Store };
        ProjectHooks.Run<WasmProject, IWasmProjectHook>(r.EditorAsm, p, () => new WasmProject(), (sub, h) => h.OnGenerate(sub), r.Folders, r.Live, log);
        Fail(r.Store);
        return p;
    }

    // Build/wasm/plugins.js: DEConfig + harici script enjeksiyonu (senkron document.write — main()'den once) + plugin js'leri birlestirilmis.
    // Donus: emcc link'e eklenecek --pre-js/--post-js bayraklari; plugin .c dosyalari out listesine.
    public static string EmitWasm(Result r, WasmProject p, string outDir, List<string> extraC)
    {
        var js = new StringBuilder("// DigitoyEngine: her build yeniden yazilir (docs/plugins.md). DE.register(plugin) ile kaydolun; onMessage(msg)/DE.send(name,msg).\n");
        js.Append("window.DE = window.DE || { plugins: {}, register: function (p) { if (p && p.name) DE.plugins[p.name] = p; } };\n");
        js.Append("window.DEConfig = " + JsonObj(p.Config) + ";\n");
        foreach (var s in p.Scripts) js.Append($"document.write('<script src=\"{s}\"><\\/script>');\n");
        foreach (var f in r.LiveFolders.Where(f => f.HasWasm))
        {
            foreach (var file in Directory.GetFiles(f.Wasm, "*.js")) js.Append($"\n// ---- {f.Name}/{Path.GetFileName(file)}\n").Append(File.ReadAllText(file)).Append('\n');
            extraC.AddRange(Directory.GetFiles(f.Wasm, "*.c"));
        }
        File.WriteAllText(Path.Combine(outDir, "plugins.js"), js.ToString());
        var flags = new StringBuilder();
        foreach (var f in p.PreJs) flags.Append($" --pre-js \"{f}\"");
        foreach (var f in p.PostJs) flags.Append($" --post-js \"{f}\"");
        return flags.ToString();
    }

    // ================= yardimcilar =================
    static string XmlEsc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "\\'");
    static string JsonObj(Dictionary<string, string> d) => "{" + string.Join(",", d.Select(kv => $"\"{kv.Key}\":\"{kv.Value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"")) + "}";
    static string Hash(string s) { unchecked { uint h = 2166136261; foreach (char c in s) h = (h ^ c) * 16777619; return h.ToString("x8"); } }
    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            string to = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(f, to, true);
        }
    }

    // Minimal plist XML okuma/yazma: string/bool/integer/array/dict (kabuk Info.plist + plugin degerleri icin yeterli).
    public static class Plist
    {
        public static Dictionary<string, object> Parse(string xml)
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            var dict = doc.Root?.Element("dict");
            return dict == null ? new Dictionary<string, object>() : (Dictionary<string, object>)Read(dict);
        }
        static object Read(System.Xml.Linq.XElement e)
        {
            switch (e.Name.LocalName)
            {
                case "dict":
                    var d = new Dictionary<string, object>();
                    var kids = e.Elements().ToList();
                    for (int i = 0; i + 1 < kids.Count; i += 2) d[kids[i].Value] = Read(kids[i + 1]);
                    return d;
                case "array": return e.Elements().Select(Read).ToList();
                case "true": return true;
                case "false": return false;
                case "integer": return int.Parse(e.Value);
                default: return e.Value;
            }
        }
        public static string Write(Dictionary<string, object> d)
        {
            var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<!-- DigitoyEngine: HER BUILD yeniden yazilir (kabuk Game/Info.plist + plugin anahtarlari). Elle duzenlemeyin. -->\n<plist version=\"1.0\">\n");
            WriteValue(sb, d, 0);
            sb.Append("</plist>\n");
            return sb.ToString();
        }
        static void WriteValue(StringBuilder sb, object v, int ind)
        {
            string t = new string('\t', ind);
            switch (v)
            {
                case Dictionary<string, object> d:
                    sb.Append(t).Append("<dict>\n");
                    foreach (var kv in d) { sb.Append(t).Append("\t<key>").Append(Esc(kv.Key)).Append("</key>\n"); WriteValue(sb, kv.Value, ind + 1); }
                    sb.Append(t).Append("</dict>\n"); break;
                case List<object> l:
                    sb.Append(t).Append("<array>\n"); foreach (var x in l) WriteValue(sb, x, ind + 1); sb.Append(t).Append("</array>\n"); break;
                case string[] sa: WriteValue(sb, sa.Cast<object>().ToList(), ind); break;
                case bool b: sb.Append(t).Append(b ? "<true/>\n" : "<false/>\n"); break;
                case int i: sb.Append(t).Append("<integer>").Append(i).Append("</integer>\n"); break;
                default: sb.Append(t).Append("<string>").Append(Esc(v?.ToString() ?? "")).Append("</string>\n"); break;
            }
        }
        static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
