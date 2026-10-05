// corelib: System.String (degismez UTF-16). Bu bildirim YENI tip yaratmaz - uyeler runtime
// tipine (Primitive.String / C: VmString+vmstring_type; VM: .NET string degeri) baglanir.
// YUZEY C# UYUMLU tutulur: C# API'sinde olmayan uye EKLENMEZ (kacak yok).
// extern = govde c_runtime/corelib.c'de (C, mangled adla) + source/Intrinsics.cs'te (VM).
// get_Chars = C# indexer'inin (s[i]) alt yuzeyi - derleyici EIndex'i buna lower eder (Roslyn gibi).
// operator + (string,int)/(int,string)/(string,char): C#'in ortuk ToString birlestirmesinin
// dogrudan karsiligi (yuzeyde ayri operator gorunur ama davranis C# ile ayni).
namespace System
{
    enum StringComparison
    {
        CurrentCulture = 0,
        CurrentCultureIgnoreCase = 1,
        InvariantCulture = 2,
        InvariantCultureIgnoreCase = 3,
        Ordinal = 4,
        OrdinalIgnoreCase = 5
    }

    class String
    {
        extern int Length { get; }
        extern char get_Chars(int index);
        extern string Substring(int startIndex);
        extern string Substring(int startIndex, int length);
        extern string Replace(string oldValue, string newValue);
        extern string Replace(char oldChar, char newChar);
        extern string[] Split(string separator);
        extern string[] Split(string separator, int count);
        extern string[] Split(char separator);
        extern string Trim();
        extern string ToLower();
        extern string ToLowerInvariant();
        extern string ToUpper();
        extern char[] ToCharArray();
        extern static bool IsNullOrEmpty(string value);
        extern static bool IsNullOrWhiteSpace(string value);
        extern static string Format(string format, byte arg0);
        extern static string Format(string format, byte arg0, byte arg1, byte arg2, byte arg3);
        extern static string Concat(string a, string b, string c);
        extern bool Equals(string other);
        extern bool Contains(string value);
        extern bool Contains(char value);
        extern bool StartsWith(string value);
        extern bool StartsWith(string value, StringComparison comparisonType);
        extern bool StartsWith(char value);
        extern bool EndsWith(string value);
        extern bool EndsWith(string value, StringComparison comparisonType);
        extern int IndexOf(string value);
        extern int IndexOf(string value, int startIndex);
        extern int IndexOf(string value, StringComparison comparisonType);
        extern override int GetHashCode();
        extern override bool Equals(object other);
        extern override string ToString();
        extern static string operator +(string a, string b);
        extern static string operator +(string a, int b);
        extern static string operator +(int a, string b);
        extern static string operator +(string a, char b);
        extern static string operator +(string a, bool b);
        extern static string operator +(string a, float b);
        extern static string operator +(float a, string b);
        extern static string operator +(string a, double b);
        extern static string operator +(string a, object b);
        extern static string operator +(object a, string b);
        extern static bool operator ==(string a, string b);
        extern static bool operator !=(string a, string b);
    }
}
