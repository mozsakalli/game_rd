// corelib: metadata tablolu AOT reflection. Lookup adi sabit descriptor havuzunda aranir;
// Field/Property/Method wrapper'lari lazy cache'lidir (handle = runtime kaydi, ILK alan sozlesmesi).
// Invoke = runtime sekil thunk'u (ref/out parametre desteklenmez). Attribute ve indexer reflection kapsam disidir.
namespace System.Reflection
{
    // CIL bit alanlari (.NET degerleriyle birebir; transpiler metadata'dan kopyalar)
    [Flags]
    public enum FieldAttributes
    {
        FieldAccessMask = 0x0007, PrivateScope = 0, Private = 1, FamANDAssem = 2, Assembly = 3, Family = 4, FamORAssem = 5, Public = 6,
        Static = 0x0010, InitOnly = 0x0020, Literal = 0x0040, NotSerialized = 0x0080, SpecialName = 0x0200, PinvokeImpl = 0x2000,
        HasFieldMarshal = 0x1000, RTSpecialName = 0x0400, HasDefault = 0x8000, HasFieldRVA = 0x0100,
    }
    [Flags]
    public enum MethodAttributes
    {
        MemberAccessMask = 0x0007, PrivateScope = 0, Private = 1, FamANDAssem = 2, Assembly = 3, Family = 4, FamORAssem = 5, Public = 6,
        Static = 0x0010, Final = 0x0020, Virtual = 0x0040, HideBySig = 0x0080, NewSlot = 0x0100, Abstract = 0x0400, SpecialName = 0x0800,
        PinvokeImpl = 0x2000, RTSpecialName = 0x1000,
    }
    [Flags]
    public enum TypeAttributes
    {
        VisibilityMask = 0x7, NotPublic = 0, Public = 1, NestedPublic = 2, NestedPrivate = 3, NestedFamily = 4, NestedAssembly = 5,
        NestedFamANDAssem = 6, NestedFamORAssem = 7, Interface = 0x20, Abstract = 0x80, Sealed = 0x100, SpecialName = 0x400,
        Serializable = 0x2000, Import = 0x1000, BeforeFieldInit = 0x00100000, LayoutMask = 0x18, SequentialLayout = 0x8, ExplicitLayout = 0x10,
    }
    [Flags]
    public enum PropertyAttributes { None = 0, SpecialName = 0x200, RTSpecialName = 0x400, HasDefault = 0x1000 }
    [Flags]
    public enum BindingFlags
    {
        Default = 0, IgnoreCase = 1, DeclaredOnly = 2, Instance = 4, Static = 8, Public = 16, NonPublic = 32, FlattenHierarchy = 64,
        InvokeMethod = 0x100, CreateInstance = 0x200, GetField = 0x400, SetField = 0x800, GetProperty = 0x1000, SetProperty = 0x2000,
    }
    public abstract class Binder { }
    public struct ParameterModifier { }

    // Tek sahte assembly: tum program (AOT'ta assembly siniri yok). Type.Assembly / AppDomain.GetAssemblies bunu dondurur.
    public class Assembly
    {
        internal Assembly() { }
        public extern Type[] GetTypes();     // uretilen tum class/struct/enum/interface descriptor'lari (dizi/runtime kokleri haric)
        public string FullName { get { return "Program, Version=0.0.0.0"; } }
        public override string ToString() { return FullName; }
        public static extern Assembly GetExecutingAssembly();
        public Type GetType(string name) { return Type.GetType(name); }
        public static bool operator ==(Assembly a, Assembly b) { return (object)a == (object)b; }
        public static bool operator !=(Assembly a, Assembly b) { return (object)a != (object)b; }
        public override bool Equals(object o) { return (object)this == o; }
        public override int GetHashCode() { return 1; }
    }

    public class AmbiguousMatchException : Exception
    {
        public AmbiguousMatchException() : base("Ambiguous match found.") { }
        public AmbiguousMatchException(string message) : base(message) { }
    }

    public class MemberInfo
    {
        public long handle;
        public extern virtual string Name { get; }
        public extern Type DeclaringType { get; }
        // Custom attribute'lar (uretilen tablo: tip + lazy kurucu). inherit yalniz Type icin base zincirini gezer.
        public extern bool IsDefined(Type attributeType, bool inherit);
        public extern object[] GetCustomAttributes(Type attributeType, bool inherit);
        public extern object[] GetCustomAttributes(bool inherit);
    }

    // .NET: System.Reflection.CustomAttributeExtensions (uzanti metotlari; derleyici bu tipe MemberRef uretir)
    public static class CustomAttributeExtensions
    {
        // .NET: inherit varsayilani TRUE; birden fazla eslesme AmbiguousMatchException
        public static T GetCustomAttribute<T>(this MemberInfo element) where T : Attribute { return GetCustomAttribute<T>(element, true); }
        public static T GetCustomAttribute<T>(this MemberInfo element, bool inherit) where T : Attribute
        {
            var all = element.GetCustomAttributes(typeof(T), inherit);
            if (all.Length == 0) return null;
            if (all.Length > 1) throw new AmbiguousMatchException("Multiple custom attributes of the same type found.");
            return (T)all[0];
        }
        public static Attribute GetCustomAttribute(this MemberInfo element, Type attributeType)
        {
            var all = element.GetCustomAttributes(attributeType, true);
            if (all.Length == 0) return null;
            if (all.Length > 1) throw new AmbiguousMatchException("Multiple custom attributes of the same type found.");
            return (Attribute)all[0];
        }
        public static Collections.Generic.IEnumerable<T> GetCustomAttributes<T>(this MemberInfo element) where T : Attribute
        {
            var all = element.GetCustomAttributes(typeof(T), false);
            var list = new Collections.Generic.List<T>(all.Length);
            for (int i = 0; i < all.Length; i++) list.Add((T)all[i]);
            return list;
        }
        public static Collections.Generic.IEnumerable<Attribute> GetCustomAttributes(this MemberInfo element, bool inherit)
        {
            var all = element.GetCustomAttributes(inherit);
            var list = new Collections.Generic.List<Attribute>(all.Length);
            for (int i = 0; i < all.Length; i++) list.Add((Attribute)all[i]);
            return list;
        }
        public static bool IsDefined(this MemberInfo element, Type attributeType) { return element.IsDefined(attributeType, false); }
    }

    public class FieldInfo : MemberInfo
    {
        public extern Type FieldType { get; }
        public extern bool IsStatic { get; }
        public extern bool IsInitOnly { get; }
        public extern FieldAttributes Attributes { get; }
        public bool IsPublic { get { return (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public; } }
        public bool IsPrivate { get { return (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private; } }
        public bool IsFamily { get { return (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Family; } }
        public bool IsAssembly { get { return (Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Assembly; } }
        public bool IsNotSerialized { get { return (Attributes & FieldAttributes.NotSerialized) != 0; } }
        public bool IsLiteral { get { return (Attributes & FieldAttributes.Literal) != 0; } }
        public extern object GetValue(object target);
        public extern void SetValue(object target, object value);
        // ---- AOT-ozel tipli erisim (docs/registry-removal.md Faz 4d): .NET'te karsiligi yok (editor expression-compile kullanir) ----
        public extern int Offset { get; }                                   // instance alan: nesne icinde bayt offset'i (GCHeader dahil); struct alani: struct icinde
        public static extern ref T RefAt<T>(object target, int offset);     // frontend intrinsic: *(T*)((char*)target + offset) — boxing yok
    }

    public class ParameterInfo
    {
        Type type; int position;
        public ParameterInfo(Type type, int position) { this.type = type; this.position = position; }
        public Type ParameterType { get { return type; } }
        public int Position { get { return position; } }
    }

    public class MethodBase : MemberInfo
    {
        public extern override string Name { get; } // yalin ad ("Scale", ".ctor")
        public extern bool IsStatic { get; }
        public extern bool IsVirtual { get; }
        public extern bool IsAbstract { get; }
        public extern bool IsConstructor { get; }
        public extern MethodAttributes Attributes { get; }
        public bool IsPublic { get { return (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public; } }
        public bool IsPrivate { get { return (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private; } }
        public bool IsFamily { get { return (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family; } }
        public extern int ParameterCount { get; } // this haric
        public extern Type GetParameterType(int index);
        public ParameterInfo[] GetParameters()
        {
            var r = new ParameterInfo[ParameterCount];
            for (int i = 0; i < r.Length; i++) r[i] = new ParameterInfo(GetParameterType(i), i);
            return r;
        }
        // Parametre tipleri birebir mi (GetConstructor(Type[]) / GetMethod(name, Type[]) icin)
        public bool MatchesParameters(Type[] types)
        {
            int n = types == null ? 0 : types.Length;
            if (n != ParameterCount) return false;
            for (int i = 0; i < n; i++)
                if (GetParameterType(i) != types[i]) return false;
            return true;
        }
        public bool SameParameters(MethodBase other)
        {
            if (other.ParameterCount != ParameterCount) return false;
            for (int i = 0; i < ParameterCount; i++)
                if (GetParameterType(i) != other.GetParameterType(i)) return false;
            return true;
        }
        public extern object Invoke(object obj, object[] parameters);
    }

    public class MethodInfo : MethodBase
    {
        public extern Type ReturnType { get; }
    }

    public class ConstructorInfo : MethodBase
    {
        // yeni nesne: tahsis + ctor (struct: kutulu)
        public extern object Invoke(object[] parameters);
    }

    public class PropertyInfo : MemberInfo
    {
        public extern Type PropertyType { get; }
        public extern PropertyAttributes Attributes { get; }
        // Compatibility surface for existing Digiplay code. CLR exposes this through GetMethod.
        public extern bool IsStatic { get; }
        public extern bool CanRead { get; }
        public extern bool CanWrite { get; }
        public MethodInfo GetMethod { get { return CanRead ? DeclaringType.GetMethod("get_" + Name) : null; } }
        public MethodInfo SetMethod { get { return CanWrite ? DeclaringType.GetMethod("set_" + Name) : null; } }
        public extern object GetValue(object target);
        public extern void SetValue(object target, object value);
    }
}
