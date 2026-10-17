using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

public enum PlayState { Editing, Playing, Paused }

public enum HotReloadMode { Off, OnSave, Instant }

// AYRIK SAHNE modeli: edit sahnesi (doc projeksiyonu) HEP yasar ve HIC simule
// olmaz (Scene View onu gosterir); Play doc'tan BAGIMSIZ bir oyun sahnesi dogurur
// (Game View onu gosterir). Stop oyun sahnesini imha eder — edit sahnesi hic
// kirlenmedigi icin restore gerekmez. Hot reload: oyun sahnesi doc'tan yeniden
// dogar (taze Start, akis kesilmez) — Kapali/Kaydette/Aninda secilebilir.
static class PlayMode
{
    public static PlayState State { get; private set; } = PlayState.Editing;

    // Editorun cizdigi/simule ettigi OYUN sahnesi. Doc'tan dogan sahne (_spawned) oyun kodu tarafindan
    // SceneLoader replace ile atilmis olabilir (Scene.Unload → Loaded'dan duser); o zaman oyunun yeni aktif
    // sahnesi izlenir — aksi halde GameView bos (eski) sahneye bakar kalirdi. Edit live sahnesi asla oyun
    // sahnesi sayilmaz (Unload sonrasi Scene.Active _loaded[0]'a, yani edit sahnesine dusebilir).
    public static Scene PlayScene
    {
        get
        {
            if (_spawned == null)
                return null;
            if (IsLoaded(_spawned))
                return _spawned;
            var a = Scene.Active;
            return a != null && a != App.EditScene.LiveScene && !a.ExternallyDriven ? a : null;
        }
    }
    static Scene _spawned;

    static bool IsLoaded(Scene s)
    {
        var l = Scene.Loaded;
        for (int i = 0; i < l.Count; i++)
            if (ReferenceEquals(l[i], s)) return true;
        return false;
    }
    public static bool Simulate => State == PlayState.Playing && PlayScene != null;

    public static HotReloadMode HotReload = HotReloadMode.OnSave;

    // RPC sim (sim.run/input.*): oyun sahnesi gercek saat yerine SABIT dt ile ilerler → kare numarasi
    // deterministik (PNG yazimi, arka plan throttling, derleme dt'yi sisirmez). 0 = gercek saat.
    public static float ForcedDt;

    public static void Play()
    {
        if (State == PlayState.Paused)
        {
            State = PlayState.Playing;
            return;
        }
        if (State == PlayState.Playing)
            return;
        InspectorPanel.FlushPendingSettings(); // bekleyen importer ayarlari oyuna taze girsin
        if (App.InPrefabStage)
        {
            EditorLog.Warning("[prefab] close the prefab stage (Back) before Play");
            return;
        }
        LocTableStore.Flush(); // oyun sahnesi tabloyu dosyadan/pak'tan degil canli instance'tan okur; yine de tutarli dursun
        SpawnPlayScene();
        State = PlayState.Playing;
        EditorWindow.GetWindow<GameViewPanel>(); // Game tab'i one
    }

    public static void Pause()
    {
        if (State == PlayState.Playing)
            State = PlayState.Paused;
        else if (State == PlayState.Paused)
            State = PlayState.Playing;
    }

    public static void Stop()
    {
        if (State == PlayState.Editing)
            return;
        State = PlayState.Editing;
        DestroyPlayScene();
        Plugin.ResetAll(); // plugin instance'lari Play omurlu (bekleyen Task'lar GC'ye)
        PlayerPrefs.Save(); // oyun kodunun kaydetmedigi prefs Stop'ta diske (release'teki Shutdown muadili)
        EditorWindow.GetWindow<SceneViewPanel>(); // Scene tab'i one
    }

    // Oyun sahnesi doc'tan yeniden dogar; Play akisi kesilmez.
    public static void DoHotReload()
    {
        if (PlayScene == null)
            return;
        DestroyPlayScene();
        SpawnPlayScene();
        System.Console.WriteLine("[play] hot reload");
    }

    static void SpawnPlayScene()
    {
        var doc = App.EditScene.Doc;
        var s = Scene.Create(doc.Name + " (Oyun)");
        doc.InstantiateInto(s, App.Catalog, App.Assets); // aktif sahne = oyun
        // SceneLoader paritesi: spawn'in suresi (instantiate + doku yukleme) bir sonraki frame'in
        // dt'sine biner → MovieClip/Tween ilk gorunur karede zaten N kare ilerlemis olur
        // ("fade 0'dan baslamiyor"). Ilk iki dt atilir; editor dt'si ayrica MaxDeltaTime ile sinirli.
        s.DiscardDeltaFrames = 2;
        _spawned = s;
        SpawnedDocVersion = App.EditScene.History.Version;
    }

    // Oyun sahnesinin dogdugu andaki doc surumu: farkliysa play sahnesi BAYAT (doc degisti, HR kosmadi).
    // Oyun kodu kendi sahnesine gecmisse (replace) doc ile bagi kalmadi → bayat sayilmaz.
    public static int SpawnedDocVersion { get; private set; }
    public static bool PlaySceneStale => _spawned != null && IsLoaded(_spawned)
        && App.EditScene.History.Version != SpawnedDocVersion;

    static void DestroyPlayScene()
    {
        if (_spawned == null)
            return;
        if (IsLoaded(_spawned))
            Scene.Unload(_spawned);
        // Oyun kodunun SceneLoader ile yarattigi sahneler de (replace modu PlayScene'i atmis olabilir).
        for (int i = Scene.Loaded.Count - 1; i >= 0; i--)
        {
            var s = Scene.Loaded[i];
            if (!s.ExternallyDriven && s != App.EditScene.LiveScene)
                Scene.Unload(s);
        }
        _spawned = null;
        Scene.SetActive(App.EditScene.LiveScene);
    }
}
