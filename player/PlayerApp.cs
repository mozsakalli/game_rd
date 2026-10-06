using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using DigitoyEngine;

namespace DigitoyPlayer;

// Standalone runtime host (Windows minimal). Editorun aksine GUI/dock/panel yok:
// pencere = oyun ekrani. Katalog reflection TARAMASI yok — editorun urettigi
// Digitoy.Registry.dll (CatalogWriter ciktisi) RegisterAll ile kurulur; player
// ve editor ayni kayit kodunu kosar. Asset kaynagi: Build/game.pak varsa pak
// (release yolu), yoksa loose Assets/ + Library/Artifacts fallback'i (dev
// konforu: editorle bir kez acilmis proje pak'siz da kosar).
public class PlayerApp
{
    // Frame'ler arasi yasayan durum STATIC'te (GC koku). Safepoint modeli: toplama yalniz
    // host dongusunde, Frame() dondukten sonra (managed frame yokken) kosar; C stack'teki
    // managed local'ler kok DEGILDIR -> dongu managed kodda tutulamaz, host'ta tutulur.
    static AssetSource _source;
    static IntPtr _window;
    static CommandBuffer _cb;
    static System.Collections.Generic.List<Camera> _gameCams;
    static double _lastT;
    static bool _mouseWasDown;

    // .NET host: ayni Init/Frame/Shutdown sozlesmesi. AOT'de C main dogrudan Init/Frame/Shutdown
    // cagirir (Main kullanilmaz; OutputType=Exe icin yine de gerekli).
    public static void Main(string[] args)
    {
#if !DE_AOT
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string text = $"[crash] {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{e.ExceptionObject}\n";
            Console.Error.WriteLine(text);
            try { File.AppendAllText("player_crash.log", text); }
            catch { }
        };
        Init(args.Length > 0 ? args[0] : DefaultProjectPath);
        while (Frame()) { }
        Shutdown();
#endif
    }

    // Pencere + sokol + async boot baslatir. Bloklamaz.
    public static void Init(string root)
    {
        if (string.IsNullOrEmpty(root))
            root = DefaultProjectPath;
        AssetDatabase.LogWarning = m => Console.WriteLine("[warn] " + m);

        // --- pencere + sokol ONCE: yukleme boyunca dongu doner (preload ekrani cizilebilir) ---
        GLFW.Init();
#if DE_RENDERER_METAL
        GLFW.WindowHint(GLFWConst.CLIENT_API, GLFWConst.NO_API);
        GLFW.WindowHint(GLFWConst.SCALE_TO_MONITOR, GLFWConst.TRUE);
#else
        GLFW.WindowHint(GLFWConst.CONTEXT_VERSION_MAJOR, 4);
        GLFW.WindowHint(GLFWConst.CONTEXT_VERSION_MINOR, 1);
        GLFW.WindowHint(GLFWConst.OPENGL_PROFILE, GLFWConst.OPENGL_CORE_PROFILE);
        GLFW.WindowHint(GLFWConst.OPENGL_FORWARD_COMPAT, GLFWConst.TRUE);
        GLFW.WindowHint(GLFWConst.SCALE_TO_MONITOR, GLFWConst.TRUE);
#endif
        _window = GLFW.CreateWindow(1280, 720, "Loading...", IntPtr.Zero, IntPtr.Zero);
#if DE_RENDERER_METAL
        Sokol.MetalInitWindow(_window);
#else
        GLFW.MakeContextCurrent(_window);
        GLFW.SwapInterval(1);
#endif
        Sokol.Setup();

        _cb = new CommandBuffer();
        _gameCams = new System.Collections.Generic.List<Camera>
            { new Camera { Order = 0, BackgroundColor = new Color(0, 0, 0, 255) } };

        // --- async acilis: pak -> proje -> katalog -> sahne (bagimlilik grafigi) ---
        // Hicbir adim bloklamaz; her Frame job'lari pompalar, continuation'lar ana
        // thread'de kosar. Hata: Task'ta kalir, Frame her kare gozler ve raporlayip firlatir.
        _bootTask = Boot(root, _window);
    }

    // Bir frame: giris, pompa, simulasyon, cizim. false = pencere kapandi.
    public static bool Frame()
    {
        if (GLFW.WindowShouldClose(_window) != GLFWConst.FALSE)
            return false;
        GLFW.PollEvents();
        GLFW.GetFramebufferSize(_window, out int fbw, out int fbh);
        if (fbw <= 0 || fbh <= 0)
            return true; // minimize

        GLFW.GetWindowContentScale(_window, out float uiScale, out _);
        if (uiScale <= 0) uiScale = 1f;
        float lw = fbw / uiScale, lh = fbh / uiScale;
        // Pencere-uzayi -> mantiksal bolen (macOS'ta pencere zaten point'tir).
        GLFW.GetWindowSize(_window, out int winW, out _);
        float mouseScale = winW > 0 ? winW / lw : uiScale;

        double t = GLFW.GetTime();
        float dt = _lastT > 0 ? (float)(t - _lastT) : 0f;
        _lastT = t;

        if (_assets != null)
            _assets.Tick(); // job pompasi + biten texture'lari butceli bagla
        else
            AsyncJobs.Pump();
        // Async Boot'un hatasi Task'ta kalir (state machine yakalar); burada gozlenir: orijinal
        // firlatma yeri + trace raporlanir, sonra yeniden firlatilir (sessiz kalma yok).
        if (_bootTask != null && _bootTask.IsFaulted)
        {
            var task = _bootTask;
            _bootTask = null;
            try { task.GetAwaiter().GetResult(); }
            catch (Exception e)
            {
                Console.WriteLine("[player] BOOT HATASI: " + e.GetType().Name + ": " + e.Message);
                Console.WriteLine(e.StackTrace);
                throw;
            }
        }
        var scene = Scene.Active;
        Scene.UpdateAll(dt, lw, lh, simulate: true);

        // Pointer: pencere = oyun ciktisi, mantiksal px birebir.
        GLFW.GetCursorPos(_window, out double mx, out double my);
        float px = (float)mx / mouseScale, py = (float)my / mouseScale;
        bool mouseDown = GLFW.GetMouseButton(_window, 0) == GLFWConst.PRESS;
        var ptr = scene.Pointer;
        if (mouseDown && !_mouseWasDown) ptr.Down(px, py);
        else if (mouseDown) ptr.Move(px, py);
        else if (_mouseWasDown) ptr.Up(px, py);
        _mouseWasDown = mouseDown;

        // Sahne kameralari swapchain'e (Target=null); kamera yoksa DriveCameras
        // pool[0] piksel-ortho fallback'ini kendisi kurar (>=1 doner).
        int camCount = scene.DriveCameras(_gameCams, lw, lh);

        _cb.Begin();
        for (int i = 0; i < camCount; i++)
            _gameCams[i].Encode(_cb, fbw, fbh);
        _cb.Submit();
        Sokol.Commit();
#if !DE_RENDERER_METAL
        GLFW.SwapBuffers(_window);
#endif
        return true;
    }

    public static void Shutdown()
    {
        Audio.Shutdown();
        Sokol.Shutdown();
        GLFW.Terminate();
    }

    static AssetDatabase _assets;
    static Task<bool> _bootTask;

    static async Task<bool> Boot(string root, IntPtr window)
    {
        {
            string name = "Game", startScene = "Scenes/Main.scene";
            string pak = root + "/Build/game.pak";
            var pakSource = await PakSource.OpenAsync(pak);
            AssetDatabase assets;
            if (pakSource != null)
            {
                _source = pakSource;
                assets = new AssetDatabase(_source); // guid tablosu pak'tan, ScanMetas yok
                Console.WriteLine("[player] pak: " + pak);
                // Proje ayarlari pak icinde pismis (YAML release'e girmez).
                if (!ProjectBinary.TryRead(await _source.ReadBytesAsync(ProjectBinary.PakKey), out name, out startScene))
                    throw new Exception("pak'ta proje kaydi yok (" + ProjectBinary.PakKey + ") — pak'i yeniden build edin");
            }
            else
            {
#if DE_EDITOR
                // Dev konforu: pak yoksa loose Assets/ + Library/Artifacts (YAML sahne, senkron).
                // Proje kimligi standart PlayerSettings.asset'ten; eski project.yaml fallback.
                string playerSettings = Path.Combine(root, "ProjectSettings", "PlayerSettings.asset");
                string settings = Path.Combine(root, "ProjectSettings", "project.yaml");
                if (File.Exists(playerSettings))
                {
                    var ps = ObjectSerializer.Load<PlayerSettings>(playerSettings);
                    name = ps.productName;
                    startScene = ps.startScene;
                }
                else if (File.Exists(settings))
                {
                    var pdoc = Yaml.Parse(File.ReadAllText(settings));
                    name = pdoc.GetScalar("name", name);
                    startScene = pdoc.GetScalar("startScene", startScene);
                }
                string assetsPath = Path.Combine(root, "Assets");
                _source = new LooseFileSource(assetsPath);
                assets = new AssetDatabase(assetsPath);
                assets.ScanMetas(createMissing: false);
                string artifacts = Path.Combine(root, "Library", "Artifacts");
                assets.ArtifactResolver = key =>
                {
                    int hash = key.IndexOf('#');
                    string rel = hash < 0 ? key : key.Substring(0, hash);
                    string art = hash < 0 ? "main" : key.Substring(hash + 1);
                    string guid = assets.PathToGuid(rel);
                    if (guid == null)
                        return null;
                    string p = Path.Combine(artifacts, guid, art);
                    return File.Exists(p) ? File.ReadAllBytes(p) : null;
                };
                Console.WriteLine("[player] loose: " + assetsPath + " (pak yok)");
#else
                throw new Exception("game.pak acilamadi: " + pak);
#endif
            }

            GLFW.SetWindowTitle(window, name);
            var catalog = LoadCatalog(root);
            Audio.Source = _source;
            Scene.Active.Catalog = catalog;
            _assets = assets;

            if (pakSource != null)
            {
                // Release: bagimlilik grafigi (atlas/font/ses/texture/prefab) hazir olunca Spawn.
                var op = SceneLoader.LoadAsync(startScene, assets, catalog);
                int lastDone = -1;
                while (!op.IsDone)
                {
                    if (op.Progress.Done != lastDone)
                    {
                        lastDone = op.Progress.Done;
                        Console.WriteLine($"[player] yukleniyor {op.Progress.Done}/{op.Progress.Total}");
                    }
                    await DigitoyEngine.Frame.Next();
                }
                if (op.Failed)
                    throw new Exception(op.Error);
                Console.WriteLine("[player] sahne hazir: " + startScene);
            }
#if DE_EDITOR
            else
            {
                assets.LoadAtlases();
                var sceneBytes = _source.ReadBytes(startScene);
                if (sceneBytes == null)
                    throw new Exception("startScene bulunamadi: " + startScene);
                var doc = SceneDoc.Parse(System.Text.Encoding.UTF8.GetString(sceneBytes));
                doc.ExpandPrefabs(catalog, assets);
                doc.Spawn(null, catalog, assets);
            }
#endif
            return true;
        }

    }

#if DE_AOT
    // Registry + oyun kodu bu assembly'de derlenmis: reflection/yukleme yok.
    static TypeCatalog LoadCatalog(string root)
    {
        var cat = new TypeCatalog();
        DigitoyEngine.Generated.Registry.RegisterAll(cat);
        Console.WriteLine("[player] katalog: gomulu registry (AOT)");
        return cat;
    }

    // Exe Build/ icinde game.pak'in yaninda durur; proje koku bir ust klasor.
    static string DefaultProjectPath => "..";
#else
    // Game.dll (varsa) + Digitoy.Registry.dll Library/Build'den yuklenir; katalog
    // uretilmis RegisterAll'dan kurulur. Registry yoksa acik hata: once editorle ac.
    static TypeCatalog LoadCatalog(string root)
    {
        string buildDir = Path.Combine(root, "Library", "Build");
        string gameDll = null;
        foreach (var p in Directory.Exists(Path.Combine(buildDir, "bin"))
            ? Directory.GetFiles(Path.Combine(buildDir, "bin"), "*.Game.dll") : Array.Empty<string>())
            gameDll = p;
        if (gameDll != null)
            Assembly.LoadFrom(gameDll); // registry referansi adla cozulur (ayni LoadFrom baglami)

        string regDll = Path.Combine(buildDir, "Digitoy.Registry.dll");
        if (!File.Exists(regDll))
            throw new Exception("Digitoy.Registry.dll yok — projeyi once editorle acin (registry uretimi): " + regDll);
        var regAsm = Assembly.LoadFrom(regDll);
        var cat = new TypeCatalog();
        regAsm.GetType("DigitoyEngine.Generated.Registry")
              .GetMethod("RegisterAll").Invoke(null, new object[] { cat });
        Console.WriteLine("[player] katalog: uretilmis registry" + (gameDll != null ? " + " + Path.GetFileName(gameDll) : ""));
        return cat;
    }

    // bin/Debug/netX.Y -> repo koku (editorle ayni gelistirme konforu).
    static string DefaultProjectPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Projects", "Sandbox"));
#endif
}
