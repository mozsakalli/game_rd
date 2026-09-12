using System;

namespace UnityEngine.Scripting
{
    /// <summary>
    /// Marks a method whose life is tied to a specific type:
    ///   1. Any type references in this method are not marked as 'uses' to the UnityLinker
    ///      (they won't keep a type from being stripped out)
    ///   2. If the named type survives marking (preserved by any mechanism — scenes, [Preserve], link.xml,
    ///       static reference), this method is automatically preserved as well.
    ///   3. If the named type is stripped, this method body is stubbed, taking the typeof(T) reference in its body with it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class StubBodyIfTypeNotUsedAttribute : Attribute
    {
        public StubBodyIfTypeNotUsedAttribute(Type preserveIfMarked) { }
        // For types not accessible at compile time. Format follows XML doc-signature convention:
        // nested types separated by '.', generics via `arity (e.g. "Ns.OuterType.Nested`1").
        // Assembly defaults to the declaring assembly of the annotated method.
        public StubBodyIfTypeNotUsedAttribute(string preserveIfMarkedTypeName) { }
        // Same as above with an explicit assembly short name.
        public StubBodyIfTypeNotUsedAttribute(string preserveIfMarkedTypeName, string assemblyName) { }
    }
}
