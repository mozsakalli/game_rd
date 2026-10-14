using System;
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
}
