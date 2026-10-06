using System;

namespace DigitoyEngine;

// Unity PlayerSettings karsiligi: oyunun YAYIN kimligi ve calisma zamani varsayilanlari.
// Proje-genis tek kayit: ProjectSettings/PlayerSettings.asset (git'e girer; kullanici
// ayarlari UserSettings/EditorSettings'ten ayridir). Standart: [Serializable] saf veri
// + ObjectSerializer; editor Project > Player Settings penceresiyle duzenlenir.
// Release'te YAML okunmaz: AssetPackBuilder gereken alanlari pak'a piser (ProjectBinary).
[Serializable]
public class PlayerSettings
{
    public string companyName = "DefaultCompany";
    public string productName = "Game";
    public string version = "0.1.0";
    public string bundleIdentifier = "com.defaultcompany.game"; // mobil/masaustu paket kimligi

    public string startScene = "Scenes/Main.scene"; // Assets'e goreli ilk sahne

    // Masaustu pencere varsayilanlari (mantiksal/point birim).
    public int defaultWidth = 1280;
    public int defaultHeight = 720;
    public bool fullscreen;
    public bool resizableWindow = true;

    // 0 = platform varsayilani (vsync).
    public int targetFrameRate;
}
