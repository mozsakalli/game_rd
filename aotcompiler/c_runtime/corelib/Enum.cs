namespace System
{
    static class Enum
    {
        public static extern object ToObject(Type enumType, object value);

        static extern bool TryParse(string value, Type enumType, out object result);

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