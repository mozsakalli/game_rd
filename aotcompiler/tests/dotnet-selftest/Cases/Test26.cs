using System;
namespace Demo26 {
    delegate int Islem(int a, int b);
    delegate int Tek(int x);
    class Hesap {
        int carpan;
        public Hesap(int c) { carpan = c; }
        public int Carp(int x) { return x * carpan; }
        public virtual int Sanal(int x) { return x + 1; }
    }
    class Turev : Hesap {
        public Turev() : base(0) { }
        public override int Sanal(int x) { return x * 100; }
    }
    class App26 {
        static int Topla(int a, int b) { return a + b; }
        static int Uygula(Islem f, int x, int y) { return f(x, y); }
        public static int Run() {
            int acc = 0;
            Islem f = Topla;
            if (f(3, 4) == 7) { acc += 1; }
            if (Uygula(Topla, 10, 20) == 30) { acc += 2; }
            var h = new Hesap(5);
            Tek t = h.Carp;
            if (t(6) == 30) { acc += 4; }
            Hesap hh = new Turev();
            Tek v = hh.Sanal;
            if (v(7) == 700) { acc += 8; }
            Islem g = f;
            if (g(1, 1) == 2) { acc += 16; }
            object o = f;
            if (o is Islem) { acc += 32; }
            Tek n = null;
            try { acc += n(1); }
            catch (NullReferenceException) { acc += 64; }
            return acc;
        }
    }
}
