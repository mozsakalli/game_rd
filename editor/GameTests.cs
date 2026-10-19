#if DE_EDITOR
using System;
using System.IO;
using DigitoyEngine;

namespace DigitoyEditor;

// Acilis smoke: proje = ortak Assets + N oyun (docs/plugins.md "Games") — Games/ kesfi, eski yerlesimden (kok PlayerSettings +
// Variants overlay + Plugins) tam dosyalara gecis, oyun olusturma/silme (kopya), oyun basina plugin ayari, ProjectBinary game
// alani, editor durumu stash/restore. Gecici dizinde; projeye dokunmaz.
public static class GameTests
{
    static int _pass, _fail;

    public static void Run()
    {
        _pass = _fail = 0;
        string root = Path.Combine(Path.GetTempPath(), "de_game_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            // --- 1) Eski yerlesim: taban (sahnesiz, notr) + 2 varyant overlay + taban plugin + overlay plugin ---
            string ps = Path.Combine(root, "ProjectSettings");
            Directory.CreateDirectory(Path.Combine(ps, "Variants", "gin", "Plugins"));
            Directory.CreateDirectory(Path.Combine(ps, "Variants", "okey"));
            Directory.CreateDirectory(Path.Combine(ps, "Plugins"));
            File.WriteAllText(Path.Combine(ps, "PlayerSettings.asset"),
                "type: PlayerSettings\nfields:\n  companyName: Digitoy\n  productName: client\n  version: 2.0\n  scenes:\n  startScene: ''\n  themes:\n    - xmas\n  activeTheme: ''\n  variants:\n    - gin\n    - okey\n  activeVariant: gin\n  excludeFolders:\n    - Games/Okey\n  orientation: Portrait\n");
            File.WriteAllText(Path.Combine(ps, "Variants", "gin", "PlayerSettings.asset"),
                "type: PlayerSettings\nfields:\n  productName: Gin\n  bundleIdentifier: com.x.gin\n  scenes:\n    - Games/Gin/Login.scene\n  startScene: Games/Gin/Login.scene\n  excludeFolders:\n    - Games/Okey\n");
            File.WriteAllText(Path.Combine(ps, "Variants", "okey", "PlayerSettings.asset"),
                "type: PlayerSettings\nfields:\n  productName: Okey\n  startScene: Games/Okey/Login.scene\n");
            File.WriteAllText(Path.Combine(ps, "Plugins", "Facebook.asset"), "type: Config\nfields:\n  appId: base-id\n  displayName: Base\n");
            File.WriteAllText(Path.Combine(ps, "Variants", "gin", "Plugins", "Facebook.asset"), "type: Config\nfields:\n  appId: gin-id\n");

            var p = Project.Load(root, "gin");
            var names = p.GameNames();
            Check(names.Count == 2 && names[0] == "gin" && names[1] == "okey", "gecis: notr taban oyun olmaz, 2 oyun");
            Check(!File.Exists(Path.Combine(ps, "PlayerSettings.asset")) && !Directory.Exists(Path.Combine(ps, "Variants")) && !Directory.Exists(Path.Combine(ps, "Plugins")), "gecis: eski dosyalar silindi");
            Check(p.Game == "gin" && p.Player.productName == "Gin" && p.Player.bundleIdentifier == "com.x.gin" && p.Player.startScene == "Games/Gin/Login.scene", "gin: overlay alanlari");
            Check(p.Player.companyName == "Digitoy" && p.Player.version == "2.0" && p.Player.orientation == ScreenOrientation.Portrait && p.Player.themes.Count == 1 && p.Player.themes[0] == "xmas", "gin: taban alanlari + tema listesi oyunun");
            string ginYaml = File.ReadAllText(p.PlayerSettingsPathFor("gin"));
            Check(!ginYaml.Contains("variants") && !ginYaml.Contains("excludeFolders") && !ginYaml.Contains("activeVariant"), "gecis: kalkan alanlar yazilmaz");
            var okey = p.PlayerFor("okey");
            Check(okey.productName == "Okey" && okey.companyName == "Digitoy" && okey.scenes.Count == 0 && okey.startScene == "Games/Okey/Login.scene", "okey: taban + overlay");
            // Plugin: gin = taban + overlay birlesimi; okey = taban aynen
            string ginFb = File.ReadAllText(p.PluginSettingsPathFor("gin", "Facebook"));
            string okeyFb = File.ReadAllText(p.PluginSettingsPathFor("okey", "Facebook"));
            Check(ginFb.Contains("appId: gin-id") && ginFb.Contains("displayName: Base"), "plugin gecis: overlay alan + taban alan");
            Check(okeyFb.Contains("appId: base-id"), "plugin gecis: overlay'siz oyun tabani alir");

            // --- 2) SetGame / Save / PlayerFor ayni instance ---
            p.SetGame("okey");
            Check(p.Game == "okey" && ReferenceEquals(p.Player, p.PlayerFor("okey")) && p.BuildDir.EndsWith(Path.Combine("Build", "okey")), "SetGame: Player = PlayerFor, BuildDir");
            p.Player.version = "3.1";
            p.SavePlayerSettings();
            var p2 = Project.Load(root, "okey");
            Check(p2.Player.version == "3.1" && p2.PlayerFor("gin").version == "2.0", "kayit oyuna ozel");

            // --- 3) CreateGame (kopya) / RemoveGame / son oyun korunur ---
            p2.CreateGame("mini", "gin");
            Check(p2.HasGame("mini") && p2.PlayerFor("mini").productName == "mini" && p2.PlayerFor("mini").bundleIdentifier == "com.x.gin", "CreateGame: kopya + ad");
            Check(p2.PlayerFor("mini").scenes.Count == 0 && p2.PlayerFor("mini").startScene == "", "CreateGame: sahne kumesi kopyalanmaz");
            Check(File.Exists(p2.PluginSettingsPathFor("mini", "Facebook")), "CreateGame: plugin ayarlari kopyalanir");
            bool threw = false;
            try { p2.CreateGame("a b", null); } catch (ArgumentException) { threw = true; }
            Check(threw, "CreateGame: gecersiz ad reddedilir");
            p2.SetGame("mini");
            p2.RemoveGame("mini");
            Check(!p2.HasGame("mini") && p2.Game == "gin", "RemoveGame: calisilan silinince ilk oyuna gecer");
            p2.RemoveGame("okey");
            threw = false;
            try { p2.RemoveGame("gin"); } catch (InvalidOperationException) { threw = true; }
            Check(threw && p2.HasGame("gin"), "son oyun silinemez");

            // --- 4) Bilinmeyen oyun ilk oyuna duser; oyunsuz proje klasor adiyla oyun uretir ---
            var p3 = Project.Load(root, "yok");
            Check(p3.Game == "gin", "bilinmeyen oyun -> ilk");
            string empty = Path.Combine(root, "Fresh_Proj");
            var p4 = Project.Load(empty);
            Check(p4.GameNames().Count == 1 && p4.Game == "Fresh_Proj" && File.Exists(p4.PlayerSettingsPath), "bos proje: klasor adiyla ilk oyun");

            // --- 5) ProjectBinary game alani + eski (bos) uyum ---
            var b3 = ProjectBinary.Write("G", "S", new[] { "S", "T" }, "okey", "Games/Okey/Boot.asset", "1.2.3", 29800000, dev: true);
            Check(ProjectBinary.TryRead(b3, out ProjectBinary.Record rec) && rec.Game == "okey" && rec.Scenes.Length == 2 && rec.Boot == "Games/Okey/Boot.asset"
                && rec.Version == "1.2.3" && rec.BuildNumber == 29800000 && rec.Dev, "ProjectBinary game + boot + version + buildNumber + dev");
            var bEmpty = ProjectBinary.Write("G", "S", null);
            Check(ProjectBinary.TryRead(bEmpty, out var rec2) && rec2.Game == "" && rec2.Boot == "" && rec2.Version == "" && rec2.BuildNumber == 0 && !rec2.Dev, "ProjectBinary bos alanlar");
            int bn = PlayerSettings.BuildNumberNow();
            Check(bn > 29_000_000 && bn < 2_100_000_000, "build numarasi unix dakika, Play Store tavaninin altinda");

            // --- 6) Editor durumu: oyun basina stash/restore ---
            var es = new EditorSettings { lastScene = "Assets/A.scene", workingTheme = "xmas", sceneCamZoom = 2f, deviceName = "ipad" };
            es.StashGame("gin");
            es.lastScene = "Assets/B.scene"; es.workingTheme = ""; es.sceneCamZoom = 1f; es.deviceName = "";
            es.StashGame("okey");
            es.RestoreGame("gin");
            Check(es.lastScene == "Assets/A.scene" && es.workingTheme == "xmas" && es.sceneCamZoom == 2f && es.deviceName == "ipad", "RestoreGame: alanlar geri gelir");
            es.RestoreGame("yok");
            Check(es.lastScene == "" && es.workingTheme == "" && es.deviceName == "ipad", "RestoreGame: kaydi olmayan oyun sahne/tema sifir, cihaz kalir");
            string esPath = Path.Combine(root, "es.asset");
            ObjectSerializer.Save(es, esPath);
            var es2 = ObjectSerializer.Load<EditorSettings>(esPath);
            Check(es2.games.Count == 2 && es2.StateFor("okey", false)?.lastScene == "Assets/B.scene", "EditorSettings.games round-trip");

            Check(PlayerSettings.IsValidGameName("okey_tr-2") && !PlayerSettings.IsValidGameName("a b") && !PlayerSettings.IsValidGameName("") && !PlayerSettings.IsValidGameName("x/y"), "oyun adi");
        }
        catch (Exception e)
        {
            _fail++;
            Console.WriteLine("[game] EXCEPTION: " + e);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"[game] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[game] FAIL: {name}"); }
    }
}
#endif
