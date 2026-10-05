// corelib: System.Type (reflection cekirdegi). Wrapper nesnesi runtime Type descriptor'ini sarar
// (handle = Type*, ILK alan - digitoyengine_type_wrapper sozlesmesi). Ayni tip = AYNI nesne (kimlik ==).
// BILINCLI SINIR: struct'larin runtime Type'i yok. typeof'ta kullanilan somut array tipleri
// ayri AOT kimlik descriptor'lari alir; array nesnelerinin GC descriptor'i yine ortaktir. Method invoke yoktur; Activator yalniz
// AOT tablosundaki somut class'larin parametresiz olusturulmasini destekler.
namespace System
{
    enum TypeCode
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

    class Type : Reflection.MemberInfo
    {
        extern override string Name { get; }
        extern string FullName { get; }
        extern Type BaseType { get; }
        extern bool IsPrimitive { get; }
        extern bool IsEnum { get; }
        extern Type[] GetGenericArguments();
        extern static TypeCode GetTypeCode(Type type);
        extern Reflection.FieldInfo GetField(string name);
        extern Reflection.FieldInfo[] GetFields();
        extern Reflection.PropertyInfo GetProperty(string name);
        extern bool IsAssignableFrom(Type type);
        bool IsInstanceOfType(object o) { return o != null && IsAssignableFrom(o.GetType()); }
    }
}
