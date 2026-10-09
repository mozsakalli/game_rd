// corelib: System.Attribute taban sinifi. Attribute'ler release'te METADATA'dir — reflection yok,
// ornegi olusturulmaz; taban yalniz turetilmis siniflarin (SerializeField, ShowIf...) imza/kalitim
// cozumu icin vardir (yoksa frontend "skip base" dusurur, diag kirlenir).
namespace System
{
    public class Attribute
    {
        public Attribute() { }
        public static bool IsDefined(Reflection.MemberInfo element, Type attributeType) { return element.IsDefined(attributeType, false); }
        public static bool IsDefined(Reflection.MemberInfo element, Type attributeType, bool inherit) { return element.IsDefined(attributeType, inherit); }
        public static Attribute GetCustomAttribute(Reflection.MemberInfo element, Type attributeType)
        {
            var all = element.GetCustomAttributes(attributeType, false);
            if (all.Length == 0) return null;
            if (all.Length > 1) throw new InvalidOperationException("Multiple custom attributes of the same type found.");
            return (Attribute)all[0];
        }
        public static Attribute[] GetCustomAttributes(Reflection.MemberInfo element, Type attributeType)
        {
            var all = element.GetCustomAttributes(attributeType, false);
            var r = new Attribute[all.Length];
            for (int i = 0; i < all.Length; i++) r[i] = (Attribute)all[i];
            return r;
        }
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
