using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitoyEngine;

namespace DigitoyEditor;

// Editor -> aotcompiler iOS verisi: tek key=value dosyasi (<proje>/Library/Build/ios.properties;
// `aotcompiler player --target ios --props <dosya>`). aotcompiler bunu generated/app.xcconfig'e cevirir (Xcode her build okur).
// Imza burada yok: Xcode Signing & Capabilities (teamId yalniz kolaylik; sertifika/profil Xcode'un).
public static class IosBuildProps
{
    public static string Write(Project project)
    {
        var ps = project.Player;
        var s = ps.ios ?? new IosSettings();

        var kv = new List<KeyValuePair<string, string>>
        {
            new("bundleId", string.IsNullOrWhiteSpace(s.bundleId) ? ps.bundleIdentifier : s.bundleId.Trim()),
            new("appName", ps.productName),
            new("version", ps.version),
            new("buildNumber", s.buildNumber.ToString()),
            new("minIos", s.minIos ?? ""),
            new("teamId", s.teamId ?? ""),
            new("orientation", ps.orientation.ToString().ToLowerInvariant()),
            new("icon", string.IsNullOrWhiteSpace(s.icon) ? "" : Path.GetFullPath(Path.Combine(project.AssetsPath, s.icon.Trim()))),
        };

        var sb = new StringBuilder();
        sb.AppendLine("# DigitoyEngine editor -> aotcompiler (her build yeniden yazilir; elle duzenlemeyin)");
        foreach (var p in kv)
            sb.Append(p.Key).Append('=').AppendLine((p.Value ?? "").Replace("\\", "/").Replace("\r", "").Replace("\n", "\\n"));

        string path = Path.Combine(project.LibraryPath, "Build", "ios.properties");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
