using System.Collections.Generic;

namespace System.Linq
{
    public static class Enumerable
    {
        public static T FirstOrDefault<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            IEnumerator<T> e = source.GetEnumerator();
            while (e.MoveNext())
            {
                T item = e.Current;
                if (predicate(item)) return item;
            }
            return default(T);
        }
    }
}