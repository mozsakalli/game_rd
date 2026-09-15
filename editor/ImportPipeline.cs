using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// ScriptedImporter modeli: editor cekirdegi HICBIR kaynak formatini bilmez.
// [AssetImporter(".ttf")] tasiyan siniflar assembly taramasiyla bulunur
// (MenuItem/EditorTool/RpcCommand deseni — oyun assembly'si kendi importer'ini
// ekleyebilir, ALC reload guvenli). Kaynak dosya Assets/'te kalir; import
// ciktilari (artifact) Library/Artifacts/<guid>/<ad> altina yazilir. Runtime
// KAYNAGI degil ARTIFACT'i yukler (AssetDatabase.ArtifactResolver editorde
// buraya baglanir; release'te pak zaten artifact'i tasir).
[AttributeUsage(AttributeTargets.Class)]
public sealed class AssetImporterAttribute : Attribute
{
    public readonly string[] Extensions;
    public int Version = 1; // artir = tum bu tur asset'ler otomatik reimport

    public AssetImporterAttribute(params string[] extensions) => Extensions = extensions;
}

public abstract class AssetImporter
{
    public abstract void Import(ImportContext ctx);
}

public sealed class ImportContext
{
    public string SourcePath;  // tam yol
    public string AssetPath;   // Assets'e goreli (asset anahtari)
    internal readonly List<(string Name, byte[] Data)> Artifacts = new();
    internal string Error;

    // "main" = kaynak anahtariyla yuklenen birincil cikti (pak bunu paketler).
    public void AddArtifact(string name, byte[] data) => Artifacts.Add((name, data));

    public void Fail(string message) => Error = message;
}

static class ImportPipeline
{
    public sealed class Entry
    {
        public string[] Extensions;
        public int Version;
        public Type Type;
        public AssetImporter Instance;
    }

    static readonly List<Entry> _entries = new();
    static readonly Dictionary<string, Entry> _byExt = new(StringComparer.OrdinalIgnoreCase);
    static string _artifactsRoot;
    static string _assetsRoot;

    public static IReadOnlyList<Entry> Entries => _entries;

    public static void Init(Project project)
    {
        _assetsRoot = project.AssetsPath;
        _artifactsRoot = Path.Combine(project.LibraryPath, "Artifacts");
    }

    // RebuildCatalog'dan cagrilir: editor + oyun assembly'lerindeki importer'lar.
    // Ayni uzantiya iki importer = HATA, ikisi de devre disi (sessiz oncelik yok).
    public static void Rebuild(params Assembly[] assemblies)
    {
        _entries.Clear();
        _byExt.Clear();
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
                    Type = t,
                    Instance = (AssetImporter)Activator.CreateInstance(t),
                };
                _entries.Add(e);
                foreach (var ext in attr.Extensions)
                {
                    if (_byExt.TryGetValue(ext, out var other))
                    {
                        EditorLog.Error($"[import] uzanti cakismasi {ext}: {other.Type.Name} vs {t.Name} — ikisi de devre disi");
                        _byExt.Remove(ext);
                        continue;
                    }
                    _byExt[ext] = e;
                }
            }
        }
    }

    public static Entry ImporterFor(string relOrExt)
        => _byExt.GetValueOrDefault(relOrExt.StartsWith('.') ? relOrExt : Path.GetExtension(relOrExt));

    // AssetDatabase.ArtifactResolver hedefi: kaynak anahtari -> "main" artifact
    // baytlari (gerekiyorsa once import). Importer'i olmayan anahtar = null
    // (cagiran kaynaga duser).
    public static byte[] ResolveMainArtifact(string rel) => GetArtifact(rel, "main");

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

    // Hash kapisi: SHA256(kaynak baytlari) + importer Version. Eslesirse mevcut
    // klasor doner; degilse import kosar. Donus: artifact klasoru (hata = null).
    public static string EnsureImported(string rel, bool force = false)
    {
        var e = ImporterFor(rel);
        if (e == null)
            return null;
        string src = Path.Combine(_assetsRoot, rel);
        if (!File.Exists(src))
            return null;
        string guid = App.Assets?.PathToGuid(rel);
        if (guid == null)
            return null; // meta'siz dosya (ScanMetas henuz gormedi)
        string dir = Path.Combine(_artifactsRoot, guid);
        string stampPath = Path.Combine(dir, ".stamp");

        byte[] srcBytes = File.ReadAllBytes(src);
        string stamp = Convert.ToHexString(SHA256.HashData(srcBytes)) + ":v" + e.Version;
        if (!force && File.Exists(stampPath) && File.ReadAllText(stampPath) == stamp)
            return dir;

        var ctx = new ImportContext { SourcePath = src, AssetPath = rel };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            e.Instance.Import(ctx);
        }
        catch (Exception ex)
        {
            ctx.Error = ex.Message;
        }
        if (ctx.Error != null || ctx.Artifacts.Count == 0)
        {
            EditorLog.Error($"[import] {rel} FAIL: {ctx.Error ?? "artifact uretilmedi"}");
            return null;
        }

        // Klasor sil-yaz: bayat artifact kalmaz, yarim yazim stamp'siz kalir (tekrar dener).
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        foreach (var (name, data) in ctx.Artifacts)
            File.WriteAllBytes(Path.Combine(dir, name), data);
        File.WriteAllText(stampPath, stamp);
        EditorLog.Info($"[import] {rel} -> {ctx.Artifacts.Count} artifact ({e.Type.Name}, {sw.ElapsedMilliseconds}ms)");
        return dir;
    }
}
