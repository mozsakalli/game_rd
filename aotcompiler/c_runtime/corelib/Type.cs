// corelib: System.Type (reflection cekirdegi). Wrapper nesnesi runtime Type descriptor'ini sarar
// (handle = Type*, ILK alan - digitoyengine_type_wrapper sozlesmesi). Ayni tip = AYNI nesne (kimlik ==).
// BILINCLI SINIR: struct'larin runtime Type'i yok. typeof'ta kullanilan somut array tipleri
// ayri AOT kimlik descriptor'lari alir; array nesnelerinin GC descriptor'i yine ortaktir. Method invoke yoktur; Activator yalniz
// AOT tablosundaki somut class'larin parametresiz olusturulmasini destekler.
namespace System
{
    public enum TypeCode
    {
        Empty = 0,
        Object = 1,
        Boolean = 3,
        Char = 4,
        SByte = 5,
        Byte = 6,
        Int16 = 7,
        UInt16 = 8,
        Int32 = 9,
        UInt32 = 10,
        Int64 = 11,
        UInt64 = 12,
        Single = 13,
        Double = 14,
        String = 18
    }

    public class Type : Reflection.MemberInfo
    {
        public extern static Type GetTypeFromHandle(RuntimeTypeHandle handle); // typeof(T) lowering (Roslyn well-known)
        public extern static Type GetType(string fullName); // tip tablosunda ada gore (null = yok)
        public extern override string Name { get; }
        public extern string FullName { get; }
        public extern Type BaseType { get; }
        public extern bool IsPrimitive { get; }
        public extern bool IsEnum { get; }
        public extern bool IsValueType { get; }
        public extern bool IsInterface { get; }
        public extern bool IsAbstract { get; }
        public bool IsClass { get { return !IsValueType && !IsInterface; } }
        public extern Type[] GetGenericArguments();
        public extern static TypeCode GetTypeCode(Type type);
        public extern Reflection.FieldInfo GetField(string name);
        public extern Reflection.FieldInfo[] GetFields();
        public extern Reflection.PropertyInfo GetProperty(string name);
        public extern Reflection.PropertyInfo[] GetProperties();
        public extern Reflection.MethodInfo GetMethod(string name);
        public extern Reflection.MethodInfo[] GetMethods();
        public extern Reflection.ConstructorInfo[] GetConstructors();
        public Reflection.ConstructorInfo GetConstructor(Type[] types)
        {
            var all = GetConstructors();
            for (int i = 0; i < all.Length; i++)
                if (all[i].MatchesParameters(types)) return all[i];
            return null;
        }
        public extern bool IsAssignableFrom(Type type);
        public bool IsInstanceOfType(object o) { return o != null && IsAssignableFrom(o.GetType()); }
        public static readonly Type[] EmptyTypes = new Type[0];
    }
}
