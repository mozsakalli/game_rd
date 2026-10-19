using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

// OYUN baglami (docs/plugins.md "Games"): aotcompiler `--game <ad>`. Proje = ortak Assets + N oyun; her oyunun TAM ayarlari
// ProjectSettings/Games/<ad>/ (PlayerSettings.asset + Plugins/<Ad>.asset). Burada yalniz build'in ihtiyac duydugu kisim:
// ayar klasoru, cikti koku (Build/<game>/), aot cache koku (Library/aot/<game>/<target>). Kimlik alanlari (--app-*, --props)
// editorun cozdugu Player'dan gelir; plugin ayarlari PluginSettingsStore oyunun klasorunden okur. Tum Assets kodu derlenir.
public static class Game
{
    public static string Name = "";

    public static string SettingsDir(string projectRoot) => Path.Combine(projectRoot, "ProjectSettings", "Games", Name);
    public static string BuildDir(string projectRoot) => Path.Combine(projectRoot, "Build", Name);
    public static string AotDir(string projectRoot, string target) => Path.Combine(projectRoot, "Library", "aot", Name, target);

    // --game <ad>: adi dogrula; bos verildiyse projede TEK oyun varsa o, yoksa hata (CI acikca soylesin).
    public static void Set(string projectRoot, string name, Action<string> log)
    {
        name = (name ?? "").Trim();
        string gamesDir = Path.Combine(projectRoot, "ProjectSettings", "Games");
        if (name.Length == 0)
        {
            var found = Directory.Exists(gamesDir)
                ? Directory.GetDirectories(gamesDir).Where(d => File.Exists(Path.Combine(d, "PlayerSettings.asset"))).Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : new System.Collections.Generic.List<string>();
            if (found.Count == 1) name = found[0];
            else throw new Exception(found.Count == 0
                ? "game: ProjectSettings/Games/ altinda oyun yok (editorle bir kez acin)"
                : "game: --game <ad> gerekli; oyunlar: " + string.Join(", ", found));
        }
        if (!Regex.IsMatch(name, "^[A-Za-z0-9_-]+$"))
            throw new Exception("game: gecersiz ad '" + name + "' (harf/rakam/_/-)");
        if (!Directory.Exists(Path.Combine(gamesDir, name)))
            throw new Exception("game: ProjectSettings/Games/" + name + "/ yok");
        Name = name;
        log($"[game] {name}: cikti Build/{name}/");
    }
}
