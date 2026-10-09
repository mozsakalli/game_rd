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
        public extern Reflection.TypeAttributes Attributes { get; }
        public bool IsSerializable { get { return (Attributes & Reflection.TypeAttributes.Serializable) != 0; } }
        public bool IsPublic { get { return (Attributes & Reflection.TypeAttributes.VisibilityMask) == Reflection.TypeAttributes.Public; } }
        public bool IsSealed { get { return (Attributes & Reflection.TypeAttributes.Sealed) != 0; } }
        public bool IsNested { get { return (Attributes & Reflection.TypeAttributes.VisibilityMask) >= Reflection.TypeAttributes.NestedPublic; } }
        public extern bool IsArray { get; }            // T[] kimlik descriptor'u (typeof(T[]) / dizi GetType())
        public extern Type GetElementType();           // dizi eleman tipi; dizi degilse null
        public extern int GetArrayRank();
        public extern Type[] GetGenericArguments();
        public extern bool IsGenericType { get; }               // kapali generic ornek (List<int>) ya da acik tanim
        public extern bool IsGenericTypeDefinition { get; }     // acik tanim descriptor'u (typeof(List<>))
        public extern Type GetGenericTypeDefinition();          // ornek -> acik tanim; tanim -> kendisi
        public extern Reflection.Assembly Assembly { get; }     // tek sahte assembly (tum program)
        public extern static TypeCode GetTypeCode(Type type);

        // ---- uye listeleri: ham listeler C'den (base zinciri dahil, tum erisimler), .NET filtre kurallari burada ----
        extern Reflection.FieldInfo[] GetFieldsRaw();
        extern Reflection.PropertyInfo[] GetPropertiesRaw();
        extern Reflection.MethodInfo[] GetMethodsRaw();
        public extern Reflection.ConstructorInfo[] GetConstructors(); // yalniz bildirilen (.NET: public instance ctor'lar + bizde hepsi)

        const Reflection.BindingFlags DefaultLookup = Reflection.BindingFlags.Instance | Reflection.BindingFlags.Static | Reflection.BindingFlags.Public;

        // .NET uye secimi: erisim (Public/NonPublic), kapsam (Instance/Static), kalitim (DeclaredOnly; kalitilan private
        // asla; kalitilan static yalniz FlattenHierarchy). Default bayrak: Public|Instance|Static.
        static bool Pass(bool isPublic, bool isPrivate, bool isStatic, Type declaring, Type self, Reflection.BindingFlags f)
        {
            if (isPublic ? (f & Reflection.BindingFlags.Public) == 0 : (f & Reflection.BindingFlags.NonPublic) == 0) return false;
            if (isStatic ? (f & Reflection.BindingFlags.Static) == 0 : (f & Reflection.BindingFlags.Instance) == 0) return false;
            if (declaring != self)
            {
                if ((f & Reflection.BindingFlags.DeclaredOnly) != 0) return false;
                if (isPrivate) return false;
                if (isStatic && (f & Reflection.BindingFlags.FlattenHierarchy) == 0) return false;
            }
            return true;
        }

        public Reflection.FieldInfo[] GetFields() { return GetFields(DefaultLookup); }
        public Reflection.FieldInfo[] GetFields(Reflection.BindingFlags flags)
        {
            var all = GetFieldsRaw();
            var list = new Collections.Generic.List<Reflection.FieldInfo>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                var f = all[i];
                if (Pass(f.IsPublic, f.IsPrivate, f.IsStatic, f.DeclaringType, this, flags)) list.Add(f);
            }
            return list.ToArray();
        }
        public Reflection.FieldInfo GetField(string name) { return GetField(name, DefaultLookup); }
        public Reflection.FieldInfo GetField(string name, Reflection.BindingFlags flags)
        {
            var all = GetFieldsRaw();
            for (int i = 0; i < all.Length; i++)
            {
                var f = all[i];
                if (f.Name == name && Pass(f.IsPublic, f.IsPrivate, f.IsStatic, f.DeclaringType, this, flags)) return f;
            }
            return null;
        }

        static bool PropPass(Reflection.PropertyInfo p, Type self, Reflection.BindingFlags f)
        {
            var g = p.GetMethod; var s = p.SetMethod;
            bool isPublic = (g != null && g.IsPublic) || (s != null && s.IsPublic);
            bool isPrivate = (g == null || g.IsPrivate) && (s == null || s.IsPrivate);
            return Pass(isPublic, isPrivate, p.IsStatic, p.DeclaringType, self, f);
        }
        public Reflection.PropertyInfo[] GetProperties() { return GetProperties(DefaultLookup); }
        public Reflection.PropertyInfo[] GetProperties(Reflection.BindingFlags flags)
        {
            var all = GetPropertiesRaw(); // turetilenden base'e; override edilen base property'si gizlenir (ayni ad)
            var list = new Collections.Generic.List<Reflection.PropertyInfo>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                var p = all[i];
                if (!PropPass(p, this, flags)) continue;
                bool hidden = false;
                for (int k = 0; k < list.Count; k++) if (list[k].Name == p.Name) { hidden = true; break; }
                if (!hidden) list.Add(p);
            }
            return list.ToArray();
        }
        public Reflection.PropertyInfo GetProperty(string name) { return GetProperty(name, DefaultLookup); }
        public Reflection.PropertyInfo GetProperty(string name, Reflection.BindingFlags flags)
        {
            var all = GetPropertiesRaw();
            for (int i = 0; i < all.Length; i++)
                if (all[i].Name == name && PropPass(all[i], this, flags)) return all[i];
            return null;
        }

        public Reflection.MethodInfo[] GetMethods() { return GetMethods(DefaultLookup); }
        public Reflection.MethodInfo[] GetMethods(Reflection.BindingFlags flags)
        {
            var all = GetMethodsRaw(); // turetilenden base'e; override edilen base metodu gizlenir (ad + parametreler)
            var list = new Collections.Generic.List<Reflection.MethodInfo>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                var m = all[i];
                if (!Pass(m.IsPublic, m.IsPrivate, m.IsStatic, m.DeclaringType, this, flags)) continue;
                bool hidden = false;
                for (int k = 0; k < list.Count; k++)
                    if (list[k].Name == m.Name && list[k].SameParameters(m)) { hidden = true; break; }
                if (!hidden) list.Add(m);
            }
            return list.ToArray();
        }
        public Reflection.MethodInfo GetMethod(string name) { return GetMethod(name, DefaultLookup, null, null, null); }
        public Reflection.MethodInfo GetMethod(string name, Reflection.BindingFlags flags) { return GetMethod(name, flags, null, null, null); }
        public Reflection.MethodInfo GetMethod(string name, Type[] types) { return GetMethod(name, DefaultLookup, null, types, null); }
        // types null = parametre filtresi yok (ilk eslesen; .NET'te coklu eslesme AmbiguousMatch - bizde ilk)
        public Reflection.MethodInfo GetMethod(string name, Reflection.BindingFlags flags, Reflection.Binder binder, Type[] types, Reflection.ParameterModifier[] modifiers)
        {
            var all = GetMethodsRaw();
            for (int i = 0; i < all.Length; i++)
            {
                var m = all[i];
                if (m.Name != name) continue;
                if (!Pass(m.IsPublic, m.IsPrivate, m.IsStatic, m.DeclaringType, this, flags)) continue;
                if (types != null && !m.MatchesParameters(types)) continue;
                return m;
            }
            return null;
        }
        public Reflection.ConstructorInfo GetConstructor(Type[] types)
        {
            var all = GetConstructors();
            for (int i = 0; i < all.Length; i++)
                if (all[i].MatchesParameters(types)) return all[i];
            return null;
        }
        public Reflection.ConstructorInfo GetConstructor(Reflection.BindingFlags flags, Reflection.Binder binder, Type[] types, Reflection.ParameterModifier[] modifiers)
        {
            var all = GetConstructors();
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c.IsStatic) continue;
                if (c.IsPublic ? (flags & Reflection.BindingFlags.Public) == 0 : (flags & Reflection.BindingFlags.NonPublic) == 0) continue;
                if (types != null && !c.MatchesParameters(types)) continue;
                return c;
            }
            return null;
        }
        public extern bool IsAssignableFrom(Type type);
        public bool IsInstanceOfType(object o) { return o != null && IsAssignableFrom(o.GetType()); }
        public static readonly Type[] EmptyTypes = new Type[0];
    }
}
