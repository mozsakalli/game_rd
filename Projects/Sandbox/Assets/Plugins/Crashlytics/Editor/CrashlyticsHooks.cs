using DigitoyEngine.Build;

// Crashlytics build kancalari (docs/plugins.md). Android: google-services + crashlytics gradle plugin'leri (app seviyesi; classpath
// generated/gradle-plugins.properties), google-services.json plugin klasorunden app/'e. iOS: SPM FirebaseCrashlytics urunu
// (yalniz o; Analytics vb girmez). Bagimlilik versiyonu plugin modulunde (Android/build.gradle.kts, BoM).
public sealed class CrashlyticsAndroid : IAndroidProjectHook
{
    public void OnGenerate(AndroidProject p)
    {
        p.GradlePlugin("com.google.gms.google-services", "4.4.2");
        p.GradlePlugin("com.google.firebase.crashlytics", "3.0.2");
        // Firebase konsolundan indirilen dosya plugin klasorune konur; yoksa uyari (Gradle google-services plugin'i build'de ister).
        if (p.HasPluginFile("Android/google-services.json")) p.AppFile("Android/google-services.json");
        else p.Warn("Android/google-services.json yok — Firebase konsolundan indirip Assets/Plugins/Crashlytics/Android/ altina koyun");
    }
}

public sealed class CrashlyticsIos : IIosProjectHook
{
    public void OnGenerate(IosProject p)
    {
        p.SwiftPackage("https://github.com/firebase/firebase-ios-sdk", "11.0.0", "FirebaseCrashlytics");
        // GoogleService-Info.plist: Xcode projesine kullanici ekler (bundle resource); hook plist anahtari yazmaz.
    }
}
