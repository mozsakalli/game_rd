namespace System
{
    public class Convert
    {
        public static extern int ToInt32(string value, int fromBase);
        public static extern uint ToUInt32(int value);

        // Kutulu deger donusumleri (IConvertible yok: tip-tip dallanma). Enum kutusu underlying int'e acilir (alias = int).
        public static int ToInt32(object value)
        {
            if (value == null) return 0;
            if (value is int i) return i;
            if (value is long l) { if (l < int.MinValue || l > int.MaxValue) throw new OverflowException("Value was either too large or too small for an Int32."); return (int)l; }
            if (value is short s) return s;
            if (value is ushort us) return us;
            if (value is byte b) return b;
            if (value is sbyte sb) return sb;
            if (value is char c) return c;
            if (value is bool bo) return bo ? 1 : 0;
            if (value is uint ui) { if (ui > int.MaxValue) throw new OverflowException("Value was either too large or too small for an Int32."); return (int)ui; }
            if (value is ulong ul) { if (ul > int.MaxValue) throw new OverflowException("Value was either too large or too small for an Int32."); return (int)ul; }
            if (value is double d) return (int)Math.Round(d);
            if (value is float f) return (int)Math.Round((double)f);
            if (value is string str) { int r; if (!int.TryParse(str, out r)) throw new FormatException("Input string was not in a correct format."); return r; }
            if (value.GetType().IsEnum) return (int)value;
            throw new InvalidCastException("Unable to cast object of type '" + value.GetType().FullName + "' to Int32.");
        }
        public static long ToInt64(object value)
        {
            if (value == null) return 0;
            if (value is long l) return l;
            if (value is ulong ul) { if (ul > long.MaxValue) throw new OverflowException("Value was either too large or too small for an Int64."); return (long)ul; }
            if (value is uint ui) return ui;
            if (value is double d) return (long)Math.Round(d);
            if (value is float f) return (long)Math.Round((double)f);
            if (value is string str) { long r; if (!long.TryParse(str, out r)) throw new FormatException("Input string was not in a correct format."); return r; }
            return ToInt32(value);
        }
        public static double ToDouble(object value)
        {
            if (value == null) return 0;
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is long l) return l;
            if (value is ulong ul) return ul;
            if (value is uint ui) return ui;
            if (value is string str) { double r; if (!double.TryParse(str, out r)) throw new FormatException("Input string was not in a correct format."); return r; }
            return ToInt32(value);
        }
        public static bool ToBoolean(object value)
        {
            if (value == null) return false;
            if (value is bool b) return b;
            if (value is string s) { if (s == "True" || s == "true") return true; if (s == "False" || s == "false") return false; throw new FormatException("String was not recognized as a valid Boolean."); }
            return ToDouble(value) != 0;
        }
        public static string ToString(object value) { return value == null ? "" : value.ToString(); }

        // Hedef tipe donusum (TypeCode uzerinden; enum -> Enum.ToObject; referans tipler: atanabilirse kendisi).
        public static object ChangeType(object value, Type conversionType)
        {
            if (conversionType == null) throw new ArgumentNullException("conversionType");
            if (value == null)
            {
                if (conversionType.IsValueType) throw new InvalidCastException("Null object cannot be converted to a value type.");
                return null;
            }
            if (conversionType.IsEnum) return Enum.ToObject(conversionType, value);
            switch (Type.GetTypeCode(conversionType))
            {
                case TypeCode.Int32: return ToInt32(value);
                case TypeCode.Int64: return ToInt64(value);
                case TypeCode.Double: return ToDouble(value);
                case TypeCode.Single: return (float)ToDouble(value);
                case TypeCode.Boolean: return ToBoolean(value);
                case TypeCode.String: return ToString(value);
                case TypeCode.Int16: return (short)ToInt32(value);
                case TypeCode.UInt16: return (ushort)ToInt32(value);
                case TypeCode.Byte: return (byte)ToInt32(value);
                case TypeCode.SByte: return (sbyte)ToInt32(value);
                case TypeCode.Char: return (char)ToInt32(value);
                case TypeCode.UInt32: return (uint)ToInt64(value);
                case TypeCode.UInt64: return (ulong)ToInt64(value);
            }
            if (conversionType.IsInstanceOfType(value)) return value;
            throw new InvalidCastException("Invalid cast from '" + value.GetType().FullName + "' to '" + conversionType.FullName + "'.");
        }

        const string Base64Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

        public static string ToBase64String(byte[] inArray)
        {
            if (inArray == null) throw new ArgumentNullException("inArray");
            return ToBase64String(inArray, 0, inArray.Length);
        }
        public static string ToBase64String(byte[] inArray, int offset, int length)
        {
            if (inArray == null) throw new ArgumentNullException("inArray");
            if (offset < 0 || length < 0 || offset + length > inArray.Length) throw new ArgumentOutOfRangeException("length");
            // Cikis boyutu bastan belli: dogrudan char[] + tek string tahsisi (StringBuilder/Concat yok)
            var outChars = new char[(length + 2) / 3 * 4];
            int i = offset, end = offset + length, o = 0;
            while (end - i >= 3)
            {
                int v = (inArray[i] << 16) | (inArray[i + 1] << 8) | inArray[i + 2];
                outChars[o] = Base64Chars[(v >> 18) & 63]; outChars[o + 1] = Base64Chars[(v >> 12) & 63];
                outChars[o + 2] = Base64Chars[(v >> 6) & 63]; outChars[o + 3] = Base64Chars[v & 63];
                i += 3; o += 4;
            }
            int rest = end - i;
            if (rest == 1)
            {
                int v = inArray[i] << 16;
                outChars[o] = Base64Chars[(v >> 18) & 63]; outChars[o + 1] = Base64Chars[(v >> 12) & 63]; outChars[o + 2] = '='; outChars[o + 3] = '=';
            }
            else if (rest == 2)
            {
                int v = (inArray[i] << 16) | (inArray[i + 1] << 8);
                outChars[o] = Base64Chars[(v >> 18) & 63]; outChars[o + 1] = Base64Chars[(v >> 12) & 63]; outChars[o + 2] = Base64Chars[(v >> 6) & 63]; outChars[o + 3] = '=';
            }
            return outChars.Length == 0 ? "" : new string(outChars);
        }

        // .NET gibi: bosluk karakterleri yok sayilir, '=' dolgusu dogrulanir, gecersiz karakter -> FormatException.
        public static byte[] FromBase64String(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            int n = 0, pad = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') continue;
                if (c == '=') { pad++; continue; }
                if (pad > 0 || DecodeBase64Char(c) < 0) throw new FormatException("The input is not a valid Base-64 string as it contains a non-base 64 character, more than two padding characters, or an illegal character among the padding characters.");
                n++;
            }
            if (pad > 2 || (n + pad) % 4 != 0) throw new FormatException("Invalid length for a Base-64 char array or string.");
            var result = new byte[n * 6 / 8];
            int acc = 0, bits = 0, o = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int d = DecodeBase64Char(s[i]);
                if (d < 0) continue; // bosluk / '='
                acc = (acc << 6) | d; bits += 6;
                if (bits >= 8) { bits -= 8; result[o++] = (byte)((acc >> bits) & 0xFF); }
            }
            return result;
        }
        static int DecodeBase64Char(char c)
        {
            if (c >= 'A' && c <= 'Z') return c - 'A';
            if (c >= 'a' && c <= 'z') return c - 'a' + 26;
            if (c >= '0' && c <= '9') return c - '0' + 52;
            if (c == '+') return 62;
            if (c == '/') return 63;
            return -1;
        }
    }
}