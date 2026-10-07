// corelib: Roslyn'in (NoStdLib) ZORUNLU tuttugu predefined tipler ve derleyici-gerekli attribute'ler.
// Primitive struct'lar burada yalniz AD olarak vardir: derleyici/runtime bunlari kendi skaler
// temsiline baglar (WellKnown.RuntimeRootFor / CilFrontend.ResolveName) — yeni tip yaratmaz.
// Attribute'ler metadata'dir; release'te reflection yok, ornek olusmaz.
namespace System
{
    public struct Void { }
    public struct Boolean { }
    public struct Char { }
    public struct Byte { }
    public struct SByte { }
    public struct UInt16 { }
    public struct UInt64 { }
    public struct UIntPtr { }
    public struct RuntimeTypeHandle { }
    public struct RuntimeFieldHandle { }
    public struct RuntimeMethodHandle { }

    // Delegate kokleri: runtime temsili VmDelegate (vmrt.h); C# `delegate` bildirimleri Roslyn'de
    // MulticastDelegate'ten turer. Uyeler runtime'da (Invoke/BeginInvoke uretilmez, CallIndirect).
    public abstract class Delegate
    {
    }
    public abstract class MulticastDelegate : Delegate
    {
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn) { ValidOn = validOn; }
        public AttributeTargets ValidOn { get; set; }
        public bool AllowMultiple { get; set; }
        public bool Inherited { get; set; }
    }

    public enum AttributeTargets
    {
        Assembly = 1, Module = 2, Class = 4, Struct = 8, Enum = 16, Constructor = 32, Method = 64, Property = 128,
        Field = 256, Event = 512, Interface = 1024, Parameter = 2048, Delegate = 4096, ReturnValue = 8192,
        GenericParameter = 16384, All = 32767,
    }

    // [Serializable]/[NonSerialized]: motorun serilestirme sozlesmesi (Roslyn pseudo-attribute: FieldAttributes.NotSerialized).
    public sealed class SerializableAttribute : Attribute { }
    public sealed class NonSerializedAttribute : Attribute { }
    public sealed class FlagsAttribute : Attribute { }
    public sealed class ParamArrayAttribute : Attribute { }
    public sealed class ThreadStaticAttribute : Attribute { }
    public sealed class ObsoleteAttribute : Attribute
    {
        public ObsoleteAttribute() { }
        public ObsoleteAttribute(string message) { }
        public ObsoleteAttribute(string message, bool error) { }
    }
}

namespace System.Reflection
{
    // Roslyn indexer bildiren her tipe [DefaultMember("Item")] basar.
    public sealed class DefaultMemberAttribute : Attribute
    {
        public DefaultMemberAttribute(string memberName) { MemberName = memberName; }
        public string MemberName;
    }
}

namespace System.Runtime.CompilerServices
{
    // extension method isareti (CS1110 bunu arar)
    public sealed class ExtensionAttribute : Attribute { }
    public sealed class IndexerNameAttribute : Attribute
    {
        public IndexerNameAttribute(string indexerName) { }
    }
    public sealed class CompilerGeneratedAttribute : Attribute { }
    public sealed class IsVolatile { }
    public sealed class IsReadOnlyAttribute : Attribute { }
    public sealed class IsByRefLikeAttribute : Attribute { }
    public sealed class IsUnmanagedAttribute : Attribute { }
    public sealed class AsyncStateMachineAttribute : Attribute
    {
        public AsyncStateMachineAttribute(Type stateMachineType) { }
    }
    public sealed class IteratorStateMachineAttribute : Attribute
    {
        public IteratorStateMachineAttribute(Type stateMachineType) { }
    }
}
