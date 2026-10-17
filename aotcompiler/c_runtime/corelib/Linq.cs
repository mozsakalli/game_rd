using System.Collections.Generic;

namespace System.Linq
{
    // Eager LINQ alt kumesi: her operator sonucu hemen List<T>'ye yazar (iterator/closure altyapisi yok; corelib tarzi).
    // Semantik .NET ile ayni (sira korunur, bos kaynakta Max/First InvalidOperationException). IEnumerable<T> olarak doner
    // (List<T> implement eder) -> zincirleme .Where(...).Select(...).ToList() calisir. Ana-thread.
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

        public static T FirstOrDefault<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            IEnumerator<T> e = source.GetEnumerator();
            return e.MoveNext() ? e.Current : default(T);
        }

        public static T First<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            IEnumerator<T> e = source.GetEnumerator();
            if (!e.MoveNext()) throw new InvalidOperationException("Sequence contains no elements");
            return e.Current;
        }

        public static T First<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null) throw new ArgumentNullException("source");
            foreach (var x in source) if (predicate(x)) return x;
            throw new InvalidOperationException("Sequence contains no matching element");
        }

        public static bool Any<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return source.GetEnumerator().MoveNext();
        }

        public static bool Any<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null) throw new ArgumentNullException("source");
            foreach (var x in source) if (predicate(x)) return true;
            return false;
        }

        public static bool All<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null) throw new ArgumentNullException("source");
            foreach (var x in source) if (!predicate(x)) return false;
            return true;
        }

        public static int Count<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (source is List<T> l) return l.Count;
            if (source is T[] a) return a.Length;
            int n = 0;
            foreach (var x in source) n++;
            return n;
        }

        public static int Count<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null) throw new ArgumentNullException("source");
            int n = 0;
            foreach (var x in source) if (predicate(x)) n++;
            return n;
        }

        public static bool Contains<T>(this IEnumerable<T> source, T value)
        {
            if (source == null) throw new ArgumentNullException("source");
            var c = EqualityComparer<T>.Default;
            foreach (var x in source) if (c.Equals(x, value)) return true;
            return false;
        }

        public static IEnumerable<T> Where<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null) throw new ArgumentNullException("source");
            var r = new List<T>();
            foreach (var x in source) if (predicate(x)) r.Add(x);
            return r;
        }

        public static IEnumerable<R> Select<T, R>(this IEnumerable<T> source, Func<T, R> selector)
        {
            if (source == null) throw new ArgumentNullException("source");
            var r = new List<R>();
            foreach (var x in source) r.Add(selector(x));
            return r;
        }

        public static IEnumerable<R> SelectMany<T, R>(this IEnumerable<T> source, Func<T, IEnumerable<R>> selector)
        {
            if (source == null) throw new ArgumentNullException("source");
            var r = new List<R>();
            foreach (var x in source) foreach (var y in selector(x)) r.Add(y);
            return r;
        }

        public static IEnumerable<T> Take<T>(this IEnumerable<T> source, int count)
        {
            if (source == null) throw new ArgumentNullException("source");
            var r = new List<T>();
            if (count <= 0) return r;
            foreach (var x in source) { r.Add(x); if (r.Count >= count) break; }
            return r;
        }

        public static IEnumerable<T> Skip<T>(this IEnumerable<T> source, int count)
        {
            if (source == null) throw new ArgumentNullException("source");
            var r = new List<T>();
            int i = 0;
            foreach (var x in source) { if (i++ >= count) r.Add(x); }
            return r;
        }

        public static IEnumerable<T> Concat<T>(this IEnumerable<T> first, IEnumerable<T> second)
        {
            if (first == null) throw new ArgumentNullException("first");
            if (second == null) throw new ArgumentNullException("second");
            var r = new List<T>(first);
            foreach (var x in second) r.Add(x);
            return r;
        }

        public static IEnumerable<T> Distinct<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            var seen = new HashSet<T>();
            var r = new List<T>();
            foreach (var x in source) if (seen.Add(x)) r.Add(x);
            return r;
        }

        public static IEnumerable<T> Reverse<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            var r = new List<T>(source);
            r.Reverse();
            return r;
        }

        public static IEnumerable<int> Range(int start, int count)
        {
            if (count < 0 || (long)start + count - 1 > int.MaxValue) throw new ArgumentOutOfRangeException("count");
            var r = new List<int>(count);
            for (int i = 0; i < count; i++) r.Add(start + i);
            return r;
        }

        public static IEnumerable<T> Repeat<T>(T element, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException("count");
            var r = new List<T>(count);
            for (int i = 0; i < count; i++) r.Add(element);
            return r;
        }

        public static List<T> ToList<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            return new List<T>(source);
        }

        public static T[] ToArray<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (source is T[] a) { var c = new T[a.Length]; for (int i = 0; i < a.Length; i++) c[i] = a[i]; return c; }
            return new List<T>(source).ToArray();
        }

        public static Dictionary<K, T> ToDictionary<T, K>(this IEnumerable<T> source, Func<T, K> keySelector)
        {
            if (source == null) throw new ArgumentNullException("source");
            var d = new Dictionary<K, T>();
            foreach (var x in source) d.Add(keySelector(x), x);
            return d;
        }

        public static Dictionary<K, V> ToDictionary<T, K, V>(this IEnumerable<T> source, Func<T, K> keySelector, Func<T, V> elementSelector)
        {
            if (source == null) throw new ArgumentNullException("source");
            var d = new Dictionary<K, V>();
            foreach (var x in source) d.Add(keySelector(x), elementSelector(x));
            return d;
        }

        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            var h = new HashSet<T>();
            foreach (var x in source) h.Add(x);
            return h;
        }

        // ---- sayisal toplamlar (int/long/float/double; .NET: int/long Sum checked overflow)
        public static int Sum(this IEnumerable<int> source) { if (source == null) throw new ArgumentNullException("source"); int s = 0; foreach (var x in source) s = checked(s + x); return s; }
        public static long Sum(this IEnumerable<long> source) { if (source == null) throw new ArgumentNullException("source"); long s = 0; foreach (var x in source) s = checked(s + x); return s; }
        public static float Sum(this IEnumerable<float> source) { if (source == null) throw new ArgumentNullException("source"); double s = 0; foreach (var x in source) s += x; return (float)s; }
        public static double Sum(this IEnumerable<double> source) { if (source == null) throw new ArgumentNullException("source"); double s = 0; foreach (var x in source) s += x; return s; }
        public static int Sum<T>(this IEnumerable<T> source, Func<T, int> selector) { if (source == null) throw new ArgumentNullException("source"); int s = 0; foreach (var x in source) s = checked(s + selector(x)); return s; }
        public static long Sum<T>(this IEnumerable<T> source, Func<T, long> selector) { if (source == null) throw new ArgumentNullException("source"); long s = 0; foreach (var x in source) s = checked(s + selector(x)); return s; }
        public static float Sum<T>(this IEnumerable<T> source, Func<T, float> selector) { if (source == null) throw new ArgumentNullException("source"); double s = 0; foreach (var x in source) s += selector(x); return (float)s; }
        public static double Sum<T>(this IEnumerable<T> source, Func<T, double> selector) { if (source == null) throw new ArgumentNullException("source"); double s = 0; foreach (var x in source) s += selector(x); return s; }

        public static int Max(this IEnumerable<int> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            bool any = false; int m = 0;
            foreach (var x in source) { if (!any || x > m) m = x; any = true; }
            if (!any) throw new InvalidOperationException("Sequence contains no elements");
            return m;
        }
        public static int Min(this IEnumerable<int> source)
        {
            if (source == null) throw new ArgumentNullException("source");
            bool any = false; int m = 0;
            foreach (var x in source) { if (!any || x < m) m = x; any = true; }
            if (!any) throw new InvalidOperationException("Sequence contains no elements");
            return m;
        }
        public static int Max<T>(this IEnumerable<T> source, Func<T, int> selector)
        {
            if (source == null) throw new ArgumentNullException("source");
            bool any = false; int m = 0;
            foreach (var x in source) { int v = selector(x); if (!any || v > m) m = v; any = true; }
            if (!any) throw new InvalidOperationException("Sequence contains no elements");
            return m;
        }
        public static int Min<T>(this IEnumerable<T> source, Func<T, int> selector)
        {
            if (source == null) throw new ArgumentNullException("source");
            bool any = false; int m = 0;
            foreach (var x in source) { int v = selector(x); if (!any || v < m) m = v; any = true; }
            if (!any) throw new InvalidOperationException("Sequence contains no elements");
            return m;
        }
        public static float Max<T>(this IEnumerable<T> source, Func<T, float> selector)
        {
            if (source == null) throw new ArgumentNullException("source");
            bool any = false; float m = 0;
            foreach (var x in source) { float v = selector(x); if (!any || v > m) m = v; any = true; }
            if (!any) throw new InvalidOperationException("Sequence contains no elements");
            return m;
        }
        public static float Min<T>(this IEnumerable<T> source, Func<T, float> selector)
        {
            if (source == null) throw new ArgumentNullException("source");
            bool any = false; float m = 0;
            foreach (var x in source) { float v = selector(x); if (!any || v < m) m = v; any = true; }
            if (!any) throw new InvalidOperationException("Sequence contains no elements");
            return m;
        }
    }
}
