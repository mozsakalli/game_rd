// ESKI demo uygulamasi — DigitoyEditor bunu tumden karsiliyor. API'lerden
// geri kaldi; referans olarak duruyor. Derlemek icin DE_DEMO_APP tanimla.
#if DE_DEMO_APP
using System;

namespace DigitoyEngine;

public unsafe class App
{
    // GUI'den kontrol edilen demo durumu.
    static bool _blur = true;
    static float _speed = 1f;

    public static void Main()
    {
        GLFW.Init();
        // sokol GLCORE backend'i GL 4.1 core context ister.
        GLFW.WindowHint(GLFWConst.CONTEXT_VERSION_MAJOR, 4);
        GLFW.WindowHint(GLFWConst.CONTEXT_VERSION_MINOR, 1);
        GLFW.WindowHint(GLFWConst.OPENGL_PROFILE, GLFWConst.OPENGL_CORE_PROFILE);
        GLFW.WindowHint(GLFWConst.OPENGL_FORWARD_COMPAT, GLFWConst.TRUE);
        GLFW.WindowHint(GLFWConst.SCALE_TO_MONITOR, GLFWConst.TRUE); // %125/%150 ekranda mantikli boyut
        var window = GLFW.CreateWindow(800, 600, "Editor", IntPtr.Zero, IntPtr.Zero);
        GLFW.MakeContextCurrent(window);
        GLFW.SwapInterval(1);

        Sokol.Setup();

        // SDF font (Windows sistem fontu; yoksa text cizilmez, UI yine calisir).
        Gui.Font = GuiFont.Load("C:\\Windows\\Fonts\\segoeui.ttf")
                ?? GuiFont.Load("C:\\Windows\\Fonts\\arial.ttf");

        var cb = new CommandBuffer();
        var quad = Mesh.Quad();
        var white = Texture.FromColor(1, 1, new Color(255, 255, 255, 255));
        var material = new Material { MainTexture = white };

        // Blur'lu dialog senaryosu: sahne yarim cozunurluklu RT'ye cizilir,
        // separable gaussian ile ping-pong blur'lanir, ekrana bulanik arka plan
        // olarak basilir ve ustune net dialog cizilir (mobil dialog-blur deseni).
        GLFW.GetFramebufferSize(window, out int fbw0, out int fbh0);
        int bw = fbw0 / 2, bh = fbh0 / 2; // yarim cozunurluk: hem hiz hem bedava yumusama
        var rtScene = Texture.CreateRenderTarget(bw, bh);
        var rtPing = Texture.CreateRenderTarget(bw, bh);

        // Blit/blur materyalleri opak (blend yok) — tam ekran kopya.
        var blurHMat = new Material
        {
            MainTexture = rtScene,
            Shader = MakeBlurShader(1f / bw, 0f),
            SrcBlend = BlendFactor.One,
            DstBlend = BlendFactor.Zero,
            SortMode = SortMode.Opaque,
        };
        var blurVMat = new Material
        {
            MainTexture = rtPing,
            Shader = MakeBlurShader(0f, 1f / bh),
            SrcBlend = BlendFactor.One,
            DstBlend = BlendFactor.Zero,
            SortMode = SortMode.Opaque,
        };
        var screenMat = new Material
        {
            MainTexture = rtScene,
            SrcBlend = BlendFactor.One,
            DstBlend = BlendFactor.Zero,
            SortMode = SortMode.Opaque,
        };

        var sceneCam = new Camera { Order = 0, Target = rtScene, BackgroundColor = new Color(45, 50, 70, 255) };
        var blurHCam = new Camera { Order = 1, Target = rtPing, ClearColor = false };
        blurHCam.SetPixelOrtho(bw, bh);
        var blurVCam = new Camera { Order = 2, Target = rtScene, ClearColor = false };
        blurVCam.SetPixelOrtho(bw, bh);
        var mainCam = new Camera { Order = 3, BackgroundColor = new Color(20, 23, 30, 255) };
        var cameras = new System.Collections.Generic.List<Camera> { sceneCam, blurHCam, blurVCam, mainCam };

        var guiHost = new GuiHost();
        GuiHost.GuiFunc drawUi = DrawUi;

        NativeWindow.Initialize(window);
        GuiInput.Attach(window);

        // Dock agaci: ALT serit — solda kontrol tablari, sagda liste/ekstra.
        // (Timeline ve Frame Dbg panelleri DigitoyEditor'e tasindi.)
        int controlsPanel = GuiDock.RegisterPanel("Kontroller", ControlsPanel);
        int listPanel = GuiDock.RegisterPanel("Liste", ListPanel);
        int extraPanel = GuiDock.RegisterPanel("Ekstra", ExtraPanel);
        GuiDock.SetRoot(GuiDock.Split(horizontal: true, 0.28f,
            GuiDock.Leaf(stackalloc int[] { controlsPanel, extraPanel }),
            GuiDock.Leaf(stackalloc int[] { listPanel })));

        // GameObject katmani demosu: yaml varsa AUTHORED kaynaktan yukle;
        // yoksa kod kurar ve ilk yaml'i yazar (bootstrap).
        _catalog = TypeCatalog.FromReflection(); // oyun assembly reload'unda yeniden kurulacak
        _assets = new AssetDatabase(AssetsPath);
        Scene.Active.Catalog = _catalog;
#if DE_EDITOR
        LifecycleTests.Run(_catalog); // izole sahnede kenar durum smoke testleri
        SerializationTests.Run(_catalog);
#endif
        if (System.IO.File.Exists(ScenePath))
        {
            SceneDoc.Load(ScenePath).InstantiateInto(Scene.Active, _catalog, _assets);
        }
        else
        {
            for (int i = 0; i < 8; i++)
            {
                var go = new GameObject("orb");
                var sr = go.AddComponent<SpriteRenderer>();
                sr.Texture = _assets.LoadTexture("orb.png"); // async: gelene kadar beyaz
                sr.Width = 110f;
                sr.Height = 110f;
                var sp = go.AddComponent<Spinner>();
                sp.Index = i;
                sp.Target = sr; // ref round-trip testi: yaml'da "N:SpriteRenderer:0"
            }
            SceneDoc.Capture(Scene.Active, _catalog).Save(ScenePath);
        }

        while (GLFW.WindowShouldClose(window) == GLFWConst.FALSE)
        {
            GLFW.PollEvents();
            GLFW.GetFramebufferSize(window, out int fbw, out int fbh);
            if (fbw <= 0 || fbh <= 0)
                continue; // minimize

            // HiDPI: UI/oyun MANTIKSAL (point) uzayda, viewport fiziksel piksel.
            GLFW.GetWindowContentScale(window, out float uiScale, out _);
            if (uiScale <= 0) uiScale = 1f;
            Gui.Scale = uiScale;
            float lw = fbw / uiScale, lh = fbh / uiScale;

            // Point koordinatlari, orijin sol-ust (y asagi).
            sceneCam.SetPixelOrtho(lw, lh); // sahne point uzayinda, RT yarim cozunurluk
            mainCam.SetPixelOrtho(lw, lh);

            float t = (float)GLFW.GetTime();
            _screenW = (int)lw;
            _screenH = (int)lh;
            TickPerfStats(t);

            // Retained sahne: Start/Update/LateUpdate + frame sonu destroy.
            Time.Tick(t);
            _assets.Tick(); // biten async yuklemeleri butceli bagla
            Scene.UpdateAll();
            Scene.Active.Render(sceneCam.Queue);

            // Blur zinciri (toggle ile): kapaliyken rtScene keskin kalir.
            if (_blur)
            {
                Mat4 fsBlur = Model2D(bw * 0.5f, bh * 0.5f, 0f, bw, bh);
                blurHCam.Queue.DrawMesh(quad, blurHMat, in fsBlur, new Color(255, 255, 255, 255), 0, 0, 1, 1, 0);
                blurVCam.Queue.DrawMesh(quad, blurVMat, in fsBlur, new Color(255, 255, 255, 255), 0, 0, 1, 1, 0);
            }

            // Final: (bulanik) arka plan + GUI
            Mat4 fsMain = Model2D(lw * 0.5f, lh * 0.5f, 0f, lw, lh);
            mainCam.Queue.DrawMesh(quad, screenMat, in fsMain, new Color(255, 255, 255, 255), 0, 0, 1, 1, 0);

            GuiRenderer.Queue = mainCam.Queue;
            guiHost.Frame(window, new Rect(0, 0, lw, lh), drawUi, uiScale);

            // Detached panel pencereleri: kendi GUI turlari + redock kontrolu.
            GuiDock.UpdateWindows();

            // Yeni raster glyph'ler varsa atlasi GPU'ya yukle (frame'de bir kez).
            Gui.Font?.FlushRasterAtlas();

            cb.Begin();
            Camera.EncodeAll(cameras, cb, fbw, fbh);
            GuiDock.EncodeWindows(cb);
#if DE_EDITOR
            // Replay pass'leri canli frame'den ONCE kosar; GUI ayni frame'de RT'yi okur.
            RenderDebug.ReplayIfFrozen();
#endif
            cb.Submit();
            Sokol.Commit();
#if DE_EDITOR
            RenderDebug.AfterSubmit(cb);
#endif

            GLFW.SwapBuffers(window);
            // MakeContextCurrent implicit flush yapar (WGL); blit guncel iceriden okur.
            GuiDock.PresentWindows();
        }

        Sokol.Shutdown();
        GLFW.DestroyWindow(window);
        GLFW.Terminate();
    }

    // Demo GUI: her event pass'inde bastan kosulur (kontrol sirasi sabit).
    static Vec2 _scroll;
    static float _panelValue = 2f;
    static int _screenW = 800, _screenH = 600;

    static void DrawUi()
    {
        // Alt serit dock alani; tab'i disari surukle = native pencereye detach.
        float dockH = MathF.Min(320f, _screenH * 0.5f);
        GuiDock.DockSpace(new Rect(0, _screenH - dockH, _screenW, dockH));
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
        GuiRenderer.DrawTextIn(new Rect(_screenW - 320, 4, 316, 18), tmp.Slice(0, n), 12f,
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

    // Stres: hot path dogrulamasi — binlerce bagimsiz hareketli sprite.
    sealed class StressOrb : Component
    {
        public int Seed;

        protected internal override void Update()
        {
            // Seed'den deterministik yorunge (alloc yok, rastgele nesnesi yok).
            float speed = 0.3f + (Seed % 17) * 0.09f;
            float radius = 60f + (Seed % 231);
            float phase = Seed * 2.399f; // altin aci: halkalar homojen dolsun
            float ang = Time.time * speed + phase;
            transform.localPosition = new Vec3(
                _screenW * 0.5f + MathF.Cos(ang) * radius,
                _screenH * 0.5f + MathF.Sin(ang) * radius, 0f);
            transform.localEulerAngles = new Vec3(0f, 0f, ang * 57.29578f);
        }
    }

    static void SpawnStress(int count)
    {
        var tex = _assets.LoadTexture("orb.png");
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("stress");
            var sr = go.AddComponent<SpriteRenderer>();
            sr.Texture = tex;
            sr.Width = 14f;
            sr.Height = 14f;
            go.AddComponent<StressOrb>().Seed = _stressSeed++;
        }
    }

    static int _stressSeed;

    // "Kontroller" paneli: dock'ta da detached pencerede de ayni func calisir.
    static readonly char[] _nameBuf = new char[64];
    static int _nameLen = InitText(_nameBuf, "Deneme yazisi");

    static int InitText(char[] buf, string s)
    {
        s.CopyTo(0, buf, 0, s.Length);
        return s.Length;
    }

    static Vec2 _ctrlScroll;

    static void ControlsPanel(int panelId)
    {
        // Icerik panel yuksekligini asiyor: scrollview'e sarilir (layout rect'leri
        // clip offset'iyle kayar, mouse lokal uzaya cevrilir).
        var vis = GuiClip.VisibleRect;
        _ctrlScroll = Gui.BeginScrollView(new Rect(0, 0, vis.width, vis.height), _ctrlScroll,
            new Rect(0, 0, vis.width - 20, 500));
        Gui.LayoutLabel("Isim");
        Gui.LayoutTextField(_nameBuf, ref _nameLen);
        if (Gui.LayoutButton("Reset"))
            _speed = 1f;
        if (Gui.LayoutButton("Speed +"))
            _speed += 0.5f;
        _blur = Gui.LayoutToggle(_blur);
        Gui.LayoutLabel("Hiz");
        _speed = Gui.LayoutHorizontalSlider(_speed, 0f, 4f);
        Gui.LayoutLabel("Sahne (yaml)");
        if (Gui.LayoutButton("Kaydet"))
            SceneDoc.Capture(Scene.Active, _catalog).Save(ScenePath);
        if (Gui.LayoutButton("Yeniden Yukle"))
            ReloadScene();
        if (Gui.LayoutButton("Klonla") && Scene.Active.RootCount > 0)
        {
            var clone = GameObject.Instantiate(Scene.Active.GetRoot(0));
            var sp = clone.GetComponent<Spinner>();
            if (sp != null)
                sp.Index = 8 + Scene.Active.RootCount % 8; // farkli aciya dussun
        }
        if (Gui.LayoutButton("Stres +5000"))
            SpawnStress(5000);
        if (Gui.LayoutButton("Prefab Kaydet") && Scene.Active.RootCount > 0)
            SceneDoc.CaptureSubtree(Scene.Active.GetRoot(0), _catalog)
                .Save(System.IO.Path.Combine(AssetsPath, "orb.prefab"));
        if (Gui.LayoutButton("Prefab Spawn"))
        {
            var go = _assets.LoadPrefab("orb.prefab")?.Instantiate();
            var sp = go?.GetComponent<Spinner>();
            if (sp != null)
                sp.Index = Scene.Active.RootCount; // farkli aciya dussun
        }
        Gui.EndScrollView();
    }

    // Ctrl+S akisinin cekirdegi: doc'tan taze sahne, eskisi tamamen yikilir.
    static void ReloadScene()
    {
        if (!System.IO.File.Exists(ScenePath))
            return;
        var doc = SceneDoc.Load(ScenePath);
        var old = Scene.Active;
        var fresh = Scene.Create(doc.Name);
        Scene.SetActive(fresh);
        doc.InstantiateInto(fresh, _catalog, _assets);
        Scene.Unload(old);
    }

    // bin/Debug/netX.Y altindan proje kokune: elle duzenlenebilir authored dosya.
    static string ScenePath => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "scene.yaml"));

    static string AssetsPath => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "assets"));

    static TypeCatalog _catalog;
    static AssetDatabase _assets;

    static void ListPanel(int panelId)
    {
        var vis = GuiClip.VisibleRect;
        var pos = new Rect(0, 0, vis.width, vis.height);
        var view = new Rect(0, 0, 200, 12 * 40);
        _scroll = Gui.BeginScrollView(pos, _scroll, view);
        for (int i = 0; i < 12; i++)
            Gui.Box(new Rect(4, i * 40, pos.width - 30, 32));
        Gui.EndScrollView();
    }

    static void ExtraPanel(int panelId)
    {
        Gui.LayoutLabel("Ekstra ayarlar");
        _panelValue = Gui.LayoutHorizontalSlider(_panelValue, 0f, 10f);
        if (Gui.LayoutButton("Blur ac/kapa"))
            _blur = !_blur;
        _speed = Gui.LayoutHorizontalSlider(_speed, 0f, 4f);
    }

    // Tek eksenli 5-tap gaussian (texel ofsetleri shader'a gomulu; fragment DSL'de

    // Tek eksenli 5-tap gaussian (texel ofsetleri shader'a gomulu; fragment DSL'de
    // uniform desteklenmedigi icin RT boyutu kurulumda bake edilir).
    static Shader MakeBlurShader(float texelX, float texelY)
    {
        string o1x = F(1.3846154f * texelX), o1y = F(1.3846154f * texelY);
        string o2x = F(3.2307692f * texelX), o2y = F(3.2307692f * texelY);
        return Shader.CreateEffect(
            "VEC4 fs_main(VEC2 uv, VEC4 color) {\n" +
            "  VEC2 o1 = VEC2(" + o1x + ", " + o1y + ");\n" +
            "  VEC2 o2 = VEC2(" + o2x + ", " + o2y + ");\n" +
            "  VEC4 c = SAMPLE(tex, uv) * 0.227027;\n" +
            "  c += (SAMPLE(tex, uv + o1) + SAMPLE(tex, uv - o1)) * 0.316216;\n" +
            "  c += (SAMPLE(tex, uv + o2) + SAMPLE(tex, uv - o2)) * 0.070270;\n" +
            "  return c * color;\n}");

        static string F(float v) => v.ToString("0.0#######", System.Globalization.CultureInfo.InvariantCulture);
    }

    // 2D TRS model matrisi (kolon-major): olcek -> Z rotasyon -> oteleme.
    static Mat4 Model2D(float x, float y, float rot, float sx, float sy)
    {
        var m = default(Mat4);
        float c = MathF.Cos(rot), s = MathF.Sin(rot);
        m.m[0] = c * sx;
        m.m[1] = s * sx;
        m.m[4] = -s * sy;
        m.m[5] = c * sy;
        m.m[10] = 1f;
        m.m[12] = x;
        m.m[13] = y;
        m.m[15] = 1f;
        return m;
    }

    // Demo: yorunge + renk animasyonu (GameObject katmani yasam dongusu egzersizi).
    sealed class Spinner : Component
    {
        public int Index;
        public SpriteRenderer Target; // serilesen referans (GoRef/CompRef testi)
        SpriteRenderer _sr;

        protected internal override void Awake() => _sr = Target ?? GetComponent<SpriteRenderer>();

        protected internal override void Update()
        {
            if (_sr == null)
                return;
            float ang = Time.time * _speed + Index * (MathF.PI * 2f / 8f);
            transform.localPosition = new Vec3(
                _screenW * 0.5f + MathF.Cos(ang) * 220f,
                _screenH * 0.5f + MathF.Sin(ang) * 220f, 0f);
            transform.localEulerAngles = new Vec3(0f, 0f, (Time.time * 2f + Index) * (180f / MathF.PI));
            _sr.Color = new Color(
                (byte)(128 + 127 * MathF.Sin(ang)),
                (byte)(128 + 127 * MathF.Sin(ang + 2.1f)),
                (byte)(128 + 127 * MathF.Sin(ang + 4.2f)), 255);
        }
    }
}
#endif
