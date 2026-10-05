// GAMOS conformance testi: AYNI dosya hem gercek dotnet hem GAMOS->C ile derlenir/kosulur,
// stdout birebir karsilastirilir. GAMOS sembolu yalniz bizim on-islemcide tanimlidir.
// BILINCLI DISLANANLAR: signed int overflow (C'de UB), default ToString/GetHashCode degerleri
// (tip adi/kimlik farkli), string.GetHashCode DEGERI (algoritma farkli), StackTrace metni,
// InvalidCastException.Message (dotnet dinamik 'Unable to cast...' uretir; bizde sabit default).
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using static Conformance.Static46;

namespace Conformance
{
    public enum Renk { Kirmizi, Yesil = 5, Mavi, Mor = -2, Lacivert }

    public delegate int Op26(int a, int b);
    public delegate int Un26(int x);

    public class Carpici26
    {
        int k;
        public Carpici26(int k0) { k = k0; }
        public int Carp(int x) { return x * k; }
        public virtual int Sanal(int x) { return x + 1; }
    }
    public class CarpiciTurev26 : Carpici26
    {
        public CarpiciTurev26() : base(0) { }
        public override int Sanal(int x) { return x * 1000; }
    }

    public class Ok28
    {
        int deger;
        public Ok28(int a) => deger = a;
        public int Iki => deger * 2;
        public int Yari { get => deger / 2; }
        public int X { get => deger; set => deger = value; }
        public int Kat(int k) => deger * k;
    }

    public static class Ext30
    {
        public static int ToInt30(this object o)
        {
            if (o != null)
            {
                if (o is int) { return (int)o; }
                if (o is bool) { return (bool)o ? 1 : 0; }
                double d = 0;
                double.TryParse(o.ToString(), out d);
                return (int)d;
            }
            return 0;
        }
        public static int Kati30(this object o, int k) { return o.ToInt30() * k; }
    }

    // partial: parcalar birlesir, alan init'leri tek ctor'da toplanir, parcalar arasi erisim serbest
    public partial class Parca25
    {
        int a = 11;
        public int A() { return a + B(); }
    }
    public partial class Parca25 : INamed
    {
        int b = 20;
        public int B() { return b; }
        public int Id() { return a * 100 + b; }
        public int Toplam { get { return A() + b; } }
    }

    public interface IShape { int Area(); }
    public interface INamed { int Id(); }

    public class Rect : IShape, INamed
    {
        int w;
        int h;
        public Rect(int w0, int h0) { w = w0; h = h0; }
        public int Area() { return w * h; }
        public int Id() { return 1; }
    }

    public class Circle : IShape
    {
        int r;
        public Circle(int r0) { r = r0; }
        public int Area() { return 3 * r * r; }
        public int R() { return r; }
    }

    public class A
    {
        protected int x;
        int y = 3;
        public A() { x = 1; }
        public A(int x0) { x = x0; }
        public virtual int Tag() { return 10; }
        public virtual int Sides { get { return 0; } }
        public int Sum() { return x + y; }
    }

    public class B : A
    {
        int z = 5;
        public B() { z = z + 1000; }
        public B(int x0) : base(x0 * 2) { z = z + 100; }
        public override int Tag() { return 20; }
        public override int Sides { get { return 4; } }
        public int Total() { return Sum() + z; }
    }

    public class C : B
    {
        int w = 7;
        public C() { w = w + 1; }
        public C(int k) : this() { w = w + k; }
        public int W() { return w; }
    }

    public class Pt
    {
        int px;
        int py;
        public Pt(int ax, int ay) { px = ax; py = ay; }
        public override int GetHashCode() { return px * 31 + py; }
        public override bool Equals(object o)
        {
            Pt p = o as Pt;
            if (p == null) { return false; }
            return p.px == px && p.py == py;
        }
        public override string ToString() { return "Pt#" + px + ":" + py; }
    }

    public struct Vec3
    {
        public float x;
        public float y;
        public float z;
        public Vec3(float ax, float ay, float az) { x = ax; y = ay; z = az; }
        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public float Dot(Vec3 o) { return x * o.x + y * o.y + z * o.z; }
        public void Scale(float f) { x *= f; y *= f; z *= f; }
        public float Sx { get { return x; } set { x = value; } }
    }

    public class Holder
    {
        public Vec3 pos;
        public int id;
        public Holder(int i) { id = i; }
    }

    public class Box<T>
    {
        T item;
        static int made;
        public Box(T v) { item = v; made = made + 1; }
        public T Get() { return item; }
        public void Set(T v) { item = v; }
        public static int Made() { return made; }
    }

    public class Pair<TA, TB>
    {
        TA a;
        TB b;
        public Pair(TA x, TB y) { a = x; b = y; }
        public TA First() { return a; }
        public TB Second() { return b; }
        public Pair<TB, TA> Swap() { return new Pair<TB, TA>(b, a); }
    }

    public class GameError : Exception
    {
        int code;
        public GameError(string m, int c) : base(m) { code = c; }
        public int Code() { return code; }
    }

    public class Cfg
    {
        public const int Carpan = 6;
        public const string Onek = "cfg";
        public static readonly int Taban = Carpan * 7; // 42, cctor'da atanir
    }

    public static class Static46
    {
        public static int LegacyValue;
        public static int SideValue;
        public static int[,] Rect00 = { { 2, 3 }, { 5, 7 } };
        public static int Imported46(int value) { return value + 2; }
        public static int Bump46() { SideValue++; return 4; }
        public static void Add46(int value) { SideValue += value; }
    }

    public interface IExplicit46 { int Value(); }
    public class Explicit46 : IExplicit46 { int IExplicit46.Value() => 7; }

    public class Nested46
    {
        public class Child
        {
            int value;
            public Child(int initial) { value = initial; }
            public int Value() { return value; }
        }
        public struct Stamp { public int value; }
        public Child Make(int value) { return new Child(value); }
    }

    public class Init46
    {
        public int Field;
        public int Property { get; set; }
        public Init46() { Field = 1; }
        public Init46(int value) { Field = value; }
    }

    public class Params46
    {
        string[] values;
        public Params46(int ignored = 0, params string[] items) { values = items; }
        public int Count() { return values.Length; }
    }

    public class Constraint39Base { }
    public interface IConstraint39 { }
    public class Constraint39Derived : Constraint39Base, IConstraint39 { public Constraint39Derived() { } }
    public class Constraint39<T> where T : Constraint39Base, IConstraint39, new()
    {
        public static U Keep<U>(U value) where U : class, IConstraint39, new() { return value; }
        public T Value;
    }

    public class Reflect46
    {
        public int field;
        public int Property { get; set; }
        public static int StaticField;
    }

    public class Accessor47
    {
        public int Value { get; private set; }
        public Accessor47(int value) { Value = value; }
    }

    [Obsolete("syntax-only attribute coverage")]
    public class Attribute43
    {
        [Obsolete]
        public int Read() { return 43; }
    }

    public class NullMember44
    {
        public string Text;
        public string Value { get { return Text; } }
    }

    public class OuterPartial46
    {
        public partial class Inner
        {
            public int Left() { return 20; }
        }
        public partial class Inner
        {
            public int Right() { return 22; }
        }
    }

    public class NullableHolder38
    {
        public float? Target;
    }

    public class Program
    {
        static int sideCount;

        static bool True1() { sideCount++; return true; }
        static bool False1() { sideCount++; return false; }

        static int AddN(int x, int y = 40) { return x + y; }
        static int AddN(int x, int y, int z) { return x + y + z; }

        static void SwapRef(ref int a, ref int b) { int t = a; a = b; b = t; }
        static bool TryGet(int k, out int v)
        {
            if (k > 0) { v = k * 10; return true; }
            v = -1;
            return false;
        }

        static T Pick<T>(T x, T y, bool second) { if (second) { return y; } return x; }

        static int ModifyCopy(Vec3 p) { p.x = 500; return (int)p.x; }

#if GAMOS
        static int Squares(int n) { int i = 0; while (i < n) { yield return i * i; i++; } }
#else
        static System.Collections.Generic.IEnumerable<int> Squares(int n) { int i = 0; while (i < n) { yield return i * i; i++; } }
#endif

        static int Boom(int x)
        {
            if (x > 3) { throw new GameError("patladi:" + x, x * 10); }
            return x;
        }

        static void P(string tag, int v)
        {
            Console.Write(tag);
            Console.Write(":");
            Console.WriteLine(v);
        }

        public static void Main()
        {
            // ---- S00 rectangular arrays: contiguous row-major storage, dynamic dimensions, bounds-aware indexing ----
            int[,] grid00 = { { 7, 12, 3 }, { 4, 8, 15 } };
            P("S00a", grid00[0, 0] + grid00[1, 2]); // 22
            P("S00f", Static46.Rect00[1, 1]); // 7
            grid00[0, 1] += 10;
            P("S00b", grid00[0, 1]); // 22
            int rows00 = 3, cols00 = 2;
            int[,] dynamic00 = new int[rows00, cols00];
            dynamic00[2, 1] = 31;
            P("S00c", dynamic00[2, 1] + dynamic00[0, 0]); // 31
            try { P("S00d", dynamic00[0, 2]); }
            catch (IndexOutOfRangeException) { P("S00d", 1); }
            int[,,] cube00 = new int[2, 3, 4];
            cube00[1, 2, 3] = 47;
            P("S00e", cube00[1, 2, 3]); // 47

            // ---- S01 aritmetik / oncelik / negatif bolme-mod (C# trunc) ----
            P("S01a", 2 + 3 * 4 - 6 / 2);          // 11
            P("S01b", -7 / 2);                      // -3
            P("S01c", -7 % 3);                      // -1
            P("S01d", 7 % -3);                      // 1
            P("S01e", (2 + 3) * (4 - 6) / 2);       // -5
            P("S01f", 1 + 2 * 3 % 4 - 5);           // -2
            P("S01g", -(-(5)));                     // 5

            // ---- S02 bitwise / shift / hex ----
            P("S02a", (0xF0 | 0x0F) ^ 0xFF);        // 0
            P("S02b", 0xAA & 0x0F);                 // 10
            P("S02c", 1 << 10);                     // 1024
            P("S02d", -16 >> 2);                    // -4 (aritmetik shift)
            P("S02e", ~5);                          // -6
            P("S02f", (3 | 4) & ~1);                // 6
            int mask = 3;
            mask <<= 4;
            mask |= 1;
            mask >>= 1;
            P("S02g", mask);                        // 24
            P("S02h", 5 & 3 | 4 ^ 1);               // oncelik: & > ^ > | -> (5&3)|(4^1) = 1|5 = 5

            // ---- S03 unsigned ----
            uint u1 = 0xFFFFFFFEu;
            P("S03a", (int)(u1 / 3u));              // 1431655764
            P("S03b", (int)(u1 % 5u));              // (4294967294 % 5) = 4
            uint big = 0x80000000u;
            P("S03c", big > 0x7FFFFFFFu ? 1 : 0);   // 1 (unsigned kiyas)
            int negTwo = -2;
            P("S03d", (int)((uint)negTwo >> 1));    // 2147483647
            long negu = -big;
            P("S03e", negu < 0 ? 1 : 0);            // 1 (-uint = long)
            ulong ul = 0xFF00FF00FF00FF00ul;
            ul >>= 8;
            P("S03f", (int)(ul & 0xFFFFul));        // 0x00FF = 255
            P("S03g", (int)(ul >> 48));             // 0x00FF = 255
            int threeHundred = 300;
            byte bb = (byte)threeHundred;
            P("S03h", bb);                          // 44
            bb += 250;
            P("S03i", bb);                          // 38 (mod 256)
            sbyte sb2 = -100;
            P("S03j", sb2 + 30);                    // -70
            ushort us = 60000;
            P("S03k", us + 1);                      // 60001 (int terfi)
            int minusOne = -1;
            P("S03l", (int)(uint)minusOne == -1 ? 1 : 0); // 1 (cift cast tur)

            // ---- S04 char ----
            P("S04a", 'A' + 1);                     // 66
            P("S04b", (int)'Z');                    // 90
            char ch = 'a';
            ch++;
            P("S04c", ch == 'b' ? 1 : 0);           // 1
            P("S04d", '9' - '0');                   // 9

            // ---- S05 kisa devre yan etkileri ----
            sideCount = 0;
            if (False1() && True1()) { P("S05x", -1); }
            P("S05a", sideCount);                   // 1 (sag calismadi)
            sideCount = 0;
            if (True1() || True1()) { sideCount += 10; }
            P("S05b", sideCount);                   // 11
            sideCount = 0;
            bool bmix = True1() & False1();         // kisa devresiz: ikisi de calisir
            P("S05c", sideCount + (bmix ? 100 : 0)); // 2

            // ---- S06 ternary / bilesik / inc-dec ----
            int k = 0;
            int z6 = k++ + ++k;                     // 0 + 2 = 2, k=2
            P("S06a", z6);
            P("S06b", k);
            int m6 = 10;
            m6 += m6++;                             // m6 = 10 + 10 = 20 (hedef tek degerlendirme, deger eski)
            P("S06c", m6);
            int t6 = 5;
            t6 = t6 > 3 ? t6 < 10 ? 1 : 2 : 3;      // ic ice ternary sag-birlesme -> 1
            P("S06d", t6);
            int p6 = 7;
            p6 -= --p6;                             // 7 - 6 = 1
            P("S06e", p6);

            // ---- S07 donguler / switch ----
            int s7 = 0;
            do { s7++; if (s7 == 2) { continue; } if (s7 >= 5) { break; } s7 += 10; } while (s7 < 100);
            P("S07a", s7);                          // 1->11->12(cont sonrasi 13? hesap: asagida dotnet referans)
            int f7 = 0;
            for (int i = 10; i > 0; i -= 3) { f7 += i; }
            P("S07b", f7);                          // 10+7+4+1 = 22
            int w7 = 0;
            int j7 = 0;
            while (j7 < 20) { j7 += 3; if (j7 % 2 == 0) { continue; } w7 += j7; }
            P("S07c", w7);
            switch (j7)
            {
                case 20:
                case 21: P("S07d", 1); break;
                case 22: P("S07d", 2); break;
                default: P("S07d", 9); break;
            }

            // ---- S08 enum ----
            Renk r = Renk.Yesil;
            P("S08a", (int)r);                      // 5
            P("S08b", (int)Renk.Mavi);              // 6
            P("S08c", (int)Renk.Mor);               // -2
            P("S08d", (int)Renk.Lacivert);          // -1 (onceki+1)
            Renk kombo = Renk.Kirmizi | Renk.Yesil; // 0|5
            P("S08e", (int)kombo);                  // 5
            P("S08f", r == Renk.Yesil ? 1 : 0);     // 1
            P("S08g", Renk.Mor < Renk.Kirmizi ? 1 : 0); // 1
            switch (r)
            {
                case Renk.Yesil: P("S08h", 50); break;
                default: P("S08h", 0); break;
            }
            r = (Renk)6;
            P("S08i", r == Renk.Mavi ? 1 : 0);      // 1

            // ---- S09 const / readonly / folding+intern ----
            P("S09a", Cfg.Carpan * 2 + 1);          // 13 (fold)
            P("S09b", Cfg.Taban);                   // 42 (static readonly, cctor)
            string g = Cfg.Onek + "X";              // const string + literal = derleme zamani
            object og1 = g;
            object og2 = "cfgX";
            P("S09c", og1 == og2 ? 1 : 0);          // 1 (intern REFERANS esitligi)
            P("S09d", Cfg.Carpan * Cfg.Carpan);     // 36 (fold)

            // ---- S10 string ----
            string sa = "Merhaba";
            string sc = "Mer" + "haba";             // fold+intern
            object oa = sa;
            object oc = sc;
            P("S10a", oa == oc ? 1 : 0);            // 1
            string sr = sa.Substring(0, 3) + sa.Substring(3); // runtime concat
            P("S10b", sr == sa ? 1 : 0);            // 1 icerik
            object or2 = sr;
            P("S10c", oa == or2 ? 0 : 2);           // 2 (farkli nesne)
            P("S10d", sa.Length);                   // 7
            P("S10e", sa.IndexOf("haba"));          // 3
            P("S10f", sa.IndexOf("yok"));           // -1
            P("S10g", sa.IndexOf(""));              // 0
            P("S10h", sa.IndexOf("a", 2));          // 4
            P("S10i", sa[1] == 'e' ? 1 : 0);        // 1
            P("S10j", ("ab" + 7).Length);           // 3
            P("S10k", (5 + "x" + 'q').Length);      // 3
            string sn = null;
            P("S10l", (sn + "x").Length);           // 1 (null bos sayilir)
            P("S10m", sn == null ? 1 : 0);          // 1
            P("S10n", sa.GetHashCode() == sc.GetHashCode() ? 1 : 0); // 1 (esit iceriklerin hash'i esit)
            P("S10o", sa.Equals(sc) ? 1 : 0);       // 1
            string acc10 = "";
            for (int i = 0; i < 4; i++) { acc10 += i; }
            P("S10p", acc10 == "0123" ? 1 : 0);     // 1
            P("S10q", sa.Substring(7).Length);      // 0 (bos kuyruk)

            // ---- S11 diziler ----
            int[] arr = new int[] { 4, 5, 6, 7 };
            arr[1] += 10;
            arr[2]++;
            P("S11a", arr[0] + arr[1] + arr[2] + arr[3] + arr.Length); // 4+15+7+7+4 = 37
            int[] zeros = new int[3];
            P("S11b", zeros[0] + zeros[2]);         // 0
            int[] one = new int[] { 42 };
            P("S11c", one[one.Length - 1]);         // 42

            // ---- S12 struct ----
            Vec3 v = new Vec3(1, 2, 3);
            v.Scale(2);
            P("S12a", (int)(v.x + v.y + v.z));      // 12
            Vec3 w12 = v + new Vec3(1, 1, 1);
            P("S12b", (int)w12.Dot(new Vec3(1, 1, 1))); // 3+5+7 = 15
            w12.Sx = 10;
            P("S12c", (int)w12.Sx);                 // 10
            Vec3[] varr = new Vec3[3];
            varr[1] = w12;
            varr[1].x = 20;
            varr[1].Scale(2);
            P("S12d", (int)(varr[1].x + varr[1].y + varr[1].z)); // 40+10+14 = 64
            P("S12e", (int)w12.x);                  // 10 (kopya etkilenmedi)
            Holder h = new Holder(7);
            h.pos = new Vec3(1, 2, 3);
            h.pos.y = 9;
            h.pos.Scale(3);
            P("S12f", (int)(h.pos.x + h.pos.y + h.pos.z)); // 3+27+9 = 39
            P("S12g", ModifyCopy(v));               // 500
            P("S12h", (int)v.x);                    // 2 (byval korunum)
            Vec3 zv = new Vec3();
            P("S12i", (int)(zv.x + zv.y + zv.z));   // 0 (sifir-init)

            // ---- S13 class / kalitim / ctor zinciri / property override ----
            B b1 = new B(4);                        // base(8): x=8,y=3, z=105 -> Total 116
            B b2 = new B();                         // x=1,y=3,z=1005 -> 1009
            C c3 = new C(10);                       // this(): w=8, sonra +10 -> 18; Total 1009
            P("S13a", b1.Total());
            P("S13b", b2.Total());
            P("S13c", c3.Total() + c3.W());
            A ab = b1;
            P("S13d", ab.Tag());                    // 20 (virtual)
            P("S13e", ab.Sides * 100);              // 400 (virtual property)
            Pt pt1 = new Pt(2, 3);
            Pt pt2 = new Pt(2, 3);
            P("S13f", pt1.GetHashCode());           // 65
            object opt = pt1;
            P("S13g", opt.Equals(pt2) ? 1 : 0);     // 1 (override dispatch)
            Console.Write("S13h:");
            Console.WriteLine(opt.ToString());      // Pt#2:3

            // ---- S14 interface ----
            IShape s1 = new Rect(3, 4);
            IShape s2 = new Circle(2);
            P("S14a", s1.Area() + s2.Area());       // 12+12 = 24
            P("S14b", s1 is INamed ? 1 : 0);        // 1
            INamed nn = s1 as INamed;
            P("S14c", nn != null ? nn.Id() : -1);   // 1
            P("S14d", s2 as INamed == null ? 1 : 0); // 1

            // ---- S15 generics ----
            Box<int> bi = new Box<int>(41);
            bi.Set(bi.Get() + 1);
            Box<float> bf = new Box<float>(2.5f);
            Box<Box<int>> nested = new Box<Box<int>>(bi);
            P("S15a", nested.Get().Get());          // 42
            P("S15b", (int)bf.Get());               // 2
            P("S15c", Box<int>.Made());             // 1 (int instantiation sayaci)
            P("S15d", Box<float>.Made());           // 1 (AYRI sayac)
            Pair<int, float> pr = new Pair<int, float>(7, 1.5f);
            Pair<float, int> q = pr.Swap();
            P("S15e", q.Second() * 100);            // 700
            P("S15f", (int)(q.First() * 2.0f));     // 3
            P("S15g", Pick(3, 8, true));            // 8 (cikarim)
            P("S15h", Pick<int>(3, 8, false));      // 3 (acik)
            Box<Vec3> bv = new Box<Vec3>(new Vec3(4, 5, 6));
            P("S15i", (int)bv.Get().y);             // 5 (struct tip argumani)

            // ---- S16 cast / is / as ----
            object o16 = new Circle(3);
            P("S16a", o16 is Circle ? 1 : 0);       // 1
            P("S16b", o16 is Rect ? 1 : 0);         // 0
            Circle c16 = (Circle)o16;
            P("S16c", c16.R());                     // 3
            P("S16d", (int)7.9f);                   // 7 (trunc)
            P("S16e", (int)-7.9f);                  // -7 (sifira dogru)
            P("S16f", (int)(float)3);               // 3
            try
            {
                Rect bad = (Rect)o16;
                P("S16g", bad.Area());
            }
            catch (InvalidCastException)
            {
                P("S16g", 777); // mesaj metni dotnet'te dinamik uretilir - yalniz yakalama akisi dogrulanir
            }

            // ---- S17 exception'lar ----
            int e17 = 0;
            try
            {
                e17 += Boom(2);
                e17 += Boom(7);
                e17 += 1000;
            }
            catch (GameError e)
            {
                e17 += e.Code();                    // 2 + 70
                Console.Write("S17a:");
                Console.WriteLine(e.Message);       // patladi:7
            }
            P("S17b", e17);                         // 72
            try
            {
                int[] a17 = new int[2];
                int i17 = 5;
                e17 += a17[i17];
            }
            catch (IndexOutOfRangeException e)
            {
                Console.Write("S17c:");
                Console.WriteLine(e.Message);       // "Index was outside the bounds of the array."
            }
            try
            {
                int dz = 10;
                int zero = 0;
                e17 += dz / zero;
            }
            catch (DivideByZeroException e)
            {
                Console.Write("S17d:");
                Console.WriteLine(e.Message);       // "Attempted to divide by zero."
            }
            int flow = 0;
            try
            {
                try { Boom(5); }
                catch (GameError) { flow += 1; throw; } // rethrow
            }
            catch (Exception)
            {
                flow += 2;
            }
            P("S17e", flow);                        // 3
            Exception plain = new Exception("ozel mesaj");
            Console.Write("S17f:");
            Console.WriteLine(plain.Message);
            int loopcatch = 0;
            for (int i = 0; i < 3; i++)
            {
                try { if (i % 2 == 0) { throw new GameError("cift", i); } loopcatch += 10; }
                catch (GameError) { loopcatch += 1; }
            }
            P("S17g", loopcatch);                   // 1+10+1 = 12

            // ---- S18 iterator / foreach ----
            int s18 = 0;
            foreach (var q18 in Squares(5)) { s18 += q18; }
            P("S18a", s18);                         // 0+1+4+9+16 = 30
            int s18b = 0;
            foreach (int q18 in Squares(3)) { if (q18 == 1) { continue; } s18b += q18; }
            P("S18b", s18b);                        // 0+4 = 4

            // ---- S19 var / out var / is-pattern ----
            var v19 = 6;
            var s19 = "abc";
            P("S19a", v19 + s19.Length);            // 9
            if (TryGet(5, out var got)) { P("S19b", got); }   // 50
            if (!TryGet(-1, out int miss)) { P("S19c", miss); } // -1
            object o19 = new Circle(4);
            if (o19 is Circle c19 && c19.R() > 2) { P("S19d", c19.R()); } // 4
            if (o19 is Rect r19) { P("S19e", r19.Area()); } else { P("S19e", -1); } // -1
            var f19 = 2.5f;
            P("S19f", (int)(f19 * 4));              // 10

            // ---- S20 overload / default / ref-out ----
            P("S20a", AddN(2));                     // 42
            P("S20b", AddN(1, 2, 3));               // 6
            int ra = 3;
            int rb = 9;
            SwapRef(ref ra, ref rb);
            P("S20c", ra * 100 + rb);               // 903
            P("S20d", AddN(AddN(1), AddN(2, 3)));   // AddN(41, 5) = 46

            // ---- S21 typeof primitive'ler (dotnet referansli: FullName/Name/BaseType zinciri) ----
            Console.WriteLine(typeof(int).FullName);
            Console.WriteLine(typeof(int).Name);
            Console.WriteLine(typeof(uint).FullName);
            Console.WriteLine(typeof(long).FullName);
            Console.WriteLine(typeof(ulong).FullName);
            Console.WriteLine(typeof(short).FullName);
            Console.WriteLine(typeof(ushort).FullName);
            Console.WriteLine(typeof(sbyte).FullName);
            Console.WriteLine(typeof(byte).FullName);
            Console.WriteLine(typeof(char).FullName);
            Console.WriteLine(typeof(bool).FullName);
            Console.WriteLine(typeof(float).FullName);
            Console.WriteLine(typeof(double).FullName);
            Console.WriteLine(typeof(int).BaseType.FullName);
            Console.WriteLine(typeof(int).BaseType.BaseType.FullName);
            P("S21a", typeof(int) == typeof(int) ? 1 : 0);
            P("S21b", typeof(double).Name == "Double" ? 1 : 0);

            // ---- S22 boxing (kutu kimligi test EDILMEZ: kucuk deger cache'i bilincli sapma) ----
            object bo = 5;
            P("S22a", (int)bo);
            P("S22b", bo is int ? 1 : 0);
            P("S22c", bo is long ? 1 : 0);
            if (bo is int bxi) { P("S22d", bxi + 1); }
            P("S22e", bo.GetHashCode());
            Console.WriteLine(bo);
            Console.WriteLine(bo.ToString());
            P("S22f", bo.Equals(5) ? 1 : 0);
            P("S22g", bo.Equals(6) ? 1 : 0);
            P("S22h", bo.Equals("x") ? 1 : 0);
            object bxb = true;
            Console.WriteLine(bxb);
            object bxc = 'Z';
            Console.WriteLine(bxc);
            object bxl = 123456789012345;
            Console.WriteLine(bxl);
            P("S22i", bxl.GetHashCode());
            object bxu = 4000000000u;
            Console.WriteLine(bxu);
            object bxh = 0.5;
            P("S22j", bxh.GetHashCode());
            P("S22k", ((object)1.5f).GetHashCode());
            object bxt = false ? (object)9 : (object)7;
            Console.WriteLine(bxt);
            int flow22 = 0;
            try { long wrong = (long)bo; flow22 += (int)wrong; }
            catch (InvalidCastException) { flow22 = 77; }
            P("S22l", flow22);
            object bxn = null;
            try { int zz = (int)bxn; flow22 += zz; }
            catch (NullReferenceException) { flow22 = 88; }
            P("S22m", flow22);

            // ---- S23 double/float ToString (en kisa round-trip, dotnet birebir) ----
            Console.WriteLine((object)0.1);
            Console.WriteLine((object)0.3);
            Console.WriteLine((object)(1.0 / 3.0));
            Console.WriteLine((object)1e15);
            Console.WriteLine((object)1e16);
            Console.WriteLine((object)1e17);
            Console.WriteLine((object)1e14);
            Console.WriteLine((object)0.0001);
            Console.WriteLine((object)0.00001);
            Console.WriteLine((object)(-0.0));
            Console.WriteLine((object)5E-324);
            Console.WriteLine((object)1.7976931348623157E308);
            Console.WriteLine((object)0.0);
            Console.WriteLine((object)123.456);
            Console.WriteLine((object)2.5f);
            Console.WriteLine((object)0.1f);
            Console.WriteLine((object)(1f / 3f));
            Console.WriteLine((object)1e7f);
            Console.WriteLine((object)1e8f);
            Console.WriteLine((object)1e9f);
            Console.WriteLine((object)1e10f);
            Console.WriteLine((object)3.4028235e38f);
            double z23 = 0.0;
            Console.WriteLine((object)(0.0 / z23));

            // ---- S24 enum boxing: uye adi ToString, underlying denkligi, exact is/Equals ----
            object oc24 = Renk.Yesil;
            Console.WriteLine(oc24);
            Console.WriteLine(oc24.ToString());
            P("S24a", oc24.GetHashCode());
            P("S24b", (int)oc24);
            P("S24c", (Renk)(object)6 == Renk.Mavi ? 1 : 0);
            object oi24 = 6;
            Renk ce24 = (Renk)oi24;
            P("S24d", ce24 == Renk.Mavi ? 1 : 0);
            P("S24e", oc24 is Renk ? 1 : 0);
            P("S24f", oc24 is int ? 1 : 0);
            P("S24g", oi24 is Renk ? 1 : 0);
            P("S24h", oc24.Equals(Renk.Yesil) ? 1 : 0);
            P("S24i", oc24.Equals(5) ? 1 : 0);
            P("S24j", typeof(Renk).Name == "Renk" ? 1 : 0);
            Console.WriteLine(typeof(Renk).FullName);
            Console.WriteLine(typeof(Renk).BaseType.FullName);
            Console.WriteLine(typeof(Renk).BaseType.BaseType.FullName);
            Console.WriteLine((object)(Renk)55);
            Console.WriteLine((object)Renk.Mor);
            if (oc24 is Renk cp24) { P("S24k", (int)cp24); }
            P("S24l", oc24.GetType() == typeof(Renk) ? 1 : 0);

            // ---- S25 partial class ----
            var pc25 = new Parca25();
            P("S25a", pc25.A());
            P("S25b", pc25.Toplam);
            P("S25c", pc25.Id());
            INamed in25 = pc25;
            P("S25d", in25.Id());

            // ---- S26 delegate: method group, arguman, instance/virtual baglama, null NRE ----
            Op26 f26 = Ekle26;
            P("S26a", f26(3, 9));
            P("S26b", Uygula26(Ekle26, 5, 6));
            P("S26c", Uygula26(f26, 7, 8));
            var cc26 = new Carpici26(7);
            Un26 u26 = cc26.Carp;
            P("S26d", u26(6));
            Carpici26 ct26 = new CarpiciTurev26();
            Un26 v26 = ct26.Sanal;
            P("S26e", v26(9));
            object od26 = f26;
            P("S26f", od26 is Op26 ? 1 : 0);
            Un26 n26 = null;
            int flow26 = 0;
            try { flow26 += n26(1); }
            catch (NullReferenceException) { flow26 = 66; }
            P("S26g", flow26);

            // ---- S27 Func/Action/Predicate (generic delegate aileleri) ----
            Func<int, int, int> t27 = Ekle26;
            P("S27a", t27(10, 20));
            P("S27b", Uygula27(Kare27, 7));
            Func<int> z27 = Doksan27;
            P("S27c", z27());
            Predicate<int> p27 = Pozitif27;
            P("S27d", p27(5) ? 1 : 0);
            P("S27e", p27(-5) ? 1 : 0);
            var cc27 = new Carpici26(6);
            Func<int, int> e27 = cc27.Carp;
            P("S27f", e27(7));
            Action<int> a27 = Kaydet27;
            a27(123);
            P("S27g", son27);

            // ---- S28 arrow syntax + capture'siz lambda ----
            var o28 = new Ok28(10);
            P("S28a", o28.Iki);
            P("S28b", o28.Yari);
            o28.X = 21;
            P("S28c", o28.X);
            P("S28d", o28.Kat(3));
            Func<int, int> f28 = lx => lx * 2;
            P("S28e", f28(30));
            P("S28f", Uygula27(ly => ly * ly, 5));
            Func<int, int, int> g28 = (la, lb) => la + lb;
            P("S28g", g28(20, 22));
            Func<int, int> h28 = (int tx) => tx + 1;
            P("S28h", h28(9));
            Func<int> z28 = () => 9;
            P("S28i", z28());
            Func<int, int> b28 = lw => { return lw + 3; };
            P("S28j", b28(4));
            Action<int> ka28 = lv => Kaydet27(lv);
            ka28(777);
            P("S28k", son27);

            // ---- S29 finally: normal/istisna/erken-return/dongu yollari ----
            log29 = 0;
            try { L29(1); } finally { L29(2); }
            P("S29a", log29);
            log29 = 0;
            try
            {
                try { throw new Exception("f29"); }
                finally { L29(4); }
            }
            catch (Exception e29) { if (e29.Message == "f29") { L29(5); } }
            P("S29b", log29);
            log29 = 0;
            P("S29c", Erken29());
            P("S29d", log29);
            int s29 = 0;
            for (int i29 = 0; i29 < 5; i29++)
            {
                try
                {
                    if (i29 == 2) { continue; }
                    if (i29 == 4) { break; }
                    s29 += i29;
                }
                finally { s29 += 100; }
            }
            P("S29e", s29);
            log29 = 0;
            try { L29(1); }
            catch (Exception) { L29(2); }
            finally { L29(3); }
            P("S29f", log29);
            log29 = 0;
            P("S29g", Cift29());
            P("S29h", log29);

            // ---- S30 extension methods + TryParse yuzeyi ----
            object eo30 = 42;
            P("S30a", eo30.ToInt30());
            object es30 = "7.9";
            P("S30b", es30.ToInt30());
            object en30 = null;
            P("S30c", en30.ToInt30());
            P("S30d", eo30.Kati30(3));
            P("S30e", double.TryParse("2.5", out double dv30) ? 1 : 0);
            Console.WriteLine((object)dv30);
            P("S30f", float.TryParse(" 1.25 ", out float fv30) ? 1 : 0);
            Console.WriteLine((object)fv30);
            P("S30g", long.TryParse("5000000000", out long lv30) ? 1 : 0);
            Console.WriteLine((object)lv30);
            P("S30h", uint.TryParse("4000000000", out uint uv30) ? 1 : 0);
            Console.WriteLine((object)uv30);
            P("S30i", int.TryParse("abc", out int bad30) ? 1 : 0);
            P("S30j", bad30);
            P("S30k", int.TryParse("2147483648", out int ov30) ? 1 : 0);
            P("S30l", uint.TryParse("-5", out uint neg30) ? 1 : 0);
            int ham30 = 5;
            P("S30m", ham30.ToInt30());

            // ---- S31 List/Dictionary generic interface yuzeyi + TypeCode ----
            IDictionary<int, int> map31 = new Dictionary<int, int> { [2] = 4, [3] = 9 };
            List<int> list31 = new List<int> { 1, 2, 3, 4 };
            P("S31a", map31.Count + map31[3]);
            P("S31b", list31.Count + list31[2]);
            P("S31c", Type.GetTypeCode(typeof(int)) == TypeCode.Int32 ? 1 : 0);
            P("S31d", typeof(int).IsPrimitive && !typeof(string).IsPrimitive ? 1 : 0);

            // ---- S32 AOT reflection: field/property/static metadata + get/set ----
            Reflect46 reflection32 = new Reflect46();
            object null32 = null;
            FieldInfo field32 = typeof(Reflect46).GetField("field");
            field32.SetValue(reflection32, 12);
            PropertyInfo prop32 = typeof(Reflect46).GetProperty("Property");
            prop32.SetValue(reflection32, 30);
            FieldInfo static32 = typeof(Reflect46).GetField("StaticField");
            static32.SetValue(null32, 5);
            P("S32a", (int)field32.GetValue(reflection32));
            P("S32b", (int)prop32.GetValue(reflection32));
            P("S32c", (int)static32.GetValue(null32));
            P("S32d", field32 == typeof(Reflect46).GetField("field") ? 1 : 0);

            // ---- S33 using static, nested type, explicit interface, chain/object initializer ----
            Nested46 nested33 = new Nested46();
            Nested46.Child child33 = nested33.Make(20);
            Nested46.Stamp stamp33 = new Nested46.Stamp { value = child33.Value() };
            int left33 = 0, right33 = 0;
            left33 = right33 = Imported46(stamp33.value);
            IExplicit46 explicit33 = new Explicit46();
            P("S33a", left33 + right33 + explicit33.Value());
            Init46 init33 = new Init46(1) { Field = 4, Property = 5 };
            P("S33b", init33.Field + init33.Property);

            // ---- S34 foreach enumerator, lambda assignment, anonymous delegate ----
            int sum34 = 0;
            foreach (var value34 in list31) { if (value34 == 3) { continue; } sum34 += value34; }
            P("S34a", sum34);
            Init46 lambda34 = new Init46();
            Action<Init46> set34 = value => value.Field = 8;
            Func<Init46, int> increment34 = value => value.Field = value.Field + 1;
            set34(lambda34);
            P("S34b", increment34(lambda34));
            Action<int> legacy34 = delegate (int value) { Static46.LegacyValue = value * 3; };
            legacy34(4);
            P("S34c", Static46.LegacyValue);

            // ---- S35 null conditional, coalesce, interpolation, params, throw expression ----
            Action<int> action35 = null;
            action35?.Invoke(Static46.Bump46());
            action35 = Static46.Add46;
            action35?.Invoke(Static46.Bump46());
            P("S35a", Static46.SideValue);
            string fallback35 = null;
            string result35 = fallback35 ?? "ok";
            P("S35b", $"value/{result35}" == "value/ok" ? 1 : 0);
            Params46 params35 = new Params46(7, "a", "b", "c");
            Params46 empty35 = new Params46();
            P("S35c", params35.Count() + empty35.Count());
            try { bool unsupported35 = Throwing35; P("S35d", unsupported35 ? 0 : 0); }
            catch (NotImplementedException) { P("S35d", 1); }

            // ---- S36 lexer: leading decimal literal + Unicode non-breaking space ----
            float decimal36 = .1f;
            int unicode36 = 2;
            P("S36a", (int)(decimal36 * 10) + unicode36);

            // ---- S37 accessor visibility syntax ----
            P("S37a", new Accessor47(42).Value);

            // ---- S38 compound assignment expression (Ease.quintInOut deseni) ----
            float scale38 = 3;
            P("S38a", (int)(scale38 *= 2));
            P("S38b", (int)(scale38 -= 1));

            // ---- S39 generic where constraint syntax (semantic enforcement TODO) ----
            Constraint39<Constraint39Derived> constrained39 = new Constraint39<Constraint39Derived>();
            constrained39.Value = new Constraint39Derived();
            P("S39a", Constraint39<Constraint39Derived>.Keep(new Constraint39Derived()) != null && constrained39.Value != null ? 1 : 0);

            // ---- S40 unchecked statement block ----
            int unchecked40 = 0;
            unchecked { unchecked40 = 40 + 2; }
            P("S40a", unchecked40);

            // ---- S41 brace-form dictionary collection element initializer ----
            Dictionary<string, int> map41 = new Dictionary<string, int>
            {
                { "one", 1 },
                { "two", 2 }
            };
            P("S41a", map41.Count + map41["two"]);

            // ---- S42 verbatim string literal ----
            string verbatim42 = @"a\b""c";
            P("S42a", verbatim42.Length);

            // ---- S43 attributes are parsed but metadata semantics remain TODO ----
            P("S43a", new Attribute43().Read());

            // ---- S44 null-conditional reference member access ----
            NullMember44 none44 = null;
            NullMember44 some44 = new NullMember44 { Text = "ok" };
            P("S44a", none44?.Value == null ? 1 : 0);
            P("S44b", some44?.Value == "ok" ? 1 : 0);

            // ---- S45 array cast syntax ----
            object boxedArray45 = new int[] { 4, 5 };
            P("S45a", ((int[])boxedArray45)[0] + ((int[])boxedArray45)[1]);

            // ---- S46 nested partial type merging ----
            OuterPartial46.Inner nestedPartial46 = new OuterPartial46.Inner();
            P("S46a", nestedPartial46.Left() + nestedPartial46.Right());

            // ---- S47 comma-separated for initializer/increment expressions ----
            int i47 = 0, j47 = 0, sum47 = 0;
            for (i47 = 0, j47 = 0; i47 < 3; i47++, j47 += 2)
                sum47 += i47 + j47;
            P("S47a", sum47);

            // ---- S48 HashSet: Add duplicate sonucu, Remove, grow ve struct enumerator ----
            HashSet<int> set48 = new HashSet<int>();
            int added48 = 0;
            for (int i48 = 0; i48 < 12; i48++) if (set48.Add(i48)) added48++;
            if (set48.Add(3)) added48 += 100;
            set48.Remove(5);
            int sum48 = 0;
            foreach (int value48 in set48) sum48 += value48;
            P("S48a", added48);
            P("S48b", set48.Count);
            P("S48c", set48.Contains(5) ? 1 : 0);
            P("S48d", sum48);

            // ---- S49 generic IList<T> and inherited IEnumerable<T> interface surface ----
            IList<int> list49 = new List<int> { 4, 5 };
            IEnumerable<int> enumerable49 = list49;
            list49[1] = 9;
            P("S49a", list49.Count + list49[0] + list49[1]);
            P("S49b", enumerable49 != null ? 1 : 0);

            // ---- S50 TaskCompletionSource: terminal state and result visibility ----
            TaskCompletionSource<int> source50 = new TaskCompletionSource<int>();
            Task<int> task50 = source50.Task;
            bool first50 = source50.TrySetResult(4);
            bool second50 = source50.TrySetResult(9);
            P("S50a", task50.IsCompleted ? 1 : 0);
            P("S50b", first50 ? 1 : 0);
            P("S50c", second50 ? 1 : 0);
            P("S50d", task50.Result);

            // ---- S38 Nullable<T>: implicit wrap/null, properties, lifted operators, coalesce ----
            int? optional38 = 12;
            P("S38a", optional38.HasValue ? optional38.Value : -1);
            optional38 = null;
            P("S38b", optional38.HasValue ? 1 : 0);
            P("S38c", optional38 ?? 7);
            int? lifted38 = optional38 + 3;
            P("S38d", lifted38.HasValue ? 1 : 0);
            P("S38e", optional38 < 0 ? 1 : 0);
            P("S38f", optional38 == null ? 1 : 0);
            P("S38g", ReadNullable38(4f) + ReadNullable38(null));
            NullableHolder38 carousel38 = new NullableHolder38();
            carousel38.Target = 6f;
            carousel38.Target += 2.5f;
            float? page38 = carousel38.Target / 2f;
            P("S38h", (int)page38.Value);
            carousel38.Target = null;
            P("S38i", carousel38.Target < 0f ? 1 : 0);
        }

        static bool Throwing35 => throw new NotImplementedException();

        static int ReadNullable38(float? value) { return value.HasValue ? (int)value.Value : 2; }

        static int log29;
        static void L29(int d) { log29 = log29 * 10 + d; }
        static int Erken29() { try { L29(6); return log29; } finally { L29(7); } }
        static int Cift29()
        {
            try
            {
                try { return 5; }
                finally { L29(1); }
            }
            finally { L29(2); }
        }

        static int son27;
        static int Kare27(int x) { return x * x; }
        static int Doksan27() { return 90; }
        static bool Pozitif27(int x) { return x > 0; }
        static void Kaydet27(int x) { son27 = x; }
        static int Uygula27(Func<int, int> f, int x) { return f(x); }

        static int Ekle26(int a, int b) { return a + b; }
        static int Uygula26(Op26 f, int a, int b) { return f(a, b); }
    }
}
