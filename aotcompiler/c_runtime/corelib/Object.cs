// corelib: System.Object koku. Bu bildirim YENI tip yaratmaz - derleyici uyeleri runtime
// kokune (Primitive.Object / C: VmObject+vmobject_type) baglar (well-known type binding).
// extern = govde c_runtime/corelib.c'de (C, mangled adla) + source/Intrinsics.cs'te (VM).
// SLOT SOZLESMESI (vmrt.h vmobject_vtable): 0=GetHashCode, 1=Equals, 2=ToString - sira DEGISMEZ.
namespace System
{
    interface IDisposable
    {
        void Dispose();
    }

    // Tek is parcacigi modeli: yonetilen thread kimligi sabit (Roslyn iterator GetEnumerator kontrolu icin yeter).
    static class Environment
    {
        public static int CurrentManagedThreadId { get { return 1; } }
    }

    class Object
    {
        public static bool Equals(object a, object b) { return a == null ? b == null : a.Equals(b); }
        extern virtual int GetHashCode();
        extern virtual bool Equals(object other);
        extern virtual string ToString();
        extern Type GetType(); // non-virtual (C# gibi); wrapper kimligi: ayni tip = ayni Type nesnesi
    }
}
