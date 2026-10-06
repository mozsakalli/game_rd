using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// ScriptedImporter modeli: editor cekirdegi HICBIR kaynak formatini bilmez.
// [AssetImporter(".ttf")] tasiyan siniflar assembly taramasiyla bulunur
// (MenuItem/EditorTool/RpcCommand deseni — oyun assembly'si kendi importer'ini
// ekleyebilir, ALC reload guvenli). Kaynak dosya Assets/'te kalir; import
// ciktilari (artifact) Library/Artifacts/<guid>/<ad> altina yazilir. Runtime
// KAYNAGI degil ARTIFACT'i yukler (AssetDatabase.ArtifactResolver editorde
// buraya baglanir; release'te pak zaten artifact'leri tasir: "main" asset
// anahtariyla, digerleri "<anahtar>#<ad>" ile).
//
// Import AYARLARI: importer bir [Serializable] ayar sinifi bildirirse (Settings)
// degerler kaynagin .meta dosyasindaki "importer:" blogundan okunur (Inspector
// duzenler). Ayarlar ve bildirilen BAGIMLILIKLAR (CollectDependencies) stamp'e
// girer — ayar/bagimlilik degisince otomatik reimport.
[AttributeUsage(AttributeTargets.Class)]
public sealed class AssetImporterAttribute : Attribute
{
    public readonly string[] Extensions;
    public int Version = 1;    // artir = tum bu tur asset'ler otomatik reimport
    public string AssetType;   // cok-tipli uzanti (".asset") icin yaml "type:" filtresi
    public Type Settings;      // [Serializable] ayar sinifi (yoksa ayarsiz)

    public AssetImporterAttribute(params string[] extensions) => Extensions = extensions;
}

public abstract class AssetImporter
{
    public abstract void Import(ImportContext ctx);

    // Kaynak dosya DISINDAKI girdiler (baska asset'ler/artifact'ler): stamp'e girer.
    // Girdiyi YUTAN importer (atlas: uyeler sayfada yasar) DependsOn yerine Absorbs
    // bildirir — pak builder uyeyi tek basina paketlemez, tuketiciye kenar yazar.
    public virtual void CollectDependencies(DependencyContext ctx) { }
}

// Artifact'in pak'taki kaderi. Importer bildirir, builder format bilmeden uygular.
public enum ArtifactScope
{
    Runtime,    // her zaman pak'a girer (main ve runtime'in okudugu ekler)
    Editor,     // editor-ici sozlesme (importer'lar arasi manifesto); pak'a girmez
    Standalone, // asset bir tuketici tarafindan YUTULMAMISSA pak'a girer (fontun kendi sheet'i)
}

public sealed class ImportContext
{
    public string SourcePath;  // tam yol
    public string AssetPath;   // Assets'e goreli (asset anahtari)
    public object Settings;    // Settings tipi (yoksa null)
    internal readonly List<(string Name, byte[] Data, ArtifactScope Scope)> Artifacts = new();
    internal string Error;
    internal Func<string, bool> Filter; // build filtresi (null = editor import, hepsi canli)

    // "main" = kaynak anahtariyla yuklenen birincil cikti (pak bunu paketler).
    public void AddArtifact(string name, byte[] data, ArtifactScope scope = ArtifactScope.Runtime)
        => Artifacts.Add((name, data, scope));

    public void Fail(string message) => Error = message;

    // Bagimlilik artifact'i (gerekirse once import edilir). Importer'siz dosya icin null.
    public byte[] ReadArtifact(string assetPath, string name) => ImportPipeline.GetArtifact(assetPath, name);

    public string FullPath(string assetPath) => ImportPipeline.FullPath(assetPath);

    // Girdi bu import'ta kullanilmali mi: editor'de daima true; pak build'inde yalniz
    // sahne grafindan erisilen girdiler. Yutan importer'lar girdilerini bununla suzer.
    public bool IsLive(string assetPath) => Filter == null || Filter(assetPath);
}

public sealed class DependencyContext
{
    public string SourcePath;
    public string AssetPath;
    public object Settings;
    internal readonly List<string> Deps = new();
    internal readonly List<string> Absorbed = new();

    public void DependsOn(string assetPath)
    {
        if (!string.IsNullOrEmpty(assetPath) && !Deps.Contains(assetPath))
            Deps.Add(assetPath);
    }

    // Girdi bu importer'in ciktisinda yasar (bagimlilik + yutma): girdinin bagimsiz
    // temsili (png pikselleri, fontun Standalone artifact'leri) pak'ta gereksizdir.
    public void Absorbs(string assetPath)
    {
        DependsOn(assetPath);
        if (!string.IsNullOrEmpty(assetPath) && !Absorbed.Contains(assetPath))
            Absorbed.Add(assetPath);
    }

    // Projedeki tum asset anahtarlari (klasor-uyelik gibi kurallar icin).
    public IEnumerable<string> AllAssets => App.Assets?.AllAssets.Keys ?? Array.Empty<string>();
}

public static class ImportPipeline
{
    public sealed class Entry
    {
        public string[] Extensions;
        public int Version;
        public string AssetType;
        public Type SettingsType;
        public Type Type;
        public AssetImporter Instance;
    }

    static readonly List<Entry> _entries = new();
    static readonly Dictionary<string, List<Entry>> _byExt = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> _stamping = new(StringComparer.OrdinalIgnoreCase); // dongu korumasi
    static string _artifactsRoot;
    static string _assetsRoot;

    public static IReadOnlyList<Entry> Entries => _entries;

    public static void Init(Project project)
    {
        _assetsRoot = project.AssetsPath;
        _artifactsRoot = Path.Combine(project.LibraryPath, "Artifacts");
    }

    public static string FullPath(string rel) => Path.Combine(_assetsRoot, rel);

    // RebuildCatalog'dan cagrilir: editor + oyun assembly'lerindeki importer'lar.
    // Ayni (uzanti, tip) icin iki importer = HATA, ikisi de devre disi (sessiz oncelik yok).
    public static void Rebuild(params Assembly[] assemblies)
    {
        _entries.Clear();
        _byExt.Clear();
        var disabled = new HashSet<Entry>();
        foreach (var asm in assemblies)
        {
            if (asm == null)
                continue;
            foreach (var t in asm.GetTypes())
            {
                var attr = t.GetCustomAttribute<AssetImporterAttribute>();
                if (attr == null || !typeof(AssetImporter).IsAssignableFrom(t) || t.IsAbstract)
                    continue;
                var e = new Entry
                {
                    Extensions = attr.Extensions,
                    Version = attr.Version,
                    AssetType = attr.AssetType,
                    SettingsType = attr.Settings,
                    Type = t,
                    Instance = (AssetImporter)Activator.CreateInstance(t),
                };
                _entries.Add(e);
                foreach (var ext in attr.Extensions)
                {
                    if (!_byExt.TryGetValue(ext, out var list))
                        _byExt[ext] = list = new List<Entry>();
                    foreach (var other in list)
                    {
                        if (string.Equals(other.AssetType, e.AssetType, StringComparison.Ordinal))
                        {
                            EditorLog.Error($"[import] uzanti cakismasi {ext}{(e.AssetType != null ? "(" + e.AssetType + ")" : "")}: {other.Type.Name} vs {t.Name} — ikisi de devre disi");
                            disabled.Add(other);
                            disabled.Add(e);
                        }
                    }
                    list.Add(e);
                }
            }
        }
        foreach (var list in _byExt.Values)
            list.RemoveAll(disabled.Contains);
    }

    // Asset yolu (veya ".ttf" gibi ciplak uzanti) -> importer. Tipli uzantilarda
    // (".asset") dosyanin "type:" basligi okunur; ciplak uzantiyla tipli eslesme yapilmaz.
    public static Entry ImporterFor(string relOrExt)
    {
        bool bare = relOrExt.StartsWith('.') && relOrExt.IndexOf('/') < 0 && relOrExt.IndexOf('\\') < 0
            && relOrExt.LastIndexOf('.') == 0;
        string ext = bare ? relOrExt : Path.GetExtension(relOrExt);
        if (!_byExt.TryGetValue(ext, out var list) || list.Count == 0)
            return null;
        Entry untyped = null;
        bool hasTyped = false;
        foreach (var e in list)
        {
            if (e.AssetType == null)
                untyped = e;
            else
                hasTyped = true;
        }
        if (!hasTyped || bare)
            return untyped;
        string full = Path.IsPathRooted(relOrExt) ? relOrExt : FullPath(relOrExt);
        string tn = File.Exists(full) ? ObjectSerializer.TypeNameOf(full) : null;
        if (tn != null)
            foreach (var e in list)
                if (e.AssetType == tn)
                    return e;
        return untyped;
    }

    // AssetDatabase.ArtifactResolver hedefi: "anahtar" -> main, "anahtar#ad" -> adli
    // artifact (gerekiyorsa once import). Importer'i olmayan anahtar = null
    // (cagiran kaynaga duser).
    public static byte[] ResolveArtifact(string key)
    {
        int hash = key.IndexOf('#');
        return hash < 0 ? GetArtifact(key, "main") : GetArtifact(key.Substring(0, hash), key.Substring(hash + 1));
    }

    public static byte[] GetArtifact(string rel, string name)
    {
        if (ImporterFor(rel) == null)
            return null;
        string dir = EnsureImported(rel);
        if (dir == null)
            return null;
        string p = Path.Combine(dir, name);
        return File.Exists(p) ? File.ReadAllBytes(p) : null;
    }

    // Artifact klasorundeki adlar ve kapsamlari (pak builder kapsama gore paketler).
    // Manifest (.artifacts) yoksa (eski import) hepsi Runtime sayilir.
    public static IEnumerable<(string Name, ArtifactScope Scope)> ArtifactNames(string rel)
    {
        string dir = EnsureImported(rel);
        if (dir == null)
            yield break;
        var scopes = ReadManifest(dir);
        foreach (var f in Directory.GetFiles(dir))
        {
            string n = Path.GetFileName(f);
            if (n == StampFile || n == ManifestFile)
                continue;
            yield return (n, scopes.TryGetValue(n, out var s) ? s : ArtifactScope.Runtime);
        }
    }

    const string StampFile = ".stamp";
    const string ManifestFile = ".artifacts";

    static Dictionary<string, ArtifactScope> ReadManifest(string dir)
    {
        var map = new Dictionary<string, ArtifactScope>(StringComparer.Ordinal);
        string p = Path.Combine(dir, ManifestFile);
        if (!File.Exists(p))
            return map;
        foreach (var line in File.ReadAllLines(p))
        {
            int eq = line.LastIndexOf('=');
            if (eq > 0 && Enum.TryParse(line[(eq + 1)..], out ArtifactScope s))
                map[line[..eq]] = s;
        }
        return map;
    }

    // --- Build-time (pak) girisleri: bagimlilik haritasi ve filtreli import ---

    // Importer'in bildirdigi girdiler (deps) ve yuttuklari (absorbed). Importer'siz
    // anahtar icin bos. Stamp hesabindaki ayni cagri; tek kaynak.
    public static (List<string> Deps, List<string> Absorbed) DependenciesOf(string rel)
    {
        var e = ImporterFor(rel);
        if (e == null)
            return (new List<string>(), new List<string>());
        var dctx = new DependencyContext { AssetPath = rel, SourcePath = FullPath(rel), Settings = LoadSettings(rel, e) };
        e.Instance.CollectDependencies(dctx);
        return (dctx.Deps, dctx.Absorbed);
    }

    // Projedeki tum importer'li asset'ler taranir: yutulan girdi -> tuketici anahtari.
    // Builder bu haritayla "uye erisildi => tuketici de erisildi" kenarini yazar;
    // hangi importer'in yuttugunu bilmez.
    public static Dictionary<string, string> AbsorbedByMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assets = App.Assets;
        if (assets == null)
            return map;
        foreach (var rel in assets.AllAssets.Keys)
        {
            var e = ImporterFor(rel);
            if (e == null)
                continue;
            var dctx = new DependencyContext { AssetPath = rel, SourcePath = FullPath(rel), Settings = LoadSettings(rel, e) };
            try { e.Instance.CollectDependencies(dctx); }
            catch (Exception ex) { EditorLog.Warning($"[import] bagimlilik toplanamadi {rel}: {ex.Message}"); continue; }
            foreach (var a in dctx.Absorbed)
            {
                if (map.TryGetValue(a, out var other) && other != rel)
                    EditorLog.Warning($"[import] {a} iki tuketici tarafindan yutuluyor: {other}, {rel} (ilki gecerli)");
                else
                    map[a] = rel;
            }
        }
        return map;
    }

    // Pak icin artifact'ler: importer isLive filtresiyle BELLEKTE calisir, Library'ye
    // yazmaz (editor tam ciktiyi korur). Filtre importer'in hicbir girdisini dusurmuyorsa
    // Library artifact'leri aynen doner (pahali import tekrar kosmaz). Hata = null.
    public static List<(string Name, byte[] Data, ArtifactScope Scope)> BuildArtifacts(string rel, Func<string, bool> isLive)
    {
        var e = ImporterFor(rel);
        if (e == null)
            return null;
        bool filtered = false;
        if (isLive != null)
        {
            foreach (var d in DependenciesOf(rel).Deps)
                if (!isLive(d)) { filtered = true; break; }
        }
        if (!filtered)
        {
            string dir = EnsureImported(rel);
            if (dir == null)
                return null;
            var list = new List<(string, byte[], ArtifactScope)>();
            foreach (var (name, scope) in ArtifactNames(rel))
                list.Add((name, File.ReadAllBytes(Path.Combine(dir, name)), scope));
            return list;
        }
        var ctx = RunImporter(e, rel, LoadSettings(rel, e), isLive);
        if (ctx.Error != null || ctx.Artifacts.Count == 0)
        {
            EditorLog.Error($"[import] {rel} build FAIL: {ctx.Error ?? "artifact uretilmedi"}");
            return null;
        }
        return ctx.Artifacts;
    }

    static ImportContext RunImporter(Entry e, string rel, object settings, Func<string, bool> filter)
    {
        var ctx = new ImportContext { SourcePath = FullPath(rel), AssetPath = rel, Settings = settings, Filter = filter };
        try
        {
            e.Instance.Import(ctx);
        }
        catch (Exception ex)
        {
            ctx.Error = ex.Message;
        }
        return ctx;
    }

    // --- Ayarlar (.meta "importer:" blogu) ---

    public static string MetaPath(string rel) => FullPath(rel) + ".meta";

    // Kaynagin ayar nesnesi (Settings tipi yoksa null). Meta'da blok yoksa varsayilanlar.
    public static object LoadSettings(string rel, Entry e = null)
    {
        e ??= ImporterFor(rel);
        if (e?.SettingsType == null)
            return null;
        var obj = Activator.CreateInstance(e.SettingsType);
        string meta = MetaPath(rel);
        if (File.Exists(meta))
        {
            try
            {
                var node = Yaml.Parse(File.ReadAllText(meta)).Get("importer");
                if (node?.Fields != null)
                    ObjectSerializer.FromNode(obj, node, App.Assets);
            }
            catch (Exception ex)
            {
                EditorLog.Warning($"[import] meta ayarlari okunamadi {rel}: {ex.Message}");
            }
        }
        return obj;
    }

    // Ayarlari meta'ya yazar (guid korunur) ve kaynagi yeniden import eder.
    public static void SaveSettings(string rel, object settings)
    {
        string meta = MetaPath(rel);
        DocNode root = File.Exists(meta) ? Yaml.Parse(File.ReadAllText(meta)) : DocNode.Map();
        var fresh = DocNode.Map();
        fresh.Add("guid", DocNode.Scal(root.GetScalar("guid", App.Assets?.PathToGuid(rel) ?? Guid.NewGuid().ToString("N"))));
        fresh.Add("importer", ObjectSerializer.ToNode(settings, App.Assets));
        AssetWatcher.NoteSelfWrite(meta);
        File.WriteAllText(meta, Yaml.Write(fresh));
    }

    static string SettingsText(object settings)
        => settings == null ? "" : Yaml.Write(ObjectSerializer.ToNode(settings, App.Assets));

    // --- Damga: kaynak + ayarlar + importer surumu + bagimliliklar ---

    // Herhangi bir asset'in icerik damgasi: importer'li ise artifact stamp'i (gerekirse
    // import kosar), degilse dosya SHA'si. Bagimlilik zincirleri buradan kurulur.
    public static string StampOf(string rel)
    {
        if (ImporterFor(rel) != null)
        {
            string dir = EnsureImported(rel);
            string sp = dir != null ? Path.Combine(dir, StampFile) : null;
            return sp != null && File.Exists(sp) ? File.ReadAllText(sp) : "fail";
        }
        string full = FullPath(rel);
        return File.Exists(full) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))) : "missing";
    }

    static string ComputeStamp(Entry e, string rel, string src, object settings)
    {
        byte[] srcBytes = File.ReadAllBytes(src);
        var sb = new StringBuilder();
        sb.Append(Convert.ToHexString(SHA256.HashData(srcBytes)));
        sb.Append(":v").Append(e.Version);
        string st = SettingsText(settings);
        if (st.Length > 0)
            sb.Append(":s").Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(st)))[..16]);

        var dctx = new DependencyContext { AssetPath = rel, SourcePath = src, Settings = settings };
        e.Instance.CollectDependencies(dctx);
        if (dctx.Deps.Count > 0)
        {
            dctx.Deps.Sort(StringComparer.Ordinal);
            var dep = new StringBuilder();
            foreach (var d in dctx.Deps)
            {
                if (!_stamping.Add(d))
                    continue; // dongu: kendi zincirinde olan bagimlilik atlanir
                try { dep.Append(d).Append('=').Append(StampOf(d)).Append('\n'); }
                finally { _stamping.Remove(d); }
            }
            sb.Append(":d").Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dep.ToString())))[..16]);
        }
        return sb.ToString();
    }

    // --- Import ---

    public static string EnsureImported(string rel, bool force = false) => Import(rel, force, out _);

    // Damga kapisi: eslesirse mevcut klasor doner (ran=false); degilse import kosar.
    // Donus: artifact klasoru (hata = null).
    public static string Import(string rel, bool force, out bool ran)
    {
        ran = false;
        var e = ImporterFor(rel);
        if (e == null)
            return null;
        string src = FullPath(rel);
        if (!File.Exists(src))
            return null;
        string guid = App.Assets?.PathToGuid(rel);
        if (guid == null)
            return null; // meta'siz dosya (ScanMetas henuz gormedi)
        string dir = Path.Combine(_artifactsRoot, guid);
        string stampPath = Path.Combine(dir, StampFile);

        object settings = LoadSettings(rel, e);
        _stamping.Add(rel);
        string stamp;
        try { stamp = ComputeStamp(e, rel, src, settings); }
        finally { _stamping.Remove(rel); }
        if (!force && File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
            return dir;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var ctx = RunImporter(e, rel, settings, filter: null);
        ran = true;
        if (ctx.Error != null || ctx.Artifacts.Count == 0)
        {
            EditorLog.Error($"[import] {rel} FAIL: {ctx.Error ?? "artifact uretilmedi"}");
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true); // bayat cikti kalmasin
            return null;
        }

        // Klasor sil-yaz: bayat artifact kalmaz, yarim yazim stamp'siz kalir (tekrar dener).
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var manifest = new StringBuilder();
        foreach (var (name, data, scope) in ctx.Artifacts)
        {
            File.WriteAllBytes(Path.Combine(dir, name), data);
            manifest.Append(name).Append('=').Append(scope).Append('\n');
        }
        File.WriteAllText(Path.Combine(dir, ManifestFile), manifest.ToString());
        File.WriteAllText(stampPath, stamp);
        EditorLog.Info($"[import] {rel} -> {ctx.Artifacts.Count} artifact ({e.Type.Name}, {sw.ElapsedMilliseconds}ms)");
        return dir;
    }
}
