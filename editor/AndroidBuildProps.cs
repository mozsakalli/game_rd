using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitoyEngine;

namespace DigitoyEditor;

// Android imza parolalari: KULLANICIYA ait, UserSettings/AndroidSigning.asset (gitignore'lu). PlayerSettings.android
// (git'e girer) yalniz keystore yolu + alias tutar. Ortam degiskenleri (DE_ANDROID_KEYSTORE_PASS / DE_ANDROID_KEY_PASS)
// aotcompiler tarafinda bunlara ustun gelir (CI). Gui'de maskeli alan yok: duz metin, pencerede "git'e girmez" notu.
[Serializable]
public class AndroidSigning
{
    public string keystorePassword = "";
    public string keyPassword = "";      // bos = keystorePassword
}

// Editor -> aotcompiler Android verisi: CLI bayraklari cogaltmak yerine tek key=value dosyasi
// (<proje>/Library/Build/android.properties; `aotcompiler player --target android --props <dosya>`).
// aotcompiler bunu generated/app.properties (Gradle her build okur) + Build/android/keystore.properties'e ayirir.
public static class AndroidBuildProps
{
    public static string Write(Project project)
    {
        var ps = project.Player;
        var a = ps.android ?? new AndroidSettings();
        var sign = project.AndroidSigning ?? new AndroidSigning();

        var kv = new List<KeyValuePair<string, string>>
        {
            new("applicationId", string.IsNullOrWhiteSpace(a.packageName) ? ps.bundleIdentifier : a.packageName.Trim()),
            new("appName", ps.productName),
            new("versionName", ps.version),
            new("versionCode", a.versionCode.ToString()),
            new("targetSdk", a.targetSdk.ToString()),
            new("abis", a.includeX86_64 ? "arm64-v8a,x86_64" : "arm64-v8a"),
            new("orientation", ps.orientation),
            new("iconForeground", ResolveAsset(project, a.iconForeground)),
            new("iconBackground", a.iconBackground != null && a.iconBackground.TrimStart().StartsWith("#")
                ? a.iconBackground.Trim() : ResolveAsset(project, a.iconBackground)),
            new("signing.storeFile", ResolveProject(project, a.keystorePath)),
            new("signing.keyAlias", a.keyAlias ?? ""),
            new("signing.storePassword", sign.keystorePassword ?? ""),
            new("signing.keyPassword", sign.keyPassword ?? ""),
        };

        var sb = new StringBuilder();
        sb.AppendLine("# DigitoyEngine editor -> aotcompiler (her build yeniden yazilir; elle duzenlemeyin)");
        foreach (var p in kv)
            sb.Append(p.Key).Append('=').AppendLine(Escape(p.Value ?? ""));

        string path = Path.Combine(project.LibraryPath, "Build", "android.properties");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    // Assets'e goreli -> mutlak (bos kalir bossa).
    static string ResolveAsset(Project project, string rel)
        => string.IsNullOrWhiteSpace(rel) ? "" : Path.GetFullPath(Path.Combine(project.AssetsPath, rel.Trim()));

    // Proje kokune goreli ya da mutlak -> mutlak.
    static string ResolveProject(Project project, string p)
        => string.IsNullOrWhiteSpace(p) ? "" : Path.GetFullPath(Path.IsPathRooted(p.Trim()) ? p.Trim() : Path.Combine(project.Root, p.Trim()));

    // java.util.Properties uyumu: ters bolu ve satir sonu kacislanir; yollar '/' ile yazilir.
    static string Escape(string v)
        => v.Replace("\\", "/").Replace("\r", "").Replace("\n", "\\n");
}
