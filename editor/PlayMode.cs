using DigitoyEngine;

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

    public static void Play()
    {
        if (State == PlayState.Paused)
        {
            State = PlayState.Playing;
            return;
        }
        if (State == PlayState.Playing)
            return;
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
        PlayScene = s;
    }

    static void DestroyPlayScene()
    {
        if (PlayScene == null)
            return;
        Scene.Unload(PlayScene);
        PlayScene = null;
        Scene.SetActive(App.EditScene.LiveScene);
    }
}
