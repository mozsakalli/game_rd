using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Unity Scene + SceneManager modeli: Scene bir KONTEYNER (instance), birden fazla
// sahne additive yuklenebilir; GameObject'ler AKTIF sahnede dogar ve omurlerini o
// sahnenin listelerinde surdurur. Yasam dongusu Unity ile birebir:
// Update() = pendingStart (Start) -> Update -> LateUpdate -> FlushDestroyed.
// Performans: listeler duz Component[] (yalniz override edenler girer), cikarma
// O(1) null-delik (iterasyon guvenli), frame sonunda kompaksiyon. Alloc yok.
public sealed class Scene
{
    public string Name;

    // Tip metaverisi (clone/serialize icin) — statik degil: reload'da taze katalogla kurulur.
    public TypeCatalog Catalog;

    // AMBIENT DURUM SAHNEYE AITTIR; Time/Screen statikleri sadece pencere:
    // Update girisinde bu sahnenin degerleri basilir. Component kodu Time.time/
    // Screen.width okur, icinde kostugu sahnenin baglamini gorur (preview'in temeli).
    public float SceneTime { get; private set; }
    public float ScreenWidth, ScreenHeight;
    // true: UpdateAll atlar — sahibi (preview paneli gibi) kendi saatiyle elle Update surer.
    public bool ExternallyDriven;

    // Sahneye ait tween havuzu: sahne saatiyle akar, sahneyle olur.
    public readonly TweenPool Tweens = new();

    // --- SceneManager (statik yonetim) ---
    static readonly List<Scene> _loaded = new();
    static Scene _active;

    // Ilk erisimde varsayilan sahne kurulur (tek sahneli kullanim sifir tören).
    public static Scene Active => _active ??= Create("Main");
    public static IReadOnlyList<Scene> Loaded => _loaded;

    public static Scene Create(string name)
    {
        var s = new Scene { Name = name };
        _loaded.Add(s);
        _active ??= s;
        return s;
    }

    public static void SetActive(Scene scene) => _active = scene;

    // simulate=false (editor edit modu): saat/Update/tween DURUR ama lifecycle
    // (Start/destroy/compaction) kosar — editor operasyonlari tutarli kalir.
    public static void UpdateAll(float dt, float screenW, float screenH, bool simulate = true)
    {
        Time.frameCount++;
        for (int i = 0; i < _loaded.Count; i++)
        {
            var s = _loaded[i];
            if (s.ExternallyDriven)
                continue; // preview gibi sahneler kendi saatiyle surulur
            s.ScreenWidth = screenW;
            s.ScreenHeight = screenH;
            s.Update(dt, simulate);
        }
        Resources.Tick(); // planlanmis (ertelenmis) kaynak toplama
    }

    // Sahneyi kaldir: tum kok GO'lar ANINDA yok edilir (OnDisable/OnDestroy kosar).
    public static void Unload(Scene scene)
    {
        while (scene._rootCount > 0)
        {
            var go = scene._roots[scene._rootCount - 1];
            scene.DestroyGameObjectNow(go);
            scene.RemoveRoot(go);
        }
        _loaded.Remove(scene);
        if (_active == scene)
            _active = _loaded.Count > 0 ? _loaded[0] : null;
        // Birkac frame sonra topla: kalan sahnelerin taze damgalari otursun.
        Resources.ScheduleCollect(3);
    }

    Scene() { }

    // --- Kok GO listesi (hiyerarsi paneli / unload icin) ---
    GameObject[] _roots = new GameObject[64];
    int _rootCount;

    public int RootCount => _rootCount;
    public GameObject GetRoot(int index) => _roots[index];

    internal void AddRoot(GameObject go)
    {
        if (go._rootIndex >= 0)
            return;
        if (_rootCount == _roots.Length) Array.Resize(ref _roots, _rootCount * 2);
        _roots[_rootCount] = go;
        go._rootIndex = _rootCount++;
    }

    internal void RemoveRoot(GameObject go)
    {
        int i = go._rootIndex;
        if (i < 0)
            return;
        go._rootIndex = -1;
        var last = _roots[--_rootCount];
        _roots[_rootCount] = null;
        if (last != go)
        {
            _roots[i] = last; // swap-remove: kok sirasi onemsiz
            last._rootIndex = i;
        }
    }

    // --- Dispatch listeleri (instance) ---
    Component[] _update = new Component[256];
    int _updateCount, _updateHoles;

    Component[] _lateUpdate = new Component[64];
    int _lateUpdateCount, _lateUpdateHoles;

    Component[] _pendingStart = new Component[64];
    int _pendingStartCount;

    Renderer[] _renderers = new Renderer[128];
    int _rendererCount, _rendererHoles;

    Component[] _destroyComps = new Component[32];
    int _destroyCompCount;
    GameObject[] _destroyGos = new GameObject[32];
    int _destroyGoCount;

    // --- Sahne sistemleri (ISceneSystem): custom isler (layout vb.) core'a ---
    // --- sizmadan faz kancalarina takilir. Kayit sirasi = cagri sirasi.     ---
    ISceneSystem[] _systems = new ISceneSystem[4];
    int _systemCount;

    // GPU guncelleme gate'i: Update'te artar — sistemler frame'de bir kez buffer
    // yazar, sonraki kameralar yalniz re-encode eder.
    internal int _frameStamp;

    public T GetSystem<T>() where T : class, ISceneSystem, new()
    {
        for (int i = 0; i < _systemCount; i++)
            if (_systems[i] is T t)
                return t;
        var sys = new T();
        if (_systemCount == _systems.Length) Array.Resize(ref _systems, _systemCount * 2);
        _systems[_systemCount++] = sys;
        return sys;
    }

    // GO aktif oldu ya da aktif GO'ya component eklendi: Awake (bir kez,
    // enabled'dan bagimsiz) + enabled ise OnEnable zinciri.
    internal void ActivateComponent(Component c)
    {
        if (c._destroyed)
            return;
        if (!c._awakeCalled)
        {
            c._awakeCalled = true;
            if ((c._flags & LifecycleFlags.Awake) != 0)
                c.Awake();
        }
        if (c._enabled)
            EnableComponent(c);
    }

    internal void EnableComponent(Component c)
    {
        if (c._destroyed || c._enableCalled)
            return;
        c._enableCalled = true;

        if ((c._flags & LifecycleFlags.Update) != 0 && c._updateSlot < 0)
        {
            if (_updateCount == _update.Length) Array.Resize(ref _update, _updateCount * 2);
            _update[_updateCount] = c;
            c._updateSlot = _updateCount++;
        }
        if ((c._flags & LifecycleFlags.LateUpdate) != 0 && c._lateUpdateSlot < 0)
        {
            if (_lateUpdateCount == _lateUpdate.Length) Array.Resize(ref _lateUpdate, _lateUpdateCount * 2);
            _lateUpdate[_lateUpdateCount] = c;
            c._lateUpdateSlot = _lateUpdateCount++;
        }
        if (c is Renderer r && r._rendererSlot < 0)
        {
            if (_rendererCount == _renderers.Length) Array.Resize(ref _renderers, _rendererCount * 2);
            _renderers[_rendererCount] = r;
            r._rendererSlot = _rendererCount++;
        }
        if (!c._started && !c._startQueued && (c._flags & LifecycleFlags.Start) != 0)
        {
            c._startQueued = true;
            if (_pendingStartCount == _pendingStart.Length) Array.Resize(ref _pendingStart, _pendingStartCount * 2);
            _pendingStart[_pendingStartCount++] = c;
        }

        if ((c._flags & LifecycleFlags.OnEnable) != 0)
            c.OnEnable(); // en son: kendini disable ederse listelerden duser
    }

    internal void DisableComponent(Component c)
    {
        if (!c._enableCalled)
            return;
        c._enableCalled = false;

        if (c._updateSlot >= 0)
        {
            _update[c._updateSlot] = null; // delik: iterasyon guvenli O(1) cikarma
            _updateHoles++;
            c._updateSlot = -1;
        }
        if (c._lateUpdateSlot >= 0)
        {
            _lateUpdate[c._lateUpdateSlot] = null;
            _lateUpdateHoles++;
            c._lateUpdateSlot = -1;
        }
        if (c is Renderer r && r._rendererSlot >= 0)
        {
            _renderers[r._rendererSlot] = null;
            _rendererHoles++;
            r._rendererSlot = -1;
        }

        if ((c._flags & LifecycleFlags.OnDisable) != 0)
            c.OnDisable();
    }

    internal void QueueDestroy(GameObject go)
    {
        if (go == null || go._destroyed || go._destroyQueued)
            return;
        go._destroyQueued = true;
        if (_destroyGoCount == _destroyGos.Length) Array.Resize(ref _destroyGos, _destroyGoCount * 2);
        _destroyGos[_destroyGoCount++] = go;
    }

    internal void QueueDestroy(Component c)
    {
        if (c == null || c._destroyed)
            return;
        if (c is Transform)
            return; // Unity: Transform tek basina yok edilemez
        c._destroyed = true; // isActiveAndEnabled aninda false; asil is frame sonunda
        if (_destroyCompCount == _destroyComps.Length) Array.Resize(ref _destroyComps, _destroyCompCount * 2);
        _destroyComps[_destroyCompCount++] = c;
    }

    // Frame surucusu: UpdateAll tum yuklu sahneler icin cagirir; ExternallyDriven
    // sahneleri sahibi surer. Girişte ambient pencereler bu sahneye cevrilir.
    public void Update(float dt, bool simulate = true)
    {
        if (simulate)
            SceneTime += dt;
        Time.time = SceneTime;
        Time.deltaTime = simulate ? dt : 0f;
        Screen.width = ScreenWidth;
        Screen.height = ScreenHeight;
        // Start: ilk Update'ten once. Start icinde enable edilenler ayni turda islenir
        // (dongu siniri her adimda yeniden okunur).
        for (int i = 0; i < _pendingStartCount; i++)
        {
            var c = _pendingStart[i];
            _pendingStart[i] = null;
            c._startQueued = false;
            if (c._destroyed || c._started || !c.isActiveAndEnabled)
                continue;
            c._started = true;
            c.Start();
        }
        _pendingStartCount = 0;

        if (simulate)
        {
            // Update: sinir baslangicta sabitlenir — frame icinde enable edilenler
            // sonraki frame'de baslar (Unity davranisi).
            int n = _updateCount;
            for (int i = 0; i < n; i++)
            {
                var c = _update[i];
                if (c != null && !c._destroyed)
                    c.Update();
            }
            Tweens.Tick(dt); // Update'ten sonra, LateUpdate'ten once (kamera takibi tween'i gorsun)
            n = _lateUpdateCount;
            for (int i = 0; i < n; i++)
            {
                var c = _lateUpdate[i];
                if (c != null && !c._destroyed)
                    c.LateUpdate();
            }
        }

        // Sistem sweep'leri (layout vb.) render'dan once — edit modunda da kosar.
        for (int i = 0; i < _systemCount; i++)
            _systems[i].AfterUpdate(this);
        _frameStamp++;

        CompactUpdate();
        CompactLateUpdate();
        CompactRenderers();
        FlushDestroyed();
    }

    // Aktif renderer'lari kuyruga encode eder (kamera-bagimsiz; cagiran secer).
    public void Render(RenderQueue queue)
    {
        for (int i = 0; i < _systemCount; i++)
            _systems[i].BeginRender(this, queue);
        int n = _rendererCount;
        for (int i = 0; i < n; i++)
        {
            var r = _renderers[i];
            if (r != null && !r._destroyed)
                r.Encode(queue);
        }
        for (int i = 0; i < _systemCount; i++)
            _systems[i].EndRender(this, queue);
    }

    void FlushDestroyed()
    {
        for (int i = 0; i < _destroyCompCount; i++)
        {
            var c = _destroyComps[i];
            _destroyComps[i] = null;
            DestroyComponentNow(c);
        }
        _destroyCompCount = 0;

        for (int i = 0; i < _destroyGoCount; i++)
        {
            var go = _destroyGos[i];
            _destroyGos[i] = null;
            DestroyGameObjectNow(go); // once mark + OnDisable/OnDestroy (alt agac dahil)
            RemoveRoot(go);
            go.transform.SetParent(null, false); // sonra kopar (destroyed guard aktivasyonu keser)
        }
        _destroyGoCount = 0;
    }

    void DestroyComponentNow(Component c)
    {
        DisableComponent(c);
        if (c._awakeCalled && (c._flags & LifecycleFlags.OnDestroy) != 0)
            c.OnDestroy();
        c._gameObject?.RemoveComponent(c);
    }

    // Alt agac dahil: cocuklar once (Unity gibi asagidan yukari OnDestroy).
    void DestroyGameObjectNow(GameObject go)
    {
        if (go._destroyed)
            return;
        for (var t = go.transform.FirstChild; t != null;)
        {
            var next = t.NextSibling;
            if (t._gameObject != null)
                DestroyGameObjectNow(t._gameObject);
            t = next;
        }
        int n = go.ComponentCount;
        for (int i = 0; i < n; i++)
        {
            var c = go.ComponentAt(i);
            DisableComponent(c);
            if (c._awakeCalled && (c._flags & LifecycleFlags.OnDestroy) != 0)
                c.OnDestroy();
            c._destroyed = true;
        }
        go.MarkDestroyed();
    }

    void CompactUpdate()
    {
        if (_updateHoles == 0)
            return;
        int w = 0;
        for (int i = 0; i < _updateCount; i++)
        {
            var c = _update[i];
            if (c == null)
                continue;
            _update[w] = c;
            c._updateSlot = w++;
        }
        for (int i = w; i < _updateCount; i++)
            _update[i] = null;
        _updateCount = w;
        _updateHoles = 0;
    }

    void CompactLateUpdate()
    {
        if (_lateUpdateHoles == 0)
            return;
        int w = 0;
        for (int i = 0; i < _lateUpdateCount; i++)
        {
            var c = _lateUpdate[i];
            if (c == null)
                continue;
            _lateUpdate[w] = c;
            c._lateUpdateSlot = w++;
        }
        for (int i = w; i < _lateUpdateCount; i++)
            _lateUpdate[i] = null;
        _lateUpdateCount = w;
        _lateUpdateHoles = 0;
    }

    void CompactRenderers()
    {
        if (_rendererHoles == 0)
            return;
        int w = 0;
        for (int i = 0; i < _rendererCount; i++)
        {
            var r = _renderers[i];
            if (r == null)
                continue;
            _renderers[w] = r;
            r._rendererSlot = w++;
        }
        for (int i = w; i < _rendererCount; i++)
            _renderers[i] = null;
        _rendererCount = w;
        _rendererHoles = 0;
    }
}
