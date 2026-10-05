// corelib: metadata tablolu AOT reflection. Lookup adi sabit descriptor havuzunda aranir;
// FieldInfo/PropertyInfo wrapper'lari lazy cache'lidir. Method invoke, attribute ve indexer
// reflection bilincli olarak kapsam disidir.
namespace System.Reflection
{
    class MemberInfo
    {
        long handle;
        extern virtual string Name { get; }
        extern Type DeclaringType { get; }
    }

    class FieldInfo : MemberInfo
    {
        extern Type FieldType { get; }
        extern bool IsStatic { get; }
        extern bool IsInitOnly { get; }
        extern object GetValue(object target);
        extern void SetValue(object target, object value);
    }

    class MethodInfo : MethodBase
    {
        bool isStatic;
        public MethodInfo(bool isStatic) { this.isStatic = isStatic; }
        public override bool IsStatic { get { return isStatic; } }
    }

    class MethodBase : MemberInfo
    {
        public virtual bool IsStatic { get { return false; } }
    }

    class PropertyInfo : MemberInfo
    {
        extern Type PropertyType { get; }
        // Compatibility surface for existing Digiplay code. CLR exposes this through GetMethod.
        extern bool IsStatic { get; }
        extern bool CanRead { get; }
        extern bool CanWrite { get; }
        public MethodInfo GetMethod { get { return CanRead ? new MethodInfo(IsStatic) : null; } }
        public MethodInfo SetMethod { get { return CanWrite ? new MethodInfo(IsStatic) : null; } }
        extern object GetValue(object target);
        extern void SetValue(object target, object value);
    }
}
