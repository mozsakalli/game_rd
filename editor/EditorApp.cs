using System;
using DigitoyEngine;

namespace DigitoyEditor;

public unsafe class App
{
    public static void Main()
    {
        // Crash gorunur olsun: yakalanmayan exception konsola + crash.log'a yazilir.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string text = $"[crash] {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{e.ExceptionObject}\n";
            Console.Error.WriteLine(text);
            Console.Error.Flush();
            try { System.IO.File.AppendAllText("crash.log", text); }
            catch { }
        };

        GLFW.Init();
        // Backend DERLEME zamani secilir: DE_RENDERER_METAL / DE_RENDERER_OPENGL
        // (csproj Renderer ozelligi; macOS varsayilan Metal, -p:Renderer=OpenGL ile GL).
#if DE_RENDERER_METAL
        bool metal = true;
#else
        bool metal = false;
#endif
        if (metal)
        {
            // Metal: GLFW GL context'i OLUSTURMAZ; sunumu CAMetalLayer + sokol yapar.
            GLFW.WindowHint(GLFWConst.CLIENT_API, GLFWConst.NO_API);
            GLFW.WindowHint(GLFWConst.SCALE_TO_MONITOR, GLFWConst.TRUE);
        }
        else
        {
            // sokol GLCORE backend'i GL 4.1 core context ister.
            GLFW.WindowHint(GLFWConst.CONTEXT_VERSION_MAJOR, 4);
            GLFW.WindowHint(GLFWConst.CONTEXT_VERSION_MINOR, 1);
            GLFW.WindowHint(GLFWConst.OPENGL_PROFILE, GLFWConst.OPENGL_CORE_PROFILE);
            GLFW.WindowHint(GLFWConst.OPENGL_FORWARD_COMPAT, GLFWConst.TRUE);
            GLFW.WindowHint(GLFWConst.SCALE_TO_MONITOR, GLFWConst.TRUE); // %125/%150 ekranda mantikli boyut
        }
        var window = GLFW.CreateWindow(1600, 900, "Editor", IntPtr.Zero, IntPtr.Zero);
        if (metal)
        {
            // NSWindow'a CAMetalLayer tak + MTLDevice kur (Setup device'i buradan alir).
            Sokol.MetalInitWindow(window);
        }
        else
        {
            GLFW.MakeContextCurrent(window);
            GLFW.SwapInterval(1);
        }

        Sokol.Setup();

        // SDF font (Windows sistem fontu; yoksa text cizilmez, UI yine calisir).
        /*
        Gui.Font = GuiFont.Load("C:\\Windows\\Fonts\\segoeui.ttf")
                ?? GuiFont.Load("C:\\Windows\\Fonts\\arial.ttf");
                */
        Gui.Font = GuiFont.Load("font.ttf");

        var cb = new CommandBuffer();

        // Oyun paylasilan GameOutput RT'sine cizilir; her Editor-kamerali viewport
        // ayni sahneyi kendi kamerasiyla kendi RT'sine encode eder; mainCam yalniz GUI.
        var sceneCam = new Camera { Order = 0, BackgroundColor = new Color(45, 50, 70, 255) };
        var mainCam = new Camera { Order = 9, BackgroundColor = new Color(28, 30, 36, 255) };
        var cameras = new System.Collections.Generic.List<Camera>();

        var guiHost = new GuiHost();
        GuiHost.GuiFunc drawUi = DrawUi;

        NativeWindow.Initialize(window);
        GuiInput.Attach(window);

        // Pencere havuzu: editor cekirdegi tipleri bilir ama iceriklerini bilmez —
        // her pencere EditorWindow olarak kendini cizer.
        var hierarchy = EditorWindow.GetWindow<HierarchyPanel>();
        var inspector = EditorWindow.GetWindow<InspectorPanel>();
        _sceneView = EditorWindow.GetWindow<SceneViewPanel>();
        var gameView = EditorWindow.GetWindow<GameViewPanel>();
        var timeline = EditorWindow.GetWindow<TimelinePanel>();
        var clip = EditorWindow.GetWindow<ClipPanel>();
        var project = EditorWindow.GetWindow<ProjectPanel>();
#if DE_EDITOR
        var dbg = EditorWindow.GetWindow<RenderDebugPanel>();
#endif
        // Kullanici yerlesimi proje altindan (UserSettings gitignore'lanir);
        // yoksa/bozuksa asagidaki varsayilan agac kurulur.
        string projectPath = Environment.GetCommandLineArgs() is { Length: > 1 } cli
            ? cli[1] : DefaultProjectPath;
        _layoutPath = System.IO.Path.Combine(projectPath, "UserSettings", "Layout.txt");
        if (!GuiDock.LoadLayout(_layoutPath))
        {
#if DE_EDITOR
            int bottom = GuiDock.Leaf(stackalloc int[] { project.PanelId, clip.PanelId, timeline.PanelId, dbg.PanelId });
#else
            int bottom = GuiDock.Leaf(stackalloc int[] { project.PanelId, clip.PanelId, timeline.PanelId });
#endif
            int center = GuiDock.Split(horizontal: false, 0.68f,
                GuiDock.Leaf(stackalloc int[] { _sceneView.PanelId, gameView.PanelId }), bottom);
            GuiDock.SetRoot(GuiDock.Split(horizontal: true, 0.2f,
                GuiDock.Leaf(stackalloc int[] { hierarchy.PanelId }),
                GuiDock.Split(horizontal: true, 0.74f, center,
                    GuiDock.Leaf(stackalloc int[] { inspector.PanelId }))));
        }

        // Native menu bar: [MenuItem] metodlari taranir (ileride oyun editor
        // assembly'si de eklenecek).
        EditorMenu.Build(window, typeof(App).Assembly);

        // Proje ac + oyun kodunu Assets'ten derle/yukle + katalog kur + sahne yukle.
        _project = Project.Load(projectPath);
        _assets = new AssetDatabase(AssetsPath);
        _assets.ScanMetas(createMissing: true); // GUID kimligi: eksik .meta uretilir
        ImportPipeline.Init(_project);
        _assets.ArtifactResolver = ImportPipeline.ResolveMainArtifact; // import'lu asset'ler Library'den
        _gameCode = new GameCode();
        _gameCode.CompileAndLoad(_project, _assets);
        RebuildCatalog();
#if DE_EDITOR
        LifecycleTests.Run(_catalog); // izole sahnede kenar durum smoke testleri
        SerializationTests.Run(_catalog);
        LayoutTests.Run(_catalog);
#endif
        // Kullanici ayarlari (standart: [Serializable] + ObjectSerializer).
        // Otomatik sahne ACILMAZ: yalniz kullanicinin son actigi sahne (varsa) geri gelir.
        _settingsPath = System.IO.Path.Combine(projectPath, "UserSettings", "EditorSettings.asset");
        try
        {
            if (System.IO.File.Exists(_settingsPath))
                Settings = ObjectSerializer.Load<EditorSettings>(_settingsPath);
        }
        catch { }
        PlayMode.HotReload = Settings.hotReload;
        Gui.FontSize = Math.Clamp(Settings.uiFontSize, 10f, 24f);
        if (Settings.lastScene.Length > 0)
        {
            string last = System.IO.Path.Combine(projectPath, Settings.lastScene);
            if (System.IO.File.Exists(last))
                _editScene.Load(last, _catalog, _assets);
        }
        AssetWatcher.Start(AssetsPath); // degisiklikler otomatik derleme/refresh tetikler
        AtlasBuilder.BuildAll(); // atlas gruplari: sprite'lar HER modda atlastan cizilir
        RpcHost.Start(); // AI/otomasyon komut kanali (localhost, satir-bazli JSON)

        while (GLFW.WindowShouldClose(window) == GLFWConst.FALSE)
        {
            GLFW.PollEvents();
            EditorMenu.Poll(); // biriken menu komutlarini calistir
            AssetWatcher.Tick(GLFW.GetTime()); // dosya degisiklikleri + biten derlemeler
            // RPC komutlari yalniz insan jesti yokken kosar (drag ortasinda yapisal
            // mutasyon = IMGUI id-kaymasi sinifi bug'lar; kural notlarda).
            if (GuiUtility.HotControl == 0 && GuiUtility.KeyboardControl == 0)
            {
                _rpcCtx.Scene = _editScene;
                _rpcCtx.Catalog = _catalog;
                _rpcCtx.Assets = _assets;
                RpcHost.Pump(_rpcCtx);
            }
            AtlasBuilder.Tick(); // kirli atlas gruplari (yukler indiginde) yeniden paketlenir
            GLFW.GetFramebufferSize(window, out int fbw, out int fbh);
            if (fbw <= 0 || fbh <= 0)
                continue; // minimize

            // HiDPI: UI/oyun MANTIKSAL (point) uzayda, viewport fiziksel piksel.
            GLFW.GetWindowContentScale(window, out float uiScale, out _);
            if (uiScale <= 0) uiScale = 1f;
            Gui.Scale = uiScale;
            float lw = fbw / uiScale, lh = fbh / uiScale;

            // Fare bolen'i: GLFW imlec konumu PENCERE (screen-coord) uzayinda gelir;
            // GUI mantiksal (lw x lh) uzayda calisir. Windows'ta pencere==framebuffer
            // (piksel) oldugundan bolen uiScale'e esit; macOS'ta pencere zaten point
            // oldugundan bolen 1'e duser. uiScale'e bolmek macOS'ta konumu yariya
            // indiriyordu (sag-alt -> merkez).
            GLFW.GetWindowSize(window, out int winW, out _);
            float mouseScale = winW > 0 ? winW / lw : uiScale;

            // Point koordinatlari, orijin sol-ust (y asagi). Oyun GameOutput boyutunda yasar.
            mainCam.SetPixelOrtho(lw, lh);

            float t = (float)GLFW.GetTime();
            _screenW = (int)lw;
            _screenH = (int)lh;
            TickPerfStats(t);
            GameOutput.BeginFrame(); // onceki frame'de emekli olan RT'ler simdi yok edilir
            _sceneView.BeginFrame();

            // AYRIK SAHNELER: edit projeksiyonu HIC simule olmaz (lifecycle-only),
            // oyun sahnesi (varsa) UpdateAll'da simule olur. Ekran = oyun cikti boyutu.
            float dt = _lastUpdateT > 0 ? (float)(t - _lastUpdateT) : 0f;
            _lastUpdateT = t;
            _assets.Tick(); // biten async yuklemeleri butceli bagla
            var editLive = _editScene.LiveScene;
            if (editLive != null)
            {
                editLive.ScreenWidth = GameOutput.ViewW;
                editLive.ScreenHeight = GameOutput.ViewH;
                editLive.Update(dt, simulate: false);
            }
            Scene.UpdateAll(dt, GameOutput.ViewW, GameOutput.ViewH, PlayMode.Simulate);

            // Oyun ciktisi: oyun sahnesi (Play'de) yoksa edit projeksiyonu.
            // Projeksiyon sahnedeki CameraComponent'ten (WYSIWYG); yoksa fallback
            // sabit piksel-ortho (eski davranis). Render'dan ONCE basilir — layout
            // fitScreen kutulari encode sirasinda kamera rect'ini okur.
            var gameScene = PlayMode.PlayScene ?? editLive;
            var camComp = gameScene?.MainCamera;
            if (camComp != null)
                camComp.ApplyTo(sceneCam, GameOutput.ViewW, GameOutput.ViewH);
            else
                sceneCam.SetPixelOrtho(GameOutput.ViewW, GameOutput.ViewH);
            gameScene?.Render(sceneCam.Queue);
            // Scene View: edit projeksiyonu (HEP donuk doc).
            if (_sceneView.VisibleLastFrame && editLive != null)
            {
                _sceneView.ApplyEditorCam();
                editLive.Render(_sceneView.EditorCam.Queue);
            }

            GuiRenderer.Queue = mainCam.Queue;
            guiHost.Frame(window, new Rect(0, 0, lw, lh), drawUi, mouseScale);

            // Detached panel pencereleri: kendi GUI turlari + redock kontrolu.
            GuiDock.UpdateWindows();
            ColorPickerWindow.UpdateWindow();

            // Surukle-birak: sol tus birakildiysa kayitli hedefe uygula/temizle.
            DragDrop.EndFrame(GLFW.GetMouseButton(window, 0) == GLFWConst.RELEASE);

            // Tum GUI pass'leri bitti: yeni raster glyph'ler varsa atlasi GPU'ya yukle
            // (encode'dan once, frame'de bir kez — sokol dinamik image kurali).
            Gui.Font?.FlushRasterAtlas();

            // Paneller GUI sirasinda resize olabilir (eski RT emekli edilir):
            // hedefler ve kamera listesi ENCODE'dan hemen once kurulur.
            sceneCam.Target = GameOutput.EnsureTarget();
            cameras.Clear();
            if (_sceneView.VisibleLastFrame)
            {
                _sceneView.EditorCam.Target = _sceneView.EnsureTarget();
                cameras.Add(_sceneView.EditorCam);
            }
            cameras.Add(mainCam);

            cb.Begin();
            // Oyun kamerasi ayrica encode edilir (Order=0, en dusuk): frame debugger
            // yalniz bu araligi yakalar — editor UI draw'lari capture'a girmez.
#if DE_EDITOR
            int gameStart = cb.Length;
#endif
            sceneCam.Encode(cb, fbw, fbh);
#if DE_EDITOR
            RenderDebug.NoteGameRange(gameStart, cb.Length, sceneCam.Target);
#endif
            Camera.EncodeAll(cameras, cb, fbw, fbh);
            GuiDock.EncodeWindows(cb);
            ColorPickerWindow.Encode(cb);
#if DE_EDITOR
            // Replay pass'leri canli frame'den ONCE kosar; GUI ayni frame'de RT'yi okur.
            RenderDebug.ReplayIfFrozen();
#endif
            cb.Submit();
            Sokol.Commit();
#if DE_EDITOR
            RenderDebug.AfterSubmit(cb);
#endif

            // GL'de swapchain sunumu SwapBuffers ile; Metal'de sokol_gfx drawable'i
            // sg_commit icinde present eder (SwapBuffers gereksiz).
            if (!metal)
                GLFW.SwapBuffers(window);
            // MakeContextCurrent implicit flush yapar (WGL); blit guncel iceriden okur.
            GuiDock.PresentWindows();
            ColorPickerWindow.Present();
        }

        RpcHost.Stop();
        GuiDock.SaveLayout(_layoutPath); // kullanici yerlesimi kalici
        SaveSettings();
        Sokol.Shutdown();
        GLFW.DestroyWindow(window);
        GLFW.Terminate();
    }

    // Editor GUI: her event pass'inde bastan kosulur (kontrol sirasi sabit).
    static int _screenW = 800, _screenH = 600;
    const float ToolbarH = 26f;
    static SceneViewPanel _sceneView;
    static int _lastReloadedVersion;
    static string _layoutPath;

    // Paneller katalog/asset'lere buradan erisir (statik tip cache degil: proje verisi).
    internal static TypeCatalog Catalog => _catalog;
    internal static AssetDatabase Assets => _assets;

    static void DrawUi()
    {
        // Global kisayollar (textfield odagi yokken): Ctrl+Z / Ctrl+Y.
        var e = Event.Current;
        if (e.Type == EventType.KeyDown && (e.Modifiers & EventModifiers.Control) != 0
            && GuiUtility.KeyboardControl == 0)
        {
            if (e.KeyCode == GLFWConst.KEY_Z) { _editScene.DoUndo(); e.Use(); }
            else if (e.KeyCode == GLFWConst.KEY_Y) { _editScene.DoRedo(); e.Use(); }
        }
        // Jest bitti (drag birakildi): ustteki undo girisi muhurlenir, coalescing durur.
        // "Aninda" hot reload da ayni anda tetiklenir (drag ortasinda her frame reload olmaz).
        if (GuiUtility.HotControl == 0)
        {
            _editScene.History.SealTop();
            if (PlayMode.HotReload == HotReloadMode.Instant
                && _editScene.History.Version != _lastReloadedVersion)
            {
                _lastReloadedVersion = _editScene.History.Version;
                PlayMode.DoHotReload(); // Play degilse no-op
            }
        }
        // Secim preview sahibinden ayrildiysa oturum ortulu biter (izler reload'la olur).
        PreviewSession.Tick(Selection.DocId);

        // Ust toolbar: Play/Pause/Stop + hot reload modu (Kapali/Kaydette/Aninda).
        if (e.Type == EventType.Repaint)
            GuiRenderer.DrawRect(new Rect(0, 0, _screenW, ToolbarH), new Color(24, 26, 32, 255));
        float cx = _screenW * 0.5f;
        if (Gui.Button(new Rect(cx - 82, 3, 50, 20), PlayMode.State == PlayState.Editing ? "Play" : "Play*"))
            PlayMode.Play();
        if (Gui.Button(new Rect(cx - 28, 3, 56, 20), PlayMode.State == PlayState.Paused ? "Pause*" : "Pause"))
            PlayMode.Pause();
        if (Gui.Button(new Rect(cx + 32, 3, 50, 20), "Stop"))
            PlayMode.Stop();
        // Sol kose: hot reload modu dongusu.
        string hrLabel = PlayMode.HotReload switch
        {
            HotReloadMode.Off => "HR: Off",
            HotReloadMode.OnSave => "HR: On Save",
            _ => "HR: Instant",
        };
        if (Gui.Button(new Rect(6, 3, 104, 20), hrLabel))
        {
            PlayMode.HotReload = (HotReloadMode)(((int)PlayMode.HotReload + 1) % 3);
            SaveSettings();
        }
        // Arka plan is durumu (derleme vs.): non-modal, toolbar'da yasar.
        if (e.Type == EventType.Repaint && AssetWatcher.Status.Length > 0)
            GuiRenderer.DrawTextIn(new Rect(118, 3, 220, 20), AssetWatcher.Status, Gui.FontSize - 3f,
                AssetWatcher.Failed ? new Color(235, 120, 120, 255) : new Color(150, 200, 150, 255), false, 2);

        // Toolbar altinda TAM pencere dockspace.
        GuiDock.DockSpace(new Rect(0, ToolbarH, _screenW, _screenH - ToolbarH));
        if (e.Type == EventType.Repaint)
            DragDrop.DrawGhost(e.MousePosition); // surukleme hayaleti en ustte
        DrawPerfHud();
    }

    // Zero-alloc perf HUD (sag ust): frame ms (EMA), alloc/frame, canli GO sayisi.
    static float _frameMsEma;
    static long _lastAllocTotal;
    static long _allocPerFrame;
    static double _lastFrameT;

    static void TickPerfStats(double now)
    {
        if (_lastFrameT > 0)
        {
            float ms = (float)((now - _lastFrameT) * 1000.0);
            _frameMsEma = _frameMsEma <= 0 ? ms : _frameMsEma * 0.95f + ms * 0.05f;
        }
        _lastFrameT = now;
        long total = GC.GetAllocatedBytesForCurrentThread();
        _allocPerFrame = total - _lastAllocTotal;
        _lastAllocTotal = total;
    }

    static void DrawPerfHud()
    {
        if (Event.Current.Type != EventType.Repaint)
            return;
        Span<char> tmp = stackalloc char[96];
        int n = 0;
        AppendInt(tmp, ref n, (int)(_frameMsEma * 100)); // ms x100 (2 ondalik)
        Append(tmp, ref n, " ms100  ");
        AppendInt(tmp, ref n, (int)_allocPerFrame);
        Append(tmp, ref n, " B/f  ");
        AppendInt(tmp, ref n, Scene.Active.RootCount);
        Append(tmp, ref n, " go");
        GuiRenderer.DrawTextIn(new Rect(_screenW - 320, 4, 316, 18), tmp.Slice(0, n), Gui.FontSize - 2f,
            new Color(160, 220, 160, 255));
    }

    static void AppendInt(Span<char> dst, ref int n, int value)
    {
        value.TryFormat(dst.Slice(n), out int w);
        n += w;
    }

    static void Append(Span<char> dst, ref int n, ReadOnlySpan<char> s)
    {
        s.CopyTo(dst.Slice(n));
        n += s.Length;
    }

    internal static void ReloadScene()
    {
        if (_editScene.Path != null)
            _editScene.Load(_editScene.Path, _catalog, _assets);
    }

    // Sahne acmanin tek bogazi: Play durur, doc yuklenir, son-sahne kaydi guncellenir.
    internal static void OpenScene(string path)
    {
        PlayMode.Stop();
        _editScene.Load(path, _catalog, _assets);
        Selection.DocId = 0;
        Settings.lastScene = System.IO.Path.GetRelativePath(_project.Root, path).Replace('\\', '/');
        SaveSettings();
    }

    internal static EditorSettings Settings = new();

    internal static void SaveSettings()
    {
        if (_settingsPath == null)
            return;
        Settings.hotReload = PlayMode.HotReload;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_settingsPath));
            ObjectSerializer.Save(Settings, _settingsPath, _assets);
        }
        catch { }
    }

    // Kod degisikligi: oyun durur, sahne yikilir, ALC unload, taze derleme, katalog + sahne yeniden.
    internal static void ReloadCode()
    {
        var job = GameCode.CompileOnly(_project);
        if (!job.Ok)
        {
            EditorLog.Error("compile failed:\n" + job.Output);
            return;
        }
        ApplyCompiledGame(job);
    }

    // ANA THREAD swap: derlenmis dll'i yukler (eski instance'lar once olur ki ALC bosalsin).
    internal static void ApplyCompiledGame(GameCode.CompileJob job)
    {
        PlayMode.Stop(); // eski oyun tiplerinin instance'lari ALC'yi rehin almasin
#if DE_EDITOR
        RenderDebug.Unfreeze(); // frozen capture eski tiplerin kaynaklarini tutmasin
#endif
        _editScene.UnloadLive();
        _assets.ScanMetas(createMissing: true); // yeni eklenen dosyalar guid alsin
        _gameCode.LoadCompiled(_project, _assets, job);
        RebuildCatalog();
        _editScene.Instantiate(_catalog, _assets); // bellekteki doc'tan (kaydedilmemis duzenlemeler korunur)
    }

    internal static Project Project => _project;

    // Katalog SADECE bilinen assembly'lerden kurulur (AppDomain taramasi degil:
    // unload edilmis eski oyun assembly'si listede gorunup ad cakismasi yaratabilir).
    static void RebuildCatalog()
    {
        _catalog = _gameCode.GameAssembly != null
            ? TypeCatalog.FromAssemblies(typeof(GameObject).Assembly, typeof(App).Assembly, _gameCode.GameAssembly)
            : TypeCatalog.FromAssemblies(typeof(GameObject).Assembly, typeof(App).Assembly);
        foreach (var r in _gameCode.Renames)
            _catalog.RegisterAlias(r.Key, r.Value); // typemap rename'leri: eski ad cozulur
        Scene.Active.Catalog = _catalog;

        // SceneView araclari: builtin'ler + oyun assembly'sinin [EditorTool]'lari.
        EditorTools.Rebuild(typeof(App).Assembly, _gameCode.GameAssembly);

        // RPC komutlari: builtin'ler + oyun assembly'sinin [RpcCommand]'lari.
        RpcRegistry.Rebuild(typeof(App).Assembly, _gameCode.GameAssembly);

        // Asset importer'lari: builtin'ler + oyun assembly'sinin [AssetImporter]'lari.
        ImportPipeline.Rebuild(typeof(App).Assembly, _gameCode.GameAssembly);

        // [CreateAssetMenu] tipleri: sag-tik Create menusu YALNIZ opt-in'lerden kurulur
        // (motor + editor authoring tipleri + oyun assembly'si).
        AssetTypes.Clear();
        foreach (var asm in _gameCode.GameAssembly != null
            ? new[] { typeof(GameObject).Assembly, typeof(App).Assembly, _gameCode.GameAssembly }
            : new[] { typeof(GameObject).Assembly, typeof(App).Assembly })
        {
            foreach (var t in asm.GetTypes())
                if (ObjectSerializer.IsCreatable(t))
                    AssetTypes.Add(t);
        }
        AssetTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
    }

    internal static readonly System.Collections.Generic.List<Type> AssetTypes = new();

    // bin/Debug/netX.Y -> repo koku. CLI arg ile baska proje acilabilir.
    static string DefaultProjectPath => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Projects", "Sandbox"));

    static string AssetsPath => _project.AssetsPath;
    static string _settingsPath;

    static Project _project;
    static TypeCatalog _catalog;
    static AssetDatabase _assets;
    static GameCode _gameCode;
    static double _lastUpdateT;
    static readonly EditorScene _editScene = new EditorScene();
    static readonly RpcContext _rpcCtx = new RpcContext();

    internal static EditorScene EditScene => _editScene;
}