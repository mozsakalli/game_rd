// corelib: StringComparer (yalniz Ordinal / OrdinalIgnoreCase; kultur yok). IComparer<string>
// olarak List<string>.Sort(comparer) ve Array.Sort'a verilir. IEqualityComparer modeli YOK
// (Dictionary/HashSet comparer'siz: GetHashCode/Equals dogrudan) — bilincli sinir.
namespace System
{
    public class StringComparer : Collections.Generic.IComparer<string>
    {
        public static StringComparer ordinal = new StringComparer(false);
        public static StringComparer ordinalIgnoreCase = new StringComparer(true);
        public bool ignoreCase;

        public StringComparer(bool ignoreCase) { this.ignoreCase = ignoreCase; }

        public static StringComparer Ordinal { get { return ordinal; } }
        public static StringComparer OrdinalIgnoreCase { get { return ordinalIgnoreCase; } }

        public int Compare(string x, string y)
        {
            if (ignoreCase)
            {
                if ((object)x != null) x = x.ToLowerInvariant();
                if ((object)y != null) y = y.ToLowerInvariant();
            }
            return string.CompareOrdinal(x, y);
        }

        public bool Equals(string x, string y) { return Compare(x, y) == 0; }

        public int GetHashCode(string obj)
        {
            if ((object)obj == null) return 0;
            return (ignoreCase ? obj.ToLowerInvariant() : obj).GetHashCode();
        }
    }
}
