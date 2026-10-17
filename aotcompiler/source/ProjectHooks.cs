using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DigitoyEngine.Build
{
    // Platform proje uretim kancalari (docs/plugins.md). Assets/**/Editor/**/*.cs -> <Ad>.Editor.dll (normal .NET'e karsi;
    // aotcompiler ve editor ayni Roslyn derlemesini yapar). Uygulayan tipler ad sirasiyla cagrilir; yalniz CANLI plugin'lerin
    // klasorundekiler + plugin disi (oyunun kendi) kancalar. Kancalar yalniz generated/'a yazilacak VERI toplar; dosya uretimi
    // aotcompiler'da (Program.Emit*). Bu dosya editor projesine Link ile de girer (PlayerSettings penceresi ayar dogrulama).
    public interface IAndroidProjectHook { void OnGenerate(AndroidProject p); }
    public interface IIosProjectHook { void OnGenerate(IosProject p); }
    public interface IWasmProjectHook { void OnGenerate(WasmProject p); }

    // Ortak: ayar erisimi + plugin klasoru.
    public abstract class PlatformProject
    {
        public string ProjectRoot { get; internal set; }
        // Kanca sinifinin ait oldugu plugin klasoru (Assets/Plugins/<Ad>); plugin disi kanca icin null.
        public string PluginDir { get; internal set; }
        public string PluginName { get; internal set; }
        internal PluginSettingsStore Store;
        internal List<string> Log = new List<string>();

        // ProjectSettings/Plugins/<TypeName>.asset -> dogrulanmis nesne (dosya yoksa new T()). [Required] bos -> build hatasi.
        public T Settings<T>() where T : class, new() => Store.Load<T>(PluginName ?? typeof(T).Name);

        // Plugin klasorune goreli dosya (AppFile/XcFramework/PreJs icin) -> mutlak yol; yoksa hata.
        protected string PluginFile(string rel)
        {
            if (PluginDir == null) throw new InvalidOperationException("plugin disi kanca plugin dosyasi veremez: " + rel);
            string p = Path.GetFullPath(Path.Combine(PluginDir, rel));
            if (!File.Exists(p)) throw new FileNotFoundException("plugin dosyasi yok: " + p);
            return p;
        }
        public bool HasPluginFile(string rel) => PluginDir != null && File.Exists(Path.Combine(PluginDir, rel));
        public void Warn(string s) => Log.Add("[uyari] " + (PluginName ?? "game") + ": " + s);
        protected void Note(string s) => Log.Add((PluginName ?? "game") + ": " + s);
    }

    public sealed class AndroidProject : PlatformProject
    {
        public readonly Dictionary<string, string> ResStrings = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> ManifestPlaceholders = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> GradlePlugins = new Dictionary<string, string>(StringComparer.Ordinal); // id -> version
        public readonly List<string> Dependencies = new List<string>();      // app seviyesi (nadiren; normalde modulde)
        public readonly List<string> MavenRepos = new List<string>();
        public readonly Dictionary<string, string> ForcedVersions = new Dictionary<string, string>(StringComparer.Ordinal); // group:artifact -> version
        public readonly List<(string src, string dstRel)> AppFiles = new List<(string, string)>(); // -> Build/android/app/<dstRel>

        // res/values/plugin_strings.xml: <string name="k">v</string> (library manifest'i @string/k okur).
        public void ResString(string name, string value) { ResStrings[name] = value ?? ""; Note($"resString {name}"); }
        // manifestPlaceholders[name] (kabuk app.properties uzerinden okur).
        public void ManifestPlaceholder(string name, string value) { ManifestPlaceholders[name] = value ?? ""; }
        // App seviyesi Gradle plugin'i (google-services, crashlytics). Ayni id -> en yuksek surum; major farki -> hata.
        public void GradlePlugin(string id, string version)
        {
            if (GradlePlugins.TryGetValue(id, out var have) && have != version)
            {
                if (Major(have) != Major(version))
                    throw new Exception($"android: gradle plugin '{id}' surum cakismasi {have} / {version} (major farkli) — IAndroidProjectHook'larda tek surumde birlesin");
                Note($"gradle plugin {id}: {have} vs {version} -> {Max(have, version)}");
            }
            GradlePlugins[id] = GradlePlugins.TryGetValue(id, out var h2) ? Max(h2, version) : version;
        }
        public void Dependency(string coordinate) { if (!Dependencies.Contains(coordinate)) Dependencies.Add(coordinate); }
        public void MavenRepo(string url) { if (!MavenRepos.Contains(url)) MavenRepos.Add(url); }
        // Transitive cakisma kirici: constraints { implementation("g:a") { version { strictly(v) } } }.
        public void ForceVersion(string groupArtifact, string version) { ForcedVersions[groupArtifact] = version; Note($"force {groupArtifact}:{version}"); }
        // Plugin klasorunden app/'e kopya (google-services.json gibi).
        public void AppFile(string pluginRel, string appRel = null) { AppFiles.Add((PluginFile(pluginRel), appRel ?? Path.GetFileName(pluginRel))); }

        internal static int Major(string v) { int d = v.IndexOf('.'); return int.TryParse(d > 0 ? v.Substring(0, d) : v, out int m) ? m : 0; }
        internal static string Max(string a, string b) => CompareVersions(a, b) >= 0 ? a : b;
        internal static int CompareVersions(string a, string b)
        {
            var pa = a.Split('.'); var pb = b.Split('.');
            for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                int x = i < pa.Length && int.TryParse(pa[i], out var xi) ? xi : 0;
                int y = i < pb.Length && int.TryParse(pb[i], out var yi) ? yi : 0;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }
    }

    public sealed class IosProject : PlatformProject
    {
        public sealed class SwiftPkg { public string Url, From; public List<string> Products = new List<string>(); }
        public readonly Dictionary<string, SwiftPkg> SwiftPackages = new Dictionary<string, SwiftPkg>(StringComparer.OrdinalIgnoreCase); // url -> pkg
        public readonly List<string> XcFrameworks = new List<string>();   // mutlak yol (plugin klasoru)
        public readonly List<string> Frameworks = new List<string>();     // sistem framework adi
        public readonly List<string> LdFlags = new List<string>();
        public readonly Dictionary<string, object> Plist = new Dictionary<string, object>(StringComparer.Ordinal); // string | string[] | Dictionary<string,object> | bool | int
        public readonly List<string> UrlSchemes = new List<string>();
        public readonly Dictionary<string, object> Entitlements = new Dictionary<string, object>(StringComparer.Ordinal);
        public readonly List<string> BackgroundModes = new List<string>();

        // Yerel SPM paketi (generated/DEPlugins/Package.swift). products ZORUNLU = iOS'ta asil trim (ObjC sinif stripping yok).
        // Ayni url -> en yuksek from. exact yok (SPM cozemez).
        public void SwiftPackage(string url, string from, params string[] products)
        {
            if (products == null || products.Length == 0) throw new ArgumentException("ios: SwiftPackage product listesi zorunlu: " + url);
            if (!SwiftPackages.TryGetValue(url, out var p)) SwiftPackages[url] = p = new SwiftPkg { Url = url, From = from };
            else if (p.From != from) { Note($"spm {url}: {p.From} vs {from} -> {AndroidProject.Max(p.From, from)}"); p.From = AndroidProject.Max(p.From, from); }
            foreach (var pr in products) if (!p.Products.Contains(pr)) p.Products.Add(pr);
        }
        public void XcFramework(string pluginRel) { string p = PluginFile(pluginRel); if (!XcFrameworks.Contains(p)) XcFrameworks.Add(p); }
        public void Framework(string name) { if (!Frameworks.Contains(name)) Frameworks.Add(name); }
        public void LdFlag(string flag) { if (!LdFlags.Contains(flag)) LdFlags.Add(flag); }
        public void UrlScheme(string scheme) { if (!UrlSchemes.Contains(scheme)) UrlSchemes.Add(scheme); }
        public void Entitlement(string key, object value) { Entitlements[key] = value; }
        public void BackgroundMode(string mode) { if (!BackgroundModes.Contains(mode)) BackgroundModes.Add(mode); }
    }

    public sealed class WasmProject : PlatformProject
    {
        public readonly List<string> Scripts = new List<string>();   // <script src=url> (plugins.js enjekte eder)
        public readonly List<string> PreJs = new List<string>();     // emcc --pre-js (mutlak)
        public readonly List<string> PostJs = new List<string>();    // emcc --post-js
        public readonly Dictionary<string, string> Config = new Dictionary<string, string>(StringComparer.Ordinal); // window.DEConfig

        public void Script(string url) { if (!Scripts.Contains(url)) Scripts.Add(url); }
        public void PreJsFile(string pluginRel) { PreJs.Add(PluginFile(pluginRel)); }
        public void PostJsFile(string pluginRel) { PostJs.Add(PluginFile(pluginRel)); }
        public void ConfigValue(string key, string value) { Config[key] = value ?? ""; }
    }

    // Plugin klasor konvansiyonu: Assets/Plugins/<Ad>/{Runtime,Editor,Android,iOS,WASM}. Ad = klasor adi = C# plugin sinifi kisa adi.
    public sealed class PluginFolder
    {
        public string Name, Dir;
        public string Runtime => Path.Combine(Dir, "Runtime");
        public string Editor => Path.Combine(Dir, "Editor");
        public string Android => Path.Combine(Dir, "Android");
        public string Ios => Path.Combine(Dir, "iOS");
        public string Wasm => Path.Combine(Dir, "WASM");
        public bool HasAndroid => Directory.Exists(Android);
        public bool HasIos => Directory.Exists(Ios);
        public bool HasWasm => Directory.Exists(Wasm);

        public static List<PluginFolder> Scan(string projectRoot)
        {
            var list = new List<PluginFolder>();
            string root = Path.Combine(projectRoot, "Assets", "Plugins");
            if (!Directory.Exists(root)) return list;
            foreach (var d in Directory.GetDirectories(root).OrderBy(x => x, StringComparer.Ordinal))
                list.Add(new PluginFolder { Name = Path.GetFileName(d), Dir = d });
            return list;
        }

        // Asset sayilmayan plugin alt klasorleri (importer/pak/watcher disli): Android, iOS, WASM.
        public static bool IsNativeFolderPath(string relPath)
        {
            var p = relPath.Replace('\\', '/');
            if (!p.StartsWith("Plugins/", StringComparison.OrdinalIgnoreCase) && !p.Contains("/Plugins/")) return false;
            return p.Contains("/Android/") || p.Contains("/iOS/") || p.Contains("/WASM/");
        }
    }

    // Kanca calistirici: Editor.dll + oyun .NET dll'inden tipleri bulur; plugin klasoru = kanca tipinin kaynak dosyasi degil,
    // tipin tanimlandigi assembly'de ayirt edilemez -> KURAL: kanca sinifinin adi plugin adiyla BASLAR (FacebookAndroid, FacebookIos)
    // ya da [PluginHook("Facebook")] attribute'u tasir; eslesmeyen = oyunun kendi kancasi (her zaman canli).
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PluginHookAttribute : Attribute { public string Plugin; public PluginHookAttribute(string plugin) { Plugin = plugin; } }

    public static class ProjectHooks
    {
        public static string OwnerPlugin(Type hookType, IReadOnlyList<PluginFolder> plugins)
        {
            var a = hookType.GetCustomAttribute<PluginHookAttribute>();
            if (a != null) return a.Plugin;
            PluginFolder best = null;
            foreach (var p in plugins)
                if (hookType.Name.StartsWith(p.Name, StringComparison.Ordinal) && (best == null || p.Name.Length > best.Name.Length))
                    best = p;
            return best?.Name;
        }

        // livePlugins: canli plugin adlari (null = hepsi). Donus: calistirilan kanca sayisi.
        public static int Run<TProject, THook>(Assembly editorAsm, TProject proto, Func<TProject> fresh, Action<TProject, THook> invoke,
            IReadOnlyList<PluginFolder> plugins, ISet<string> livePlugins, Action<string> log) where TProject : PlatformProject
        {
            if (editorAsm == null) return 0;
            int n = 0;
            foreach (var t in editorAsm.GetTypes().Where(t => !t.IsAbstract && typeof(THook).IsAssignableFrom(t)).OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                string owner = OwnerPlugin(t, plugins);
                if (owner != null && livePlugins != null && !livePlugins.Contains(owner)) { log($"kanca atlandi (plugin canli degil): {t.Name}"); continue; }
                var folder = owner != null ? plugins.FirstOrDefault(p => p.Name == owner) : null;
                var sub = fresh();
                sub.ProjectRoot = proto.ProjectRoot; sub.Store = proto.Store; sub.PluginDir = folder?.Dir; sub.PluginName = owner; sub.Log = proto.Log;
                var hook = (THook)Activator.CreateInstance(t);
                invoke(sub, hook);
                Merge(proto, sub);
                log($"kanca: {t.Name}" + (owner != null ? $" ({owner})" : ""));
                n++;
            }
            return n;
        }

        static void Merge<T>(T into, T from) where T : PlatformProject
        {
            switch (into)
            {
                case AndroidProject a:
                    var b = (AndroidProject)(object)from;
                    foreach (var kv in b.ResStrings) a.ResStrings[kv.Key] = kv.Value;
                    foreach (var kv in b.ManifestPlaceholders) a.ManifestPlaceholders[kv.Key] = kv.Value;
                    foreach (var kv in b.GradlePlugins) a.GradlePlugin(kv.Key, kv.Value);
                    foreach (var d in b.Dependencies) a.Dependency(d);
                    foreach (var r in b.MavenRepos) a.MavenRepo(r);
                    foreach (var kv in b.ForcedVersions) a.ForcedVersions[kv.Key] = kv.Value;
                    a.AppFiles.AddRange(b.AppFiles);
                    break;
                case IosProject i:
                    var j = (IosProject)(object)from;
                    foreach (var kv in j.SwiftPackages) i.SwiftPackage(kv.Key, kv.Value.From, kv.Value.Products.ToArray());
                    foreach (var x in j.XcFrameworks) if (!i.XcFrameworks.Contains(x)) i.XcFrameworks.Add(x);
                    foreach (var f in j.Frameworks) i.Framework(f);
                    foreach (var f in j.LdFlags) i.LdFlag(f);
                    foreach (var kv in j.Plist) { if (i.Plist.ContainsKey(kv.Key)) i.Log.Add($"plist '{kv.Key}' cakisti: son kazandi"); i.Plist[kv.Key] = kv.Value; }
                    foreach (var s in j.UrlSchemes) i.UrlScheme(s);
                    foreach (var kv in j.Entitlements) i.Entitlements[kv.Key] = kv.Value;
                    foreach (var m in j.BackgroundModes) i.BackgroundMode(m);
                    break;
                case WasmProject w:
                    var v = (WasmProject)(object)from;
                    foreach (var s in v.Scripts) w.Script(s);
                    w.PreJs.AddRange(v.PreJs); w.PostJs.AddRange(v.PostJs);
                    foreach (var kv in v.Config) w.Config[kv.Key] = kv.Value;
                    break;
            }
        }
    }

    // Plugin ayar deposu: ProjectSettings/Plugins/<Name>.asset (YAML; ObjectSerializer bicimi: type + fields) -> tipli nesne.
    // aotcompiler/editor .NET'te: reflection ile alanlar; [Required] bos string/null -> hata (canli plugin icin build durur).
    // YAML okuma: engine'in Yaml/ObjectSerializer'i DE_EDITOR'lu .NET DigitoyEngine.dll'inde (editor); aotcompiler'da o dll
    // yuklenmis olur (Editor.dll referansi) -> reflection ile cagirilir (derleme zamani bagimlilik yok).
    public sealed class PluginSettingsStore
    {
        readonly string _dir;
        readonly Assembly _engineNet;
        readonly Dictionary<string, object> _cache = new Dictionary<string, object>();
        public readonly List<string> Errors = new List<string>();

        public PluginSettingsStore(string projectRoot, Assembly engineNetDll)
        {
            _dir = Path.Combine(projectRoot, "ProjectSettings", "Plugins");
            _engineNet = engineNetDll;
        }

        public static string PathFor(string projectRoot, string name) => Path.Combine(projectRoot, "ProjectSettings", "Plugins", name + ".asset");

        public T Load<T>(string name) where T : class, new() => (T)Load(name, typeof(T));

        public object Load(string name, Type t)
        {
            if (_cache.TryGetValue(name, out var o)) return o;
            o = Activator.CreateInstance(t);
            string path = Path.Combine(_dir, name + ".asset");
            if (File.Exists(path))
            {
                var ser = _engineNet?.GetType("DigitoyEngine.ObjectSerializer");
                var m = ser?.GetMethod("LoadInto", new[] { typeof(object), typeof(string), _engineNet.GetType("DigitoyEngine.AssetDatabase") });
                if (m == null) throw new Exception("ObjectSerializer.LoadInto bulunamadi (DigitoyEngine .NET dll DE_EDITOR ile derlenmis olmali)");
                m.Invoke(null, new object[] { o, path, null });
            }
            Validate(name, o);
            _cache[name] = o;
            return o;
        }

        // [Required] alanlar: null / bos string / bos dizi -> hata satiri.
        public void Validate(string name, object o)
        {
            foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                // aotcompiler engine'e referans vermez: attribute adiyla (DigitoyEngine.RequiredAttribute).
                if (!f.GetCustomAttributes(false).Any(a => a.GetType().FullName == "DigitoyEngine.RequiredAttribute")) continue;
                var v = f.GetValue(o);
                bool empty = v == null || (v is string s && string.IsNullOrWhiteSpace(s)) || (v is Array arr && arr.Length == 0);
                if (empty) Errors.Add($"{name}.{f.Name} zorunlu (Player Settings > Plugins > {name})");
            }
        }
    }
}
