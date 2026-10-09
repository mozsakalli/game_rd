// corelib: System.Object koku. Bu bildirim YENI tip yaratmaz - derleyici uyeleri runtime
// kokune (Primitive.Object / C: VmObject+vmobject_type) baglar (well-known type binding).
// extern = govde c_runtime/corelib.c'de (C, mangled adla) + source/Intrinsics.cs'te (VM).
// SLOT SOZLESMESI (vmrt.h vmobject_vtable): 0=GetHashCode, 1=Equals, 2=ToString - sira DEGISMEZ.
namespace System
{
    public interface IDisposable
    {
        public void Dispose();
    }

    // Tek is parcacigi modeli: yonetilen thread kimligi sabit (Roslyn iterator GetEnumerator kontrolu icin yeter).
    public static class Environment
    {
        public static int CurrentManagedThreadId { get { return 1; } }
        public extern static int TickCount { get; } // ms, monoton
    }

    // Tek domain: AppDomain.CurrentDomain.GetAssemblies() -> [tek sahte Assembly]
    public sealed class AppDomain
    {
        static readonly AppDomain current = new AppDomain();
        public static AppDomain CurrentDomain { get { return current; } }
        public Reflection.Assembly[] GetAssemblies() { return new Reflection.Assembly[] { Reflection.Assembly.GetExecutingAssembly() }; }
    }

    public class Object
    {
        public static bool Equals(object a, object b) { return a == null ? b == null : a.Equals(b); }
        public static bool ReferenceEquals(object a, object b) { return a == b; }
        public extern virtual int GetHashCode();
        public extern virtual bool Equals(object other);
        public extern virtual string ToString();
        public extern Type GetType(); // non-virtual (C# gibi); wrapper kimligi: ayni tip = ayni Type nesnesi
    }
}
