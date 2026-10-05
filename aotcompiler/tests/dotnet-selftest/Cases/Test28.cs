using System;
namespace Demo28 {
    class Nokta {
        int deger;
        public Nokta(int a) => deger = a;
        public int Iki => deger * 2;
        public int Yari { get => deger / 2; }
        public int X { get => deger; set => deger = value; }
        public int Kat(int k) => deger * k;
    }
    class App28 {
        static int son;
        static void Kaydet(int v) => son = v;
        static int Uygula(Func<int, int> f, int v) { return f(v); }
        public static int Run() {
            int acc = 0;
            var n = new Nokta(10);
            if (n.Iki == 20) { acc += 1; }
            if (n.Yari == 5) { acc += 2; }
            n.X = 21;
            if (n.X == 21) { acc += 4; }
            if (n.Kat(3) == 63) { acc += 8; }
            Func<int, int> f = x => x * 2;
            if (f(30) == 60) { acc += 16; }
            if (Uygula(x => x * x, 5) == 25) { acc += 32; }
            Func<int, int, int> g = (a, b) => a + b;
            if (g(20, 22) == 42) { acc += 64; }
            Func<int, int> h = (int tx) => tx + 1;
            if (h(9) == 10) { acc += 128; }
            Func<int> z = () => 9;
            if (z() == 9) { acc += 256; }
            Func<int, int> blk = w => { return w + 3; };
            if (blk(4) == 7) { acc += 512; }
            Action<int> ka = v => Kaydet(v);
            ka(77);
            if (son == 77) { acc += 1024; }
            return acc;
        }
    }
}
