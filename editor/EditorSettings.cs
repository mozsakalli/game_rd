using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Kullanici-bazli editor ayarlari (UserSettings/ — gitignore'lanir). Standart:
// ayar = [Serializable] sinif + ObjectSerializer; tek istisna dock agaci (Layout.txt,
// ozyinelemeli yapi kendi kompakt formatinda kalir).
[Serializable]
public class EditorSettings
{
    public string lastScene = "";  // proje kokune goreli; bos = acik sahne yok
    public HotReloadMode hotReload = HotReloadMode.OnSave;
    public float uiFontSize = 16f; // editor UI metin boyutu (mantiksal pt)
    // Scene View kamerasi (dunya merkezi + zoom); zoom <= 0 = kaydedilmemis.
    public float sceneCamX = 400f, sceneCamY = 300f, sceneCamZoom = 0f;
    // Game View cihaz profili (fiziksel px + pixelRatio); deviceW=0 = panel takibi.
    public string deviceName = "";
    public int deviceW, deviceH;
    public float devicePixelRatio;
    // Game View bar tercihleri: yon zorlamasi (Auto = PlayerSettings) + 1:1 gosterim.
    public ScreenOrientation gameViewOrientation = ScreenOrientation.Auto;
    public bool gameViewOneToOne;
    // Toolbar "Working language" (Localize.Language; edit projeksiyonu + Play). Bos = tablonun kaynak dili.
    public string workingLanguage = "";
    // Toolbar "Working theme" (SceneDoc.ActiveTheme; edit projeksiyonu + Play). Bos = taban (default). Oyun basina (games[]).
    public string workingTheme = "";
    // Calisilan oyun (docs/plugins.md "Games"): ProjectSettings/Games/<ad>. Play + Player Build + pak bununla.
    // `--game <ad>` ile baslatilan surecte sabittir ve buraya YAZILMAZ (ayni projede ikinci editor baska oyunda).
    public string workingGame = "";
    // Oyun basina editor durumu: oyun degisince ustteki canli alanlar (lastScene, workingTheme, kamera, cihaz) buraya
    // saklanir, hedef oyununki geri gelir — oyun degistirmek = calisma alani degistirmek.
    public List<GameEditorState> games = new();
    // Project paneli gorunumu: liste / izgara + izgara hucre boyutu (mantiksal px).
    public bool projectGrid;
    public int projectCell = 96;
    // Player Settings penceresi: son secili sol kategori ("General", "Android", "Plugin:Facebook"...).
    public string playerSettingsPage = "";
    // Player Settings penceresi: duzenlenen oyun sekmesi (bos = calisilan oyun).
    public string playerSettingsGame = "";
    // Window/Perf HUD: Game View sol ustunde frame ms / alloc / GO / image havuzu (varsayilan kapali).
    public bool perfHud;

    public GameEditorState StateFor(string game, bool create)
    {
        foreach (var g in games) if (string.Equals(g.name, game, StringComparison.OrdinalIgnoreCase)) return g;
        if (!create) return null;
        var n = new GameEditorState { name = game };
        games.Add(n);
        return n;
    }

    // Canli alanlar -> oyunun kaydi.
    public void StashGame(string game)
    {
        if (string.IsNullOrEmpty(game)) return;
        var g = StateFor(game, true);
        g.lastScene = lastScene; g.workingTheme = workingTheme;
        g.sceneCamX = sceneCamX; g.sceneCamY = sceneCamY; g.sceneCamZoom = sceneCamZoom;
        g.deviceName = deviceName; g.deviceW = deviceW; g.deviceH = deviceH; g.devicePixelRatio = devicePixelRatio;
        g.gameViewOrientation = gameViewOrientation;
    }

    // Oyunun kaydi -> canli alanlar (kayit yoksa sahne/tema sifirlanir, cihaz/kamera kalir).
    public void RestoreGame(string game)
    {
        var g = StateFor(game, false);
        if (g == null) { lastScene = ""; workingTheme = ""; return; }
        lastScene = g.lastScene; workingTheme = g.workingTheme;
        sceneCamX = g.sceneCamX; sceneCamY = g.sceneCamY; sceneCamZoom = g.sceneCamZoom;
        deviceName = g.deviceName; deviceW = g.deviceW; deviceH = g.deviceH; devicePixelRatio = g.devicePixelRatio;
        gameViewOrientation = g.gameViewOrientation;
    }
}

[Serializable]
public class GameEditorState
{
    public string name = "";
    public string lastScene = "";
    public string workingTheme = "";
    public float sceneCamX = 400f, sceneCamY = 300f, sceneCamZoom = 0f;
    public string deviceName = "";
    public int deviceW, deviceH;
    public float devicePixelRatio;
    public ScreenOrientation gameViewOrientation = ScreenOrientation.Auto;
}
