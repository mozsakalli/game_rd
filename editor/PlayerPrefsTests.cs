#if DE_EDITOR
using System;
using System.IO;
using DigitoyEngine;

namespace DigitoyEditor;

// Acilis smoke: PlayerPrefs (desktop dosya backend'i). Gecici kokte kosar; bitince proje kokune doner
// (editorun oyun prefs'ine dokunmaz). Mobil/web backend'leri burada test edilmez (platform deposu).
public static class PlayerPrefsTests
{
    static int _pass, _fail;

    public static void Run(string projectRoot)
    {
        _pass = _fail = 0;
        string tmp = Path.Combine(Path.GetTempPath(), "de-prefs-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            PlayerPrefs.SetRoot(tmp); // klasor yok: Save mkdir eder
            Check(!PlayerPrefs.HasKey("a") && PlayerPrefs.GetInt("a", 7) == 7, "bos depo: varsayilan doner");
            PlayerPrefs.SetInt("a", 42);
            PlayerPrefs.SetFloat("f", 1.5f);
            PlayerPrefs.SetString("s", "merhaba\tdünya\nsatır \\ ters");
            PlayerPrefs.SetBool("b", true);
            PlayerPrefs.SetString("empty", "");
            Check(PlayerPrefs.GetInt("a") == 42 && PlayerPrefs.GetFloat("f") == 1.5f && PlayerPrefs.GetBool("b"), "yazilan okunur (int/float/bool)");
            Check(PlayerPrefs.GetString("s") == "merhaba\tdünya\nsatır \\ ters", "unicode + kacis karakterleri bellekte");
            Check(PlayerPrefs.HasKey("empty") && PlayerPrefs.GetString("empty", "x") == "", "bos string anahtari var sayilir");
            Check(PlayerPrefs.GetInt("s", -1) == -1, "tip uyusmazligi varsayilana duser");
            PlayerPrefs.SetInt("a", 43);
            Check(PlayerPrefs.GetInt("a") == 43, "ustune yazma");
            PlayerPrefs.DeleteKey("b");
            Check(!PlayerPrefs.HasKey("b") && PlayerPrefs.HasKey("a"), "DeleteKey yalniz o anahtari siler");

            PlayerPrefs.Save();
            Check(File.Exists(Path.Combine(tmp, "prefs.dat")), "Save dosyayi yazar (mkdir dahil)");
            PlayerPrefs.SetInt("unsaved", 1);
            PlayerPrefs.SetRoot(tmp); // bellegi dusur -> diskten yeniden yukle
            Check(!PlayerPrefs.HasKey("unsaved"), "kaydedilmemis degisiklik kok yenilemede duser");
            Check(PlayerPrefs.GetInt("a") == 43 && PlayerPrefs.GetFloat("f") == 1.5f, "diskten geri yukleme (sayilar)");
            Check(PlayerPrefs.GetString("s") == "merhaba\tdünya\nsatır \\ ters", "diskten geri yukleme (kacisli string round-trip)");
            Check(PlayerPrefs.HasKey("empty") && PlayerPrefs.GetString("empty", "x") == "", "bos string round-trip");

            PlayerPrefs.DeleteAll();
            Check(!PlayerPrefs.HasKey("a"), "DeleteAll bellegi bosaltir");
            PlayerPrefs.Save();
            PlayerPrefs.SetRoot(tmp);
            Check(!PlayerPrefs.HasKey("a") && !PlayerPrefs.HasKey("s"), "DeleteAll + Save diske yansir");
            PlayerPrefs.SetString(null, "x"); PlayerPrefs.SetString("", "x"); // sessiz
            Check(PlayerPrefs.GetString(null, "d") == "d" && !PlayerPrefs.HasKey(""), "null/bos anahtar guvenli");
        }
        finally
        {
            PlayerPrefs.SetRoot(Path.Combine(projectRoot, "Library"));
            try { Directory.Delete(tmp, true); } catch { }
        }
        Console.WriteLine($"[prefs] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[prefs] FAIL: {name}"); }
    }
}
#endif
