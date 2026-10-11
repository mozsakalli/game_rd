using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// GENERIC UYUM MATRISI (docs/engine-notes.md GENERICS-S3): tek generic "Probe<T>" govdesi, T'nin HER sekil kategorisiyle
// (skaler/enum/struct/ref-struct/string/class/iface/delegate/dizi/Nullable) ayni op listesini kosar. Her hucre .NET baseline
// ile birebir karsilastirilir (Test61..Test72). Yeni bir op kategorisi eklenirse BURAYA eklenir; hucre kirmiziysa frontend/
// instantiator/emisyon o kombinasyonu yanlis indirgiyor demektir. Kurallar: string.GetHashCode/obj.GetHashCode (randomize/kimlik)
// KULLANILMAZ; sonuc deterministik long hash.
namespace Matrix
{
    public enum Color { Red = 1, Green = 2, Blue = 3 }
    public struct SA { public int a; public float b; public SA(int a, float b) { this.a = a; this.b = b; } public override string ToString() { return "SA" + a; } public int Sum() { return a + (int)b; } }
    public struct SR { public string s; public int n; public SR(string s, int n) { this.s = s; this.n = n; } public override string ToString() { return s + n; } }
    public interface IFace { int Id(); }
    public class C : IFace { public int v; public C(int v) { this.v = v; } public int Id() { return v; } public override string ToString() { return "C" + v; } }
    public class D : C { public D(int v) : base(v) { } public override string ToString() { return "D" + v; } }

    public interface IHolder<T> { T Get(); void Put(T v); int Kind(); }

    public class Base<T>
    {
        public T slot;
        public static int created;
        public Base() { created++; }
        public virtual int Describe() { return 1; }
        public T Echo(T v) { slot = v; return slot; }
    }
    public class Derived<T> : Base<T>, IHolder<T>
    {
        public T second;
        public override int Describe() { return 2 + base.Describe(); }
        public T Get() { return second; }
        public void Put(T v) { second = v; }
        public int Kind() { return typeof(T).IsValueType ? 1 : 2; }
    }

    public static class H
    {
        public static long h;
        public static bool trace; // stderr'e adim adim deger (diff'e girmez): .NET vs AOT farkini bulmak icin
        public static void L(long d) { if (trace) Console.Error.WriteLine("[h] " + d); h = (h * 31 + d) % 1000000007L; }
        public static void S(string s) { if (s == null) { L(-1); return; } L(s.Length); for (int i = 0; i < s.Length; i++) L(s[i]); }
        public static void B(bool b) { L(b ? 1 : 0); }
    }

    public static class Probe<T>
    {
        public static int count;                 // ornekleme basina ayri static
        static T Pick(T a, T b, bool first) { return first ? a : b; }
        static void Swap(ref T x, ref T y) { T t = x; x = y; y = t; }
        static bool TryGet(T src, out T dst) { dst = src; return true; }
        static U Convert<U>(T v, Func<T, U> f) { return f(v); }         // generic metot icinde generic metot
        static IEnumerable<T> Iter(T a, T b) { yield return a; yield return b; }
        static async Task<T> Later(T v) { await Task.CompletedTask; return v; }
        static string Str(T v) { return v == null ? "null" : v.ToString(); } // constrained callvirt

        public static long Run(T a, T b, string expectName)
        {
            H.h = 17;
            count++;
            H.L(count);
            // 1) box / unbox / is / cast
            object o = a;
            H.B(o is T);
            H.B(o != null);
            T back = (T)o;
            H.B(back.Equals(a));
            H.B(o is string);
            H.B(o is ValueType);
            if (o is T t2) H.S(Str(t2));
            // 2) default / typeof
            T d = default(T);
            H.B(d == null);
            H.B(typeof(T).IsValueType);
            H.B(typeof(T) == typeof(int));
            H.B(typeof(T) == o.GetType());
            // 3) diziler
            T[] arr = new T[3];
            arr[0] = a; arr[1] = b; arr[2] = arr[0];
            H.L(arr.Length);
            H.B(arr[2].Equals(a));
            H.B(arr[1].Equals(a));
            object oa = arr;
            H.B(oa is T[]);
            H.B(oa is Array);
            Array ga = (Array)oa;
            H.L(ga.Length);
            // 4) static alan + generic base + sanal + iface
            var der = new Derived<T>();
            der.Put(b);
            der.slot = a;
            H.L(der.Describe());
            H.B(der.Echo(b).Equals(b));
            H.B(der.slot.Equals(b));
            IHolder<T> ih = der;
            H.B(ih.Get().Equals(b));
            H.L(ih.Kind());
            H.L(Base<T>.created);
            // 5) ref/out
            T x = a, y = b;
            Swap(ref x, ref y);
            H.B(x.Equals(b) && y.Equals(a));
            T got;
            H.B(TryGet(a, out got) && got.Equals(a));
            // 6) delegate'ler (indirect call) + capture
            Func<T, T> id = v => v;
            Action<T> act = v => H.S(Str(v));
            Func<T, T, bool, T> pick = Pick;
            act(id(a));
            H.B(pick(a, b, false).Equals(b));
            H.S(Convert(a, v => Str(v) + "!"));
            H.L(Convert(b, v => 7));
            // 7) constrained callvirt'ler
            H.S(Str(a));
            H.B(a.Equals(b));
            H.B(a.Equals(a));
            H.B(Str(a) == a.ToString());
            // 8) koleksiyonlar (T anahtar/eleman)
            var list = new List<T> { a, b };
            list.Add(a);
            H.L(list.Count);
            H.L(list.IndexOf(b));
            H.B(list.Contains(a));
            list.Remove(a);
            H.L(list.Count);
            int n = 0;
            foreach (var e in list) { n++; H.S(Str(e)); }
            H.L(n);
            var dict = new Dictionary<int, T>();
            dict[1] = a; dict[2] = b; dict[1] = b;
            H.L(dict.Count);
            H.B(dict[1].Equals(b));
            T dv;
            H.B(dict.TryGetValue(3, out dv));
            H.B(dv == null);
            // 9) iterator + async
            foreach (var e in Iter(a, b)) H.S(Str(e));
            var task = Later(b);
            H.B(task.IsCompleted);
            H.B(task.Result.Equals(b));
            // 10) ad
            H.S(expectName);
            return H.h;
        }
    }
}
