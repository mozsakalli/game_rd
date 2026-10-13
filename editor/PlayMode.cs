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
    public static Scene PlayScene { get; private set; }
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
        if (App.InPrefabStage)
        {
            EditorLog.Warning("[prefab] close the prefab stage (Back) before Play");
            return;
        }
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
        PlayScene = s;
        SpawnedDocVersion = App.EditScene.History.Version;
    }

    // Oyun sahnesinin dogdugu andaki doc surumu: farkliysa play sahnesi BAYAT (doc degisti, HR kosmadi).
    public static int SpawnedDocVersion { get; private set; }
    public static bool PlaySceneStale => PlayScene != null && App.EditScene.History.Version != SpawnedDocVersion;

    static void DestroyPlayScene()
    {
        if (PlayScene == null)
            return;
        Scene.Unload(PlayScene);
        // Oyun kodunun SceneLoader ile yarattigi sahneler de (replace modu PlayScene'i atmis olabilir).
        for (int i = Scene.Loaded.Count - 1; i >= 0; i--)
        {
            var s = Scene.Loaded[i];
            if (!s.ExternallyDriven)
                Scene.Unload(s);
        }
        PlayScene = null;
        Scene.SetActive(App.EditScene.LiveScene);
    }
}
