using System;
using System.IO;
using System.Reflection;
using DigitoyEngine;

namespace DigitoyPlayer;

// .NET DEV HOST (docs/platform-hosts.md): editorsuz hizli test. GLFW pencere acar, olaylari
// GameHost.Event'e verir, GameHost.Frame'i surer — AOT'deki host_desktop.c'nin managed esi.
// Oyun mantigi burada DEGIL, DigitoyEngine.GameHost'ta. Release/AOT yolunda bu dosya derlenmez
// (AOT host = c_runtime/host_desktop.c + de_app.c; Registry de_game_register ile baglanir).
//
// Asset kaynagi: Build/game.pak varsa pak (release yolu), yoksa loose Assets/ + Library/Artifacts
// fallback'i (dev konforu: editorle bir kez acilmis proje pak'siz da kosar; DE_EDITOR gerekir).
public static class PlayerApp
{
    static IntPtr _window;
    static bool _mouseDown;
    static float _mouseScale = 1f;

    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string text = $"[crash] {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{e.ExceptionObject}\n";
            Console.Error.WriteLine(text);
            try { File.AppendAllText("player_crash.log", text); }
            catch { }
        };
        string root = args.Length > 0 ? args[0] : DefaultProjectPath;

        GLFW.Init();
#if DE_RENDERER_METAL
        GLFW.WindowHint(GLFWConst.CLIENT_API, GLFWConst.NO_API);
#else
        GLFW.WindowHint(GLFWConst.CONTEXT_VERSION_MAJOR, 4);
        GLFW.WindowHint(GLFWConst.CONTEXT_VERSION_MINOR, 1);
        GLFW.WindowHint(GLFWConst.OPENGL_PROFILE, GLFWConst.OPENGL_CORE_PROFILE);
        GLFW.WindowHint(GLFWConst.OPENGL_FORWARD_COMPAT, GLFWConst.TRUE);
#endif
        GLFW.WindowHint(GLFWConst.SCALE_TO_MONITOR, GLFWConst.TRUE);
        _window = GLFW.CreateWindow(1280, 720, "Loading...", IntPtr.Zero, IntPtr.Zero);
#if DE_RENDERER_METAL
        Sokol.MetalInitWindow(_window);
#else
        GLFW.MakeContextCurrent(_window);
        GLFW.SwapInterval(1);
#endif
        GameHost.SetTitle = t => GLFW.SetWindowTitle(_window, t);
        GameHost.CatalogProvider = () => LoadCatalog(root);

        Measure(out int fbw, out int fbh, out float scale);
        if (File.Exists(Path.Combine(root, "Build", "game.pak")))
            GameHost.Init(root, fbw, fbh, scale);
        else
            InitLoose(root, fbw, fbh, scale);

        double last = 0;
        while (GLFW.WindowShouldClose(_window) == GLFWConst.FALSE)
        {
            GLFW.PollEvents();
            Measure(out fbw, out fbh, out scale);
            PollPointer();
            double t = GLFW.GetTime();
            float dt = last > 0 ? (float)(t - last) : 0f;
            last = t;
            if (!GameHost.Frame(dt, fbw, fbh, scale)) break;
#if !DE_RENDERER_METAL
            if (fbw > 0 && fbh > 0) GLFW.SwapBuffers(_window);
#endif
        }
        GameHost.Shutdown();
        GLFW.Terminate();
    }

    static void Measure(out int fbw, out int fbh, out float scale)
    {
        GLFW.GetFramebufferSize(_window, out fbw, out fbh);
        GLFW.GetWindowContentScale(_window, out scale, out _);
        if (scale <= 0) scale = 1f;
        GLFW.GetWindowSize(_window, out int winW, out _);
        float lw = fbw / scale;
        _mouseScale = winW > 0 && lw > 0 ? winW / lw : scale;
    }

    // GLFW callback'i yerine poll (dev host; marshal'siz). Mantiksal px'e cevrilip olay olarak verilir.
    static void PollPointer()
    {
        GLFW.GetCursorPos(_window, out double mx, out double my);
        float px = (float)mx / _mouseScale, py = (float)my / _mouseScale;
        bool down = GLFW.GetMouseButton(_window, 0) == GLFWConst.PRESS;
        if (down && !_mouseDown) GameHost.Event(GameHost.EvPointerDown, 0, px, py, 0, 0);
        else if (down) GameHost.Event(GameHost.EvPointerMove, 0, px, py, 0, 0);
        else if (_mouseDown) GameHost.Event(GameHost.EvPointerUp, 0, px, py, 0, 0);
        _mouseDown = down;
    }

    // Pak yok: loose Assets/ + Library/Artifacts (YAML sahne). Proje kimligi PlayerSettings.asset'ten.
    static void InitLoose(string root, int fbw, int fbh, float scale)
    {
#if DE_EDITOR
        string name = "Game", startScene = "Scenes/Main.scene";
        string playerSettings = Path.Combine(root, "ProjectSettings", "PlayerSettings.asset");
        if (File.Exists(playerSettings))
        {
            var ps = ObjectSerializer.Load<PlayerSettings>(playerSettings);
            name = ps.productName;
            startScene = ps.startScene;
        }
        string assetsPath = Path.Combine(root, "Assets");
        var assets = new AssetDatabase(assetsPath);
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
        GameHost.InitLoose(new LooseFileSource(assetsPath), name, assets, startScene, fbw, fbh, scale);
#else
        throw new Exception("game.pak yok: " + Path.Combine(root, "Build", "game.pak") + " (loose Assets yolu yalniz Debug/DE_EDITOR)");
#endif
    }

    // Game.dll (varsa) Library/Build/bin'den yuklenir; katalog reflection'la kurulur
    // (docs/registry-removal.md: editorle AYNI yol). Game.dll yoksa yalniz engine tipleri.
    static TypeCatalog LoadCatalog(string root)
    {
        string buildDir = Path.Combine(root, "Library", "Build");
        string gameDll = null;
        foreach (var p in Directory.Exists(Path.Combine(buildDir, "bin"))
            ? Directory.GetFiles(Path.Combine(buildDir, "bin"), "*.Game.dll") : Array.Empty<string>())
            gameDll = p;
        var cat = gameDll != null
            ? TypeCatalog.FromAssemblies(typeof(GameObject).Assembly, Assembly.LoadFrom(gameDll))
            : TypeCatalog.FromAssemblies(typeof(GameObject).Assembly);
        Console.WriteLine("[player] katalog: reflection" + (gameDll != null ? " + " + Path.GetFileName(gameDll) : " (yalniz engine)"));
        return cat;
    }

    // bin/Debug/netX.Y -> repo koku (editorle ayni gelistirme konforu).
    static string DefaultProjectPath => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Projects", "Sandbox"));
}
