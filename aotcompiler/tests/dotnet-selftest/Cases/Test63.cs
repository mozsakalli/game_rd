using System;

// Generic TABAN + imzasinda T tasiyan sanal uye eslesmesi (override/itable/reflection). Matrix.cs'teki Describe() T tasimiyordu;
// bu hucreler .NET'in "ad + substitute edilmis imza" kuralini isim-mangling'den bagimsiz dogrular. Her hucre .NET baseline ile birebir.
namespace GenOverride
{
    public abstract class Ser<T>
    {
        public virtual string Enc(T v) { return "base:" + v; }
        public virtual bool Should(T v) { return false; }
        public abstract T Make(int seed);
        public virtual T Value { get; set; }
        public virtual int Mix(ref T a, out T b) { b = a; return 1; }
        public virtual string Pair<U>(T t, U u) { return "base:" + t + "/" + u; }
        public string Run(T v) { return Enc(v) + (Should(v) ? "!" : "."); }
    }

    // a) somut argumanli taban: override Enc(int) <-> sablon Enc(T)
    public class IntSer : Ser<int>
    {
        public override string Enc(int v) { return "int:" + (v * 2); }
        public override bool Should(int v) { return v > 3; }
        public override int Make(int seed) { return seed + 100; }
        public override int Value { get { return base.Value + 1; } set { base.Value = value * 10; } }
        public override int Mix(ref int a, out int b) { a += 5; b = a * 2; return 2; }
        public override string Pair<U>(int t, U u) { return "int:" + t + "/" + u + "/" + base.Pair(t, u); }
    }

    // b) farkli harf: D<U> : B<U>
    public class B<T> { public virtual string F(T x) { return "B" + x; } public virtual T Id(T x) { return x; } }
    public class D<U> : B<U> { public override string F(U x) { return "D" + x + base.F(x); } public override U Id(U x) { return base.Id(x); } }

    // c) 3 seviye: Base<T> -> Mid<T> (override etmez) -> Leaf : Mid<string>
    public class Mid<T> : B<T> { public string Via(T x) { return "M" + F(x); } }
    public class Leaf : Mid<string> { public override string F(string x) { return "L" + x.Length + base.F(x); } }

    // f) overload: Foo(T) ve Foo(int) aynı tabanda; X : Base<string> ikisini de ezer
    public class Ov<T>
    {
        public virtual string Foo(T x) { return "T"; }
        public virtual string Foo(int x) { return "I"; }
    }
    public class OvS : Ov<string>
    {
        public override string Foo(string x) { return "s" + x; }
        public override string Foo(int x) { return "i" + x; }
    }

    // g) generic iface'in somut ornegi: acik + ortuk impl, generic ve somut sinifta
    public interface IConv<T> { string Conv(T v); T Back(string s); }
    public class ConvInt : IConv<int>
    {
        string IConv<int>.Conv(int v) { return "ci" + v; }
        public int Back(string s) { return s.Length; }
    }
    public class ConvG<T> : IConv<T>
    {
        public string Conv(T v) { return "cg" + v; }
        public T Back(string s) { return default(T); }
    }
    public class ConvGS : ConvG<string> { public override string ToString() { return "cgs"; } }

    // h) property/field reflection (generic taban uzerinden)
    public class PropSer : Ser<string>
    {
        public override string Make(int seed) { return "m" + seed; }
        public override string Value { get { return "[" + base.Value + "]"; } set { base.Value = value + "!"; } }
    }

    static class Chk
    {
        public static long h;
        public static void S(string s) { for (int i = 0; i < s.Length; i++) h = h * 31 + s[i]; h = h * 31 + 7; }
        public static void I(long v) { h = h * 31 + v; }
        public static int Done() { int r = (int)(h % 1000000007L); h = 0; return r; }
    }
}

namespace Demo74 // a) override Enc(int) / Should(int) / abstract Make / Run(T) tabanda cagirir
{
    using GenOverride;
    class App74
    {
        public static int Run()
        {
            Ser<int> s = new IntSer();
            Chk.S(s.Run(2)); Chk.S(s.Run(5)); Chk.I(s.Make(1));
            IntSer d = (IntSer)s; Chk.S(d.Enc(7));
            return Chk.Done();
        }
    }
}
namespace Demo75 // b) farkli harf D<U> : B<U>, iki ornekleme
{
    using GenOverride;
    class App75
    {
        public static int Run()
        {
            B<int> bi = new D<int>(); B<string> bs = new D<string>();
            Chk.S(bi.F(3)); Chk.S(bs.F("q")); Chk.I(bi.Id(9)); Chk.S(bs.Id("z"));
            return Chk.Done();
        }
    }
}
namespace Demo76 // c) 3 seviye + base. zinciri
{
    using GenOverride;
    class App76
    {
        public static int Run()
        {
            Leaf l = new Leaf(); B<string> b = l; Mid<string> m = l;
            Chk.S(b.F("abc")); Chk.S(m.Via("xy")); Chk.S(new Mid<string>().Via("k"));
            return Chk.Done();
        }
    }
}
namespace Demo77 // d) ref/out T override
{
    using GenOverride;
    class App77
    {
        public static int Run()
        {
            Ser<int> s = new IntSer();
            int a = 1, b;
            Chk.I(s.Mix(ref a, out b)); Chk.I(a); Chk.I(b);
            return Chk.Done();
        }
    }
}
namespace Demo78 // e) generic sinifta generic metot override Pair<U>(T,U)
{
    using GenOverride;
    class App78
    {
        public static int Run()
        {
            Ser<int> s = new IntSer();
            Chk.S(s.Pair<string>(1, "u")); Chk.S(s.Pair<int>(2, 3));
            return Chk.Done();
        }
    }
}
namespace Demo79 // f) Foo(T)/Foo(int) overload ikisi de override
{
    using GenOverride;
    class App79
    {
        public static int Run()
        {
            Ov<string> o = new OvS();
            Chk.S(o.Foo("a")); Chk.S(o.Foo(4)); Chk.S(new Ov<string>().Foo("a")); Chk.S(new Ov<int>().Foo(1));
            return Chk.Done();
        }
    }
}
namespace Demo80 // g) IConv<int>/IConv<string> itable: acik impl, generic impl, miras yoluyla impl
{
    using GenOverride;
    class App80
    {
        public static int Run()
        {
            IConv<int> ci = new ConvInt(); IConv<string> cs = new ConvGS(); IConv<int> cg = new ConvG<int>();
            Chk.S(ci.Conv(5)); Chk.I(ci.Back("abcd")); Chk.S(cs.Conv("w")); Chk.S(cs.Back("x") ?? "null"); Chk.S(cg.Conv(8)); Chk.I(cg.Back(""));
            Chk.S(cs.ToString());
            return Chk.Done();
        }
    }
}
namespace Demo81 // h) virtual T Value property override + reflection get/set
{
    using GenOverride;
    class App81
    {
        public static int Run()
        {
            Ser<int> s = new IntSer(); s.Value = 2; Chk.I(s.Value);
            Ser<string> p = new PropSer(); p.Value = "v"; Chk.S(p.Value);
            var prop = typeof(PropSer).GetProperty("Value");
            if (prop != null)
            {
                prop.SetValue(p, "r");
                Chk.S((string)prop.GetValue(p));
            }
            else Chk.S("noprop");
            return Chk.Done();
        }
    }
}
namespace Demo82 // i) abstract T Make() donusu T; dizi ve kutulama uzerinden
{
    using GenOverride;
    class App82
    {
        public static int Run()
        {
            Ser<string> p = new PropSer(); Ser<int> s = new IntSer();
            object o1 = p.Make(3), o2 = s.Make(4);
            Chk.S(o1.ToString()); Chk.S(o2.ToString()); Chk.I(o2 is int ? 1 : 0);
            return Chk.Done();
        }
    }
}
namespace Demo83 // IEqualityComparer<T>/EqualityComparer<T>.Default: StringComparer ile Dictionary/HashSet, anonim tip Equals/GetHashCode
{
    using System.Collections.Generic;
    using GenOverride;
    class App83
    {
        public static int Run()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            d["Key"] = 1; d["KEY"] = 2; d["other"] = 3;
            Chk.I(d.Count); Chk.I(d["key"]); Chk.I(d.ContainsKey("OTHER") ? 1 : 0); Chk.I(d.Remove("kEy") ? 1 : 0); Chk.I(d.Count);
            var hs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Chk.I(hs.Add("a") ? 1 : 0); Chk.I(hs.Add("A") ? 1 : 0); Chk.I(hs.Count);
            var plain = new Dictionary<string, int>(); plain["Key"] = 1; plain["KEY"] = 2; Chk.I(plain.Count);
            var a1 = new { Name = "x", N = 3 }; var a2 = new { Name = "x", N = 3 }; var a3 = new { Name = "y", N = 3 };
            Chk.I(a1.Equals(a2) ? 1 : 0); Chk.I(a1.Equals(a3) ? 1 : 0); Chk.I(a1.GetHashCode() == a2.GetHashCode() ? 1 : 0);
            Chk.I(EqualityComparer<int>.Default.Equals(5, 5) ? 1 : 0); Chk.I(EqualityComparer<string>.Default.Equals(null, null) ? 1 : 0);
            Chk.I(EqualityComparer<string>.Default.Equals("q", null) ? 1 : 0); Chk.I(EqualityComparer<string>.Default.GetHashCode(null));
            Chk.I(object.ReferenceEquals(EqualityComparer<int>.Default, EqualityComparer<int>.Default) ? 1 : 0);
            Chk.S(a1.ToString()); Chk.S(string.Format("{0}+{1}={{{2}}}", 1, "b", a3.N)); Chk.S(string.Format("[{0,3}|{1,-4}|{2}]", 7, "ab", null));
            return Chk.Done();
        }
    }
}
namespace Demo84 // ConcurrentDictionary (tek thread sarmalayici): TryAdd/GetOrAdd/AddOrUpdate/TryRemove/TryUpdate/foreach
{
    using System.Collections.Concurrent;
    using GenOverride;
    class App84
    {
        public static int Run()
        {
            var d = new ConcurrentDictionary<string, int>();
            Chk.I(d.TryAdd("a", 1) ? 1 : 0); Chk.I(d.TryAdd("a", 2) ? 1 : 0); Chk.I(d["a"]);
            Chk.I(d.GetOrAdd("b", k => k.Length + 10)); Chk.I(d.GetOrAdd("b", 99)); Chk.I(d.GetOrAdd("c", 7));
            Chk.I(d.AddOrUpdate("a", 100, (k, v) => v + 50)); Chk.I(d.AddOrUpdate("z", k => k.Length, (k, v) => -1));
            Chk.I(d.TryUpdate("c", 8, 7) ? 1 : 0); Chk.I(d.TryUpdate("c", 9, 7) ? 1 : 0); Chk.I(d["c"]);
            int removed; Chk.I(d.TryRemove("b", out removed) ? 1 : 0); Chk.I(removed); Chk.I(d.TryRemove("nope", out removed) ? 1 : 0);
            Chk.I(d.Count); Chk.I(d.IsEmpty ? 1 : 0);
            int sum = 0; foreach (var kv in d) sum += kv.Value + kv.Key.Length; Chk.I(sum); foreach (var k in d.Keys) sum += k.Length; Chk.I(sum);
            d["q"] = 5; Chk.I(d.ContainsKey("q") ? 1 : 0); d.Clear(); Chk.I(d.Count);
            return Chk.Done();
        }
    }
}
