// corelib: metadata tablolu AOT reflection. Lookup adi sabit descriptor havuzunda aranir;
// FieldInfo/PropertyInfo wrapper'lari lazy cache'lidir. Method invoke, attribute ve indexer
// reflection bilincli olarak kapsam disidir.
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

    public class MethodInfo : MethodBase
    {
        public bool isStatic;
        public MethodInfo(bool isStatic) { this.isStatic = isStatic; }
        public override bool IsStatic { get { return isStatic; } }
    }

    public class MethodBase : MemberInfo
    {
        public virtual bool IsStatic { get { return false; } }
    }

    public class PropertyInfo : MemberInfo
    {
        public extern Type PropertyType { get; }
        // Compatibility surface for existing Digiplay code. CLR exposes this through GetMethod.
        public extern bool IsStatic { get; }
        public extern bool CanRead { get; }
        public extern bool CanWrite { get; }
        public MethodInfo GetMethod { get { return CanRead ? new MethodInfo(IsStatic) : null; } }
        public MethodInfo SetMethod { get { return CanWrite ? new MethodInfo(IsStatic) : null; } }
        public extern object GetValue(object target);
        public extern void SetValue(object target, object value);
    }
}
