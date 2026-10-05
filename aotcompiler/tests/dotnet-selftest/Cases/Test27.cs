using System;
namespace Demo27 {
    class Sayici {
        public static int son;
        int taban;
        public Sayici(int t) { taban = t; }
        public int Ekle(int x) { return taban + x; }
    }
    class App27 {
        static int Topla(int a, int b) { return a + b; }
        static int Kare(int x) { return x * x; }
        static int Doksan9() { return 99; }
        static bool Pozitif(int x) { return x > 0; }
        static void Kaydet(int x) { Sayici.son = x; }
        static int Uygula(Func<int, int> f, int x) { return f(x); }
        public static int Run() {
            int acc = 0;
            Func<int, int, int> t = Topla;
            if (t(3, 4) == 7) { acc += 1; }
            if (Uygula(Kare, 6) == 36) { acc += 2; }
            Action<int> k = Kaydet;
            k(5);
            if (Sayici.son == 5) { acc += 4; }
            Func<int> d = Doksan9;
            if (d() == 99) { acc += 8; }
            Predicate<int> p = Pozitif;
            if (p(3) && !p(-3)) { acc += 16; }
            var s = new Sayici(30);
            Func<int, int> e = s.Ekle;
            if (e(12) == 42) { acc += 32; }
            return acc;
        }
    }
}
