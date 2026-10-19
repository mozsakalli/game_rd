using System;
using System.Collections.Generic;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Acik proje: Unity proje modeli — Assets/ (tum icerik + kod), ProjectSettings/, Library/ (turetilmis cache; git'e girmez).
// PROJE = ORTAK Assets + N OYUN (docs/plugins.md "Games"): her oyunun TAM ayar dosyasi
// ProjectSettings/Games/<ad>/PlayerSettings.asset (+ Plugins/<Ad>.asset). Taban/overlay YOK; en az bir oyun ZORUNLU
// (yoksa klasor adiyla uretilir). Calisilan oyun (Game) = Play/Build/pak baglami; EditorSettings.workingGame (ya da --game).
// Eski yerlesim (kok PlayerSettings.asset + Variants/ overlay + Plugins/) ilk acilista Games/ altina tasinir.
public sealed class Project
{
    public string Root;
    // Calisilan oyunun ayarlari (PlayerFor(Game) ile ayni instance).
    public PlayerSettings Player = new();
    // Calisilan oyun adi (ProjectSettings/Games/<Game>); SetGame ile degisir.
    public string Game { get; private set; } = "";

    readonly Dictionary<string, PlayerSettings> _players = new(StringComparer.OrdinalIgnoreCase);

    public string Name => Player.productName;
    public string StartScene => Player.startScene; // Assets'e goreli

    public string AssetsPath => Path.Combine(Root, "Assets");
    public string LibraryPath => Path.Combine(Root, "Library");
    public string ProjectSettingsPath => Path.Combine(Root, "ProjectSettings");
    public string UserSettingsPath => Path.Combine(Root, "UserSettings");
    public string GamesPath => Path.Combine(ProjectSettingsPath, "Games");
    public string GameDir(string game) => Path.Combine(GamesPath, game);
    public string PlayerSettingsPathFor(string game) => Path.Combine(GameDir(game), "PlayerSettings.asset");
    public string PluginSettingsDirFor(string game) => Path.Combine(GameDir(game), "Plugins");
    public string PluginSettingsPathFor(string game, string plugin) => Path.Combine(PluginSettingsDirFor(game), plugin + ".asset");
    public string PlayerSettingsPath => PlayerSettingsPathFor(Game);
    public string StartScenePath => Path.Combine(AssetsPath, StartScene);
    // Cikti koku: Build/<game>/ (aotcompiler ayni kural).
    public string BuildDir => Path.Combine(Root, "Build", Game);

    public static Project Load(string root, string game = "")
    {
        var p = new Project { Root = Path.GetFullPath(root) };
        Directory.CreateDirectory(p.AssetsPath);
        Directory.CreateDirectory(p.LibraryPath);
        p.MigrateLegacy();
        var names = p.GameNames();
        if (names.Count == 0)
        {
            string def = SanitizeName(Path.GetFileName(p.Root.TrimEnd('\\', '/')));
            p.CreateGame(def, null);
            EditorLog.Info("[project] oyun tanimi yoktu: ProjectSettings/Games/" + def + "/ olusturuldu");
            names = p.GameNames();
        }
        game = (game ?? "").Trim();
        if (game.Length > 0 && !ContainsName(names, game))
        {
            EditorLog.Warning("[game] bilinmeyen oyun '" + game + "' -> " + names[0]);
            game = "";
        }
        p.SetGame(game.Length > 0 ? game : names[0]);
        // GOC (2026-10-18): eski UserSettings/AndroidSigning.asset parolalari PlayerSettings.android'e ([Sensitive]) tasinir, dosya silinir.
        string legacySign = Path.Combine(p.UserSettingsPath, "AndroidSigning.asset");
        if (File.Exists(legacySign))
        {
            try
            {
                var doc = Yaml.Parse(File.ReadAllText(legacySign));
                var f = doc.Get("fields") ?? doc;
                string sp = f.GetScalar("keystorePassword", ""), kp = f.GetScalar("keyPassword", "");
                if (sp.Length > 0 && string.IsNullOrEmpty(p.Player.android.keystorePassword)) p.Player.android.keystorePassword = sp;
                if (kp.Length > 0 && string.IsNullOrEmpty(p.Player.android.keyPassword)) p.Player.android.keyPassword = kp;
                p.SavePlayerSettings();
                File.Delete(legacySign);
                EditorLog.Info("[project] AndroidSigning.asset -> PlayerSettings.android (keystorePassword/keyPassword, [Sensitive]) tasindi");
            }
            catch (Exception e) { EditorLog.Warning("[project] AndroidSigning.asset tasinamadi: " + e.Message); }
        }
        return p;
    }

    // Oyun adlari: ProjectSettings/Games/* (PlayerSettings.asset iceren klasorler), sirali.
    public List<string> GameNames()
    {
        var list = new List<string>();
        if (Directory.Exists(GamesPath))
            foreach (var d in Directory.GetDirectories(GamesPath))
            {
                string n = Path.GetFileName(d);
                if (PlayerSettings.IsValidGameName(n) && File.Exists(Path.Combine(d, "PlayerSettings.asset")))
                    list.Add(n);
            }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public bool HasGame(string game) => ContainsName(GameNames(), game);

    // Oyunun ayarlari (dosyadan bir kez; Player Settings sekmeleri calisilan oyuna dokunmadan duzenler).
    public PlayerSettings PlayerFor(string game)
    {
        if (_players.TryGetValue(game, out var ps)) return ps;
        ps = new PlayerSettings();
        string path = PlayerSettingsPathFor(game);
        if (File.Exists(path))
        {
            try { ps = ObjectSerializer.Load<PlayerSettings>(path, Assets); }
            catch (Exception e) { EditorLog.Error("[project] " + game + "/PlayerSettings okunamadi: " + e.Message); }
        }
        _players[game] = ps;
        return ps;
    }

    // Asset referanslarinin (boot: IGameBoot) cozumu icin veritabani. Oyun kodu yuklenince/yeniden derlenince (katalog tazelenince)
    // ayarlar yeniden okunur: [Asset] tipler o anda cozulur (acilista kod yokken null kalirdi). Player instance'i degisir.
    public AssetDatabase Assets { get; private set; }

    public void BindAssets(AssetDatabase assets)
    {
        Assets = assets;
        _players.Clear();
        if (Game.Length > 0) Player = PlayerFor(Game);
    }

    // Calisilan oyunu degistir (Play/Build/pak baglami). Bilinmeyen ad -> ArgumentException.
    public void SetGame(string game)
    {
        game = (game ?? "").Trim();
        if (!HasGame(game))
            throw new ArgumentException("unknown game: " + game + " (ProjectSettings/Games/)");
        Game = game;
        Player = PlayerFor(game);
    }

    // Oyunun ayar dosyasini yaz (varsayilan: calisilan oyun).
    public void SavePlayerSettings(string game = null)
    {
        game ??= Game;
        Directory.CreateDirectory(GameDir(game));
        ObjectSerializer.Save(PlayerFor(game), PlayerSettingsPathFor(game), Assets);
    }

    // Yeni oyun: copyFrom verilirse o oyunun PlayerSettings + Plugins/ kopyalanir (ortak alanlar icin sablon), yoksa varsayilan.
    public void CreateGame(string name, string copyFrom)
    {
        name = (name ?? "").Trim();
        if (!PlayerSettings.IsValidGameName(name))
            throw new ArgumentException("invalid game name: '" + name + "' (letters/digits/_/-)");
        if (Directory.Exists(GameDir(name)))
            throw new ArgumentException("game already exists: " + name);
        Directory.CreateDirectory(GameDir(name));
        if (!string.IsNullOrEmpty(copyFrom) && HasGame(copyFrom))
        {
            File.Copy(PlayerSettingsPathFor(copyFrom), PlayerSettingsPathFor(name));
            string pd = PluginSettingsDirFor(copyFrom);
            if (Directory.Exists(pd))
            {
                Directory.CreateDirectory(PluginSettingsDirFor(name));
                foreach (var f in Directory.GetFiles(pd, "*.asset"))
                    File.Copy(f, Path.Combine(PluginSettingsDirFor(name), Path.GetFileName(f)));
            }
            var ps = PlayerFor(name);
            ps.productName = name;
            // Sahne kumesi kopyalanmaz: yeni oyun kendi sahnelerini secer (unutulursa kaynagin sahneleri build'e girerdi).
            ps.scenes.Clear();
            ps.startScene = "";
            SavePlayerSettings(name);
        }
        else
        {
            _players[name] = new PlayerSettings { productName = name };
            SavePlayerSettings(name);
        }
    }

    // Oyunu sil (klasoruyle). Son oyun silinemez; calisilan silinirse ilk kalan oyuna gecilir.
    public void RemoveGame(string name)
    {
        var names = GameNames();
        if (!ContainsName(names, name)) return;
        if (names.Count <= 1)
            throw new InvalidOperationException("a project needs at least one game");
        Directory.Delete(GameDir(name), true);
        _players.Remove(name);
        if (string.Equals(Game, name, StringComparison.OrdinalIgnoreCase))
            SetGame(GameNames()[0]);
    }

    static bool ContainsName(List<string> names, string n)
    {
        foreach (var x in names) if (string.Equals(x, n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static string SanitizeName(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s ?? "") if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
        return sb.Length > 0 ? sb.ToString() : "Game";
    }

    // --- Eski yerlesimden gecis (tek sefer; DocNode duzeyinde, tip gerektirmez — oyun kodu henuz derli degil) ---
    // Kaynak: ProjectSettings/PlayerSettings.asset (taban), Variants/<v>/PlayerSettings.asset (kismi overlay),
    // Plugins/<Ad>.asset (taban) + Variants/<v>/Plugins/<Ad>.asset (overlay). Hedef: Games/<ad>/ tam dosyalar.
    // Taban bir oyun olur (Games/<projeKlasoru>) — sahnesi yoksa (yalniz varyant tasiyan notr taban) olmaz.
    void MigrateLegacy()
    {
        string legacyPs = Path.Combine(ProjectSettingsPath, "PlayerSettings.asset");
        string variantsDir = Path.Combine(ProjectSettingsPath, "Variants");
        string pluginsDir = Path.Combine(ProjectSettingsPath, "Plugins");
        if (!File.Exists(legacyPs) && !Directory.Exists(variantsDir) && !Directory.Exists(pluginsDir))
        {
            MigrateProjectYaml();
            return;
        }
        try
        {
            DocNode baseDoc = File.Exists(legacyPs) ? Yaml.Parse(File.ReadAllText(legacyPs)) : null;
            var baseFields = baseDoc?.Get("fields") ?? DocNode.Map();
            string baseName = SanitizeName(Path.GetFileName(Root.TrimEnd('\\', '/'))); // oyun kimligi = proje klasoru (productName gorunen ad)
            var variants = new List<string>();
            if (Directory.Exists(variantsDir))
                foreach (var d in Directory.GetDirectories(variantsDir))
                    if (PlayerSettings.IsValidGameName(Path.GetFileName(d))) variants.Add(Path.GetFileName(d));
            bool baseHasScene = baseFields.GetScalar("startScene", "").Length > 0 || (baseFields.Get("scenes")?.Items?.Count ?? 0) > 0;
            var created = new List<string>();
            if (variants.Count == 0 || baseHasScene)
            {
                WriteGame(baseName, Strip(baseFields), pluginsDir, null);
                created.Add(baseName);
            }
            foreach (var v in variants)
            {
                string ovPath = Path.Combine(variantsDir, v, "PlayerSettings.asset");
                var ov = File.Exists(ovPath) ? (Yaml.Parse(File.ReadAllText(ovPath)).Get("fields") ?? DocNode.Map()) : DocNode.Map();
                WriteGame(v, Strip(Merge(baseFields, ov)), pluginsDir, Path.Combine(variantsDir, v, "Plugins"));
                created.Add(v);
            }
            if (File.Exists(legacyPs)) File.Delete(legacyPs);
            if (Directory.Exists(variantsDir)) Directory.Delete(variantsDir, true);
            if (Directory.Exists(pluginsDir)) Directory.Delete(pluginsDir, true);
            EditorLog.Info("[project] eski ayar yerlesimi ProjectSettings/Games/ altina tasindi: " + string.Join(", ", created));
        }
        catch (Exception e) { EditorLog.Error("[project] ayar gecisi basarisiz: " + e.Message); }
    }

    // Eski project.yaml (yalniz ad + startScene tohumu) -> ilk oyun.
    void MigrateProjectYaml()
    {
        string legacy = Path.Combine(ProjectSettingsPath, "project.yaml");
        if (!File.Exists(legacy) || GameNames().Count > 0) return;
        var doc = Yaml.Parse(File.ReadAllText(legacy));
        string name = SanitizeName(doc.GetScalar("name", Path.GetFileName(Root.TrimEnd('\\', '/'))));
        _players[name] = new PlayerSettings { productName = doc.GetScalar("name", name), startScene = doc.GetScalar("startScene", "Scenes/Main.scene") };
        SavePlayerSettings(name);
    }

    void WriteGame(string game, DocNode fields, string basePluginsDir, string overlayPluginsDir)
    {
        Directory.CreateDirectory(GameDir(game));
        var root = DocNode.Map();
        root.Add("type", DocNode.Scal(nameof(PlayerSettings)));
        root.Add("fields", fields);
        File.WriteAllText(PlayerSettingsPathFor(game), Yaml.Write(root));
        // Plugin ayarlari: taban + overlay alan birlesimi; yalniz overlay'de olan dosya aynen.
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(basePluginsDir)) foreach (var f in Directory.GetFiles(basePluginsDir, "*.asset")) names.Add(Path.GetFileName(f));
        if (overlayPluginsDir != null && Directory.Exists(overlayPluginsDir)) foreach (var f in Directory.GetFiles(overlayPluginsDir, "*.asset")) names.Add(Path.GetFileName(f));
        if (names.Count == 0) return;
        Directory.CreateDirectory(PluginSettingsDirFor(game));
        foreach (var n in names)
        {
            string bp = Path.Combine(basePluginsDir ?? "", n), op = overlayPluginsDir != null ? Path.Combine(overlayPluginsDir, n) : null;
            DocNode bd = File.Exists(bp) ? Yaml.Parse(File.ReadAllText(bp)) : null;
            DocNode od = op != null && File.Exists(op) ? Yaml.Parse(File.ReadAllText(op)) : null;
            var src = bd ?? od;
            var merged = DocNode.Map();
            merged.Add("type", DocNode.Scal(src.GetScalar("type", "Config")));
            merged.Add("fields", Merge(bd?.Get("fields") ?? DocNode.Map(), od?.Get("fields") ?? DocNode.Map()));
            File.WriteAllText(Path.Combine(PluginSettingsDirFor(game), n), Yaml.Write(merged));
        }
    }

    // Overlay alanlari tabanin ustune (alan bazli; ic nesne bir butun).
    static DocNode Merge(DocNode baseFields, DocNode overlay)
    {
        var r = DocNode.Map();
        foreach (var kv in baseFields.Fields)
            r.Add(kv.Key, overlay.Get(kv.Key) ?? kv.Value);
        foreach (var kv in overlay.Fields)
            if (baseFields.Get(kv.Key) == null) r.Add(kv.Key, kv.Value);
        return r;
    }

    // Kalkan alanlar yazilmaz.
    static DocNode Strip(DocNode fields)
    {
        var r = DocNode.Map();
        foreach (var kv in fields.Fields)
            if (kv.Key != "variants" && kv.Key != "activeVariant" && kv.Key != "excludeFolders") r.Add(kv.Key, kv.Value);
        return r;
    }
}
