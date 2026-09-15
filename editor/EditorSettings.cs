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
}
