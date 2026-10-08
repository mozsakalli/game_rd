// corelib: metadata tablolu AOT reflection. Lookup adi sabit descriptor havuzunda aranir;
// Field/Property/Method wrapper'lari lazy cache'lidir (handle = runtime kaydi, ILK alan sozlesmesi).
// Invoke = runtime sekil thunk'u (ref/out parametre desteklenmez). Attribute ve indexer reflection kapsam disidir.
namespace System.Reflection
{
    public class MemberInfo
    {
        public long handle;
        public extern virtual string Name { get; }
        public extern Type DeclaringType { get; }
    }

    public class FieldInfo : MemberInfo
    {
        public extern Type FieldType { get; }
        public extern bool IsStatic { get; }
        public extern bool IsInitOnly { get; }
        public extern object GetValue(object target);
        public extern void SetValue(object target, object value);
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
