namespace DigitoyEditor;

// Yerlesik menu ogeleri. Kullanici/oyun kodu da ayni desenle ekler:
// [MenuItem("Menu/Alt/Oge")] static void Foo() { ... }
static class BuiltinMenus
{
    [MenuItem("File/Save Scene", 0)]
    static void SaveScene()
    {
        App.EditScene.Save();
        if (PlayMode.HotReload != HotReloadMode.Off)
            PlayMode.DoHotReload(); // Play'deyse oyun doc'tan taze dogar (akis kesilmez)
    }

    [MenuItem("File/Reload Scene", 1)]
    static void ReloadScene() => App.ReloadScene();

    [MenuItem("Edit/Undo", 0)]
    static void Undo() => App.EditScene.DoUndo();

    [MenuItem("Edit/Redo", 1)]
    static void Redo() => App.EditScene.DoRedo();

    [MenuItem("Project/Compile+Load Code", 0)]
    static void ReloadCode() => AssetWatcher.RequestBuild(); // arka planda derler, ana thread swap eder

    [MenuItem("Run/Play", 0)]
    static void Play() => PlayMode.Play();

    [MenuItem("Run/Pause", 1)]
    static void Pause() => PlayMode.Pause();

    [MenuItem("Run/Stop", 2)]
    static void Stop() => PlayMode.Stop();

    [MenuItem("Run/Hot Reload Mode", 3)]
    static void CycleHotReload()
    {
        PlayMode.HotReload = (HotReloadMode)(((int)PlayMode.HotReload + 1) % 3);
        App.SaveSettings();
        System.Console.WriteLine($"[play] hot reload mode: {PlayMode.HotReload}");
    }
}
