using System;
using System.Collections.Generic;

namespace DigitoyEngine;

public enum ScreenOrientation { Landscape, Portrait, Auto }

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

    // Mobil ekran yonu (Android manifest / iOS Info.plist; proje kabugu ilk uretimde yazilir).
    // Editor Game View cihaz profili de buna gore doner (view.device).
    public ScreenOrientation orientation = ScreenOrientation.Landscape;

    // Dinamik modul (Build > Module): bu proje bir host oyuna karsi modul olarak publish ediliyorsa, host'un AOT
    // player assembly'si (player/bin/Release/net9.0/DigitoyPlayer.dll gibi) — host tipleri "provided" sayilir,
    // modul kodu onlara isimle baglanir. Bos = yalniz engine API'si.
    public string moduleHostDll = "";

    // Dinamik modul HOST'u mu (docs/engine-notes.md TRIM)? Kapali (varsayilan) = lean build: interpreter (vmint) derlenmez,
    // reflection meta yalniz kok kumeye (pak component tipleri + alanlari + [Preserve]) -> kucuk ikili. Acik = tam meta + vmint.
    public bool moduleHost;

    // Android'e ozel yayin ayarlari (Build Android Project -> generated/app.properties; kabuk Gradle her build okur).
    public AndroidSettings android = new();

    // iOS'a ozel yayin ayarlari (Build iOS Project -> generated/app.xcconfig; Xcode kabugu her build okur).
    public IosSettings ios = new();

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

// Android yayin ayarlari (PlayerSettings.android; git'e girer). SIFRELER BURADA DEGIL: keystore/key parolasi
// editor UserSettings/AndroidSigning.asset'te (gitignore'lu) ya da DE_ANDROID_KEYSTORE_PASS / DE_ANDROID_KEY_PASS
// ortam degiskenlerinde; build sirasinda Build/android/keystore.properties'e yazilir (Gradle konvansiyonu, gitignore'lu).
[Serializable]
public class AndroidSettings
{
    public string packageName = "";        // bos = bundleIdentifier (applicationId)
    public int versionCode = 1;            // Play Store her yuklemede artis ister (versionName = PlayerSettings.version)
    public int targetSdk = 35;
    public bool includeX86_64 = true;      // emulator ABI'si; kapali = yalniz arm64-v8a (kucuk APK)
    public string keystorePath = "";       // proje kokune goreli ya da mutlak .keystore/.jks; bos = debug imza
    public string keyAlias = "";
    public string iconForeground = "";     // Assets'e goreli PNG (adaptive icon on plani, 432x432 onerilir); bos = engine varsayilani
    public string iconBackground = "#1E1E1E"; // "#RRGGBB" renk ya da Assets'e goreli PNG
}

// iOS yayin ayarlari (PlayerSettings.ios; git'e girer). Imza/sertifika burada DEGIL: Xcode "Signing & Capabilities"
// (Automatic; teamId verilirse xcconfig'e DEVELOPMENT_TEAM yazilir, ilk acilista secmek gerekmez).
[Serializable]
public class IosSettings
{
    public string bundleId = "";           // bos = bundleIdentifier
    public int buildNumber = 1;            // CFBundleVersion; App Store her yuklemede artis ister (CFBundleShortVersionString = PlayerSettings.version)
    public string minIos = "15.0";         // IPHONEOS_DEPLOYMENT_TARGET
    public string teamId = "";             // Apple Developer Team ID (10 karakter); bos = Xcode'da secilir
    public string icon = "";               // Assets'e goreli PNG (1024x1024, opak, kose yuvarlatma YOK); bos = engine varsayilani
}
