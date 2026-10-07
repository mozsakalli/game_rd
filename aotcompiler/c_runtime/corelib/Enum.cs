namespace System
{
    // .NET sekli: enum tipleri bundan turer (Roslyn zorunlu). Statik yuzey (TryParse) ayni.
    public abstract class Enum : ValueType
    {
        public static extern object ToObject(Type enumType, object value);

        public static extern bool TryParse(string value, Type enumType, out object result);

        public static bool TryParse<TEnum>(string value, out TEnum result)
        {
            object parsed;
            if (TryParse(value, typeof(TEnum), out parsed))
            {
                result = (TEnum)parsed;
                return true;
            }
            result = default(TEnum);
            return false;
        }
    }
}