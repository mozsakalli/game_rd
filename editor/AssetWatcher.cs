using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using DigitoyEngine;

namespace DigitoyEditor;

// Faz 1 otomatik iterasyon: Assets/ izlenir, .cs degisince oyun kodu ARKA PLANDA
// derlenir, sonuc ana thread'de frame basinda uygulanir (ALC swap). Hata = Console'a,
// eski assembly calismaya devam eder. Kurallar:
//  - Worker yalniz saf is yapar (dosya IO + dotnet build subprocess).
//  - Sahne/ALC/katalog mutasyonu YALNIZ ana thread (Tick icinde).
//  - Olay firtinasi debounce ile tek ise iner; derleme surerken gelen degisiklik
//    "pending" olur, biten isin ardindan tek yeni derleme kosar.
public static class AssetWatcher
{
    static FileSystemWatcher _fsw;
    static readonly ConcurrentQueue<string> _events = new();
    static double _lastEventAt = -1;
    static bool _codeDirty;
    static bool _rebuildPending;      // derleme surerken yeni degisiklik geldi
    static Task<GameCode.CompileJob> _building;
    static readonly System.Collections.Generic.HashSet<string> _assetDirty = new(StringComparer.OrdinalIgnoreCase);
    static readonly System.Collections.Generic.Dictionary<string, double> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    static string _assetsRoot;

    const double DebounceSeconds = 0.35;
    const double SelfWriteMaskSeconds = 2.0;

    public static string Status { get; private set; } = ""; // toolbar durum metni
    public static bool Failed { get; private set; }

    // RPC/otomasyon icin build sonucu sinyali: her biten derlemede versiyon artar.
    public static int BuildVersion { get; private set; }
    public static bool LastBuildOk { get; private set; }
    public static string LastBuildOutput { get; private set; } = "";

    // Inspector gibi tuketicilerin dis degisikligi fark etmesi icin.
    public static int AssetChangeVersion { get; private set; }
    public static string LastChangedAsset { get; private set; }

    // Editorun KENDI yazdigi dosyalar watcher'da yankilanmasin (reload dongusu/
    // yazma sirasinda text buffer sifirlanmasi olmasin).
    public static void NoteSelfWrite(string fullPath)
        => _selfWrites[System.IO.Path.GetFullPath(fullPath)] = GLFW.GetTime();

    public static void Start(string assetsPath)
    {
        _assetsRoot = System.IO.Path.GetFullPath(assetsPath);
        _fsw = new FileSystemWatcher(assetsPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
        };
        _fsw.Changed += OnFsEvent;
        _fsw.Created += OnFsEvent;
        _fsw.Deleted += OnFsEvent;
        _fsw.Renamed += (_, e) => { _events.Enqueue(e.OldFullPath); _events.Enqueue(e.FullPath); };
        _fsw.EnableRaisingEvents = true;
    }

    static void OnFsEvent(object sender, FileSystemEventArgs e) => _events.Enqueue(e.FullPath);

    // Ana thread, her frame: olaylari sinifla + biten derlemeyi uygula.
    public static void Tick(double now)
    {
        while (_events.TryDequeue(out var path))
        {
            string full;
            try { full = System.IO.Path.GetFullPath(path); }
            catch { continue; }
            if (_selfWrites.TryGetValue(full, out double at) && now - at < SelfWriteMaskSeconds)
                continue; // bizim yazdigimiz dosyanin yankisi
            if (full.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue; // sahibiyle birlikte gelir; tek basina ilginc degil
            _lastEventAt = now;
            if (full.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                if (!full.Replace('\\', '/').Contains("/Editor/"))
                    _codeDirty = true;
            }
            else
            {
                _assetDirty.Add(full);
            }
        }

        // Debounce doldu: birikenleri isle (derleme + asset refresh'leri).
        if ((_codeDirty || _assetDirty.Count > 0) && now - _lastEventAt >= DebounceSeconds)
        {
            if (_codeDirty)
            {
                _codeDirty = false;
                RequestBuild();
            }
            if (_assetDirty.Count > 0)
            {
                foreach (var full in _assetDirty)
                    ApplyAssetChange(full);
                _assetDirty.Clear();
            }
        }

        if (_building is { IsCompleted: true })
        {
            var job = _building.Result;
            _building = null;
            Apply(job);
            if (_rebuildPending)
            {
                _rebuildPending = false;
                RequestBuild();
            }
        }
    }

    // ANA THREAD: dis asset degisikligini uygun en hafif yolla yansit.
    static void ApplyAssetChange(string full)
    {
        var assets = App.Assets;
        if (assets == null || !full.StartsWith(_assetsRoot, StringComparison.OrdinalIgnoreCase))
            return;
        string rel = System.IO.Path.GetRelativePath(_assetsRoot, full).Replace('\\', '/');
        string ext = System.IO.Path.GetExtension(full).ToLowerInvariant();

        // Yeni/silinen dosyalar guid kazansin/dussun (tek dosya icin de ucuz tarama).
        if (!File.Exists(full) || assets.PathToGuid(rel) == null)
            assets.ScanMetas(createMissing: true);

        switch (ext)
        {
            case ".png" or ".jpg" or ".jpeg":
                // Yerinde piksel patch'i: sahne/play/referanslar hic bozulmaz.
                if (assets.ReloadTexture(rel))
                    EditorLog.Info($"texture patched in-place: {rel}");
                AtlasBuilder.MarkDirtyByMember(rel); // uye oldugu gruplar tazelenir
                break;
            case ".prefab":
                assets.InvalidatePrefab(rel);
                App.EditScene.ReexpandPrefabs();
                EditorLog.Info($"prefab reloaded: {rel}");
                if (PlayMode.State != PlayState.Editing && PlayMode.HotReload == HotReloadMode.Instant)
                    PlayMode.DoHotReload();
                break;
            case ".scene":
                if (App.EditScene.Path != null
                    && string.Equals(System.IO.Path.GetFullPath(App.EditScene.Path), full, StringComparison.OrdinalIgnoreCase))
                {
                    // Cakisma politikasi: kaydedilmemis duzenleme varsa BELLEK kazanir.
                    if (App.EditScene.Dirty)
                        EditorLog.Warning($"scene changed on disk but has unsaved edits, keeping in-memory: {rel} (save to overwrite)");
                    else
                    {
                        App.ReloadScene();
                        EditorLog.Info($"scene reloaded from disk: {rel}");
                    }
                }
                break;
            case ".asset":
                AssetChangeVersion++;
                LastChangedAsset = full;
                if (File.Exists(full) && AtlasBuilder.IsAtlasGroupAsset(full))
                    AtlasBuilder.MarkDirtyGroup(rel);
                EditorLog.Info($"asset changed: {rel}");
                break;
            default:
                // Importer'li kaynak (ttf vs.): artifact tazelenir, cache duser.
                if (File.Exists(full) && ImportPipeline.ImporterFor(ext) != null)
                {
                    ImportPipeline.EnsureImported(rel, force: true);
                    assets.InvalidateImported(rel);
                    AssetChangeVersion++;
                }
                break;
        }
    }

    // El ile de cagrilabilir (Project/Compile+Load Code menusu buna yonlenebilir).
    public static void RequestBuild()
    {
        if (_building != null)
        {
            _rebuildPending = true;
            return;
        }
        Status = "Compiling...";
        Failed = false;
        EditorLog.Info("code change detected, compiling...");
        var project = App.Project;
        _building = Task.Run(() => GameCode.CompileOnly(project));
    }

    // Ana thread: basari = ALC swap + katalog + sahne yeniden; hata = log, eski kod yasar.
    static void Apply(GameCode.CompileJob job)
    {
        BuildVersion++;
        LastBuildOk = job.Ok;
        LastBuildOutput = job.Ok ? "" : job.Output;
        if (!job.Ok)
        {
            Failed = true;
            int n = 0;
            foreach (var line in job.Output.Split('\n'))
            {
                var t = line.Trim();
                if (t.Contains(": error ") && n < 30)
                {
                    EditorLog.Error(t);
                    n++;
                }
            }
            if (n == 0)
                EditorLog.Error("compile failed:\n" + job.Output);
            Status = "Compile failed";
            EditorWindow.GetWindow<ConsolePanel>(); // hatalar gorunur olsun
            return;
        }
        bool wasPlaying = PlayMode.State != PlayState.Editing;
        App.ApplyCompiledGame(job);
        if (wasPlaying)
            PlayMode.Play(); // taze koda taze oyun (runtime durum zaten reload'da olur)
        Status = "";
        EditorLog.Info($"code reloaded ({job.Scripts.Count} scripts)");
    }
}
