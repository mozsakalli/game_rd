using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DigitoyEngine;

// Unity PlayerPrefs karsiligi: kucuk kalici anahtar/deger (ayar, skor, ilerleme bayragi).
// Depo platformun kendisinin (de_fs.c prefs bolumu): iOS NSUserDefaults, Android SharedPreferences,
// Web localStorage, desktop <writable>/prefs.dat. Degerler string; int/float invariant kultur.
// Save(): desktop'ta diske yazar — GameHost Pause/Shutdown ve editor Play-Stop otomatik cagirir;
// kritik anlarda (satin alma, level sonu) oyun kodu kendisi cagirir. Buyuk/sik veri icin DEGIL
// (frame basina yazma yok; her cagri native sinir + string marshal).
public static unsafe class PlayerPrefs
{
    const string Lib = "digitoyengine_native";

    // Sayi <-> string invariant. DE_AOT: Digitoy.CoreLib'de CultureInfo yok; ToString()/TryParse(s, out) zaten
    // invariant + shortest-roundtrip (corelib.c sb_double). .NET tarafinda kultur acikca verilir.
#if DE_AOT
    static string FormatInt(int v) => v.ToString();
    static string FormatFloat(float v) => v.ToString();
    static bool ParseInt(string s, out int v) => int.TryParse(s, out v);
    static bool ParseFloat(string s, out float v) => float.TryParse(s, out v);
#else
    static string FormatInt(int v) => v.ToString(CultureInfo.InvariantCulture);
    static string FormatFloat(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static bool ParseInt(string s, out int v) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
    static bool ParseFloat(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
#endif

    public static void SetInt(string key, int value) => SetString(key, FormatInt(value));
    public static int GetInt(string key, int defaultValue = 0)
    {
        var s = GetString(key, null);
        return s != null && ParseInt(s, out int v) ? v : defaultValue;
    }

    public static void SetFloat(string key, float value) => SetString(key, FormatFloat(value));
    public static float GetFloat(string key, float defaultValue = 0f)
    {
        var s = GetString(key, null);
        return s != null && ParseFloat(s, out float v) ? v : defaultValue;
    }

    public static void SetBool(string key, bool value) => SetInt(key, value ? 1 : 0);
    public static bool GetBool(string key, bool defaultValue = false) => GetInt(key, defaultValue ? 1 : 0) != 0;

    public static void SetString(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
            return;
        de_prefs_set(key, value ?? "");
    }

    public static string GetString(string key, string defaultValue = "")
    {
        if (string.IsNullOrEmpty(key))
            return defaultValue;
        byte* p; int n;
        if (de_prefs_get(key, &p, &n) == 0 || p == null)
            return defaultValue;
        string s;
        if (n == 0)
            s = "";
        else
        {
            var bytes = new byte[n];
            Marshal.Copy((IntPtr)p, bytes, 0, n);
            s = Encoding.UTF8.GetString(bytes);
        }
        de_prefs_free(p);
        return s;
    }

    public static bool HasKey(string key) => !string.IsNullOrEmpty(key) && de_prefs_has(key) != 0;
    public static void DeleteKey(string key) { if (!string.IsNullOrEmpty(key)) de_prefs_delete(key); }
    public static void DeleteAll() => de_prefs_delete_all();
    public static void Save() => de_prefs_save();

    // Host kurar (desktop dosya koku): .NET dev host / editor <proje>/Library; AOT'de de_app_init writableRoot
    // native'de dogrudan verir. Kok degisince bellekteki kaydedilmemis degisiklik dusurulur.
    public static void SetRoot(string dir) => de_prefs_set_root(dir ?? "");

    [DllImport(Lib, EntryPoint = "de_prefs_set_root")]
    static extern void de_prefs_set_root([MarshalAs(UnmanagedType.LPUTF8Str)] string root);
    // out tampon C'de void** (wasm32: IntPtr imza uyusmazligi) -> pointer.
    [DllImport(Lib, EntryPoint = "de_prefs_get")]
    static extern int de_prefs_get([MarshalAs(UnmanagedType.LPUTF8Str)] string key, byte** outData, int* len);
    [DllImport(Lib, EntryPoint = "de_prefs_free")]
    static extern void de_prefs_free(byte* p);
    [DllImport(Lib, EntryPoint = "de_prefs_set")]
    static extern void de_prefs_set([MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Lib, EntryPoint = "de_prefs_delete")]
    static extern void de_prefs_delete([MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [DllImport(Lib, EntryPoint = "de_prefs_delete_all")]
    static extern void de_prefs_delete_all();
    [DllImport(Lib, EntryPoint = "de_prefs_has")]
    static extern int de_prefs_has([MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [DllImport(Lib, EntryPoint = "de_prefs_save")]
    static extern void de_prefs_save();
}
