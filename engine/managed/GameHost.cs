using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DigitoyEngine;

// Platformsuz oyun host'u (docs/platform-hosts.md). Pencere/surface/dongu/girdi/yasam dongusu
// PLATFORM HOST'undadir (c_runtime/host_desktop.c, Android GameActivity, iOS GameViewController,
// wasm host.js); bu sinif yalniz oyunu surer: pak -> katalog -> sahne, UpdateAll, pointer,
// kameralar, Commit, Module.TickAll. GLFW/pencere bilgisi YOK; zaman dt olarak gelir, girdi
// OLAY olarak gelir (poll yok), swap/drawable host'un isi.
//
// Thread sozlesmesi: tum metotlar TEK thread'den (render thread) cagrilir. AOT'de giris
// yalniz generated.c'deki de_managed_* kopruleri uzerinden (de_app.c) -> GC safepoint
// Frame dondukten sonra, managed frame yokken.
//
// Frame'ler arasi yasayan durum STATIC'te (GC koku): C stack'teki managed local'ler kok DEGILDIR.
public static class GameHost
{
    // --- olay tipleri (de_app.h DeEventType ile birebir) ---
    public const int EvPointerDown = 1, EvPointerMove = 2, EvPointerUp = 3, EvPointerCancel = 4;
    public const int EvKeyDown = 10, EvKeyUp = 11, EvText = 12;
    public const int EvBack = 20, EvFocus = 21, EvResize = 22;

    static AssetSource _source;
    static AssetDatabase _assets;
    static Task<bool> _bootTask;
    static CommandBuffer _cb;
    static System.Collections.Generic.List<Camera> _gameCams;
    static string _title;
    static bool _paused;

    // Host servisleri (AOT: host C sembolleri; .NET dev host: delegeler). Yoksa sessiz.
    public static Action<string> SetTitle;
    // .NET dev host katalogu kendisi yukler (Assembly.LoadFrom); AOT'de de_game_register.
    public static Func<TypeCatalog> CatalogProvider;
    // Geri tusu (Android/wasm): true = oyun isledi; null/false = varsayilan (uygulamadan cik).
    public static Func<bool> BackRequested;
    static bool _quit;

    public static string Title => _title;
    public static bool IsBooted => _assets != null && _bootTask == null;

    // Runtime hazir, grafik context'i bu thread'de aktif. Bloklamaz: Boot async kosar, Frame pompalar.
    public static void Init(string root, int fbw, int fbh, float scale)
    {
        if (string.IsNullOrEmpty(root))
            root = "..";  // exe Build/ icinde game.pak'in yaninda; proje koku bir ust klasor
        AssetDatabase.LogWarning = m => Console.WriteLine("[warn] " + m);
        Sokol.Setup();
        _cb = new CommandBuffer();
        _gameCams = new System.Collections.Generic.List<Camera>
            { new Camera { Order = 0, BackgroundColor = new Color(0, 0, 0, 255) } };
        _bootTask = Boot(root);
    }

    // Host olay kuyrugunu Frame'den ONCE bosaltirken cagirir. Simdilik tek pointer (id yok sayilir).
    public static void Event(int type, int id, float x, float y, int a, int b)
    {
        var ptr = Scene.Active.Pointer;
        switch (type)
        {
            case EvPointerDown: ptr.Down(x, y); break;
            case EvPointerMove: ptr.Move(x, y); break;
            case EvPointerUp: ptr.Up(x, y); break;
            case EvPointerCancel: ptr.Up(x, y); break;
            case EvBack:
                if (BackRequested == null || !BackRequested())
                    _quit = true;
                break;
        }
    }

    // Bir frame: pompa, simulasyon, cizim. false = oyun cikmak istiyor.
    public static bool Frame(float dt, int fbw, int fbh, float scale)
    {
        if (_quit)
            return false;
        if (fbw <= 0 || fbh <= 0)
            return true; // minimize / surface yok
        if (scale <= 0) scale = 1f;
        float lw = fbw / scale, lh = fbh / scale;

        if (_assets != null)
            _assets.Tick(); // job pompasi + biten texture'lari butceli bagla
        else
            AsyncJobs.Pump();
        Module.TickAll();
        // Async Boot'un hatasi Task'ta kalir; burada gozlenir ve yeniden firlatilir (sessiz kalma yok).
        if (_bootTask != null && _bootTask.IsFaulted)
        {
            var task = _bootTask;
            _bootTask = null;
            try { task.GetAwaiter().GetResult(); }
            catch (Exception e)
            {
                Console.WriteLine("[host] BOOT HATASI: " + e.GetType().Name + ": " + e.Message);
                Console.WriteLine(e.StackTrace);
                throw;
            }
        }
        if (_bootTask != null && _bootTask.IsCompleted)
            _bootTask = null;

        var scene = Scene.Active;
        Scene.UpdateAll(_paused ? 0f : dt, lw, lh, simulate: true);

        int camCount = scene.DriveCameras(_gameCams, lw, lh);
        _cb.Begin();
        for (int i = 0; i < camCount; i++)
            _gameCams[i].Encode(_cb, fbw, fbh);
        _cb.Submit();
        Sokol.Commit();
        return true;
    }

    public static void Pause() { _paused = true; Audio.Suspend(); }
    public static void Resume() { _paused = false; Audio.Resume(); }

    public static void Shutdown()
    {
        Audio.Shutdown();
        Sokol.Shutdown();
    }

    static async Task<bool> Boot(string root)
    {
        string pak = root + "/Build/game.pak";
        var pakSource = await PakSource.OpenAsync(pak);
        if (pakSource == null)
            throw new Exception("game.pak acilamadi: " + pak);
        _source = pakSource;
        var assets = new AssetDatabase(_source); // guid tablosu pak'tan, ScanMetas yok
        Console.WriteLine("[host] pak: " + pak);
        if (!ProjectBinary.TryRead(await _source.ReadBytesAsync(ProjectBinary.PakKey), out string name, out string startScene))
            throw new Exception("pak'ta proje kaydi yok (" + ProjectBinary.PakKey + ") — pak'i yeniden build edin");
        _title = name;
        SetTitle?.Invoke(name);

        // Test kancasi (docs/registry-removal.md Faz 0): Build/scene-dump.flag varsa sahnenin alan dokumunu
        // Awake'ten ONCE (saf deserialize durumu) stdout'a bas ve cik. Editor RPC scene.dumpFields(edit) ile karsilastirilir.
        var dumpFlag = await AsyncJobs.Await(NativeFs.Open(root + "/Build/scene-dump.flag"));
        bool dump = dumpFlag.Ok;
        if (dump)
        {
            NativeFs.Close(dumpFlag.W);
            SceneBinary.BeforeActivate = () =>
            {
                Console.WriteLine("[scene-dump-begin]");
                Console.Write(SceneDump.Write(Scene.Active, Scene.Active.Catalog));
                Console.WriteLine("[scene-dump-end]");
            };
        }
        await BootWith(assets, startScene);
        if (dump)
        {
            SceneBinary.BeforeActivate = null;
            _quit = true;
            return true;
        }

        // Dev/test kancasi (docs/modules.md): Build/autoload.module.pak varsa dinamik modul olarak yukle.
        string autoload = root + "/Build/autoload.module.pak";
        var probe = await PakSource.OpenAsync(autoload);
        if (probe != null)
        {
            var mod = await Module.LoadAsync(autoload);
            Console.WriteLine(mod.IsLoaded ? "[host] autoload modul yuklendi: " + mod.Name : "[host] autoload modul HATA: " + mod.Error);
        }
        return true;
    }

    // Katalog + sahne (bagimlilik grafigi hazir olunca Spawn).
    static async Task BootWith(AssetDatabase assets, string startScene)
    {
        var catalog = LoadCatalog();
        Audio.Source = _source;
        Scene.Active.Catalog = catalog;
        _assets = assets;
        var op = SceneLoader.LoadAsync(startScene, assets, catalog);
        int lastDone = -1;
        while (!op.IsDone)
        {
            if (op.Progress.Done != lastDone)
            {
                lastDone = op.Progress.Done;
                Console.WriteLine($"[host] yukleniyor {op.Progress.Done}/{op.Progress.Total}");
            }
            await DigitoyEngine.Frame.Next();
        }
        if (op.Failed)
            throw new Exception(op.Error);
        Console.WriteLine("[host] sahne hazir: " + startScene);
    }

    // Dev host (loose Assets/, pak yok; yalniz DE_EDITOR): YAML sahne senkron spawn.
#if DE_EDITOR
    public static void InitLoose(AssetSource source, string title, AssetDatabase assets, string startScene, int fbw, int fbh, float scale)
    {
        AssetDatabase.LogWarning = m => Console.WriteLine("[warn] " + m);
        Sokol.Setup();
        _cb = new CommandBuffer();
        _gameCams = new System.Collections.Generic.List<Camera>
            { new Camera { Order = 0, BackgroundColor = new Color(0, 0, 0, 255) } };
        _source = source;
        _title = title;
        SetTitle?.Invoke(title);
        var catalog = LoadCatalog();
        Audio.Source = _source;
        Scene.Active.Catalog = catalog;
        _assets = assets;
        assets.LoadAtlases();
        var sceneBytes = _source.ReadBytes(startScene);
        if (sceneBytes == null)
            throw new Exception("startScene bulunamadi: " + startScene);
        var doc = SceneDoc.Parse(System.Text.Encoding.UTF8.GetString(sceneBytes));
        doc.ExpandPrefabs(catalog, assets);
        doc.Spawn(null, catalog, assets);
    }
#endif

#if DE_AOT
    // Platform host saglar (de_app.h).
    [DllImport("__Internal", EntryPoint = "de_host_set_title")]
    static extern void de_host_set_title(string utf8);

    static GameHost() { SetTitle = t => de_host_set_title(t); }

    // Katalog runtime reflection'dan (Reflect.ComponentTypes -> Type.GetSubtypes; docs/registry-removal.md).
    static TypeCatalog LoadCatalog()
    {
        var cat = TypeCatalog.FromAssemblies();
        Console.WriteLine("[host] katalog: reflection (AOT)");
        return cat;
    }
#else
    static TypeCatalog LoadCatalog()
    {
        if (CatalogProvider == null)
            throw new Exception("GameHost.CatalogProvider yok (dev host katalogu saglamali)");
        return CatalogProvider();
    }
#endif
}
