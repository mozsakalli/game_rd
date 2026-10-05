using System;
using System.IO;
using System.Reflection;
using DigitoyEngine;

namespace DigitoyPlayer;

// Standalone runtime host (Windows minimal). Editorun aksine GUI/dock/panel yok:
// pencere = oyun ekrani. Katalog reflection TARAMASI yok — editorun urettigi
// Digitoy.Registry.dll (CatalogWriter ciktisi) RegisterAll ile kurulur; player
// ve editor ayni kayit kodunu kosar. Asset kaynagi: Build/game.pak varsa pak
// (release yolu), yoksa loose Assets/ + Library/Artifacts fallback'i (dev
// konforu: editorle bir kez acilmis proje pak'siz da kosar).
public unsafe class PlayerApp
{
    static AssetSource _source;

#if DE_AOT
    // AOT giris: arguman yok; proje koku exe'nin yanindaki Build/ klasorunun ustu.
    public static void Main() => Run(Array.Empty<string>());
#else
    public static void Main(string[] args) => Run(args);
#endif

    static void Run(string[] args)
    {
#if !DE_AOT
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string text = $"[crash] {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{e.ExceptionObject}\n";
            Console.Error.WriteLine(text);
            try { File.AppendAllText("player_crash.log", text); }
            catch { }
        };
#endif

        string root = Path.GetFullPath(args.Length > 0 ? args[0] : DefaultProjectPath);
        AssetDatabase.LogWarning = m => Console.WriteLine("[warn] " + m);
        string name = "Game", startScene = "Scenes/Main.scene";

        // --- asset kaynagi ---
        string pak = Path.Combine(root, "Build", "game.pak");
        AssetDatabase assets;
        if (File.Exists(pak))
        {
            _source = new PakSource(pak);
            assets = new AssetDatabase(_source); // guid tablosu pak'tan, ScanMetas yok
            Console.WriteLine("[player] pak: " + pak);
            // Proje ayarlari pak icinde pismis (YAML release'e girmez).
            if (!ProjectBinary.TryRead(_source.ReadBytes(ProjectBinary.PakKey), out name, out startScene))
                throw new Exception("pak'ta proje kaydi yok (" + ProjectBinary.PakKey + ") — pak'i yeniden build edin");
        }
        else
        {
            string settings = Path.Combine(root, "ProjectSettings", "project.yaml");
            if (File.Exists(settings))
            {
                var pdoc = Yaml.Parse(File.ReadAllText(settings));
                name = pdoc.GetScalar("name", name);
                startScene = pdoc.GetScalar("startScene", startScene);
            }
            string assetsPath = Path.Combine(root, "Assets");
            _source = new LooseFileSource(assetsPath);
            assets = new AssetDatabase(assetsPath);
            assets.ScanMetas(createMissing: false);
            // Importer'li asset'ler (font, atlas vb.) editorun urettigi artifact'lardan:
            // Library/Artifacts/<guid>/main; "anahtar#ad" -> adli artifact. Yoksa null -> kaynaga duser.
            string artifacts = Path.Combine(root, "Library", "Artifacts");
            assets.ArtifactResolver = key =>
            {
                int hash = key.IndexOf('#');
                string rel = hash < 0 ? key : key.Substring(0, hash);
                string name = hash < 0 ? "main" : key.Substring(hash + 1);
                string guid = assets.PathToGuid(rel);
                if (guid == null)
                    return null;
                string p = Path.Combine(artifacts, guid, name);
                return File.Exists(p) ? File.ReadAllBytes(p) : null;
            };
            Console.WriteLine("[player] loose: " + assetsPath + " (pak yok)");
        }

        // --- katalog: uretilmis kayit (reflection taramasi YOK) ---
        var catalog = LoadCatalog(root);
        Audio.Source = _source; // muzik: pak araligi / loose dosya; URL'ler dogrudan

        // --- pencere + sokol ---
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
        var window = GLFW.CreateWindow(1280, 720, name, IntPtr.Zero, IntPtr.Zero);
#if DE_RENDERER_METAL
        Sokol.MetalInitWindow(window);
#else
        GLFW.MakeContextCurrent(window);
        GLFW.SwapInterval(1);
#endif
        Sokol.Setup();

        // Atlas sayfalari (GL baglami hazir): sprite/font'lar yuklenirken bolgelere baglanir.
        int atlases = assets.LoadAtlases();
        if (atlases > 0)
            Console.WriteLine($"[player] {atlases} atlas yuklendi");

        // --- sahne ---
        var scene = Scene.Active;
        scene.Catalog = catalog;
        var sceneBytes = _source.ReadBytes(startScene);
        if (sceneBytes == null)
            throw new Exception("startScene bulunamadi: " + startScene);
        if (SceneBinary.IsBaked(sceneBytes))
        {
            // Release: pismis sahne (prefab'lar build'de acilmis) -> dogrudan spawn.
            SceneBinary.Spawn(sceneBytes, null, catalog, assets);
            Console.WriteLine("[player] sahne: baked " + startScene);
        }
        else
        {
            // Dev (loose): YAML yolu.
            var doc = SceneDoc.Parse(System.Text.Encoding.UTF8.GetString(sceneBytes));
            doc.ExpandPrefabs(catalog, assets); // prefab delta kayitlari tam agaca acilir
            doc.Spawn(null, catalog, assets);
        }

        var cb = new CommandBuffer();
        var gameCams = new System.Collections.Generic.List<Camera>
            { new Camera { Order = 0, BackgroundColor = new Color(0, 0, 0, 255) } };
        int camCount = 1;
        double lastT = 0;
        bool mouseWasDown = false;

        while (GLFW.WindowShouldClose(window) == GLFWConst.FALSE)
        {
            GLFW.PollEvents();
            GLFW.GetFramebufferSize(window, out int fbw, out int fbh);
            if (fbw <= 0 || fbh <= 0)
                continue; // minimize

            GLFW.GetWindowContentScale(window, out float uiScale, out _);
            if (uiScale <= 0) uiScale = 1f;
            float lw = fbw / uiScale, lh = fbh / uiScale;
            // Pencere-uzayi -> mantiksal bolen (macOS'ta pencere zaten point'tir).
            GLFW.GetWindowSize(window, out int winW, out _);
            float mouseScale = winW > 0 ? winW / lw : uiScale;

            double t = GLFW.GetTime();
            float dt = lastT > 0 ? (float)(t - lastT) : 0f;
            lastT = t;

            assets.Tick(); // biten async yuklemeleri butceli bagla
            Scene.UpdateAll(dt, lw, lh, simulate: true);

            // Pointer: pencere = oyun ciktisi, mantiksal px birebir.
            GLFW.GetCursorPos(window, out double mx, out double my);
            float px = (float)mx / mouseScale, py = (float)my / mouseScale;
            bool mouseDown = GLFW.GetMouseButton(window, 0) == GLFWConst.PRESS;
            var ptr = scene.Pointer;
            if (mouseDown && !mouseWasDown) ptr.Down(px, py);
            else if (mouseDown) ptr.Move(px, py);
            else if (mouseWasDown) ptr.Up(px, py);
            mouseWasDown = mouseDown;

            // Sahne kameralari swapchain'e (Target=null); kamera yoksa DriveCameras
            // pool[0] piksel-ortho fallback'ini kendisi kurar (>=1 doner).
            camCount = scene.DriveCameras(gameCams, lw, lh);

            cb.Begin();
            for (int i = 0; i < camCount; i++)
                gameCams[i].Encode(cb, fbw, fbh);
            cb.Submit();
            Sokol.Commit();
#if !DE_RENDERER_METAL
            GLFW.SwapBuffers(window);
#endif
        }

        Audio.Shutdown();
        Sokol.Shutdown();
        GLFW.Terminate();
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
