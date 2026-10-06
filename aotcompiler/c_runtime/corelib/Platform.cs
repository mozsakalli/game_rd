// corelib: System.Attribute taban sinifi. Attribute'ler release'te METADATA'dir — reflection yok,
// ornegi olusturulmaz; taban yalniz turetilmis siniflarin (SerializeField, ShowIf...) imza/kalitim
// cozumu icin vardir (yoksa frontend "skip base" dusurur, diag kirlenir).
namespace System
{
    class Attribute
    {
        public Attribute() { }
    }

    // Platform sorgulari: derleme zamani sabitleri C tarafinda (#ifdef); oyun kodu
    // `if (OperatingSystem.IsIOS())` yazabilir, AOT'de dal sabit katlanir.
    static class OperatingSystem
    {
        extern public static bool IsWindows();
        extern public static bool IsMacOS();
        extern public static bool IsIOS();
        extern public static bool IsAndroid();
        extern public static bool IsLinux();
        extern public static bool IsBrowser();
    }
}
