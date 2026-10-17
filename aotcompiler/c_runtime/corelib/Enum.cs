namespace System
{
    // .NET sekli: enum tipleri bundan turer (Roslyn zorunlu). Statik yuzey (TryParse) ayni.
    public abstract class Enum : ValueType
    {
        public static extern object ToObject(Type enumType, object value);
        public static object ToObject(Type enumType, long value) { return ToObject(enumType, (object)value); } // C govdesi long kutusunu acar (rootid 6)
        public static object ToObject(Type enumType, int value) { return ToObject(enumType, (object)value); }

        public static extern bool TryParse(Type enumType, string value, out object result); // .NET sirasi (Type, string, out)

        // Tanimli uye mi: kutulanan degerin ToString'i uye adi (tanimli) ya da sayi (tanimsiz) doner; ad geri parse edilirse tanimlidir.
        public static bool IsDefined(Type enumType, object value)
        {
            if (enumType == null) throw new ArgumentNullException("enumType");
            if (value == null) throw new ArgumentNullException("value");
            if (value is string name) { object tmp; return TryParse(enumType, name, out tmp); }
            string s = ToObject(enumType, value).ToString();
            object parsed;
            return TryParse(enumType, s, out parsed);
        }

        public static bool TryParse<TEnum>(string value, out TEnum result)
        {
            object parsed;
            if (TryParse(typeof(TEnum), value, out parsed))
            {
                result = (TEnum)parsed;
                return true;
            }
            result = default(TEnum);
            return false;
        }
    }
}