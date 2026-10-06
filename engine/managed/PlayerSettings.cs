using System;
using System.Collections.Generic;

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

    // Unity Build Settings > Scenes In Build karsiligi: pak'a girecek sahneler (Assets'e
    // goreli). Pak builder yalniz bu sahnelerden erisilen asset'leri paketler; listede
    // olmayan sahne ve ona ozel icerik build'e girmez. startScene listede olmak zorundadir
    // (pencere zorlar; builder eksikse uyarip ekler). Ikisi de ozel bolumle cizilir.
    [HideInInspector] public List<string> scenes = new();
    [HideInInspector] public string startScene = "Scenes/Main.scene"; // Assets'e goreli ilk sahne

    // Masaustu pencere varsayilanlari (mantiksal/point birim).
    public int defaultWidth = 1280;
    public int defaultHeight = 720;
    public bool fullscreen;
    public bool resizableWindow = true;

    // 0 = platform varsayilani (vsync).
    public int targetFrameRate;

    // Build kokleri: scenes + startScene (tekil, sira korunur, startScene listede yoksa basa).
    public List<string> BuildScenes()
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(startScene))
            list.Add(startScene.Trim());
        foreach (var s in scenes)
            if (!string.IsNullOrWhiteSpace(s) && !list.Contains(s.Trim()))
                list.Add(s.Trim());
        return list;
    }
}
