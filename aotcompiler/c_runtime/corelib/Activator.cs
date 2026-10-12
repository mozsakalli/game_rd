namespace System
{
    public class Activator
    {
        public static extern object CreateInstanceByName(string typeName);
        // Type handle ile (ad aramasi yok): parametresiz ctor; struct/primitive -> kutulanmis default(T) (.NET semantigi).
        public static extern object CreateInstanceOf(Type type);

        public static Runtime.Remoting.ObjectHandle CreateInstance(string assemblyName, string typeName)
        {
            object value = CreateInstanceByName(typeName);
            return value == null ? null : new Runtime.Remoting.ObjectHandle(value);
        }

        public static object CreateInstance(Type type)
        {
            return CreateInstanceOf(type);
        }

        // nonPublic: AOT'ta erisim denetimi yok -> ayni yol (private parametresiz ctor da bulunur)
        public static object CreateInstance(Type type, bool nonPublic)
        {
            return CreateInstanceOf(type);
        }

        public static T CreateInstance<T>()
        {
            return (T)CreateInstance(typeof(T));
        }
    }
}