using System;
using System.Collections.Generic;

namespace DigitoyEngine;

public enum ScreenOrientation { Landscape, Portrait, Auto }

// Oyunun giris noktasi (PlayerSettings.boot): [Serializable, Asset] sinif uygular; GameHost/editor Play, sahneden once Boot() cagirir.
// Icinde Scene.Active/GameObject KULLANMA (henuz sahne yok); servisleri kur, baglantiyi baslat, statikleri doldur. Idempotent olmali.
public interface IGameBoot
{
    void Boot();
}

// Unity PlayerSettings karsiligi: BIR OYUNUN yayin kimligi ve calisma zamani varsayilanlari.
// Proje = ortak Assets/ + N oyun (docs/plugins.md "Games"): her oyunun TAM ayar dosyasi
// ProjectSettings/Games/<ad>/PlayerSettings.asset (git'e girer; kullanici ayarlari UserSettings/EditorSettings'ten
// ayridir). Taban/overlay yok. Standart: [Serializable] saf veri + ObjectSerializer; editor Project > Player Settings
// penceresi (oyun sekmeleri) ile duzenlenir. Release'te YAML okunmaz: AssetPackBuilder gereken alanlari pak'a piser (ProjectBinary).
[Serializable]
public class PlayerSettings
{
    // [Category]: Player Settings penceresi sol listesi (General/Display/Themes/Build; Android/iOS ic nesneleri ve
    // Scenes/Games/Plugins ozel sayfalar). Serilesmeyi etkilemez.
    public string companyName = "DefaultCompany";
    public string productName = "Game";
    // Kullaniciya gorunen surum: TEK kaynak (Android versionName, iOS CFBundleShortVersionString, runtime GameHost.Version).
    public string version = "0.1.0";
    public string bundleIdentifier = "com.defaultcompany.game"; // mobil/masaustu paket kimligi

    // BUILD NUMARASI: elle alan YOK. Build aninda unix DAKIKA (int): monoton, Play Store tavani (2.1 milyar) 2000+ yil uzak,
    // iOS CFBundleVersion'a sigar; Android ve iOS AYNI sayiyi alir (tek build kimligi). Ayni dakikada iki magaza yuklemesi yapma.
    // CI/geri alma icin --build-number ile ezilir.
    public static int BuildNumberNow() => (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60);

    // Unity Build Settings > Scenes In Build karsiligi: pak'a girecek sahneler (Assets'e
    // goreli). Pak builder yalniz bu sahnelerden erisilen asset'leri paketler; listede
    // olmayan sahne ve ona ozel icerik build'e girmez. startScene listede olmak zorundadir
    // (pencere zorlar; builder eksikse uyarip ekler). Ikisi de ozel bolumle cizilir.
    [HideInInspector] public List<string> scenes = new();
    [HideInInspector] public string startScene = "Scenes/Main.scene"; // Assets'e goreli ilk sahne

    // OYUN GIRISI (docs/plugins.md "IGameBoot"): oyunun TEK ayar+baslatma nesnesi — [Serializable, Asset] bir sinif, IGameBoot uygular,
    // alanlari gomulu (NetworkSettings, GameDefinition... ayri dosya degil). GameHost pak acilip katalog kurulunca, startScene'den
    // ONCE Boot() cagirir; editor Play de acik sahneden bagimsiz ayni cagriyi yapar (Lobby'den Play = servisler hazir).
    // Sahne yok; sahneyle ilgisi yok. Bos = boot yok. Boot() idempotent olmali (tekrar Play, hot reload).
    public IGameBoot boot;

    // TEMA (docs/engine-notes.md TEMA): sahne/prefab dosyalarindaki isimli override katmanlari. themes = bu OYUNUN
    // ad listesi (serbest metin: xmas, kurban...; taban "default" listede YAZILMAZ; adlar oyunlar arasi ortak sozluk —
    // Shared prefab'taki katman adini listesine alan her oyunda etkin). activeTheme = BUILD temasi: pak tek temayla piser,
    // digerleri ve asset'leri pak'a girmez (bos = taban). Editor "Working theme" oyun basina EditorSettings'te (kullanici).
    [Category("Themes")] public List<string> themes = new();
    [Category("Themes")] public string activeTheme = "";

    // Masaustu pencere varsayilanlari (mantiksal/point birim).
    [Category("Display")] public int defaultWidth = 1280;
    [Category("Display")] public int defaultHeight = 720;
    [Category("Display")] public bool fullscreen;
    [Category("Display")] public bool resizableWindow = true;

    // 0 = platform varsayilani (vsync).
    [Category("Display")] public int targetFrameRate;

    // Mobil ekran yonu (Android manifest / iOS Info.plist; proje kabugu ilk uretimde yazilir).
    // Editor Game View cihaz profili de buna gore doner (view.device).
    [Category("Display")] public ScreenOrientation orientation = ScreenOrientation.Landscape;

    // Dinamik modul (Build > Module): bu proje bir host oyuna karsi modul olarak publish ediliyorsa, host'un AOT
    // player assembly'si (player/bin/Release/net9.0/DigitoyPlayer.dll gibi) — host tipleri "provided" sayilir,
    // modul kodu onlara isimle baglanir. Bos = yalniz engine API'si.
    [Category("Build")] public string moduleHostDll = "";

    // Kod/meta budama (Unity "Managed Stripping Level" karsiligi; docs/engine-notes.md TRIM). true (varsayilan) = lean build:
    // erisilemeyen kod emit edilmez, interpreter (vmint) derlenmez, reflection meta yalniz kok kumeye (pak component tipleri
    // + alanlari + [Preserve]) -> kucuk ikili (Android libgame.so 10.7 -> 4.5 MB). false = full: tam meta + vmint; yalniz
    // dinamik modul yukleyen HOST ya da autoload gelistirme (scene-dump-test) icin. Release = true; modulden erisilecek
    // API [Preserve] ile kok yapilir (Unity mantigi), ayri "modul yuzeyi" kavrami yok.
    [Category("Build")] public bool stripCode = true;

    // Android'e ozel yayin ayarlari (Build Android Project -> generated/app.properties; kabuk Gradle her build okur).
    [Category("Android")] public AndroidSettings android = new();

    // iOS'a ozel yayin ayarlari (Build iOS Project -> generated/app.xcconfig; Xcode kabugu her build okur).
    [Category("iOS")] public IosSettings ios = new();

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

    // Oyun adi dogrulamasi (ProjectSettings/Games/<ad> klasor adi): harf/rakam/_/-.
    public static bool IsValidGameName(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return false;
        foreach (char c in v) if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return false;
        return true;
    }
}

// Android yayin ayarlari (PlayerSettings.android; git'e girer, varyantla override edilebilir). Imza parolalari da burada,
// [Sensitive]: RPC/MCP ciktisinda '***' (AI okuyamaz), inspector maskeli. CI'da DE_ANDROID_KEYSTORE_PASS / DE_ANDROID_KEY_PASS
// ortam degiskenleri ustun gelir; build sirasinda Build/android/keystore.properties'e yazilir (Gradle konvansiyonu, gitignore'lu).
[Serializable]
public class AndroidSettings
{
    public string packageName = "";        // bos = bundleIdentifier (applicationId)
    // versionCode YOK: build numarasi otomatik (PlayerSettings.BuildNumber: unix dakika; Android versionCode + iOS CFBundleVersion ayni sayi).
    public int targetSdk = 35;
    public bool includeX86_64 = true;      // emulator ABI'si YALNIZ debug APK'ya girer; release her zaman yalniz arm64-v8a
    public string keystorePath = "";       // proje kokune goreli ya da mutlak .keystore/.jks; bos = debug imza
    public string keyAlias = "";
    [Sensitive] public string keystorePassword = "";
    [Sensitive] public string keyPassword = "";   // bos = keystorePassword
    public string iconForeground = "";     // Assets'e goreli PNG (adaptive icon on plani, 432x432 onerilir); bos = engine varsayilani
    public string iconBackground = "#1E1E1E"; // "#RRGGBB" renk ya da Assets'e goreli PNG
}

// iOS yayin ayarlari (PlayerSettings.ios; git'e girer). Imza/sertifika burada DEGIL: Xcode "Signing & Capabilities"
// (Automatic; teamId verilirse xcconfig'e DEVELOPMENT_TEAM yazilir, ilk acilista secmek gerekmez).
[Serializable]
public class IosSettings
{
    public string bundleId = "";           // bos = bundleIdentifier
    // buildNumber YOK: CFBundleVersion otomatik (PlayerSettings.BuildNumber); CFBundleShortVersionString = PlayerSettings.version.
    public string minIos = "15.0";         // IPHONEOS_DEPLOYMENT_TARGET
    public string teamId = "";             // Apple Developer Team ID (10 karakter); bos = Xcode'da secilir
    public string icon = "";               // Assets'e goreli PNG (1024x1024, opak, kose yuvarlatma YOK); bos = engine varsayilani
}
