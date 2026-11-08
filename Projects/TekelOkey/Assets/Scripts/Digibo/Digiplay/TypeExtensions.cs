using System;
using System.Collections.Generic;

namespace Digiplay
{
    public static class TypeExtensions
    {
        public static int ToInt(this object o)
        {
            if (o != null)
            {
                if (o is int)
                    return (int)o;
                else if (o is uint)
                    return (int)(uint)o;
                else if (o is float)
                    return (int)(float)o;
                else if (o is long)
                    return (int)(long)o;
                else if (o is bool)
                    return (bool)o ? 1 : 0;
                else
                {
                    double dVal = 0;
                    double.TryParse(o.ToString(), out dVal);
                    return (int)dVal;
                }
            }
            return 0;
        }

        public static int ToInt(this object obj, string key)
        {
            IDictionary<string, object> dict = obj as IDictionary<string, object>;
            if (dict != null)
            {
                object o;
                if(dict.TryGetValue(key, out o))
                    return o.ToInt();
            }
            return 0;
        }

        public static float ToFloat(this object o)
        {
            if (o != null)
            {
                if (o is int)
                    return (int)o;
                else if (o is uint)
                    return (float)(uint)o;
                else if (o is float)
                    return (float)o;
                else if (o is long)
                    return (float)(long)o;
                else
                {
                    float.TryParse(o.ToString(), out float dVal);
                    return (float)dVal;
                }
            }
            return 0;
        }

        // ToColor (Digiplay.Util.Parser.parseColor bagimli) PORT DISI: renk ayristirma motor tarafinda cozulecek.

        public static float ToFloat(this object obj, string key)
        {
            IDictionary<string, object> dict = obj as IDictionary<string, object>;
            if (dict != null)
            {
                object o;
                if (dict.TryGetValue(key, out o))
                    return o.ToFloat();
            }
            return 0;
        }

        public static long ToLong(this object val)
        {
            if (val != null)
            {
                if (val is int)
                    return (int)val;
                else if (val is uint)
                    return (long)(uint)val;
                else if (val is float)
                    return (long)(float)val;
                else if (val is long)
                    return (long)val;
                else
                {
                    long dVal = 0;
                    long.TryParse(val.ToString(), out dVal);
                    return (long)dVal;
                }
            }
            return 0;
        }

        public static long ToLong(this object obj, string key)
        {
            IDictionary<string, object> dict = obj as IDictionary<string, object>;
            if (dict != null)
            {
                object o;
                if (dict.TryGetValue(key, out o))
                    return o.ToLong();
            }
            return 0;
        }

        public static double ToDouble(this object val)
        {
            if (val != null)
            {
                if (val is int)
                    return (double)(int)val;
                else if (val is uint)
                    return (double)(uint)val;
                else if (val is float)
                    return (double)(float)val;
                else if (val is bool)
                    return (bool)val ? 1 : 0;
                else if (val is double)
                    return (double)val;
                else
                {
                    double dVal = 0;
                    double.TryParse(val.ToString(), out dVal);
                    return dVal;
                }
            }
            return 0;
        }

        public static double ToDouble(this object obj, string key)
        {
            IDictionary<string, object> dict = obj as IDictionary<string, object>;
            if (dict != null)
            {
                object o;
                if (dict.TryGetValue(key, out o))
                    return o.ToLong();
            }
            return 0;
        }

        public static double ToDoubleFromString(this object obj, string key)
        {
            var dict = obj as IDictionary<string, object>;
            if (dict != null)
            {
                object o;
                if (dict.TryGetValue(key, out o))
                {
                    double dVal = 0;
                    double.TryParse(o.ToString(), out dVal);
                    return dVal;
                }  
            }
            return 0;
        }

        public static bool ToBool(this object o)
        {
            if (o != null)
            {
                if (o is bool)
                    return (bool)o;
                if (o is string)
                {
                    var str = (string)o;
                    return str == "True" || str == "true" || str == "yes";
                }
                else return o.ToInt() != 0;
            }
            return false;
        }

        public static bool ToBool(this object obj, string key)
        {
            IDictionary<string, object> dict = obj as IDictionary<string, object>;
            if (dict != null)
            {
                object o;
                if (dict.TryGetValue(key, out o))
                    return o.ToBool();
            }
            return false;
        }

#pragma warning disable
        public static string ToString(this object obj, string key)
        {
            IDictionary<string, object> dict = obj as IDictionary<string, object>;
            if (dict != null)
            {
                object o;
                if (dict.TryGetValue(key, out o))
                    return o != null ? o.ToString() : null;
            }
            return null;
        }
#pragma warning restore

        public static List<T> ToList<T>(this IDictionary<string, object> dict, string key)
        {
            object o = null;
            dict.TryGetValue(key, out o);
            return o != null ? o as List<T> : null;
        }

        public static List<int> ToIntList(this IDictionary<string, object> dict, string key)
        {
            object o = null;
            dict.TryGetValue(key, out o);
            List<object> objList = (List<object>) o;
            if (objList == null) objList = new List<object>();

            List<int> intList = new List<int>();
            objList.ForEach((obj) => intList.Add(obj.ToInt()));
            return intList;
        }

        public static Dynamic ToDynamic(this object obj, string key)
        {
            IDictionary<string, object> dict = obj as IDictionary<string, object>;
            if (dict != null)
            {
                object o;
                if (dict.TryGetValue(key, out o))
                    return o as Dynamic;
            }
            return null;
        }

        public static object ToObject(this object obj, string key)
        {
            IDictionary<string, object> dict = obj as IDictionary<string, object>;
            if (dict != null)
            {
                object o;
                if (dict.TryGetValue(key, out o))
                    return o;
            }
            return null;
        }

        // Extension method, call for any object, eg "if (x.IsNumeric())..."
        public static bool IsNumeric(this object x) { return x != null && IsNumeric(x.GetType()); }

        // Method where you know the type of the object
        public static bool IsNumeric(Type type) { return IsNumeric(type, Type.GetTypeCode(type)); }

        // Method where you know the type and the type code of the object
        // PORT NOTU: TypeCode.Decimal kosulu cikarildi — AOT corelib'de decimal yok (TypeCode enum'unda da tanimsiz);
        // Digibo modelleri decimal kullanmaz (para long).
        public static bool IsNumeric(Type type, TypeCode typeCode) { return type.IsPrimitive && typeCode != TypeCode.Object && typeCode != TypeCode.Boolean && typeCode != TypeCode.Char; }

        public static bool ContainsKey(this object obj, string key)
        {
            IDictionary<string, object> dict = obj as IDictionary<string, object>;
            return dict != null && dict.ContainsKey(key);
        }

        // Unbind (eski Digiplay.Binding sarmalayicisi) PORT DISI: yeni motorda model degeri sarilmaz, duz nesnedir.
    }
}