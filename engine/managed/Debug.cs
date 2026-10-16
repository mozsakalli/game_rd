using System;

namespace DigitoyEngine;

// Oyun kodu icin log yuzeyi (Unity Debug.Log benzeri). Varsayilan stdout/stderr (player/terminal); editor acilista
// Info/Warning/Error sink'lerini Console paneline baglar (AssetDatabase.LogWarning deseni). Delegeler motor DLL'inde
// yasar, ALC reload'dan etkilenmez. Uretimde maliyet: tek string birlestirme.
public static class Debug
{
    public static Action<string> OnInfo = m => Console.WriteLine(m);
    public static Action<string> OnWarning = m => Console.WriteLine("[warn] " + m);
    public static Action<string> OnError = m => Console.Error.WriteLine("[error] " + m);

    public static void Log(object message) { OnInfo(Text(message)); }
    public static void LogWarning(object message) { OnWarning(Text(message)); }
    public static void LogError(object message) { OnError(Text(message)); }
    public static void LogException(Exception e) { OnError(e == null ? "null" : e.ToString()); }

    static string Text(object message) => message == null ? "null" : message.ToString();
}
