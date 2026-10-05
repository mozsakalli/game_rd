// CIL conformance dilim 1a: bu dosya dotnet ile DLL'e derlenir, (1) dotnet kosumu REFERANS,
// (2) CilFrontend DLL+PDB'yi bizim IR'a cevirir -> C -> exe. stdout birebir ayni olmali.
// Kapsam: class/kalitim/virtual/iface, alanlar/statics/cctor, ctor zinciri, dizi, string temel,
// enum+switch, ternary (merge-spill), boxing/unbox/is, typeof, delegate (non-generic) + method group.
// BILEREK YOK (dilim 1b/2): try-catch-finally, generic'ler, struct methodlari, ref/out, lambda.
using System;

namespace CilProg
{
    public enum Renk { Kirmizi, Yesil = 5, Mavi }

    public interface IAlan { int Alan(); }

    public delegate int Islem(int a, int b);

    public class Sekil : IAlan
    {
        protected int k;
        public Sekil(int k0) { k = k0; }
        public virtual int Alan() { return k; }
    }

    public class Kare : Sekil
    {
        public Kare(int k0) : base(k0) { }
        public override int Alan() { return k * k; }
    }

    public static class Program
    {
        static int sayac = 7; // static init -> .cctor yolu

        static int Topla(int a, int b) { return a + b; }
        static int Uygula(Islem f, int x, int y) { return f(x, y); }

        public static void Main()
        {
            // aritmetik + dongu + dizi
            int[] a = new int[6];
            for (int i = 0; i < a.Length; i++) { a[i] = i * i; }
            int s = 0;
            for (int j = 0; j < a.Length; j++) { s += a[j]; }
            Console.WriteLine(s);
            Console.WriteLine(a.Length);

            // string temel + Concat remap
            string ad = "Gamos";
            string msg = ad + "CIL";
            Console.WriteLine(msg);
            Console.WriteLine(msg.Length);

            // kalitim + virtual + iface dispatch
            Sekil x = new Kare(5);
            Console.WriteLine(x.Alan());
            IAlan ia = x;
            Console.WriteLine(ia.Alan());
            Sekil y = new Sekil(9);
            Console.WriteLine(y.Alan());

            // ternary (dallar arasi deger tasima - merge spill)
            int t = x.Alan() > 20 ? 100 : 200;
            Console.WriteLine(t);
            int u = y.Alan() > 20 ? 100 : 200;
            Console.WriteLine(u);

            // enum + switch
            Renk r = Renk.Yesil;
            switch (r)
            {
                case Renk.Kirmizi: Console.WriteLine(1); break;
                case Renk.Yesil: Console.WriteLine(2); break;
                default: Console.WriteLine(3); break;
            }
            Console.WriteLine((int)r);

            // boxing / unbox / is / GetType-typeof
            object o = 42;
            Console.WriteLine((int)o);
            Console.WriteLine(o is Sekil ? 1 : 0);
            object os = x;
            if (os is Kare kk) { Console.WriteLine(kk.Alan()); }
            Console.WriteLine(o.GetType() == typeof(int) ? 1 : 0);
            Console.WriteLine(typeof(Kare).FullName);
            Console.WriteLine(o.ToString());

            // statics + cctor
            Console.WriteLine(sayac);
            sayac += 3;
            Console.WriteLine(sayac);

            // delegate: method group + arguman + cagri
            Islem f = Topla;
            Console.WriteLine(f(20, 22));
            Console.WriteLine(Uygula(Topla, 5, 6));

            // string uzerinden long/double boxing ToString (corelib yuzeyi)
            object lo = 5000000000L;
            Console.WriteLine(lo);
            object dob = 2.5;
            Console.WriteLine(dob);
        }
    }
}
