// corelib: motorun kullandigi interop/derleyici attribute'leri ve isaretci tipleri (metadata; release'te
// reflection yok). [DllImport] P/Invoke: CIL frontend EntryPoint'i C sembolu olarak alir (statik link).
// delegate* unmanaged: Roslyn CallConv* isaretcilerini ve RuntimeFeature.UnmanagedSignatureCallingConvention'i arar.
namespace System.Runtime.InteropServices
{
    public enum CallingConvention { Winapi = 1, Cdecl = 2, StdCall = 3, ThisCall = 4, FastCall = 5 }
    public enum CharSet { None = 1, Ansi = 2, Unicode = 3, Auto = 4 }
    public enum LayoutKind { Sequential = 0, Explicit = 2, Auto = 3 }
    public enum UnmanagedType
    {
        Bool = 2, I1 = 3, U1 = 4, I2 = 5, U2 = 6, I4 = 7, U4 = 8, I8 = 9, U8 = 10, R4 = 11, R8 = 12,
        LPStr = 20, LPWStr = 21, LPTStr = 22, ByValTStr = 23, Struct = 27, ByValArray = 30, SysInt = 31, SysUInt = 32,
        FunctionPtr = 38, LPArray = 42, LPStruct = 43, LPUTF8Str = 48,
    }

    public sealed class DllImportAttribute : Attribute
    {
        public DllImportAttribute(string dllName) { Value = dllName; }
        public string Value;
        public string EntryPoint;
        public CallingConvention CallingConvention;
        public CharSet CharSet;
        public bool ExactSpelling;
        public bool SetLastError;
        public bool PreserveSig;
        public bool BestFitMapping;
        public bool ThrowOnUnmappableChar;
    }

    public sealed class MarshalAsAttribute : Attribute
    {
        public MarshalAsAttribute(UnmanagedType unmanagedType) { Value = unmanagedType; }
        public MarshalAsAttribute(short unmanagedType) { Value = (UnmanagedType)unmanagedType; }
        public UnmanagedType Value;
        public UnmanagedType ArraySubType;
        public int SizeConst;
        public short SizeParamIndex;
        public string MarshalType;
        public Type MarshalTypeRef;
        public string MarshalCookie;
    }

    public sealed class StructLayoutAttribute : Attribute
    {
        public StructLayoutAttribute(LayoutKind layoutKind) { Value = layoutKind; }
        public StructLayoutAttribute(short layoutKind) { Value = (LayoutKind)layoutKind; }
        public LayoutKind Value;
        public int Pack;
        public int Size;
        public CharSet CharSet;
    }

    public sealed class FieldOffsetAttribute : Attribute
    {
        public FieldOffsetAttribute(int offset) { Value = offset; }
        public int Value;
    }

    public sealed class InAttribute : Attribute { }
    public sealed class OutAttribute : Attribute { }
    public sealed class OptionalAttribute : Attribute { }
    public sealed class UnmanagedCallersOnlyAttribute : Attribute
    {
        public Type[] CallConvs;
        public string EntryPoint;
    }
}

namespace System.Runtime.CompilerServices
{
    // delegate* unmanaged[Cdecl] isaretcileri (Roslyn modopt olarak yazar; frontend GetModifiedType ile dusurur)
    public class CallConvCdecl { }
    public class CallConvStdcall { }
    public class CallConvThiscall { }
    public class CallConvFastcall { }
    public class CallConvMemberFunction { }
    public class CallConvSuppressGCTransition { }

    public static class RuntimeFeature
    {
        public const string UnmanagedSignatureCallingConvention = "UnmanagedSignatureCallingConvention";
        public const string NumericIntPtr = "NumericIntPtr"; // IntPtr = nint: Roslyn donusumleri conv.i/conv.u8 ile yazar (op_Explicit cagrisi yok)
        public const string DefaultImplementationsOfInterfaces = "DefaultImplementationsOfInterfaces";
        public static bool IsSupported(string feature) { return true; }
    }

    public enum MethodImplOptions { Unmanaged = 4, NoInlining = 8, ForwardRef = 16, Synchronized = 32, NoOptimization = 64, PreserveSig = 128, AggressiveInlining = 256, AggressiveOptimization = 512, InternalCall = 4096 }
    public sealed class MethodImplAttribute : Attribute
    {
        public MethodImplAttribute(MethodImplOptions methodImplOptions) { Value = methodImplOptions; }
        public MethodImplAttribute(short value) { Value = (MethodImplOptions)value; }
        public MethodImplAttribute() { }
        public MethodImplOptions Value;
    }

    public sealed class TupleElementNamesAttribute : Attribute
    {
        public TupleElementNamesAttribute(string[] transformNames) { }
    }
    public sealed class InternalsVisibleToAttribute : Attribute
    {
        public InternalsVisibleToAttribute(string assemblyName) { }
    }
    public sealed class SkipLocalsInitAttribute : Attribute { }
    public sealed class ModuleInitializerAttribute : Attribute { }
    public enum UnsafeAccessorKind { Constructor, Method, StaticMethod, Field, StaticField }
    public sealed class UnsafeAccessorAttribute : Attribute
    {
        public UnsafeAccessorAttribute(UnsafeAccessorKind kind) { Kind = kind; }
        public UnsafeAccessorKind Kind;
        public string Name;
    }
}
