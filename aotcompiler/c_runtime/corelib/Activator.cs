namespace System
{
    public class Activator
    {
        public static extern object CreateInstanceByName(string typeName);

        public static Runtime.Remoting.ObjectHandle CreateInstance(string assemblyName, string typeName)
        {
            object value = CreateInstanceByName(typeName);
            return value == null ? null : new Runtime.Remoting.ObjectHandle(value);
        }

        public static object CreateInstance(Type type)
        {
            return CreateInstanceByName(type.FullName);
        }

        // nonPublic: AOT'ta erisim denetimi yok -> ayni yol (private parametresiz ctor da bulunur)
        public static object CreateInstance(Type type, bool nonPublic)
        {
            return CreateInstanceByName(type.FullName);
        }

        public static T CreateInstance<T>()
        {
            return (T)CreateInstance(typeof(T));
        }
    }
}