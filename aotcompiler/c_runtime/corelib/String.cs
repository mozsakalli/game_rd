// corelib: System.String (degismez UTF-16). Bu bildirim YENI tip yaratmaz - uyeler runtime
// tipine (Primitive.String / C: VmString+vmstring_type; VM: .NET string degeri) baglanir.
// YUZEY C# UYUMLU tutulur: C# API'sinde olmayan uye EKLENMEZ (kacak yok).
// extern = govde c_runtime/corelib.c'de (C, mangled adla) + source/Intrinsics.cs'te (VM).
// this[int] indexer'i [IndexerName("Chars")] ile get_Chars adini korur (C sembolu sabit).
// operator + (string,int)/(int,string)/(string,char): C#'in ortuk ToString birlestirmesinin
// dogrudan karsiligi (yuzeyde ayri operator gorunur ama davranis C# ile ayni).
namespace System
{
    public enum StringComparison
    {
        CurrentCulture = 0,
        CurrentCultureIgnoreCase = 1,
        InvariantCulture = 2,
        InvariantCultureIgnoreCase = 3,
        Ordinal = 4,
        OrdinalIgnoreCase = 5
    }

    public class String
    {
        public extern int Length { get; }
        [System.Runtime.CompilerServices.IndexerName("Chars")] // metadata adi get_Chars: C sembolu System_String_get_Chars_Int
        public extern char this[int index] { get; }
        public extern string Substring(int startIndex);
        public extern string Substring(int startIndex, int length);
        public extern string Replace(string oldValue, string newValue);
        public extern string Replace(char oldChar, char newChar);
        public extern string[] Split(string separator);
        public extern string[] Split(string separator, int count);
        public extern string[] Split(char separator);
        public extern string Trim();
        public extern string ToLower();
        public extern string ToLowerInvariant();
        public extern string ToUpper();
        public extern char[] ToCharArray();
        public extern static bool IsNullOrEmpty(string value);
        public extern static bool IsNullOrWhiteSpace(string value);
        public extern static string Format(string format, byte arg0);
        public extern static string Format(string format, byte arg0, byte arg1, byte arg2, byte arg3);
        public static string Concat(string a, string b) { return Concat(a, b, ""); } // Roslyn well-known uye: `a + b` BUNA lower edilir (govdede + yazilamaz: ozyineleme)
        public extern static string Concat(string a, string b, string c);
        public static string Concat(string a, string b, string c, string d) { return Concat(Concat(a, b, c), d, ""); }
        public static string Concat(string[] values)
        {
            string r = "";
            for (int i = 0; i < values.Length; i++) r = Concat(r, values[i], "");
            return r;
        }
        public static string Join(string separator, string[] values)
        {
            if (values == null) throw new NullReferenceException();
            string r = "";
            for (int i = 0; i < values.Length; i++) { if (i > 0) r = Concat(r, separator, ""); r = Concat(r, values[i], ""); }
            return r;
        }
        public extern bool Equals(string other);
        // Ordinal karsilastirma (UTF-16 kod birimi sirasi; .NET CompareOrdinal ile ayni isaret).
        public static int CompareOrdinal(string a, string b)
        {
            if ((object)a == null) return (object)b == null ? 0 : -1;
            if ((object)b == null) return 1;
            int n = a.Length < b.Length ? a.Length : b.Length;
            for (int i = 0; i < n; i++)
            {
                int d = a[i] - b[i];
                if (d != 0) return d;
            }
            return a.Length - b.Length;
        }
        public static bool Equals(string a, string b, StringComparison comparisonType)
        {
            if ((object)a == null || (object)b == null) return (object)a == (object)b;
            if (comparisonType == StringComparison.OrdinalIgnoreCase || comparisonType == StringComparison.InvariantCultureIgnoreCase || comparisonType == StringComparison.CurrentCultureIgnoreCase)
                return a.ToLowerInvariant().Equals(b.ToLowerInvariant());
            return a.Equals(b);
        }
        public extern bool Contains(string value);
        public extern bool Contains(char value);
        public extern bool StartsWith(string value);
        public extern bool StartsWith(string value, StringComparison comparisonType);
        public extern bool StartsWith(char value);
        public extern bool EndsWith(string value);
        public extern bool EndsWith(string value, StringComparison comparisonType);
        public int IndexOf(char value) { return IndexOf(value, 0); }
        public int IndexOf(char value, int startIndex)
        {
            for (int i = startIndex; i < Length; i++) if (this[i] == value) return i;
            return -1;
        }
        public int LastIndexOf(char value)
        {
            for (int i = Length - 1; i >= 0; i--) if (this[i] == value) return i;
            return -1;
        }
        public extern int IndexOf(string value);
        public extern int IndexOf(string value, int startIndex);
        public extern int IndexOf(string value, StringComparison comparisonType);
        public extern override int GetHashCode();
        public extern override bool Equals(object other);
        public extern override string ToString();
        public extern static string operator +(string a, string b);
        public extern static string operator +(string a, int b);
        public extern static string operator +(int a, string b);
        public extern static string operator +(string a, char b);
        public extern static string operator +(string a, bool b);
        public extern static string operator +(string a, float b);
        public extern static string operator +(float a, string b);
        public extern static string operator +(string a, double b);
        public extern static string operator +(string a, object b);
        public extern static string operator +(object a, string b);
        public extern static bool operator ==(string a, string b);
        public extern static bool operator !=(string a, string b);
    }
}
