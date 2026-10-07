// corelib: System.Attribute taban sinifi. Attribute'ler release'te METADATA'dir — reflection yok,
// ornegi olusturulmaz; taban yalniz turetilmis siniflarin (SerializeField, ShowIf...) imza/kalitim
// cozumu icin vardir (yoksa frontend "skip base" dusurur, diag kirlenir).
namespace System
{
    public class Attribute
    {
        public Attribute() { }
    }

    // Platform sorgulari: derleme zamani sabitleri C tarafinda (#ifdef); oyun kodu
    // `if (OperatingSystem.IsIOS())` yazabilir, AOT'de dal sabit katlanir.
    public static class OperatingSystem
    {
        public extern static bool IsWindows();
        public extern static bool IsMacOS();
        public extern static bool IsIOS();
        public extern static bool IsAndroid();
        public extern static bool IsLinux();
        public extern static bool IsBrowser();
    }
}
