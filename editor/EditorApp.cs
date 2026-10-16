using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

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

        // SDF font: koda gomulu font.ttf (EmbeddedFont.cs).
        Gui.Font = GuiFont.FromMemory(EmbeddedFont.Data);

        var cb = new CommandBuffer();

        // Oyun paylasilan GameOutput RT'sine cizilir; sahnedeki her aktif
        // CameraComponent icin havuzdan bir render kamerasi surulur (stacking).
        // Her Editor-kamerali viewport ayni sahneyi kendi kamerasiyla kendi
        // RT'sine encode eder; mainCam yalniz GUI.
        var gameCams = new System.Collections.Generic.List<Camera>
            { new Camera { Order = 0, BackgroundColor = new Color(45, 50, 70, 255) } };
        int gameCamCount = 1;
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
        var clip = EditorWindow.GetWindow<ClipEditorPanel>();
        var project = EditorWindow.GetWindow<ProjectPanel>();
#if DE_EDITOR
        var dbg = EditorWindow.GetWindow<RenderDebugPanel>();
#endif
        // Kullanici yerlesimi proje altindan (UserSettings gitignore'lanir);
        // yoksa/bozuksa asagidaki varsayilan agac kurulur.
        // Proje: CLI arg > kullanici tercihlerinde son acilan (hala varsa) > Projects/Sandbox. Acilan proje recent'a yazilir.
        string projectPath = ProjectSwitcher.ResolveStartupProject(
            Environment.GetCommandLineArgs() is { Length: > 1 } cli ? cli[1] : null, DefaultProjectPath);
        projectPath = System.IO.Path.GetFullPath(projectPath);
        { var prefs = EditorPrefs.Load(); prefs.Touch(projectPath); prefs.Save(); }
        _window = window;
        _projectFolder = System.IO.Path.GetFileName(projectPath.TrimEnd('\\', '/'));
        GLFW.SetWindowTitle(window, "Editor - " + _projectFolder);
        _layoutPath = System.IO.Path.Combine(projectPath, "UserSettings", "Layout.txt");
        if (!GuiDock.LoadLayout(_layoutPath))
        {
#if DE_EDITOR
            int bottom = GuiDock.Leaf(stackalloc int[] { project.PanelId, clip.PanelId, dbg.PanelId });
#else
            int bottom = GuiDock.Leaf(stackalloc int[] { project.PanelId, clip.PanelId });
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
        _assets.ArtifactResolver = ImportPipeline.ResolveArtifact; // import'lu asset'ler Library'den
        _assets.ArtifactPathResolver = ImportPipeline.ResolveArtifactPath;
        Audio.Source = _assets.Source; // muzik anahtarlari loose dosyadan
        PlayerPrefs.SetRoot(_project.LibraryPath); // oyun PlayerPrefs'i proje-yerel (Library/prefs.dat; git'e girmez)
        AssetDatabase.LogWarning = EditorLog.Warning;
        PixelEffect.LogError = EditorLog.Error; // fx derleme hatalari Console paneline
        Debug.OnInfo = EditorLog.Info;          // oyun kodunun Debug.Log* cagrilari Console paneline (stdout'a EditorLog akitir)
        Debug.OnWarning = EditorLog.Warning;
        Debug.OnError = EditorLog.Error;
        _gameCode = new GameCode();
        try { GameCode.WriteIdeProject(_project); } // IDE csproj (proje koku), derlemeden bagimsiz
        catch (Exception e) { EditorLog.Warning("[gamecode] IDE csproj yazilamadi: " + e.Message); }
        try { IdeFiles.Write(_project); } // .vscode/mcp.json + AI kurallari sablonu + .gitignore
        catch (Exception e) { EditorLog.Warning("[project] IDE dosyalari yazilamadi: " + e.Message); }
        _gameCode.CompileAndLoad(_project, _assets);
        RebuildCatalog();
#if DE_EDITOR
        LifecycleTests.Run(_catalog); // izole sahnede kenar durum smoke testleri
        MovieClipTests.Run(_catalog);
        ParticleTests.Run(_catalog);
        SerializationTests.Run(_catalog);
        InspectorTests.Run();
        LayoutTests.Run(_catalog);
        PointerTests.Run(_catalog);
        RenderGroupTests.Run(_catalog);
        PlayerPrefsTests.Run(projectPath);
        JsonTests.Run(_catalog);
        AsyncQueueTests.Run();
        NetTests.Run();
        ProgressTests.Run(_catalog);
        CatalogTests.Run(_catalog);
        ObjectBinaryTests.Run(_catalog);
        LocalizationTests.Run(_catalog);
        RichTextTests.Run(_catalog);
        PrefabTests.Run(_catalog);
        ThemeTests.Run(_catalog);
#endif
        // Apply yolu: stash'li sahne (stage) de kapsama girsin.
        EditorScene.PrefabEditScope = (es, mutate) => WithPrefabEdit(mutate);
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
        GameOutput.SetDevice(Settings.deviceW, Settings.deviceH, Settings.devicePixelRatio, Settings.deviceName);
        GameOutput.Orientation = Settings.gameViewOrientation;
        GameOutput.OneToOne = Settings.gameViewOneToOne;
        LocTableStore.ApplyWorkingLanguage();
        // Working theme: proje listesinde yoksa (silinmis) tabana duser; prefab cache henuz bos.
        SceneDoc.ActiveTheme = ThemeNames().Contains(Settings.workingTheme ?? "") ? Settings.workingTheme : "";
        if (Settings.sceneCamZoom > 0)
            _sceneView.SetView(new Vec2(Settings.sceneCamX, Settings.sceneCamY), Settings.sceneCamZoom);
        if (Settings.lastScene.Length > 0)
        {
            string last = System.IO.Path.Combine(projectPath, Settings.lastScene);
            if (System.IO.File.Exists(last))
                _editScene.Load(last, _catalog, _assets);
        }
        AssetWatcher.Start(AssetsPath); // degisiklikler otomatik derleme/refresh tetikler
        AtlasSystem.RefreshAll(); // atlas gruplari: import (stamp kapisi) + sayfalar baglanir
        // AI/otomasyon komut kanali (localhost, satir-bazli JSON); ayni port Build/wasm'i HTTP ile de sunar.
        RpcHost.Start(staticRoot: System.IO.Path.Combine(projectPath, "Build", "wasm"));

        while (GLFW.WindowShouldClose(window) == GLFWConst.FALSE)
        {
            GLFW.PollEvents();
            EditorMenu.Poll(); // biriken menu komutlarini calistir
            AssetWatcher.Tick(GLFW.GetTime()); // dosya degisiklikleri + biten derlemeler
            LocTableStore.Tick(GLFW.GetTime()); // bekleyen ceviri tablosu yazimlari (bosta)
            _editScene.TickLoc(GLFW.GetTime(), GuiUtility.KeyboardControl == 0); // bekleyen otomatik bag
            // RPC komutlari yalniz insan jesti yokken kosar (drag ortasinda yapisal
            // mutasyon = IMGUI id-kaymasi sinifi bug'lar; kural notlarda). Otomasyon (ui.*)
            // sentetik jest surerken de bekler; klavye odagi sentetik girdiden kaldiysa engel degil.
            bool idle = GuiUtility.HotControl == 0 && GuiUtility.KeyboardControl == 0;
            if ((idle || UiAutomation.Engaged) && !UiAutomation.Busy)
            {
                _rpcCtx.Scene = _editScene;
                _rpcCtx.Catalog = _catalog;
                _rpcCtx.Assets = _assets;
                RpcHost.Pump(_rpcCtx);
            }
            GLFW.GetFramebufferSize(window, out int fbw, out int fbh);
            if (fbw <= 0 || fbh <= 0)
                continue; // minimize

            // HiDPI: UI/oyun MANTIKSAL (point) uzayda, viewport fiziksel piksel.
            GLFW.GetWindowContentScale(window, out float uiScale, out _);
            if (uiScale <= 0) uiScale = 1f;
            Gui.Scale = uiScale;
            Screen.pixelRatio = GameOutput.PixelRatio; // cihaz profili varsa onun, yoksa pencerenin
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
            if (dt > GameHost.MaxDeltaTime) dt = GameHost.MaxDeltaTime; // player paritesi: uzun frame (derleme, import, arka plan) tek dev adim olmasin
            _assets.Tick(2, dt); // async pompasi + biten async yuklemeleri butceli bagla
            var editLive = _editScene.LiveScene;
            if (editLive != null)
            {
                editLive.ScreenWidth = GameOutput.ViewW;
                editLive.ScreenHeight = GameOutput.ViewH;
                editLive.Update(dt, simulate: false);
                PreviewSession.Step(dt);
            }
            Scene.UpdateAll(PlayMode.ForcedDt > 0f && PlayMode.Simulate ? PlayMode.ForcedDt : dt,
                GameOutput.ViewW, GameOutput.ViewH, PlayMode.Simulate);

            // Oyun ciktisi: oyun sahnesi (Play'de) yoksa edit projeksiyonu.
            // Sahnedeki aktif CameraComponent'ler depth sirasiyla havuz kameralarina
            // surulur (cullingMask routing); hic yoksa fallback piksel-ortho.
            // Render'dan ONCE — layout fitScreen kutulari encode'da kamera rect'ini okur.
            var gameScene = PlayMode.PlayScene ?? editLive;
            if (gameScene != null)
                gameCamCount = gameScene.DriveCameras(gameCams, GameOutput.ViewW, GameOutput.ViewH);
            else
            {
                gameCamCount = 1;
                var cam0 = gameCams[0];
                cam0.ClearColor = true;
                cam0.ClearDepth = true;
                cam0.SetPixelOrtho(GameOutput.ViewW, GameOutput.ViewH);
            }
            // Scene View: edit projeksiyonu (HEP donuk doc).
            if (_sceneView.VisibleLastFrame && editLive != null)
            {
                _sceneView.ApplyEditorCam();
                editLive.Render(_sceneView.EditorCam.Queue);
            }

            GuiRenderer.Queue = mainCam.Queue;
            UiAutomation.Tick(window, mouseScale); // sentetik girdi adimi (varsa) bu frame'in kuyruguna
            guiHost.Frame(window, new Rect(0, 0, lw, lh), drawUi, mouseScale);

            // Detached panel pencereleri: kendi GUI turlari + redock kontrolu.
            GuiDock.UpdateWindows();
            ColorPickerWindow.UpdateWindow();
            EditorPopup.UpdateActive();

            // Surukle-birak: sol tus birakildiysa kayitli hedefe uygula/temizle.
            DragDrop.EndFrame(GLFW.GetMouseButton(window, 0) == GLFWConst.RELEASE);

            // Tum GUI pass'leri bitti: yeni raster glyph'ler varsa atlasi GPU'ya yukle
            // (encode'dan once, frame'de bir kez — sokol dinamik image kurali).
            Gui.Font?.FlushRasterAtlas();

            // Paneller GUI sirasinda resize olabilir (eski RT emekli edilir):
            // hedefler ve kamera listesi ENCODE'dan hemen once kurulur.
            var gameRt = GameOutput.EnsureTarget();
            for (int i = 0; i < gameCamCount; i++)
                gameCams[i].Target = gameRt;
            cameras.Clear();
            if (_sceneView.VisibleLastFrame)
            {
                _sceneView.EditorCam.Target = _sceneView.EnsureTarget();
                cameras.Add(_sceneView.EditorCam);
            }
            cameras.Add(mainCam);

            cb.Begin();
            // Oyun kameralari ayrica encode edilir (Order = depth sirasi): frame
            // debugger yalniz bu araligi yakalar — editor UI draw'lari capture'a girmez.
#if DE_EDITOR
            int gameStart = cb.Length;
#endif
            for (int i = 0; i < gameCamCount; i++)
                gameCams[i].Encode(cb, fbw, fbh);
#if DE_EDITOR
            RenderDebug.NoteGameRange(gameStart, cb.Length, gameRt);
#endif
            Camera.EncodeAll(cameras, cb, fbw, fbh);
            GuiDock.EncodeWindows(cb);
            ColorPickerWindow.Encode(cb);
            EditorPopup.EncodeActive(cb);
#if DE_EDITOR
            // Replay pass'leri canli frame'den ONCE kosar; GUI ayni frame'de RT'yi okur.
            RenderDebug.ReplayIfFrozen();
#endif
            cb.Submit();
            Sokol.Commit();
            UiAutomation.CaptureIfRequested(fbw, fbh, lw, lh); // ui.screenshot: tamamlanmis arka tampon
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
            EditorPopup.PresentActive();
        }

        RpcHost.Stop();
        LocTableStore.Flush();
        GuiDock.SaveLayout(_layoutPath); // kullanici yerlesimi kalici
        SaveSettings();
        Audio.Shutdown();
        Sokol.Shutdown();
        GLFW.DestroyWindow(window);
        GLFW.Terminate();
    }

    // Editor GUI: her event pass'inde bastan kosulur (kontrol sirasi sabit).
    static int _screenW = 800, _screenH = 600;
    const float ToolbarH = 26f;
    internal static int ScreenW => _screenW;
    internal static int ScreenH => _screenH;
    internal static float ToolbarHeight => ToolbarH;
    // ComboBox ogeleri — indeks = (int)HotReloadMode.
    static readonly string[] _hrNames = { "HR: Off", "HR: On Save", "HR: Instant" };
    static SceneViewPanel _sceneView;
    static int _lastReloadedVersion;
    static string _layoutPath;

    // Paneller katalog/asset'lere buradan erisir (statik tip cache degil: proje verisi).
    internal static TypeCatalog Catalog => _catalog;
    internal static AssetDatabase Assets => _assets;
    internal static System.Reflection.Assembly GameAssembly => _gameCode?.GameAssembly; // ModuleBuilder: modul tipleri = bu assembly

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
        UpdateWindowTitle();

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
        // Sol kose: hot reload modu (combobox).
        var newHr = (HotReloadMode)Gui.ComboBox(new Rect(6, 3, 104, 20), (int)PlayMode.HotReload, _hrNames);
        if (newHr != PlayMode.HotReload)
        {
            PlayMode.HotReload = newHr;
            SaveSettings();
        }
        // Working language: edit projeksiyonu + Play bu dili gorur; inspector bu dilin hucresini duzenler.
        DrawLanguageCombo(new Rect(_screenW - 96, 3, 90, 20));
        // Working theme (dil gibi): edit projeksiyonu + Play; duzenlemeler bu temanin katmanina yazilir.
        if (Project.Player.themes.Count > 0)
            DrawThemeCombo(new Rect(_screenW - 96 - 6 - 110, 3, 110, 20));
        // Arka plan is durumu (derleme, player build vs.): non-modal, toolbar'da yasar.
        if (e.Type == EventType.Repaint)
        {
            string status = AssetWatcher.Status.Length > 0 ? AssetWatcher.Status : PlayerBuilder.Status;
            bool failed = AssetWatcher.Status.Length > 0 ? AssetWatcher.Failed : PlayerBuilder.Failed;
            if (status.Length > 0)
                GuiRenderer.DrawTextIn(new Rect(118, 3, 220, 20), status, Gui.FontSize - 3f,
                    failed ? new Color(235, 120, 120, 255) : new Color(150, 200, 150, 255), false, 2);
        }

        // Toolbar altinda TAM pencere dockspace.
        GuiDock.DockSpace(new Rect(0, ToolbarH, _screenW, _screenH - ToolbarH));
        if (e.Type == EventType.Repaint)
            DragDrop.DrawGhost(e.MousePosition); // surukleme hayaleti en ustte
        DrawPerfHud();
    }

    // Toolbar dil combosu: host tablosunun dilleri (tablo yoksa yalniz "en"); secim EditorSettings'e kalici.
    static string[] _langNames = System.Array.Empty<string>();

    static void DrawLanguageCombo(in Rect rect)
    {
        var langs = LocTableStore.Languages(LocTableStore.HostScope);
        if (_langNames.Length != langs.Count)
            _langNames = new string[langs.Count];
        for (int i = 0; i < langs.Count; i++)
            _langNames[i] = langs[i];
        string cur = LocTableStore.WorkingLanguage;
        int idx = 0;
        for (int i = 0; i < _langNames.Length; i++)
            if (string.Equals(_langNames[i], cur, System.StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        int next = Gui.ComboBox(rect, idx, _langNames);
        if (next != idx && next >= 0 && next < _langNames.Length)
            LocTableStore.WorkingLanguage = _langNames[next];
    }

    // --- Working theme (docs/engine-notes.md TEMA) ---
    static string[] _themeNames = System.Array.Empty<string>();

    // Combo listesi: "default" (taban) + PlayerSettings.themes.
    internal static List<string> ThemeNames()
    {
        var list = new List<string> { "" };
        foreach (var t in Project?.Player?.themes ?? new List<string>())
            if (!string.IsNullOrWhiteSpace(t) && !list.Contains(t.Trim()))
                list.Add(t.Trim());
        return list;
    }

    static void DrawThemeCombo(in Rect rect)
    {
        var names = ThemeNames();
        if (_themeNames.Length != names.Count)
            _themeNames = new string[names.Count];
        int idx = 0;
        for (int i = 0; i < names.Count; i++)
        {
            _themeNames[i] = names[i].Length == 0 ? "default" : names[i];
            if (string.Equals(names[i], SceneDoc.ActiveTheme, System.StringComparison.OrdinalIgnoreCase))
                idx = i;
        }
        int next = Gui.ComboBox(rect, idx, _themeNames);
        if (next != idx && next >= 0 && next < names.Count)
            SetWorkingTheme(names[next]);
    }

    // Tema gecisi = dil gibi kayit istemez: acik sahne(ler) deltaya sikisir (aktif katman diff'lenir), tema degisir,
    // prefab cache duser (katman LoadPrefab'da bindiriliyor), yeniden acilir. Undo yigini temizlenir, dirty korunur.
    internal static void SetWorkingTheme(string theme)
    {
        theme ??= "";
        if (string.Equals(theme, SceneDoc.ActiveTheme, System.StringComparison.OrdinalIgnoreCase))
            return;
        if (theme.Length > 0 && !ThemeNames().Contains(theme))
            throw new System.ArgumentException("unknown theme: " + theme + " (PlayerSettings.themes)");
        Settings.workingTheme = theme;
        SaveSettings();
        WithPrefabEdit(() => SceneDoc.ActiveTheme = theme);
        _editScene.ResetHistoryKeepDirty();
        _stashedScene?.ResetHistoryKeepDirty();
        Selection.Clear();
        EditorLog.Info("[theme] working theme: " + (theme.Length == 0 ? "default" : theme));
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
    // Prefab stage'deyken once stage kapanir (dirty ise Save/Discard/Cancel sorulur, acilis ertelenir).
    internal static void OpenScene(string path)
    {
        if (InPrefabStage && !TryExitPrefabStage(() => OpenScene(path)))
            return;
        PlayMode.Stop();
        _editScene.Load(path, _catalog, _assets);
        Selection.DocId = 0;
        Settings.lastScene = System.IO.Path.GetRelativePath(_project.Root, path).Replace('\\', '/');
        SaveSettings();
    }

    // --- PREFAB STAGE (Unity modeli): .prefab dosyasi gecici EditorScene olarak acilir, asil sahne
    // BELLEKTE stash edilir (dirty/undo korunur). Cikista prefab cache duser + sahne yeniden expand
    // olur → instance'lar yeni prefab'i alir, override'lar korunur. lastScene degismez. ---
    static EditorScene _stashedScene;
    internal static bool InPrefabStage => _stashedScene != null;
    internal static string PrefabStagePath => InPrefabStage ? _editScene.Path : null;
    // Stage'deyken bellekte bekleyen asil sahne (RPC dirty kontrolu icin); stage disinda null.
    internal static EditorScene StashedScene => _stashedScene;

    // Bellekteki tum kaydedilmemis duzenlemeleri "atildi" sayar (RPC force yolu: popup acilmadan
    // stage kapanir / sahne degisir). Doc'a dokunmaz, yalnizca dirty bayragi duser.
    internal static void DiscardAllChanges()
    {
        _editScene.DiscardChanges();
        _stashedScene?.DiscardChanges();
    }

    internal static void OpenPrefab(string path)
    {
        if (PlayMode.State != PlayState.Editing)
        {
            EditorLog.Warning("[prefab] stop Play before editing a prefab");
            return;
        }
        if (InPrefabStage)
        {
            if (string.Equals(System.IO.Path.GetFullPath(_editScene.Path), System.IO.Path.GetFullPath(path),
                    StringComparison.OrdinalIgnoreCase))
                return;
            if (!TryExitPrefabStage(() => OpenPrefab(path)))
                return;
        }
        _stashedScene = _editScene;
        _editScene = new EditorScene { SingleRoot = true };
        _editScene.Load(path, _catalog, _assets);
        Selection.Clear();
        foreach (var g in _editScene.Doc.Objects)
            if (g.Parent == 0) { Selection.DocId = g.Id; break; }
        EditorLog.Info("[prefab] editing: " + System.IO.Path.GetFileName(path));
    }

    // Dirty degilse hemen cikar (true). Dirty ise popup acar, 'then' secimden sonra kosar (false).
    internal static bool TryExitPrefabStage(Action then = null)
    {
        if (!InPrefabStage)
            return true;
        if (!_editScene.Dirty)
        {
            ExitPrefabStageNow();
            return true;
        }
        SaveChangesPopup.Show(System.IO.Path.GetFileName(_editScene.Path),
            onSave: () => { _editScene.Save(); ExitPrefabStageNow(); then?.Invoke(); },
            onDiscard: () => { ExitPrefabStageNow(); then?.Invoke(); });
        return false;
    }

    static void ExitPrefabStageNow()
    {
        if (!InPrefabStage)
            return;
        string rel = System.IO.Path.GetRelativePath(_assets.Root, _editScene.Path).Replace('\\', '/');
        _editScene.UnloadLive();
        _editScene = _stashedScene;
        _stashedScene = null;
        ReexpandAllPrefabs(rel); // collapse (eski prefab) → cache dus → taze expand; aktif sahne geri gelir
        Selection.Clear();
        EditorLog.Info("[prefab] stage closed: " + rel);
    }

    // Prefab degisti: acik sahne VE (stage'deyse) stash'li sahne once ESKI prefab'a gore
    // deltaya sikisir, cache sonra TUMDEN duser (nested tuketiciler de bayatlar), ikisi de taze acilir.
    internal static void ReexpandAllPrefabs(string changedRel) => WithPrefabEdit(null);

    // Apply yolu + dosya degisimi ayni bogaz: collapse(eski cache) → mutate → invalidate all → reexpand.
    internal static void WithPrefabEdit(Action mutate)
    {
        string a = _editScene.Collapse();
        string b = _stashedScene?.Collapse();
        mutate?.Invoke();
        _assets.InvalidateAllPrefabs();
        _editScene.ReexpandFrom(a);
        _stashedScene?.ReexpandFrom(b);
    }

    internal static EditorSettings Settings = new();

    internal static void SaveSettings()
    {
        if (_settingsPath == null)
            return;
        Settings.hotReload = PlayMode.HotReload;
        Settings.deviceW = GameOutput.ForcedW;
        Settings.deviceH = GameOutput.ForcedH;
        Settings.devicePixelRatio = GameOutput.Forced ? GameOutput.PixelRatio : 0f;
        Settings.deviceName = GameOutput.ForcedName;
        Settings.gameViewOrientation = GameOutput.Orientation;
        Settings.gameViewOneToOne = GameOutput.OneToOne;
        if (_sceneView != null)
        {
            Settings.sceneCamX = _sceneView.Center.x;
            Settings.sceneCamY = _sceneView.Center.y;
            Settings.sceneCamZoom = _sceneView.Zoom;
        }
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
        _stashedScene?.UnloadLive(); // stage arkasindaki sahne de eski tipleri tutmasin
        _assets.ScanMetas(createMissing: true); // yeni eklenen dosyalar guid alsin
        _gameCode.LoadCompiled(_project, _assets, job);
        RebuildCatalog();
        _stashedScene?.Instantiate(_catalog, _assets); // once stash: aktif sahne sonda edit'in olsun
        _editScene.Instantiate(_catalog, _assets); // bellekteki doc'tan (kaydedilmemis duzenlemeler korunur)
        // AOT uyumluluk: scriptler corelib'e karsi ikinci kez derlenir (bilgi amacli; editor play'ini etkilemez).
        AotCompatCheck.Check(job.Scripts, job.Plugins);
    }

    internal static Project Project => _project;

    // Katalog SADECE bilinen assembly'lerden kurulur (AppDomain taramasi degil:
    // unload edilmis eski oyun assembly'si listede gorunup ad cakismasi yaratabilir).
    // Tek katalog yolu: reflection (docs/registry-removal.md). Player da ayni yoldan kurar.
    static void RebuildCatalog()
    {
        // Plugin dll'leri + oyun assembly'si: tek liste (GameCode.CodeAssemblies); null yok.
        var code = _gameCode.CodeAssemblies;
        var all = new System.Reflection.Assembly[2 + code.Length];
        all[0] = typeof(GameObject).Assembly; all[1] = typeof(App).Assembly;
        Array.Copy(code, 0, all, 2, code.Length);
        var editorAndCode = new System.Reflection.Assembly[1 + code.Length];
        editorAndCode[0] = typeof(App).Assembly;
        Array.Copy(code, 0, editorAndCode, 1, code.Length);

        _catalog = TypeCatalog.FromAssemblies(all);
        foreach (var r in _gameCode.Renames)
            _catalog.RegisterAlias(r.Key, r.Value); // typemap rename'leri: eski ad cozulur
        Scene.Active.Catalog = _catalog;
        _assets.Catalog = _catalog; // [Asset] nesne cache'i eski tiplerle birlikte duser

        // SceneView araclari: builtin'ler + oyun/plugin assembly'lerinin [EditorTool]'lari.
        EditorTools.Rebuild(editorAndCode);

        // RPC komutlari: builtin'ler + oyun/plugin assembly'lerinin [RpcCommand]'lari.
        RpcRegistry.Rebuild(editorAndCode);

        // Asset importer'lari: builtin'ler + oyun/plugin assembly'lerinin [AssetImporter]'lari.
        ImportPipeline.Rebuild(editorAndCode);

        // [CreateAssetMenu] tipleri: sag-tik Create menusu YALNIZ opt-in'lerden kurulur
        // (motor + editor authoring tipleri + oyun/plugin assembly'leri).
        AssetTypes.Clear();
        foreach (var asm in all)
        {
            foreach (var t in asm.GetTypes())
                if (ObjectSerializer.IsCreatable(t))
                    AssetTypes.Add(t);
        }
        AssetTypes.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
    }

    internal static readonly System.Collections.Generic.List<Type> AssetTypes = new();

    // bin/Debug/netX.Y -> repo koku (editor/, player/, aotcompiler/ kardes klasorler).
    internal static string RepoRoot => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    // CLI arg ile baska proje acilabilir.
    static string DefaultProjectPath => System.IO.Path.Combine(RepoRoot, "Projects", "Sandbox");

    static IntPtr _window;
    static string _projectFolder = "";
    // Baslik durumu: (sahne yolu, dirty, play) degisince bir kez yazilir — frame'de alloc yok.
    static string _titleScenePath;
    static bool _titleDirty, _titlePlaying;

    // "Editor - <proje> - <Sahne>[*]  (Playing)": acik sahne ve kaydedilmemis degisiklik gostergesi.
    static void UpdateWindowTitle()
    {
        if (_window == IntPtr.Zero || _editScene == null)
            return;
        string scenePath = _editScene.Path;
        bool dirty = _editScene.Dirty;
        bool playing = PlayMode.State != PlayState.Editing;
        if (ReferenceEquals(scenePath, _titleScenePath) && dirty == _titleDirty && playing == _titlePlaying)
            return;
        _titleScenePath = scenePath;
        _titleDirty = dirty;
        _titlePlaying = playing;
        string scene = scenePath == null ? "(untitled)" : System.IO.Path.GetFileNameWithoutExtension(scenePath);
        GLFW.SetWindowTitle(_window, "Editor - " + _projectFolder + " - " + scene
            + (dirty ? "*" : "") + (playing ? "  (Playing)" : ""));
    }
    // Proje degistirme: pencereyi kapat (cikis yolu RPC/layout/ayarlari kaydeder); yeni surec zaten baslatildi.
    internal static void RequestClose() => GLFW.SetWindowShouldClose(_window, GLFWConst.TRUE);

    static string AssetsPath => _project.AssetsPath;
    static string _settingsPath;

    static Project _project;
    static TypeCatalog _catalog;
    static AssetDatabase _assets;
    static GameCode _gameCode;
    static double _lastUpdateT;
    static EditorScene _editScene = new EditorScene(); // prefab stage'de gecici olarak degisir
    static readonly RpcContext _rpcCtx = new RpcContext();

    internal static EditorScene EditScene => _editScene;
}